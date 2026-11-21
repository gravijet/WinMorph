using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The complete WinMorph lock screen, including unlocking: the Windows password is typed
    /// into the look itself and checked against the Windows account (LogonUser), or Windows Hello
    /// (fingerprint, face, PIN) is used. Nothing of the Windows 11 lock screen is shown.
    ///
    /// Safety: it runs inside the design shell, whose keyboard hook blocks the Windows key,
    /// Alt+Tab, Ctrl+Esc and Ctrl+Shift+Esc while locked. A guard checks several times per second;
    /// if anything tries to get past it (Ctrl+Alt+Del, another window in front, the PC goes to
    /// sleep, too many wrong passwords, an error) the real Windows lock takes over at once.
    /// </summary>
    public static class CustomLock
    {
        static volatile bool active;
        public static bool Active => active;

        static readonly List<Window> windows = new List<Window>();
        static readonly HashSet<IntPtr> handles = new HashSet<IntPtr>();
        static DispatcherTimer guard, topmost, idleBack;
        static LockAuth auth;
        static ShellTheme theme;
        static int fails, fgMisses;
        static volatile bool helloBusy, verifying;
        static DateTime shownAt;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;
        /// <summary>Hacker messages are terminal lines; the other looks speak normally.</summary>
        static string M(string termDe, string termEn, string de, string en) => auth != null && !auth.Terminal ? L(de, en) : L(termDe, termEn);

        // ---------------- show ----------------
        public static void Show()
        {
            if (active) return;
            try { ShowCore(); }
            catch (Exception ex) { Log(ex); RealLock(); }
        }

        static void ShowCore()
        {
            active = true;
            fails = 0; fgMisses = 0; helloBusy = false; verifying = false;
            shownAt = DateTime.Now;
            var presetId = Core.Reg.Get(ShellControl.SettingsKey, "LastPreset") as string;
            var p = Core.Presets.Find(presetId ?? "") ?? Core.Presets.All[0];
            theme = ShellThemes.ForLock(p.Id);
            auth = new LockAuth { UseHello = Hello, UseWindows = RealLock, Submit = Verify };
            CheckHello(auth);
            HackerPanel.CloseAll();
            HoverPreview.CloseNow();
            // the Windows screen saver must not start on top while WinMorph's lock is shown
            Core.Native.SystemParametersInfo(0x0011 /* SPI_SETSCREENSAVEACTIVE */, 0, IntPtr.Zero, 0);

            bool first = true;
            foreach (var mon in W32.Monitors())
            {
                var mi = W32.Monitor(mon, out double s);
                var w = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false,
                    Left = mi.rcMonitor.Left / s, Top = mi.rcMonitor.Top / s, Width = (mi.rcMonitor.Right - mi.rcMonitor.Left) / s, Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / s,
                    Background = Brushes.Black, Cursor = Cursors.None, FontFamily = new FontFamily(theme.Font), AllowsTransparency = false,
                };
                var content = first ? LockScreen.BuildLock(theme, p, auth) : LockScreen.BuildSecondary(theme, p);
                if (content is FrameworkElement fe) { fe.Opacity = 0; fe.Loaded += (o, e) => fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))); }
                w.Content = content;
                w.PreviewKeyDown += OnKeyDown;
                w.PreviewTextInput += OnText;
                w.MouseDown += (o, e) => { if (Grace) return; w.Cursor = Cursors.Arrow; auth.Begin(); };
                w.MouseMove += (o, e) => { if (!Grace && w.Cursor == Cursors.None) w.Cursor = Cursors.Arrow; };
                w.Closing += (o, e) => { if (active) e.Cancel = true; };   // Alt+F4 and the like
                RenderGuard.Watch(w);
                windows.Add(w);
                w.Show();
                handles.Add(new WindowInteropHelper(w).Handle);
                first = false;
            }
            auth.Changed();
            Focus();

            topmost = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, (s, e) =>
            {
                if (helloBusy) return;
                foreach (var h in handles) W32.SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }, Dispatcher.CurrentDispatcher);
            guard = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (s, e) => Guard(), Dispatcher.CurrentDispatcher);
            // back to the big clock after a while without input
            idleBack = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) =>
            {
                if (auth.Typing && !verifying && !helloBusy && (DateTime.Now - auth.LastInput).TotalSeconds > 30) { auth.Reset(); auth.Changed(); }
            }, Dispatcher.CurrentDispatcher);
        }

        // keys held from Win+L (or a key repeat) must not count as "I am back"
        static bool Grace => (DateTime.Now - shownAt).TotalMilliseconds < 900;

        static void Focus()
        {
            var w = windows.FirstOrDefault();
            if (w == null) return;
            var h = new WindowInteropHelper(w).Handle;
            W32.Activate(h);
            w.Activate();
            Keyboard.Focus(w);
        }

        // ---------------- input ----------------
        static readonly Key[] Ignore = { Key.LWin, Key.RWin, Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.System, Key.CapsLock, Key.NumLock, Key.L, Key.Apps, Key.VolumeUp, Key.VolumeDown, Key.VolumeMute, Key.MediaPlayPause };

        // the key that wakes the lock only shows the password field; its character is not typed
        static bool eatWakeText;

        static void OnKeyDown(object sender, KeyEventArgs e)
        {
            eatWakeText = false;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // character keys stay unhandled so they arrive as text (OnText); everything else is swallowed
            bool printable = (key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9) || (key >= Key.NumPad0 && key <= Key.Divide) || key == Key.Space || (key >= Key.Oem1 && key <= Key.Oem102);
            if (!printable || e.Key == Key.System) e.Handled = true;
            if (verifying || helloBusy) { e.Handled = true; return; }
            if (Grace || e.IsRepeat && !auth.Typing) return;
            if (!auth.Typing)
            {
                if (Ignore.Contains(key) && key != Key.L) return;
                if (key == Key.L && (DateTime.Now - shownAt).TotalSeconds < 2) return;
                auth.Begin();
                // looks that show their password field from the start type the first key right away
                eatWakeText = printable && !auth.AlwaysOpen;
                if (key == Key.F1) Hello();
                if (key == Key.F2) RealLock();
                if (key == Key.Enter && auth.AlwaysOpen) Verify();
                return;
            }
            if (auth.KeyFilter?.Invoke(key) == true) { e.Handled = true; return; }
            switch (key)
            {
                case Key.Enter: Verify(); break;
                case Key.Escape: auth.Reset(); auth.Changed(); break;
                case Key.Back: auth.Backspace(); break;
                case Key.F1: Hello(); break;
                case Key.F2: RealLock(); break;
                case Key.V when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                    try { if (Clipboard.ContainsText()) foreach (var ch in Clipboard.GetText()) if (!char.IsControl(ch)) auth.Add(ch); } catch { }
                    break;
            }
        }

        static void OnText(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
            if (eatWakeText) { eatWakeText = false; return; }
            if (verifying || helloBusy || Grace || !auth.Typing) return;   // typing starts in OnKeyDown
            foreach (var ch in e.Text)
            {
                if (char.IsControl(ch)) continue;
                auth.Add(ch);
            }
        }

        // ---------------- unlock ----------------
        static void Verify()
        {
            if (verifying || helloBusy) return;
            if (auth.Length == 0) { auth.Message(M("passwort eingeben", "type your password", "Gib dein Kennwort ein.", "Type your password."), false); return; }
            verifying = true; auth.Busy = true; auth.Changed();
            auth.Message(M("prüfe …", "verifying …", "Einen Moment …", "One moment …"), false);
            var pw = auth.Take();
            string user = Environment.UserName, domain = Environment.UserDomainName;
            Task.Run(() => CheckPassword(user, domain, pw)).ContinueWith(t =>
            {
                verifying = false; auth.Busy = false;
                if (t.Result) Unlock();
                else
                {
                    fails++;
                    if (fails >= 5) { auth.Message(M("zu viele versuche – windows übernimmt", "too many attempts – windows takes over", "Zu viele Versuche – Windows übernimmt die Anmeldung", "Too many attempts – Windows takes over the sign-in"), true); RealLock(); return; }
                    auth.Denied(M("zugriff verweigert: falsches passwort", "access denied: wrong password", "Der Benutzername oder das Kennwort ist falsch.", "The user name or password is incorrect."));
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LogonUser(string user, string domain, string password, int type, int provider, out IntPtr token);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static bool CheckPassword(string user, string domain, string pw)
        {
            try
            {
                foreach (var d in new[] { domain, "." }.Distinct())
                {
                    if (LogonUser(user, d, pw, 2 /* INTERACTIVE */, 0, out var tok)) { CloseHandle(tok); return true; }
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>Whether Windows Hello is set up: the looks only offer it then.</summary>
        static async void CheckHello(LockAuth a)
        {
            try
            {
                var avail = await global::Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
                a.HelloReady = avail == global::Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available;
                if (auth == a && active) a.Changed();
            }
            catch { }
        }

        /// <summary>Fingerprint, face or PIN through the small Windows Hello prompt.</summary>
        static async void Hello()
        {
            if (helloBusy || verifying) return;
            helloBusy = true;
            auth.Message(M("windows hello: finger auflegen oder in die kamera schauen", "windows hello: touch the sensor or look at the camera", "Finger auflegen oder in die Kamera schauen", "Touch the sensor or look at the camera"), false);
            try
            {
                var avail = await global::Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
                if (avail != global::Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available)
                {
                    auth.Message(M("windows hello ist nicht eingerichtet", "windows hello is not set up", "Windows Hello ist nicht eingerichtet.", "Windows Hello is not set up."), true);
                    return;
                }
                var h = new WindowInteropHelper(windows[0]).Handle;
                var result = await HelloInterop.Request(h, L("WinMorph entsperren", "Unlock WinMorph"));
                if (result == global::Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified) { Unlock(); return; }
                auth.Message(M("windows hello abgebrochen", "windows hello cancelled", "Windows Hello wurde abgebrochen.", "Windows Hello was cancelled."), true);
            }
            catch (Exception ex) { Log(ex); auth.Message(M("windows hello nicht verfügbar", "windows hello unavailable", "Windows Hello ist gerade nicht verfügbar.", "Windows Hello is unavailable right now."), true); }
            finally { helloBusy = false; if (active) Focus(); }
        }

        static void Unlock()
        {
            auth.Granted();
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            tm.Tick += (s, e) => { tm.Stop(); Close(); };
            tm.Start();
        }

        static void Close()
        {
            active = false;
            guard?.Stop(); topmost?.Stop(); idleBack?.Stop();
            guard = topmost = idleBack = null;
            foreach (var w in windows.ToList()) try { w.Close(); } catch { }
            windows.Clear(); handles.Clear();
            auth?.Reset();
            LockScreen.StopTimers();
            Core.Native.SystemParametersInfo(0x0011, 1, IntPtr.Zero, 0);   // screen saver back on
            Closed?.Invoke();
        }

        public static event Action Closed;

        /// <summary>
        /// Test picture (nothing is locked, nothing on screen): the lock with typed password and
        /// after a wrong one, rendered off-screen into two PNG files.
        /// </summary>
        public static void RenderTest(string presetId, string prefix, Action done)
        {
            var p = Core.Presets.Find(presetId) ?? Core.Presets.Find("terminal");
            var th = ShellThemes.ForLock(p.Id);
            var a = new LockAuth();
            auth = a;
            var w = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Width = 1600, Height = 900, Background = Brushes.Black, FontFamily = new FontFamily(th.Font) };
            w.Content = LockScreen.BuildLock(th, p, a);
            w.Show();
            void Shot(string f)
            {
                var el = (FrameworkElement)w.Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(el);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = System.IO.File.Create(f)) enc.Save(fs);
            }
            int step = 0;
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            tm.Tick += (s, e) =>
            {
                step++;
                if (step == 1) { Shot(prefix + "-idle.png"); a.Begin(); foreach (var ch in "geheim12") a.Add(ch); }
                else if (step == 2) { Shot(prefix + "-typing.png"); a.Take(); a.Denied(M("zugriff verweigert: falsches passwort", "access denied: wrong password", "Der Benutzername oder das Kennwort ist falsch.", "The user name or password is incorrect.")); }
                else if (step == 3) { Shot(prefix + "-denied.png"); tm.Stop(); LockScreen.StopTimers(); w.Close(); done(); }
            };
            tm.Start();
        }

        /// <summary>Removes the WinMorph lock without unlocking (the Windows lock is already in front).</summary>
        public static void ForceClose() { if (active) Close(); }

        /// <summary>The real Windows lock takes over (and the WinMorph lock goes away behind it).</summary>
        public static void RealLock()
        {
            if (!active) { Task.Run(() => Core.LockGuard.LockNow()); return; }
            guard?.Stop();
            Task.Run(() => Core.LockGuard.LockNow()).ContinueWith(t => Close(), TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Called when the PC goes to sleep: Windows locks it then (wake-up needs the Windows sign-in).</summary>
        public static void Suspending() { if (active) RealLock(); }

        // ---------------- guard ----------------
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static void Guard()
        {
            if (!active) return;
            try
            {
                // Ctrl+Alt+Del, a UAC prompt or the Windows lock: another desktop has the input
                var d = OpenInputDesktop(0, false, 0x0100);
                if (d == IntPtr.Zero) { RealLock(); return; }
                CloseDesktop(d);
                if (helloBusy) return;
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return;   // in between two windows
                bool ours = handles.Contains(fg);
                if (!ours && fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint pid);
                    ours = pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id && !IsShellBar(fg);
                }
                if (ours) { fgMisses = 0; return; }
                fgMisses++;
                Focus();
                if (fgMisses >= 24) RealLock();   // something keeps itself in front for 6 s: hand over to Windows
            }
            catch (Exception ex) { Log(ex); RealLock(); }
        }

        static bool IsShellBar(IntPtr h) => false;

        static void Log(Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Core.Backup.DataDir, "lock-errors.log"), DateTime.Now + "  " + ex + Environment.NewLine); } catch { }
        }
    }

    /// <summary>The password line of the lock screen: what is typed and what is shown.</summary>
    public sealed class LockAuth
    {
        readonly StringBuilder pw = new StringBuilder();
        public TextBlock Prompt, Info;              // set by the lock screen design
        public Border Box;                           // flashes red/green
        public Color Accent = Color.FromRgb(0x00, 0xFF, 0x41);
        public bool Terminal = true;
        public bool Typing { get; private set; }
        public DateTime LastInput = DateTime.Now;
        string message; bool messageBad;
        bool blink;
        /// <summary>The look shows its password field from the start (Windows 7): the first key is already typed.</summary>
        public bool AlwaysOpen;
        /// <summary>Windows Hello is set up; the looks offer it only then (checked when the lock opens).</summary>
        public bool HelloReady;
        /// <summary>The password is being checked right now.</summary>
        public bool Busy;
        /// <summary>Set by the lock: what a click in the design does (Windows Hello, the Windows sign-in, unlock).</summary>
        public Action UseHello, UseWindows, Submit;
        /// <summary>A design may take keys first (Windows 7 error page: any key = OK). True = handled.</summary>
        public Func<Key, bool> KeyFilter;
        /// <summary>Set by looks that draw the password stage themselves (Windows 7, Zen); called on every change.</summary>
        public Action Render;
        /// <summary>Set by such looks: wrong password (shake etc.) and unlocked.</summary>
        public Action OnDenied, OnGranted;
        public string Note => message;
        public bool NoteBad => messageBad;
        public bool Blink => blink;
        public bool Unlocked { get; private set; }
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        public int Length => pw.Length;

        public void Begin() { if (Typing) return; Typing = true; LastInput = DateTime.Now; message = null; Changed(); }
        public void Add(char c) { if (pw.Length < 256) pw.Append(c); LastInput = DateTime.Now; message = null; Changed(); }
        public void Backspace() { if (pw.Length > 0) pw.Length--; LastInput = DateTime.Now; Changed(); }
        public string Take() { var s = pw.ToString(); Clear(); return s; }
        void Clear() { for (int i = 0; i < pw.Length; i++) pw[i] = '\0'; pw.Length = 0; }
        public void Reset() { Clear(); Typing = false; Unlocked = false; message = null; Changed(); }
        public void Message(string text, bool bad) { message = text; messageBad = bad; LastInput = DateTime.Now; Changed(); }

        public void Tick() { blink = !blink; Changed(); }

        public void Changed()
        {
            if (Render != null) { Render(); return; }
            if (Prompt == null) return;
            string user = ShellActions.UserName.ToLower().Replace(' ', '_');
            string cursor = blink ? "█" : " ";
            if (!Typing)
            {
                Prompt.Text = Terminal ? L("sitzung gesperrt", "session locked") + " " + cursor : "";
                if (Info != null) { Info.Text = message ?? ""; Info.Foreground = B(message != null && messageBad ? Color.FromRgb(0xFF, 0x3B, 0x5C) : Core.Applier.Mix(Accent, Colors.Black, 0.35)); }
            }
            else
            {
                string dots = new string('•', Math.Min(pw.Length, 32));
                Prompt.Text = Terminal ? L("passwort für ", "password for ") + user + ": " + dots + cursor : (pw.Length == 0 ? L("Passwort", "Password") : dots) + (blink ? "|" : " ");
                if (Info != null)
                {
                    Info.Text = message ?? "";
                    Info.Foreground = B(message != null && messageBad ? Color.FromRgb(0xFF, 0x3B, 0x5C) : Core.Applier.Mix(Accent, Colors.Black, Terminal ? 0.35 : 0.0));
                    if (!Terminal && message == null) Info.Foreground = B(Color.FromArgb(190, 255, 255, 255));
                }
            }
        }

        public void Denied(string text)
        {
            Message(text, true);
            if (OnDenied != null) { OnDenied(); return; }
            if (Box == null) return;
            var old = Box.BorderBrush;
            Box.BorderBrush = B(Color.FromRgb(0xFF, 0x3B, 0x5C));
            var tt = new TranslateTransform();
            Box.RenderTransform = tt;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
            foreach (var (t, x) in new[] { (0, 0.0), (60, -14.0), (120, 12.0), (180, -9.0), (240, 6.0), (300, -3.0), (360, 0.0) })
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
            tt.BeginAnimation(TranslateTransform.XProperty, shake);
            var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            back.Tick += (s, e) => { back.Stop(); Box.BorderBrush = old; };
            back.Start();
        }

        public void Granted()
        {
            message = Terminal ? L("zugriff erlaubt", "access granted") : L("Willkommen", "Welcome");
            messageBad = false;
            Typing = true; Clear(); Unlocked = true;
            if (OnGranted != null) { OnGranted(); return; }
            if (Prompt != null) Prompt.Text = Terminal ? L("sitzung entsperrt", "session unlocked") : "";
            if (Info != null) { Info.Text = message; Info.Foreground = B(Accent); }
            if (Box != null) Box.BorderBrush = B(Accent);
        }
    }

    /// <summary>Windows Hello prompt for a desktop window (IUserConsentVerifierInterop).</summary>
    static class HelloInterop
    {
        [ComImport, Guid("39E050C3-4E74-441A-8DC0-B81104DF949C"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        interface IUserConsentVerifierInterop
        {
            [PreserveSig]
            int RequestVerificationForWindowAsync(IntPtr appWindow, [MarshalAs(UnmanagedType.HString)] string message, [In] ref Guid riid, out IntPtr asyncOperation);
        }

        public static async Task<global::Windows.Security.Credentials.UI.UserConsentVerificationResult> Request(IntPtr hwnd, string message)
        {
            var factory = (IUserConsentVerifierInterop)System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal.GetActivationFactory(typeof(global::Windows.Security.Credentials.UI.UserConsentVerifier));
            // IID of IAsyncOperation<UserConsentVerificationResult> (WinRT parameterised interface id)
            var iid = new Guid("fd596ffd-2318-558f-9dbe-d21df43764a5");
            int hr = factory.RequestVerificationForWindowAsync(hwnd, message, ref iid, out var op);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            var asyncOp = (global::Windows.Foundation.IAsyncOperation<global::Windows.Security.Credentials.UI.UserConsentVerificationResult>)Marshal.GetObjectForIUnknown(op);
            Marshal.Release(op);
            return await asyncOp;
        }
    }
}

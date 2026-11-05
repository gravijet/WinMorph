using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Runs the WinMorph shell: hides the Windows taskbar, shows themed bars, docks and
    /// start menus, and recolours window frames. Everything is undone on exit.
    /// </summary>
    public sealed class ShellHost
    {
        readonly ShellTheme t;
        readonly WindowTracker tracker;
        readonly List<BarWindow> bars = new List<BarWindow>();
        readonly List<Action> refreshers = new List<Action>();
        readonly List<Action> clockUpdaters = new List<Action>();
        readonly DispatcherTimer clock, nativeGuard;
        FrameworkElement startAnchor;
        BarWindow startBar;
        StartMenu menu;
        DateTime menuClosed;
        bool peek;
        bool origAutoHide;
        KeyHook keys;
        Switcher switcher;
        volatile bool switching;
        int building; // monitor index while bars are built
        DispatcherTimer rebuildTimer, peekTimer;
        static bool De => Core.Loc.De;

        readonly bool preview;
        readonly List<(Edge edge, double size, UIElement content, Brush bg)> previewBars = new List<(Edge, double, UIElement, Brush)>();

        public ShellHost(ShellTheme theme, bool preview = false)
        {
            t = theme;
            this.preview = preview;
            tracker = preview ? WindowTracker.Demo(t) : new WindowTracker(t);
            tracker.Changed += () => { foreach (var r in refreshers) r(); };
            tracker.ForegroundChanged += () => { foreach (var r in refreshers) r(); };
            if (!preview)
            {
                AppCatalog.Loaded += () => { foreach (var r in refreshers) r(); };
                AppCatalog.LoadAsync(Dispatcher.CurrentDispatcher);
            }

            if (preview) { BuildBars(); return; }

            ThemeToolTips();
            origAutoHide = OriginalAutoHide();
            Core.Applier.SetAutoHideRaw(true);
            HideNative();

            BuildAllMonitors();

            clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) => { foreach (var c in clockUpdaters) c(); }, Dispatcher.CurrentDispatcher);
            nativeGuard = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (s, e) => { if (!peek) HideNative(); }, Dispatcher.CurrentDispatcher);
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            foreach (var r in refreshers) r();
            foreach (var c in clockUpdaters) c();
            audioOk = Hacker && Audio.Available;
            keys = new KeyHook(OnKey);
            keys.Start();
            // Win+L → WinMorph lock screen (Windows would lock before the hook can stop it)
            if (LockSaverReady) Core.LockGuard.Arm(); else Core.LockGuard.Disarm();
            Core.LockGuard.WatchPower(() => LockSaverReady && keys != null);
            Core.LockGuard.Suspending -= CustomLock.Suspending;
            Core.LockGuard.Suspending += CustomLock.Suspending;
            if (LockSaverReady) MakeSaverHandOver();
            CustomLock.Closed -= AfterUnlock;
            CustomLock.Closed += AfterUnlock;
            RefreshLockRedirect();
            new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) =>
            {
                // also picks up the one-time setup done from the app while the shell runs
                if (keys != null && LockSaverReady && !Core.LockGuard.Armed && Core.LockGuard.Ready && !Core.LockGuard.Locked()) Core.LockGuard.Arm();
                RefreshLockRedirect();
            }, Dispatcher.CurrentDispatcher);
            AppCatalog.PinsChanged += () => { foreach (var r in refreshers) r(); };
            Gaming.RestorePerf();   // left over from a crash
            if (t.Id == "zen" || t.Id == "gaming") Notify.Use(t);
            if (Hacker)
            {
                ShellActions.Own = t;
                Notify.Start(t);
                lastNet = NetState();
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetChanged;
            }
            // build the start menu in the background so the first open is instant
            AppCatalog.Loaded += () => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(WarmMenu), DispatcherPriority.ApplicationIdle);
        }

        bool Hacker => t.Start == StartKind.Terminal;

        // ---------- own notices for Wi-Fi changes (hacker look) ----------
        string lastNet;
        DispatcherTimer netTimer;
        static string NetState()
        {
            try { var n = Wlan.Current; if (n != null) return "wifi:" + n.Ssid; } catch { }
            return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "online" : "offline";
        }
        void OnNetChanged(object sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            // Windows fires this several times per change: look once things have settled
            netTimer?.Stop();
            netTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            netTimer.Tick += (s2, e2) =>
            {
                netTimer.Stop();
                var now = NetState();
                if (now == lastNet) return;
                lastNet = now;
                if (now.StartsWith("wifi:")) Notify.Show("wifi", (De ? "verbunden mit " : "connected to ") + now.Substring(5));
                else if (now == "offline") Notify.Show("net", De ? "verbindung getrennt – offline" : "disconnected – offline");
                else Notify.Show("net", De ? "online" : "online");
            };
            netTimer.Start();
        }));

        /// <summary>Tooltips in the colours and font of the look instead of the white Windows ones.</summary>
        void ThemeToolTips()
        {
            var st = new Style(typeof(ToolTip));
            var bg = Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B);
            st.Setters.Add(new Setter(Control.BackgroundProperty, B(Hacker ? C(0x000000) : bg)));
            st.Setters.Add(new Setter(Control.ForegroundProperty, B(t.MenuFg)));
            st.Setters.Add(new Setter(Control.BorderBrushProperty, B(A(t.Accent, 200))));
            st.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(t.Font)));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
            var tpl = new ControlTemplate(typeof(ToolTip));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Hacker || t.Btn == BtnStyle.Bevel ? 0 : 6));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(TextElement.FontFamilyProperty, new FontFamily(t.Font));
            cp.SetValue(TextElement.ForegroundProperty, B(t.MenuFg));
            border.AppendChild(cp);
            tpl.VisualTree = border;
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            Application.Current.Resources[typeof(ToolTip)] = st;
        }
        HackerConsole console;

        void WarmMenu()
        {
            if (Hacker)
            {
                if (console != null) return;
                console = new HackerConsole(t, () => tracker.ByRecentUse());
                console.Hidden += () => menuClosed = DateTime.Now;
                console.ShowActivated = false; console.Show(); console.Hide(); console.ShowActivated = true;
                menuClosed = DateTime.MinValue;
                return;
            }
            if (menu != null) return;
            menu = new StartMenu(t, new Rect(0, 0, 0, 0), Edge.Bottom);
            menu.Hidden += () => menuClosed = DateTime.Now;
            menu.ShowActivated = false;   // warm up without taking focus from the user's window
            menu.Show();
            menu.CloseMenu();
            menu.ShowActivated = true;
            menuClosed = DateTime.MinValue;
        }

        /// <summary>One set of bars per monitor; primary is built last so it owns the Start anchor.</summary>
        void BuildAllMonitors()
        {
            var mons = W32.Monitors();
            for (int i = mons.Count - 1; i >= 0; i--) { building = i; BuildBars(); }
            building = 0;
            foreach (var b in bars)
            {
                b.ExplorerRestarted += () => { Core.Applier.SetAutoHideRaw(true); HideNative(); };
                b.DisplayChanged += ScheduleRebuild;
                b.Show();
            }
        }

        void ScheduleRebuild()
        {
            rebuildTimer?.Stop();
            rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            rebuildTimer.Tick += (s, e) =>
            {
                rebuildTimer.Stop();
                if (W32.Monitors().Count == bars.Select(b => b.MonitorIndex).Distinct().Count()) { foreach (var b in bars) b.Place(); return; }
                foreach (var b in bars.ToList()) try { b.Close(); } catch { }
                bars.Clear(); refreshers.Clear(); clockUpdaters.Clear();
                BuildAllMonitors();
                foreach (var r in refreshers) r();
                foreach (var c in clockUpdaters) c();
            };
            rebuildTimer.Start();
        }

        // ---------------- preview image (no system changes) ----------------
        public System.Windows.Media.Imaging.BitmapSource RenderPreview(Core.Preset p, int w, int h, bool withMenu, double outScale = 1)
        {
            foreach (var r in refreshers) r();
            foreach (var c in clockUpdaters) c();
            var root = new Grid { Width = w, Height = h, ClipToBounds = true };
            root.Children.Add(new Image { Source = Core.WallpaperArt.Render(p, (int)(w * outScale), (int)(h * outScale), false), Stretch = Stretch.Fill });
            var dock = new DockPanel { LastChildFill = true };
            foreach (var b in previewBars.OrderBy(x => x.edge == Edge.Left ? 1 : 0))
            {
                var host = new Border { Background = b.bg, Child = b.content };
                if (b.edge == Edge.Left || b.edge == Edge.Right) host.Width = b.size; else host.Height = b.size;
                DockPanel.SetDock(host, b.edge == Edge.Top ? System.Windows.Controls.Dock.Top : b.edge == Edge.Left ? System.Windows.Controls.Dock.Left : b.edge == Edge.Right ? System.Windows.Controls.Dock.Right : System.Windows.Controls.Dock.Bottom);
                dock.Children.Add(host);
            }
            var desk = new Grid();
            dock.Children.Add(desk);
            root.Children.Add(dock);

            if (withMenu && Hacker)
            {
                var hc = new HackerConsole(t, () => tracker.Windows);
                hc.PreviewFill();
                var content = (FrameworkElement)hc.Content;
                hc.Content = null;
                content.Width = Math.Min(1000, w * 0.62); content.Height = h * 0.58;
                content.HorizontalAlignment = HorizontalAlignment.Center; content.VerticalAlignment = VerticalAlignment.Top;
                content.Margin = new Thickness(0, t.BarHeight, 0, 0);
                ((Border)content).Background = B(0x000000);
                desk.Children.Add(content);
            }
            else if (withMenu)
            {
                var m = new StartMenu(t, new Rect(0, 0, 0, 0), Edge.Bottom);
                var content = (FrameworkElement)m.Content;
                m.Content = null;
                if (m.IsFullScreen) { content.Width = w; content.Height = h; root.Children.Add(content); }
                else
                {
                    content.HorizontalAlignment = t.Menu == MenuKind.Palette || t.Layout == BarLayout.Floating || t.Menu == MenuKind.Grid ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    content.VerticalAlignment = t.Menu == MenuKind.Palette ? VerticalAlignment.Top : (t.Layout == BarLayout.TerminalTop || t.Layout == BarLayout.GnomeTopLeftDock ? VerticalAlignment.Top : VerticalAlignment.Bottom);
                    if (t.Menu == MenuKind.Palette) content.Margin = new Thickness(0, h * 0.2, 0, 0);
                    desk.Children.Add(content);
                }
            }
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w * outScale), (int)(h * outScale), 96 * outScale, 96 * outScale, PixelFormats.Pbgra32);
            bmp.Render(root);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Releases the hooks a preview host created.</summary>
        public void DisposePreview()
        {
            tracker.Dispose();
            refreshers.Clear();
            clockUpdaters.Clear();
        }

        // ---------------- native taskbar ----------------
        static IEnumerable<IntPtr> NativeBars()
        {
            var main = W32.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) yield return main;
            IntPtr h = IntPtr.Zero;
            while ((h = W32.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return h;
        }

        static void HideNative() { foreach (var h in NativeBars()) if (W32.IsWindowVisible(h)) W32.ShowWindow(h, W32.SW_HIDE); }
        static void ShowNative() { foreach (var h in NativeBars()) W32.ShowWindow(h, W32.SW_SHOWNA); }

        void TogglePeek()
        {
            peek = !peek;
            if (peek)
            {
                Core.Applier.SetAutoHideRaw(false); ShowNative();
                // the Windows taskbar is only shown briefly (for its tray icons), then hidden again
                peekTimer?.Stop();
                peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                peekTimer.Tick += (s, e) => { peekTimer.Stop(); if (peek) TogglePeek(); };
                peekTimer.Start();
            }
            else { peekTimer?.Stop(); Core.Applier.SetAutoHideRaw(true); HideNative(); }
        }

        /// <summary>The auto-hide setting from before the shell ever ran (kept in the registry).</summary>
        static bool OriginalAutoHide()
        {
            var v = Core.Reg.Get(ShellControl.SettingsKey, "OrigAutoHide");
            if (v is int i) return i == 1;
            bool cur = Core.Applier.GetAutoHide();
            using (var k = Core.Reg.Create(ShellControl.SettingsKey)) k.SetValue("OrigAutoHide", cur ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            return cur;
        }

        /// <summary>After unlocking (or a remote session) Windows shows its taskbar again – hide it at once.</summary>
        void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            winDown = false; switching = false;
            if (LockSaverReady) Core.LockGuard.Arm();
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                CancelSwitcher();
                foreach (var delay in new[] { 0, 300, 900, 2000, 4000 })
                {
                    var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delay)) };
                    tm.Tick += (s2, e2) => { tm.Stop(); Core.Applier.SetAutoHideRaw(true); HideNative(); foreach (var b in bars) try { b.Place(); } catch { } };
                    tm.Start();
                }
            }));
        }

        /// <summary>keepHidden: at sign-out/shutdown the taskbar stays hidden, so it does not flash up at the next sign-in.</summary>
        public void Shutdown(bool keepHidden = false)
        {
            try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
            clock.Stop(); nativeGuard.Stop();
            keys?.Stop(); keys = null;
            Core.LockGuard.Disarm();   // Win+L belongs to Windows again
            HoverPreview.CloseNow();
            try { switcher?.Close(); } catch { }
            try { menu?.Close(); } catch { }
            try { console?.Close(); } catch { }
            HackerPanel.CloseAll();
            Notify.Stop();
            ShellActions.Own = null;
            Zen.Shutdown();
            Gaming.Shutdown();
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetChanged;
            netTimer?.Stop();
            foreach (var b in bars) try { b.Close(); } catch { }
            tracker.Dispose();
            if (keepHidden) return;
            ShowNative();
            Core.Applier.SetAutoHideRaw(origAutoHide);
        }

        // ---------------- keyboard: Windows key, Win+X, Win+C, Ctrl+Esc, Alt+Tab ----------------
        bool winDown, otherKey, swallowedDuringWin;

        void Ui(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

        /// <summary>Runs on the keyboard thread – must stay quick. Returns true to swallow the key.</summary>
        bool OnKey(bool down, bool up, uint vk, uint flags)
        {
            bool alt = (flags & 0x20) != 0;
            if (CustomLock.Active)
            {
                // locked: nothing may switch away from the lock screen
                if (vk == 0x5B || vk == 0x5C)
                {
                    winDown = false;
                    if (down) return true;
                    KeyHook.Tap(0xE8);   // the release goes through (so Windows does not think it is still held), without opening Start
                    return false;
                }
                if (alt && (vk == 0x09 || vk == 0x1B || vk == 0x73 || vk == 0x20)) return true;   // Alt+Tab, Alt+Esc, Alt+F4, Alt+Space
                if (vk == 0x1B && W32.KeyDown(0x11)) return true;                                 // Ctrl+Esc, Ctrl+Shift+Esc (Task Manager)
                if (vk == 0x86) return true;                                                      // Copilot key
                switching = false;
                return false;
            }
            if (vk == 0x5B || vk == 0x5C)
            {
                if (down) { if (!winDown) { winDown = true; otherKey = false; swallowedDuringWin = false; } return false; }
                if (up && winDown)
                {
                    winDown = false;
                    // a dummy key stops Windows from opening its own Start menu
                    if (!otherKey || swallowedDuringWin) KeyHook.Tap(0xE8);
                    if (!otherKey) Ui(() => ToggleMenu());
                }
                return false;
            }
            if (winDown && !W32.KeyDown(0x5B) && !W32.KeyDown(0x5C)) winDown = false;   // the release was missed (e.g. Win+L)
            if (switching && down && !alt && !W32.KeyDown(0x12)) { switching = false; Ui(CancelSwitcher); }
            // Win+L → own lock screen (still locks). Only while Windows' own Win+L is switched off –
            // otherwise Windows locks anyway and the WinMorph lock screen would follow after unlocking.
            if (down && winDown && vk == 0x4C && lockRedirect)
            {
                swallowedDuringWin = true; otherKey = true;
                Ui(ShellActions.Lock);
                return true;
            }
            if (down && winDown)
            {
                otherKey = true;
                if (vk == 0x58) { swallowedDuringWin = true; Ui(ShowQuickMenu); return true; }                 // Win+X
                if (vk == 0x43 || vk == 0x86) { swallowedDuringWin = true; Ui(OpenAi); return true; }        // Win+C / Copilot key
                if (t.Id == "gaming" && vk == 0x46 && W32.KeyDown(0x10)) { swallowedDuringWin = true; Ui(() => Gaming.ToggleOverlay(t)); return true; }   // Win+Shift+F → overlay
                if (Hacker)
                {
                    if (vk == 0x0D) { swallowedDuringWin = true; Ui(HackerConsole.OpenTerminal); return true; }                        // Win+Enter → terminal
                    if (vk == 0x45) { swallowedDuringWin = true; Ui(() => ShellActions.Files()); return true; }                                             // Win+E → own file manager
                    if (vk == 0x52) { swallowedDuringWin = true; Ui(() => { console?.SafeHide(); HackerPanel.Toggle("run", t, new Rect(0, 0, 0, 0)); }); return true; } // Win+R → own run box
                    if (vk >= 0x31 && vk <= 0x39) { int n = (int)vk - 0x31; swallowedDuringWin = true; Ui(() => FocusNth(n)); return true; } // Win+1..9
                    if (vk == 0x51 && W32.KeyDown(0x10)) { swallowedDuringWin = true; Ui(() => { var fg = W32.GetForegroundWindow(); if (fg != IntPtr.Zero) W32.Close(fg); }); return true; } // Win+Shift+Q
                }
                return false;
            }
            if (vk == 0x86 && down) { Ui(OpenAi); return true; }                                             // Copilot key (F23)
            if (Hacker && audioOk && (vk == 0xAD || vk == 0xAE || vk == 0xAF))                                // volume keys → own display
            {
                if (down) Ui(() => { if (vk == 0xAD) Audio.Muted = !Audio.Muted; else Audio.Change(vk == 0xAF ? 2 : -2); Osd.Volume(t, Audio.Level, Audio.Muted); });
                return true;
            }
            if (vk == 0x7B && down && alt && W32.KeyDown(0x11) && W32.KeyDown(0x10)) { Ui(ShellControl.EmergencyStop); return true; } // Ctrl+Alt+Shift+F12

            if (vk == 0x09 && down && alt)                                                                   // Alt+Tab
            {
                bool back = W32.KeyDown(0x10);
                if (!switching) { switching = true; Ui(() => OpenSwitcher(back)); }
                else Ui(() => switcher?.Move(back ? -1 : 1));
                return true;
            }
            if (switching)
            {
                if (down && vk == 0x1B) { switching = false; Ui(CancelSwitcher); return true; }
                if (down && (vk == 0x25 || vk == 0x26)) { Ui(() => switcher?.Move(-1)); return true; }
                if (down && (vk == 0x27 || vk == 0x28)) { Ui(() => switcher?.Move(1)); return true; }
                if (up && (vk == 0xA4 || vk == 0xA5 || vk == 0x12)) { switching = false; KeyHook.Tap(0xE8); Ui(CommitSwitcher); return false; }
            }
            if (Hacker && vk == 0x1B && down && !alt && W32.KeyDown(0x11) && W32.KeyDown(0x10)) { Ui(ShellActions.TaskManager); return true; } // Ctrl+Shift+Esc → own task manager
            if (vk == 0x1B && down && !alt && W32.KeyDown(0x11) && !W32.KeyDown(0x10)) { Ui(() => ToggleMenu()); return true; } // Ctrl+Esc
            return false;
        }

        bool audioOk;
        static bool LockSaverReady => ShellActions.SaverReady();

        /// <summary>
        /// The screen saver must not be "password protected": then Windows would show its own lock
        /// after it. WinMorph locks by itself instead (its own lock, or the real Windows lock as fallback).
        /// </summary>
        static void MakeSaverHandOver()
        {
            try
            {
                if (Core.Reg.Get(@"HKCU\Control Panel\Desktop", "ScreenSaverIsSecure") as string == "0") return;
                using (var k = Core.Reg.Create(@"HKCU\Control Panel\Desktop")) k.SetValue("ScreenSaverIsSecure", "0");
                Core.Native.SystemParametersInfo(0x0077 /* SPI_SETSCREENSAVESECURE */, 0, IntPtr.Zero, 3);
            }
            catch { }
        }

        void AfterUnlock()
        {
            winDown = false; switching = false;
            Core.Applier.SetAutoHideRaw(true); HideNative();
            foreach (var b in bars) try { b.Place(); } catch { }
        }
        // read on the keyboard thread: a cached flag, refreshed by a timer (registry reads are too slow there)
        volatile bool lockRedirect;
        void RefreshLockRedirect() => lockRedirect = LockSaverReady && Core.LockGuard.Armed;

        void FocusNth(int n)
        {
            var list = tracker.Windows;
            if (n < list.Count) W32.Activate(list[n].Hwnd);
        }

        void OpenSwitcher(bool back)
        {
            HoverPreview.CloseNow();
            var list = tracker.ByRecentUse();
            if (list.Count == 0) { switching = false; return; }
            int start = list.Count > 1 ? (back ? list.Count - 1 : 1) : 0;
            switcher?.Close();
            switcher = new Switcher(t, list, start);
            switcher.Show();
            if (!switching) CommitSwitcher(); // Alt was already released
        }

        void CommitSwitcher()
        {
            var w = switcher?.Selected;
            try { switcher?.Close(); } catch { }
            switcher = null;
            if (w != null) W32.Activate(w.Hwnd);
        }

        void CancelSwitcher() { try { switcher?.Close(); } catch { } switcher = null; }

        void ShowQuickMenu()
        {
            if (Hacker) { HackerPanel.Toggle("power", t, new Rect(0, 0, 0, 0)); return; }
            Rect anchor = startAnchor != null && startBar != null && startAnchor.IsLoaded ? startBar.ScreenRectOf(startAnchor) : new Rect(0, 0, 0, 0);
            var q = new QuickMenu(t, anchor, startBar?.Edge ?? Edge.Bottom);
            q.Show();
        }

        /// <summary>Win+C and the Copilot key always go to ChatGPT.</summary>
        void OpenAi()
        {
            if (Hacker) ToggleMenu(null, "? ");
            else if (t.Menu == MenuKind.Palette || t.Menu == MenuKind.Hub) ToggleMenu(null, Core.Loc.De ? "frag " : "ask ");
            else ShellActions.AskAi(null);
        }

        // ---------------- start menu ----------------
        public void ToggleMenu(FrameworkElement anchorEl = null, string search = null)
        {
            HoverPreview.CloseNow();
            if (Hacker)
            {
                HackerPanel.CloseAll();
                if (console == null) { console = new HackerConsole(t, () => tracker.ByRecentUse()); console.Hidden += () => menuClosed = DateTime.Now; }
                if (console.IsOpen) { console.SafeHide(); return; }
                if ((DateTime.Now - menuClosed).TotalMilliseconds < 250) return;
                console.Open(search);
                return;
            }
            if (menu != null && menu.IsOpen) { menu.CloseMenu(); return; }
            if ((DateTime.Now - menuClosed).TotalMilliseconds < 250) return; // the click that just closed it
            var el = anchorEl ?? startAnchor;
            var bar = (el != null ? Window.GetWindow(el) as BarWindow : null) ?? startBar;
            Rect anchor = el != null && bar != null && el.IsLoaded ? bar.ScreenRectOf(el) : new Rect(0, 0, 0, 0);
            if (menu == null)
            {
                menu = new StartMenu(t, anchor, bar?.Edge ?? Edge.Bottom);
                menu.Hidden += () => menuClosed = DateTime.Now;
            }
            menu.Open(anchor, bar?.Edge ?? Edge.Bottom, search);
        }

        // ---------------- bars ----------------
        void BuildBars()
        {
            switch (t.Layout)
            {
                case BarLayout.Bottom: ClassicBar(); break;
                case BarLayout.MacTopDock: TopBar(gnome: false); BuildDock(Edge.Bottom, floating: true); break;
                case BarLayout.GnomeTopLeftDock: TopBar(gnome: true); BuildDock(Edge.Left, floating: false); break;
                case BarLayout.GnomeTopBottomDock: TopBar(gnome: true); BuildDock(Edge.Bottom, floating: true); break;
                case BarLayout.Floating: FloatingBar(); break;
                case BarLayout.TerminalTop: TerminalBar(); break;
                case BarLayout.AiOnly: AiOnlyBar(); break;
                case BarLayout.Waybar: WaybarBar(); break;
            }
        }

        BarWindow NewBar(Edge edge, double thickness, bool blur, UIElement content, Brush bg)
        {
            if (preview) { previewBars.Add((edge, thickness, content, bg)); return null; }
            uint abgr = 0;
            var w = new BarWindow(edge, thickness, blur, abgr, building) { Content = new Border { Background = bg, Child = content } };
            TextOptions.SetTextFormattingMode(w, t.Btn == BtnStyle.Bevel ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            bars.Add(w);
            return w;
        }

        // ---------- Windows-style bottom bar (95, XP, 7, Vista, 8, 10, Mint, ChromeOS, Gaming) ----------
        void ClassicBar()
        {
            var dock = new DockPanel { LastChildFill = true };
            var start = StartButton();
            DockPanel.SetDock(start, Dock.Left);
            dock.Children.Add(start);

            var tray = Tray();
            DockPanel.SetDock(tray, Dock.Right);
            dock.Children.Add(tray);

            if (t.SearchBox)
            {
                var sb = new Border { Width = 300, Margin = new Thickness(0, 0, 4, 0), Background = B(0xFFFFFF, 235), Cursor = Cursors.IBeam };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
                sp.Children.Add(Glyph("", 14, C(0x333333)));
                sp.Children.Add(T(t.SearchHint, t, 12.5, C(0x555555)));
                ((TextBlock)sp.Children[1]).Margin = new Thickness(10, 0, 0, 0);
                sb.Child = sp;
                sb.MouseLeftButtonUp += (s, e) => ToggleMenu(null, "");
                DockPanel.SetDock(sb, Dock.Left);
                dock.Children.Add(sb);
                var tv = Click(Glyph("", 16, t.BarFg), ShellActions.TaskView, t.BtnHover, pad: new Thickness(14, 0, 14, 0));
                DockPanel.SetDock(tv, Dock.Left);
                dock.Children.Add(tv);
            }

            if (t.QuickLaunch)
            {
                var ql = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                Action fillQl = () =>
                {
                    ql.Children.Clear();
                    foreach (var a in AppCatalog.Pinned().Take(3))
                    {
                        var app = a;
                        var b = Click(Img(app.Icon, 16), app.Start, t.BtnHover, pad: new Thickness(3));
                        b.ToolTip = app.Name;
                        ql.Children.Add(b);
                    }
                };
                fillQl();
                if (!preview) AppCatalog.Loaded += fillQl;
                if (t.Btn == BtnStyle.Bevel) ql.Children.Insert(0, new Border { Width = 2, Height = 20, BorderBrush = B(0x808080), BorderThickness = new Thickness(1, 0, 0, 0), Background = B(0xFFFFFF), Margin = new Thickness(0, 0, 4, 0) });
                DockPanel.SetDock(ql, Dock.Left);
                dock.Children.Add(ql);
            }

            FrameworkElement tasks = t.Btn == BtnStyle.Pill ? DockItems(Orientation.Horizontal, t.DockIcon * 0.72) : TaskButtons();
            if (t.CenterTasks) tasks.HorizontalAlignment = HorizontalAlignment.Center;
            dock.Children.Add(tasks);

            Brush bg = t.BarBg;
            UIElement content = dock;
            if (t.Btn == BtnStyle.Bevel)
                content = new Border { BorderBrush = B(0xFFFFFF), BorderThickness = new Thickness(0, 1, 0, 0), Child = new Border { BorderBrush = B(0xDFDFDF), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(2, 1, 2, 1), Child = dock } };
            else if (t.BarLine.A > 0)
                content = new Border { BorderBrush = B(t.BarLine), BorderThickness = new Thickness(0, t.Id == "gaming" ? 2 : 1, 0, 0), Child = dock };
            if (t.Id == "win7")
            {
                // glossy highlight on the upper half
                var g = new Grid();
                g.Children.Add(new Border { Background = V((0, C(0xFFFFFF, 46)), (0.5, C(0xFFFFFF, 14)), (0.51, C(0xFFFFFF, 0)), (1, C(0xFFFFFF, 0))), IsHitTestVisible = false });
                g.Children.Add(content);
                content = g;
            }
            startBar = NewBar(Edge.Bottom, t.BarHeight, t.Blur, content, bg);
        }

        // ---------- top bar (macOS menu bar / GNOME panel) ----------
        void TopBar(bool gnome)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var start = StartButton();
            left.Children.Add(start);
            if (!gnome)
            {
                var appName = T("", t, 13, t.BarFg, FontWeights.Bold);
                appName.Margin = new Thickness(10, 0, 14, 0);
                left.Children.Add(appName);
                var menus = new StackPanel { Orientation = Orientation.Horizontal };
                string[] items = De ? new[] { "Ablage", "Bearbeiten", "Darstellung", "Fenster", "Hilfe" } : new[] { "File", "Edit", "View", "Window", "Help" };
                // These open the focused app's own menu bar via Alt, like the macOS menu bar.
                foreach (var it in items)
                {
                    var b = Click(T(it, t, 13, t.BarFg), () => { var fg = tracker.Foreground; if (fg != IntPtr.Zero) { W32.Activate(fg); W32.SendKeys(0x12); } }, t.BtnHover, radius: new CornerRadius(4), pad: new Thickness(8, 1, 8, 1));
                    menus.Children.Add(b);
                }
                left.Children.Add(menus);
                refreshers.Add(() =>
                {
                    var w = tracker.Windows.FirstOrDefault(x => x.Hwnd == tracker.Foreground);
                    appName.Text = w != null ? w.AppName : (De ? "Finder" : "Finder");
                });
            }
            g.Children.Add(left);

            var clockText = T("", t, 13, t.BarFg, gnome ? FontWeights.SemiBold : FontWeights.Normal);
            var clockBtn = Click(clockText, ShellActions.Notifications, t.BtnHover, radius: new CornerRadius(12), pad: new Thickness(12, 1, 12, 1));
            clockUpdaters.Add(() =>
            {
                var now = DateTime.Now; var ci = De ? new CultureInfo("de-DE") : new CultureInfo("en-US");
                clockText.Text = gnome ? now.ToString(De ? "d. MMM  HH:mm" : "MMM d  HH:mm", ci) : now.ToString(De ? "ddd d. MMM  HH:mm" : "ddd MMM d  h:mm tt", ci);
            });

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            var sys = new StackPanel { Orientation = Orientation.Horizontal };
            sys.Children.Add(Glyph("", 13, t.BarFg));
            var vol = Glyph("", 13, t.BarFg); vol.Margin = new Thickness(10, 0, 0, 0); sys.Children.Add(vol);
            if (gnome) { var pw = Glyph("", 13, t.BarFg); pw.Margin = new Thickness(10, 0, 0, 0); sys.Children.Add(pw); }
            right.Children.Add(Click(sys, ShellActions.QuickSettings, t.BtnHover, radius: new CornerRadius(12), pad: new Thickness(10, 3, 10, 3)));

            if (gnome)
            {
                Grid.SetColumn(clockBtn, 1);
                g.Children.Add(clockBtn);
            }
            else right.Children.Add(clockBtn);
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            var bar = NewBar(Edge.Top, t.BarHeight, t.Blur, g, t.BarBg);
            startBar = bar;
        }

        // ---------- dock (macOS / GNOME) ----------
        void BuildDock(Edge edge, bool floating)
        {
            bool vertical = edge == Edge.Left;
            double icon = t.DockIcon;
            var items = DockItems(vertical ? Orientation.Vertical : Orientation.Horizontal, icon);
            FrameworkElement content;
            if (floating)
            {
                bool dark = t.Id != "macos";
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(t.Radius), Padding = new Thickness(8, 5, 8, 5),
                    Background = dark ? B(0x1E1E22, 175) : B(0xFFFFFF, 120), BorderBrush = B(0xFFFFFF, dark ? (byte)45 : (byte)150), BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6),
                    Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.3 },
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                if (t.Layout == BarLayout.MacTopDock) row.Children.Add(DockTile(null, "dots", De ? "Launchpad" : "Launchpad", false, false, () => ToggleMenu(), icon, C(0x7B6CF6)));
                row.Children.Add(items);
                row.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 8, 6, 8), Background = B(dark ? 0xFFFFFFu : 0x000000u, 60) });
                row.Children.Add(DockTile(W32.ShellItemIcon("::{645FF040-5081-101B-9F08-00AA002F954E}", 64), null, De ? "Papierkorb" : "Trash", false, false, () => ShellActions.Run("explorer.exe", "shell:RecycleBinFolder"), icon, Colors.Gray));
                if (t.Layout != BarLayout.MacTopDock) row.Children.Insert(0, DockTile(null, "dots", De ? "Apps anzeigen" : "Show apps", false, false, () => ToggleMenu(), icon, C(0x4A4A4A)));
                pill.Child = row;
                content = pill;
                NewBar(Edge.Bottom, icon + 26, false, content, Brushes.Transparent);
            }
            else
            {
                var panel = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 6, 0, 6) };
                var showApps = DockTile(null, "dots", De ? "Apps anzeigen" : "Show apps", false, false, () => ToggleMenu(), icon, C(0x3A3A3A));
                DockPanel.SetDock(showApps, System.Windows.Controls.Dock.Bottom);
                panel.Children.Add(showApps);
                DockPanel.SetDock(items, System.Windows.Controls.Dock.Top);
                panel.Children.Add(items);
                content = panel;
                NewBar(Edge.Left, icon + 22, false, content, B(0x1D1D1D, 235));
            }
        }

        // ---------- floating pill (NovaOS, Zen) ----------
        void FloatingBar()
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(t.Radius), Height = t.BarHeight, Padding = new Thickness(10, 0, 14, 0),
                Background = t.BarBg, BorderBrush = B(t.BarLine.A > 0 ? t.BarLine : C(0xFFFFFF, 40)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = t.Id == "zen" ? 0.18 : 0.6, Color = t.Id == "zen" ? Colors.Black : t.Accent },
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(StartButton());
            row.Children.Add(new Border { Width = 1, Margin = new Thickness(10, 12, 10, 12), Background = B(A(t.BarFg, 50)) });
            row.Children.Add(DockItems(Orientation.Horizontal, t.DockIcon));
            row.Children.Add(new Border { Width = 1, Margin = new Thickness(10, 12, 10, 12), Background = B(A(t.BarFg, 50)) });
            row.Children.Add(Tray());
            pill.Child = row;
            startBar = NewBar(Edge.Bottom, t.BarHeight + 18, false, pill, Brushes.Transparent);
        }

        // ---------- Nexus AI: one floating command capsule, nothing else ----------
        void AiOnlyBar()
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(t.Radius), Height = t.BarHeight, Width = 1000, Padding = new Thickness(8, 0, 14, 0),
                Background = t.BarBg, BorderBrush = new LinearGradientBrush(C(0x9B87FF), C(0x22D3EE), 0), BorderThickness = new Thickness(1.3),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.65, Color = t.Accent },
            };
            var dock = new DockPanel();
            var start = StartButton();
            DockPanel.SetDock(start, System.Windows.Controls.Dock.Left);
            dock.Children.Add(start);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(DockItems(Orientation.Horizontal, t.DockIcon, runningOnly: true));
            right.Children.Add(new Border { Width = 1, Margin = new Thickness(8, 14, 8, 14), Background = B(A(t.BarFg, 50)) });
            right.Children.Add(Tray());
            DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
            dock.Children.Add(right);

            var box = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = B(t.BarFg), CaretBrush = B(t.Accent), FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(t.Font) };
            var hint = new TextBlock { Text = t.SearchHint, Foreground = B(t.BarFgDim), FontSize = 15, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), FontFamily = new FontFamily(t.Font) };
            var g = new Grid { Margin = new Thickness(6, 0, 6, 0) };
            g.Children.Add(hint); g.Children.Add(box);
            var prompt = new Border { Child = g, CornerRadius = new CornerRadius(20), Background = B(0xFFFFFF, 14), Margin = new Thickness(6, 9, 6, 9), Padding = new Thickness(12, 0, 12, 0), BorderBrush = B(0xFFFFFF, 26), BorderThickness = new Thickness(1) };
            box.TextChanged += (s, e) =>
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (box.Text.Length > 0) { var text = box.Text; box.Text = ""; ToggleMenu(start, text); }
            };
            dock.Children.Add(prompt);
            pill.Child = dock;
            startBar = NewBar(Edge.Bottom, t.BarHeight + 22, false, pill, Brushes.Transparent);
        }

        // ---------- Arch Linux: floating Waybar ----------
        void WaybarBar()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
            left.Children.Add(StartButton());
            var ws = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(ws);
            refreshers.Add(() =>
            {
                ws.Children.Clear();
                int n = 1;
                foreach (var w in tracker.Windows)
                {
                    var win = w;
                    bool active = w.Hwnd == tracker.Foreground && !w.Minimized;
                    var tb = T((n++).ToString(), t, 12.5, active ? C(0x0B1016) : (w.Minimized ? t.BarFgDim : t.BarFg), active ? FontWeights.Bold : FontWeights.Normal);
                    tb.HorizontalAlignment = HorizontalAlignment.Center;
                    var b = Click(tb, () => ToggleWindow(win), A(t.Accent, 70), active ? B(t.Accent) : null, new CornerRadius(6));
                    b.Width = 26; b.Height = 22; b.Margin = new Thickness(2, 0, 2, 0); b.ToolTip = w.Title;
                    AttachExtras(b, () => new List<WinInfo> { win }, null, win);
                    ws.Children.Add(b);
                }
            });
            g.Children.Add(left);

            var title = T("", t, 12.5, t.BarFg);
            title.MaxWidth = 520;
            Grid.SetColumn(title, 1);
            refreshers.Add(() => { var w = tracker.Windows.FirstOrDefault(x => x.Hwnd == tracker.Foreground); title.Text = w?.Title ?? "~"; });
            g.Children.Add(title);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
            FrameworkElement Module(TextBlock text, Action click, Color? color = null)
            {
                var b = Click(text, click, A(t.Accent, 60), B(0xFFFFFF, 12), new CornerRadius(7), new Thickness(9, 1, 9, 1));
                b.Margin = new Thickness(3, 5, 3, 5);
                if (color.HasValue) text.Foreground = B(color.Value);
                return b;
            }
            var cpu = T("", t, 12); var mem = T("", t, 12); var clockText = T("", t, 12);
            right.Children.Add(Module(cpu, ShellActions.TaskManager, C(0x89B4FA)));
            right.Children.Add(Module(mem, ShellActions.TaskManager, C(0xA6E3A1)));
            var net = new StackPanel { Orientation = Orientation.Horizontal };
            net.Children.Add(Glyph("\uE701", 12, C(0xF9E2AF))); var vol = Glyph("\uE767", 12, C(0xF5C2E7)); vol.Margin = new Thickness(9, 0, 0, 0); net.Children.Add(vol);
            var netBtn = Click(net, ShellActions.QuickSettings, A(t.Accent, 60), B(0xFFFFFF, 12), new CornerRadius(7), new Thickness(9, 1, 9, 1));
            netBtn.Margin = new Thickness(3, 5, 3, 5);
            right.Children.Add(netBtn);
            right.Children.Add(Module(clockText, ShellActions.Notifications, t.Accent));
            clockUpdaters.Add(() =>
            {
                cpu.Text = "cpu " + SysStats.Cpu().ToString("0") + "%";
                mem.Text = "mem " + SysStats.Mem().ToString("0") + "%";
                clockText.Text = DateTime.Now.ToString(De ? "ddd dd MMM  HH:mm" : "ddd MMM dd  HH:mm", De ? new CultureInfo("de-DE") : new CultureInfo("en-US"));
            });
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            var barBorder = new Border { Child = g, Margin = new Thickness(8, 6, 8, 2), CornerRadius = new CornerRadius(t.Radius), Background = t.BarBg, BorderBrush = B(A(t.Accent, 150)), BorderThickness = new Thickness(1) };
            startBar = NewBar(Edge.Top, t.BarHeight + 10, false, barBorder, Brushes.Transparent);
        }

        // ---------- terminal status line (hacker look) ----------
        void TerminalBar()
        {
            var dock = new DockPanel();
            var start = StartButton();
            DockPanel.SetDock(start, System.Windows.Controls.Dock.Left);
            dock.Children.Add(start);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
            Color dim = t.BarFgDim;
            FrameworkElement Mod(TextBlock tb, string panel, string tip)
            {
                Border b = null;
                b = Click(tb, () => OpenPanel(panel, b), t.BtnHover, pad: new Thickness(7, 0, 7, 0));
                return b;
            }
            void Sep() => right.Children.Add(T("│", t, 12.5, A(dim, 150)));
            var net = T("", t, 12.5, dim); var cpu = T("", t, 12.5, t.BarFg); var mem = T("", t, 12.5, t.BarFg);
            var vol = T("", t, 12.5, t.BarFg); var bat = T("", t, 12.5, t.BarFg); var clockText = T("", t, 12.5, t.BarFg);
            right.Children.Add(Mod(net, "net", De ? "Netzwerk" : "Network")); Sep();
            right.Children.Add(Mod(cpu, "sys", De ? "System-Monitor" : "System monitor")); Sep();
            right.Children.Add(Mod(mem, "sys", De ? "System-Monitor" : "System monitor")); Sep();
            var volMod = Mod(vol, "vol", De ? "Lautstärke (Mausrad ändert)" : "Volume (mouse wheel changes it)");
            volMod.MouseWheel += (s, e) => { Audio.Change(e.Delta > 0 ? 2 : -2); UpdateVol(); Osd.Volume(t, Audio.Level, Audio.Muted); };
            right.Children.Add(volMod); Sep();
            bool hasBat = SysStats.Battery(out _, out _);
            if (hasBat) { right.Children.Add(Mod(bat, "power", De ? "Akku und Energie" : "Battery and power")); Sep(); }
            right.Children.Add(Mod(clockText, "cal", De ? "Kalender" : "Calendar")); Sep();
            right.Children.Add(Mod(T("[pwr]", t, 12.5, t.BarFg, FontWeights.Bold), "power", De ? "Herunterfahren, Neustart, Sperren …" : "Shut down, restart, lock …"));
            DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
            dock.Children.Add(right);
            void UpdateVol()
            {
                int l = Audio.Level;
                vol.Text = l < 0 ? "vol --" : (Audio.Muted ? "mute" : "vol " + l.ToString("00") + "%");
                vol.Foreground = B(Audio.Muted ? dim : t.BarFg);
            }
            int tick = 0;
            clockUpdaters.Add(() =>
            {
                clockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                double c = SysInfo.CpuSample(), m = SysStats.Mem();
                cpu.Text = "cpu " + c.ToString("00") + "% " + SysInfo.Sparkline(SysInfo.CpuHistory.Skip(Math.Max(0, SysInfo.CpuHistory.Count - 8)));
                cpu.Foreground = B(c > 85 ? C(0xFFB000) : t.BarFg);
                mem.Text = "mem " + m.ToString("00") + "%";
                mem.Foreground = B(m > 90 ? C(0xFFB000) : t.BarFg);
                SysInfo.NetRate(out double rx, out double tx);
                net.Text = "\u2193" + SysInfo.Bytes(rx).PadLeft(5) + " \u2191" + SysInfo.Bytes(tx).PadLeft(5);
                if (tick++ % 2 == 0) UpdateVol();
                if (hasBat && SysStats.Battery(out int pct, out bool ac))
                {
                    bat.Text = "bat " + pct + "%" + (ac ? "+" : "");
                    bat.Foreground = B(pct < 20 && !ac ? C(0xFF4D4D) : t.BarFg);
                    if (lastAc != null && ac != lastAc) Notify.Show("power", ac ? (De ? "netzteil angeschlossen · " : "charger connected · ") + pct + "%" : (De ? "akkubetrieb · " : "on battery · ") + pct + "%", null, 4);
                    if (!ac && (pct == 20 || pct == 10 || pct == 5) && pct != lastWarn) { lastWarn = pct; Notify.Show("power", (De ? "akku niedrig: " : "battery low: ") + pct + "%", De ? "netzteil anschließen" : "plug in the charger", 10); }
                    lastAc = ac;
                }
            });
            dock.Children.Add(TaskButtons());
            startBar = NewBar(Edge.Top, t.BarHeight, false, new Border { BorderBrush = B(A(t.Accent, 120)), BorderThickness = new Thickness(0, 0, 0, 1), Child = dock }, t.BarBg);
        }

        bool? lastAc; int lastWarn;

        void OpenPanel(string kind, FrameworkElement el)
        {
            HoverPreview.CloseNow();
            var bar = el != null ? Window.GetWindow(el) as BarWindow : null;
            var r = bar != null && el.IsLoaded ? bar.ScreenRectOf(el) : new Rect(0, 0, 0, 0);
            HackerPanel.Toggle(kind, t, r);
        }

        // ---------------- start buttons ----------------
        FrameworkElement StartButton()
        {
            FrameworkElement el;
            FrameworkElement self = null;
            string startText = De ? "Start" : "Start";
            switch (t.Start)
            {
                case StartKind.Classic:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(ClassicLogo(15));
                    sp.Children.Add(new TextBlock { Text = startText, FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = B(0x000000), Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Microsoft Sans Serif") });
                    var bevel = new Bevel { Child = new Border { Padding = new Thickness(2, 0, 4, 0), Child = sp }, Margin = new Thickness(0, 1, 4, 1) };
                    bevel.MouseLeftButtonDown += (s, e) => { bevel.Sunken = true; bevel.InvalidateVisual(); bevel.InvalidateArrange(); };
                    bevel.MouseLeftButtonUp += (s, e) => { bevel.Sunken = false; bevel.InvalidateVisual(); bevel.InvalidateArrange(); ToggleMenu(self); };
                    bevel.MouseLeave += (s, e) => { if (bevel.Sunken) { bevel.Sunken = false; bevel.InvalidateVisual(); } };
                    el = bevel;
                    break;
                }
                case StartKind.Xp:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 22, 0) };
                    sp.Children.Add(ClassicLogo(18));
                    sp.Children.Add(new TextBlock { Text = De ? "start" : "start", Foreground = B(0xFFFFFF), FontSize = 18, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, FontFamily = new FontFamily("Trebuchet MS, Franklin Gothic Medium, Tahoma"), Margin = new Thickness(6, -2, 0, 0), VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { ShadowDepth = 1.5, BlurRadius = 2, Opacity = 0.7 } });
                    var normal = V((0, C(0x5DBB4B)), (0.12, C(0x3E9E33)), (0.5, C(0x379131)), (0.88, C(0x2E7F28)), (1, C(0x1E5E18)));
                    var b = Click(sp, () => ToggleMenu(self), C(0x4FB43F), normal, new CornerRadius(0, 12, 12, 0));
                    b.Margin = new Thickness(0, 0, 10, 0);
                    b.BorderBrush = B(0x1E5E18); b.BorderThickness = new Thickness(0, 0, 1, 0);
                    el = b;
                    break;
                }
                case StartKind.Orb:
                {
                    var orb = new Grid { Width = 40, Height = 40 };
                    var glass = new Ellipse_ { Fill = new RadialGradientBrush(C(0x6EC1FF), C(0x0B3C78)) { GradientOrigin = new Point(0.5, 0.25) }, Stroke = B(0xFFFFFF, 140) };
                    orb.Children.Add(glass.Make(36));
                    orb.Children.Add(new Ellipse_ { Fill = V((0, C(0xFFFFFF, 140)), (1, C(0xFFFFFF, 0))) }.Make(34, 17, top: true));
                    orb.Children.Add(Logo4(16, C(0xF35325), C(0x81BC06), C(0x05A6F0), C(0xFFBA08), -4));
                    var b = Click(orb, () => ToggleMenu(self), C(0x000000, 0), pad: new Thickness(6, 0, 8, 0));
                    b.MouseEnter += (s, e) => orb.Effect = new DropShadowEffect { Color = C(0x7FD4FF), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
                    b.MouseLeave += (s, e) => orb.Effect = null;
                    el = b;
                    break;
                }
                case StartKind.Logo:
                {
                    var logo = Logo4(16, Colors.White, Colors.White, Colors.White, Colors.White);
                    el = Click(logo, () => ToggleMenu(self), A(t.Accent, 200), pad: new Thickness(16, 0, 16, 0));
                    break;
                }
                case StartKind.Mint:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = V((0, C(0xA6E26B)), (1, C(0x6FA33A))), Child = new TextBlock { Text = "m", Foreground = B(0xFFFFFF), FontWeight = FontWeights.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) } });
                    sp.Children.Add(T(De ? "Menü" : "Menu", t, 12.5));
                    ((TextBlock)sp.Children[1]).Margin = new Thickness(8, 0, 0, 0);
                    el = Click(sp, () => ToggleMenu(self), t.BtnHover, pad: new Thickness(10, 0, 12, 0));
                    break;
                }
                case StartKind.Activities:
                {
                    var tb = T(De ? "Aktivitäten" : "Activities", t, 13, t.BarFg, FontWeights.SemiBold);
                    el = Click(tb, () => ToggleMenu(self), t.BtnHover, radius: new CornerRadius(12), pad: new Thickness(12, 2, 12, 2));
                    el.Margin = new Thickness(4, 0, 0, 0);
                    break;
                }
                case StartKind.Launcher:
                {
                    var ring = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderBrush = B(0xFFFFFF), BorderThickness = new Thickness(2.5) };
                    var b = Click(ring, () => ToggleMenu(self), C(0xFFFFFF, 30), radius: new CornerRadius(18), pad: new Thickness(10));
                    b.Margin = new Thickness(8, 0, 0, 0);
                    b.VerticalAlignment = VerticalAlignment.Center;
                    el = b;
                    break;
                }
                case StartKind.AiOrb:
                {
                    var orb = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new RadialGradientBrush(C(0xB8F7FF), C(0x0077A8)) { GradientOrigin = new Point(0.35, 0.3) } };
                    var glow = new DropShadowEffect { Color = t.Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 };
                    orb.Effect = glow;
                    var pulse = new DoubleAnimation(8, 26, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                    glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, pulse);
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(orb);
                    sp.Children.Add(T("Synapse", t, 13.5, t.BarFg, FontWeights.SemiBold));
                    ((TextBlock)sp.Children[1]).Margin = new Thickness(9, 0, 4, 0);
                    el = Click(sp, () => ToggleMenu(self), A(t.Accent, 35), radius: new CornerRadius(10), pad: new Thickness(6, 4, 8, 4));
                    break;
                }
                case StartKind.Nova:
                {
                    var diamond = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush(C(0xC084FC), C(0x6D28D9), 45), RenderTransform = new RotateTransform(45), RenderTransformOrigin = new Point(0.5, 0.5) };
                    diamond.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(diamond);
                    sp.Children.Add(T("nova", t, 15, C(0xEDE9FE), FontWeights.SemiBold));
                    ((TextBlock)sp.Children[1]).Margin = new Thickness(12, 0, 2, 0);
                    el = Click(sp, () => ToggleMenu(self), A(t.Accent, 45), radius: new CornerRadius(18), pad: new Thickness(10, 6, 12, 6));
                    break;
                }
                case StartKind.Terminal:
                    el = Click(T(" [λ] menu ", t, 12.5, C(0x000000), FontWeights.Bold), () => ToggleMenu(self), C(0x7CFF9B), B(t.Accent), pad: new Thickness(4, 0, 4, 0));
                    break;
                case StartKind.Zen:
                {
                    var ring = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderBrush = B(t.BarFg), BorderThickness = new Thickness(1.5) };
                    el = Click(ring, () => ToggleMenu(self), t.BtnHover, radius: new CornerRadius(14), pad: new Thickness(8));
                    break;
                }
                case StartKind.Gaming:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(Glyph("", 18, Colors.White));
                    sp.Children.Add(T(De ? "SPIELEN" : "PLAY", t, 13, Colors.White, FontWeights.Bold));
                    ((TextBlock)sp.Children[1]).Margin = new Thickness(8, 0, 0, 0);
                    var b = Click(sp, () => ToggleMenu(self), Core.Applier.Mix(t.Accent, Colors.White, 0.2), B(t.Accent), pad: new Thickness(16, 0, 20, 0));
                    b.RenderTransform = new SkewTransform(-14, 0);
                    b.Margin = new Thickness(6, 0, 10, 0);
                    el = b;
                    break;
                }
                case StartKind.Nexus:
                {
                    var orb = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new RadialGradientBrush(C(0xE9E3FF), C(0x4B2FD6)) { GradientOrigin = new Point(0.35, 0.3) } };
                    var glow = new DropShadowEffect { Color = t.Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };
                    orb.Effect = glow;
                    glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(8, 24, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                    el = Click(orb, () => ToggleMenu(self), A(t.Accent, 40), radius: new CornerRadius(20), pad: new Thickness(8));
                    break;
                }
                case StartKind.Arch:
                {
                    var tri = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 9,0 L 18,18 C 13,13 5,13 0,18 Z"), Fill = B(t.Accent), Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(tri);
                    sp.Children.Add(T("arch", t, 13, t.BarFg, FontWeights.Bold));
                    ((TextBlock)sp.Children[1]).Margin = new Thickness(7, 0, 0, 0);
                    el = Click(sp, () => ToggleMenu(self), t.BtnHover, radius: new CornerRadius(7), pad: new Thickness(10, 3, 10, 3));
                    break;
                }
                case StartKind.Apple:
                default:
                {
                    var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = B(t.BarFg) };
                    el = Click(dot, () => ToggleMenu(self), t.BtnHover, radius: new CornerRadius(5), pad: new Thickness(12, 4, 8, 4));
                    break;
                }
            }
            el.ToolTip = De ? "Start (Windows-Taste)" : "Start (Windows key)";
            self = el;
            startAnchor = el;
            return el;
        }

        // ---------------- task buttons (one per window) ----------------
        FrameworkElement TaskButtons()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
            refreshers.Add(() =>
            {
                panel.Children.Clear();
                int i = 1;
                foreach (var w in tracker.Windows)
                {
                    bool active = w.Hwnd == tracker.Foreground && !w.Minimized;
                    var el = TaskButton(w, active, i++);
                    AttachExtras(el, () => new List<WinInfo> { w }, null, w);
                    panel.Children.Add(el);
                }
            });
            return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        void ToggleWindow(WinInfo w)
        {
            if (w.Hwnd == tracker.Foreground && !W32.IsIconic(w.Hwnd)) W32.Minimize(w.Hwnd);
            else W32.Activate(w.Hwnd);
        }

        void WindowMenu(WinInfo w) => ItemMenu(new List<WinInfo> { w }, null);

        AppEntry EntryFor(WinInfo w)
        {
            if (w == null) return null;
            var hit = w.AppId != null ? AppCatalog.Apps.FirstOrDefault(a => string.Equals(a.ParsingPath, w.AppId, StringComparison.OrdinalIgnoreCase)) : null;
            if (hit == null && w.ExePath != null) hit = AppCatalog.Apps.FirstOrDefault(a => a.ExePath != null && string.Equals(a.ExePath, w.ExePath, StringComparison.OrdinalIgnoreCase));
            if (hit == null && w.ExePath != null) hit = new AppEntry { Name = w.AppName, ExePath = w.ExePath, Launch = w.ExePath };
            return hit;
        }

        /// <summary>Right-click menu for a taskbar button or dock icon.</summary>
        void ItemMenu(List<WinInfo> wins, AppEntry app)
        {
            app = app ?? EntryFor(wins.FirstOrDefault());
            var items = new List<(string, Action)>();
            if (app != null)
            {
                var a = app;
                items.Add((De ? "Neues Fenster" : "New window", a.Start));
                if (AppCatalog.IsPinned(a)) items.Add((De ? "Von der Leiste lösen" : "Unpin from bar", () => AppCatalog.Unpin(a)));
                else items.Add((De ? "An die Leiste anheften" : "Pin to bar", () => AppCatalog.Pin(a)));
            }
            if (wins.Count > 0)
            {
                items.Add(("-", null));
                items.Add((De ? "Minimieren" : "Minimise", () => { foreach (var w in wins) W32.Minimize(w.Hwnd); }));
                items.Add((wins.Count > 1 ? (De ? "Alle Fenster schließen" : "Close all windows") : (De ? "Fenster schließen" : "Close window"), () => { foreach (var w in wins) W32.Close(w.Hwnd); }));
            }
            MiniMenu.Show(t, items);
        }

        /// <summary>Live preview on hover, new window on middle click, themed menu on right click.</summary>
        void AttachExtras(FrameworkElement el, Func<List<WinInfo>> wins, AppEntry app, WinInfo single)
        {
            el.MouseEnter += (s, e) => HoverPreview.Hover(t, el, () => wins().Where(w => W32.IsWindow(w.Hwnd)).ToList(), w => W32.Activate(w.Hwnd));
            el.MouseLeave += (s, e) => HoverPreview.Leave();
            el.PreviewMouseLeftButtonDown += (s, e) => HoverPreview.CloseNow();
            el.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { (app ?? EntryFor(single ?? wins().FirstOrDefault()))?.Start(); e.Handled = true; } };
            el.PreviewMouseRightButtonUp += (s, e) => { e.Handled = true; ItemMenu(wins(), app); };
        }

        FrameworkElement TaskButton(WinInfo w, bool active, int index)
        {
            double h = t.BarHeight;
            switch (t.Btn)
            {
                case BtnStyle.Bevel:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(Img(w.Icon, 16));
                    sp.Children.Add(new TextBlock { Text = w.Title, FontSize = 11.5, FontWeight = active ? FontWeights.Bold : FontWeights.Normal, Foreground = B(0x000000), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = new FontFamily("Microsoft Sans Serif") });
                    var b = new Bevel { Sunken = active, Dither = active, Width = 160, Margin = new Thickness(0, 1, 3, 1), Child = sp, ToolTip = w.Title };
                    b.MouseLeftButtonUp += (s, e) => ToggleWindow(w);
                    b.MouseRightButtonUp += (s, e) => WindowMenu(w);
                    return b;
                }
                case BtnStyle.Luna:
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0) };
                    sp.Children.Add(Img(w.Icon, 16));
                    sp.Children.Add(new TextBlock { Text = w.Title, FontSize = 11.5, Foreground = B(0xFFFFFF), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    Brush bg = active ? V((0, C(0x1A49B5)), (0.15, C(0x1E52B7)), (1, C(0x2C66D6))) : V((0, C(0x4B92F8)), (0.1, C(0x3C81F3)), (0.9, C(0x3277EC)), (1, C(0x2463D5)));
                    var b = Click(sp, () => ToggleWindow(w), C(0x5A9AF8), bg, new CornerRadius(3), onRight: () => WindowMenu(w));
                    b.Width = 160; b.Margin = new Thickness(0, 4, 3, 3);
                    b.BorderBrush = B(active ? 0x10388Fu : 0x1B52C9u); b.BorderThickness = new Thickness(1);
                    b.ToolTip = w.Title;
                    return b;
                }
                case BtnStyle.Glass:
                {
                    var content = t.Labels ? (UIElement)LabelContent(w, 22, 12) : Img(w.Icon, 26);
                    var bg = active ? V((0, C(0xFFFFFF, 90)), (0.5, C(0xFFFFFF, 40)), (1, C(0xFFFFFF, 60))) : V((0, C(0xFFFFFF, 40)), (1, C(0xFFFFFF, 12)));
                    var b = Click(content, () => ToggleWindow(w), C(0xFFFFFF, 70), bg, new CornerRadius(3), onRight: () => WindowMenu(w));
                    b.Width = t.Labels ? 170 : 58; b.Margin = new Thickness(1, 2, 1, 2);
                    b.BorderBrush = B(0xFFFFFF, active ? (byte)170 : (byte)70); b.BorderThickness = new Thickness(1);
                    if (active) b.Effect = new DropShadowEffect { Color = C(0x9FD8FF), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7 };
                    b.ToolTip = w.Title;
                    return b;
                }
                case BtnStyle.Text:
                {
                    var text = " " + index + ":" + Short(w.AppName ?? w.Title, 14) + (w.Minimized ? "-" : (active ? "*" : " ")) + " ";
                    var b = Click(T(text, t, 12.5, active ? C(0x000000) : t.BarFg, active ? FontWeights.Bold : FontWeights.Normal), () => ToggleWindow(w), t.BtnHover, active ? B(t.Accent) : null, onRight: () => WindowMenu(w));
                    b.ToolTip = w.Title;
                    return b;
                }
                case BtnStyle.Angular:
                {
                    var g = new Grid { Width = 54 };
                    g.Children.Add(Img(w.Icon, 24));
                    g.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = B(active ? t.Accent : A(t.Accent, 90)), Margin = new Thickness(active ? 4 : 16, 0, active ? 4 : 16, 0) });
                    var b = Click(g, () => ToggleWindow(w), A(t.Accent, 60), active ? B(A(t.Accent, 70)) : null, onRight: () => WindowMenu(w));
                    b.RenderTransform = new SkewTransform(-14, 0);
                    b.Margin = new Thickness(2, 4, 2, 0);
                    b.ToolTip = w.Title;
                    return b;
                }
                default: // Flat (Windows 8/10, Mint)
                {
                    bool mint = t.Id == "mint";
                    var g = new Grid();
                    g.Children.Add(t.Labels ? (UIElement)LabelContent(w, 18, 12) : Img(w.Icon, 24));
                    var line = new Border { Height = mint ? 2 : 2, VerticalAlignment = mint ? VerticalAlignment.Top : VerticalAlignment.Bottom, Background = B(mint ? t.Accent : Core.Applier.Mix(t.Accent, Colors.White, 0.35)), Margin = new Thickness(active ? 0 : 6, 0, active ? 0 : 6, 0) };
                    g.Children.Add(line);
                    var b = Click(g, () => ToggleWindow(w), t.BtnHover, active ? B(t.BtnActive) : null, onRight: () => WindowMenu(w));
                    b.Width = t.Labels ? 170 : 48;
                    b.ToolTip = w.Title;
                    return b;
                }
            }
        }

        static string Short(string s, int n) => s.Length <= n ? s.ToLowerInvariant() : s.Substring(0, n).ToLowerInvariant();

        StackPanel LabelContent(WinInfo w, double icon, double font)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            sp.Children.Add(Img(w.Icon, icon));
            sp.Children.Add(new TextBlock { Text = w.Title, FontSize = font, Foreground = B(t.BtnFg), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 });
            return sp;
        }

        // ---------------- dock items (pinned + running) ----------------
        FrameworkElement DockItems(Orientation o, double icon, bool runningOnly = false)
        {
            var panel = new StackPanel { Orientation = o, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            List<AppEntry> pinned = null;
            refreshers.Add(() =>
            {
                if (pinned == null || AppCatalog.Apps.Count > 0 && pinned.Count < 5) pinned = AppCatalog.Pinned();
                panel.Children.Clear();
                var used = new HashSet<IntPtr>();
                foreach (var app in pinned)
                {
                    var wins = tracker.Windows.Where(w => app.Matches(w)).ToList();
                    foreach (var w in wins) used.Add(w.Hwnd);
                    bool active = wins.Any(w => w.Hwnd == tracker.Foreground && !w.Minimized);
                    var a = app;
                    if (runningOnly) continue;
                    var tile = DockTile(app.Icon, null, app.Name, wins.Count > 0, active, () => { if (wins.Count == 0) a.Start(); else ToggleGroup(wins); }, icon, Colors.Gray, o == Orientation.Vertical);
                    AttachExtras(tile, () => wins, a, null);
                    panel.Children.Add(tile);
                }
                foreach (var grp in tracker.Windows.Where(w => !used.Contains(w.Hwnd)).GroupBy(w => w.Key))
                {
                    var wins = grp.ToList();
                    bool active = wins.Any(w => w.Hwnd == tracker.Foreground && !w.Minimized);
                    var tile = DockTile(wins[0].Icon, null, wins[0].Title, true, active, () => ToggleGroup(wins), icon, Colors.Gray, o == Orientation.Vertical, wins[0]);
                    AttachExtras(tile, () => wins, null, wins[0]);
                    panel.Children.Add(tile);
                }
            });
            return panel;
        }

        void ToggleGroup(List<WinInfo> wins)
        {
            var fg = wins.FirstOrDefault(w => w.Hwnd == tracker.Foreground && !W32.IsIconic(w.Hwnd));
            if (fg != null)
            {
                int i = wins.IndexOf(fg);
                if (wins.Count > 1) W32.Activate(wins[(i + 1) % wins.Count].Hwnd);
                else W32.Minimize(fg.Hwnd);
            }
            else W32.Activate(wins[0].Hwnd);
        }

        FrameworkElement DockTile(ImageSource icon, string glyph, string name, bool running, bool active, Action click, double size, Color glyphBg, bool vertical = false, WinInfo win = null)
        {
            var g = new Grid { Width = size + 10, Height = size + (vertical ? 10 : 12) };
            FrameworkElement img;
            if (icon == null && glyph == null) { glyph = ""; glyphBg = C(0x3A3D44); } // window without its own icon
            if (icon != null) img = Img(icon, size * 0.86);
            else img = new Border { Width = size * 0.86, Height = size * 0.86, CornerRadius = new CornerRadius(size * 0.22), Background = V((0, Core.Applier.Mix(glyphBg, Colors.White, 0.25)), (1, glyphBg)), Child = glyph == "dots" ? Dots(size * 0.38, Colors.White) : (UIElement)Glyph(glyph, size * 0.4, Colors.White) };
            img.RenderTransformOrigin = vertical ? new Point(0, 0.5) : new Point(0.5, 1);
            var scale = new ScaleTransform(1, 1);
            img.RenderTransform = scale;
            img.VerticalAlignment = VerticalAlignment.Center;
            img.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(img);
            if (running)
            {
                Color dotColor = t.Layout == BarLayout.GnomeTopLeftDock || t.Layout == BarLayout.GnomeTopBottomDock ? t.Accent : (t.Id == "macos" ? C(0x333333) : (t.Id == "zen" ? t.BarFg : C(0xFFFFFF)));
                if (t.Layout == BarLayout.Floating && t.Id != "zen") dotColor = t.Accent;
                var dot = new Border
                {
                    Width = vertical ? 4 : (active ? 14 : 5), Height = vertical ? (active ? 14 : 5) : 4, CornerRadius = new CornerRadius(2), Background = B(dotColor),
                    VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom, HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                    Margin = new Thickness(vertical ? -2 : 0, 0, 0, vertical ? 0 : 0),
                };
                g.Children.Add(dot);
            }
            var b = new Border { Child = g, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name, CornerRadius = new CornerRadius(8) };
            if (active && t.Layout != BarLayout.MacTopDock) b.Background = B(t.BtnActive.A > 0 ? A(t.BtnActive, t.BtnActive.A) : C(0xFFFFFF, 30));
            bool magnify = t.Layout == BarLayout.MacTopDock;
            b.MouseEnter += (s, e) =>
            {
                double to = magnify ? 1.35 : 1.0;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
                if (!magnify) b.Background = B(t.BtnHover);
            };
            b.MouseLeave += (s, e) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                if (!magnify) b.Background = active && t.Layout != BarLayout.MacTopDock ? B(t.BtnActive.A > 0 ? t.BtnActive : C(0xFFFFFF, 30)) : Brushes.Transparent;
            };
            b.MouseLeftButtonUp += (s, e) => click();
            b.Margin = vertical ? new Thickness(4, 1, 4, 1) : new Thickness(1, 0, 1, 0);
            return b;
        }

        // ---------------- tray (clock and system buttons) ----------------
        // ---------- Zen: focus timer, breathing, background sound ----------
        void ZenTray(StackPanel sp, Color fg, Color hover)
        {
            Border Item(TextBlock tb, Action a, string tip) { var b = Click(tb, a, hover, pad: new Thickness(8, 0, 8, 0), radius: new CornerRadius(12)); b.ToolTip = tip; sp.Children.Add(b); return b; }
            TextBlock Txt(string s) => new TextBlock { Text = s, Foreground = B(fg), FontFamily = new FontFamily(t.Font), FontSize = t.FontSize, VerticalAlignment = VerticalAlignment.Center };
            var timer = Txt(Zen.TimerText);
            Item(timer, () => { Zen.ToggleTimer(); timer.Text = Zen.TimerText; }, De ? "Fokus-Timer: 25 Minuten Fokus, dann 5 Minuten Pause (Klick = Start/Stopp)" : "Focus timer: 25 minutes of focus, then a 5 minute break (click = start/stop)");
            Item(Txt("≋"), () => Zen.Breathe(t), De ? "Atemübung (1 Minute)" : "Breathing exercise (1 minute)");
            var sound = Txt(Zen.SoundText);
            Item(sound, () => { Zen.NextSound(); sound.Text = Zen.SoundText; }, De ? "Hintergrundklang: aus → Rauschen → Regen" : "Background sound: off → noise → rain");
            if (!preview) clockUpdaters.Add(() => { Zen.Tick(); timer.Text = Zen.TimerText; });
        }

        // ---------- Gaming: live values, game library, performance mode ----------
        void GamingTray(StackPanel sp, Color fg, Color hover)
        {
            Border Item(TextBlock tb, Action a, string tip) { var b = Click(tb, a, hover, pad: new Thickness(8, 0, 8, 0)); b.ToolTip = tip; sp.Children.Add(b); return b; }
            TextBlock Txt(string s, Color c) => new TextBlock { Text = s, Foreground = B(c), FontFamily = new FontFamily(t.Font), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var stats = Txt(preview ? "CPU 9%   GPU 21%   RAM 48%   18 ms" : "", A(fg, 210));
            Item(stats, () => Gaming.ToggleOverlay(t), De ? "Live-Werte · Klick oder Win+Umschalt+F: Overlay im Spiel" : "Live values · click or Win+Shift+F: in-game overlay");
            Item(Txt(De ? "🎮 SPIELE" : "🎮 GAMES", fg), () => Gaming.Library(t), De ? "Spiele-Bibliothek (Steam, Epic)" : "Game library (Steam, Epic)");
            var perf = Txt("⚡", Gaming.PerfOn ? t.Accent : A(fg, 150));
            Item(perf, () => { Gaming.TogglePerf(); perf.Foreground = B(Gaming.PerfOn ? t.Accent : A(fg, 150)); }, De ? "Leistungsmodus an/aus (wird beim Beenden zurückgesetzt)" : "Performance mode on/off (reset when you stop)");
            int tick = 0;
            if (!preview) clockUpdaters.Add(() => { if (tick++ % 2 == 0) stats.Text = Gaming.Stats(); });
        }

        FrameworkElement Tray()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
            Color fg = t.Btn == BtnStyle.Bevel ? C(0x000000) : t.BarFg;
            Color hover = t.Btn == BtnStyle.Bevel ? C(0x000000, 20) : t.BtnHover;
            FrameworkElement G(string glyph, Action a, string tip) { var b = Click(Glyph(glyph, 13, fg), a, hover, pad: new Thickness(6, 0, 6, 0), radius: new CornerRadius(t.Btn == BtnStyle.Pill ? 8 : 0)); b.ToolTip = tip; return b; }
            sp.Children.Add(G("", ShellActions.QuickSettings, De ? "Netzwerk" : "Network"));
            sp.Children.Add(G("", ShellActions.QuickSettings, De ? "Lautstärke" : "Volume"));

            var clockText = new TextBlock { Foreground = B(fg), FontFamily = new FontFamily(t.Font), FontSize = t.ShowDate ? 11.5 : t.FontSize, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var clockBtn = Click(clockText, ShellActions.Notifications, hover, pad: new Thickness(8, 0, 8, 0), radius: new CornerRadius(t.Btn == BtnStyle.Pill ? 8 : 0));
            if (SysStats.Battery(out _, out _))
            {
                var bat = new TextBlock { Foreground = B(fg), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(t.Font) };
                var batBtn = Click(bat, () => ShellActions.Run("ms-settings:batterysaver"), hover, pad: new Thickness(6, 0, 6, 0), radius: new CornerRadius(t.Btn == BtnStyle.Pill ? 8 : 0));
                sp.Children.Add(batBtn);
                clockUpdaters.Add(() => { if (SysStats.Battery(out int pct, out bool ac)) bat.Text = (ac ? "\u26A1" : "") + pct + "%"; });
            }
            if (t.Id == "zen") ZenTray(sp, fg, hover);
            if (t.Id == "gaming") GamingTray(sp, fg, hover);
            sp.Children.Add(clockBtn);
            clockUpdaters.Add(() =>
            {
                var now = DateTime.Now;
                clockText.Text = t.ShowDate && t.Layout == BarLayout.Bottom ? now.ToString("HH:mm") + "\n" + now.ToString(De ? "dd.MM.yyyy" : "yyyy-MM-dd") : now.ToString("HH:mm");
            });

            if (t.Layout == BarLayout.Bottom && t.Btn != BtnStyle.Pill)
            {
                var desk = new Border { Width = t.Btn == BtnStyle.Glass ? 14 : 6, Background = Brushes.Transparent, BorderBrush = B(A(fg, 70)), BorderThickness = new Thickness(1, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = De ? "Desktop anzeigen" : "Show desktop", Margin = new Thickness(4, 0, 0, 0) };
                desk.MouseLeftButtonUp += (s, e) => ShellActions.ShowDesktop();
                sp.Children.Add(desk);
            }

            switch (t.Btn)
            {
                case BtnStyle.Bevel:
                    return new Bevel { Sunken = true, Child = sp, Margin = new Thickness(2, 1, 0, 1) };
                case BtnStyle.Luna:
                    return new Border
                    {
                        Child = sp, Padding = new Thickness(6, 0, 4, 0),
                        Background = V((0, C(0x1BA0F0)), (0.08, C(0x18A5F2)), (0.5, C(0x0E8BE8)), (1, C(0x0A6DD3))),
                        BorderBrush = B(0x0B4FB8), BorderThickness = new Thickness(1, 0, 0, 0),
                    };
                case BtnStyle.Pill when t.Layout == BarLayout.Bottom:
                    return new Border { Child = sp, Background = B(0xFFFFFF, 25), CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 7, 8, 7), Padding = new Thickness(6, 0, 6, 0) };
                default:
                    return sp;
            }
        }
    }

    /// <summary>Tiny helper to create ellipses without a lot of code.</summary>
    internal sealed class Ellipse_
    {
        public Brush Fill; public Brush Stroke;
        public System.Windows.Shapes.Ellipse Make(double w, double h = 0, bool top = false)
            => new System.Windows.Shapes.Ellipse
            {
                Width = w, Height = h > 0 ? h : w, Fill = Fill, Stroke = Stroke, StrokeThickness = Stroke != null ? 1 : 0,
                VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = top ? new Thickness(0, 3, 0, 0) : new Thickness(0),
            };
    }

    /// <summary>CPU and memory usage for the terminal status line.</summary>
    internal static class SysStats
    {
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public uint len, load; public ulong total, avail, tp, ap, tv, av, ae; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
        static long li, lk, lu;

        static double lastCpu;
        static DateTime lastCpuAt;

        /// <summary>CPU load since the previous reading (cached, so several callers can share it).</summary>
        public static double Cpu()
        {
            if ((DateTime.Now - lastCpuAt).TotalMilliseconds < 900) return lastCpu;
            lastCpuAt = DateTime.Now;
            GetSystemTimes(out long i, out long k, out long u);
            long di = i - li, dt = (k - lk) + (u - lu);
            li = i; lk = k; lu = u;
            return lastCpu = dt <= 0 ? 0 : Math.Max(0, Math.Min(99, 100.0 * (dt - di) / dt));
        }

        [StructLayout(LayoutKind.Sequential)] struct POWERSTATUS { public byte ac, flag, percent, saver; public int life, full; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out POWERSTATUS s);

        /// <summary>False when the PC has no battery.</summary>
        public static bool Battery(out int percent, out bool charging)
        {
            percent = 0; charging = false;
            if (!GetSystemPowerStatus(out var p) || p.flag == 128 || p.flag == 255 || p.percent > 100) return false;
            percent = p.percent; charging = p.ac == 1;
            return true;
        }

        public static void MemTotals(out ulong total, out ulong avail)
        {
            var m = new MEMSTAT { len = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            GlobalMemoryStatusEx(ref m);
            total = m.total; avail = m.avail;
        }

        public static double Mem()
        {
            var m = new MEMSTAT { len = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            GlobalMemoryStatusEx(ref m);
            return Math.Min(99, m.load);
        }
    }
}

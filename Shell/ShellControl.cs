using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinMorph.Shell
{
    /// <summary>Starts, stops and hosts the background shell process (WinMorph.exe --shell).</summary>
    public static class ShellControl
    {
        public const string SettingsKey = @"HKCU\Software\WinMorph";
        public const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "WinMorph Shell";
        const string MutexName = "WinMorphShell_v1", ExitName = "WinMorphShellExit_v1", SwitchName = "WinMorphShellSwitch_v1";

        static string Exe => Process.GetCurrentProcess().MainModule.FileName;

        public static bool IsRunning
        {
            get
            {
                try { using (var m = Mutex.OpenExisting(MutexName)) return true; }
                catch { return false; }
            }
        }

        /// <summary>Asks the shell to quit; a shell that does not answer within 4 s is ended.</summary>
        /// <param name="keepTaskbar">A new look follows right away: the Windows taskbar stays hidden in between.</param>
        public static void Stop(bool keepTaskbar = false)
        {
            try
            {
                if (keepTaskbar && EventWaitHandle.TryOpenExisting(SwitchName, out var sw)) { sw.Set(); sw.Dispose(); }
                if (EventWaitHandle.TryOpenExisting(ExitName, out var ev)) { ev.Set(); ev.Dispose(); }
            }
            catch { }
            for (int i = 0; i < 40 && IsRunning; i++) Thread.Sleep(100);
            if (IsRunning && Core.Reg.Get(SettingsKey, "ShellPid") is int pid && pid != Process.GetCurrentProcess().Id)
            {
                try { using (var p = Process.GetProcessById(pid)) if (p.ProcessName.Equals("WinMorph", StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } } catch { }
            }
        }

        // ---------------- the Windows taskbar ----------------
        // The shell hides the Windows taskbar and switches it to auto-hide (so maximised windows use the
        // whole screen). The original auto-hide state is kept in the registry until it is given back,
        // so the taskbar comes back even when the shell process crashed, was killed or the PC restarted.
        const string OrigAutoHideValue = "OrigAutoHide";

        static System.Collections.Generic.IEnumerable<IntPtr> NativeBars()
        {
            var main = W32.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) yield return main;
            IntPtr h = IntPtr.Zero;
            while ((h = W32.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return h;
        }

        internal static void HideNative() { foreach (var h in NativeBars()) if (W32.IsWindowVisible(h)) W32.ShowWindow(h, W32.SW_HIDE); }
        internal static void ShowNative() { foreach (var h in NativeBars()) if (!W32.IsWindowVisible(h)) W32.ShowWindow(h, W32.SW_SHOWNA); }

        /// <summary>True when the Windows taskbar is hidden right now (by a running or crashed shell).</summary>
        public static bool NativeHidden
        {
            get { foreach (var h in NativeBars()) if (!W32.IsWindowVisible(h)) return true; return false; }
        }

        /// <summary>Called by the shell before it hides the taskbar: remembers the user's auto-hide state once.</summary>
        internal static bool TakeTaskbar()
        {
            if (Core.Reg.Get(SettingsKey, OrigAutoHideValue) is int i) return i == 1;
            bool cur = Core.Applier.GetAutoHide();
            using (var k = Core.Reg.Create(SettingsKey)) k.SetValue(OrigAutoHideValue, cur ? 1 : 0, RegistryValueKind.DWord);
            return cur;
        }

        /// <summary>True while WinMorph still owes the user their taskbar (shell running or not cleaned up).</summary>
        public static bool TaskbarTaken => Core.Reg.Get(SettingsKey, OrigAutoHideValue) is int;

        /// <summary>
        /// Gives everything back that only the shell process changes at run time: the Windows taskbar
        /// (visible, original auto-hide), Win+L. Works from any process, also after a crash.
        /// Stops the shell first.
        /// </summary>
        public static void Release()
        {
            Stop();
            GiveBackTaskbar();
        }

        /// <summary>The part of <see cref="Release"/> that does not need the shell to be stopped first.</summary>
        static void GiveBackTaskbar()
        {
            Core.LockGuard.Disarm();
            CursorSkin.Reset();   // the user's own mouse pointers again
            if (Core.Reg.Get(SettingsKey, OrigAutoHideValue) is int orig)
            {
                Core.Applier.SetAutoHideRaw(orig == 1);
                using (var k = Core.Reg.Open(SettingsKey, true)) k?.DeleteValue(OrigAutoHideValue, false);
            }
            ShowNative();
        }

        /// <summary>(Re)starts the shell and waits until it runs. Returns false if it did not come up.</summary>
        public static bool Start()
        {
            Stop(keepTaskbar: true);
            try { Process.Start(new ProcessStartInfo(Exe, "--shell") { UseShellExecute = false }); } catch { return false; }
            for (int i = 0; i < 80 && !IsRunning; i++) Thread.Sleep(100);
            return IsRunning;
        }

        /// <summary>Writes the chosen preset and the autostart entry.</summary>
        public static void Enable(string presetId)
        {
            using (var k = Core.Reg.Create(SettingsKey)) k.SetValue("ShellPreset", presetId, RegistryValueKind.String);
            using (var k = Core.Reg.Create(RunKey)) k.SetValue(RunValue, "\"" + Exe + "\" --shell", RegistryValueKind.String);
            Core.Autostart.Register(Exe);   // starts the shell right at sign-in; the Run entry is the fallback
        }

        public static void Disable()
        {
            Release();
            using (var k = Core.Reg.Open(SettingsKey, true)) k?.DeleteValue("ShellPreset", false);
            using (var k = Core.Reg.Open(RunKey, true)) k?.DeleteValue(RunValue, false);
            Core.Autostart.Remove();
        }

        public static string CurrentPreset => Core.Reg.Get(SettingsKey, "ShellPreset") as string;

        const string LockNowName = "WinMorphLockNow_v1", LockShownName = "WinMorphLockShown_v1";

        /// <summary>True in the design shell process itself.</summary>
        public static bool InShell => host != null;

        /// <summary>
        /// From another process (the screen saver): asks the running shell to show the WinMorph lock.
        /// Returns true once it is on screen.
        /// </summary>
        public static bool RequestLock(int timeoutMs)
        {
            try
            {
                if (!EventWaitHandle.TryOpenExisting(LockNowName, out var now)) return false;
                if (!EventWaitHandle.TryOpenExisting(LockShownName, out var shown)) { now.Dispose(); return false; }
                using (now) using (shown)
                {
                    now.Set();
                    return shown.WaitOne(timeoutMs);
                }
            }
            catch { return false; }
        }

        // ---------- inside the shell process ----------
        static ShellHost host;
        static Mutex mutex;
        static Action emergency;

        /// <summary>Ctrl+Alt+Shift+F12: stop the shell immediately, the normal taskbar comes back.</summary>
        public static void EmergencyStop() => emergency?.Invoke();

        static volatile bool earlyDone;

        /// <summary>
        /// The very first thing the shell process does (before WPF draws anything): it remembers the JIT work of
        /// earlier starts (multi-core JIT makes the next start faster) and keeps the Windows taskbar hidden
        /// until the look's own bar is there. Only when a known look is set.
        /// </summary>
        public static void EarlyStart()
        {
            try
            {
                System.Runtime.ProfileOptimization.SetProfileRoot(Core.Backup.DataDir);
                System.Runtime.ProfileOptimization.StartProfile("shell.jitprofile");
            }
            catch { }
            if (ShellThemes.For(CurrentPreset ?? "") == null) return;
            new Thread(() =>
            {
                var until = DateTime.UtcNow.AddSeconds(30);
                while (!earlyDone && DateTime.UtcNow < until)
                {
                    try { HideNative(); } catch { }
                    Thread.Sleep(40);
                }
            }) { IsBackground = true, Name = "WinMorph early taskbar" }.Start();
        }

        /// <summary>How long the look took to appear (process start → bar on screen), the last 20 starts.</summary>
        static void LogStart()
        {
            try
            {
                var ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                var file = System.IO.Path.Combine(Core.Backup.DataDir, "shell-start.log");
                var lines = System.IO.File.Exists(file) ? System.IO.File.ReadAllLines(file).ToList() : new System.Collections.Generic.List<string>();
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Math.Round(ms) + " ms  uptime " + Math.Round(Environment.TickCount / 1000.0) + " s");
                System.IO.File.WriteAllLines(file, lines.Skip(Math.Max(0, lines.Count - 20)));
            }
            catch { }
        }

        public static void RunShell(Application app)
        {
            mutex = new Mutex(true, MutexName, out bool first);
            if (!first) { app.Shutdown(0); return; }
            using (var k = Core.Reg.Create(SettingsKey)) k.SetValue("ShellPid", Process.GetCurrentProcess().Id, RegistryValueKind.DWord);
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var theme = ShellThemes.For(CurrentPreset ?? "");
            // a look of an older version (or none): nothing to draw, and the taskbar must not stay taken
            if (theme == null) { earlyDone = true; GiveBackTaskbar(); app.Shutdown(0); return; }

            var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitName);
            var switching = new EventWaitHandle(false, EventResetMode.ManualReset, SwitchName);
            switching.Reset();
            var ui = Dispatcher.CurrentDispatcher;
            Action quit = () => { try { host?.Shutdown(keepHidden: switching.WaitOne(0)); } catch { } host = null; app.Shutdown(0); };

            // Self-healing: on an error the shell rebuilds itself; after repeated errors it gives
            // the normal Windows taskbar back instead of leaving the user without one.
            var crashes = new System.Collections.Generic.List<DateTime>();
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log(e.Exception);
                e.Handled = true;
                // an error while the WinMorph lock is shown: the real Windows lock takes over first
                if (CustomLock.Active) { try { Core.LockGuard.LockNow(); } catch { } try { CustomLock.ForceClose(); } catch { } }
                crashes.RemoveAll(c => (DateTime.Now - c).TotalSeconds > 60);
                crashes.Add(DateTime.Now);
                try { host?.Shutdown(); } catch { }
                host = null;
                if (crashes.Count <= 3) { try { host = new ShellHost(theme); return; } catch (Exception ex) { Log(ex); } }
                quit();
            };
            emergency = quit;
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log(e.ExceptionObject as Exception);
                if (CustomLock.Active) { try { Core.LockGuard.LockNow(); } catch { } }
                try { host?.Shutdown(); } catch { }
            };
            app.SessionEnding += (s, e) => { try { host?.Shutdown(keepHidden: true); } catch { } };

            var waiter = new Thread(() => { exit.WaitOne(); ui.BeginInvoke(quit); }) { IsBackground = true };
            waiter.Start();

            // the screen saver (idle timeout) hands over to the WinMorph lock of this process
            var lockNow = new EventWaitHandle(false, EventResetMode.AutoReset, LockNowName);
            var lockShown = new EventWaitHandle(false, EventResetMode.ManualReset, LockShownName);
            var lockWaiter = new Thread(() =>
            {
                while (true)
                {
                    lockNow.WaitOne();
                    ui.BeginInvoke(new Action(() => { if (host != null) { CustomLock.Show(); if (CustomLock.Active) lockShown.Set(); } }));
                }
            }) { IsBackground = true };
            lockWaiter.Start();
            CustomLock.Closed += () => lockShown.Reset();

            host = new ShellHost(theme);
            earlyDone = true;   // the host keeps the taskbar hidden from here on
            LogStart();
            // after a restart the shell brings the look's live desktop along (its own Run entry may come later or be off)
            if (!string.IsNullOrEmpty(Live.LiveWall.Spec) && !Live.LiveWall.IsRunning) Live.LiveWall.Start();
            System.Threading.Tasks.Task.Run(() => { try { Core.Applier.InstallLockSaver(); } catch { } });
            Core.Updater.RunInShell();
        }

        static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Core.Backup.DataDir, "shell-errors.log"), DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine); } catch { }
        }
    }
}

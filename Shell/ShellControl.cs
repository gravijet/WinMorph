using System;
using System.Diagnostics;
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
        const string MutexName = "WinMorphShell_v1", ExitName = "WinMorphShellExit_v1";

        static string Exe => Process.GetCurrentProcess().MainModule.FileName;

        public static bool IsRunning
        {
            get
            {
                try { using (var m = Mutex.OpenExisting(MutexName)) return true; }
                catch { return false; }
            }
        }

        public static void Stop()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ExitName, out var ev)) { ev.Set(); ev.Dispose(); }
            }
            catch { }
            for (int i = 0; i < 60 && IsRunning; i++) Thread.Sleep(100);
        }

        public static void Start()
        {
            Stop();
            try { Process.Start(new ProcessStartInfo(Exe, "--shell") { UseShellExecute = false }); } catch { }
        }

        /// <summary>Writes the chosen preset and the autostart entry.</summary>
        public static void Enable(string presetId)
        {
            using (var k = Core.Reg.Create(SettingsKey)) k.SetValue("ShellPreset", presetId, RegistryValueKind.String);
            using (var k = Core.Reg.Create(RunKey)) k.SetValue(RunValue, "\"" + Exe + "\" --shell", RegistryValueKind.String);
        }

        public static void Disable()
        {
            Stop();
            Core.LockGuard.Disarm();
            using (var k = Core.Reg.Open(SettingsKey, true)) k?.DeleteValue("OrigAutoHide", false);
            using (var k = Core.Reg.Open(SettingsKey, true)) k?.DeleteValue("ShellPreset", false);
            using (var k = Core.Reg.Open(RunKey, true)) k?.DeleteValue(RunValue, false);
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

        public static void RunShell(Application app)
        {
            mutex = new Mutex(true, MutexName, out bool first);
            if (!first) { app.Shutdown(0); return; }
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var theme = ShellThemes.For(CurrentPreset ?? "");
            if (theme == null) { app.Shutdown(0); return; }

            var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitName);
            var ui = Dispatcher.CurrentDispatcher;
            Action quit = () => { try { host?.Shutdown(); } catch { } host = null; app.Shutdown(0); };

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
            System.Threading.Tasks.Task.Run(() => { try { Core.Applier.InstallLockSaver(); } catch { } });
            Core.Updater.RunInShell();
        }

        static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Core.Backup.DataDir, "shell-errors.log"), DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine); } catch { }
        }
    }
}

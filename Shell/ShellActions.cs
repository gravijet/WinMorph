using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace WinMorph.Shell
{
    internal static class ShellActions
    {
        const byte LWIN = 0x5B;
        static bool De => Core.Loc.De;

        public static void Run(string target, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true }); } catch { }
        }

        public static void Folder(Environment.SpecialFolder f) => Run("explorer.exe", "\"" + Environment.GetFolderPath(f) + "\"");
        public static void Downloads() => Run("explorer.exe", "shell:Downloads");
        public static void ThisPC() => Run("explorer.exe", "shell:MyComputerFolder");
        public static void ControlPanel() => Run("control.exe");
        public static void Settings() => Run("ms-settings:");
        public static void RunDialog() => W32.SendKeys(LWIN, (byte)'R');
        public static void Search() => W32.SendKeys(LWIN, (byte)'S');
        public static void QuickSettings() => W32.SendKeys(LWIN, (byte)'A');
        public static void Notifications() => W32.SendKeys(LWIN, (byte)'N');
        public static void ShowDesktop() => W32.SendKeys(LWIN, (byte)'D');
        public static void TaskView() => W32.SendKeys(LWIN, 0x09);
        /// <summary>Set while the hacker look runs: it has its own task manager and file manager.</summary>
        internal static ShellTheme Own;
        public static void TaskManager() { if (Own != null) HackerTasks.Open(Own); else Run("taskmgr.exe"); }
        public static void Files(string path = null) { if (Own != null) HackerFiles.Open(Own, path); else Run("explorer.exe", path == null ? null : "\"" + path + "\""); }
        public static void WinMorphApp() => Run(Process.GetCurrentProcess().MainModule.FileName);
        public static void WebSearch(string q) => Run("https://www.bing.com/search?q=" + Uri.EscapeDataString(q));
        /// <summary>Always ChatGPT: with a question it opens chatgpt.com with the prompt filled in.</summary>
        public static void AskAi(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) { AppCatalog.ChatGpt().Start(); return; }
            Run("https://chatgpt.com/?q=" + Uri.EscapeDataString(q));
        }

        public static void Screenshot() => W32.SendKeys(LWIN, 0x10, (byte)'S');

        public static void Volume(int dir)
        {
            if (Audio.Available) { if (dir == 0) Audio.Muted = !Audio.Muted; else Audio.Change(dir * 10); return; }
            byte vk = dir > 0 ? (byte)0xAF : dir < 0 ? (byte)0xAE : (byte)0xAD;
            for (int i = 0; i < (dir == 0 ? 1 : 5); i++) KeyHook.Tap(vk);
        }

        public static void SetDark(bool dark)
        {
            const string k = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using (var key = Core.Reg.Create(k))
            {
                key.SetValue("AppsUseLightTheme", dark ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            Core.Native.Broadcast("ImmersiveColorSet");
        }

        public static void Restore() => Run(Process.GetCurrentProcess().MainModule.FileName, "--restore-ui");

        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool wakeup);
        [DllImport("secur32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserNameEx(int format, StringBuilder name, ref uint size);

        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        /// <summary>True when the WinMorph lock screen is installed as the password-protected screen saver.</summary>
        public static bool SaverReady()
        {
            try
            {
                var scr = Core.Reg.Get(@"HKCU\Control Panel\Desktop", "SCRNSAVE.EXE") as string;
                var secure = Core.Reg.Get(@"HKCU\Control Panel\Desktop", "ScreenSaverIsSecure") as string;
                // not "secure": WinMorph locks by itself (its own lock, or the real Windows lock as fallback)
                return scr != null && scr.EndsWith("WinMorph.scr", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(scr);
            }
            catch { return false; }
        }

        /// <summary>
        /// Locks the PC. With WinMorph's lock screen installed, its themed lock screen appears
        /// first (it is a secure screen saver: the Windows sign-in follows). If it does not
        /// start for any reason, the normal Windows lock is used, so the PC is always locked.
        /// </summary>
        public static void Lock()
        {
            // inside the design shell: the complete WinMorph lock screen, unlocked with the Windows password or Hello
            if (ShellControl.InShell) { CustomLock.Show(); if (CustomLock.Active) return; RealLock(); return; }
            if (ShellControl.IsRunning && ShellControl.RequestLock(2500)) return;
            if (!SaverReady()) { RealLock(); return; }
            SendMessage(GetDesktopWindow(), 0x0112, (IntPtr)0xF140, IntPtr.Zero);   // WM_SYSCOMMAND, SC_SCREENSAVE
            var check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            check.Tick += (s, e) =>
            {
                check.Stop();
                if (!SaverRunning() && !Core.LockGuard.Locked()) RealLock();
            };
            check.Start();
        }

        /// <summary>The real Windows lock (works while Win+L is redirected to WinMorph).</summary>
        public static void RealLock() => System.Threading.Tasks.Task.Run(() => { if (!Core.LockGuard.LockNow()) LockWorkStation(); });

        static bool SaverRunning()
        {
            bool running = false;
            try { foreach (var p in Process.GetProcessesByName("WinMorph")) { try { if (p.MainModule.FileName.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) running = true; } catch { } p.Dispose(); } } catch { }
            return running;
        }
        public static void Sleep() => SetSuspendState(false, false, false);

        public static void Shutdown() { if (Confirm(De ? "Computer jetzt herunterfahren?" : "Shut down now?")) Run("shutdown.exe", "/s /t 0"); }
        public static void Restart() { if (Confirm(De ? "Computer jetzt neu starten?" : "Restart now?")) Run("shutdown.exe", "/r /t 0"); }
        public static void SignOut() { if (Confirm(De ? "Jetzt abmelden?" : "Sign out now?")) Run("shutdown.exe", "/l"); }

        static bool Confirm(string text) => MessageBox.Show(text, "WinMorph", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        static string user;
        public static string UserName
        {
            get
            {
                if (user != null) return user;
                try
                {
                    var sb = new StringBuilder(256); uint size = 256;
                    if (GetUserNameEx(3, sb, ref size) && sb.Length > 0) return user = sb.ToString();
                }
                catch { }
                return user = Environment.UserName;
            }
        }
    }
}

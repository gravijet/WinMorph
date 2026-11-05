using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace WinMorph.Core
{
    public static class Tools
    {
        static string Pf => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        public static string OpenShellExe => Path.Combine(Pf, "Open-Shell", "StartMenu.exe");
        public static bool OpenShellInstalled => File.Exists(OpenShellExe);
        public static bool ExplorerPatcherInstalled => Directory.Exists(Path.Combine(Pf, "ExplorerPatcher"));

        public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public static int Build => Environment.OSVersion.Version.Build;

        /// <summary>Runs this exe elevated with the given arguments and waits. Returns false if the user said no.</summary>
        public static bool RunElevated(string args)
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
                p?.WaitForExit(60000);
                return p != null && p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static void RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(4000); } catch { }
            }
            bool running = false;
            for (int i = 0; i < 20 && !running; i++)
            {
                Thread.Sleep(250);
                running = Process.GetProcessesByName("explorer").Any();
            }
            if (!running) Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            // wait until the taskbar exists again
            for (int i = 0; i < 60 && Native.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; i++) Thread.Sleep(250);
            Thread.Sleep(800);
        }

        public static bool OpenShellRunning => Process.GetProcessesByName("StartMenu").Any();

        public static void StopOpenShell()
        {
            if (!OpenShellInstalled) return;
            try { Process.Start(OpenShellExe, "-exit")?.WaitForExit(5000); } catch { }
        }

        public static void StartOpenShell()
        {
            if (!OpenShellInstalled) return;
            try { Process.Start(OpenShellExe); } catch { }
        }
    }
}

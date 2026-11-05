using System;
using System.Diagnostics;
using System.IO;

namespace WinMorph.Core
{
    /// <summary>Reconnects active looks after an update and restores Windows before uninstalling.</summary>
    public static class Installer
    {
        const string UninstallKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph";
        static string Exe => Process.GetCurrentProcess().MainModule.FileName;

        /// <summary>Runs from the installed copy: Start menu recovery shortcuts and autostart pointing to the installed application.</summary>
        public static void PostInstall()
        {
            var exe = Exe;
            if (Reg.Get(Shell.ShellControl.SettingsKey, "Language") == null) Loc.SaveLanguage(Loc.De);
            History.EnsureShortcuts();
            // Inno Setup owns the uninstall entry and shortcuts; remove the old custom entry after upgrading.
            var root = Reg.Root(UninstallKey, out var sub);
            root.DeleteSubKeyTree(sub, false);
            // a look that is already active (e.g. from a portable copy) now starts from the installed file
            if (Shell.ShellControl.CurrentPreset != null) { Shell.ShellControl.Enable(Shell.ShellControl.CurrentPreset); Shell.ShellControl.Start(); }
            if (!string.IsNullOrEmpty(Live.LiveWall.Spec))
            {
                using (var k = Reg.Create(Shell.ShellControl.RunKey)) k.SetValue(Live.LiveWall.RunValue, "\"" + exe + "\" --live");
                Live.LiveWall.Stop(); Live.LiveWall.Start();
            }
        }

        /// <summary>Restores Windows before the standard uninstaller removes the application.</summary>
        public static void Cleanup()
        {
            var result = History.RestoreOriginal();
            if (result.Failed != 0 || result.NeedsElevation)
                throw new InvalidOperationException(Loc.De ? "Windows konnte nicht vollständig wiederhergestellt werden." : "Windows could not be fully restored.");
            Shell.ShellControl.Disable();
            Live.LiveWall.Stop();
            using (var k = Reg.Open(Shell.ShellControl.RunKey, true))
            {
                k?.DeleteValue(Shell.ShellControl.RunValue, false);
                k?.DeleteValue(Live.LiveWall.RunValue, false);
            }
            File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinMorph.lnk"));
        }

        public static void Uninstall()
        {
            var uninstaller = Path.Combine(Path.GetDirectoryName(Exe), "unins000.exe");
            if (File.Exists(uninstaller)) Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true });
            else History.Interactive(undo: false);
        }
    }
}

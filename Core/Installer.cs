using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace WinMorph.Core
{
    /// <summary>The parts of installing that WinMorph.exe does itself (the setup only copies the file and calls --post-install).</summary>
    public static class Installer
    {
        const string UninstallKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WinMorph";
        static string Exe => Process.GetCurrentProcess().MainModule.FileName;

        /// <summary>Runs from the installed copy: Start menu entries, "Apps &amp; features" entry, autostart pointing here.</summary>
        public static void PostInstall(bool desktopShortcut)
        {
            var exe = Exe;
            History.EnsureShortcuts();
            if (desktopShortcut)
                try { History.Make(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinMorph.lnk"), exe, "", Loc.De ? "Looks wechseln" : "Change looks"); } catch { }
            using (var k = Reg.Create(UninstallKey))
            {
                k.SetValue("DisplayName", "WinMorph");
                k.SetValue("DisplayVersion", typeof(Installer).Assembly.GetName().Version.ToString(3));
                k.SetValue("Publisher", "WinMorph");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", Path.GetDirectoryName(exe));
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
            // a look that is already active (e.g. from a portable copy) now starts from the installed file
            if (Shell.ShellControl.CurrentPreset != null) { Shell.ShellControl.Enable(Shell.ShellControl.CurrentPreset); Shell.ShellControl.Start(); }
            if (!string.IsNullOrEmpty(Live.LiveWall.Spec))
            {
                using (var k = Reg.Create(Shell.ShellControl.RunKey)) k.SetValue(Live.LiveWall.RunValue, "\"" + exe + "\" --live");
                Live.LiveWall.Stop(); Live.LiveWall.Start();
            }
        }

        /// <summary>Puts Windows back to its original state, removes autostart, shortcuts and the program folder.</summary>
        public static void Uninstall()
        {
            bool de = Loc.De;
            if (MessageBox.Show(de ? "WinMorph deinstallieren? Windows wird dabei auf den Zustand vor WinMorph zurückgesetzt." : "Uninstall WinMorph? Windows is reset to how it was before WinMorph.",
                "WinMorph", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { History.RestoreOriginal(); } catch (Exception ex) { MessageBox.Show(ex.Message, "WinMorph"); }
            Shell.ShellControl.Disable();
            Live.LiveWall.Stop();
            using (var k = Reg.Open(Shell.ShellControl.RunKey, true)) { k?.DeleteValue(Shell.ShellControl.RunValue, false); k?.DeleteValue(Live.LiveWall.RunValue, false); }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinMorph"), true); } catch { }
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinMorph.lnk")); } catch { }
            try { var root = Reg.Root(UninstallKey, out var sub); root.DeleteSubKeyTree(sub, false); } catch { }
            // the running exe cannot delete itself: a hidden cmd removes the folder a moment after we exit
            var dir = Path.GetDirectoryName(Exe);
            if (dir.IndexOf(@"\Programs\WinMorph", StringComparison.OrdinalIgnoreCase) >= 0)
                try { Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + dir + "\"") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
            MessageBox.Show(de ? "WinMorph wurde entfernt." : "WinMorph has been removed.", "WinMorph");
        }
    }
}

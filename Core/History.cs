using System;
using System.IO;
using System.Windows;

namespace WinMorph.Core
{
    /// <summary>Undo the last look, or go all the way back to the original Windows.</summary>
    public static class History
    {
        public static bool CanUndo => Backup.LatestStep() != null;
        public static bool CanRestore => new Backup(Backup.DefaultFile).HasData;

        /// <summary>Returns to the look that was active before the last change.</summary>
        public static RestoreResult UndoLast(bool restartExplorer = true)
        {
            var file = Backup.LatestStep();
            if (file == null) return null;
            var b = new Backup(file);
            var r = Restorer.Run(b, restartExplorer: restartExplorer);
            if (r.NeedsElevation && Tools.RunElevated("--restore-hklm \"" + file + "\"")) r.NeedsElevation = false;
            if (r.Failed != 0 || r.NeedsElevation) return r;
            try
            {
                File.Delete(file);
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + "-*")) File.Delete(f);
            }
            catch { }
            return r;
        }

        /// <summary>Everything back to how it was before WinMorph was used for the first time.</summary>
        public static RestoreResult RestoreOriginal(bool restartExplorer = true)
        {
            var b = new Backup(Backup.DefaultFile);
            if (!b.HasData) { WinMorph.Shell.ShellControl.Disable(); return new RestoreResult(); }
            var r = Restorer.Run(b, restartExplorer: restartExplorer);
            if (r.NeedsElevation && Tools.RunElevated("--restore-hklm \"" + b.File + "\"")) r.NeedsElevation = false;
            WinMorph.Shell.ShellControl.Disable();
            WinMorph.Live.LiveWall.Stop();
            using (var k = Reg.Open(WinMorph.Shell.ShellControl.SettingsKey, true)) k?.DeleteValue("LiveWall", false);
            using (var k = Reg.Open(WinMorph.Shell.ShellControl.RunKey, true)) k?.DeleteValue(WinMorph.Live.LiveWall.RunValue, false);
            if (r.Failed == 0 && !r.NeedsElevation) { b.Retire(); Backup.ClearSteps(); }
            return r;
        }

        /// <summary>Start menu shortcuts, so restoring always works, even without opening WinMorph.</summary>
        public static void EnsureShortcuts()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                if (exe.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) return;
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinMorph");
                Directory.CreateDirectory(dir);
                bool de = Loc.De;
                Make(Path.Combine(dir, "WinMorph.lnk"), exe, "", de ? "Looks wechseln" : "Change looks");
                Make(Path.Combine(dir, de ? "WinMorph – Original wiederherstellen.lnk" : "WinMorph – Restore original.lnk"), exe, "--restore-ui", de ? "Setzt Windows auf den Zustand vor WinMorph zurück" : "Puts Windows back to how it was before WinMorph");
                Make(Path.Combine(dir, de ? "WinMorph – Letzten Look rückgängig.lnk" : "WinMorph – Undo last look.lnk"), exe, "--undo-ui", de ? "Zurück zum vorherigen Look" : "Back to the previous look");
            }
            catch { }
        }

        internal static void Make(string lnk, string target, string args, string desc)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object sc = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var st = sc.GetType();
            st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { args });
            st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { desc });
            st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
        }

        /// <summary>Restore/undo with a confirmation dialog (used by the shortcuts and the shell menus).</summary>
        public static void Interactive(bool undo)
        {
            bool de = Loc.De;
            if (undo && !CanUndo) { MessageBox.Show(de ? "Es gibt keinen früheren Look zum Zurückkehren." : "There is no earlier look to go back to.", "WinMorph"); return; }
            if (!undo && !CanRestore && WinMorph.Shell.ShellControl.CurrentPreset == null) { MessageBox.Show(de ? "Windows ist bereits im Originalzustand." : "Windows is already in its original state.", "WinMorph"); return; }
            var q = undo ? (de ? "Zum vorherigen Look zurückkehren?" : "Go back to the previous look?")
                         : (de ? "Alles auf das Original-Windows zurücksetzen (Zustand vor WinMorph)?" : "Reset everything to the original Windows (before WinMorph)?");
            if (MessageBox.Show(q, "WinMorph", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var r = undo ? UndoLast() : RestoreOriginal();
            MessageBox.Show(de ? "Fertig – " + (r?.Restored ?? 0) + " Werte wiederhergestellt." : "Done – " + (r?.Restored ?? 0) + " values restored.", "WinMorph");
        }
    }
}

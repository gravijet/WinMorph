using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WinMorph.Core
{
    public sealed class RestoreResult
    {
        public int Restored, Failed;
        public bool NeedsElevation;
        public List<string> Problems = new List<string>();
    }

    /// <summary>Puts every recorded setting back to its original value.</summary>
    public static class Restorer
    {
        /// <param name="reset">Before a new look: back to the original without restarting anything (the new look does that).</param>
        public static RestoreResult Run(Backup b, bool machineOnly = false, bool restartExplorer = true, bool reset = false)
        {
            var r = new RestoreResult();
            if (!machineOnly) { WinMorph.Shell.ShellControl.Stop(); WinMorph.Live.LiveWall.Stop(); }
            bool admin = Tools.IsAdmin;
            string autoHide = null;
            var keysToCheck = new List<KeyValuePair<string, string>>();
            // setting a picture switches Windows Spotlight and slideshows off: these values are written again at the end
            var late = new List<string[]>();

            foreach (var e in b.Entries)
            {
                var parts = e.Key.Split('\t');
                try
                {
                    if (parts[0] == "REG")
                    {
                        bool machine = parts[1].StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
                        if (machineOnly && !machine) continue;
                        if (machine && !admin) { r.NeedsElevation = true; continue; }
                        Reg.RestoreSnapshot(parts[1], parts[2], e.Value);
                        if (IsLate(parts[1])) late.Add(new[] { parts[1], parts[2], e.Value });
                        r.Restored++;
                    }
                    else if (parts[0] == "KEY")
                    {
                        keysToCheck.Add(new KeyValuePair<string, string>(parts[1], e.Value));
                    }
                    else if (parts[0] == "VAL" && !machineOnly)
                    {
                        RestoreValue(parts[1], e.Value, ref autoHide);
                        r.Restored++;
                    }
                }
                catch (Exception ex) { r.Failed++; r.Problems.Add(e.Key.Replace('\t', ' ') + ": " + ex.Message); }
            }

            // Keys that did not exist before are removed again (e.g. the classic context menu switch).
            foreach (var k in keysToCheck)
            {
                bool machine = k.Key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
                if (machineOnly && !machine) continue;
                if (machine && !admin) { r.NeedsElevation = true; continue; }
                if (k.Value != "0") continue;
                try
                {
                    var root = Reg.Root(k.Key, out var sub);
                    root.DeleteSubKeyTree(sub, false);
                }
                catch (Exception ex) { r.Failed++; r.Problems.Add(k.Key + ": " + ex.Message); }
            }

            if (machineOnly) return r;

            foreach (var l in late) try { Reg.RestoreSnapshot(l[0], l[1], l[2]); } catch { }

            Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, Native.SPIF_UPDATE);
            try
            {
                if (Reg.Get(@"HKCU\Software\Microsoft\Windows\DWM", "ColorizationColor") is int col && Native.DwmGetColorizationParameters(out var dp) == 0)
                {
                    dp.clrColor = unchecked((uint)col);
                    dp.clrAfterGlow = unchecked((uint)col);
                    Native.DwmSetColorizationParameters(ref dp, 0);
                }
            }
            catch { }

            foreach (var area in new[] { "ImmersiveColorSet", "TraySettings", "WindowsThemeElement", "Policy" }) Native.Broadcast(area);
            Native.Broadcast(null);
            Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

            if (reset) return r;
            if (restartExplorer)
            {
                Tools.RestartExplorer();
                if (Tools.OpenShellRunning) { Tools.StopOpenShell(); Tools.StartOpenShell(); }
            }
            if (autoHide != null) Applier.SetAutoHideRaw(autoHide == "1");
            // undoing one step can bring back a previous look that used the WinMorph shell
            var shellPreset = WinMorph.Shell.ShellControl.CurrentPreset;
            if (shellPreset != null && WinMorph.Shell.ShellThemes.For(shellPreset) != null) WinMorph.Shell.ShellControl.Start();
            if (!string.IsNullOrEmpty(WinMorph.Live.LiveWall.Spec)) WinMorph.Live.LiveWall.Start();
            return r;
        }

        static bool IsLate(string path) =>
            path.Equals(Applier.Cdm, StringComparison.OrdinalIgnoreCase) || path.Equals(Applier.WallpapersKey, StringComparison.OrdinalIgnoreCase) || path.Equals(Applier.SpotlightKey, StringComparison.OrdinalIgnoreCase);

        static void RestoreValue(string name, string value, ref string autoHide)
        {
            if (name.StartsWith("SPI:"))
            {
                Applier.SetSpi(name.Substring(4), int.Parse(value));
                return;
            }
            switch (name)
            {
                case "ANIM":
                    var ai = new Native.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.ANIMATIONINFO)), iMinAnimate = int.Parse(value) };
                    Native.SystemParametersInfo(Native.SPI_SETANIMATION, ai.cbSize, ref ai, Native.SPIF_UPDATE);
                    break;
                case "NCM":
                    var m = Native.FromBytes<Native.NONCLIENTMETRICS>(Convert.FromBase64String(value));
                    m.cbSize = Marshal.SizeOf(typeof(Native.NONCLIENTMETRICS));
                    Native.SystemParametersInfo(Native.SPI_SETNONCLIENTMETRICS, (uint)m.cbSize, ref m, Native.SPIF_UPDATE);
                    break;
                case "ICONFONT":
                    var f = Native.FromBytes<Native.LOGFONT>(Convert.FromBase64String(value));
                    Native.SystemParametersInfo(Native.SPI_SETICONTITLELOGFONT, (uint)Marshal.SizeOf(typeof(Native.LOGFONT)), ref f, Native.SPIF_UPDATE);
                    break;
                case "LOCKIMG":
                    LockImage.Set(value);
                    break;
                case "WALL":
                    Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, value ?? "", Native.SPIF_UPDATE);
                    break;
                case "AUTOHIDE":
                    autoHide = value;
                    break;
            }
        }
    }
}

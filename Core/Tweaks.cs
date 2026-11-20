using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinMorph.Core
{
    public sealed class ApplyOptions
    {
        public string WallpaperFile;   // rendered beforehand on the UI thread
        public string LockFile;        // designed Windows lock screen picture (UI thread too); null = the wallpaper

        /// <summary>Renders the pictures a look needs. Call on the UI thread.</summary>
        public static ApplyOptions For(Preset p)
        {
            var o = new ApplyOptions { WallpaperFile = WallpaperArt.RenderForScreen(p) };
            try { o.LockFile = WinMorph.Shell.LockScreen.RenderForWindows(p); } catch { }
            return o;
        }
    }

    /// <summary>
    /// Applies a look. Only WinMorph's own design: the shell (taskbar, start menu, Alt+Tab, frames),
    /// the desktop picture and the lock screen. Every value is recorded in the backup before it changes.
    /// </summary>
    public sealed class Applier
    {
        const string Desktop = @"HKCU\Control Panel\Desktop";
        internal const string Serialize = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";

        // name, get, set, value passed in uiParam (true) or pvParam (false) – only for restoring backups of older versions
        internal static readonly (string Name, uint Get, uint Set, bool Ui)[] SpiValues =
        {
            ("DragFullWindows", 0x0026, 0x0025, true),
            ("ClientAreaAnimation", 0x1042, 0x1043, false),
            ("MenuAnimation", 0x1002, 0x1003, false),
            ("MenuFade", 0x1012, 0x1013, false),
            ("ComboBoxAnimation", 0x1004, 0x1005, false),
            ("ListBoxSmoothScrolling", 0x1006, 0x1007, false),
            ("TooltipAnimation", 0x1016, 0x1017, false),
            ("SelectionFade", 0x1014, 0x1015, false),
            ("CursorShadow", 0x101A, 0x101B, false),
            ("DropShadow", 0x1024, 0x1025, false),
            ("FontSmoothing", 0x004A, 0x004B, true),
            ("FontSmoothingType", 0x200A, 0x200B, false),
            ("WinArranging", 0x0082, 0x0083, false),
            ("SnapSizing", 0x008E, 0x008F, false),
            ("DockMoving", 0x0090, 0x0091, false),
            ("ActiveWindowTracking", 0x1000, 0x1001, false),
            ("ActiveWndTrkZOrder", 0x100C, 0x100D, false),
            ("ActiveWndTrkTimeout", 0x2002, 0x2003, false),
            ("MenuShowDelay", 0x006A, 0x006B, true),
            ("ScreenSaveActive", 0x0010, 0x0011, true),
        };

        readonly Backup bk;
        readonly bool de;
        public readonly List<string> Problems = new List<string>();
        public int Changed;

        public Applier(Backup backup, bool german) { bk = backup; de = german; }

        string L(string g, string e) => de ? g : e;

        // ---------- primitive writes (always back up first) ----------
        void Write(string path, string name, object value, RegistryValueKind kind)
        {
            try
            {
                bk.Reg(path, name);
                using (var k = Reg.Create(path)) k.SetValue(name, value, kind);
                Changed++;
            }
            catch (Exception ex) { Problems.Add(path.Substring(path.LastIndexOf('\\') + 1) + "\\" + (name == "" ? "(Standard)" : name) + ": " + ex.Message); }
        }

        void Dw(string path, string name, int v) => Write(path, name, v, RegistryValueKind.DWord);
        void Sz(string path, string name, string v) => Write(path, name, v, RegistryValueKind.String);

        internal static bool SetSpi(string name, int value)
        {
            var s = Array.Find(SpiValues, x => x.Name == name);
            return s.Ui
                ? Native.SystemParametersInfo(s.Set, (uint)value, IntPtr.Zero, Native.SPIF_UPDATE)
                : Native.SystemParametersInfo(s.Set, 0, (IntPtr)value, Native.SPIF_UPDATE);
        }

        // ---------- main entry ----------
        /// <param name="step">Reports the stage that starts now: "reset", "desktop", "lock", "shell".</param>
        public void Apply(Preset p, ApplyOptions o, Action<string> step = null)
        {
            // every look starts from the original Windows: nothing of the previous look is left behind.
            // The undo step remembers the previous look first.
            step?.Invoke("reset");
            if (bk.HasData)
            {
                if (bk.Mirror != null) { CaptureCurrent(bk.Mirror, bk); bk.Mirror.Save(); }
                try
                {
                    var r = Restorer.Run(bk, restartExplorer: false, reset: true);
                    Problems.AddRange(r.Problems);
                }
                catch (Exception ex) { Problems.Add("Reset: " + ex.Message); }
            }
            else WinMorph.Shell.ShellControl.Release();

            step?.Invoke("desktop");
            DesktopArea(p, o);

            step?.Invoke("lock");
            LockArea(p, o);

            step?.Invoke("shell");
            bk.Reg(WinMorph.Shell.ShellControl.SettingsKey, "ShellPreset");
            bk.Reg(WinMorph.Shell.ShellControl.RunKey, WinMorph.Shell.ShellControl.RunValue);
            // autostart programs start without the usual delay, so the design shell appears right after sign-in
            Dw(Serialize, "StartupDelayInMSec", 0);
            // remember which look is active (the lock screen and the app read it)
            Sz(WinMorph.Shell.ShellControl.SettingsKey, "LastPreset", p.Id);
            bk.Save();
            WinMorph.Shell.ShellControl.Enable(p.Id);
            if (!WinMorph.Shell.ShellControl.Start())
            {
                // never leave the user without a taskbar
                WinMorph.Shell.ShellControl.Release();
                Problems.Add(L("Design-Shell startet nicht", "Design shell does not start"));
            }
            if (!string.IsNullOrEmpty(WinMorph.Live.LiveWall.Spec)) WinMorph.Live.LiveWall.Start();
            Changed++;
        }

        internal static bool GetAutoHide()
        {
            var d = new Native.APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(Native.APPBARDATA)) };
            return ((long)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d) & 1) != 0;
        }

        internal static void SetAutoHideRaw(bool on)
        {
            var d = new Native.APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(Native.APPBARDATA)),
                hWnd = Native.FindWindow("Shell_TrayWnd", null),
                lParam = (IntPtr)(on ? 1 : 2), // ABS_AUTOHIDE or ABS_ALWAYSONTOP
            };
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
        }

        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ---------- desktop ----------
        void DesktopArea(Preset p, ApplyOptions o)
        {
            if (o.WallpaperFile != null && File.Exists(o.WallpaperFile))
            {
                RecordWallpaper(bk);
                Sz(Desktop, "WallpaperStyle", "10");
                Sz(Desktop, "TileWallpaper", "0");
                if (Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, o.WallpaperFile, Native.SPIF_UPDATE)) Changed++;
                else Problems.Add(L("Hintergrundbild", "Wallpaper"));
            }

            // live desktop of the look (or none) – recorded for undo/restore
            try
            {
                var item = p.Live != null ? WinMorph.Live.WallCatalog.Find(p.Live) : null;
                if (item != null) WinMorph.Live.LiveWall.Apply(bk, item.Spec, null, start: false);
                else if (!string.IsNullOrEmpty(WinMorph.Live.LiveWall.Spec)) WinMorph.Live.LiveWall.Apply(bk, "", null);
                Changed++;
            }
            catch (Exception ex) { Problems.Add("Live: " + ex.Message); }
        }

        void LockArea(Preset p, ApplyOptions o)
        {
            // themed lock screen: WinMorph replaces the screen saver picture only. Whether and when the
            // screen saver starts stays the user's own setting (timeout and "on" are not touched).
            var scr = InstallLockSaver();
            if (scr != null)
            {
                Sz(Desktop, "SCRNSAVE.EXE", scr);
                Sz(Desktop, "ScreenSaverIsSecure", "0");   // WinMorph locks itself: its own lock screen, or the real Windows lock as fallback
            }

            // lock screen picture without Spotlight ads and tips
            Dw(Cdm, "RotatingLockScreenEnabled", 0);
            Dw(Cdm, "RotatingLockScreenOverlayEnabled", 0);
            Dw(Cdm, "SubscribedContent-338387Enabled", 0);
            if ((o.LockFile ?? o.WallpaperFile) != null) SetLockPicture(p, o.LockFile ?? o.WallpaperFile);
        }

        internal const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        internal const string WallpapersKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
        internal const string SpotlightKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings";

        /// <summary>
        /// Remembers the wallpaper exactly: picture, slideshow or Windows Spotlight. Windows overwrites
        /// its own copy (TranscodedWallpaper) and Spotlight pictures change, so the picture is copied.
        /// </summary>
        internal static void RecordWallpaper(Backup bk)
        {
            bk.Reg(Desktop, "WallpaperStyle");
            bk.Reg(Desktop, "TileWallpaper");
            bk.Reg(WallpapersKey, "BackgroundType");
            bk.Reg(SpotlightKey, "EnabledState");
            bk.Reg(@"HKCU\Control Panel\Colors", "Background");
            if (!bk.Has("VAL\tWALL")) bk.ValueOnly("WALL", SaveWallpaper(Path.Combine(Backup.DataDir, "wall-original")));
            if (bk.Mirror != null && !bk.Mirror.Has("VAL\tWALL")) bk.Mirror.ValueOnly("WALL", SaveWallpaper(Path.Combine(Backup.StepsDir, Path.GetFileNameWithoutExtension(bk.Mirror.File) + "-wall")));
        }

        /// <summary>Path of the picture shown now; pictures Windows will replace are copied to target (+ extension) first.</summary>
        static string SaveWallpaper(string target)
        {
            var cur = CurrentWallpaper();
            if (cur.Length == 0) return "";   // plain colour
            bool replaced = cur.IndexOf("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase) >= 0 || cur.IndexOf(@"\DesktopSpotlight\", StringComparison.OrdinalIgnoreCase) >= 0
                         || cur.IndexOf(@"\Themes\", StringComparison.OrdinalIgnoreCase) >= 0 || !File.Exists(cur);
            if (!replaced) return cur;
            var src = File.Exists(cur) ? cur : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            try
            {
                if (!File.Exists(src)) return cur;
                var ext = Path.GetExtension(src);
                var file = target + (string.IsNullOrEmpty(ext) ? ".jpg" : ext);
                File.Copy(src, file, true);
                return file;
            }
            catch { return cur; }
        }

        internal static string CurrentWallpaper()
        {
            var sb = new StringBuilder(1024);
            Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0);
            return sb.ToString();
        }

        /// <summary>Records the current value of every setting the original backup knows (the undo step before a reset).</summary>
        internal static void CaptureCurrent(Backup step, Backup orig)
        {
            foreach (var e in orig.Entries)
            {
                var parts = e.Key.Split('\t');
                try
                {
                    if (parts[0] == "REG") step.Reg(parts[1], parts[2]);
                    else if (parts[0] == "KEY") step.Key(parts[1]);
                    else if (parts[0] != "VAL") continue;
                    else if (parts[1].StartsWith("SPI:")) { var sp = Array.Find(SpiValues, x => x.Name == parts[1].Substring(4)); step.Value(parts[1], Native.GetInt(sp.Get).ToString()); }
                    else switch (parts[1])
                    {
                        case "ANIM":
                            var ai = new Native.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.ANIMATIONINFO)) };
                            Native.SystemParametersInfo(Native.SPI_GETANIMATION, ai.cbSize, ref ai, 0);
                            step.Value("ANIM", ai.iMinAnimate.ToString()); break;
                        case "NCM": step.Value("NCM", Convert.ToBase64String(Native.ToBytes(Native.GetNcm()))); break;
                        case "ICONFONT": step.Value("ICONFONT", Convert.ToBase64String(Native.ToBytes(Native.GetIconFont()))); break;
                        case "AUTOHIDE": step.Value("AUTOHIDE", GetAutoHide() ? "1" : "0"); break;
                        case "WALL": step.ValueOnly("WALL", SaveWallpaper(Path.Combine(Backup.StepsDir, Path.GetFileNameWithoutExtension(step.File) + "-wall"))); break;
                        case "LOCKIMG":
                            var img = Path.Combine(Backup.StepsDir, Path.GetFileNameWithoutExtension(step.File) + "-lock.img");
                            if (LockImage.SaveCurrent(img)) step.ValueOnly("LOCKIMG", img);
                            break;
                    }
                }
                catch { }
            }
        }

        internal static string InstallLockSaver()
        {
            try
            {
                var target = Path.Combine(Backup.DataDir, "WinMorph.scr");
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                bool newer = !File.Exists(target) || File.GetLastWriteTimeUtc(exe) > File.GetLastWriteTimeUtc(target) || new FileInfo(exe).Length != new FileInfo(target).Length;
                if (newer && !string.Equals(exe, target, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(exe, target, true); } catch (IOException) { /* in use: keep the existing copy */ }
                }
                return File.Exists(target) ? target : null;
            }
            catch { return null; }
        }

        void SetLockPicture(Preset p, string picture)
        {
            try
            {
                // keep the user's own picture: once for "restore original", once for this undo step
                if (!bk.Has("VAL\tLOCKIMG"))
                {
                    var orig = Path.Combine(Backup.DataDir, "lock-original.img");
                    if (LockImage.SaveCurrent(orig)) bk.ValueOnly("LOCKIMG", orig);
                }
                if (bk.Mirror != null && !bk.Mirror.Has("VAL\tLOCKIMG"))
                {
                    var stepImg = Path.Combine(Backup.StepsDir, Path.GetFileNameWithoutExtension(bk.Mirror.File) + "-lock.img");
                    if (LockImage.SaveCurrent(stepImg)) bk.Mirror.ValueOnly("LOCKIMG", stepImg);
                }
                var target = Path.Combine(Backup.DataDir, "lock-" + p.Id + ".png");
                File.Copy(picture, target, true);
                if (LockImage.Set(target)) Changed++;
                else Problems.Add(L("Sperrbild", "Lock screen picture"));
            }
            catch (Exception ex) { Problems.Add(L("Sperrbild", "Lock screen picture") + ": " + ex.Message); }
        }

        // ---------- admin: all-users lock screen picture of older versions ----------
        public const string LockKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

        /// <summary>
        /// True when an old WinMorph sign-in picture for all users is still set. It overrides the
        /// per-user lock screen picture, so new looks would not change the lock screen.
        /// </summary>
        public static bool LockScreenOverrideIsOurs()
        {
            var path = Reg.Get(LockKey, "LockScreenImagePath") as string;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinMorph");
            return path != null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Runs in the elevated helper process: removes WinMorph's all-users picture again.</summary>
        public static int ClearLockScreen(string backupFile)
        {
            try
            {
                if (!LockScreenOverrideIsOurs()) return 0;
                var b = new Backup(backupFile);
                b.Key(LockKey);
                foreach (var n in new[] { "LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus" }) b.Reg(LockKey, n);
                b.Save();
                using (var k = Reg.Open(LockKey, true))
                    foreach (var n in new[] { "LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus" }) k?.DeleteValue(n, false);
                return 0;
            }
            catch { return 1; }
        }
    }
}

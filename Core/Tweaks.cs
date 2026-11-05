using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinMorph.Core
{
    public sealed class ApplyOptions
    {
        public Area Areas = Area.All;
        public bool UseOpenShell = true;
        public bool UseExplorerPatcher = true;
        public bool RestartExplorer = true;
        public string WallpaperFile;   // rendered beforehand on the UI thread
        public string LockFile;        // designed Windows lock screen picture (UI thread too); null = the wallpaper

        /// <summary>Renders the pictures a look needs. Call on the UI thread.</summary>
        public static ApplyOptions For(Preset p, Area areas)
        {
            var o = new ApplyOptions { Areas = areas };
            if (areas.HasFlag(Area.Desktop) || areas.HasFlag(Area.Lock)) o.WallpaperFile = WallpaperArt.RenderForScreen(p);
            if (areas.HasFlag(Area.Lock)) try { o.LockFile = WinMorph.Shell.LockScreen.RenderForWindows(p); } catch { }
            return o;
        }
    }

    /// <summary>Applies a preset. Every value is recorded in the backup before it is changed.</summary>
    public sealed class Applier
    {
        const string Adv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string SearchKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
        const string Personalize = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string Dwm = @"HKCU\Software\Microsoft\Windows\DWM";
        const string AccentKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        const string Desktop = @"HKCU\Control Panel\Desktop";
        const string Cursors = @"HKCU\Control Panel\Cursors";
        const string StuckRects = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
        const string DesktopIcons = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
        const string ThisPcGuid = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        const string ClassicMenuKey = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string OpenShellKey = @"HKCU\Software\OpenShell\StartMenu\Settings";
        const string EpKey = @"HKCU\Software\ExplorerPatcher";

        // name, get, set, value passed in uiParam (true) or pvParam (false)
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

        static readonly string[] CursorNames = { "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person" };

        static string[] CursorFiles(CursorKind k)
        {
            switch (k)
            {
                case CursorKind.Aero: return new[] { "aero_arrow.cur", "aero_helpsel.cur", "aero_working.ani", "aero_busy.ani", "", "", "aero_pen.cur", "aero_unavail.cur", "aero_ns.cur", "aero_ew.cur", "aero_nwse.cur", "aero_nesw.cur", "aero_move.cur", "aero_up.cur", "aero_link.cur", "aero_pin.cur", "aero_person.cur" };
                case CursorKind.Black: return Suffix("_r", "");
                case CursorKind.Inverted: return Suffix("_i", "aero_link_i.cur");
                default: return new string[17]; // empty = built-in classic Windows cursors
            }
        }

        static string[] Suffix(string s, string hand) => new[] { "arrow" + s + ".cur", "help" + s + ".cur", "wait" + s + ".cur", "busy" + s + ".cur", "cross" + s + ".cur", "beam" + s + ".cur", "pen" + s + ".cur", "no" + s + ".cur", "size4" + s + ".cur", "size3" + s + ".cur", "size2" + s + ".cur", "size1" + s + ".cur", "move" + s + ".cur", "up" + s + ".cur", hand, "pin" + s + ".cur", "person" + s + ".cur" };

        static string CursorSchemeName(CursorKind k) => k == CursorKind.Aero ? "Windows Aero" : k == CursorKind.Black ? "Windows Black" : k == CursorKind.Inverted ? "Windows Inverted" : "";

        readonly Backup bk;
        readonly bool de;
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> Notes = new List<string>();
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
        void Dw(string path, string name, bool v) => Dw(path, name, v ? 1 : 0);
        void Sz(string path, string name, string v) => Write(path, name, v, RegistryValueKind.String);

        void Spi(string name, int value)
        {
            var s = Array.Find(SpiValues, x => x.Name == name);
            int current = Native.GetInt(s.Get);
            bk.Value("SPI:" + name, current.ToString());
            if (!SetSpi(s.Name, value)) Problems.Add("SPI " + name);
            else Changed++;
        }

        internal static bool SetSpi(string name, int value)
        {
            var s = Array.Find(SpiValues, x => x.Name == name);
            return s.Ui
                ? Native.SystemParametersInfo(s.Set, (uint)value, IntPtr.Zero, Native.SPIF_UPDATE)
                : Native.SystemParametersInfo(s.Set, 0, (IntPtr)value, Native.SPIF_UPDATE);
        }

        // ---------- main entry ----------
        public void Apply(Preset p, ApplyOptions o)
        {
            // every look starts from the original Windows: parts that are switched off stay original and
            // nothing of the previous look is left behind. The undo step remembers the previous look first.
            if (bk.HasData)
            {
                if (bk.Mirror != null) { CaptureCurrent(bk.Mirror, bk); bk.Mirror.Save(); }
                try { Restorer.Run(bk, restartExplorer: false, reset: true); } catch (Exception ex) { Problems.Add("Reset: " + ex.Message); }
            }
            bool wantShell = o.Areas.HasFlag(Area.Shell) && WinMorph.Shell.ShellThemes.For(p.Id) != null;
            if (o.Areas.HasFlag(Area.Shell))
            {
                WinMorph.Shell.ShellControl.Stop();
                bk.Reg(WinMorph.Shell.ShellControl.SettingsKey, "ShellPreset");
                bk.Reg(WinMorph.Shell.ShellControl.RunKey, WinMorph.Shell.ShellControl.RunValue);
                bk.Save();
                // start autostart programs without the usual delay, so the design shell appears right after sign-in
                if (wantShell) Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0);
                if (wantShell) WinMorph.Shell.ShellControl.Enable(p.Id);
                else WinMorph.Shell.ShellControl.Disable();
                Banners(own: wantShell && p.Id == "terminal");
                Changed++;
            }
            if (o.Areas.HasFlag(Area.Taskbar)) Taskbar(p, o);
            if (o.Areas.HasFlag(Area.Colors)) Colors(p);
            if (o.Areas.HasFlag(Area.Windows)) Windows(p);
            if (o.Areas.HasFlag(Area.Fonts)) Fonts(p);
            if (o.Areas.HasFlag(Area.Desktop)) DesktopArea(p, o);
            if (o.Areas.HasFlag(Area.Lock)) LockArea(p, o);
            // remember which look is active (the lock screen and the app read it)
            Sz(WinMorph.Shell.ShellControl.SettingsKey, "LastPreset", p.Id);
            if (o.Areas.HasFlag(Area.Sounds)) Sounds(p);
            if (o.Areas.HasFlag(Area.Cursor)) Cursor(p);
            if (o.Areas.HasFlag(Area.Start)) Start(p, o);

            bk.Save();

            foreach (var area in new[] { "ImmersiveColorSet", "TraySettings", "WindowsThemeElement", "Policy" }) Native.Broadcast(area);
            Native.Broadcast(null);
            Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED → refresh desktop icons

            if (o.RestartExplorer)
            {
                Tools.RestartExplorer();
                if (o.Areas.HasFlag(Area.Start) && o.UseOpenShell && p.OsStyle != null && Tools.OpenShellInstalled)
                {
                    Tools.StopOpenShell();
                    Tools.StartOpenShell();
                }
            }
            else Notes.Add(L("Einige Taskleisten-Änderungen erscheinen erst nach Neuanmeldung oder Explorer-Neustart.", "Some taskbar changes appear after signing in again or restarting Explorer."));

            // auto-hide must be set after Explorer is back
            if (o.Areas.HasFlag(Area.Taskbar)) SetAutoHide(p.AutoHide);
            if (wantShell) WinMorph.Shell.ShellControl.Start();
            if (!string.IsNullOrEmpty(WinMorph.Live.LiveWall.Spec)) WinMorph.Live.LiveWall.Start();
        }

        const string NotifKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        /// <summary>
        /// own = the hacker shell shows notifications itself: Windows keeps them in its notification centre but shows no banner.
        /// Otherwise every app gets its original banner setting back.
        /// shortcut: only apps that have notified before are listed; a brand-new app shows a Windows banner until the look is applied again.
        /// </summary>
        void Banners(bool own)
        {
            string[] apps;
            try { using (var k = Reg.Open(NotifKey)) apps = k?.GetSubKeyNames() ?? new string[0]; } catch { return; }
            foreach (var app in apps)
            {
                var path = NotifKey + "\\" + app;
                var key = "REG\t" + path + "\tShowBanner";
                if (own) { Dw(path, "ShowBanner", 0); continue; }
                if (!bk.Has(key)) continue;
                var orig = bk.Entries.First(e => e.Key == key).Value;
                bk.Reg(path, "ShowBanner");   // the undo step remembers the current value
                try { Reg.RestoreSnapshot(path, "ShowBanner", orig); } catch { }
            }
        }

        void Taskbar(Preset p, ApplyOptions o)
        {
            Dw(Adv, "TaskbarAl", p.Center ? 1 : 0);
            Dw(Adv, "TaskbarGlomLevel", p.Combine);
            Dw(Adv, "MMTaskbarGlomLevel", p.Combine);
            Dw(SearchKey, "SearchboxTaskbarMode", p.Search);
            Dw(Adv, "ShowTaskViewButton", p.TaskView);
            Dw(Adv, "ShowCopilotButton", p.Copilot);
            Dw(Adv, "TaskbarSmallIcons", p.SmallIcons);
            Dw(Adv, "ShowSecondsInSystemClock", p.Seconds);
            Dw(Adv, "TaskbarAnimations", p.Animations);
            // Widgets is protected on some builds; failure is harmless.
            int before = Problems.Count;
            Dw(Adv, "TaskbarDa", p.Widgets);
            if (Problems.Count > before)
            {
                Problems.RemoveAt(Problems.Count - 1);
                Notes.Add(L("Widgets-Knopf wird von Windows geschützt – bitte ggf. in den Einstellungen umschalten.", "The Widgets button is protected by Windows – toggle it in Settings if needed."));
            }

            if (o.UseExplorerPatcher && Tools.ExplorerPatcherInstalled)
            {
                int style = p.EpTaskbar == 1 ? (Tools.Build >= 26100 ? 2 : 1) : 0;
                Dw(EpKey, "OldTaskbar", style);
                SetTaskbarEdge(p.EpTaskbar == 1 ? p.EpEdge : 3);
            }
        }

        void SetTaskbarEdge(int edge)
        {
            try
            {
                if (!(Reg.Get(StuckRects, "Settings") is byte[] data) || data.Length < 13) return;
                if (data[12] == edge) return;
                var copy = (byte[])data.Clone();
                copy[12] = (byte)edge;
                Write(StuckRects, "Settings", copy, RegistryValueKind.Binary);
            }
            catch (Exception ex) { Problems.Add("StuckRects3: " + ex.Message); }
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

        void SetAutoHide(bool on)
        {
            bk.Value("AUTOHIDE", GetAutoHide() ? "1" : "0");
            bk.Save();
            SetAutoHideRaw(on);
            Changed++;
        }

        // ---------- colours ----------
        static uint Abgr(Color c) => 0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R;
        static uint Argb(Color c, byte a) => ((uint)a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        void Colors(Preset p)
        {
            var c = p.Id == "terminal" ? WinMorph.Shell.ShellThemes.HackerColor : p.Accent;
            Dw(Personalize, "SystemUsesLightTheme", p.DarkSystem ? 0 : 1);
            Dw(Personalize, "AppsUseLightTheme", p.DarkApps ? 0 : 1);
            Dw(Personalize, "EnableTransparency", p.Transparency);
            Dw(Personalize, "ColorPrevalence", p.AccentTaskbar && p.DarkSystem);
            Dw(Desktop, "AutoColorization", 0);

            var shades = new[]
            {
                Mix(c, Colors_.White, .60), Mix(c, Colors_.White, .40), Mix(c, Colors_.White, .20), c,
                Mix(c, Colors_.Black, .22), Mix(c, Colors_.Black, .42), Mix(c, Colors_.Black, .62), Color.FromRgb(0x88, 0x88, 0x88),
            };
            var palette = new byte[32];
            for (int i = 0; i < 8; i++) { palette[i * 4] = shades[i].R; palette[i * 4 + 1] = shades[i].G; palette[i * 4 + 2] = shades[i].B; palette[i * 4 + 3] = 0; }
            Write(AccentKey, "AccentPalette", palette, RegistryValueKind.Binary);
            Dw(AccentKey, "AccentColorMenu", unchecked((int)Abgr(c)));
            Dw(AccentKey, "StartColorMenu", unchecked((int)Abgr(shades[5])));

            Dw(Dwm, "AccentColor", unchecked((int)Abgr(c)));
            Dw(Dwm, "ColorizationColor", unchecked((int)Argb(c, 0xC4)));
            Dw(Dwm, "ColorizationAfterglow", unchecked((int)Argb(c, 0xC4)));
            Dw(Dwm, "ColorPrevalence", p.AccentTitle);
            Dw(Dwm, "EnableWindowColorization", p.AccentTitle);

            try
            {
                if (Native.DwmGetColorizationParameters(out var dp) == 0)
                {
                    dp.clrColor = Argb(c, 0xC4);
                    dp.clrAfterGlow = Argb(c, 0xC4);
                    Native.DwmSetColorizationParameters(ref dp, 0);
                }
            }
            catch { /* not available on this build – registry values still apply */ }
        }

        // ---------- windows & motion ----------
        void Windows(Preset p)
        {
            var ai = new Native.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.ANIMATIONINFO)) };
            Native.SystemParametersInfo(Native.SPI_GETANIMATION, ai.cbSize, ref ai, 0);
            bk.Value("ANIM", ai.iMinAnimate.ToString());
            ai.iMinAnimate = p.Animations ? 1 : 0;
            if (Native.SystemParametersInfo(Native.SPI_SETANIMATION, ai.cbSize, ref ai, Native.SPIF_UPDATE)) Changed++;

            int anim = p.Animations ? 1 : 0, menu = p.MenuAnim ? 1 : 0;
            Spi("ClientAreaAnimation", anim);
            Spi("ComboBoxAnimation", anim);
            Spi("ListBoxSmoothScrolling", anim);
            Spi("TooltipAnimation", menu);
            Spi("SelectionFade", menu);
            Spi("MenuAnimation", menu);
            Spi("MenuFade", menu);
            Spi("DragFullWindows", p.FullDrag ? 1 : 0);
            Spi("DropShadow", p.Shadows ? 1 : 0);
            Spi("CursorShadow", p.Shadows ? 1 : 0);
            Spi("FontSmoothing", p.ClearType ? 1 : 0);
            if (p.ClearType) Spi("FontSmoothingType", 2);
            Spi("WinArranging", p.Snap ? 1 : 0);
            Spi("SnapSizing", p.Snap ? 1 : 0);
            Spi("DockMoving", p.Snap ? 1 : 0);
            Spi("ActiveWindowTracking", p.FocusFollows ? 1 : 0);
            Spi("ActiveWndTrkZOrder", 0);
            if (p.FocusFollows) Spi("ActiveWndTrkTimeout", 150);
            Spi("MenuShowDelay", p.MenuDelay);

            Dw(Adv, "DisallowShaking", !p.Shake);
            Dw(Adv, "EnableSnapAssistFlyout", p.Snap);
            Dw(Adv, "UseCompactMode", p.Compact);
            Dw(Adv, "LaunchTo", p.LaunchThisPC ? 1 : 2);
            Dw(Adv, "ListviewShadow", p.Shadows);

            // Windows 10 style right-click menu (HKCU only, removed again on restore)
            bk.Key(ClassicMenuKey);
            try
            {
                if (p.ClassicContextMenu)
                {
                    using (var k = Reg.Create(ClassicMenuKey + @"\InprocServer32")) k.SetValue("", "", RegistryValueKind.String);
                }
                else
                {
                    var root = Reg.Root(ClassicMenuKey, out var sub);
                    root.DeleteSubKeyTree(sub, false);
                }
                Changed++;
            }
            catch (Exception ex) { Problems.Add("ContextMenu: " + ex.Message); }
        }

        // ---------- fonts ----------
        void Fonts(Preset p)
        {
            try
            {
                var m = Native.GetNcm();
                bk.Value("NCM", Convert.ToBase64String(Native.ToBytes(m)));
                m.lfCaptionFont.lfFaceName = p.Font;
                m.lfCaptionFont.lfWeight = p.CaptionBold ? 700 : 400;
                m.lfSmCaptionFont.lfFaceName = p.Font;
                m.lfSmCaptionFont.lfWeight = p.CaptionBold ? 700 : 400;
                m.lfMenuFont.lfFaceName = p.Font;
                m.lfStatusFont.lfFaceName = p.Font;
                m.lfMessageFont.lfFaceName = p.Font;
                if (Native.SystemParametersInfo(Native.SPI_SETNONCLIENTMETRICS, (uint)m.cbSize, ref m, Native.SPIF_UPDATE)) Changed++;
                else Problems.Add("NonClientMetrics");

                var f = Native.GetIconFont();
                bk.Value("ICONFONT", Convert.ToBase64String(Native.ToBytes(f)));
                f.lfFaceName = p.Font;
                if (Native.SystemParametersInfo(Native.SPI_SETICONTITLELOGFONT, (uint)Marshal.SizeOf(typeof(Native.LOGFONT)), ref f, Native.SPIF_UPDATE)) Changed++;
            }
            catch (Exception ex) { Problems.Add("Fonts: " + ex.Message); }
        }

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

            Dw(Adv, "HideIcons", p.HideDesktopIcons);
            Dw(DesktopIcons, ThisPcGuid, p.ShowThisPC ? 0 : 1);

            // live wallpaper of the look (or none) – recorded for undo/restore
            try
            {
                var item = p.Live != null ? WinMorph.Live.WallCatalog.Find(p.Live) : null;
                if (item != null && (!item.IsVideo || item.Downloaded)) WinMorph.Live.LiveWall.Apply(bk, item.Spec, null, start: false);
                else if (!string.IsNullOrEmpty(WinMorph.Live.LiveWall.Spec)) WinMorph.Live.LiveWall.Apply(bk, "", null);
                Changed++;
            }
            catch (Exception ex) { Problems.Add("Live: " + ex.Message); }
        }

        void LockArea(Preset p, ApplyOptions o)
        {
            // themed lock screen: WinMorph runs as a password-protected screen saver,
            // so the secure Windows sign-in still appears when the user returns
            var scr = InstallLockSaver();
            if (scr != null)
            {
                Sz(Desktop, "SCRNSAVE.EXE", scr);
                Sz(Desktop, "ScreenSaverIsSecure", "0");   // WinMorph locks itself: its own lock screen, or the real Windows lock as fallback
                Sz(Desktop, "ScreenSaveTimeOut", "600");
                Spi("ScreenSaveActive", 1);
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
            var sb = new StringBuilder(1024);
            Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0);
            var cur = sb.ToString();
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

        void SetLockPicture(Preset p, string wallpaper)
        {
            try
            {
                // keep the user's own picture: once for "restore original", once for this undo step
                if (!bk.Has("VAL	LOCKIMG"))
                {
                    var orig = Path.Combine(Backup.DataDir, "lock-original.img");
                    if (LockImage.SaveCurrent(orig)) bk.ValueOnly("LOCKIMG", orig);
                }
                if (bk.Mirror != null)
                {
                    var stepImg = Path.Combine(Backup.StepsDir, Path.GetFileNameWithoutExtension(bk.Mirror.File) + "-lock.img");
                    if (LockImage.SaveCurrent(stepImg)) bk.Mirror.ValueOnly("LOCKIMG", stepImg);
                }
                var target = Path.Combine(Backup.DataDir, "lock-" + p.Id + ".png");
                File.Copy(wallpaper, target, true);
                if (LockImage.Set(target)) Changed++;
                else Problems.Add(L("Sperrbild", "Lock screen picture"));
            }
            catch (Exception ex) { Problems.Add(L("Sperrbild", "Lock screen picture") + ": " + ex.Message); }
        }

        // ---------- sounds ----------
        void Sounds(Preset p)
        {
            const string schemes = @"HKCU\AppEvents\Schemes";
            string target = p.Silent ? ".None" : ".Default";
            Sz(schemes, "", target);
            try
            {
                using (var apps = Reg.Open(schemes + @"\Apps"))
                {
                    if (apps == null) return;
                    foreach (var app in apps.GetSubKeyNames())
                        using (var appKey = apps.OpenSubKey(app))
                            foreach (var ev in appKey.GetSubKeyNames())
                            {
                                string evPath = schemes + @"\Apps\" + app + @"\" + ev;
                                if (!Reg.KeyExists(evPath + @"\.Current")) continue;
                                string value = "";
                                if (!p.Silent) value = Reg.Get(evPath + @"\.Default", "") as string ?? "";
                                bk.Reg(evPath + @"\.Current", "");
                                using (var k = Reg.Create(evPath + @"\.Current")) k.SetValue("", value, RegistryValueKind.ExpandString);
                            }
                }
                Changed++;
            }
            catch (Exception ex) { Problems.Add("Sounds: " + ex.Message); }
        }

        // ---------- cursor ----------
        void Cursor(Preset p)
        {
            var files = CursorFiles(p.Cursor);
            for (int i = 0; i < CursorNames.Length; i++)
            {
                var f = files[i];
                Write(Cursors, CursorNames[i], string.IsNullOrEmpty(f) ? "" : @"%SystemRoot%\cursors\" + f, RegistryValueKind.ExpandString);
            }
            Sz(Cursors, "", CursorSchemeName(p.Cursor));
            Dw(Cursors, "Scheme Source", p.Cursor == CursorKind.Classic ? 0 : 2);
            Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, Native.SPIF_UPDATE);
        }

        // ---------- start menu ----------
        void Start(Preset p, ApplyOptions o)
        {
            Dw(Adv, "Start_Layout", p.StartLayout);
            Dw(Adv, "Start_IrisRecommendations", p.Recommendations);
            Dw(Adv, "Start_TrackDocs", p.Recommendations);

            if (o.UseExplorerPatcher && Tools.ExplorerPatcherInstalled)
                Dw(Adv, "Start_ShowClassicMode", p.EpClassicStart);

            if (o.UseOpenShell && Tools.OpenShellInstalled)
            {
                if (p.OsStyle != null)
                {
                    Sz(OpenShellKey, "MenuStyle", p.OsStyle);
                    string skinValue = p.OsStyle == "Classic1" ? "SkinC1" : p.OsStyle == "Classic2" ? "SkinC2" : "SkinW7";
                    Sz(OpenShellKey, skinValue, p.OsSkin);
                    Dw(OpenShellKey, "EnableStartButton", 1);
                    Sz(OpenShellKey, "StartButtonType", p.OsButton ?? "AeroButton");
                    if (!o.RestartExplorer) { Tools.StopOpenShell(); Tools.StartOpenShell(); }
                }
                else if (Tools.OpenShellRunning)
                {
                    Tools.StopOpenShell();
                    Notes.Add(L("Open-Shell wurde beendet, damit das Windows-Startmenü erscheint.", "Open-Shell was closed so the Windows Start menu shows."));
                }
            }
            else if (p.OsStyle != null && o.UseOpenShell)
            {
                Notes.Add(L("Für das klassische Windows-Startmenü ist Open-Shell nötig (nicht installiert).", "The classic Windows Start menu needs Open-Shell (not installed)."));
            }
        }

        // ---------- admin: lock screen ----------
        public const string LockKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

        /// <summary>Runs in the elevated helper process.</summary>
        public static int ApplyLockScreen(string png, string backupFile)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinMorph");
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, "lockscreen-" + DateTime.Now.Ticks + ".png");
                File.Copy(png, target, true);
                var b = new Backup(backupFile);
                b.Key(LockKey);
                foreach (var n in new[] { "LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus" }) b.Reg(LockKey, n);
                b.Save();
                using (var k = Reg.Create(LockKey))
                {
                    k.SetValue("LockScreenImagePath", target, RegistryValueKind.String);
                    k.SetValue("LockScreenImageUrl", target, RegistryValueKind.String);
                    k.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
                }
                return 0;
            }
            catch { return 1; }
        }

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

    // System.Windows.Media.Colors clashes with the method name above.
    internal static class Colors_
    {
        public static readonly Color White = Color.FromRgb(255, 255, 255);
        public static readonly Color Black = Color.FromRgb(0, 0, 0);
    }
}

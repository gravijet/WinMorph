using System.Windows;
using System.Windows.Media;

namespace WinMorph.Shell
{
    public static class ShellThemes
    {
        static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        static Brush S(uint rgb, byte a = 255) { var b = new SolidColorBrush(C(rgb, a)); b.Freeze(); return b; }
        static Brush V(params (double o, uint c, byte a)[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var s in stops) b.GradientStops.Add(new GradientStop(C(s.c, s.a), s.o));
            b.Freeze();
            return b;
        }
        static FrameColors F(uint cap, uint capIn, uint text, uint textIn, uint border, bool square = false, uint? borderIn = null)
            => new FrameColors { Caption = C(cap), CaptionInactive = C(capIn), Text = C(text), TextInactive = C(textIn), Border = C(border), BorderInactive = C(borderIn ?? capIn), Square = square };

        /// <summary>The shell theme of a look; null for unknown ids (looks of older versions).</summary>
        public static ShellTheme For(string id)
        {
            bool de = Core.Loc.De;
            switch (id)
            {
                case "win7": return new ShellTheme
                {
                    Id = id, Name = "Windows 7", Btn = BtnStyle.Glass, Start = StartKind.Orb, Menu = MenuKind.Seven, Lock = LockKind.Seven, Switch = SwitchKind.Aero,
                    BarHeight = 42, BarBg = V((0, 0x5E83AD, 150), (0.5, 0x2C4D72, 170), (1, 0x1A3150, 200)), Blur = true,
                    BarLine = C(0xFFFFFF, 90), Labels = false, Accent = C(0x2B6CB3), Font = "Segoe UI", FontSize = 12,
                    MenuBg = C(0xFFFFFF), MenuFg = C(0x1E1E1E), MenuSub = C(0x6D6D6D), MenuHover = C(0xD7E9FC), MenuSide = C(0x23395A, 235), MenuSideFg = C(0xFFFFFF), MenuRadius = 6,
                    SearchHint = de ? "Programme/Dateien durchsuchen" : "Search programs and files",
                    Frame = F(0x8BA9CF, 0xBCCDE3, 0x000000, 0x5A5A5A, 0x5E7FA8),
                };
                case "terminal":
                {
                    // the hacker colour can be chosen in the app (green by default)
                    var g = HackerColor; var d = Core.Applier.Mix(g, Colors.Black, 0.45);
                    return new ShellTheme
                    {
                        Id = id, Name = "Hacker", Layout = BarLayout.TerminalTop, Btn = BtnStyle.Text, Start = StartKind.Terminal, Menu = MenuKind.Palette, Lock = LockKind.Terminal, Switch = SwitchKind.Table,
                        BarHeight = 24, BarBg = S(0x000000), BarFg = g, BarFgDim = d, BtnFg = g, BtnActive = g, BtnHover = Color.FromArgb(40, g.R, g.G, g.B),
                        Labels = true, Accent = g, Font = "Consolas", FontSize = 12.5, ShowDate = true,
                        MenuBg = C(0x000000, 248), MenuFg = g, MenuSub = d, MenuHover = Color.FromArgb(45, g.R, g.G, g.B),
                        SearchHint = "$ run _",
                        // dark, neutral frames: no coloured border around every window
                        Frame = new FrameColors { Caption = C(0x0A0A0A), CaptionInactive = C(0x0A0A0A), Text = g, TextInactive = d, Border = C(0x2A2A2A), BorderInactive = C(0x1A1A1A), Square = true },
                    };
                }
                case "zen": return new ShellTheme
                {
                    // "Morgenteich": frosted glass over a koi pond, ink text, pond green and koi orange
                    Id = id, Name = "Zen", Layout = BarLayout.Floating, Btn = BtnStyle.Pill, Start = StartKind.Zen, Menu = MenuKind.Zen, Lock = LockKind.Zen, Switch = SwitchKind.Shoji,
                    BarHeight = 50, BarBg = S(0xF4F8F7, 150), BarFg = C(0x1C2A2D), BarFgDim = C(0x4F6064), BtnFg = C(0x1C2A2D), BtnHover = C(0xFFFFFF, 120), BtnActive = C(0xFFFFFF, 190),
                    Accent = C(0x0E7C78), Font = "Yu Gothic UI", FontSize = 13.5, Radius = 25, DockIcon = 30,
                    MenuBg = C(0xF4F8F7, 235), MenuFg = C(0x1C2A2D), MenuSub = C(0x4F6064), MenuHover = C(0x0E7C78, 38), MenuRadius = 24, SearchHint = de ? "Apps, Dateien, Befehle" : "Apps, files, commands",
                    Frame = F(0xE6F0EE, 0xF2F7F6, 0x1C2A2D, 0x7A8A8D, 0x0E7C78, borderIn: 0xB9CFCB),
                };
                case "macos": return new ShellTheme
                {
                    // macOS Tahoe, light: a transparent menu bar on top, the glass Dock below, system blue
                    Id = id, Name = "macOS", Layout = BarLayout.Mac, Btn = BtnStyle.Pill, Start = StartKind.Mac, Menu = MenuKind.Launchpad, Lock = LockKind.Mac, Switch = SwitchKind.Mac, MenuFullScreen = true,
                    BarHeight = 24, BarBg = S(0xFFFFFF, 1), BarFg = C(0x1D1D1F), BarFgDim = C(0x6E6E73), BtnFg = C(0x1D1D1F), BtnHover = C(0xFFFFFF, 90), BtnActive = C(0xFFFFFF, 120),
                    Accent = C(0x007AFF), Font = "Segoe UI Variable Text, Segoe UI", FontSize = 13, Radius = 22, DockIcon = 54,
                    MenuBg = C(0xF6F6F6, 235), MenuFg = C(0x1D1D1F), MenuSub = C(0x6E6E73), MenuHover = C(0x007AFF), MenuRadius = 11, SearchHint = de ? "Suche" : "Search",
                    Frame = F(0xF6F6F6, 0xFBFBFB, 0x1D1D1F, 0x9A9A9E, 0xC9C9CC, borderIn: 0xDCDCDE),
                };
                default: return null;
            }
        }

        public static readonly (string De, string En, uint Rgb)[] HackerColors =
        {
            ("Grün", "Green", 0x00FF41), ("Cyan", "Cyan", 0x00E5FF), ("Bernstein", "Amber", 0xFFB000),
            ("Rot", "Red", 0xFF3B3B), ("Violett", "Violet", 0xB57BFF), ("Weiß", "White", 0xE8E8E8),
        };

        /// <summary>Colour of the hacker look (HKCU\Software\WinMorph\HackerColor = "#RRGGBB").</summary>
        public static Color HackerColor
        {
            get
            {
                var s = Core.Reg.Get(ShellControl.SettingsKey, "HackerColor") as string;
                if (s != null && s.Length == 7 && s[0] == '#' && uint.TryParse(s.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out var v)) return C(v);
                return C(0x00FF41);
            }
            set { using (var k = Core.Reg.Create(ShellControl.SettingsKey)) k.SetValue("HackerColor", "#" + value.R.ToString("X2") + value.G.ToString("X2") + value.B.ToString("X2")); }
        }

        /// <summary>Lock screen theme for any id; looks of older versions fall back to Windows 7.</summary>
        public static ShellTheme ForLock(string id) => For(id ?? "") ?? For("win7");
    }
}

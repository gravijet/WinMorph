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
                    Id = id, Name = "Zen", Layout = BarLayout.Floating, Btn = BtnStyle.Pill, Start = StartKind.Zen, Menu = MenuKind.Zen, Lock = LockKind.Zen, Switch = SwitchKind.Shoji,
                    BarHeight = 40, BarBg = S(0xF7F2E9, 225), BarFg = C(0x6B5B45), BarFgDim = C(0xA89A85), BtnFg = C(0x6B5B45), BtnHover = C(0x6B5B45, 25), BtnActive = C(0x6B5B45, 45),
                    Accent = C(0xA0805A), Font = "Segoe UI Light", FontSize = 13, Radius = 20, DockIcon = 24,
                    MenuBg = C(0xFBF8F2, 245), MenuFg = C(0x4A3F30), MenuSub = C(0x9C8E79), MenuHover = C(0xA0805A, 40), MenuRadius = 20, SearchHint = de ? "Atme ein. Tippe." : "Breathe. Type.",
                    Frame = F(0xF3EDE2, 0xF8F4EC, 0x6B5B45, 0xB3A691, 0xD8C6AA),
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

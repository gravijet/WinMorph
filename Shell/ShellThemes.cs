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

        /// <summary>Returns null for presets that keep the original Windows taskbar.</summary>
        public static ShellTheme For(string id)
        {
            if (id == "macos-dark") id = "macos";       // merged into macOS (light/dark switch)
            else if (id == "synapse") id = "nexus";     // removed, the closest look takes over
            var t = Build(id);
            if (t == null) return null;
            switch (id)
            {
                case "win95": t.Lock = LockKind.Classic; t.Switch = SwitchKind.Classic; break;
                case "xp": t.Lock = LockKind.Xp; t.Switch = SwitchKind.Classic; break;
                case "mint": t.Lock = LockKind.Gnome; t.Switch = SwitchKind.Classic; break;
                case "win7": t.Lock = LockKind.Seven; break;
                case "macos": t.Lock = LockKind.Mac; t.Switch = SwitchKind.Mac; break;
                case "ubuntu": case "debian": case "arch": t.Lock = LockKind.Gnome; break;
                case "terminal": t.Lock = LockKind.Terminal; t.Switch = SwitchKind.Text; break;
                case "nexus": t.Lock = LockKind.Ai; break;
                case "zen": t.Lock = LockKind.Zen; t.Switch = SwitchKind.Mac; break;
                case "gaming": t.Lock = LockKind.Gaming; break;
            }
            return t;
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

        /// <summary>Lock screen theme for any preset, including the ones without a shell.</summary>
        public static ShellTheme ForLock(string id)
            => For(id) ?? new ShellTheme { Id = id ?? "win11", Name = "Windows", Lock = LockKind.Modern, Accent = C(0x0078D4), Font = "Segoe UI Variable Display", MenuFg = C(0xFFFFFF) };

        static ShellTheme Build(string id)
        {
            bool de = Core.Loc.De;
            switch (id)
            {
                case "win95": return new ShellTheme
                {
                    Id = id, Name = "Windows 95", Btn = BtnStyle.Bevel, Start = StartKind.Classic, Menu = MenuKind.Classic,
                    BarHeight = 30, BarBg = S(0xC0C0C0), BarFg = C(0x000000), BarFgDim = C(0x404040), BtnFg = C(0x000000),
                    Labels = true, QuickLaunch = true, Font = "Microsoft Sans Serif", FontSize = 11.5, Accent = C(0x000080),
                    MenuBg = C(0xC0C0C0), MenuFg = C(0x000000), MenuSub = C(0x404040), MenuHover = C(0x000080), MenuHeader = C(0x808080), MenuHeaderFg = C(0xC0C0C0),
                    Frame = F(0x000080, 0x808080, 0xFFFFFF, 0xC0C0C0, 0xC0C0C0, square: true),
                };
                case "xp": return new ShellTheme
                {
                    Id = id, Name = "Windows XP", Btn = BtnStyle.Luna, Start = StartKind.Xp, Menu = MenuKind.Xp,
                    BarHeight = 34, BarBg = V((0, 0x3D95FF, 255), (0.06, 0x2B7DF5, 255), (0.5, 0x245DDB, 255), (0.92, 0x1F4FC7, 255), (1, 0x1941A5, 255)),
                    BarFg = C(0xFFFFFF), BtnBg = C(0x3C81F3), BtnHover = C(0x5A9AF8), BtnActive = C(0x1E52B7), BtnFg = C(0xFFFFFF),
                    Labels = true, QuickLaunch = true, Font = "Tahoma", FontSize = 11.5, Accent = C(0x2E9B2E),
                    MenuBg = C(0xFFFFFF), MenuFg = C(0x000000), MenuSub = C(0x5A5A5A), MenuHover = C(0x316AC5), MenuSide = C(0xD3E5FA), MenuSideFg = C(0x00136B),
                    MenuHeader = C(0x1C5CD6), MenuHeaderFg = C(0xFFFFFF), MenuRadius = 6,
                    Frame = F(0x0A55E0, 0x7A96DF, 0xFFFFFF, 0xD8E4F8, 0x0831D9),
                };
                case "win7": return new ShellTheme
                {
                    Id = id, Name = "Windows 7", Btn = BtnStyle.Glass, Start = StartKind.Orb, Menu = MenuKind.Seven,
                    BarHeight = 42, BarBg = V((0, 0x5E83AD, 150), (0.5, 0x2C4D72, 170), (1, 0x1A3150, 200)), Blur = true,
                    BarLine = C(0xFFFFFF, 90), Labels = false, Accent = C(0x2B6CB3), Font = "Segoe UI", FontSize = 12,
                    MenuBg = C(0xFFFFFF), MenuFg = C(0x1E1E1E), MenuSub = C(0x6D6D6D), MenuHover = C(0xD7E9FC), MenuSide = C(0x23395A, 235), MenuSideFg = C(0xFFFFFF), MenuRadius = 6,
                    SearchHint = de ? "Programme/Dateien durchsuchen" : "Search programs and files",
                    Frame = F(0x8BA9CF, 0xBCCDE3, 0x000000, 0x5A5A5A, 0x5E7FA8),
                };
                case "win10": return new ShellTheme
                {
                    Id = id, Name = "Windows 10", Btn = BtnStyle.Flat, Start = StartKind.Logo, Menu = MenuKind.Ten,
                    BarHeight = 40, BarBg = S(0x101010, 235), Blur = true, Accent = C(0x0078D7), SearchBox = true, ShowDate = true,
                    MenuBg = C(0x1F1F1F, 245), MenuFg = C(0xFFFFFF), MenuSub = C(0xAAAAAA), MenuHover = C(0xFFFFFF, 30),
                    SearchHint = de ? "Zur Suche Text hier eingeben" : "Type here to search",
                    Frame = F(0xFFFFFF, 0xFFFFFF, 0x000000, 0x9A9A9A, 0x0078D7, square: true, borderIn: 0xAAAAAA),
                };
                case "macos":
                {
                    bool dark = Core.Presets.MacDark;
                    return new ShellTheme
                    {
                        Id = id, Name = "macOS", Layout = BarLayout.MacTopDock, Btn = BtnStyle.Pill, Start = StartKind.Apple, Menu = MenuKind.Grid, MenuFullScreen = true,
                        BarHeight = 26, BarBg = S(dark ? 0x1E1E1Eu : 0xF4F4F6u, dark ? (byte)200 : (byte)205), Blur = true,
                        BarFg = C(dark ? 0xF2F2F2u : 0x1D1D1Fu), BarFgDim = C(dark ? 0xBBBBBBu : 0x55555Au), BtnFg = C(dark ? 0xF2F2F2u : 0x1D1D1Fu),
                        BtnHover = C(dark ? 0xFFFFFFu : 0x000000u, 28), Accent = C(dark ? 0x0A84FFu : 0x007AFFu), Font = "Segoe UI Variable Display", FontSize = 13,
                        Radius = 18, DockIcon = 50,
                        MenuBg = C(dark ? 0x101014u : 0x2A2440u, 190), MenuFg = C(0xFFFFFF), MenuSub = C(0xDDDDDD), MenuHover = C(0xFFFFFF, 40),
                        SearchHint = de ? "Suchen" : "Search",
                        Frame = dark ? F(0x2D2D30, 0x262628, 0xE8E8E8, 0x8A8A8A, 0x3A3A3C) : F(0xECECEC, 0xF6F6F6, 0x262626, 0xA0A0A0, 0xC9C9C9),
                    };
                }
                case "ubuntu": return new ShellTheme
                {
                    Id = id, Name = "Ubuntu", Layout = BarLayout.GnomeTopLeftDock, Btn = BtnStyle.Pill, Start = StartKind.Activities, Menu = MenuKind.Grid, MenuFullScreen = true,
                    BarHeight = 30, BarBg = S(0x000000, 235), BarFg = C(0xFFFFFF), Accent = C(0xE95420), Font = "Segoe UI", FontSize = 13, DockIcon = 42, Radius = 0,
                    MenuBg = C(0x1A0F18, 215), MenuFg = C(0xFFFFFF), MenuSub = C(0xCCCCCC), MenuHover = C(0xFFFFFF, 35), SearchHint = de ? "Tippen, um zu suchen" : "Type to search",
                    Frame = F(0x303030, 0x242424, 0xFFFFFF, 0x9A9A9A, 0x1E1E1E),
                };
                case "debian": return new ShellTheme
                {
                    Id = id, Name = "Debian", Layout = BarLayout.GnomeTopBottomDock, Btn = BtnStyle.Pill, Start = StartKind.Activities, Menu = MenuKind.Grid, MenuFullScreen = true,
                    BarHeight = 30, BarBg = S(0x000000, 235), Accent = C(0xD70A53), Font = "Segoe UI", FontSize = 13, DockIcon = 44, Radius = 16,
                    MenuBg = C(0x101018, 215), MenuFg = C(0xFFFFFF), MenuSub = C(0xCCCCCC), MenuHover = C(0xFFFFFF, 35), SearchHint = de ? "Tippen, um zu suchen" : "Type to search",
                    Frame = F(0xEBEBEB, 0xFAFAFA, 0x2E3436, 0x929595, 0xBDBDBD),
                };
                case "mint": return new ShellTheme
                {
                    Id = id, Name = "Linux Mint", Btn = BtnStyle.Flat, Start = StartKind.Mint, Menu = MenuKind.Seven,
                    BarHeight = 36, BarBg = S(0x2F2F2F, 250), Labels = true, QuickLaunch = true, Accent = C(0x8FD14F), BtnActive = C(0xFFFFFF, 30), ShowDate = false,
                    MenuBg = C(0x383838), MenuFg = C(0xEFEFEF), MenuSub = C(0xAAAAAA), MenuHover = C(0x8FD14F, 70), MenuSide = C(0x2B2B2B), MenuSideFg = C(0xEFEFEF), MenuRadius = 4,
                    SearchHint = de ? "Suchen …" : "Search …",
                    Frame = F(0x353535, 0x2C2C2C, 0xE5E5E5, 0x9A9A9A, 0x8FD14F, borderIn: 0x2C2C2C),
                };
                case "chromeos": return new ShellTheme
                {
                    Id = id, Name = "ChromeOS", Btn = BtnStyle.Pill, Start = StartKind.Launcher, Menu = MenuKind.Grid,
                    BarHeight = 48, BarBg = S(0x202124, 150), Blur = true, CenterTasks = true, Accent = C(0x8AB4F8), Font = "Segoe UI", FontSize = 12.5, Radius = 16,
                    MenuBg = C(0x2D2E31, 240), MenuFg = C(0xE8EAED), MenuSub = C(0xBDC1C6), MenuHover = C(0xFFFFFF, 25), MenuRadius = 18, SearchHint = de ? "Geräte, Apps, Einstellungen durchsuchen" : "Search your device, apps, settings",
                    Frame = F(0xDEE3EA, 0xF1F3F4, 0x202124, 0x80868B, 0xC4C7C5),
                };
                case "nova": return new ShellTheme
                {
                    Id = id, Name = "NovaOS", Layout = BarLayout.Floating, Btn = BtnStyle.Pill, Start = StartKind.Nova, Menu = MenuKind.Grid,
                    BarHeight = 52, BarBg = V((0, 0x1B1530, 225), (1, 0x0B0914, 235)), BarLine = C(0x8B5CF6, 160), Accent = C(0x8B5CF6), Font = "Bahnschrift", FontSize = 12.5, Radius = 26, DockIcon = 34,
                    MenuBg = C(0x0D0A18, 238), MenuFg = C(0xF3F0FF), MenuSub = C(0xA99BD8), MenuHover = C(0x8B5CF6, 70), MenuRadius = 22, SearchHint = de ? "Wohin möchtest du?" : "Where to?",
                    Frame = F(0x0B0914, 0x0B0914, 0xC4B5FD, 0x6B6385, 0x8B5CF6, borderIn: 0x2A2440),
                };
                case "terminal":
                {
                    // the hacker colour can be chosen in the app (green by default)
                    var g = HackerColor; var d = Core.Applier.Mix(g, Colors.Black, 0.45);
                    return new ShellTheme
                    {
                        Id = id, Name = "Hacker", Layout = BarLayout.TerminalTop, Btn = BtnStyle.Text, Start = StartKind.Terminal, Menu = MenuKind.Palette,
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
                    Id = id, Name = "Zen", Layout = BarLayout.Floating, Btn = BtnStyle.Pill, Start = StartKind.Zen, Menu = MenuKind.Palette,
                    BarHeight = 40, BarBg = S(0xF7F2E9, 225), BarFg = C(0x6B5B45), BarFgDim = C(0xA89A85), BtnFg = C(0x6B5B45), BtnHover = C(0x6B5B45, 25), BtnActive = C(0x6B5B45, 45),
                    Accent = C(0xA0805A), Font = "Segoe UI Light", FontSize = 13, Radius = 20, DockIcon = 24,
                    MenuBg = C(0xFBF8F2, 245), MenuFg = C(0x4A3F30), MenuSub = C(0x9C8E79), MenuHover = C(0xA0805A, 40), MenuRadius = 20, SearchHint = de ? "Atme ein. Tippe." : "Breathe. Type.",
                    Frame = F(0xF3EDE2, 0xF8F4EC, 0x6B5B45, 0xB3A691, 0xD8C6AA),
                };
                case "gaming": return new ShellTheme
                {
                    Id = id, Name = "Gaming", Btn = BtnStyle.Angular, Start = StartKind.Gaming, Menu = MenuKind.Ten,
                    BarHeight = 44, BarBg = V((0, 0x16161E, 250), (1, 0x08080C, 255)), BarLine = C(0xE81E4A), Accent = C(0xE81E4A), Font = "Bahnschrift", FontSize = 12.5, BtnActive = C(0xE81E4A, 90), CenterTasks = true,
                    MenuBg = C(0x0C0C12, 250), MenuFg = C(0xFFFFFF), MenuSub = C(0xFF8FA6), MenuHover = C(0xE81E4A, 70),
                    Frame = F(0x0A0A0F, 0x0A0A0F, 0xFF2D55, 0x6A5A60, 0xE81E4A, square: true, borderIn: 0x2A1A20),
                };
                case "nexus": return new ShellTheme
                {
                    Id = id, Name = "Nexus AI", Layout = BarLayout.AiOnly, Btn = BtnStyle.Pill, Start = StartKind.Nexus, Menu = MenuKind.Hub, MenuFullScreen = true,
                    BarHeight = 56, BarBg = V((0, 0x15122B, 225), (1, 0x07060F, 240)), BarLine = C(0x9B87FF, 170), BarFg = C(0xEDEBFF), BarFgDim = C(0x9A95C8),
                    Accent = C(0x7C5CFF), Font = "Segoe UI Variable Display", FontSize = 13, Radius = 28, DockIcon = 30,
                    BtnHover = C(0xFFFFFF, 30), BtnActive = C(0x7C5CFF, 80),
                    MenuBg = C(0x05040C, 215), MenuFg = C(0xF4F2FF), MenuSub = C(0xA9A3D9), MenuHover = C(0x7C5CFF, 70), MenuRadius = 24,
                    SearchHint = de ? "Sag mir, was du tun möchtest …" : "Tell me what you want to do …",
                    Frame = F(0x0B0A16, 0x0B0A16, 0xD9D3FF, 0x6B6690, 0x9B87FF, borderIn: 0x24203E),
                };
                case "arch": return new ShellTheme
                {
                    Id = id, Name = "Arch Linux", Layout = BarLayout.Waybar, Btn = BtnStyle.Pill, Start = StartKind.Arch, Menu = MenuKind.Palette,
                    BarHeight = 34, BarBg = S(0x11161D, 238), BarLine = C(0x1793D1, 200), BarFg = C(0xCDD6F4), BarFgDim = C(0x7F8BA3),
                    BtnHover = C(0x1793D1, 50), BtnActive = C(0x1793D1), BtnFg = C(0xCDD6F4), Accent = C(0x1793D1), Font = "Cascadia Mono, Consolas", FontSize = 12.5, Radius = 10, ShowDate = true,
                    MenuBg = C(0x11161D, 248), MenuFg = C(0xCDD6F4), MenuSub = C(0x7F8BA3), MenuHover = C(0x1793D1, 90), MenuRadius = 10,
                    SearchHint = "drun: ",
                    Frame = F(0x11161D, 0x11161D, 0xCDD6F4, 0x6C7086, 0x1793D1, square: true, borderIn: 0x2A313C),
                };
                default: return null; // win11 → original taskbar
            }
        }
    }
}

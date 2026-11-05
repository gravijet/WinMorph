using System.Windows.Media;

namespace WinMorph.Shell
{
    public enum BarLayout { Bottom, MacTopDock, GnomeTopLeftDock, GnomeTopBottomDock, Floating, TerminalTop, AiOnly, Waybar }
    public enum BtnStyle { Bevel, Luna, Glass, Flat, Pill, Text, Angular }
    public enum StartKind { Classic, Xp, Orb, Logo, Mint, Activities, Launcher, AiOrb, Nova, Terminal, Zen, Gaming, Apple, Nexus, Arch }
    public enum MenuKind { Classic, Xp, Seven, Ten, Grid, Palette, Hub }
    public enum LockKind { Modern, Mac, Gnome, Xp, Classic, Seven, Terminal, Ai, Zen, Gaming }
    public enum SwitchKind { Classic, Thumbs, Mac, Text }

    public sealed class FrameColors
    {
        public Color Caption, CaptionInactive, Text, TextInactive, Border, BorderInactive;
        public bool Square;
    }

    /// <summary>Everything the WinMorph shell needs to draw one look.</summary>
    public sealed class ShellTheme
    {
        public string Id, Name;
        public BarLayout Layout = BarLayout.Bottom;
        public BtnStyle Btn = BtnStyle.Flat;
        public StartKind Start = StartKind.Logo;
        public MenuKind Menu = MenuKind.Ten;
        public bool MenuFullScreen;

        public double BarHeight = 40;          // DIP
        public Brush BarBg;
        public Color BarFg = Colors.White, BarFgDim = Color.FromRgb(0xB0, 0xB0, 0xB0), BarLine = Color.FromArgb(0, 0, 0, 0);
        public Color BtnBg = Color.FromArgb(0, 0, 0, 0), BtnHover = Color.FromArgb(40, 255, 255, 255), BtnActive = Color.FromArgb(70, 255, 255, 255), BtnFg = Colors.White;
        public Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);
        public bool Labels = false, CenterTasks = false, Blur = false, QuickLaunch = false, SearchBox = false, ShowDate = false;
        public string Font = "Segoe UI";
        public double FontSize = 12;
        public double Radius = 0;              // floating bar / dock corner radius
        public double DockIcon = 44;

        // start menu
        public Color MenuBg = Color.FromRgb(0x20, 0x20, 0x20), MenuFg = Colors.White, MenuSub = Color.FromRgb(0xA0, 0xA0, 0xA0), MenuHover = Color.FromArgb(40, 255, 255, 255);
        public Color MenuSide = Color.FromRgb(0x18, 0x18, 0x18), MenuSideFg = Colors.White, MenuHeader = Color.FromRgb(0x10, 0x10, 0x10), MenuHeaderFg = Colors.White;
        public double MenuRadius = 0;
        public string SearchHint;

        public FrameColors Frame;
        public LockKind Lock = LockKind.Modern;
        public SwitchKind Switch = SwitchKind.Thumbs;
    }
}

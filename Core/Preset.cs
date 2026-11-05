using System;
using System.Windows.Media;

namespace WinMorph.Core
{
    public enum WallKind { Bloom, Beams, Aero, Hills, Teal, Waves, WavesDark, Jelly, Swirl, Mint, Chrome, Aurora, Matrix, Zen, Stripes, Holo, Arch }
    public enum CursorKind { Aero, Classic, Black, Inverted }

    [Flags]
    public enum Area
    {
        None = 0, Taskbar = 1, Colors = 2, Windows = 4, Desktop = 8, Cursor = 16, Start = 32, Fonts = 64, Sounds = 128, Shell = 256, Lock = 512,
        All = Taskbar | Colors | Windows | Desktop | Cursor | Start | Fonts | Sounds | Shell | Lock,
        /// <summary>Only WinMorph's own design on top of Windows: no Windows settings are changed.</summary>
        Design = Shell | Desktop | Lock,
        /// <summary>Parts that change real Windows settings.</summary>
        Settings = Taskbar | Colors | Windows | Cursor | Start | Fonts | Sounds
    }

    /// <summary>A complete, fixed look. Users pick one; they do not edit values.</summary>
    public sealed class Preset
    {
        public string Id, Name, Group, TagDe, TagEn, DescDe, DescEn;

        // Taskbar
        public bool Center = true;
        public int Combine = 0;          // 0 always, 1 when full, 2 never (shows labels)
        public int Search = 1;           // 0 hidden, 1 icon, 2 box, 3 icon + label
        public bool TaskView = true, Widgets = false, Copilot = false, AutoHide = false, SmallIcons = false, Seconds = false;
        public int EpTaskbar = 0;        // ExplorerPatcher: 0 = Windows 11 taskbar, 1 = Windows 10 taskbar
        public int EpEdge = 3;           // ExplorerPatcher taskbar edge: 0 left, 1 top, 2 right, 3 bottom

        // Colours
        public bool DarkSystem = false, DarkApps = false, Transparency = true;
        public Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);
        public bool AccentTaskbar = false, AccentTitle = false;

        // Windows & motion
        public bool Animations = true, FullDrag = true, Snap = true, Shake = false, MenuAnim = true, Shadows = true;
        public bool FocusFollows = false, ClearType = true, ClassicContextMenu = false, Compact = false, LaunchThisPC = false;
        public int MenuDelay = 400;

        // Fonts (Win32 title bars, menus, dialogs, desktop icons)
        public string Font = "Segoe UI";
        public bool CaptionBold = false;

        // Desktop
        public WallKind Wall = WallKind.Bloom;
        public bool HideDesktopIcons = false, ShowThisPC = false;
        public string ScreenSaver = null;   // e.g. "Mystify.scr"; null = none
        public string Live = null;          // live wallpaper, e.g. "matrix-hud" (see Live.WallCatalog)

        // Sounds
        public bool Silent = false;

        // Cursor
        public CursorKind Cursor = CursorKind.Aero;

        // Start menu
        public int StartLayout = 0;          // 0 default, 1 more pins, 2 more recommendations
        public bool Recommendations = true;
        public string OsStyle = null;        // Open-Shell: Classic1, Classic2, Win7 (null = Windows start menu)
        public string OsSkin = null;
        public string OsButton = null;       // ClassicButton, AeroButton
        public bool EpClassicStart = false;  // ExplorerPatcher: Windows 10 start menu

        public string Tag(bool de) => de ? TagDe : TagEn;
        public string Desc(bool de) => de ? DescDe : DescEn;

        public bool IsLight => !DarkSystem;
    }
}

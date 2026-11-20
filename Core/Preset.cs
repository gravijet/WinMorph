using System.Windows.Media;

namespace WinMorph.Core
{
    public enum WallKind { Aero, Matrix, Zen }

    /// <summary>
    /// A complete, fixed look. Applying it always applies everything WinMorph draws itself
    /// (taskbar, start menu, Alt+Tab, window frames, desktop, lock screen) and never changes
    /// Windows settings such as dark mode, accent colour, fonts, cursors or sounds.
    /// </summary>
    public sealed class Preset
    {
        public string Id, Name, TagDe, TagEn, DescDe, DescEn;
        public Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);
        public WallKind Wall = WallKind.Aero;
        public string Live = null;          // live desktop of the look, e.g. "zen-desk" (see Live.WallCatalog)

        public string Tag(bool de) => de ? TagDe : TagEn;
        public string Desc(bool de) => de ? DescDe : DescEn;
    }
}

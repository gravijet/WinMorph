using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WinMorph.Shell
{
    /// <summary>Small helpers to build the shell UI in code.</summary>
    internal static class Ui
    {
        public const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush B(uint rgb, byte a = 255) => B(Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        public static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        public static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        public static LinearGradientBrush V(params (double o, Color c)[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var s in stops) b.GradientStops.Add(new GradientStop(s.c, s.o));
            b.Freeze();
            return b;
        }

        public static TextBlock T(string text, ShellTheme t, double? size = null, Color? color = null, FontWeight? weight = null, string font = null)
            => new TextBlock
            {
                Text = text, FontFamily = new FontFamily(font ?? t.Font), FontSize = size ?? t.FontSize, Foreground = B(color ?? t.BarFg),
                FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };

        public static TextBlock Glyph(string code, double size, Color color)
            => new TextBlock { Text = code, FontFamily = new FontFamily(IconFont), FontSize = size, Foreground = B(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

        public static Image Img(ImageSource src, double size)
        {
            var i = new Image { Source = src, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality);
            return i;
        }

        /// <summary>A hoverable, clickable area. Hover colour is applied to the Border background.</summary>
        public static Border Click(UIElement child, Action onClick, Color hover, Brush normal = null, CornerRadius? radius = null, Thickness? pad = null, Action onRight = null)
        {
            var b = new Border { Child = child, Background = normal ?? Brushes.Transparent, CornerRadius = radius ?? new CornerRadius(0), Padding = pad ?? new Thickness(0), Cursor = Cursors.Hand };
            var hoverBrush = B(hover);
            b.MouseEnter += (s, e) => { b.Tag = b.Background; b.Background = hoverBrush; };
            b.MouseLeave += (s, e) => { if (b.Tag is Brush old) b.Background = old; };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick?.Invoke(); };
            if (onRight != null) b.MouseRightButtonUp += (s, e) => { e.Handled = true; onRight(); };
            return b;
        }

        /// <summary>A four-pane logo in the given colours (a simple, generic window mark).</summary>
        public static FrameworkElement Logo4(double size, Color a, Color b, Color c, Color d, double skew = 0)
        {
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            double q = size * 0.46, gap = size * 0.08;
            void Add(Color col, double x, double y) => g.Children.Add(new Border { Width = q, Height = q, Background = B(col), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0), CornerRadius = new CornerRadius(size * 0.04) });
            Add(a, 0, 0); Add(b, q + gap, 0); Add(c, 0, q + gap); Add(d, q + gap, q + gap);
            if (skew != 0) g.RenderTransform = new SkewTransform(0, skew);
            return g;
        }

        /// <summary>3x3 dots, the usual "show all apps" symbol.</summary>
        public static FrameworkElement Dots(double size, Color color)
        {
            var g = new UniformGrid { Rows = 3, Columns = 3, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 9; i++) g.Children.Add(new System.Windows.Shapes.Ellipse { Width = size * 0.2, Height = size * 0.2, Fill = B(color) });
            return g;
        }

        public static ImageSource FolderIcon(Environment.SpecialFolder f) => W32.FileIcon(Environment.GetFolderPath(f));
        public static ImageSource ThisPcIcon() => W32.ShellItemIcon("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 32);
        public static ImageSource SysIcon(string exe) => W32.FileIcon(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe));

        public static FrameworkElement ClassicLogo(double size) => Logo4(size, C(0xF35325), C(0x81BC06), C(0x05A6F0), C(0xFFBA08), -6);
    }

    /// <summary>Windows 95 style 3D bevel (raised or sunken).</summary>
    internal sealed class Bevel : Decorator
    {
        public bool Sunken;
        public bool Dither;
        public Brush Fill = Ui.B(0xC0C0C0);
        static readonly Pen White = new Pen(Ui.B(0xFFFFFF), 1), Light = new Pen(Ui.B(0xDFDFDF), 1), Dark = new Pen(Ui.B(0x808080), 1), Black = new Pen(Ui.B(0x000000), 1);
        static Brush dither;

        protected override Size MeasureOverride(Size s)
        {
            if (Child == null) return new Size(4, 4);
            Child.Measure(new Size(Math.Max(0, s.Width - 6), Math.Max(0, s.Height - 6)));
            return new Size(Child.DesiredSize.Width + 6, Child.DesiredSize.Height + 6);
        }

        protected override Size ArrangeOverride(Size s)
        {
            Child?.Arrange(new Rect(3 + (Sunken ? 1 : 0), 3 + (Sunken ? 1 : 0), Math.Max(0, s.Width - 6), Math.Max(0, s.Height - 6)));
            return s;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            dc.DrawRectangle(Dither ? DitherBrush() : Fill, null, new Rect(0, 0, w, h));
            void L(Pen p, double x1, double y1, double x2, double y2) => dc.DrawLine(p, new Point(x1 + 0.5, y1 + 0.5), new Point(x2 + 0.5, y2 + 0.5));
            Pen o1 = Sunken ? Black : White, o2 = Sunken ? White : Black, i1 = Sunken ? Dark : Light, i2 = Sunken ? Light : Dark;
            L(o1, 0, 0, w - 1, 0); L(o1, 0, 0, 0, h - 1);
            L(o2, 0, h - 1, w - 1, h - 1); L(o2, w - 1, 0, w - 1, h - 1);
            L(i1, 1, 1, w - 2, 1); L(i1, 1, 1, 1, h - 2);
            L(i2, 1, h - 2, w - 2, h - 2); L(i2, w - 2, 1, w - 2, h - 2);
        }

        static Brush DitherBrush()
        {
            if (dither != null) return dither;
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Ui.B(0xC0C0C0), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
            dg.Children.Add(new GeometryDrawing(Ui.B(0xFFFFFF), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            dg.Children.Add(new GeometryDrawing(Ui.B(0xFFFFFF), null, new RectangleGeometry(new Rect(1, 1, 1, 1))));
            var b = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 2, 2), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            b.Freeze();
            return dither = b;
        }
    }
}

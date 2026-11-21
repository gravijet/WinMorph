using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The Zen look ("Morgenteich", a koi pond in the morning): every Zen surface is frosted glass over the
    /// pond (the wallpaper shines through, softly blurred), set in Yu Gothic UI, with the colours of the pond.
    /// Motion is water: things surface with a ripple and settle without a bounce.
    /// </summary>
    internal static class ZenStyle
    {
        // palette "Morgenteich"
        public static readonly Color Ink = C(0x1C2A2D), Ink2 = C(0x4F6064), Mist = C(0xF4F8F7),
            Pond = C(0x0E7C78), PondDeep = C(0x0B4F5C), Moss = C(0x4A8A3E), Koi = C(0xF0621F), Blossom = C(0xDE4F95), Momiji = C(0xC9302A), Sun = C(0xF6C343);

        public const string Font = "Yu Gothic UI";
        public static readonly FontFamily Family = new FontFamily(Font);

        public static TextBlock Text(string s, double size, Color c, FontWeight? w = null) => new TextBlock
        {
            Text = s, FontFamily = Family, FontSize = size, Foreground = B(c), FontWeight = w ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // ---------------- frosted glass ----------------
        static BitmapSource frost;
        static string frostFile;

        /// <summary>The pond, small and blurred: everything behind a Zen surface.</summary>
        public static BitmapSource FrostImage
        {
            get
            {
                string file = null;
                try { file = Core.WallpaperArt.RenderForScreen(Core.Presets.Find("zen")); } catch { }
                if (frost != null && file == frostFile) return frost;
                frostFile = file;
                ImageSource src = null;
                if (file != null)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file); bi.DecodePixelWidth = 400; bi.EndInit(); bi.Freeze();
                        src = bi;
                    }
                    catch { }
                }
                if (src == null) src = Core.WallpaperArt.Render(Core.Presets.Find("zen"), 400, 225, false);
                const int w = 320, h = 180;
                var img = new Image { Source = src, Width = w + 40, Height = h + 40, Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 14, KernelType = KernelType.Gaussian } };
                var host = new Canvas { Width = w, Height = h, ClipToBounds = true };
                Canvas.SetLeft(img, -20); Canvas.SetTop(img, -20);
                host.Children.Add(img);
                host.Measure(new Size(w, h)); host.Arrange(new Rect(0, 0, w, h)); host.UpdateLayout();
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(host);
                rtb.Freeze();
                return frost = rtb;
            }
        }

        /// <summary>
        /// A frosted pane: the blurred pond at the place on the screen where the pane is, a misty tint and a
        /// thin light edge. The pond lines up with the desktop behind it, so the pane reads as glass.
        /// </summary>
        public static Grid Pane(UIElement content, double radius, byte tint = 150, bool shadow = true)
        {
            var brush = new ImageBrush(FrostImage) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            var cr = new CornerRadius(radius);
            var g = new Grid();
            var glass = new Border { CornerRadius = cr, Background = brush };
            if (shadow) glass.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.28, Color = PondDeep };
            g.Children.Add(glass);
            g.Children.Add(new Border { CornerRadius = cr, Background = B(A(Mist, tint)) });
            // light falls on the upper edge of the glass
            g.Children.Add(new Border
            {
                CornerRadius = cr, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                BorderBrush = new LinearGradientBrush(C(0xFFFFFF, 230), C(0xFFFFFF, 90), 90),
            });
            g.Children.Add(content);
            void Align()
            {
                if (PresentationSource.FromVisual(g) == null || g.ActualWidth < 1) return;
                try
                {
                    var p = g.PointToScreen(new Point(0, 0));
                    var mi = W32.Monitor(W32.MonitorFromPoint(new W32.POINT { X = (int)p.X + 2, Y = (int)p.Y + 2 }, 2), out double sc);
                    double mw = mi.rcMonitor.Right - mi.rcMonitor.Left, mh = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    brush.Viewbox = new Rect((p.X - mi.rcMonitor.Left) / mw, (p.Y - mi.rcMonitor.Top) / mh, g.ActualWidth * sc / mw, g.ActualHeight * sc / mh);
                }
                catch { }
            }
            g.LayoutUpdated += (s, e) => Align();
            g.Loaded += (s, e) =>
            {
                Align();
                var w = Window.GetWindow(g);
                if (w != null) { w.LocationChanged += (s2, e2) => Align(); w.IsVisibleChanged += (s2, e2) => Align(); }
            };
            return g;
        }

        // ---------------- scroll bars: a thin rounded reed instead of Windows' bar with arrows ----------------
        static ResourceDictionary scroll;
        public static ResourceDictionary ScrollBars
        {
            get
            {
                if (scroll != null) return scroll;
                const string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='ZenThumb' TargetType='Thumb'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
      <Border x:Name='b' CornerRadius='4' Background='#661C2A2D' Margin='2'/>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#AA0E7C78'/></Trigger>
      <Trigger Property='IsDragging' Value='True'><Setter TargetName='b' Property='Background' Value='#FF0E7C78'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='ZenPage' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ZenPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ZenPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Style='{StaticResource ZenThumb}'/></Track.Thumb>
      </Track>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'>
      <Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='10'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
        <Track x:Name='PART_Track'>
          <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ZenPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
          <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ZenPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
          <Track.Thumb><Thumb Style='{StaticResource ZenThumb}'/></Track.Thumb>
        </Track>
      </ControlTemplate></Setter.Value></Setter>
    </Trigger></Style.Triggers>
  </Style>
</ResourceDictionary>";
                try { scroll = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml); }
                catch { scroll = new ResourceDictionary(); }
                return scroll;
            }
        }

        // ---------------- motion: water ----------------
        public static readonly IEasingFunction Surface = new QuinticEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction Settle = new SineEase { EasingMode = EasingMode.EaseInOut };

        /// <summary>
        /// The signature: a pane surfaces like a ring of water spreading from a dropped stone. It is revealed by a
        /// circle that grows from <paramref name="origin"/> (relative 0..1) while it rises a few pixels.
        /// </summary>
        public static void Ripple(FrameworkElement el, Point origin, double ms = 420)
        {
            if (!SystemParameters.ClientAreaAnimation || RenderGuard.Software) { el.Opacity = 1; return; }
            el.UpdateLayout();
            double w = Math.Max(1, el.ActualWidth), h = Math.Max(1, el.ActualHeight);
            var c = new Point(origin.X * w, origin.Y * h);
            double r = Math.Sqrt(Math.Pow(Math.Max(c.X, w - c.X), 2) + Math.Pow(Math.Max(c.Y, h - c.Y), 2));
            var clip = new EllipseGeometry(c, 1, 1);
            el.Clip = clip;
            var d = TimeSpan.FromMilliseconds(ms);
            var grow = new DoubleAnimation(Math.Min(24, r * 0.1), r * 1.02, d) { EasingFunction = Surface };
            grow.Completed += (s, e) => { if (el.Clip == clip) el.Clip = null; };
            clip.BeginAnimation(EllipseGeometry.RadiusXProperty, grow);
            clip.BeginAnimation(EllipseGeometry.RadiusYProperty, grow);
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(ms * 0.6)));
            var tt = el.RenderTransform as TranslateTransform ?? new TranslateTransform();
            el.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = Surface });
        }

        /// <summary>Rings that spread from a point and fade (a key press on the lock, a click on a stone).</summary>
        public static void Rings(Panel layer, Point at, Color c, int count = 2, double size = 80)
        {
            if (!SystemParameters.ClientAreaAnimation || RenderGuard.Software) return;
            for (int i = 0; i < count; i++)
            {
                var ring = new Ellipse { Width = size, Height = size, Stroke = B(c), StrokeThickness = 1.6, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(at.X - size / 2, at.Y - size / 2, 0, 0), Opacity = 0 };
                var sc = new ScaleTransform(0.1, 0.1);
                ring.RenderTransform = sc;
                layer.Children.Add(ring);
                var begin = TimeSpan.FromMilliseconds(i * 140);
                var d = TimeSpan.FromMilliseconds(900);
                var grow = new DoubleAnimation(0.1, 1, d) { BeginTime = begin, EasingFunction = Surface };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = d };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.08)));
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), Settle));
                fade.Completed += (s, e) => layer.Children.Remove(ring);
                ring.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        /// <summary>A round pebble button with a glyph: the Zen shape for actions.</summary>
        public static Border Stone(string glyph, Action click, string tip, double size = 38, Color? fill = null, Color? fg = null)
        {
            var f = fill ?? C(0xFFFFFF, 150);
            var b = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = B(f), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip,
                BorderBrush = B(0xFFFFFF, 200), BorderThickness = new Thickness(1),
                Child = Glyph(glyph, size * 0.4, fg ?? Ink), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            };
            Press(b, f, A(Pond, 60));
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click?.Invoke(); };
            return b;
        }

        /// <summary>Hover tint and a soft press for any Zen control.</summary>
        public static void Press(Border b, Color normal, Color hover)
        {
            var bg = new SolidColorBrush(normal);
            b.Background = bg;
            var sc = b.RenderTransform as ScaleTransform;
            if (sc == null) { sc = new ScaleTransform(1, 1); b.RenderTransform = sc; b.RenderTransformOrigin = new Point(0.5, 0.5); }
            var t = TimeSpan.FromMilliseconds(160);
            b.MouseEnter += (s, e) => bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(hover, t));
            b.MouseLeave += (s, e) => { bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(normal, t)); Scale(sc, 1); };
            b.PreviewMouseLeftButtonDown += (s, e) => Scale(sc, 0.95);
            b.PreviewMouseLeftButtonUp += (s, e) => Scale(sc, 1);
        }

        static void Scale(ScaleTransform sc, double to)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to < 1 ? 90 : 220)) { EasingFunction = Surface };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>The Zen mark: a lotus of three petals over a ripple (the start button, the lock, the apps).</summary>
        public static FrameworkElement Lotus(double size)
        {
            var g = new Grid { Width = size, Height = size };
            Geometry Petal(double angle)
            {
                var p = Geometry.Parse("M 50,84 C 30,64 34,30 50,10 C 66,30 70,64 50,84 Z").Clone();
                p.Transform = new RotateTransform(angle, 50, 84);
                return p;
            }
            var petals = new GeometryGroup();
            petals.Children.Add(Petal(-46)); petals.Children.Add(Petal(46));
            var side = new Path { Data = petals, Fill = new LinearGradientBrush(C(0xF59BC6), Blossom, 90), Stretch = Stretch.None };
            var mid = new Path { Data = Petal(0), Fill = new LinearGradientBrush(C(0xFFC3A0), Koi, 90.0) };
            var canvas = new Viewbox { Child = new Grid { Width = 100, Height = 100, Children = { side, mid } } };
            g.Children.Add(canvas);
            return g;
        }
    }
}

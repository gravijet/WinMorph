using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The macOS look (Tahoe, light appearance): Apple's system colours, SF Pro when it is installed and the
    /// embedded Inter (SIL OFL) otherwise, vibrancy (the wallpaper blurred and saturated behind every surface),
    /// app icons on the macOS icon grid, SF-Symbol-like line symbols, menus, switches, sliders and alerts.
    /// Everything is drawn here; no Apple artwork, fonts or logos are shipped.
    /// </summary>
    internal static class MacStyle
    {
        // ---------------- system colours (light appearance) ----------------
        public static readonly Color Label = C(0x1D1D1F), Secondary = C(0x6E6E73), Tertiary = C(0xAEAEB2), Line = C(0x000000, 26),
            Blue = C(0x007AFF), Red = C(0xFF3B30), Orange = C(0xFF9500), Yellow = C(0xFFCC00), Green = C(0x34C759), Mint = C(0x00C7BE),
            Teal = C(0x30B0C7), Indigo = C(0x5856D6), Purple = C(0xAF52DE), Pink = C(0xFF2D55), Gray = C(0x8E8E93), Fill = C(0x787880, 36);

        // traffic lights
        public static readonly Color CloseRed = C(0xFF5F57), MinYellow = C(0xFEBC2E), ZoomGreen = C(0x28C840);

        public const double MenuBarHeight = 24, DockIcon = 54;

        static bool Installed(string name) { try { return Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase)); } catch { return false; } }
        static readonly bool sf = Installed("SF Pro Text") || Installed("SF Pro");
        static FontFamily Embedded(string name) => new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#" + name);
        public static readonly FontFamily Font = Installed("SF Pro Text") ? new FontFamily("SF Pro Text") : Installed("SF Pro") ? new FontFamily("SF Pro") : Embedded("Inter");
        public static readonly FontFamily Display = Installed("SF Pro Display") ? new FontFamily("SF Pro Display") : Installed("SF Pro") ? new FontFamily("SF Pro") : Embedded("Inter Display");
        /// <summary>The Apple mark only exists in Apple's own fonts; without them the menu shows a neutral mark.</summary>
        public static bool HasAppleMark => sf;

        public static TextBlock Text(string s, double size, Color c, FontWeight? w = null, bool display = false) => new TextBlock
        {
            Text = s, FontFamily = display ? Display : Font, FontSize = size, Foreground = B(c), FontWeight = w ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        /// <summary>Applies the look's type and text rendering to a window.</summary>
        public static void Prepare(Window w)
        {
            w.FontFamily = Font; w.FontSize = 13;
            w.Resources[typeof(TextBlock)] = new Style(typeof(TextBlock));
            w.Resources.MergedDictionaries.Add(ScrollBars);
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(w, TextRenderingMode.Grayscale);
        }

        public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction OutQuint = new QuinticEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction In = new CubicEase { EasingMode = EasingMode.EaseIn };
        /// <summary>macOS springs: quick, settle with a very small overshoot.</summary>
        public static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 };

        // ---------------- vibrancy ----------------
        static BitmapSource frost;
        static string frostFile;

        /// <summary>The wallpaper of the look, blurred and with more colour: what shines through every surface.</summary>
        public static BitmapSource FrostImage
        {
            get
            {
                string file = null;
                try { file = Core.WallpaperArt.RenderForScreen(Core.Presets.Find("macos")); } catch { }
                if (frost != null && file == frostFile) return frost;
                frostFile = file;
                ImageSource src = null;
                if (file != null)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file); bi.DecodePixelWidth = 480; bi.EndInit(); bi.Freeze();
                        src = bi;
                    }
                    catch { }
                }
                if (src == null) src = Core.WallpaperArt.Render(Core.Presets.Find("macos"), 480, 270, false);
                return frost = Frost(src);
            }
        }

        /// <summary>A picture blurred and with more colour, the way macOS materials show what is behind them.</summary>
        static BitmapSource Frost(ImageSource src, double lift = 0)
        {
            const int w = 384, h = 216;
            var img = new Image { Source = src, Width = w + 60, Height = h + 60, Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 22, KernelType = KernelType.Gaussian } };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var host = new Canvas { Width = w, Height = h, ClipToBounds = true };
            Canvas.SetLeft(img, -30); Canvas.SetTop(img, -30);
            host.Children.Add(img);
            host.Measure(new Size(w, h)); host.Arrange(new Rect(0, 0, w, h)); host.UpdateLayout();
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            // vibrancy lifts the colour of what is behind
            var px = new byte[w * h * 4];
            rtb.CopyPixels(px, w * 4, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2], l = 0.3 * r + 0.59 * g + 0.11 * b;
                px[i] = Lift(Sat(l + (b - l) * 1.45), lift); px[i + 1] = Lift(Sat(l + (g - l) * 1.45), lift); px[i + 2] = Lift(Sat(l + (r - l) * 1.45), lift); px[i + 3] = 255;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            bmp.Freeze();
            return bmp;
        }

        static byte Sat(double v) => (byte)Math.Max(0, Math.Min(255, v));
        static byte Lift(byte v, double k) => (byte)(v + (255 - v) * k);

        static BitmapSource light, lightFrom;
        /// <summary>
        /// The material of surfaces that open over windows (menus, panels, Spotlight, the switcher): the wallpaper
        /// frost lifted towards white, so it stays light and neutral whatever window is behind. The screen itself
        /// is never read.
        /// </summary>
        static BitmapSource LightFrost
        {
            get
            {
                var f = FrostImage;
                if (light == null || lightFrom != f) { light = Frost(f, 0.5); lightFrom = f; }
                return light;
            }
        }

        /// <summary>Average brightness of the top strip of the wallpaper: the menu bar text is dark on light pictures.</summary>
        public static bool LightTop
        {
            get
            {
                try
                {
                    var f = FrostImage;
                    int w = f.PixelWidth;
                    var px = new byte[w * 4 * 6];
                    f.CopyPixels(new Int32Rect(0, 0, w, 6), px, w * 4, 0);
                    double sum = 0;
                    for (int i = 0; i < px.Length; i += 4) sum += 0.3 * px[i + 2] + 0.59 * px[i + 1] + 0.11 * px[i];
                    return sum / (px.Length / 4) > 150;
                }
                catch { return true; }
            }
        }

        /// <summary>
        /// A vibrant pane: the blurred wallpaper at the place on the screen where the pane is, a tint, and the fine
        /// light rim and dark hairline of macOS materials.
        /// </summary>
        public static Grid Glass(UIElement content, double radius, Color tint, bool shadow = true, bool rim = true, bool behind = false)
        {
            var brush = new ImageBrush(behind ? LightFrost : FrostImage) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            var cr = new CornerRadius(radius);
            var g = new Grid();
            var glass = new Border { CornerRadius = cr, Background = brush };
            if (shadow) glass.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Direction = 270, Opacity = 0.22, Color = Colors.Black };
            g.Children.Add(glass);
            g.Children.Add(new Border { CornerRadius = cr, Background = B(tint) });
            if (rim)
            {
                g.Children.Add(new Border { CornerRadius = cr, BorderThickness = new Thickness(0.8), BorderBrush = B(0x000000, 34), IsHitTestVisible = false });
                g.Children.Add(new Border { CornerRadius = new CornerRadius(Math.Max(0, radius - 1)), Margin = new Thickness(0.8), BorderThickness = new Thickness(1), IsHitTestVisible = false, BorderBrush = new LinearGradientBrush(C(0xFFFFFF, 200), C(0xFFFFFF, 70), 90) });
            }
            g.Children.Add(content);
            void Align()
            {
                if (PresentationSource.FromVisual(g) == null || g.ActualWidth < 1) return;
                try
                {
                    var p = g.PointToScreen(new Point(0, 0));
                    var mon = W32.MonitorFromPoint(new W32.POINT { X = (int)p.X + 2, Y = (int)p.Y + 2 }, 2);
                    var mi = W32.Monitor(mon, out double sc);
                    double mw = mi.rcMonitor.Right - mi.rcMonitor.Left, mh = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    double vw = Math.Min(1, g.ActualWidth * sc / mw), vh = Math.Min(1, g.ActualHeight * sc / mh);
                    // stay on the picture (a pane still sliding in from outside the screen keeps showing glass)
                    double vx = Math.Max(0, Math.Min(1 - vw, (p.X - mi.rcMonitor.Left) / mw)), vy = Math.Max(0, Math.Min(1 - vh, (p.Y - mi.rcMonitor.Top) / mh));
                    brush.Viewbox = new Rect(vx, vy, vw, vh);
                }
                catch { }
            }
            // render transforms (slide-ins, pops) do not cause layout: follow them for a moment after showing
            DispatcherTimer follow = null;
            void Follow()
            {
                follow?.Stop();
                int n = 0;
                follow = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
                follow.Tick += (s, e) => { Align(); if (++n > 18) follow.Stop(); };
                follow.Start();
            }
            g.LayoutUpdated += (s, e) => Align();
            g.Loaded += (s, e) =>
            {
                Align(); Follow();
                var w = Window.GetWindow(g);
                if (w != null) { w.LocationChanged += (s2, e2) => Align(); w.IsVisibleChanged += (s2, e2) => { Align(); if (w.IsVisible) Follow(); }; }
            };
            return g;
        }

        /// <summary>Menus, popovers, the switcher: the light "menu" material.</summary>
        public static readonly Color MenuTint = C(0xF6F6F6, 205);

        // ---------------- scroll bars: thin overlay bars without arrows ----------------
        static ResourceDictionary scroll;
        public static ResourceDictionary ScrollBars
        {
            get
            {
                if (scroll != null) return scroll;
                const string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='MacThumb' TargetType='Thumb'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
      <Border x:Name='t' CornerRadius='4' Background='#55000000' Margin='2'/>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#88000000'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='11'/>
    <Setter Property='MinWidth' Value='11'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Grid Background='Transparent'>
        <Track x:Name='PART_Track' IsDirectionReversed='True'>
          <Track.Thumb><Thumb Style='{StaticResource MacThumb}'/></Track.Thumb>
        </Track>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='11'/><Setter Property='MinHeight' Value='11'/>
        <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='False'><Track.Thumb><Thumb Style='{StaticResource MacThumb}'/></Track.Thumb></Track></Grid>
        </ControlTemplate></Setter.Value></Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
                return scroll = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
            }
        }

        // ---------------- the macOS icon shape ----------------
        /// <summary>The continuous-corner icon shape (a superellipse), in the given box.</summary>
        public static Geometry Squircle(double x, double y, double w, double h)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                const int n = 160; const double e = 2 / 5.0;
                double a = w / 2, b = h / 2, cx = x + a, cy = y + b;
                for (int i = 0; i < n; i++)
                {
                    double t = 2 * Math.PI * i / n, ct = Math.Cos(t), st = Math.Sin(t);
                    var p = new Point(cx + a * Math.Sign(ct) * Math.Pow(Math.Abs(ct), e), cy + b * Math.Sign(st) * Math.Pow(Math.Abs(st), e));
                    if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        }

        static readonly ConditionalWeakTable<ImageSource, ImageSource> tiles = new ConditionalWeakTable<ImageSource, ImageSource>();
        static ImageSource blankTile;

        /// <summary>
        /// A Windows app icon on the macOS icon grid: on a light squircle with the soft shadow of macOS icons
        /// (as macOS shows icons that do not have the shape themselves). The picture includes the grid's margin.
        /// </summary>
        public static ImageSource Tile(ImageSource icon)
        {
            if (icon == null) return blankTile ?? (blankTile = Own("app"));
            if (tiles.TryGetValue(icon, out var hit)) return hit;
            var made = Render(128, dc =>
            {
                var shape = Squircle(12.5, 10, 103, 103);
                dc.PushTransform(new TranslateTransform(0, 2.2));
                dc.PushOpacity(0.22);
                dc.DrawGeometry(B(0x000000), null, shape);
                dc.Pop(); dc.Pop();
                dc.DrawGeometry(new LinearGradientBrush(C(0xFFFFFF), C(0xE6E6EB), 90), new Pen(B(0x000000, 22), 0.8), shape);
                dc.DrawImage(icon, new Rect(64 - 33, 61.5 - 33, 66, 66));
            }, shadowBlur: 5);
            tiles.Add(icon, made);
            return made;
        }

        /// <summary>Renders a 128 px drawing, optionally with a soft shadow under the whole picture.</summary>
        public static ImageSource Render(int size, Action<DrawingContext> draw, double shadowBlur = 0)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 128.0, size / 128.0));
                draw(dc);
            }
            if (shadowBlur > 0) dv.Effect = new DropShadowEffect { BlurRadius = shadowBlur * size / 128.0, ShadowDepth = 1.5 * size / 128.0, Direction = 270, Opacity = 0.18 };
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static readonly Dictionary<string, ImageSource> own = new Dictionary<string, ImageSource>();

        /// <summary>Icons of the look's own apps and dock items, drawn on the 128 px macOS icon grid.</summary>
        public static ImageSource Own(string kind)
        {
            if (own.TryGetValue(kind, out var hit)) return hit;
            var body = Squircle(12.5, 10, 103, 103);
            ImageSource img = Render(128, dc =>
            {
                switch (kind)
                {
                    case "finder":
                    {
                        // two blues split by a soft curve, a white folder in the middle
                        dc.DrawGeometry(new LinearGradientBrush(C(0x7AD0FF), C(0x1C9BF5), 90), null, body);
                        dc.PushClip(body);
                        var right = Geometry.Parse("M66,8 C58,40 74,70 62,118 L120,118 L120,8 Z");
                        dc.DrawGeometry(new LinearGradientBrush(C(0x2A86F0), C(0x0B4FC4), 90), null, right);
                        dc.Pop();
                        Folder(dc, new Rect(36, 40, 56, 44), C(0xFFFFFF), C(0xEAF4FF), 245);
                        break;
                    }
                    case "launchpad":
                    {
                        dc.DrawGeometry(new LinearGradientBrush(C(0xF7F7FA), C(0xD5D5DC), 90), new Pen(B(0x000000, 22), 0.8), body);
                        var cols = new[] { Red, Orange, Yellow, Green, Mint, Blue, Indigo, Purple, Pink };
                        for (int i = 0; i < 9; i++)
                        {
                            double x = 33 + (i % 3) * 22, y = 30 + (i / 3) * 22;
                            dc.DrawRoundedRectangle(new LinearGradientBrush(Core.Applier.Mix(cols[i], Colors.White, 0.25), cols[i], 90), null, new Rect(x, y, 18, 18), 5.2, 5.2);
                        }
                        break;
                    }
                    case "activity":
                    {
                        dc.DrawGeometry(new LinearGradientBrush(C(0x3A3A3C), C(0x0B0B0C), 90), null, body);
                        var grid = new Pen(B(0x34C759, 40), 0.8);
                        for (int i = 1; i < 6; i++) { dc.DrawLine(grid, new Point(22 + i * 14, 26), new Point(22 + i * 14, 98)); }
                        for (int i = 1; i < 5; i++) { dc.DrawLine(grid, new Point(20, 24 + i * 15), new Point(108, 24 + i * 15)); }
                        var pulse = new Pen(B(0x34C759), 4.2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        dc.DrawGeometry(null, pulse, Geometry.Parse("M22,66 L44,66 L52,48 L62,88 L72,36 L80,66 L106,66"));
                        break;
                    }
                    case "settings":
                    {
                        dc.DrawGeometry(new LinearGradientBrush(C(0xE5E5EA), C(0x8E8E93), 90), new Pen(B(0x000000, 22), 0.8), body);
                        dc.DrawGeometry(new LinearGradientBrush(C(0x6E6E73), C(0x2C2C2E), 90), null, Gear(64, 61.5, 38, 29, 12));
                        dc.DrawEllipse(new LinearGradientBrush(C(0xD1D1D6), C(0xF2F2F7), 90), new Pen(B(0x1C1C1E, 120), 1.2), new Point(64, 61.5), 15, 15);
                        dc.DrawEllipse(B(0x3A3A3C), null, new Point(64, 61.5), 6, 6);
                        break;
                    }
                    case "downloads":
                    {
                        Folder(dc, new Rect(14, 24, 100, 80), C(0x5DB4F6), C(0x8FCFFB), 255);
                        var arrow = new Pen(B(0x1F7FD8, 230), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                        dc.DrawLine(arrow, new Point(64, 50), new Point(64, 82));
                        dc.DrawGeometry(null, arrow, Geometry.Parse("M50,70 L64,84 L78,70"));
                        break;
                    }
                    case "trash":
                    case "trash-full":
                    {
                        // a translucent bin with ribs, its lid on top
                        var bin = Geometry.Parse("M30,36 L98,36 L91,112 C90.6,116 87.6,118 84,118 L44,118 C40.4,118 37.4,116 37,112 Z");
                        dc.DrawGeometry(new LinearGradientBrush(C(0xF4F7FA, 235), C(0xC9D2DB, 235), 90), new Pen(B(0x6E7C8A, 170), 1.2), bin);
                        if (kind == "trash-full")
                        {
                            dc.PushClip(bin);
                            dc.DrawGeometry(B(0xFFFFFF), new Pen(B(0x9AA6B2), 1), Geometry.Parse("M34,52 L58,40 L72,60 L50,74 Z"));
                            dc.DrawGeometry(B(0xF2F2F2), new Pen(B(0x9AA6B2), 1), Geometry.Parse("M58,50 L92,44 L94,70 L62,72 Z"));
                            dc.Pop();
                        }
                        var rib = new Pen(B(0x8796A5, 150), 1.6);
                        foreach (var x in new[] { 46.0, 56, 64, 72, 82 }) dc.DrawLine(rib, new Point(x + (x - 64) * 0.02, 44), new Point(x - (x - 64) * 0.08, 110));
                        dc.DrawRoundedRectangle(new LinearGradientBrush(C(0xFDFEFF), C(0xC4CDD6), 90), new Pen(B(0x6E7C8A, 170), 1.2), new Rect(26, 26, 76, 11), 5, 5);
                        break;
                    }
                    default:
                        dc.DrawGeometry(new LinearGradientBrush(C(0xFFFFFF), C(0xE6E6EB), 90), new Pen(B(0x000000, 22), 0.8), body);
                        dc.DrawRoundedRectangle(new LinearGradientBrush(C(0xC7C7CC), C(0x8E8E93), 90), null, new Rect(40, 38, 48, 46), 9, 9);
                        break;
                }
            }, shadowBlur: kind == "trash" || kind == "trash-full" || kind == "downloads" ? 3 : 5);
            return own[kind] = img;
        }

        /// <summary>The macOS folder: a lighter front panel over the back panel with its tab.</summary>
        public static void Folder(DrawingContext dc, Rect r, Color back, Color front, byte alpha)
        {
            double w = r.Width, h = r.Height, x = r.X, y = r.Y, rad = w * 0.07;
            var tab = new StreamGeometry();
            using (var c = tab.Open())
            {
                c.BeginFigure(new Point(x, y + rad), true, true);
                c.ArcTo(new Point(x + rad, y), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(new Point(x + w * 0.34, y), true, true);
                c.LineTo(new Point(x + w * 0.42, y + h * 0.11), true, true);
                c.LineTo(new Point(x + w - rad, y + h * 0.11), true, true);
                c.ArcTo(new Point(x + w, y + h * 0.11 + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(new Point(x + w, y + h - rad), true, true);
                c.ArcTo(new Point(x + w - rad, y + h), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(new Point(x + rad, y + h), true, true);
                c.ArcTo(new Point(x, y + h - rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
            }
            tab.Freeze();
            dc.DrawGeometry(B(A(Core.Applier.Mix(back, Colors.Black, 0.08), alpha)), null, tab);
            var frontRect = new Rect(x, y + h * 0.2, w, h * 0.8);
            dc.DrawRoundedRectangle(new LinearGradientBrush(A(front, alpha), A(back, alpha), 90), null, frontRect, rad, rad);
            dc.DrawRectangle(B(0xFFFFFF, (byte)(alpha * 0.35)), null, new Rect(x + rad, y + h * 0.2 + 1, w - 2 * rad, 1.2));
        }

        static ImageSource folderIcon;
        /// <summary>The blue macOS folder for the Finder.</summary>
        public static ImageSource FolderIcon => folderIcon ?? (folderIcon = Render(128, dc => Folder(dc, new Rect(10, 22, 108, 86), C(0x4AA8F2), C(0x8ACCFA), 255), 3));

        static Geometry Gear(double cx, double cy, double outer, double inner, int teeth)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                int n = teeth * 4;
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n - Math.PI / n;
                    double r = (i % 4 == 1 || i % 4 == 2) ? outer : inner;
                    var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
                    if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        }

        // ---------------- symbols (drawn like SF Symbols, on a 24 x 24 grid) ----------------
        /// <summary>A line symbol. level: wifi 0..3, speaker 0..3, battery 0..100 (-1 = not used).</summary>
        public static FrameworkElement Sym(string name, double size, Color c, double level = -1, double weight = 1.0)
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            double sw = 1.75 * weight;
            Brush br = B(c), dim = B(A(c, (byte)(c.A * 0.3)));
            void S(string data, double w = 0, Brush b = null) => canvas.Children.Add(new Path { Data = Geometry.Parse(data), Stroke = b ?? br, StrokeThickness = w > 0 ? w : sw, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            void F(string data, Brush b = null) => canvas.Children.Add(new Path { Data = Geometry.Parse(data), Fill = b ?? br });
            void G(Geometry geo, Brush fill, Brush stroke = null, double w = 0) => canvas.Children.Add(new Path { Data = geo, Fill = fill, Stroke = stroke, StrokeThickness = w });
            switch (name)
            {
                case "wifi":
                case "wifi.slash":
                {
                    // the fan: three bands and a wedge, lit up to the signal level
                    int lv = level < 0 ? 3 : (int)level;
                    G(Sector(12, 19.5, 0, 3.6), lv >= 0 && name == "wifi" ? br : dim);
                    G(Sector(12, 19.5, 5.4, 9.0), lv >= 1 && name == "wifi" ? br : dim);
                    G(Sector(12, 19.5, 10.8, 14.4), lv >= 2 && name == "wifi" ? br : dim);
                    if (name == "wifi.slash") S("M4,4 L20,20", 1.8);
                    break;
                }
                case "battery":
                {
                    S("M3.2,7.2 L18.4,7.2 Q20.4,7.2 20.4,9.2 L20.4,14.8 Q20.4,16.8 18.4,16.8 L3.2,16.8 Q1.2,16.8 1.2,14.8 L1.2,9.2 Q1.2,7.2 3.2,7.2 Z", 1.1, B(A(c, (byte)(c.A * 0.55))));
                    F("M21.6,10.2 Q22.8,10.6 22.8,12 Q22.8,13.4 21.6,13.8 Z", B(A(c, (byte)(c.A * 0.55))));
                    double lv = Math.Max(0, Math.Min(100, level < 0 ? 100 : level));
                    if (lv > 0) canvas.Children.Add(new Rectangle { Width = Math.Max(1.6, 15.6 * lv / 100), Height = 7.2, RadiusX = 1.3, RadiusY = 1.3, Fill = lv <= 10 ? B(Red) : br, Margin = new Thickness(3, 8.4, 0, 0) });
                    break;
                }
                case "bolt": F("M13.4,2.5 L5.5,13.4 L11.2,13.4 L9.8,21.5 L18.5,9.9 L12.6,9.9 Z"); break;
                case "magnifyingglass": S("M10.2,3.6 A6.6,6.6 0 1 1 10.19,3.6 Z", 2.0); S("M15.1,15.1 L20.4,20.4", 2.4); break;
                case "controlcenter":
                    S("M7,4.6 L17,4.6 A3.4,3.4 0 0 1 17,11.4 L7,11.4 A3.4,3.4 0 0 1 7,4.6 Z", 1.6);
                    F("M7,5.6 A2.4,2.4 0 1 1 6.99,5.6 Z");
                    F("M7,12.6 L17,12.6 A3.4,3.4 0 0 1 17,19.4 L7,19.4 A3.4,3.4 0 0 1 7,12.6 Z");
                    F("M17,13.8 A2.2,2.2 0 1 1 16.99,13.8 Z", B(C(0xFFFFFF, 235)));
                    break;
                case "bluetooth": S("M7,7.6 L16.4,15.8 L11.8,19.8 L11.8,4.2 L16.4,8.2 L7,16.4", 1.7); break;
                case "moon": G(new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(11.6, 12.4), 8.2, 8.2), new EllipseGeometry(new Point(16.6, 8.6), 7.0, 7.0)), br); break;
                case "sun":
                    G(new EllipseGeometry(new Point(12, 12), 4.1, 4.1), br);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; S(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0},{1} L{2},{3}", 12 + 6.6 * Math.Cos(a), 12 + 6.6 * Math.Sin(a), 12 + 8.6 * Math.Cos(a), 12 + 8.6 * Math.Sin(a)), 1.8); }
                    break;
                case "speaker":
                {
                    F("M3,9.2 L6.6,9.2 L11.4,5 Q12.4,4.3 12.4,5.6 L12.4,18.4 Q12.4,19.7 11.4,19 L6.6,14.8 L3,14.8 Q2.2,14.8 2.2,14 L2.2,10 Q2.2,9.2 3,9.2 Z");
                    int lv = level < 0 ? 3 : (int)level;
                    if (lv == 0) { S("M15.4,9.4 L20.6,14.6 M20.6,9.4 L15.4,14.6", 1.7); break; }
                    S("M15.2,9.4 A3.6,3.6 0 0 1 15.2,14.6", 1.7, lv >= 1 ? br : dim);
                    S("M17.6,7 A7,7 0 0 1 17.6,17", 1.7, lv >= 2 ? br : dim);
                    S("M20,4.6 A10.4,10.4 0 0 1 20,19.4", 1.7, lv >= 3 ? br : dim);
                    break;
                }
                case "lock":
                    S("M8,10.6 L8,7.8 A4,4 0 0 1 16,7.8 L16,10.6", 1.9);
                    F("M6.4,10.4 L17.6,10.4 Q19.2,10.4 19.2,12 L19.2,19.2 Q19.2,20.8 17.6,20.8 L6.4,20.8 Q4.8,20.8 4.8,19.2 L4.8,12 Q4.8,10.4 6.4,10.4 Z");
                    break;
                case "lock.small":
                    S("M9,11 L9,8.6 A3,3 0 0 1 15,8.6 L15,11", 1.6);
                    F("M7.8,10.8 L16.2,10.8 Q17.2,10.8 17.2,11.8 L17.2,17 Q17.2,18 16.2,18 L7.8,18 Q6.8,18 6.8,17 L6.8,11.8 Q6.8,10.8 7.8,10.8 Z");
                    break;
                case "camera": S("M3,8 L3,5 Q3,3 5,3 L8,3 M16,3 L19,3 Q21,3 21,5 L21,8 M21,16 L21,19 Q21,21 19,21 L16,21 M8,21 L5,21 Q3,21 3,19 L3,16", 1.7); G(new EllipseGeometry(new Point(12, 12), 3.4, 3.4), br); break;
                case "chevron.right": S("M9.4,5.6 L15.6,12 L9.4,18.4", 2.0); break;
                case "chevron.left": S("M14.6,5.6 L8.4,12 L14.6,18.4", 2.0); break;
                case "chevron.down": S("M5.6,9.4 L12,15.6 L18.4,9.4", 2.0); break;
                case "chevron.up": S("M5.6,14.6 L12,8.4 L18.4,14.6", 2.0); break;
                case "checkmark": S("M5,12.6 L9.8,17.4 L19.2,6.8", 2.2); break;
                case "xmark": S("M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5", 2.0); break;
                case "xmark.circle": S("M12,2.8 A9.2,9.2 0 1 1 11.99,2.8 Z", 1.6); S("M8.8,8.8 L15.2,15.2 M15.2,8.8 L8.8,15.2", 1.7); break;
                case "info.circle": S("M12,2.8 A9.2,9.2 0 1 1 11.99,2.8 Z", 1.6); S("M12,11 L12,16.6", 1.9); F("M12,6.6 A1.2,1.2 0 1 1 11.99,6.6 Z"); break;
                case "ellipsis.circle": S("M12,2.8 A9.2,9.2 0 1 1 11.99,2.8 Z", 1.6); foreach (var x in new[] { 7.6, 12, 16.4 }) G(new EllipseGeometry(new Point(x, 12), 1.3, 1.3), br); break;
                case "plus": S("M12,5 L12,19 M5,12 L19,12", 2.0); break;
                case "power": S("M7.4,6.8 A7.4,7.4 0 1 0 16.6,6.8", 1.9); S("M12,3.4 L12,11.4", 1.9); break;
                case "person": G(new EllipseGeometry(new Point(12, 8.2), 3.9, 3.9), br); F("M4.6,20 Q4.6,13.6 12,13.6 Q19.4,13.6 19.4,20 Z"); break;
                case "list": foreach (var y in new[] { 6.5, 12, 17.5 }) { G(new EllipseGeometry(new Point(4.8, y), 1.3, 1.3), br); S(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M9,{0} L20,{0}", y), 1.7); } break;
                case "grid": foreach (var (x, y) in new[] { (4.0, 4.0), (13.4, 4.0), (4.0, 13.4), (13.4, 13.4) }) canvas.Children.Add(new Rectangle { Width = 6.6, Height = 6.6, RadiusX = 1.6, RadiusY = 1.6, Stroke = br, StrokeThickness = 1.6, Margin = new Thickness(x, y, 0, 0) }); break;
                case "clock": S("M12,2.8 A9.2,9.2 0 1 1 11.99,2.8 Z", 1.6); S("M12,6.8 L12,12 L15.6,14.2", 1.7); break;
                case "desktop": S("M3.4,4.6 L20.6,4.6 Q21.4,4.6 21.4,5.4 L21.4,16.4 Q21.4,17.2 20.6,17.2 L3.4,17.2 Q2.6,17.2 2.6,16.4 L2.6,5.4 Q2.6,4.6 3.4,4.6 Z M8,20.4 L16,20.4", 1.6); break;
                case "doc": S("M6.2,2.8 L13.8,2.8 L18.6,7.6 L18.6,20 Q18.6,21.2 17.4,21.2 L6.2,21.2 Q5,21.2 5,20 L5,4 Q5,2.8 6.2,2.8 Z M13.6,3 L13.6,7.8 L18.4,7.8", 1.5); break;
                case "arrow.down.circle": S("M12,2.8 A9.2,9.2 0 1 1 11.99,2.8 Z", 1.6); S("M12,7.2 L12,16.2 M8.4,12.8 L12,16.4 L15.6,12.8", 1.8); break;
                case "house": S("M3.6,11 L12,3.8 L20.4,11 M6,9.4 L6,19.8 Q6,20.6 6.8,20.6 L10,20.6 L10,15 L14,15 L14,20.6 L17.2,20.6 Q18,20.6 18,19.8 L18,9.4", 1.6); break;
                case "photo": S("M4,4.4 L20,4.4 Q21.2,4.4 21.2,5.6 L21.2,18.4 Q21.2,19.6 20,19.6 L4,19.6 Q2.8,19.6 2.8,18.4 L2.8,5.6 Q2.8,4.4 4,4.4 Z M3.2,17 L9,11.2 L13.4,15.6 L16,13 L21,18", 1.5); G(new EllipseGeometry(new Point(15.8, 8.8), 1.6, 1.6), br); break;
                case "music": S("M9,18 L9,5.6 L19,3.6 L19,15.8", 1.7); G(new EllipseGeometry(new Point(6.6, 18.2), 2.6, 2.2), br); G(new EllipseGeometry(new Point(16.6, 16), 2.6, 2.2), br); break;
                case "film": S("M4,3.6 L20,3.6 Q20.8,3.6 20.8,4.4 L20.8,19.6 Q20.8,20.4 20,20.4 L4,20.4 Q3.2,20.4 3.2,19.6 L3.2,4.4 Q3.2,3.6 4,3.6 Z M7.4,3.8 L7.4,20.2 M16.6,3.8 L16.6,20.2 M3.4,8 L7.2,8 M3.4,12 L7.2,12 M3.4,16 L7.2,16 M16.8,8 L20.6,8 M16.8,12 L20.6,12 M16.8,16 L20.6,16", 1.4); break;
                case "laptop": S("M5,5.2 L19,5.2 Q20.2,5.2 20.2,6.4 L20.2,15.4 L3.8,15.4 L3.8,6.4 Q3.8,5.2 5,5.2 Z M1.8,17.4 L22.2,17.4 Q21.8,19.2 20,19.2 L4,19.2 Q2.2,19.2 1.8,17.4 Z", 1.5); break;
                case "drive": S("M3.4,8.2 L20.6,8.2 Q21.4,8.2 21.4,9 L21.4,15.6 Q21.4,16.4 20.6,16.4 L3.4,16.4 Q2.6,16.4 2.6,15.6 L2.6,9 Q2.6,8.2 3.4,8.2 Z", 1.6); G(new EllipseGeometry(new Point(17.4, 12.3), 1.2, 1.2), br); break;
                case "eject": F("M12,4.4 L19.6,13.2 L4.4,13.2 Z"); canvas.Children.Add(new Rectangle { Width = 15.2, Height = 2.6, RadiusX = 1, RadiusY = 1, Fill = br, Margin = new Thickness(4.4, 16, 0, 0) }); break;
                case "trash": S("M4.4,6.4 L19.6,6.4 M9.4,6.2 L9.4,4.6 Q9.4,3.4 10.6,3.4 L13.4,3.4 Q14.6,3.4 14.6,4.6 L14.6,6.2 M6.2,6.6 L7.2,19.4 Q7.3,20.6 8.6,20.6 L15.4,20.6 Q16.7,20.6 16.8,19.4 L17.8,6.6 M10.2,10 L10.4,17 M13.8,10 L13.6,17", 1.5); break;
                case "folder": S("M3,6.2 Q3,4.6 4.6,4.6 L9,4.6 L11,6.6 L19.4,6.6 Q21,6.6 21,8.2 L21,17.8 Q21,19.4 19.4,19.4 L4.6,19.4 Q3,19.4 3,17.8 Z M3.2,9.4 L20.8,9.4", 1.5); break;
                case "app": foreach (var (x, y) in new[] { (4.0, 4.0), (13.4, 4.0), (4.0, 13.4), (13.4, 13.4) }) canvas.Children.Add(new Rectangle { Width = 6.6, Height = 6.6, RadiusX = 2, RadiusY = 2, Fill = br, Margin = new Thickness(x, y, 0, 0) }); break;
                case "sidebar": S("M4,4.4 L20,4.4 Q21.2,4.4 21.2,5.6 L21.2,18.4 Q21.2,19.6 20,19.6 L4,19.6 Q2.8,19.6 2.8,18.4 L2.8,5.6 Q2.8,4.4 4,4.4 Z M9.4,4.6 L9.4,19.4", 1.5); break;
                case "arrow.right": S("M5,12 L19,12 M13.4,6.4 L19,12 L13.4,17.6", 2.0); break;
                case "gauge": S("M4.2,16 A8.4,8.4 0 1 1 19.8,16", 1.8); S("M12,13.4 L15.8,8.4", 2.0); G(new EllipseGeometry(new Point(12, 13.6), 1.7, 1.7), br); break;
                case "bell": S("M6.2,16.6 L6.2,10.8 A5.8,5.8 0 0 1 17.8,10.8 L17.8,16.6 L19.4,18.4 L4.6,18.4 Z M10,20.6 A2,2 0 0 0 14,20.6", 1.6); break;
                case "airplay": S("M6,17 L3.6,17 Q2.6,17 2.6,16 L2.6,4.6 Q2.6,3.6 3.6,3.6 L20.4,3.6 Q21.4,3.6 21.4,4.6 L21.4,16 Q21.4,17 20.4,17 L18,17", 1.5); F("M12,13 L17.4,20.4 L6.6,20.4 Z"); break;
                case "globe": S("M12,2.8 A9.2,9.2 0 1 1 11.99,2.8 Z M12,3 Q6.8,12 12,21 Q17.2,12 12,3 M2.9,12 L21.1,12 M4.4,7.2 Q12,9.2 19.6,7.2 M4.4,16.8 Q12,14.8 19.6,16.8", 1.4); break;
                case "equal": S("M5.6,9.2 L18.4,9.2 M5.6,14.8 L18.4,14.8", 2.2); break;
                case "doc.text": S("M6.2,2.8 L13.8,2.8 L18.6,7.6 L18.6,20 Q18.6,21.2 17.4,21.2 L6.2,21.2 Q5,21.2 5,20 L5,4 Q5,2.8 6.2,2.8 Z M13.6,3 L13.6,7.8 L18.4,7.8 M8.4,12 L15.4,12 M8.4,15 L15.4,15 M8.4,18 L12.6,18", 1.4); break;
                default: G(new EllipseGeometry(new Point(12, 12), 4, 4), br); break;
            }
            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = false };
        }

        /// <summary>A band of the Wi-Fi fan: a 90° ring segment pointing up from (cx, cy).</summary>
        static Geometry Sector(double cx, double cy, double r0, double r1)
        {
            double a0 = -135 * Math.PI / 180, a1 = -45 * Math.PI / 180;
            Point P(double r, double a) => new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                if (r0 <= 0.01)
                {
                    c.BeginFigure(new Point(cx, cy), true, true);
                    c.LineTo(P(r1, a0), true, true);
                    c.ArcTo(P(r1, a1), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true, true);
                }
                else
                {
                    c.BeginFigure(P(r0, a0), true, true);
                    c.LineTo(P(r1, a0), true, true);
                    c.ArcTo(P(r1, a1), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(P(r0, a1), true, true);
                    c.ArcTo(P(r0, a0), new Size(r0, r0), 0, false, SweepDirection.Counterclockwise, true, true);
                }
            }
            g.Freeze();
            return g;
        }

        // ---------------- controls ----------------
        /// <summary>The macOS switch: a capsule that turns blue, the knob slides over with a short spring.</summary>
        public static Border Switch(bool on, Action<bool> changed, double w = 32, double h = 18)
        {
            var knob = new Ellipse { Width = h - 3, Height = h - 3, Fill = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1.5, 0, 0, 0), Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0.8, Direction = 270, Opacity = 0.3 } };
            var move = new TranslateTransform(on ? w - h : 0, 0);
            knob.RenderTransform = move;
            var track = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h / 2), Background = B(on ? Blue : C(0x000000, 30)), Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            bool state = on;
            track.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                state = !state;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(state ? w - h : 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Spring });
                var bg = new SolidColorBrush(((SolidColorBrush)track.Background).Color);
                track.Background = bg;
                bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(state ? Blue : C(0x000000, 30), TimeSpan.FromMilliseconds(180)));
                changed?.Invoke(state);
            };
            return track;
        }

        /// <summary>The Control Center slider: a capsule that fills with white, its symbol inside at the left.</summary>
        public static Grid Slider(double value, Action<double> changed, Func<double, FrameworkElement> symbol, double width = 260, double height = 26)
        {
            var g = new Grid { Width = width, Height = height, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var track = new Border { CornerRadius = new CornerRadius(height / 2), Background = B(0x000000, 26) };
            var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.18 } };
            var host = new Grid { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(height * 0.32, 0, 0, 0) };
            g.Children.Add(track); g.Children.Add(fill); g.Children.Add(host);
            double v = Math.Max(0, Math.Min(1, value));
            void Show()
            {
                fill.Width = height + (width - height) * v;
                host.Children.Clear();
                host.Children.Add(symbol(v));
            }
            Show();
            void At(Point p) { v = Math.Max(0, Math.Min(1, (p.X - height / 2) / (width - height))); Show(); changed?.Invoke(v); }
            g.MouseLeftButtonDown += (s, e) => { g.CaptureMouse(); At(e.GetPosition(g)); e.Handled = true; };
            g.MouseMove += (s, e) => { if (g.IsMouseCaptured) At(e.GetPosition(g)); };
            g.MouseLeftButtonUp += (s, e) => g.ReleaseMouseCapture();
            g.MouseWheel += (s, e) => { v = Math.Max(0, Math.Min(1, v + (e.Delta > 0 ? 0.04 : -0.04))); Show(); changed?.Invoke(v); };
            g.Tag = (Action<double>)(nv => { if (!g.IsMouseCaptured) { v = Math.Max(0, Math.Min(1, nv)); Show(); } });
            return g;
        }

        /// <summary>A capsule push button (Tahoe); the default one is blue with white text.</summary>
        public static Border Button(string text, Action click, bool primary = false, double minWidth = 0)
        {
            var tb = Text(text, 13, primary ? Colors.White : Label, primary ? FontWeights.Medium : FontWeights.Normal);
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border
            {
                Child = tb, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 4, 16, 5), MinWidth = minWidth, Cursor = Cursors.Hand,
                Background = primary ? (Brush)new LinearGradientBrush(C(0x2E8BFF), C(0x0A6FF0), 90) : B(0xFFFFFF, 235),
                BorderBrush = B(0x000000, primary ? (byte)40 : (byte)30), BorderThickness = new Thickness(0.6),
                Effect = new DropShadowEffect { BlurRadius = 2.5, ShadowDepth = 0.6, Direction = 270, Opacity = 0.16 },
            };
            b.MouseLeftButtonDown += (s, e) => { b.Opacity = 0.82; e.Handled = true; };
            b.MouseLeave += (s, e) => b.Opacity = 1;
            b.MouseLeftButtonUp += (s, e) => { b.Opacity = 1; e.Handled = true; click?.Invoke(); };
            return b;
        }

        /// <summary>A rounded text field with the blue focus ring.</summary>
        public static (Border box, TextBox tb) Field(string hint, double width = 0, string symbol = null, double height = 26, double font = 13)
        {
            var tb = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = Font, FontSize = font, Foreground = B(Label), CaretBrush = B(Label), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
            var hintTb = Text(hint, font, Tertiary);
            hintTb.IsHitTestVisible = false; hintTb.Margin = new Thickness(2, 0, 0, 0);
            tb.TextChanged += (s, e) => hintTb.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var g = new Grid();
            g.Children.Add(hintTb); g.Children.Add(tb);
            var dock = new DockPanel();
            if (symbol != null) { var sy = Sym(symbol, font + 1, Secondary); sy.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(sy, Dock.Left); dock.Children.Add(sy); }
            dock.Children.Add(g);
            var box = new Border { Child = dock, CornerRadius = new CornerRadius(height / 2), Background = B(0x000000, 14), Padding = new Thickness(10, 0, 10, 0), Height = height, BorderThickness = new Thickness(0), BorderBrush = B(A(Blue, 0)) };
            if (width > 0) box.Width = width;
            tb.GotKeyboardFocus += (s, e) => { box.BorderThickness = new Thickness(2.5); box.Padding = new Thickness(7.5, 0, 7.5, 0); box.BorderBrush = B(A(Blue, 110)); box.Background = B(0xFFFFFF); };
            tb.LostKeyboardFocus += (s, e) => { box.BorderThickness = new Thickness(0); box.Padding = new Thickness(10, 0, 10, 0); box.Background = B(0x000000, 14); };
            return (box, tb);
        }

        /// <summary>The traffic lights of an own window: close, minimise, zoom. Symbols show while the pointer is over them.</summary>
        public static StackPanel TrafficLights(Window w, double d = 13)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            var lights = new List<(Ellipse e, Color c)>();
            var glyphs = new List<FrameworkElement>();
            void Light(Color c, string glyph, Action a)
            {
                var g = new Grid { Width = d, Height = d, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Arrow };
                var e = new Ellipse { Fill = B(c), Stroke = B(Core.Applier.Mix(c, Colors.Black, 0.18)), StrokeThickness = 0.6 };
                g.Children.Add(e);
                var path = new Path { Data = Geometry.Parse(glyph), Stroke = B(0x000000, 150), StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Fill = glyph.Contains("Z") ? B(0x000000, 150) : null, Stretch = Stretch.None, Visibility = Visibility.Hidden, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(path);
                g.MouseLeftButtonUp += (s, e2) => { e2.Handled = true; a(); };
                g.MouseLeftButtonDown += (s, e2) => e2.Handled = true;
                System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(g, true);
                lights.Add((e, c)); glyphs.Add(path);
                sp.Children.Add(g);
            }
            Light(CloseRed, "M0,0 L6,6 M6,0 L0,6", w.Close);
            Light(MinYellow, "M0,0 L7,0", () => w.WindowState = WindowState.Minimized);
            Light(ZoomGreen, "M0,0 L4.2,0 L0,4.2 Z M6.4,6.4 L2.2,6.4 L6.4,2.2 Z", () => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            sp.MouseEnter += (s, e) => { foreach (var g in glyphs) g.Visibility = Visibility.Visible; Paint(true); };
            sp.MouseLeave += (s, e) => { foreach (var g in glyphs) g.Visibility = Visibility.Hidden; Paint(w.IsActive); };
            void Paint(bool active) { foreach (var (e, c) in lights) { e.Fill = B(active ? c : C(0xD0D0D3)); e.Stroke = B(active ? Core.Applier.Mix(c, Colors.Black, 0.18) : C(0xBDBDC1)); } }
            w.Activated += (s, e) => Paint(true);
            w.Deactivated += (s, e) => { if (!sp.IsMouseOver) Paint(false); };
            return sp;
        }

        /// <summary>A short "no" shake, as macOS shakes a field or a window after a wrong password.</summary>
        public static void Shake(UIElement el, double amp = 10)
        {
            var tt = el.RenderTransform as TranslateTransform ?? new TranslateTransform();
            el.RenderTransform = tt;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
            foreach (var (ms, k) in new[] { (0, 0.0), (65, -1.0), (130, 1.0), (195, -1.0), (260, 1.0), (325, -0.7), (390, 0.5), (455, -0.2), (520, 0.0) })
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(k * amp, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            tt.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        /// <summary>Pops a surface in the macOS way: a quick fade with a slight scale from its origin.</summary>
        public static void Pop(FrameworkElement el, Point origin, double from = 0.94, double ms = 200)
        {
            el.RenderTransformOrigin = origin;
            var sc = new ScaleTransform(from, from);
            el.RenderTransform = sc;
            var a = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Spring };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * 0.6)));
        }

        /// <summary>Tool windows of the look never show in Alt+Tab or the dock.</summary>
        public static void ToolWindow(Window w)
        {
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW);
            };
        }

        /// <summary>The German short date of the menu bar ("Fr. 21. Nov. 20:19").</summary>
        public static string MenuClock(DateTime d)
        {
            if (!Core.Loc.De) return d.ToString("ddd MMM d  h:mm tt", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
            string[] days = { "So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa." };
            string[] months = { "Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez." };
            return days[(int)d.DayOfWeek] + " " + d.Day + ". " + months[d.Month - 1] + " " + d.ToString("HH:mm");
        }
    }

    /// <summary>
    /// A macOS menu: the light menu material, 22 px rows, the blue highlight with white text, shortcuts at the
    /// right, check marks, separators and section titles. Keyboard: arrows, Return, Escape, and left/right to
    /// the neighbouring menu of the menu bar.
    /// </summary>
    internal sealed class MacMenu : Window
    {
        public sealed class Item
        {
            public string Text, Shortcut; public Action Run; public bool Enabled = true, Checked, Header; public ImageSource Icon;
            public static Item Sep => new Item { Text = "-" };
            public static Item Title(string t) => new Item { Text = t, Header = true, Enabled = false };
        }

        static MacMenu open;
        public static MacMenu Current => open;
        public static DateTime ClosedAt;
        public static string ClosedKey;
        public string MenuKey;
        public Action<int> Neighbour;   // -1 left, +1 right (menu bar)
        readonly List<(Border row, Item item, TextBlock text, TextBlock shortcut, FrameworkElement check)> rows = new List<(Border, Item, TextBlock, TextBlock, FrameworkElement)>();
        int hot = -1;
        bool closing;
        public event Action Closing2;

        public static MacMenu Show(IList<Item> items, Point screenDip, string key = null, bool alignRight = false, double minWidth = 0)
        {
            open?.Dismiss(true);
            var m = new MacMenu(items, minWidth) { MenuKey = key };
            open = m;
            m.Left = -20000; m.Top = -20000;
            m.Show();
            m.UpdateLayout();
            var mi = W32.Monitor(W32.MonitorFromPoint(new W32.POINT { X = (int)(screenDip.X * Scale()), Y = (int)(screenDip.Y * Scale()) }, 2), out double s);
            double wr = mi.rcWork.Right / s, wb = mi.rcWork.Bottom / s, wl = mi.rcWork.Left / s, wt = mi.rcMonitor.Top / s;
            double x = alignRight ? screenDip.X - m.ActualWidth + 6 : screenDip.X - 6;
            double y = screenDip.Y - 4;
            m.Left = Math.Max(wl, Math.Min(x, wr - m.ActualWidth));
            m.Top = Math.Max(wt, Math.Min(y, wb - m.ActualHeight));
            m.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(m).Handle);
            return m;
        }

        static double Scale() { W32.PrimaryMonitor(out double s); return s; }

        /// <summary>A context menu at the pointer.</summary>
        public static MacMenu AtCursor(IList<Item> items)
        {
            W32.GetCursorPos(out var p);
            double s = Scale();
            return Show(items, new Point(p.X / s + 4, p.Y / s + 4));
        }

        MacMenu(IList<Item> items, double minWidth)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            MacStyle.Prepare(this);
            MacStyle.ToolWindow(this);
            RenderGuard.Watch(this);
            bool anyCheck = items.Any(i => i.Checked);
            var sp = new StackPanel { Margin = new Thickness(5), MinWidth = Math.Max(minWidth, 180) };
            foreach (var it in items)
            {
                if (it.Text == "-") { sp.Children.Add(new Border { Height = 1, Background = B(MacStyle.Line), Margin = new Thickness(9, 5, 9, 5) }); continue; }
                if (it.Header)
                {
                    var h = MacStyle.Text(it.Text, 11.5, MacStyle.Secondary, FontWeights.SemiBold);
                    h.Margin = new Thickness(10, 4, 10, 2);
                    sp.Children.Add(h);
                    continue;
                }
                var dock = new DockPanel { Height = 22, LastChildFill = true };
                FrameworkElement check = null;
                if (anyCheck)
                {
                    check = MacStyle.Sym("checkmark", 12, MacStyle.Label);
                    ((FrameworkElement)check).Width = 12; check.Margin = new Thickness(0, 0, 5, 0);
                    check.Visibility = it.Checked ? Visibility.Visible : Visibility.Hidden;
                    DockPanel.SetDock(check, Dock.Left);
                    dock.Children.Add(check);
                }
                TextBlock sc = null;
                if (!string.IsNullOrEmpty(it.Shortcut))
                {
                    sc = MacStyle.Text(it.Shortcut, 13, it.Enabled ? MacStyle.Secondary : MacStyle.Tertiary);
                    sc.Margin = new Thickness(28, 0, 0, 0);
                    DockPanel.SetDock(sc, Dock.Right);
                    dock.Children.Add(sc);
                }
                if (it.Icon != null) { var im = Img(it.Icon, 16); im.Margin = new Thickness(0, 0, 7, 0); DockPanel.SetDock(im, Dock.Left); dock.Children.Add(im); }
                var tb = MacStyle.Text(it.Text, 13, it.Enabled ? MacStyle.Label : MacStyle.Tertiary);
                dock.Children.Add(tb);
                var row = new Border { Child = dock, CornerRadius = new CornerRadius(6), Padding = new Thickness(anyCheck ? 6 : 10, 0, 10, 0), Background = Brushes.Transparent };
                int index = rows.Count;
                rows.Add((row, it, tb, sc, check));
                if (it.Enabled)
                {
                    row.MouseEnter += (s, e) => Hot(index);
                    row.MouseLeave += (s, e) => { if (hot == index) Hot(-1); };
                    row.MouseLeftButtonUp += (s, e) => { e.Handled = true; Choose(index); };
                }
                sp.Children.Add(row);
            }
            var pane = MacStyle.Glass(sp, 11, MacStyle.MenuTint, behind: true);
            Content = new Border { Child = pane, Padding = new Thickness(14, 8, 14, 22), Background = Brushes.Transparent };
            Deactivated += (s, e) => Dismiss(false);
            PreviewKeyDown += OnKey;
            Loaded += (s, e) => ((FrameworkElement)Content).BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
        }

        void Hot(int i)
        {
            if (hot >= 0 && hot < rows.Count) { var r = rows[hot]; r.row.Background = Brushes.Transparent; r.text.Foreground = B(MacStyle.Label); if (r.shortcut != null) r.shortcut.Foreground = B(MacStyle.Secondary); Recolor(r.check, MacStyle.Label); }
            hot = i;
            if (hot >= 0 && hot < rows.Count) { var r = rows[hot]; r.row.Background = B(MacStyle.Blue); r.text.Foreground = Brushes.White; if (r.shortcut != null) r.shortcut.Foreground = B(0xFFFFFF, 220); Recolor(r.check, Colors.White); }
        }

        static void Recolor(FrameworkElement sym, Color c)
        {
            if (!(sym is Viewbox vb) || !(vb.Child is Canvas cv)) return;
            foreach (var p in cv.Children.OfType<Path>()) { if (p.Stroke != null) p.Stroke = B(c); if (p.Fill != null) p.Fill = B(c); }
        }

        void Choose(int i)
        {
            var it = rows[i].item;
            // the chosen row blinks once, as on the Mac, then the menu fades
            var row = rows[i].row;
            var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            int n = 0;
            blink.Tick += (s, e) =>
            {
                n++;
                if (n == 1) { row.Background = Brushes.Transparent; rows[i].text.Foreground = B(MacStyle.Label); return; }
                blink.Stop();
                Dismiss(false);
                try { it.Run?.Invoke(); } catch { }
            };
            blink.Start();
        }

        void OnKey(object s, KeyEventArgs e)
        {
            int Next(int from, int dir)
            {
                for (int k = 1; k <= rows.Count; k++) { int j = ((from + dir * k) % rows.Count + rows.Count) % rows.Count; if (rows[j].item.Enabled) return j; }
                return -1;
            }
            switch (e.Key)
            {
                case Key.Escape: Dismiss(false); break;
                case Key.Down: Hot(Next(hot < 0 ? -1 : hot, 1)); break;
                case Key.Up: Hot(Next(hot < 0 ? rows.Count : hot, -1)); break;
                case Key.Enter: case Key.Space: if (hot >= 0) Choose(hot); break;
                case Key.Left: if (Neighbour != null) { var n = Neighbour; Dismiss(true); n(-1); } break;
                case Key.Right: if (Neighbour != null) { var n = Neighbour; Dismiss(true); n(1); } break;
                default: return;
            }
            e.Handled = true;
        }

        /// <summary>Closes the menu; instant when another menu takes its place.</summary>
        public void Dismiss(bool instant)
        {
            if (closing) return;
            closing = true;
            if (open == this) { open = null; ClosedAt = DateTime.Now; ClosedKey = MenuKey; }
            Closing2?.Invoke();
            if (instant) { try { Close(); } catch { } return; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) => { try { Close(); } catch { } };
            ((FrameworkElement)Content).BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>
    /// A macOS alert (Big Sur and later): centred, the app icon on top, bold title, small text, optional field,
    /// buttons side by side with the default one in blue. Return and Escape work.
    /// </summary>
    internal sealed class MacAlert : Window
    {
        public PasswordBox Password;
        public TextBox Input;

        public static MacAlert Ask(string title, string text, ImageSource icon, (string text, Action run, bool primary)[] buttons, bool password = false, string input = null)
        {
            var a = new MacAlert(title, text, icon, buttons, password, input);
            a.Show();
            a.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(a).Handle);
            return a;
        }

        MacAlert(string title, string text, ImageSource icon, (string text, Action run, bool primary)[] buttons, bool password, string input)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MacStyle.Prepare(this);
            RenderGuard.Watch(this);
            var sp = new StackPanel { Width = 260, Margin = new Thickness(16, 20, 16, 16) };
            if (icon != null) { var im = Img(icon, 64); im.HorizontalAlignment = HorizontalAlignment.Center; im.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(im); }
            var t = MacStyle.Text(title, 13, MacStyle.Label, FontWeights.Bold);
            t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            if (!string.IsNullOrEmpty(text))
            {
                var d = MacStyle.Text(text, 11.5, MacStyle.Label);
                d.TextWrapping = TextWrapping.Wrap; d.TextAlignment = TextAlignment.Center; d.HorizontalAlignment = HorizontalAlignment.Center; d.Margin = new Thickness(0, 6, 0, 0); d.LineHeight = 15;
                sp.Children.Add(d);
            }
            Action primaryRun = null;
            if (password || input != null)
            {
                FrameworkElement field;
                if (password)
                {
                    Password = new PasswordBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
                    field = Password;
                }
                else
                {
                    Input = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13, FontFamily = MacStyle.Font, VerticalContentAlignment = VerticalAlignment.Center, Text = input, Padding = new Thickness(0) };
                    field = Input;
                }
                var box = new Border { Child = field, Height = 26, CornerRadius = new CornerRadius(7), Background = B(0xFFFFFF), BorderBrush = B(A(MacStyle.Blue, 120)), BorderThickness = new Thickness(2.5), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 14, 0, 0) };
                sp.Children.Add(box);
                Loaded += (s, e) => { if (Password != null) Password.Focus(); else { Input.Focus(); var dot = Input.Text.LastIndexOf('.'); Input.Select(0, dot > 0 ? dot : Input.Text.Length); } };
            }
            var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 16, 0, 0) };
            Action cancel = null;
            foreach (var (bt, run, primary) in buttons)
            {
                var r = run;
                var b = MacStyle.Button(bt, () => { Close(); r?.Invoke(); }, primary);
                b.Margin = new Thickness(4, 0, 4, 0);
                row.Children.Add(b);
                if (primary) primaryRun = () => { Close(); r?.Invoke(); };
                else if (cancel == null) cancel = () => { Close(); r?.Invoke(); };
            }
            sp.Children.Add(row);
            Content = new Border { Child = MacStyle.Glass(sp, 18, C(0xF4F4F6, 238), behind: true), Padding = new Thickness(30, 20, 30, 40), Background = Brushes.Transparent };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && primaryRun != null) { e.Handled = true; primaryRun(); }
                else if (e.Key == Key.Escape) { e.Handled = true; if (cancel != null) cancel(); else Close(); }
            };
            Loaded += (s, e) => MacStyle.Pop((FrameworkElement)Content, new Point(0.5, 0.45), 0.9, 220);
        }

        public void ShakeIt() => MacStyle.Shake((UIElement)Content, 11);
    }
}

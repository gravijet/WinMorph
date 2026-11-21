using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Core
{
    /// <summary>Draws every wallpaper procedurally, so no copyrighted images are shipped.</summary>
    public static class WallpaperArt
    {
        static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        static SolidColorBrush S(uint rgb, byte a = 255) { var b = new SolidColorBrush(C(rgb, a)); b.Freeze(); return b; }

        static LinearGradientBrush Lin(double x1, double y1, double x2, double y2, params (double o, Color c)[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2) };
            foreach (var s in stops) b.GradientStops.Add(new GradientStop(s.c, s.o));
            b.Freeze();
            return b;
        }

        static RadialGradientBrush Glow(Color c, double alpha = 1)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(110 * alpha), c.R, c.G, c.B), 0.45));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }

        static void Blob(DrawingContext dc, Color c, double x, double y, double r, double alpha = 1)
            => dc.DrawEllipse(Glow(c, alpha), null, new Point(x, y), r, r);

        public static void Draw(DrawingContext dc, double W, double H, Preset p)
        {
            var full = new Rect(0, 0, W, H);
            double s = H / 1080.0;
            var rnd = new Random(p.Id.GetHashCode() & 0x7fffffff);
            dc.PushClip(new RectangleGeometry(full));

            switch (p.Wall)
            {
                case WallKind.Aero:
                {
                    // Aero "harmony": deep blue sky, a bright burst and sweeping coloured light ribbons
                    dc.DrawRectangle(Lin(0, 0, 0.4, 1, (0, C(0x06306B)), (0.45, C(0x0E62B4)), (1, C(0x0A3D7A))), null, full);
                    var burst = new Point(W * 0.47, H * 0.44);
                    Blob(dc, C(0x9FE3FF), burst.X, burst.Y, H * 0.72, 0.55);
                    Blob(dc, C(0xFFFFFF), burst.X, burst.Y, H * 0.22, 0.75);
                    var ribbons = new (uint c, double y0, double y1, double width)[]
                    {
                        (0x7CE34B, 0.92, 0.30, 0.050), (0xFFC23A, 0.98, 0.42, 0.035), (0x37B7FF, 0.80, 0.18, 0.060),
                        (0xFF6A3D, 1.04, 0.52, 0.022), (0xFFFFFF, 0.86, 0.36, 0.012),
                    };
                    foreach (var r in ribbons)
                    {
                        var g = new StreamGeometry();
                        using (var c = g.Open())
                        {
                            c.BeginFigure(new Point(-W * 0.05, H * r.y0), true, true);
                            c.BezierTo(new Point(W * 0.30, H * (r.y0 - 0.05)), new Point(burst.X - W * 0.08, burst.Y + H * 0.06), burst, true, true);
                            c.BezierTo(new Point(burst.X + W * 0.12, burst.Y - H * 0.08), new Point(W * 0.78, H * r.y1), new Point(W * 1.05, H * (r.y1 - 0.06)), true, true);
                            c.LineTo(new Point(W * 1.05, H * (r.y1 - 0.06 + r.width)), true, true);
                            c.BezierTo(new Point(W * 0.78, H * (r.y1 + r.width)), new Point(burst.X + W * 0.12, burst.Y - H * 0.08 + H * r.width * 0.6), burst, true, true);
                            c.BezierTo(new Point(burst.X - W * 0.08, burst.Y + H * 0.06 + H * r.width), new Point(W * 0.30, H * (r.y0 - 0.05 + r.width)), new Point(-W * 0.05, H * (r.y0 + r.width)), true, true);
                        }
                        dc.DrawGeometry(Lin(0, 0.5, 1, 0.5, (0, C(r.c, 0)), (0.35, C(r.c, 170)), (0.5, C(0xFFFFFF, 210)), (0.7, C(r.c, 150)), (1, C(r.c, 0))), null, g);
                    }
                    for (int i = 0; i < 30; i++)
                    {
                        double r = H * (0.008 + rnd.NextDouble() * 0.035);
                        dc.DrawEllipse(S(0xFFFFFF, (byte)rnd.Next(14, 46)), new Pen(S(0xFFFFFF, 70), 1 * s), new Point(rnd.NextDouble() * W, rnd.NextDouble() * H), r, r);
                    }
                    break;
                }
                case WallKind.Matrix:
                {
                    // calm hacker desktop in the chosen colour: dark, soft glow, faint dot grid
                    var g = WinMorph.Shell.ShellThemes.HackerColor;
                    dc.DrawRectangle(S(0x08090A), null, full);
                    dc.DrawRectangle(new RadialGradientBrush(Color.FromArgb(40, g.R, g.G, g.B), Color.FromArgb(0, g.R, g.G, g.B)) { Center = new Point(0.78, 0.3), GradientOrigin = new Point(0.78, 0.3), RadiusX = 0.6, RadiusY = 0.8 }, null, full);
                    var dot = new SolidColorBrush(Color.FromArgb(46, g.R, g.G, g.B)); dot.Freeze();
                    double step = Math.Max(24, W / 60);
                    for (double y = 0; y < H; y += step)
                        for (double x = 0; x < W; x += step) dc.DrawRectangle(dot, null, new Rect(x, y, Math.Max(1, s), Math.Max(1, s)));
                    break;
                }
                case WallKind.Zen:
                    PondArt.Draw(dc, W, H);   // "Morgenteich": the koi pond
                    break;
                case WallKind.Mac:
                    MacArt.Draw(dc, W, H);    // sheets of blue glass under a morning sky
                    break;
            }
            dc.Pop();
        }






        static Geometry Wave(double W, double H, double baseY, double amp, double freq, double phase)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, H + 5), true, true);
                for (int i = 0; i <= 512; i++)
                {
                    double x = W * i / 512.0;
                    double y = baseY + Math.Sin(i / 512.0 * Math.PI * 2 * freq + phase) * amp + Math.Sin(i / 512.0 * Math.PI * 5 + phase * 2) * amp * 0.25;
                    ctx.LineTo(new Point(x, y), false, true);
                }
                ctx.LineTo(new Point(W, H + 5), false, false);
            }
            g.Freeze();
            return g;
        }


        // ---------- rendering ----------
        public static BitmapSource Render(Preset p, int w, int h, bool withPreview)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                Draw(dc, w, h, p);
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        public static string RenderToFile(Preset p, string file, int w, int h)
        {
            var bmp = Render(p, w, h, false);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using (var fs = File.Create(file)) enc.Save(fs);
            return file;
        }

        public static string RenderForScreen(Preset p)
        {
            int w = Math.Max(1920, Native.GetSystemMetrics(0));
            int h = Math.Max(1080, Native.GetSystemMetrics(1));
            var c = WinMorph.Shell.ShellThemes.HackerColor;   // the hacker picture follows the chosen colour
            var variant = p.Id == "terminal" ? "-" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2") : p.Id == "macos" ? "-m1" : "";
            var file = Path.Combine(Backup.DataDir, "Wallpapers", p.Id + "-" + w + "x" + h + variant + "-v4.png");
            return File.Exists(file) ? file : RenderToFile(p, file, w, h);
        }
    }
}

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
                case WallKind.Bloom:
                {
                    dc.DrawRectangle(Lin(0, 0, 1, 1, (0, C(0xE3EEFB)), (1, C(0xA7C3EC))), null, full);
                    var center = new Point(W * 0.56, H * 0.62);
                    Blob(dc, C(0x3D7BF0), center.X, center.Y, H * 0.55, 0.35);
                    for (int i = 0; i < 7; i++)
                    {
                        double ang = -150 + i * 22;
                        var petal = Petal(H * 0.62, H * 0.17);
                        dc.PushTransform(new TranslateTransform(center.X, center.Y));
                        dc.PushTransform(new RotateTransform(ang));
                        dc.DrawGeometry(Lin(0, 0.5, 1, 0.5, (0, C(0x0A3FB8, 235)), (0.6, C(0x2E7BF6, 220)), (1, C(0x8DC4FF, 150))), null, petal);
                        dc.Pop(); dc.Pop();
                    }
                    break;
                }
                case WallKind.Beams:
                {
                    dc.DrawRectangle(Lin(0, 0, 1, 1, (0, C(0x00163A)), (1, C(0x000510))), null, full);
                    var origin = new Point(W * 0.08, H * 0.5);
                    for (int i = 0; i < 9; i++)
                    {
                        double y1 = H * (-0.2 + i * 0.17), y2 = y1 + H * 0.09;
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(origin, true, true);
                            ctx.LineTo(new Point(W * 1.05, y1), false, false);
                            ctx.LineTo(new Point(W * 1.05, y2), false, false);
                        }
                        dc.DrawGeometry(Lin(0, 0, 1, 0, (0, C(0x9FD8FF, 0)), (0.5, C(0x3AA0FF, (byte)(40 + i * 8))), (1, C(0x0060D0, 20))), null, g);
                    }
                    Blob(dc, C(0x5CC2FF), W * 0.55, H * 0.5, H * 0.35, 0.45);
                    Blob(dc, C(0xFFFFFF), W * 0.55, H * 0.5, H * 0.06, 0.8);
                    break;
                }
                case WallKind.Aero:
                {
                    dc.DrawRectangle(Lin(0, 0, 1, 1, (0, C(0x0B4F94)), (0.55, C(0x1C8BDB)), (1, C(0x7FD0F7))), null, full);
                    Blob(dc, C(0xFFFFFF), W * 0.5, H * 0.46, H * 0.55, 0.5);
                    var pen = new Pen(S(0xFFFFFF, 60), 6 * s);
                    for (int i = 0; i < 6; i++)
                        dc.DrawEllipse(null, pen, new Point(W * 0.5, H * 1.45), W * (0.45 + i * 0.07), H * (0.95 + i * 0.05));
                    for (int i = 0; i < 26; i++)
                    {
                        double r = H * (0.01 + rnd.NextDouble() * 0.05);
                        dc.DrawEllipse(S(0xFFFFFF, (byte)rnd.Next(18, 55)), new Pen(S(0xFFFFFF, 90), 1.2 * s), new Point(rnd.NextDouble() * W, rnd.NextDouble() * H), r, r);
                    }
                    break;
                }
                case WallKind.Hills:
                {
                    dc.DrawRectangle(Lin(0, 0, 0, 1, (0, C(0x1F5FCC)), (0.65, C(0x7DB8F2)), (1, C(0xC6E2FA))), null, full);
                    for (int i = 0; i < 7; i++)
                    {
                        double cx = rnd.NextDouble() * W, cy = H * (0.08 + rnd.NextDouble() * 0.3);
                        for (int j = 0; j < 14; j++)
                            Blob(dc, C(0xFFFFFF), cx + (rnd.NextDouble() - 0.5) * H * 0.42, cy + (rnd.NextDouble() - 0.5) * H * 0.07, H * (0.07 + rnd.NextDouble() * 0.08), 0.32);
                    }
                    var back = Hill(W, H, 0.70, 0.60, 0.66);
                    dc.DrawGeometry(Lin(0, 0, 0, 1, (0, C(0x4C9A2A)), (1, C(0x24611A))), null, back);
                    var front = Hill(W, H, 0.80, 0.58, 0.86);
                    dc.DrawGeometry(Lin(0, 0, 0.3, 1, (0, C(0x7BD13B)), (0.4, C(0x52AD27)), (1, C(0x2D7A14))), null, front);
                    break;
                }
                case WallKind.Teal:
                    dc.DrawRectangle(S(0x008080), null, full);
                    break;
                case WallKind.Waves:
                case WallKind.WavesDark:
                {
                    bool dark = p.Wall == WallKind.WavesDark;
                    dc.DrawRectangle(dark ? Lin(0, 0, 1, 1, (0, C(0x0B0B2E)), (1, C(0x2A0E3F)))
                                          : Lin(0, 0, 1, 1, (0, C(0xFFC37A)), (0.5, C(0xF26AA0)), (1, C(0x6C4BDB))), null, full);
                    uint[] cols = dark ? new uint[] { 0x3A2A8C, 0x5B2B9E, 0x7A2F9A, 0x2F4FB8, 0x1B1E5C }
                                       : new uint[] { 0xFFB36B, 0xFF7E9E, 0xD65CC5, 0x8D5BF0, 0x4F5FE8 };
                    for (int i = 0; i < cols.Length; i++)
                    {
                        double baseY = H * (0.30 + i * 0.14);
                        var g = Wave(W, H, baseY, H * (0.06 + 0.02 * i), 1.1 + i * 0.25, i * 1.7);
                        var c = C(cols[i]);
                        dc.DrawGeometry(Lin(0, 0, 1, 1, (0, Color.FromArgb(230, c.R, c.G, c.B)), (1, Color.FromArgb(200, (byte)(c.R * 0.7), (byte)(c.G * 0.7), (byte)(c.B * 0.85)))), null, g);
                    }
                    break;
                }
                case WallKind.Jelly:
                {
                    var bg = new RadialGradientBrush { Center = new Point(0.72, 0.32), GradientOrigin = new Point(0.72, 0.32), RadiusX = 0.9, RadiusY = 1.1 };
                    bg.GradientStops.Add(new GradientStop(C(0xE95420), 0));
                    bg.GradientStops.Add(new GradientStop(C(0x9B2C5E), 0.3));
                    bg.GradientStops.Add(new GradientStop(C(0x4A0D3C), 0.65));
                    bg.GradientStops.Add(new GradientStop(C(0x1E0018), 1));
                    dc.DrawRectangle(bg, null, full);
                    for (int i = 0; i < 14; i++)
                    {
                        double r = H * (0.05 + rnd.NextDouble() * 0.18);
                        dc.DrawEllipse(Glow(C(0xFFFFFF), 0.10), new Pen(S(0xFFFFFF, 26), 2 * s), new Point(rnd.NextDouble() * W, rnd.NextDouble() * H), r, r);
                    }
                    break;
                }
                case WallKind.Swirl:
                {
                    dc.DrawRectangle(Lin(0, 0, 1, 1, (0, C(0x0A1A3A)), (1, C(0x1F3D73))), null, full);
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        var c0 = new Point(W * 0.52, H * 0.47);
                        bool first = true;
                        for (double t = 0; t < Math.PI * 4.6; t += 0.03)
                        {
                            double r = H * 0.026 * t * 1.25;
                            var pt = new Point(c0.X + Math.Cos(t) * r, c0.Y + Math.Sin(t) * r * 0.95);
                            if (first) { ctx.BeginFigure(pt, false, false); first = false; } else ctx.LineTo(pt, true, true);
                        }
                    }
                    g.Freeze();
                    for (int i = 5; i >= 1; i--) dc.DrawGeometry(null, new Pen(S(0xD70A53, (byte)(18 * (6 - i))), s * 7 * i) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
                    dc.DrawGeometry(null, new Pen(S(0xFF3D7F), s * 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
                    break;
                }
                case WallKind.Mint:
                {
                    dc.DrawRectangle(S(0x2E4A33), null, full);
                    double cell = H / 7;
                    int cols = (int)(W / cell) + 3, rows = (int)(H / cell) + 4;
                    var pts = new Point[cols, rows];
                    for (int x = 0; x < cols; x++)
                        for (int y = 0; y < rows; y++)
                            pts[x, y] = new Point(x * cell + (rnd.NextDouble() - 0.5) * cell * 0.7, y * cell - cell + (rnd.NextDouble() - 0.5) * cell * 0.7);
                    for (int x = 0; x < cols - 1; x++)
                        for (int y = 0; y < rows - 1; y++)
                        {
                            Tri(dc, pts[x, y], pts[x + 1, y], pts[x, y + 1], Shade(0x2E4A33, 0x6FA33A, rnd.NextDouble() * 0.55 + (double)y / rows * 0.2));
                            Tri(dc, pts[x + 1, y], pts[x + 1, y + 1], pts[x, y + 1], Shade(0x2E4A33, 0x6FA33A, rnd.NextDouble() * 0.55 + (double)y / rows * 0.2));
                        }
                    dc.DrawRectangle(Lin(0, 0, 0, 1, (0, C(0x000000, 0)), (1, C(0x000000, 110))), null, full);
                    break;
                }
                case WallKind.Chrome:
                {
                    dc.DrawRectangle(Lin(0, 0, 1, 1, (0, C(0xA8C7FA)), (0.5, C(0xF6AEA9)), (1, C(0xFDE293))), null, full);
                    Blob(dc, C(0xFFFFFF), W * 0.2, H * 0.8, H * 0.6, 0.55);
                    Blob(dc, C(0x81C995), W * 0.85, H * 0.25, H * 0.45, 0.55);
                    Blob(dc, C(0x8AB4F8), W * 0.6, H * 0.9, H * 0.5, 0.5);
                    break;
                }
                case WallKind.Aurora:
                {
                    dc.DrawRectangle(S(0x05040C), null, full);
                    Blob(dc, C(0x8B5CF6), W * 0.25, H * 0.35, H * 0.75, 0.75);
                    Blob(dc, C(0xEC4899), W * 0.75, H * 0.75, H * 0.7, 0.55);
                    Blob(dc, C(0x22D3EE), W * 0.65, H * 0.15, H * 0.5, 0.45);
                    Blob(dc, C(0x4F46E5), W * 0.05, H * 0.95, H * 0.55, 0.6);
                    var gridPen = new Pen(S(0xFFFFFF, 10), 1 * s);
                    for (double x = 0; x < W; x += H / 18) dc.DrawLine(gridPen, new Point(x, 0), new Point(x, H));
                    for (double y = 0; y < H; y += H / 18) dc.DrawLine(gridPen, new Point(0, y), new Point(W, y));
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
                {
                    dc.DrawRectangle(Lin(0, 0, 0, 1, (0, C(0xF5EFE4)), (1, C(0xE2D4BD))), null, full);
                    dc.DrawEllipse(S(0xD99A6C, 220), null, new Point(W * 0.68, H * 0.36), H * 0.09, H * 0.09);
                    uint[] m = { 0xD8C6AA, 0xC2AB8C, 0xA88F72, 0x8C755D };
                    for (int i = 0; i < m.Length; i++)
                        dc.DrawGeometry(S(m[i], 235), null, Wave(W, H, H * (0.55 + i * 0.1), H * 0.07, 0.8 + i * 0.4, i * 2.3));
                    break;
                }
                case WallKind.Stripes:
                {
                    dc.DrawRectangle(S(0x0A0A0F), null, full);
                    for (int i = -10; i < 30; i++)
                    {
                        double x = i * H * 0.12;
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            double w = H * (0.01 + rnd.NextDouble() * 0.05);
                            ctx.BeginFigure(new Point(x, H), true, true);
                            ctx.LineTo(new Point(x + H * 0.8, 0), false, false);
                            ctx.LineTo(new Point(x + H * 0.8 + w, 0), false, false);
                            ctx.LineTo(new Point(x + w, H), false, false);
                        }
                        dc.DrawGeometry(S(0xE81E4A, (byte)rnd.Next(12, 90)), null, g);
                    }
                    Blob(dc, C(0xFF2D55), W * 0.5, H * 1.0, H * 0.7, 0.45);
                    break;
                }
                case WallKind.Holo:
                {
                    dc.DrawRectangle(S(0x030308), null, full);
                    var c0 = new Point(W * 0.5, H * 0.56);
                    Blob(dc, C(0x7C5CFF), c0.X - H * 0.35, c0.Y - H * 0.1, H * 0.7, 0.55);
                    Blob(dc, C(0x00E5FF), c0.X + H * 0.4, c0.Y + H * 0.05, H * 0.6, 0.45);
                    Blob(dc, C(0xFF4FD8), c0.X + H * 0.05, c0.Y + H * 0.35, H * 0.5, 0.35);
                    // iridescent ring made of many coloured arc segments
                    double R = H * 0.3;
                    int seg = 180;
                    for (int ring = 0; ring < 3; ring++)
                    {
                        double r = R * (1 - ring * 0.16), thick = H * (0.012 - ring * 0.003);
                        for (int i = 0; i < seg; i++)
                        {
                            double a1 = Math.PI * 2 * i / seg, a2 = Math.PI * 2 * (i + 1.2) / seg;
                            var col = Hsv((i * 2.0 + ring * 40) % 360, 0.75, 1);
                            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(170 - ring * 45), col.R, col.G, col.B)), thick);
                            dc.DrawLine(pen, new Point(c0.X + Math.Cos(a1) * r, c0.Y + Math.Sin(a1) * r * 0.92), new Point(c0.X + Math.Cos(a2) * r, c0.Y + Math.Sin(a2) * r * 0.92));
                        }
                    }
                    Blob(dc, C(0xFFFFFF), c0.X, c0.Y, R * 0.42, 0.18);
                    for (int i = 0; i < 220; i++)
                    {
                        double r = (0.4 + rnd.NextDouble() * 1.4) * s;
                        dc.DrawEllipse(S(0xFFFFFF, (byte)rnd.Next(30, 160)), null, new Point(rnd.NextDouble() * W, rnd.NextDouble() * H), r, r);
                    }
                    dc.DrawRectangle(Lin(0, 0, 0, 1, (0, C(0x000000, 0)), (0.75, C(0x000000, 0)), (1, C(0x000000, 160))), null, full);
                    break;
                }
                case WallKind.Arch:
                {
                    dc.DrawRectangle(Lin(0, 0, 1, 1, (0, C(0x0A0F16)), (1, C(0x0F2233))), null, full);
                    Blob(dc, C(0x1793D1), W * 0.72, H * 0.42, H * 0.75, 0.35);
                    var apex = new Point(W * 0.7, H * 0.2);
                    for (int i = 0; i < 26; i++)
                    {
                        double spread = H * (0.12 + i * 0.045);
                        var pen = new Pen(S(0x1793D1, (byte)Math.Max(18, 150 - i * 5)), (i == 0 ? 3 : 1.4) * s);
                        dc.DrawLine(pen, apex, new Point(apex.X - spread, H * 1.05));
                        dc.DrawLine(pen, apex, new Point(apex.X + spread, H * 1.05));
                    }
                    for (int i = 0; i < 12; i++)
                    {
                        double y = H * (0.55 + i * 0.04);
                        dc.DrawLine(new Pen(S(0x1793D1, (byte)(14 + i * 3)), 1 * s), new Point(0, y), new Point(W, y));
                    }
                    dc.DrawEllipse(S(0x6FD2FF), null, apex, 4 * s, 4 * s);
                    Blob(dc, C(0x6FD2FF), apex.X, apex.Y, 40 * s, 0.6);
                    break;
                }
            }
            dc.Pop();
        }

        static Color Hsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        static SolidColorBrush Shade(uint a, uint b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            var ca = C(a); var cb = C(b);
            return new SolidColorBrush(Color.FromRgb((byte)(ca.R + (cb.R - ca.R) * t), (byte)(ca.G + (cb.G - ca.G) * t), (byte)(ca.B + (cb.B - ca.B) * t)));
        }

        static void Tri(DrawingContext dc, Point a, Point b, Point c, Brush fill)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open()) { ctx.BeginFigure(a, true, true); ctx.LineTo(b, false, false); ctx.LineTo(c, false, false); }
            dc.DrawGeometry(fill, new Pen(fill, 1), g);
        }

        static Geometry Petal(double len, double width)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.BezierTo(new Point(len * 0.3, -width), new Point(len * 0.8, -width * 0.8), new Point(len, 0), true, true);
                ctx.BezierTo(new Point(len * 0.8, width * 0.8), new Point(len * 0.3, width), new Point(0, 0), true, true);
            }
            g.Freeze();
            return g;
        }

        static Geometry Hill(double W, double H, double leftY, double peakY, double rightY)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(-10, H * leftY), true, true);
                ctx.BezierTo(new Point(W * 0.25, H * (peakY - 0.06)), new Point(W * 0.55, H * (peakY - 0.04)), new Point(W + 10, H * rightY), true, true);
                ctx.LineTo(new Point(W + 10, H + 10), false, false);
                ctx.LineTo(new Point(-10, H + 10), false, false);
            }
            g.Freeze();
            return g;
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

        static void Aurora(DrawingContext dc, double W, double H, double s, Color[] cols, Random rnd)
        {
            for (int band = 0; band < 3; band++)
            {
                var c = cols[band % cols.Length];
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    double y0 = H * (0.3 + band * 0.15);
                    ctx.BeginFigure(new Point(-20, y0), false, false);
                    ctx.BezierTo(new Point(W * 0.3, y0 - H * (0.25 + rnd.NextDouble() * 0.1)), new Point(W * 0.6, y0 + H * 0.25), new Point(W + 20, y0 - H * 0.1), true, true);
                }
                g.Freeze();
                for (int i = 8; i >= 1; i--)
                    dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(10 + 9 * (8 - i)), c.R, c.G, c.B)), s * 22 * i), g);
            }
        }

        // ---------- rendering ----------
        public static BitmapSource Render(Preset p, int w, int h, bool withPreview)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                Draw(dc, w, h, p);
                if (withPreview) PreviewArt.DrawUi(dc, w, h, p);
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
            var file = Path.Combine(Backup.DataDir, "Wallpapers", p.Id + "-" + w + "x" + h + "-v2.png");
            return File.Exists(file) ? file : RenderToFile(p, file, w, h);
        }
    }
}

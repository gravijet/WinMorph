using System;
using System.Windows;
using System.Windows.Media;

namespace WinMorph.Core
{
    /// <summary>
    /// The Zen desktop "Morgenteich": a koi pond seen from above in the morning. Teal water with light
    /// on it, lily pads, two lotus flowers, a few fallen petals and koi. Used by the still wallpaper (and so
    /// by the frosted glass of every Zen surface) and by the live pond, which lets the koi swim.
    /// </summary>
    public static class PondArt
    {
        static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        static SolidColorBrush S(uint rgb, byte a = 255) { var b = new SolidColorBrush(C(rgb, a)); b.Freeze(); return b; }

        /// <summary>The water: lighter where the morning sun falls in, deeper to the lower right, with soft light bands.</summary>
        public static void Water(DrawingContext dc, double W, double H)
        {
            var full = new Rect(0, 0, W, H);
            var deep = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            deep.GradientStops.Add(new GradientStop(C(0x3FB0A4), 0));
            deep.GradientStops.Add(new GradientStop(C(0x16867F), 0.42));
            deep.GradientStops.Add(new GradientStop(C(0x0B5866), 1));
            deep.Freeze();
            dc.DrawRectangle(deep, null, full);
            // sunlight entering at the upper left
            var sun = new RadialGradientBrush { Center = new Point(0.22, 0.16), GradientOrigin = new Point(0.22, 0.16), RadiusX = 0.55, RadiusY = 0.75 };
            sun.GradientStops.Add(new GradientStop(C(0xF8E7A6, 120), 0));
            sun.GradientStops.Add(new GradientStop(C(0xBDE8D6, 40), 0.5));
            sun.GradientStops.Add(new GradientStop(C(0xBDE8D6, 0), 1));
            sun.Freeze();
            dc.DrawRectangle(sun, null, full);
            // caustic light: thin bright wavy bands drifting across the water
            var rnd = new Random(7);
            double s = H / 1080.0;
            for (int i = 0; i < 26; i++)
            {
                double y = rnd.NextDouble() * H, x0 = -W * 0.1, len = W * (0.25 + rnd.NextDouble() * 0.5), amp = H * (0.004 + rnd.NextDouble() * 0.01);
                x0 += rnd.NextDouble() * W;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(x0, y + Math.Sin(i) * amp), false, false);
                    for (int k = 1; k <= 90; k++)
                    {
                        double x = x0 + len * k / 90.0;
                        ctx.LineTo(new Point(x, y + Math.Sin(k * 0.22 + i) * amp + Math.Sin(k * 0.07 + i * 2) * amp * 0.6), true, true);
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, new Pen(S(0xE8FFF6, (byte)rnd.Next(10, 26)), (1.0 + rnd.NextDouble() * 1.8) * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g);
            }
        }

        // lily pads: centre (relative), radius (relative to height), rotation of the notch
        static readonly (double x, double y, double r, double notch, bool lotus)[] Pads =
        {
            (0.08, 0.20, 0.085, 30, false), (0.15, 0.36, 0.055, 160, false), (0.04, 0.48, 0.06, 250, false),
            (0.83, 0.16, 0.095, 200, true), (0.93, 0.32, 0.06, 80, false), (0.74, 0.08, 0.05, 300, false),
            (0.88, 0.70, 0.11, 120, false), (0.97, 0.56, 0.05, 20, false), (0.72, 0.84, 0.06, 260, false),
            (0.20, 0.82, 0.075, 340, true), (0.30, 0.94, 0.05, 60, false), (0.10, 0.66, 0.045, 180, false),
        };

        /// <summary>The lily pads, lotus flowers and petals: everything that floats on the water (drawn over the koi).</summary>
        public static void Floating(DrawingContext dc, double W, double H)
        {
            double s = H / 1080.0;
            foreach (var p in Pads)
            {
                var c = new Point(p.x * W, p.y * H);
                double r = p.r * H;
                // soft shadow on the water
                dc.DrawEllipse(S(0x05343D, 60), null, new Point(c.X + r * 0.08, c.Y + r * 0.12), r * 1.02, r * 1.02);
                var pad = PadGeometry(c, r, p.notch);
                var fill = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.45), RadiusX = 0.7, RadiusY = 0.7 };
                fill.GradientStops.Add(new GradientStop(C(0x8FD06B), 0));
                fill.GradientStops.Add(new GradientStop(C(0x4E9A3E), 0.6));
                fill.GradientStops.Add(new GradientStop(C(0x2F6E2E), 1));
                fill.Freeze();
                dc.DrawGeometry(fill, new Pen(S(0x2A5E27, 160), 1.2 * s), pad);
                // veins from the notch outwards
                var vein = new Pen(S(0xC8EFA0, 70), 1.1 * s);
                for (int k = 0; k < 9; k++)
                {
                    double a = (p.notch + 20 + k * 36) * Math.PI / 180;
                    dc.DrawLine(vein, c, new Point(c.X + Math.Cos(a) * r * 0.9, c.Y + Math.Sin(a) * r * 0.9));
                }
                if (p.lotus) LotusFlower(dc, new Point(c.X + r * 0.15, c.Y - r * 0.1), r * 0.62);
            }
            // fallen petals drifting on the water
            var rnd = new Random(11);
            for (int i = 0; i < 18; i++)
            {
                var at = new Point(rnd.NextDouble() * W, rnd.NextDouble() * H);
                double len = (10 + rnd.NextDouble() * 10) * s;
                dc.PushTransform(new RotateTransform(rnd.NextDouble() * 360, at.X, at.Y));
                dc.DrawEllipse(S(0x05343D, 40), null, new Point(at.X + 2 * s, at.Y + 3 * s), len, len * 0.42);
                dc.DrawEllipse(new LinearGradientBrush(C(0xFFD3E6), C(0xF07AAE), 0), null, at, len, len * 0.42);
                dc.Pop();
            }
        }

        static Geometry PadGeometry(Point c, double r, double notchDeg)
        {
            double a0 = (notchDeg + 9) * Math.PI / 180, a1 = (notchDeg - 9) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(c, true, true);
                ctx.LineTo(new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r), true, true);
                ctx.ArcTo(new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r), new Size(r, r), 0, true, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        static void LotusFlower(DrawingContext dc, Point c, double r)
        {
            var outer = new LinearGradientBrush(C(0xFFE1EE), C(0xE2589A), 90); outer.Freeze();
            var inner = new LinearGradientBrush(C(0xFFF2F7), C(0xF27FB4), 90); inner.Freeze();
            var edge = new Pen(S(0xB83A78, 90), Math.Max(0.6, r * 0.02));
            Geometry Petal(double len, double width)
                => Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M0,0 C{0},{1} {2},{3} 0,{4} C{5},{3} {6},{1} 0,0 Z", width, -len * 0.3, width * 0.8, -len * 0.8, -len, -width * 0.8, -width));
            var big = Petal(r, r * 0.42); var small = Petal(r * 0.7, r * 0.34);
            for (int i = 0; i < 8; i++)
            {
                dc.PushTransform(new TranslateTransform(c.X, c.Y));
                dc.PushTransform(new RotateTransform(i * 45 + 22));
                dc.DrawGeometry(outer, edge, big);
                dc.Pop(); dc.Pop();
            }
            for (int i = 0; i < 6; i++)
            {
                dc.PushTransform(new TranslateTransform(c.X, c.Y));
                dc.PushTransform(new RotateTransform(i * 60));
                dc.DrawGeometry(inner, edge, small);
                dc.Pop(); dc.Pop();
            }
            var heart = new RadialGradientBrush(C(0xFFE58A), C(0xE8A51C)); heart.Freeze();
            dc.DrawEllipse(heart, new Pen(S(0xB57C12, 160), r * 0.02), c, r * 0.2, r * 0.2);
        }

        // ---------------- koi ----------------
        static readonly Geometry Body = Geometry.Parse("M100,0 C100,-10 93,-15 80,-15 C60,-15 40,-10 20,-4.5 L6,-1.8 L6,1.8 L20,4.5 C40,10 60,15 80,15 C93,15 100,10 100,0 Z");

        /// <summary>One koi at <paramref name="at"/>, heading <paramref name="angle"/> degrees, <paramref name="len"/> long.
        /// pattern: 0 kohaku (white, vermilion), 1 yamabuki (gold), 2 showa (black, red, white), 3 orange ogon. tail: -1..1 swing.</summary>
        public static void Koi(DrawingContext dc, Point at, double angle, double len, int pattern, double tail, bool shadow = true)
        {
            double k = len / 120.0;
            if (shadow)
            {
                dc.PushTransform(new TranslateTransform(at.X + len * 0.06, at.Y + len * 0.1));
                dc.PushTransform(new RotateTransform(angle));
                dc.PushTransform(new ScaleTransform(k, k));
                dc.PushTransform(new TranslateTransform(-60, 0));
                dc.DrawGeometry(S(0x03303A, 70), null, Body);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }
            dc.PushTransform(new TranslateTransform(at.X, at.Y));
            dc.PushTransform(new RotateTransform(angle));
            dc.PushTransform(new ScaleTransform(k, k));
            dc.PushTransform(new TranslateTransform(-60, 0));
            Color baseC = pattern == 1 ? C(0xF6C343) : pattern == 3 ? C(0xF58A2E) : C(0xFBF7F0);
            var finBrush = S(pattern == 1 ? 0xFBE3A0u : 0xFFFFFFu, 150);
            // tail fin, swinging
            dc.PushTransform(new RotateTransform(tail * 22, 7, 0));
            dc.DrawGeometry(finBrush, null, Geometry.Parse("M8,0 C0,-6 -8,-15 -15,-13 C-9,-5 -9,5 -15,13 C-8,15 0,6 8,0 Z"));
            dc.Pop();
            // pectoral fins
            dc.DrawGeometry(finBrush, null, Geometry.Parse("M74,-11 C70,-22 62,-26 56,-24 C60,-19 64,-14 70,-10 Z"));
            dc.DrawGeometry(finBrush, null, Geometry.Parse("M74,11 C70,22 62,26 56,24 C60,19 64,14 70,10 Z"));
            var body = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, baseC.R + 10), (byte)Math.Min(255, baseC.G + 10), (byte)Math.Min(255, baseC.B + 10)), Color.FromRgb((byte)(baseC.R * 0.86), (byte)(baseC.G * 0.86), (byte)(baseC.B * 0.86)), 90);
            dc.DrawGeometry(body, null, Body);
            // the pattern, clipped to the body
            dc.PushClip(Body);
            var red = S(0xE8451C);
            switch (pattern)
            {
                case 0:
                    dc.DrawEllipse(red, null, new Point(82, -2), 13, 11);
                    dc.DrawEllipse(red, null, new Point(52, 3), 15, 9);
                    dc.DrawEllipse(red, null, new Point(28, -2), 9, 6);
                    break;
                case 2:
                    dc.DrawEllipse(S(0x1B1A1C), null, new Point(60, -6), 16, 8);
                    dc.DrawEllipse(S(0x1B1A1C), null, new Point(34, 4), 10, 6);
                    dc.DrawEllipse(red, null, new Point(84, 3), 12, 9);
                    dc.DrawEllipse(red, null, new Point(46, 5), 9, 6);
                    break;
                case 1:
                    dc.DrawEllipse(S(0xFFF1B8, 120), null, new Point(70, -5), 22, 5);
                    break;
                default:
                    dc.DrawEllipse(S(0xFFD08A, 140), null, new Point(70, -5), 22, 5);
                    break;
            }
            // light along the back
            dc.DrawEllipse(S(0xFFFFFF, 50), null, new Point(68, -5), 26, 3);
            dc.Pop();
            // eyes
            dc.DrawEllipse(S(0x1B1A1C), null, new Point(92, -6), 1.6, 1.6);
            dc.DrawEllipse(S(0x1B1A1C), null, new Point(92, 6), 1.6, 1.6);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }

        /// <summary>Where the koi of the still wallpaper swim (relative position, heading, length relative to height, pattern).</summary>
        public static readonly (double x, double y, double angle, double len, int pattern)[] StillKoi =
        {
            (0.32, 0.30, -18, 0.15, 0), (0.42, 0.22, 165, 0.12, 1), (0.62, 0.55, 210, 0.16, 2), (0.56, 0.68, 20, 0.11, 3), (0.24, 0.58, 95, 0.13, 0),
        };

        public static void Draw(DrawingContext dc, double W, double H)
        {
            Water(dc, W, H);
            foreach (var k in StillKoi) Koi(dc, new Point(k.x * W, k.y * H), k.angle, k.len * H, k.pattern, 0.4);
            Floating(dc, W, H);
        }
    }
}

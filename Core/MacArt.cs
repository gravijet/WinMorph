using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Core
{
    /// <summary>
    /// The macOS desktop, drawn pixel by pixel from one formula: a warm morning sky at the upper left and four
    /// sheets of blue glass that sweep from the lower left to the upper right, each with a bright rim where the
    /// light catches it and a soft shadow on the sheet behind. The live desktop uses the same formula with time,
    /// so the sheets drift slowly. Our own picture in the spirit of macOS, no Apple artwork.
    /// </summary>
    public static class MacArt
    {
        // the top edge of each sheet: v = c + slope·u + waves
        static readonly (double c, double slope, double amp, double freq, double phase, double speed)[] Edges =
        {
            (0.60, -0.80, 0.105, 0.55, 0.50, 0.050),
            (0.79, -0.82, 0.115, 0.58, 1.25, -0.038),
            (0.98, -0.84, 0.120, 0.52, 2.05, 0.032),
            (1.17, -0.82, 0.110, 0.50, 2.80, -0.027),
        };

        // each sheet: colour at its lit rim, colour in its depth
        static readonly (Color rim, Color deep)[] Sheets =
        {
            (Rgb(0xC4E6FB), Rgb(0x74B8EF)),
            (Rgb(0x66AEF4), Rgb(0x2671DB)),
            (Rgb(0x3A82EA), Rgb(0x1247B9)),
            (Rgb(0x2A63D8), Rgb(0x0E318E)),
        };

        static readonly Color SkyWarm = Rgb(0xF8E6D2), SkyMid = Rgb(0xF5F4F1), SkyCool = Rgb(0xBCD8F2), SkyGlow = Rgb(0xFFF4E6);

        static Color Rgb(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

        static double Edge(int k, double u, double t)
        {
            var e = Edges[k];
            return e.c + e.slope * u + e.amp * Math.Sin(Math.PI * 2 * e.freq * u + e.phase + e.speed * t)
                 + e.amp * 0.22 * Math.Sin(Math.PI * 2 * 1.7 * u + e.phase * 1.7 - e.speed * t * 1.4);
        }

        /// <summary>Fills a 0x00RRGGBB buffer. t = seconds (0 for the still picture).</summary>
        public static void Fill(uint[] px, int W, int H, double t, bool dither)
        {
            int n = Edges.Length;
            var edge = new double[n];
            double px1 = 1.0 / H;                       // one pixel in v units
            for (int x = 0; x < W; x++)
            {
                double u = x / (double)Math.Max(1, W - 1);
                for (int k = 0; k < n; k++) edge[k] = Edge(k, u, t);
                for (int y = 0; y < H; y++)
                {
                    double v = y / (double)Math.Max(1, H - 1);
                    // which sheet is in front here: the last edge above the pixel
                    int k = -1;
                    for (int j = 0; j < n; j++) if (v >= edge[j]) k = j;
                    double r, g, b;
                    Shade(k, u, v, edge, t, out r, out g, out b);
                    // soft, anti-aliased rim: blend with the sheet behind within ~1.2 px of the edge
                    if (k >= 0)
                    {
                        double d = (v - edge[k]) / px1;
                        if (d < 1.2)
                        {
                            Shade(k - 1, u, v, edge, t, out double r0, out double g0, out double b0);
                            double a = Math.Max(0, d / 1.2);
                            r = r0 + (r - r0) * a; g = g0 + (g - g0) * a; b = b0 + (b - b0) * a;
                        }
                    }
                    if (dither) { double dz = (((x * 7 + y * 13) & 3) - 1.5) * 0.55; r += dz; g += dz; b += dz; }
                    px[y * W + x] = (uint)(Cl(r) << 16 | Cl(g) << 8 | Cl(b));
                }
            }
        }

        static void Shade(int k, double u, double v, double[] edge, double t, out double r, out double g, out double b)
        {
            int n = Edges.Length;
            if (k < 0)
            {
                // the sky: warm at the upper left, cooler towards the glass, a glow where the sun would be
                // through a bright middle, so warm and cool never mix to grey
                double m = Clamp01(0.75 * u + 0.55 * v);
                Color a0 = m < 0.45 ? SkyWarm : SkyMid, a1 = m < 0.45 ? SkyMid : SkyCool;
                double mm = m < 0.45 ? m / 0.45 : (m - 0.45) / 0.55;
                mm = mm * mm * (3 - 2 * mm);
                r = a0.R + (a1.R - a0.R) * mm; g = a0.G + (a1.G - a0.G) * mm; b = a0.B + (a1.B - a0.B) * mm;
                double gl = Ex((u - 0.18) * (u - 0.18) * 9 + (v - 0.12) * (v - 0.12) * 14);
                r += (SkyGlow.R - r) * gl * 0.55; g += (SkyGlow.G - g) * gl * 0.55; b += (SkyGlow.B - b) * gl * 0.55;
            }
            else
            {
                var s = Sheets[k];
                double dTop = v - edge[k];
                double depth = Clamp01(dTop / 0.26);
                depth = depth * (2 - depth);   // ease out: the colour deepens quickly below the rim
                r = s.rim.R + (s.deep.R - s.rim.R) * depth; g = s.rim.G + (s.deep.G - s.rim.G) * depth; b = s.rim.B + (s.deep.B - s.rim.B) * depth;
                // the rim catches the light: a crisp bright line and a wider sheen
                double q = dTop / 0.0022;
                double line = (q < 4 ? Ex(q * q) * 0.62 : 0) + Ex(dTop / 0.018) * 0.20;
                // light travels along the rim from the upper right
                line *= 0.55 + 0.45 * Clamp01(u * 1.2);
                r += (255 - r) * line; g += (255 - g) * line; b += (255 - b) * line;
                // a broad, cool sheen inside the glass, following the rim: the sheet looks curved
                double sq = (dTop - 0.11) / 0.065;
                double sheen = 0.16 * Ex(sq * sq) * (0.4 + 0.6 * Clamp01(u * 1.3));
                r += (0x86 - r) * sheen; g += (0xC8 - g) * sheen; b += (0xFF - b) * sheen;
            }
            // the next sheet in front casts a soft shadow just above its rim
            if (k + 1 < n)
            {
                double dNext = edge[k + 1] - v;
                if (dNext > 0)
                {
                    double sh = 1 - (k < 0 ? 0.10 : 0.24) * Ex(dNext / (k < 0 ? 0.02 : 0.03));
                    r *= sh; g *= sh; b *= sh;
                }
            }
            // a quiet vignette at the bottom
            double vig = 1 - 0.10 * Clamp01((v - 0.75) / 0.25);
            r *= vig; g *= vig; b *= vig;
        }

        /// <summary>e^-x for x ≥ 0, fast enough for every pixel of the live desktop (error below 1 %).</summary>
        static double Ex(double x)
        {
            if (x > 12) return 0;
            double y = 1 + x / 16;               // (1 + x/16)^-16 → e^-x
            y *= y; y *= y; y *= y; y *= y;
            return 1 / y;
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        static int Cl(double v) => v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5);

        public static void Draw(DrawingContext dc, double W, double H)
        {
            int w = Math.Max(16, (int)Math.Round(W)), h = Math.Max(16, (int)Math.Round(H));
            var px = new uint[w * h];
            Fill(px, w, h, 0, true);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
            bmp.Freeze();
            dc.DrawImage(bmp, new Rect(0, 0, W, H));
        }
    }
}

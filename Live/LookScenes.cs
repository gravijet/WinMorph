using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Live
{
    /// <summary>Text drawn into a scene's pixel buffer (proportional fonts, anti-aliased, cached per text).</summary>
    internal static class SceneText
    {
        public sealed class Mask { public int W, H; public byte[] A; }
        static readonly Dictionary<string, Mask> cache = new Dictionary<string, Mask>();

        public static Mask Get(string text, string font, double px, FontWeight weight)
        {
            var key = font + "|" + px + "|" + weight + "|" + text;
            lock (cache)
            {
                if (cache.TryGetValue(key, out var m)) return m;
                if (cache.Count > 400) cache.Clear();   // values change every second: keep it small
                var tf = new Typeface(new FontFamily(font), FontStyles.Normal, weight, FontStretches.Normal);
#pragma warning disable CS0618
                var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, px, Brushes.White);
#pragma warning restore CS0618
                int w = Math.Max(1, (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 2), h = Math.Max(1, (int)Math.Ceiling(ft.Height) + 2);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) { dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h)); dc.DrawText(ft, new Point(1, 1)); }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var buf = new int[w * h];
                rtb.CopyPixels(buf, w * 4, 0);
                var a = new byte[w * h];
                for (int i = 0; i < buf.Length; i++) a[i] = (byte)((buf[i] >> 8) & 255);
                return cache[key] = new Mask { W = w, H = h, A = a };
            }
        }

        public static void Draw(uint[] px, int W, int H, Mask m, int x, int y, Color c, double alpha = 1)
        {
            for (int yy = 0; yy < m.H; yy++)
            {
                int sy = y + yy;
                if ((uint)sy >= (uint)H) continue;
                for (int xx = 0; xx < m.W; xx++)
                {
                    int sx = x + xx, a = (int)(m.A[yy * m.W + xx] * alpha);
                    if (a <= 0 || (uint)sx >= (uint)W) continue;
                    int i = sy * W + sx;
                    uint v = px[i];
                    int vr = (int)(v >> 16 & 255), vg = (int)(v >> 8 & 255), vb = (int)(v & 255);
                    px[i] = (uint)((vr + ((c.R - vr) * a >> 8)) << 16 | (vg + ((c.G - vg) * a >> 8)) << 8 | (vb + ((c.B - vb) * a >> 8)));
                }
            }
        }
    }

    /// <summary>
    /// Zen desktop "Morgenteich": the koi pond of the wallpaper, and the koi swim. They glide slowly under
    /// the lily pads, beat their tails, turn away from the edges, and now and then a ring of water spreads.
    /// Water and floating leaves are drawn once; each frame only the small areas around the koi change.
    /// </summary>
    sealed class ZenScene : Scene
    {
        public override int Fps => 20;
        public override double Resolution => 0.6;

        sealed class Fish
        {
            public double X, Y, A, Len, Phase, Tail;
            public int Pattern, SW, SH;
            public uint[][] Frames;   // premultiplied ARGB, the koi facing right, tail in six positions
        }

        sealed class Ring { public double X, Y, Age; }

        uint[] water, floating, composite;
        readonly List<Fish> fish = new List<Fish>();
        readonly List<Ring> rings = new List<Ring>();
        double nextRing;
        const int TailFrames = 6;

        static uint[] Pixels(int w, int h, Action<DrawingContext> draw, bool keepAlpha)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) draw(dc);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var buf = new int[w * h];
            rtb.CopyPixels(buf, w * 4, 0);
            var res = new uint[w * h];
            for (int i = 0; i < buf.Length; i++) res[i] = keepAlpha ? (uint)buf[i] : (uint)buf[i] & 0xFFFFFF;
            return res;
        }

        protected override void Init()
        {
            water = Pixels(W, H, dc => Core.PondArt.Water(dc, W, H), false);
            floating = Pixels(W, H, dc => Core.PondArt.Floating(dc, W, H), true);
            composite = (uint[])water.Clone();
            for (int i = 0; i < composite.Length; i++) composite[i] = Over(floating[i], composite[i]);
            fish.Clear(); rings.Clear();
            var rnd = new Random(5);
            foreach (var k in Core.PondArt.StillKoi)
            {
                double len = k.len * H;
                var f = new Fish { X = k.x * W, Y = k.y * H, A = k.angle * Math.PI / 180, Len = len, Pattern = k.pattern, Phase = rnd.NextDouble() * 10, SW = (int)(len * 1.4), SH = (int)(len * 0.62) };
                f.Frames = new uint[TailFrames][];
                for (int i = 0; i < TailFrames; i++)
                {
                    double tail = -1 + 2.0 * i / (TailFrames - 1);
                    var fr = f; double t2 = tail;
                    f.Frames[i] = Pixels(f.SW, f.SH, dc => Core.PondArt.Koi(dc, new Point(fr.SW * 0.55, fr.SH / 2.0), 0, fr.Len, fr.Pattern, t2, false), true);
                }
                fish.Add(f);
            }
            nextRing = 3;
        }

        static uint Over(uint src, uint dst)
        {
            uint a = src >> 24;
            if (a == 0) return dst;
            if (a == 255) return src & 0xFFFFFF;
            uint inv = 255 - a;
            uint r = ((src >> 16) & 255) + (((dst >> 16) & 255) * inv) / 255;
            uint g = ((src >> 8) & 255) + (((dst >> 8) & 255) * inv) / 255;
            uint b = (src & 255) + ((dst & 255) * inv) / 255;
            return (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
        }

        public override void Step(double dt)
        {
            dt = Math.Min(dt, 0.2);
            Array.Copy(composite, Px, Px.Length);
            double margin = Math.Min(W, H) * 0.09, speedBase = H * 0.032;
            var boxes = new List<(int x0, int y0, int x1, int y1)>();
            foreach (var f in fish)
            {
                // wandering: a slow, uneven turning, and away from the banks
                double turn = Math.Sin(T * 0.31 + f.Phase) * 0.32 + Math.Sin(T * 0.113 + f.Phase * 2.1) * 0.22;
                double want = double.NaN;
                if (f.X < margin) want = 0; else if (f.X > W - margin) want = Math.PI;
                else if (f.Y < margin) want = Math.PI / 2; else if (f.Y > H - margin) want = -Math.PI / 2;
                if (!double.IsNaN(want))
                {
                    double d = Math.Atan2(Math.Sin(want - f.A), Math.Cos(want - f.A));
                    turn += Math.Sign(d) * 0.9;
                }
                f.A += turn * dt;
                double speed = speedBase * (0.75 + 0.35 * Math.Sin(T * 0.21 + f.Phase));
                f.X += Math.Cos(f.A) * speed * dt;
                f.Y += Math.Sin(f.A) * speed * dt;
                f.Tail += dt * (2.2 + speed / speedBase);
                double r = Math.Sqrt(f.SW * f.SW + f.SH * f.SH) / 2 + f.Len * 0.12 + 2;
                boxes.Add(((int)(f.X - r), (int)(f.Y - r), (int)(f.X + r), (int)(f.Y + r)));
            }
            // under the koi: plain water again, then their shadows and bodies, then the leaves that float over them
            foreach (var b in boxes) CopyBox(water, b);
            foreach (var f in fish) Blit(f, true);
            foreach (var f in fish) Blit(f, false);
            foreach (var b in boxes) FloatBox(b);

            // now and then a ring of water at a koi's head
            nextRing -= dt;
            if (nextRing <= 0 && fish.Count > 0)
            {
                var f = fish[R.Next(fish.Count)];
                rings.Add(new Ring { X = f.X + Math.Cos(f.A) * f.Len * 0.45, Y = f.Y + Math.Sin(f.A) * f.Len * 0.45 });
                nextRing = 4 + R.NextDouble() * 5;
            }
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var ring = rings[i];
                ring.Age += dt;
                if (ring.Age > 3) { rings.RemoveAt(i); continue; }
                double p = ring.Age / 3;
                int alpha = (int)(70 * (1 - p) * (1 - p));
                Circle(ring.X, ring.Y, H * (0.01 + 0.06 * Math.Sqrt(p)), 255, 255, 255, alpha);
                if (p > 0.15) Circle(ring.X, ring.Y, H * (0.06 * Math.Sqrt(p - 0.15)), 255, 255, 255, alpha / 2);
            }
        }

        void CopyBox(uint[] src, (int x0, int y0, int x1, int y1) b)
        {
            int x0 = Math.Max(0, b.x0), x1 = Math.Min(W, b.x1), y0 = Math.Max(0, b.y0), y1 = Math.Min(H, b.y1);
            if (x1 <= x0) return;
            for (int y = y0; y < y1; y++) Array.Copy(src, y * W + x0, Px, y * W + x0, x1 - x0);
        }

        void FloatBox((int x0, int y0, int x1, int y1) b)
        {
            int x0 = Math.Max(0, b.x0), x1 = Math.Min(W, b.x1), y0 = Math.Max(0, b.y0), y1 = Math.Min(H, b.y1);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) { int i = y * W + x; uint s = floating[i]; if (s >> 24 != 0) Px[i] = Over(s, Px[i]); }
        }

        /// <summary>Draws a koi turned to its heading (bilinear), or its shadow a little below it.</summary>
        void Blit(Fish f, bool shadow)
        {
            int frame = (int)Math.Round((Math.Sin(f.Tail) + 1) / 2 * (TailFrames - 1));
            var spr = f.Frames[Math.Max(0, Math.Min(TailFrames - 1, frame))];
            double cs = Math.Cos(f.A), sn = Math.Sin(f.A);
            double cx = f.X + (shadow ? f.Len * 0.06 : 0), cy = f.Y + (shadow ? f.Len * 0.1 : 0);
            double ox = f.SW * 0.55, oy = f.SH / 2.0;   // the sprite's pivot (where the koi was drawn)
            double rad = Math.Sqrt(f.SW * f.SW + f.SH * f.SH) / 2 + 2;
            int x0 = Math.Max(0, (int)(cx - rad)), x1 = Math.Min(W - 1, (int)(cx + rad)), y0 = Math.Max(0, (int)(cy - rad)), y1 = Math.Min(H - 1, (int)(cy + rad));
            for (int y = y0; y <= y1; y++)
            {
                double dy = y - cy;
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x - cx;
                    double u = cs * dx + sn * dy + ox, v = -sn * dx + cs * dy + oy;
                    if (u < 0 || v < 0 || u >= f.SW - 1 || v >= f.SH - 1) continue;
                    int iu = (int)u, iv = (int)v; double fu = u - iu, fv = v - iv;
                    int i0 = iv * f.SW + iu;
                    uint p00 = spr[i0], p10 = spr[i0 + 1], p01 = spr[i0 + f.SW], p11 = spr[i0 + f.SW + 1];
                    double w00 = (1 - fu) * (1 - fv), w10 = fu * (1 - fv), w01 = (1 - fu) * fv, w11 = fu * fv;
                    double a = (p00 >> 24) * w00 + (p10 >> 24) * w10 + (p01 >> 24) * w01 + (p11 >> 24) * w11;
                    if (a < 1) continue;
                    int di = y * W + x;
                    uint d = Px[di];
                    if (shadow)
                    {
                        // the koi's shadow on the bottom of the pond: darker teal
                        double k = 1 - a / 255.0 * 0.28;
                        Px[di] = (uint)((int)(((d >> 16) & 255) * k) << 16 | (int)(((d >> 8) & 255) * k) << 8 | (int)((d & 255) * k));
                        continue;
                    }
                    double r = ((p00 >> 16) & 255) * w00 + ((p10 >> 16) & 255) * w10 + ((p01 >> 16) & 255) * w01 + ((p11 >> 16) & 255) * w11;
                    double g = ((p00 >> 8) & 255) * w00 + ((p10 >> 8) & 255) * w10 + ((p01 >> 8) & 255) * w01 + ((p11 >> 8) & 255) * w11;
                    double bl = (p00 & 255) * w00 + (p10 & 255) * w10 + (p01 & 255) * w01 + (p11 & 255) * w11;
                    double inv = 1 - a / 255.0;
                    Px[di] = (uint)(Cl((int)(r + ((d >> 16) & 255) * inv)) << 16 | Cl((int)(g + ((d >> 8) & 255) * inv)) << 8 | Cl((int)(bl + (d & 255) * inv)));
                }
            }
        }
    }

}

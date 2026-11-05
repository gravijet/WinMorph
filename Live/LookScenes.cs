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
    /// Zen desktop: a mountain landscape whose light follows the time of day, a big calm clock,
    /// the date and a thought for the day. Changes once a minute – practically no battery use.
    /// </summary>
    sealed class ZenScene : Scene
    {
        public override int Fps => 1;
        uint[] bg;
        int bgKey = -1;
        string drawn;

        // hour, sky top, sky bottom, sun/moon, mountains, text
        static readonly (double h, uint top, uint bottom, uint sun, uint mount, uint text)[] Light =
        {
            (0,  0x0E1424, 0x283050, 0xE8E6DA, 0x1A2036, 0xE6E2D6),
            (5,  0x1E2440, 0x6A5A78, 0xF2D0B0, 0x3A3448, 0xF4EDE2),
            (7,  0xF0C7A4, 0xF8E8D4, 0xF29E6A, 0xB39276, 0x5A4636),
            (10, 0xF5EFE4, 0xE6D8C2, 0xD99A6C, 0xB8A07F, 0x5E4D3A),
            (16, 0xF3E4CC, 0xEBCFA8, 0xE08A55, 0xA98868, 0x5A4532),
            (19, 0xE79A6E, 0x8E5F7C, 0xF36B3A, 0x5C4258, 0xFBEFE6),
            (21, 0x232A48, 0x4A3E60, 0xEDE6D2, 0x2A2840, 0xEAE4D8),
            (24, 0x0E1424, 0x283050, 0xE8E6DA, 0x1A2036, 0xE6E2D6),
        };

        static readonly string[] ThoughtsDe =
        {
            "Atme. Du bist genau hier.", "Eins nach dem anderen.", "Langsam ist auch ein Tempo.", "Was jetzt zählt, ist dieser Moment.",
            "Ruhe ist eine Fähigkeit, keine Laune.", "Weniger, aber besser.", "Du musst heute nicht alles schaffen.", "Lass los, was du nicht halten musst.",
            "Ein klarer Kopf beginnt mit einem tiefen Atemzug.", "Pausen sind Teil der Arbeit.", "Achte auf das, was gut ist.", "Der Berg wächst nicht, du wächst.",
            "Stille ist auch eine Antwort.", "Heute ist genug.", "Konzentration ist ein Geschenk an dich selbst.", "Kleine Schritte tragen weit.",
            "Sei freundlich – auch zu dir.", "Nichts muss perfekt sein.", "Schau aus dem Fenster. Atme aus.", "Der Fluss eilt nicht und kommt doch an.",
        };
        static readonly string[] ThoughtsEn =
        {
            "Breathe. You are right here.", "One thing at a time.", "Slow is a speed too.", "What matters is this moment.",
            "Calm is a skill, not a mood.", "Less, but better.", "You don't have to do it all today.", "Let go of what you don't need to hold.",
            "A clear mind starts with a deep breath.", "Breaks are part of the work.", "Notice what is good.", "The mountain doesn't grow, you do.",
            "Silence is an answer too.", "Today is enough.", "Focus is a gift to yourself.", "Small steps go a long way.",
            "Be kind – to yourself as well.", "Nothing has to be perfect.", "Look out of the window. Breathe out.", "The river doesn't hurry, yet it arrives.",
        };

        protected override void Init() { bg = null; drawn = null; }

        static Color Col(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

        static (Color top, Color bottom, Color sun, Color mount, Color text) At(double hour)
        {
            int i = 0;
            while (i < Light.Length - 2 && hour >= Light[i + 1].h) i++;
            var a = Light[i]; var b = Light[i + 1];
            double t = (hour - a.h) / (b.h - a.h);
            Color M(uint x, uint y) => Mix(Col(x), Col(y), t);
            return (M(a.top, b.top), M(a.bottom, b.bottom), M(a.sun, b.sun), M(a.mount, b.mount), M(a.text, b.text));
        }

        void BuildBackground(double hour)
        {
            var p = At(hour);
            bg = new uint[W * H];
            for (int y = 0; y < H; y++)
            {
                var c = Rgb(Mix(p.top, p.bottom, y / (double)(H - 1)));
                for (int x = 0; x < W; x++) bg[y * W + x] = c;
            }
            var keep = Px; Px = bg;
            // sun by day (climbs and sinks with the time), moon at night – to the right of the clock
            bool day = hour >= 6 && hour < 20;
            double f = day ? (hour - 6) / 14 : 0.5;
            double sx = W * 0.78, sy = day ? H * (0.5 - 0.24 * Math.Sin(Math.PI * f)) : H * 0.22;
            Glow(sx, sy, H * 0.22, p.sun.R / 3, p.sun.G / 3, p.sun.B / 3, 0.5);
            int sr = (int)(H * (day ? 0.075 : 0.05));
            for (int y = -sr; y <= sr; y++)
                for (int x = -sr; x <= sr; x++)
                {
                    double d = Math.Sqrt(x * x + y * y);
                    if (d <= sr) Blend((int)sx + x, (int)sy + y, p.sun.R, p.sun.G, p.sun.B, (int)(235 * Math.Min(1, (sr - d) + 0.3)));
                }
            Px = keep;
            // four mountain ranges, the far ones fade into the sky
            for (int i = 0; i < 4; i++)
            {
                var c = Mix(Mix(p.bottom, p.mount, 0.38 + i * 0.2), Color.FromRgb(0, 0, 0), i * 0.07);
                uint cv = Rgb(c);
                double baseY = H * (0.58 + i * 0.09), amp = H * (0.075 - i * 0.01);
                for (int x = 0; x < W; x++)
                {
                    double u = x / (double)W * Math.PI * 2;
                    double ridge = baseY - amp * (0.62 * Math.Sin(u * (0.8 + i * 0.45) + i * 2.3) + 0.38 * Math.Sin(u * (2.2 + i * 0.7) + i * 1.1) + 0.12 * Math.Sin(u * 7.3 + i));
                    for (int y = Math.Max(0, (int)ridge); y < H; y++) bg[y * W + x] = cv;
                }
            }
        }

        public override void Step(double dt)
        {
            var now = DateTime.Now;
            double hour = now.Hour + now.Minute / 60.0;
            int key = (int)(hour * 12);   // light changes every 5 minutes
            var stamp = now.ToString("yyyyMMddHHmm");
            if (bg == null || key != bgKey) { BuildBackground(hour); bgKey = key; drawn = null; }
            if (stamp == drawn) return;
            drawn = stamp;
            Array.Copy(bg, Px, Px.Length);
            var p = At(hour);
            bool de = Core.Loc.De;
            var clock = SceneText.Get(now.ToString("HH:mm"), "Segoe UI Light", H * 0.15, FontWeights.Light);
            int y0 = (int)(H * 0.1);
            SceneText.Draw(Px, W, H, clock, (W - clock.W) / 2, y0, p.text, 0.92);
            var date = SceneText.Get(now.ToString(de ? "dddd, d. MMMM" : "dddd, MMMM d", de ? new CultureInfo("de-DE") : new CultureInfo("en-US")), "Segoe UI", H * 0.028, FontWeights.Normal);
            SceneText.Draw(Px, W, H, date, (W - date.W) / 2, y0 + clock.H, p.text, 0.8);
            var list = de ? ThoughtsDe : ThoughtsEn;
            var thought = SceneText.Get("„" + list[now.DayOfYear % list.Length] + "“", "Segoe UI Light", H * 0.024, FontWeights.Light);
            SceneText.Draw(Px, W, H, thought, (W - thought.W) / 2, y0 + clock.H + date.H + (int)(H * 0.02), p.text, 0.7);
        }
    }

    /// <summary>Gaming desktop: dark with red light stripes and a live HUD (clock, CPU, GPU, RAM, ping, network).</summary>
    sealed class GameScene : Scene
    {
        public override int Fps => 1;
        uint[] bg;
        static readonly Color Red = Color.FromRgb(0xFF, 0x2D, 0x55), White = Color.FromRgb(0xF2, 0xF2, 0xF5), Grey = Color.FromRgb(0x8A, 0x86, 0x92);

        protected override void Init()
        {
            Px = Gradient(Color.FromRgb(0x0E, 0x0D, 0x14), Color.FromRgb(0x05, 0x05, 0x07));
            var rnd = new Random(7);
            for (int i = -12; i < 34; i++)
            {
                double x = i * H * 0.12, w = H * (0.008 + rnd.NextDouble() * 0.04);
                int a = rnd.Next(10, 70);
                for (int y = 0; y < H; y++)
                {
                    double off = x + (H - y) * 0.8;
                    for (int xx = (int)off; xx < off + w; xx++) Blend(xx, y, 0xE8, 0x1E, 0x4A, a);
                }
            }
            Glow(W * 0.5, H * 1.02, H * 0.75, 90, 8, 24, 0.55);
            bg = (uint[])Px.Clone();
        }

        void Rect(int x, int y, int w, int h, Color c, int a)
        {
            for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++) Blend(xx, yy, c.R, c.G, c.B, a);
        }

        public override void Step(double dt)
        {
            Array.Copy(bg, Px, Px.Length);
            bool de = Core.Loc.De;
            var now = DateTime.Now;
            int pw = (int)(W * 0.25), x0 = W - pw - W / 28, y0 = H / 12, pad = (int)(H * 0.022), rowH = (int)(H * 0.052);
            int ph = pad * 3 + (int)(H * 0.11) + rowH * 6;
            Rect(x0, y0, pw, ph, Color.FromRgb(0, 0, 0), 150);
            Rect(x0, y0, Math.Max(3, W / 600), ph, Red, 255);
            const string font = "Bahnschrift";
            var clock = SceneText.Get(now.ToString("HH:mm"), font, H * 0.085, FontWeights.SemiBold);
            SceneText.Draw(Px, W, H, clock, x0 + pad, y0 + pad / 2, White);
            var date = SceneText.Get(now.ToString(de ? "dddd, dd.MM." : "dddd, MM/dd", de ? new CultureInfo("de-DE") : new CultureInfo("en-US")).ToUpperInvariant(), font, H * 0.02, FontWeights.Normal);
            SceneText.Draw(Px, W, H, date, x0 + pad + 4, y0 + pad / 2 + clock.H - pad / 3, Red);
            int y = y0 + pad * 2 + (int)(H * 0.11);
            Shell.SysInfo.NetRate(out double rx, out double tx);
            int ping = Shell.SysInfo.PingMs;
            double gpu = Shell.SysInfo.GpuLoad;
            var rows = new List<(string label, string value, double bar)>
            {
                ("CPU", Shell.SysInfo.CpuSample().ToString("0") + " %", Shell.SysInfo.CpuSample()),
                ("GPU", gpu < 0 ? "–" : gpu.ToString("0") + " %", Math.Max(0, gpu)),
                ("RAM", Shell.SysStats.Mem().ToString("0") + " %", Shell.SysStats.Mem()),
                ("PING", ping < 0 ? "–" : ping + " ms", ping < 0 ? 0 : Math.Min(100, ping / 1.5)),
                ("NET", "↓ " + Shell.SysInfo.Bytes(rx) + "/s  ↑ " + Shell.SysInfo.Bytes(tx) + "/s", -1),
            };
            if (Shell.SysStats.Battery(out int pct, out bool ac)) rows.Add(("BAT", pct + " %" + (ac ? (de ? "  lädt" : "  charging") : ""), pct));
            foreach (var r in rows)
            {
                var l = SceneText.Get(r.label, font, H * 0.02, FontWeights.SemiBold);
                SceneText.Draw(Px, W, H, l, x0 + pad + 4, y, Grey);
                var v = SceneText.Get(r.value, font, H * 0.024, FontWeights.SemiBold);
                SceneText.Draw(Px, W, H, v, x0 + pw - pad - v.W, y - 2, White);
                if (r.bar >= 0)
                {
                    int bw = pw - pad * 2 - 4, by = y + l.H + 3;
                    Rect(x0 + pad + 4, by, bw, Math.Max(3, H / 260), Color.FromRgb(0x40, 0x30, 0x38), 255);
                    bool warn = r.label == "BAT" ? r.bar < 20 : r.bar > 85;
                    Rect(x0 + pad + 4, by, (int)(bw * Math.Min(100, r.bar) / 100), Math.Max(3, H / 260), warn ? Color.FromRgb(0xFF, 0xB0, 0x20) : Red, 255);
                }
                y += rowH;
            }
        }
    }
}

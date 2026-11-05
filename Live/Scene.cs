using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Live
{
    /// <summary>
    /// A procedural animated wallpaper. It draws into a plain pixel buffer (0x00RRGGBB),
    /// which is fast and works the same with or without graphics acceleration.
    /// </summary>
    public abstract class Scene
    {
        public int W, H;
        public uint[] Px;
        public double T;                    // seconds since start
        public double Mx = -1, My = -1;     // mouse in buffer pixels (-1 = not on this screen)
        public bool Click;                  // the left button was pressed since the last frame
        public Color Accent = Color.FromRgb(0x00, 0xFF, 0x41);
        protected readonly Random R = new Random();

        /// <summary>Fraction of the screen resolution that is rendered (lower = faster and softer).</summary>
        public virtual double Resolution => 1.0;
        public virtual int Fps => 30;
        public bool MouseIn => Mx >= 0 && My >= 0 && Mx < W && My < H;

        public void Setup(int w, int h) { W = Math.Max(16, w); H = Math.Max(16, h); Px = new uint[W * H]; Init(); }
        protected abstract void Init();
        public abstract void Step(double dt);
        /// <summary>Called when the wallpaper is hidden (maximised window, locked) or shown again.</summary>
        public virtual void Pause(bool on) { }

        // ---------------- drawing helpers ----------------
        protected static int Cl(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
        protected static uint Rgb(int r, int g, int b) => (uint)(Cl(r) << 16 | Cl(g) << 8 | Cl(b));
        protected static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);
        protected static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>Darkens every pixel: keep = 0..256 (256 = unchanged).</summary>
        protected unsafe void Fade(int keep)
        {
            uint k = (uint)Math.Max(0, Math.Min(256, keep));
            fixed (uint* p0 = Px)
            {
                uint* p = p0, end = p0 + Px.Length;
                while (p < end)
                {
                    uint v = *p;
                    if (v != 0) *p = (((v & 0xFF00FF) * k >> 8) & 0xFF00FF) | (((v & 0x00FF00) * k >> 8) & 0x00FF00);
                    p++;
                }
            }
        }

        protected void Fill(uint c) { for (int i = 0; i < Px.Length; i++) Px[i] = c; }

        protected void Blend(int x, int y, int r, int g, int b, int a)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H || a <= 0) return;
            int i = y * W + x;
            uint v = Px[i];
            int vr = (int)(v >> 16 & 255), vg = (int)(v >> 8 & 255), vb = (int)(v & 255);
            if (a > 255) a = 255;
            Px[i] = (uint)((vr + ((r - vr) * a >> 8)) << 16 | (vg + ((g - vg) * a >> 8)) << 8 | (vb + ((b - vb) * a >> 8)));
        }

        protected void Add(int x, int y, int r, int g, int b)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
            int i = y * W + x;
            uint v = Px[i];
            Px[i] = (uint)(Cl((int)(v >> 16 & 255) + r) << 16 | Cl((int)(v >> 8 & 255) + g) << 8 | Cl((int)(v & 255) + b));
        }

        /// <summary>Anti-aliased-ish line (DDA with alpha), optionally additive.</summary>
        protected void Line(double x0, double y0, double x1, double y1, int r, int g, int b, int a, bool add = false)
        {
            double dx = x1 - x0, dy = y1 - y0;
            int n = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (n > 4000) return;
            if (n == 0) n = 1;
            double sx = dx / n, sy = dy / n, x = x0, y = y0;
            for (int i = 0; i <= n; i++)
            {
                int ix = (int)x, iy = (int)y;
                if (add) Add(ix, iy, r * a >> 8, g * a >> 8, b * a >> 8); else Blend(ix, iy, r, g, b, a);
                x += sx; y += sy;
            }
        }

        /// <summary>Soft glowing dot (additive).</summary>
        protected void Glow(double cx, double cy, double radius, int r, int g, int b, double strength)
        {
            int x0 = (int)(cx - radius), x1 = (int)(cx + radius) + 1, y0 = (int)(cy - radius), y1 = (int)(cy + radius) + 1;
            double inv = 1.0 / (radius * radius);
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                {
                    double d = ((x - cx) * (x - cx) + (y - cy) * (y - cy)) * inv;
                    if (d >= 1) continue;
                    double f = (1 - d); f *= f * strength;
                    Add(x, y, (int)(r * f), (int)(g * f), (int)(b * f));
                }
        }

        protected void Circle(double cx, double cy, double rad, int r, int g, int b, int a, bool add = false)
        {
            int n = Math.Max(24, (int)(rad * 6.3));
            double px = cx + rad, py = cy;
            for (int i = 1; i <= n; i++)
            {
                double ang = i * Math.PI * 2 / n, nx = cx + Math.Cos(ang) * rad, ny = cy + Math.Sin(ang) * rad;
                Line(px, py, nx, ny, r, g, b, a, add);
                px = nx; py = ny;
            }
        }

        /// <summary>Vertical gradient between two colours.</summary>
        protected uint[] Gradient(Color top, Color bottom)
        {
            var buf = new uint[W * H];
            for (int y = 0; y < H; y++)
            {
                var c = Rgb(Mix(top, bottom, y / (double)Math.Max(1, H - 1)));
                for (int x = 0; x < W; x++) buf[y * W + x] = c;
            }
            return buf;
        }

        // ---------------- registry of scenes ----------------
        public static Scene Create(string id)
        {
            if (id != null && id.StartsWith("video:")) return new MediaScene(id.Substring(6), image: false);
            if (id != null && id.StartsWith("image:")) return new MediaScene(id.Substring(6), image: true);
            switch (id)
            {
                case "hacker-desk": return new DeskScene { Accent = Shell.ShellThemes.HackerColor };
                case "zen-desk": return new ZenScene();
                case "gaming-desk": return new GameScene();
                case "matrix": case "matrix-hud": return new MatrixScene { Accent = Shell.ShellThemes.HackerColor };
                case "matrix-red": return new MatrixScene { Accent = Color.FromRgb(0xFF, 0x2A, 0x2A) };
                case "matrix-cyan": return new MatrixScene { Accent = Color.FromRgb(0x00, 0xE5, 0xFF) };
                case "terminal-log": return new TerminalLogScene();
                case "radar": return new RadarScene();
                case "starfield": return new StarfieldScene();
                case "plexus": return new PlexusScene();
                case "plexus-green": return new PlexusScene { Accent = Color.FromRgb(0x00, 0xFF, 0x41), Dark = true };
                case "bokeh": return new BokehScene();
                default: return new MatrixScene();
            }
        }
    }

    /// <summary>Pre-rendered characters as alpha masks for fast drawing into the pixel buffer.</summary>
    public sealed class GlyphAtlas
    {
        public readonly int CW, CH;
        public readonly string Chars;
        readonly byte[][] masks;
        static readonly Dictionary<string, GlyphAtlas> cache = new Dictionary<string, GlyphAtlas>();

        public static GlyphAtlas Get(string chars, string font, int px, bool mirror)
        {
            var key = chars.GetHashCode() + "|" + font + "|" + px + "|" + mirror;
            lock (cache)
            {
                if (!cache.TryGetValue(key, out var a)) cache[key] = a = new GlyphAtlas(chars, font, px, mirror);
                return a;
            }
        }

        GlyphAtlas(string chars, string font, int px, bool mirror)
        {
            Chars = chars;
            var tf = new Typeface(new FontFamily(font), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var probe = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, px, Brushes.White, 1.0);
            CW = Math.Max(4, (int)Math.Ceiling(probe.WidthIncludingTrailingWhitespace) + 1);
            CH = Math.Max(6, (int)Math.Ceiling(probe.Height));
            masks = new byte[chars.Length][];
            var dv = new DrawingVisual();
            for (int i = 0; i < chars.Length; i++)
            {
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, CW, CH));
                    var ft = new FormattedText(chars[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, px, Brushes.White, 1.0);
                    if (mirror) { dc.PushTransform(new ScaleTransform(-1, 1, CW / 2.0, 0)); }
                    dc.DrawText(ft, new Point((CW - ft.WidthIncludingTrailingWhitespace) / 2, 0));
                    if (mirror) dc.Pop();
                }
                var rtb = new RenderTargetBitmap(CW, CH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var buf = new int[CW * CH];
                rtb.CopyPixels(buf, CW * 4, 0);
                var m = new byte[CW * CH];
                for (int k = 0; k < buf.Length; k++) m[k] = (byte)((buf[k] >> 8) & 255);
                masks[i] = m;
            }
        }

        /// <summary>Draws glyph number idx at pixel (x,y). With clear=true the cell is wiped first.</summary>
        public void Draw(Scene s, int idx, int x, int y, Color c, double strength = 1, bool clear = true)
        {
            var m = masks[idx % masks.Length];
            int W = s.W, H = s.H;
            var px = s.Px;
            int r = (int)(c.R * strength), g = (int)(c.G * strength), b = (int)(c.B * strength);
            for (int yy = 0; yy < CH; yy++)
            {
                int sy = y + yy;
                if ((uint)sy >= (uint)H) continue;
                int row = sy * W, mrow = yy * CW;
                for (int xx = 0; xx < CW; xx++)
                {
                    int sx = x + xx;
                    if ((uint)sx >= (uint)W) continue;
                    int a = m[mrow + xx];
                    int i = row + sx;
                    if (clear) px[i] = a == 0 ? 0u : (uint)((r * a >> 8) << 16 | (g * a >> 8) << 8 | (b * a >> 8));
                    else if (a > 0)
                    {
                        uint v = px[i];
                        int vr = (int)(v >> 16 & 255), vg = (int)(v >> 8 & 255), vb = (int)(v & 255);
                        vr = Math.Max(vr, r * a >> 8); vg = Math.Max(vg, g * a >> 8); vb = Math.Max(vb, b * a >> 8);
                        px[i] = (uint)(vr << 16 | vg << 8 | vb);
                    }
                }
            }
        }

        public int IndexOf(char ch) { int i = Chars.IndexOf(ch); return i < 0 ? 0 : i; }
    }

    // =====================================================================
    // Matrix digital rain – interactive: drops near the mouse speed up and glow,
    // a click starts a burst of new drops at the pointer.
    // =====================================================================
    /// <summary>Calm, static desktop for the hacker look: dark, a faint grid, no animation. The info panel is drawn on top.</summary>
    sealed class DeskScene : Scene
    {
        public override int Fps => 1;   // only the clock and the system values change
        protected override void Init()
        {
            int ar = Accent.R, ag = Accent.G, ab = Accent.B;
            double cx = W * 0.78, cy = H * 0.3, maxD = Math.Sqrt(W * W + H * H);
            int step = Math.Max(24, W / 60);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxD;
                    double glow = Math.Max(0, 0.9 - d * 1.6) * 0.10;    // soft light behind the info panel
                    bool dot = x % step == 0 && y % step == 0;
                    double k = glow + (dot ? 0.16 : 0);
                    Px[y * W + x] = Rgb((int)(8 + ar * k), (int)(9 + ag * k), (int)(10 + ab * k));
                }
        }
        public override void Step(double dt) { }
    }

    sealed class MatrixScene : Scene
    {
        const string Glyphs = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789Z:.=*+-<>¦|";
        public override int Fps => 40;

        sealed class Layer
        {
            public GlyphAtlas A;
            public int Cols, Rows;
            public double[] Head, Speed;
            public double Bright, Pace;
        }

        Layer front, back;
        Color headColor, trail, backTrail;
        double lastMx = -1, lastMy = -1;
        readonly List<double[]> waves = new List<double[]>();   // x, y, age

        protected override void Init()
        {
            front = MakeLayer(Math.Max(14, H / 44), 1.0, 1.0);
            back = MakeLayer(Math.Max(9, H / 78), 0.42, 0.55);
            headColor = Mix(Accent, Colors.White, 0.85);
            trail = Accent;
            backTrail = Mix(Accent, Colors.Black, 0.35);
            Fill(0);
        }

        Layer MakeLayer(int px, double bright, double pace)
        {
            var l = new Layer { A = GlyphAtlas.Get(Glyphs, "MS Gothic, Consolas", px, true), Bright = bright, Pace = pace };
            l.Cols = W / l.A.CW + 1; l.Rows = H / l.A.CH + 1;
            l.Head = new double[l.Cols]; l.Speed = new double[l.Cols];
            for (int i = 0; i < l.Cols; i++) { l.Head[i] = R.Next(-l.Rows, l.Rows); l.Speed[i] = Speed() * pace; }
            return l;
        }

        double Speed() => 8 + R.NextDouble() * 22;

        double fadeDt;
        int frameNo;

        public override void Step(double dt)
        {
            // fading the whole picture is the expensive part: do it every second frame (looks the same)
            fadeDt += dt;
            if ((frameNo++ & 1) == 0) { Fade((int)(256 * Math.Pow(0.14, fadeDt))); fadeDt = 0; }
            bool m = MouseIn;
            if (m && Click) waves.Add(new[] { Mx, My, 0 });
            Click = false;

            StepLayer(back, dt, m, false);
            StepLayer(front, dt, m, true);

            if (m)
            {
                // code trail behind the moving mouse
                if (lastMx >= 0)
                {
                    double dx = Mx - lastMx, dy = My - lastMy, dist = Math.Sqrt(dx * dx + dy * dy);
                    int steps = (int)(dist / front.A.CH);
                    for (int i = 0; i <= Math.Min(steps, 30); i++)
                    {
                        double f = steps == 0 ? 1 : i / (double)steps;
                        int cx = (int)((lastMx + dx * f) / front.A.CW), cy = (int)((lastMy + dy * f) / front.A.CH);
                        front.A.Draw(this, R.Next(Glyphs.Length), cx * front.A.CW, cy * front.A.CH, Colors.White, 1);
                    }
                }
                // glyphs light up around the pointer
                double rad = H * 0.11;
                for (int k = 0; k < 7; k++)
                {
                    double ang = R.NextDouble() * Math.PI * 2, r = Math.Sqrt(R.NextDouble()) * rad;
                    int cx = (int)((Mx + Math.Cos(ang) * r) / front.A.CW), cy = (int)((My + Math.Sin(ang) * r) / front.A.CH);
                    front.A.Draw(this, R.Next(Glyphs.Length), cx * front.A.CW, cy * front.A.CH, Mix(trail, Colors.White, 1 - r / rad), 0.9);
                }
                lastMx = Mx; lastMy = My;
            }
            else lastMx = -1;

            // click shock waves: a ring of bright code that runs outwards
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                w[2] += dt;
                double radius = w[2] * H * 0.9;
                if (w[2] > 1.4) { waves.RemoveAt(i); continue; }
                int n = (int)(radius * 2 * Math.PI / front.A.CW / 1.5) + 8;
                double strength = 1 - w[2] / 1.4;
                for (int k = 0; k < n; k++)
                {
                    double ang = k * Math.PI * 2 / n;
                    int cx = (int)((w[0] + Math.Cos(ang) * radius) / front.A.CW), cy = (int)((w[1] + Math.Sin(ang) * radius) / front.A.CH);
                    if (cx < 0 || cy < 0 || cx >= front.Cols || cy >= front.Rows) continue;
                    front.A.Draw(this, R.Next(Glyphs.Length), cx * front.A.CW, cy * front.A.CH, Mix(trail, Colors.White, strength), 0.5 + strength * 0.5);
                }
                // the wave also restarts the drops it passes
                int c0 = (int)((w[0] - radius) / front.A.CW), c1 = (int)((w[0] + radius) / front.A.CW);
                foreach (var c in new[] { c0, c1 }) if (c >= 0 && c < front.Cols && R.Next(2) == 0) { front.Head[c] = w[1] / front.A.CH; front.Speed[c] = 40; }
            }
        }

        void StepLayer(Layer l, double dt, bool m, bool isFront)
        {
            var a = l.A;
            int mcol = m ? (int)(Mx / a.CW) : -999;
            int near = isFront ? 5 : 3;
            for (int c = 0; c < l.Cols; c++)
            {
                bool close = Math.Abs(c - mcol) <= near;
                double ny = l.Head[c] + l.Speed[c] * dt * (close ? 2.4 : 1);
                int from = (int)Math.Floor(l.Head[c]), to = (int)Math.Floor(ny);
                for (int cell = from + 1; cell <= to; cell++)
                {
                    if (cell - 1 >= 0 && cell - 1 < l.Rows) a.Draw(this, R.Next(Glyphs.Length), c * a.CW, (cell - 1) * a.CH, isFront ? trail : backTrail, 0.95 * l.Bright);
                    if (cell >= 0 && cell < l.Rows)
                    {
                        a.Draw(this, R.Next(Glyphs.Length), c * a.CW, cell * a.CH, close ? Colors.White : headColor, l.Bright);
                        if (isFront) Glow(c * a.CW + a.CW / 2.0, cell * a.CH + a.CH / 2.0, a.CW * 1.1, trail.R, trail.G, trail.B, close ? 0.55 : 0.25);
                    }
                }
                l.Head[c] = ny;
                if (l.Head[c] > l.Rows + R.Next(0, l.Rows)) { l.Head[c] = -R.Next(0, l.Rows / 2 + 1); l.Speed[c] = Speed() * l.Pace; }
            }
            // glyphs inside the trails flicker
            for (int k = 0; k < l.Cols / 3; k++)
            {
                int c = R.Next(l.Cols), cell = (int)l.Head[c] - R.Next(2, 20);
                if (cell >= 0 && cell < l.Rows) a.Draw(this, R.Next(Glyphs.Length), c * a.CW, cell * a.CH, isFront ? trail : backTrail, (0.45 + R.NextDouble() * 0.4) * l.Bright, clear: false);
            }
        }
    }

    // =====================================================================
    // Terminal log – an endless boot / scan / decrypt session typed live.
    // =====================================================================
    sealed class TerminalLogScene : Scene
    {
        const string Ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~█░▒▓";
        GlyphAtlas atlas;
        int cols, rows, cx, cy;
        string line = ""; int pos; Color lineColor;
        double typeAcc, pause, blink;
        readonly Queue<(string, Color)> pending = new Queue<(string, Color)>();
        string host = Environment.MachineName.ToLowerInvariant(), user = Environment.UserName.ToLowerInvariant();

        protected override void Init()
        {
            atlas = GlyphAtlas.Get(Ascii, "Consolas", Math.Max(12, H / 62), false);
            cols = W / atlas.CW; rows = H / atlas.CH;
            Fill(0);
            cx = 1; cy = 1;
            Queue("WinMorph secure shell v4.2 · " + host, Mix(Accent, Colors.White, 0.6));
            Queue("", Accent);
        }

        void Queue(string s, Color c) => pending.Enqueue((s, c));

        static readonly string[] Services = { "network-manager", "cryptd", "sshd", "firewalld", "sentinel", "kernel-audit", "tor-relay", "vault", "deepscan", "packet-sniffer", "neural-ids", "quantum-rng" };
        static readonly string[] Words = { "handshake", "payload", "checksum", "cipher", "socket", "buffer", "kernel", "sector", "token", "proxy", "tunnel", "daemon" };

        void Generate()
        {
            Color ok = Accent, dim = Mix(Accent, Colors.Black, 0.45), warn = Color.FromRgb(0xFF, 0xB0, 0x00), err = Color.FromRgb(0xFF, 0x3B, 0x3B), white = Mix(Accent, Colors.White, 0.7);
            int kind = R.Next(12);
            switch (kind)
            {
                case 0: case 1: Queue("[  OK  ] Started " + Services[R.Next(Services.Length)] + ".service", ok); break;
                case 2:
                    var sb = new System.Text.StringBuilder("0x" + R.Next(0x10000000).ToString("X8") + "  ");
                    for (int i = 0; i < 16; i++) sb.Append(R.Next(256).ToString("x2")).Append(i == 7 ? "  " : " ");
                    Queue(sb.ToString(), dim); break;
                case 3: Queue(user + "@" + host + ":~$ " + new[] { "nmap -sV 10.0.0." + R.Next(255), "ssh root@" + R.Next(255) + "." + R.Next(255) + ".0.1", "cat /var/log/auth.log | grep fail", "./decrypt --key=" + R.Next(99999).ToString("X5") + " vault.bin", "tail -f /dev/neural", "sudo ./inject --stealth" }[R.Next(6)], white); break;
                case 4: Queue("decrypting " + Words[R.Next(Words.Length)] + " block " + R.Next(4096) + "/4096 [" + new string('█', R.Next(4, 30)).PadRight(30, '░') + "]", ok); break;
                case 5: Queue("[ WARN ] unusual " + Words[R.Next(Words.Length)] + " on port " + R.Next(1, 65535), warn); break;
                case 6: Queue("trace route " + R.Next(255) + "." + R.Next(255) + "." + R.Next(255) + "." + R.Next(255) + " … " + R.Next(4, 30) + " hops, " + R.Next(4, 240) + " ms", dim); break;
                case 7: if (R.Next(4) == 0) { Queue("", ok); Queue(">>> ACCESS GRANTED <<<", white); Queue("", ok); } else Queue("auth " + Words[R.Next(Words.Length)] + "… ok", ok); break;
                case 8: if (R.Next(5) == 0) Queue("[ FAIL ] intrusion attempt blocked from " + R.Next(255) + "." + R.Next(255) + ".x.x", err); else Queue("sync " + R.Next(10, 999) + " MB … done", dim); break;
                case 9: Queue("scan 10.0." + R.Next(255) + "." + R.Next(255) + "  port " + R.Next(20, 9000) + "/tcp  " + (R.Next(3) == 0 ? "open" : "filtered"), ok); break;
                default: Queue("sha256 " + Hex(64), dim); break;
            }
        }

        string Hex(int n) { var c = new char[n]; for (int i = 0; i < n; i++) c[i] = "0123456789abcdef"[R.Next(16)]; return new string(c); }

        void NewLine()
        {
            Cursor(false);
            cx = 1; cy++;
            if (cy >= rows - 1)
            {
                int shift = atlas.CH * W;
                Array.Copy(Px, shift, Px, 0, Px.Length - shift);
                Array.Clear(Px, Px.Length - shift, shift);
                cy = rows - 2;
            }
        }

        void Cursor(bool on)
        {
            int x = cx * atlas.CW, y = cy * atlas.CH;
            if (on) atlas.Draw(this, atlas.IndexOf('█'), x, y, Accent, 0.9);
            else atlas.Draw(this, 0, x, y, Accent, 1);
        }

        public override void Step(double dt)
        {
            if (Click && MouseIn) { Queue("", Accent); Queue("> signal traced to [" + (int)Mx + "," + (int)My + "] … locked", Color.FromRgb(0xFF, 0xB0, 0x00)); }
            Click = false;
            blink += dt;
            if (pause > 0) { pause -= dt; Cursor(((int)(blink * 2.5)) % 2 == 0); return; }
            typeAcc += dt * (line.StartsWith("0x") || line.StartsWith("sha") ? 900 : 220);
            while (typeAcc >= 1)
            {
                typeAcc -= 1;
                if (pos >= line.Length)
                {
                    if (line.Length > 0 || pos > 0) NewLine();
                    if (pending.Count == 0) Generate();
                    var next = pending.Dequeue();
                    line = next.Item1.Length > cols - 3 ? next.Item1.Substring(0, cols - 3) : next.Item1; lineColor = next.Item2; pos = 0;
                    if (R.Next(6) == 0) { pause = 0.2 + R.NextDouble() * 0.7; break; }
                    if (line.Length == 0) { pos = 0; NewLine(); continue; }
                }
                char ch = line[pos++];
                atlas.Draw(this, atlas.IndexOf(ch), cx * atlas.CW, cy * atlas.CH, lineColor, 1);
                cx++;
            }
            Cursor(true);
        }
    }

    // =====================================================================
    // Radar – rotating sweep, targets light up; the mouse is tracked as a target,
    // a click places a lock-on marker.
    // =====================================================================
    sealed class RadarScene : Scene
    {
        public override double Resolution => 0.5;
        uint[] baseLayer;
        double angle, cxp, cyp, rad;
        readonly List<double[]> blips = new List<double[]>();   // angle, dist(0..1), life
        readonly List<double[]> locks = new List<double[]>();   // x, y, t

        protected override void Init()
        {
            cxp = W / 2.0; cyp = H / 2.0; rad = Math.Min(W, H) * 0.44;
            Fill(Rgb(2, 8, 4));
            var dim = Mix(Accent, Colors.Black, 0.72);
            for (int i = 1; i <= 5; i++) Circle(cxp, cyp, rad * i / 5, dim.R, dim.G, dim.B, 255);
            Line(cxp - rad, cyp, cxp + rad, cyp, dim.R, dim.G, dim.B, 200);
            Line(cxp, cyp - rad, cxp, cyp + rad, dim.R, dim.G, dim.B, 200);
            for (int d = 0; d < 360; d += 5)
            {
                double a = d * Math.PI / 180, r0 = rad * (d % 30 == 0 ? 0.94 : 0.97);
                Line(cxp + Math.Cos(a) * r0, cyp + Math.Sin(a) * r0, cxp + Math.Cos(a) * rad, cyp + Math.Sin(a) * rad, dim.R, dim.G, dim.B, 255);
            }
            // faint grid outside the scope
            for (int x = 0; x < W; x += 24) for (int y = 0; y < H; y += 24) Blend(x, y, dim.R, dim.G, dim.B, 160);
            baseLayer = (uint[])Px.Clone();
            for (int i = 0; i < 7; i++) blips.Add(new[] { R.NextDouble() * Math.PI * 2, 0.15 + R.NextDouble() * 0.8, 0 });
        }

        public override void Step(double dt)
        {
            Array.Copy(baseLayer, Px, Px.Length);
            angle = (angle + dt * 1.25) % (Math.PI * 2);
            for (int k = 0; k < 46; k++)
            {
                double a = angle - k * 0.014;
                int al = (int)(120 * Math.Pow(1 - k / 46.0, 2));
                Line(cxp, cyp, cxp + Math.Cos(a) * rad, cyp + Math.Sin(a) * rad, Accent.R, Accent.G, Accent.B, al, add: true);
            }
            if (MouseIn)
            {
                double dx = Mx - cxp, dy = My - cyp, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < rad)
                {
                    Glow(Mx, My, 7, 255, 180, 0, 1.2);
                    if (Click) locks.Add(new[] { Mx, My, 0 });
                }
            }
            Click = false;
            foreach (var b in blips)
            {
                double diff = (angle - b[0] + Math.PI * 4) % (Math.PI * 2);
                if (diff < 0.05) b[2] = 1;
                b[2] = Math.Max(0, b[2] - dt * 0.35);
                if (b[2] > 0.01) Glow(cxp + Math.Cos(b[0]) * b[1] * rad, cyp + Math.Sin(b[0]) * b[1] * rad, 6, Accent.R, Accent.G, Accent.B, b[2] * 1.4);
                if (b[2] == 0 && R.Next(600) == 0) { b[0] = R.NextDouble() * Math.PI * 2; b[1] = 0.15 + R.NextDouble() * 0.8; }
            }
            for (int i = locks.Count - 1; i >= 0; i--)
            {
                var l = locks[i]; l[2] += dt;
                if (l[2] > 8) { locks.RemoveAt(i); continue; }
                double s = 10 + 4 * Math.Sin(l[2] * 6);
                int a = (int)(255 * Math.Min(1, (8 - l[2]) / 2));
                Line(l[0] - s, l[1], l[0] - s / 2, l[1], 255, 60, 60, a); Line(l[0] + s / 2, l[1], l[0] + s, l[1], 255, 60, 60, a);
                Line(l[0], l[1] - s, l[0], l[1] - s / 2, 255, 60, 60, a); Line(l[0], l[1] + s / 2, l[0], l[1] + s, 255, 60, 60, a);
                Circle(l[0], l[1], s * 0.8, 255, 60, 60, a);
            }
        }
    }

    // =====================================================================
    // Starfield / warp – the flight direction follows the mouse, a click jumps to warp speed.
    // =====================================================================
    sealed class StarfieldScene : Scene
    {
        public override double Resolution => 0.75;
        double[] sx, sy, sz;
        double cx, cy, warp = 1;

        protected override void Init()
        {
            int n = Math.Min(1400, W * H / 1500);
            sx = new double[n]; sy = new double[n]; sz = new double[n];
            for (int i = 0; i < n; i++) Reset(i, R.NextDouble());
            cx = W / 2.0; cy = H / 2.0;
        }

        void Reset(int i, double z) { sx[i] = R.NextDouble() * 2 - 1; sy[i] = R.NextDouble() * 2 - 1; sz[i] = z; }

        public override void Step(double dt)
        {
            Fade((int)(256 * Math.Pow(0.0005, dt)));
            double tx = W / 2.0, ty = H / 2.0;
            if (MouseIn) { tx = W / 2.0 + (Mx - W / 2.0) * 0.6; ty = H / 2.0 + (My - H / 2.0) * 0.6; }
            cx += (tx - cx) * Math.Min(1, dt * 1.5); cy += (ty - cy) * Math.Min(1, dt * 1.5);
            if (Click && MouseIn) warp = 7;
            Click = false;
            warp += (1 - warp) * Math.Min(1, dt * 0.8);
            double f = Math.Max(W, H) * 0.5, v = 0.18 * warp * dt;
            for (int i = 0; i < sz.Length; i++)
            {
                double oz = sz[i], nz = oz - v;
                if (nz <= 0.01) { Reset(i, 1); continue; }
                double x0 = cx + sx[i] / oz * f, y0 = cy + sy[i] / oz * f, x1 = cx + sx[i] / nz * f, y1 = cy + sy[i] / nz * f;
                sz[i] = nz;
                if (x1 < 0 || x1 >= W || y1 < 0 || y1 >= H) { Reset(i, 1); continue; }
                int b = (int)(255 * Math.Min(1, (1 - nz) * 1.3));
                Line(x0, y0, x1, y1, 200, 220, 255, b, add: true);
            }
        }
    }

    // =====================================================================
    // Plexus network – particles connected by lines; the mouse pulls lines and
    // pushes particles, a click sends out a shock wave.
    // =====================================================================
    sealed class PlexusScene : Scene
    {
        public bool Dark;
        uint[] bg;
        double[] px, py, vx, vy;
        double linkDist;

        public PlexusScene() { Accent = Color.FromRgb(0x38, 0xD9, 0xFF); }

        protected override void Init()
        {
            bg = Dark ? Gradient(Color.FromRgb(0, 6, 2), Color.FromRgb(0, 0, 0)) : Gradient(Color.FromRgb(4, 10, 26), Color.FromRgb(10, 4, 30));
            int n = Math.Max(40, Math.Min(220, W * H / 14000));
            px = new double[n]; py = new double[n]; vx = new double[n]; vy = new double[n];
            for (int i = 0; i < n; i++) { px[i] = R.NextDouble() * W; py[i] = R.NextDouble() * H; vx[i] = (R.NextDouble() - 0.5) * 30; vy[i] = (R.NextDouble() - 0.5) * 30; }
            linkDist = Math.Min(W, H) * 0.14;
        }

        public override void Step(double dt)
        {
            Array.Copy(bg, Px, Px.Length);
            bool m = MouseIn;
            double ld2 = linkDist * linkDist;
            for (int i = 0; i < px.Length; i++)
            {
                if (m)
                {
                    double dx = px[i] - Mx, dy = py[i] - My, d2 = dx * dx + dy * dy;
                    if (Click && d2 < ld2 * 5) { double d = Math.Sqrt(d2) + 1; vx[i] += dx / d * 260; vy[i] += dy / d * 260; }
                    else if (d2 < ld2) { double d = Math.Sqrt(d2) + 1; vx[i] += dx / d * 60 * dt; vy[i] += dy / d * 60 * dt; }
                }
                double sp = Math.Sqrt(vx[i] * vx[i] + vy[i] * vy[i]);
                if (sp > 40) { vx[i] *= 1 - Math.Min(0.9, dt * 1.5); vy[i] *= 1 - Math.Min(0.9, dt * 1.5); }
                px[i] += vx[i] * dt; py[i] += vy[i] * dt;
                if (px[i] < 0) { px[i] = 0; vx[i] = Math.Abs(vx[i]); } else if (px[i] >= W) { px[i] = W - 1; vx[i] = -Math.Abs(vx[i]); }
                if (py[i] < 0) { py[i] = 0; vy[i] = Math.Abs(vy[i]); } else if (py[i] >= H) { py[i] = H - 1; vy[i] = -Math.Abs(vy[i]); }
            }
            Click = false;
            for (int i = 0; i < px.Length; i++)
            {
                for (int j = i + 1; j < px.Length; j++)
                {
                    double dx = px[i] - px[j], dy = py[i] - py[j], d2 = dx * dx + dy * dy;
                    if (d2 < ld2) Line(px[i], py[i], px[j], py[j], Accent.R, Accent.G, Accent.B, (int)(110 * (1 - d2 / ld2)), add: true);
                }
                if (m)
                {
                    double dx = px[i] - Mx, dy = py[i] - My, d2 = dx * dx + dy * dy;
                    if (d2 < ld2 * 2.2) Line(px[i], py[i], Mx, My, 255, 255, 255, (int)(150 * (1 - d2 / (ld2 * 2.2))), add: true);
                }
                Glow(px[i], py[i], 3.2, Accent.R, Accent.G, Accent.B, 1.3);
            }
            if (m) Glow(Mx, My, 6, 255, 255, 255, 0.9);
        }
    }

    // =====================================================================
    // Bokeh – soft lights drifting upwards, they move out of the way of the mouse.
    // =====================================================================
    sealed class BokehScene : Scene
    {
        public override double Resolution => 0.4;
        uint[] bg;
        double[] x, y, r, s, ph; Color[] col;
        protected override void Init()
        {
            bg = Gradient(Color.FromRgb(10, 12, 30), Color.FromRgb(30, 12, 20));
            int n = 70;
            x = new double[n]; y = new double[n]; r = new double[n]; s = new double[n]; ph = new double[n]; col = new Color[n];
            var palette = new[] { Color.FromRgb(255, 200, 90), Color.FromRgb(90, 220, 255), Color.FromRgb(255, 140, 90), Color.FromRgb(160, 255, 200) };
            for (int i = 0; i < n; i++) { x[i] = R.NextDouble() * W; y[i] = R.NextDouble() * H; r[i] = H * (0.02 + R.NextDouble() * 0.07); s[i] = 4 + R.NextDouble() * 14; ph[i] = R.NextDouble() * 6; col[i] = palette[R.Next(palette.Length)]; }
        }
        public override void Step(double dt)
        {
            Array.Copy(bg, Px, Px.Length);
            for (int i = 0; i < x.Length; i++)
            {
                y[i] -= s[i] * dt; x[i] += Math.Sin(T * 0.4 + ph[i]) * 6 * dt;
                if (MouseIn) { double dx = x[i] - Mx, dy = y[i] - My, d = Math.Sqrt(dx * dx + dy * dy) + 1; if (d < H * 0.18) { x[i] += dx / d * 40 * dt; y[i] += dy / d * 40 * dt; } }
                if (y[i] < -r[i]) { y[i] = H + r[i]; x[i] = R.NextDouble() * W; }
                double tw = 0.35 + 0.25 * Math.Sin(T * 0.9 + ph[i] * 3);
                Glow(x[i], y[i], r[i], col[i].R, col[i].G, col[i].B, tw);
            }
            Click = false;
        }
    }

}

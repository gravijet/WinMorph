using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Shell
{
    /// <summary>
    /// The look's mouse pointers. They are set for this sign-in only (SetSystemCursor): nothing is written to
    /// the registry, and <see cref="Reset"/> (or signing out) brings back the user's own pointer scheme.
    /// Windows 7: the Aero pointers that ship with Windows. Hacker: pointers drawn in the hacker colour.
    /// Zen: rounded ink pointers with a white rim, koi orange for links.
    /// </summary>
    internal static class CursorSkin
    {
        [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cursor, uint id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadCursorFromFile(string file);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

        const uint NORMAL = 32512, IBEAM = 32513, WAIT = 32514, CROSS = 32515, UP = 32516, SIZENWSE = 32642, SIZENESW = 32643, SIZEWE = 32644, SIZENS = 32645, SIZEALL = 32646, NO = 32648, HAND = 32649, APPSTARTING = 32650, HELP = 32651, PIN = 32671, PERSON = 32672;

        /// <summary>The user's own pointers again (re-read from their scheme in the registry).</summary>
        public static void Reset()
        {
            try { SystemParametersInfo(0x0057 /* SPI_SETCURSORS */, 0, IntPtr.Zero, 0); } catch { }
        }

        public static void Apply(ShellTheme t)
        {
            try
            {
                if (t.Id == "win7") Seven();
                else if (t.Id == "terminal") Hacker(t.Accent);
                else if (t.Id == "zen") Zen();
                else if (t.Id == "macos") Mac();
                else Reset();
            }
            catch { Reset(); }
        }

        static void Set(string file, uint id)
        {
            if (!File.Exists(file)) return;
            var h = LoadCursorFromFile(file);
            if (h != IntPtr.Zero) SetSystemCursor(h, id);   // takes ownership of the handle
        }

        static void Seven()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Cursors");
            foreach (var (file, id) in new[]
            {
                ("aero_arrow.cur", NORMAL), ("aero_link.cur", HAND), ("aero_busy.ani", WAIT), ("aero_working.ani", APPSTARTING), ("aero_helpsel.cur", HELP),
                ("aero_ns.cur", SIZENS), ("aero_ew.cur", SIZEWE), ("aero_nwse.cur", SIZENWSE), ("aero_nesw.cur", SIZENESW), ("aero_move.cur", SIZEALL),
                ("aero_unavail.cur", NO), ("aero_up.cur", UP), ("aero_pin.cur", PIN), ("aero_person.cur", PERSON),
            }) Set(Path.Combine(dir, file), id);
        }

        [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr h);

        /// <summary>Self-test: a generated pointer file is a valid cursor.</summary>
        internal static bool SelfCheck(string file)
        {
            try
            {
                WriteCur(file, Arrow(Color.FromRgb(0, 255, 65), false), new Point(1, 1), 32);
                var h = LoadCursorFromFile(file);
                if (h == IntPtr.Zero) return false;
                DestroyCursor(h);
                return true;
            }
            catch { return false; }
            finally { try { File.Delete(file); } catch { } }
        }

        // ---------------- hacker pointers ----------------
        static void Hacker(Color g)
        {
            int size = Math.Max(32, GetSystemMetrics(13 /* SM_CXCURSOR */));
            var dir = Path.Combine(Core.Backup.DataDir, "cursors", "hacker-" + g.R.ToString("X2") + g.G.ToString("X2") + g.B.ToString("X2") + "-" + size + "-v1");
            Directory.CreateDirectory(dir);
            var shapes = new List<(string name, uint id, Func<DrawingGroup> draw, Point hot)>
            {
                ("arrow", NORMAL, () => Arrow(g, false), new Point(1, 1)),
                ("link", HAND, () => Arrow(g, true), new Point(1, 1)),
                ("beam", IBEAM, () => Beam(g), new Point(16, 16)),
                ("wait", WAIT, () => Ring(g, false), new Point(16, 16)),
                ("working", APPSTARTING, () => Ring(g, true), new Point(1, 1)),
                ("cross", CROSS, () => Cross(g), new Point(16, 16)),
                ("ns", SIZENS, () => Double(g, 90), new Point(16, 16)),
                ("we", SIZEWE, () => Double(g, 0), new Point(16, 16)),
                ("nwse", SIZENWSE, () => Double(g, 45), new Point(16, 16)),
                ("nesw", SIZENESW, () => Double(g, -45), new Point(16, 16)),
                ("move", SIZEALL, () => Move(g), new Point(16, 16)),
                ("no", NO, () => No(g), new Point(16, 16)),
            };
            foreach (var (name, id, draw, hot) in shapes)
            {
                var file = Path.Combine(dir, name + ".cur");
                if (!File.Exists(file)) WriteCur(file, draw(), hot, size);
                Set(file, id);
            }
        }

        // ---------------- Zen pointers: a rounded ink arrow with a white rim, koi orange for links ----------------
        static void Zen()
        {
            int size = Math.Max(32, GetSystemMetrics(13 /* SM_CXCURSOR */));
            var dir = Path.Combine(Core.Backup.DataDir, "cursors", "zen-" + size + "-v1");
            Directory.CreateDirectory(dir);
            Color ink = Color.FromRgb(0x1C, 0x2A, 0x2D), pond = Color.FromRgb(0x0E, 0x7C, 0x78), koi = Color.FromRgb(0xF0, 0x62, 0x1F);
            var shapes = new List<(string name, uint id, Func<DrawingGroup> draw, Point hot)>
            {
                ("arrow", NORMAL, () => SoftArrow(ink, null), new Point(2, 2)),
                ("link", HAND, () => SoftArrow(ink, koi), new Point(2, 2)),
                ("working", APPSTARTING, () => SoftArrow(ink, pond), new Point(2, 2)),
                ("wait", WAIT, () => Pebble(pond), new Point(16, 16)),
                ("beam", IBEAM, () => Beam(pond), new Point(16, 16)),
                ("ns", SIZENS, () => Double(pond, 90), new Point(16, 16)),
                ("we", SIZEWE, () => Double(pond, 0), new Point(16, 16)),
                ("nwse", SIZENWSE, () => Double(pond, 45), new Point(16, 16)),
                ("nesw", SIZENESW, () => Double(pond, -45), new Point(16, 16)),
                ("move", SIZEALL, () => Move(pond), new Point(16, 16)),
                ("no", NO, () => No(Color.FromRgb(0xC9, 0x30, 0x2A)), new Point(16, 16)),
            };
            foreach (var (name, id, draw, hot) in shapes)
            {
                var file = Path.Combine(dir, name + ".cur");
                if (!File.Exists(file)) WriteCur(file, draw(), hot, size);
                Set(file, id);
            }
        }

        static DrawingGroup SoftArrow(Color ink, Color? dot)
        {
            var arrow = Geo("M2.5,2.5 C2.5,2.5 2.5,20 2.5,20.5 C2.5,21.6 3.6,22 4.4,21.2 L8.2,17.4 L11,23.6 C11.4,24.5 12.3,24.8 13.1,24.4 C13.9,24 14.2,23.1 13.8,22.3 L11.1,16.2 L16.3,16.2 C17.4,16.2 17.9,14.9 17.1,14.2 Z");
            var pen = new Pen(Frozen(Color.FromRgb(255, 255, 255)), 1.6) { LineJoin = PenLineJoin.Round }; pen.Freeze();
            var g = Group(new GeometryDrawing(Frozen(ink), pen, arrow));
            if (dot.HasValue) g.Children.Add(new GeometryDrawing(Frozen(dot.Value), new Pen(Frozen(Color.FromRgb(255, 255, 255)), 1.2), new EllipseGeometry(new Point(21, 23), 4.2, 4.2)));
            return g;
        }

        static DrawingGroup Pebble(Color c)
        {
            var pen = new Pen(Frozen(Color.FromRgb(255, 255, 255)), 1.6); pen.Freeze();
            return Group(new GeometryDrawing(null, new Pen(Frozen(c), 3.2), new EllipseGeometry(new Point(16, 16), 9, 9)),
                         new GeometryDrawing(Frozen(Color.FromRgb(0xF0, 0x62, 0x1F)), pen, new EllipseGeometry(new Point(16, 7), 3.2, 3.2)));
        }

        // all shapes are drawn on a 32 x 32 grid: a black body with a thin line in the hacker colour
        static readonly Brush Black = Frozen(Color.FromArgb(235, 0, 0, 0));
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Pen Line(Color c, double w) { var p = new Pen(Frozen(c), w) { LineJoin = PenLineJoin.Miter, StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square }; p.Freeze(); return p; }
        static DrawingGroup Group(params Drawing[] d) { var g = new DrawingGroup(); foreach (var x in d) g.Children.Add(x); g.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 32, 32)); return g; }
        static Geometry Geo(string s) => Geometry.Parse(s);

        static DrawingGroup Arrow(Color g, bool filled)
        {
            var arrow = Geo("M1.5,1.5 L1.5,22 L6.5,17.2 L10,25.5 L13.4,24 L10,15.8 L16.8,15.8 Z");
            return Group(new GeometryDrawing(filled ? Frozen(g) : Black, Line(filled ? Color.FromRgb(0, 0, 0) : g, 1.3), arrow));
        }

        static DrawingGroup Beam(Color g) => Group(
            new GeometryDrawing(null, Line(Color.FromRgb(0, 0, 0), 4), Geo("M12,6 L20,6 M16,6 L16,26 M12,26 L20,26")),
            new GeometryDrawing(null, Line(g, 1.6), Geo("M12,6 L20,6 M16,6 L16,26 M12,26 L20,26")));

        static DrawingGroup Ring(Color g, bool withArrow)
        {
            // a block spinner frame: three lit quarters, one dark, like a terminal progress glyph
            var parts = new List<Drawing>();
            double cx = withArrow ? 22 : 16, cy = withArrow ? 22 : 16, r = withArrow ? 7 : 10;
            parts.Add(new GeometryDrawing(Black, null, new EllipseGeometry(new Point(cx, cy), r + 3, r + 3)));
            parts.Add(new GeometryDrawing(null, Line(Color.FromArgb(110, g.R, g.G, g.B), 2.6), new EllipseGeometry(new Point(cx, cy), r, r)));
            var arc = new PathGeometry();
            var f = new PathFigure { StartPoint = new Point(cx, cy - r) };
            f.Segments.Add(new ArcSegment(new Point(cx - r, cy), new Size(r, r), 0, true, SweepDirection.Clockwise, true));
            arc.Figures.Add(f);
            parts.Add(new GeometryDrawing(null, Line(g, 2.6), arc));
            var all = new List<Drawing>();
            if (withArrow) all.AddRange(Arrow(g, false).Children);
            all.AddRange(parts);
            return Group(all.ToArray());
        }

        static DrawingGroup Cross(Color g) => Group(
            new GeometryDrawing(null, Line(Color.FromRgb(0, 0, 0), 3.6), Geo("M16,3 L16,12 M16,20 L16,29 M3,16 L12,16 M20,16 L29,16")),
            new GeometryDrawing(null, Line(g, 1.4), Geo("M16,3 L16,12 M16,20 L16,29 M3,16 L12,16 M20,16 L29,16")),
            new GeometryDrawing(Frozen(g), null, new RectangleGeometry(new Rect(15, 15, 2, 2))));

        static DrawingGroup Double(Color g, double angle)
        {
            var shape = Geo("M2,16 L8,10 L8,14 L24,14 L24,10 L30,16 L24,22 L24,18 L8,18 L8,22 Z");
            var d = new GeometryDrawing(Black, Line(g, 1.2), shape);
            var grp = Group(d);
            grp.Transform = new RotateTransform(angle, 16, 16);
            return Group(grp);
        }

        static DrawingGroup Move(Color g)
        {
            var a = Double(g, 0); var b = Double(g, 90);
            return Group(a, b);
        }

        static DrawingGroup No(Color g) => Group(
            new GeometryDrawing(Black, Line(g, 2.2), new EllipseGeometry(new Point(16, 16), 11, 11)),
            new GeometryDrawing(null, Line(g, 2.2), Geo("M8.5,8.5 L23.5,23.5")));

        // ---------------- macOS pointers: black arrow with a white rim, the white hand, the thin I-beam, the spinning wheel ----------------
        static void Mac()
        {
            int size = Math.Max(32, GetSystemMetrics(13 /* SM_CXCURSOR */));
            var dir = Path.Combine(Core.Backup.DataDir, "cursors", "mac-" + size + "-v1");
            Directory.CreateDirectory(dir);
            var white = Color.FromRgb(255, 255, 255);
            var shapes = new List<(string name, uint id, Func<DrawingGroup> draw, Point hot)>
            {
                ("arrow", NORMAL, MacArrow, new Point(4, 2.5)),
                ("link", HAND, MacHand, new Point(13, 3)),
                ("beam", IBEAM, MacBeam, new Point(16, 16)),
                ("ns", SIZENS, () => Double(white, 90), new Point(16, 16)),
                ("we", SIZEWE, () => Double(white, 0), new Point(16, 16)),
                ("nwse", SIZENWSE, () => Double(white, 45), new Point(16, 16)),
                ("nesw", SIZENESW, () => Double(white, -45), new Point(16, 16)),
                ("move", SIZEALL, () => Move(white), new Point(16, 16)),
                ("no", NO, MacNo, new Point(16, 16)),
                ("cross", CROSS, () => Cross(white), new Point(16, 16)),
            };
            foreach (var (name, id, draw, hot) in shapes)
            {
                var file = Path.Combine(dir, name + ".cur");
                if (!File.Exists(file)) WriteCur(file, draw(), hot, size);
                Set(file, id);
            }
            // the spinning wheel, animated
            var wait = Path.Combine(dir, "wait.ani");
            if (!File.Exists(wait)) WriteAni(wait, Enumerable.Range(0, 8).Select(i => Wheel(i * 45.0 / 1, 16, 16, 11)).ToList(), new Point(16, 16), size, 4);
            Set(wait, WAIT);
            var working = Path.Combine(dir, "working.ani");
            if (!File.Exists(working)) WriteAni(working, Enumerable.Range(0, 8).Select(i => { var g = MacArrow(); g.Children.Add(Wheel(i * 45.0, 23, 23, 7)); return g; }).ToList(), new Point(4, 2.5), size, 4);
            Set(working, APPSTARTING);
        }

        static DrawingGroup MacArrow()
        {
            var arrow = Geo("M4,2.5 L4,24 L9,19.4 L12.4,27.2 C12.6,27.7 13.1,27.9 13.6,27.7 L15.7,26.8 C16.2,26.6 16.4,26.1 16.2,25.6 L12.8,17.9 L19.4,17.9 Z");
            var rim = new Pen(Frozen(Color.FromRgb(255, 255, 255)), 1.5) { LineJoin = PenLineJoin.Round }; rim.Freeze();
            var shadow = new DrawingGroup { Transform = new TranslateTransform(0.6, 1.2), Opacity = 0.32 };
            shadow.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0, 0, 0)), new Pen(Frozen(Color.FromRgb(0, 0, 0)), 2.2), arrow));
            return Group(shadow, new GeometryDrawing(Frozen(Color.FromRgb(0, 0, 0)), rim, arrow));
        }

        static DrawingGroup MacHand()
        {
            var hand = Geo("M11,4.5 C11,3.4 11.9,2.6 13,2.6 C14.1,2.6 15,3.4 15,4.5 L15,12.8 C15.3,12.1 16,11.6 16.9,11.6 C18,11.6 18.8,12.4 18.8,13.5 L18.8,14.2 C19.1,13.6 19.8,13.1 20.6,13.1 C21.7,13.1 22.5,13.9 22.5,15 L22.5,15.8 C22.8,15.3 23.4,14.9 24.2,14.9 C25.3,14.9 26,15.7 26,16.8 L26,22.5 C26,26.6 23,29.6 19,29.6 L16.4,29.6 C14,29.6 12.4,28.6 11,26.8 L6.2,20.6 C5.5,19.7 5.7,18.5 6.6,17.8 C7.5,17.1 8.7,17.3 9.4,18.1 L11,20 Z");
            var line = new Pen(Frozen(Color.FromRgb(0, 0, 0)), 1.3) { LineJoin = PenLineJoin.Round }; line.Freeze();
            var thin = new Pen(Frozen(Color.FromRgb(0, 0, 0)), 1.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; thin.Freeze();
            var shadow = new DrawingGroup { Transform = new TranslateTransform(0.6, 1.2), Opacity = 0.28 };
            shadow.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0, 0, 0)), null, hand));
            return Group(shadow, new GeometryDrawing(Frozen(Color.FromRgb(255, 255, 255)), line, hand), new GeometryDrawing(null, thin, Geo("M15,13 L15,18.6 M18.8,14.4 L18.8,18.6 M22.5,16 L22.5,18.6")));
        }

        static DrawingGroup MacBeam()
        {
            var shape = Geo("M12.5,5 C14.4,5 15.4,5.6 16,6.6 C16.6,5.6 17.6,5 19.5,5 M16,6.6 L16,25.4 M12.5,27 C14.4,27 15.4,26.4 16,25.4 C16.6,26.4 17.6,27 19.5,27 M13.6,16 L18.4,16");
            var outer = new Pen(Frozen(Color.FromRgb(255, 255, 255)), 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; outer.Freeze();
            var inner = new Pen(Frozen(Color.FromRgb(0, 0, 0)), 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; inner.Freeze();
            return Group(new GeometryDrawing(null, outer, shape), new GeometryDrawing(null, inner, shape));
        }

        static DrawingGroup MacNo()
        {
            var outer = new Pen(Frozen(Color.FromRgb(255, 255, 255)), 5.4); outer.Freeze();
            var inner = new Pen(Frozen(Color.FromRgb(0, 0, 0)), 2.6); inner.Freeze();
            var ring = new EllipseGeometry(new Point(16, 16), 10, 10);
            var bar = Geo("M9,9 L23,23");
            return Group(new GeometryDrawing(null, outer, ring), new GeometryDrawing(null, outer, bar), new GeometryDrawing(null, inner, ring), new GeometryDrawing(null, inner, bar));
        }

        /// <summary>The spinning wheel: six glossy colour sectors, turned by the given angle.</summary>
        static DrawingGroup Wheel(double angle, double cx, double cy, double r)
        {
            var cols = new[] { Color.FromRgb(0x3E, 0xC2, 0x5F), Color.FromRgb(0x2D, 0xB7, 0xE8), Color.FromRgb(0x3D, 0x6F, 0xE6), Color.FromRgb(0xB0, 0x4F, 0xD6), Color.FromRgb(0xE8, 0x45, 0x3C), Color.FromRgb(0xF2, 0x8C, 0x28), Color.FromRgb(0xF7, 0xC7, 0x44) };
            var g = new DrawingGroup();
            int n = cols.Length;
            for (int i = 0; i < n; i++)
            {
                double a0 = (angle + i * 360.0 / n) * Math.PI / 180, a1 = (angle + (i + 1) * 360.0 / n) * Math.PI / 180;
                var sg = new StreamGeometry();
                using (var c = sg.Open())
                {
                    c.BeginFigure(new Point(cx, cy), true, true);
                    c.LineTo(new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), true, true);
                    c.ArcTo(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                }
                sg.Freeze();
                var fill = new RadialGradientBrush(Color.FromRgb((byte)Math.Min(255, cols[i].R + 60), (byte)Math.Min(255, cols[i].G + 60), (byte)Math.Min(255, cols[i].B + 60)), cols[i]) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                fill.Freeze();
                g.Children.Add(new GeometryDrawing(fill, null, sg));
            }
            var gloss = new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { GradientOrigin = new Point(0.35, 0.25), Center = new Point(0.4, 0.3), RadiusX = 0.6, RadiusY = 0.6 };
            gloss.Freeze();
            g.Children.Add(new GeometryDrawing(gloss, new Pen(Frozen(Color.FromArgb(150, 40, 40, 40)), 0.7), new EllipseGeometry(new Point(cx, cy), r, r)));
            return g;
        }

        /// <summary>An animated cursor (RIFF ACON): each frame is a complete .cur.</summary>
        static void WriteAni(string file, List<DrawingGroup> frames, Point hot, int size, int jiffies)
        {
            var cur = frames.Select(f => CurBytes(f, hot, size)).ToList();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                void Tag(string s) => w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                Tag("RIFF"); w.Write(0); Tag("ACON");
                Tag("anih"); w.Write(36); w.Write(36); w.Write(frames.Count); w.Write(frames.Count); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(jiffies); w.Write(1);
                Tag("LIST"); long listSizeAt = ms.Position; w.Write(0); Tag("fram");
                foreach (var c in cur) { Tag("icon"); w.Write(c.Length); w.Write(c); if (c.Length % 2 == 1) w.Write((byte)0); }
                long end = ms.Position;
                ms.Position = listSizeAt; w.Write((int)(end - listSizeAt - 4));
                ms.Position = 4; w.Write((int)(end - 8));
                File.WriteAllBytes(file, ms.ToArray());
            }
        }

        static void WriteCur(string file, DrawingGroup art, Point hot, int size) => File.WriteAllBytes(file, CurBytes(art, hot, size));

        /// <summary>A .cur file with 32-bit images in several sizes (Windows picks the one for the pointer size).</summary>
        static byte[] CurBytes(DrawingGroup art, Point hot, int size)
        {
            var sizes = new SortedSet<int> { 32, 48, 64, size };
            var images = new List<(int s, byte[] dib)>();
            foreach (var s in sizes)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(s / 32.0, s / 32.0));
                    dc.DrawDrawing(art);
                }
                var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                var px = new byte[s * s * 4];
                bmp.CopyPixels(px, s * 4, 0);
                // un-premultiply (cursor DIBs are straight alpha), rows bottom-up
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32); w.Write(0); w.Write(s * s * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                    for (int y = s - 1; y >= 0; y--)
                        for (int x = 0; x < s; x++)
                        {
                            int i = (y * s + x) * 4; byte a = px[i + 3];
                            if (a == 0) { w.Write(0); continue; }
                            w.Write((byte)Math.Min(255, px[i] * 255 / a)); w.Write((byte)Math.Min(255, px[i + 1] * 255 / a)); w.Write((byte)Math.Min(255, px[i + 2] * 255 / a)); w.Write(a);
                        }
                    int maskRow = ((s + 31) / 32) * 4;
                    w.Write(new byte[maskRow * s]);   // AND mask: all zero, the alpha channel decides
                    images.Add((s, ms.ToArray()));
                }
            }
            using (var fs = new MemoryStream())
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)2); w.Write((short)images.Count);
                int offset = 6 + 16 * images.Count;
                foreach (var (s, dib) in images)
                {
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)Math.Round(hot.X * s / 32.0)); w.Write((short)Math.Round(hot.Y * s / 32.0));
                    w.Write(dib.Length); w.Write(offset);
                    offset += dib.Length;
                }
                foreach (var (_, dib) in images) w.Write(dib);
                w.Flush();
                return fs.ToArray();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Writes a .cur file with 32-bit images in several sizes (Windows picks the one for the pointer size).</summary>
        static void WriteCur(string file, DrawingGroup art, Point hot, int size)
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
            using (var fs = File.Create(file))
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
            }
        }
    }
}

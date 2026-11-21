using System;
using System.Windows;
using System.Windows.Media;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The tray symbols of a look, drawn by hand instead of Windows 11's icon font: Windows 7's white bars,
    /// speaker and battery with a dark rim (readable on the glass bar), Zen's rounded ink strokes.
    /// </summary>
    internal sealed class TrayGlyph : FrameworkElement
    {
        public enum Kind { Wifi, Wired, Offline, Sound, Battery }
        public Kind What;
        public int Level;          // wifi 0..4, sound 0..100, battery 0..100
        public bool Muted, Charging;
        readonly bool seven;
        readonly Color ink;

        public TrayGlyph(Kind kind, bool sevenStyle, Color color, double size = 16)
        {
            What = kind; seven = sevenStyle; ink = color;
            Width = size * (kind == Kind.Battery && !sevenStyle ? 1.5 : 1.25); Height = size;
            VerticalAlignment = VerticalAlignment.Center;
            SnapsToDevicePixels = true;
        }

        public void Set(Kind kind, int level, bool muted = false, bool charging = false)
        {
            if (kind == What && level == Level && muted == Muted && charging == Charging) return;
            What = kind; Level = level; Muted = muted; Charging = charging;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var fg = B(ink);
            var dim = B(A(ink, seven ? (byte)90 : (byte)70));
            var rim = new Pen(B(0x000000, 120), 1.6);
            Pen Stroke(Brush b, double t) => new Pen(b, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            switch (What)
            {
                case Kind.Wifi:
                case Kind.Offline:
                {
                    if (seven)
                    {
                        // five rising bars, the lit ones white
                        int lit = What == Kind.Offline ? 0 : Math.Max(1, Math.Min(5, (int)Math.Round(Level * 1.25)));
                        double bw = w / 6.2;
                        for (int i = 0; i < 5; i++)
                        {
                            double bh = h * (0.28 + i * 0.18);
                            var r = new Rect(i * bw * 1.25, h - bh, bw, bh);
                            dc.DrawRectangle(i < lit ? fg : dim, rim, r);
                        }
                        if (What == Kind.Offline) dc.DrawEllipse(B(0xD6A000), rim, new Point(w * 0.82, h * 0.25), h * 0.2, h * 0.2);
                        break;
                    }
                    // Zen: a stone dropped in water – three arcs above a dot
                    var c = new Point(w / 2, h * 0.92);
                    int on = What == Kind.Offline ? 0 : Math.Max(1, Math.Min(3, (int)Math.Ceiling(Level * 0.75)));
                    for (int i = 0; i < 3; i++)
                    {
                        double r = h * (0.32 + i * 0.27);
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            double a1 = -135 * Math.PI / 180, a2 = -45 * Math.PI / 180;
                            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), false, false);
                            ctx.ArcTo(new Point(c.X + r * Math.Cos(a2), c.Y + r * Math.Sin(a2)), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                        }
                        dc.DrawGeometry(null, Stroke(i < on ? fg : dim, 1.7), g);
                    }
                    dc.DrawEllipse(fg, null, new Point(c.X, c.Y - 1), 1.6, 1.6);
                    if (What == Kind.Offline) dc.DrawLine(Stroke(B(ZenStyle.Momiji), 1.7), new Point(w * 0.2, h * 0.15), new Point(w * 0.8, h * 0.85));
                    break;
                }
                case Kind.Wired:
                {
                    // a small screen with a cable
                    var scr = new Rect(w * 0.12, h * 0.12, w * 0.76, h * 0.55);
                    if (seven) { dc.DrawRectangle(V((0, C(0xBFE3FF)), (1, C(0x3A78C4))), rim, scr); dc.DrawRectangle(fg, rim, new Rect(w * 0.38, h * 0.7, w * 0.24, h * 0.22)); }
                    else { dc.DrawRoundedRectangle(null, Stroke(fg, 1.6), scr, 2.5, 2.5); dc.DrawLine(Stroke(fg, 1.6), new Point(w / 2, h * 0.67), new Point(w / 2, h * 0.92)); }
                    break;
                }
                case Kind.Sound:
                {
                    var body = new StreamGeometry();
                    using (var ctx = body.Open())
                    {
                        ctx.BeginFigure(new Point(w * 0.06, h * 0.36), true, true);
                        ctx.LineTo(new Point(w * 0.24, h * 0.36), true, true);
                        ctx.LineTo(new Point(w * 0.46, h * 0.14), true, true);
                        ctx.LineTo(new Point(w * 0.46, h * 0.86), true, true);
                        ctx.LineTo(new Point(w * 0.24, h * 0.64), true, true);
                        ctx.LineTo(new Point(w * 0.06, h * 0.64), true, true);
                    }
                    if (seven) dc.DrawGeometry(fg, rim, body);
                    else dc.DrawGeometry(null, Stroke(fg, 1.6), body);
                    if (Muted)
                    {
                        var red = Stroke(B(seven ? C(0xE0352B) : ZenStyle.Momiji), seven ? 2.2 : 1.7);
                        dc.DrawLine(red, new Point(w * 0.62, h * 0.34), new Point(w * 0.92, h * 0.66));
                        dc.DrawLine(red, new Point(w * 0.92, h * 0.34), new Point(w * 0.62, h * 0.66));
                        break;
                    }
                    int waves = Level <= 0 ? 0 : Level < 34 ? 1 : Level < 67 ? 2 : 3;
                    for (int i = 0; i < 3; i++)
                    {
                        double r = h * (0.16 + i * 0.16);
                        var g = new StreamGeometry();
                        var c = new Point(w * 0.5, h * 0.5);
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(new Point(c.X + r * Math.Cos(-0.85), c.Y + r * Math.Sin(-0.85)), false, false);
                            ctx.ArcTo(new Point(c.X + r * Math.Cos(0.85), c.Y + r * Math.Sin(0.85)), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                        }
                        if (seven) dc.DrawGeometry(null, new Pen(B(0x000000, 110), 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
                        dc.DrawGeometry(null, Stroke(i < waves ? fg : dim, seven ? 1.6 : 1.5), g);
                    }
                    break;
                }
                case Kind.Battery:
                {
                    if (seven)
                    {
                        // upright battery, green charge (amber when low, red when almost empty)
                        var outer = new Rect(w * 0.26, h * 0.14, w * 0.48, h * 0.84);
                        dc.DrawRectangle(B(0xFFFFFF, 60), rim, outer);
                        dc.DrawRectangle(fg, rim, new Rect(w * 0.4, h * 0.03, w * 0.2, h * 0.12));
                        double fillH = (outer.Height - 2) * Math.Max(4, Level) / 100.0;
                        var col = Level <= 10 ? C(0xE0352B) : Level <= 25 ? C(0xF2B01E) : C(0x5BD33A);
                        dc.DrawRectangle(V((0, Core.Applier.Mix(col, Colors.White, 0.3)), (1, col)), null, new Rect(outer.Left + 1, outer.Bottom - 1 - fillH, outer.Width - 2, fillH));
                        if (Charging) dc.DrawGeometry(B(0xFFE45C), new Pen(B(0x000000, 140), 0.8), Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0},{1} L{2},{3} L{4},{5} L{6},{7} L{8},{9} L{10},{11} Z", w * 0.56, h * 0.2, w * 0.36, h * 0.58, w * 0.5, h * 0.58, w * 0.42, h * 0.92, w * 0.66, h * 0.48, w * 0.52, h * 0.48)));
                        break;
                    }
                    var shell = new Rect(w * 0.04, h * 0.24, w * 0.82, h * 0.52);
                    dc.DrawRoundedRectangle(null, Stroke(fg, 1.5), shell, 3.5, 3.5);
                    dc.DrawRoundedRectangle(fg, null, new Rect(w * 0.88, h * 0.4, w * 0.08, h * 0.2), 1, 1);
                    double fw = (shell.Width - 4) * Math.Max(6, Level) / 100.0;
                    var level = Level <= 15 && !Charging ? ZenStyle.Momiji : Charging ? ZenStyle.Moss : ink;
                    dc.DrawRoundedRectangle(B(level), null, new Rect(shell.Left + 2, shell.Top + 2, fw, shell.Height - 4), 2, 2);
                    break;
                }
            }
        }
    }
}

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WinMorph.Core
{
    /// <summary>Draws a small mock-up of a window and the taskbar on top of the wallpaper.</summary>
    public static class PreviewArt
    {
        static Brush B(Color c, byte a = 255) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
        static Color Rgb(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

        public static void DrawUi(DrawingContext dc, double W, double H, Preset p)
        {
            double s = H / 270.0;
            bool retro = p.OsStyle == "Classic1" || p.Id == "win95";
            double radius = retro ? 0 : 5 * s;

            // ---- window ----
            var win = new Rect(W * 0.17, H * 0.14, W * 0.5, H * 0.52);
            Color body = p.DarkApps ? Rgb(0x202020) : Rgb(0xFAFAFA);
            Color title = p.AccentTitle ? p.Accent : (p.DarkApps ? Rgb(0x2B2B2B) : Rgb(0xEEEEEE));
            if (p.Shadows) dc.DrawRoundedRectangle(B(Rgb(0x000000), 60), null, new Rect(win.X + 2 * s, win.Y + 4 * s, win.Width, win.Height), radius + 2, radius + 2);
            dc.DrawRoundedRectangle(B(body), retro ? new Pen(B(Rgb(0x808080)), 1.2 * s) : null, win, radius, radius);
            var titleRect = new Rect(win.X, win.Y, win.Width, 16 * s);
            dc.PushClip(new RectangleGeometry(win, radius, radius));
            dc.DrawRectangle(B(title), null, titleRect);
            dc.Pop();
            Color tText = Luma(title) > 0.6 ? Rgb(0x1A1A1A) : Rgb(0xFFFFFF);
            var ft = new FormattedText(p.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(p.Font), FontStyles.Normal, p.CaptionBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                8.5 * s, B(tText), 1.0);
            dc.DrawText(ft, new Point(win.X + 6 * s, win.Y + 3 * s));
            for (int i = 0; i < 3; i++)
            {
                double cx = win.Right - (8 + i * 11) * s;
                dc.DrawRectangle(B(tText, 170), null, new Rect(cx - 3 * s, win.Y + 7.5 * s, 6 * s, 1.2 * s));
            }
            // content lines
            Color line = p.DarkApps ? Rgb(0x3A3A3A) : Rgb(0xE1E1E1);
            for (int i = 0; i < 5; i++)
                dc.DrawRoundedRectangle(B(i == 0 ? p.Accent : line), null, new Rect(win.X + 10 * s, win.Y + (26 + i * 12) * s, win.Width * (i == 0 ? 0.25 : 0.8 - i * 0.08), 6 * s), 2 * s, 2 * s);

            // ---- taskbar ----
            double th = (p.AutoHide ? 10 : 18) * s;
            Color bar;
            if (p.AccentTaskbar && p.DarkSystem) bar = Applier.Mix(p.Accent, Rgb(0x000000), 0.42);
            else bar = p.DarkSystem ? Rgb(0x1C1C1C) : Rgb(0xF0F2F5);
            byte alpha = (byte)(p.Transparency ? 215 : 255);
            var barRect = new Rect(0, H - th, W, th);
            dc.DrawRectangle(B(bar, alpha), null, barRect);
            if (p.AutoHide)
            {
                dc.DrawRectangle(B(Rgb(0xFFFFFF), 70), null, new Rect(W * 0.42, H - th + 3 * s, W * 0.16, 1.5 * s));
                return;
            }

            Color icon = p.DarkSystem ? Rgb(0xDADADA) : Rgb(0x5A5A5A);
            bool labels = p.Combine == 2;
            int count = labels ? 4 : 6;
            double iconW = labels ? 34 * s : 10 * s, gap = 4 * s;
            double startW = p.OsStyle != null ? (p.OsButton == "AeroButton" ? 14 * s : 26 * s) : 10 * s;
            double searchW = p.Search == 2 || p.Search == 3 ? 40 * s : (p.Search == 1 ? 10 * s : 0);
            double total = startW + gap + (searchW > 0 ? searchW + gap : 0) + count * (iconW + gap);
            double x = p.Center ? (W - total) / 2 : 4 * s;
            double cy = H - th / 2;

            // start button
            if (p.OsStyle != null && p.OsButton == "AeroButton")
            {
                dc.DrawEllipse(B(Applier.Mix(p.Accent, Rgb(0xFFFFFF), 0.3)), new Pen(B(Rgb(0xFFFFFF), 160), 1 * s), new Point(x + startW / 2, cy), 7 * s, 7 * s);
            }
            else if (p.OsStyle != null)
            {
                var r = new Rect(x, cy - 6.5 * s, startW, 13 * s);
                dc.DrawRectangle(B(Rgb(0xC0C0C0)), new Pen(B(Rgb(0x404040)), 0.8 * s), r);
                var st = new FormattedText("Start", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Tahoma"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 7 * s, B(Rgb(0x000000)), 1.0);
                dc.DrawText(st, new Point(x + (startW - st.Width) / 2, cy - st.Height / 2));
            }
            else
            {
                double q = 4.2 * s, o = 0.6 * s;
                Color lg = p.DarkSystem ? Applier.Mix(p.Accent, Rgb(0xFFFFFF), 0.35) : p.Accent;
                dc.DrawRectangle(B(lg), null, new Rect(x + 0.4 * s, cy - q - o, q, q));
                dc.DrawRectangle(B(lg), null, new Rect(x + q + 1.4 * s, cy - q - o, q, q));
                dc.DrawRectangle(B(lg), null, new Rect(x + 0.4 * s, cy + o, q, q));
                dc.DrawRectangle(B(lg), null, new Rect(x + q + 1.4 * s, cy + o, q, q));
            }
            x += startW + gap;

            if (searchW > 0)
            {
                Color sb = p.DarkSystem ? Rgb(0x3A3A3A) : Rgb(0xFFFFFF);
                dc.DrawRoundedRectangle(B(sb), null, new Rect(x, cy - 5 * s, searchW, 10 * s), 5 * s, 5 * s);
                x += searchW + gap;
            }

            uint[] palette = { 0x3B82F6, 0xF59E0B, 0x10B981, 0xEF4444, 0x8B5CF6, 0x06B6D4 };
            for (int i = 0; i < count; i++)
            {
                if (labels)
                {
                    var r = new Rect(x, cy - 6 * s, iconW, 12 * s);
                    dc.DrawRoundedRectangle(B(icon, i == 0 ? (byte)70 : (byte)35), null, r, 2 * s, 2 * s);
                    dc.DrawRoundedRectangle(B(Rgb(palette[i])), null, new Rect(x + 2 * s, cy - 3 * s, 6 * s, 6 * s), 1.5 * s, 1.5 * s);
                    dc.DrawRectangle(B(icon, 150), null, new Rect(x + 11 * s, cy - 0.8 * s, iconW - 15 * s, 1.6 * s));
                }
                else
                {
                    dc.DrawRoundedRectangle(B(Rgb(palette[i])), null, new Rect(x, cy - 5 * s, iconW, iconW), 2.5 * s, 2.5 * s);
                    if (i == 0) dc.DrawRoundedRectangle(B(p.Accent), null, new Rect(x + 2.5 * s, H - 2.2 * s, 5 * s, 1.4 * s), 0.7 * s, 0.7 * s);
                }
                x += iconW + gap;
            }

            // clock
            var clock = new FormattedText(p.Seconds ? "12:34:56" : "12:34", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(p.Font), 6.5 * s, B(icon), 1.0);
            dc.DrawText(clock, new Point(W - clock.Width - 6 * s, cy - clock.Height / 2));
        }

        static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
    }
}

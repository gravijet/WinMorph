using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The Alt+Tab switcher of a look. Every look has its own:
    ///  Aero (Windows 7): a glass panel with live thumbnails and a glass highlight that glides between them.
    ///  Shoji (Zen): paper doors slide shut and open again on the chosen window; the windows hang as wooden ema plaques that swing.
    ///  Table (Hacker): a process table with PID and memory, inverted selection and a live preview pane.
    /// </summary>
    public sealed class Switcher : Window
    {
        readonly ShellTheme t;
        readonly List<WinInfo> wins;
        readonly List<FrameworkElement> cells = new List<FrameworkElement>();
        readonly List<LiveThumb> thumbs = new List<LiveThumb>();
        readonly List<(WinInfo w, FrameworkElement target)> thumbTargets = new List<(WinInfo, FrameworkElement)>();
        Action<int, bool> onSelect;
        int index;
        bool glass;

        /// <summary>The user clicked a window with the mouse.</summary>
        public event Action Picked;

        public WinInfo Selected => wins.Count == 0 ? null : wins[index];

        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        public Switcher(ShellTheme theme, List<WinInfo> windows, int start)
        {
            t = theme; wins = windows; index = Math.Max(0, Math.Min(start, wins.Count - 1));
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily(t.Font);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            switch (t.Switch)
            {
                case SwitchKind.Shoji: Content = BuildShoji(); break;
                case SwitchKind.Table: Content = BuildTable(); break;
                default: Content = BuildAero(); break;
            }
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
                SetWindowLong(h, W32.GWL_EXSTYLE, ex | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE);
                int corner = t.Switch == SwitchKind.Table || t.Switch == SwitchKind.Shoji ? 1 : 2;   // 1 = square, 2 = round
                W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
                if (glass)
                {
                    // see-through window that is not layered, so live thumbnails still work on it
                    var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    DwmExtendFrameIntoClientArea(h, ref m);
                    HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
                    W32.SetBlur(h, t.Switch == SwitchKind.Shoji ? 4 : 3, t.Switch == SwitchKind.Shoji ? 0x66E4ECF2u : 0);
                }
                if (t.Switch == SwitchKind.Aero) { int border = 0x00704A1E; W32.DwmSetWindowAttribute(h, W32.DWMWA_BORDER_COLOR, ref border, 4); }
            };
            Loaded += (s, e) =>
            {
                Place();
                // thumbnails come in a moment after the frame, like the real thing; layout is final by then
                var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(t.Switch == SwitchKind.Table ? 40 : 70) };
                tm.Tick += (s2, e2) =>
                {
                    tm.Stop();
                    foreach (var (w, target) in thumbTargets) { var lt = new LiveThumb(this, w.Hwnd, target); if (lt.Ok) thumbs.Add(lt); }
                    onSelect?.Invoke(index, false);
                };
                tm.Start();
                onSelect?.Invoke(index, false);
            };
            Closed += (s, e) => { foreach (var th in thumbs) th.Dispose(); thumbs.Clear(); selThumb?.Dispose(); };
        }

        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);

        void Place()
        {
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            if (t.Switch == SwitchKind.Shoji) return;   // already covers the monitor
            double mw = (mi.rcWork.Right - mi.rcWork.Left) / s, mh = (mi.rcWork.Bottom - mi.rcWork.Top) / s;
            Left = mi.rcWork.Left / s + (mw - ActualWidth) / 2;
            Top = mi.rcWork.Top / s + (mh - ActualHeight) / 2;
        }

        public void Move(int d)
        {
            if (wins.Count == 0) return;
            index = (index + d + wins.Count) % wins.Count;
            if (IsLoaded) onSelect?.Invoke(index, true);
        }

        void Clickable(FrameworkElement el, int i)
        {
            el.Cursor = Cursors.Hand;
            el.MouseLeftButtonUp += (s, e) => { index = i; Picked?.Invoke(); };
            el.MouseEnter += (s, e) => { if (index != i) { index = i; onSelect?.Invoke(i, true); } };
        }

        LiveThumb selThumb;
        /// <summary>One live picture that follows the selection (Zen and Hacker show only the chosen window large).</summary>
        void ShowSelectedThumb(FrameworkElement target)
        {
            selThumb?.Dispose(); selThumb = null;
            if (!IsLoaded || wins.Count == 0) return;
            var lt = new LiveThumb(this, wins[index].Hwnd, target);
            if (lt.Ok) selThumb = lt;
        }

        static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction Soft = new QuinticEase { EasingMode = EasingMode.EaseOut };

        static void Animate(Animatable target, DependencyProperty p, double to, double ms, IEasingFunction ease = null, double? from = null, double delay = 0)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(delay) };
            if (from.HasValue) a.From = from;
            target.BeginAnimation(p, a);
        }
        static void Animate(UIElement target, DependencyProperty p, double to, double ms, IEasingFunction ease = null, double? from = null, double delay = 0)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(delay) };
            if (from.HasValue) a.From = from;
            target.BeginAnimation(p, a);
        }

        static UIElement Empty(Color fg, string font) => new TextBlock
        {
            Text = L("Keine offenen Fenster", "No open windows"), Foreground = B(fg), FontSize = 15, FontFamily = new FontFamily(font), Margin = new Thickness(48, 28, 48, 28),
        };

        // =====================================================================
        // Windows 7: Aero glass
        // =====================================================================
        UIElement BuildAero()
        {
            glass = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            const double cw = 196, ch = 138, pad = 10;   // cell, thumbnail inside it
            int cols = Math.Max(1, Math.Min(wins.Count, 7));

            var panel = new Grid();
            // the glass itself: translucent blue with the typical light streaks, a white inner edge
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = { new GradientStop(C(0x8CC4EC, 120), 0), new GradientStop(C(0x3A79B4, 120), 0.45), new GradientStop(C(0x1E4F84, 150), 1) } },
            });
            var streaks = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            for (int i = 0; i < 5; i++)
                streaks.Children.Add(new Rectangle
                {
                    Width = 46 + i * 9, Height = 900, Fill = new LinearGradientBrush(C(0xFFFFFF, 0), C(0xFFFFFF, (byte)(22 + i * 4)), 0),
                    RenderTransform = new RotateTransform(28), Margin = new Thickness(80 + i * 210, -260, 0, 0),
                });
            panel.Children.Add(streaks);
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderBrush = B(0xFFFFFF, 110), BorderThickness = new Thickness(1), IsHitTestVisible = false });
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = V((0, C(0xFFFFFF, 60)), (0.18, C(0xFFFFFF, 12)), (0.19, C(0xFFFFFF, 0)), (1, C(0xFFFFFF, 0))), IsHitTestVisible = false });

            if (wins.Count == 0) { panel.Children.Add(Empty(C(0xFFFFFF), t.Font)); return panel; }

            var sp = new StackPanel { Margin = new Thickness(18, 12, 18, 18) };
            // title row: the chosen window's icon and title, white with a soft dark glow
            var titleIcon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
            var title = new TextBlock { Foreground = B(0xFFFFFF), FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = cols * cw - 40, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.75 } };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 10) };
            titleRow.Children.Add(titleIcon); titleRow.Children.Add(title);
            sp.Children.Add(titleRow);

            int rows = (wins.Count + cols - 1) / cols;
            var area = new Grid { Width = cols * cw, Height = rows * ch };
            // the glass highlight glides from cell to cell
            var hl = new Border
            {
                Width = cw, Height = ch, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(5),
                BorderBrush = B(0xFFFFFF, 210), BorderThickness = new Thickness(1),
                Background = V((0, C(0xFFFFFF, 95)), (0.5, C(0xFFFFFF, 45)), (1, C(0xBFE3FF, 70))),
                Effect = new DropShadowEffect { Color = C(0xBFE6FF), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 },
            };
            var hlMove = new TranslateTransform();
            hl.RenderTransform = hlMove;
            area.Children.Add(hl);
            for (int i = 0; i < wins.Count; i++)
            {
                var w = wins[i];
                var cell = new Grid { Width = cw, Height = ch, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(i % cols * cw, i / cols * ch, 0, 0), Background = Brushes.Transparent };
                var thumb = new Grid { Margin = new Thickness(pad, pad, pad, pad + 4) };
                // the icon waits underneath; the live picture (also of minimised windows: their last frame) covers it
                thumb.Children.Add(new Border { Background = B(0x0A2440, 70), CornerRadius = new CornerRadius(3) });
                thumb.Children.Add(Img(w.Icon, 48));
                cell.Children.Add(thumb);
                var badge = Img(w.Icon, 22);
                badge.HorizontalAlignment = HorizontalAlignment.Right; badge.VerticalAlignment = VerticalAlignment.Bottom; badge.Margin = new Thickness(0, 0, 2, 0);
                cell.Children.Add(badge);
                thumbTargets.Add((w, thumb));
                Clickable(cell, i);
                cells.Add(cell); area.Children.Add(cell);
            }
            sp.Children.Add(area);
            panel.Children.Add(sp);

            onSelect = (i, anim) =>
            {
                double x = i % cols * cw, y = i / cols * ch;
                if (anim) { Animate(hlMove, TranslateTransform.XProperty, x, 150); Animate(hlMove, TranslateTransform.YProperty, y, 150); }
                else { hlMove.BeginAnimation(TranslateTransform.XProperty, null); hlMove.BeginAnimation(TranslateTransform.YProperty, null); hlMove.X = x; hlMove.Y = y; }
                title.Text = wins[i].Title;
                titleIcon.Source = wins[i].Icon;
            };
            panel.Opacity = 0;
            Loaded += (s, e) => Animate(panel, OpacityProperty, 1, 120);
            return panel;
        }

        // =====================================================================
        // Zen: shoji and ema. The chosen window stands behind sliding paper doors;
        // the windows hang as wooden plaques on a cord.
        // =====================================================================
        UIElement BuildShoji()
        {
            glass = true;
            Background = Brushes.Transparent;
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            Left = mi.rcMonitor.Left / s; Top = mi.rcMonitor.Top / s;
            double W = (mi.rcMonitor.Right - mi.rcMonitor.Left) / s, H = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / s;
            Width = W; Height = H;
            Color ink = C(0x2E2A24), sub = C(0x8A7A63), wood = C(0x8B6B4A), woodDark = C(0x4E3926), washi = C(0xFAF6EE), seal = C(0xC8452D);

            var root = new Canvas { Width = W, Height = H, ClipToBounds = true };
            // washi veil over the blurred desktop, a far ink-wash ridge at the bottom
            root.Children.Add(new Rectangle { Width = W, Height = H, Fill = new LinearGradientBrush(C(0xF8F3EA, 205), C(0xEEE4D3, 235), 90) });
            foreach (var (baseY, amp, alpha) in new[] { (0.86, 0.07, (byte)22), (0.92, 0.05, (byte)34) })
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(0, H), true, true);
                    c.LineTo(new Point(0, H * baseY), false, false);
                    c.BezierTo(new Point(W * 0.22, H * (baseY - amp)), new Point(W * 0.36, H * (baseY + amp * 0.4)), new Point(W * 0.55, H * (baseY - amp * 0.6)), false, false);
                    c.BezierTo(new Point(W * 0.72, H * (baseY - amp * 1.4)), new Point(W * 0.86, H * baseY), new Point(W, H * (baseY - amp * 0.3)), false, false);
                    c.LineTo(new Point(W, H), false, false);
                }
                root.Children.Add(new Path { Data = g, Fill = new LinearGradientBrush(C(0x2E2A24, alpha), C(0x2E2A24, 0), 90), IsHitTestVisible = false });
            }
            if (wins.Count == 0)
            {
                var empty = Empty(ink, "Segoe UI Light");
                root.Children.Add(Place(empty, W * 0.4, H * 0.42));
                return root;
            }

            // ---- the shoji: a wooden frame, a room behind it, two paper doors ----
            double fw = Math.Round(Math.Min(W * 0.56, 1080)), fh = Math.Round(Math.Min(H * 0.52, fw * 0.6));
            double fx = Math.Round((W - fw) / 2), fy = Math.Round(H * 0.09);
            const double rim = 12;
            root.Children.Add(Place(new Border
            {
                Width = fw + rim * 2, Height = fh + rim * 2, CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(C(0x6B4F35), woodDark, 90),
                Effect = new DropShadowEffect { Color = C(0x3A2A1A), BlurRadius = 46, ShadowDepth = 14, Direction = 270, Opacity = 0.28 },
            }, fx - rim, fy - rim));
            var room = new Canvas { Width = fw, Height = fh, ClipToBounds = true, Background = new LinearGradientBrush(C(0x6E5A44), C(0x4E3E2E), 90) };
            var roomIcon = new Image { Width = 112, Height = 112, Opacity = 0.85 };
            room.Children.Add(Place(roomIcon, (fw - 112) / 2, (fh - 112) / 2));
            FrameworkElement Door(bool left)
            {
                double dw = fw / 2;
                var d = new Grid { Width = dw, Height = fh, Background = new LinearGradientBrush(washi, C(0xF0E7D6), 0) };
                // kumiko lattice
                for (int k = 1; k < 3; k++) d.Children.Add(new Rectangle { Width = 3, Fill = B(wood), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(dw * k / 3 - 1.5, 0, 0, 0) });
                for (int k = 1; k < 4; k++) d.Children.Add(new Rectangle { Height = 3, Fill = B(wood), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, fh * k / 4 - 1.5, 0, 0) });
                d.Children.Add(new Border { BorderBrush = B(woodDark), BorderThickness = new Thickness(left ? 5 : 3, 5, left ? 3 : 5, 5) });
                // the finger pull near the meeting edge
                d.Children.Add(new Ellipse
                {
                    Width = 12, Height = 30, Fill = B(C(0x5E4A35)), Stroke = B(C(0xB79A72)), StrokeThickness = 2,
                    HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left ? 0 : 18, 0, left ? 18 : 0, 0),
                });
                return d;
            }
            var doorL = Door(true); var doorR = Door(false);
            var moveL = new TranslateTransform(); var moveR = new TranslateTransform(fw / 2, 0);
            doorL.RenderTransform = moveL; doorR.RenderTransform = moveR;
            room.Children.Add(doorL); room.Children.Add(doorR);
            root.Children.Add(Place(room, fx, fy));

            // ---- title, written below the frame ----
            var titleMove = new TranslateTransform();
            var title = new TextBlock { Foreground = B(ink), FontFamily = new FontFamily("Segoe UI Light"), FontSize = 30, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = fw, RenderTransform = titleMove };
            var app = new TextBlock { Foreground = B(sub), FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, TextAlignment = TextAlignment.Center, Width = fw };
            root.Children.Add(Place(title, fx, fy + fh + rim + 26));
            root.Children.Add(Place(app, fx, fy + fh + rim + 70));

            // ---- ema: every window a wooden plaque on a cord ----
            int max = Math.Min(wins.Count, 12);
            const double ew = 58, eh = 64, gap = 26, drop = 16;
            double rowW = max * ew + (max - 1) * gap, rx = (W - rowW) / 2, ry = fy + fh + rim + 128;
            double sag = 14 + max * 1.5;
            var rope = new StreamGeometry();
            using (var c = rope.Open()) { c.BeginFigure(new Point(rx - 40, ry), false, false); c.QuadraticBezierTo(new Point(W / 2, ry + sag * 2), new Point(rx + rowW + 40, ry), true, true); }
            root.Children.Add(new Path { Data = rope, Stroke = B(wood), StrokeThickness = 2.4 });
            double RopeY(double x) { double tt = (x - (rx - 40)) / (rowW + 80); return ry + 4 * sag * tt * (1 - tt); }

            var swings = new List<RotateTransform>();
            var strings = new List<Line>();
            var seals = new List<Border>();
            for (int i = 0; i < max; i++)
            {
                double hx = rx + i * (ew + gap) + ew / 2, hy = RopeY(hx);
                var plaque = new Canvas { Width = ew, Height = drop + eh, Background = Brushes.Transparent };
                var str = new Line { X1 = ew / 2, Y1 = 0, X2 = ew / 2, Y2 = drop + 4, Stroke = B(wood), StrokeThickness = 1.6 };
                var shape = new Path
                {
                    Data = Geometry.Parse(FormattableString.Invariant($"M0,13 L{ew / 2},0 L{ew},13 L{ew},{eh} L0,{eh} Z")),
                    Fill = new LinearGradientBrush(C(0xE6CDA4), C(0xC9A06A), 90), Stroke = B(C(0x9A7550)), StrokeThickness = 1.2,
                    Effect = new DropShadowEffect { Color = C(0x3A2A1A), BlurRadius = 10, ShadowDepth = 4, Direction = 270, Opacity = 0.22 },
                };
                plaque.Children.Add(str);
                plaque.Children.Add(Place(shape, 0, drop));
                plaque.Children.Add(Place(new Ellipse { Width = 5, Height = 5, Fill = B(woodDark) }, ew / 2 - 2.5, drop + 6));
                plaque.Children.Add(Place(Img(wins[i].Icon, 28), (ew - 28) / 2, drop + 22));
                var mark = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(3), Background = B(seal), Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
                    Child = new TextBlock { Text = Initial(wins[i]), Foreground = B(0xFFFFFF), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                plaque.Children.Add(Place(mark, ew - 14, drop + eh - 14));
                var swing = new RotateTransform(0);
                var fall = new TranslateTransform(0, -26);
                plaque.RenderTransformOrigin = new Point(0.5, 0);
                plaque.RenderTransform = new TransformGroup { Children = { swing, fall } };
                plaque.Opacity = 0;
                Clickable(plaque, i);
                swings.Add(swing); strings.Add(str); seals.Add(mark); cells.Add(plaque);
                root.Children.Add(Place(plaque, hx - ew / 2, hy));
                // they are hung up one after another and settle on the cord
                double delay = 90 + i * Math.Max(18, 44 - i * 3);
                Animate(fall, TranslateTransform.YProperty, 0, 620, new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 7 }, delay: delay);
                Animate(plaque, OpacityProperty, 1, 260, Soft, delay: delay);
            }
            if (wins.Count > max) root.Children.Add(Place(new TextBlock { Text = "+" + (wins.Count - max), Foreground = B(sub), FontSize = 15 }, rx + rowW + 52, ry - 10));

            // ---- the doors: one value (0 closed … 1 open) drives paper and live picture ----
            double open = 0, vel = 0, goal = 0;
            int shown = -1, last = -1;
            bool running = false;
            LiveThumb pic = null;
            var frameRect = new Rect(fx, fy, fw, fh);
            void Show(int i)
            {
                shown = i;
                pic?.Dispose(); pic = null;
                roomIcon.Source = wins[i].Icon;
                // what runs in the app, minimised ones included (DWM keeps their last frame); the icon only if there is no picture
                var lt = new LiveThumb(this, wins[i].Hwnd, room); if (lt.Ok) pic = lt;
                pic?.Place(frameRect, 0);
                roomIcon.Visibility = pic != null ? Visibility.Hidden : Visibility.Visible;
                title.Text = wins[i].Title;
                app.Text = wins[i].AppName;
                Animate(title, OpacityProperty, 1, 420, Soft, from: 0);
                Animate(titleMove, TranslateTransform.YProperty, 0, 520, Soft, from: 8);
            }
            void Frame(object sender, EventArgs e)
            {
                const double dt = 1 / 60.0;
                bool closing = goal < open;
                double k = closing ? 900 : 150, d = closing ? 60 : 19.5;   // close briskly, open slowly with a soft settle
                vel += (-k * (open - goal) - d * vel) * dt;
                open += vel * dt;
                if (goal == 0 && open < 0.02) { open = 0; vel = 0; if (shown != index) Show(index); goal = 1; }
                double o = Math.Max(0, open);
                moveL.X = -o * fw / 2; moveR.X = fw / 2 + o * fw / 2;
                pic?.Place(frameRect, o);
                if (goal == 1 && Math.Abs(open - 1) < 0.0008 && Math.Abs(vel) < 0.002)
                {
                    open = 1; moveL.X = -fw / 2; moveR.X = fw; pic?.Place(frameRect, 1);
                    CompositionTarget.Rendering -= Frame; running = false;
                }
            }
            void Run() { if (!running) { running = true; CompositionTarget.Rendering += Frame; } }
            Closed += (s2, e) => { CompositionTarget.Rendering -= Frame; pic?.Dispose(); };

            onSelect = (i, anim) =>
            {
                // the ema swings out towards the direction of travel and gets the red seal
                if (i != last)
                {
                    int dir = last < 0 ? 1 : (i > last ? 1 : -1);
                    if (i < max)
                    {
                        swings[i].BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(dir * 11, 0, TimeSpan.FromMilliseconds(1500)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 3, Springiness = 3 } });
                        strings[i].Stroke = B(seal);
                        var sc = (ScaleTransform)seals[i].RenderTransform;
                        var press = new DoubleAnimation(1.9, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                        sc.BeginAnimation(ScaleTransform.ScaleXProperty, press); sc.BeginAnimation(ScaleTransform.ScaleYProperty, press);
                        seals[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.95, TimeSpan.FromMilliseconds(120)));
                    }
                    if (last >= 0 && last < max)
                    {
                        strings[last].Stroke = B(wood);
                        seals[last].BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
                        swings[last].BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-dir * 4, 0, TimeSpan.FromMilliseconds(900)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 } });
                    }
                    last = i;
                }
                if (i != shown) { goal = 0; Run(); }        // doors slide shut, the next window, doors slide open
                else if (open < 1) { goal = 1; Run(); }
            };
            return root;
        }

        static string Initial(WinInfo w)
        {
            var n = w.AppName ?? w.Title ?? "?";
            foreach (var ch in n) if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "·";
        }

        static UIElement Place(UIElement el, double x, double y) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }

        // =====================================================================
        // Hacker: process table
        // =====================================================================
        UIElement BuildTable()
        {
            Background = B(0x000000);
            SizeToContent = SizeToContent.WidthAndHeight;
            var mono = new FontFamily("Consolas");
            Color fg = t.MenuFg, dim = t.BarFgDim, acc = t.Accent;
            TextBlock Tx(string s, Color c, double size = 13.5, FontWeight? wgt = null) => new TextBlock { Text = s, Foreground = B(c), FontFamily = mono, FontSize = size, FontWeight = wgt ?? FontWeights.Normal };

            var outer = new Grid { Margin = new Thickness(0) };
            var box = new Border { BorderBrush = B(A(acc, 170)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 9, 0, 9), Padding = new Thickness(14, 16, 14, 14) };
            outer.Children.Add(box);
            // the frame title cuts into the border, like a TUI
            var head = new StackPanel { Orientation = Orientation.Horizontal, Background = B(0x000000), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            head.Children.Add(Tx(" alt+tab ", C(0x000000), 13, FontWeights.Bold));
            ((TextBlock)head.Children[0]).Background = B(acc);
            head.Children.Add(Tx("  " + wins.Count + L(" fenster ", " windows ") + "· " + DateTime.Now.ToString("HH:mm:ss") + " ", dim, 13));
            outer.Children.Add(head);
            var foot = Tx(L(" tab/⇧tab · ←→↑↓ · esc abbrechen · alt loslassen = wechseln ", " tab/⇧tab · ←→↑↓ · esc cancel · release alt = switch "), dim, 12);
            foot.Background = B(0x000000); foot.HorizontalAlignment = HorizontalAlignment.Right; foot.VerticalAlignment = VerticalAlignment.Bottom; foot.Margin = new Thickness(0, 0, 14, 0);
            outer.Children.Add(foot);

            if (wins.Count == 0) { box.Child = Tx(L("$ keine offenen fenster", "$ no open windows"), fg, 14); return outer; }

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(640) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            box.Child = cols;

            var list = new StackPanel();
            list.Children.Add(Tx(L("   #  PID     PROZESS          RAM     TITEL", "   #  PID     PROCESS          MEM     TITLE"), dim, 13));
            list.Children.Add(new Border { Height = 1, Background = B(A(acc, 70)), Margin = new Thickness(0, 4, 0, 4) });
            var rows = new List<(Border b, TextBlock tb, TextBlock caret)>();
            int max = Math.Min(wins.Count, 18);
            for (int i = 0; i < max; i++)
            {
                var w = wins[i];
                W32.GetWindowThreadProcessId(w.Hwnd, out uint pid);
                var name = (w.ExePath != null ? System.IO.Path.GetFileNameWithoutExtension(w.ExePath) : "?").ToLowerInvariant();
                if (name.Length > 15) name = name.Substring(0, 15);
                string mem = "    ?";
                try { using (var p = Process.GetProcessById((int)pid)) mem = Bytes(p.WorkingSet64); } catch { }
                var tb = Tx(" " + (i + 1).ToString().PadLeft(2) + "  " + pid.ToString().PadRight(7) + " " + name.PadRight(16) + " " + mem.PadLeft(6) + "  " + w.Title, fg, 14);
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
                var caret = Tx("▌", C(0x000000), 14);
                var line = new DockPanel();
                DockPanel.SetDock(caret, Dock.Left);
                line.Children.Add(caret); line.Children.Add(tb);
                var b = new Border { Child = line, Padding = new Thickness(0, 2, 6, 2), Background = Brushes.Transparent };
                Clickable(b, i);
                rows.Add((b, tb, caret)); cells.Add(b); list.Children.Add(b);
                // rows are printed one after another, like terminal output
                b.Opacity = 0;
                b.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.Zero) { BeginTime = TimeSpan.FromMilliseconds(i * 11) });
            }
            if (wins.Count > max) list.Children.Add(Tx("   … +" + (wins.Count - max), dim, 13));
            cols.Children.Add(list);

            var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            var label = Tx("", dim, 12.5);
            right.Children.Add(label);
            var pane = new Grid { Width = 440, Height = 276, Margin = new Thickness(0, 6, 0, 0) };
            pane.Children.Add(new Border { BorderBrush = B(A(acc, 120)), BorderThickness = new Thickness(1), Background = B(A(acc, 10)) });
            var noPic = Tx(L("[ keine vorschau ]", "[ no preview ]"), dim, 13);
            noPic.HorizontalAlignment = HorizontalAlignment.Center; noPic.VerticalAlignment = VerticalAlignment.Center;
            pane.Children.Add(noPic);
            var target = new Border { Margin = new Thickness(6) };
            pane.Children.Add(target);
            right.Children.Add(pane);
            var meta = Tx("", dim, 12.5);
            meta.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(meta);
            cols.Children.Add(right);

            // the selected row blinks its caret: the only idle motion in the hacker switcher
            var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            int sel = -1; bool on = true;
            blink.Tick += (s, e) => { on = !on; if (sel >= 0 && sel < rows.Count) rows[sel].caret.Foreground = B(on ? C(0x000000) : acc); };
            Loaded += (s, e) => blink.Start();
            Closed += (s, e) => blink.Stop();

            onSelect = (i, anim) =>
            {
                for (int k = 0; k < rows.Count; k++)
                {
                    bool active = k == i;
                    rows[k].b.Background = active ? B(acc) : Brushes.Transparent;
                    rows[k].tb.Foreground = B(active ? C(0x000000) : fg);
                    rows[k].tb.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
                    rows[k].caret.Foreground = B(active ? C(0x000000) : C(0x000000, 0));
                }
                sel = i;
                var w = wins[i];
                label.Text = "preview :: " + (w.ExePath != null ? System.IO.Path.GetFileName(w.ExePath).ToLowerInvariant() : "?");
                meta.Text = "hwnd 0x" + w.Hwnd.ToInt64().ToString("X8") + (w.Minimized ? L("  · minimiert", "  · minimised") : "");
                ShowSelectedThumb(target);
                noPic.Visibility = selThumb == null ? Visibility.Visible : Visibility.Collapsed;
            };
            return outer;
        }

        static string Bytes(long b) => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0") + "G" : (b >> 20) + "M";
    }
}

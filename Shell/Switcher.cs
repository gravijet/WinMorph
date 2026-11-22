using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    ///  Pond (Zen): the chosen window large in a frosted frame over the misted pond, every window a pebble that lifts with a ring of water.
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
                case SwitchKind.Mac: Content = BuildMac(); break;
                default: Content = BuildAero(); break;
            }
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
                SetWindowLong(h, W32.GWL_EXSTYLE, ex | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE);
                int corner = t.Switch == SwitchKind.Table || t.Switch == SwitchKind.Shoji || t.Switch == SwitchKind.Mac ? 1 : 2;
                if (t.Switch == SwitchKind.Mac) { int none = unchecked((int)0xFFFFFFFE); W32.DwmSetWindowAttribute(h, W32.DWMWA_BORDER_COLOR, ref none, 4); }   // the pond covers the whole monitor   // 1 = square, 2 = round
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

        /// <summary>
        /// macOS: the app switcher. One icon per app on a light glass bar in the middle of the screen; the chosen
        /// app sits on a grey rounded plate with its name below.
        /// </summary>
        UIElement BuildMac()
        {
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            MacStyle.Prepare(this);
            // icons as large as macOS draws them, smaller when many apps must fit on the screen
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
            double avail = (mi.rcWork.Right - mi.rcWork.Left) / sc * 0.92 - 60;
            double icon = Math.Max(40, Math.Min(108, Math.Floor(avail / Math.Max(1, wins.Count)) - 22)), cell = icon + 22;
            var grid = new Grid { Margin = new Thickness(11, 11, 11, 6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var plates = new List<Border>();
            macImgs.Clear();
            for (int i = 0; i < wins.Count; i++)
            {
                var w = wins[i];
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
                var plate = new Border { Width = cell, Height = cell, CornerRadius = new CornerRadius(Math.Round(cell * 0.19)), Background = Brushes.Transparent };
                var entry = AppCatalog.Apps.FirstOrDefault(a => a.Matches(w));
                var img = new Image { Source = MacApps.IconOf(w.Hwnd) ?? (w.ExePath != null && System.IO.Path.GetFileName(w.ExePath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ? MacStyle.Own("finder") : MacStyle.Tile(entry?.Icon ?? w.Icon)), Width = icon, Height = icon };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                plate.Child = img;
                Grid.SetColumn(plate, i);
                grid.Children.Add(plate);
                plates.Add(plate);
                macImgs.Add(img);
                Clickable(plate, i);
                plate.Cursor = Cursors.Arrow;
            }
            var name = MacStyle.Text("", 13, MacStyle.Label);
            name.HorizontalAlignment = HorizontalAlignment.Center; name.TextAlignment = TextAlignment.Center; name.VerticalAlignment = VerticalAlignment.Top; name.Margin = new Thickness(0, 2, 0, 0);
            var nameHost = new Canvas { ClipToBounds = false };
            nameHost.Children.Add(name);
            Grid.SetRow(nameHost, 1); Grid.SetColumnSpan(nameHost, Math.Max(1, wins.Count));
            grid.Children.Add(nameHost);
            onSelect = (i, anim) =>
            {
                for (int k = 0; k < plates.Count; k++) plates[k].Background = k == i ? B(0x000000, 30) : Brushes.Transparent;
                name.Text = MacApps.AppName(wins[i]);
                // the name sits under the chosen icon and stays inside the bar
                name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double total = cell * wins.Count, wdt = Math.Min(name.DesiredSize.Width, total);
                name.MaxWidth = total;
                Canvas.SetLeft(name, Math.Max(0, Math.Min(total - wdt, i * cell + cell / 2 - wdt / 2)));
            };
            var pane = MacStyle.Glass(grid, Math.Round(cell * 0.19) + 11, C(0xECECEF, 196), behind: true);
            var host = new Border { Child = pane, Padding = new Thickness(30) };
            Loaded += (s, e) => MacStyle.Pop(pane, new Point(0.5, 0.5), 0.97, 140);
            return host;
        }

        readonly List<Image> macImgs = new List<Image>();

        /// <summary>macOS: Q (quit) or H (hide) while the switcher is open greys the chosen app out; the shell closes or hides its windows.</summary>
        public void MarkSelected()
        {
            if (index < 0 || index >= macImgs.Count) return;
            macImgs[index].BeginAnimation(OpacityProperty, new DoubleAnimation(0.38, TimeSpan.FromMilliseconds(160)));
        }

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
        // Zen: the pond mirror – the chosen window large over the misted pond, every window a pebble
        // =====================================================================
        UIElement BuildShoji()
        {
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            Left = mi.rcMonitor.Left / s; Top = mi.rcMonitor.Top / s;
            double W = (mi.rcMonitor.Right - mi.rcMonitor.Left) / s, H = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / s;
            Width = W; Height = H;
            // the whole pond, blurred and misted: the window is not layered, so the live pictures can sit on it
            Background = new ImageBrush(ZenStyle.FrostImage) { Stretch = Stretch.Fill };
            var ink = ZenStyle.Ink; var ink2 = ZenStyle.Ink2;
            var root = new Canvas { Width = W, Height = H, ClipToBounds = true };
            root.Children.Add(new Rectangle { Width = W, Height = H, Fill = B(A(ZenStyle.Mist, 105)) });
            var rings = new Grid { Width = W, Height = H, IsHitTestVisible = false };
            root.Children.Add(rings);
            if (wins.Count == 0)
            {
                var empty = ZenStyle.Text(L("Keine offenen Fenster", "No open windows"), 22, ink, FontWeights.SemiBold);
                root.Children.Add(Place(empty, W / 2 - 140, H * 0.45));
                return root;
            }

            // ---- the mirror: a frosted frame holding the live picture of the chosen window ----
            double fw = Math.Round(Math.Min(W * 0.58, 1120)), fh = Math.Round(Math.Min(H * 0.56, fw * 0.6));
            double fx = Math.Round((W - fw) / 2), fy = Math.Round(H * 0.1);
            const double pad = 14;
            var frame = new Border
            {
                Width = fw + pad * 2, Height = fh + pad * 2, CornerRadius = new CornerRadius(26), Background = B(0xFFFFFF, 120),
                BorderBrush = new LinearGradientBrush(C(0xFFFFFF, 235), C(0xFFFFFF, 110), 90), BorderThickness = new Thickness(1.2),
                Effect = new DropShadowEffect { Color = ZenStyle.PondDeep, BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.25 },
            };
            root.Children.Add(Place(frame, fx - pad, fy - pad));
            var roomIcon = new Image { Width = 128, Height = 128, Opacity = 0.9 };
            root.Children.Add(Place(roomIcon, fx + (fw - 128) / 2, fy + (fh - 128) / 2));
            var frameMove = new TranslateTransform(0, 16);
            frame.RenderTransform = frameMove; frame.Opacity = 0;
            Animate(frameMove, TranslateTransform.YProperty, 0, 520, ZenStyle.Surface);
            Animate(frame, OpacityProperty, 1, 320, ZenStyle.Surface);

            var title = new TextBlock { Foreground = B(ink), FontFamily = ZenStyle.Family, FontSize = 26, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = fw };
            var app = new TextBlock { Foreground = B(ink2), FontFamily = ZenStyle.Family, FontSize = 14, TextAlignment = TextAlignment.Center, Width = fw };
            var titleMove = new TranslateTransform();
            title.RenderTransform = titleMove;
            root.Children.Add(Place(title, fx, fy + fh + pad + 22));
            root.Children.Add(Place(app, fx, fy + fh + pad + 60));

            // ---- the pebbles: one per window ----
            int max = Math.Min(wins.Count, 14);
            const double pw = 66, gap = 18;
            double rowW = max * pw + (max - 1) * gap, rx = (W - rowW) / 2, ry = fy + fh + pad + 112;
            var lifts = new List<TranslateTransform>();
            var halos = new List<Ellipse>();
            for (int i = 0; i < max; i++)
            {
                var cell = new Grid { Width = pw, Height = pw, Background = Brushes.Transparent };
                var halo = new Ellipse { Stroke = B(ZenStyle.Koi), StrokeThickness = 3, Opacity = 0, Margin = new Thickness(-5) };
                cell.Children.Add(halo);
                cell.Children.Add(new Border { CornerRadius = new CornerRadius(pw / 2), Background = B(0xFFFFFF, 185), BorderBrush = B(0xFFFFFF, 240), BorderThickness = new Thickness(1), Effect = new DropShadowEffect { Color = ZenStyle.PondDeep, BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.2 } });
                cell.Children.Add(wins[i].Icon != null ? (UIElement)Img(wins[i].Icon, 34) : ZenStyle.Text(Initial(wins[i]), 22, ZenStyle.Pond, FontWeights.SemiBold));
                ((FrameworkElement)cell.Children[2]).HorizontalAlignment = HorizontalAlignment.Center;
                var lift = new TranslateTransform(0, 22);
                cell.RenderTransform = lift; cell.Opacity = 0;
                Clickable(cell, i);
                lifts.Add(lift); halos.Add(halo); cells.Add(cell);
                root.Children.Add(Place(cell, rx + i * (pw + gap), ry));
                // they surface one after another, the gaps shrinking like a pouring of stones
                double delay = 60 + i * Math.Max(16, 40 - i * 3);
                Animate(lift, TranslateTransform.YProperty, 0, 520, ZenStyle.Surface, delay: delay);
                Animate(cell, OpacityProperty, 1, 280, ZenStyle.Surface, delay: delay);
            }
            if (wins.Count > max) root.Children.Add(Place(ZenStyle.Text("+" + (wins.Count - max), 15, ink2, FontWeights.SemiBold), rx + rowW + 20, ry + pw / 2 - 10));

            // ---- one value drives the picture: it fades out, the next window comes, it fades in ----
            LiveThumb pic = null;
            int shown = -1, last = -1;
            double alpha = 0, goal = 1;
            bool running = false;
            var frameRect = new Rect(fx, fy, fw, fh);
            void Show(int i)
            {
                shown = i;
                pic?.Dispose(); pic = null;
                roomIcon.Source = wins[i].Icon;
                var lt = new LiveThumb(this, wins[i].Hwnd, root); if (lt.Ok) pic = lt;
                pic?.Place(frameRect, 1, 0);
                roomIcon.Visibility = pic != null ? Visibility.Hidden : Visibility.Visible;
                title.Text = wins[i].Title;
                app.Text = wins[i].AppName;
                Animate(title, OpacityProperty, 1, 380, ZenStyle.Surface, from: 0);
                Animate(titleMove, TranslateTransform.YProperty, 0, 460, ZenStyle.Surface, from: 8);
            }
            void Frame(object sender, EventArgs e)
            {
                double speed = goal < alpha ? 1 / 0.09 : 1 / 0.2;   // out quickly, in softly
                alpha += Math.Sign(goal - alpha) * speed / 60.0;
                if (goal == 0 && alpha <= 0) { alpha = 0; if (shown != index) Show(index); goal = 1; }
                alpha = Math.Max(0, Math.Min(1, alpha));
                double eased = 1 - Math.Pow(1 - alpha, 3);
                pic?.Place(frameRect, 1, (byte)Math.Round(255 * eased));
                roomIcon.Opacity = 0.9 * eased;
                if (goal == 1 && alpha >= 1) { CompositionTarget.Rendering -= Frame; running = false; }
            }
            void Run() { if (!running) { running = true; CompositionTarget.Rendering += Frame; } }
            Closed += (s2, e) => { CompositionTarget.Rendering -= Frame; pic?.Dispose(); };

            onSelect = (i, anim) =>
            {
                if (i != last)
                {
                    if (last >= 0 && last < max)
                    {
                        Animate(lifts[last], TranslateTransform.YProperty, 0, 320, ZenStyle.Surface);
                        Animate(halos[last], OpacityProperty, 0, 200);
                    }
                    if (i < max)
                    {
                        Animate(lifts[i], TranslateTransform.YProperty, -10, 360, ZenStyle.Surface);
                        Animate(halos[i], OpacityProperty, 1, 160);
                        // a ring spreads from the chosen pebble
                        var c = new Point(rx + i * (pw + gap) + pw / 2, ry + pw / 2 - 10);
                        ZenStyle.Rings(rings, c, A(ZenStyle.Koi, 200), 2, 120);
                    }
                    last = i;
                }
                if (i != shown) { if (shown < 0) { Show(i); alpha = 0; goal = 1; } else goal = 0; Run(); }
                else if (alpha < 1) { goal = 1; Run(); }
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

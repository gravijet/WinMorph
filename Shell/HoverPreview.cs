using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>Live window previews that pop up when hovering a taskbar button or dock icon.</summary>
    public sealed class HoverPreview : Window
    {
        static HoverPreview current;
        static DispatcherTimer openTimer, closeTimer;
        static FrameworkElement pendingAnchor;
        readonly List<LiveThumb> thumbs = new List<LiveThumb>();
        readonly List<(IntPtr hwnd, FrameworkElement target)> targets = new List<(IntPtr, FrameworkElement)>();
        bool over;

        /// <summary>Call from MouseEnter of a taskbar element.</summary>
        public static void Hover(ShellTheme t, FrameworkElement anchor, Func<List<WinInfo>> wins, Action<WinInfo> activate)
        {
            closeTimer?.Stop();
            pendingAnchor = anchor;
            openTimer?.Stop();
            openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(current != null ? 60 : 420) };
            openTimer.Tick += (s, e) =>
            {
                openTimer.Stop();
                if (pendingAnchor != anchor || !anchor.IsMouseOver) return;
                var list = wins();
                if (list.Count == 0) return;
                current?.Close();
                current = new HoverPreview(t, anchor, list, activate);
                current.Show();
            };
            openTimer.Start();
        }

        /// <summary>Call from MouseLeave of a taskbar element.</summary>
        public static void Leave()
        {
            openTimer?.Stop();
            pendingAnchor = null;
            ScheduleClose();
        }

        public static void CloseNow() { openTimer?.Stop(); closeTimer?.Stop(); current?.Close(); current = null; }

        static void ScheduleClose()
        {
            closeTimer?.Stop();
            closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                if (current != null && !current.over && pendingAnchor == null) { current.Close(); current = null; }
            };
            closeTimer.Start();
        }

        HoverPreview(ShellTheme t, FrameworkElement anchor, List<WinInfo> wins, Action<WinInfo> activate)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
            bool retro = t.Btn == BtnStyle.Bevel;
            bool term = t.Btn == BtnStyle.Text;   // hacker look: text only, square, no icons
            Color bg = retro ? C(0xC0C0C0) : Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B);
            if (t.Menu == MenuKind.Xp || (t.Menu == MenuKind.Seven && t.Id != "mint")) bg = C(0x1E3350);
            Color fg = retro ? C(0x000000) : (bg.R + bg.G + bg.B > 380 ? C(0x1E1E1E) : C(0xF2F2F2));
            Background = B(bg);
            FontFamily = new FontFamily(t.Font);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
            foreach (var w in wins.Take(5))
            {
                var win = w;
                var head = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
                var close = Click(term ? (UIElement)new TextBlock { Text = "[x]", Foreground = B(fg), FontFamily = new FontFamily("Consolas") } : Glyph("", 10, fg), () => { W32.Close(win.Hwnd); CloseNow(); }, term ? A(t.Accent, 80) : C(0xE81123), radius: new CornerRadius(term ? 0 : 3), pad: new Thickness(5, 3, 5, 3));
                DockPanel.SetDock(close, Dock.Right);
                head.Children.Add(close);
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                if (!term) title.Children.Add(Img(win.Icon, 16));
                title.Children.Add(new TextBlock { Text = win.Title, Foreground = B(fg), FontFamily = new FontFamily(t.Font), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(title);
                var thumbArea = new Grid { Width = 220, Height = 128 };
                if (term) thumbArea.Children.Add(new TextBlock { Text = Core.Loc.De ? "minimiert" : "minimised", Foreground = B(A(fg, 140)), FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                else thumbArea.Children.Add(Img(win.Icon, 40));
                var sp = new StackPanel();
                sp.Children.Add(head); sp.Children.Add(thumbArea);
                var card = Click(sp, () => { activate(win); CloseNow(); }, A(t.Accent, term ? (byte)40 : (byte)70), radius: new CornerRadius(retro || term ? 0 : 8), pad: new Thickness(8));
                card.Margin = new Thickness(2);
                row.Children.Add(card);
                targets.Add((win.Hwnd, thumbArea));
            }
            Content = retro ? (UIElement)new Bevel { Child = row } : term ? new Border { Child = row, BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1) } : (UIElement)row;
            if (term) { Background = B(0x000000); FontFamily = new FontFamily("Consolas"); }

            RenderGuard.Watch(this);
            MouseEnter += (s, e) => { over = true; closeTimer?.Stop(); };
            MouseLeave += (s, e) => { over = false; ScheduleClose(); };

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
                SetWindowLong(h, W32.GWL_EXSTYLE, ex | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE);
                int corner = retro || term || (t.Frame?.Square ?? false) ? 1 : 2;
                W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            Loaded += (s, e) =>
            {
                Place(anchor);
                foreach (var (hwnd, target) in targets) { var lt = new LiveThumb(this, hwnd, target); if (lt.Ok) thumbs.Add(lt); }
            };
            Closed += (s, e) => { foreach (var th in thumbs) th.Dispose(); thumbs.Clear(); };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        void Place(FrameworkElement anchor)
        {
            var bar = Window.GetWindow(anchor);
            if (bar == null) return;
            var src = PresentationSource.FromVisual(anchor);
            double s = src?.CompositionTarget.TransformToDevice.M11 ?? 1;
            var p = anchor.PointToScreen(new Point(0, 0));
            double ax = p.X / s, ay = p.Y / s, aw = anchor.ActualWidth, ah = anchor.ActualHeight;
            var edge = (bar as BarWindow)?.Edge ?? Edge.Bottom;
            var mi = W32.Monitor(W32.MonitorFromPoint(new W32.POINT { X = (int)p.X, Y = (int)p.Y }, 2), out _);
            double wl = mi.rcMonitor.Left / s, wr = mi.rcMonitor.Right / s, wt = mi.rcMonitor.Top / s, wb = mi.rcMonitor.Bottom / s;
            double x, y;
            switch (edge)
            {
                case Edge.Top: x = ax + aw / 2 - ActualWidth / 2; y = ay + ah + 6; break;
                case Edge.Left: x = ax + aw + 6; y = ay + ah / 2 - ActualHeight / 2; break;
                default: x = ax + aw / 2 - ActualWidth / 2; y = ay - ActualHeight - 8; break;
            }
            Left = Math.Max(wl + 4, Math.Min(x, wr - ActualWidth - 4));
            Top = Math.Max(wt + 4, Math.Min(y, wb - ActualHeight - 4));
        }
    }
}

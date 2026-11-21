using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// WinMorph's own notices in the look's design (Windows 7 balloon tips, frosted Zen cards, terminal banners):
    /// focus timer, Wi-Fi, the terminal timer. Other apps' notifications stay Windows': WinMorph neither reads them
    /// nor changes their settings (both look like spyware to antivirus programs). <see cref="RestoreBanners"/> only
    /// cleans up after version 6.2.0 test builds, which switched Windows' banners off.
    /// </summary>
    internal static class Notify
    {
        public sealed class Item { public DateTime When; public string App, Title, Body, Aumid; }

        static readonly List<Window> shown = new List<Window>();
        static readonly List<Item> history = new List<Item>();
        static ShellTheme theme;
        const int MaxShown = 3;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        /// <summary>Newest first; the Zen calendar shows them.</summary>
        public static IReadOnlyList<Item> History => history;
        /// <summary>Do not disturb: notices only gather in the history.</summary>
        public static bool Quiet;
        public static event Action Changed;

        public static void Dismiss(Item i) { history.Remove(i); Changed?.Invoke(); }
        public static void ClearHistory() { history.Clear(); Changed?.Invoke(); }

        const string SettingsRoot = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        const string BannersOff = "BannersOff";   // REG_MULTI_SZ in WinMorph's key: "aumid|original" ("-" = had no value)

        public static void Use(ShellTheme t)
        {
            Stop();
            theme = t;
        }

        public static void Stop()
        {
            theme = null;
            foreach (var w in shown.ToList()) try { w.Close(); } catch { }
            shown.Clear();
            RestoreBanners();
        }

        static List<string> Recorded() => (Core.Reg.Get(ShellControl.SettingsKey, BannersOff) as string[])?.ToList() ?? new List<string>();

        /// <summary>Gives every app its Windows banner back as it was. Safe to call from any process, any time.</summary>
        public static void RestoreBanners()
        {
            try
            {
                foreach (var e in Recorded())
                {
                    int bar = e.LastIndexOf('|');
                    if (bar <= 0) continue;
                    string aumid = e.Substring(0, bar), orig = e.Substring(bar + 1);
                    using (var k = Core.Reg.Open(SettingsRoot + "\\" + aumid, true))
                    {
                        if (k == null) continue;
                        if (orig == "-") k.DeleteValue("ShowBanner", false);
                        else if (int.TryParse(orig, out int v)) k.SetValue("ShowBanner", v, RegistryValueKind.DWord);
                    }
                }
                using (var k = Core.Reg.Open(ShellControl.SettingsKey, true)) k?.DeleteValue(BannersOff, false);
            }
            catch { }
        }

        static void Add(Item i)
        {
            history.Insert(0, i);
            while (history.Count > 30) history.RemoveAt(history.Count - 1);
            Changed?.Invoke();
        }

        /// <summary>The shell's own messages (Wi-Fi, timer, focus): into the history and onto the screen.</summary>
        public static void Show(string app, string title, string body = null, double seconds = 6)
        {
            if (theme == null) return;
            var i = new Item { When = DateTime.Now, App = app, Title = title, Body = body };
            Add(i);
            Banner(i, seconds);
        }

        static void Banner(Item i, double seconds = 6)
        {
            var t = theme;
            if (t == null || Quiet || CustomLock.Active) return;
            Window w;
            if (t.Start == StartKind.Terminal) w = TerminalBanner(t, i);
            else if (t.Id == "zen") w = ZenCard(i);
            else if (t.Id == "macos") w = MacPanels.Banner(i);
            else w = SevenBalloon(i);
            var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            close.Tick += (s, e) => { if (w.IsMouseOver) return; close.Stop(); Leave(w); };
            w.MouseLeftButtonUp += (s, e) =>
            {
                close.Stop(); Leave(w);
            };
            w.Closed += (s, e) => { close.Stop(); shown.Remove(w); Arrange(); };
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE);
            };
            RenderGuard.Watch(w);
            // Windows 7 showed one balloon at a time
            int max = t.Id == "win7" ? 1 : MaxShown;
            while (shown.Count >= max) shown[0].Close();
            shown.Add(w);
            w.Show();
            Arrange();
            Enter(w);
            close.Start();
        }

        static Window Base(bool transparent) => new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight, AllowsTransparency = transparent, Background = transparent ? Brushes.Transparent : Brushes.Black, Cursor = Cursors.Hand,
        };

        static Window TerminalBanner(ShellTheme t, Item i)
        {
            Color g = t.Accent, d = t.BarFgDim;
            var mono = new FontFamily("Consolas");
            var stack = new StackPanel { MaxWidth = 380 };
            stack.Children.Add(new TextBlock { Text = "[" + (i.App ?? "").ToLowerInvariant() + "]  " + i.When.ToString("HH:mm"), Foreground = B(d), FontFamily = mono, FontSize = 11.5 });
            if (!string.IsNullOrEmpty(i.Title)) stack.Children.Add(new TextBlock { Text = i.Title, Foreground = B(g), FontFamily = mono, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrEmpty(i.Body)) stack.Children.Add(new TextBlock { Text = Cut(i.Body), Foreground = B(Core.Applier.Mix(g, Colors.White, 0.35)), FontFamily = mono, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            var w = Base(false);
            w.Content = new Border { BorderBrush = B(0x2A2A2A), BorderThickness = new Thickness(1), Padding = new Thickness(16, 11, 16, 13), Child = stack, MinWidth = 300 };
            w.SourceInitialized += (s, e) => { var h = new WindowInteropHelper(w).Handle; int corner = 1; W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4); };
            return w;
        }

        static string Cut(string s) => s.Length > 300 ? s.Substring(0, 299) + "…" : s;

        /// <summary>Zen: a frosted card that surfaces at the lower right, above the bar.</summary>
        static Window ZenCard(Item i)
        {
            var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
            var top = new DockPanel();
            var when = ZenStyle.Text(i.When.ToString("HH:mm"), 11.5, ZenStyle.Ink2);
            DockPanel.SetDock(when, Dock.Right);
            top.Children.Add(when);
            var dot = new Ellipse { Width = 8, Height = 8, Fill = B(ZenStyle.Koi), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Left);
            top.Children.Add(dot);
            top.Children.Add(ZenStyle.Text(i.App ?? "", 12, ZenStyle.Pond, FontWeights.SemiBold));
            stack.Children.Add(top);
            if (!string.IsNullOrEmpty(i.Title)) { var tt = ZenStyle.Text(i.Title, 14.5, ZenStyle.Ink, FontWeights.SemiBold); tt.TextWrapping = TextWrapping.Wrap; tt.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(tt); }
            if (!string.IsNullOrEmpty(i.Body)) { var bt = ZenStyle.Text(Cut(i.Body), 13, ZenStyle.Ink2); bt.TextWrapping = TextWrapping.Wrap; bt.Margin = new Thickness(0, 2, 0, 0); stack.Children.Add(bt); }
            var pane = ZenStyle.Pane(stack, 20, 175);
            pane.Width = 340;
            var w = Base(true);
            w.Content = new Border { Child = pane, Padding = new Thickness(24, 14, 24, 30) };
            return w;
        }

        /// <summary>Windows 7: the balloon tip above the clock, with its tail and close button.</summary>
        static Window SevenBalloon(Item i)
        {
            var stack = new StackPanel { MaxWidth = 320 };
            var head = new DockPanel();
            var x = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 9, Foreground = B(0x6D6D6D), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0) };
            DockPanel.SetDock(x, Dock.Right);
            head.Children.Add(x);
            var info = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Top };
            info.Children.Add(new Ellipse { Fill = new RadialGradientBrush(C(0x7FB2F0), C(0x1F5FB8)) { GradientOrigin = new Point(0.4, 0.3) } });
            info.Children.Add(new TextBlock { Text = "i", Foreground = B(0xFFFFFF), FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Georgia"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(info, Dock.Left);
            head.Children.Add(info);
            var title = string.IsNullOrEmpty(i.Title) ? i.App : i.Title;
            head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = B(0x1E1E1E), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(head);
            var text = string.IsNullOrEmpty(i.Title) ? i.Body : (string.IsNullOrEmpty(i.Body) ? i.App : i.Body);
            if (!string.IsNullOrEmpty(text)) stack.Children.Add(new TextBlock { Text = Cut(text), FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = B(0x1E1E1E), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(23, 4, 0, 0) });
            // the balloon with its tail pointing down at the tray
            var shape = new Grid();
            var bubble = new Border
            {
                Child = stack, Padding = new Thickness(11, 9, 10, 12), CornerRadius = new CornerRadius(6),
                Background = V((0, C(0xFFFFFF)), (1, C(0xE4E5F0))), BorderBrush = B(0x767676), BorderThickness = new Thickness(1),
            };
            shape.Children.Add(bubble);
            var tail = new Path { Data = Geometry.Parse("M0,0 L18,0 L20,16 Z"), Fill = B(0xE4E5F0), Stroke = B(0x767676), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 46, -15) };
            var cover = new Border { Height = 2, Width = 17, Background = B(0xE4E5F0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 47, 0) };
            shape.Children.Add(tail); shape.Children.Add(cover);
            shape.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.35 };
            var w = Base(true);
            w.Content = new Border { Child = shape, Padding = new Thickness(10, 10, 10, 26) };
            return w;
        }

        static void Enter(Window w)
        {
            if (!(w.Content is FrameworkElement c)) return;
            if (theme?.Id == "zen") { ZenStyle.Ripple(c, new Point(0.9, 1), 420); return; }
            if (theme?.Id == "macos") { MacPanels.Enter(w); return; }
            c.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(theme?.Id == "win7" ? 250 : 160)));
        }

        static void Leave(Window w)
        {
            if (!(w.Content is FrameworkElement c) || !w.IsVisible) { try { w.Close(); } catch { } return; }
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            a.Completed += (s, e) => { try { w.Close(); } catch { } };
            c.BeginAnimation(UIElement.OpacityProperty, a);
        }

        static void Arrange()
        {
            var mi = W32.PrimaryMonitor(out double sc);
            double right = mi.rcWork.Right / sc, top = mi.rcWork.Top / sc, bottom = mi.rcWork.Bottom / sc;
            bool mac = theme?.Id == "macos";
            bool term = theme?.Start == StartKind.Terminal || mac;
            double y = term ? top + (mac ? -1 : 8) : bottom;
            for (int i = shown.Count - 1; i >= 0; i--)   // newest nearest to the bar
            {
                var w = shown[i];
                w.UpdateLayout();
                w.Left = right - w.ActualWidth - (mac ? -6 : term ? 10 : 0);
                if (term) { w.Top = y; y += w.ActualHeight + (mac ? -20 : 8); }
                else { w.Top = y - w.ActualHeight + (theme?.Id == "zen" ? 16 : 6); y = w.Top + (theme?.Id == "zen" ? 20 : 6); }
            }
        }
    }
}

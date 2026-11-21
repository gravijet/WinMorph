using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// The tray panels of a look: network, volume, battery, clock (calendar and notifications) and Zen's own
    /// calm panel. They replace Windows 11's quick settings, calendar and notification centre. Windows 7 gets
    /// its original flyouts (white body, glass rim, Aero controls, links to the classic control panels),
    /// Zen frosted glass that surfaces with a ripple, Hacker its terminal panels.
    /// </summary>
    internal static class Flyouts
    {
        /// <summary>Opens (or closes, when it is already open) a panel. kind: net, vol, bat, clock, zen.</summary>
        public static Window Open(ShellTheme t, string kind, Rect anchor)
        {
            if (t.Start == StartKind.Terminal)
            {
                HackerPanel.Toggle(kind == "bat" ? "power" : kind == "clock" ? "cal" : kind, t, anchor);
                return null;
            }
            return Flyout.Toggle(t, kind, anchor);
        }

        public static void CloseAll() { Flyout.CloseOpen(); HackerPanel.CloseAll(); }
    }

    internal sealed class Flyout : Window
    {
        static Flyout open;
        static string openKind;
        static DateTime closedAt;

        readonly ShellTheme t;
        readonly Rect anchor;
        readonly bool zen;
        readonly StackPanel body = new StackPanel();
        readonly StackPanel footer = new StackPanel();
        readonly List<DispatcherTimer> timers = new List<DispatcherTimer>();
        FrameworkElement surface;
        bool done;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;
        static CultureInfo Ci => De ? new CultureInfo("de-DE") : new CultureInfo("en-US");

        // Windows 7 colours
        static readonly Color SevenText = C(0x1E1E1E), SevenSub = C(0x5A5A5A), SevenLink = C(0x0066CC), SevenLine = C(0xD5DFE5);

        public static Flyout Toggle(ShellTheme t, string kind, Rect anchor)
        {
            if (open != null && openKind == kind) { open.Done(); return null; }
            if (openKind == kind && (DateTime.Now - closedAt).TotalMilliseconds < 300) return null;   // the click that just closed it
            open?.Done();
            var f = new Flyout(t, anchor, kind);
            open = f; openKind = kind;
            f.Show();
            f.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(f).Handle);
            return f;
        }

        public static void CloseOpen() => open?.Done();
        public static bool IsOpen(string kind) => open != null && openKind == kind;

        Flyout(ShellTheme theme, Rect anchorDip, string kind, bool activate = true)
        {
            t = theme; anchor = anchorDip; zen = t.Id == "zen";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
            ShowActivated = activate;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            FontFamily = zen ? ZenStyle.Family : new FontFamily("Segoe UI");
            FontSize = zen ? 13.5 : 12;
            if (!zen) { Resources.MergedDictionaries.Add(SevenWindow.Aero); Resources[typeof(TextBlock)] = new Style(typeof(TextBlock)); }
            else Resources[typeof(TextBlock)] = new Style(typeof(TextBlock));
            Deactivated += (s, e) => Done();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Done(); };
            Closed += (s, e) => { foreach (var tm in timers) tm.Stop(); };
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW);
            };

            switch (kind)
            {
                case "net": BuildNet(); break;
                case "vol": BuildVolume(); break;
                case "bat": BuildBattery(); break;
                case "zen": BuildZen(); break;
                default: BuildClock(); break;
            }
            surface = (FrameworkElement)Chrome();
            Content = surface;
            Loaded += (s, e) => { Place(); Reveal(); };
            SizeChanged += (s, e) => Place();
        }

        void Done()
        {
            if (done) return;
            done = true;
            if (open == this) { open = null; closedAt = DateTime.Now; }
            try { Close(); } catch { }
        }

        void Every(double seconds, Action a)
        {
            try { a(); } catch { }
            var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            tm.Tick += (s, e) => { try { a(); } catch { } };
            tm.Start();
            timers.Add(tm);
        }

        // ---------------- frame ----------------
        UIElement Chrome()
        {
            if (zen)
            {
                var inner = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
                inner.Children.Add(body);
                if (footer.Children.Count > 0) { footer.Margin = new Thickness(0, 14, 0, 0); inner.Children.Add(footer); }
                var pane = ZenStyle.Pane(inner, 24, 170);
                pane.Width = 360;
                return new Border { Child = pane, Padding = new Thickness(26, 18, 26, 30), Background = Brushes.Transparent };
            }
            // Windows 7: a white panel inside a thin glass rim, links on a light blue strip at the bottom
            var dock = new DockPanel { LastChildFill = true };
            if (footer.Children.Count > 0)
            {
                var foot = new Border { Child = footer, Padding = new Thickness(12, 8, 12, 9), Background = V((0, C(0xF4F7FB)), (1, C(0xE1E9F4))), BorderBrush = B(SevenLine), BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 3, 3) };
                DockPanel.SetDock(foot, Dock.Bottom);
                dock.Children.Add(foot);
            }
            body.Margin = new Thickness(14, 12, 14, 12);
            dock.Children.Add(body);
            var white = new Border { Child = dock, Background = B(0xFFFFFF), CornerRadius = new CornerRadius(3), BorderBrush = B(0x000000, 70), BorderThickness = new Thickness(1) };
            var rim = new Border
            {
                Child = white, Padding = new Thickness(5), CornerRadius = new CornerRadius(7),
                Background = V((0, C(0x9DB9DC, 235)), (0.5, C(0x6F94C2, 235)), (1, C(0x5A80AF, 240))),
                BorderBrush = B(0x0F2747, 170), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.45 },
            };
            var gloss = new Grid();
            gloss.Children.Add(rim);
            gloss.Children.Add(new Border { CornerRadius = new CornerRadius(7, 7, 0, 0), Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1), IsHitTestVisible = false, Background = V((0, C(0xFFFFFF, 90)), (1, C(0xFFFFFF, 0))) });
            return new Border { Child = gloss, Padding = new Thickness(10), Background = Brushes.Transparent };
        }

        void Place()
        {
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            double wl = mi.rcWork.Left / s, wt = mi.rcWork.Top / s, wr = mi.rcWork.Right / s, wb = mi.rcWork.Bottom / s;
            double x = anchor.Width > 0 ? anchor.Left + anchor.Width / 2 - ActualWidth / 2 : wr - ActualWidth;
            if (!zen && anchor.Width > 0) x = anchor.Right - ActualWidth + 10;
            double y = wb - ActualHeight + (zen ? 22 : 2);
            if (t.Layout == BarLayout.TerminalTop) y = wt;
            Left = Math.Max(wl, Math.Min(x, wr - ActualWidth));
            Top = Math.Max(wt, Math.Min(y, wb - ActualHeight + (zen ? 30 : 10)));
        }

        void Reveal()
        {
            if (!(Content is FrameworkElement c)) return;
            if (zen)
            {
                // surfaces from the tray icon like a ring of water
                double ox = 0.5;
                if (anchor.Width > 0 && ActualWidth > 0) ox = Math.Max(0.05, Math.Min(0.95, (anchor.Left + anchor.Width / 2 - Left) / ActualWidth));
                ZenStyle.Ripple(c, new Point(ox, 1.0), 460);
                return;
            }
            c.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            var tt = new TranslateTransform(0, 6);
            c.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ---------------- building blocks ----------------
        Color Fg => zen ? ZenStyle.Ink : SevenText;
        Color Sub => zen ? ZenStyle.Ink2 : SevenSub;

        TextBlock Txt(string s, double size, Color c, FontWeight? w = null)
        {
            var tb = new TextBlock { Text = s, FontSize = size, Foreground = B(c), FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FontFamily = zen ? ZenStyle.Family : new FontFamily("Segoe UI") };
            return tb;
        }

        TextBlock Heading(string s)
        {
            var tb = zen ? Txt(s, 19, ZenStyle.Ink, FontWeights.SemiBold) : Txt(s, 12, SevenText);
            tb.Margin = new Thickness(0, 0, 0, zen ? 12 : 8);
            body.Children.Add(tb);
            return tb;
        }

        /// <summary>Windows 7's section label: grey text with a thin line after it.</summary>
        void Section(string s)
        {
            if (zen) { var tb = Txt(s, 12, ZenStyle.Ink2, FontWeights.SemiBold); tb.Margin = new Thickness(2, 14, 0, 6); body.Children.Add(tb); return; }
            var g = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Txt(s, 12, C(0x1E3287)));
            var line = new Border { Height = 1, Background = B(SevenLine), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(line, 1);
            g.Children.Add(line);
            body.Children.Add(g);
        }

        void Link(string text, Action a)
        {
            var tb = Txt(text, zen ? 12.5 : 12, zen ? ZenStyle.Pond : SevenLink);
            tb.Cursor = Cursors.Hand; tb.Margin = new Thickness(0, 2, 0, 2);
            if (zen) tb.FontWeight = FontWeights.SemiBold;
            tb.MouseEnter += (s, e) => tb.TextDecorations = TextDecorations.Underline;
            tb.MouseLeave += (s, e) => tb.TextDecorations = null;
            tb.MouseLeftButtonUp += (s, e) => { e.Handled = true; Done(); a(); };
            footer.Children.Add(tb);
        }

        /// <summary>Zen: a soft pill button; Windows 7: an Aero push button.</summary>
        FrameworkElement Btn(string text, Action a, bool primary = false)
        {
            if (!zen)
            {
                var b = new Button { Content = text, MinWidth = 75, Height = 23, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = primary };
                b.Click += (s, e) => a();
                return b;
            }
            var tb = ZenStyle.Text(text, 13, primary ? Colors.White : ZenStyle.Ink, FontWeights.SemiBold);
            tb.Margin = new Thickness(16, 7, 16, 8);
            var bd = new Border { Child = tb, CornerRadius = new CornerRadius(17), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            ZenStyle.Press(bd, primary ? ZenStyle.Pond : C(0xFFFFFF, 170), primary ? ZenStyle.PondDeep : C(0xFFFFFF, 235));
            bd.MouseLeftButtonUp += (s, e) => { e.Handled = true; a(); };
            return bd;
        }

        /// <summary>Zen's toggle: a pebble that slides in a groove and colours it.</summary>
        FrameworkElement Switch(bool on, Action<bool> changed)
        {
            if (!zen)
            {
                var cb = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
                cb.Click += (s, e) => changed(cb.IsChecked == true);
                return cb;
            }
            var groove = new SolidColorBrush(on ? ZenStyle.Pond : C(0x8A9A9C, 120));
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.25 } };
            var move = new TranslateTransform(on ? 20 : 0, 0);
            knob.RenderTransform = move;
            var track = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Background = groove, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            bool state = on;
            track.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                state = !state;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(state ? 20 : 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ZenStyle.Surface });
                groove.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(state ? ZenStyle.Pond : C(0x8A9A9C, 120), TimeSpan.FromMilliseconds(220)));
                changed(state);
            };
            return track;
        }

        /// <summary>A labelled row with a control on the right.</summary>
        void Setting(string glyph, string title, FrameworkElement right, string sub = null)
        {
            var dock = new DockPanel { Margin = new Thickness(0, zen ? 6 : 4, 0, zen ? 6 : 4) };
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (glyph != null)
            {
                var g = Glyph(glyph, zen ? 16 : 14, zen ? ZenStyle.Pond : C(0x2B579A));
                g.Width = 26; g.HorizontalAlignment = HorizontalAlignment.Left;
                left.Children.Add(g);
            }
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Txt(title, zen ? 14 : 12, Fg, zen ? FontWeights.SemiBold : FontWeights.Normal));
            if (sub != null) words.Children.Add(Txt(sub, zen ? 12 : 11, Sub));
            left.Children.Add(words);
            dock.Children.Add(left);
            body.Children.Add(dock);
        }

        /// <summary>A horizontal level (volume, brightness). Zen: a water line in a rounded groove; Windows 7: the Aero slider.</summary>
        FrameworkElement Level(int value, Action<int> changed, out Action<int> update)
        {
            if (!zen)
            {
                var sl = new Slider { Minimum = 0, Maximum = 100, Value = value, Width = 210, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, SmallChange = 2, LargeChange = 10 };
                bool quiet = false;
                sl.ValueChanged += (s, e) => { if (!quiet) changed((int)Math.Round(sl.Value)); };
                update = v => { quiet = true; sl.Value = v; quiet = false; };
                return sl;
            }
            var fill = new Border { Width = 14, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(ZenStyle.Pond, C(0x2FB3A6), 0) };
            var knob = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = B(0xFFFFFF), BorderBrush = B(ZenStyle.Pond), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.25 } };
            var groove = new Grid { Height = 24, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var track = new Border { Height = 14, CornerRadius = new CornerRadius(7), Background = B(C(0x8A9A9C, 70)), VerticalAlignment = VerticalAlignment.Center };
            track.Child = fill;
            groove.Children.Add(track);
            groove.Children.Add(knob);
            int cur = value;
            void Show(int v, bool animate)
            {
                cur = Math.Max(0, Math.Min(100, v));
                double w = Math.Max(0, groove.ActualWidth - 22);
                double x = w * cur / 100.0;
                var d = TimeSpan.FromMilliseconds(animate ? 220 : 0);
                fill.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(14, x + 11), d) { EasingFunction = ZenStyle.Surface });
                knob.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(x, 0, 0, 0), d) { EasingFunction = ZenStyle.Surface });
            }
            groove.SizeChanged += (s, e) => Show(cur, false);
            void FromMouse(MouseEventArgs e)
            {
                double w = Math.Max(1, groove.ActualWidth - 22);
                int v = (int)Math.Round((e.GetPosition(groove).X - 11) / w * 100);
                v = Math.Max(0, Math.Min(100, v));
                Show(v, false); changed(v);
            }
            groove.MouseLeftButtonDown += (s, e) => { groove.CaptureMouse(); FromMouse(e); e.Handled = true; };
            groove.MouseMove += (s, e) => { if (groove.IsMouseCaptured) FromMouse(e); };
            groove.MouseLeftButtonUp += (s, e) => { groove.ReleaseMouseCapture(); e.Handled = true; };
            groove.MouseWheel += (s, e) => { int v = Math.Max(0, Math.Min(100, cur + (e.Delta > 0 ? 2 : -2))); Show(v, true); changed(v); };
            update = v => Show(v, true);
            return groove;
        }

        // ================= network =================
        void BuildNet()
        {
            var list = new StackPanel();
            var status = Txt("", zen ? 12.5 : 11.5, Sub);
            status.Margin = new Thickness(0, 6, 0, 0);
            bool wifi = Wlan.Available;
            if (zen)
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                FrameworkElement sw = null;
                if (wifi)
                {
                    sw = Switch(true, on => { status.Text = on ? L("WLAN wird eingeschaltet …", "Turning Wi-Fi on …") : L("WLAN wird ausgeschaltet …", "Turning Wi-Fi off …"); _ = Hardware.SetRadio(Hardware.RadioKind.WiFi, on); });
                    DockPanel.SetDock(sw, Dock.Right);
                    head.Children.Add(sw);
                }
                var h = Txt(wifi ? "WLAN" : L("Netzwerk", "Network"), 19, ZenStyle.Ink, FontWeights.SemiBold);
                head.Children.Add(h);
                body.Children.Add(head);
            }
            else Heading(L("Derzeit verbunden mit:", "Currently connected to:"));
            var current = new StackPanel();
            body.Children.Add(current);
            if (wifi)
            {
                Section(zen ? L("In der Nähe", "Nearby") : L("Drahtlosnetzwerkverbindung", "Wireless Network Connection"));
                var scroll = new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
                body.Children.Add(scroll);
            }
            body.Children.Add(status);
            string expanded = null;
            Action refresh = null;
            refresh = () =>
            {
                var nets = wifi ? Wlan.Networks() : new List<Wlan.Net>();
                var con = nets.FirstOrDefault(n => n.Connected);
                current.Children.Clear();
                if (con != null) current.Children.Add(NetCard(con.Ssid, L("Internetzugriff", "Internet access"), Signal(con.Signal, true)));
                else
                {
                    var eth = SysInfo.Adapters().FirstOrDefault(a => a.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet);
                    if (eth != null) current.Children.Add(NetCard(eth.Name, L("Verbunden über Kabel", "Connected by cable"), Glyph("", 18, zen ? ZenStyle.Pond : C(0x2B579A))));
                    else current.Children.Add(Txt(L("Nicht verbunden", "Not connected"), zen ? 14 : 12, Sub));
                }
                list.Children.Clear();
                foreach (var n in nets.Where(x => !x.Connected).Take(12))
                {
                    var net = n;
                    var row = new StackPanel();
                    var line = new DockPanel { Margin = new Thickness(zen ? 4 : 2, 5, 4, 5) };
                    var sig = Signal(n.Signal, false);
                    DockPanel.SetDock(sig, Dock.Right);
                    line.Children.Add(sig);
                    if (n.Secured) { var lk = Glyph("", 11, Sub); lk.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(lk, Dock.Right); line.Children.Add(lk); }
                    line.Children.Add(Txt(n.Ssid, zen ? 14 : 12, Fg));
                    row.Children.Add(line);
                    var box = new Border { Child = row, CornerRadius = new CornerRadius(zen ? 12 : 3), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Padding = new Thickness(zen ? 6 : 2, 1, 6, 1), Margin = new Thickness(0, 1, 0, 1) };
                    box.MouseEnter += (s, e) => { if (zen) box.Background = B(0xFFFFFF, 130); else { box.Background = V((0, C(0xF1F6FD)), (1, C(0xE3EEFC))); box.BorderBrush = B(0xB8D6FB); } };
                    box.MouseLeave += (s, e) => { if (expanded == net.Ssid) return; box.Background = Brushes.Transparent; box.BorderBrush = Brushes.Transparent; };
                    if (expanded == n.Ssid)
                    {
                        box.Background = zen ? B(0xFFFFFF, 160) : V((0, C(0xF1F6FD)), (1, C(0xD5E7FC)));
                        var act = new StackPanel { Margin = new Thickness(zen ? 4 : 2, 4, 4, 8) };
                        PasswordBox pw = null;
                        if (n.Profile == null && n.Secured && n.Personal)
                        {
                            act.Children.Add(Txt(L("Netzwerkschlüssel", "Network key"), zen ? 12 : 11.5, Sub));
                            pw = new PasswordBox { Margin = new Thickness(0, 3, 0, 6), Height = zen ? 30 : 22, FontSize = 13, Padding = new Thickness(zen ? 10 : 2, 3, 6, 3) };
                            if (zen) { pw.BorderThickness = new Thickness(1); pw.BorderBrush = B(A(ZenStyle.Pond, 140)); pw.Background = B(0xFFFFFF, 220); }
                            act.Children.Add(pw);
                        }
                        Action connect = () =>
                        {
                            status.Text = L("Verbindung wird hergestellt …", "Connecting …");
                            var pass = pw?.Password;
                            Task.Run(() => net.Profile != null ? Wlan.Connect(net.Profile) : (net.Secured && !net.Personal ? "enterprise" : Wlan.ConnectNew(net, pass)))
                                .ContinueWith(r => { status.Text = r.Result == null ? L("Verbunden mit ", "Connected to ") + net.Ssid : r.Result == "enterprise" ? L("Dieses Netz braucht eine Firmen-Anmeldung – bitte über die Windows-Einstellungen verbinden.", "This network needs a company sign-in – please connect in Windows settings.") : L("Verbinden fehlgeschlagen.", "Could not connect."); expanded = null; refresh(); }, TaskScheduler.FromCurrentSynchronizationContext());
                        };
                        if (pw != null) pw.KeyDown += (s, e) => { if (e.Key == Key.Enter) connect(); };
                        act.Children.Add(Btn(L("Verbinden", "Connect"), connect, true));
                        row.Children.Add(act);
                        if (pw != null) Dispatcher.BeginInvoke(new Action(() => pw.Focus()), DispatcherPriority.Input);
                    }
                    box.MouseLeftButtonUp += (s, e) => { if (expanded == net.Ssid) return; expanded = net.Ssid; refresh(); };
                    list.Children.Add(box);
                }
                if (con != null)
                {
                    current.Children.Add(new Border
                    {
                        Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                        Child = Btn(L("Trennen", "Disconnect"), () => { Wlan.Disconnect(); status.Text = L("Getrennt.", "Disconnected."); refresh(); }),
                    });
                }
            };
            if (wifi) Task.Run(() => Wlan.Scan());
            refresh();
            Every(4, () => { if (expanded == null) refresh(); });
            if (zen) Link(L("Netzwerkeinstellungen", "Network settings"), () => ShellActions.Run("ms-settings:network"));
            else Link(L("Netzwerk- und Freigabecenter öffnen", "Open Network and Sharing Center"), () => ShellActions.Run("control.exe", "/name Microsoft.NetworkAndSharingCenter"));
        }

        FrameworkElement NetCard(string name, string sub, FrameworkElement icon)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
            var words = new StackPanel();
            words.Children.Add(Txt(name, zen ? 15 : 12, Fg, zen ? FontWeights.SemiBold : FontWeights.Bold));
            words.Children.Add(Txt(sub, zen ? 12.5 : 11.5, Sub));
            dock.Children.Add(words);
            if (!zen) return dock;
            return new Border { Child = dock, CornerRadius = new CornerRadius(14), Background = B(0xFFFFFF, 150), Padding = new Thickness(12, 10, 12, 10) };
        }

        /// <summary>Signal strength: Windows 7's five green bars, or Zen's three rising stones.</summary>
        FrameworkElement Signal(int pct, bool big)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Height = zen ? 16 : 14 };
            int n = zen ? 4 : 5;
            int lit = Math.Max(1, (int)Math.Ceiling(pct / (100.0 / n)));
            for (int i = 0; i < n; i++)
            {
                double h = (zen ? 5 : 3) + i * (zen ? 3.5 : 2.4);
                bool on = i < lit;
                sp.Children.Add(new Border
                {
                    Width = zen ? 4 : 3, Height = h, Margin = new Thickness(0, 0, zen ? 2 : 1, 0), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(zen ? 2 : 0),
                    Background = zen ? B(on ? ZenStyle.Pond : C(0x8A9A9C, 90)) : (on ? (Brush)V((0, C(0x8FE07A)), (1, C(0x2E9A1E))) : B(0xD7D7D7)),
                    BorderBrush = zen ? null : B(on ? C(0x1F6E14) : C(0xA0A0A0)), BorderThickness = new Thickness(zen ? 0 : 0.6),
                });
            }
            return sp;
        }

        // ================= volume =================
        void BuildVolume()
        {
            if (!zen) { BuildSevenVolume(); return; }
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var pct = Txt("", 19, ZenStyle.Pond, FontWeights.SemiBold);
            DockPanel.SetDock(pct, Dock.Right);
            head.Children.Add(pct);
            head.Children.Add(Txt(L("Klang", "Sound"), 19, ZenStyle.Ink, FontWeights.SemiBold));
            body.Children.Add(head);
            var dev = Txt(Audio.DeviceName, 12.5, ZenStyle.Ink2);
            dev.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(dev);
            Border mute = null;
            var row = new DockPanel();
            Action<int> setLevel = null;
            var level = Level(Math.Max(0, Audio.Level), v => { Audio.Level = v; pct.Text = v + " %"; UpdateMute(); }, out setLevel);
            mute = ZenStyle.Stone("", () => { Audio.Muted = !Audio.Muted; UpdateMute(); }, L("Stumm", "Mute"), 40);
            mute.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(mute, Dock.Left);
            row.Children.Add(mute);
            row.Children.Add(level);
            body.Children.Add(row);
            void UpdateMute()
            {
                bool m = Audio.Muted;
                ((TextBlock)mute.Child).Text = m ? "" : "";
                ((TextBlock)mute.Child).Foreground = B(m ? ZenStyle.Momiji : ZenStyle.Ink);
            }
            int last = -2; bool lastMute = false;
            Every(0.5, () =>
            {
                int l = Audio.Level; bool m = Audio.Muted;
                if (l == last && m == lastMute) return;
                last = l; lastMute = m;
                pct.Text = l < 0 ? "" : l + " %";
                if (l >= 0) setLevel(l);
                UpdateMute();
                dev.Text = l < 0 ? L("Kein Ausgabegerät", "No output device") : Audio.DeviceName;
            });
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Right || e.Key == Key.Up) { Audio.Change(2); e.Handled = true; }
                else if (e.Key == Key.Left || e.Key == Key.Down) { Audio.Change(-2); e.Handled = true; }
            };
        }

        /// <summary>Windows 7's volume flyout: the speaker, a tall slider, the mute button and "Mixer".</summary>
        void BuildSevenVolume()
        {
            var col = new StackPanel { Width = 92, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = new TrayGlyph(TrayGlyph.Kind.Sound, true, C(0x6E8FBA), 30) { Level = 100, HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(icon);
            var name = Txt(Audio.DeviceName.Split('(')[0].Trim(), 11, SevenText);
            name.TextAlignment = TextAlignment.Center; name.Margin = new Thickness(0, 4, 0, 8); name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap;
            col.Children.Add(name);
            var sl = new Slider { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, IsMoveToPointEnabled = true, SmallChange = 2, LargeChange = 10, TickPlacement = TickPlacement.Both, TickFrequency = 10 };
            col.Children.Add(sl);
            var pct = Txt("", 11, SevenSub);
            pct.TextAlignment = TextAlignment.Center; pct.Margin = new Thickness(0, 4, 0, 6);
            col.Children.Add(pct);
            var muteGlyph = Glyph("", 16, C(0x2B579A));
            var mute = new Button { Content = muteGlyph, Width = 36, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = L("Lautsprecher stummschalten", "Mute speakers") };
            col.Children.Add(mute);
            body.Children.Add(col);
            bool quiet = false;
            sl.ValueChanged += (s, e) => { if (quiet) return; Audio.Level = (int)Math.Round(sl.Value); };
            mute.Click += (s, e) => Audio.Muted = !Audio.Muted;
            Every(0.4, () =>
            {
                int l = Audio.Level; bool m = Audio.Muted;
                quiet = true; if (l >= 0) sl.Value = l; quiet = false;
                pct.Text = l < 0 ? "" : l.ToString();
                muteGlyph.Text = m ? "" : "";
                muteGlyph.Foreground = B(m ? C(0xC42B1C) : C(0x2B579A));
            });
            Link("Mixer", () => ShellActions.Run("sndvol.exe"));
        }


        // ================= battery and power =================
        void BuildBattery()
        {
            var state = Txt("", zen ? 14 : 12, Fg);
            TextBlock big = null;
            Border water = null; Border waterBox = null;
            if (zen)
            {
                var head = new DockPanel();
                big = Txt("", 40, ZenStyle.Ink, FontWeights.Light);
                head.Children.Add(Txt(L("Akku", "Battery"), 19, ZenStyle.Ink, FontWeights.SemiBold));
                body.Children.Add(head);
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
                DockPanel.SetDock(big, Dock.Left);
                row.Children.Add(big);
                state.VerticalAlignment = VerticalAlignment.Center; state.Margin = new Thickness(14, 0, 0, 0);
                row.Children.Add(state);
                body.Children.Add(row);
                // the charge as a level of water in a rounded vessel
                water = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), Background = new LinearGradientBrush(ZenStyle.Moss, C(0x7CC36A), 0) };
                waterBox = new Border { Height = 16, CornerRadius = new CornerRadius(8), Background = B(C(0x8A9A9C, 70)), Child = water, Margin = new Thickness(0, 4, 0, 4) };
                body.Children.Add(waterBox);
            }
            else
            {
                var dock = new DockPanel();
                var b0 = Hardware.Battery();
                var img = new TrayGlyph(TrayGlyph.Kind.Battery, true, C(0xFFFFFF), 32) { Level = b0?.Percent ?? 100, Charging = b0 != null && b0.Plugged, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(img, Dock.Left);
                dock.Children.Add(img);
                dock.Children.Add(state);
                body.Children.Add(dock);
            }
            Every(5, () =>
            {
                var b = Hardware.Battery();
                if (b == null) { state.Text = L("Netzbetrieb (kein Akku)", "Plugged in (no battery)"); if (big != null) big.Text = ""; return; }
                string how = b.Charging ? L("wird geladen", "charging") : b.Plugged ? L("Netzbetrieb", "plugged in") : b.Left.HasValue ? Hardware.Duration(b.Left.Value, De) + L(" verbleibend", " remaining") : L("Akkubetrieb", "on battery");
                if (zen)
                {
                    big.Text = b.Percent + " %";
                    state.Text = how + (b.Saver ? "\n" + L("Energiesparen ist an", "Energy saver is on") : "");
                    waterBox.UpdateLayout();
                    double w = Math.Max(16, waterBox.ActualWidth * b.Percent / 100.0);
                    water.Width = w;
                    water.Background = new LinearGradientBrush(b.Percent <= 15 && !b.Plugged ? ZenStyle.Momiji : ZenStyle.Moss, b.Percent <= 15 && !b.Plugged ? ZenStyle.Koi : C(0x7CC36A), 0);
                }
                else state.Text = b.Left.HasValue ? L("Verbleibend: ", "Remaining: ") + Hardware.Duration(b.Left.Value, De) + " (" + b.Percent + " %)" : b.Percent + L(" % verfügbar (", "% available (") + how + ")";
            });

            var plans = Hardware.PowerPlans();
            if (plans.Count > 0)
            {
                Section(zen ? L("Energiemodus", "Power mode") : L("Energiesparplan auswählen:", "Select a power plan:"));
                var active = Hardware.ActivePlan;
                if (!zen)
                {
                    foreach (var p in plans)
                    {
                        var id = p.id;
                        var rb = new RadioButton { Content = p.name, IsChecked = id == active, GroupName = "plan", Margin = new Thickness(2, 3, 0, 3) };
                        rb.Checked += (s, e) => Hardware.ActivePlan = id;
                        body.Children.Add(rb);
                    }
                }
                else
                {
                    var wrap = new WrapPanel();
                    var chips = new List<(Guid id, Border b, TextBlock tb)>();
                    void Mark(Guid on) { foreach (var c in chips) { bool sel = c.id == on; c.b.Background = B(sel ? ZenStyle.Pond : C(0xFFFFFF, 150)); c.tb.Foreground = B(sel ? Colors.White : ZenStyle.Ink); } }
                    foreach (var p in plans)
                    {
                        var id = p.id;
                        var tb = ZenStyle.Text(p.name, 12.5, ZenStyle.Ink, FontWeights.SemiBold);
                        tb.Margin = new Thickness(12, 5, 12, 6);
                        var chip = new Border { Child = tb, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand };
                        chip.MouseLeftButtonUp += (s, e) => { Hardware.ActivePlan = id; Mark(id); };
                        chips.Add((id, chip, tb));
                        wrap.Children.Add(chip);
                    }
                    Mark(active);
                    body.Children.Add(wrap);
                }
            }
            BrightnessRow();
            if (zen) Link(L("Energieeinstellungen", "Power settings"), () => ShellActions.Run("ms-settings:powersleep"));
            else Link(L("Weitere Energieoptionen", "More power options"), () => ShellActions.Run("control.exe", "powercfg.cpl"));
        }

        void BrightnessRow()
        {
            var holder = new StackPanel();
            body.Children.Add(holder);
            Task.Run(() => Hardware.Brightness()).ContinueWith(r =>
            {
                if (r.Result < 0 || done) return;
                var lbl = zen ? Txt(L("Helligkeit", "Brightness"), 12, ZenStyle.Ink2, FontWeights.SemiBold) : Txt(L("Bildschirmhelligkeit:", "Screen brightness:"), 12, C(0x1E3287));
                lbl.Margin = new Thickness(zen ? 2 : 0, 14, 0, 6);
                holder.Children.Add(lbl);
                var row = new DockPanel();
                var sun = Glyph("", zen ? 18 : 14, zen ? ZenStyle.Sun : C(0xD08A00));
                sun.Margin = new Thickness(0, 0, 12, 0); sun.Width = 22;
                DockPanel.SetDock(sun, Dock.Left);
                row.Children.Add(sun);
                row.Children.Add(Level(r.Result, v => Hardware.SetBrightness(v), out _));
                holder.Children.Add(row);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ================= clock, calendar, notifications =================
        void BuildClock()
        {
            if (!zen) { BuildSevenClock(); return; }
            var time = Txt("", 44, ZenStyle.Ink, FontWeights.Light);
            var date = Txt("", 14, ZenStyle.Ink2);
            body.Children.Add(time);
            body.Children.Add(date);
            Every(1, () => { var n = DateTime.Now; time.Text = n.ToString("HH:mm"); date.Text = n.ToString(De ? "dddd, d. MMMM yyyy" : "dddd, MMMM d, yyyy", Ci); });
            body.Children.Add(new Border { Height = 12 });
            body.Children.Add(ZenCalendar());

            // notifications: the newest first, each can be dismissed
            var head = new DockPanel { Margin = new Thickness(0, 18, 0, 6) };
            var clear = Txt(L("Alle löschen", "Clear all"), 12.5, ZenStyle.Pond, FontWeights.SemiBold);
            clear.Cursor = Cursors.Hand;
            DockPanel.SetDock(clear, Dock.Right);
            head.Children.Add(clear);
            head.Children.Add(Txt(L("Mitteilungen", "Notifications"), 12, ZenStyle.Ink2, FontWeights.SemiBold));
            body.Children.Add(head);
            var list = new StackPanel();
            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
            void Fill()
            {
                list.Children.Clear();
                var items = Notify.History.ToList();
                clear.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (items.Count == 0) { list.Children.Add(Txt(L("Keine Mitteilungen", "No notifications"), 13, ZenStyle.Ink2)); return; }
                foreach (var n in items)
                {
                    var item = n;
                    var card = new StackPanel();
                    var top = new DockPanel();
                    var when = Txt(item.When.ToString("HH:mm"), 11.5, ZenStyle.Ink2);
                    DockPanel.SetDock(when, Dock.Right);
                    top.Children.Add(when);
                    top.Children.Add(Txt(item.App, 11.5, ZenStyle.Pond, FontWeights.SemiBold));
                    card.Children.Add(top);
                    if (!string.IsNullOrEmpty(item.Title)) card.Children.Add(Txt(item.Title, 13.5, ZenStyle.Ink, FontWeights.SemiBold));
                    if (!string.IsNullOrEmpty(item.Body)) { var bt = Txt(item.Body, 12.5, ZenStyle.Ink2); bt.MaxHeight = 54; bt.TextTrimming = TextTrimming.CharacterEllipsis; card.Children.Add(bt); }
                    var box = new Border { Child = card, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, ToolTip = L("Klick blendet aus", "Click to dismiss") };
                    ZenStyle.Press(box, C(0xFFFFFF, 150), C(0xFFFFFF, 215));
                    box.MouseLeftButtonUp += (s, e) => { Notify.Dismiss(item); Fill(); };
                    list.Children.Add(box);
                }
            }
            clear.MouseLeftButtonUp += (s, e) => { Notify.ClearHistory(); Fill(); };
            Fill();
        }

        FrameworkElement ZenCalendar()
        {
            var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var root = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var title = Txt("", 14, ZenStyle.Ink, FontWeights.SemiBold);
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            var grid = new UniformGrid { Columns = 7 };
            void Draw()
            {
                title.Text = month.ToString("MMMM yyyy", Ci);
                grid.Children.Clear();
                var names = Ci.DateTimeFormat.AbbreviatedDayNames;
                for (int i = 0; i < 7; i++)
                {
                    var tb = Txt(names[(i + 1) % 7].Substring(0, 2), 11.5, ZenStyle.Ink2, FontWeights.SemiBold);
                    tb.TextAlignment = TextAlignment.Center; tb.Margin = new Thickness(0, 0, 0, 4);
                    grid.Children.Add(tb);
                }
                int offset = ((int)month.DayOfWeek + 6) % 7;
                for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
                for (int d = 1; d <= DateTime.DaysInMonth(month.Year, month.Month); d++)
                {
                    bool today = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month && d == DateTime.Today.Day;
                    bool weekend = (offset + d - 1) % 7 >= 5;
                    var tb = Txt(d.ToString(), 13, today ? Colors.White : weekend ? ZenStyle.Ink2 : ZenStyle.Ink, today ? FontWeights.SemiBold : FontWeights.Normal);
                    tb.TextAlignment = TextAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center;
                    grid.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = today ? B(ZenStyle.Koi) : Brushes.Transparent, Child = tb });
                }
            }
            Border Arrow(string g, int delta)
            {
                var b = ZenStyle.Stone(g, () => { month = month.AddMonths(delta); Draw(); }, null, 28, C(0xFFFFFF, 120));
                b.Margin = new Thickness(4, 0, 0, 0);
                return b;
            }
            nav.Children.Add(Arrow("", -1)); nav.Children.Add(Arrow("", 1));
            DockPanel.SetDock(nav, Dock.Right);
            head.Children.Add(nav);
            head.Children.Add(title);
            root.Children.Add(head);
            root.Children.Add(grid);
            Draw();
            return root;
        }

        /// <summary>Windows 7's clock flyout: the long date, an analog clock beside the month calendar, and the settings link.</summary>
        void BuildSevenClock()
        {
            var date = Txt("", 12, SevenLink);
            date.Margin = new Thickness(2, 0, 0, 8);
            body.Children.Add(date);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var clock = new AnalogClock { Width = 150, Height = 150, Margin = new Thickness(0, 4, 18, 0), VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(clock);
            var cal = new System.Windows.Controls.Calendar { DisplayDate = DateTime.Today, SelectedDate = DateTime.Today, IsTodayHighlighted = true, Language = System.Windows.Markup.XmlLanguage.GetLanguage(Ci.IetfLanguageTag) };
            row.Children.Add(cal);
            body.Children.Add(row);
            Every(1, () => { date.Text = DateTime.Now.ToString(De ? "dddd, d. MMMM yyyy" : "dddd, MMMM d, yyyy", Ci); clock.InvalidateVisual(); });
            Link(L("Datum- und Uhrzeiteinstellungen ändern …", "Change date and time settings …"), () => ShellActions.Run("control.exe", "timedate.cpl"));
        }

        sealed class AnalogClock : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth, r = w / 2;
                var c = new Point(r, r);
                dc.DrawEllipse(new RadialGradientBrush(C(0xFFFFFF), C(0xD9E3EE)) { GradientOrigin = new Point(0.4, 0.3) }, new Pen(B(0x8AA2BD), 3), c, r - 2, r - 2);
                dc.DrawEllipse(null, new Pen(B(0xFFFFFF, 200), 1), c, r - 5, r - 5);
                for (int i = 0; i < 60; i++)
                {
                    double a = i * Math.PI / 30;
                    bool hour = i % 5 == 0;
                    double r1 = r - (hour ? 16 : 11), r2 = r - 8;
                    dc.DrawLine(new Pen(B(hour ? C(0x2E3C4D) : C(0x8A98A8)), hour ? 2.2 : 1), new Point(r + r1 * Math.Sin(a), r - r1 * Math.Cos(a)), new Point(r + r2 * Math.Sin(a), r - r2 * Math.Cos(a)));
                }
                var now = DateTime.Now;
                void Hand(double angle, double len, double width, Color col)
                {
                    double a = angle * Math.PI / 180;
                    var pen = new Pen(B(col), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(pen, new Point(r - 8 * Math.Sin(a), r + 8 * Math.Cos(a)), new Point(r + len * Math.Sin(a), r - len * Math.Cos(a)));
                }
                Hand((now.Hour % 12 + now.Minute / 60.0) * 30, r * 0.5, 4.5, C(0x1F2A36));
                Hand((now.Minute + now.Second / 60.0) * 6, r * 0.72, 3, C(0x1F2A36));
                Hand(now.Second * 6, r * 0.78, 1.2, C(0xC42B1C));
                dc.DrawEllipse(B(0x1F2A36), null, c, 4, 4);
            }
        }

        // ================= Zen: calm =================
        void BuildZen()
        {
            Heading(L("Ruhe", "Calm"));
            // focus timer
            var focusText = Txt("", 12.5, ZenStyle.Ink2);
            FrameworkElement focusBtn = null;
            var focusHolder = new Border();
            void FocusUi()
            {
                focusText.Text = Zen.Running ? Zen.TimerText : L("25 Minuten Fokus, dann 5 Minuten Pause", "25 minutes of focus, then a 5 minute break");
                focusHolder.Child = focusBtn = Btn(Zen.Running ? L("Beenden", "Stop") : L("Starten", "Start"), () => { Zen.ToggleTimer(); FocusUi(); }, !Zen.Running);
            }
            Setting("", L("Fokus", "Focus"), focusHolder, null);
            body.Children.Add(focusText);
            ((FrameworkElement)body.Children[body.Children.Count - 1]).Margin = new Thickness(26, -4, 0, 4);
            FocusUi();
            Every(1, () => { if (Zen.Running) focusText.Text = Zen.TimerText; });

            Setting("", L("Atmen", "Breathe"), Btn(L("Eine Minute", "One minute"), () => { Done(); Zen.Breathe(t); }), L("Ein, halten, aus – sechs Runden", "In, hold, out – six rounds"));

            // background sound
            var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 2, 0, 6) };
            var chips = new List<(int i, Border b, TextBlock tb)>();
            void Mark() { foreach (var c in chips) { bool sel = c.i == Zen.Sound; c.b.Background = B(sel ? ZenStyle.Pond : C(0xFFFFFF, 150)); c.tb.Foreground = B(sel ? Colors.White : ZenStyle.Ink); } }
            string[] names = { L("Aus", "Off"), L("Rauschen", "Noise"), L("Regen", "Rain") };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var tb = ZenStyle.Text(names[i], 12.5, ZenStyle.Ink, FontWeights.SemiBold);
                tb.Margin = new Thickness(12, 5, 12, 6);
                var chip = new Border { Child = tb, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
                chip.MouseLeftButtonUp += (s, e) => { Zen.SetSound(idx); Mark(); };
                chips.Add((i, chip, tb));
                soundRow.Children.Add(chip);
            }
            Mark();
            var soundLbl = Txt(L("Hintergrundklang", "Background sound"), 14, ZenStyle.Ink, FontWeights.SemiBold);
            var sh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            var sg = Glyph("", 16, ZenStyle.Pond); sg.Width = 26; sg.HorizontalAlignment = HorizontalAlignment.Left;
            sh.Children.Add(sg); sh.Children.Add(soundLbl);
            body.Children.Add(sh);
            body.Children.Add(soundRow);

            Setting("", L("Nicht stören", "Do not disturb"), Switch(Notify.Quiet, on => Notify.Quiet = on), L("Mitteilungen sammeln sich leise im Kalender", "Notifications gather quietly in the calendar"));

            var bt = new Border();
            Setting("", "Bluetooth", bt);
            Hardware.RadioOn(Hardware.RadioKind.Bluetooth).ContinueWith(r =>
            {
                if (r.Result == null) { bt.Child = Txt(L("nicht vorhanden", "not available"), 12, ZenStyle.Ink2); return; }
                bt.Child = Switch(r.Result.Value, on => _ = Hardware.SetRadio(Hardware.RadioKind.Bluetooth, on));
            }, TaskScheduler.FromCurrentSynchronizationContext());
            BrightnessRow();
        }
    }
}

namespace WinMorph.Shell
{
    /// <summary>
    /// What the volume keys show instead of Windows' pop-up: Zen a frosted pill above the bar,
    /// Windows 7 a small white panel with the Aero level bar at the tray. Never takes the focus.
    /// </summary>
    internal static class VolumeOsd
    {
        static Window w;
        static ShellTheme theme;
        static System.Windows.Threading.DispatcherTimer hide;
        static Action update;

        public static void Show(ShellTheme t)
        {
            if (Flyout.IsOpen("vol")) return;   // the open volume panel shows it already
            if (w == null || theme != t) Build(t);
            update();
            if (!w.IsVisible)
            {
                w.Show();
                if (w.Content is FrameworkElement c)
                {
                    if (t.Id == "zen") ZenStyle.Ripple(c, new Point(0.5, 1), 360);
                    else c.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
                }
            }
            Place();
            hide.Stop(); hide.Start();
        }

        static void Build(ShellTheme t)
        {
            try { w?.Close(); } catch { }
            theme = t;
            bool zen = t.Id == "zen";
            w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, AllowsTransparency = true, Background = Brushes.Transparent, UseLayoutRounding = true,
            };
            w.SourceInitialized += (s, e) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE | 0x20);
            };
            RenderGuard.Watch(w);
            var glyph = Ui.Glyph("", zen ? 18 : 16, zen ? ZenStyle.Ink : Ui.C(0x2B579A));
            glyph.Width = 24;
            var pct = new TextBlock { FontFamily = zen ? ZenStyle.Family : new FontFamily("Segoe UI"), FontSize = zen ? 14 : 12, FontWeight = zen ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.B(zen ? ZenStyle.Ink : Ui.C(0x1E1E1E)), Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Width = 230 };
            DockPanel.SetDock(glyph, Dock.Left); DockPanel.SetDock(pct, Dock.Right);
            row.Children.Add(glyph); row.Children.Add(pct);
            if (zen)
            {
                var fill = new Border { Width = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(ZenStyle.Pond, Ui.C(0x2FB3A6), 0) };
                var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Ui.B(Ui.C(0x8A9A9C, 70)), Child = fill, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(track);
                update = () =>
                {
                    int l = Math.Max(0, Audio.Level); bool m = Audio.Muted;
                    pct.Text = m ? "" : l + " %";
                    glyph.Text = m ? "" : ""; glyph.Foreground = Ui.B(m ? ZenStyle.Momiji : ZenStyle.Ink);
                    track.UpdateLayout();
                    double target = m ? 0 : Math.Max(10, track.ActualWidth * l / 100.0);
                    fill.BeginAnimation(FrameworkElement.WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(160)) { EasingFunction = ZenStyle.Surface });
                };
                var pane = ZenStyle.Pane(new Border { Child = row, Padding = new Thickness(16, 12, 18, 12) }, 22, 180);
                w.Content = new Border { Child = pane, Padding = new Thickness(24, 14, 24, 28) };
            }
            else
            {
                w.Resources.MergedDictionaries.Add(SevenWindow.Aero);
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(bar);
                update = () =>
                {
                    int l = Math.Max(0, Audio.Level); bool m = Audio.Muted;
                    pct.Text = m ? "" : l.ToString();
                    glyph.Text = m ? "" : ""; glyph.Foreground = Ui.B(m ? Ui.C(0xC42B1C) : Ui.C(0x2B579A));
                    bar.Value = m ? 0 : l;
                };
                var white = new Border { Child = row, Background = Ui.B(0xFFFFFF), CornerRadius = new CornerRadius(3), Padding = new Thickness(12, 10, 12, 10), BorderBrush = Ui.B(0x000000, 70), BorderThickness = new Thickness(1) };
                var rim = new Border { Child = white, Padding = new Thickness(5), CornerRadius = new CornerRadius(7), Background = Ui.V((0, Ui.C(0x9DB9DC, 235)), (1, Ui.C(0x5A80AF, 240))), BorderBrush = Ui.B(0x0F2747, 170), BorderThickness = new Thickness(1), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.4 } };
                w.Content = new Border { Child = rim, Padding = new Thickness(10) };
            }
            hide = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            hide.Tick += (s, e) => { hide.Stop(); w.Hide(); };
        }

        static void Place()
        {
            w.UpdateLayout();
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
            double wl = mi.rcWork.Left / sc, wr = mi.rcWork.Right / sc, wb = mi.rcWork.Bottom / sc;
            if (theme.Id == "zen") { w.Left = wl + (wr - wl - w.ActualWidth) / 2; w.Top = wb - w.ActualHeight + 10; }
            else { w.Left = wr - w.ActualWidth; w.Top = wb - w.ActualHeight; }
        }
    }
}

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
    /// The macOS menu bar panels: Control Center, the Wi-Fi and battery menus and Notification Center with its
    /// widgets; also the volume display and the notification banners. They open below the menu bar, close when
    /// they lose focus, and replace Windows' quick settings and notification centre for the look.
    /// </summary>
    internal static class MacPanels
    {
        static Window open;
        static string openKind;
        static DateTime closedAt;
        static Action onClosed;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;
        static CultureInfo Ci => De ? new CultureInfo("de-DE") : new CultureInfo("en-US");
        public static Window Open => open;

        /// <summary>Opens a panel below its menu bar item (or closes it when it is open). kind: cc, net, bat, nc.</summary>
        public static bool Toggle(ShellTheme t, string kind, Rect anchor, Action closed)
        {
            if (kind != "nc" && anchor.Width > 0) lastAnchor = anchor;
            if (open != null && openKind == kind) { Close(open); return false; }
            if (openKind == kind && (DateTime.Now - closedAt).TotalMilliseconds < 300) return false;
            MacMenu.Current?.Dismiss(true);
            if (open != null) Close(open, instant: true);
            Window w;
            switch (kind)
            {
                case "net": w = Menu(Wifi(), anchor, 300); break;
                case "bat": w = Menu(Battery(), anchor, 280); break;
                case "nc": w = Center(); break;
                default: kind = "cc"; w = Menu(ControlCenter(), anchor, 330, right: true); break;
            }
            open = w; openKind = kind; onClosed = closed;
            w.Show();
            w.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(w).Handle);
            return true;
        }

        public static void CloseAll() { if (open != null) Close(open, instant: true); }

        static void Close(Window w, bool instant = false)
        {
            if (w.Tag as string == "closing") return;
            w.Tag = "closing";
            if (open == w) { open = null; closedAt = DateTime.Now; var c = onClosed; onClosed = null; c?.Invoke(); }
            if (instant || !(w.Content is FrameworkElement fe)) { try { w.Close(); } catch { } return; }
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            a.Completed += (s, e) => { try { w.Close(); } catch { } };
            fe.BeginAnimation(UIElement.OpacityProperty, a);
        }

        static Window Base()
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                AllowsTransparency = true, Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight,
            };
            MacStyle.Prepare(w);
            MacStyle.ToolWindow(w);
            RenderGuard.Watch(w);
            w.Deactivated += (s, e) => Close(w);
            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(w); };
            return w;
        }

        static (double l, double t, double r, double b) Work()
        {
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            return (mi.rcWork.Left / s, mi.rcWork.Top / s, mi.rcWork.Right / s, mi.rcWork.Bottom / s);
        }

        /// <summary>A menu-style panel under its status item.</summary>
        static Window Menu(UIElement body, Rect anchor, double width, bool right = false)
        {
            var w = Base();
            var pane = MacStyle.Glass(body, 14, MacStyle.MenuTint);
            pane.Width = width;
            w.Content = new Border { Child = pane, Padding = new Thickness(14, 6, 14, 24) };
            w.Loaded += (s, e) =>
            {
                var (wl, wt, wr, wb) = Work();
                double x = right || anchor.Width <= 0 ? wr - w.ActualWidth + 6 : anchor.Right - w.ActualWidth + 14 + 6;
                w.Left = Math.Max(wl, Math.Min(x, wr - w.ActualWidth + 6));
                w.Top = wt - 1;
                MacStyle.Pop(pane, new Point(1, 0), 0.97, 180);
            };
            return w;
        }

        // ---------------- building blocks ----------------
        static TextBlock T(string s, double size, Color c, FontWeight? w = null) => MacStyle.Text(s, size, c, w);

        static FrameworkElement Header(string text, FrameworkElement right = null)
        {
            var d = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
            if (right != null) { DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); }
            d.Children.Add(T(text, 13, MacStyle.Label, FontWeights.Bold));
            return d;
        }

        static TextBlock Section(string text) { var tb = T(text, 11.5, MacStyle.Secondary, FontWeights.SemiBold); tb.Margin = new Thickness(10, 8, 10, 3); return tb; }
        static Border Sep() => new Border { Height = 1, Background = B(MacStyle.Line), Margin = new Thickness(10, 5, 10, 5) };

        /// <summary>A row of a menu panel: grey highlight under the pointer (the Control Center style of macOS menus).</summary>
        static Border Row(UIElement content, Action click, double height = 26)
        {
            var b = new Border { Child = content, Height = height, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Arrow };
            if (click != null)
            {
                b.MouseEnter += (s, e) => b.Background = B(0x000000, 20);
                b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            }
            return b;
        }

        static Border Link(string text, Action click) => Row(T(text, 13, MacStyle.Label), click, 24);

        static Grid Circle(string sym, bool on, double size = 26, Color? onColor = null)
        {
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Fill = B(on ? (onColor ?? MacStyle.Blue) : C(0x000000, 22)) });
            var s = MacStyle.Sym(sym, size * 0.55, on ? Colors.White : MacStyle.Label, sym == "wifi" ? 2 : -1);
            g.Children.Add(s);
            return g;
        }

        // ---------------- Wi-Fi ----------------
        static UIElement Wifi()
        {
            var sp = new StackPanel { Margin = new Thickness(5, 5, 5, 6) };
            var list = new StackPanel();
            Border sw = null;
            var head = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
            var swHost = new Border();
            DockPanel.SetDock(swHost, Dock.Right);
            head.Children.Add(swHost);
            head.Children.Add(T(L("WLAN", "Wi‑Fi"), 13, MacStyle.Label, FontWeights.Bold));
            sp.Children.Add(head);
            sp.Children.Add(list);
            sp.Children.Add(Sep());
            sp.Children.Add(Link(L("WLAN-Einstellungen …", "Wi‑Fi Settings…"), () => { CloseAll(); ShellActions.Run("ms-settings:network-wifi"); }));

            var busy = T(L("Netzwerke werden gesucht …", "Looking for networks…"), 12, MacStyle.Secondary);
            busy.Margin = new Thickness(10, 4, 10, 6);
            list.Children.Add(busy);
            Hardware.RadioOn(Hardware.RadioKind.WiFi).ContinueWith(r =>
            {
                if (r.Result == null) return;
                bool on = r.Result.Value;
                sw = MacStyle.Switch(on, v => { Hardware.SetRadio(Hardware.RadioKind.WiFi, v).ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(new Action(Fill))); });
                swHost.Child = sw;
            }, TaskScheduler.FromCurrentSynchronizationContext());

            void Fill()
            {
                Task.Run(() => { try { Wlan.Scan(); } catch { } return Wlan.Networks(); }).ContinueWith(r =>
                {
                    list.Children.Clear();
                    var nets = r.Result ?? new List<Wlan.Net>();
                    if (nets.Count == 0)
                    {
                        var none = T(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? L("Über Kabel verbunden", "Connected by cable") : L("Keine Netzwerke gefunden", "No networks found"), 12.5, MacStyle.Secondary);
                        none.Margin = new Thickness(10, 4, 10, 6);
                        list.Children.Add(none);
                        return;
                    }
                    var known = nets.Where(n => n.Connected || n.Profile != null).ToList();
                    var other = nets.Except(known).ToList();
                    if (known.Count > 0) { list.Children.Add(Section(known.Count == 1 ? L("Bekanntes Netzwerk", "Known Network") : L("Bekannte Netzwerke", "Known Networks"))); foreach (var n in known) list.Children.Add(NetRow(n)); }
                    if (other.Count > 0) { list.Children.Add(Section(L("Andere Netzwerke", "Other Networks"))); foreach (var n in other.Take(9)) list.Children.Add(NetRow(n)); }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            Fill();
            return sp;
        }

        static Border NetRow(Wlan.Net n)
        {
            var d = new DockPanel();
            var circle = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 9, 0) };
            circle.Children.Add(new Ellipse { Fill = B(n.Connected ? MacStyle.Blue : C(0x000000, 20)) });
            circle.Children.Add(MacStyle.Sym("wifi", 15, n.Connected ? Colors.White : MacStyle.Label, Math.Min(2, n.Signal / 34)));
            DockPanel.SetDock(circle, Dock.Left);
            d.Children.Add(circle);
            if (n.Secured) { var lk = MacStyle.Sym("lock.small", 14, MacStyle.Secondary); DockPanel.SetDock(lk, Dock.Right); d.Children.Add(lk); }
            d.Children.Add(T(n.Ssid, 13, MacStyle.Label));
            return Row(d, n.Connected ? (Action)null : () => Join(n), 34);
        }

        static void Join(Wlan.Net n)
        {
            CloseAll();
            void Connect(string pw)
            {
                Task.Run(() => n.Profile != null && pw == null ? Wlan.Connect(n.Profile) : Wlan.ConnectNew(n, pw)).ContinueWith(r =>
                {
                    if (r.Result != null)
                        MacAlert.Ask(L("Verbindung mit „" + n.Ssid + "“ nicht möglich.", "Could not join “" + n.Ssid + "”."), r.Result, MacStyle.Own("settings"), new[] { ("OK", (Action)null, true) });
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            if (n.Profile != null || !n.Secured) { Connect(null); return; }
            string kind = n.Auth == 9 ? "WPA3" : "WPA2";
            MacAlert a = null;
            a = MacAlert.Ask(L("Das WLAN-Netzwerk „" + n.Ssid + "“ erfordert ein " + kind + "-Passwort.", "The Wi‑Fi network “" + n.Ssid + "” requires a " + kind + " password."), null, MacStyle.Own("settings"),
                new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (L("Verbinden", "Join"), (Action)(() => Connect(a.Password.Password)), true) }, password: true);
        }

        // ---------------- battery ----------------
        static UIElement Battery()
        {
            var sp = new StackPanel { Margin = new Thickness(5, 5, 5, 6) };
            var b = Hardware.Battery();
            var pct = T(b != null ? b.Percent + " %" : "", 13, MacStyle.Secondary);
            sp.Children.Add(Header(L("Batterie", "Battery"), pct));
            var src = T(b == null ? "" : (b.Plugged || b.Charging ? L("Energiequelle: Netzteil", "Power Source: Power Adapter") : L("Energiequelle: Batterie", "Power Source: Battery")), 12.5, MacStyle.Secondary);
            src.Margin = new Thickness(10, 0, 10, 2);
            sp.Children.Add(src);
            if (b != null && b.Left.HasValue && !b.Plugged)
            {
                var left = T(L("Noch etwa ", "About ") + Hardware.Duration(b.Left.Value, De) + L("", " remaining"), 12.5, MacStyle.Secondary);
                left.Margin = new Thickness(10, 0, 10, 2);
                sp.Children.Add(left);
            }
            var plans = Hardware.PowerPlans();
            if (plans.Count > 1)
            {
                sp.Children.Add(Sep());
                sp.Children.Add(Section(L("Energiemodus", "Energy Mode")));
                var active = Hardware.ActivePlan;
                foreach (var (id, name) in plans)
                {
                    var pid = id;
                    var d = new DockPanel();
                    var chk = MacStyle.Sym("checkmark", 12, MacStyle.Label);
                    chk.Width = 14; chk.Margin = new Thickness(0, 0, 6, 0);
                    chk.Visibility = id == active ? Visibility.Visible : Visibility.Hidden;
                    DockPanel.SetDock(chk, Dock.Left);
                    d.Children.Add(chk);
                    d.Children.Add(T(name, 13, MacStyle.Label));
                    sp.Children.Add(Row(d, () => { Hardware.ActivePlan = pid; CloseAll(); }, 24));
                }
            }
            sp.Children.Add(Sep());
            sp.Children.Add(Link(L("Batterieeinstellungen …", "Battery Settings…"), () => { CloseAll(); ShellActions.Run("ms-settings:batterysaver"); }));
            return sp;
        }

        // ---------------- Control Center ----------------
        static Border Module(UIElement content, Thickness? pad = null) => new Border
        {
            Child = content, CornerRadius = new CornerRadius(16), Background = B(0xFFFFFF, 120), Padding = pad ?? new Thickness(10),
            BorderBrush = B(0xFFFFFF, 150), BorderThickness = new Thickness(0.6),
        };

        static UIElement ControlCenter()
        {
            var root = new StackPanel { Margin = new Thickness(10) };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // network: Wi-Fi, Bluetooth, energy mode
            var net = new StackPanel();
            FrameworkElement Toggle(string sym, string title, Func<string> sub, Func<bool> isOn, Action<Action> circleClick, Action textClick, Color? onColor = null)
            {
                var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var circleHost = new Border { Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 9, 0), Background = Brushes.Transparent };
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Arrow };
                var tt = T(title, 13, MacStyle.Label, FontWeights.SemiBold);
                var st = T("", 11.5, MacStyle.Secondary);
                words.Children.Add(tt); words.Children.Add(st);
                void Refresh() { circleHost.Child = Circle(sym, isOn(), 28, onColor); st.Text = sub(); }
                Refresh();
                circleHost.MouseLeftButtonUp += (s, e) => { e.Handled = true; circleClick?.Invoke(Refresh); };
                if (textClick != null) words.MouseLeftButtonUp += (s, e) => { e.Handled = true; textClick(); };
                DockPanel.SetDock(circleHost, Dock.Left);
                d.Children.Add(circleHost); d.Children.Add(words);
                return d;
            }
            bool? wifiOn = null, btOn = null;
            var wifiRow = new Border(); var btRow = new Border();
            void WifiRow() => wifiRow.Child = Toggle("wifi", L("WLAN", "Wi‑Fi"), () => Wlan.Current?.Ssid ?? (wifiOn == false ? L("Aus", "Off") : L("Nicht verbunden", "Not Connected")), () => wifiOn != false && Wlan.Current != null || wifiOn == true,
                refresh => Hardware.SetRadio(Hardware.RadioKind.WiFi, wifiOn != true).ContinueWith(_ => { wifiOn = wifiOn != true; Application.Current.Dispatcher.BeginInvoke(new Action(WifiRow)); }),
                () => { var a = MenuAnchor(); CloseAll(); Toggle2("net", a); });
            void BtRow() => btRow.Child = Toggle("bluetooth", "Bluetooth", () => btOn == true ? L("Ein", "On") : btOn == false ? L("Aus", "Off") : "—", () => btOn == true,
                refresh => Hardware.SetRadio(Hardware.RadioKind.Bluetooth, btOn != true).ContinueWith(_ => { btOn = btOn != true; Application.Current.Dispatcher.BeginInvoke(new Action(BtRow)); }),
                () => { CloseAll(); ShellActions.Run("ms-settings:bluetooth"); });
            WifiRow(); BtRow();
            Hardware.RadioOn(Hardware.RadioKind.WiFi).ContinueWith(r => { wifiOn = r.Result; WifiRow(); }, TaskScheduler.FromCurrentSynchronizationContext());
            Hardware.RadioOn(Hardware.RadioKind.Bluetooth).ContinueWith(r => { btOn = r.Result; BtRow(); }, TaskScheduler.FromCurrentSynchronizationContext());
            net.Children.Add(wifiRow); net.Children.Add(btRow);
            var plans = Hardware.PowerPlans();
            if (plans.Count > 0)
            {
                var energy = new Border();
                void EnergyRow() => energy.Child = Toggle("bolt", L("Energiemodus", "Energy Mode"), () => plans.FirstOrDefault(p => p.id == Hardware.ActivePlan).name ?? "—", () => true,
                    refresh => { int i = plans.FindIndex(p => p.id == Hardware.ActivePlan); Hardware.ActivePlan = plans[(i + 1) % plans.Count].id; EnergyRow(); }, null, MacStyle.Orange);
                EnergyRow();
                net.Children.Add(energy);
            }
            top.Children.Add(Module(net, new Thickness(10, 8, 10, 8)));

            var rightCol = new Grid();
            rightCol.RowDefinitions.Add(new RowDefinition()); rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); rightCol.RowDefinitions.Add(new RowDefinition());
            var focusHost = new Border();
            void FocusModule()
            {
                var d = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
                var c = Circle("moon", Notify.Quiet, 28, MacStyle.Indigo);
                c.Margin = new Thickness(0, 0, 9, 0);
                DockPanel.SetDock(c, Dock.Left);
                d.Children.Add(c);
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(T(L("Fokus", "Focus"), 13, MacStyle.Label, FontWeights.SemiBold));
                words.Children.Add(T(Notify.Quiet ? L("Nicht stören", "Do Not Disturb") : L("Aus", "Off"), 11.5, MacStyle.Secondary));
                d.Children.Add(words);
                var m = Module(d);
                m.Cursor = Cursors.Hand;
                m.MouseLeftButtonUp += (s, e) => { Notify.Quiet = !Notify.Quiet; FocusModule(); };
                focusHost.Child = m;
            }
            FocusModule();
            rightCol.Children.Add(focusHost);
            var small = new Grid();
            small.ColumnDefinitions.Add(new ColumnDefinition()); small.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); small.ColumnDefinitions.Add(new ColumnDefinition());
            FrameworkElement Small(string sym, string tip, Action a)
            {
                var m = Module(MacStyle.Sym(sym, 20, MacStyle.Label), new Thickness(6));
                m.ToolTip = tip; m.Cursor = Cursors.Hand; m.MinHeight = 46;
                m.MouseLeftButtonUp += (s, e) => { e.Handled = true; CloseAll(); a(); };
                return m;
            }
            var lockTile = Small("lock", L("Bildschirm sperren", "Lock Screen"), ShellActions.Lock);
            var shotTile = Small("camera", L("Bildschirmfoto", "Screenshot"), ShellActions.Screenshot);
            Grid.SetColumn(shotTile, 2);
            small.Children.Add(lockTile); small.Children.Add(shotTile);
            Grid.SetRow(small, 2);
            rightCol.Children.Add(small);
            Grid.SetColumn(rightCol, 2);
            top.Children.Add(rightCol);
            root.Children.Add(top);

            // display brightness
            int bright = Hardware.Brightness();
            if (bright >= 0)
            {
                var sp = new StackPanel();
                var t1 = T(L("Bildschirm", "Display"), 13, MacStyle.Label, FontWeights.SemiBold);
                t1.Margin = new Thickness(2, 0, 0, 7);
                sp.Children.Add(t1);
                sp.Children.Add(MacStyle.Slider(bright / 100.0, v => Hardware.SetBrightness((int)Math.Round(v * 100)), v => MacStyle.Sym("sun", 15, MacStyle.Secondary), 288));
                var m = Module(sp);
                m.Margin = new Thickness(0, 10, 0, 0);
                root.Children.Add(m);
            }
            // sound
            if (Audio.Available)
            {
                var sp = new StackPanel();
                var t2 = T(L("Ton", "Sound"), 13, MacStyle.Label, FontWeights.SemiBold);
                t2.Margin = new Thickness(2, 0, 0, 7);
                sp.Children.Add(t2);
                int lv = Math.Max(0, Audio.Level);
                var slider = MacStyle.Slider(Audio.Muted ? 0 : lv / 100.0, v => { Audio.Level = (int)Math.Round(v * 100); if (Audio.Muted && v > 0) Audio.Muted = false; },
                    v => MacStyle.Sym("speaker", 15, MacStyle.Secondary, v <= 0.001 ? 0 : v < 0.34 ? 1 : v < 0.67 ? 2 : 3), 288);
                sp.Children.Add(slider);
                var m = Module(sp);
                m.Margin = new Thickness(0, 10, 0, 0);
                root.Children.Add(m);
            }
            return root;
        }

        static Rect lastAnchor;
        static Rect MenuAnchor() => lastAnchor;
        static ShellTheme Theme => ShellActions.Own ?? ShellThemes.For("macos");
        static void Toggle2(string kind, Rect anchor) => Application.Current.Dispatcher.BeginInvoke(new Action(() => Toggle(Theme, kind, anchor, null)), DispatcherPriority.Background);

        // ---------------- Notification Center ----------------
        static Window Center()
        {
            var w = Base();
            w.SizeToContent = SizeToContent.Manual;
            var (wl, wt, wr, wb) = Work();
            double width = 372;
            w.Width = width; w.Height = wb - wt; w.Left = wr - width; w.Top = wt;
            var col = new StackPanel { Margin = new Thickness(10, 8, 14, 10) };

            var items = Notify.History.ToList();
            if (items.Count > 0)
            {
                var head = new DockPanel { Margin = new Thickness(6, 0, 6, 6) };
                var clear = T(L("Alle löschen", "Clear All"), 12, MacStyle.Blue);
                clear.Cursor = Cursors.Hand;
                clear.MouseLeftButtonUp += (s, e) => { Notify.ClearHistory(); Close(w, true); };
                DockPanel.SetDock(clear, Dock.Right);
                head.Children.Add(clear);
                var ht = T(L("Mitteilungen", "Notifications"), 13, Colors.White, FontWeights.SemiBold);
                ht.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.45 };
                head.Children.Add(ht);
                col.Children.Add(head);
                foreach (var i in items.OrderByDescending(x => x.When).Take(6))
                {
                    var card = Card(i);
                    card.Margin = new Thickness(0, 0, 0, 8);
                    col.Children.Add(card);
                }
            }
            col.Children.Add(CalendarWidget());
            var bat = Hardware.Battery();
            if (bat != null) col.Children.Add(BatteryWidget(bat));

            var scroll = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            var host = new Grid { Background = B(0xFFFFFF, 1) };
            host.Children.Add(scroll);
            host.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == host || e.OriginalSource == scroll) Close(w); };
            w.Content = host;
            w.Loaded += (s, e) =>
            {
                var tt = new TranslateTransform(width, 0);
                col.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)) { EasingFunction = MacStyle.OutQuint });
            };
            return w;
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return L("jetzt", "now");
            if (d.TotalMinutes < 60) return L("vor " + (int)d.TotalMinutes + " Min.", (int)d.TotalMinutes + "m ago");
            return t.ToString(t.Date == DateTime.Today ? "HH:mm" : "dd.MM.");
        }

        static Grid Card(Notify.Item i, double width = 344)
        {
            var d = new DockPanel { Margin = new Thickness(12, 11, 12, 12) };
            var icon = Img(MacStyle.Own("settings"), 38);
            icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(-3, -2, 9, 0);
            DockPanel.SetDock(icon, Dock.Left);
            d.Children.Add(icon);
            var words = new StackPanel();
            var top = new DockPanel();
            var when = T(Ago(i.When), 12, MacStyle.Secondary);
            DockPanel.SetDock(when, Dock.Right);
            top.Children.Add(when);
            top.Children.Add(T(string.IsNullOrEmpty(i.Title) ? i.App : i.Title, 13, MacStyle.Label, FontWeights.SemiBold));
            words.Children.Add(top);
            var body = string.IsNullOrEmpty(i.Title) ? i.Body : (string.IsNullOrEmpty(i.Body) ? i.App : i.Body);
            if (!string.IsNullOrEmpty(body)) { var bt = T(body.Length > 220 ? body.Substring(0, 219) + "…" : body, 13, MacStyle.Label); bt.TextWrapping = TextWrapping.Wrap; bt.MaxHeight = 54; words.Children.Add(bt); }
            d.Children.Add(words);
            var g = MacStyle.Glass(d, 20, C(0xF5F5F7, 190));
            g.Width = width;
            return g;
        }

        static Grid CalendarWidget()
        {
            var now = DateTime.Now;
            var g = new Grid { Margin = new Thickness(16, 14, 16, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel();
            left.Children.Add(T(now.ToString("dddd", Ci).ToUpper(Ci), 11, MacStyle.Red, FontWeights.SemiBold));
            var big = MacStyle.Text(now.Day.ToString(), 44, MacStyle.Label, FontWeights.Light, display: true);
            big.Margin = new Thickness(-2, -4, 0, 0);
            left.Children.Add(big);
            g.Children.Add(left);
            var right = new StackPanel();
            right.Children.Add(T(now.ToString("MMMM", Ci).ToUpper(Ci), 11, MacStyle.Red, FontWeights.SemiBold));
            var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 5, 0, 0) };
            var names = De ? new[] { "M", "D", "M", "D", "F", "S", "S" } : new[] { "M", "T", "W", "T", "F", "S", "S" };
            foreach (var n in names) { var tb = T(n, 9.5, MacStyle.Secondary, FontWeights.SemiBold); tb.HorizontalAlignment = HorizontalAlignment.Center; grid.Children.Add(tb); }
            var first = new DateTime(now.Year, now.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
            int days = DateTime.DaysInMonth(now.Year, now.Month);
            for (int day = 1; day <= days; day++)
            {
                bool today = day == now.Day;
                var tb = T(day.ToString(), 10.5, today ? Colors.White : MacStyle.Label, today ? FontWeights.SemiBold : FontWeights.Normal);
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Grid { Height = 17, Width = 19 };
                if (today) cell.Children.Add(new Ellipse { Fill = B(MacStyle.Red), Width = 17, Height = 17 });
                cell.Children.Add(tb);
                grid.Children.Add(cell);
            }
            right.Children.Add(grid);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            var w = MacStyle.Glass(g, 22, C(0xF5F5F7, 200));
            w.Width = 344; w.Margin = new Thickness(0, 0, 0, 10);
            w.Cursor = Cursors.Hand;
            w.MouseLeftButtonUp += (s, e) => { CloseAll(); ShellActions.Run("outlookcal:"); };
            return w;
        }

        static Grid BatteryWidget(Hardware.BatteryState b)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 14) };
            var ring = new Grid { Width = 58, Height = 58 };
            ring.Children.Add(new Ellipse { Stroke = B(0x000000, 26), StrokeThickness = 5.5 });
            double a = Math.Max(0.01, b.Percent / 100.0) * 2 * Math.PI;
            var arc = new Path
            {
                Stroke = B(b.Percent <= 20 && !b.Plugged ? MacStyle.Red : MacStyle.Green), StrokeThickness = 5.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Data = new PathGeometry(new[] { new PathFigure(new Point(29, 2.75), new[] { new ArcSegment(new Point(29 + 26.25 * Math.Sin(a), 29 - 26.25 * Math.Cos(a)), new Size(26.25, 26.25), 0, a > Math.PI, SweepDirection.Clockwise, true) }, false) }),
            };
            ring.Children.Add(arc);
            ring.Children.Add(MacStyle.Sym(b.Plugged || b.Charging ? "bolt" : "laptop", 22, MacStyle.Label));
            row.Children.Add(ring);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            words.Children.Add(T(L("Batterien", "Batteries"), 12, MacStyle.Secondary, FontWeights.SemiBold));
            words.Children.Add(MacStyle.Text(b.Percent + " %", 26, MacStyle.Label, FontWeights.Medium, display: true));
            row.Children.Add(words);
            var w = MacStyle.Glass(row, 22, C(0xF5F5F7, 200));
            w.Width = 344;
            return w;
        }

        // ---------------- volume display (top right, like Control Center's sound module) ----------------
        static Window hud;
        public static Window Hud => hud;
        static Grid hudSlider;
        static FrameworkElement hudContent;
        static DispatcherTimer hudHide;

        public static void VolumeHud()
        {
            if (hud == null)
            {
                hud = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                    SizeToContent = SizeToContent.WidthAndHeight, AllowsTransparency = true, Background = Brushes.Transparent,
                };
                MacStyle.Prepare(hud);
                hud.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(hud).Handle;
                    W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE | 0x20);
                };
                RenderGuard.Watch(hud);
                var sp = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
                sp.Children.Add(T(L("Ton", "Sound"), 13, MacStyle.Label, FontWeights.SemiBold));
                hudSlider = MacStyle.Slider(0, null, v => MacStyle.Sym("speaker", 15, MacStyle.Secondary, v <= 0.001 ? 0 : v < 0.34 ? 1 : v < 0.67 ? 2 : 3), 272);
                hudSlider.IsHitTestVisible = false;
                hudSlider.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(hudSlider);
                var pane = MacStyle.Glass(sp, 18, MacStyle.MenuTint);
                hudContent = new Border { Child = pane, Padding = new Thickness(14, 6, 14, 24) };
                hud.Content = hudContent;
                hudHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                hudHide.Tick += (s, e) =>
                {
                    hudHide.Stop();
                    var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
                    a.Completed += (s2, e2) => { if (hudContent.Opacity < 0.05) hud.Hide(); };
                    hudContent.BeginAnimation(UIElement.OpacityProperty, a);
                };
            }
            int lv = Audio.Muted ? 0 : Math.Max(0, Audio.Level);
            (hudSlider.Tag as Action<double>)?.Invoke(lv / 100.0);
            bool wasHidden = !hud.IsVisible || hudContent.Opacity < 0.99;
            if (!hud.IsVisible) hud.Show();
            hud.UpdateLayout();
            var (wl, wt, wr, wb) = Work();
            hud.Left = wr - hud.ActualWidth + 6; hud.Top = wt - 1;
            if (wasHidden) hudContent.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            hudHide.Stop(); hudHide.Start();
        }

        // ---------------- notification banners ----------------
        /// <summary>The banner of a notice: a glass card that slides in at the upper right.</summary>
        public static Window Banner(Notify.Item i)
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, AllowsTransparency = true, Background = Brushes.Transparent, Cursor = Cursors.Arrow,
            };
            MacStyle.Prepare(w);
            var card = Card(i, 350);
            w.Content = new Border { Child = card, Padding = new Thickness(16, 6, 16, 22) };
            w.MouseLeftButtonUp += (s, e) => Application.Current.Dispatcher.BeginInvoke(new Action(() => Toggle(Theme, "nc", new Rect(0, 0, 0, 0), null)));
            return w;
        }

        public static void Enter(Window w)
        {
            if (!(w.Content is FrameworkElement c)) return;
            var tt = new TranslateTransform(380, 0);
            c.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = MacStyle.OutQuint });
        }
    }
}

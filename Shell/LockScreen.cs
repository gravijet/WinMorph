using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Themed lock screen. It runs as a password-protected screen saver, so the real,
    /// secure Windows sign-in appears as soon as the user comes back.
    /// </summary>
    public static class LockScreen
    {
        static readonly List<Window> windows = new List<Window>();
        static Point? firstMouse;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        /// <summary>The animated background for the lock screen (the live wallpaper, or Matrix for the hacker look).</summary>
        static string LiveSpec(Core.Preset p)
        {
            var spec = Live.LiveWall.Spec;
            if (spec == "scene:matrix-hud") return "scene:matrix";   // the lock screen has its own clock and status
            if (!string.IsNullOrEmpty(spec)) return spec;
            return p.Id == "terminal" ? "scene:matrix" : null;
        }

        static bool lockOnExit, closing;

        /// <param name="lockOnExit">screen saver mode: lock Windows for real before the picture goes away</param>
        public static void Show(string presetId, Action onClosed, bool lockOnExit = false)
        {
            LockScreen.lockOnExit = lockOnExit; closing = false;
            var p = Core.Presets.Find(presetId ?? "") ?? Core.Presets.All[0];
            var t = ShellThemes.ForLock(p.Id);
            string wall = null;
            try { wall = Core.WallpaperArt.RenderForScreen(p); } catch { }
            ImageSource wallImg = null;
            if (wall != null)
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(wall); bi.EndInit(); bi.Freeze();
                wallImg = bi;
            }
            var live = LiveSpec(p);
            bool first = true;
            firstMouse = null;
            foreach (var mon in W32.Monitors())
            {
                var mi = W32.Monitor(mon, out double s);
                var w = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false,
                    Left = mi.rcMonitor.Left / s, Top = mi.rcMonitor.Top / s, Width = (mi.rcMonitor.Right - mi.rcMonitor.Left) / s, Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / s,
                    Background = Brushes.Black, Cursor = Cursors.None, FontFamily = new FontFamily(t.Font),
                };
                var content = first ? Build(t, p, wallImg, live) : Secondary(t, wallImg, live);
                if (content is FrameworkElement fe) { fe.Opacity = 0; fe.Loaded += (o, e) => fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700))); }
                w.Content = content;
                w.KeyDown += (a2, e) => CloseAll(onClosed);
                w.MouseDown += (a2, e) => CloseAll(onClosed);
                w.MouseMove += (a2, e) =>
                {
                    var pos = e.GetPosition(null);
                    if (firstMouse == null) firstMouse = pos;
                    else if ((pos - firstMouse.Value).Length > 25) CloseAll(onClosed);
                };
                RenderGuard.Watch(w);
                windows.Add(w);
                w.Show();
                // stay above everything (also above the WinMorph bars when started from the shell)
                var hw = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                var top = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                top.Tick += (o, e) => { if (!w.IsVisible) { top.Stop(); return; } W32.SetWindowPos(hw, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); };
                top.Start();
                w.Closed += (o, e) => top.Stop();
                if (first) w.Activate();
                first = false;
            }
        }

        /// <summary>Background: live animation, or the picture with a slow zoom.</summary>
        static UIElement Background(ImageSource wall, string live)
        {
            if (live != null) return new Live.LiveView(live, mouse: Live.MouseMode.None);
            var img = new Image { Source = wall, Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5) };
            if (!RenderGuard.Software)
            {
                var sc = new ScaleTransform(1, 1);
                img.RenderTransform = sc;
                var zoom = new DoubleAnimation(1, 1.08, TimeSpan.FromSeconds(30)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, zoom); sc.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
            }
            return img;
        }

        /// <summary>Other monitors: the same background with a small clock.</summary>
        static UIElement Secondary(ShellTheme t, ImageSource wall, string live)
        {
            var g = new Grid { ClipToBounds = true };
            g.Children.Add(Background(wall, live));
            g.Children.Add(new Border { Background = B(0x000000, 90) });
            bool term = t.Lock == LockKind.Terminal;
            var time = Text("", 64, term ? t.Accent : C(0xFFFFFF), FontWeights.Light, term ? "Consolas" : null);
            time.VerticalAlignment = VerticalAlignment.Bottom; time.HorizontalAlignment = HorizontalAlignment.Right; time.Margin = new Thickness(0, 0, 60, 50);
            Tick(() => time.Text = DateTime.Now.ToString(term ? "HH:mm:ss" : "HH:mm"));
            g.Children.Add(time);
            return g;
        }

        static string StatusLine()
        {
            var parts = new List<string>();
            parts.Add("cpu " + SysInfo.CpuSample().ToString("00") + "%");
            parts.Add("mem " + SysStats.Mem().ToString("00") + "%");
            parts.Add("up " + SysInfo.UptimeText);
            parts.Add("ip " + SysInfo.LocalIp);
            if (SysStats.Battery(out int pct, out bool ac)) parts.Add("bat " + pct + "%" + (ac ? "+" : ""));
            return string.Join("   │   ", parts);
        }

        static void CloseAll(Action onClosed)
        {
            if (closing) return;
            closing = true;
            // the secure Windows lock comes first, so the desktop is never visible in between
            if (lockOnExit) { try { Core.LockGuard.LockNow(); } catch { } }
            foreach (var w in windows.ToList()) try { w.Close(); } catch { }
            windows.Clear();
            onClosed?.Invoke();
        }

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            if (h < 5 || h >= 22) return L("Gute Nacht", "Good night");
            if (h < 11) return L("Guten Morgen", "Good morning");
            if (h < 17) return L("Guten Tag", "Good afternoon");
            return L("Guten Abend", "Good evening");
        }

        static string FirstName => ShellActions.UserName.Split(' ')[0];
        static CultureInfo Ci => De ? new CultureInfo("de-DE") : new CultureInfo("en-US");

        static TextBlock Text(string s, double size, Color c, FontWeight? w = null, string font = null) => new TextBlock
        {
            Text = s, FontSize = size, Foreground = B(c), FontWeight = w ?? FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = font != null ? new FontFamily(font) : null ?? new FontFamily("Segoe UI Variable Display, Segoe UI"), TextAlignment = TextAlignment.Center,
        };

        static readonly List<DispatcherTimer> timers = new List<DispatcherTimer>();

        static void Tick(Action a)
        {
            a();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => a();
            timer.Start();
            timers.Add(timer);
        }

        static DispatcherTimer Timer(int ms, EventHandler tick)
        {
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            tm.Tick += tick;
            timers.Add(tm);
            return tm;
        }

        /// <summary>Stops the clocks and effects of a closed lock screen (the shell keeps running).</summary>
        public static void StopTimers()
        {
            foreach (var tm in timers) tm.Stop();
            timers.Clear();
        }

        static (ImageSource wall, string live) Pictures(Core.Preset p)
        {
            ImageSource wallImg = null;
            try
            {
                var wall = Core.WallpaperArt.RenderForScreen(p);
                if (wall != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(wall); bi.EndInit(); bi.Freeze();
                    wallImg = bi;
                }
            }
            catch { }
            return (wallImg, LiveSpec(p));
        }

        /// <summary>Main monitor of the WinMorph lock (with the password line).</summary>
        public static UIElement BuildLock(ShellTheme t, Core.Preset p, LockAuth auth)
        {
            var (wall, live) = Pictures(p);
            return Build(t, p, wall, live, false, auth);
        }

        public static UIElement BuildSecondary(ShellTheme t, Core.Preset p)
        {
            var (wall, live) = Pictures(p);
            return Secondary(t, wall, live);
        }

        static Border Avatar(double size, Color a, Color b, double radius)
            => new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = V((0, a), (1, b)), HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = ShellActions.UserName.Substring(0, 1).ToUpper(), Foreground = B(0xFFFFFF), FontSize = size * 0.45, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold },
            };

        /// <param name="still">picture for the Windows lock screen: no clock of its own (Windows draws one), nothing animated</param>
        static UIElement Build(ShellTheme t, Core.Preset p, ImageSource wall, string live = null, bool still = false, LockAuth auth = null)
        {
            var root = BuildDesign(t, p, wall, live, still, auth);
            if (auth != null && auth.Prompt == null && root is Grid g)
            {
                // looks without their own password line get a simple one at the bottom
                auth.Terminal = false; auth.Accent = t.Accent;
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                auth.Prompt = new TextBlock { FontSize = 20, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily(t.Font) };
                auth.Info = new TextBlock { FontSize = 13, Foreground = B(0xFFFFFF, 190), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), FontFamily = new FontFamily(t.Font) };
                sp.Children.Add(auth.Prompt); sp.Children.Add(auth.Info);
                auth.Box = new Border { Child = sp, Background = B(0x000000, 150), BorderBrush = B(0xFFFFFF, 60), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(28, 14, 28, 14), MinWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 40) };
                g.Children.Add(auth.Box);
                Tick(auth.Tick);
            }
            return root;
        }

        static UIElement BuildDesign(ShellTheme t, Core.Preset p, ImageSource wall, string live, bool still, LockAuth auth)
        {
            var root = new Grid { ClipToBounds = true };
            var bg = (FrameworkElement)Background(wall, live);
            string hint = L("Drücke eine Taste, um zu entsperren", "Press any key to unlock");

            switch (t.Lock)
            {
                case LockKind.Classic:
                {
                    root.Children.Add(new Border { Background = B(0x008080) });
                    var dlg = new Bevel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 440 };
                    var sp = new StackPanel();
                    var title = new Border { Background = new LinearGradientBrush(C(0x000080), C(0x1084D0), 0), Padding = new Thickness(6, 3, 6, 3), Child = new TextBlock { Text = L("Willkommen bei ", "Welcome to ") + p.Name, Foreground = B(0xFFFFFF), FontWeight = FontWeights.Bold, FontSize = 12, FontFamily = new FontFamily("Microsoft Sans Serif") } };
                    sp.Children.Add(title);
                    var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(ClassicLogo(36));
                    row.Children.Add(new TextBlock { Text = L("Drücken Sie eine beliebige Taste,\num sich bei Windows anzumelden.", "Press any key\nto log on to Windows."), Margin = new Thickness(16, 0, 0, 0), FontSize = 12, FontFamily = new FontFamily("Microsoft Sans Serif"), Foreground = B(0x000000) });
                    body.Children.Add(row);
                    var clock = new TextBlock { FontSize = 12, FontFamily = new FontFamily("Microsoft Sans Serif"), Margin = new Thickness(0, 16, 0, 0), Foreground = B(0x000000) };
                    body.Children.Add(new Bevel { Sunken = true, Fill = B(0xFFFFFF), Child = clock });
                    Tick(() => clock.Text = " " + ShellActions.UserName + "   ·   " + DateTime.Now.ToString("HH:mm:ss") + " ");
                    sp.Children.Add(body);
                    dlg.Child = sp;
                    root.Children.Add(dlg);
                    break;
                }
                case LockKind.Xp:
                {
                    var g = new Grid { Background = B(0x5A7EDC) };
                    g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
                    g.RowDefinitions.Add(new RowDefinition());
                    g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
                    var top = new Border { Background = B(0x00309C), BorderBrush = new LinearGradientBrush(C(0x5A7EDC), C(0xF2A93B), 0) { GradientStops = { new GradientStop(C(0x5A7EDC), 0), new GradientStop(C(0xFFFFFF), 0.3), new GradientStop(C(0x5A7EDC), 1) } }, BorderThickness = new Thickness(0, 0, 0, 2) };
                    var bottom = new Border { Background = B(0x00309C), BorderBrush = new LinearGradientBrush(C(0x5A7EDC), C(0xF2A93B), 0) { GradientStops = { new GradientStop(C(0x5A7EDC), 0), new GradientStop(C(0xF2A93B), 0.3), new GradientStop(C(0x5A7EDC), 1) } }, BorderThickness = new Thickness(0, 2, 0, 0) };
                    Grid.SetRow(bottom, 2);
                    var bottomContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 0, 0) };
                    bottomContent.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Background = V((0, C(0xF26C4F)), (1, C(0xC2350A))), BorderBrush = B(0xFFFFFF), BorderThickness = new Thickness(1), Child = Glyph("", 15, Colors.White) });
                    bottomContent.Children.Add(new TextBlock { Text = L("Computer ausschalten", "Turn off computer"), Foreground = B(0xFFFFFF), FontSize = 14, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Tahoma") });
                    bottom.Child = bottomContent;
                    g.Children.Add(top); g.Children.Add(bottom);
                    var mid = new Grid { Background = new RadialGradientBrush(C(0x7C9AE6), C(0x5A7EDC)) { Center = new Point(0.2, 0.2), GradientOrigin = new Point(0.2, 0.2), RadiusX = 0.6, RadiusY = 0.7 } };
                    Grid.SetRow(mid, 1);
                    mid.ColumnDefinitions.Add(new ColumnDefinition());
                    mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
                    mid.ColumnDefinitions.Add(new ColumnDefinition());
                    var left = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 50, 0) };
                    var brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                    brand.Children.Add(ClassicLogo(54));
                    brand.Children.Add(new TextBlock { Text = p.Name, Foreground = B(0xFFFFFF), FontSize = 40, FontStyle = FontStyles.Italic, FontWeight = FontWeights.Bold, Margin = new Thickness(14, 0, 0, 0), FontFamily = new FontFamily("Franklin Gothic Medium, Trebuchet MS, Tahoma"), VerticalAlignment = VerticalAlignment.Center });
                    left.Children.Add(brand);
                    left.Children.Add(new TextBlock { Text = L("Klicken Sie auf Ihren Benutzernamen, um zu beginnen", "To begin, click your user name"), Foreground = B(0xFFFFFF), FontSize = 15, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Tahoma") });
                    mid.Children.Add(left);
                    var divider = new Border { Background = new LinearGradientBrush(C(0x5A7EDC), C(0xFFFFFF), 90) { GradientStops = { new GradientStop(C(0x5A7EDC), 0), new GradientStop(C(0xFFFFFF), 0.5), new GradientStop(C(0x5A7EDC), 1) } }, Margin = new Thickness(0, 60, 0, 60) };
                    Grid.SetColumn(divider, 1); mid.Children.Add(divider);
                    var userRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(50, 0, 0, 0) };
                    var tile = Avatar(64, C(0xF7A84B), C(0xD35400), 6);
                    tile.BorderBrush = B(0xFFC74A); tile.BorderThickness = new Thickness(3);
                    userRow.Children.Add(tile);
                    var name = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    name.Children.Add(new TextBlock { Text = ShellActions.UserName, Foreground = B(0xFFFFFF), FontSize = 22, FontFamily = new FontFamily("Tahoma") });
                    var clock = new TextBlock { Foreground = B(0xD8E4F8), FontSize = 13, FontFamily = new FontFamily("Tahoma") };
                    Tick(() => clock.Text = DateTime.Now.ToString("HH:mm") + " · " + hint);
                    name.Children.Add(clock);
                    userRow.Children.Add(name);
                    Grid.SetColumn(userRow, 2); mid.Children.Add(userRow);
                    g.Children.Add(mid);
                    root.Children.Add(g);
                    break;
                }
                case LockKind.Mac:
                {
                    bg.Effect = new BlurEffect { Radius = 50, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
                    ((FrameworkElement)bg).Margin = new Thickness(-60);
                    root.Children.Add(bg);
                    root.Children.Add(new Border { Background = B(0x000000, 40) });
                    var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, 0, 0) };
                    var date = Text("", 24, C(0xFFFFFF), FontWeights.SemiBold);
                    var time = Text("", 120, C(0xFFFFFF, 230), FontWeights.Bold);
                    top.Children.Add(date); top.Children.Add(time);
                    Tick(() => { date.Text = DateTime.Now.ToString(De ? "dddd, d. MMMM" : "dddd, MMMM d", Ci); time.Text = DateTime.Now.ToString("HH:mm"); });
                    root.Children.Add(top);
                    var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 90) };
                    bottom.Children.Add(Avatar(72, C(0x8E8E93), C(0x5A5A5E), 36));
                    bottom.Children.Add(Text(ShellActions.UserName, 16, C(0xFFFFFF), FontWeights.SemiBold));
                    ((TextBlock)bottom.Children[1]).Margin = new Thickness(0, 10, 0, 4);
                    bottom.Children.Add(Text(hint, 13, C(0xFFFFFF, 180)));
                    root.Children.Add(bottom);
                    break;
                }
                case LockKind.Gnome:
                {
                    root.Children.Add(bg);
                    root.Children.Add(new Border { Background = B(0x000000, 110) });
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var time = Text("", 110, C(0xFFFFFF), FontWeights.Bold, t.Id == "arch" ? "Cascadia Mono, Consolas" : null);
                    var date = Text("", 26, C(0xFFFFFF, 220), FontWeights.SemiBold);
                    sp.Children.Add(time); sp.Children.Add(date);
                    Tick(() => { time.Text = DateTime.Now.ToString("HH:mm"); date.Text = DateTime.Now.ToString(De ? "dddd, d. MMMM" : "dddd, MMMM d", Ci); });
                    root.Children.Add(sp);
                    var h = Text(t.Id == "arch" ? "[ " + hint.ToLower() + " ]" : hint, 15, C(0xFFFFFF, 170), null, t.Id == "arch" ? "Cascadia Mono, Consolas" : null);
                    h.VerticalAlignment = VerticalAlignment.Bottom; h.Margin = new Thickness(0, 0, 0, 60);
                    root.Children.Add(h);
                    break;
                }
                case LockKind.Seven:
                {
                    root.Children.Add(bg);
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var frame = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(10), Background = V((0, C(0xFFFFFF, 140)), (1, C(0xFFFFFF, 60))), BorderBrush = B(0xFFFFFF, 200), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(9), Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 2, Opacity = 0.5 } };
                    frame.Child = Avatar(130, t.Accent, Core.Applier.Mix(t.Accent, Colors.Black, 0.4), 4);
                    sp.Children.Add(frame);
                    sp.Children.Add(Text(ShellActions.UserName, 26, C(0xFFFFFF)));
                    ((TextBlock)sp.Children[1]).Margin = new Thickness(0, 14, 0, 2);
                    ((TextBlock)sp.Children[1]).Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.7 };
                    sp.Children.Add(Text(L("Gesperrt", "Locked"), 15, C(0xFFFFFF, 210)));
                    var clock = Text("", 14, C(0xFFFFFF, 200));
                    Tick(() => clock.Text = DateTime.Now.ToString("HH:mm") + " · " + hint);
                    sp.Children.Add(clock);
                    root.Children.Add(sp);
                    var brand = Text(p.Name, 26, C(0xFFFFFF, 220), FontWeights.Light);
                    brand.VerticalAlignment = VerticalAlignment.Bottom; brand.Margin = new Thickness(0, 0, 0, 40);
                    root.Children.Add(brand);
                    break;
                }
                case LockKind.Terminal:
                {
                    var G = t.Accent; var D = Core.Applier.Mix(G, Colors.Black, 0.45);
                    var mono = "Consolas";
                    root.Children.Add(bg); ((FrameworkElement)bg).Opacity = 0.55;
                    // darker centre so the text stays readable
                    root.Children.Add(new Border { Background = new RadialGradientBrush(C(0x000000, 235), C(0x000000, 60)) { RadiusX = 0.55, RadiusY = 0.6 } });

                    // boot / security log, typed line by line (top left)
                    var log = new TextBlock { Foreground = B(D), FontFamily = new FontFamily(mono), FontSize = 14, Margin = new Thickness(46, 36, 0, 0), LineHeight = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                    root.Children.Add(log);
                    string user = ShellActions.UserName.ToLower().Replace(' ', '_'), host = Environment.MachineName.ToLower();
                    var lines = new List<string>
                    {
                        "[  OK  ] " + L("sitzung ", "session ") + user + "@" + host + L(" gesichert", " sealed"),
                        "[  OK  ] " + L("bildschirm gesperrt um ", "screen locked at ") + DateTime.Now.ToString("HH:mm:ss"),
                        "[  OK  ] " + L("tastatur und maus übernommen", "keyboard and mouse input captured"),
                        "[  OK  ] " + L("netzwerk ", "network ") + SysInfo.LocalIp + L(" überwacht", " monitored"),
                        "[  OK  ] " + L("zugriffsschutz aktiv", "intrusion detection armed"),
                        "[ INFO ] " + SysInfo.Os.ToLowerInvariant(),
                        "[ INFO ] " + L("laufzeit ", "uptime ") + SysInfo.UptimeText,
                        L("[ WARTE ] auf authentifizierung …", "[ WAIT ] awaiting authentication …"),
                    };
                    int li = 0, ci = 0;
                    var sbLog = new System.Text.StringBuilder();
                    DispatcherTimer typer = null;
                    typer = Timer(28, (o, e) =>
                    {
                        if (li >= lines.Count) { typer.Stop(); return; }
                        var l = lines[li];
                        int n = Math.Min(l.Length - ci, 3);
                        sbLog.Append(l, ci, n); ci += n;
                        if (ci >= l.Length) { sbLog.Append('\n'); li++; ci = 0; }
                        log.Text = sbLog.ToString();
                    });
                    if (still)
                    {
                        lines[1] = "[  OK  ] " + L("bildschirm gesperrt", "screen locked");
                        lines[lines.Count - 1] = L("[ WARTE ] auf authentifizierung", "[ WAIT ] awaiting authentication");
                        log.Text = string.Join("\n", lines);
                    }
                    else typer.Start();

                    // centre: SYSTEM LOCKED, big clock, auth box
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    var titleGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                    TextBlock Title(Color c) => new TextBlock { Text = L("S Y S T E M   G E S P E R R T", "S Y S T E M   L O C K E D"), FontFamily = new FontFamily(mono), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = B(c), HorizontalAlignment = HorizontalAlignment.Center };
                    var red = Title(C(0xFF2A55, 200)); var cyan = Title(C(0x00E5FF, 200)); var main = Title(G);
                    red.Opacity = 0; cyan.Opacity = 0;
                    var rt = new TranslateTransform(); var ct = new TranslateTransform();
                    red.RenderTransform = rt; cyan.RenderTransform = ct;
                    titleGrid.Children.Add(red); titleGrid.Children.Add(cyan); titleGrid.Children.Add(main);
                    var rnd = new Random();
                    int gl = 0;
                    var glitch = Timer(120, (o, e) =>
                    {
                        if (gl > 0) { gl--; rt.X = rnd.Next(-6, 7); ct.X = rnd.Next(-6, 7); red.Opacity = cyan.Opacity = 1; main.Opacity = rnd.Next(2) == 0 ? 0.6 : 1; }
                        else { red.Opacity = cyan.Opacity = 0; main.Opacity = 1; if (rnd.Next(25) == 0) gl = 3; }
                    });
                    if (!still) glitch.Start();
                    sp.Children.Add(titleGrid);

                    var clock = new TextBlock { FontFamily = new FontFamily(mono), FontSize = 150, FontWeight = FontWeights.Bold, Foreground = B(G), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                    clock.Effect = new DropShadowEffect { Color = G, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.7 };
                    var date = Text("", 20, D, null, mono);
                    if (still)
                    {
                        // Windows puts its own clock at the top – keep the design below it
                        sp.Margin = new Thickness(0, 260, 0, 0);
                        var tag = new TextBlock { Text = "■  ENCRYPTED SESSION  ·  ACCESS RESTRICTED  ■", FontFamily = new FontFamily(mono), FontSize = 15, Foreground = B(C(0xFF3B5C)), HorizontalAlignment = HorizontalAlignment.Center };
                        sp.Children.Add(new Border { Child = tag, BorderBrush = B(C(0xFF3B5C, 160)), BorderThickness = new Thickness(1), Background = B(0x000000, 170), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
                    }
                    else { sp.Children.Add(clock); sp.Children.Add(date); }

                    var authPanel = new StackPanel();
                    authPanel.Children.Add(new TextBlock { Text = user + "@" + host, Foreground = B(Core.Applier.Mix(G, Colors.White, 0.6)), FontFamily = new FontFamily(mono), FontSize = 18 });
                    var prompt = new TextBlock { Foreground = B(G), FontFamily = new FontFamily(mono), FontSize = 18, Margin = new Thickness(0, 6, 0, 0) };
                    authPanel.Children.Add(prompt);
                    var authBox = new Border { Child = authPanel, BorderBrush = B(G), BorderThickness = new Thickness(1), Background = B(0x000000, 200), Padding = new Thickness(26, 16, 26, 16), Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 620 };
                    sp.Children.Add(authBox);
                    if (auth != null)
                    {
                        // the real password line: typed right here, checked against the Windows account
                        var info = new TextBlock { Foreground = B(D), FontFamily = new FontFamily(mono), FontSize = 13, Margin = new Thickness(0, 10, 0, 0) };
                        authPanel.Children.Add(info);
                        auth.Prompt = prompt; auth.Info = info; auth.Box = authBox; auth.Accent = G; auth.Terminal = true;
                    }
                    root.Children.Add(sp);

                    var status = new TextBlock { Foreground = B(D), FontFamily = new FontFamily(mono), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 34) };
                    root.Children.Add(status);
                    bool blink = false; int tick = 0;
                    if (still)
                    {
                        prompt.Text = "> " + L("authentifizieren: beliebige taste drücken", "authenticate: press any key") + " █";
                        status.Text = "secure session   │   " + host + "   │   " + SysInfo.Os.ToLowerInvariant() + "   │   winmorph hacker";
                        break;
                    }
                    Tick(() =>
                    {
                        blink = !blink;
                        var now = DateTime.Now;
                        clock.Text = now.ToString("HH:mm:ss");
                        date.Text = now.ToString(De ? "dddd, dd. MMMM yyyy" : "dddd, MMMM dd yyyy", Ci).ToLowerInvariant();
                        if (auth != null) auth.Tick();
                        else prompt.Text = "> " + L("authentifizieren: beliebige taste drücken", "authenticate: press any key") + (blink ? " █" : "  ");
                        if (tick++ % 2 == 0) status.Text = StatusLine();
                    });
                    break;
                }
                case LockKind.Ai:
                {
                    root.Children.Add(bg);
                    root.Children.Add(new Border { Background = B(0x000000, 120) });
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var orb = new Border { Width = 170, Height = 170, CornerRadius = new CornerRadius(85), HorizontalAlignment = HorizontalAlignment.Center, Background = new RadialGradientBrush(Core.Applier.Mix(t.Accent, Colors.White, 0.7), Core.Applier.Mix(t.Accent, Colors.Black, 0.5)) { GradientOrigin = new Point(0.35, 0.3) } };
                    var glow = new DropShadowEffect { Color = t.Accent, BlurRadius = 40, ShadowDepth = 0, Opacity = 1 };
                    orb.Effect = glow;
                    glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(20, 90, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                    var sc = new ScaleTransform(1, 1);
                    orb.RenderTransform = sc; orb.RenderTransformOrigin = new Point(0.5, 0.5);
                    var pulse = new DoubleAnimation(0.94, 1.04, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, pulse); sc.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                    sp.Children.Add(orb);
                    var greet = Text(Greeting() + ", " + FirstName, 46, C(0xFFFFFF), FontWeights.SemiBold);
                    greet.Margin = new Thickness(0, 46, 0, 6);
                    sp.Children.Add(greet);
                    var time = Text("", 22, C(0xFFFFFF, 190));
                    Tick(() => time.Text = DateTime.Now.ToString(De ? "HH:mm · dddd, d. MMMM" : "HH:mm · dddd, MMMM d", Ci));
                    sp.Children.Add(time);
                    var h = Text(L("Ich bin bereit, wenn du es bist. Drücke eine Taste.", "Ready when you are. Press any key."), 15, A(Core.Applier.Mix(t.Accent, Colors.White, 0.5), 230));
                    h.Margin = new Thickness(0, 30, 0, 0);
                    sp.Children.Add(h);
                    root.Children.Add(sp);
                    break;
                }
                case LockKind.Zen:
                {
                    root.Children.Add(bg);
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var circle = new Border { Width = 160, Height = 160, CornerRadius = new CornerRadius(80), BorderBrush = B(0x6B5B45, 160), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center };
                    var sc = new ScaleTransform(1, 1);
                    circle.RenderTransform = sc; circle.RenderTransformOrigin = new Point(0.5, 0.5);
                    var breathe = new DoubleAnimation(0.8, 1.15, TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, breathe); sc.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
                    sp.Children.Add(circle);
                    var word = Text(L("atme ein · atme aus", "breathe in · breathe out"), 20, C(0x6B5B45), FontWeights.Light);
                    word.Margin = new Thickness(0, 40, 0, 6);
                    sp.Children.Add(word);
                    var time = Text("", 54, C(0x4A3F30), FontWeights.Light);
                    Tick(() => time.Text = DateTime.Now.ToString("HH:mm"));
                    sp.Children.Add(time);
                    root.Children.Add(sp);
                    break;
                }
                case LockKind.Gaming:
                {
                    root.Children.Add(bg);
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, RenderTransform = new SkewTransform(-10, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
                    var time = Text("", 170, C(0xFFFFFF), FontWeights.Bold, "Bahnschrift");
                    time.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.9 };
                    sp.Children.Add(time);
                    sp.Children.Add(Text(L("DRÜCKE EINE TASTE", "PRESS ANY KEY"), 26, t.Accent, FontWeights.Bold, "Bahnschrift"));
                    Tick(() => time.Text = DateTime.Now.ToString("HH:mm"));
                    root.Children.Add(sp);
                    break;
                }
                default:
                {
                    root.Children.Add(bg);
                    root.Children.Add(new Border { Background = V((0, C(0x000000, 0)), (0.6, C(0x000000, 20)), (1, C(0x000000, 120))) });
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(90, 0, 0, 110) };
                    var time = new TextBlock { FontSize = 130, Foreground = B(0xFFFFFF), FontWeight = FontWeights.Light, FontFamily = new FontFamily("Segoe UI Variable Display Light, Segoe UI Light") };
                    var date = new TextBlock { FontSize = 34, Foreground = B(0xFFFFFF), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
                    sp.Children.Add(time); sp.Children.Add(date);
                    Tick(() => { time.Text = DateTime.Now.ToString("HH:mm"); date.Text = DateTime.Now.ToString(De ? "dddd, d. MMMM" : "dddd, MMMM d", Ci); });
                    root.Children.Add(sp);
                    break;
                }
            }
            return root;
        }

        /// <summary>Renders a lock screen into an image (for previews).</summary>
        public static BitmapSource Render(string presetId, int w, int h, double scale, bool still = false)
        {
            var p = Core.Presets.Find(presetId) ?? Core.Presets.All[0];
            var t = ShellThemes.ForLock(p.Id);
            ImageSource pic = p.Id == "terminal" ? (ImageSource)Live.LiveView.Snapshot("matrix", (int)(w * scale), (int)(h * scale), 3, fullResolution: true) : Core.WallpaperArt.Render(p, (int)(w * scale), (int)(h * scale), false);
            var ui = (FrameworkElement)Build(t, p, pic, null, still);
            ui.Width = w; ui.Height = h;
            var host = new Border { Child = ui, Width = w, Height = h, Background = Brushes.Black };
            host.Measure(new Size(w, h)); host.Arrange(new Rect(0, 0, w, h)); host.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(host);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// The picture for the real Windows lock screen (shown when the lid closes, after sleep, …).
        /// Only looks with a designed lock screen get one; otherwise null (the wallpaper is used).
        /// Must run on a UI thread.
        /// </summary>
        public static string RenderForWindows(Core.Preset p)
        {
            if (p == null || ShellThemes.ForLock(p.Id).Lock != LockKind.Terminal) return null;
            var mi = W32.PrimaryMonitor(out double s);
            int pw = mi.rcMonitor.Right - mi.rcMonitor.Left, ph = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            if (s <= 0) s = 1;
            var bmp = Render(p.Id, (int)Math.Round(pw / s), (int)Math.Round(ph / s), s, still: true);
            var file = System.IO.Path.Combine(Core.Backup.DataDir, "lock-design-" + p.Id + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(file)) enc.Save(fs);
            return file;
        }
    }
}

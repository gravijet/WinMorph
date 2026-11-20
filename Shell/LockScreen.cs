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

        /// <summary>
        /// The animated background of a look's lock screen: digital rain for the hacker look. The others use
        /// their still wallpaper (their live desktops carry a clock of their own, the lock screen has one).
        /// Always the look's own, never whatever live desktop happens to run.
        /// </summary>
        static string LiveSpec(Core.Preset p) => p.Id == "terminal" ? "scene:matrix" : null;

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

        /// <summary>A short "no" shake: a few swings that settle.</summary>
        static void Shake(UIElement el, double amp = 12)
        {
            var tt = el.RenderTransform as TranslateTransform ?? new TranslateTransform();
            el.RenderTransform = tt;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
            foreach (var (ms, k) in new[] { (0, 0.0), (60, -1.0), (125, 0.85), (190, -0.6), (255, 0.35), (320, -0.15), (380, 0.0) })
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(k * amp, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase()));
            tt.BeginAnimation(TranslateTransform.XProperty, shake);
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
            if (auth != null && auth.Prompt == null && auth.Render == null && root is Grid g)
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
                case LockKind.Seven:
                {
                    root.Children.Add(bg);
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
                    var frame = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(10), Background = V((0, C(0xFFFFFF, 140)), (1, C(0xFFFFFF, 60))), BorderBrush = B(0xFFFFFF, 200), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(9), Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 2, Opacity = 0.5 } };
                    frame.Child = Avatar(130, t.Accent, Core.Applier.Mix(t.Accent, Colors.Black, 0.4), 4);
                    sp.Children.Add(frame);
                    var name = Text(ShellActions.UserName, 26, C(0xFFFFFF));
                    name.Margin = new Thickness(0, 14, 0, 4);
                    name.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.7 };
                    sp.Children.Add(name);
                    var state = Text(L("Gesperrt", "Locked"), 15, C(0xFFFFFF, 220));
                    state.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6 };
                    sp.Children.Add(state);

                    // the Windows 7 sign-in line: white password box and the round blue arrow
                    var pwText = new TextBlock { FontSize = 13, Foreground = B(0x1E1E1E), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI") };
                    var placeholder = new TextBlock { Text = L("Kennwort", "Password"), FontSize = 13, FontStyle = FontStyles.Italic, Foreground = B(0x8D8D8D), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI") };
                    var fieldGrid = new Grid();
                    fieldGrid.Children.Add(placeholder); fieldGrid.Children.Add(pwText);
                    var field = new Border
                    {
                        Width = 226, Height = 28, CornerRadius = new CornerRadius(2), Padding = new Thickness(7, 0, 7, 0), Child = fieldGrid,
                        Background = V((0, C(0xFFFFFF)), (1, C(0xF4F7FB))), BorderBrush = B(0x5F7FA6), BorderThickness = new Thickness(1),
                        Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Color = C(0xBFE3FF), Opacity = 0.9 },
                    };
                    var arrowGlyph = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    var arrow = new Grid { Width = 30, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
                    arrow.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new RadialGradientBrush(C(0x6FB5F2), C(0x1E5BAA)) { GradientOrigin = new Point(0.5, 0.25), Center = new Point(0.5, 0.4), RadiusX = 0.7, RadiusY = 0.7 }, Stroke = B(0x0E3A75), StrokeThickness = 1 });
                    arrow.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(4, 2, 4, 14), Fill = V((0, C(0xFFFFFF, 150)), (1, C(0xFFFFFF, 10))) });
                    arrow.Children.Add(arrowGlyph);
                    var pwRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(38, 12, 0, 0), Opacity = 0, Visibility = Visibility.Collapsed };
                    pwRow.Children.Add(field); pwRow.Children.Add(arrow);
                    var rowMove = new TranslateTransform(); pwRow.RenderTransform = rowMove;
                    sp.Children.Add(pwRow);
                    var note = Text("", 13, C(0xFFFFFF, 225));
                    note.Margin = new Thickness(0, 12, 0, 0); note.TextWrapping = TextWrapping.Wrap; note.MaxWidth = 460;
                    note.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.7 };
                    sp.Children.Add(note);
                    root.Children.Add(sp);
                    var brand = Text(p.Name, 26, C(0xFFFFFF, 220), FontWeights.Light);
                    brand.VerticalAlignment = VerticalAlignment.Bottom; brand.Margin = new Thickness(0, 0, 0, 40);
                    root.Children.Add(brand);

                    if (auth == null)
                    {
                        Tick(() => note.Text = DateTime.Now.ToString("HH:mm") + "  ·  " + hint);
                        break;
                    }
                    auth.Terminal = false; auth.Accent = t.Accent;
                    bool shown = false;
                    auth.Render = () =>
                    {
                        if (auth.Unlocked) return;
                        if (auth.Typing != shown)
                        {
                            shown = auth.Typing;
                            if (shown)
                            {
                                pwRow.Visibility = Visibility.Visible;
                                pwRow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                                rowMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                            }
                            else { pwRow.BeginAnimation(UIElement.OpacityProperty, null); pwRow.Opacity = 0; pwRow.Visibility = Visibility.Collapsed; }
                        }
                        placeholder.Visibility = auth.Length == 0 ? Visibility.Visible : Visibility.Hidden;
                        pwText.Text = new string('●', Math.Min(auth.Length, 24)) + (auth.Blink && auth.Length > 0 ? "|" : "");
                        if (auth.Note != null) { note.Text = auth.Note; note.Foreground = B(auth.NoteBad ? C(0xFFD6D6) : C(0xFFFFFF, 225)); }
                        else if (shown) { note.Text = L("Enter: Entsperren   ·   F1: Windows Hello   ·   F2: Windows-Anmeldung", "Enter: unlock   ·   F1: Windows Hello   ·   F2: Windows sign-in"); note.Foreground = B(C(0xFFFFFF, 170)); }
                        else { note.Text = DateTime.Now.ToString("HH:mm") + "  ·  " + hint; note.Foreground = B(C(0xFFFFFF, 200)); }
                    };
                    auth.OnDenied = () =>
                    {
                        auth.Render();
                        field.BorderBrush = B(0xC42B1C);
                        Shake(pwRow);
                        var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                        back.Tick += (o, e) => { back.Stop(); field.BorderBrush = B(0x5F7FA6); };
                        back.Start();
                    };
                    auth.OnGranted = () =>
                    {
                        // like Windows 7: just "Welcome"
                        pwRow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                        note.Text = "";
                        state.Text = L("Willkommen", "Welcome");
                        state.FontSize = 22;
                    };
                    Tick(auth.Tick);
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
                        var tag = new TextBlock { Text = L("■  VERSCHLÜSSELTE SITZUNG  ·  ZUGRIFF BESCHRÄNKT  ■", "■  ENCRYPTED SESSION  ·  ACCESS RESTRICTED  ■"), FontFamily = new FontFamily(mono), FontSize = 15, Foreground = B(C(0xFF3B5C)), HorizontalAlignment = HorizontalAlignment.Center };
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
                        status.Text = L("geschützte sitzung   │   ", "secure session   │   ") + host + "   │   " + SysInfo.Os.ToLowerInvariant() + "   │   winmorph hacker";
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
                case LockKind.Zen:
                {
                    root.Children.Add(bg);
                    var ink = C(0x4A3F30); var umber = C(0x6B5B45); var shu = C(0xB5452B);
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var circle = new Border { Width = 160, Height = 160, CornerRadius = new CornerRadius(80), BorderBrush = B(0x6B5B45, 160), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center };
                    var sc = new ScaleTransform(1, 1);
                    circle.RenderTransform = sc; circle.RenderTransformOrigin = new Point(0.5, 0.5);
                    var breathe = new DoubleAnimation(0.8, 1.15, TimeSpan.FromSeconds(4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                    if (!still) { sc.BeginAnimation(ScaleTransform.ScaleXProperty, breathe); sc.BeginAnimation(ScaleTransform.ScaleYProperty, breathe); }
                    sp.Children.Add(circle);
                    var word = Text(L("atme ein · atme aus", "breathe in · breathe out"), 20, umber, FontWeights.Light);
                    var time = Text("", 54, ink, FontWeights.Light);
                    Tick(() => time.Text = DateTime.Now.ToString("HH:mm"));
                    // the breathing words and the password share one place: one fades out while the other comes in
                    var place = new Grid { Margin = new Thickness(0, 40, 0, 6), HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 340 };
                    place.Children.Add(word);
                    sp.Children.Add(place);
                    sp.Children.Add(time);
                    root.Children.Add(sp);
                    if (auth == null) break;

                    // the password: small ink stones laid on a brush line
                    var stage = new Grid { Opacity = 0, IsHitTestVisible = false };
                    var stones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Height = 18, Margin = new Thickness(0, 0, 0, 9) };
                    var hold = Text(L("Passwort", "Password"), 15, C(0x9C8E79), FontWeights.Light);
                    hold.VerticalAlignment = VerticalAlignment.Bottom; hold.Margin = new Thickness(0, 0, 0, 9);
                    var stroke = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M0,3 C40,0.6 120,0.2 160,1.4 C200,2.4 280,1.2 320,3.4 C280,4.6 200,4.2 160,4.6 C120,5 40,5.4 0,3 Z"),
                        Fill = B(umber), Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Width = 320, Height = 6, Stretch = Stretch.Fill,
                    };
                    var lineBox = new Grid { Height = 40 };
                    lineBox.Children.Add(hold); lineBox.Children.Add(stones); lineBox.Children.Add(stroke);
                    var note = Text("", 13, C(0x8A7A62), FontWeights.Normal);
                    note.Margin = new Thickness(0, 10, 0, 0);
                    var stack = new StackPanel();
                    stack.Children.Add(lineBox); stack.Children.Add(note);
                    stage.Children.Add(stack);
                    var stageMove = new TranslateTransform(); stage.RenderTransform = stageMove;
                    place.Children.Add(stage);

                    auth.Terminal = false; auth.Accent = umber;
                    bool shown = false;
                    var soft = new CubicEase { EasingMode = EasingMode.EaseOut };
                    auth.Render = () =>
                    {
                        if (auth.Unlocked) return;
                        if (auth.Typing != shown)
                        {
                            shown = auth.Typing;
                            word.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(shown ? 0 : 1, TimeSpan.FromMilliseconds(shown ? 220 : 400)));
                            stage.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(shown ? 1 : 0, TimeSpan.FromMilliseconds(shown ? 420 : 220)) { BeginTime = TimeSpan.FromMilliseconds(shown ? 120 : 0) });
                            stageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shown ? 10 : 0, shown ? 0 : 6, TimeSpan.FromMilliseconds(520)) { EasingFunction = soft });
                            if (!shown) stones.Children.Clear();
                        }
                        // one stone per character: a new one drops onto the line, a deleted one is taken away
                        while (stones.Children.Count > auth.Length) stones.Children.RemoveAt(stones.Children.Count - 1);
                        while (stones.Children.Count < Math.Min(auth.Length, 24))
                        {
                            int i = stones.Children.Count;
                            double d = 9 + (i * 7 % 5);
                            var stone = new System.Windows.Shapes.Ellipse { Width = d * 1.18, Height = d, Fill = B(ink), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Bottom, RenderTransformOrigin = new Point(0.5, 1) };
                            var st = new ScaleTransform(0.2, 0.2); var tr = new TranslateTransform(0, -14);
                            var tg = new TransformGroup(); tg.Children.Add(st); tg.Children.Add(tr);
                            stone.RenderTransform = tg;
                            stones.Children.Add(stone);
                            var drop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
                            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = drop });
                            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = drop });
                            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BounceEase { Bounces = 1, Bounciness = 4, EasingMode = EasingMode.EaseOut } });
                        }
                        hold.Visibility = auth.Length == 0 ? Visibility.Visible : Visibility.Hidden;
                        note.Text = auth.Note ?? L("Enter · entsperren      F1 · Windows Hello", "Enter · unlock      F1 · Windows Hello");
                        note.Foreground = B(auth.Note != null && auth.NoteBad ? shu : C(0x8A7A62));
                    };
                    auth.OnDenied = () =>
                    {
                        // the stones roll off the line, the line flushes vermilion and calms down again
                        foreach (var el in stones.Children.Cast<UIElement>().ToList())
                        {
                            var tr = new TranslateTransform(); el.RenderTransform = tr;
                            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 26, TimeSpan.FromMilliseconds(380)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)));
                        }
                        var clear = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                        clear.Tick += (o, e) => { clear.Stop(); stones.Children.Clear(); auth.Render(); };
                        clear.Start();
                        var red = new SolidColorBrush(shu); stroke.Fill = red;
                        red.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(umber, TimeSpan.FromMilliseconds(1400)) { BeginTime = TimeSpan.FromMilliseconds(500) });
                        Shake(stage, 7);
                        hold.Visibility = Visibility.Hidden;
                        note.Text = auth.Note; note.Foreground = B(shu);
                    };
                    auth.OnGranted = () =>
                    {
                        // the breath completes: the circle opens wide and fades into the desktop
                        note.Text = L("Willkommen zurück", "Welcome back"); note.Foreground = B(umber);
                        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(2.2, TimeSpan.FromMilliseconds(450)) { EasingFunction = soft });
                        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(2.2, TimeSpan.FromMilliseconds(450)) { EasingFunction = soft });
                        circle.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(450)));
                    };
                    Tick(auth.Tick);
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

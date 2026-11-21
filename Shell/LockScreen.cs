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
    public static partial class LockScreen
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

            switch (t.Lock)
            {
                case LockKind.Seven:
                {
                    // Windows 7's own lock: picture tile, name, "Locked", the password field with the round blue arrow,
                    // "Switch User", the red power button and the edition at the bottom – nothing that Windows 7 did not show.
                    root.Children.Add(bg);
                    var segoe = new FontFamily("Segoe UI");
                    var center = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 60) };
                    var sp = new StackPanel();
                    center.Children.Add(sp);

                    var frame = new Border
                    {
                        Width = 150, Height = 150, CornerRadius = new CornerRadius(9), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center,
                        Background = V((0, C(0xFFFFFF, 150)), (0.5, C(0xFFFFFF, 90)), (1, C(0xFFFFFF, 60))), BorderBrush = B(0xFFFFFF, 210), BorderThickness = new Thickness(1),
                        Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Opacity = 0.55, Color = C(0x061A3A) },
                    };
                    var pic = AccountPicture.Get(192);
                    frame.Child = pic != null
                        ? (UIElement)new Border { CornerRadius = new CornerRadius(3), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill }, BorderBrush = B(0x000000, 90), BorderThickness = new Thickness(1) }
                        : Avatar(128, t.Accent, Core.Applier.Mix(t.Accent, Colors.Black, 0.4), 3);
                    sp.Children.Add(frame);
                    var name = Text(ShellActions.UserName, 24, C(0xFFFFFF), null, "Segoe UI");
                    name.Margin = new Thickness(0, 12, 0, 2);
                    name.Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.8, Color = C(0x061A3A) };
                    sp.Children.Add(name);
                    var state = Text(L("Gesperrt", "Locked"), 14, C(0xFFFFFF, 225), null, "Segoe UI");
                    state.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.7, Color = C(0x061A3A) };
                    sp.Children.Add(state);

                    // the password line
                    var pwText = new TextBlock { FontSize = 13, Foreground = B(0x1E1E1E), VerticalAlignment = VerticalAlignment.Center, FontFamily = segoe };
                    var placeholder = new TextBlock { Text = L("Kennwort", "Password"), FontSize = 13, FontStyle = FontStyles.Italic, Foreground = B(0x8D8D8D), VerticalAlignment = VerticalAlignment.Center, FontFamily = segoe };
                    var caret = new Border { Width = 1, Height = 15, Background = B(0x1E1E1E), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                    var fieldRow = new StackPanel { Orientation = Orientation.Horizontal };
                    fieldRow.Children.Add(pwText); fieldRow.Children.Add(caret);
                    var fieldGrid = new Grid();
                    fieldGrid.Children.Add(placeholder); fieldGrid.Children.Add(fieldRow);
                    var field = new Border
                    {
                        Width = 226, Height = 26, CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 0, 6, 0), Child = fieldGrid,
                        Background = V((0, C(0xFFFFFF)), (1, C(0xF4F7FB))), BorderBrush = B(0x3D6DA8), BorderThickness = new Thickness(1),
                        Effect = new DropShadowEffect { BlurRadius = 7, ShadowDepth = 0, Color = C(0xBFE3FF), Opacity = 0.9 },
                    };
                    var arrowGlyph = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 12, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    var arrow = new Grid { Width = 29, Height = 29, Margin = new Thickness(7, 0, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                    var arrowBall = new System.Windows.Shapes.Ellipse { Fill = new RadialGradientBrush(C(0x6FB5F2), C(0x1E5BAA)) { GradientOrigin = new Point(0.5, 0.25), Center = new Point(0.5, 0.4), RadiusX = 0.7, RadiusY = 0.7 }, Stroke = B(0x0E3A75), StrokeThickness = 1 };
                    arrow.Children.Add(arrowBall);
                    arrow.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(4, 2, 4, 14), Fill = V((0, C(0xFFFFFF, 150)), (1, C(0xFFFFFF, 10))) });
                    arrow.Children.Add(arrowGlyph);
                    arrow.MouseEnter += (o, e) => arrowBall.Fill = new RadialGradientBrush(C(0x93CBFA), C(0x2A6FC2)) { GradientOrigin = new Point(0.5, 0.25), Center = new Point(0.5, 0.4), RadiusX = 0.7, RadiusY = 0.7 };
                    arrow.MouseLeave += (o, e) => arrowBall.Fill = new RadialGradientBrush(C(0x6FB5F2), C(0x1E5BAA)) { GradientOrigin = new Point(0.5, 0.25), Center = new Point(0.5, 0.4), RadiusX = 0.7, RadiusY = 0.7 };
                    arrow.MouseLeftButtonUp += (o, e) => { e.Handled = true; auth?.Submit?.Invoke(); };
                    var pwRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(36, 14, 0, 0) };
                    pwRow.Children.Add(field); pwRow.Children.Add(arrow);
                    sp.Children.Add(pwRow);

                    // a quiet line under the field: Windows Hello (only when it is set up) and short messages
                    var note = Text("", 12, C(0xFFFFFF, 230), null, "Segoe UI");
                    note.Margin = new Thickness(0, 10, 0, 0); note.TextWrapping = TextWrapping.Wrap; note.MaxWidth = 420; note.MinHeight = 18;
                    note.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.7, Color = C(0x061A3A) };
                    sp.Children.Add(note);
                    var hello = Text(L("Mit Windows Hello anmelden", "Sign in with Windows Hello"), 12, C(0xBFE3FF), null, "Segoe UI");
                    hello.Cursor = Cursors.Hand; hello.Visibility = Visibility.Collapsed; hello.Margin = new Thickness(0, 4, 0, 0);
                    hello.MouseEnter += (o, e) => hello.TextDecorations = TextDecorations.Underline;
                    hello.MouseLeave += (o, e) => hello.TextDecorations = null;
                    hello.MouseLeftButtonUp += (o, e) => { e.Handled = true; auth?.UseHello?.Invoke(); };
                    sp.Children.Add(hello);

                    // "Switch User": the glassy Windows 7 button
                    Border GlassButton(string text, Action a)
                    {
                        var tb = Text(text, 13, C(0xFFFFFF), null, "Segoe UI");
                        tb.Margin = new Thickness(16, 5, 16, 6);
                        var normal = V((0, C(0xFFFFFF, 70)), (0.5, C(0xFFFFFF, 35)), (0.51, C(0xFFFFFF, 15)), (1, C(0xFFFFFF, 30)));
                        var hot = V((0, C(0xFFFFFF, 110)), (0.5, C(0xFFFFFF, 60)), (0.51, C(0xFFFFFF, 30)), (1, C(0xFFFFFF, 60)));
                        var btn = new Border { Child = tb, CornerRadius = new CornerRadius(3), BorderBrush = B(0xFFFFFF, 140), BorderThickness = new Thickness(1), Background = normal, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center };
                        btn.MouseEnter += (o, e) => btn.Background = hot;
                        btn.MouseLeave += (o, e) => btn.Background = normal;
                        btn.MouseLeftButtonUp += (o, e) => { e.Handled = true; a?.Invoke(); };
                        return btn;
                    }
                    var switchUser = GlassButton(L("Benutzer wechseln", "Switch User"), () => auth?.UseWindows?.Invoke());
                    switchUser.Margin = new Thickness(0, 28, 0, 0);
                    sp.Children.Add(switchUser);

                    // "Welcome" with the turning ring while the password is checked (as Windows 7 did)
                    var welcome = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed };
                    var ring = new System.Windows.Shapes.Ellipse
                    {
                        Width = 22, Height = 22, StrokeThickness = 3, Stroke = new LinearGradientBrush(C(0xFFFFFF), C(0xFFFFFF, 0), 0), RenderTransformOrigin = new Point(0.5, 0.5),
                        StrokeDashArray = new DoubleCollection { 5, 2.2 }, Margin = new Thickness(0, 0, 10, 0),
                    };
                    var spin = new RotateTransform();
                    ring.RenderTransform = spin;
                    welcome.Children.Add(ring);
                    welcome.Children.Add(Text(L("Willkommen", "Welcome"), 24, C(0xFFFFFF), null, "Segoe UI"));
                    sp.Children.Add(welcome);

                    // the error page: Windows 7 swaps the tile for its message and an OK button
                    var error = new StackPanel { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
                    var errText = Text(L("Der Benutzername oder das Kennwort ist falsch.", "The user name or password is incorrect."), 15, C(0xFFFFFF), null, "Segoe UI");
                    errText.Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.8, Color = C(0x061A3A) };
                    error.Children.Add(errText);
                    Action dismiss = null;
                    var ok = GlassButton("OK", () => dismiss?.Invoke());
                    ok.Margin = new Thickness(0, 22, 0, 0); ((TextBlock)ok.Child).Margin = new Thickness(30, 5, 30, 6);
                    error.Children.Add(ok);
                    center.Children.Add(error);
                    root.Children.Add(center);

                    // the edition at the bottom
                    var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 38) };
                    var logo = ClassicLogo(30); logo.Margin = new Thickness(0, 0, 10, 0);
                    brand.Children.Add(logo);
                    var brandText = new TextBlock { FontFamily = new FontFamily("Segoe UI Light"), FontSize = 26, Foreground = B(0xFFFFFF, 235), VerticalAlignment = VerticalAlignment.Center };
                    brandText.Inlines.Add(new System.Windows.Documents.Run("Windows"));
                    brandText.Inlines.Add(new System.Windows.Documents.Run(" 7") { FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold });
                    brandText.Inlines.Add(new System.Windows.Documents.Run("  " + AccountPicture.SevenEdition) { FontSize = 20 });
                    brand.Children.Add(brandText);
                    brand.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.6, Color = C(0x061A3A) };
                    root.Children.Add(brand);

                    // the red power button at the bottom right, with its menu
                    var powerMenu = new StackPanel { Visibility = Visibility.Collapsed };
                    var power = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 34, 34) };
                    var redBall = new Grid { Width = 34, Height = 34, Cursor = Cursors.Hand, Background = Brushes.Transparent };
                    redBall.Children.Add(new System.Windows.Shapes.Ellipse { Fill = new RadialGradientBrush(C(0xF08A6E), C(0xB3261E)) { GradientOrigin = new Point(0.5, 0.2), Center = new Point(0.5, 0.35), RadiusX = 0.75, RadiusY = 0.75 }, Stroke = B(0x6E120C), StrokeThickness = 1 });
                    redBall.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(5, 2, 5, 16), Fill = V((0, C(0xFFFFFF, 140)), (1, C(0xFFFFFF, 0))) });
                    redBall.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 15, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    var drop = new Border { Width = 16, Height = 22, Margin = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(3), Background = V((0, C(0xFFFFFF, 60)), (1, C(0xFFFFFF, 20))), BorderBrush = B(0xFFFFFF, 110), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 8, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    power.Children.Add(redBall); power.Children.Add(drop);
                    redBall.MouseLeftButtonUp += (o, e) => { e.Handled = true; ShellActions.ShutdownNow(); };
                    drop.MouseLeftButtonUp += (o, e) => { e.Handled = true; powerMenu.Visibility = powerMenu.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
                    var menuBox = new Border
                    {
                        Child = powerMenu, Background = B(0xF5F6F7), BorderBrush = B(0x979797), BorderThickness = new Thickness(1), Padding = new Thickness(2),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 34, 74),
                        Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Opacity = 0.4 },
                    };
                    void PowerItem(string text, Action a)
                    {
                        var tb = new TextBlock { Text = text, FontFamily = segoe, FontSize = 12, Foreground = B(0x1E1E1E), Margin = new Thickness(26, 4, 30, 4) };
                        var it = new Border { Child = tb, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                        it.MouseEnter += (o, e) => { it.Background = V((0, C(0xF1F6FD)), (1, C(0xD5E7FC))); it.BorderBrush = B(0xA8D8EB); };
                        it.MouseLeave += (o, e) => { it.Background = Brushes.Transparent; it.BorderBrush = Brushes.Transparent; };
                        it.MouseLeftButtonUp += (o, e) => { e.Handled = true; powerMenu.Visibility = Visibility.Collapsed; a(); };
                        powerMenu.Children.Add(it);
                    }
                    PowerItem(L("Energie sparen", "Sleep"), ShellActions.Sleep);
                    PowerItem(L("Neu starten", "Restart"), ShellActions.RestartNow);
                    PowerItem(L("Herunterfahren", "Shut down"), ShellActions.ShutdownNow);
                    menuBox.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = powerMenu });
                    root.Children.Add(power);
                    root.Children.Add(menuBox);

                    if (auth == null) break;
                    auth.Terminal = false; auth.Accent = t.Accent; auth.AlwaysOpen = true;
                    bool erred = false, busyShown = false;
                    var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever };
                    void Busy(bool on)
                    {
                        if (on == busyShown) return;
                        busyShown = on;
                        pwRow.Visibility = note.Visibility = switchUser.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                        welcome.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                        state.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                        if (on) { hello.Visibility = Visibility.Collapsed; spin.BeginAnimation(RotateTransform.AngleProperty, spinAnim); }
                        else spin.BeginAnimation(RotateTransform.AngleProperty, null);
                    }
                    dismiss = () =>
                    {
                        if (!erred) return;
                        erred = false;
                        error.Visibility = Visibility.Collapsed;
                        sp.Visibility = Visibility.Visible;
                        sp.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                        auth.Message(null, false);
                    };
                    auth.KeyFilter = k => { if (!erred) return false; dismiss(); return true; };
                    auth.Render = () =>
                    {
                        if (auth.Unlocked) return;
                        Busy(auth.Busy);
                        placeholder.Visibility = auth.Length == 0 ? Visibility.Visible : Visibility.Hidden;
                        pwText.Text = new string('●', Math.Min(auth.Length, 26));
                        caret.Opacity = auth.Blink ? 1 : 0;
                        note.Text = erred ? "" : auth.Note ?? "";
                        note.Foreground = B(auth.Note != null && auth.NoteBad ? C(0xFFD6D6) : C(0xFFFFFF, 230));
                        hello.Visibility = auth.HelloReady && !busyShown ? Visibility.Visible : Visibility.Collapsed;
                    };
                    auth.OnDenied = () =>
                    {
                        Busy(false);
                        erred = true;
                        sp.Visibility = Visibility.Collapsed;
                        error.Visibility = Visibility.Visible;
                        error.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                        auth.Render();
                    };
                    auth.OnGranted = () => Busy(true);
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
                        // fingerprint or face: offered as one more terminal button, only when Windows Hello is set up
                        var hello = new TextBlock { Text = "[ windows hello ]", Foreground = B(G), FontFamily = new FontFamily(mono), FontSize = 13, Margin = new Thickness(0, 12, 0, 0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
                        var helloBox = new Border { Child = hello, HorizontalAlignment = HorizontalAlignment.Left };
                        hello.MouseEnter += (o, e) => { helloBox.Background = B(G); hello.Foreground = B(0x000000); };
                        hello.MouseLeave += (o, e) => { helloBox.Background = null; hello.Foreground = B(G); };
                        hello.MouseLeftButtonUp += (o, e) => { e.Handled = true; auth.UseHello?.Invoke(); };
                        authPanel.Children.Add(helloBox);
                        Tick(() => hello.Visibility = auth.HelloReady ? Visibility.Visible : Visibility.Collapsed);
                    }
                    root.Children.Add(sp);

                    var status = new TextBlock { Foreground = B(D), FontFamily = new FontFamily(mono), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 34) };
                    root.Children.Add(status);
                    bool blink = false; int tick = 0;
                    if (still)
                    {
                        prompt.Text = L("sitzung gesperrt", "session locked") + " █";
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
                        else prompt.Text = L("sitzung gesperrt", "session locked") + (blink ? " █" : "  ");
                        if (tick++ % 2 == 0) status.Text = StatusLine();
                    });
                    break;
                }
                case LockKind.Zen:
                {
                    // the pond, a little misted; time and date, the account, a frosted password field
                    root.Children.Add(bg);
                    root.Children.Add(new Border { Background = new LinearGradientBrush(C(0x0B4F5C, 40), C(0x0B4F5C, 120), 90) });
                    var rings = new Grid { IsHitTestVisible = false };
                    root.Children.Add(rings);
                    var white = C(0xFFFFFF);
                    var shade = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.45, Color = C(0x0B3440) };
                    var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 90, 0, 0) };
                    var time = new TextBlock { FontFamily = ZenStyle.Family, FontWeight = FontWeights.Light, FontSize = 128, Foreground = B(white), HorizontalAlignment = HorizontalAlignment.Center, Effect = shade };
                    var date = new TextBlock { FontFamily = ZenStyle.Family, FontSize = 22, Foreground = B(C(0xFFFFFF, 235)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -8, 0, 0), Effect = shade };
                    top.Children.Add(time); top.Children.Add(date);
                    Tick(() => { time.Text = DateTime.Now.ToString("HH:mm"); date.Text = DateTime.Now.ToString(De ? "dddd, d. MMMM" : "dddd, MMMM d", Ci); });
                    root.Children.Add(top);

                    var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 150) };
                    var pic = AccountPicture.Get(192);
                    var avatar = new Border { Width = 104, Height = 104, CornerRadius = new CornerRadius(52), BorderBrush = B(C(0xFFFFFF, 230)), BorderThickness = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Center, Effect = shade };
                    if (pic != null) avatar.Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill };
                    else { avatar.Background = B(ZenStyle.Pond); avatar.Child = new TextBlock { Text = ShellActions.UserName.Substring(0, 1).ToUpper(), FontFamily = ZenStyle.Family, FontSize = 44, Foreground = B(white), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; }
                    mid.Children.Add(avatar);
                    var name = new TextBlock { Text = ShellActions.UserName, FontFamily = ZenStyle.Family, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = B(white), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 18), Effect = shade };
                    mid.Children.Add(name);

                    // the field: dots of pond green drop in, one per character
                    var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
                    var hold = new TextBlock { Text = L("Kennwort", "Password"), FontFamily = ZenStyle.Family, FontSize = 15, Foreground = B(ZenStyle.Ink2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
                    var go = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = B(ZenStyle.Koi), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
                    var goGlyph = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 14, Foreground = B(white), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
                    go.Child = goGlyph;
                    go.MouseLeftButtonUp += (o, e) => { e.Handled = true; auth?.Submit?.Invoke(); };
                    var fieldGrid = new Grid();
                    fieldGrid.Children.Add(hold); fieldGrid.Children.Add(dots); fieldGrid.Children.Add(go);
                    var field = new Border { Width = 340, Height = 48, CornerRadius = new CornerRadius(24), Background = B(C(0xFFFFFF, 205)), BorderBrush = B(C(0xFFFFFF, 240)), BorderThickness = new Thickness(1.2), Child = fieldGrid, Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.25, Color = C(0x0B3440) } };
                    var fieldMove = new TranslateTransform();
                    field.RenderTransform = fieldMove;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                    row.Children.Add(field);
                    var hello = ZenStyle.Stone("", () => auth?.UseHello?.Invoke(), "Windows Hello", 48, C(0xFFFFFF, 205));
                    hello.Margin = new Thickness(12, 0, 0, 0); hello.Visibility = Visibility.Collapsed;
                    row.Children.Add(hello);
                    mid.Children.Add(row);
                    var note = new TextBlock { FontFamily = ZenStyle.Family, FontSize = 15, Foreground = B(white), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), MinHeight = 22, Effect = shade };
                    mid.Children.Add(note);
                    root.Children.Add(mid);

                    // stones at the lower right: switch user, power (with its three choices)
                    var corner = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 36) };
                    var powerMenu = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) };
                    void Choice(string g, string tip, Action a) { var st = ZenStyle.Stone(g, a, tip, 44, C(0xFFFFFF, 190)); st.Margin = new Thickness(0, 0, 8, 0); powerMenu.Children.Add(st); }
                    Choice("", L("Energie sparen", "Sleep"), ShellActions.Sleep);
                    Choice("", L("Neu starten", "Restart"), ShellActions.RestartNow);
                    Choice("", L("Herunterfahren", "Shut down"), ShellActions.ShutdownNow);
                    corner.Children.Add(powerMenu);
                    var switchUser = ZenStyle.Stone("", () => auth?.UseWindows?.Invoke(), L("Benutzer wechseln", "Switch user"), 44, C(0xFFFFFF, 190));
                    switchUser.Margin = new Thickness(0, 0, 8, 0);
                    corner.Children.Add(switchUser);
                    corner.Children.Add(ZenStyle.Stone("", () =>
                    {
                        bool open = powerMenu.Visibility != Visibility.Visible;
                        powerMenu.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                        if (open) ZenStyle.Ripple(powerMenu, new Point(1, 0.5), 320);
                    }, L("Ein/Aus", "Power"), 44, C(0xFFFFFF, 190)));
                    root.Children.Add(corner);
                    // battery at the lower left, only on a laptop
                    var b0 = Hardware.Battery();
                    if (b0 != null)
                    {
                        var batRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(44, 0, 0, 46), Effect = shade };
                        var bg2 = new TrayGlyph(TrayGlyph.Kind.Battery, false, white, 18) { Level = b0.Percent, Charging = b0.Plugged };
                        var pct = new TextBlock { FontFamily = ZenStyle.Family, FontSize = 15, Foreground = B(white), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        batRow.Children.Add(bg2); batRow.Children.Add(pct);
                        Tick(() => { var b = Hardware.Battery(); if (b == null) return; bg2.Set(TrayGlyph.Kind.Battery, b.Percent, false, b.Plugged); pct.Text = b.Percent + " %"; });
                        root.Children.Add(batRow);
                    }
                    if (auth == null) break;

                    auth.Terminal = false; auth.Accent = ZenStyle.Pond; auth.AlwaysOpen = true;
                    var spin = new RotateTransform();
                    goGlyph.RenderTransform = spin;
                    bool busyShown = false;
                    Point FieldCenter()
                    {
                        try { return field.TranslatePoint(new Point(field.ActualWidth / 2, field.ActualHeight / 2), rings); } catch { return new Point(0, 0); }
                    }
                    auth.Render = () =>
                    {
                        if (auth.Unlocked) return;
                        if (auth.Busy != busyShown)
                        {
                            busyShown = auth.Busy;
                            goGlyph.Text = busyShown ? "" : "";
                            if (busyShown) spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
                            else spin.BeginAnimation(RotateTransform.AngleProperty, null);
                        }
                        // a new character drops in as a dot, and a ring of water spreads from the field
                        int want = Math.Min(auth.Length, 22);
                        if (dots.Children.Count < want) ZenStyle.Rings(rings, FieldCenter(), C(0xFFFFFF, 200), 1, 380);
                        while (dots.Children.Count > want) dots.Children.RemoveAt(dots.Children.Count - 1);
                        while (dots.Children.Count < want)
                        {
                            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = B(ZenStyle.Pond), Margin = new Thickness(0, 0, 7, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
                            var sc = new ScaleTransform(0.2, 0.2); dot.RenderTransform = sc;
                            dots.Children.Add(dot);
                            var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ZenStyle.Surface };
                            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop); sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                        }
                        hold.Visibility = auth.Length == 0 ? Visibility.Visible : Visibility.Hidden;
                        note.Text = auth.Note ?? "";
                        note.Foreground = B(auth.Note != null && auth.NoteBad ? C(0xFFD1C9) : white);
                        hello.Visibility = auth.HelloReady ? Visibility.Visible : Visibility.Collapsed;
                    };
                    auth.OnDenied = () =>
                    {
                        auth.Render();
                        ZenStyle.Rings(rings, FieldCenter(), ZenStyle.Momiji, 2, 420);
                        var red = new SolidColorBrush(C(0xFFFFFF, 240));
                        field.BorderBrush = red;
                        red.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1400), KeyFrames = { new LinearColorKeyFrame(ZenStyle.Momiji, KeyTime.FromPercent(0.1)), new EasingColorKeyFrame(C(0xFFFFFF, 240), KeyTime.FromPercent(1), ZenStyle.Settle) } });
                        Shake(field, 9);
                    };
                    auth.OnGranted = () =>
                    {
                        // one wide ring opens over the whole pond and everything clears
                        ZenStyle.Rings(rings, FieldCenter(), C(0xFFFFFF, 230), 3, 900);
                        foreach (var el in new UIElement[] { mid, top, corner }) el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ZenStyle.Settle });
                    };
                    Tick(auth.Tick);
                    break;
                }
                case LockKind.Mac:
                    MacLock(root, bg, auth);
                    break;
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

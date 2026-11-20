using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinMorph.Core;

namespace WinMorph
{
    /// <summary>
    /// The WinMorph app: a stage. The chosen look stands behind a velvet curtain that gathers and opens
    /// when you pick another; applying lights the cue list step by step; the changes page is the cue sheet
    /// of everything WinMorph changed and the proof after a rollback.
    /// </summary>
    public partial class MainWindow : Window
    {
        static bool De => Loc.De;
        static string L(string de, string en) => De ? de : en;
        static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static SolidColorBrush B(uint rgb, byte a = 255) => B(C(rgb, a));
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        Brush R(string key) => (Brush)FindResource(key);

        static readonly bool Calm = !SystemParameters.ClientAreaAnimation;   // Windows "show animations" off
        static Duration Ms(double ms) => TimeSpan.FromMilliseconds(Calm ? Math.Min(ms, 120) : ms);
        static readonly IEasingFunction Out = new QuinticEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction OutSoft = new CubicEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction In = new CubicEase { EasingMode = EasingMode.EaseIn };

        static void Anim(IAnimatable el, DependencyProperty p, double to, double ms, IEasingFunction ease = null, double? from = null, double delay = 0, Action done = null)
        {
            var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(Calm ? 0 : delay) };
            if (from.HasValue) a.From = from;
            if (done != null) a.Completed += (s, e) => done();
            el.BeginAnimation(p, a);
        }

        Preset selected;
        bool busy;

        public MainWindow()
        {
            InitializeComponent();
            Shell.RenderGuard.Watch(this);
            try
            {
                Logo.Source = new IconBitmapDecoder(new Uri("pack://application:,,,/WinMorph.ico"), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.OrderByDescending(f => f.PixelWidth).First();
                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/WinMorph.ico"));
            }
            catch { }
            selected = Presets.Find(Shell.ShellControl.CurrentPreset ?? "") ?? Presets.All[0];
            BuildStage();
            Shell.AppCatalog.Loaded += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                catalogReady = true;
                foreach (var id in provisional) previews.Remove(id);
                if (provisional.Contains(selected.Id)) ShowPreview(selected, curtain: false);
                provisional.Clear();
            }));
            Shell.AppCatalog.LoadAsync(Dispatcher);
            Task.Run(() => History.EnsureShortcuts());
            BuildAll(entrance: true);
            SourceInitialized += (s, e) => LightTitleBar();
            SizeChanged += (s, e) => DetailCol.Width = new GridLength(ActualWidth < 1140 ? 350 : 400);
            PreviewKeyDown += OnKey;
            Updater.MarkUiOpen();
            Task.Run(() =>
            {
                Updater.Cleanup();
                try { foreach (var f in Directory.GetFiles(PreviewDir)) if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(7)) File.Delete(f); } catch { }
            });
            CheckUpdate();
        }

        // =====================================================================
        // look colours: every look floods its own colour field
        // =====================================================================
        static Brush Field(Preset p)
        {
            Brush b;
            switch (p.Id)
            {
                case "win7": b = new LinearGradientBrush(new GradientStopCollection { new GradientStop(C(0x1452B0), 0), new GradientStop(C(0x1F6FD1), 0.55), new GradientStop(C(0x4FB8EC), 1) }, new Point(0, 0), new Point(1, 1)); break;
                case "zen": b = new LinearGradientBrush(new GradientStopCollection { new GradientStop(C(0x47711F), 0), new GradientStop(C(0x5E8A2E), 0.6), new GradientStop(C(0x86AD4C), 1) }, new Point(0, 0), new Point(1, 1)); break;
                default: b = new LinearGradientBrush(C(0x070A08), C(0x16201A), 45); break;
            }
            b.Freeze();
            return b;
        }
        static Color Hacker => Shell.ShellThemes.HackerColor;
        /// <summary>Text on the look's colour field.</summary>
        static Color OnField(Preset p) => p.Id == "terminal" ? Hacker : C(0xFFFFFF);
        /// <summary>The look's strongest colour for buttons (white text on it, 4.5:1 or better).</summary>
        static Color Act(Preset p) => p.Id == "win7" ? C(0x1A5FC0) : p.Id == "zen" ? C(0x4A7322) : C(0x0B0F0C);
        static Color ActText(Preset p) => p.Id == "terminal" ? Hacker : C(0xFFFFFF);

        // =====================================================================
        // building
        // =====================================================================
        void BuildAll(bool entrance = false)
        {
            TLooks.Text = L("Looks", "Looks");
            TChanges.Text = L("Änderungen", "Changes");
            LangDe.Opacity = De ? 1 : 0.55; LangEn.Opacity = De ? 0.55 : 1;
            LangDe.Background = De ? R("Paper") : Brushes.Transparent; LangEn.Background = De ? Brushes.Transparent : R("Paper");
            ShowUpdate();
            BuildTiles(entrance);
            BuildDetail(entrance);
            ShowPreview(selected, curtain: !entrance, entrance: entrance);
            RefreshCount();
            if (ChangesPage.Visibility == Visibility.Visible) BuildChanges();
            Dispatcher.BeginInvoke(new Action(() => MoveTabLine(false)), DispatcherPriority.Loaded);
        }

        readonly Dictionary<string, RadioButton> tiles = new Dictionary<string, RadioButton>();

        void BuildTiles(bool entrance)
        {
            TileRow.Children.Clear(); tiles.Clear();
            var active = Shell.ShellControl.CurrentPreset;
            int n = 0;
            foreach (var p in Presets.All)
            {
                var fg = OnField(p);
                var g = new Grid { Margin = new Thickness(20, 14, 18, 14) };
                var name = new Viewbox { StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 64, 0),
                    Child = new TextBlock { Text = p.Name.ToUpperInvariant(), FontFamily = (FontFamily)FindResource("Display"), FontWeight = FontWeights.ExtraBold, FontSize = 36, Foreground = B(fg) } };
                var tag = new TextBlock { Text = p.Tag(De), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B(A(fg, 220)), VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 26, 0) };
                var key = new TextBlock { Text = (n + 1).ToString(), FontFamily = (FontFamily)FindResource("Mono"), FontSize = 12, Foreground = B(A(fg, 150)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                g.Children.Add(name); g.Children.Add(tag); g.Children.Add(key);
                if (p.Id == active) g.Children.Add(ActivePill());
                var lift = new TranslateTransform();
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Tile"), GroupName = "look", Background = Field(p), BorderBrush = p.Id == "terminal" ? B(Hacker) : R("Ink"), Content = g, IsChecked = p == selected,
                    Margin = new Thickness(n == 0 ? 0 : 7, 0, n == Presets.All.Count - 1 ? 0 : 7, 0), RenderTransform = lift,
                    Opacity = p == selected ? 1 : 0.86, ToolTip = L("Taste ", "Key ") + (n + 1),
                };
                rb.Effect = new DropShadowEffect { Color = Act(p), BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = p == selected ? 0.35 : 0 };
                if (p == selected) lift.Y = -6;
                var look = p;
                rb.Checked += (s, e) => Select(look);
                rb.MouseEnter += (s, e) => { if (look != selected) Anim(rb, OpacityProperty, 1, 160, OutSoft); };
                rb.MouseLeave += (s, e) => { if (look != selected) Anim(rb, OpacityProperty, 0.86, 220, OutSoft); };
                tiles[p.Id] = rb;
                TileRow.Children.Add(rb);
                if (entrance)
                {
                    var rise = new TranslateTransform(0, 22);
                    g.RenderTransform = rise; g.Opacity = 0;
                    Anim(rise, TranslateTransform.YProperty, 0, 620, Out, delay: 120 + n * 70);
                    Anim(g, OpacityProperty, 1, 380, OutSoft, delay: 120 + n * 70);
                }
                n++;
            }
        }

        FrameworkElement ActivePill()
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = R("Go"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (!Calm) dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(new TextBlock { Text = L("AKTIV", "ACTIVE"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = R("Ink") });
            return new Border { Background = R("Paper"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 9, 3), Child = sp, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        }

        void Select(Preset p)
        {
            if (p == selected) return;
            if (busy) { tiles[selected.Id].IsChecked = true; return; }
            selected = p;
            foreach (var kv in tiles)
            {
                bool on = kv.Key == p.Id;
                var rb = kv.Value;
                Anim((TranslateTransform)rb.RenderTransform, TranslateTransform.YProperty, on ? -6 : 0, on ? 520 : 300, on ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : OutSoft);
                Anim(rb, OpacityProperty, on ? 1 : 0.86, 220, OutSoft);
                if (rb.Effect is DropShadowEffect sh) Anim(sh, DropShadowEffect.OpacityProperty, on ? 0.35 : 0, 300, OutSoft);
                if (on && rb.IsChecked != true) rb.IsChecked = true;
            }
            BuildDetail(false);
            ShowPreview(p, curtain: true);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (Overlay.Visibility == Visibility.Visible) { if (e.Key == Key.Escape && !working) { CloseDialog(); e.Handled = true; } return; }
            if (LooksPage.Visibility != Visibility.Visible || busy || e.OriginalSource is TextBox) return;
            int i = e.Key >= Key.D1 && e.Key <= Key.D3 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad3 ? e.Key - Key.NumPad1 : -1;
            if (i >= 0 && i < Presets.All.Count) { Select(Presets.All[i]); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Apply_Click(null, null); e.Handled = true; }
        }

        // =====================================================================
        // the stage and its curtain
        // =====================================================================
        Image stageImg;
        ScaleTransform curtainL, curtainR;
        Grid stageLoading;

        void BuildStage()
        {
            var inner = StageInner;
            inner.Children.Clear();
            var clip = new Grid { ClipToBounds = true };
            inner.SizeChanged += (s, e) => clip.Clip = new RectangleGeometry(new Rect(0, 0, inner.ActualWidth, inner.ActualHeight), 10, 10);
            stageImg = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(stageImg, BitmapScalingMode.HighQuality);
            clip.Children.Add(new Border { Background = B(0x1C1D22) });
            clip.Children.Add(stageImg);
            stageLoading = new Grid { Visibility = Visibility.Collapsed };
            stageLoading.Children.Add(new TextBlock { Text = L("Bühne wird gebaut …", "Setting the stage …"), Foreground = B(0xFFFFFF, 170), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            clip.Children.Add(stageLoading);

            // the curtain: two velvet halves that gather to the sides, a valance with a gold edge
            var curtain = new Grid { IsHitTestVisible = false };
            curtain.ColumnDefinitions.Add(new ColumnDefinition()); curtain.ColumnDefinitions.Add(new ColumnDefinition());
            curtainL = new ScaleTransform(1, 1); curtainR = new ScaleTransform(1, 1);
            curtain.Children.Add(new Rectangle { Fill = Velvet(), RenderTransformOrigin = new Point(0, 0), RenderTransform = curtainL, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5 } });
            var right = new Rectangle { Fill = Velvet(), RenderTransformOrigin = new Point(1, 0), RenderTransform = curtainR, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5 } };
            Grid.SetColumn(right, 1);
            curtain.Children.Add(right);
            clip.Children.Add(curtain);
            var valance = new Grid { Height = 26, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            valance.Children.Add(new Border { Background = Velvet(dark: true) });
            valance.Children.Add(new Border { Height = 2.5, VerticalAlignment = VerticalAlignment.Bottom, Background = new LinearGradientBrush(C(0xC99A3A), C(0xF2D27A), 0) { SpreadMethod = GradientSpreadMethod.Reflect, EndPoint = new Point(0.05, 0) } });
            clip.Children.Add(valance);
            inner.Children.Add(clip);
        }

        /// <summary>Velvet with vertical folds: a repeating light-dark gradient.</summary>
        static Brush Velvet(bool dark = false)
        {
            var folds = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.09, 0), SpreadMethod = GradientSpreadMethod.Repeat };
            uint a = dark ? 0x5A0820u : 0x7A0C2Fu, b = dark ? 0x86112Fu : 0xB01A47u, c = dark ? 0x4A061Au : 0x630925u;
            folds.GradientStops.Add(new GradientStop(C(a), 0)); folds.GradientStops.Add(new GradientStop(C(b), 0.35));
            folds.GradientStops.Add(new GradientStop(C(a), 0.7)); folds.GradientStops.Add(new GradientStop(C(c), 1));
            folds.Freeze();
            return folds;
        }

        int stageVersion;

        /// <summary>Shows the look on the stage. With curtain: it gathers in, the scenery changes, it opens again.</summary>
        void ShowPreview(Preset p, bool curtain, bool entrance = false)
        {
            int v = ++stageVersion;
            void Swap()
            {
                if (v != stageVersion) return;
                var img = PreviewOf(p);
                if (img != null) stageImg.Source = img;
                stageLoading.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
                Open(entrance ? 280 : 60);
            }
            if (!curtain && !entrance) { stageImg.Source = PreviewOf(p) ?? stageImg.Source; return; }
            if (entrance) { curtainL.ScaleX = curtainR.ScaleX = 1; Dispatcher.BeginInvoke(new Action(Swap), DispatcherPriority.Background); return; }
            // close (quick, accelerating), then the change happens behind the curtain
            Anim(curtainL, ScaleTransform.ScaleXProperty, 1, 260, In);
            Anim(curtainR, ScaleTransform.ScaleXProperty, 1, 260, In, done: () => Dispatcher.BeginInvoke(new Action(Swap), DispatcherPriority.Background));
        }

        void Open(double delay)
        {
            // opening is heavy and decisive: velvet gathers to the sides and settles
            var ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 };
            Anim(curtainL, ScaleTransform.ScaleXProperty, 0.07, 900, ease, delay: delay);
            Anim(curtainR, ScaleTransform.ScaleXProperty, 0.07, 900, ease, delay: delay + 40);
        }

        // ---- preview pictures: drawn once, kept on disk for a day ----
        readonly Dictionary<string, BitmapSource> previews = new Dictionary<string, BitmapSource>();
        readonly HashSet<string> provisional = new HashSet<string>();
        bool catalogReady;

        BitmapSource PreviewOf(Preset p)
        {
            if (previews.TryGetValue(p.Id, out var cached)) return cached;
            var file = PreviewFile(p);
            var hit = LoadPreview(file);
            if (hit != null) return previews[p.Id] = hit;
            var th = Shell.ShellThemes.For(p.Id);
            BitmapSource bmp;
            try
            {
                if (th == null || !catalogReady) { provisional.Add(p.Id); bmp = WallpaperArt.Render(p, 1280, 720, false); }
                else
                {
                    var host = new Shell.ShellHost(th, preview: true);
                    try { bmp = host.RenderPreview(p, 1440, 810, true, 0.85); }
                    finally { host.DisposePreview(); }
                    var frozen = bmp;
                    if (!frozen.IsFrozen) frozen.Freeze();
                    Task.Run(() => { try { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(frozen)); using (var fs = File.Create(file + ".tmp")) enc.Save(fs); File.Copy(file + ".tmp", file, true); File.Delete(file + ".tmp"); } catch { } });
                }
            }
            catch { return null; }
            if (!bmp.IsFrozen) bmp.Freeze();
            return previews[p.Id] = bmp;
        }

        static string PreviewDir { get { var d = System.IO.Path.Combine(Backup.DataDir, "previews"); Directory.CreateDirectory(d); return d; } }

        static string PreviewFile(Preset p)
        {
            var c = Hacker;
            string variant = p.Id == "terminal" ? c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2") : "";
            return System.IO.Path.Combine(PreviewDir, p.Id + "-stage-" + (De ? "de" : "en") + "-" + variant + "-" + Updater.Version + ".png");
        }

        static BitmapSource LoadPreview(string file)
        {
            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists || fi.Length < 1000 || DateTime.UtcNow - fi.LastWriteTimeUtc > TimeSpan.FromDays(1)) return null;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file); bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // =====================================================================
        // detail card: name, what is applied (the cue list), the action
        // =====================================================================
        readonly List<(Ellipse dot, ScaleTransform pop, string step)> cues = new List<(Ellipse, ScaleTransform, string)>();
        Button applyBtn;
        TextBlock applyText, status;

        static (string step, string de, string en, string subDe, string subEn)[] Parts(Preset p)
        {
            switch (p.Id)
            {
                case "win7": return new[]
                {
                    ("shell", "Taskleiste & Startmenü", "Taskbar & Start menu", "Glasleiste mit Startkugel, zweispaltiges Startmenü mit Suche", "Glass bar with start orb, two-column Start menu with search"),
                    ("shell", "Alt+Tab", "Alt+Tab", "Aero-Glas mit Live-Vorschau aller Fenster", "Aero glass with live previews of every window"),
                    ("shell", "Fensterrahmen", "Window frames", "Blaue Glas-Titelleisten", "Blue glass title bars"),
                    ("desktop", "Desktop", "Desktop", "Aero-Hintergrund mit Lichtbändern", "Aero wallpaper with light ribbons"),
                    ("lock", "Sperrbildschirm", "Lock screen", "Aero-Anmeldung, auch bei Win+L", "Aero sign-in, also on Win+L"),
                };
                case "zen": return new[]
                {
                    ("shell", "Taskleiste & Startmenü", "Taskbar & Start menu", "Schwebende helle Leiste mit Fokus-Timer, Atemübung und Regen", "Floating light bar with focus timer, breathing and rain"),
                    ("shell", "Alt+Tab", "Alt+Tab", "Shōji-Schiebetüren und Ema-Täfelchen", "Shoji sliding doors and ema plaques"),
                    ("shell", "Fensterrahmen", "Window frames", "Warme Papier-Titelleisten", "Warm paper title bars"),
                    ("desktop", "Desktop", "Desktop", "Berglandschaft, Licht folgt der Tageszeit", "Mountain landscape lit by the time of day"),
                    ("lock", "Sperrbildschirm", "Lock screen", "Stiller Sperrbildschirm mit Uhr", "Quiet lock screen with a clock"),
                };
                default: return new[]
                {
                    ("shell", "Statuszeile & Terminal", "Status line & terminal", "Netz, CPU, RAM, Akku, Uhr; Windows-Taste öffnet das Terminal", "Network, CPU, RAM, battery, clock; the Windows key opens the terminal"),
                    ("shell", "Alt+Tab", "Alt+Tab", "Prozess-Tabelle mit PID, RAM und Live-Vorschau", "Process table with PID, memory and live preview"),
                    ("shell", "Fensterrahmen", "Window frames", "Dunkle Rahmen in deiner Farbe", "Dark frames in your colour"),
                    ("desktop", "Desktop", "Desktop", "Live-Systeminfos neben deinen Symbolen", "Live system info next to your icons"),
                    ("lock", "Sperrbildschirm", "Lock screen", "Terminal-Sperrbildschirm, auch bei Win+L", "Terminal lock screen, also on Win+L"),
                };
            }
        }

        void BuildDetail(bool entrance)
        {
            var p = selected;
            DetailHost.Children.Clear();
            cues.Clear();
            var dock = new DockPanel { LastChildFill = true };

            // colour field header
            var head = new Border { Background = Field(p), CornerRadius = new CornerRadius(19, 19, 0, 0), Padding = new Thickness(28, 22, 28, 20) };
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock { Text = p.Name.ToUpperInvariant(), FontFamily = (FontFamily)FindResource("Display"), FontWeight = FontWeights.ExtraBold, FontSize = 50, Foreground = B(OnField(p)), Margin = new Thickness(0, -6, 0, 0) });
            hs.Children.Add(new TextBlock { Text = p.Tag(De), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B(OnField(p)), Opacity = 0.85 });
            head.Child = hs;
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);

            // action area at the bottom
            var foot = new StackPanel { Margin = new Thickness(28, 6, 28, 24) };
            applyText = new TextBlock { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = B(ActText(p)) };
            applyBtn = new Button { Style = (Style)FindResource("Btn"), Background = B(Act(p)), Content = applyText, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (p.Id == "terminal") applyBtn.BorderBrush = B(Hacker);
            applyBtn.Click += Apply_Click;
            bool active = Shell.ShellControl.CurrentPreset == p.Id;
            applyText.Text = active ? L(p.Name + " neu anwenden", "Apply " + p.Name + " again") : L(p.Name + " anwenden", "Apply " + p.Name);
            foot.Children.Add(applyBtn);
            status = new TextBlock { FontSize = 13, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 0, 0), LineHeight = 19 };
            status.Text = L("Ändert keine Windows-Einstellungen. Unter „Änderungen“ lässt sich alles vollständig zurücksetzen.", "Changes no Windows settings. Everything can be fully reset under “Changes”.");
            foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);

            // body: description, cue list, colour
            var body = new StackPanel { Margin = new Thickness(28, 20, 28, 8) };
            body.Children.Add(new TextBlock { Text = p.Desc(De), FontSize = 14, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
            body.Children.Add(new TextBlock { Text = L("WIRD ANGEWENDET", "WHAT IS APPLIED"), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = R("Faint"), Margin = new Thickness(0, 20, 0, 10) });
            foreach (var part in Parts(p))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 11) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var pop = new ScaleTransform(1, 1);
                var dot = new Ellipse { Width = 10, Height = 10, Fill = B(0xD5D7DD), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = pop };
                row.Children.Add(dot);
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = De ? part.de : part.en, FontSize = 14, FontWeight = FontWeights.SemiBold });
                t.Children.Add(new TextBlock { Text = De ? part.subDe : part.subEn, FontSize = 12.5, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                body.Children.Add(row);
                cues.Add((dot, pop, part.step));
            }
            if (p.Id == "terminal") body.Children.Add(ColorPicker());
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body, Focusable = false };
            dock.Children.Add(scroll);
            DetailHost.Children.Add(dock);

            // the card re-composes: header colour first, then the text rises
            var rise = new TranslateTransform(0, entrance ? 26 : 10);
            body.RenderTransform = rise; body.Opacity = 0;
            Anim(rise, TranslateTransform.YProperty, 0, 520, Out, delay: entrance ? 240 : 40);
            Anim(body, OpacityProperty, 1, 340, OutSoft, delay: entrance ? 240 : 40);
            if (entrance) { var slide = new TranslateTransform(30, 0); DetailCard.RenderTransform = slide; DetailCard.Opacity = 0; Anim(slide, TranslateTransform.XProperty, 0, 700, Out, delay: 60); Anim(DetailCard, OpacityProperty, 1, 420, OutSoft, delay: 60); }
            SetBusyUi(busy);
        }

        FrameworkElement ColorPicker()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = L("FARBE", "COLOUR"), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = R("Faint"), Margin = new Thickness(0, 10, 0, 10) });
            var row = new WrapPanel();
            var cur = Hacker; bool known = false;
            foreach (var (de, en, rgb) in Shell.ShellThemes.HackerColors)
            {
                var c = C(rgb); bool on = c == cur; known |= on;
                row.Children.Add(Swatch(c, De ? de : en, on));
            }
            var own = new Button { Style = (Style)FindResource("Outline"), Height = 34, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 8), Content = (known ? "" : "● ") + L("Eigene …", "Custom …"), ToolTip = L("Eigene Farbe wählen", "Pick your own colour") };
            if (!known) own.Foreground = B(Applier.Mix(cur, Colors.Black, 0.35));
            own.Click += (s, e) =>
            {
                using (var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B) })
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetHackerColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
            };
            row.Children.Add(own);
            sp.Children.Add(row);
            return sp;
        }

        Button Swatch(Color c, string name, bool on)
        {
            var b = new Button { Width = 34, Height = 34, Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, ToolTip = name, FocusVisualStyle = (Style)FindResource("Focus") };
            var ring = new Border { CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2.5), BorderBrush = on ? R("Ink") : Brushes.Transparent, Padding = new Thickness(3) };
            ring.Child = new Border { CornerRadius = new CornerRadius(12), Background = new LinearGradientBrush(C(0x0B0F0C), c, 45) };
            b.Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            b.Content = ring;
            b.Click += (s, e) => SetHackerColor(c);
            return b;
        }

        void SetHackerColor(Color c)
        {
            if (busy) return;
            if (c.R + c.G + c.B < 120) c = Applier.Mix(c, Colors.White, 0.35);   // too dark to read on black
            Shell.ShellThemes.HackerColor = c;
            previews.Remove("terminal");
            BuildTiles(false);
            BuildDetail(false);
            ShowPreview(selected, curtain: true);
            // active: the whole look follows the new colour at once (bar, frames, desktop, lock picture)
            if (Shell.ShellControl.CurrentPreset == "terminal") Apply_Click(null, null);
            else status.Text = L("Farbe gespeichert.", "Colour saved.");
        }

        // =====================================================================
        // applying: the cue lights go on one after another
        // =====================================================================
        void Cue(string step)
        {
            if (step == "reset") { status.Text = L("Vorherigen Look abbauen …", "Taking down the previous look …"); return; }
            var order = new[] { "desktop", "lock", "shell" };
            int at = Array.IndexOf(order, step);
            foreach (var c in cues)
            {
                int my = Array.IndexOf(order, c.step);
                if (my < at) Light(c, "done");
                else if (my == at) Light(c, "active");
            }
            status.Text = step == "desktop" ? L("Desktop und Hintergrund …", "Desktop and wallpaper …") : step == "lock" ? L("Sperrbildschirm …", "Lock screen …") : L("Taskleiste, Startmenü und Alt+Tab starten …", "Starting taskbar, Start menu and Alt+Tab …");
        }

        void Light((Ellipse dot, ScaleTransform pop, string step) c, string state)
        {
            c.dot.BeginAnimation(OpacityProperty, null);
            c.dot.Opacity = 1;
            switch (state)
            {
                case "active":
                    c.dot.Fill = R("Cue");
                    if (!Calm) c.dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                    break;
                case "done":
                case "fail":
                    c.dot.Fill = R(state == "done" ? "Go" : "Stop");
                    var pop = new DoubleAnimation(1.7, 1, Ms(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.8 } };
                    c.pop.BeginAnimation(ScaleTransform.ScaleXProperty, pop); c.pop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                    break;
                default:
                    c.dot.Fill = B(0xD5D7DD);
                    break;
            }
        }

        void SetBusyUi(bool on)
        {
            if (applyBtn != null) applyBtn.IsEnabled = !on;
            foreach (var t in tiles.Values) t.IsHitTestVisible = !on;
            TabChanges.IsEnabled = !on; TabLooks.IsEnabled = !on;
            LangDe.IsEnabled = LangEn.IsEnabled = !on;
        }

        async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            busy = true;
            SetBusyUi(true);
            var p = selected;
            foreach (var c in cues) Light(c, "idle");
            applyText.Text = L("Wird angewendet …", "Applying …");
            status.Text = L("Bilder für den Look vorbereiten …", "Preparing the look's pictures …");
            var clock = Stopwatch.StartNew();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);   // let the button state paint first
            Applier ap = null;
            string error = null;
            try
            {
                var o = ApplyOptions.For(p);
                var bk = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
                ap = new Applier(bk, De);
                var applier = ap;
                await Task.Run(() => applier.Apply(p, o, step => Dispatcher.BeginInvoke(new Action(() => Cue(step)))));
                await LockSetupOnce();
            }
            catch (Exception ex) { error = ex.Message; }
            busy = false;
            bool ok = error == null && ap != null && ap.Problems.Count == 0;
            // finale: the remaining lights go green one after another
            int k = 0;
            foreach (var c in cues)
            {
                var cc = c;
                var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Calm ? 1 : 1 + k++ * 70) };
                tm.Tick += (s2, e2) => { tm.Stop(); Light(cc, ok || cc.step != "shell" ? "done" : "fail"); };
                tm.Start();
            }
            BuildTiles(false);
            if (selected == p)
            {
                applyText.Text = L(p.Name + " neu anwenden", "Apply " + p.Name + " again");
                status.Text = error != null ? error
                    : ok ? string.Format(L("{0} ist aktiv. Fertig in {1:0.0} s.", "{0} is active. Done in {1:0.0} s."), p.Name, clock.Elapsed.TotalSeconds)
                    : L("Angewendet, mit Problemen: ", "Applied with problems: ") + string.Join(", ", ap.Problems.Take(3)) + (ap.Problems.Count > 3 ? " …" : "");
            }
            SetBusyUi(false);
            RefreshCount();
            Activate();
        }

        /// <summary>
        /// One admin prompt, once: lets Win+L show the look's lock screen (Windows would lock before WinMorph
        /// could react) and removes an all-users picture of an older version. Declining is remembered.
        /// </summary>
        async Task LockSetupOnce()
        {
            bool needGuard = !LockGuard.Ready && !(Reg.Get(Shell.ShellControl.SettingsKey, "LockSetupDeclined") is int);
            bool stale = Applier.LockScreenOverrideIsOurs();
            if (!needGuard && !stale) return;
            status.Text = L("Einmalige Freigabe für den Sperrbildschirm bei Win+L …", "One-time permission for the lock screen on Win+L …");
            string args = "--lock-setup " + (needGuard ? LockGuard.UserSid ?? "-" : "-") + " \"" + Backup.DefaultFile + "\"";
            bool ok = await Task.Run(() => Tools.RunElevated(args));
            if (!ok && needGuard) using (var k = Reg.Create(Shell.ShellControl.SettingsKey)) k.SetValue("LockSetupDeclined", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }

        // =====================================================================
        // pages
        // =====================================================================
        void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (LooksPage == null || ChangesPage == null) return;
            bool changes = TabChanges.IsChecked == true;
            var show = changes ? (FrameworkElement)ChangesPage : LooksPage;
            var hide = changes ? (FrameworkElement)LooksPage : ChangesPage;
            if (changes) BuildChanges();
            hide.Visibility = Visibility.Collapsed;
            show.Visibility = Visibility.Visible;
            var slide = new TranslateTransform(changes ? 26 : -26, 0);
            show.RenderTransform = slide;
            Anim(slide, TranslateTransform.XProperty, 0, 420, Out);
            Anim(show, OpacityProperty, 1, 280, OutSoft, from: 0);
            MoveTabLine(true);
        }

        void MoveTabLine(bool anim)
        {
            var tab = TabChanges.IsChecked == true ? (FrameworkElement)TabChanges : TabLooks;
            if (!tab.IsLoaded) return;
            var x = tab.TransformToAncestor((Visual)TabLine.Parent).Transform(new Point(0, 0)).X + 4;
            var tt = (TranslateTransform)TabLine.RenderTransform;
            double w = Math.Max(10, tab.ActualWidth - 8);
            if (anim) { Anim(tt, TranslateTransform.XProperty, x, 380, Out); Anim(TabLine, WidthProperty, w, 380, Out); }
            else { tt.BeginAnimation(TranslateTransform.XProperty, null); TabLine.BeginAnimation(WidthProperty, null); tt.X = x; TabLine.Width = w; }
        }

        int lastCount = -1;
        void RefreshCount()
        {
            int n;
            try { n = Changes.Read(new Backup(Backup.DefaultFile)).Count(c => c.Active); } catch { n = 0; }
            ChangeCount.Text = n.ToString();
            ChangeBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (n != lastCount && lastCount >= 0 && n > 0)
            {
                var st = (ScaleTransform)ChangeBadge.RenderTransform;
                var pop = new DoubleAnimation(1.5, 1, Ms(480)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.9 } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, pop); st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            lastCount = n;
            Dispatcher.BeginInvoke(new Action(() => MoveTabLine(false)), DispatcherPriority.Loaded);
        }

        // =====================================================================
        // changes: the cue sheet
        // =====================================================================
        RestoreResult lastRestore;
        bool lastWasUndo;

        void BuildChanges()
        {
            ChangesPage.Children.Clear();
            var b = new Backup(Backup.DefaultFile);
            List<Change> list;
            try { list = Changes.Read(b); } catch { list = new List<Change>(); }
            int active = list.Count(c => c.Active);
            bool anything = b.HasData || Shell.ShellControl.TaskbarTaken || Shell.ShellControl.CurrentPreset != null || active > 0;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // header: title, summary, the two actions
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var undo = new Button { Style = (Style)FindResource("Outline"), Content = L("Letzten Schritt rückgängig", "Undo last step"), Margin = new Thickness(0, 0, 12, 0), IsEnabled = History.CanUndo };
            undo.Click += async (s, e) => await RunRestore(undo: true);
            var restore = new Button { Style = (Style)FindResource("Btn"), Height = 44, FontSize = 14, Content = L("Alles zurücksetzen", "Reset everything"), IsEnabled = anything };
            restore.Click += (s, e) => Confirm(
                L("Alles auf das Original zurücksetzen?", "Reset everything to the original?"),
                L("Taskleiste, Startmenü, Alt+Tab, Fensterrahmen, Hintergrund und Sperrbildschirm werden wieder wie vor WinMorph. Danach wird jeder Wert einzeln geprüft.", "Taskbar, Start menu, Alt+Tab, window frames, wallpaper and lock screen go back to how they were before WinMorph. Every value is checked afterwards."),
                L("Zurücksetzen", "Reset"), async () => await RunRestore(undo: false));
            actions.Children.Add(undo); actions.Children.Add(restore);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var ht = new StackPanel();
            ht.Children.Add(new TextBlock { Text = L("ÄNDERUNGEN", "CHANGES"), FontFamily = (FontFamily)FindResource("Display"), FontWeight = FontWeights.ExtraBold, FontSize = 46, Margin = new Thickness(0, -6, 0, 0) });
            string sum = !anything ? L("Nichts geändert: Windows ist im Originalzustand.", "Nothing changed: Windows is in its original state.")
                : active == 0 ? L("Alle Werte entsprechen dem Original.", "Every value matches the original.")
                : string.Format(L("{0} Bereiche weichen vom Original ab", "{0} areas differ from the original"), active) + (b.Created != null ? string.Format(L(" · Original gesichert am {0:dd.MM.yyyy, HH:mm}", " · original saved on {0:yyyy-MM-dd, HH:mm}"), b.Created.Value) : "");
            ht.Children.Add(new TextBlock { Text = sum, FontSize = 14, Foreground = R("Sub") });
            head.Children.Add(ht);
            grid.Children.Add(head);

            // the proof after a rollback
            if (lastRestore != null)
            {
                var banner = Banner(lastRestore, lastWasUndo);
                Grid.SetRow(banner, 1);
                grid.Children.Add(banner);
            }

            // the sheet
            var sheet = new Border { Background = R("Paper"), CornerRadius = new CornerRadius(18), BorderBrush = R("Line"), BorderThickness = new Thickness(1), Padding = new Thickness(0, 6, 0, 6) };
            Grid.SetRow(sheet, 2);
            if (!anything && lastRestore == null)
            {
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                empty.Children.Add(new TextBlock { Text = L("Noch kein Look angewendet.", "No look applied yet."), FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                empty.Children.Add(new TextBlock { Text = L("Sobald du einen Look anwendest, steht hier jede einzelne Änderung mit dem Originalwert.", "Once you apply a look, every single change is listed here with its original value."), FontSize = 14, Foreground = R("Sub"), Margin = new Thickness(0, 6, 0, 18), HorizontalAlignment = HorizontalAlignment.Center });
                var go = new Button { Style = (Style)FindResource("Btn"), Height = 44, FontSize = 14, Content = L("Look auswählen", "Choose a look"), HorizontalAlignment = HorizontalAlignment.Center };
                go.Click += (s, e) => TabLooks.IsChecked = true;
                empty.Children.Add(go);
                sheet.Child = empty;
            }
            else
            {
                var rows = new StackPanel();
                rows.Children.Add(SheetRow("Q", L("BEREICH", "AREA"), L("VORHER (ORIGINAL)", "BEFORE (ORIGINAL)"), L("JETZT", "NOW"), null, true, null));
                int q = 1;
                foreach (var c in list.OrderByDescending(x => x.Active))
                {
                    var row = SheetRow("Q" + q, c.Title, c.Before, c.Now, c.Active, false, c.Group);
                    rows.Children.Add(row);
                    var rise = new TranslateTransform(0, 12);
                    row.RenderTransform = rise; row.Opacity = 0;
                    Anim(rise, TranslateTransform.YProperty, 0, 460, Out, delay: 40 + q * 34);
                    Anim(row, OpacityProperty, 1, 300, OutSoft, delay: 40 + q * 34);
                    q++;
                }
                sheet.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows, Focusable = false };
            }
            grid.Children.Add(sheet);

            // footer: updates, version, website
            var foot = new DockPanel { Margin = new Thickness(4, 16, 4, 0) };
            var auto = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = Updater.Auto, Content = new TextBlock { Text = L("Updates automatisch installieren", "Install updates automatically"), FontSize = 13, Foreground = R("Sub") } };
            auto.Click += (s, e) => Updater.Auto = auto.IsChecked == true;
            DockPanel.SetDock(auto, Dock.Left);
            foot.Children.Add(auto);
            var site = new TextBlock { FontSize = 13, Foreground = R("Sub"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = Updater.Site };
            site.Inlines.Add(new System.Windows.Documents.Run("WinMorph " + Updater.Version + "   "));
            site.Inlines.Add(new System.Windows.Documents.Run("Website ↗") { Foreground = R("Samt"), FontWeight = FontWeights.SemiBold });
            site.MouseLeftButtonUp += (s, e) => OpenSite();
            foot.Children.Add(site);
            Grid.SetRow(foot, 3);
            grid.Children.Add(foot);

            ChangesPage.Children.Add(grid);
        }

        FrameworkElement SheetRow(string q, string title, string before, string now, bool? active, bool header, string group)
        {
            var g = new Grid { Margin = new Thickness(22, 0, 22, 0), MinHeight = header ? 38 : 52 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            TextBlock T(string s, int col, double size, bool mono = false, Brush fg = null, FontWeight? w = null)
            {
                var t = new TextBlock { Text = s ?? "", FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 14, 0), Foreground = fg ?? R("Ink"), FontWeight = w ?? FontWeights.Normal };
                if (!header && !string.IsNullOrEmpty(s)) t.ToolTip = s;
                if (mono) t.FontFamily = (FontFamily)FindResource("Mono");
                Grid.SetColumn(t, col);
                g.Children.Add(t);
                return t;
            }
            if (header)
            {
                foreach (var (s, col) in new[] { (q, 0), (title, 1), (before, 2), (now, 3), ("STATUS", 4) })
                    T(s, col, 11, false, R("Faint"), FontWeights.Bold);
                return new Border { Child = g, BorderBrush = R("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
            }
            var chip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            chip.Children.Add(new Border { Width = 4, Height = 22, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0), Background = GroupBrush(group) });
            chip.Children.Add(new TextBlock { Text = q, FontFamily = (FontFamily)FindResource("Mono"), FontSize = 13, Foreground = R("Faint"), VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(chip);
            T(title, 1, 14, false, null, FontWeights.SemiBold);
            T(before, 2, 13, true, R("Sub"));
            T(now, 3, 13, true, active == true ? R("Ink") : R("Sub"));
            var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Background = active == true ? R("SamtSoft") : R("GoSoft") };
            pill.Child = new TextBlock { Text = active == true ? L("● geändert", "● changed") : L("✓ Original", "✓ original"), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = active == true ? R("Samt") : R("GoInk") };
            Grid.SetColumn(pill, 4);
            g.Children.Add(pill);
            return new Border { Child = g, BorderBrush = R("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        Brush GroupBrush(string group)
        {
            switch (group)
            {
                case "shell": return R("Samt");
                case "desktop": return B(0x1F6FD1);
                case "lock": return B(0x5E8A2E);
                default: return B(0xA7AAB2);
            }
        }

        FrameworkElement Banner(RestoreResult r, bool undo)
        {
            bool ok = undo ? r.Complete : r.Complete && r.Different == 0;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 16, 22, 16), Background = ok ? R("GoSoft") : B(0xFDE7E8), BorderBrush = ok ? R("Go") : R("Stop"), BorderThickness = new Thickness(1.5) };
            var dock = new DockPanel();
            // a check (or a cross) that draws itself
            var mark = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(ok ? "M4,15 L11,22 L25,7" : "M6,6 L22,22 M22,6 L6,22"), Stroke = ok ? R("Go") : R("Stop"), StrokeThickness = 3.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, StrokeDashArray = new DoubleCollection { 14, 14 }, StrokeDashOffset = 14,
            };
            Anim(mark, Shape.StrokeDashOffsetProperty, 0, 640, OutSoft, delay: 160);
            DockPanel.SetDock(mark, Dock.Left);
            dock.Children.Add(mark);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string title = undo ? (ok ? L("Der vorherige Stand ist zurück.", "The previous state is back.") : L("Rückgängig unvollständig.", "Undo incomplete."))
                : ok ? L("Zurückgesetzt und geprüft: Windows ist wie vor WinMorph.", "Reset and checked: Windows is as it was before WinMorph.")
                : L("Noch nicht alles zurückgesetzt.", "Not everything is reset yet.");
            t.Children.Add(new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = ok ? R("GoInk") : R("Stop") });
            string sub = !ok ? string.Join(" · ", r.Problems.Take(4)) + " " + L("Das Backup bleibt erhalten, einfach erneut versuchen.", "The backup is kept, just try again.")
                : undo ? string.Format(L("{0} Werte wiederhergestellt.", "{0} values restored."), r.Restored)
                : string.Format(L("{0} Werte einzeln mit dem Original verglichen, alle gleich. Taskleiste sichtbar, Win+L wieder bei Windows.", "{0} values compared with the original one by one, all equal. Taskbar visible, Win+L back with Windows."), r.Checked);
            t.Children.Add(new TextBlock { Text = sub, FontSize = 13, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            dock.Children.Add(t);
            card.Child = dock;
            g.Children.Add(card);
            var drop = new TranslateTransform(0, -14);
            g.RenderTransform = drop; g.Opacity = 0;
            Anim(drop, TranslateTransform.YProperty, 0, 560, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
            Anim(g, OpacityProperty, 1, 260, OutSoft);
            return g;
        }

        async Task RunRestore(bool undo)
        {
            if (busy) return;
            busy = true;
            SetBusyUi(true);
            ShowWorking(undo ? L("Vorheriger Stand wird wiederhergestellt …", "Restoring the previous state …") : L("Alles wird zurückgesetzt und geprüft …", "Resetting and checking everything …"));
            RestoreResult r;
            try { r = await Task.Run(() => undo ? History.UndoLast() : History.RestoreOriginal()); }
            catch (Exception ex) { r = new RestoreResult { Failed = 1 }; r.Problems.Add(ex.Message); }
            HideWorking();
            lastRestore = r ?? new RestoreResult();
            lastWasUndo = undo;
            busy = false;
            SetBusyUi(false);
            selected = Presets.Find(Shell.ShellControl.CurrentPreset ?? "") ?? selected;
            BuildTiles(false);
            BuildDetail(false);
            BuildChanges();
            RefreshCount();
            Activate();
        }

        // =====================================================================
        // dialog and working overlay
        // =====================================================================
        void Confirm(string title, string text, string yes, Func<Task> onYes)
        {
            Overlay.Children.Clear();
            Overlay.Opacity = 1;
            var scrim = new Border { Background = B(0x15161A, 120), Opacity = 0 };
            scrim.MouseLeftButtonUp += (s, e) => CloseDialog();
            Overlay.Children.Add(scrim);
            var card = new Border { Background = R("Paper"), CornerRadius = new CornerRadius(20), Padding = new Thickness(30, 26, 30, 24), Width = 500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 18, Direction = 270, Opacity = 0.3 } };
            var sp = new StackPanel();
            sp.Children.Add(new Border { Height = 5, Width = 54, CornerRadius = new CornerRadius(3), Background = R("Samt"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) });
            sp.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 14, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 8, 0, 22) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var no = new Button { Style = (Style)FindResource("Outline"), Content = L("Abbrechen", "Cancel"), Margin = new Thickness(0, 0, 10, 0) };
            no.Click += (s, e) => CloseDialog();
            var ok = new Button { Style = (Style)FindResource("Btn"), Height = 44, FontSize = 14, Content = yes };
            ok.Click += async (s, e) => { Overlay.Visibility = Visibility.Collapsed; Overlay.Children.Clear(); await onYes(); };
            row.Children.Add(no); row.Children.Add(ok);
            sp.Children.Add(row);
            card.Child = sp;
            var sc = new ScaleTransform(0.94, 0.94);
            card.RenderTransformOrigin = new Point(0.5, 0.5); card.RenderTransform = sc; card.Opacity = 0;
            Overlay.Children.Add(card);
            Overlay.Visibility = Visibility.Visible;
            Anim(scrim, OpacityProperty, 1, 220, OutSoft);
            Anim(card, OpacityProperty, 1, 200, OutSoft);
            var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            Anim(sc, ScaleTransform.ScaleXProperty, 1, 340, spring); Anim(sc, ScaleTransform.ScaleYProperty, 1, 340, spring);
            Dispatcher.BeginInvoke(new Action(() => { no.Focus(); Keyboard.Focus(no); }), DispatcherPriority.Input);
        }

        void CloseDialog()
        {
            if (Overlay.Visibility != Visibility.Visible || working) return;
            foreach (UIElement el in Overlay.Children) Anim(el, OpacityProperty, 0, 150, In);
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Calm ? 1 : 160) };
            tm.Tick += (s, e) => { tm.Stop(); if (!working) { Overlay.Visibility = Visibility.Collapsed; Overlay.Children.Clear(); } };
            tm.Start();
        }

        bool working;
        void ShowWorking(string text)
        {
            working = true;
            Overlay.Children.Clear();
            Overlay.Children.Add(new Border { Background = B(0xF3F4F7, 215) });
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            // three cue lights chase each other while it runs
            var lights = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            for (int i = 0; i < 3; i++)
            {
                var d = new Ellipse { Width = 14, Height = 14, Fill = R("Cue"), Margin = new Thickness(6, 0, 6, 0), Opacity = 0.25 };
                d.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(380)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 190), EasingFunction = new SineEase() });
                lights.Children.Add(d);
            }
            sp.Children.Add(lights);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = L("Das dauert nur ein paar Sekunden.", "This only takes a few seconds."), FontSize = 13, Foreground = R("Sub"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            Overlay.Children.Add(sp);
            Overlay.Visibility = Visibility.Visible;
            Anim(Overlay, OpacityProperty, 1, 200, OutSoft, from: 0);
        }

        void HideWorking() { working = false; Overlay.Visibility = Visibility.Collapsed; Overlay.Children.Clear(); }

        // =====================================================================
        // updates, language, window
        // =====================================================================
        Updater.Release update;
        Task<string> updateFile;

        async void CheckUpdate()
        {
            try
            {
                update = await Task.Run(() => Updater.Check());
                if (update == null) return;
                // the installed copy loads it right away, so "Update" takes only seconds
                if (Updater.Installed) updateFile = Updater.Download(update, null);
                ShowUpdate();
                if (updateFile != null) { await updateFile; ShowUpdate(); }
            }
            catch { ShowUpdate(); }
        }

        void ShowUpdate()
        {
            UpdBtn.Visibility = update == null ? Visibility.Collapsed : Visibility.Visible;
            if (update == null) return;
            bool ready = updateFile != null && updateFile.Status == TaskStatus.RanToCompletion;
            TUpd.Text = !Updater.Installed ? L("Version " + update.Version + " herunterladen", "Download version " + update.Version)
                : ready ? L("Update " + update.Version + " installieren", "Install update " + update.Version) : L("Update " + update.Version + " lädt …", "Update " + update.Version + " loading …");
        }

        async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (busy || update == null) return;
            if (!Updater.Installed) { OpenSite(); return; }
            UpdBtn.IsEnabled = false;
            if (updateFile == null || updateFile.IsFaulted) updateFile = Updater.Download(update, pct => Dispatcher.BeginInvoke(new Action(() => TUpd.Text = pct + " %")));
            try
            {
                var file = await updateFile;
                TUpd.Text = L("Wird installiert …", "Installing …");
                Updater.Install(file, relaunch: true);
                await Task.Delay(400);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                TUpd.Text = ex.Message;
                updateFile = Updater.Download(update, null);   // the next click tries again
                UpdBtn.IsEnabled = true;
            }
        }

        // with automatic updates on, a downloaded update is installed when the window closes
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Updater.Auto && Updater.Installed && UpdBtn.IsEnabled && updateFile != null && updateFile.Status == TaskStatus.RanToCompletion)
                try { Updater.Install(updateFile.Result, relaunch: false); } catch { }
        }

        static void OpenSite()
        {
            try { Process.Start(new ProcessStartInfo(Updater.Site) { UseShellExecute = true }); } catch { }
        }

        void LangDe_Click(object sender, RoutedEventArgs e) => SetLanguage(true);
        void LangEn_Click(object sender, RoutedEventArgs e) => SetLanguage(false);

        void SetLanguage(bool de)
        {
            if (de == De || busy) return;
            Loc.SaveLanguage(de);
            previews.Clear();
            Task.Run(() =>
            {
                History.EnsureShortcuts();
                // the running look speaks the new language, too
                if (Shell.ShellControl.IsRunning) Shell.ShellControl.Start();
                if (!string.IsNullOrEmpty(Live.LiveWall.Spec)) { Live.LiveWall.Stop(); Live.LiveWall.Start(); }
            });
            BuildAll();
        }

        void LightTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int off = 0;
                Native.DwmSetWindowAttribute(hwnd, 20, ref off, 4);
                int caption = 0x00FFFFFF;
                Native.DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            }
            catch { }
        }

        // ---------------- screenshot (docs and CI) ----------------
        public void ScreenshotMode(string file, string presetId, bool de)
        {
            Loc.De = de;
            ContentRendered += async (s, e) =>
            {
                if (presetId == "changes") TabChanges.IsChecked = true;
                else if (presetId != null && Presets.Find(presetId) != null) { selected = Presets.Find(presetId); BuildAll(); }
                await Task.Delay(6000);
                var bmp = new RenderTargetBitmap((int)(Root.ActualWidth * 2), (int)(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                bmp.Render(Root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(file)) enc.Save(fs);
                Close();
            };
        }
    }
}

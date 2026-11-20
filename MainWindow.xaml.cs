using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using WinMorph.Core;

namespace WinMorph
{
    /// <summary>
    /// The WinMorph app: a screen-print studio. Every look is a print. Choosing one pulls the squeegee over the
    /// proof sheet and the look appears behind the blade; applying it prints one pass per part (desktop, lock
    /// screen, shell) with its own progress. The changes page is the proof of everything WinMorph changed,
    /// stamped ORIGINAL or GEÄNDERT, and the evidence after a rollback.
    /// </summary>
    public partial class MainWindow : Window
    {
        static bool De => Loc.De;
        static string L(string de, string en) => De ? de : en;
        static CultureInfo Ci => De ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture;
        static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static SolidColorBrush B(uint rgb, byte a = 255) => B(C(rgb, a));
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        Brush R(string key) => (Brush)FindResource(key);
        FontFamily Display => (FontFamily)FindResource("Display");
        FontFamily Mono => (FontFamily)FindResource("Mono");

        static readonly bool Calm = !SystemParameters.ClientAreaAnimation;   // Windows "show animations" off
        static Duration Ms(double ms) => TimeSpan.FromMilliseconds(Calm ? Math.Min(ms, 120) : ms);
        static readonly IEasingFunction Out = new QuinticEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction OutSoft = new CubicEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction In = new CubicEase { EasingMode = EasingMode.EaseIn };
        static readonly IEasingFunction Pull = new CubicEase { EasingMode = EasingMode.EaseInOut };

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
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/WinMorph.ico")); } catch { }
            Mark.Children.Add(RegMark(34, C(0xD6006F), 2.4));
            selected = Presets.Find(Shell.ShellControl.CurrentPreset ?? "") ?? Presets.All[0];
            BuildSheet();
            Shell.AppCatalog.Loaded += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                catalogReady = true;
                foreach (var id in provisional) previews.Remove(id);
                if (provisional.Contains(selected.Id)) ShowPrint(selected, pull: false);
                provisional.Clear();
            }));
            Shell.AppCatalog.LoadAsync(Dispatcher);
            Task.Run(() => History.EnsureShortcuts());
            BuildAll(entrance: true);
            SourceInitialized += (s, e) => LightTitleBar();
            SizeChanged += (s, e) => TicketCol.Width = new GridLength(ActualWidth < 1160 ? 350 : 390);
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
        // inks: every look is printed in its own colours
        // =====================================================================
        static Color Hacker => Shell.ShellThemes.HackerColor;

        /// <summary>The look's three inks: the field colour of its screen, then two supporting inks.</summary>
        static Color[] Inks(Preset p)
        {
            switch (p.Id)
            {
                case "win7": return new[] { C(0x0A62C9), C(0x37B4F2), C(0x7ED957) };      // Aero blue, sky, the green of the start orb
                case "zen": return new[] { C(0xC23F27), C(0x8A6A44), C(0xE9D9BC) };       // shu vermilion, umber, washi
                default: return new[] { C(0x0B0F0C), Hacker, Applier.Mix(Hacker, Colors.Black, 0.55) };
            }
        }
        static Brush Field(Preset p)
        {
            var i = Inks(p);
            Brush b = p.Id == "terminal" ? new LinearGradientBrush(C(0x050806), C(0x142019), 45)
                : new LinearGradientBrush(i[0], Applier.Mix(i[0], i[1], 0.35), 45);
            b.Freeze();
            return b;
        }
        /// <summary>Text on the look's field (white, or the hacker colour on black; 4.5:1 or better).</summary>
        static Color OnField(Preset p) => p.Id == "terminal" ? Hacker : C(0xFFFFFF);

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
            BuildScreens(entrance);
            BuildTicket(entrance);
            ShowPrint(selected, pull: true, entrance: entrance);
            RefreshCount();
            if (ChangesPage.Visibility == Visibility.Visible) BuildChanges();
        }

        /// <summary>A registration target: circle with cross hairs, the printer's mark that layers line up on.</summary>
        static FrameworkElement RegMark(double size, Color c, double stroke = 1.2)
        {
            var g = new Grid { Width = size, Height = size, IsHitTestVisible = false };
            var pen = B(c);
            g.Children.Add(new Ellipse { Width = size * 0.62, Height = size * 0.62, Stroke = pen, StrokeThickness = stroke, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Rectangle { Width = stroke, Fill = pen, HorizontalAlignment = HorizontalAlignment.Center });
            g.Children.Add(new Rectangle { Height = stroke, Fill = pen, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Ellipse { Width = size * 0.24, Height = size * 0.24, Fill = pen, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return g;
        }

        // ---------------- the screens (look tiles) ----------------
        readonly Dictionary<string, (RadioButton rb, TranslateTransform lift)> screens = new Dictionary<string, (RadioButton, TranslateTransform)>();

        void BuildScreens(bool entrance)
        {
            ScreenRow.Children.Clear(); screens.Clear();
            var active = Shell.ShellControl.CurrentPreset;
            int n = 0;
            foreach (var p in Presets.All)
            {
                var fg = OnField(p);
                var g = new Grid { Margin = new Thickness(16, 10, 14, 12) };
                var name = new Viewbox
                {
                    StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, 70, 0),
                    Child = new TextBlock { Text = p.Name, FontFamily = Display, FontWeight = FontWeights.ExtraBold, FontSize = 34, Foreground = B(fg) },
                };
                var tag = new TextBlock { Text = p.Tag(De), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B(A(fg, 225)), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 58, 0), LineHeight = 17 };
                // the colour bar printed on the screen's edge: the look's inks, then its key
                var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var ink in Inks(p).Skip(1)) bar.Children.Add(new Border { Width = 11, Height = 11, Background = B(ink), BorderBrush = B(A(fg, 120)), BorderThickness = new Thickness(1), Margin = new Thickness(3, 0, 0, 1) });
                bar.Children.Add(new TextBlock { Text = (n + 1).ToString(), FontFamily = Mono, FontSize = 12, Foreground = B(A(fg, 170)), Margin = new Thickness(8, 0, 0, -2) });
                g.Children.Add(name); g.Children.Add(tag); g.Children.Add(bar);
                if (p.Id == active) g.Children.Add(ActiveStamp());
                var lift = new TranslateTransform();
                var rise = new TranslateTransform();
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Screen"), GroupName = "look", Background = Field(p), BorderBrush = R("Magenta"), Content = g, IsChecked = p == selected,
                    Margin = new Thickness(n == 0 ? 4 : 9, 6, n == Presets.All.Count - 1 ? 4 : 9, 2), ToolTip = L("Taste ", "Key ") + (n + 1),
                    RenderTransform = new TransformGroup { Children = { lift, rise } },
                };
                AutomationProperties.SetName(rb, p.Name);
                rb.Effect = new DropShadowEffect { Color = C(0x16171B), BlurRadius = 22, ShadowDepth = 8, Direction = 270, Opacity = p == selected ? 0.28 : 0.08 };
                if (p == selected) lift.Y = -5;
                var look = p;
                rb.Checked += (s, e) => Select(look);
                rb.MouseEnter += (s, e) => { if (look != selected) Anim(lift, TranslateTransform.YProperty, -2, 160, OutSoft); };
                rb.MouseLeave += (s, e) => { if (look != selected) Anim(lift, TranslateTransform.YProperty, 0, 220, OutSoft); };
                screens[p.Id] = (rb, lift);
                ScreenRow.Children.Add(rb);
                if (entrance)
                {
                    rise.Y = 26; rb.Opacity = 0;
                    Anim(rise, TranslateTransform.YProperty, 0, 640, Out, delay: 160 + n * 60);
                    Anim(rb, OpacityProperty, 1, 380, OutSoft, delay: 160 + n * 60);
                }
                n++;
            }
        }

        /// <summary>A rubber stamp on the active look.</summary>
        FrameworkElement ActiveStamp() => new Border
        {
            Child = new TextBlock { Text = L("AKTIV", "ACTIVE"), FontFamily = Display, FontWeight = FontWeights.ExtraBold, FontSize = 13, Foreground = R("Paper") },
            Background = R("Magenta"), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 1, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, RenderTransform = new RotateTransform(4), RenderTransformOrigin = new Point(0.5, 0.5),
        };

        void Select(Preset p)
        {
            if (p == selected) return;
            if (busy) { screens[selected.Id].rb.IsChecked = true; return; }
            selected = p;
            foreach (var kv in screens)
            {
                bool on = kv.Key == p.Id;
                var (rb, lift) = kv.Value;
                Anim(lift, TranslateTransform.YProperty, on ? -5 : 0, on ? 480 : 260, on ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } : OutSoft);
                if (rb.Effect is DropShadowEffect sh) Anim(sh, DropShadowEffect.OpacityProperty, on ? 0.28 : 0.08, 260, OutSoft);
                if (on && rb.IsChecked != true) rb.IsChecked = true;
            }
            BuildTicket(false);
            ShowPrint(p, pull: true);
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
        // the proof sheet and the squeegee
        // =====================================================================
        Image printOld, printNew;
        Grid printArea, blade;
        Rectangle wash;
        TranslateTransform bladeMove, washMove;
        RectangleGeometry reveal;
        TextBlock sheetCaption, sheetLoading;
        StackPanel colorBar;
        bool pulling;

        void BuildSheet()
        {
            SheetHost.Children.Clear();
            // the sheet: white paper with crop marks and registration targets around the printed area
            var sheet = new Border
            {
                Background = R("Paper"), Margin = new Thickness(18, 16, 18, 16), Padding = new Thickness(8, 6, 8, 14), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.14, Color = C(0x16171B) },
            };
            var dock = new DockPanel();
            var foot = new DockPanel { Margin = new Thickness(38, 0, 38, 0) };
            sheetCaption = new TextBlock { FontFamily = Mono, FontSize = 11.5, Foreground = R("Faint"), VerticalAlignment = VerticalAlignment.Center };
            colorBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(colorBar, Dock.Right);
            foot.Children.Add(colorBar); foot.Children.Add(sheetCaption);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);

            printArea = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Width = 640, Height = 360 };
            printOld = new Image { Stretch = Stretch.Uniform };
            printNew = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(printOld, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(printNew, BitmapScalingMode.HighQuality);
            reveal = new RectangleGeometry(new Rect(0, 0, 0, 0));
            printNew.Clip = reveal;
            printArea.Children.Add(new Border { Background = B(0xF1F2F5) });
            printArea.Children.Add(printOld);
            printArea.Children.Add(printNew);
            // fresh ink right behind the blade, and the blade itself (rubber in an aluminium holder)
            wash = new Rectangle { Height = 90, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
            washMove = new TranslateTransform(); wash.RenderTransform = washMove;
            printArea.Children.Add(wash);
            blade = new Grid { Height = 26, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
            blade.Children.Add(new Border { Height = 16, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(C(0xE9ECF1), 0), new GradientStop(C(0xA7ADB7), 0.6), new GradientStop(C(0x80868F), 1) }, 90) });
            blade.Children.Add(new Border { Height = 10, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 2, 2), Background = new LinearGradientBrush(C(0x2B2C31), C(0x101114), 90) });
            blade.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 5, Direction = 270, Opacity = 0.35 };
            bladeMove = new TranslateTransform(); blade.RenderTransform = bladeMove;
            printArea.Children.Add(blade);
            sheetLoading = new TextBlock { Text = L("Druckvorlage wird belichtet …", "Exposing the screen …"), FontSize = 14, Foreground = R("Faint"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            printArea.Children.Add(sheetLoading);

            // crop marks and registration targets hug the printed area (in a 30 px margin around it)
            const double M = 30;
            printArea.Margin = new Thickness(M);
            var marks = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            marks.Children.Add(printArea);
            var mag = C(0xD6006F);
            foreach (var (h, v) in new[] { (HorizontalAlignment.Left, VerticalAlignment.Top), (HorizontalAlignment.Right, VerticalAlignment.Top), (HorizontalAlignment.Left, VerticalAlignment.Bottom), (HorizontalAlignment.Right, VerticalAlignment.Bottom) })
            {
                bool l = h == HorizontalAlignment.Left, t = v == VerticalAlignment.Top;
                // a horizontal and a vertical hairline in line with the edges, stopping short of the print
                marks.Children.Add(new Rectangle { Width = 18, Height = 1, Fill = R("Ink"), HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(l ? 4 : 0, t ? M : 0, l ? 0 : 4, t ? 0 : M), IsHitTestVisible = false });
                marks.Children.Add(new Rectangle { Width = 1, Height = 18, Fill = R("Ink"), HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(l ? M : 0, t ? 4 : 0, l ? 0 : M, t ? 0 : 4), IsHitTestVisible = false });
            }
            var top = RegMark(20, mag); top.VerticalAlignment = VerticalAlignment.Top; top.Margin = new Thickness(0, 4, 0, 0);
            var left = RegMark(20, mag); left.HorizontalAlignment = HorizontalAlignment.Left; left.Margin = new Thickness(4, 0, 0, 0);
            var right = RegMark(20, mag); right.HorizontalAlignment = HorizontalAlignment.Right; right.Margin = new Thickness(0, 0, 4, 0);
            marks.Children.Add(top); marks.Children.Add(left); marks.Children.Add(right);
            dock.Children.Add(marks);
            sheet.Child = dock;
            SheetHost.Children.Add(sheet);
            SheetHost.SizeChanged += (s, e) => FitPrint();
        }

        /// <summary>The printed area keeps 16:9 and fills the sheet as far as the table allows.</summary>
        void FitPrint()
        {
            double w = SheetHost.ActualWidth - 36 - 16 - 60 - 4, h = SheetHost.ActualHeight - 32 - 20 - 60 - 18 - 4;
            if (w <= 0 || h <= 0) return;
            double k = Math.Min(w / 16, h / 9);
            printArea.Width = Math.Floor(16 * k); printArea.Height = Math.Floor(9 * k);
            if (!pulling) { reveal.BeginAnimation(RectangleGeometry.RectProperty, null); reveal.Rect = new Rect(0, 0, printArea.Width, printArea.Height); }
        }

        int printVersion;

        /// <summary>Shows the look on the sheet. With a pull: the squeegee crosses the sheet and the new print appears behind it.</summary>
        void ShowPrint(Preset p, bool pull, bool entrance = false)
        {
            int v = ++printVersion;
            var inks = Inks(p);
            sheetCaption.Text = L("ANDRUCK", "PROOF") + "  ·  " + p.Name.ToUpperInvariant() + "  ·  " + (Presets.All.IndexOf(p) + 1) + "/" + Presets.All.Count;
            colorBar.Children.Clear();
            foreach (var ink in inks.Concat(new[] { C(0xD6006F), C(0xFFC21A), C(0x16171B) }))
                colorBar.Children.Add(new Border { Width = 14, Height = 10, Background = B(ink), Margin = new Thickness(2, 0, 0, 0), BorderBrush = B(0xDEE1E7), BorderThickness = new Thickness(ink.R + ink.G + ink.B > 660 ? 1 : 0) });
            void Go()
            {
                if (v != printVersion) return;
                var img = PreviewOf(p);
                sheetLoading.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
                if (!pull || Calm || img == null) { printOld.Source = img; printNew.Source = null; return; }
                printNew.Source = img;
                double w = printArea.Width, h = printArea.Height;
                var dur = TimeSpan.FromMilliseconds(entrance ? 900 : 620);
                var begin = TimeSpan.FromMilliseconds(entrance ? 240 : 0);
                wash.Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(A(inks[0], 0), 0), new GradientStop(A(inks[0], 110), 0.72), new GradientStop(A(inks[1], 170), 1) }, 90);
                pulling = true;
                var r = new RectAnimation(new Rect(0, 0, w, 0), new Rect(0, 0, w, h), dur) { EasingFunction = Pull, BeginTime = begin };
                r.Completed += (s, e) =>
                {
                    if (v != printVersion) return;
                    pulling = false;
                    printOld.Source = printNew.Source; printNew.Source = null;
                    reveal.BeginAnimation(RectangleGeometry.RectProperty, null); reveal.Rect = new Rect(0, 0, printArea.Width, printArea.Height);
                    Anim(blade, OpacityProperty, 0, 180, In);
                    Anim(wash, OpacityProperty, 0, 260, In);
                };
                reveal.BeginAnimation(RectangleGeometry.RectProperty, r);
                bladeMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-26, h, dur) { EasingFunction = Pull, BeginTime = begin });
                washMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-26 - 90, h - 90, dur) { EasingFunction = Pull, BeginTime = begin });
                blade.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(80)) { BeginTime = begin });
                wash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { BeginTime = begin });
            }
            // the preview may take a moment the first time: draw after the click has painted
            Dispatcher.BeginInvoke(new Action(Go), entrance ? DispatcherPriority.Background : DispatcherPriority.Input);
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
            return System.IO.Path.Combine(PreviewDir, p.Id + "-print-" + (De ? "de" : "en") + "-" + variant + "-" + Updater.Version + ".png");
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
        // the job ticket: what the look prints, pass by pass, and the action
        // =====================================================================
        sealed class Pass
        {
            public string Step;
            public Border Fill;
            public ScaleTransform FillScale;
            public TextBlock State;
            public Path Check;
        }
        readonly List<Pass> passes = new List<Pass>();
        Button applyBtn;
        TextBlock applyText, status;
        Border applyFill;
        ScaleTransform applyScale;

        /// <summary>The passes of a look: desktop, lock screen, then everything the design shell draws.</summary>
        static (string step, string de, string en, string subDe, string subEn)[] Passes(Preset p)
        {
            switch (p.Id)
            {
                case "win7": return new[]
                {
                    ("desktop", "Desktop", "Desktop", "Aero-Hintergrund mit Lichtbändern", "Aero wallpaper with light ribbons"),
                    ("lock", "Sperrbildschirm", "Lock screen", "Aero-Anmeldung mit Kennwortfeld, auch bei Win+L", "Aero sign-in with password box, also on Win+L"),
                    ("shell", "Taskleiste, Startmenü, Alt+Tab", "Taskbar, Start menu, Alt+Tab", "Glasleiste mit Startkugel, Aero-Alt+Tab mit Live-Vorschau, blaue Titelleisten", "Glass bar with start orb, Aero Alt+Tab with live previews, blue title bars"),
                    ("shell", "Explorer, Task-Manager, Ausführen", "Explorer, Task Manager, Run", "Im Windows-7-Stil auf Win+E, Strg+Umschalt+Esc und Win+R; Aero-Mauszeiger", "Windows 7 style on Win+E, Ctrl+Shift+Esc and Win+R; Aero pointers"),
                };
                case "zen": return new[]
                {
                    ("desktop", "Desktop", "Desktop", "Berglandschaft, Licht folgt der Tageszeit", "Mountain landscape lit by the time of day"),
                    ("lock", "Sperrbildschirm", "Lock screen", "Atemkreis; das Passwort legt sich als Tintensteine auf einen Pinselstrich", "Breathing circle; the password lies as ink stones on a brush stroke"),
                    ("shell", "Leiste & Startmenü", "Bar & Start menu", "Schwebende helle Leiste mit Fokus-Timer, Atemübung und Regen; Startmenü als Hängerolle", "Floating light bar with focus timer, breathing and rain; Start menu as a hanging scroll"),
                    ("shell", "Alt+Tab & Fenster", "Alt+Tab & windows", "Shōji-Schiebetüren mit dem Inhalt der App, Ema-Täfelchen, warme Papier-Titelleisten", "Shoji doors with the app's content, ema plaques, warm paper title bars"),
                };
                default: return new[]
                {
                    ("desktop", "Desktop", "Desktop", "Live-Systeminfos neben deinen Symbolen", "Live system info next to your icons"),
                    ("lock", "Sperrbildschirm", "Lock screen", "Terminal-Anmeldung im Code-Regen, auch bei Win+L", "Terminal sign-in in the code rain, also on Win+L"),
                    ("shell", "Statuszeile, Terminal, Alt+Tab", "Status line, terminal, Alt+Tab", "Netz, CPU, RAM, Akku, Uhr; die Windows-Taste öffnet das Terminal; Prozess-Tabelle", "Network, CPU, RAM, battery, clock; the Windows key opens the terminal; process table"),
                    ("shell", "Dateien, Task-Manager, Ausführen", "Files, task manager, run", "Eigene Terminal-Apps auf Win+E, Strg+Umschalt+Esc und Win+R; Mauszeiger in deiner Farbe", "Own terminal apps on Win+E, Ctrl+Shift+Esc and Win+R; pointers in your colour"),
                };
            }
        }

        void BuildTicket(bool entrance)
        {
            var p = selected;
            var inks = Inks(p);
            TicketHost.Children.Clear();
            passes.Clear();
            var card = new Border { Background = R("Paper"), CornerRadius = new CornerRadius(14), BorderBrush = R("Rule"), BorderThickness = new Thickness(1.2), ClipToBounds = true };
            var dock = new DockPanel { LastChildFill = true };

            // head: the look's ink field with the job number
            var head = new Border { Background = Field(p), Padding = new Thickness(26, 18, 26, 18), CornerRadius = new CornerRadius(13, 13, 0, 0) };
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock { Text = L("AUFTRAG", "JOB") + "  Nº " + (Presets.All.IndexOf(p) + 1).ToString("00") + "  ·  " + L("3 DRUCKGÄNGE", "3 PASSES"), FontFamily = Mono, FontSize = 11.5, Foreground = B(A(OnField(p), 210)) });
            hs.Children.Add(new TextBlock { Text = p.Name, FontFamily = Display, FontWeight = FontWeights.ExtraBold, FontSize = 46, Foreground = B(OnField(p)), Margin = new Thickness(0, 2, 0, 0) });
            hs.Children.Add(new TextBlock { Text = p.Tag(De), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B(A(OnField(p), 225)) });
            head.Child = hs;
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            // the perforation where the ticket was torn off the pad
            var perf = new Rectangle { Height = 1.5, Stroke = R("Rule"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 } };
            DockPanel.SetDock(perf, Dock.Top);
            dock.Children.Add(perf);

            // foot: the action; while printing, the button itself fills like a progress bar
            var foot = new StackPanel { Margin = new Thickness(26, 4, 26, 22) };
            bool active = Shell.ShellControl.CurrentPreset == p.Id;
            string idle = active ? L(p.Name + " neu anwenden", "Apply " + p.Name + " again") : L(p.Name + " anwenden", "Apply " + p.Name);
            applyBtn = new Button { Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(applyBtn, idle);
            applyBtn.Click += Apply_Click;
            applyScale = new ScaleTransform(0, 1);
            applyFill = new Border { Background = R("MagentaDeep"), CornerRadius = new CornerRadius(8), RenderTransform = applyScale, IsHitTestVisible = false };
            applyText = new TextBlock { Text = idle, FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var btnHost = new Grid();
            btnHost.Children.Add(applyBtn); btnHost.Children.Add(applyFill); btnHost.Children.Add(applyText);
            foot.Children.Add(btnHost);
            status = new TextBlock { FontSize = 13, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 11, 0, 0), LineHeight = 19 };
            status.Text = L("Unter „Änderungen“ lässt sich alles vollständig zurücksetzen. Strg+Enter wendet an.", "Everything can be fully reset under “Changes”. Ctrl+Enter applies.");
            foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);

            // body: description and the passes
            var body = new StackPanel { Margin = new Thickness(26, 16, 26, 8) };
            body.Children.Add(new TextBlock { Text = p.Desc(De), FontSize = 13.5, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
            body.Children.Add(new TextBlock { Text = L("DRUCKGÄNGE", "PASSES"), FontFamily = Mono, FontSize = 11.5, Foreground = R("Faint"), Margin = new Thickness(0, 16, 0, 8) });
            int k = 0;
            foreach (var part in Passes(p))
            {
                var ink = p.Id == "terminal" ? (k == 0 ? Applier.Mix(Hacker, Colors.Black, 0.55) : Hacker) : inks[Math.Min(k, 2)];
                bool pale = ink.R + ink.G + ink.B > 600;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                // the swatch: an ink square that fills from the bottom while its pass prints, then gets a check
                var sw = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(3), BorderBrush = B(pale ? C(0xB9AC98) : ink), BorderThickness = new Thickness(1.6), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
                var fillScale = new ScaleTransform(1, 0.25);
                var fill = new Border { Background = B(ink), RenderTransformOrigin = new Point(0.5, 1), RenderTransform = fillScale };
                var check = new Path { Data = Geometry.Parse("M4,10 L8,14 L15,5"), Stroke = B(pale || ink.G > 200 ? C(0x16171B) : C(0xFFFFFF)), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 20, 20 }, StrokeDashOffset = 20, Width = 19, Height = 19, Opacity = 0 };
                var swg = new Grid(); swg.Children.Add(fill); swg.Children.Add(check);
                sw.Child = swg;
                row.Children.Add(sw);
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = De ? part.de : part.en, FontSize = 14, FontWeight = FontWeights.Bold });
                t.Children.Add(new TextBlock { Text = (De ? part.subDe : part.subEn).Replace("+", "\u2060+\u2060"), FontSize = 12.5, Foreground = R("Sub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                var state = new TextBlock { FontFamily = Mono, FontSize = 11.5, Foreground = R("Faint"), Margin = new Thickness(10, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                Grid.SetColumn(state, 2);
                row.Children.Add(state);
                body.Children.Add(row);
                passes.Add(new Pass { Step = part.step, Fill = fill, FillScale = fillScale, State = state, Check = check });
                k++;
            }
            if (p.Id == "terminal") body.Children.Add(ColorPicker());
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body, Focusable = false };
            dock.Children.Add(scroll);
            card.Child = dock;
            TicketHost.Children.Add(card);

            // the ticket is laid down: the head settles first, then the body rises
            var rise = new TranslateTransform(0, entrance ? 24 : 10);
            body.RenderTransform = rise; body.Opacity = 0;
            Anim(rise, TranslateTransform.YProperty, 0, 520, Out, delay: entrance ? 260 : 60);
            Anim(body, OpacityProperty, 1, 340, OutSoft, delay: entrance ? 260 : 60);
            var headIn = new TranslateTransform(0, -8); head.RenderTransform = headIn;
            Anim(headIn, TranslateTransform.YProperty, 0, 420, Out);
            if (entrance) { var slide = new TranslateTransform(28, 0); card.RenderTransform = slide; card.Opacity = 0; Anim(slide, TranslateTransform.XProperty, 0, 700, Out, delay: 80); Anim(card, OpacityProperty, 1, 420, OutSoft, delay: 80); }
            SetBusyUi(busy);
        }

        FrameworkElement ColorPicker()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = L("DRUCKFARBE", "INK COLOUR"), FontFamily = Mono, FontSize = 11.5, Foreground = R("Faint"), Margin = new Thickness(0, 8, 0, 8) });
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
            AutomationProperties.SetName(b, name);
            var ring = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2.5), BorderBrush = on ? R("Magenta") : Brushes.Transparent, Padding = new Thickness(3) };
            ring.Child = new Border { CornerRadius = new CornerRadius(2), Background = new LinearGradientBrush(C(0x0B0F0C), c, 45) };
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
            BuildScreens(false);
            BuildTicket(false);
            ShowPrint(selected, pull: true);
            // active: the whole look follows the new colour at once (bar, frames, pointers, desktop, lock picture)
            if (Shell.ShellControl.CurrentPreset == "terminal") Apply_Click(null, null);
            else status.Text = L("Druckfarbe gespeichert.", "Ink colour saved.");
        }

        // =====================================================================
        // applying: one pass after another, each with its own progress
        // =====================================================================
        static readonly string[] Order = { "reset", "desktop", "lock", "shell" };
        static readonly Dictionary<string, (double weight, double expectMs)> Weights = new Dictionary<string, (double, double)>
        {
            ["reset"] = (0.12, 600), ["desktop"] = (0.30, 1600), ["lock"] = (0.18, 900), ["shell"] = (0.40, 2600),
        };
        string currentStep;
        Stopwatch stepClock;

        static double DoneBefore(string step) => Order.TakeWhile(s => s != step).Sum(s => Weights[s].weight);

        void Step(string step)
        {
            // the previous pass is printed: its swatch is full and checked, with its time
            if (currentStep != null) FinishPass(currentStep, true);
            currentStep = step;
            stepClock = Stopwatch.StartNew();
            var (w, expect) = Weights[step];
            // the button fills towards 90 % of this pass in the time it usually takes, then waits for the real end
            applyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(DoneBefore(step) + w * 0.9, Ms(expect)) { EasingFunction = OutSoft });
            foreach (var ps in passes.Where(x => x.Step == step))
            {
                ps.FillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 0.9, Ms(expect)) { EasingFunction = OutSoft });
                ps.State.Text = L("druckt", "printing");
                ps.State.Foreground = R("Magenta");
            }
            string text;
            switch (step)
            {
                case "reset": text = L("Vorherigen Look abbauen", "Taking down the previous look"); break;
                case "desktop": text = L("Druckgang 1 von 3 · Desktop", "Pass 1 of 3 · desktop"); break;
                case "lock": text = L("Druckgang 2 von 3 · Sperrbildschirm", "Pass 2 of 3 · lock screen"); break;
                default: text = L("Druckgang 3 von 3 · Taskleiste, Alt+Tab, Apps", "Pass 3 of 3 · taskbar, Alt+Tab, apps"); break;
            }
            status.Text = text + " …";
        }

        void FinishPass(string step, bool ok)
        {
            var secs = stepClock?.Elapsed.TotalSeconds ?? 0;
            int i = 0;
            foreach (var ps in passes.Where(x => x.Step == step))
            {
                ps.FillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Ms(200)) { EasingFunction = Out });
                ps.Check.Opacity = ok ? 1 : 0;
                Anim(ps.Check, Shape.StrokeDashOffsetProperty, ok ? 0 : 20, 360, OutSoft, delay: 120 + i++ * 60);
                ps.State.Text = ok ? secs.ToString("0.0", Ci) + " s" : L("Fehler", "error");
                ps.State.Foreground = ok ? R("GoInk") : R("Stop");
                if (!ok) ps.Fill.Background = R("Stop");
            }
        }

        void SetBusyUi(bool on)
        {
            if (applyBtn != null) applyBtn.IsEnabled = !on;
            foreach (var t in screens.Values) t.rb.IsHitTestVisible = !on;
            TabChanges.IsEnabled = !on; TabLooks.IsEnabled = !on;
            LangDe.IsEnabled = LangEn.IsEnabled = !on;
        }

        async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            busy = true;
            SetBusyUi(true);
            var p = selected;
            foreach (var ps in passes)
            {
                ps.FillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); ps.FillScale.ScaleY = 0.25;
                ps.Check.BeginAnimation(Shape.StrokeDashOffsetProperty, null); ps.Check.StrokeDashOffset = 20; ps.Check.Opacity = 0;
                ps.State.Text = L("wartet", "waiting"); ps.State.Foreground = R("Faint");
            }
            applyScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); applyScale.ScaleX = 0;
            applyFill.BeginAnimation(OpacityProperty, null); applyFill.Opacity = 1;
            status.Text = L("Druckvorlagen für den Look vorbereiten …", "Preparing the look's screens …");
            var clock = Stopwatch.StartNew();
            var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (s, ev) => applyText.Text = L("Wird gedruckt …  ", "Printing …  ") + clock.Elapsed.TotalSeconds.ToString("0.0", Ci) + " s", Dispatcher);
            applyText.Text = L("Wird gedruckt …", "Printing …");
            currentStep = null;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);   // let the button state paint first
            Applier ap = null;
            string error = null;
            try
            {
                var o = ApplyOptions.For(p);
                var bk = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
                ap = new Applier(bk, De);
                var applier = ap;
                await Task.Run(() => applier.Apply(p, o, step => Dispatcher.BeginInvoke(new Action(() => { if (Weights.ContainsKey(step)) Step(step); }))));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await LockSetupOnce();
            }
            catch (Exception ex) { error = ex.Message; }
            ticker.Stop();
            busy = false;
            bool ok = error == null && ap != null && ap.Problems.Count == 0;
            if (currentStep != null) FinishPass(currentStep, ok);
            if (!ok)
                foreach (var ps in passes.Where(x => Array.IndexOf(Order, x.Step) > Array.IndexOf(Order, currentStep ?? "reset")))
                { ps.State.Text = L("nicht gedruckt", "not printed"); ps.State.Foreground = R("Stop"); }
            currentStep = null;
            applyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Ms(260)) { EasingFunction = Out });
            Anim(applyFill, OpacityProperty, 0, 500, OutSoft, delay: 1200);
            BuildScreens(false);
            if (selected == p)
            {
                applyText.Text = ok ? "✓  " + L(p.Name + " ist aktiv", p.Name + " is active") : L(p.Name + " erneut anwenden", "Apply " + p.Name + " again");
                status.Text = error != null ? error
                    : ok ? string.Format(Ci, L("Gedruckt in {0:0.0} s. Bleibt auch nach einem Neustart.", "Printed in {0:0.0} s. Stays after a restart, too."), clock.Elapsed.TotalSeconds)
                    : L("Angewendet, mit Problemen: ", "Applied with problems: ") + string.Join(", ", ap.Problems.Take(3)) + (ap.Problems.Count > 3 ? " …" : "");
                var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                back.Tick += (s2, e2) => { back.Stop(); if (!busy && selected == p) applyText.Text = L(p.Name + " neu anwenden", "Apply " + p.Name + " again"); };
                back.Start();
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
            var slide = new TranslateTransform(changes ? 30 : -30, 0);
            show.RenderTransform = slide;
            Anim(slide, TranslateTransform.XProperty, 0, 440, Out);
            Anim(show, OpacityProperty, 1, 280, OutSoft, from: 0);
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
        }

        // =====================================================================
        // changes: the proof of everything WinMorph changed
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

            // head: title, summary, the two actions
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var undo = new Button { Style = (Style)FindResource("Outline"), Content = L("Letzten Schritt rückgängig", "Undo last step"), Margin = new Thickness(0, 0, 12, 0), IsEnabled = History.CanUndo };
            undo.Click += async (s, e) => await RunRestore(undo: true);
            var restore = new Button { Style = (Style)FindResource("Btn"), Height = 44, FontSize = 14, Content = L("Alles zurücksetzen", "Reset everything"), IsEnabled = anything };
            restore.Click += (s, e) => Confirm(
                L("Alles auf das Original zurücksetzen?", "Reset everything to the original?"),
                L("Taskleiste, Startmenü, Alt+Tab, Fensterrahmen, Mauszeiger, Hintergrund und Sperrbildschirm werden wieder wie vor WinMorph. Danach wird jeder Wert einzeln geprüft.", "Taskbar, Start menu, Alt+Tab, window frames, pointers, wallpaper and lock screen go back to how they were before WinMorph. Every value is checked afterwards."),
                L("Zurücksetzen", "Reset"), async () => await RunRestore(undo: false));
            actions.Children.Add(undo); actions.Children.Add(restore);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var ht = new StackPanel();
            ht.Children.Add(new TextBlock { Text = L("Änderungen", "Changes"), FontFamily = Display, FontWeight = FontWeights.ExtraBold, FontSize = 44, Margin = new Thickness(0, -6, 0, 0) });
            string sum = !anything ? L("Nichts geändert: Windows ist im Originalzustand.", "Nothing changed: Windows is in its original state.")
                : active == 0 ? L("Alle Werte entsprechen dem Original.", "Every value matches the original.")
                : string.Format(L("{0} Bereiche weichen vom Original ab", "{0} areas differ from the original"), active) + (b.Created != null ? string.Format(L(" · Original gesichert am {0:dd.MM.yyyy, HH:mm}", " · original saved on {0:yyyy-MM-dd, HH:mm}"), b.Created.Value) : "");
            ht.Children.Add(new TextBlock { Text = sum, FontSize = 14, Foreground = R("Sub") });
            head.Children.Add(ht);
            grid.Children.Add(head);

            if (lastRestore != null)
            {
                var banner = Banner(lastRestore, lastWasUndo);
                Grid.SetRow(banner, 1);
                grid.Children.Add(banner);
            }

            // the proof sheet
            var sheet = new Border { Background = R("Paper"), CornerRadius = new CornerRadius(12), BorderBrush = R("Rule"), BorderThickness = new Thickness(1.2), Padding = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(sheet, 2);
            if (!anything && lastRestore == null)
            {
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var mark = RegMark(46, C(0xD6006F), 2); mark.Margin = new Thickness(0, 0, 0, 14);
                empty.Children.Add(mark);
                empty.Children.Add(new TextBlock { Text = L("Noch kein Look gedruckt.", "No look printed yet."), FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                empty.Children.Add(new TextBlock { Text = L("Sobald du einen Look anwendest, steht hier jede einzelne Änderung mit dem Originalwert.", "Once you apply a look, every single change is listed here with its original value."), FontSize = 14, Foreground = R("Sub"), Margin = new Thickness(0, 6, 0, 18), HorizontalAlignment = HorizontalAlignment.Center });
                var go = new Button { Style = (Style)FindResource("Btn"), Height = 44, FontSize = 14, Content = L("Look auswählen", "Choose a look"), HorizontalAlignment = HorizontalAlignment.Center };
                go.Click += (s, e) => TabLooks.IsChecked = true;
                empty.Children.Add(go);
                sheet.Child = empty;
            }
            else
            {
                var rows = new StackPanel();
                rows.Children.Add(SheetRow("Nº", L("BEREICH", "AREA"), L("VORHER (ORIGINAL)", "BEFORE (ORIGINAL)"), L("JETZT", "NOW"), null, true, null, 0));
                int q = 1;
                foreach (var c in list.OrderByDescending(x => x.Active))
                {
                    var row = SheetRow(q.ToString("00"), c.Title, c.Before, c.Now, c.Active, false, c.Group, q);
                    rows.Children.Add(row);
                    var rise = new TranslateTransform(0, 12);
                    row.RenderTransform = rise; row.Opacity = 0;
                    Anim(rise, TranslateTransform.YProperty, 0, 460, Out, delay: 40 + q * 32);
                    Anim(row, OpacityProperty, 1, 300, OutSoft, delay: 40 + q * 32);
                    q++;
                }
                sheet.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows, Focusable = false };
            }
            grid.Children.Add(sheet);

            // foot: updates, version, website
            var foot = new DockPanel { Margin = new Thickness(4, 16, 4, 0) };
            var auto = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = Updater.Auto, Content = new TextBlock { Text = L("Updates automatisch installieren", "Install updates automatically"), FontSize = 13, Foreground = R("Sub") } };
            auto.Click += (s, e) => Updater.Auto = auto.IsChecked == true;
            DockPanel.SetDock(auto, Dock.Left);
            foot.Children.Add(auto);
            var site = new TextBlock { FontSize = 13, Foreground = R("Sub"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = Updater.Site };
            site.Inlines.Add(new System.Windows.Documents.Run("WinMorph " + Updater.Version + "   ") { FontFamily = Mono });
            site.Inlines.Add(new System.Windows.Documents.Run("Website ↗") { Foreground = R("Magenta"), FontWeight = FontWeights.Bold });
            site.MouseLeftButtonUp += (s, e) => OpenSite();
            foot.Children.Add(site);
            Grid.SetRow(foot, 3);
            grid.Children.Add(foot);

            ChangesPage.Children.Add(grid);
        }

        FrameworkElement SheetRow(string q, string title, string before, string now, bool? active, bool header, string group, int index)
        {
            var g = new Grid { Margin = new Thickness(22, 0, 22, 0), MinHeight = header ? 38 : 54 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            TextBlock T(string s, int col, double size, bool mono = false, Brush fg = null, FontWeight? w = null)
            {
                var t = new TextBlock { Text = s ?? "", FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 14, 0), Foreground = fg ?? R("Ink"), FontWeight = w ?? FontWeights.Normal };
                if (!header && !string.IsNullOrEmpty(s)) t.ToolTip = s;
                if (mono) t.FontFamily = Mono;
                Grid.SetColumn(t, col);
                g.Children.Add(t);
                return t;
            }
            if (header)
            {
                foreach (var (s, col) in new[] { (q, 0), (title, 1), (before, 2), (now, 3), ("STATUS", 4) })
                    T(s, col, 11, true, R("Faint"));
                return new Border { Child = g, BorderBrush = R("Rule"), BorderThickness = new Thickness(0, 0, 0, 1) };
            }
            var chip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            chip.Children.Add(new Border { Width = 12, Height = 12, Margin = new Thickness(0, 0, 10, 0), Background = GroupBrush(group) });
            chip.Children.Add(new TextBlock { Text = q, FontFamily = Mono, FontSize = 13, Foreground = R("Faint"), VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(chip);
            T(title, 1, 14, false, null, FontWeights.SemiBold);
            T(before, 2, 13, true, R("Sub"));
            T(now, 3, 13, true, active == true ? R("Ink") : R("Sub"));
            // a rubber stamp: GEÄNDERT in magenta, ORIGINAL in green; it lands with a small thump
            bool changed = active == true;
            var stamp = new Border
            {
                CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(2), Padding = new Thickness(8, 1, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = changed ? R("Magenta") : R("Go"), Background = changed ? R("MagentaSoft") : R("GoSoft"), RenderTransformOrigin = new Point(0.5, 0.5),
                Child = new TextBlock { Text = changed ? L("GEÄNDERT", "CHANGED") : "ORIGINAL", FontFamily = Display, FontWeight = FontWeights.ExtraBold, FontSize = 12.5, Foreground = changed ? R("Magenta") : R("GoInk") },
            };
            var sc = new ScaleTransform(1, 1);
            stamp.RenderTransform = new TransformGroup { Children = { sc, new RotateTransform(index % 2 == 0 ? -3 : 2.5) } };
            if (!Calm)
            {
                var at = TimeSpan.FromMilliseconds(180 + index * 40);
                var thump = new DoubleAnimation(1.7, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }, BeginTime = at };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, thump); sc.BeginAnimation(ScaleTransform.ScaleYProperty, thump);
                stamp.Opacity = 0;
                stamp.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)) { BeginTime = at });
            }
            Grid.SetColumn(stamp, 4);
            g.Children.Add(stamp);
            return new Border { Child = g, BorderBrush = R("Rule"), BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        Brush GroupBrush(string group)
        {
            switch (group)
            {
                case "shell": return R("Magenta");
                case "desktop": return B(0x0A62C9);
                case "lock": return B(0xFFC21A);
                default: return B(0xA7AAB2);
            }
        }

        FrameworkElement Banner(RestoreResult r, bool undo)
        {
            bool ok = undo ? r.Complete : r.Complete && r.Different == 0;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 16, 22, 16), Background = ok ? R("GoSoft") : B(0xFDE7E8), BorderBrush = ok ? R("Go") : R("Stop"), BorderThickness = new Thickness(1.5) };
            var dock = new DockPanel();
            var mark = new Path
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
                : string.Format(L("{0} Werte einzeln mit dem Original verglichen, alle gleich. Taskleiste sichtbar, Mauszeiger und Win+L wieder von Windows.", "{0} values compared with the original one by one, all equal. Taskbar visible, pointers and Win+L back with Windows."), r.Checked);
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
            BuildScreens(false);
            BuildTicket(false);
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
            var scrim = new Border { Background = B(0x16171B, 120), Opacity = 0 };
            scrim.MouseLeftButtonUp += (s, e) => CloseDialog();
            Overlay.Children.Add(scrim);
            var card = new Border { Background = R("Paper"), CornerRadius = new CornerRadius(14), Padding = new Thickness(30, 26, 30, 24), Width = 500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 18, Direction = 270, Opacity = 0.3 } };
            var sp = new StackPanel();
            var mark = RegMark(30, C(0xD6006F), 2); mark.HorizontalAlignment = HorizontalAlignment.Left; mark.Margin = new Thickness(0, 0, 0, 14);
            sp.Children.Add(mark);
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
            Overlay.Children.Add(new Border { Background = B(0xFFFFFF, 228) });
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 360 };
            // the screen is washed out: a squeegee sweeps back and forth over a strip of fading ink
            var strip = new Grid { Height = 44, ClipToBounds = true, Margin = new Thickness(0, 0, 0, 18) };
            var ink = new Border { Background = new LinearGradientBrush(C(0xD6006F), C(0xFFC21A), 0), CornerRadius = new CornerRadius(4) };
            strip.Children.Add(ink);
            var sq = new Border { Width = 10, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = new LinearGradientBrush(C(0x2B2C31), C(0x101114), 0), Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Direction = 0, Opacity = 0.35 } };
            var move = new TranslateTransform(); sq.RenderTransform = move;
            strip.Children.Add(sq);
            if (!Calm)
            {
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 350, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Pull });
                ink.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(1800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            }
            sp.Children.Add(strip);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
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
                int caption = 0x00F2EEEC;   // the print table colour (BGR)
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMorph.Core;

namespace WinMorph
{
    public partial class MainWindow : Window
    {
        // WinMorph's own design first, then the parts that change real Windows settings (off by default)
        static readonly Area[] AreaOrder = { Area.Shell, Area.Desktop, Area.Lock, Area.Colors, Area.Taskbar, Area.Start, Area.Windows, Area.Fonts, Area.Cursor, Area.Sounds };
        static readonly string[] NavKeys = { "all", "live", "windows", "apple", "linux", "future", "special" };

        readonly Dictionary<string, BitmapSource> thumbs = new Dictionary<string, BitmapSource>();
        readonly Dictionary<string, BitmapSource> bigThumbs = new Dictionary<string, BitmapSource>();
        readonly Dictionary<string, Button> cardButtons = new Dictionary<string, Button>();
        readonly List<Border> cardImages = new List<Border>();
        string filter = "all";
        bool catalogReady;

        /// <summary>Renders what the WinMorph shell will really look like (no system changes).</summary>
        BitmapSource Preview(Preset p, bool big)
        {
            // drawn previews are kept on disk for a day: the next start shows them at once instead of drawing all looks again
            var file = PreviewFile(p, big);
            var hit = LoadPreview(file);
            if (hit != null) return hit;
            var th = Shell.ShellThemes.For(p.Id);
            if (th != null && !catalogReady) { provisional.Add(p.Id); return WallpaperArt.Render(p, big ? 768 : 496, big ? 432 : 278, true); }
            BitmapSource bmp;
            if (th == null) bmp = WallpaperArt.Render(p, big ? 768 : 496, big ? 432 : 278, true);
            else
            {
                var host = new Shell.ShellHost(th, preview: true);
                try { bmp = big ? host.RenderPreview(p, 1440, 810, !th.MenuFullScreen, 0.56) : host.RenderPreview(p, 1060, 596, false, 0.48); }
                finally { host.DisposePreview(); }
            }
            if (!bmp.IsFrozen) bmp.Freeze();
            Task.Run(() => { try { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using (var fs = File.Create(file + ".tmp")) enc.Save(fs); File.Copy(file + ".tmp", file, true); File.Delete(file + ".tmp"); } catch { } });
            return bmp;
        }

        readonly HashSet<string> provisional = new HashSet<string>();
        static string PreviewDir { get { var d = Path.Combine(Backup.DataDir, "previews"); Directory.CreateDirectory(d); return d; } }

        /// <summary>One file per look, size, language and look option (hacker colour, macOS light/dark) and version.</summary>
        static string PreviewFile(Preset p, bool big)
        {
            var c = Shell.ShellThemes.HackerColor;
            string variant = p.Id == "terminal" ? c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2") : p.Id == "macos" ? (Presets.MacDark ? "dark" : "light") : "";
            return Path.Combine(PreviewDir, p.Id + "-" + (big ? "big" : "small") + "-" + (Loc.De ? "de" : "en") + "-" + variant + "-" + Updater.Version + ".png");
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

        static void FadeIn(UIElement el)
        {
            el.Opacity = 0;
            el.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new System.Windows.Media.Animation.CubicEase() });
        }
        Preset selected;
        bool busy;

        public MainWindow()
        {
            InitializeComponent();
            Shell.RenderGuard.Watch(this);
            Logo.Source = RenderLogo(68);
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/WinMorph.ico")); } catch { }
            selected = Presets.All[0];
            Shell.AppCatalog.Loaded += () =>
            {
                catalogReady = true;
                foreach (var id in provisional) { thumbs.Remove(id); bigThumbs.Remove(id); }
                if (provisional.Count > 0) RebuildAll();
                provisional.Clear();
            };
            Shell.AppCatalog.LoadAsync(Dispatcher);
            System.Threading.Tasks.Task.Run(() => History.EnsureShortcuts());
            RebuildAll();
            SourceInitialized += (s, e) => DarkTitleBar();
            Updater.MarkUiOpen();
            Task.Run(() =>
            {
                Updater.Cleanup();
                // old previews (other versions, colours, languages)
                try { foreach (var f in Directory.GetFiles(PreviewDir)) if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(7)) File.Delete(f); } catch { }
            });
            OptAutoUpdate.IsChecked = Updater.Auto;
            CheckUpdate();
        }

        // ---------------- updates ----------------
        Updater.Release update;
        Task<string> updateFile;

        async void CheckUpdate()
        {
            try
            {
                update = await Task.Run(() => Updater.Check());
                if (update == null) return;
                // the installed copy loads it right away, so "Update now" takes only seconds
                if (Updater.Installed) updateFile = Updater.Download(update, pct => Dispatcher.BeginInvoke(new Action(() => { if (UpdBtn.IsEnabled) UpdText.Text = Loc.F("upd.progress", pct); })));
                ShowUpdate();
                if (updateFile != null) { await updateFile; ShowUpdate(); }
            }
            catch { updateFile = null; ShowUpdate(); }
        }

        void ShowUpdate()
        {
            UpdateCard.Visibility = update == null ? Visibility.Collapsed : Visibility.Visible;
            if (update == null) return;
            UpdTitle.Text = Loc.F("upd.title", update.Version);
            bool ready = updateFile != null && updateFile.Status == TaskStatus.RanToCompletion;
            UpdText.Text = Loc.Get(!Updater.Installed ? "upd.portable" : ready ? "upd.ready" : "upd.loading");
            TUpdBtn.Text = Loc.Get(Updater.Installed ? "upd.install" : "upd.download");
        }

        async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (busy || update == null) return;
            if (!Updater.Installed || updateFile == null) { Website_Click(null, null); return; }
            UpdBtn.IsEnabled = false;
            try
            {
                var file = await updateFile;
                UpdText.Text = Loc.Get("upd.installing");
                Updater.Install(file, relaunch: true);
                await Task.Delay(400);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdText.Text = ex.Message;
                updateFile = Updater.Download(update, null);   // next click tries again
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

        void AutoUpdate_Click(object sender, RoutedEventArgs e) => Updater.Auto = OptAutoUpdate.IsChecked == true;

        void Website_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.Site) { UseShellExecute = true }); } catch { }
        }

        // ---------------- building UI ----------------
        void RebuildAll()
        {
            TSubtitle.Text = Loc.Get("subtitle");
            TRestore.Text = Loc.Get("restore");
            TUndo.Text = Loc.Get("undo");
            TFooter.Text = Loc.Get("footer");
            TChanges.Text = Loc.Get("changes");
            TOptions.Text = Loc.Get("options");
            TOptExplorer.Text = Loc.Get("opt.explorer");
            TOptLock.Text = Loc.Get("opt.lock");
            TAutoUpdate.Text = Loc.Get("upd.auto");
            TVersion.Text = Loc.F("upd.version", Updater.Version);
            ShowUpdate();
            LangDe.Opacity = Loc.De ? 1 : 0.55;
            LangEn.Opacity = Loc.De ? 0.55 : 1;
            BuildNav();
            BuildGallery();
            if (filter == "live" && liveSelected != null) ShowLiveDetail(liveSelected); else ShowDetail(selected);
            UpdateBackupInfo();
        }

        void BuildNav()
        {
            NavPanel.Children.Clear();
            foreach (var key in NavKeys)
            {
                int count = key == "all" ? Presets.All.Count : key == "live" ? Live.WallCatalog.All.Count : Presets.All.Count(p => p.Group == key);
                var row = new DockPanel();
                var countText = new TextBlock { Text = count.ToString(), FontSize = 12, Foreground = (Brush)FindResource("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(countText, Dock.Right);
                row.Children.Add(countText);
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(key == "live" ? 2 : 4), Background = new SolidColorBrush(NavColor(key)), Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = Loc.Get(key), Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEF)), FontSize = 14 });
                var rb = new RadioButton { Style = (Style)FindResource("Nav"), Content = row, GroupName = "nav", IsChecked = key == filter };
                if (key == "live") rb.Margin = new Thickness(0, 2, 0, 12);
                rb.Checked += (s, e) => { filter = key; BuildGallery(); GalleryScroll.ScrollToTop(); };
                NavPanel.Children.Add(rb);
            }
        }

        static Color NavColor(string key)
        {
            switch (key)
            {
                case "live": return Color.FromRgb(0x22, 0xD3, 0x7A);
                case "windows": return Color.FromRgb(0x3B, 0x8E, 0xF0);
                case "apple": return Color.FromRgb(0xC9, 0xCE, 0xD6);
                case "linux": return Color.FromRgb(0xF0, 0x8A, 0x3B);
                case "future": return Color.FromRgb(0xA7, 0x7B, 0xFF);
                case "special": return Color.FromRgb(0xF0, 0x4E, 0x6E);
                default: return Color.FromRgb(0x7C, 0x8C, 0xFF);
            }
        }

        void BuildGallery()
        {
            bool live = filter == "live";
            Cards.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
            ExtrasPanel.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            THead.Text = Loc.Get(live ? "livehead" : (filter == "all" ? "headline" : filter));
            THeadSub.Text = Loc.Get(live ? "livesub" : "headsub");
            if (live) { BuildLive(); return; }

            Cards.Children.Clear();
            cardButtons.Clear();
            cardImages.Clear();
            foreach (var p in Presets.All.Where(x => filter == "all" || x.Group == filter))
                Cards.Children.Add(MakeCard(p));
            HighlightSelected();
            LayoutCards();
        }

        void Cards_SizeChanged(object sender, SizeChangedEventArgs e) { if (e.WidthChanged) LayoutCards(); }

        void LayoutCards()
        {
            double avail = Cards.ActualWidth;
            if (avail <= 0) return;
            const double gap = 16, min = 232;
            int cols = Math.Max(1, (int)(avail / (min + gap)));
            double w = Math.Floor(avail / cols - gap) - 1;
            foreach (var b in cardButtons.Values) b.Width = w;
            foreach (var img in cardImages) img.Height = Math.Round((w - 4) * 9 / 16);
        }

        Button MakeCard(Preset p)
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            var imgHost = new Border { Height = 139, Child = img, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2C)) };
            imgHost.SizeChanged += (s, e) => imgHost.Clip = TopRounded(imgHost.ActualWidth, imgHost.ActualHeight, 10);
            cardImages.Add(imgHost);

            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(p.Accent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nameRow = new DockPanel();
            if (p.Id == Shell.ShellControl.CurrentPreset)
            {
                var badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xD3, 0x7A)), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = Loc.Get("live.current"), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0xE6, 0x8F)) } };
                DockPanel.SetDock(badge, Dock.Right);
                nameRow.Children.Add(badge);
            }
            DockPanel.SetDock(dot, Dock.Left);
            nameRow.Children.Add(dot);
            nameRow.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var text = new StackPanel { Margin = new Thickness(14, 11, 14, 13) };
            text.Children.Add(nameRow);
            text.Children.Add(new TextBlock { Text = p.Tag(Loc.De), FontSize = 12, Foreground = (Brush)FindResource("Sub"), Margin = new Thickness(18, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

            var content = new StackPanel();
            content.Children.Add(imgHost);
            content.Children.Add(text);

            var btn = new Button { Style = (Style)FindResource("CardButton"), Content = content, ToolTip = p.Desc(Loc.De) };
            btn.Click += (s, e) => ShowDetail(p);
            cardButtons[p.Id] = btn;

            if (thumbs.TryGetValue(p.Id, out var cached)) img.Source = cached;
            else Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!thumbs.ContainsKey(p.Id)) thumbs[p.Id] = Preview(p, false);
                img.Source = thumbs[p.Id];
                FadeIn(img);
            }));
            return btn;
        }

        static Geometry TopRounded(double w, double h, double r)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, h), true, true);
                c.LineTo(new Point(0, r), false, false);
                c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(w - r, 0), false, false);
                c.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(w, h), false, false);
            }
            g.Freeze();
            return g;
        }

        void HighlightSelected()
        {
            foreach (var kv in cardButtons)
                kv.Value.BorderBrush = kv.Key == selected?.Id ? new SolidColorBrush(selected.Accent) : Brushes.Transparent;
        }

        // ---------------- detail panel ----------------
        void ShowDetail(Preset p)
        {
            liveSelected = null;
            LiveDetailMode(false);
            selected = p;
            HighlightSelected();
            if (bigThumbs.TryGetValue(p.Id, out var big)) BigPreview.Source = big;
            else
            {
                // the click answers at once with the small picture; the big one follows after the next frame
                BigPreview.Source = thumbs.TryGetValue(p.Id, out var small) ? small : null;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (selected != p || liveSelected != null) return;
                    if (!bigThumbs.TryGetValue(p.Id, out var b)) bigThumbs[p.Id] = b = Preview(p, true);
                    BigPreview.Source = b;
                }));
            }

            DName.Text = p.Name;
            DTag.Text = p.Tag(Loc.De);
            var pc = p.Id == "terminal" ? Shell.ShellThemes.HackerColor : p.Accent;
            DTag.Foreground = new SolidColorBrush(Lighten(pc));
            DDesc.Text = p.Desc(Loc.De);

            var accent = new SolidColorBrush(ButtonColor(pc));
            ApplyBtn.Background = accent;
            TApply.Text = Loc.F("apply", p.Name);
            foreach (var cb in new[] { OptExplorer, OptLock }) cb.Tag = accent;

            BuildColorBox(p);

            // each look remembers what it applies
            AreaList.Children.Clear();
            var parts = Presets.Parts(p.Id);
            foreach (var a in AreaOrder)
            {
                if (a == Area.Colors)
                    AreaList.Children.Add(new TextBlock { Text = Loc.Get("settingshead"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Faint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) });
                var title = new TextBlock { Text = Loc.Get("a." + a), FontSize = 13.5, FontWeight = FontWeights.SemiBold };
                var detail = new TextBlock { Text = Describe(p, a), FontSize = 12, Foreground = (Brush)FindResource("Sub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), LineHeight = 17 };
                var stack = new StackPanel();
                stack.Children.Add(title);
                stack.Children.Add(detail);
                var area = a;
                var cb = new CheckBox { Style = (Style)FindResource("Switch"), Content = stack, IsChecked = parts.HasFlag(a), Tag = accent, Margin = new Thickness(0, 0, 0, 13) };
                var look = p.Id;
                cb.Checked += (s, e) => Presets.SetParts(look, Presets.Parts(look) | area);
                cb.Unchecked += (s, e) => Presets.SetParts(look, Presets.Parts(look) & ~area);
                AreaList.Children.Add(cb);
            }
        }

        /// <summary>Colour choice of the hacker look: six ready colours plus any own colour.</summary>
        void BuildColorBox(Preset p)
        {
            ColorBox.Children.Clear();
            ColorBox.Visibility = p.Id == "terminal" || p.Id == "macos" ? Visibility.Visible : Visibility.Collapsed;
            if (p.Id == "macos") { BuildMacMode(); return; }
            if (p.Id != "terminal") return;
            var cur = Shell.ShellThemes.HackerColor;
            ColorBox.Children.Add(new TextBlock { Text = Loc.De ? "FARBE" : "COLOUR", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Faint"), Margin = new Thickness(0, 20, 0, 10) });
            var row = new WrapPanel();
            bool known = false;
            foreach (var (de, en, rgb) in Shell.ShellThemes.HackerColors)
            {
                var c = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                bool on = c == cur; known |= on;
                row.Children.Add(Swatch(c, Loc.De ? de : en, on));
            }
            var own = new Button { Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 8, 8), Height = 34, ToolTip = Loc.De ? "Eigene Farbe wählen" : "Pick your own colour" };
            var ownRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (!known) ownRow.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(cur), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            ownRow.Children.Add(new TextBlock { Text = Loc.De ? "Eigene …" : "Custom …", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            own.Content = ownRow;
            own.Click += (s, e) =>
            {
                using (var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B) })
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetHackerColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
            };
            row.Children.Add(own);
            ColorBox.Children.Add(row);
        }

        /// <summary>macOS look: light or dark.</summary>
        void BuildMacMode()
        {
            ColorBox.Children.Add(new TextBlock { Text = Loc.De ? "DARSTELLUNG" : "APPEARANCE", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Faint"), Margin = new Thickness(0, 20, 0, 10) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var dark in new[] { false, true })
            {
                bool on = Presets.MacDark == dark;
                var b = new Button { Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 96, Content = (on ? "✓  " : "") + (dark ? (Loc.De ? "Dunkel" : "Dark") : (Loc.De ? "Hell" : "Light")), Opacity = on ? 1 : 0.6 };
                var d = dark;
                b.Click += (s, e) =>
                {
                    if (Presets.MacDark == d) return;
                    Presets.MacDark = d;
                    thumbs.Remove("macos"); bigThumbs.Remove("macos");
                    // already active: the shell picks up the mode right away
                    if (Shell.ShellControl.CurrentPreset == "macos" && Shell.ShellControl.IsRunning) Task.Run(() => Shell.ShellControl.Start());
                    BuildGallery();
                    ShowDetail(Presets.Find("macos"));
                    Status.Text = Loc.De ? "Gespeichert. Hintergrund und Farben ändern sich mit „macOS anwenden“." : "Saved. Wallpaper and colours change with “Apply macOS”.";
                };
                row.Children.Add(b);
            }
            ColorBox.Children.Add(row);
        }

        Button Swatch(Color c, string name, bool on)
        {
            var b = new Button { Width = 34, Height = 34, Margin = new Thickness(0, 0, 8, 8), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name };
            var ring = new Border { CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2), BorderBrush = on ? Brushes.White : Brushes.Transparent, Padding = new Thickness(3) };
            ring.Child = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(c) };
            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            b.Template = tpl;
            b.Content = ring;
            b.Click += (s, e) => SetHackerColor(c);
            return b;
        }

        void SetHackerColor(Color c)
        {
            if (c.R + c.G + c.B < 120) c = Applier.Mix(c, Color.FromRgb(255, 255, 255), 0.35);   // too dark to read on black
            Shell.ShellThemes.HackerColor = c;
            thumbs.Remove("terminal"); bigThumbs.Remove("terminal");
            var hacker = Presets.Find("terminal");
            // already active: the shell and the desktop pick up the colour right away
            if (Shell.ShellControl.CurrentPreset == "terminal" && Shell.ShellControl.IsRunning)
                Task.Run(() => { Shell.ShellControl.Start(); Live.LiveWall.Start(); });
            BuildGallery();
            ShowDetail(hacker);
            Status.Text = Loc.De ? "Farbe gespeichert. Auch Hintergrund, Akzentfarbe und Sperrbild übernehmen: „Hacker anwenden“." : "Colour saved. To update wallpaper, accent and lock picture too: “Apply Hacker”.";
        }

        static string Describe(Preset p, Area a)
        {
            var parts = new List<string>();
            Func<string, string> G = Loc.Get;
            switch (a)
            {
                case Area.Shell:
                    var th = WinMorph.Shell.ShellThemes.For(p.Id);
                    if (th == null) { parts.Add(G("c.noshell")); break; }
                    parts.Add(G("c.shell.bar." + th.Layout));
                    parts.Add(G("c.shell.menu." + th.Menu));
                    if (th.Frame != null) parts.Add(G("c.shell.frames"));
                    parts.Add(G("c.shell.winkey"));
                    parts.Add(G("c.shell.alttab"));
                    parts.Add(G("c.shell.gpt"));
                    break;
                case Area.Taskbar:
                    parts.Add(Loc.F("c.align", G(p.Center ? "c.center" : "c.left")));
                    parts.Add(G(p.Combine == 2 ? "c.combine2" : "c.combine0"));
                    parts.Add(G("c.search" + p.Search));
                    if (p.TaskView) parts.Add(G("c.taskview"));
                    if (p.Widgets) parts.Add(G("c.widgets"));
                    if (p.Copilot) parts.Add(G("c.copilot"));
                    if (p.AutoHide) parts.Add(G("c.autohide"));
                    if (p.Seconds) parts.Add(G("c.seconds"));
                    break;
                case Area.Colors:
                    parts.Add(Loc.F("c.mode", G(p.DarkSystem ? "c.dark" : "c.light"), G(p.DarkApps ? "c.dark" : "c.light")));
                    parts.Add(Loc.F("c.accent", "#" + p.Accent.R.ToString("X2") + p.Accent.G.ToString("X2") + p.Accent.B.ToString("X2")));
                    parts.Add(G(p.Transparency ? "c.transp" : "c.notransp"));
                    if (p.AccentTaskbar && p.DarkSystem) parts.Add(G("c.acctb"));
                    if (p.AccentTitle) parts.Add(G("c.acctitle"));
                    break;
                case Area.Windows:
                    parts.Add(G(p.Animations ? "c.anim" : "c.noanim"));
                    parts.Add(G(p.FullDrag ? "c.fulldrag" : "c.outline"));
                    parts.Add(G(p.Snap ? "c.snap" : "c.nosnap"));
                    if (!p.Shadows) parts.Add(G("c.noshadow"));
                    if (p.FocusFollows) parts.Add(G("c.focus"));
                    if (!p.ClearType) parts.Add(G("c.nocleartype"));
                    if (p.MenuDelay == 0) parts.Add(G("c.menudelay"));
                    if (p.ClassicContextMenu) parts.Add(G("c.ctx"));
                    if (p.Compact) parts.Add(G("c.compact"));
                    break;
                case Area.Fonts:
                    parts.Add(Loc.F("c.font", p.Font, p.CaptionBold ? G("c.bold") : ""));
                    break;
                case Area.Desktop:
                    parts.Add(G(p.Live != null ? "c.livewall" : "c.wall"));
                    if (p.HideDesktopIcons) parts.Add(G("c.noicons"));
                    if (p.ShowThisPC) parts.Add(G("c.thispc"));
                    break;
                case Area.Lock:
                    parts.Add(G("c.lockscreen"));
                    parts.Add(G("c.lockpic"));
                    break;
                case Area.Cursor:
                    parts.Add(G("cur." + p.Cursor));
                    break;
                case Area.Sounds:
                    parts.Add(G(p.Silent ? "c.silent" : "c.sounds"));
                    break;
                case Area.Start:
                    parts.Add(G("c.layout" + p.StartLayout));
                    if (!p.Recommendations) parts.Add(G("c.norec"));
                    if (p.OsStyle != null) parts.Add(Loc.F("c.os", G("os." + p.OsStyle) + " · " + p.OsSkin));
                    if (p.EpClassicStart) parts.Add(G("c.epstart"));
                    break;
            }
            return string.Join(" · ", parts);
        }

        // ---------------- live wallpapers ----------------
        Live.WallItem liveSelected;
        static readonly Live.WallItem OffItem = new Live.WallItem { Id = "off", Group = "own" };
        static readonly Live.WallItem OwnItem = new Live.WallItem { Id = "own", Group = "own" };
        readonly Dictionary<string, ImageSource> liveThumbs = new Dictionary<string, ImageSource>();
        readonly Dictionary<string, Button> liveCards = new Dictionary<string, Button>();
        readonly List<(Button btn, Border img, StackPanel text)> liveLayout = new List<(Button, Border, StackPanel)>();
        bool liveSizeHooked;

        /// <summary>Cards fill the available width (like the preset gallery).</summary>
        void LayoutLive()
        {
            double avail = ExtrasPanel.ActualWidth;
            if (avail <= 0) return;
            const double gap = 14, min = 210;
            int cols = Math.Max(1, (int)(avail / (min + gap)));
            double w = Math.Floor(avail / cols - gap) - 1;
            foreach (var (btn, img, text) in liveLayout) { btn.Width = w; img.Width = w - 4; img.Height = Math.Round((w - 4) * 9 / 16); text.Width = w - 28; }
        }
        Live.LiveView livePreview;

        string LiveName(Live.WallItem it) => it == OffItem ? Loc.Get("live.off") : it == OwnItem ? Loc.Get("live.own") : it.Name(Loc.De);
        string LiveTag(Live.WallItem it) => it == OffItem ? Loc.Get("live.off.tag") : it == OwnItem ? Loc.Get("live.own.tag") : it.Tag(Loc.De);
        static readonly Color LiveAccent = Color.FromRgb(0x00, 0xC8, 0x53);

        void BuildLive()
        {
            ExtrasPanel.Children.Clear();
            liveCards.Clear();
            liveLayout.Clear();
            if (!liveSizeHooked) { liveSizeHooked = true; ExtrasPanel.SizeChanged += (s, e) => { if (filter == "live" && e.WidthChanged) LayoutLive(); }; }
            var current = Live.LiveWall.Spec;
            foreach (var g in new[] { "own" }.Concat(Live.WallCatalog.Groups))
            {
                ExtrasPanel.Children.Add(new TextBlock { Text = Loc.Get("lg." + g).ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Faint"), Margin = new Thickness(2, 6, 0, 10) });
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                var items = g == "own" ? new List<Live.WallItem> { OffItem, OwnItem } : Live.WallCatalog.All.Where(x => x.Group == g).ToList();
                foreach (var it in items) wrap.Children.Add(LiveCard(it, current != null && it.Scene != null && it.Spec == current || (it.IsVideo && current == it.Spec)));
                ExtrasPanel.Children.Add(wrap);
            }
            if (liveSelected == null) ShowLiveDetail(Live.WallCatalog.FromSpec(current) ?? Live.WallCatalog.All[0]);
            HighlightLive();
            Dispatcher.BeginInvoke(new Action(LayoutLive), DispatcherPriority.Loaded);
        }

        Button LiveCard(Live.WallItem it, bool active)
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            var imgHost = new Border { Width = 236, Height = 133, Child = img, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C)) };
            imgHost.SizeChanged += (s, e) => imgHost.Clip = TopRounded(imgHost.ActualWidth, imgHost.ActualHeight, 10);
            if (it == OffItem || it == OwnItem)
                imgHost.Child = new TextBlock { Text = it == OffItem ? "⏻" : "＋", FontSize = 40, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA3)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var text = new StackPanel { Margin = new Thickness(12, 9, 12, 11), Width = 212 };
            var nameRow = new DockPanel();
            if (active) { var a = new TextBlock { Text = Loc.Get("live.current"), FontSize = 11, Foreground = new SolidColorBrush(LiveAccent), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(a, Dock.Right); nameRow.Children.Add(a); }
            nameRow.Children.Add(new TextBlock { Text = LiveName(it), FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(nameRow);
            text.Children.Add(new TextBlock { Text = LiveTag(it), FontSize = 11.5, Foreground = (Brush)FindResource("Sub"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            var content = new StackPanel();
            content.Children.Add(imgHost); content.Children.Add(text);
            var btn = new Button { Style = (Style)FindResource("CardButton"), Content = content, Margin = new Thickness(0, 0, 14, 14), Width = 240 };
            liveLayout.Add((btn, imgHost, text));
            btn.Click += (s, e) => ShowLiveDetail(it);
            liveCards[it.Id] = btn;
            if (it.Scene != null || it.IsVideo) LoadLiveThumb(it, img);
            return btn;
        }

        async void LoadLiveThumb(Live.WallItem it, Image img)
        {
            if (liveThumbs.TryGetValue(it.Id, out var cached)) { img.Source = cached; return; }
            if (it.Scene != null)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                try { var bmp = Live.LiveView.Snapshot(it.Scene, 472, 266, 2.5); liveThumbs[it.Id] = bmp; img.Source = bmp; } catch { }
                return;
            }
            var file = await Live.WallCatalog.Thumb(it);
            if (file == null) return;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file); bi.DecodePixelWidth = 472; bi.EndInit(); bi.Freeze();
                liveThumbs[it.Id] = bi; img.Source = bi;
            }
            catch { }
        }

        void HighlightLive()
        {
            foreach (var kv in liveCards) kv.Value.BorderBrush = liveSelected != null && kv.Key == liveSelected.Id ? new SolidColorBrush(LiveAccent) : Brushes.Transparent;
        }

        void LiveDetailMode(bool on)
        {
            var v = on ? Visibility.Collapsed : Visibility.Visible;
            foreach (var el in new UIElement[] { TChanges, AreaList, TOptions, OptExplorer, OptLock }) el.Visibility = v;
            if (!on && livePreview != null) { if (livePreview.Parent is Viewbox vb) vb.Child = null; PreviewFrame.Child = BigPreview; livePreview = null; }
        }

        void ShowLiveDetail(Live.WallItem it)
        {
            liveSelected = it;
            LiveDetailMode(true);
            HighlightLive();
            DName.Text = LiveName(it);
            DTag.Text = LiveTag(it);
            DTag.Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0xE6, 0x8F));
            DDesc.Text = it == OffItem ? Loc.Get("live.off.desc") : it == OwnItem ? Loc.Get("live.own.desc") : it.IsVideo ? Loc.F("live.desc.video", "Mixkit (mixkit.co)", it.SizeMb) : Loc.Get(it.Interactive ? "live.desc.scene" : "live.desc.calm");
            ApplyBtn.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x8F, 0x4E));
            TApply.Text = it == OffItem ? Loc.Get("live.off") : it == OwnItem ? Loc.Get("live.own") : Loc.Get("live.apply");
            Status.Text = "";
            // animated, interactive preview
            livePreview = null;
            if (it.Scene != null) ShowLivePreview("scene:" + it.Scene, Live.MouseMode.Anywhere);
            else if (it.IsVideo && it.Downloaded) ShowLivePreview("video:" + it.LocalVideo, Live.MouseMode.None);
            else if (it.IsVideo)
            {
                // WPF cannot stream videos from the internet (it crashes on current Windows builds):
                // until the video is downloaded, the preview is its big still picture with a slow zoom
                PreviewFrame.Child = BigPreview; BigPreview.Source = null;
                Live.WallCatalog.Thumb(it, large: true).ContinueWith(tk =>
                {
                    if (liveSelected == it && tk.Status == TaskStatus.RanToCompletion && tk.Result != null) ShowLivePreview("image:" + tk.Result, Live.MouseMode.Anywhere);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else { PreviewFrame.Child = BigPreview; BigPreview.Source = null; }
        }

        void ShowLivePreview(string spec, Live.MouseMode mouse)
        {
            if (livePreview?.Parent is Viewbox old) old.Child = null;
            livePreview = new Live.LiveView(spec, mouse: mouse);
            // render at a desktop-like size and scale down, so the preview looks like the real thing
            livePreview.Width = 1280; livePreview.Height = 720;
            PreviewFrame.Child = new Viewbox { Child = livePreview, Stretch = Stretch.UniformToFill };
        }

        async Task ApplyLive(Live.WallItem it)
        {
            var bk = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
            if (!bk.HasData && MessageBox.Show(this, Loc.Get("confirm.first"), Loc.Get("confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            string spec, poster = null, name = LiveName(it);
            if (it == OffItem) spec = "";
            else if (it == OwnItem)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = (Loc.De ? "Videos und Bilder" : "Videos and pictures") + "|*.mp4;*.wmv;*.mov;*.avi;*.m4v;*.jpg;*.jpeg;*.png;*.bmp", Title = Loc.Get("live.own") };
                if (dlg.ShowDialog(this) != true) return;
                bool image = new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant());
                spec = (image ? "image:" : "video:") + dlg.FileName;
                if (image) poster = dlg.FileName;
                name = System.IO.Path.GetFileName(dlg.FileName);
            }
            else
            {
                SetBusy(true, Loc.Get("applying"));
                if (it.IsVideo && !it.Downloaded)
                {
                    bool ok = await Live.WallCatalog.EnsureDownloaded(it, pct => Dispatcher.BeginInvoke(new Action(() => Status.Text = Loc.F("live.loading", pct))));
                    if (!ok) { SetBusy(false, Loc.Get("live.failed")); return; }
                }
                var mi = Shell.W32.PrimaryMonitor(out _);
                poster = it.IsVideo ? await Live.WallCatalog.Thumb(it, true) : Live.LiveWall.Poster(it, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                spec = it.Spec;
            }
            SetBusy(true, Loc.Get("applying"));
            try
            {
                await Task.Run(() => Live.LiveWall.Apply(bk, spec, poster));
                SetBusy(false, spec == "" ? Loc.Get("live.offdone") : Loc.F("live.done", name));
            }
            catch (Exception ex) { SetBusy(false, ex.Message); }
            UpdateBackupInfo();
            if (filter == "live") { var keep = liveSelected; BuildLive(); liveSelected = keep; HighlightLive(); }
        }

        // ---------------- actions ----------------
        async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (liveSelected != null) { await ApplyLive(liveSelected); return; }
            if (selected == null) return;
            var bk = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
            if (!bk.HasData && MessageBox.Show(this, Loc.Get("confirm.first"), Loc.Get("confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            SetBusy(true, Loc.Get("applying"));
            var p = selected;
            var areas = Presets.Parts(p.Id);
            var o = new ApplyOptions { Areas = areas, RestartExplorer = OptExplorer.IsChecked == true };
            bool lockScreen = OptLock.IsChecked == true;
            await Task.Delay(30);
            try
            {
                var pics = ApplyOptions.For(p, areas | (lockScreen ? Area.Lock : Area.None));
                string wallFile = pics.LockFile ?? pics.WallpaperFile;
                o.WallpaperFile = areas.HasFlag(Area.Desktop) || areas.HasFlag(Area.Lock) ? pics.WallpaperFile : null;
                o.LockFile = areas.HasFlag(Area.Lock) ? pics.LockFile : null;
                var ap = new Applier(bk, Loc.De);
                await Task.Run(() => ap.Apply(p, o));
                // one admin prompt for everything that needs it:
                //  - Win+L may show the WinMorph lock screen (Windows would lock before WinMorph can react)
                //  - an old all-users picture from WinMorph is removed (it hid every new lock screen)
                //  - optionally: the sign-in picture for all users
                bool wantGuard = areas.HasFlag(Area.Shell) && areas.HasFlag(Area.Lock) && Shell.ShellThemes.For(p.Id) != null && !LockGuard.Ready;
                bool staleOverride = !lockScreen && Applier.LockScreenOverrideIsOurs();
                if (lockScreen || wantGuard || staleOverride)
                {
                    string args = "--lock-setup " + (wantGuard ? LockGuard.UserSid ?? "-" : "-") + " " + (lockScreen && wallFile != null ? "\"" + wallFile + "\"" : "-") + " \"" + Backup.DefaultFile + "\"";
                    bool ok = await Task.Run(() => Tools.RunElevated(args));
                    if (!ok) ap.Problems.Add(Loc.De ? "Sperrbildschirm (keine Admin-Rechte)" : "Lock screen (no admin rights)");
                    // the shell arms Win+L when it starts; it is already running, so do it here once
                    else if (wantGuard && Shell.ShellActions.SaverReady()) LockGuard.Arm();
                }
                var msg = Loc.F("done", ap.Changed);
                if (ap.Notes.Count > 0) msg += "\n" + string.Join("\n", ap.Notes);
                if (ap.Problems.Count > 0) msg += "\n" + Loc.F("problems", string.Join(", ", ap.Problems.Take(4)) + (ap.Problems.Count > 4 ? " …" : ""));
                SetBusy(false, msg);
            }
            catch (Exception ex) { SetBusy(false, ex.Message); }
            UpdateBackupInfo();
            if (filter != "live") BuildGallery();   // moves the "active" badge
            Activate();
        }

        async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (!History.CanRestore && Shell.ShellControl.CurrentPreset == null) { Status.Text = Loc.Get("nobackup"); return; }
            if (MessageBox.Show(this, Loc.Get("confirm.restore"), Loc.Get("restore"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetBusy(true, Loc.Get("restoring"));
            try
            {
                bool restart = OptExplorer.IsChecked == true;
                var r = await Task.Run(() => History.RestoreOriginal(restart));
                var msg = Loc.F("restored", r.Restored);
                if (r.Problems.Count > 0) msg += "\n" + Loc.F("problems", string.Join(", ", r.Problems.Take(3)));
                SetBusy(false, msg);
            }
            catch (Exception ex) { SetBusy(false, ex.Message); }
            UpdateBackupInfo();
            if (filter != "live") BuildGallery();   // moves the "active" badge
            Activate();
        }

        async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (!History.CanUndo) { Status.Text = Loc.Get("noundo"); return; }
            SetBusy(true, Loc.Get("restoring"));
            try
            {
                bool restart = OptExplorer.IsChecked == true;
                var r = await Task.Run(() => History.UndoLast(restart));
                SetBusy(false, Loc.F("undone", r?.Restored ?? 0));
            }
            catch (Exception ex) { SetBusy(false, ex.Message); }
            UpdateBackupInfo();
            if (filter != "live") BuildGallery();   // moves the "active" badge
            Activate();
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            ApplyBtn.IsEnabled = !on;
            RestoreBtn.IsEnabled = !on;
            UndoBtn.IsEnabled = !on && History.CanUndo;
            Status.Text = text;
        }

        void UpdateBackupInfo()
        {
            var bk = new Backup(Backup.DefaultFile);
            BackupInfo.Text = bk.HasData && bk.Created != null
                ? Loc.F("backupfrom", bk.Created.Value.ToString(Loc.De ? "dd.MM.yyyy HH:mm" : "yyyy-MM-dd HH:mm"), bk.Count) + (Backup.StepCount > 0 ? " · " + Loc.F("steps", Backup.StepCount) : "")
                : Loc.Get("nobackup");
            UndoBtn.IsEnabled = !busy && History.CanUndo;
        }

        void LangDe_Click(object sender, RoutedEventArgs e) { Loc.De = true; RebuildAll(); }
        void LangEn_Click(object sender, RoutedEventArgs e) { Loc.De = false; RebuildAll(); }

        void PreviewFrame_SizeChanged(object sender, SizeChangedEventArgs e)
            => PreviewFrame.Clip = new RectangleGeometry(new Rect(0, 0, PreviewFrame.ActualWidth, PreviewFrame.ActualHeight), 12, 12);

        // ---------------- helpers ----------------
        static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        static Color ButtonColor(Color c) => Luma(c) > 0.55 ? Applier.Mix(c, Color.FromRgb(0, 0, 0), 0.3) : c;
        static Color Lighten(Color c) => Luma(c) < 0.45 ? Applier.Mix(c, Color.FromRgb(255, 255, 255), 0.45) : c;

        void DarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int on = 1;
                Native.DwmSetWindowAttribute(hwnd, 20, ref on, 4);
                int caption = 0x001D1714; // #14171D as 0x00BBGGRR
                Native.DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            }
            catch { }
        }

        static BitmapSource RenderLogo(int s)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = new LinearGradientBrush(Color.FromRgb(0x7C, 0x8C, 0xFF), Color.FromRgb(0x9B, 0x4D, 0xFF), 45);
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), s * 0.24, s * 0.24);
                double q = s * 0.2, g = s * 0.05, x0 = s * 0.28, y0 = s * 0.28;
                var w = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
                var w2 = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                dc.DrawRoundedRectangle(w, null, new Rect(x0, y0, q, q), q * 0.25, q * 0.25);
                dc.DrawEllipse(w2, null, new Point(x0 + q * 1.5 + g, y0 + q / 2), q / 2, q / 2);
                dc.DrawEllipse(w2, null, new Point(x0 + q / 2, y0 + q * 1.5 + g), q / 2, q / 2);
                dc.DrawRoundedRectangle(w, null, new Rect(x0 + q + g, y0 + q + g, q, q), q * 0.5, q * 0.5);
            }
            var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        // ---------------- screenshot (for docs/testing) ----------------
        public void ScreenshotMode(string file, string presetId, bool de)
        {
            Loc.De = de;
            ContentRendered += async (s, e) =>
            {
                Live.WallItem liveItem = presetId != null && presetId.StartsWith("live:") ? Live.WallCatalog.Find(presetId.Substring(5)) : null;
                if (presetId == "live" || liveItem != null) { filter = "live"; }
                else if (presetId != null && Presets.Find(presetId) != null) selected = Presets.Find(presetId);
                RebuildAll();
                if (liveItem != null) ShowLiveDetail(liveItem);   // e.g. "live:mixkit-49122" opens that card
                // the full window size even on a small screen, at twice the resolution (sharp on the website)
                Root.Width = 1320; Root.Height = 840;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>The start menu window. One class, six very different layouts.</summary>
    public sealed partial class StartMenu : Window
    {
        readonly ShellTheme t;
        Rect anchor;     // start button position in DIPs
        Edge edge;
        Action resetView;
        public event Action Hidden;
        bool closing;
        static bool De => Core.Loc.De;
        string L(string de, string en) => De ? de : en;

        public StartMenu(ShellTheme theme, Rect anchorDip, Edge barEdge, string initialSearch = null)
        {
            t = theme; anchor = anchorDip; edge = barEdge;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; UseLayoutRounding = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily(t.Font);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Deactivated += (s, e) => SafeClose();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) SafeClose(); };
            pendingSearch = initialSearch;

            switch (t.Menu)
            {
                case MenuKind.Seven: Content = BuildSeven(); break;
                case MenuKind.Zen: Content = BuildZen(); break;
                default: Content = BuildPalette(); break;
            }
            Left = -20000; Top = -20000;
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (fullScreen) W32.SetBlur(h, 3, 0);
            };
        }

        public bool IsFullScreen => fullScreen;
        string pendingSearch;
        TextBox searchBox;
        bool fullScreen;

        /// <summary>The menu is built once and then only shown/hidden, so it opens instantly.</summary>
        void SafeClose()
        {
            if (closing) return;
            closing = true;
            try { Hide(); } catch { }
            try { resetView?.Invoke(); if (searchBox != null) searchBox.Text = ""; } catch { }
            Hidden?.Invoke();
        }

        public bool IsOpen => !closing && IsVisible;

        public void Open(Rect anchorDip, Edge barEdge, string search = null)
        {
            anchor = anchorDip; edge = barEdge; closing = false;
            if (!IsVisible) Show();
            UpdateLayout();
            PlaceWindow();
            Activate();
            W32.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Animate();
            if (searchBox != null)
            {
                searchBox.Focus();
                Keyboard.Focus(searchBox);
                if (!string.IsNullOrEmpty(search)) { searchBox.Text = search; searchBox.CaretIndex = search.Length; }
            }
        }

        public void CloseMenu() => SafeClose();

        void Animate()
        {
            if (!(Content is FrameworkElement c)) return;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            c.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fullScreen ? 160 : 120)));
            if (t.Menu == MenuKind.Zen) { SurfaceZen(); return; }
            if (fullScreen)
            {
                var sc = new ScaleTransform(1.04, 1.04);
                c.RenderTransformOrigin = new Point(0.5, 0.5);
                c.RenderTransform = sc;
                var a = new System.Windows.Media.Animation.DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                var tt = new TranslateTransform(0, edge == Edge.Top ? -14 : 14);
                c.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
            }
        }

        void Launch(AppEntry a) { SafeClose(); a.Start(); }
        void Do(Action a) { SafeClose(); a(); }

        void PlaceWindow()
        {
            var mon = anchor.Width > 0 ? W32.MonitorFromPoint(new W32.POINT { X = (int)((anchor.Left + 2) * DpiOf()), Y = (int)((anchor.Top + 2) * DpiOf()) }, 2) : W32.MonitorAtCursor();
            var mi = W32.Monitor(mon, out double s);
            var monRect = new Rect(mi.rcMonitor.Left / s, mi.rcMonitor.Top / s, (mi.rcMonitor.Right - mi.rcMonitor.Left) / s, (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / s);
            var work = new Rect(mi.rcWork.Left / s, mi.rcWork.Top / s, (mi.rcWork.Right - mi.rcWork.Left) / s, (mi.rcWork.Bottom - mi.rcWork.Top) / s);
            if (fullScreen)
            {
                SizeToContent = SizeToContent.Manual;
                Left = monRect.Left; Top = monRect.Top; Width = monRect.Width; Height = monRect.Height;
                return;
            }
            double w = ActualWidth, h = ActualHeight, x, y;
            if (t.Menu == MenuKind.Zen)
            {
                // just above the floating bar, centred on the screen
                x = monRect.Left + (monRect.Width - w) / 2;
                y = work.Bottom - h + 14;
            }
            else if (t.Menu == MenuKind.Palette)
            {
                x = monRect.Left + (monRect.Width - w) / 2;
                y = t.Layout == BarLayout.TerminalTop ? work.Top + 40 : monRect.Top + monRect.Height * 0.22;
            }
            else if (t.Layout == BarLayout.Floating)
            {
                x = monRect.Left + (monRect.Width - w) / 2;
                y = work.Bottom - h - 10;
            }
            else
            {
                x = anchor.Left;
                y = edge == Edge.Top ? anchor.Bottom : anchor.Top - h;
            }
            Left = Math.Max(work.Left, Math.Min(x, work.Right - w));
            Top = Math.Max(work.Top, Math.Min(y, work.Bottom - h + (t.Menu == MenuKind.Zen ? 14 : 0)));
        }

        double DpiOf() { W32.PrimaryMonitor(out double s); return s; }

        // ---------------- shared pieces ----------------
        List<AppEntry> Apps(string filter = null)
        {
            var all = AppCatalog.Apps;
            if (string.IsNullOrWhiteSpace(filter)) return all;
            var f = filter.Trim();
            return all.Where(a => a.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0)
                      .OrderBy(a => a.Name.StartsWith(f, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ThenBy(a => a.Name).ToList();
        }

        FrameworkElement Row(ImageSource icon, string text, Action click, double iconSize, Color fg, Color hover, double fontSize, FontWeight? weight = null, Color? hoverFg = null, string glyph = null, double height = 0, string sub = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) sp.Children.Add(Img(icon, iconSize));
            else if (glyph != null) { var g = Glyph(glyph, iconSize * 0.8, fg); g.Width = iconSize; sp.Children.Add(g); }
            else sp.Children.Add(new Border { Width = iconSize, Height = iconSize });
            var tb = new TextBlock { Text = text, FontSize = fontSize, Foreground = B(fg), FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            if (sub != null)
            {
                var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
                tb.Margin = new Thickness(0);
                st.Children.Add(tb);
                st.Children.Add(new TextBlock { Text = sub, FontSize = fontSize - 1.5, Foreground = B(A(fg, 150)) });
                sp.Children.Add(st);
            }
            else sp.Children.Add(tb);
            var b = Click(sp, click, hover, pad: new Thickness(8, 4, 8, 4));
            if (height > 0) b.Height = height;
            if (hoverFg != null)
            {
                b.MouseEnter += (s, e) => { tb.Foreground = B(hoverFg.Value); };
                b.MouseLeave += (s, e) => { tb.Foreground = B(fg); };
            }
            return b;
        }

        FrameworkElement AppRow(AppEntry a, double iconSize, Color fg, Color hover, double fontSize, Color? hoverFg = null, double height = 0)
            => Row(a.Icon, a.Name, () => Launch(a), iconSize, fg, hover, fontSize, hoverFg: hoverFg, height: height);

        TextBox Search(string hint, Color fg, Brush bg, double fontSize, CornerRadius radius, Thickness pad, Color? border = null)
        {
            var box = new TextBox
            {
                Background = Brushes.Transparent, Foreground = B(fg), CaretBrush = B(fg), BorderThickness = new Thickness(0), FontSize = fontSize,
                FontFamily = new FontFamily(t.Font), VerticalContentAlignment = VerticalAlignment.Center,
            };
            var hintText = new TextBlock { Text = hint, Foreground = B(A(fg, 120)), FontSize = fontSize, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), FontFamily = new FontFamily(t.Font) };
            box.TextChanged += (s, e) => hintText.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var g = new Grid();
            g.Children.Add(hintText);
            g.Children.Add(box);
            searchHost = new Border { Child = g, Background = bg, CornerRadius = radius, Padding = pad, BorderBrush = border.HasValue ? B(border.Value) : null, BorderThickness = border.HasValue ? new Thickness(1) : new Thickness(0) };
            searchBox = box;
            return box;
        }
        Border searchHost;

        void OnSearch(Action<string> update)
        {
            searchBox.TextChanged += (s, e) => update(searchBox.Text);
            searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    var best = Intents.For(searchBox.Text).FirstOrDefault();
                    if (best != null) Do(best.Run);
                }
            };
        }

        static UIElement Separator(Color c, double margin = 4) => new Border { Height = 1, Background = B(c), Margin = new Thickness(margin, 4, margin, 4) };

        ScrollViewer Scroll(UIElement content, double maxHeight) => new ScrollViewer { Content = content, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

        void WhenAppsReady(Action rebuild)
        {
            if (AppCatalog.Apps.Count > 0) return;
            Action h = null;
            h = () => { AppCatalog.Loaded -= h; if (!closing) rebuild(); };
            AppCatalog.Loaded += h;
        }


        static void TextElement_SetFont(DependencyObject d, string font) => TextElement.SetFontFamily(d, new FontFamily(font));


        FrameworkElement AllProgramsButton(string text, bool forward, Action a)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = B(0x000000) });
            sp.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Background = V((0, C(0x5DC74B)), (1, C(0x2E8B22))), Child = Glyph(forward ? "" : "", 11, Colors.White) });
            return Click(sp, a, C(0x316AC5, 60), radius: new CornerRadius(3), pad: new Thickness(8, 6, 8, 6));
        }

        // ================= Windows 7 / Vista / Mint =================
        UIElement BuildSeven()
        {
            bool mint = t.Id == "mint";
            var outer = new Border { CornerRadius = new CornerRadius(t.MenuRadius), Background = mint ? B(t.MenuSide) : V((0, A(t.MenuSide, 230)), (1, A(t.MenuSide, 250))), Padding = new Thickness(7), BorderBrush = B(0xFFFFFF, mint ? (byte)25 : (byte)110), BorderThickness = new Thickness(1) };
            outer.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.5 };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mint ? 300 : 265) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var leftPanel = new Border { Background = B(t.MenuBg), CornerRadius = new CornerRadius(mint ? 0 : 4), BorderBrush = B(0x000000, 60), BorderThickness = new Thickness(mint ? 0 : 1) };
            var left = new DockPanel();
            leftPanel.Child = left;

            // search at the bottom
            var search = Search(t.SearchHint ?? L("Suchen", "Search"), mint ? t.MenuFg : C(0x333333), mint ? B(0x2A2A2A) : B(0xFFFFFF), 12.5, new CornerRadius(mint ? 3 : 2), new Thickness(8, 5, 8, 5), mint ? (Color?)null : C(0xA9B8CC));
            var searchWrap = new Border { Child = searchHost, Margin = new Thickness(8, 6, 8, 8) };
            DockPanel.SetDock(searchWrap, Dock.Bottom);
            left.Children.Add(searchWrap);

            var listHost = new ContentControl();
            var allBtnHost = new Border { Margin = new Thickness(6, 0, 6, 0) };
            DockPanel.SetDock(allBtnHost, Dock.Bottom);
            left.Children.Add(allBtnHost);
            left.Children.Add(listHost);

            Color fg = t.MenuFg;
            Action showPinned = null, showAll = null;
            showPinned = () =>
            {
                var sp = new StackPanel { Margin = new Thickness(6, 8, 6, 4) };
                foreach (var a in AppCatalog.Pinned()) sp.Children.Add(AppRow(a, 32, fg, t.MenuHover, 12.5, null, 42));
                listHost.Content = sp;
                allBtnHost.Child = Row(null, L("Alle Programme", "All Programs"), showAll, 16, fg, t.MenuHover, 12.5, glyph: "", height: 32);
            };
            showAll = () =>
            {
                var sp = new StackPanel { Margin = new Thickness(6, 6, 6, 4) };
                foreach (var a in Apps()) sp.Children.Add(AppRow(a, 16, fg, t.MenuHover, 12, null, 26));
                listHost.Content = Scroll(sp, 420);
                allBtnHost.Child = Row(null, L("Zurück", "Back"), showPinned, 16, fg, t.MenuHover, 12.5, glyph: "", height: 32);
            };
            OnSearch(q =>
            {
                if (q.Trim().Length == 0) { showPinned(); return; }
                var sp = new StackPanel { Margin = new Thickness(6, 6, 6, 4) };
                foreach (var a in Apps(q).Take(10)) sp.Children.Add(AppRow(a, 24, fg, t.MenuHover, 12.5, null, 34));
                foreach (var it in Intents.For(q, 0)) { var x = it; sp.Children.Add(Row(x.Icon, x.Title, () => Do(x.Run), 24, fg, t.MenuHover, 12.5, glyph: x.Glyph, height: 34)); }
                listHost.Content = sp;
            });
            showPinned();
            resetView = () => showPinned();
            WhenAppsReady(() => { if (searchBox.Text.Length == 0) showPinned(); });
            grid.Children.Add(leftPanel);

            // right column
            var right = new DockPanel { Margin = new Thickness(8, 4, 2, 2) };
            Grid.SetColumn(right, 1);
            var places = new StackPanel();
            Color rfg = t.MenuSideFg, rhov = mint ? A(t.Accent, 70) : C(0xFFFFFF, 45);
            // Windows 7: the account picture in its glass frame, sitting on the top edge of the menu
            var pic = AccountPicture.Get(64);
            places.Children.Add(new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, -2, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(4),
                Background = V((0, C(0xFFFFFF, 170)), (1, C(0xFFFFFF, 80))), BorderBrush = B(0xFFFFFF, 200), BorderThickness = new Thickness(1),
                Child = pic != null ? (UIElement)new Border { CornerRadius = new CornerRadius(2), Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } }
                    : new Border { Background = V((0, A(t.Accent, 255)), (1, A(t.Accent, 170))), Child = new TextBlock { Text = ShellActions.UserName.Substring(0, 1).ToUpper(), FontSize = 22, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            });
            void P(string text, Action a, bool bold = false) => places.Children.Add(Row(null, text, () => Do(a), 0, rfg, rhov, 12.5, bold ? FontWeights.SemiBold : FontWeights.Normal, height: 31));
            P(ShellActions.UserName, () => ShellActions.Folder(Environment.SpecialFolder.UserProfile), true);
            P(L("Dokumente", "Documents"), () => ShellActions.Folder(Environment.SpecialFolder.MyDocuments));
            P(L("Bilder", "Pictures"), () => ShellActions.Folder(Environment.SpecialFolder.MyPictures));
            P(L("Musik", "Music"), () => ShellActions.Folder(Environment.SpecialFolder.MyMusic));
            P(L("Downloads", "Downloads"), ShellActions.Downloads);
            places.Children.Add(Separator(C(0xFFFFFF, 50), 6));
            P(mint ? L("Rechner", "Computer") : L("Computer", "Computer"), ShellActions.ThisPC);
            P(L("Systemsteuerung", "Control Panel"), ShellActions.ControlPanel);
            P(L("Einstellungen", "Settings"), ShellActions.Settings);
            P("WinMorph", ShellActions.WinMorphApp);
            right.Children.Add(places);

            var power = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 8, 0, 4) };
            var shut = Click(new TextBlock { Text = L("Herunterfahren", "Shut down"), Foreground = B(0xFFFFFF), FontSize = 12 }, () => Do(ShellActions.Shutdown), C(0xFFFFFF, 60),
                V((0, C(0xFFFFFF, 60)), (1, C(0xFFFFFF, 20))), new CornerRadius(3, 0, 0, 3), new Thickness(10, 5, 10, 5));
            shut.BorderBrush = B(0xFFFFFF, 90); shut.BorderThickness = new Thickness(1);
            var more = Click(Glyph("", 10, Colors.White), () => PowerMenu(), C(0xFFFFFF, 60), V((0, C(0xFFFFFF, 60)), (1, C(0xFFFFFF, 20))), new CornerRadius(0, 3, 3, 0), new Thickness(7, 5, 7, 5));
            more.BorderBrush = B(0xFFFFFF, 90); more.BorderThickness = new Thickness(0, 1, 1, 1);
            power.Children.Add(shut); power.Children.Add(more);
            DockPanel.SetDock(power, Dock.Bottom);
            right.Children.Insert(0, power);
            grid.Children.Add(right);

            outer.Child = grid;
            return outer;
        }

        void PowerMenu()
        {
            var cm = new ContextMenu();
            void I(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (s, e) => Do(a); cm.Items.Add(mi); }
            I(L("Neu starten", "Restart"), ShellActions.Restart);
            I(L("Energie sparen", "Sleep"), ShellActions.Sleep);
            I(L("Sperren", "Lock"), ShellActions.Lock);
            I(L("Abmelden", "Sign out"), ShellActions.SignOut);
            I(L("Herunterfahren", "Shut down"), ShellActions.Shutdown);
            cm.IsOpen = true;
        }



        // ================= Command palette (Terminal / Zen / Arch) =================
        UIElement BuildPalette()
        {
            bool term = t.Id == "terminal", ai = false;
            var outer = new Border
            {
                Background = B(t.MenuBg), CornerRadius = new CornerRadius(t.MenuRadius), Width = 640, Padding = new Thickness(0, 0, 0, 8),
                BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1),
            };
            outer.Effect = new DropShadowEffect { BlurRadius = ai ? 40 : 24, ShadowDepth = 0, Opacity = ai ? 0.7 : 0.35, Color = ai || term ? t.Accent : Colors.Black };
            var sp = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(18, 14, 18, 10) };
            if (term) head.Children.Add(new TextBlock { Text = ShellActions.UserName.ToLower().Replace(' ', '_') + "@winmorph:~$ ", Foreground = B(t.MenuFg), FontFamily = new FontFamily("Consolas"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            else if (ai)
            {
                var orb = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 12, 0), Background = new RadialGradientBrush(C(0xB8F7FF), C(0x0077A8)) };
                orb.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
                head.Children.Add(orb);
            }
            else head.Children.Add(Glyph("", 18, t.MenuSub));
            if (!term && !ai) ((FrameworkElement)head.Children[0]).Margin = new Thickness(0, 0, 12, 0);
            Search(t.SearchHint, t.MenuFg, Brushes.Transparent, 20, new CornerRadius(0), new Thickness(0));
            if (term) searchBox.FontFamily = new FontFamily("Consolas");
            head.Children.Add(searchHost);
            sp.Children.Add(head);
            sp.Children.Add(new Border { Height = 1, Background = B(A(t.Accent, (byte)(term ? 120 : 40))) });

            var results = new StackPanel { Margin = new Thickness(8, 6, 8, 0) };
            sp.Children.Add(results);
            int selected = 0;
            var actions = new List<Action>();
            var rows = new List<Border>();

            Action<string> fill = q =>
            {
                results.Children.Clear(); actions.Clear(); rows.Clear(); selected = 0;
                q = q ?? "";
                void Add(ImageSource icon, string glyph, string text, string sub, Action a)
                {
                    var r = (Border)Row(icon, text, a, 26, t.MenuFg, t.MenuHover, 14, glyph: glyph, height: 42, sub: sub);
                    r.CornerRadius = new CornerRadius(term ? 0 : 8);
                    if (term) foreach (var tb in FindText(r)) tb.FontFamily = new FontFamily("Consolas");
                    results.Children.Add(r); actions.Add(a); rows.Add(r);
                }
                if (q.Trim().Length == 0)
                {
                    results.Children.Add(new TextBlock { Text = term ? "# " + L("angeheftet", "pinned") : (ai ? L("Vorschläge", "Suggestions") : L("Favoriten", "Favourites")), Foreground = B(t.MenuSub), FontSize = 12, Margin = new Thickness(10, 6, 0, 4), FontFamily = new FontFamily(term ? "Consolas" : t.Font) });
                    foreach (var a in AppCatalog.Pinned()) { var app = a; Add(app.Icon, null, app.Name, null, () => Launch(app)); }
                }
                else
                {
                    var text = q.Trim();
                    foreach (var it in Intents.For(q, 6)) { var x = it; Add(x.Icon, x.Glyph, x.Title, term ? (x.Sub == "App" ? "exec" : x.Sub.ToLowerInvariant()) : x.Sub, () => Do(x.Run)); }
                    if (term) Add(null, "\uE756", "run " + text, L("Befehl ausführen", "run command"), () => Do(() => ShellActions.Run("cmd.exe", "/c start \"\" " + text)));
                }
                if (rows.Count > 0) rows[0].Background = B(t.MenuHover);
            };
            searchBox.TextChanged += (s, e) => fill(searchBox.Text);
            searchBox.PreviewKeyDown += (s, e) =>
            {
                if (rows.Count == 0) return;
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    rows[selected].Background = Brushes.Transparent;
                    selected = (selected + (e.Key == Key.Down ? 1 : rows.Count - 1)) % rows.Count;
                    rows[selected].Background = B(t.MenuHover);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter) { e.Handled = true; var a = actions[selected]; a(); SafeClose(); }
            };
            fill(null);
            WhenAppsReady(() => fill(searchBox.Text));
            outer.Child = sp;
            // room for the glow: the window is larger than the panel, so its corners stay round
            return new Border { Child = outer, Padding = new Thickness(28), Background = Brushes.Transparent };
        }


        static IEnumerable<TextBlock> FindText(DependencyObject root)
        {
            if (root is TextBlock tb) yield return tb;
            if (root is Panel p) foreach (UIElement c in p.Children) foreach (var x in FindText(c)) yield return x;
            if (root is Decorator d && d.Child != null) foreach (var x in FindText(d.Child)) yield return x;
        }
    }
}

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
    public sealed class StartMenu : Window
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
            TextOptions.SetTextFormattingMode(this, t.Btn == BtnStyle.Bevel ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            Deactivated += (s, e) => SafeClose();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) SafeClose(); };
            pendingSearch = initialSearch;

            switch (t.Menu)
            {
                case MenuKind.Classic: Content = BuildClassic(); break;
                case MenuKind.Xp: Content = BuildXp(); break;
                case MenuKind.Seven: Content = BuildSeven(); break;
                case MenuKind.Ten: Content = BuildTen(); break;
                case MenuKind.Grid: Content = BuildGrid(); break;
                case MenuKind.Hub: Content = BuildHub(); break;
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
            if (!(Content is FrameworkElement c) || t.Menu == MenuKind.Classic) return;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            c.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fullScreen ? 160 : 120)));
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
            if (t.Menu == MenuKind.Palette)
            {
                x = monRect.Left + (monRect.Width - w) / 2;
                y = t.Layout == BarLayout.TerminalTop || t.Layout == BarLayout.Waybar ? work.Top + 40 : monRect.Top + monRect.Height * 0.22;
            }
            else if (t.Layout == BarLayout.Floating || t.CenterTasks && t.Menu == MenuKind.Grid)
            {
                x = t.Start == StartKind.Launcher ? anchor.Left : monRect.Left + (monRect.Width - w) / 2;
                y = work.Bottom - h - 10;
            }
            else
            {
                x = anchor.Left;
                y = edge == Edge.Top ? anchor.Bottom : anchor.Top - h;
                if (t.Start == StartKind.Launcher) y -= 8;
            }
            Left = Math.Max(work.Left, Math.Min(x, work.Right - w));
            Top = Math.Max(work.Top, Math.Min(y, work.Bottom - h));
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

        // ================= Windows 95 =================
        UIElement BuildClassic()
        {
            var root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            var main = new Bevel();
            var dock = new DockPanel();
            var banner = new Border { Width = 22, Background = V((0, C(0x000080)), (1, C(0x1084D0))) };
            var bannerText = new TextBlock { Text = t.Name, Foreground = B(0xFFFFFF), FontWeight = FontWeights.Bold, FontSize = 17, FontFamily = new FontFamily("Arial"), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            bannerText.LayoutTransform = new RotateTransform(-90);
            banner.Child = bannerText;
            dock.Children.Add(banner);
            DockPanel.SetDock(banner, Dock.Left);

            var items = new StackPanel { Width = 190 };
            Color fg = C(0x000000), hov = C(0x000080), hovFg = C(0xFFFFFF);
            Bevel sub = null;
            resetView = () => { if (sub != null) { root.Children.Remove(sub); sub = null; } };
            Action showPrograms = () =>
            {
                if (sub != null) { root.Children.Remove(sub); sub = null; return; }
                sub = new Bevel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-3, 0, 0, 0) };
                var list = new StackPanel { Width = 240 };
                foreach (var a in Apps()) list.Children.Add(AppRow(a, 16, fg, hov, 11.5, hovFg, 22));
                if (AppCatalog.Apps.Count == 0) list.Children.Add(new TextBlock { Text = L("Wird geladen …", "Loading …"), Margin = new Thickness(8) });
                sub.Child = Scroll(list, 560);
                root.Children.Add(sub);
            };
            items.Children.Add(Row(FolderIcon(Environment.SpecialFolder.Programs), L("Programme", "Programs") + "   ▸", showPrograms, 24, fg, hov, 12, hoverFg: hovFg, height: 34));
            items.Children.Add(Row(FolderIcon(Environment.SpecialFolder.MyDocuments), L("Dokumente", "Documents"), () => Do(() => ShellActions.Folder(Environment.SpecialFolder.MyDocuments)), 24, fg, hov, 12, hoverFg: hovFg, height: 34));
            items.Children.Add(Row(null, L("Einstellungen", "Settings"), () => Do(ShellActions.Settings), 24, fg, hov, 12, hoverFg: hovFg, glyph: "", height: 34));
            items.Children.Add(Row(SysIcon("control.exe"), L("Systemsteuerung", "Control Panel"), () => Do(ShellActions.ControlPanel), 24, fg, hov, 12, hoverFg: hovFg, height: 34));
            items.Children.Add(Row(null, L("Suchen", "Find"), () => Do(ShellActions.Search), 24, fg, hov, 12, hoverFg: hovFg, glyph: "", height: 34));
            items.Children.Add(Row(SysIcon("cmd.exe"), L("Ausführen …", "Run …"), () => Do(ShellActions.RunDialog), 24, fg, hov, 12, hoverFg: hovFg, height: 34));
            items.Children.Add(new Border { Height = 2, Margin = new Thickness(2, 3, 2, 3), BorderBrush = B(0x808080), BorderThickness = new Thickness(0, 1, 0, 0), Background = B(0xFFFFFF) });
            items.Children.Add(Row(null, L("Abmelden …", "Log Off …"), () => Do(ShellActions.SignOut), 24, fg, hov, 12, hoverFg: hovFg, glyph: "", height: 34));
            items.Children.Add(Row(null, L("Beenden …", "Shut Down …"), () => Do(ShellActions.Shutdown), 24, fg, hov, 12, hoverFg: hovFg, glyph: "", height: 34));
            dock.Children.Add(items);
            main.Child = dock;
            root.Children.Insert(0, main);
            TextElement_SetFont(root, "Microsoft Sans Serif");
            return root;
        }

        static void TextElement_SetFont(DependencyObject d, string font) => TextElement.SetFontFamily(d, new FontFamily(font));

        // ================= Windows XP =================
        UIElement BuildXp()
        {
            var outer = new Border { CornerRadius = new CornerRadius(7, 7, 0, 0), Background = B(0x1C5CD6), Padding = new Thickness(0), Width = 410, BorderBrush = B(0x0B3CB0), BorderThickness = new Thickness(1) };
            outer.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.45 };
            var dock = new DockPanel();

            // header
            var header = new Border { Height = 58, CornerRadius = new CornerRadius(6, 6, 0, 0), Background = new LinearGradientBrush(C(0x1C5CD6), C(0x4B92F0), 90) };
            var hp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            hp.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(4), BorderBrush = B(0xFFFFFF, 220), BorderThickness = new Thickness(2), Background = V((0, C(0xF7A84B)), (1, C(0xD35400))), Child = new TextBlock { Text = ShellActions.UserName.Substring(0, 1).ToUpper(), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            hp.Children.Add(new TextBlock { Text = ShellActions.UserName, Foreground = B(0xFFFFFF), FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 1, Opacity = 0.6 } });
            header.Child = hp;
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
            dock.Children.Add(new Border { Height = 2, Background = new LinearGradientBrush(C(0x1C5CD6), C(0xF7A84B), 0) { GradientStops = { new GradientStop(C(0x1C5CD6), 0), new GradientStop(C(0xF7A84B), 0.5), new GradientStop(C(0x1C5CD6), 1) } } });
            DockPanel.SetDock(dock.Children[1], Dock.Top);

            // footer
            var footer = new Border { Height = 42, Background = new LinearGradientBrush(C(0x1C5CD6), C(0x3E86EE), 90) };
            var fp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
            FrameworkElement FootBtn(string glyph, uint color, string text, Action a)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Background = V((0, Ui.C(color)), (1, Ui.C(color, 200))), BorderBrush = B(0xFFFFFF, 160), BorderThickness = new Thickness(1), Child = Glyph(glyph, 13, Colors.White) });
                sp.Children.Add(new TextBlock { Text = text, Foreground = B(0xFFFFFF), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), FontSize = 11.5 });
                return Click(sp, () => Do(a), C(0xFFFFFF, 40), radius: new CornerRadius(3), pad: new Thickness(6, 3, 8, 3));
            }
            fp.Children.Add(FootBtn("", 0xE8A33C, L("Abmelden", "Log Off"), ShellActions.SignOut));
            fp.Children.Add(FootBtn("", 0xD8401C, L("Ausschalten", "Turn Off Computer"), ShellActions.Shutdown));
            footer.Child = fp;
            DockPanel.SetDock(footer, Dock.Bottom);
            dock.Children.Add(footer);

            // body
            var body = new Grid { Background = B(0xFFFFFF) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var right = new StackPanel { Background = B(0xD3E5FA), Margin = new Thickness(0) };
            var rightBorder = new Border { Child = right, BorderBrush = B(0x95BDEE), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(4, 6, 4, 6), Background = B(0xD3E5FA) };
            Grid.SetColumn(rightBorder, 1);
            body.Children.Add(left);
            body.Children.Add(rightBorder);

            Color fg = C(0x000000), hov = C(0x316AC5), hovFg = C(0xFFFFFF);
            var listHost = new ContentControl();
            Action showPinned = null, showAll = null;
            var allBtn = new Border();
            showPinned = () =>
            {
                var sp = new StackPanel();
                var pins = AppCatalog.Pinned();
                for (int i = 0; i < pins.Count; i++)
                {
                    var a = pins[i];
                    sp.Children.Add(Row(a.Icon, a.Name, () => Launch(a), 30, fg, hov, 12, i < 2 ? FontWeights.Bold : FontWeights.Normal, hovFg, height: 40, sub: i == 0 ? L("Dateien und Ordner", "Files and folders") : i == 1 ? "Internet" : null));
                    if (i == 1) sp.Children.Add(Separator(C(0xD6D6D6), 8));
                }
                listHost.Content = sp;
                allBtn.Child = AllProgramsButton(L("Alle Programme", "All Programs"), true, showAll);
            };
            showAll = () =>
            {
                var sp = new StackPanel();
                foreach (var a in Apps()) sp.Children.Add(AppRow(a, 16, fg, hov, 11.5, hovFg, 24));
                listHost.Content = Scroll(sp, 380);
                allBtn.Child = AllProgramsButton(L("Zurück", "Back"), false, showPinned);
            };
            showPinned();
            resetView = () => showPinned();
            WhenAppsReady(() => showPinned());
            DockPanel.SetDock(allBtn, Dock.Bottom);
            left.Children.Add(allBtn);
            left.Children.Add(new Border { Height = 1, Background = B(0xD6D6D6), Margin = new Thickness(8, 2, 8, 2) });
            DockPanel.SetDock(left.Children[1], Dock.Bottom);
            left.Children.Add(listHost);

            Color rfg = C(0x00136B);
            void R(string glyph, string text, Action a, bool bold = true, ImageSource ico = null) => right.Children.Add(Row(ico, text, () => Do(a), 24, rfg, hov, 12, bold ? FontWeights.Bold : FontWeights.Normal, hovFg, glyph, 32));
            R("", L("Eigene Dateien", "My Documents"), () => ShellActions.Folder(Environment.SpecialFolder.MyDocuments), ico: FolderIcon(Environment.SpecialFolder.MyDocuments));
            R("", L("Eigene Bilder", "My Pictures"), () => ShellActions.Folder(Environment.SpecialFolder.MyPictures), ico: FolderIcon(Environment.SpecialFolder.MyPictures));
            R("", L("Eigene Musik", "My Music"), () => ShellActions.Folder(Environment.SpecialFolder.MyMusic), ico: FolderIcon(Environment.SpecialFolder.MyMusic));
            R("", L("Arbeitsplatz", "My Computer"), ShellActions.ThisPC, ico: ThisPcIcon());
            right.Children.Add(Separator(C(0x95BDEE), 8));
            R("", L("Systemsteuerung", "Control Panel"), ShellActions.ControlPanel, false, ico: SysIcon("control.exe"));
            R("", L("Einstellungen", "Settings"), ShellActions.Settings, false);
            R("", L("Suchen", "Search"), ShellActions.Search, false);
            R("", L("Ausführen …", "Run …"), ShellActions.RunDialog, false, ico: SysIcon("cmd.exe"));
            R("", "WinMorph", ShellActions.WinMorphApp, false);

            dock.Children.Add(body);
            outer.Child = dock;
            TextElement_SetFont(outer, "Tahoma");
            return outer;
        }

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
            places.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(mint ? 26 : 4), Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Center,
                Background = V((0, A(t.Accent, 255)), (1, A(t.Accent, 170))), BorderBrush = B(0xFFFFFF, 140), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = ShellActions.UserName.Substring(0, 1).ToUpper(), FontSize = 24, Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
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

        // ================= Windows 8 / 10 / Gaming =================
        UIElement BuildTen()
        {
            fullScreen = t.MenuFullScreen;
            var outer = new Border { Background = B(t.MenuBg), Width = fullScreen ? double.NaN : 660, Height = fullScreen ? double.NaN : 560 };
            if (!fullScreen) outer.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.5 };
            // no coloured frame around the menu (window borders stay neutral)
            var grid = new Grid { Margin = fullScreen ? new Thickness(60, 50, 60, 50) : new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fullScreen ? 320 : 250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            // icon strip
            var strip = new DockPanel { Background = fullScreen ? Brushes.Transparent : B(0x000000, 40) };
            var stripBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            FrameworkElement S(string glyph, string tip, Action a) { var b = Click(Glyph(glyph, 16, t.MenuFg), () => Do(a), t.MenuHover, pad: new Thickness(0, 12, 0, 12)); b.ToolTip = tip; return b; }
            var userTile = Click(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = B(t.Accent), Child = new TextBlock { Text = ShellActions.UserName.Substring(0, 1).ToUpper(), Foreground = B(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 } }, () => Do(() => ShellActions.Folder(Environment.SpecialFolder.UserProfile)), t.MenuHover, pad: new Thickness(0, 8, 0, 8));
            userTile.ToolTip = ShellActions.UserName;
            stripBottom.Children.Add(userTile);
            stripBottom.Children.Add(S("", L("Dokumente", "Documents"), () => ShellActions.Folder(Environment.SpecialFolder.MyDocuments)));
            stripBottom.Children.Add(S("", L("Bilder", "Pictures"), () => ShellActions.Folder(Environment.SpecialFolder.MyPictures)));
            stripBottom.Children.Add(S("", L("Einstellungen", "Settings"), ShellActions.Settings));
            var pw = Click(Glyph("", 16, t.MenuFg), PowerMenu, t.MenuHover, pad: new Thickness(0, 12, 0, 12));
            stripBottom.Children.Add(pw);
            strip.Children.Add(stripBottom);
            grid.Children.Add(strip);

            // app list
            var listCol = new DockPanel { Margin = new Thickness(6, 10, 6, 6) };
            Grid.SetColumn(listCol, 1);
            Search(t.SearchHint ?? L("Apps durchsuchen", "Search apps"), t.MenuFg, B(0xFFFFFF, 18), 13, new CornerRadius(t.Id == "gaming" ? 0 : 2), new Thickness(8, 6, 8, 6));
            searchHost.Margin = new Thickness(4, 0, 4, 8);
            DockPanel.SetDock(searchHost, Dock.Top);
            listCol.Children.Add(searchHost);
            var listHost = new ContentControl();
            listCol.Children.Add(listHost);
            Action<string> fill = q =>
            {
                var sp = new StackPanel();
                char last = '\0';
                foreach (var a in Apps(q))
                {
                    char first = char.ToUpper(a.Name[0]);
                    if (string.IsNullOrWhiteSpace(q) && first != last)
                    {
                        last = first;
                        sp.Children.Add(new TextBlock { Text = char.IsLetter(first) ? first.ToString() : "#", Foreground = B(t.MenuFg), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 10, 0, 4) });
                    }
                    sp.Children.Add(AppRow(a, 26, t.MenuFg, t.MenuHover, 12.5, null, 36));
                }
                if (!string.IsNullOrWhiteSpace(q))
                    foreach (var it in Intents.For(q, 0)) { var x = it; sp.Children.Add(Row(x.Icon, x.Title, () => Do(x.Run), 26, t.MenuFg, t.MenuHover, 12.5, glyph: x.Glyph, height: 36)); }
                listHost.Content = Scroll(sp, fullScreen ? 2000 : 470);
            };
            OnSearch(fill);
            fill(null);
            WhenAppsReady(() => fill(searchBox.Text));
            grid.Children.Add(listCol);

            // tiles
            var tilesCol = new StackPanel { Margin = new Thickness(14, 14, 14, 10) };
            Grid.SetColumn(tilesCol, 2);
            tilesCol.Children.Add(new TextBlock { Text = t.Id == "gaming" ? L("SCHNELLSTART", "QUICK LAUNCH") : L("Häufig verwendet", "Frequently used"), Foreground = B(t.MenuFg), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) });
            var tiles = new WrapPanel();
            double ts = fullScreen ? 132 : 104;
            Action fillTiles = () =>
            {
                tiles.Children.Clear();
                foreach (var a in AppCatalog.Pinned().Concat(AppCatalog.Apps.Take(fullScreen ? 10 : 4)))
                {
                    var app = a;
                    var g = new Grid { Width = ts, Height = ts };
                    g.Children.Add(new Image { Source = app.Icon, Width = ts * 0.36, Height = ts * 0.36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
                    g.Children.Add(new TextBlock { Text = app.Name, Foreground = B(0xFFFFFF), FontSize = 11, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(7, 0, 7, 6), TextTrimming = TextTrimming.CharacterEllipsis });
                    var tileColor = t.Id == "gaming" ? A(t.Accent, 200) : Core.Applier.Mix(t.Accent, Colors.White, tiles.Children.Count % 3 * 0.12);
                    var tile = Click(g, () => Launch(app), Core.Applier.Mix(tileColor, Colors.White, 0.15), B(tileColor), new CornerRadius(t.Id == "gaming" ? 0 : 0));
                    tile.Margin = new Thickness(0, 0, 6, 6);
                    if (t.Id == "gaming") tile.RenderTransform = new SkewTransform(-6, 0);
                    tiles.Children.Add(tile);
                }
            };
            fillTiles();
            WhenAppsReady(fillTiles);
            tilesCol.Children.Add(tiles);
            grid.Children.Add(tilesCol);

            outer.Child = grid;
            if (fullScreen) outer.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == outer) SafeClose(); };
            return outer;
        }

        // ================= Launchpad / GNOME / ChromeOS / Nova =================
        UIElement BuildGrid()
        {
            fullScreen = t.MenuFullScreen;
            var outer = new Border
            {
                Background = B(t.MenuBg), CornerRadius = new CornerRadius(fullScreen ? 0 : t.MenuRadius),
                Width = fullScreen ? double.NaN : 760, Height = fullScreen ? double.NaN : 560,
                BorderBrush = t.Id == "nova" ? B(A(t.Accent, 150)) : null, BorderThickness = new Thickness(t.Id == "nova" ? 1 : 0),
            };
            if (!fullScreen) outer.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 4, Opacity = 0.45, Color = t.Id == "nova" ? t.Accent : Colors.Black };
            var dock = new DockPanel { Margin = fullScreen ? new Thickness(120, 70, 120, 60) : new Thickness(28, 24, 28, 20) };
            Search(t.SearchHint ?? L("Suchen", "Search"), t.MenuFg, B(0xFFFFFF, 30), fullScreen ? 15 : 14, new CornerRadius(20), new Thickness(16, 8, 16, 8), C(0xFFFFFF, 50));
            searchHost.Width = fullScreen ? 340 : double.NaN;
            searchHost.HorizontalAlignment = fullScreen ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            searchHost.Margin = new Thickness(0, 0, 0, fullScreen ? 40 : 20);
            DockPanel.SetDock(searchHost, Dock.Top);
            dock.Children.Add(searchHost);

            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            double tileW = fullScreen ? 130 : 108, iconS = fullScreen ? 64 : 48;
            Action<string> fill = q =>
            {
                wrap.Children.Clear();
                foreach (var a in Apps(q))
                {
                    var app = a;
                    var sp = new StackPanel { Width = tileW };
                    sp.Children.Add(new Image { Source = app.Icon, Width = iconS, Height = iconS, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) });
                    sp.Children.Add(new TextBlock { Text = app.Name, Foreground = B(t.MenuFg), FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 4, 4) });
                    var tile = Click(sp, () => Launch(app), t.MenuHover, radius: new CornerRadius(12), pad: new Thickness(2, 4, 2, 6));
                    tile.Margin = new Thickness(4, 2, 4, 12);
                    wrap.Children.Add(tile);
                }
                if (!string.IsNullOrWhiteSpace(q))
                    foreach (var it in Intents.For(q, 0))
                    {
                        var x = it;
                        var sp2 = new StackPanel { Width = tileW };
                        if (x.Icon != null) sp2.Children.Add(new Image { Source = x.Icon, Width = iconS, Height = iconS, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) });
                        else { var gl = Glyph(x.Glyph, iconS * 0.6, t.MenuFg); gl.Height = iconS; gl.Margin = new Thickness(0, 6, 0, 6); sp2.Children.Add(gl); }
                        sp2.Children.Add(new TextBlock { Text = x.Title, Foreground = B(t.MenuFg), FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, Margin = new Thickness(4, 0, 4, 4) });
                        var tile2 = Click(sp2, () => Do(x.Run), t.MenuHover, radius: new CornerRadius(12), pad: new Thickness(2, 4, 2, 6));
                        tile2.Margin = new Thickness(4, 2, 4, 12);
                        wrap.Children.Add(tile2);
                    }
                if (wrap.Children.Count == 0) wrap.Children.Add(new TextBlock { Text = AppCatalog.Apps.Count == 0 ? L("Apps werden geladen …", "Loading apps …") : L("Nichts gefunden", "Nothing found"), Foreground = B(t.MenuSub), FontSize = 14, Margin = new Thickness(20) });
            };
            OnSearch(fill);
            fill(null);
            WhenAppsReady(() => fill(searchBox.Text));
            var scroll = Scroll(wrap, 5000);
            dock.Children.Add(scroll);
            outer.Child = dock;
            if (fullScreen)
            {
                outer.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == outer || e.OriginalSource == dock || e.OriginalSource == wrap) SafeClose(); };
            }
            return outer;
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
            return outer;
        }

        // ================= AI hub (Nexus AI) =================
        UIElement BuildHub()
        {
            fullScreen = true;
            var outer = new Border { Background = B(t.MenuBg) };
            var col = new StackPanel { Width = 780, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 110, 0, 0) };

            var orb = new Border { Width = 74, Height = 74, CornerRadius = new CornerRadius(37), HorizontalAlignment = HorizontalAlignment.Center, Background = new RadialGradientBrush(C(0xE9E3FF), C(0x4B2FD6)) { GradientOrigin = new Point(0.35, 0.3) } };
            var glow = new DropShadowEffect { Color = t.Accent, BlurRadius = 30, ShadowDepth = 0, Opacity = 1 };
            orb.Effect = glow;
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new System.Windows.Media.Animation.DoubleAnimation(14, 46, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            col.Children.Add(orb);

            int hour = DateTime.Now.Hour;
            string greet = hour < 5 || hour >= 22 ? L("Gute Nacht", "Good night") : hour < 11 ? L("Guten Morgen", "Good morning") : hour < 17 ? L("Guten Tag", "Good afternoon") : L("Guten Abend", "Good evening");
            var title = new TextBlock { Text = greet + ", " + ShellActions.UserName.Split(' ')[0], FontSize = 44, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 4), Foreground = new LinearGradientBrush(C(0xC4B5FD), C(0x67E8F9), 0) };
            col.Children.Add(title);
            col.Children.Add(new TextBlock { Text = L("Was möchtest du tun? Apps, Befehle oder Fragen – ich kümmere mich darum.", "What would you like to do? Apps, commands or questions – I'll handle it."), Foreground = B(t.MenuSub), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 28) });

            Search(t.SearchHint, t.MenuFg, B(0xFFFFFF, 18), 21, new CornerRadius(30), new Thickness(26, 16, 26, 16), A(t.Accent, 200));
            searchHost.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.55 };
            col.Children.Add(searchHost);

            var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
            void Chip(string text, string fillWith)
            {
                var c = Click(new TextBlock { Text = text, Foreground = B(t.MenuFg), FontSize = 13 }, () => { searchBox.Text = fillWith; searchBox.CaretIndex = fillWith.Length; searchBox.Focus(); }, A(t.Accent, 80), B(0xFFFFFF, 14), new CornerRadius(16), new Thickness(14, 7, 14, 7));
                c.Margin = new Thickness(4); c.BorderBrush = B(0xFFFFFF, 30); c.BorderThickness = new Thickness(1);
                chips.Children.Add(c);
            }
            Chip(L("✦ Frag ChatGPT …", "✦ Ask ChatGPT …"), L("frag ", "ask "));
            Chip(L("Öffne …", "Open …"), L("öffne ", "open "));
            Chip(L("Suche im Web …", "Search the web …"), L("suche ", "search "));
            Chip(L("Dunkel", "Dark mode"), L("dunkel", "dark"));
            Chip(L("Bildschirmfoto", "Screenshot"), "screenshot");
            Chip(L("Sperren", "Lock"), L("sperren", "lock"));
            col.Children.Add(chips);

            var results = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            col.Children.Add(results);

            var pinnedTitle = new TextBlock { Text = L("DEINE APPS", "YOUR APPS"), Foreground = B(t.MenuSub), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 34, 0, 10) };
            var pinned = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(pinnedTitle);
            col.Children.Add(pinned);
            Action fillPinned = () =>
            {
                pinned.Children.Clear();
                foreach (var a in AppCatalog.Pinned())
                {
                    var app = a;
                    var sp = new StackPanel { Width = 88 };
                    sp.Children.Add(new Image { Source = app.Icon, Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 6) });
                    sp.Children.Add(new TextBlock { Text = app.Name, Foreground = B(t.MenuFg), FontSize = 11.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var tile = Click(sp, () => Launch(app), A(t.Accent, 60), radius: new CornerRadius(16), pad: new Thickness(4, 6, 4, 8));
                    tile.Margin = new Thickness(4);
                    pinned.Children.Add(tile);
                }
            };
            fillPinned();
            WhenAppsReady(fillPinned);

            int selected = 0;
            var rows = new List<Border>();
            var acts = new List<Action>();
            Action<string> fill = q =>
            {
                results.Children.Clear(); rows.Clear(); acts.Clear(); selected = 0;
                bool empty = string.IsNullOrWhiteSpace(q);
                chips.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                pinnedTitle.Visibility = pinned.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                if (empty) return;
                foreach (var it in Intents.For(q, 5))
                {
                    var x = it;
                    var r = (Border)Row(x.Icon, x.Title, () => Do(x.Run), 28, t.MenuFg, A(t.Accent, 70), 15, glyph: x.Glyph, height: 50, sub: x.Sub);
                    r.CornerRadius = new CornerRadius(14);
                    results.Children.Add(r); rows.Add(r); acts.Add(x.Run);
                }
                if (rows.Count > 0) rows[0].Background = B(A(t.Accent, 70));
            };
            searchBox.TextChanged += (s, e) => fill(searchBox.Text);
            searchBox.PreviewKeyDown += (s, e) =>
            {
                if (rows.Count == 0) return;
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    rows[selected].Background = Brushes.Transparent;
                    selected = (selected + (e.Key == Key.Down ? 1 : rows.Count - 1)) % rows.Count;
                    rows[selected].Background = B(A(t.Accent, 70));
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter) { e.Handled = true; var a = acts[selected]; Do(a); }
            };
            resetView = () => fill("");

            outer.Child = col;
            outer.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == outer || e.OriginalSource == col) SafeClose(); };
            return outer;
        }

        static IEnumerable<TextBlock> FindText(DependencyObject root)
        {
            if (root is TextBlock tb) yield return tb;
            if (root is Panel p) foreach (UIElement c in p.Children) foreach (var x in FindText(c)) yield return x;
            if (root is Decorator d && d.Child != null) foreach (var x in FindText(d.Child)) yield return x;
        }
    }
}

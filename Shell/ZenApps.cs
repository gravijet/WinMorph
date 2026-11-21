using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Base of Zen's own apps (Dateien, Aktivität, Ausführen): a frosted pane over the pond inside a window whose
    /// title bar takes the colours of the mist, an own pebble icon, the Yu Gothic text of the look.
    /// </summary>
    internal abstract class ZenWindow : Window
    {
        protected static bool De => Core.Loc.De;
        protected static string L(string de, string en) => De ? de : en;
        protected static readonly ShellTheme Theme = ShellThemes.For("zen");
        IntPtr hwnd;
        protected readonly Grid Overlay = new Grid { Visibility = Visibility.Collapsed };

        protected ZenWindow(string title, string glyph, Color iconColor, double w, double h)
        {
            Title = title; Width = w; Height = h;
            FontFamily = ZenStyle.Family; FontSize = 13.5;
            Foreground = B(ZenStyle.Ink);
            Background = B(ZenStyle.Mist);
            Resources[typeof(TextBlock)] = new Style(typeof(TextBlock));
            Resources.MergedDictionaries.Add(ZenStyle.ScrollBars);
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = AppIcon(glyph, iconColor);
            // our own title row with pebble buttons; Windows keeps the frame for snapping and resizing
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });
            StateChanged += (s, e) => { if (Content is FrameworkElement c) c.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); };
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                WindowTracker.OwnApps.Add(hwnd);
                var f = Theme.Frame;
                int cap = f.Caption.B << 16 | f.Caption.G << 8 | f.Caption.R, txt = f.Text.B << 16 | f.Text.G << 8 | f.Text.R, border = f.Border.B << 16 | f.Border.G << 8 | f.Border.R, corner = 2;
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_CAPTION_COLOR, ref cap, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_TEXT_COLOR, ref txt, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_BORDER_COLOR, ref border, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            Closed += (s, e) => WindowTracker.OwnApps.Remove(hwnd);
            RenderGuard.Watch(this);
        }

        /// <summary>The frosted body, with room for an overlay (questions like rename) on top.</summary>
        protected void Body(UIElement content)
        {
            // title row: the app's pebble, its title, and three stones (minimise, maximise, close)
            var dock = new DockPanel();
            var bar = new DockPanel { Height = 44, Margin = new Thickness(14, 0, 8, 0) };
            var stones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border Cap(string g, string tip, Action a, Color hover)
            {
                var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = tip, Child = Glyph(g, 10, ZenStyle.Ink) };
                ZenStyle.Press(b, C(0xFFFFFF, 120), hover);
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; a(); };
                System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                return b;
            }
            stones.Children.Add(Cap("\uE921", L("Minimieren", "Minimise"), () => WindowState = WindowState.Minimized, C(0xFFFFFF, 230)));
            var maxGlyph = Cap("\uE922", L("Maximieren", "Maximise"), () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, C(0xFFFFFF, 230));
            stones.Children.Add(maxGlyph);
            StateChanged += (s, e) => ((TextBlock)maxGlyph.Child).Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            var close = Cap("\uE8BB", L("Schließen", "Close"), Close, A(ZenStyle.Momiji, 210));
            close.MouseEnter += (s, e) => ((TextBlock)close.Child).Foreground = Brushes.White;
            close.MouseLeave += (s, e) => ((TextBlock)close.Child).Foreground = B(ZenStyle.Ink);
            stones.Children.Add(close);
            DockPanel.SetDock(stones, Dock.Right);
            bar.Children.Add(stones);
            var icon = new Image { Source = Icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left);
            bar.Children.Add(icon);
            var title = Txt(Title, 13.5, ZenStyle.Ink, FontWeights.SemiBold);
            title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title") { Source = this });
            bar.Children.Add(title);
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(content);
            var g = new Grid();
            g.Children.Add(ZenStyle.Pane(dock, 0, 205, shadow: false));
            g.Children.Add(Overlay);
            Content = g;
        }

        /// <summary>A white pebble with the app's sign in its colour: the icon in the bar and Alt+Tab.</summary>
        static ImageSource AppIcon(string glyph, Color c)
        {
            var g = new Grid { Width = 64, Height = 64 };
            g.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Core.Applier.Mix(c, Colors.White, 0.25), c, 90) });
            g.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 30, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            g.Measure(new Size(64, 64)); g.Arrange(new Rect(0, 0, 64, 64));
            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(g); rtb.Freeze();
            return rtb;
        }

        protected static T Single<T>(ref T current, Func<T> make) where T : Window
        {
            if (current != null && current.IsLoaded)
            {
                if (current.WindowState == WindowState.Minimized) current.WindowState = WindowState.Normal;
                current.Activate();
                W32.SetForegroundWindow(new WindowInteropHelper(current).Handle);
                return current;
            }
            var w = make();
            current = w;
            w.Show();
            w.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(w).Handle);
            if (w.Content is FrameworkElement c) ZenStyle.Ripple(c, new Point(0.5, 0.5), 380);
            return w;
        }

        protected static TextBlock Txt(string s, double size, Color c, FontWeight? w = null) => ZenStyle.Text(s, size, c, w);

        /// <summary>A pill button; primary ones are pond green.</summary>
        protected static Border Pill(string text, Action a, bool primary = false, string glyph = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 6, 14, 7) };
            if (glyph != null) { var g = Glyph(glyph, 13, primary ? Colors.White : ZenStyle.Pond); g.Margin = new Thickness(0, 0, 7, 0); sp.Children.Add(g); }
            sp.Children.Add(Txt(text, 13, primary ? Colors.White : ZenStyle.Ink, FontWeights.SemiBold));
            var b = new Border { Child = sp, CornerRadius = new CornerRadius(16), Cursor = Cursors.Hand };
            ZenStyle.Press(b, primary ? ZenStyle.Pond : C(0xFFFFFF, 170), primary ? ZenStyle.PondDeep : C(0xFFFFFF, 235));
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; a(); };
            return b;
        }

        /// <summary>A soft rounded text field.</summary>
        protected static (Border box, TextBox tb) Field(string hint, double width = 0, string glyph = null)
        {
            var tb = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = ZenStyle.Family, FontSize = 13.5, Foreground = B(ZenStyle.Ink), CaretBrush = B(ZenStyle.Pond), VerticalContentAlignment = VerticalAlignment.Center };
            var hintTb = Txt(hint, 13.5, ZenStyle.Ink2);
            hintTb.IsHitTestVisible = false; hintTb.Margin = new Thickness(3, 0, 0, 0);
            tb.TextChanged += (s, e) => hintTb.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var g = new Grid();
            g.Children.Add(hintTb); g.Children.Add(tb);
            var dock = new DockPanel();
            if (glyph != null) { var gl = Glyph(glyph, 14, ZenStyle.Pond); gl.Margin = new Thickness(0, 0, 9, 0); DockPanel.SetDock(gl, Dock.Left); dock.Children.Add(gl); }
            dock.Children.Add(g);
            var box = new Border { Child = dock, CornerRadius = new CornerRadius(17), Background = B(0xFFFFFF, 185), Padding = new Thickness(14, 6, 14, 6), BorderThickness = new Thickness(1.5), BorderBrush = B(A(ZenStyle.Pond, 0)), Height = 34 };
            if (width > 0) box.Width = width;
            tb.GotKeyboardFocus += (s, e) => box.BorderBrush = B(A(ZenStyle.Pond, 150));
            tb.LostKeyboardFocus += (s, e) => box.BorderBrush = B(A(ZenStyle.Pond, 0));
            return (box, tb);
        }

        /// <summary>A question over the window (rename, new folder): a frosted card with a field and two pills.</summary>
        protected void Ask(string title, string value, Action<string> done)
        {
            var (box, tb) = Field("", 320);
            tb.Text = value ?? "";
            var card = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            card.Children.Add(Txt(title, 16, ZenStyle.Ink, FontWeights.SemiBold));
            box.Margin = new Thickness(0, 12, 0, 14);
            card.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            void Finish(string r) { Overlay.Visibility = Visibility.Collapsed; Overlay.Children.Clear(); done(r); }
            var cancel = Pill(L("Abbrechen", "Cancel"), () => Finish(null));
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
            buttons.Children.Add(Pill("OK", () => Finish(tb.Text), true));
            card.Children.Add(buttons);
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Finish(tb.Text); } else if (e.Key == Key.Escape) { e.Handled = true; Finish(null); } };
            var pane = ZenStyle.Pane(card, 22, 215);
            pane.HorizontalAlignment = HorizontalAlignment.Center; pane.VerticalAlignment = VerticalAlignment.Center;
            Overlay.Children.Clear();
            Overlay.Children.Add(new Border { Background = B(A(ZenStyle.PondDeep, 40)) });
            Overlay.Children.Add(pane);
            Overlay.Visibility = Visibility.Visible;
            ZenStyle.Ripple(pane, new Point(0.5, 0.5), 320);
            Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); var dot = tb.Text.LastIndexOf('.'); tb.Select(0, dot > 0 ? dot : tb.Text.Length); }), DispatcherPriority.Input);
        }

        /// <summary>A ListBox whose rows are rounded and tinted in the pond colours instead of Windows' blue.</summary>
        protected static ListBox ZenList()
        {
            var lb = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(lb, true);
            VirtualizingPanel.SetVirtualizationMode(lb, VirtualizationMode.Recycling);
            var item = new Style(typeof(ListBoxItem));
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));
            bd.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            tpl.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, B(0xFFFFFF, 120), "bd"));
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, B(A(ZenStyle.Pond, 48)), "bd"));
            tpl.Triggers.Add(hover); tpl.Triggers.Add(sel);
            item.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            item.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            lb.ItemContainerStyle = item;
            return lb;
        }
    }

    // =====================================================================
    // Dateien: Zen's explorer
    // =====================================================================
    internal sealed class ZenFiles : ZenWindow
    {
        static ZenFiles current;
        public static void Open(string folder = null)
        {
            var w = Single(ref current, () => new ZenFiles());
            w.Go(folder ?? w.dir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        public sealed class Entry
        {
            public string Path; public bool Dir; public long SizeV = -1; public DateTime TimeV;
            public ImageSource Icon { get; set; }
            public string Name { get; set; }
            public string Time { get; set; }
            public string Size { get; set; }
        }

        string dir;   // "" = this PC (the drives)
        readonly List<string> back = new List<string>(), fwd = new List<string>();
        readonly ListBox list;
        readonly StackPanel crumbs, places;
        readonly TextBox filter, address;
        readonly Grid addressHost;
        readonly TextBlock status;
        List<Entry> all = new List<Entry>();
        string sortKey = "name"; bool sortDesc;
        FileSystemWatcher watcher;
        readonly DispatcherTimer reload;
        Border backBtn, fwdBtn;

        ZenFiles() : base(L("Dateien", "Files"), "", ZenStyle.Pond, 1040, 680)
        {
            WindowTracker.StandsFor(this, "explorer.exe");
            MinWidth = 620; MinHeight = 400;
            var root = new DockPanel { Margin = new Thickness(14, 12, 14, 10) };

            // ---- top: back, forward, up; the path as pebbles; search ----
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var navStones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            backBtn = ZenStyle.Stone("", Back, L("Zurück (Alt+←)", "Back (Alt+←)"), 34);
            fwdBtn = ZenStyle.Stone("", Forward, L("Vorwärts (Alt+→)", "Forward (Alt+→)"), 34);
            var upBtn = ZenStyle.Stone("", Up, L("Eine Ebene höher (Rücktaste)", "Up one level (Backspace)"), 34);
            backBtn.Margin = fwdBtn.Margin = new Thickness(0, 0, 6, 0);
            navStones.Children.Add(backBtn); navStones.Children.Add(fwdBtn); navStones.Children.Add(upBtn);
            DockPanel.SetDock(navStones, Dock.Left);
            top.Children.Add(navStones);
            var (fbox, ftb) = Field(L("In diesem Ordner suchen", "Search this folder"), 240, "");
            filter = ftb;
            fbox.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(fbox, Dock.Right);
            top.Children.Add(fbox);
            crumbs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            address = new TextBox { Visibility = Visibility.Collapsed, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = ZenStyle.Family, FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = B(ZenStyle.Pond) };
            addressHost = new Grid { Cursor = Cursors.IBeam, Background = Brushes.Transparent };
            addressHost.Children.Add(crumbs); addressHost.Children.Add(address);
            var addrBox = new Border { Child = addressHost, CornerRadius = new CornerRadius(17), Background = B(0xFFFFFF, 150), Padding = new Thickness(8, 0, 12, 0), Height = 34, ClipToBounds = true };
            addressHost.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == addressHost || e.OriginalSource == crumbs) BeginEdit(); };
            address.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { var t = Environment.ExpandEnvironmentVariables(address.Text.Trim().Trim('"')); EndEdit(); Go(t); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndEdit(); e.Handled = true; }
            };
            address.LostKeyboardFocus += (s, e) => EndEdit();
            top.Children.Add(addrBox);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            status = Txt("", 12, ZenStyle.Ink2);
            status.Margin = new Thickness(6, 8, 0, 0);
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            // ---- left: places and drives ----
            places = new StackPanel { Width = 196, Margin = new Thickness(0, 0, 12, 0) };
            var placesScroll = new ScrollViewer { Content = places, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            DockPanel.SetDock(placesScroll, Dock.Left);
            root.Children.Add(placesScroll);
            BuildPlaces();

            // ---- the list with its column heads ----
            var main = new DockPanel();
            var heads = new Grid { Margin = new Thickness(10, 0, 10, 4) };
            Columns(heads);
            void Head(string text, string key, int col, HorizontalAlignment al = HorizontalAlignment.Left)
            {
                var tb = Txt(text, 12, ZenStyle.Ink2, FontWeights.SemiBold);
                tb.Cursor = Cursors.Hand; tb.HorizontalAlignment = al;
                tb.MouseLeftButtonUp += (s, e) => { if (sortKey == key) sortDesc = !sortDesc; else { sortKey = key; sortDesc = key != "name"; } Show(); };
                Grid.SetColumn(tb, col); heads.Children.Add(tb);
            }
            Head("Name", "name", 1); Head(L("Geändert", "Modified"), "time", 2); Head(L("Größe", "Size"), "size", 3, HorizontalAlignment.Right);
            DockPanel.SetDock(heads, Dock.Top);
            main.Children.Add(heads);
            list = ZenList();
            list.ItemTemplate = RowTemplate();
            TextSearch.SetTextPath(list, "Name");
            list.MouseDoubleClick += (s, e) => { if ((e.OriginalSource as DependencyObject) != null && list.SelectedItem != null) OpenSel(); };
            list.PreviewKeyDown += OnKey;
            list.MouseRightButtonUp += (s, e) => Menu();
            list.SelectionChanged += (s, e) => Status();
            main.Children.Add(list);
            var listPane = new Border { Child = main, CornerRadius = new CornerRadius(18), Background = B(0xFFFFFF, 110), Padding = new Thickness(6, 10, 6, 6) };
            root.Children.Add(listPane);
            filter.TextChanged += (s, e) => Show();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || e.SystemKey == Key.Left) { Back(); e.Handled = true; }
                else if (e.SystemKey == Key.Right) { Forward(); e.Handled = true; }
                else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { filter.Focus(); e.Handled = true; }
                else if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { BeginEdit(); e.Handled = true; }
            };
            MouseUp += (s, e) => { if (e.ChangedButton == MouseButton.XButton1) Back(); else if (e.ChangedButton == MouseButton.XButton2) Forward(); };
            Body(root);
            reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            reload.Tick += (s, e) => { reload.Stop(); Load(keep: true); };
            Closed += (s, e) => { watcher?.Dispose(); reload.Stop(); current = null; };
        }

        static void Columns(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        }

        static DataTemplate RowTemplate()
        {
            var t = new DataTemplate(typeof(Entry));
            var g = new FrameworkElementFactory(typeof(Grid));
            foreach (var w in new[] { new GridLength(30), new GridLength(1, GridUnitType.Star), new GridLength(150), new GridLength(90) })
            {
                var c = new FrameworkElementFactory(typeof(ColumnDefinition));
                c.SetValue(ColumnDefinition.WidthProperty, w);
                g.AppendChild(c);
            }
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Icon"));
            img.SetValue(FrameworkElement.WidthProperty, 20.0); img.SetValue(FrameworkElement.HeightProperty, 20.0);
            img.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            g.AppendChild(img);
            FrameworkElementFactory Text(string path, int col, Color c, HorizontalAlignment al = HorizontalAlignment.Left)
            {
                var tb = new FrameworkElementFactory(typeof(TextBlock));
                tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
                tb.SetValue(Grid.ColumnProperty, col);
                tb.SetValue(TextBlock.ForegroundProperty, B(c));
                tb.SetValue(TextBlock.FontFamilyProperty, ZenStyle.Family);
                tb.SetValue(TextBlock.FontSizeProperty, 13.5);
                tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                tb.SetValue(FrameworkElement.HorizontalAlignmentProperty, al);
                return tb;
            }
            g.AppendChild(Text("Name", 1, ZenStyle.Ink));
            g.AppendChild(Text("Time", 2, ZenStyle.Ink2));
            g.AppendChild(Text("Size", 3, ZenStyle.Ink2, HorizontalAlignment.Right));
            t.VisualTree = g;
            return t;
        }

        // ---------------- places ----------------
        readonly List<(Border b, string path)> placeRows = new List<(Border, string)>();

        void BuildPlaces()
        {
            places.Children.Clear(); placeRows.Clear();
            void Group(string title) { var tb = Txt(title, 12, ZenStyle.Ink2, FontWeights.SemiBold); tb.Margin = new Thickness(10, places.Children.Count == 0 ? 2 : 14, 0, 4); places.Children.Add(tb); }
            void Place(string glyph, string name, string path, Color tint, FrameworkElement extra = null)
            {
                var dock = new DockPanel();
                var g = Glyph(glyph, 15, tint); g.Width = 24; g.HorizontalAlignment = HorizontalAlignment.Left;
                DockPanel.SetDock(g, Dock.Left);
                dock.Children.Add(g);
                var words = new StackPanel();
                words.Children.Add(Txt(name, 13.5, ZenStyle.Ink));
                if (extra != null) words.Children.Add(extra);
                dock.Children.Add(words);
                var b = new Border { Child = dock, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                b.MouseEnter += (s, e) => { if (dir != path) b.Background = B(0xFFFFFF, 110); };
                b.MouseLeave += (s, e) => { if (dir != path) b.Background = Brushes.Transparent; };
                b.MouseLeftButtonUp += (s, e) => Go(path);
                places.Children.Add(b); placeRows.Add((b, path));
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Group(L("Orte", "Places"));
            Place("", ShellActions.UserName, home, ZenStyle.Pond);
            Place("", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ZenStyle.Pond);
            Place("", "Downloads", System.IO.Path.Combine(home, "Downloads"), ZenStyle.Koi);
            Place("", L("Dokumente", "Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ZenStyle.Pond);
            Place("", L("Bilder", "Pictures"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), ZenStyle.Blossom);
            Place("", L("Musik", "Music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), ZenStyle.Moss);
            Place("", L("Videos", "Videos"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), ZenStyle.Moss);
            Group(L("Laufwerke", "Drives"));
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Network)))
            {
                FrameworkElement bar = null;
                try
                {
                    double used = 1 - d.TotalFreeSpace / (double)Math.Max(1, d.TotalSize);
                    var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, 128 * used), Background = B(used > 0.9 ? ZenStyle.Momiji : ZenStyle.Pond) };
                    var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
                    stack.Children.Add(new Border { Width = 128, Height = 6, CornerRadius = new CornerRadius(3), Background = B(C(0x8A9A9C, 70)), Child = fill, HorizontalAlignment = HorizontalAlignment.Left });
                    stack.Children.Add(Txt(Gb(d.TotalFreeSpace) + L(" frei", " free"), 11, ZenStyle.Ink2));
                    bar = stack;
                }
                catch { }
                Place(d.DriveType == DriveType.Removable ? "" : "", DriveName(d), d.RootDirectory.FullName, ZenStyle.Ink2, bar);
            }
        }

        static string DriveName(DriveInfo d)
        {
            string label = "";
            try { label = d.VolumeLabel; } catch { }
            if (string.IsNullOrWhiteSpace(label)) label = d.DriveType == DriveType.Removable ? L("Wechseldatenträger", "Removable drive") : L("Lokaler Datenträger", "Local disk");
            return label + " (" + d.Name.TrimEnd('\\') + ")";
        }

        static string Gb(long b) => b >= 1L << 40 ? (b / (double)(1L << 40)).ToString("0.0") + " TB" : (b / 1073741824.0).ToString("0") + " GB";

        void MarkPlaces() { foreach (var (b, p) in placeRows) b.Background = string.Equals(p.TrimEnd('\\'), (dir ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? B(A(ZenStyle.Pond, 48)) : Brushes.Transparent; }

        // ---------------- navigation ----------------
        public void Go(string target, bool remember = true)
        {
            if (target == null) return;
            if (target.Length > 0 && File.Exists(target) && !Directory.Exists(target))
            {
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; }
                return;
            }
            if (target.Length > 0 && !Directory.Exists(target)) { status.Text = L("Nicht gefunden: ", "Not found: ") + target; return; }
            if (remember && dir != null && dir != target) { back.Add(dir); fwd.Clear(); }
            dir = target;
            filter.Text = "";
            Load();
        }

        void Back() { if (back.Count == 0) return; fwd.Add(dir); var d = back[back.Count - 1]; back.RemoveAt(back.Count - 1); Go(d, false); }
        void Forward() { if (fwd.Count == 0) return; back.Add(dir); var d = fwd[fwd.Count - 1]; fwd.RemoveAt(fwd.Count - 1); Go(d, false); }
        void Up()
        {
            if (string.IsNullOrEmpty(dir)) return;
            var parent = Directory.GetParent(dir.TrimEnd('\\') + (dir.EndsWith(":\\") ? "" : ""));
            Go(dir.Length <= 3 ? "" : parent?.FullName ?? "");
        }

        void Watch()
        {
            watcher?.Dispose(); watcher = null;
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => { reload.Stop(); reload.Start(); }));
                watcher.Created += h; watcher.Deleted += h; watcher.Changed += h; watcher.Renamed += (s, e) => h(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void Load(string select = null, bool keep = false)
        {
            var keepSel = keep ? new HashSet<string>(list.SelectedItems.Cast<Entry>().Select(x => x.Path)) : null;
            all = new List<Entry>();
            var ci = De ? new System.Globalization.CultureInfo("de-DE") : new System.Globalization.CultureInfo("en-US");
            if (string.IsNullOrEmpty(dir))
            {
                foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
                {
                    string size = ""; try { size = Gb(d.TotalFreeSpace) + L(" frei", " free"); } catch { }
                    all.Add(new Entry { Path = d.RootDirectory.FullName, Dir = true, Name = DriveName(d), Icon = Icon(d.RootDirectory.FullName, true), Size = size });
                }
            }
            else
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var x in di.EnumerateFileSystemInfos())
                    {
                        if ((x.Attributes & FileAttributes.Hidden) != 0 || (x.Attributes & FileAttributes.System) != 0) continue;
                        bool isDir = (x.Attributes & FileAttributes.Directory) != 0;
                        long size = isDir ? -1 : ((FileInfo)x).Length;
                        all.Add(new Entry { Path = x.FullName, Dir = isDir, Name = x.Name, TimeV = x.LastWriteTime, Time = x.LastWriteTime.ToString("g", ci), SizeV = size, Size = isDir ? "" : Size(size), Icon = Icon(x.FullName, isDir) });
                    }
                }
                catch (Exception ex) { status.Text = ex.Message; }
            }
            Show(keepSel);
            if (select != null) { var hit = all.FirstOrDefault(x => x.Name.Equals(select, StringComparison.OrdinalIgnoreCase)); if (hit != null) { list.SelectedItem = hit; list.ScrollIntoView(hit); } }
            Crumbs(); MarkPlaces(); Watch();
            backBtn.Opacity = back.Count > 0 ? 1 : 0.45; fwdBtn.Opacity = fwd.Count > 0 ? 1 : 0.45;
            Title = string.IsNullOrEmpty(dir) ? L("Dieser PC", "This PC") : (System.IO.Path.GetFileName(dir.TrimEnd('\\')) is string n && n.Length > 0 ? n : dir);
        }

        static string Size(long b) => b < 1024 ? b + " B" : b < 1 << 20 ? (b / 1024.0).ToString("0") + " KB" : b < 1 << 30 ? (b / 1048576.0).ToString("0.0") + " MB" : (b / 1073741824.0).ToString("0.0") + " GB";

        static readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> OwnIcon = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".url", ".msc", ".scr" };
        static ImageSource folder;
        /// <summary>Zen's folder: a rounded pond-green folder instead of Windows' yellow one.</summary>
        static ImageSource Folder()
        {
            if (folder != null) return folder;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new LinearGradientBrush(C(0x0B6A67), C(0x0B4F5C), 90), null, new Rect(2, 6, 28, 22), 4, 4);
                dc.DrawRoundedRectangle(B(0x0B5A60), null, new Rect(2, 4, 12, 6), 2.5, 2.5);
                dc.DrawRoundedRectangle(new LinearGradientBrush(C(0x3FB0A4), ZenStyle.Pond, 90), null, new Rect(2, 10, 28, 18), 4, 4);
                dc.DrawRectangle(B(0xFFFFFF, 60), null, new Rect(5, 11.5, 22, 1.2));
            }
            var rtb = new RenderTargetBitmap(64, 64, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return folder = rtb;
        }

        static ImageSource Icon(string path, bool isDir)
        {
            if (isDir && path.Length > 3) return Folder();
            var ext = isDir ? "drive:" + path : System.IO.Path.GetExtension(path);
            if (!isDir && OwnIcon.Contains(ext)) return W32.FileIcon(path, false);
            if (icons.TryGetValue(ext, out var hit)) return hit;
            return icons[ext] = W32.FileIcon(path, false);
        }

        void Show(HashSet<string> keep = null)
        {
            IEnumerable<Entry> q = all;
            var f = filter.Text.Trim();
            if (f.Length > 0) q = q.Where(x => x.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0);
            Func<Entry, object> key = sortKey == "time" ? (Func<Entry, object>)(x => x.TimeV) : sortKey == "size" ? (x => x.SizeV) : (x => x.Name);
            q = q.OrderByDescending(x => x.Dir);
            q = sortDesc ? ((IOrderedEnumerable<Entry>)q).ThenByDescending(key) : ((IOrderedEnumerable<Entry>)q).ThenBy(key, sortKey == "name" ? (IComparer<object>)new NameOrder() : Comparer<object>.Default);
            var items = q.ToList();
            list.ItemsSource = items;
            if (keep != null) foreach (var x in items.Where(x => keep.Contains(x.Path))) list.SelectedItems.Add(x);
            Status();
        }

        sealed class NameOrder : IComparer<object>
        {
            [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
            public int Compare(object a, object b) => StrCmpLogicalW(a as string ?? "", b as string ?? "");
        }

        void Status()
        {
            int n = (list.ItemsSource as List<Entry>)?.Count ?? 0, s = list.SelectedItems.Count;
            status.Text = n + (n == 1 ? L(" Element", " item") : L(" Elemente", " items")) + (s > 0 ? "   ·   " + s + L(" ausgewählt", " selected") : "");
        }

        void Crumbs()
        {
            crumbs.Children.Clear();
            var parts = new List<(string name, string path)> { (L("Dieser PC", "This PC"), "") };
            if (!string.IsNullOrEmpty(dir))
            {
                var root = System.IO.Path.GetPathRoot(dir);
                parts.Add((root.TrimEnd('\\'), root));
                var acc = root;
                foreach (var seg in dir.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { acc = System.IO.Path.Combine(acc, seg); parts.Add((seg, acc)); }
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var (name, path) = parts[i];
                bool last = i == parts.Count - 1;
                var tb = Txt(name, 13, last ? ZenStyle.Ink : ZenStyle.Ink2, last ? FontWeights.SemiBold : FontWeights.Normal);
                var chip = new Border { Child = tb, CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 4, 8, 5), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                ZenStyle.Press(chip, C(0xFFFFFF, 0), C(0xFFFFFF, 190));
                chip.MouseLeftButtonUp += (s, e) => { e.Handled = true; Go(path); };
                crumbs.Children.Add(chip);
                if (!last) crumbs.Children.Add(Txt("›", 13, ZenStyle.Ink2));
            }
        }

        void BeginEdit() { address.Text = dir ?? ""; crumbs.Visibility = Visibility.Collapsed; address.Visibility = Visibility.Visible; address.Focus(); address.SelectAll(); }
        void EndEdit() { address.Visibility = Visibility.Collapsed; crumbs.Visibility = Visibility.Visible; }

        // ---------------- actions ----------------
        List<Entry> Sel => list.SelectedItems.Cast<Entry>().ToList();

        void OnKey(object s, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Enter: OpenSel(); break;
                case Key.Back: Up(); break;
                case Key.Delete: Delete(shift); break;
                case Key.F2: Rename(); break;
                case Key.F5: Load(keep: true); break;
                case Key.C when ctrl: ToClipboard(false); break;
                case Key.X when ctrl: ToClipboard(true); break;
                case Key.V when ctrl: Paste(); break;
                case Key.N when ctrl && shift: NewFolder(); break;
                default: return;
            }
            e.Handled = true;
        }

        void Menu()
        {
            var sel = Sel;
            bool fs = !string.IsNullOrEmpty(dir);
            var items = new List<(string, Action)>();
            if (sel.Count > 0)
            {
                items.Add((L("Öffnen", "Open"), OpenSel));
                if (fs)
                {
                    items.Add(("-", null));
                    items.Add((L("Ausschneiden", "Cut"), () => ToClipboard(true)));
                    items.Add((L("Kopieren", "Copy"), () => ToClipboard(false)));
                    items.Add((L("Umbenennen", "Rename"), Rename));
                    items.Add((L("Löschen", "Delete"), () => Delete(false)));
                }
                items.Add(("-", null));
                items.Add((L("Eigenschaften", "Properties"), Properties));
            }
            else if (fs)
            {
                items.Add((L("Einfügen", "Paste"), Paste));
                items.Add((L("Neuer Ordner", "New folder"), NewFolder));
                items.Add((L("Terminal hier öffnen", "Open terminal here"), () => ShellActions.Run("wt.exe", "-d \"" + dir + "\"")));
                items.Add(("-", null));
                items.Add((L("Eigenschaften", "Properties"), Properties));
            }
            if (items.Count > 0) MiniMenu.Show(Theme, items);
        }

        void OpenSel()
        {
            var sel = Sel;
            if (sel.Count == 0) return;
            if (sel.Count == 1 && sel[0].Dir) { Go(sel[0].Path); return; }
            foreach (var x in sel.Where(x => !x.Dir))
            {
                try { Process.Start(new ProcessStartInfo(x.Path) { UseShellExecute = true, WorkingDirectory = dir }); }
                catch (Exception ex) { status.Text = x.Name + ": " + ex.Message; }
            }
        }

        void ToClipboard(bool cut)
        {
            var sel = Sel;
            if (sel.Count == 0 || string.IsNullOrEmpty(dir)) return;
            var files = new StringCollection();
            foreach (var x in sel) files.Add(x.Path);
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            try { Clipboard.SetDataObject(data, true); status.Text = sel.Count + (cut ? L(" ausgeschnitten", " cut") : L(" kopiert", " copied")); } catch { }
        }

        void Paste()
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (!Clipboard.ContainsFileDropList()) return;
                var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                bool move = Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4 && (new BinaryReader(ms).ReadInt32() & 2) != 0;
                var target = dir;
                Task.Run(() => HackerFiles.FileOp(move ? HackerFiles.FO_MOVE : HackerFiles.FO_COPY, files, target, (ushort)(HackerFiles.FOF_ALLOWUNDO | HackerFiles.FOF_RENAMEONCOLLISION)))
                    .ContinueWith(_ => { if (move) Clipboard.Clear(); Load(); }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch { }
        }

        void Delete(bool permanent)
        {
            var sel = Sel;
            if (sel.Count == 0 || string.IsNullOrEmpty(dir)) return;
            // Windows' own delete: into the recycle bin, with undo (it asks only before deleting for good)
            Task.Run(() => HackerFiles.FileOp(HackerFiles.FO_DELETE, sel.Select(x => x.Path).ToArray(), null, permanent ? (ushort)0 : (ushort)(HackerFiles.FOF_ALLOWUNDO | HackerFiles.FOF_NOCONFIRMATION)))
                .ContinueWith(_ => Load(), TaskScheduler.FromCurrentSynchronizationContext());
        }

        static bool ValidName(string n) => !string.IsNullOrWhiteSpace(n) && n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;

        void Rename()
        {
            var sel = Sel;
            if (sel.Count != 1 || string.IsNullOrEmpty(dir)) return;
            var x = sel[0];
            Ask(L("Umbenennen", "Rename"), x.Name, name =>
            {
                list.Focus();
                if (name == null || name == x.Name) return;
                if (!ValidName(name)) { status.Text = L("Ein Name darf keines dieser Zeichen enthalten: \\ / : * ? \" < > |", "A name can't contain any of these characters: \\ / : * ? \" < > |"); return; }
                try { var target = System.IO.Path.Combine(dir, name); if (x.Dir) Directory.Move(x.Path, target); else File.Move(x.Path, target); Load(name); }
                catch (Exception ex) { status.Text = ex.Message; }
            });
        }

        void NewFolder()
        {
            if (string.IsNullOrEmpty(dir)) return;
            var baseName = L("Neuer Ordner", "New folder");
            var name = baseName;
            for (int i = 2; Directory.Exists(System.IO.Path.Combine(dir, name)) || File.Exists(System.IO.Path.Combine(dir, name)); i++) name = baseName + " (" + i + ")";
            Ask(baseName, name, chosen =>
            {
                list.Focus();
                if (chosen == null) return;
                if (!ValidName(chosen)) { status.Text = L("Ungültiger Name", "Invalid name"); return; }
                try { Directory.CreateDirectory(System.IO.Path.Combine(dir, chosen)); Load(chosen); } catch (Exception ex) { status.Text = ex.Message; }
            });
        }

        void Properties()
        {
            var sel = Sel;
            HackerFiles.ShowProperties(sel.Count == 1 ? sel[0].Path : string.IsNullOrEmpty(dir) ? "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" : dir);
        }
    }

    // =====================================================================
    // Aktivität: Zen's task manager
    // =====================================================================
    internal sealed class ZenTasks : ZenWindow
    {
        static ZenTasks current;
        public static void Open() => Single(ref current, () => new ZenTasks());

        public sealed class Proc
        {
            public int Pid; public double CpuV; public long MemV; public IntPtr Hwnd; public string PathV;
            public ImageSource Icon { get; set; }
            public string Name { get; set; }
            public string Cpu { get; set; }
            public string Mem { get; set; }
        }

        readonly ListBox list;
        readonly TextBox filter;
        readonly TextBlock status, cpuText, memText, countText;
        readonly Polyline cpuLine, memLine;
        readonly Queue<double> cpuHist = new Queue<double>(), memHist = new Queue<double>();
        readonly DispatcherTimer tick;
        Dictionary<int, TimeSpan> lastCpu; DateTime lastAt = DateTime.Now;
        bool busy;
        static readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        ZenTasks() : base(L("Aktivität", "Activity"), "", ZenStyle.Koi, 900, 640)
        {
            WindowTracker.StandsFor(this, "taskmgr.exe");
            MinWidth = 600; MinHeight = 420;
            var root = new DockPanel { Margin = new Thickness(16, 14, 16, 10) };

            // two gauges: processor and memory, each with its line of the last minute
            var gauges = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            gauges.ColumnDefinitions.Add(new ColumnDefinition()); gauges.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); gauges.ColumnDefinitions.Add(new ColumnDefinition()); gauges.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); gauges.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            Border Gauge(string title, Color c, out TextBlock value, out Polyline line)
            {
                var g = new Grid { Height = 92 };
                line = new Polyline { Stroke = B(c), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.9 };
                var canvas = new Canvas { ClipToBounds = true };
                canvas.Children.Add(line);
                g.Children.Add(canvas);
                var words = new StackPanel { Margin = new Thickness(14, 10, 0, 0) };
                words.Children.Add(Txt(title, 12, ZenStyle.Ink2, FontWeights.SemiBold));
                value = Txt("", 30, ZenStyle.Ink, FontWeights.Light);
                words.Children.Add(value);
                g.Children.Add(words);
                return new Border { Child = g, CornerRadius = new CornerRadius(18), Background = B(0xFFFFFF, 130), ClipToBounds = true };
            }
            var cpuCard = Gauge(L("Prozessor", "Processor"), ZenStyle.Koi, out cpuText, out cpuLine);
            var memCard = Gauge(L("Arbeitsspeicher", "Memory"), ZenStyle.Pond, out memText, out memLine);
            Grid.SetColumn(memCard, 2);
            gauges.Children.Add(cpuCard); gauges.Children.Add(memCard);
            var countCard = new StackPanel { Margin = new Thickness(14, 10, 0, 0) };
            countCard.Children.Add(Txt(L("Prozesse", "Processes"), 12, ZenStyle.Ink2, FontWeights.SemiBold));
            countText = Txt("", 30, ZenStyle.Ink, FontWeights.Light);
            countCard.Children.Add(countText);
            var cc = new Border { Child = countCard, CornerRadius = new CornerRadius(18), Background = B(0xFFFFFF, 130) };
            Grid.SetColumn(cc, 4);
            gauges.Children.Add(cc);
            DockPanel.SetDock(gauges, Dock.Top);
            root.Children.Add(gauges);

            var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var end = Pill(L("Beenden", "End task"), Kill, false, "");
            DockPanel.SetDock(end, Dock.Right);
            tools.Children.Add(end);
            var where = Pill(L("Speicherort", "Location"), Location, false, "");
            where.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(where, Dock.Right);
            tools.Children.Add(where);
            var (fbox, ftb) = Field(L("Prozesse suchen", "Search processes"), 260, "");
            filter = ftb;
            fbox.HorizontalAlignment = HorizontalAlignment.Left;
            tools.Children.Add(fbox);
            DockPanel.SetDock(tools, Dock.Top);
            root.Children.Add(tools);

            status = Txt("", 12, ZenStyle.Ink2);
            status.Margin = new Thickness(6, 8, 0, 0);
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            var main = new DockPanel();
            var heads = new Grid { Margin = new Thickness(10, 0, 10, 4) };
            foreach (var w in new[] { new GridLength(30), new GridLength(1, GridUnitType.Star), new GridLength(90), new GridLength(110) }) heads.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            void Head(string t, int col) { var tb = Txt(t, 12, ZenStyle.Ink2, FontWeights.SemiBold); if (col > 1) tb.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(tb, col); heads.Children.Add(tb); }
            Head("Name", 1); Head(L("Prozessor", "CPU"), 2); Head(L("Speicher", "Memory"), 3);
            DockPanel.SetDock(heads, Dock.Top);
            main.Children.Add(heads);
            list = ZenList();
            list.SelectionMode = SelectionMode.Single;
            list.ItemTemplate = Template();
            list.MouseDoubleClick += (s, e) => SwitchTo();
            list.PreviewKeyDown += (s, e) => { if (e.Key == Key.Delete) { Kill(); e.Handled = true; } else if (e.Key == Key.Enter) { SwitchTo(); e.Handled = true; } };
            list.MouseRightButtonUp += (s, e) =>
            {
                if (!(list.SelectedItem is Proc p)) return;
                var items = new List<(string, Action)>();
                if (p.Hwnd != IntPtr.Zero) items.Add((L("Wechseln zu", "Switch to"), SwitchTo));
                items.Add((L("Speicherort öffnen", "Open file location"), Location));
                items.Add(("-", null));
                items.Add((L("Beenden", "End task"), Kill));
                MiniMenu.Show(Theme, items);
            };
            main.Children.Add(list);
            root.Children.Add(new Border { Child = main, CornerRadius = new CornerRadius(18), Background = B(0xFFFFFF, 110), Padding = new Thickness(6, 10, 6, 6) });
            filter.TextChanged += (s, e) => Refresh();
            Body(root);
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            tick.Tick += (s, e) => Refresh();
            foreach (var v in SysInfo.CpuHistory) Push(cpuHist, v);
            Loaded += (s, e) => { Refresh(); tick.Start(); };
            Closed += (s, e) => { tick.Stop(); current = null; };
        }

        static DataTemplate Template()
        {
            var t = new DataTemplate(typeof(Proc));
            var g = new FrameworkElementFactory(typeof(Grid));
            foreach (var w in new[] { new GridLength(30), new GridLength(1, GridUnitType.Star), new GridLength(90), new GridLength(110) })
            { var c = new FrameworkElementFactory(typeof(ColumnDefinition)); c.SetValue(ColumnDefinition.WidthProperty, w); g.AppendChild(c); }
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Icon"));
            img.SetValue(FrameworkElement.WidthProperty, 20.0); img.SetValue(FrameworkElement.HeightProperty, 20.0);
            img.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            g.AppendChild(img);
            foreach (var (path, col, c) in new[] { ("Name", 1, ZenStyle.Ink), ("Cpu", 2, ZenStyle.Ink2), ("Mem", 3, ZenStyle.Ink2) })
            {
                var tb = new FrameworkElementFactory(typeof(TextBlock));
                tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
                tb.SetValue(Grid.ColumnProperty, col);
                tb.SetValue(TextBlock.ForegroundProperty, B(c));
                tb.SetValue(TextBlock.FontFamilyProperty, ZenStyle.Family);
                tb.SetValue(TextBlock.FontSizeProperty, 13.5);
                tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                if (col > 1) tb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
                g.AppendChild(tb);
            }
            t.VisualTree = g;
            return t;
        }

        void Refresh()
        {
            if (busy) return;
            busy = true;
            double cpu = SysInfo.CpuSample(), mem = SysStats.Mem();
            Push(cpuHist, cpu); Push(memHist, mem);
            cpuText.Text = cpu.ToString("0") + " %"; memText.Text = mem.ToString("0") + " %";
            Draw(cpuLine, cpuHist); Draw(memLine, memHist);
            var windows = new Dictionary<int, IntPtr>();
            try { foreach (var w in ShellActions.Windows?.Invoke() ?? new List<WinInfo>()) { W32.GetWindowThreadProcessId(w.Hwnd, out uint pid); if (!windows.ContainsKey((int)pid)) windows[(int)pid] = w.Hwnd; } } catch { }
            var f = filter.Text.Trim();
            Task.Run(() =>
            {
                var now = DateTime.Now;
                var cur = new Dictionary<int, TimeSpan>();
                var res = new List<Proc>();
                double sec = Math.Max(0.2, (now - lastAt).TotalSeconds);
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        double pc = 0;
                        try { var ct = p.TotalProcessorTime; cur[p.Id] = ct; if (lastCpu != null && lastCpu.TryGetValue(p.Id, out var old)) pc = (ct - old).TotalSeconds / sec * 100 / Environment.ProcessorCount; } catch { }
                        string path = null; try { path = p.MainModule?.FileName; } catch { }
                        res.Add(new Proc { Pid = p.Id, Name = p.ProcessName, CpuV = pc, MemV = p.WorkingSet64, PathV = path, Hwnd = windows.TryGetValue(p.Id, out var h) ? h : IntPtr.Zero });
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                lastCpu = cur; lastAt = now;
                return res;
            }).ContinueWith(t =>
            {
                busy = false;
                var procs = t.Result;
                countText.Text = procs.Count.ToString();
                // processes with a window are named after the window's program
                foreach (var p in procs)
                {
                    p.Cpu = p.CpuV < 0.05 ? "0 %" : p.CpuV.ToString(p.CpuV < 10 ? "0.0" : "0") + " %";
                    p.Mem = (p.MemV / 1048576.0).ToString("0") + " MB";
                    if (p.PathV != null)
                    {
                        if (!icons.TryGetValue(p.PathV, out var ic)) icons[p.PathV] = ic = W32.FileIcon(p.PathV, false);
                        p.Icon = ic;
                        try { var d = FileVersionInfo.GetVersionInfo(p.PathV).FileDescription; if (!string.IsNullOrWhiteSpace(d)) p.Name = d; } catch { }
                    }
                }
                IEnumerable<Proc> q = procs;
                if (f.Length > 0) q = q.Where(p => p.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0 || p.Pid.ToString() == f);
                var sorted = q.OrderByDescending(p => p.Hwnd != IntPtr.Zero).ThenByDescending(p => p.CpuV).ThenByDescending(p => p.MemV).ToList();
                int selPid = (list.SelectedItem as Proc)?.Pid ?? -1;
                list.ItemsSource = sorted;
                var again = sorted.FirstOrDefault(p => p.Pid == selPid);
                if (again != null) list.SelectedItem = again;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static void Push(Queue<double> q, double v) { q.Enqueue(v); while (q.Count > 60) q.Dequeue(); }

        static void Draw(Polyline line, Queue<double> hist)
        {
            var canvas = (Canvas)line.Parent;
            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w <= 0) return;
            var pts = new PointCollection();
            int i = 0, n = hist.Count;
            foreach (var v in hist) { pts.Add(new Point(w - (n - 1 - i) * (w / 59.0), h - 6 - (h * 0.55) * v / 100.0)); i++; }
            line.Points = pts;
        }

        void Kill()
        {
            if (!(list.SelectedItem is Proc p)) return;
            status.Text = HackerPanel.Kill(p.Pid, p.Name);
            Refresh();
        }

        void SwitchTo()
        {
            if (list.SelectedItem is Proc p && p.Hwnd != IntPtr.Zero) { W32.Activate(p.Hwnd); }
        }

        void Location()
        {
            if (list.SelectedItem is Proc p && p.PathV != null) ShellActions.Files(System.IO.Path.GetDirectoryName(p.PathV));
        }
    }

    // =====================================================================
    // Ausführen: one line, run with Enter (Ctrl+Shift+Enter as administrator)
    // =====================================================================
    internal sealed class ZenRun : Window
    {
        static ZenRun current;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        public static void Open()
        {
            if (current != null) { current.Activate(); return; }
            current = new ZenRun();
            current.Show();
            current.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(current).Handle);
        }

        ZenRun()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = ZenStyle.Family;
            var stack = new StackPanel { Margin = new Thickness(18, 16, 18, 14), Width = 460 };
            var head = new DockPanel();
            var g = Glyph("", 16, ZenStyle.Pond); g.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(g, Dock.Left);
            head.Children.Add(g);
            var box = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = ZenStyle.Family, FontSize = 18, Foreground = B(ZenStyle.Ink), CaretBrush = B(ZenStyle.Pond) };
            var hint = ZenStyle.Text(L("Programm, Ordner, Dokument oder Adresse", "Program, folder, document or address"), 18, ZenStyle.Ink2);
            hint.IsHitTestVisible = false; hint.Margin = new Thickness(3, 0, 0, 0);
            box.TextChanged += (s, e) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var g2 = new Grid(); g2.Children.Add(hint); g2.Children.Add(box);
            head.Children.Add(g2);
            stack.Children.Add(new Border { Child = head, CornerRadius = new CornerRadius(20), Background = B(0xFFFFFF, 180), Padding = new Thickness(14, 8, 14, 9) });
            var error = ZenStyle.Text("", 12.5, ZenStyle.Momiji);
            error.TextWrapping = TextWrapping.Wrap; error.Margin = new Thickness(6, 8, 0, 0); error.Visibility = Visibility.Collapsed;
            stack.Children.Add(error);
            // the last commands, one click away
            var recent = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            List<string> hist = new List<string>();
            try { if (File.Exists(HackerPanel.RunHistoryFile)) hist = File.ReadAllLines(HackerPanel.RunHistoryFile).Where(x => x.Trim().Length > 0).Reverse().Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList(); } catch { }
            void Run(string text, bool admin)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var err = HackerPanel.RunCommand(text, admin);
                if (err != null) { error.Text = err; error.Visibility = Visibility.Visible; return; }
                try
                {
                    var allLines = File.Exists(HackerPanel.RunHistoryFile) ? File.ReadAllLines(HackerPanel.RunHistoryFile).ToList() : new List<string>();
                    allLines.Add(text.Trim());
                    File.WriteAllLines(HackerPanel.RunHistoryFile, allLines.Skip(Math.Max(0, allLines.Count - 30)));
                }
                catch { }
                Close();
            }
            foreach (var h in hist)
            {
                var x = h;
                var row = new Border { Child = ZenStyle.Text(x, 13.5, ZenStyle.Ink), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 6, 14, 7), Cursor = Cursors.Hand };
                ZenStyle.Press(row, C(0xFFFFFF, 0), C(0xFFFFFF, 150));
                row.MouseLeftButtonUp += (s, e) => Run(x, false);
                recent.Children.Add(row);
            }
            if (hist.Count > 0) stack.Children.Add(recent);
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Run(box.Text, (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)); }
            };
            Content = new Border { Child = ZenStyle.Pane(stack, 24, 185), Padding = new Thickness(26, 18, 26, 30) };
            Deactivated += (s, e) => { try { Close(); } catch { } };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closed += (s, e) => current = null;
            RenderGuard.Watch(this);
            Loaded += (s, e) =>
            {
                var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
                double wl = mi.rcWork.Left / sc, wr = mi.rcWork.Right / sc, wt = mi.rcWork.Top / sc, wb = mi.rcWork.Bottom / sc;
                Left = wl + (wr - wl - ActualWidth) / 2; Top = wt + (wb - wt) * 0.28;
                ZenStyle.Ripple((FrameworkElement)Content, new Point(0.5, 0.5), 360);
                box.Focus();
            };
        }
    }
}

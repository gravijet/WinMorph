using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;
using Item = WinMorph.Shell.MacMenu.Item;

namespace WinMorph.Shell
{
    /// <summary>Names and icons of the look's own windows (for the menu bar and the Dock), Trash state, About and Force Quit.</summary>
    internal static class MacApps
    {
        static readonly Dictionary<IntPtr, (string name, string icon)> own = new Dictionary<IntPtr, (string, string)>();
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        public static void Register(IntPtr h, string name, string icon) => own[h] = (name, icon);
        public static void Unregister(IntPtr h) => own.Remove(h);
        public static string NameOf(IntPtr h) => own.TryGetValue(h, out var v) ? v.name : null;
        public static ImageSource IconOf(IntPtr h) => own.TryGetValue(h, out var v) ? MacStyle.Own(v.icon) : null;

        /// <summary>The name macOS would show for a window's app: its Start menu name, Finder for Explorer.</summary>
        public static string AppName(WinInfo w)
        {
            var mine = NameOf(w.Hwnd);
            if (mine != null) return mine;
            string exe = w.ExePath != null ? Path.GetFileName(w.ExePath) : "";
            if (exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) return "Finder";
            if (exe.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)) return "Terminal";
            var e = (w.AppId != null ? AppCatalog.Apps.FirstOrDefault(a => string.Equals(a.ParsingPath, w.AppId, StringComparison.OrdinalIgnoreCase)) : null)
                    ?? AppCatalog.Apps.FirstOrDefault(a => a.Matches(w));
            var n = (e?.Name ?? w.AppName ?? w.Title ?? "").Trim();
            return n.Length > 28 ? n.Substring(0, 28).TrimEnd() + "…" : n;
        }

        [StructLayout(LayoutKind.Sequential)] struct SHQUERYRBINFO { public int cbSize; public long i64Size, i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);

        public static bool TrashFull()
        {
            try { var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) }; return SHQueryRecycleBin(null, ref i) == 0 && i.i64NumItems > 0; }
            catch { return false; }
        }

        // ---------------- About This PC ----------------
        static Window about;
        public static void About()
        {
            if (about != null) { about.Activate(); return; }
            var w = new Window { Title = L("Über diesen PC", "About This PC"), ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = B(0xF4F4F6) };
            MacStyle.Prepare(w);
            w.Icon = MacStyle.Own("settings");
            MacWindow.Chrome(w, 40);
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            var lights = MacStyle.TrafficLights(w, 12);
            lights.Margin = new Thickness(14, 14, 0, 0); lights.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(lights);
            bool laptop = Hardware.Battery() != null;
            var pic = new Image { Source = Computer(laptop), Width = 150, Height = 110, Margin = new Thickness(0, 10, 0, 8) };
            sp.Children.Add(pic);
            string model = Reg(@"HKLM\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName") ?? (laptop ? "Notebook" : "PC");
            var name = MacStyle.Text(Environment.MachineName, 22, MacStyle.Label, FontWeights.Bold, display: true);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(name);
            var sub = MacStyle.Text(model, 11.5, MacStyle.Secondary);
            sub.HorizontalAlignment = HorizontalAlignment.Center; sub.Margin = new Thickness(0, 2, 0, 14);
            sp.Children.Add(sub);
            SysStats.MemTotals(out ulong total, out _);
            string os = (Reg(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows");
            int build = 0; int.TryParse(Reg(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild") ?? "0", out build);
            if (build >= 22000) os = os.Replace("Windows 10", "Windows 11");
            os += " " + (Reg(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") ?? "");
            var rows = new List<(string, string)>
            {
                (L("Prozessor", "Processor"), (Reg(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "").Trim()),
                (L("Speicher", "Memory"), Math.Round(total / 1073741824.0) + " GB"),
                (L("Startvolume", "Startup disk"), StartDisk()),
                ("Windows", os.Trim()),
            };
            var grid = new Grid { Margin = new Thickness(18, 0, 18, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            int r = 0;
            foreach (var (k, v) in rows)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                grid.RowDefinitions.Add(new RowDefinition());
                var kt = MacStyle.Text(k, 11.5, MacStyle.Label); kt.HorizontalAlignment = HorizontalAlignment.Right; kt.Margin = new Thickness(0, 2, 8, 2); kt.VerticalAlignment = VerticalAlignment.Top;
                var vt = MacStyle.Text(v, 11.5, MacStyle.Secondary); vt.TextWrapping = TextWrapping.Wrap; vt.Margin = new Thickness(0, 2, 0, 2);
                Grid.SetRow(kt, r); Grid.SetRow(vt, r); Grid.SetColumn(vt, 1);
                grid.Children.Add(kt); grid.Children.Add(vt);
                r++;
            }
            sp.Children.Add(grid);
            var more = MacStyle.Button(L("Weitere Infos …", "More Info…"), () => ShellActions.Run("ms-settings:about"));
            more.HorizontalAlignment = HorizontalAlignment.Center; more.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(more);
            w.Content = sp;
            w.SourceInitialized += (s, e) => MacWindow.Frame(w);
            w.Closed += (s, e) => about = null;
            about = w;
            w.Show(); w.Activate();
        }

        static string StartDisk()
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                string label = d.VolumeLabel;
                return (string.IsNullOrWhiteSpace(label) ? L("Lokaler Datenträger", "Local Disk") : label) + " (" + d.Name.TrimEnd('\\') + ")";
            }
            catch { return null; }
        }

        static string Reg(string key, string name) { try { return Core.Reg.Get(key, name)?.ToString(); } catch { return null; } }

        /// <summary>A drawn computer for the About window: a notebook on laptops, a display on desktops.</summary>
        static ImageSource Computer(bool laptop)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var wall = new LinearGradientBrush(C(0x6FB3F2), C(0x0E3A9A), 60);
                if (laptop)
                {
                    dc.DrawRoundedRectangle(B(0x2C2C2E), null, new Rect(25, 8, 250, 165), 12, 12);
                    dc.DrawRoundedRectangle(wall, null, new Rect(33, 16, 234, 147), 4, 4);
                    dc.DrawGeometry(new LinearGradientBrush(C(0xE5E5EA), C(0xA1A1A6), 90), null, Geometry.Parse("M4,176 L296,176 L290,190 Q288,194 282,194 L18,194 Q12,194 10,190 Z"));
                    dc.DrawRoundedRectangle(B(0x8E8E93), null, new Rect(125, 176, 50, 4), 2, 2);
                }
                else
                {
                    dc.DrawRoundedRectangle(new LinearGradientBrush(C(0xE5E5EA), C(0xB0B0B5), 90), null, new Rect(20, 4, 260, 160), 10, 10);
                    dc.DrawRectangle(wall, null, new Rect(28, 12, 244, 138));
                    dc.DrawGeometry(new LinearGradientBrush(C(0xD1D1D6), C(0x9A9AA0), 90), null, Geometry.Parse("M125,164 L175,164 L182,192 L118,192 Z"));
                    dc.DrawRoundedRectangle(B(0xC7C7CC), null, new Rect(100, 190, 100, 6), 3, 3);
                }
            }
            var rtb = new RenderTargetBitmap(300, 200, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }

        // ---------------- Force Quit ----------------
        static Window forceQuit;
        public static void ForceQuit()
        {
            if (forceQuit != null) { forceQuit.Activate(); return; }
            var w = new Window { Title = L("Programme sofort beenden", "Force Quit Applications"), ResizeMode = ResizeMode.NoResize, Width = 380, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = B(0xF4F4F6) };
            MacStyle.Prepare(w);
            w.Icon = MacStyle.Own("activity");
            MacWindow.Chrome(w, 40);
            var dock = new DockPanel { Margin = new Thickness(0) };
            var head = new DockPanel { Height = 40 };
            var lights = MacStyle.TrafficLights(w, 12);
            lights.Margin = new Thickness(14, 0, 0, 0);
            DockPanel.SetDock(lights, Dock.Left);
            head.Children.Add(lights);
            var title = MacStyle.Text(w.Title, 13, MacStyle.Label, FontWeights.SemiBold);
            title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(-60, 0, 0, 0);
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            var info = MacStyle.Text(L("Wenn ein Programm nicht reagiert, wähle es in der Liste aus und klicke auf „Sofort beenden“.", "If an app doesn't respond for a while, select its name and click Force Quit."), 11.5, MacStyle.Label);
            info.TextWrapping = TextWrapping.Wrap; info.Margin = new Thickness(18, 4, 18, 10);
            DockPanel.SetDock(info, Dock.Top);
            dock.Children.Add(info);
            var list = MacWindow.List();
            var bottom = new DockPanel { Margin = new Thickness(18, 12, 18, 16) };
            var quit = MacStyle.Button(L("Sofort beenden", "Force Quit"), () =>
            {
                if (!(list.SelectedItem is FrameworkElement el) || !(el.Tag is (int pid, string name))) return;
                MacAlert.Ask(L("Möchtest du „" + name + "“ sofort beenden?", "Do you want to force “" + name + "” to quit?"), L("Ungesicherte Änderungen gehen verloren.", "You will lose any unsaved changes."), MacStyle.Own("activity"),
                    new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (L("Sofort beenden", "Force Quit"), (Action)(() => { HackerPanel.Kill(pid, name); Fill(); }), true) });
            }, true);
            DockPanel.SetDock(quit, Dock.Right);
            bottom.Children.Add(quit);
            bottom.Children.Add(new Border());
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(bottom);
            dock.Children.Add(new Border { Child = list, Margin = new Thickness(18, 0, 18, 0), BorderBrush = B(0x000000, 30), BorderThickness = new Thickness(0.8), Background = B(0xFFFFFF), CornerRadius = new CornerRadius(6), Padding = new Thickness(4) });
            void Fill()
            {
                list.Items.Clear();
                var groups = (ShellActions.Windows?.Invoke() ?? new List<WinInfo>()).Where(x => W32.IsWindow(x.Hwnd)).GroupBy(x => x.Key);
                foreach (var g in groups)
                {
                    var first = g.First();
                    W32.GetWindowThreadProcessId(first.Hwnd, out uint pid);
                    if (pid == (uint)Process.GetCurrentProcess().Id) continue;
                    string nm = first.AppName ?? first.Title;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 24, Tag = ((int)pid, nm) };
                    var img = Img(MacStyle.Tile(first.Icon), 20); img.Margin = new Thickness(2, 0, 7, 0);
                    row.Children.Add(img);
                    row.Children.Add(MacStyle.Text(nm, 13, MacStyle.Label));
                    list.Items.Add(row);
                }
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }
            Fill();
            w.Content = dock;
            w.SourceInitialized += (s, e) => MacWindow.Frame(w);
            w.Closed += (s, e) => forceQuit = null;
            forceQuit = w;
            w.Show(); w.Activate();
        }
    }

    /// <summary>
    /// Base of the look's own apps: a white window with the toolbar drawn into the title bar, the traffic lights
    /// at the left, Windows keeping the frame for snapping and resizing.
    /// </summary>
    internal abstract class MacWindow : Window
    {
        protected static bool De => Core.Loc.De;
        protected static string L(string de, string en) => De ? de : en;
        protected static readonly ShellTheme Theme = ShellThemes.For("macos");
        readonly string appName, iconKind;
        IntPtr hwnd;

        protected MacWindow(string title, string app, string icon, double w, double h)
        {
            Title = title; Width = w; Height = h; appName = app; iconKind = icon;
            MacStyle.Prepare(this);
            Background = B(0xFFFFFF);
            Foreground = B(MacStyle.Label);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = MacStyle.Own(icon);
            Chrome(this, 52);
            StateChanged += (s, e) => { if (Content is FrameworkElement c) c.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); };
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                WindowTracker.OwnApps.Add(hwnd);
                MacApps.Register(hwnd, appName, iconKind);
                Frame(this);
            };
            Closed += (s, e) => { WindowTracker.OwnApps.Remove(hwnd); MacApps.Unregister(hwnd); };
            RenderGuard.Watch(this);
        }

        public static void Chrome(Window w, double caption)
            => System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = caption, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });

        /// <summary>White caption, soft grey border, round corners: the frame Windows draws around the window.</summary>
        public static void Frame(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            int cap = 0xFFFFFF, txt = 0x1F1D1D, border = 0xCCC9C9, corner = 2;
            W32.DwmSetWindowAttribute(h, W32.DWMWA_CAPTION_COLOR, ref cap, 4);
            W32.DwmSetWindowAttribute(h, W32.DWMWA_TEXT_COLOR, ref txt, 4);
            W32.DwmSetWindowAttribute(h, W32.DWMWA_BORDER_COLOR, ref border, 4);
            W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
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
            Present(w);
            return w;
        }

        /// <summary>Opens a window the macOS way: it grows from slightly smaller and fades in.</summary>
        protected static void Present(Window w)
        {
            w.Show();
            w.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(w).Handle);
            if (w.Content is FrameworkElement c) MacStyle.Pop(c, new Point(0.5, 0.5), 0.96, 220);
        }

        /// <summary>A small toolbar button (Tahoe: symbols on a glass capsule).</summary>
        protected static Border ToolButton(string sym, Action click, string tip, double w = 34)
        {
            var b = new Border { Width = w, Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Child = MacStyle.Sym(sym, 15, MacStyle.Secondary), ToolTip = tip, Cursor = Cursors.Arrow };
            b.MouseEnter += (s, e) => b.Background = B(0x000000, 14);
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => { b.Background = B(0x000000, 30); e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) => { b.Background = B(0x000000, 14); e.Handled = true; click(); };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        /// <summary>A group of toolbar buttons on one white capsule.</summary>
        protected static Border Capsule(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) sp.Children.Add(i);
            var b = new Border { Child = sp, CornerRadius = new CornerRadius(17), Background = B(0xFFFFFF, 235), Padding = new Thickness(2), BorderBrush = B(0x000000, 22), BorderThickness = new Thickness(0.6), Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.10 }, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        /// <summary>A list with macOS rows: blue selection with white text, grey stripes when asked.</summary>
        public static ListBox List(bool stripes = false, double radius = 5)
        {
            var lb = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, HorizontalContentAlignment = HorizontalAlignment.Stretch, Foreground = B(MacStyle.Label), FontFamily = MacStyle.Font };
            ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(lb, true);
            VirtualizingPanel.SetVirtualizationMode(lb, VirtualizationMode.Recycling);
            if (stripes) lb.AlternationCount = 2;
            var item = new Style(typeof(ListBoxItem));
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(TextElement.ForegroundProperty, B(MacStyle.Label));
            bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            tpl.VisualTree = bd;
            if (stripes)
            {
                var alt = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
                alt.Setters.Add(new Setter(Border.BackgroundProperty, B(0xF4F5F5), "bd"));
                tpl.Triggers.Add(alt);
            }
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, B(MacStyle.Blue), "bd"));
            sel.Setters.Add(new Setter(TextElement.ForegroundProperty, Brushes.White, "bd"));
            tpl.Triggers.Add(sel);
            var selOff = new MultiTrigger();
            selOff.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, true));
            selOff.Conditions.Add(new Condition(Selector.IsSelectionActiveProperty, false));
            selOff.Setters.Add(new Setter(Border.BackgroundProperty, B(0xDCDCDE), "bd"));
            selOff.Setters.Add(new Setter(TextElement.ForegroundProperty, B(MacStyle.Label), "bd"));
            tpl.Triggers.Add(selOff);
            item.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            item.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            lb.ItemContainerStyle = item;
            return lb;
        }
    }

    // =====================================================================
    // Finder
    // =====================================================================
    internal sealed class MacFiles : MacWindow
    {
        static MacFiles current;
        public const string Trash = "::trash";
        static bool Open2(string folder, bool fresh)
        {
            var w = fresh ? null : current;
            if (w == null || !w.IsLoaded) { w = new MacFiles(); current = w; Present(w); }
            else { if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Activate(); W32.SetForegroundWindow(new WindowInteropHelper(w).Handle); }
            w.Go(folder ?? w.dir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return true;
        }
        public static void Open(string folder = null) => Open2(folder, false);
        public static void OpenNew(string folder) => Open2(folder, true);
        public static void OpenTrash() => Open(Trash);

        public static void EmptyTrash()
        {
            MacAlert.Ask(L("Möchtest du die Objekte im Papierkorb wirklich endgültig löschen?", "Are you sure you want to permanently erase the items in the Trash?"), L("Dieser Vorgang kann nicht widerrufen werden.", "You can't undo this action."), MacStyle.Own("trash-full"),
                new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (L("Papierkorb entleeren", "Empty Trash"), (Action)(() => { MacApps.SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2); current?.Reload(); }), true) });
        }

        public sealed class Entry
        {
            public string Path; public bool Dir; public long SizeV = -1; public DateTime TimeV; public object Shell;
            public ImageSource Icon { get; set; }
            public string Name { get; set; }
            public string Time { get; set; }
            public string Size { get; set; }
            public string Kind { get; set; }
        }

        string dir;   // "" = the computer (drives), Trash = the recycle bin
        readonly List<string> back = new List<string>(), fwd = new List<string>();
        readonly ListBox icons, rows;
        readonly Grid listHost;
        readonly StackPanel places;
        readonly TextBox filter;
        readonly TextBlock status, title;
        readonly Border emptyBtn;
        bool listMode, showDot;
        List<Entry> all = new List<Entry>();
        string sortKey = "name"; bool sortDesc;
        FileSystemWatcher watcher;
        readonly DispatcherTimer reload;
        Border backBtn, fwdBtn, iconBtn, listBtn;

        MacFiles() : base("Finder", "Finder", "finder", 980, 600)
        {
            WindowTracker.StandsFor(this, "explorer.exe");
            MinWidth = 640; MinHeight = 380;
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- the floating sidebar with the traffic lights ----
            var side = new DockPanel();
            var lights = MacStyle.TrafficLights(this);
            lights.Margin = new Thickness(14, 16, 0, 10); lights.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(lights, Dock.Top);
            side.Children.Add(lights);
            places = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
            side.Children.Add(new ScrollViewer { Content = places, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
            var sideCard = new Border { Child = side, Margin = new Thickness(8, 8, 0, 8), CornerRadius = new CornerRadius(16), Background = B(0xEDEDEF), BorderBrush = B(0x000000, 16), BorderThickness = new Thickness(0.6) };
            root.Children.Add(sideCard);
            BuildPlaces();

            // ---- toolbar in the title bar ----
            var main = new DockPanel();
            Grid.SetColumn(main, 1);
            var bar = new DockPanel { Height = 52, Margin = new Thickness(12, 0, 12, 0) };
            backBtn = ToolButton("chevron.left", Back, L("Zurück", "Back"));
            fwdBtn = ToolButton("chevron.right", Forward, L("Vorwärts", "Forward"));
            var nav = Capsule(backBtn, fwdBtn);
            DockPanel.SetDock(nav, Dock.Left);
            bar.Children.Add(nav);
            var (fbox, ftb) = MacStyle.Field(L("Suchen", "Search"), 170, "magnifyingglass", 30);
            filter = ftb;
            fbox.Background = B(0xFFFFFF, 235); fbox.BorderBrush = B(0x000000, 22);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(fbox, true);
            DockPanel.SetDock(fbox, Dock.Right);
            bar.Children.Add(fbox);
            iconBtn = ToolButton("grid", () => SetMode(false), L("Symbole", "Icons"));
            listBtn = ToolButton("list", () => SetMode(true), L("Liste", "List"));
            var views = Capsule(iconBtn, listBtn);
            views.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(views, Dock.Right);
            bar.Children.Add(views);
            emptyBtn = MacStyle.Button(L("Entleeren", "Empty"), EmptyTrash);
            emptyBtn.VerticalAlignment = VerticalAlignment.Center; emptyBtn.Margin = new Thickness(0, 0, 10, 0); emptyBtn.Visibility = Visibility.Collapsed;
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(emptyBtn, true);
            DockPanel.SetDock(emptyBtn, Dock.Right);
            bar.Children.Add(emptyBtn);
            title = MacStyle.Text("", 15, MacStyle.Label, FontWeights.Bold);
            title.Margin = new Thickness(14, 0, 0, 0);
            bar.Children.Add(title);
            DockPanel.SetDock(bar, Dock.Top);
            main.Children.Add(bar);

            status = MacStyle.Text("", 11.5, MacStyle.Secondary);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            var statusBar = new Border { Child = status, Height = 26, BorderBrush = B(MacStyle.Line), BorderThickness = new Thickness(0, 0.6, 0, 0) };
            DockPanel.SetDock(statusBar, Dock.Bottom);
            main.Children.Add(statusBar);

            // ---- icon view and list view ----
            icons = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, Padding = new Thickness(10, 6, 10, 6) };
            ScrollViewer.SetHorizontalScrollBarVisibility(icons, ScrollBarVisibility.Disabled);
            var wrap = new FrameworkElementFactory(typeof(WrapPanel));
            icons.ItemsPanel = new ItemsPanelTemplate(wrap);
            icons.ItemTemplate = IconTemplate();
            var ic = new Style(typeof(ListBoxItem));
            var ictpl = new ControlTemplate(typeof(ListBoxItem));
            ictpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            ic.Setters.Add(new Setter(Control.TemplateProperty, ictpl));
            ic.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            icons.ItemContainerStyle = ic;

            rows = List(stripes: true, radius: 5);
            rows.ItemTemplate = RowTemplate();
            var heads = new Grid { Margin = new Thickness(18, 0, 18, 0), Height = 26 };
            Columns(heads);
            void Head(string text, string key, int col, HorizontalAlignment al = HorizontalAlignment.Left)
            {
                var tb = MacStyle.Text(text, 11.5, MacStyle.Secondary, FontWeights.SemiBold);
                tb.HorizontalAlignment = al; tb.Cursor = Cursors.Arrow;
                tb.MouseLeftButtonUp += (s, e) => { if (sortKey == key) sortDesc = !sortDesc; else { sortKey = key; sortDesc = key != "name"; } Show(); };
                Grid.SetColumn(tb, col); heads.Children.Add(tb);
            }
            Head("Name", "name", 0); Head(L("Änderungsdatum", "Date Modified"), "time", 1); Head(L("Größe", "Size"), "size", 2, HorizontalAlignment.Right); Head(L("Art", "Kind"), "kind", 3);
            var listDock = new DockPanel { Margin = new Thickness(0) };
            var headBorder = new Border { Child = heads, BorderBrush = B(MacStyle.Line), BorderThickness = new Thickness(0, 0, 0, 0.6) };
            DockPanel.SetDock(headBorder, Dock.Top);
            listDock.Children.Add(headBorder);
            rows.Margin = new Thickness(10, 2, 10, 4);
            listDock.Children.Add(rows);
            listHost = new Grid();
            listHost.Children.Add(icons);
            listHost.Children.Add(listDock);
            main.Children.Add(listHost);
            root.Children.Add(main);

            foreach (var lb in new[] { icons, rows })
            {
                var l = lb;
                TextSearch.SetTextPath(l, "Name");
                l.MouseDoubleClick += (s, e) => { if (l.SelectedItem != null && Sel.Count > 0) OpenSel(); };
                l.PreviewKeyDown += OnKey;
                l.MouseRightButtonUp += (s, e) => Menu();
                l.SelectionChanged += (s, e) => Status();
                l.PreviewMouseLeftButtonDown += (s, e) => { if (ItemsControl.ContainerFromElement(l, e.OriginalSource as DependencyObject) == null) l.UnselectAll(); };
            }
            filter.TextChanged += (s, e) => Show();
            PreviewKeyDown += (s, e) =>
            {
                if (e.SystemKey == Key.Left || e.Key == Key.OemOpenBrackets && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Back(); e.Handled = true; }
                else if (e.SystemKey == Key.Right || e.Key == Key.OemCloseBrackets && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Forward(); e.Handled = true; }
                else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { filter.Focus(); e.Handled = true; }
                else if (e.SystemKey == Key.Up) { Up(); e.Handled = true; }
                else if (e.Key == Key.OemPeriod && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)) { showDot = !showDot; Load(keep: true); e.Handled = true; }
            };
            MouseUp += (s, e) => { if (e.ChangedButton == MouseButton.XButton1) Back(); else if (e.ChangedButton == MouseButton.XButton2) Forward(); };
            Content = root;
            SetMode(false);
            reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            reload.Tick += (s, e) => { reload.Stop(); Load(keep: true); };
            Closed += (s, e) => { watcher?.Dispose(); reload.Stop(); if (current == this) current = null; };
        }

        void SetMode(bool list)
        {
            listMode = list;
            icons.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)rows.Parent).Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            iconBtn.Background = list ? Brushes.Transparent : B(0x000000, 20);
            listBtn.Background = list ? B(0x000000, 20) : Brushes.Transparent;
            var keep = Sel;
            var target = Active;
            target.ItemsSource = (list ? icons : rows).ItemsSource;
            foreach (var x in keep) target.SelectedItems.Add(x);
        }

        ListBox Active => listMode ? rows : icons;

        static void Columns(Grid g)
        {
            foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(180), new GridLength(80), new GridLength(150) })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }

        static DataTemplate RowTemplate()
        {
            var t = new DataTemplate(typeof(Entry));
            var g = new FrameworkElementFactory(typeof(Grid));
            g.SetValue(FrameworkElement.HeightProperty, 22.0);
            foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(180), new GridLength(80), new GridLength(150) })
            { var c = new FrameworkElementFactory(typeof(ColumnDefinition)); c.SetValue(ColumnDefinition.WidthProperty, w); g.AppendChild(c); }
            var name = new FrameworkElementFactory(typeof(StackPanel));
            name.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("Icon"));
            img.SetValue(FrameworkElement.WidthProperty, 17.0); img.SetValue(FrameworkElement.HeightProperty, 17.0);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0));
            img.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.AppendChild(img);
            name.AppendChild(Text("Name", 0, 1));
            g.AppendChild(name);
            g.AppendChild(Text("Time", 1, 0.62));
            var size = Text("Size", 2, 0.62);
            size.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            size.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            g.AppendChild(size);
            g.AppendChild(Text("Kind", 3, 0.62));
            t.VisualTree = g;
            return t;
        }

        static FrameworkElementFactory Text(string path, int col, double opacity)
        {
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding(path));
            tb.SetValue(Grid.ColumnProperty, col);
            tb.SetValue(TextBlock.FontFamilyProperty, MacStyle.Font);
            tb.SetValue(TextBlock.FontSizeProperty, 13.0);
            tb.SetValue(UIElement.OpacityProperty, opacity);
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            return tb;
        }

        static DataTemplate IconTemplate()
        {
            var t = new DataTemplate(typeof(Entry));
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(FrameworkElement.WidthProperty, 100.0);
            sp.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 4, 2, 6));
            var plate = new FrameworkElementFactory(typeof(Border), "plate");
            plate.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            plate.SetValue(FrameworkElement.WidthProperty, 74.0); plate.SetValue(FrameworkElement.HeightProperty, 70.0);
            plate.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("Icon"));
            img.SetValue(FrameworkElement.WidthProperty, 60.0); img.SetValue(FrameworkElement.HeightProperty, 60.0);
            img.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
            plate.AppendChild(img);
            sp.AppendChild(plate);
            var label = new FrameworkElementFactory(typeof(Border), "label");
            label.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            label.SetValue(Border.PaddingProperty, new Thickness(4, 1, 4, 1));
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
            label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            var tb = new FrameworkElementFactory(typeof(TextBlock), "name");
            tb.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            tb.SetValue(TextBlock.FontFamilyProperty, MacStyle.Font);
            tb.SetValue(TextBlock.FontSizeProperty, 12.0);
            tb.SetValue(TextBlock.ForegroundProperty, B(MacStyle.Label));
            tb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            tb.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tb.SetValue(FrameworkElement.MaxHeightProperty, 32.0);
            label.AppendChild(tb);
            sp.AppendChild(label);
            t.VisualTree = sp;
            var selected = new DataTrigger { Binding = new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) }, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, B(0x000000, 26), "plate"));
            selected.Setters.Add(new Setter(Border.BackgroundProperty, B(MacStyle.Blue), "label"));
            selected.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White, "name"));
            t.Triggers.Add(selected);
            return t;
        }

        // ---------------- sidebar ----------------
        readonly List<(Border b, string path)> placeRows = new List<(Border, string)>();

        void BuildPlaces()
        {
            places.Children.Clear(); placeRows.Clear();
            void Group(string title) { var tb = MacStyle.Text(title, 11, MacStyle.Secondary, FontWeights.SemiBold); tb.Margin = new Thickness(10, places.Children.Count == 0 ? 2 : 14, 0, 4); places.Children.Add(tb); }
            void Place(string sym, string name, string path)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var s = MacStyle.Sym(sym, 16, MacStyle.Blue);
                s.Margin = new Thickness(0, 0, 8, 0);
                sp.Children.Add(s);
                sp.Children.Add(MacStyle.Text(name, 13, MacStyle.Label));
                var b = new Border { Child = sp, Height = 28, CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 0, 9, 0), Background = Brushes.Transparent, Cursor = Cursors.Arrow };
                b.MouseLeftButtonUp += (s2, e) => Go(path);
                places.Children.Add(b); placeRows.Add((b, path));
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Group(L("Favoriten", "Favorites"));
            Place("desktop", L("Schreibtisch", "Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Place("doc", L("Dokumente", "Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Place("arrow.down.circle", "Downloads", System.IO.Path.Combine(home, "Downloads"));
            Place("house", ShellActions.UserName.Split(' ')[0], home);
            Place("photo", L("Bilder", "Pictures"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            Place("music", L("Musik", "Music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            Place("film", L("Filme", "Movies"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            Group(L("Orte", "Locations"));
            Place("desktop", Environment.MachineName, "");
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable || x.DriveType == DriveType.Network)))
                Place(d.DriveType == DriveType.Removable ? "eject" : "drive", DriveName(d), d.RootDirectory.FullName);
            Place("trash", L("Papierkorb", "Trash"), Trash);
        }

        static string DriveName(DriveInfo d)
        {
            string label = "";
            try { label = d.VolumeLabel; } catch { }
            if (string.IsNullOrWhiteSpace(label)) label = d.DriveType == DriveType.Removable ? L("Wechseldatenträger", "Removable") : L("Lokaler Datenträger", "Local Disk");
            return label + " (" + d.Name.TrimEnd('\\') + ")";
        }

        void MarkPlaces()
        {
            foreach (var (b, p) in placeRows)
                b.Background = string.Equals(p.TrimEnd('\\'), (dir ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? B(0x000000, 22) : Brushes.Transparent;
        }

        // ---------------- navigation ----------------
        public void Go(string target, bool remember = true)
        {
            if (target == null) return;
            if (target != Trash && target.Length > 0 && File.Exists(target) && !Directory.Exists(target))
            {
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; }
                return;
            }
            if (target != Trash && target.Length > 0 && !Directory.Exists(target)) { status.Text = L("Nicht gefunden: ", "Not found: ") + target; return; }
            if (remember && dir != null && dir != target) { back.Add(dir); fwd.Clear(); }
            dir = target;
            filter.Text = "";
            Load();
        }

        void Back() { if (back.Count == 0) return; fwd.Add(dir); var d = back[back.Count - 1]; back.RemoveAt(back.Count - 1); Go(d, false); }
        void Forward() { if (fwd.Count == 0) return; back.Add(dir); var d = fwd[fwd.Count - 1]; fwd.RemoveAt(fwd.Count - 1); Go(d, false); }
        void Up()
        {
            if (string.IsNullOrEmpty(dir) || dir == Trash) return;
            Go(dir.Length <= 3 ? "" : Directory.GetParent(dir.TrimEnd('\\'))?.FullName ?? "");
        }
        void Reload() => Load(keep: true);

        void Watch()
        {
            watcher?.Dispose(); watcher = null;
            if (string.IsNullOrEmpty(dir) || dir == Trash) return;
            try
            {
                watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => { reload.Stop(); reload.Start(); }));
                watcher.Created += h; watcher.Deleted += h; watcher.Changed += h; watcher.Renamed += (s, e) => h(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        static readonly System.Globalization.CultureInfo Ci = new System.Globalization.CultureInfo(Core.Loc.De ? "de-DE" : "en-US");

        /// <summary>Finder dates: "Heute, 14:03", "Gestern, 09:12", "12.11.2026, 10:22".</summary>
        static string When(DateTime t)
        {
            if (t.Date == DateTime.Today) return L("Heute, ", "Today at ") + t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return L("Gestern, ", "Yesterday at ") + t.ToString("HH:mm");
            return t.ToString(De ? "dd.MM.yyyy, HH:mm" : "MMM d, yyyy 'at' h:mm tt", Ci);
        }

        void Load(string select = null, bool keep = false)
        {
            var keepSel = keep ? new HashSet<string>(Sel.Select(x => x.Path)) : null;
            all = new List<Entry>();
            emptyBtn.Visibility = dir == Trash ? Visibility.Visible : Visibility.Collapsed;
            if (dir == Trash) LoadTrash();
            else if (string.IsNullOrEmpty(dir))
            {
                foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady))
                {
                    string size = ""; try { size = Gb(d.TotalFreeSpace) + L(" frei", " free"); } catch { }
                    all.Add(new Entry { Path = d.RootDirectory.FullName, Dir = true, Name = DriveName(d), Icon = Icon(d.RootDirectory.FullName, true, true), Size = "", Time = size, Kind = L("Volume", "Volume") });
                }
            }
            else
            {
                try
                {
                    foreach (var x in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                    {
                        if ((x.Attributes & FileAttributes.Hidden) != 0 || (x.Attributes & FileAttributes.System) != 0) continue;
                        if (!showDot && x.Name.StartsWith(".")) continue;   // like the Finder: dot files only after Ctrl+Shift+.
                        bool isDir = (x.Attributes & FileAttributes.Directory) != 0;
                        long size = isDir ? -1 : ((FileInfo)x).Length;
                        all.Add(new Entry { Path = x.FullName, Dir = isDir, Name = x.Name, TimeV = x.LastWriteTime, Time = When(x.LastWriteTime), SizeV = size, Size = isDir ? "--" : Size(size), Icon = Icon(x.FullName, isDir), Kind = isDir ? L("Ordner", "Folder") : KindOf(x.FullName) });
                    }
                }
                catch (Exception ex) { status.Text = ex.Message; }
            }
            Show(keepSel);
            if (select != null) { var hit = all.FirstOrDefault(x => x.Name.Equals(select, StringComparison.OrdinalIgnoreCase)); if (hit != null) { Active.SelectedItem = hit; Active.ScrollIntoView(hit); } }
            MarkPlaces(); Watch();
            backBtn.Opacity = back.Count > 0 ? 1 : 0.4; fwdBtn.Opacity = fwd.Count > 0 ? 1 : 0.4;
            Title = title.Text = dir == Trash ? L("Papierkorb", "Trash") : string.IsNullOrEmpty(dir) ? Environment.MachineName : (System.IO.Path.GetFileName(dir.TrimEnd('\\')) is string n && n.Length > 0 ? n : dir);
            if (string.IsNullOrEmpty(dir) || dir == Trash) title.Text = Title;
        }

        static object Get(object o, string name) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, o, null);
        static object Call(object o, string name, params object[] args) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, o, args);

        void LoadTrash()
        {
            try
            {
                var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                var folder = Call(shell, "NameSpace", 10);
                var items = Call(folder, "Items");
                int count = (int)Get(items, "Count");
                for (int i = 0; i < count; i++)
                {
                    var it = Call(items, "Item", i);
                    if (it == null) continue;
                    string name = Get(it, "Name") as string, path = Get(it, "Path") as string;
                    bool isDir = (bool)Get(it, "IsFolder");
                    string from = Call(folder, "GetDetailsOf", it, 1) as string;
                    long size = -1; try { size = Convert.ToInt64(Get(it, "Size")); } catch { }
                    all.Add(new Entry { Path = path, Dir = isDir, Name = name, Time = from, SizeV = size, Size = isDir ? "--" : Size(size), Icon = Icon(path ?? name, isDir), Kind = L("Ursprung: ", "From: ") + System.IO.Path.GetFileName((from ?? "").TrimEnd('\\')), Shell = it });
                }
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        static string Size(long b) => b < 0 ? "--" : b < 1000 ? b + " Byte" : b < 1000000 ? (b / 1000.0).ToString("0", Ci) + " KB" : b < 1000000000 ? (b / 1e6).ToString("0.#", Ci) + " MB" : (b / 1e9).ToString("0.##", Ci) + " GB";
        static string Gb(long b) => (b / 1e9).ToString(b >= 1e11 ? "0" : "0.#", Ci) + " GB";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        static readonly Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string KindOf(string path)
        {
            var ext = System.IO.Path.GetExtension(path);
            if (kinds.TryGetValue(ext, out var k)) return k;
            var info = new SHFILEINFO();
            try { SHGetFileInfo(path, 0x80, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x400 | 0x10); } catch { }
            return kinds[ext] = string.IsNullOrEmpty(info.type) ? ext.TrimStart('.').ToUpperInvariant() + L("-Datei", " file") : info.type;
        }

        static readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> OwnIcon = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".url", ".msc", ".scr" };
        static ImageSource Icon(string path, bool isDir, bool drive = false)
        {
            if (isDir && !drive) return MacStyle.FolderIcon;
            var ext = drive ? "drive:" + path : System.IO.Path.GetExtension(path);
            if (!drive && OwnIcon.Contains(ext)) return W32.ShellItemIcon(path, 64) ?? W32.FileIcon(path, true);
            if (iconCache.TryGetValue(ext, out var hit)) return hit;
            return iconCache[ext] = W32.ShellItemIcon(path, 64) ?? W32.FileIcon(path, true);
        }

        void Show(HashSet<string> keep = null)
        {
            IEnumerable<Entry> q = all;
            var f = filter.Text.Trim();
            if (f.Length > 0) q = q.Where(x => x.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0);
            Func<Entry, object> key = sortKey == "time" ? (Func<Entry, object>)(x => x.TimeV) : sortKey == "size" ? (x => x.SizeV) : sortKey == "kind" ? (x => x.Kind) : (x => x.Name);
            IOrderedEnumerable<Entry> o = sortKey == "name" ? q.OrderBy(x => 0) : q.OrderByDescending(x => x.Dir);
            o = sortDesc ? o.ThenByDescending(key) : o.ThenBy(key, sortKey == "name" ? (IComparer<object>)new NameOrder() : Comparer<object>.Default);
            var items = o.ToList();
            icons.ItemsSource = items; rows.ItemsSource = items;
            if (keep != null) foreach (var x in items.Where(x => keep.Contains(x.Path))) Active.SelectedItems.Add(x);
            Status();
        }

        sealed class NameOrder : IComparer<object>
        {
            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
            public int Compare(object a, object b) => StrCmpLogicalW(a as string ?? "", b as string ?? "");
        }

        void Status()
        {
            int n = (Active.ItemsSource as List<Entry>)?.Count ?? 0, s = Active.SelectedItems.Count;
            string free = "";
            try { if (!string.IsNullOrEmpty(dir) && dir != Trash) free = ", " + Gb(new DriveInfo(System.IO.Path.GetPathRoot(dir)).AvailableFreeSpace) + L(" verfügbar", " available"); } catch { }
            status.Text = (s > 0 ? s + L(" von ", " of ") : "") + n + (n == 1 ? L(" Objekt", " item") : L(" Objekte", " items")) + (s > 0 ? L(" ausgewählt", " selected") : "") + free;
        }

        // ---------------- actions ----------------
        List<Entry> Sel => (listMode ? rows : icons).SelectedItems.Cast<Entry>().ToList();

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

        /// <summary>The Finder window behind a window handle: the menu bar acts on it directly.</summary>
        public static MacFiles Of(IntPtr hwnd) => Application.Current?.Windows.OfType<MacFiles>().FirstOrDefault(f => new WindowInteropHelper(f).Handle == hwnd);

        /// <summary>Copies or moves the files on the clipboard into a folder (Finder's Paste, also onto the desktop).</summary>
        public static void PasteInto(string target, Action done = null)
        {
            try
            {
                if (string.IsNullOrEmpty(target) || !Clipboard.ContainsFileDropList()) return;
                var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                bool move = Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4 && (new BinaryReader(ms).ReadInt32() & 2) != 0;
                Task.Run(() => HackerFiles.FileOp(move ? HackerFiles.FO_MOVE : HackerFiles.FO_COPY, files, target, (ushort)(HackerFiles.FOF_ALLOWUNDO | HackerFiles.FOF_RENAMEONCOLLISION)))
                    .ContinueWith(_ => { if (move) try { Clipboard.Clear(); } catch { } done?.Invoke(); }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch { }
        }

        /// <summary>A new folder with a free name ("Neuer Ordner", "Neuer Ordner 2" …) in a folder.</summary>
        public static string NewFolderIn(string parent)
        {
            try
            {
                string name = L("Neuer Ordner", "untitled folder"), path = Path.Combine(parent, name);
                for (int i = 2; Directory.Exists(path) || File.Exists(path); i++) path = Path.Combine(parent, name + " " + i);
                Directory.CreateDirectory(path);
                return path;
            }
            catch { return null; }
        }

        /// <summary>The menu bar's Ablage, Bearbeiten, Darstellung and Gehe zu for this Finder window; every item acts on it.</summary>
        public List<Item> BarMenu(string key)
        {
            var sel = Sel;
            bool fs = !string.IsNullOrEmpty(dir) && dir != Trash, any = sel.Count > 0;
            var items = new List<Item>();
            var list = listMode ? rows : icons;
            switch (key)
            {
                case "file":
                    items.Add(new Item { Text = L("Neues Finder-Fenster", "New Finder Window"), Shortcut = "⌃N", Run = () => OpenNew(fs ? dir : null) });
                    items.Add(new Item { Text = L("Neuer Ordner", "New Folder"), Shortcut = "⇧⌃N", Enabled = fs, Run = NewFolder });
                    items.Add(new Item { Text = L("Öffnen", "Open"), Enabled = any && dir != Trash, Run = OpenSel });
                    items.Add(new Item { Text = L("Fenster schließen", "Close Window"), Shortcut = "⌃W", Run = Close });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Informationen", "Get Info"), Enabled = fs, Run = Properties });
                    items.Add(new Item { Text = L("Umbenennen", "Rename"), Shortcut = "F2", Enabled = fs && sel.Count == 1, Run = Rename });
                    items.Add(Item.Sep);
                    if (dir == Trash)
                    {
                        items.Add(new Item { Text = L("Zurücklegen", "Put Back"), Enabled = any, Run = PutBack });
                        items.Add(new Item { Text = L("Papierkorb entleeren …", "Empty Trash…"), Enabled = all.Count > 0, Run = EmptyTrash });
                    }
                    else items.Add(new Item { Text = L("In den Papierkorb legen", "Move to Trash"), Shortcut = "Entf", Enabled = fs && any, Run = () => Delete(false) });
                    break;
                case "edit":
                    items.Add(new Item { Text = L("Ausschneiden", "Cut"), Shortcut = "⌃X", Enabled = fs && any, Run = () => ToClipboard(true) });
                    items.Add(new Item { Text = L("Kopieren", "Copy"), Shortcut = "⌃C", Enabled = fs && any, Run = () => ToClipboard(false) });
                    items.Add(new Item { Text = L("Einsetzen", "Paste"), Shortcut = "⌃V", Enabled = fs && Clipboard.ContainsFileDropList(), Run = Paste });
                    items.Add(new Item { Text = L("Alles auswählen", "Select All"), Shortcut = "⌃A", Enabled = all.Count > 0, Run = () => { list.SelectAll(); list.Focus(); } });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Suchen", "Find"), Shortcut = "⌃F", Run = () => { filter.Focus(); filter.SelectAll(); } });
                    break;
                case "view":
                    items.Add(new Item { Text = L("Als Symbole", "as Icons"), Checked = !listMode, Run = () => SetMode(false) });
                    items.Add(new Item { Text = L("Als Liste", "as List"), Checked = listMode, Run = () => SetMode(true) });
                    items.Add(Item.Sep);
                    foreach (var (text, k) in new[] { (L("Nach Name sortieren", "Sort by Name"), "name"), (L("Nach Änderungsdatum sortieren", "Sort by Date Modified"), "time"), (L("Nach Größe sortieren", "Sort by Size"), "size"), (L("Nach Art sortieren", "Sort by Kind"), "kind") })
                    {
                        var sk = k;
                        items.Add(new Item { Text = text, Checked = sortKey == sk, Run = () => { sortKey = sk; sortDesc = sk != "name"; Show(); } });
                    }
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = showDot ? L("Ausgeblendete Objekte ausblenden", "Hide Hidden Files") : L("Ausgeblendete Objekte einblenden", "Show Hidden Files"), Shortcut = "⇧⌃.", Run = () => { showDot = !showDot; Load(keep: true); } });
                    items.Add(new Item { Text = L("Neu laden", "Reload"), Shortcut = "F5", Run = Reload });
                    break;
                case "go":
                    items.Add(new Item { Text = L("Zurück", "Back"), Shortcut = "⌃[", Enabled = back.Count > 0, Run = Back });
                    items.Add(new Item { Text = L("Vorwärts", "Forward"), Shortcut = "⌃]", Enabled = fwd.Count > 0, Run = Forward });
                    items.Add(new Item { Text = L("Übergeordneter Ordner", "Enclosing Folder"), Enabled = fs, Run = Up });
                    items.Add(Item.Sep);
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    foreach (var (text, path) in new[] { (L("Dokumente", "Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), (L("Schreibtisch", "Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), ("Downloads", Path.Combine(home, "Downloads")), (L("Benutzerordner", "Home"), home), (L("Computer", "Computer"), ""), (L("Papierkorb", "Trash"), Trash) })
                    {
                        var target = path;
                        items.Add(new Item { Text = text, Checked = dir == target, Run = () => Go(target) });
                    }
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Gehe zum Ordner …", "Go to Folder…"), Run = AskFolder });
                    break;
            }
            return items;
        }

        void AskFolder()
        {
            MacAlert a = null;
            a = MacAlert.Ask(L("Gehe zum Ordner:", "Go to the folder:"), null, MacStyle.FolderIcon, new[]
            {
                (L("Abbrechen", "Cancel"), (Action)null, false),
                (L("Öffnen", "Go"), (Action)(() =>
                {
                    var p = Environment.ExpandEnvironmentVariables((a?.Input?.Text ?? "").Trim().Trim('"'));
                    if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
                    if (Directory.Exists(p)) { Go(p); Activate(); }
                    else if (File.Exists(p)) { Go(Path.GetDirectoryName(p)); Activate(); }
                }), true),
            }, input: fsDir());
        }

        string fsDir() => !string.IsNullOrEmpty(dir) && dir != Trash ? dir : "";

        void Menu()
        {
            var sel = Sel;
            bool fs = !string.IsNullOrEmpty(dir) && dir != Trash;
            var items = new List<Item>();
            if (dir == Trash)
            {
                if (sel.Count > 0) items.Add(new Item { Text = L("Zurücklegen", "Put Back"), Run = PutBack });
                items.Add(new Item { Text = L("Papierkorb entleeren", "Empty Trash"), Enabled = all.Count > 0, Run = EmptyTrash });
            }
            else if (sel.Count > 0)
            {
                items.Add(new Item { Text = L("Öffnen", "Open"), Run = OpenSel });
                if (fs)
                {
                    items.Add(new Item { Text = L("In den Papierkorb legen", "Move to Trash"), Run = () => Delete(false) });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Informationen", "Get Info"), Run = Properties });
                    items.Add(new Item { Text = L("Umbenennen", "Rename"), Run = Rename });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Kopieren", "Copy"), Run = () => ToClipboard(false) });
                    items.Add(new Item { Text = L("Ausschneiden", "Cut"), Run = () => ToClipboard(true) });
                }
            }
            else if (fs)
            {
                items.Add(new Item { Text = L("Neuer Ordner", "New Folder"), Run = NewFolder });
                items.Add(Item.Sep);
                items.Add(new Item { Text = L("Informationen", "Get Info"), Run = Properties });
                items.Add(Item.Sep);
                items.Add(new Item { Text = L("Objekt einsetzen", "Paste Item"), Enabled = Clipboard.ContainsFileDropList(), Run = Paste });
                items.Add(Item.Sep);
                items.Add(new Item { Text = L("Neues Terminal beim Ordner", "New Terminal at Folder"), Run = () => ShellActions.Run("wt.exe", "-d \"" + dir + "\"") });
            }
            if (items.Count > 0) MacMenu.AtCursor(items);
        }

        void OpenSel()
        {
            var sel = Sel;
            if (sel.Count == 0 || dir == Trash) return;
            if (sel.Count == 1 && sel[0].Dir) { Go(sel[0].Path); return; }
            foreach (var x in sel.Where(x => !x.Dir))
            {
                try { Process.Start(new ProcessStartInfo(x.Path) { UseShellExecute = true, WorkingDirectory = dir }); }
                catch (Exception ex) { status.Text = x.Name + ": " + ex.Message; }
            }
        }

        void PutBack()
        {
            foreach (var x in Sel.Where(x => x.Shell != null))
            {
                try
                {
                    bool done = false;
                    var verbs = Call(x.Shell, "Verbs");
                    int n = (int)Get(verbs, "Count");
                    for (int i = 0; i < n && !done; i++)
                    {
                        var v = Call(verbs, "Item", i);
                        var name = (Get(v, "Name") as string ?? "").Replace("&", "");
                        if (name.IndexOf("wiederherstellen", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("restore", StringComparison.OrdinalIgnoreCase) >= 0) { Call(v, "DoIt"); done = true; }
                    }
                    if (!done) Call(x.Shell, "InvokeVerb", "undelete");
                }
                catch (Exception ex) { status.Text = ex.Message; }
            }
            reload.Stop(); reload.Interval = TimeSpan.FromMilliseconds(600); reload.Start();
        }

        void ToClipboard(bool cut)
        {
            var sel = Sel;
            if (sel.Count == 0 || string.IsNullOrEmpty(dir) || dir == Trash) return;
            var files = new StringCollection();
            foreach (var x in sel) files.Add(x.Path);
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            try { Clipboard.SetDataObject(data, true); } catch { }
        }

        void Paste()
        {
            if (string.IsNullOrEmpty(dir) || dir == Trash) return;
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
            if (sel.Count == 0 || string.IsNullOrEmpty(dir) || dir == Trash) return;
            Task.Run(() => HackerFiles.FileOp(HackerFiles.FO_DELETE, sel.Select(x => x.Path).ToArray(), null, permanent ? (ushort)0 : (ushort)(HackerFiles.FOF_ALLOWUNDO | HackerFiles.FOF_NOCONFIRMATION)))
                .ContinueWith(_ => Load(), TaskScheduler.FromCurrentSynchronizationContext());
        }

        static bool ValidName(string n) => !string.IsNullOrWhiteSpace(n) && n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;

        void Rename()
        {
            var sel = Sel;
            if (sel.Count != 1 || string.IsNullOrEmpty(dir) || dir == Trash) return;
            var x = sel[0];
            MacAlert a = null;
            a = MacAlert.Ask(L("Umbenennen", "Rename"), null, x.Icon, new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (L("Umbenennen", "Rename"), (Action)(() =>
            {
                var name = a.Input.Text.Trim();
                if (name == x.Name) return;
                if (!ValidName(name)) { status.Text = L("Ein Name darf keines dieser Zeichen enthalten: \\ / : * ? \" < > |", "A name can't contain any of these characters: \\ / : * ? \" < > |"); return; }
                try { var target = System.IO.Path.Combine(dir, name); if (x.Dir) Directory.Move(x.Path, target); else File.Move(x.Path, target); Load(name); }
                catch (Exception ex) { status.Text = ex.Message; }
            }), true) }, input: x.Name);
        }

        void NewFolder()
        {
            if (string.IsNullOrEmpty(dir) || dir == Trash) return;
            var baseName = L("Neuer Ordner", "untitled folder");
            var name = baseName;
            for (int i = 2; Directory.Exists(System.IO.Path.Combine(dir, name)) || File.Exists(System.IO.Path.Combine(dir, name)); i++) name = baseName + " " + i;
            try { Directory.CreateDirectory(System.IO.Path.Combine(dir, name)); Load(name); } catch (Exception ex) { status.Text = ex.Message; }
        }

        void Properties()
        {
            var sel = Sel;
            HackerFiles.ShowProperties(sel.Count == 1 ? sel[0].Path : string.IsNullOrEmpty(dir) ? "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" : dir);
        }
    }

    // =====================================================================
    // Aktivitätsanzeige
    // =====================================================================
    internal sealed class MacTasks : MacWindow
    {
        static MacTasks current;
        public static void Open() => Single(ref current, () => new MacTasks());

        public sealed class Proc
        {
            public int Pid; public double CpuV; public long MemV; public IntPtr Hwnd; public string PathV; public TimeSpan TimeV; public int ThreadsV;
            public ImageSource Icon { get; set; }
            public string Name { get; set; }
            public string Cpu { get; set; }
            public string CpuTime { get; set; }
            public string Threads { get; set; }
            public string Mem { get; set; }
            public string PidText { get; set; }
        }

        readonly ListBox list;
        readonly Grid heads;
        readonly TextBox filter;
        readonly TextBlock subtitle;
        readonly StackPanel stats;
        readonly Canvas graph;
        readonly TextBlock graphTitle;
        readonly Queue<(double sys, double user)> cpuHist = new Queue<(double, double)>();
        readonly Queue<double> memHist = new Queue<double>();
        readonly DispatcherTimer tick;
        Dictionary<int, TimeSpan> lastCpu; DateTime lastAt = DateTime.Now;
        bool busy, memTab;
        string sortKey = "cpu"; bool sortDesc = true;
        List<Proc> lastProcs = new List<Proc>();
        Border cpuSeg, memSeg;
        static readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly Color SysRed = C(0xE0443E), UserBlue = C(0x2F7CF6);

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        long li, lk, lu;

        MacTasks() : base(L("Aktivitätsanzeige", "Activity Monitor"), L("Aktivitätsanzeige", "Activity Monitor"), "activity", 900, 620)
        {
            WindowTracker.StandsFor(this, "taskmgr.exe");
            MinWidth = 700; MinHeight = 420;
            var root = new DockPanel();

            // ---- toolbar ----
            var bar = new DockPanel { Height = 52, Margin = new Thickness(18, 0, 12, 0) };
            var lights = MacStyle.TrafficLights(this);
            lights.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(lights, Dock.Left);
            bar.Children.Add(lights);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
            titles.Children.Add(MacStyle.Text(Title, 13, MacStyle.Label, FontWeights.Bold));
            subtitle = MacStyle.Text(L("Alle Prozesse", "All Processes"), 11, MacStyle.Secondary);
            titles.Children.Add(subtitle);
            DockPanel.SetDock(titles, Dock.Left);
            bar.Children.Add(titles);
            var tools = Capsule(ToolButton("xmark.circle", Stop, L("Prozess beenden", "Quit Process")), ToolButton("info.circle", Location, L("Speicherort", "Location")));
            DockPanel.SetDock(tools, Dock.Left);
            bar.Children.Add(tools);
            var (fbox, ftb) = MacStyle.Field(L("Suchen", "Search"), 170, "magnifyingglass", 30);
            filter = ftb;
            fbox.Background = B(0xFFFFFF, 235);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(fbox, true);
            DockPanel.SetDock(fbox, Dock.Right);
            bar.Children.Add(fbox);
            Border Seg(string text, Action a)
            {
                var b = new Border { Child = MacStyle.Text(text, 13, MacStyle.Label, FontWeights.Medium), Padding = new Thickness(14, 4, 14, 5), CornerRadius = new CornerRadius(13), Cursor = Cursors.Arrow };
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; a(); };
                System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                return b;
            }
            cpuSeg = Seg("CPU", () => SetTab(false));
            memSeg = Seg(L("Speicher", "Memory"), () => SetTab(true));
            var segs = Capsule(cpuSeg, memSeg);
            segs.HorizontalAlignment = HorizontalAlignment.Center;
            bar.Children.Add(segs);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            // ---- bottom: the load panel ----
            var bottom = new Grid { Height = 128, Background = B(0xF6F6F7) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stats = new StackPanel { Margin = new Thickness(26, 16, 10, 10) };
            bottom.Children.Add(stats);
            var graphBox = new StackPanel { Margin = new Thickness(8, 12, 8, 12) };
            graphTitle = MacStyle.Text("", 10.5, MacStyle.Secondary, FontWeights.SemiBold);
            graphTitle.HorizontalAlignment = HorizontalAlignment.Center;
            graphBox.Children.Add(graphTitle);
            graph = new Canvas { Height = 82, ClipToBounds = true, Background = B(0xFFFFFF), Margin = new Thickness(0, 4, 0, 0) };
            graphBox.Children.Add(new Border { Child = graph, BorderBrush = B(0x000000, 40), BorderThickness = new Thickness(0.6) });
            Grid.SetColumn(graphBox, 1);
            bottom.Children.Add(graphBox);
            var counts = new StackPanel { Margin = new Thickness(18, 16, 26, 10), Name = "counts" };
            Grid.SetColumn(counts, 2);
            bottom.Children.Add(counts);
            var bottomBorder = new Border { Child = bottom, BorderBrush = B(MacStyle.Line), BorderThickness = new Thickness(0, 0.6, 0, 0) };
            DockPanel.SetDock(bottomBorder, Dock.Bottom);
            root.Children.Add(bottomBorder);
            this.counts = counts;

            // ---- the process table ----
            var main = new DockPanel();
            heads = new Grid { Height = 24, Margin = new Thickness(18, 0, 18, 0) };
            var headBorder = new Border { Child = heads, BorderBrush = B(MacStyle.Line), BorderThickness = new Thickness(0, 0.6, 0, 0.6) };
            DockPanel.SetDock(headBorder, Dock.Top);
            main.Children.Add(headBorder);
            list = List(stripes: true, radius: 0);
            list.SelectionMode = SelectionMode.Single;
            list.Margin = new Thickness(10, 0, 10, 0);
            list.MouseDoubleClick += (s, e) => SwitchTo();
            list.PreviewKeyDown += (s, e) => { if (e.Key == Key.Delete) { Stop(); e.Handled = true; } else if (e.Key == Key.Enter) { SwitchTo(); e.Handled = true; } };
            list.MouseRightButtonUp += (s, e) =>
            {
                if (!(list.SelectedItem is Proc p)) return;
                var items = new List<Item>();
                if (p.Hwnd != IntPtr.Zero) items.Add(new Item { Text = L("Wechseln zu", "Switch To"), Run = SwitchTo });
                items.Add(new Item { Text = L("Im Finder zeigen", "Show in Finder"), Enabled = p.PathV != null, Run = Location });
                items.Add(Item.Sep);
                items.Add(new Item { Text = L("Beenden", "Quit"), Run = Stop });
                MacMenu.AtCursor(items);
            };
            main.Children.Add(list);
            root.Children.Add(main);
            filter.TextChanged += (s, e) => Show();
            Content = root;
            SetTab(false);
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            tick.Tick += (s, e) => Refresh();
            GetSystemTimes(out li, out lk, out lu);
            Loaded += (s, e) => { Refresh(); tick.Start(); };
            Closed += (s, e) => { tick.Stop(); if (current == this) current = null; };
        }

        readonly StackPanel counts;

        (string head, string key, GridLength w, bool right)[] Cols => memTab
            ? new[] { (L("Prozessname", "Process Name"), "name", new GridLength(1, GridUnitType.Star), false), (L("Speicher", "Memory"), "mem", new GridLength(110), true), ("Threads", "threads", new GridLength(80), true), ("PID", "pid", new GridLength(80), true) }
            : new[] { (L("Prozessname", "Process Name"), "name", new GridLength(1, GridUnitType.Star), false), ("% CPU", "cpu", new GridLength(80), true), (L("CPU-Zeit", "CPU Time"), "time", new GridLength(100), true), ("Threads", "threads", new GridLength(80), true), ("PID", "pid", new GridLength(80), true) };

        void SetTab(bool mem)
        {
            memTab = mem;
            sortKey = mem ? "mem" : "cpu"; sortDesc = true;
            cpuSeg.Background = mem ? Brushes.Transparent : B(0x000000, 22);
            memSeg.Background = mem ? B(0x000000, 22) : Brushes.Transparent;
            heads.Children.Clear(); heads.ColumnDefinitions.Clear();
            int i = 0;
            foreach (var (head, key, w, right) in Cols)
            {
                heads.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
                var k = key;
                var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, Background = Brushes.Transparent, Margin = new Thickness(0, 0, right ? 8 : 0, 0) };
                sp.Children.Add(MacStyle.Text(head, 11.5, MacStyle.Label, FontWeights.SemiBold));
                if (k == sortKey) { var ch = MacStyle.Sym(sortDesc ? "chevron.down" : "chevron.up", 9, MacStyle.Secondary); ch.Margin = new Thickness(4, 0, 0, 0); sp.Children.Add(ch); }
                sp.MouseLeftButtonUp += (s, e) => { if (sortKey == k) sortDesc = !sortDesc; else { sortKey = k; sortDesc = k != "name"; } SetTabKeep(); };
                Grid.SetColumn(sp, i++);
                heads.Children.Add(sp);
            }
            list.ItemTemplate = Template();
            graphTitle.Text = mem ? L("SPEICHERDRUCK", "MEMORY PRESSURE") : L("CPU-LAST", "CPU LOAD");
            Show();
            DrawGraph();
        }

        void SetTabKeep() { var k = sortKey; var d = sortDesc; SetTab(memTab); sortKey = k; sortDesc = d; SetHeadsOnly(); Show(); }
        void SetHeadsOnly()
        {
            int i = 0;
            foreach (StackPanel sp in heads.Children.OfType<StackPanel>())
            {
                var key = Cols[i++].key;
                while (sp.Children.Count > 1) sp.Children.RemoveAt(1);
                if (key == sortKey) { var ch = MacStyle.Sym(sortDesc ? "chevron.down" : "chevron.up", 9, MacStyle.Secondary); ch.Margin = new Thickness(4, 0, 0, 0); sp.Children.Add(ch); }
            }
        }

        DataTemplate Template()
        {
            var t = new DataTemplate(typeof(Proc));
            var g = new FrameworkElementFactory(typeof(Grid));
            g.SetValue(FrameworkElement.HeightProperty, 20.0);
            foreach (var c in Cols) { var cd = new FrameworkElementFactory(typeof(ColumnDefinition)); cd.SetValue(ColumnDefinition.WidthProperty, c.w); g.AppendChild(cd); }
            var name = new FrameworkElementFactory(typeof(StackPanel));
            name.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("Icon"));
            img.SetValue(FrameworkElement.WidthProperty, 16.0); img.SetValue(FrameworkElement.HeightProperty, 16.0);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0));
            name.AppendChild(img);
            var nt = new FrameworkElementFactory(typeof(TextBlock));
            nt.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nt.SetValue(TextBlock.FontSizeProperty, 12.0);
            nt.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            nt.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.AppendChild(nt);
            g.AppendChild(name);
            var map = new Dictionary<string, string> { { "cpu", "Cpu" }, { "time", "CpuTime" }, { "threads", "Threads" }, { "pid", "PidText" }, { "mem", "Mem" } };
            int col = 1;
            foreach (var c in Cols.Skip(1))
            {
                var tb = new FrameworkElementFactory(typeof(TextBlock));
                tb.SetBinding(TextBlock.TextProperty, new Binding(map[c.key]));
                tb.SetValue(Grid.ColumnProperty, col++);
                tb.SetValue(TextBlock.FontSizeProperty, 12.0);
                tb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
                tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                tb.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
                tb.SetValue(TextBlock.FontFamilyProperty, MacStyle.Font);
                tb.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
                g.AppendChild(tb);
            }
            t.VisualTree = g;
            return t;
        }

        void Refresh()
        {
            if (busy) return;
            busy = true;
            GetSystemTimes(out long i, out long k, out long u);
            long di = i - li, dk = k - lk, du = u - lu, total = dk + du;
            li = i; lk = k; lu = u;
            double sys = total > 0 ? Math.Max(0, 100.0 * (dk - di) / total) : 0, user = total > 0 ? 100.0 * du / total : 0;
            cpuHist.Enqueue((sys, user)); while (cpuHist.Count > 90) cpuHist.Dequeue();
            memHist.Enqueue(SysStats.Mem()); while (memHist.Count > 90) memHist.Dequeue();
            var windows = new Dictionary<int, IntPtr>();
            try { foreach (var w in ShellActions.Windows?.Invoke() ?? new List<WinInfo>()) { W32.GetWindowThreadProcessId(w.Hwnd, out uint pid); if (!windows.ContainsKey((int)pid)) windows[(int)pid] = w.Hwnd; } } catch { }
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
                        double pc = 0; TimeSpan ct = TimeSpan.Zero;
                        try { ct = p.TotalProcessorTime; cur[p.Id] = ct; if (lastCpu != null && lastCpu.TryGetValue(p.Id, out var old)) pc = (ct - old).TotalSeconds / sec * 100 / Environment.ProcessorCount; } catch { }
                        string path = null; try { path = p.MainModule?.FileName; } catch { }
                        int th = 0; try { th = p.Threads.Count; } catch { }
                        res.Add(new Proc { Pid = p.Id, Name = p.ProcessName, CpuV = pc, MemV = p.WorkingSet64, PathV = path, TimeV = ct, ThreadsV = th, Hwnd = windows.TryGetValue(p.Id, out var h) ? h : IntPtr.Zero });
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                lastCpu = cur; lastAt = now;
                return res;
            }).ContinueWith(t =>
            {
                busy = false;
                lastProcs = t.Result;
                foreach (var p in lastProcs)
                {
                    p.Cpu = p.CpuV.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
                    p.CpuTime = ((int)p.TimeV.TotalHours > 0 ? (int)p.TimeV.TotalHours + ":" : "") + p.TimeV.ToString(@"mm\:ss\.ff");
                    p.Threads = p.ThreadsV.ToString();
                    p.PidText = p.Pid.ToString();
                    p.Mem = p.MemV >= 1L << 30 ? (p.MemV / 1073741824.0).ToString("0.00") + " GB" : (p.MemV / 1048576.0).ToString("0.0") + " MB";
                    if (p.PathV != null)
                    {
                        if (!iconCache.TryGetValue(p.PathV, out var ic)) iconCache[p.PathV] = ic = W32.FileIcon(p.PathV, false);
                        p.Icon = ic;
                        try { var d = FileVersionInfo.GetVersionInfo(p.PathV).FileDescription; if (!string.IsNullOrWhiteSpace(d)) p.Name = d; } catch { }
                    }
                }
                Show();
                Stats(sys, user);
                DrawGraph();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Show()
        {
            IEnumerable<Proc> q = lastProcs;
            var f = filter.Text.Trim();
            if (f.Length > 0) q = q.Where(p => p.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0 || p.Pid.ToString() == f);
            Func<Proc, object> key = sortKey == "name" ? (Func<Proc, object>)(p => p.Name) : sortKey == "mem" ? (p => p.MemV) : sortKey == "time" ? (p => p.TimeV) : sortKey == "threads" ? (p => p.ThreadsV) : sortKey == "pid" ? (p => p.Pid) : (p => p.CpuV);
            var sorted = (sortDesc ? q.OrderByDescending(key) : q.OrderBy(key)).ToList();
            int sel = (list.SelectedItem as Proc)?.Pid ?? -1;
            list.ItemsSource = sorted;
            var again = sorted.FirstOrDefault(p => p.Pid == sel);
            if (again != null) list.SelectedItem = again;
            subtitle.Text = L("Alle Prozesse", "All Processes") + (f.Length > 0 ? " (" + sorted.Count + ")" : "");
        }

        void Stats(double sys, double user)
        {
            stats.Children.Clear(); counts.Children.Clear();
            var ci = System.Globalization.CultureInfo.CurrentCulture;
            void Line(StackPanel to, string k, string v, Color? c = null)
            {
                var d = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var vt = MacStyle.Text(v, 12, MacStyle.Label);
                DockPanel.SetDock(vt, Dock.Right);
                d.Children.Add(vt);
                d.Children.Add(MacStyle.Text(k, 12, c ?? MacStyle.Label));
                to.Children.Add(d);
            }
            if (memTab)
            {
                SysStats.MemTotals(out ulong total, out ulong avail);
                Line(stats, L("Physischer Speicher:", "Physical Memory:"), (total / 1073741824.0).ToString("0.00", ci) + " GB");
                Line(stats, L("Belegter Speicher:", "Memory Used:"), ((total - avail) / 1073741824.0).ToString("0.00", ci) + " GB");
                Line(stats, L("Verfügbar:", "Available:"), (avail / 1073741824.0).ToString("0.00", ci) + " GB");
            }
            else
            {
                Line(stats, "System:", sys.ToString("0.00", ci) + " %", SysRed);
                Line(stats, L("Benutzer:", "User:"), user.ToString("0.00", ci) + " %", UserBlue);
                Line(stats, L("Inaktiv:", "Idle:"), Math.Max(0, 100 - sys - user).ToString("0.00", ci) + " %");
            }
            Line(counts, "Threads:", lastProcs.Sum(p => p.ThreadsV).ToString("N0", ci));
            Line(counts, L("Prozesse:", "Processes:"), lastProcs.Count.ToString("N0", ci));
        }

        void DrawGraph()
        {
            graph.Children.Clear();
            double w = graph.ActualWidth, h = graph.ActualHeight;
            if (w <= 0) { graph.Dispatcher.BeginInvoke(new Action(() => { if (graph.ActualWidth > 0) DrawGraph(); }), DispatcherPriority.Loaded); return; }
            const int slots = 90;
            double bw = w / slots;
            if (memTab)
            {
                var pts = new PointCollection { new Point(w, h) };
                var arr = memHist.ToArray();
                for (int i = arr.Length - 1, x = 0; i >= 0; i--, x++) pts.Add(new Point(w - x * bw, h - h * arr[i] / 100));
                pts.Add(new Point(w - arr.Length * bw, h));
                graph.Children.Add(new Polygon { Points = pts, Fill = B(A(arr.Length > 0 && arr[arr.Length - 1] > 90 ? MacStyle.Red : MacStyle.Green, 200)) });
                return;
            }
            var hist = cpuHist.ToArray();
            for (int i = hist.Length - 1, x = 0; i >= 0; i--, x++)
            {
                double left = w - (x + 1) * bw;
                double hs = h * hist[i].sys / 100, hu = h * hist[i].user / 100;
                var r1 = new Rectangle { Width = Math.Max(1, bw - 0.6), Height = hs, Fill = B(SysRed) };
                Canvas.SetLeft(r1, left); Canvas.SetTop(r1, h - hs);
                var r2 = new Rectangle { Width = Math.Max(1, bw - 0.6), Height = hu, Fill = B(UserBlue) };
                Canvas.SetLeft(r2, left); Canvas.SetTop(r2, h - hs - hu);
                graph.Children.Add(r1); graph.Children.Add(r2);
            }
        }

        /// <summary>The menu bar's menus for Activity Monitor.</summary>
        public static List<Item> BarMenu(IntPtr hwnd, string key)
        {
            var m = Application.Current?.Windows.OfType<MacTasks>().FirstOrDefault(f => new WindowInteropHelper(f).Handle == hwnd);
            var items = new List<Item>();
            if (m == null) return items;
            var p = m.list.SelectedItem as Proc;
            switch (key)
            {
                case "file":
                    items.Add(new Item { Text = L("Fenster schließen", "Close Window"), Shortcut = "⌃W", Run = m.Close });
                    break;
                case "edit":
                    items.Add(new Item { Text = L("Kopieren", "Copy"), Shortcut = "⌃C", Enabled = p != null, Run = () => { try { Clipboard.SetText(p.Name + " (PID " + p.Pid + ")"); } catch { } } });
                    items.Add(new Item { Text = L("Suchen", "Find"), Shortcut = "⌃F", Run = () => { m.filter.Focus(); m.filter.SelectAll(); } });
                    break;
                case "view":
                    items.Add(new Item { Text = "CPU", Checked = !m.memTab, Run = () => m.SetTab(false) });
                    items.Add(new Item { Text = L("Speicher", "Memory"), Checked = m.memTab, Run = () => m.SetTab(true) });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Prozess beenden", "Quit Process"), Enabled = p != null, Run = m.Stop });
                    items.Add(new Item { Text = L("Zum Fenster wechseln", "Switch to Window"), Enabled = p != null && p.Hwnd != IntPtr.Zero, Run = m.SwitchTo });
                    items.Add(new Item { Text = L("Im Finder zeigen", "Show in Finder"), Enabled = p?.PathV != null, Run = m.Location });
                    break;
            }
            return items;
        }

        void Stop()
        {
            if (!(list.SelectedItem is Proc p)) return;
            MacAlert.Ask(L("Möchtest du diesen Prozess wirklich beenden?", "Are you sure you want to quit this process?"), "„" + p.Name + "“", MacStyle.Own("activity"),
                new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (L("Sofort beenden", "Force Quit"), (Action)(() => { HackerPanel.Kill(p.Pid, p.Name); Refresh(); }), false), (L("Beenden", "Quit"), (Action)(() => { if (p.Hwnd != IntPtr.Zero) W32.Close(p.Hwnd); else HackerPanel.Kill(p.Pid, p.Name); Refresh(); }), true) });
        }

        void SwitchTo() { if (list.SelectedItem is Proc p && p.Hwnd != IntPtr.Zero) W32.Activate(p.Hwnd); }
        void Location() { if (list.SelectedItem is Proc p && p.PathV != null) ShellActions.Files(System.IO.Path.GetDirectoryName(p.PathV)); }
    }
}

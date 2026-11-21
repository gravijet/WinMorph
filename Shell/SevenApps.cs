using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Base of the Windows 7 look's own apps (Run, Task Manager, Explorer). They use WPF's original
    /// Windows 7 control theme (Aero), so buttons, tabs, lists, menus and scroll bars look like Windows 7.
    /// </summary>
    internal abstract class SevenWindow : Window
    {
        protected static bool De => Core.Loc.De;
        protected static string L(string de, string en) => De ? de : en;
        protected static readonly Color Face = C(0xF0F0F0);
        IntPtr hwnd;

        static ResourceDictionary aero;
        static ResourceDictionary Aero
        {
            get
            {
                if (aero != null) return aero;
                try { aero = new ResourceDictionary { Source = new Uri("/PresentationFramework.Aero;V4.0.0.0;31bf3856ad364e35;component/themes/Aero.NormalColor.xaml", UriKind.Relative) }; }
                catch { aero = new ResourceDictionary(); }
                return aero;
            }
        }

        protected SevenWindow(string title, double w, double h)
        {
            Title = title; Width = w; Height = h;
            Resources.MergedDictionaries.Add(Aero);
            // the WinMorph app's text style must not leak into these windows: plain Windows 7 text
            Resources[typeof(TextBlock)] = new Style(typeof(TextBlock));
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
            Background = B(Face);
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                WindowTracker.OwnApps.Add(hwnd);   // in the taskbar and Alt+Tab like any app
                var f = ShellThemes.For("win7").Frame;
                int cap = f.Caption.B << 16 | f.Caption.G << 8 | f.Caption.R, txt = 0, border = f.Border.B << 16 | f.Border.G << 8 | f.Border.R;
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_CAPTION_COLOR, ref cap, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_TEXT_COLOR, ref txt, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_BORDER_COLOR, ref border, 4);
            };
            Closed += (s, e) => WindowTracker.OwnApps.Remove(hwnd);
            RenderGuard.Watch(this);
        }

        protected static ImageSource SysIcon(string file, bool large) => W32.FileIcon(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), file), large);

        protected static Button Btn(string text, Action a, double width = 75, bool isDefault = false)
        {
            var b = new Button { Content = text, MinWidth = width, Height = 23, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0), IsDefault = isDefault };
            b.Click += (s, e) => a();
            return b;
        }

        /// <summary>Brings an open window of this kind to the front instead of opening a second one.</summary>
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
            return w;
        }

        /// <summary>A small Windows 7 question box with one text field (rename, new folder).</summary>
        protected string Ask(string title, string question, string value)
        {
            var d = new SevenPrompt(title, question, value) { Owner = this };
            return d.ShowDialog() == true ? d.Answer : null;
        }
    }

    internal sealed class SevenPrompt : SevenWindow
    {
        readonly TextBox box;
        public string Answer => box.Text.Trim();

        public SevenPrompt(string title, string question, string value) : base(title, 380, 160)
        {
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Btn("OK", () => { if (Answer.Length > 0) DialogResult = true; }, isDefault: true));
            var cancel = Btn(L("Abbrechen", "Cancel"), () => DialogResult = false); cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            var band = new Border { Child = buttons, Background = B(Face), BorderBrush = B(0xDFDFDF), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10) };
            DockPanel.SetDock(band, Dock.Bottom);
            root.Children.Add(band);
            var sp = new StackPanel { Margin = new Thickness(14, 14, 14, 14) };
            sp.Children.Add(new TextBlock { Text = question, Margin = new Thickness(0, 0, 0, 6) });
            box = new TextBox { Text = value, Height = 23, VerticalContentAlignment = VerticalAlignment.Center };
            sp.Children.Add(box);
            root.Children.Add(new Border { Child = sp, Background = Brushes.White });
            Content = root;
            Loaded += (s, e) =>
            {
                box.Focus();
                int dot = value.LastIndexOf('.');
                if (dot > 0) box.Select(0, dot); else box.SelectAll();
            };
        }
    }

    // =====================================================================
    // Run (Win+R)
    // =====================================================================
    internal sealed class SevenRun : SevenWindow
    {
        static SevenRun current;
        public static void Open() => Single(ref current, () => new SevenRun());

        readonly ComboBox box;

        SevenRun() : base(L("Ausführen", "Run"), 410, 210)
        {
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Icon = W32.ShellItemIcon("shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}", 32) ?? SysIcon("cmd.exe", false);
            var root = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Btn("OK", () => Go(false), isDefault: true));
            var cancel = Btn(L("Abbrechen", "Cancel"), Close); cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            buttons.Children.Add(Btn(L("Durchsuchen...", "Browse..."), Browse));
            var band = new Border { Child = buttons, Padding = new Thickness(10, 10, 10, 10), BorderBrush = B(0xDFDFDF), BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(band, Dock.Bottom);
            root.Children.Add(band);

            var g = new Grid { Margin = new Thickness(12, 16, 14, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var icon = Img(W32.ShellItemIcon("shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}", 32) ?? SysIcon("cmd.exe", true), 32);
            icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 0, 14, 0);
            g.Children.Add(icon);
            var intro = new TextBlock { Text = L("Geben Sie den Namen eines Programms, Ordners, Dokuments oder einer Internetressource an.", "Type the name of a program, folder, document, or Internet resource, and Windows will open it for you."), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(intro, 1); g.Children.Add(intro);
            var row = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
            var label = new TextBlock { Text = L("Öffnen:", "Open:"), VerticalAlignment = VerticalAlignment.Center, Width = 46 };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            box = new ComboBox { IsEditable = true, Height = 23 };
            foreach (var h in History()) box.Items.Add(h);
            if (box.Items.Count > 0) box.Text = (string)box.Items[0];
            row.Children.Add(box);
            Grid.SetRow(row, 1); Grid.SetColumnSpan(row, 2); g.Children.Add(row);
            root.Children.Add(g);
            Content = root;

            Loaded += (s, e) =>
            {
                // like Windows 7: just above the bottom left corner
                var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
                Left = mi.rcWork.Left / sc + 12; Top = mi.rcWork.Bottom / sc - ActualHeight - 12;
                box.Focus();
                if (box.Template.FindName("PART_EditableTextBox", box) is TextBox tb) tb.SelectAll();
            };
            Deactivated += (s, e) => { if (IsLoaded && OwnedWindows.Count == 0) Topmost = false; };
        }

        static List<string> History()
        {
            try { if (File.Exists(HackerPanel.RunHistoryFile)) return File.ReadAllLines(HackerPanel.RunHistoryFile).Where(x => x.Trim().Length > 0).Reverse().Distinct(StringComparer.OrdinalIgnoreCase).Take(26).ToList(); } catch { }
            return new List<string>();
        }

        void Go(bool admin)
        {
            var text = (box.Text ?? "").Trim();
            if (text.Length == 0) return;
            admin |= Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var error = HackerPanel.RunCommand(text, admin);
            if (error != null)
            {
                MessageBox.Show(this, L("\"" + text + "\" wurde nicht gefunden. Vergewissern Sie sich, dass der Name richtig eingegeben wurde, und wiederholen Sie den Vorgang.", "Windows cannot find '" + text + "'. Make sure you typed the name correctly, and then try again."), text, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            try
            {
                var all = File.Exists(HackerPanel.RunHistoryFile) ? File.ReadAllLines(HackerPanel.RunHistoryFile).ToList() : new List<string>();
                all.RemoveAll(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));
                all.Add(text);
                File.WriteAllLines(HackerPanel.RunHistoryFile, all.Skip(Math.Max(0, all.Count - 30)));
            }
            catch { }
            Close();
        }

        void Browse()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = L("Durchsuchen", "Browse"), Filter = L("Programme", "Programs") + "|*.exe;*.pif;*.com;*.bat;*.cmd|" + L("Alle Dateien", "All files") + "|*.*" };
            if (d.ShowDialog(this) == true) box.Text = "\"" + d.FileName + "\"";
        }
    }

    // =====================================================================
    // Task Manager (Ctrl+Shift+Esc)
    // =====================================================================
    internal sealed class SevenTasks : SevenWindow
    {
        static SevenTasks current;
        public static void Open() => Single(ref current, () => new SevenTasks());
        internal static void Open(int tab) { Open(); current.tabs.SelectedIndex = tab; }

        sealed class AppRow { public IntPtr Hwnd; public ImageSource Icon { get; set; } public string Task { get; set; } public string Status { get; set; } }
        sealed class ProcRow { public int Pid; public string Name { get; set; } public string Cpu { get; set; } public string Mem { get; set; } public string Desc { get; set; } public double CpuV; public long MemV; }

        readonly ListView apps, procs;
        readonly TabControl tabs;
        readonly StatusBarItem sbProcs, sbCpu, sbMem;
        readonly Graph cpuGraph, memGraph;
        readonly Meter cpuMeter, memMeter;
        readonly TextBlock memTotal, memAvail, memUsed, sysHandles, sysThreads, sysProcs, sysUp;
        readonly DispatcherTimer timer;
        readonly Dictionary<int, (TimeSpan cpu, DateTime at)> lastCpu = new Dictionary<int, (TimeSpan, DateTime)>();
        static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string sort = "cpu"; bool sortDesc = true;
        bool loading;

        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "Registry", "Memory Compression", "LsaIso", "sihost" };

        SevenTasks() : base(L("Windows Task-Manager", "Windows Task Manager"), 500, 560)
        {
            WindowTracker.StandsFor(this, "taskmgr.exe");
            MinWidth = 420; MinHeight = 440;
            Icon = SysIcon("Taskmgr.exe", false);
            var root = new DockPanel();

            var menu = new Menu();
            var file = new MenuItem { Header = L("_Datei", "_File") };
            file.Items.Add(Item(L("Neuer Task (Ausführen...)", "New Task (Run...)"), SevenRun.Open));
            file.Items.Add(new Separator());
            file.Items.Add(Item(L("Task-Manager beenden", "Exit Task Manager"), Close));
            var options = new MenuItem { Header = L("_Optionen", "_Options") };
            var onTop = new MenuItem { Header = L("Immer im Vordergrund", "Always On Top"), IsCheckable = true };
            onTop.Click += (s, e) => Topmost = onTop.IsChecked;
            options.Items.Add(onTop);
            var view = new MenuItem { Header = L("_Ansicht", "_View") };
            view.Items.Add(Item(L("Jetzt aktualisieren", "Refresh Now"), Refresh));
            var help = new MenuItem { Header = "_?" };
            help.Items.Add(Item(L("Info zu WinMorph", "About WinMorph"), ShellActions.WinMorphApp));
            menu.Items.Add(file); menu.Items.Add(options); menu.Items.Add(view); menu.Items.Add(help);
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);

            var status = new StatusBar();
            sbProcs = new StatusBarItem { Width = 110 }; sbCpu = new StatusBarItem { Width = 140 }; sbMem = new StatusBarItem();
            status.Items.Add(sbProcs);
            status.Items.Add(new Separator());
            status.Items.Add(sbCpu);
            status.Items.Add(new Separator());
            status.Items.Add(sbMem);
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            tabs = new TabControl { Margin = new Thickness(6, 6, 6, 4) };

            // ---- applications ----
            apps = new ListView();
            var ag = new GridView();
            ag.Columns.Add(new GridViewColumn { Header = "Task", Width = 300, CellTemplate = IconText("Icon", "Task") });
            ag.Columns.Add(new GridViewColumn { Header = "Status", Width = 110, DisplayMemberBinding = new Binding("Status") });
            apps.View = ag;
            apps.MouseDoubleClick += (s, e) => SwitchTo();
            var appPage = new DockPanel { Margin = new Thickness(6) };
            var appButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            appButtons.Children.Add(Btn(L("Task beenden", "End Task"), EndTask, 90));
            appButtons.Children.Add(Btn(L("Wechseln zu", "Switch To"), SwitchTo, 90));
            appButtons.Children.Add(Btn(L("Neuer Task...", "New Task..."), SevenRun.Open, 90));
            DockPanel.SetDock(appButtons, Dock.Bottom);
            appPage.Children.Add(appButtons);
            appPage.Children.Add(apps);
            tabs.Items.Add(new TabItem { Header = L("Anwendungen", "Applications"), Content = appPage });

            // ---- processes ----
            procs = new ListView();
            var pg = new GridView();
            pg.Columns.Add(Col(L("Abbildname", "Image Name"), "Name", 150, "name"));
            pg.Columns.Add(Col("CPU", "Cpu", 44, "cpu", right: true));
            pg.Columns.Add(Col(L("Arbeitsspeicher", "Memory"), "Mem", 110, "mem", right: true));
            pg.Columns.Add(Col(L("Beschreibung", "Description"), "Desc", 170, "desc"));
            procs.View = pg;
            procs.KeyDown += (s, e) => { if (e.Key == Key.Delete) EndProcess(); };
            var procPage = new DockPanel { Margin = new Thickness(6) };
            var procButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            procButtons.Children.Add(Btn(L("Prozess beenden", "End Process"), EndProcess, 100));
            DockPanel.SetDock(procButtons, Dock.Bottom);
            procPage.Children.Add(procButtons);
            procPage.Children.Add(procs);
            tabs.Items.Add(new TabItem { Header = L("Prozesse", "Processes"), Content = procPage });

            // ---- performance ----
            var perf = new Grid { Margin = new Thickness(8) };
            perf.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            perf.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) perf.RowDefinitions.Add(new RowDefinition { Height = i < 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            cpuMeter = new Meter(); memMeter = new Meter { Mb = true };
            cpuGraph = new Graph(); memGraph = new Graph { Line = C(0xFFFF00) };
            Place(perf, Group(L("CPU-Auslastung", "CPU Usage"), cpuMeter), 0, 0);
            Place(perf, Group(L("Verlauf der CPU-Auslastung", "CPU Usage History"), cpuGraph), 0, 1);
            Place(perf, Group(L("Arbeitsspeicher", "Memory"), memMeter), 1, 0);
            Place(perf, Group(L("Verlauf der physikalischen Speicherauslastung", "Physical Memory Usage History"), memGraph), 1, 1);
            var numbers = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            numbers.ColumnDefinitions.Add(new ColumnDefinition()); numbers.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock V() => new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
            memTotal = V(); memAvail = V(); memUsed = V(); sysHandles = V(); sysThreads = V(); sysProcs = V(); sysUp = V();
            var left = Table(L("Physikalischer Speicher (MB)", "Physical Memory (MB)"), (L("Insgesamt", "Total"), memTotal), (L("Verwendet", "In use"), memUsed), (L("Verfügbar", "Available"), memAvail));
            var right = Table("System", ("Handles", sysHandles), ("Threads", sysThreads), (L("Prozesse", "Processes"), sysProcs), (L("Laufzeit", "Up Time"), sysUp));
            right.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(right, 1);
            numbers.Children.Add(left); numbers.Children.Add(right);
            Grid.SetRow(numbers, 2); Grid.SetColumnSpan(numbers, 2);
            perf.Children.Add(numbers);
            tabs.Items.Add(new TabItem { Header = L("Leistung", "Performance"), Content = perf });

            root.Children.Add(tabs);
            Content = root;
            tabs.SelectionChanged += (s, e) => { if (e.Source == tabs) Refresh(); };
            Loaded += (s, e) => Refresh();
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => { if (WindowState != WindowState.Minimized && IsVisible) Refresh(); }, Dispatcher);
            Closed += (s, e) => { timer.Stop(); current = null; };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.F5) Refresh(); };
        }

        static MenuItem Item(string header, Action a) { var m = new MenuItem { Header = header }; m.Click += (s, e) => a(); return m; }

        static void Place(Grid g, UIElement el, int row, int col) { Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el); }

        static GroupBox Group(string header, UIElement content) => new GroupBox { Header = header, Content = content, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(4) };

        static GroupBox Table(string header, params (string label, TextBlock value)[] rows)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = rows[i].label }; Grid.SetRow(l, i); g.Children.Add(l);
                Grid.SetRow(rows[i].value, i); Grid.SetColumn(rows[i].value, 1); g.Children.Add(rows[i].value);
            }
            return new GroupBox { Header = header, Content = g, Padding = new Thickness(4, 2, 4, 2) };
        }

        GridViewColumn Col(string header, string path, double width, string key, bool right = false)
        {
            var h = new GridViewColumnHeader { Content = header, HorizontalContentAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            h.Click += (s, e) => { if (sort == key) sortDesc = !sortDesc; else { sort = key; sortDesc = key == "cpu" || key == "mem"; } Refresh(); };
            var c = new GridViewColumn { Header = h, Width = width };
            if (right)
            {
                var f = new FrameworkElementFactory(typeof(TextBlock));
                f.SetBinding(TextBlock.TextProperty, new Binding(path));
                f.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
                f.SetValue(FrameworkElement.WidthProperty, width - 14);
                c.CellTemplate = new DataTemplate { VisualTree = f };
            }
            else c.DisplayMemberBinding = new Binding(path);
            return c;
        }

        internal static DataTemplate IconText(string icon, string text)
        {
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding(icon));
            img.SetValue(FrameworkElement.WidthProperty, 16.0); img.SetValue(FrameworkElement.HeightProperty, 16.0);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding(text));
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            sp.AppendChild(img); sp.AppendChild(tb);
            return new DataTemplate { VisualTree = sp };
        }

        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr h);
        [StructLayout(LayoutKind.Sequential)]
        struct PERFINFO { public int cb; public IntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize; public int HandleCount, ProcessCount, ThreadCount; }
        [DllImport("psapi.dll")] static extern bool GetPerformanceInfo(out PERFINFO p, int size);

        void Refresh()
        {
            double cpu = SysStats.Cpu(), mem = SysStats.Mem();
            SysStats.MemTotals(out ulong total, out ulong avail);
            cpuGraph.Push(cpu); memGraph.Push(mem);
            cpuMeter.Set(cpu, cpu.ToString("0") + " %");
            memMeter.Set(mem, ((total - avail) / 1073741824.0).ToString("0.00") + " GB");
            if (GetPerformanceInfo(out var pi, Marshal.SizeOf(typeof(PERFINFO))))
            {
                sysHandles.Text = pi.HandleCount.ToString("N0"); sysThreads.Text = pi.ThreadCount.ToString("N0"); sysProcs.Text = pi.ProcessCount.ToString("N0");
                sbProcs.Content = L("Prozesse: ", "Processes: ") + pi.ProcessCount;
            }
            var up = SysInfo.Uptime;
            sysUp.Text = ((int)up.TotalDays).ToString() + ":" + up.Hours.ToString("00") + ":" + up.Minutes.ToString("00") + ":" + up.Seconds.ToString("00");
            memTotal.Text = (total >> 20).ToString("N0"); memAvail.Text = (avail >> 20).ToString("N0"); memUsed.Text = ((total - avail) >> 20).ToString("N0");
            sbCpu.Content = L("CPU-Auslastung: ", "CPU Usage: ") + cpu.ToString("0") + " %";
            sbMem.Content = L("Physikalischer Speicher: ", "Physical Memory: ") + mem.ToString("0") + " %";

            if (tabs.SelectedIndex == 0) RefreshApps();
            else if (tabs.SelectedIndex == 1) RefreshProcs();
        }

        void RefreshApps()
        {
            var keep = (apps.SelectedItem as AppRow)?.Hwnd ?? IntPtr.Zero;
            var list = (ShellActions.Windows?.Invoke() ?? new List<WinInfo>())
                .Select(w => new AppRow { Hwnd = w.Hwnd, Icon = w.Icon, Task = w.Title, Status = IsHungAppWindow(w.Hwnd) ? L("Keine Rückmeldung", "Not Responding") : L("Wird ausgeführt", "Running") })
                .OrderBy(a => a.Task, StringComparer.CurrentCultureIgnoreCase).ToList();
            apps.ItemsSource = list;
            var again = list.FindIndex(a => a.Hwnd == keep);
            apps.SelectedIndex = again >= 0 ? again : list.Count > 0 ? 0 : -1;
        }

        void RefreshProcs()
        {
            if (loading) return;
            loading = true;
            var by = sort; bool desc = sortDesc;
            Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                int cores = Environment.ProcessorCount;
                var rows = new List<ProcRow>();
                var seen = new HashSet<int>();
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                    {
                        if (p.Id == 0) continue;
                        var r = new ProcRow { Pid = p.Id, Name = p.ProcessName + ".exe" };
                        seen.Add(p.Id);
                        try { r.MemV = p.WorkingSet64; } catch { }
                        try
                        {
                            var t = p.TotalProcessorTime;
                            lock (lastCpu)
                            {
                                if (lastCpu.TryGetValue(p.Id, out var last)) r.CpuV = Math.Max(0, (t - last.cpu).TotalMilliseconds / Math.Max(1, (now - last.at).TotalMilliseconds) * 100 / cores);
                                lastCpu[p.Id] = (t, now);
                            }
                        }
                        catch { }
                        r.Desc = Describe(p.Id, p.ProcessName);
                        r.Cpu = Math.Round(r.CpuV).ToString("00");
                        r.Mem = (r.MemV / 1024).ToString("N0") + " K";
                        rows.Add(r);
                    }
                }
                lock (lastCpu) foreach (var gone in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(gone);
                IEnumerable<ProcRow> q = rows;
                switch (by)
                {
                    case "name": q = q.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase); break;
                    case "mem": q = q.OrderBy(x => x.MemV); break;
                    case "desc": q = q.OrderBy(x => x.Desc, StringComparer.CurrentCultureIgnoreCase); break;
                    default: q = q.OrderBy(x => x.CpuV).ThenBy(x => x.MemV); break;
                }
                var list = q.ToList();
                if (desc) list.Reverse();
                return list;
            }).ContinueWith(t =>
            {
                loading = false;
                if (t.IsFaulted || !IsLoaded) return;
                int keep = (procs.SelectedItem as ProcRow)?.Pid ?? -1;
                var off = FindScroll(procs)?.VerticalOffset ?? 0;
                procs.ItemsSource = t.Result;
                var again = t.Result.FindIndex(r => r.Pid == keep);
                if (again >= 0) procs.SelectedIndex = again;
                FindScroll(procs)?.ScrollToVerticalOffset(off);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static ScrollViewer FindScroll(DependencyObject d)
        {
            if (d is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var r = FindScroll(VisualTreeHelper.GetChild(d, i)); if (r != null) return r; }
            return null;
        }

        static string Describe(int pid, string name)
        {
            lock (descriptions) if (descriptions.TryGetValue(name, out var known)) return known;
            string text = "";
            try
            {
                var h = W32.OpenProcess(0x1000, false, (uint)pid);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024); int n = sb.Capacity;
                        if (W32.QueryFullProcessImageName(h, 0, sb, ref n)) text = FileVersionInfo.GetVersionInfo(sb.ToString()).FileDescription ?? "";
                    }
                    finally { W32.CloseHandle(h); }
                }
            }
            catch { }
            lock (descriptions) descriptions[name] = text;
            return text;
        }

        void EndTask()
        {
            if (apps.SelectedItem is AppRow a) { W32.Close(a.Hwnd); RefreshApps(); }
        }

        void SwitchTo()
        {
            if (apps.SelectedItem is AppRow a) W32.Activate(a.Hwnd);
        }

        void EndProcess()
        {
            if (!(procs.SelectedItem is ProcRow p)) return;
            var bare = p.Name.EndsWith(".exe") ? p.Name.Substring(0, p.Name.Length - 4) : p.Name;
            if (Protected.Contains(bare))
            {
                MessageBox.Show(this, L("\"" + p.Name + "\" wird von Windows benötigt und kann nicht beendet werden.", "\"" + p.Name + "\" is needed by Windows and cannot be ended."), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var ok = MessageBox.Show(this, L("Möchten Sie \"" + p.Name + "\" beenden?\n\nWenn ein geöffnetes Programm diesem Prozess zugeordnet ist, wird es geschlossen und nicht gespeicherte Daten gehen verloren.", "Do you want to end \"" + p.Name + "\"?\n\nIf an open program is associated with this process, it will close and you will lose any unsaved data."), Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (ok != MessageBoxResult.OK) return;
            try { using (var pr = Process.GetProcessById(p.Pid)) pr.Kill(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
            RefreshProcs();
        }

        /// <summary>The black history graph with the green grid of the Windows 7 Task Manager; the grid scrolls with time.</summary>
        sealed class Graph : FrameworkElement
        {
            public Color Line = C(0x00FF00);
            readonly List<double> values = new List<double>();
            int shift;
            public void Push(double v) { values.Add(v); if (values.Count > 200) values.RemoveAt(0); shift = (shift + 1) % 6; InvalidateVisual(); }
            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth, h = ActualHeight;
                if (w < 4 || h < 4) return;
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                var grid = new Pen(B(0x008040), 1);
                const double cell = 12;
                for (double y = h; y >= 0; y -= cell) dc.DrawLine(grid, new Point(0, Math.Round(y) + 0.5), new Point(w, Math.Round(y) + 0.5));
                for (double x = w - shift * 2; x >= 0; x -= cell) dc.DrawLine(grid, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, h));
                if (values.Count < 2) return;
                const double step = 2;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    int n = values.Count;
                    for (int i = 0; i < n; i++)
                    {
                        double x = w - (n - 1 - i) * step, y = h - 1 - values[i] / 100.0 * (h - 2);
                        if (x < -step) continue;
                        if (i == 0 || x - step < -step) g.BeginFigure(new Point(x, y), false, false); else g.LineTo(new Point(x, y), true, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, new Pen(B(Line), 1), geo);
            }
        }

        /// <summary>The LED column meter: lit green segments up to the value, the number below.</summary>
        sealed class Meter : FrameworkElement
        {
            public bool Mb;
            double value; string label = "";
            public void Set(double v, string text) { value = v; label = text; InvalidateVisual(); }
            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth, h = ActualHeight;
                if (w < 4 || h < 4) return;
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, B(Mb ? C(0xFFFF00) : C(0x00FF00)), 1.0);
                dc.DrawText(ft, new Point((w - ft.Width) / 2, h - ft.Height - 3));
                double top = 6, bottom = h - ft.Height - 8, colW = Math.Min(16, (w - 20) / 2);
                int segs = Math.Max(4, (int)((bottom - top) / 3));
                int lit = (int)Math.Round(value / 100.0 * segs);
                var on = B(Mb ? C(0xFFFF00) : C(0x00FF00)); var off = B(0x008000);
                for (int i = 0; i < segs; i++)
                {
                    double y = bottom - (i + 1) * 3 + 1;
                    for (int c = 0; c < 2; c++)
                        dc.DrawRectangle(i < lit ? on : off, null, new Rect(w / 2 - colW - 1 + c * (colW + 2), y, colW, 2));
                }
            }
        }
    }

    // =====================================================================
    // Explorer (Win+E, folders from the Start menu)
    // =====================================================================
    internal sealed class SevenFiles : SevenWindow
    {
        static SevenFiles current;
        public const string ComputerPath = "::computer";

        /// <summary>Opens the explorer (or the open one) at a folder.</summary>
        public static void Open(string folder = null)
        {
            var w = Single(ref current, () => new SevenFiles());
            w.Go(folder ?? w.dir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        sealed class Entry
        {
            public string Path; public bool Dir; public long SizeV = -1; public DateTime TimeV;
            public ImageSource Icon { get; set; }
            public string Name { get; set; }
            public string Time { get; set; }
            public string Type { get; set; }
            public string Size { get; set; }
        }

        string dir;
        readonly List<string> back = new List<string>(), fwd = new List<string>();
        readonly ListView list;
        readonly StackPanel crumbs, nav;
        readonly Grid addressHost;
        readonly TextBox addressEdit, search;
        readonly TextBlock searchHint, detailName, detailInfo, detailInfo2;
        readonly Image detailIcon;
        readonly Button openBtn;
        readonly Border backBtn, fwdBtn;
        List<Entry> all = new List<Entry>();
        string sortKey = "name"; bool sortDesc;
        FileSystemWatcher watcher;
        readonly DispatcherTimer reload;

        static readonly Color Sel1 = C(0xDCEBFC), Sel2 = C(0xC1DBFC), SelLine = C(0x7DA2CE), Hover1 = C(0xF5FAFD), Hover2 = C(0xE8F3FD), HoverLine = C(0xB8D6FB);

        SevenFiles() : base(L("Explorer", "Explorer"), 980, 640)
        {
            WindowTracker.StandsFor(this, "explorer.exe");
            MinWidth = 560; MinHeight = 360;
            Icon = W32.FileIcon(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), false);
            var root = new DockPanel();

            // ---- the glass band: back/forward orbs, breadcrumb address, search ----
            var f = ShellThemes.For("win7").Frame;
            var band = new Border { Background = V((0, f.Caption), (1, Core.Applier.Mix(f.Caption, Colors.White, 0.35))), Padding = new Thickness(6, 4, 8, 6) };
            var bandGrid = new Grid();
            bandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bandGrid.ColumnDefinitions.Add(new ColumnDefinition());
            bandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var orbs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            backBtn = Orb("", L("Zurück", "Back"), Back); fwdBtn = Orb("", L("Vorwärts", "Forward"), Forward);
            orbs.Children.Add(backBtn); orbs.Children.Add(fwdBtn);
            bandGrid.Children.Add(orbs);

            crumbs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            addressEdit = new TextBox { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.White };
            addressEdit.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { var t = addressEdit.Text.Trim().Trim('"'); EndEdit(); Go(Environment.ExpandEnvironmentVariables(t)); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndEdit(); e.Handled = true; }
            };
            addressEdit.LostKeyboardFocus += (s, e) => EndEdit();
            addressHost = new Grid { Background = Brushes.White, Cursor = Cursors.IBeam };
            addressHost.Children.Add(crumbs); addressHost.Children.Add(addressEdit);
            addressHost.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == addressHost) BeginEdit(); };
            var address = new Border { Child = addressHost, Height = 24, BorderBrush = B(0x8EA4C1), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), Background = Brushes.White, ClipToBounds = true };
            Grid.SetColumn(address, 1);
            bandGrid.Children.Add(address);

            search = new TextBox { BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Padding = new Thickness(4, 0, 22, 0) };
            searchHint = new TextBlock { FontStyle = FontStyles.Italic, Foreground = B(0x6D6D6D), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 22, 0), IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
            var glass = Glyph("", 12, C(0x3C5A82)); glass.HorizontalAlignment = HorizontalAlignment.Right; glass.Margin = new Thickness(0, 0, 6, 0);
            var searchGrid = new Grid { Background = Brushes.White };
            searchGrid.Children.Add(searchHint); searchGrid.Children.Add(search); searchGrid.Children.Add(glass);
            search.TextChanged += (s, e) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; Show(); };
            search.KeyDown += (s, e) => { if (e.Key == Key.Escape) { search.Text = ""; list.Focus(); } else if (e.Key == Key.Down || e.Key == Key.Enter) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; } };
            var searchBox = new Border { Child = searchGrid, Height = 24, BorderBrush = B(0x8EA4C1), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(searchBox, 2);
            bandGrid.Children.Add(searchBox);
            band.Child = bandGrid;
            DockPanel.SetDock(band, Dock.Top);
            root.Children.Add(band);

            // ---- command bar ----
            var bar = new Border { Height = 32, Background = V((0, C(0xFAFCFE)), (0.5, C(0xE9F0F8)), (0.51, C(0xDFE8F4)), (1, C(0xDCE6F4))), BorderBrush = B(0xA0AFC3), BorderThickness = new Thickness(0, 1, 0, 1) };
            var cmds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            var organize = Cmd(L("Organisieren ▾", "Organize ▾"), null);
            organize.Click += (s, e) => { var m = ItemMenu(selection: true); m.PlacementTarget = organize; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
            cmds.Children.Add(organize);
            openBtn = Cmd(L("Öffnen", "Open"), OpenSel);
            cmds.Children.Add(openBtn);
            cmds.Children.Add(Cmd(L("Neuer Ordner", "New folder"), NewFolder));
            bar.Child = cmds;
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            // ---- details pane ----
            var details = new Border { Height = 64, Background = V((0, C(0xF4F8FC)), (1, C(0xE0E9F5))), BorderBrush = B(0xB9C9DA), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 6, 14, 6) };
            var dp = new DockPanel();
            detailIcon = new Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0) };
            RenderOptions.SetBitmapScalingMode(detailIcon, BitmapScalingMode.HighQuality);
            DockPanel.SetDock(detailIcon, Dock.Left);
            dp.Children.Add(detailIcon);
            var dsp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            detailName = new TextBlock { FontSize = 13, Foreground = B(0x1E3287) };
            detailInfo = new TextBlock { Foreground = B(0x5A6779) };
            detailInfo2 = new TextBlock { Foreground = B(0x5A6779) };
            dsp.Children.Add(detailName); dsp.Children.Add(detailInfo); dsp.Children.Add(detailInfo2);
            dp.Children.Add(dsp);
            details.Child = dp;
            DockPanel.SetDock(details, Dock.Bottom);
            root.Children.Add(details);

            // ---- navigation pane ----
            nav = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 200, Background = Brushes.White, BorderBrush = B(0xD6E5F5), BorderThickness = new Thickness(0, 0, 1, 0) };
            var navBorder = new Border { Child = navScroll, BorderBrush = B(0xD6E5F5), BorderThickness = new Thickness(0, 0, 1, 0) };
            DockPanel.SetDock(navBorder, Dock.Left);
            root.Children.Add(navBorder);
            BuildNav();

            // ---- file list ----
            list = new ListView { BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended };
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            var gv = new GridView();
            gv.Columns.Add(Col(L("Name", "Name"), null, 300, "name", SevenTasks.IconText("Icon", "Name")));
            gv.Columns.Add(Col(L("Änderungsdatum", "Date modified"), "Time", 140, "time"));
            gv.Columns.Add(Col(L("Typ", "Type"), "Type", 150, "type"));
            gv.Columns.Add(Col(L("Größe", "Size"), "Size", 90, "size", right: true));
            list.View = gv;
            list.MouseDoubleClick += (s, e) => { if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListViewItem) OpenSel(); };
            list.SelectionChanged += (s, e) => UpdateDetails();
            list.ContextMenuOpening += (s, e) =>
            {
                bool onItem = e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListViewItem;
                if (!onItem) list.SelectedItems.Clear();
                list.ContextMenu = ItemMenu(onItem);
            };
            list.ContextMenu = ItemMenu(false);
            root.Children.Add(list);
            Content = root;

            PreviewKeyDown += OnKey;
            reload = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (s, e) => { ((DispatcherTimer)s).Stop(); Load(); }, Dispatcher) { IsEnabled = false };
            Closed += (s, e) => { watcher?.Dispose(); current = null; };
        }

        Border Orb(string glyph, string tip, Action a)
        {
            // the round blue Windows 7 back/forward buttons
            var g = new Grid { Width = 26, Height = 26 };
            var fill = new Ellipse { Fill = new RadialGradientBrush(C(0x5FA8EE), C(0x1D58A8)) { GradientOrigin = new Point(0.5, 0.3), Center = new Point(0.5, 0.45), RadiusX = 0.65, RadiusY = 0.65 }, Stroke = B(0x173F78), StrokeThickness = 1 };
            g.Children.Add(fill);
            g.Children.Add(new Ellipse { Margin = new Thickness(4, 2, 4, 12), Fill = V((0, C(0xFFFFFF, 160)), (1, C(0xFFFFFF, 0))) });
            g.Children.Add(Glyph(glyph, 11, Colors.White));
            var b = new Border { Child = g, Margin = new Thickness(0, 0, 2, 0), ToolTip = tip, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            b.MouseEnter += (s, e) => { if (b.IsEnabled) fill.Fill = new RadialGradientBrush(C(0x8CC6FF), C(0x2B6CC4)) { GradientOrigin = new Point(0.5, 0.3), Center = new Point(0.5, 0.45), RadiusX = 0.65, RadiusY = 0.65 }; };
            b.MouseLeave += (s, e) => fill.Fill = new RadialGradientBrush(C(0x5FA8EE), C(0x1D58A8)) { GradientOrigin = new Point(0.5, 0.3), Center = new Point(0.5, 0.45), RadiusX = 0.65, RadiusY = 0.65 };
            b.MouseLeftButtonUp += (s, e) => { if (b.IsEnabled) a(); };
            b.IsEnabledChanged += (s, e) => b.Opacity = b.IsEnabled ? 1 : 0.45;
            return b;
        }

        static Button Cmd(string text, Action a)
        {
            // flat command bar buttons that light up blue on hover, like Windows 7
            var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 3, 2, 3), Foreground = B(0x1E395B), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            bd.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, V((0, C(0xFBFDFF)), (0.5, C(0xE3EFFD)), (1, C(0xCFE3FA))), "bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, B(0x7DA2CE), "bd"));
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            tpl.Triggers.Add(hover); tpl.Triggers.Add(off);
            b.Template = tpl;
            if (a != null) b.Click += (s, e) => a();
            return b;
        }

        GridViewColumn Col(string header, string path, double width, string key, DataTemplate cell = null, bool right = false)
        {
            var h = new GridViewColumnHeader { Content = header, HorizontalContentAlignment = HorizontalAlignment.Left };
            h.Click += (s, e) => { if (sortKey == key) sortDesc = !sortDesc; else { sortKey = key; sortDesc = false; } Show(); };
            var c = new GridViewColumn { Header = h, Width = width };
            if (cell != null) c.CellTemplate = cell;
            else if (right)
            {
                var fe = new FrameworkElementFactory(typeof(TextBlock));
                fe.SetBinding(TextBlock.TextProperty, new Binding(path));
                fe.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
                fe.SetValue(FrameworkElement.WidthProperty, width - 14);
                fe.SetValue(TextBlock.ForegroundProperty, B(0x6D6D6D));
                c.CellTemplate = new DataTemplate { VisualTree = fe };
            }
            else
            {
                var fe = new FrameworkElementFactory(typeof(TextBlock));
                fe.SetBinding(TextBlock.TextProperty, new Binding(path));
                fe.SetValue(TextBlock.ForegroundProperty, B(0x6D6D6D));
                fe.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                c.CellTemplate = new DataTemplate { VisualTree = fe };
            }
            return c;
        }

        // ---------------- navigation pane ----------------
        Border navSelected;

        void BuildNav()
        {
            nav.Children.Clear();
            void Head(string text, ImageSource icon, string target)
            {
                var r = NavRow(text, icon, target, 0);
                ((TextBlock)((StackPanel)r.Child).Children[1]).Foreground = B(0x1E3287);
                r.Margin = new Thickness(0, nav.Children.Count == 0 ? 0 : 10, 0, 0);
                nav.Children.Add(r);
            }
            void Sub(string text, ImageSource icon, string target) => nav.Children.Add(NavRow(text, icon, target, 18));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string Known(string guid, string fallback) { var p = KnownFolder(guid); return p ?? System.IO.Path.Combine(home, fallback); }
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var downloads = Known("374DE290-123F-4565-9164-39C4925E467B", "Downloads");
            Head(L("Favoriten", "Favorites"), W32.ShellItemIcon("shell:::{323CA680-C24D-4099-B94D-446DD2D7249E}", 16), null);
            Sub("Desktop", Small(desktop), desktop);
            Sub("Downloads", Small(downloads), downloads);
            Sub(L("Zuletzt besucht", "Recent Places"), Small(Environment.GetFolderPath(Environment.SpecialFolder.Recent)), Environment.GetFolderPath(Environment.SpecialFolder.Recent));
            Head(L("Bibliotheken", "Libraries"), W32.ShellItemIcon("shell:::{031E4825-7B94-4dc3-B131-E946B44C8DD5}", 16), null);
            foreach (var (name, path) in new[]
            {
                (L("Bilder", "Pictures"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                (L("Dokumente", "Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                (L("Musik", "Music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                (L("Videos", "Videos"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            }) if (Directory.Exists(path)) Sub(name, Small(path), path);
            Head(L("Computer", "Computer"), W32.ShellItemIcon("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 16), ComputerPath);
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady)) Sub(DriveName(d), Small(d.RootDirectory.FullName), d.RootDirectory.FullName);
        }

        Border NavRow(string text, ImageSource icon, string target, double indent)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent, 0, 0, 0) };
            var img = new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
            sp.Children.Add(img);
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Border { Child = sp, Height = 22, Padding = new Thickness(8, 0, 6, 0), Margin = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Tag = target };
            b.MouseEnter += (s, e) => { if (b != navSelected) { b.Background = V((0, Hover1), (1, Hover2)); b.BorderBrush = B(HoverLine); } };
            b.MouseLeave += (s, e) => { if (b != navSelected) { b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; } };
            b.MouseLeftButtonUp += (s, e) => { if (target != null) Go(target); };
            return b;
        }

        void MarkNav()
        {
            if (navSelected != null) { navSelected.Background = Brushes.Transparent; navSelected.BorderBrush = Brushes.Transparent; }
            navSelected = nav.Children.OfType<Border>().FirstOrDefault(b => b.Tag is string t && string.Equals(t.TrimEnd('\\'), (dir ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (navSelected != null) { navSelected.Background = V((0, Sel1), (1, Sel2)); navSelected.BorderBrush = B(SelLine); }
        }

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        static string KnownFolder(string guid)
        {
            try { if (SHGetKnownFolderPath(new Guid(guid), 0, IntPtr.Zero, out var p) == 0) { var s = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); return s; } } catch { }
            return null;
        }

        static string DriveName(DriveInfo d)
        {
            string label = "";
            try { label = d.VolumeLabel; } catch { }
            if (string.IsNullOrEmpty(label)) label = d.DriveType == DriveType.Removable ? L("Wechseldatenträger", "Removable Disk") : d.DriveType == DriveType.Network ? L("Netzlaufwerk", "Network Drive") : L("Lokaler Datenträger", "Local Disk");
            return label + " (" + d.Name.TrimEnd('\\') + ")";
        }

        // ---------------- shell icons and type names ----------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        static readonly Dictionary<string, (ImageSource icon, string type)> byExt = new Dictionary<string, (ImageSource, string)>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> OwnIcon = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".url", ".cur", ".ani", ".msc", ".scr" };

        static (ImageSource icon, string type) Shell(string path, bool dir, bool large = false, bool exact = false)
        {
            string key = dir ? "<dir>" : System.IO.Path.GetExtension(path);
            bool own = exact || OwnIcon.Contains(key);
            if (!own && !large && byExt.TryGetValue(key, out var hit)) return hit;
            var info = new SHFILEINFO();
            uint flags = 0x100 | 0x400 | (large ? 0u : 1u) | (own ? 0u : 0x10u);   // ICON | TYPENAME | (SMALL) | (USEFILEATTRIBUTES)
            SHGetFileInfo(own ? path : (dir ? "folder" : "x" + key), dir ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf(info), flags);
            var r = (W32.IconFromHandle(info.hIcon, true), info.type);
            if (!own && !large) byExt[key] = r;
            return r;
        }
        static ImageSource Small(string path) => Shell(path, true, exact: true).icon;

        // ---------------- loading ----------------
        public void Go(string target, bool remember = true)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            if (target != ComputerPath)
            {
                if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) { ShellActions.Run("explorer.exe", target); return; }
                if (File.Exists(target)) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } return; }
                if (!Directory.Exists(target))
                {
                    MessageBox.Show(this, L("\"" + target + "\" wurde nicht gefunden. Überprüfen Sie die Schreibweise, und wiederholen Sie den Vorgang.", "Windows can't find '" + target + "'. Check the spelling and try again."), "Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                target = System.IO.Path.GetFullPath(target);
            }
            if (remember && dir != null && !string.Equals(dir, target, StringComparison.OrdinalIgnoreCase)) { back.Add(dir); fwd.Clear(); }
            dir = target;
            search.Text = "";
            Load();
            Watch();
        }

        void Back() { if (back.Count == 0) return; fwd.Add(dir); var d = back[back.Count - 1]; back.RemoveAt(back.Count - 1); Go(d, false); }
        void Forward() { if (fwd.Count == 0) return; back.Add(dir); var d = fwd[fwd.Count - 1]; fwd.RemoveAt(fwd.Count - 1); Go(d, false); }
        void Up()
        {
            if (dir == ComputerPath) return;
            var parent = Directory.GetParent(dir.TrimEnd('\\') + (dir.EndsWith(":\\") ? "" : ""));
            Go(dir.Length <= 3 ? ComputerPath : parent?.FullName ?? ComputerPath);
        }

        void Watch()
        {
            watcher?.Dispose(); watcher = null;
            if (dir == ComputerPath) return;
            try
            {
                watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => { reload.Stop(); reload.Start(); }));
                watcher.Created += h; watcher.Deleted += h; watcher.Changed += h; watcher.Renamed += (s, e) => h(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void Load(string select = null)
        {
            var keep = select != null ? new HashSet<string>(new[] { select }, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(list.SelectedItems.Cast<Entry>().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var entries = new List<Entry>();
            var ci = De ? new CultureInfo("de-DE") : new CultureInfo("en-US");
            if (dir == ComputerPath)
            {
                Title = L("Computer", "Computer");
                foreach (var d in DriveInfo.GetDrives())
                {
                    var e = new Entry { Path = d.RootDirectory.FullName, Dir = true, Name = DriveName(d), Icon = Small(d.RootDirectory.FullName) };
                    e.Type = d.DriveType == DriveType.Fixed ? L("Lokaler Datenträger", "Local Disk") : d.DriveType == DriveType.Removable ? L("Wechseldatenträger", "Removable Disk") : d.DriveType == DriveType.CDRom ? L("CD-Laufwerk", "CD Drive") : L("Laufwerk", "Drive");
                    try { if (d.IsReady) { e.SizeV = d.TotalSize; e.Size = Gb(d.TotalFreeSpace) + L(" frei von ", " free of ") + Gb(d.TotalSize); } } catch { }
                    entries.Add(e);
                }
            }
            else
            {
                Title = System.IO.Path.GetFileName(dir.TrimEnd('\\')) is string n && n.Length > 0 ? n : dir;
                try
                {
                    var di = new DirectoryInfo(dir);
                    var items = di.EnumerateFileSystemInfos().Where(x => (x.Attributes & FileAttributes.Hidden) == 0).ToList();
                    bool exactIcons = items.Count < 600;
                    foreach (var x in items)
                    {
                        bool isDir = (x.Attributes & FileAttributes.Directory) != 0;
                        var (icon, type) = Shell(x.FullName, isDir, exact: exactIcons && !isDir && OwnIcon.Contains(x.Extension));
                        var e = new Entry { Path = x.FullName, Dir = isDir, Name = x.Name, Icon = icon, Type = isDir ? L("Dateiordner", "File folder") : type, TimeV = x.LastWriteTime };
                        e.Time = x.LastWriteTime.ToString(De ? "dd.MM.yyyy HH:mm" : "M/d/yyyy h:mm tt", ci);
                        if (!isDir && x is FileInfo fi) { e.SizeV = fi.Length; e.Size = Math.Ceiling(fi.Length / 1024.0).ToString("N0", ci) + " KB"; }
                        entries.Add(e);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            all = entries;
            Show(keep);
            backBtn.IsEnabled = back.Count > 0; fwdBtn.IsEnabled = fwd.Count > 0;
            Crumbs();
            MarkNav();
            searchHint.Text = L("Durchsuchen: ", "Search ") + Title;
        }

        static string Gb(long b) => (b / 1073741824.0).ToString("0.0") + " GB";

        void Show(HashSet<string> keep = null)
        {
            if (list == null) return;
            keep = keep ?? new HashSet<string>(list.SelectedItems.Cast<Entry>().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var q = search.Text.Trim();
            IEnumerable<Entry> shown = q.Length == 0 ? all : all.Where(e => e.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            Func<Entry, object> key = sortKey == "time" ? (Func<Entry, object>)(e => e.TimeV) : sortKey == "type" ? e => e.Type : sortKey == "size" ? e => (object)e.SizeV : e => e.Name;
            var cmp = sortKey == "name" || sortKey == "type" ? (IComparer<object>)Comparer<object>.Create((a, b) => string.Compare((string)a, (string)b, StringComparison.CurrentCultureIgnoreCase)) : Comparer<object>.Default;
            // folders first, like Explorer
            var sorted = sortDesc ? shown.OrderBy(e => e.Dir ? 0 : 1).ThenByDescending(key, cmp).ToList() : shown.OrderBy(e => e.Dir ? 0 : 1).ThenBy(key, cmp).ToList();
            list.ItemsSource = sorted;
            foreach (var e in sorted.Where(x => keep.Contains(x.Name))) list.SelectedItems.Add(e);
            if (list.SelectedItems.Count > 0) list.ScrollIntoView(list.SelectedItems[0]);
            UpdateDetails();
        }

        void Crumbs()
        {
            crumbs.Children.Clear();
            var parts = new List<(string name, string path)> { (L("Computer", "Computer"), ComputerPath) };
            if (dir != ComputerPath)
            {
                var rootDir = System.IO.Path.GetPathRoot(dir);
                var drive = DriveInfo.GetDrives().FirstOrDefault(d => string.Equals(d.Name, rootDir, StringComparison.OrdinalIgnoreCase));
                parts.Add((drive != null ? DriveName(drive) : rootDir, rootDir));
                var acc = rootDir;
                foreach (var seg in dir.Substring(rootDir.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { acc = System.IO.Path.Combine(acc, seg); parts.Add((seg, acc)); }
            }
            var icon = new Image { Source = dir == ComputerPath ? W32.ShellItemIcon("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 16) : Small(dir), Width = 16, Height = 16, Margin = new Thickness(5, 0, 2, 0) };
            crumbs.Children.Add(icon);
            foreach (var (name, path) in parts)
            {
                crumbs.Children.Add(new TextBlock { Text = " ▸ ", Foreground = B(0x46566C), FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
                var t = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
                var b = Click(t, () => Go(path), C(0xD7E9FC), radius: new CornerRadius(2), pad: new Thickness(3, 1, 3, 1));
                b.Cursor = Cursors.Arrow;
                crumbs.Children.Add(b);
            }
        }

        void BeginEdit()
        {
            crumbs.Visibility = Visibility.Collapsed;
            addressEdit.Visibility = Visibility.Visible;
            addressEdit.Text = dir == ComputerPath ? L("Computer", "Computer") : dir;
            addressEdit.Focus(); addressEdit.SelectAll();
        }

        void EndEdit()
        {
            if (addressEdit.Visibility != Visibility.Visible) return;
            addressEdit.Visibility = Visibility.Collapsed;
            crumbs.Visibility = Visibility.Visible;
        }

        void UpdateDetails()
        {
            var sel = Sel;
            openBtn.IsEnabled = sel.Count > 0;
            var ci = De ? new CultureInfo("de-DE") : new CultureInfo("en-US");
            if (sel.Count == 1)
            {
                var e = sel[0];
                detailIcon.Source = Shell(e.Path, e.Dir, large: true, exact: true).icon;
                detailName.Text = e.Name;
                detailInfo.Text = e.Type;
                detailInfo2.Text = e.Dir ? (e.Time != null ? L("Änderungsdatum: ", "Date modified: ") + e.Time : e.Size ?? "") : L("Änderungsdatum: ", "Date modified: ") + e.Time + "      " + L("Größe: ", "Size: ") + e.Size;
            }
            else
            {
                detailIcon.Source = dir == ComputerPath ? W32.ShellItemIcon("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 48) : Shell(dir ?? "", true, large: true, exact: true).icon;
                int n = (list.ItemsSource as List<Entry>)?.Count ?? 0;
                detailName.Text = sel.Count > 1 ? sel.Count + L(" Elemente ausgewählt", " items selected") : n + (n == 1 ? L(" Element", " item") : L(" Elemente", " items"));
                long bytes = sel.Where(x => !x.Dir && x.SizeV > 0).Sum(x => x.SizeV);
                detailInfo.Text = sel.Count > 1 && bytes > 0 ? L("Größe: ", "Size: ") + Math.Ceiling(bytes / 1024.0).ToString("N0", ci) + " KB" : "";
                detailInfo2.Text = "";
            }
        }

        List<Entry> Sel => list.SelectedItems.Cast<Entry>().ToList();

        // ---------------- actions ----------------
        void OnKey(object s, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) { if (e.Key == Key.F5) Load(); return; }
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool done = true;
            if (key == Key.Enter) OpenSel();
            else if (key == Key.Back || (alt && key == Key.Up)) Up();
            else if (alt && key == Key.Left || key == Key.BrowserBack) Back();
            else if (alt && key == Key.Right || key == Key.BrowserForward) Forward();
            else if (key == Key.F2) Rename();
            else if (key == Key.F5) Load();
            else if (key == Key.Delete) Delete(shift);
            else if (ctrl && shift && key == Key.N) NewFolder();
            else if (ctrl && key == Key.C) ToClipboard(false);
            else if (ctrl && key == Key.X) ToClipboard(true);
            else if (ctrl && key == Key.V) Paste();
            else if (ctrl && key == Key.A) list.SelectAll();
            else if ((ctrl && (key == Key.F || key == Key.E)) || key == Key.F3) search.Focus();
            else if ((ctrl && key == Key.L) || (alt && key == Key.D) || key == Key.F4) BeginEdit();
            else if (alt && key == Key.Enter) Properties();
            else done = false;
            if (done) e.Handled = true;
        }

        ContextMenu ItemMenu(bool selection)
        {
            var m = new ContextMenu();
            void Add(string text, Action a, bool enabled = true, string gesture = null) { var i = new MenuItem { Header = text, IsEnabled = enabled, InputGestureText = gesture ?? "" }; i.Click += (s, e) => a(); m.Items.Add(i); }
            bool any = list.SelectedItems.Count > 0 && selection;
            bool canPaste = false; try { canPaste = Clipboard.ContainsFileDropList() && dir != ComputerPath; } catch { }
            if (any)
            {
                Add(L("Öffnen", "Open"), OpenSel);
                m.Items.Add(new Separator());
                Add(L("Ausschneiden", "Cut"), () => ToClipboard(true), dir != ComputerPath, L("Strg+X", "Ctrl+X"));
                Add(L("Kopieren", "Copy"), () => ToClipboard(false), dir != ComputerPath, L("Strg+C", "Ctrl+C"));
                Add(L("Einfügen", "Paste"), Paste, canPaste, L("Strg+V", "Ctrl+V"));
                m.Items.Add(new Separator());
                Add(L("Löschen", "Delete"), () => Delete(false), dir != ComputerPath, L("Entf", "Del"));
                Add(L("Umbenennen", "Rename"), Rename, dir != ComputerPath && list.SelectedItems.Count == 1, "F2");
                m.Items.Add(new Separator());
                Add(L("Eigenschaften", "Properties"), Properties);
            }
            else
            {
                Add(L("Aktualisieren", "Refresh"), () => Load(), true, "F5");
                m.Items.Add(new Separator());
                Add(L("Einfügen", "Paste"), Paste, canPaste, L("Strg+V", "Ctrl+V"));
                Add(L("Neuer Ordner", "New folder"), NewFolder, dir != ComputerPath, L("Strg+Umschalt+N", "Ctrl+Shift+N"));
                m.Items.Add(new Separator());
                Add(L("Eigenschaften", "Properties"), Properties, dir != ComputerPath);
            }
            return m;
        }

        void OpenSel()
        {
            var sel = Sel;
            if (sel.Count == 0) return;
            if (sel.Count == 1 && sel[0].Dir) { Go(sel[0].Path); return; }
            foreach (var x in sel.Where(x => !x.Dir))
            {
                try { Process.Start(new ProcessStartInfo(x.Path) { UseShellExecute = true, WorkingDirectory = dir }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, x.Name, MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }

        void ToClipboard(bool cut)
        {
            var sel = Sel;
            if (sel.Count == 0 || dir == ComputerPath) return;
            var files = new StringCollection();
            foreach (var x in sel) files.Add(x.Path);
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            try { Clipboard.SetDataObject(data, true); } catch { }
        }

        void Paste()
        {
            if (dir == ComputerPath) return;
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
            if (sel.Count == 0 || dir == ComputerPath) return;
            // Windows' own delete: recycle bin, progress, undo (it asks only for a permanent delete)
            Task.Run(() => HackerFiles.FileOp(HackerFiles.FO_DELETE, sel.Select(x => x.Path).ToArray(), null, permanent ? (ushort)0 : (ushort)(HackerFiles.FOF_ALLOWUNDO | HackerFiles.FOF_NOCONFIRMATION)))
                .ContinueWith(_ => Load(), TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Rename()
        {
            var sel = Sel;
            if (sel.Count != 1 || dir == ComputerPath) return;
            var name = Ask(L("Umbenennen", "Rename"), L("Neuer Name:", "New name:"), sel[0].Name);
            if (name == null || name == sel[0].Name) return;
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, L("Ein Dateiname darf keines der folgenden Zeichen enthalten:\n\\ / : * ? \" < > |", "A file name can't contain any of the following characters:\n\\ / : * ? \" < > |"), L("Umbenennen", "Rename"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var target = System.IO.Path.Combine(dir, name);
                if (sel[0].Dir) Directory.Move(sel[0].Path, target); else File.Move(sel[0].Path, target);
                Load(name);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, L("Umbenennen", "Rename"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void NewFolder()
        {
            if (dir == ComputerPath) return;
            var baseName = L("Neuer Ordner", "New folder");
            var name = baseName;
            for (int i = 2; Directory.Exists(System.IO.Path.Combine(dir, name)) || File.Exists(System.IO.Path.Combine(dir, name)); i++) name = baseName + " (" + i + ")";
            try { Directory.CreateDirectory(System.IO.Path.Combine(dir, name)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, baseName, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            Load(name);
            var chosen = Ask(baseName, L("Name des neuen Ordners:", "Name of the new folder:"), name);
            if (chosen != null && chosen != name && chosen.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0)
            {
                try { Directory.Move(System.IO.Path.Combine(dir, name), System.IO.Path.Combine(dir, chosen)); Load(chosen); } catch { }
            }
        }

        void Properties()
        {
            var sel = Sel;
            HackerFiles.ShowProperties(sel.Count == 1 ? sel[0].Path : dir == ComputerPath ? "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" : dir);
        }
    }
}

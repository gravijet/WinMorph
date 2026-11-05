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
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Base of the hacker look's own apps (task manager, file manager): a normal window
    /// (snap, resize, Alt+Tab, status line) drawn in terminal style.
    /// </summary>
    internal abstract class HackerWindow : Window
    {
        protected readonly ShellTheme t;
        protected Color G => t.Accent;
        protected Color D => t.BarFgDim;
        protected static readonly FontFamily Mono = new FontFamily("Consolas");
        protected static bool De => Core.Loc.De;
        protected static string L(string de, string en) => De ? de : en;
        protected readonly TextBlock status;
        IntPtr hwnd;

        protected HackerWindow(ShellTheme theme, string title, double w, double h)
        {
            t = theme;
            Title = title; Width = w; Height = h; MinWidth = 560; MinHeight = 340;
            Background = B(0x050505); Foreground = B(G); FontFamily = Mono; FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/WinMorph.ico")); } catch { }
            status = Text("", D);
            status.Margin = new Thickness(10, 4, 10, 6);
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                WindowTracker.OwnApps.Add(hwnd);   // listed in the status line and Alt+Tab like any app
                int on = 1, corner = 1, cap = 0x0A0A0A, txt = G.B << 16 | G.G << 8 | G.R;
                W32.DwmSetWindowAttribute(hwnd, 20, ref on, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_CAPTION_COLOR, ref cap, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_TEXT_COLOR, ref txt, 4);
                W32.DwmSetWindowAttribute(hwnd, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            Closed += (s, e) => WindowTracker.OwnApps.Remove(hwnd);
            RenderGuard.Watch(this);
        }

        protected TextBlock Text(string s, Color c, double size = 13)
            => new TextBlock { Text = s, Foreground = B(c), FontFamily = Mono, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        protected TextBox Input()
            => new TextBox
            {
                Background = B(0x000000), Foreground = B(G), CaretBrush = B(G), SelectionBrush = B(A(G, 120)), BorderBrush = B(A(G, 90)), BorderThickness = new Thickness(0, 0, 0, 1),
                FontFamily = Mono, FontSize = 13, Padding = new Thickness(4, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center,
            };

        protected Border Btn(string text, Action a)
        {
            var tb = Text("[ " + text + " ]", G);
            var b = Click(tb, a, G, pad: new Thickness(2, 2, 2, 2));
            b.MouseEnter += (s, e) => tb.Foreground = B(0x000000);
            b.MouseLeave += (s, e) => tb.Foreground = B(G);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>One line of a list: text plus its colour.</summary>
        protected sealed class Row { public string Text { get; set; } public Brush Fg { get; set; } public object Tag; }

        /// <summary>A fast, virtualised monospace list with terminal-style selection.</summary>
        protected ListBox List()
        {
            var lb = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(lb, System.Windows.Controls.ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(lb, true);
            VirtualizingPanel.SetVirtualizationMode(lb, VirtualizationMode.Recycling);
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding("Text"));
            tb.SetBinding(TextBlock.ForegroundProperty, new Binding("Fg"));
            tb.SetValue(TextBlock.FontFamilyProperty, Mono);
            tb.SetValue(TextBlock.FontSizeProperty, 13.0);
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            lb.ItemTemplate = new DataTemplate { VisualTree = tb };
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.PaddingProperty, new Thickness(10, 1, 10, 1));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            tpl.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, B(A(G, 22)), "bd"));
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, B(A(G, 70)), "bd"));
            tpl.Triggers.Add(hover);
            tpl.Triggers.Add(sel);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(TemplateProperty, tpl));
            style.Setters.Add(new Setter(FocusVisualStyleProperty, null));
            lb.ItemContainerStyle = style;
            return lb;
        }

        protected static string Fit(string s, int n) => s == null ? new string(' ', n) : s.Length > n ? s.Substring(0, n - 1) + "~" : s.PadRight(n);

        protected static string Size(long b) => b < 1024 ? b + " B" : b < 1048576 ? (b / 1024.0).ToString("0.0") + " K" : b < 1073741824 ? (b / 1048576.0).ToString("0.0") + " M" : (b / 1073741824.0).ToString("0.00") + " G";

        /// <summary>Brings an existing window of this kind to the front instead of opening a second one.</summary>
        protected static T Single<T>(ref T current, Func<T> make) where T : Window
        {
            if (current != null && current.IsLoaded)
            {
                if (current.WindowState == WindowState.Minimized) current.WindowState = WindowState.Normal;
                current.Activate();
                return current;
            }
            var w = make();
            current = w;
            w.Show();
            w.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(w).Handle);
            return w;
        }
    }

    // =====================================================================
    // task manager: processes with CPU/RAM, kill, switch to, open location
    // =====================================================================
    internal sealed class HackerTasks : HackerWindow
    {
        static HackerTasks current;
        public static void Open(ShellTheme t) => Single(ref current, () => new HackerTasks(t));

        readonly ListBox list;
        readonly TextBox filter;
        readonly TextBlock head, bars, cols;
        readonly DispatcherTimer timer;
        readonly Dictionary<int, (TimeSpan cpu, DateTime at)> lastCpu = new Dictionary<int, (TimeSpan, DateTime)>();
        string sort = "cpu";
        bool loading;

        sealed class Proc { public int Pid; public string Name, Path, Window; public double Cpu; public long Mem; public IntPtr Hwnd; }

        // never offered for killing: Windows needs them
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "Registry", "Memory Compression", "LsaIso", "sihost" };

        HackerTasks(ShellTheme theme) : base(theme, "top · " + L("Task-Manager", "task manager"), 860, 600)
        {
            var root = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            head = Text("", G, 13.5); head.Margin = new Thickness(10, 0, 10, 0);
            bars = Text("", G); bars.Margin = new Thickness(10, 2, 10, 6);
            var tools = new DockPanel { Margin = new Thickness(10, 0, 10, 6) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Btn(L("beenden", "kill") + " ⌦", Kill));
            buttons.Children.Add(Btn(L("wechseln", "switch") + " ⏎", SwitchTo));
            buttons.Children.Add(Btn(L("ordner", "location"), Location));
            buttons.Children.Add(Btn("taskmgr.exe", () => ShellActions.Run("taskmgr.exe")));
            DockPanel.SetDock(buttons, Dock.Right);
            tools.Children.Add(buttons);
            var prompt = Text("filter> ", D);
            DockPanel.SetDock(prompt, Dock.Left);
            tools.Children.Add(prompt);
            filter = Input();
            filter.TextChanged += (s, e) => Refresh();
            tools.Children.Add(filter);
            cols = Text("", D); cols.Margin = new Thickness(10, 2, 10, 2); cols.Cursor = Cursors.Hand;
            cols.ToolTip = L("Klick: sortieren (cpu → ram → name)", "Click: sort (cpu → mem → name)");
            cols.MouseLeftButtonUp += (s, e) => { sort = sort == "cpu" ? "mem" : sort == "mem" ? "name" : "cpu"; Refresh(); };
            list = List();
            list.MouseDoubleClick += (s, e) => SwitchTo();
            foreach (var el in new UIElement[] { head, bars, tools, cols }) { DockPanel.SetDock(el, Dock.Top); root.Children.Add(el); }
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);
            root.Children.Add(list);
            Content = root;
            PreviewKeyDown += OnKey;
            Loaded += (s, e) => { filter.Focus(); Refresh(); };
            // only while visible: a minimised task manager costs nothing
            timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) => { if (WindowState != WindowState.Minimized && IsVisible) Refresh(); }, Dispatcher);
            Closed += (s, e) => { timer.Stop(); current = null; };
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { if (filter.Text.Length > 0) filter.Text = ""; else Close(); e.Handled = true; }
            else if (e.Key == Key.Delete) { Kill(); e.Handled = true; }
            else if (e.Key == Key.Enter) { SwitchTo(); e.Handled = true; }
            else if (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.PageDown || e.Key == Key.PageUp)
            {
                int n = list.Items.Count; if (n == 0) return;
                int step = e.Key == Key.PageDown ? 15 : e.Key == Key.PageUp ? -15 : e.Key == Key.Down ? 1 : -1;
                list.SelectedIndex = Math.Max(0, Math.Min(n - 1, list.SelectedIndex + step));
                list.ScrollIntoView(list.SelectedItem);
                e.Handled = true;
            }
        }

        Proc Selected => (list.SelectedItem as Row)?.Tag as Proc;

        void Refresh()
        {
            if (loading) return;
            loading = true;
            var f = filter.Text.Trim();
            var by = sort;
            var windows = new Dictionary<int, (IntPtr, string)>();
            W32.EnumWindows((h, l) =>
            {
                if (WindowTracker.IsAppWindow(h, 0)) { W32.GetWindowThreadProcessId(h, out uint pid); if (!windows.ContainsKey((int)pid)) windows[(int)pid] = (h, W32.Text(h)); }
                return true;
            }, IntPtr.Zero);
            Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                var procs = new List<Proc>();
                int cores = Environment.ProcessorCount;
                var seen = new HashSet<int>();
                foreach (var p in Process.GetProcesses())
                {
                    using (p)
                    {
                        var pr = new Proc { Pid = p.Id, Name = p.ProcessName };
                        seen.Add(p.Id);
                        try { pr.Mem = p.WorkingSet64; } catch { }
                        try
                        {
                            var cpu = p.TotalProcessorTime;
                            lock (lastCpu)
                            {
                                if (lastCpu.TryGetValue(p.Id, out var last)) pr.Cpu = Math.Max(0, (cpu - last.cpu).TotalMilliseconds / Math.Max(1, (now - last.at).TotalMilliseconds) * 100 / cores);
                                lastCpu[p.Id] = (cpu, now);
                            }
                        }
                        catch { }
                        if (windows.TryGetValue(p.Id, out var w)) { pr.Hwnd = w.Item1; pr.Window = w.Item2; }
                        procs.Add(pr);
                    }
                }
                lock (lastCpu) foreach (var gone in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(gone);
                IEnumerable<Proc> q = procs;
                if (f.Length > 0) q = q.Where(x => x.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || x.Pid.ToString() == f || (x.Window ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                q = by == "mem" ? q.OrderByDescending(x => x.Mem) : by == "name" ? q.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase) : q.OrderByDescending(x => x.Cpu).ThenByDescending(x => x.Mem);
                return (all: procs, shown: q.ToList());
            }).ContinueWith(tk =>
            {
                loading = false;
                if (tk.IsFaulted || !IsLoaded) return;
                var (all, shown) = tk.Result;
                int keep = Selected?.Pid ?? -1;
                var rows = shown.Select(x => new Row
                {
                    Text = x.Pid.ToString().PadLeft(7) + "  " + Fit(x.Name, 26) + (x.Cpu >= 0.05 ? x.Cpu.ToString("0.0") : "·").PadLeft(6) + "  " + Size(x.Mem).PadLeft(9) + "   " + (x.Hwnd != IntPtr.Zero ? "● " + x.Window : ""),
                    Fg = B(x.Cpu >= 25 ? C(0xFF5555) : x.Hwnd != IntPtr.Zero ? G : Core.Applier.Mix(G, Colors.Black, 0.25)),
                    Tag = x,
                }).ToList();
                list.ItemsSource = rows;
                var again = rows.FindIndex(r => ((Proc)r.Tag).Pid == keep);
                if (again >= 0) list.SelectedIndex = again; else if (rows.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
                double cpu = SysInfo.CpuSample(), mem = SysStats.Mem();
                SysStats.MemTotals(out ulong total, out ulong avail);
                head.Text = "top - " + DateTime.Now.ToString("HH:mm:ss") + "  up " + SysInfo.UptimeText + "  ·  " + all.Count + " " + L("prozesse", "processes") + "  ·  " + all.Count(x => x.Hwnd != IntPtr.Zero) + " " + L("fenster", "windows");
                bars.Text = "cpu [" + SysInfo.Bar(cpu, 24, '|', '·') + "] " + cpu.ToString("00") + "%    mem [" + SysInfo.Bar(mem, 24, '|', '·') + "] " + mem.ToString("00") + "%  " + Size((long)(total - avail)) + " / " + Size((long)total);
                string Mark(string k) => sort == k ? "▼" : " ";
                cols.Text = "    PID  " + Fit("NAME" + Mark("name"), 26) + ("CPU%" + Mark("cpu")).PadLeft(6) + "  " + ("MEM" + Mark("mem")).PadLeft(9) + "   " + L("FENSTER", "WINDOW");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Kill()
        {
            var p = Selected;
            if (p == null) return;
            if (Protected.Contains(p.Name)) { status.Text = "! " + p.Name + L(" wird von Windows gebraucht – nicht beendet.", " is needed by Windows – not killed."); return; }
            try
            {
                using (var pr = Process.GetProcessById(p.Pid)) pr.Kill();
                status.Text = "killed " + p.Name + " (" + p.Pid + ")";
            }
            catch (Exception ex) { status.Text = "! " + p.Name + ": " + ex.Message; }
            Refresh();
        }

        void SwitchTo()
        {
            var p = Selected;
            if (p == null) return;
            if (p.Hwnd == IntPtr.Zero) { status.Text = p.Name + L(" hat kein Fenster.", " has no window."); return; }
            W32.Activate(p.Hwnd);
        }

        void Location()
        {
            var p = Selected;
            if (p == null) return;
            try
            {
                string path;
                using (var pr = Process.GetProcessById(p.Pid)) path = pr.MainModule.FileName;
                HackerFiles.Open(t, Path.GetDirectoryName(path), Path.GetFileName(path));
            }
            catch { status.Text = L("! kein Zugriff auf den Pfad (Systemprozess)", "! no access to the path (system process)"); }
        }
    }

    // =====================================================================
    // file manager: browse, open, copy/cut/paste, rename, delete, new folder
    // =====================================================================
    internal sealed class HackerFiles : HackerWindow
    {
        static HackerFiles current;

        /// <summary>Opens the file manager (or the open one) at a folder, optionally with a file selected.</summary>
        public static void Open(ShellTheme t, string folder = null, string select = null)
        {
            var w = Single(ref current, () => new HackerFiles(t));
            w.Go(folder ?? w.dir ?? Home, select);
        }

        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        readonly ListBox list;
        readonly TextBox path, filter;
        readonly StackPanel places;
        readonly List<string> back = new List<string>(), fwd = new List<string>();
        string dir;
        bool hidden;
        FileSystemWatcher watch;
        DispatcherTimer watchDelay;
        string pendingSelect;

        sealed class Entry { public string Path, Name; public bool Dir; public long Size; public DateTime Time; public bool Up; }

        HackerFiles(ShellTheme theme) : base(theme, "files", 980, 640)
        {
            var root = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            // path line
            var top = new DockPanel { Margin = new Thickness(10, 0, 10, 6) };
            var nav = new StackPanel { Orientation = Orientation.Horizontal };
            nav.Children.Add(Btn("←", Back));
            nav.Children.Add(Btn("→", Forward));
            nav.Children.Add(Btn("↑", Up));
            DockPanel.SetDock(nav, Dock.Left);
            top.Children.Add(nav);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Text(" filter> ", D));
            filter = Input(); filter.Width = 160;
            filter.TextChanged += (s, e) => Load();
            right.Children.Add(filter);
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);
            path = Input();
            path.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Go(Environment.ExpandEnvironmentVariables(path.Text.Trim().Replace("~", Home))); list.Focus(); e.Handled = true; } };
            top.Children.Add(path);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            // action line
            var acts = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
            foreach (var (text, a) in new (string, Action)[]
            {
                (L("öffnen", "open"), OpenSel), (L("kopieren", "copy"), () => ToClipboard(false)), (L("ausschneiden", "cut"), () => ToClipboard(true)), (L("einfügen", "paste"), Paste),
                (L("umbenennen", "rename"), Rename), (L("löschen", "delete"), () => Delete(false)), (L("neuer ordner", "new dir"), NewFolder),
                (L("eigenschaften", "props"), Props), ("explorer.exe", () => ShellActions.Run("explorer.exe", "\"" + dir + "\"")), (L("terminal hier", "terminal here"), () => ShellActions.Run("wt.exe", "-d \"" + dir + "\"")),
            }) acts.Children.Add(Btn(text, a));
            DockPanel.SetDock(acts, Dock.Top);
            root.Children.Add(acts);

            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            // places on the left
            places = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            var side = new Border { Width = 190, BorderBrush = B(A(G, 50)), BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = places, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            DockPanel.SetDock(side, Dock.Left);
            root.Children.Add(side);
            BuildPlaces();

            list = List();
            list.SelectionMode = SelectionMode.Extended;
            list.MouseDoubleClick += (s, e) => OpenSel();
            list.SelectionChanged += (s, e) => ShowStatus();
            root.Children.Add(list);
            Content = root;
            PreviewKeyDown += OnKey;
            Closed += (s, e) => { watch?.Dispose(); current = null; };
            Loaded += (s, e) => list.Focus();
        }

        void BuildPlaces()
        {
            places.Children.Clear();
            void Place(string label, string target, string glyph)
            {
                var b = Click(Text(glyph + " " + label, G), () => Go(target), A(G, 40), pad: new Thickness(8, 3, 8, 3));
                places.Children.Add(b);
            }
            places.Children.Add(Text(L("orte", "places"), D)); ((TextBlock)places.Children[0]).Margin = new Thickness(8, 4, 0, 4);
            Place("~ home", Home, "⌂");
            Place(L("desktop", "desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "▪");
            Place(L("dokumente", "documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "▪");
            Place(L("downloads", "downloads"), Path.Combine(Home, "Downloads"), "↓");
            Place(L("bilder", "pictures"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "▪");
            Place(L("musik", "music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "♪");
            Place(L("videos", "videos"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "▶");
            var drives = Text(L("laufwerke", "drives"), D); drives.Margin = new Thickness(8, 12, 0, 4);
            places.Children.Add(drives);
            foreach (var d in DriveInfo.GetDrives())
            {
                string info = "";
                try { if (d.IsReady) info = "  " + (100 - d.TotalFreeSpace * 100 / Math.Max(1, d.TotalSize)) + "%"; } catch { }
                Place(d.Name.TrimEnd('\\') + info, d.Name, "◆");
            }
            var bin = Click(Text("♻ " + L("papierkorb", "recycle bin"), G), () => ShellActions.Run("explorer.exe", "shell:RecycleBinFolder"), A(G, 40), pad: new Thickness(8, 3, 8, 3));
            bin.Margin = new Thickness(0, 12, 0, 0);
            places.Children.Add(bin);
        }

        void OnKey(object s, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (path.IsKeyboardFocused || filter.IsKeyboardFocused)
            {
                if (key == Key.Escape) { if (filter.IsKeyboardFocused) filter.Text = ""; list.Focus(); e.Handled = true; }
                else if (key == Key.Down && filter.IsKeyboardFocused) { list.Focus(); e.Handled = true; }
                return;
            }
            e.Handled = true;
            if (key == Key.Enter) OpenSel();
            else if (key == Key.Back || (alt && key == Key.Up)) Up();
            else if (alt && key == Key.Left) Back();
            else if (alt && key == Key.Right) Forward();
            else if (key == Key.F5) { BuildPlaces(); Load(); }
            else if (key == Key.F2) Rename();
            else if (key == Key.Delete) Delete(shift);
            else if (ctrl && key == Key.C) ToClipboard(false);
            else if (ctrl && key == Key.X) ToClipboard(true);
            else if (ctrl && key == Key.V) Paste();
            else if (ctrl && shift && key == Key.N) NewFolder();
            else if (ctrl && key == Key.L) { path.Focus(); path.SelectAll(); }
            else if (ctrl && key == Key.F) filter.Focus();
            else if (ctrl && key == Key.H) { hidden = !hidden; Load(); status.Text = hidden ? L("versteckte dateien: an", "hidden files: on") : L("versteckte dateien: aus", "hidden files: off"); }
            else if (ctrl && key == Key.A) list.SelectAll();
            else if (key == Key.Escape) Close();
            else e.Handled = false;
        }

        public void Go(string target, string select = null)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try { target = Path.GetFullPath(target); } catch { status.Text = "! " + target; return; }
            if (File.Exists(target)) { select = Path.GetFileName(target); target = Path.GetDirectoryName(target); }
            if (!Directory.Exists(target)) { status.Text = "! " + L("ordner nicht gefunden: ", "folder not found: ") + target; return; }
            if (dir != null && !string.Equals(dir, target, StringComparison.OrdinalIgnoreCase)) { back.Add(dir); fwd.Clear(); }
            dir = target;
            pendingSelect = select;
            filter.Text = "";
            Load();
            Watch();
        }

        void Back() { if (back.Count == 0) return; fwd.Add(dir); var d = back[back.Count - 1]; back.RemoveAt(back.Count - 1); dir = d; Load(); Watch(); }
        void Forward() { if (fwd.Count == 0) return; back.Add(dir); var d = fwd[fwd.Count - 1]; fwd.RemoveAt(fwd.Count - 1); dir = d; Load(); Watch(); }
        void Up()
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) return;
            var from = Path.GetFileName(dir.TrimEnd('\\'));
            back.Add(dir); fwd.Clear();
            dir = parent.FullName;
            pendingSelect = from;
            Load(); Watch();
        }

        /// <summary>Changes in the folder show up by themselves (a moment later, all at once).</summary>
        void Watch()
        {
            watch?.Dispose(); watch = null;
            try
            {
                watch = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    watchDelay?.Stop();
                    watchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (s2, e2) => { watchDelay.Stop(); Load(); }, Dispatcher);
                }));
                watch.Created += h; watch.Deleted += h; watch.Changed += h;
                watch.Renamed += (s, e) => h(s, e);
                watch.EnableRaisingEvents = true;
            }
            catch { }
        }

        void Load(string select = null)
        {
            var d = dir;
            var f = filter.Text.Trim();
            bool showHidden = hidden;
            var keep = select ?? pendingSelect ?? ((list.SelectedItem as Row)?.Tag as Entry)?.Name;
            pendingSelect = null;
            path.Text = d.StartsWith(Home, StringComparison.OrdinalIgnoreCase) ? "~" + d.Substring(Home.Length) : d;
            Title = "files · " + (Path.GetFileName(d.TrimEnd('\\')) is string n && n.Length > 0 ? n : d);
            Task.Run(() =>
            {
                var items = new List<Entry>();
                var di = new DirectoryInfo(d);
                foreach (var x in di.EnumerateFileSystemInfos())
                {
                    try
                    {
                        if (!showHidden && (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (f.Length > 0 && x.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        bool isDir = (x.Attributes & FileAttributes.Directory) != 0;
                        items.Add(new Entry { Path = x.FullName, Name = x.Name, Dir = isDir, Size = isDir ? 0 : ((FileInfo)x).Length, Time = x.LastWriteTime });
                    }
                    catch { }
                }
                return items.OrderByDescending(x => x.Dir).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }).ContinueWith(tk =>
            {
                if (!IsLoaded || d != dir) return;
                if (tk.IsFaulted) { list.ItemsSource = null; status.Text = "! " + (tk.Exception?.InnerException?.Message ?? "error"); return; }
                var dimC = Core.Applier.Mix(G, Colors.Black, 0.3);
                var rows = new List<Row>();
                if (Directory.GetParent(d) != null && f.Length == 0) rows.Add(new Row { Text = "d  " + Fit("..", 48), Fg = B(D), Tag = new Entry { Up = true, Dir = true, Name = ".." } });
                foreach (var x in tk.Result)
                    rows.Add(new Row
                    {
                        Text = (x.Dir ? "d  " : "-  ") + Fit(x.Name + (x.Dir ? "/" : ""), 48) + "  " + (x.Dir ? "" : Size(x.Size)).PadLeft(9) + "   " + x.Time.ToString("yyyy-MM-dd HH:mm"),
                        Fg = B(x.Dir ? G : dimC),
                        Tag = x,
                    });
                list.ItemsSource = rows;
                int at = keep == null ? -1 : rows.FindIndex(r => string.Equals(((Entry)r.Tag).Name, keep, StringComparison.OrdinalIgnoreCase));
                list.SelectedIndex = at >= 0 ? at : rows.Count > 0 ? 0 : -1;
                if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
                ShowStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        List<Entry> Sel => list.SelectedItems.Cast<Row>().Select(r => (Entry)r.Tag).Where(x => !x.Up).ToList();

        void ShowStatus()
        {
            var rows = list.ItemsSource as List<Row>;
            if (rows == null) return;
            var all = rows.Select(r => (Entry)r.Tag).Where(x => !x.Up).ToList();
            var sel = Sel;
            string free = "";
            try { var drv = new DriveInfo(Path.GetPathRoot(dir)); free = "  ·  " + Size(drv.AvailableFreeSpace) + L(" frei", " free"); } catch { }
            status.Text = all.Count(x => x.Dir) + L(" ordner, ", " dirs, ") + all.Count(x => !x.Dir) + L(" dateien", " files") + free
                + (sel.Count > 1 ? "  ·  " + sel.Count + L(" ausgewählt", " selected") + " (" + Size(sel.Sum(x => x.Size)) + ")" : sel.Count == 1 && !sel[0].Dir ? "  ·  " + sel[0].Name + " " + Size(sel[0].Size) : "")
                + "    ⏎ " + L("öffnen", "open") + "  ⌫ " + L("hoch", "up") + "  F2  ⌦  ^C ^X ^V  ^⇧N  ^H";
        }

        void OpenSel()
        {
            var row = list.SelectedItem as Row;
            if (row == null) return;
            var x = (Entry)row.Tag;
            if (x.Up) { Up(); return; }
            if (x.Dir) { Go(x.Path); return; }
            try { Process.Start(new ProcessStartInfo(x.Path) { UseShellExecute = true, WorkingDirectory = dir }); }
            catch (Exception ex) { status.Text = "! " + ex.Message; }
        }

        void ToClipboard(bool cut)
        {
            var sel = Sel;
            if (sel.Count == 0) return;
            var files = new StringCollection();
            foreach (var x in sel) files.Add(x.Path);
            var data = new DataObject();
            data.SetFileDropList(files);
            // the same "preferred drop effect" Windows Explorer uses: pasting there moves or copies too
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            try { Clipboard.SetDataObject(data, true); status.Text = (cut ? "cut " : "copied ") + sel.Count + (sel.Count == 1 ? " item" : " items"); }
            catch (Exception ex) { status.Text = "! " + ex.Message; }
        }

        void Paste()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return;
                var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                bool move = Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4 && (new BinaryReader(ms).ReadInt32() & 2) != 0;
                var target = dir;
                Task.Run(() => FileOp(move ? FO_MOVE : FO_COPY, files, target, (ushort)(FOF_ALLOWUNDO | FOF_RENAMEONCOLLISION)))
                    .ContinueWith(_ => { if (move) Clipboard.Clear(); Load(); }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex) { status.Text = "! " + ex.Message; }
        }

        void Delete(bool permanent)
        {
            var sel = Sel;
            if (sel.Count == 0) return;
            // into the recycle bin (Windows asks only for a permanent delete)
            Task.Run(() => FileOp(FO_DELETE, sel.Select(x => x.Path).ToArray(), null, permanent ? (ushort)0 : (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION)))
                .ContinueWith(_ => Load(), TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Rename()
        {
            var sel = Sel;
            if (sel.Count != 1) return;
            Ask(L("neuer name: ", "new name: "), sel[0].Name, name =>
            {
                if (name == sel[0].Name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
                var target = Path.Combine(dir, name);
                try { if (sel[0].Dir) Directory.Move(sel[0].Path, target); else File.Move(sel[0].Path, target); pendingSelect = name; Load(name); }
                catch (Exception ex) { status.Text = "! " + ex.Message; }
            });
        }

        void NewFolder()
        {
            Ask(L("neuer ordner: ", "new folder: "), L("Neuer Ordner", "New folder"), name =>
            {
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
                try { Directory.CreateDirectory(Path.Combine(dir, name)); Load(name); }
                catch (Exception ex) { status.Text = "! " + ex.Message; }
            });
        }

        /// <summary>A one-line question in the status line (Enter = ok, Esc = cancel).</summary>
        void Ask(string question, string value, Action<string> done)
        {
            var host = (DockPanel)Content;
            var row = new DockPanel { Margin = new Thickness(10, 2, 10, 4) };
            var q = Text(question, G);
            DockPanel.SetDock(q, Dock.Left);
            row.Children.Add(q);
            var box = Input();
            box.Text = value;
            row.Children.Add(box);
            DockPanel.SetDock(row, Dock.Bottom);
            host.Children.Insert(host.Children.IndexOf(status) + 1, row);
            bool finished = false;
            void Finish(bool ok)
            {
                if (finished) return;
                finished = true;
                host.Children.Remove(row);
                list.Focus();
                if (ok && box.Text.Trim().Length > 0) done(box.Text.Trim());
            }
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Finish(true); e.Handled = true; } else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
            box.LostKeyboardFocus += (s, e) => Finish(false);
            box.Loaded += (s, e) =>
            {
                box.Focus();
                int dot = value.LastIndexOf('.');
                if (dot > 0 && !Directory.Exists(Path.Combine(dir, value))) box.Select(0, dot); else box.SelectAll();
            };
        }

        void Props()
        {
            var sel = Sel;
            var target = sel.Count == 1 ? sel[0].Path : dir;
            var info = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)), lpVerb = "properties", lpFile = target, nShow = 1, fMask = 0x0000000C };
            ShellExecuteEx(ref info);
        }

        // ---------------- Windows' own copy/move/delete (progress, conflicts, recycle bin, undo) ----------------
        const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3;
        const ushort FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_RENAMEONCOLLISION = 0x08;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHELLEXECUTEINFO
        {
            public int cbSize; public uint fMask; public IntPtr hwnd; public string lpVerb; public string lpFile; public string lpParameters; public string lpDirectory; public int nShow;
            public IntPtr hInstApp, lpIDList; public string lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        static void FileOp(uint func, string[] from, string to, ushort flags)
        {
            var op = new SHFILEOPSTRUCT { wFunc = func, pFrom = string.Join("\0", from) + "\0\0", pTo = to == null ? null : to + "\0\0", fFlags = flags };
            SHFileOperation(ref op);
        }
    }
}

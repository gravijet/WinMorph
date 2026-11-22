using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;
using Path = System.IO.Path;

namespace WinMorph.Shell
{
    /// <summary>
    /// macOS Spotlight (Tahoe): a glass search bar in the upper third of the screen with buttons for apps, files
    /// and actions on its right; the results in a second glass pane below, in sections (Top Hit, Applications,
    /// System Settings, Actions, Documents, Folders, the web). Files come from the Windows search index, or from
    /// a quick look through the user's folders when there is no index. Typing a sum shows the result.
    /// </summary>
    internal sealed class MacSpotlight : Window
    {
        enum Scope { All, Apps, Files, Actions }

        sealed class Hit
        {
            public string Title, Sub, Path, Key;
            public ImageSource Icon;
            public Func<double, FrameworkElement> Glyph;
            public Action Run, Reveal;
            public bool Big;
        }

        static MacSpotlight current;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;
        static readonly CultureInfo Ci = new CultureInfo(Core.Loc.De ? "de-DE" : "en-US");
        const double PaneWidth = 700;

        readonly TextBox box;
        readonly TextBlock hint, chipText, empty;
        readonly Border chip;
        readonly Grid chipIcon;
        readonly StackPanel cats, list;
        readonly WrapPanel grid;
        readonly Grid results;
        readonly ScrollViewer scroller;
        readonly TranslateTransform resultsSlide = new TranslateTransform();
        readonly List<(Border el, Hit hit)> rows = new List<(Border, Hit)>();
        readonly DispatcherTimer fileTimer;
        Scope scope = Scope.All;
        int sel = -1, version, gridCols = 7;
        string fileQuery;
        List<Hit> fileHits = new List<Hit>(), folderHits = new List<Hit>();
        bool closing;

        public static Window Current => current;

        public static void Toggle(string text = null)
        {
            if (current != null) { current.FadeClose(); return; }
            MacMenu.Current?.Dismiss(true);
            MacPanels.CloseAll();
            current = new MacSpotlight();
            current.Show();
            current.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(current).Handle);
            if (!string.IsNullOrEmpty(text)) { current.box.Text = text; current.box.CaretIndex = text.Length; }
        }

        public static void CloseNow() { try { current?.Close(); } catch { } }

        MacSpotlight()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.Height; Width = PaneWidth + 60;
            MacStyle.Prepare(this);
            MacStyle.ToolWindow(this);
            RenderGuard.Watch(this);

            // ---- the bar ----
            var bar = new DockPanel { Height = 52, Margin = new Thickness(18, 0, 10, 0), LastChildFill = true };
            var glass = MacStyle.Sym("magnifyingglass", 19, MacStyle.Secondary);
            glass.Margin = new Thickness(0, 0, 11, 0);
            DockPanel.SetDock(glass, Dock.Left);
            bar.Children.Add(glass);

            chipIcon = new Grid { Width = 15, Height = 15, Margin = new Thickness(0, 0, 5, 0) };
            chipText = MacStyle.Text("", 13, MacStyle.Label, FontWeights.Medium);
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            chipRow.Children.Add(chipIcon); chipRow.Children.Add(chipText);
            chip = new Border { Child = chipRow, CornerRadius = new CornerRadius(8), Background = B(0x000000, 18), Padding = new Thickness(8, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Cursor = Cursors.Arrow };
            chip.MouseLeftButtonUp += (s, e) => { e.Handled = true; SetScope(Scope.All); };
            DockPanel.SetDock(chip, Dock.Left);
            bar.Children.Add(chip);

            cats = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            cats.Children.Add(Category("app", L("Programme", "Applications"), Scope.Apps));
            cats.Children.Add(Category("doc.text", L("Dateien", "Files"), Scope.Files));
            cats.Children.Add(Category("bolt", L("Aktionen", "Actions"), Scope.Actions));
            DockPanel.SetDock(cats, Dock.Right);
            bar.Children.Add(cats);

            box = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = MacStyle.Font, FontSize = 21, Foreground = B(MacStyle.Label), CaretBrush = B(MacStyle.Blue), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0), SelectionBrush = B(MacStyle.Blue) };
            hint = MacStyle.Text(L("Spotlight-Suche", "Spotlight Search"), 21, C(0x3C3C43, 120));
            hint.IsHitTestVisible = false; hint.Margin = new Thickness(2, 0, 0, 0);
            var field = new Grid();
            field.Children.Add(hint); field.Children.Add(box);
            bar.Children.Add(field);
            var barPane = MacStyle.Glass(bar, 26, C(0xF6F6F8, 196), behind: true);

            // ---- the results ----
            list = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
            grid = new WrapPanel { Margin = new Thickness(8, 8, 8, 8), Visibility = Visibility.Collapsed };
            var holder = new StackPanel();
            holder.Children.Add(list); holder.Children.Add(grid);
            scroller = new ScrollViewer { Content = holder, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 452, Focusable = false };
            empty = MacStyle.Text("", 14, MacStyle.Secondary);
            empty.HorizontalAlignment = HorizontalAlignment.Center; empty.Margin = new Thickness(0, 26, 0, 28); empty.Visibility = Visibility.Collapsed;
            var body = new Grid();
            body.Children.Add(scroller); body.Children.Add(empty);
            results = MacStyle.Glass(body, 22, C(0xF6F6F8, 206), behind: true);
            results.Margin = new Thickness(0, 8, 0, 0);
            results.Visibility = Visibility.Collapsed;
            results.RenderTransform = resultsSlide;

            var stack = new StackPanel { Margin = new Thickness(30, 14, 30, 40) };
            stack.Children.Add(barPane); stack.Children.Add(results);
            Content = stack;

            fileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            fileTimer.Tick += (s, e) => { fileTimer.Stop(); FindFilesAsync(); };

            box.TextChanged += (s, e) => Search();
            Deactivated += (s, e) => FadeClose();
            PreviewKeyDown += OnKey;
            Closed += (s, e) => { closing = true; fileTimer.Stop(); if (current == this) current = null; };
            Loaded += (s, e) =>
            {
                var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
                double ml = mi.rcMonitor.Left / sc, mw = (mi.rcMonitor.Right - mi.rcMonitor.Left) / sc, mt = mi.rcMonitor.Top / sc, mh = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / sc;
                Left = ml + (mw - ActualWidth) / 2; Top = mt + mh * 0.2;
                MacStyle.Pop(barPane, new Point(0.5, 0.5), 0.96, 220);
                box.Focus();
            };
        }

        Border Category(string sym, string tip, Scope s)
        {
            var b = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Brushes.Transparent, Child = MacStyle.Sym(sym, 15, MacStyle.Secondary), Margin = new Thickness(2, 0, 0, 0), ToolTip = tip, Cursor = Cursors.Arrow };
            b.MouseEnter += (o, e) => b.Background = B(0x000000, 16);
            b.MouseLeave += (o, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (o, e) => { e.Handled = true; b.Background = B(0x000000, 30); };
            b.MouseLeftButtonUp += (o, e) => { e.Handled = true; b.Background = Brushes.Transparent; SetScope(s); };
            return b;
        }

        void SetScope(Scope s)
        {
            scope = s;
            if (s == Scope.All) chip.Visibility = Visibility.Collapsed;
            else
            {
                chipIcon.Children.Clear();
                chipIcon.Children.Add(MacStyle.Sym(s == Scope.Apps ? "app" : s == Scope.Files ? "doc.text" : "bolt", 14, MacStyle.Label));
                chipText.Text = s == Scope.Apps ? L("Programme", "Applications") : s == Scope.Files ? L("Dateien", "Files") : L("Aktionen", "Actions");
                chip.Visibility = Visibility.Visible;
                MacStyle.Pop(chip, new Point(0, 0.5), 0.9, 160);
            }
            hint.Text = s == Scope.All ? L("Spotlight-Suche", "Spotlight Search") : s == Scope.Apps ? L("Programme durchsuchen", "Search Applications") : s == Scope.Files ? L("Dateien durchsuchen", "Search Files") : L("Aktionen durchsuchen", "Search Actions");
            box.Focus();
            Search();
        }

        // ================= searching =================
        void Search()
        {
            version++;
            var text = box.Text.Trim();
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            cats.Visibility = box.Text.Length == 0 && scope == Scope.All ? Visibility.Visible : Visibility.Collapsed;
            if (scope == Scope.Apps) { ShowApps(text); return; }
            grid.Visibility = Visibility.Collapsed; list.Visibility = Visibility.Visible;

            // the file results of the last search stay while they still match, until the new ones arrive
            if (text.Length == 0) { fileHits.Clear(); folderHits.Clear(); fileQuery = null; }
            else
            {
                fileHits = fileHits.Where(h => Contains(h.Title, text)).ToList();
                folderHits = folderHits.Where(h => Contains(h.Title, text)).ToList();
            }
            Render(text, keep: null);
            if (text.Length > 0 && scope != Scope.Actions) { fileTimer.Stop(); fileTimer.Start(); }
        }

        void Render(string text, string keep)
        {
            var sections = new List<(string title, List<Hit> hits)>();
            if (text.Length == 0)
            {
                if (scope == Scope.Files) sections.Add((L("Zuletzt benutzt", "Recents"), Recents()));
                else if (scope == Scope.Actions) { sections.Add((L("Aktionen", "Actions"), Actions("").ToList())); sections.Add((L("Systemeinstellungen", "System Settings"), SettingsHits("").ToList())); }
            }
            else if (scope == Scope.Files)
            {
                sections.Add((L("Dokumente", "Documents"), fileHits.Take(24).ToList()));
                sections.Add((L("Ordner", "Folders"), folderHits.Take(10).ToList()));
            }
            else if (scope == Scope.Actions)
            {
                sections.Add((L("Aktionen", "Actions"), Actions(text).ToList()));
                sections.Add((L("Systemeinstellungen", "System Settings"), SettingsHits(text).ToList()));
            }
            else
            {
                var calc = SpotCalc.Eval(text);
                var apps = Intents.Match(text).Take(6).Select(AppHit).ToList();
                var acts = Actions(text).Take(4).ToList();
                var sets = SettingsHits(text).Take(3).ToList();
                var docs = fileHits.Take(6).ToList();
                var dirs = folderHits.Take(3).ToList();
                Hit top = null;
                if (calc != null) top = CalcHit(calc.Value);
                else if (apps.Count > 0 && Starts(apps[0].Title, text)) { top = apps[0]; apps.RemoveAt(0); }
                else if (acts.Count > 0 && Starts(acts[0].Title, text)) { top = acts[0]; acts.RemoveAt(0); }
                else if (sets.Count > 0 && Starts(sets[0].Title, text)) { top = sets[0]; sets.RemoveAt(0); }
                else if (apps.Count > 0) { top = apps[0]; apps.RemoveAt(0); }
                else if (docs.Count > 0 && Starts(docs[0].Title, text)) { top = docs[0]; docs.RemoveAt(0); }
                if (top != null && top.Sub == null) top.Sub = top.Key.StartsWith("app:") ? L("Programm", "Application") : top.Key.StartsWith("set:") ? L("Systemeinstellungen", "System Settings") : L("Aktion", "Action");
                if (top != null) { top.Big = true; sections.Add((L("Toptreffer", "Top Hit"), new List<Hit> { top })); }
                sections.Add((L("Programme", "Applications"), apps));
                sections.Add((L("Systemeinstellungen", "System Settings"), sets));
                sections.Add((L("Aktionen", "Actions"), acts));
                sections.Add((L("Dokumente", "Documents"), docs));
                sections.Add((L("Ordner", "Folders"), dirs));
                sections.Add((L("Im Web suchen", "Search the Web"), new List<Hit> { WebHit(text) }));
            }

            list.Children.Clear(); rows.Clear();
            foreach (var (title, hits) in sections)
            {
                if (hits.Count == 0) continue;
                var head = MacStyle.Text(title, 11.5, MacStyle.Secondary, FontWeights.SemiBold);
                head.Margin = new Thickness(10, list.Children.Count == 0 ? 6 : 12, 0, 3);
                list.Children.Add(head);
                foreach (var h in hits) { var r = Row(h); rows.Add((r, h)); list.Children.Add(r); }
            }
            bool searching = scope == Scope.Files && text.Length > 0 && fileQuery != text;
            empty.Text = searching ? L("Suche …", "Searching…") : L("Keine Ergebnisse", "No Results");
            empty.Visibility = rows.Count == 0 && (text.Length > 0 || scope != Scope.All) ? Visibility.Visible : Visibility.Collapsed;
            ShowResults(text.Length > 0 || scope != Scope.All);
            int at = keep == null ? -1 : rows.FindIndex(r => r.hit.Key == keep);
            Select(at >= 0 ? at : 0, scroll: at < 0);
            if (at < 0) scroller.ScrollToTop();
        }

        void ShowResults(bool on)
        {
            bool was = results.Visibility == Visibility.Visible;
            results.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on && !was)
            {
                results.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
                resultsSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = MacStyle.OutQuint });
            }
        }

        Border Row(Hit h)
        {
            double ic = h.Big ? 34 : 24;
            var d = new DockPanel { Height = h.Big ? 46 : 32 };
            FrameworkElement icon;
            if (h.Icon != null) icon = Img(h.Icon, ic);
            else if (h.Glyph != null) icon = h.Glyph(ic);
            else icon = new Border();
            icon.Width = ic; icon.Height = ic; icon.Margin = new Thickness(0, 0, 10, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            d.Children.Add(icon);
            if (h.Path != null && IsPicture(h.Path) && icon is Image im) Thumb(h.Path, im);
            var sub = MacStyle.Text(h.Sub ?? "", 12, MacStyle.Secondary);
            sub.Margin = new Thickness(14, 0, 0, 0); sub.MaxWidth = 320;
            DockPanel.SetDock(sub, Dock.Right);
            d.Children.Add(sub);
            var title = MacStyle.Text(h.Title, h.Big ? 15 : 13.5, MacStyle.Label, h.Big ? FontWeights.SemiBold : (FontWeight?)null);
            d.Children.Add(title);
            var row = new Border { Child = d, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 10, 0), Background = Brushes.Transparent, Tag = new[] { title, sub } };
            int index = rows.Count;
            row.MouseLeftButtonUp += (s, e) => { e.Handled = true; Run(index, reveal: Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); };
            return row;
        }

        void Select(int i, bool scroll = true)
        {
            if (rows.Count == 0) { sel = -1; return; }
            sel = Math.Max(0, Math.Min(rows.Count - 1, i));
            bool inGrid = scope == Scope.Apps;
            for (int k = 0; k < rows.Count; k++)
            {
                bool on = k == sel;
                var el = rows[k].el;
                if (inGrid) { el.Background = on ? B(0x000000, 26) : Brushes.Transparent; continue; }
                el.Background = on ? B(MacStyle.Blue) : Brushes.Transparent;
                if (el.Tag is TextBlock[] t) { t[0].Foreground = B(on ? Colors.White : MacStyle.Label); t[1].Foreground = B(on ? C(0xFFFFFF, 215) : MacStyle.Secondary); }
            }
            if (scroll) rows[sel].el.BringIntoView();
        }

        void Run(int i, bool reveal = false)
        {
            if (i < 0 || i >= rows.Count) return;
            var h = rows[i].hit;
            var a = reveal && h.Reveal != null ? h.Reveal : h.Run;
            Close();
            try { a?.Invoke(); } catch { }
        }

        void OnKey(object s, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool inGrid = scope == Scope.Apps;
            switch (e.Key)
            {
                case Key.Escape:
                    if (box.Text.Length > 0) box.Text = "";
                    else if (scope != Scope.All) SetScope(Scope.All);
                    else FadeClose();
                    e.Handled = true; break;
                case Key.Down: Select(sel + (inGrid ? gridCols : 1)); e.Handled = true; break;
                case Key.Up: Select(sel - (inGrid ? gridCols : 1)); e.Handled = true; break;
                case Key.Right when inGrid: Select(sel + 1); e.Handled = true; break;
                case Key.Left when inGrid: Select(sel - 1); e.Handled = true; break;
                case Key.Enter: Run(sel, reveal: ctrl); e.Handled = true; break;
                case Key.Back when box.Text.Length == 0 && scope != Scope.All: SetScope(Scope.All); e.Handled = true; break;
                case Key.D1 when ctrl: SetScope(Scope.Apps); e.Handled = true; break;
                case Key.D2 when ctrl: SetScope(Scope.Files); e.Handled = true; break;
                case Key.D3 when ctrl: SetScope(Scope.Actions); e.Handled = true; break;
            }
        }

        void FadeClose()
        {
            if (closing) return;
            closing = true;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            a.Completed += (s, e) => { try { Close(); } catch { } };
            ((UIElement)Content).BeginAnimation(OpacityProperty, a);
        }

        // ================= the apps view =================
        void ShowApps(string f)
        {
            list.Visibility = Visibility.Collapsed; grid.Visibility = Visibility.Visible;
            grid.Children.Clear(); rows.Clear();
            var apps = (f.Length == 0 ? AppCatalog.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase) : Intents.Match(f)).ToList();
            const double cellW = 97;
            gridCols = (int)((PaneWidth - 16) / cellW);
            foreach (var a in apps)
            {
                var app = a;
                var sp = new StackPanel { Width = cellW - 8 };
                var img = Img(MacStyle.Tile(app.Icon), 58);
                img.HorizontalAlignment = HorizontalAlignment.Center;
                sp.Children.Add(img);
                var name = MacStyle.Text(app.Name, 11.5, MacStyle.Label);
                name.TextWrapping = TextWrapping.Wrap; name.TextAlignment = TextAlignment.Center; name.MaxHeight = 30; name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(name);
                var cell = new Border { Child = sp, Width = cellW - 1, Height = 104, Padding = new Thickness(4, 6, 4, 4), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent };
                var h = AppHit(app);
                int index = rows.Count;
                cell.MouseEnter += (s, e) => { if (index != sel) cell.Background = B(0x000000, 12); };
                cell.MouseLeave += (s, e) => { if (index != sel) cell.Background = Brushes.Transparent; };
                cell.MouseLeftButtonUp += (s, e) => { e.Handled = true; Run(index); };
                rows.Add((cell, h));
                grid.Children.Add(cell);
            }
            empty.Text = L("Keine Ergebnisse", "No Results");
            empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowResults(true);
            Select(f.Length > 0 ? 0 : -1, scroll: false);
            if (f.Length == 0) { sel = -1; foreach (var r in rows) r.el.Background = Brushes.Transparent; }
            scroller.ScrollToTop();
        }

        // ================= kinds of results =================
        static bool Contains(string s, string f) => s != null && s.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0;
        static bool Starts(string s, string f) => s != null && (s.StartsWith(f, StringComparison.CurrentCultureIgnoreCase) || s.IndexOf(" " + f, StringComparison.CurrentCultureIgnoreCase) >= 0);

        static Hit AppHit(AppEntry a)
        {
            string path = a.ExePath ?? (a.Launch != null && File.Exists(a.Launch) ? a.Launch : null);
            return new Hit
            {
                Title = a.Name, Sub = null, Icon = MacStyle.Tile(a.Icon), Key = "app:" + a.Name, Run = a.Start,
                Reveal = path != null ? (Action)(() => MacFiles.Open(Path.GetDirectoryName(path))) : null,
            };
        }

        static Hit CalcHit(double v)
        {
            string shown = SpotCalc.Format(v);
            return new Hit
            {
                Title = "= " + shown, Sub = L("Rechner", "Calculator"), Key = "calc",
                Glyph = size => Badge("equal", MacStyle.Orange, size), Run = () => { try { Clipboard.SetText(shown); } catch { } },
            };
        }

        static Hit WebHit(string text) => new Hit
        {
            Title = L("„" + text + "“ im Web suchen", "Search the web for “" + text + "”"), Sub = "Web", Key = "web",
            Glyph = size => Badge("globe", MacStyle.Blue, size), Run = () => ShellActions.WebSearch(text),
        };

        /// <summary>A coloured rounded square with a white symbol, like the action icons of macOS.</summary>
        static FrameworkElement Badge(string sym, Color c, double size)
        {
            var g = new Grid { Width = size, Height = size };
            var light = Color.FromRgb((byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40));
            g.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.25), Background = new LinearGradientBrush(light, c, 90) });
            g.Children.Add(MacStyle.Sym(sym, size * 0.58, Colors.White));
            return g;
        }

        static readonly ShellTheme Mac = ShellThemes.For("macos");

        static void Confirm(string title, string action, Action run)
            => MacAlert.Ask(title, null, MacStyle.Own("finder"), new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (action, run, true) });

        static IEnumerable<Hit> Actions(string q)
        {
            var all = new (string title, string sym, Color color, Action run, string words)[]
            {
                (L("Neues Finder-Fenster", "New Finder Window"), "folder", MacStyle.Blue, () => MacFiles.OpenNew(null), "finder fenster dateien explorer ordner files"),
                (L("Bildschirm sperren", "Lock Screen"), "lock", MacStyle.Indigo, ShellActions.Lock, "sperren lock"),
                (L("Ruhezustand", "Sleep"), "moon", MacStyle.Indigo, ShellActions.Sleep, "schlaf sleep energie"),
                (L("Neustart …", "Restart…"), "power", MacStyle.Orange, () => Confirm(L("Möchtest du deinen Computer jetzt neu starten?", "Are you sure you want to restart your computer now?"), L("Neustart", "Restart"), ShellActions.RestartNow), "neu starten restart reboot"),
                (L("Ausschalten …", "Shut Down…"), "power", MacStyle.Red, () => Confirm(L("Möchtest du deinen Computer jetzt ausschalten?", "Are you sure you want to shut down your computer now?"), L("Ausschalten", "Shut Down"), ShellActions.ShutdownNow), "herunterfahren shutdown"),
                (L("Abmelden …", "Log Out…"), "person", MacStyle.Teal, () => Confirm(L("Möchtest du dich wirklich abmelden?", "Are you sure you want to quit all apps and log out now?"), L("Abmelden", "Log Out"), () => ShellActions.Run("shutdown.exe", "/l")), "logout sign out"),
                (L("Bildschirmfoto", "Screenshot"), "camera", MacStyle.Gray, ShellActions.Screenshot, "screenshot foto aufnahme"),
                (L("Schreibtisch anzeigen", "Show Desktop"), "desktop", MacStyle.Teal, ShellActions.ShowDesktop, "desktop"),
                (L("Aktivitätsanzeige", "Activity Monitor"), "gauge", MacStyle.Green, ShellActions.TaskManager, "task manager prozesse activity cpu speicher"),
                (L("Kontrollzentrum", "Control Center"), "controlcenter", MacStyle.Blue, () => MacPanels.Toggle(Mac, "cc", new Rect(0, 0, 0, 0), null), "control center wlan ton helligkeit"),
                (L("Mitteilungszentrale", "Notification Center"), "bell", MacStyle.Red, () => MacPanels.Toggle(Mac, "nc", new Rect(0, 0, 0, 0), null), "mitteilungen notification kalender"),
                (L("Papierkorb entleeren …", "Empty Trash…"), "trash", MacStyle.Gray, MacFiles.EmptyTrash, "papierkorb trash leeren"),
                (L("Papierkorb öffnen", "Open Trash"), "trash", MacStyle.Gray, MacFiles.OpenTrash, "papierkorb trash"),
                (L("Ton aus/ein", "Mute/Unmute"), "speaker", MacStyle.Pink, () => ShellActions.Volume(0), "stumm mute lautstärke"),
                (L("Über diesen PC", "About This PC"), "info.circle", MacStyle.Gray, MacApps.About, "info computer system version"),
                (L("Sofort beenden …", "Force Quit…"), "xmark.circle", MacStyle.Red, MacApps.ForceQuit, "beenden force quit hängt"),
                ("WinMorph", "app", MacStyle.Mint, ShellActions.WinMorphApp, "look design wiederherstellen restore winmorph"),
            };
            var f = q.Trim();
            return all.Where(a => f.Length == 0 || Starts(a.title, f) || a.words.Split(' ').Any(w => w.StartsWith(f, StringComparison.CurrentCultureIgnoreCase)) || (f.Length >= 3 && Contains(a.title, f)))
                .OrderBy(a => f.Length > 0 && Starts(a.title, f) ? 0 : 1)
                .Select(a => { var x = a; return new Hit { Title = x.title, Sub = null, Key = "act:" + x.title, Glyph = size => Badge(x.sym, x.color, size), Run = x.run }; });
        }

        static IEnumerable<Hit> SettingsHits(string q)
        {
            var all = new (string title, string uri, string words)[]
            {
                (L("WLAN", "Wi‑Fi"), "ms-settings:network-wifi", "wifi wlan netzwerk internet"),
                ("Bluetooth", "ms-settings:bluetooth", "bluetooth geräte kopfhörer"),
                (L("Netzwerk", "Network"), "ms-settings:network-status", "netzwerk ethernet vpn internet"),
                (L("Displays", "Displays"), "ms-settings:display", "bildschirm monitor auflösung helligkeit display anzeige"),
                (L("Ton", "Sound"), "ms-settings:sound", "ton audio lautsprecher mikrofon sound"),
                (L("Hintergrundbild", "Wallpaper"), "ms-settings:personalization-background", "hintergrund wallpaper bild"),
                (L("Mitteilungen", "Notifications"), "ms-settings:notifications", "mitteilungen benachrichtigungen notifications"),
                (L("Fokus", "Focus"), "ms-settings:quiethours", "fokus nicht stören focus"),
                (L("Batterie", "Battery"), "ms-settings:batterysaver", "batterie akku energie battery power"),
                (L("Energie & Ruhezustand", "Energy"), "ms-settings:powersleep", "energie ruhezustand sleep power"),
                (L("Datum & Uhrzeit", "Date & Time"), "ms-settings:dateandtime", "datum uhrzeit zeit date time"),
                (L("Sprache & Region", "Language & Region"), "ms-settings:regionlanguage", "sprache region language"),
                (L("Tastatur", "Keyboard"), "ms-settings:keyboard", "tastatur keyboard eingabe"),
                (L("Maus", "Mouse"), "ms-settings:mousetouchpad", "maus trackpad touchpad mouse"),
                (L("Drucker & Scanner", "Printers & Scanners"), "ms-settings:printers", "drucker scanner printer"),
                (L("Programme", "Apps"), "ms-settings:appsfeatures", "programme apps deinstallieren uninstall"),
                (L("Speicher", "Storage"), "ms-settings:storagesense", "speicher festplatte storage"),
                (L("Softwareupdate", "Software Update"), "ms-settings:windowsupdate", "update aktualisierung software"),
                (L("Datenschutz & Sicherheit", "Privacy & Security"), "ms-settings:privacy", "datenschutz sicherheit privacy security"),
                (L("Benutzer:innen & Gruppen", "Users & Groups"), "ms-settings:otherusers", "benutzer konto users account"),
                (L("Anmeldeoptionen", "Login Options"), "ms-settings:signinoptions", "passwort anmelden hello pin login"),
                (L("Bedienungshilfen", "Accessibility"), "ms-settings:easeofaccess", "bedienungshilfen accessibility"),
            };
            var f = q.Trim();
            var settingsIcon = MacStyle.Own("settings");
            return all.Where(a => f.Length == 0 || Starts(a.title, f) || a.words.Split(' ').Any(w => w.StartsWith(f, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(a => f.Length > 0 && Starts(a.title, f) ? 0 : 1)
                .Select(a => { var x = a; return new Hit { Title = x.title, Sub = null, Key = "set:" + x.uri, Icon = settingsIcon, Run = () => ShellActions.Run(x.uri) }; });
        }

        static List<Hit> Recents()
        {
            var hits = new List<Hit>();
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(x => x.LastWriteTime).Take(14))
                {
                    var path = f.FullName;
                    hits.Add(new Hit { Title = Path.GetFileNameWithoutExtension(f.Name), Sub = When(f.LastWriteTime), Key = "rec:" + path, Icon = W32.FileIcon(path), Run = () => ShellActions.Run(path) });
                }
            }
            catch { }
            return hits;
        }

        static string When(DateTime t) => t.Date == DateTime.Today ? L("Heute, ", "Today, ") + t.ToString("t", Ci) : t.Date == DateTime.Today.AddDays(-1) ? L("Gestern, ", "Yesterday, ") + t.ToString("t", Ci) : t.ToString("d", Ci);

        static Hit FileHit(string path, bool dir)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string parent = Path.GetDirectoryName(path) ?? "";
            string where = parent.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? parent.Substring(home.Length).Trim('\\') : parent;
            var parts = where.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(LocalName).ToList();
            where = parts.Count == 0 ? L("Benutzerordner", "Home") : string.Join(" › ", parts.Skip(Math.Max(0, parts.Count - 2)));
            return new Hit
            {
                Title = Path.GetFileName(path), Sub = where, Path = path, Key = "file:" + path,
                Icon = dir ? MacStyle.FolderIcon : W32.FileIcon(path), Glyph = size => MacStyle.Sym("doc", size, MacStyle.Secondary),
                Run = () => { if (dir) MacFiles.Open(path); else ShellActions.Run(path); },
                Reveal = () => MacFiles.Open(dir ? path : parent),
            };
        }

        static string LocalName(string part)
        {
            switch (part.ToLowerInvariant())
            {
                case "documents": return L("Dokumente", "Documents");
                case "desktop": return L("Schreibtisch", "Desktop");
                case "pictures": return L("Bilder", "Pictures");
                case "music": return L("Musik", "Music");
                case "videos": return L("Filme", "Movies");
                default: return part;
            }
        }

        static bool IsPicture(string p)
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".gif" || e == ".bmp" || e == ".webp" || e == ".heic";
        }

        /// <summary>Pictures show a small preview instead of the file type icon, loaded after the row is on screen.</summary>
        void Thumb(string path, Image img)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closing) return;
                var t = W32.ShellItemIcon(path, 64);
                if (t != null) img.Source = t;
            }), DispatcherPriority.Background);
        }

        // ================= files =================
        void FindFilesAsync()
        {
            var q = box.Text.Trim();
            if (q.Length == 0 || closing) return;
            int ver = version;
            int max = scope == Scope.Files ? 40 : 14;
            Task.Run(() => FindFiles(q, max)).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closing || ver != version || scope == Scope.Apps) return;
                    fileQuery = q;
                    fileHits = t.Result.Where(x => !x.dir).Select(x => FileHit(x.path, false)).ToList();
                    folderHits = t.Result.Where(x => x.dir).Select(x => FileHit(x.path, true)).ToList();
                    string keep = sel >= 0 && sel < rows.Count ? rows[sel].hit.Key : null;
                    Render(q, keep);
                }));
            });
        }

        static List<(string path, bool dir)> FindFiles(string q, int max)
        {
            List<(string path, bool dir)> found;
            try { found = IndexSearch(q, max); }
            catch { found = ScanSearch(q, max); }
            // names that start with what was typed come first
            return found.OrderBy(x => Path.GetFileName(x.path).StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ToList();
        }

        /// <summary>The Windows search index: names containing the text, in the user's folders, newest first.</summary>
        static List<(string path, bool dir)> IndexSearch(string q, int max)
        {
            var term = new string(q.Where(ch => ch != '\'' && ch != '%' && ch != '_' && ch != '[' && ch != ']').ToArray());
            var list = new List<(string, bool)>();
            if (term.Length == 0) return list;
            // the folders in the user's home; AppData and hidden ones are left out (searching them takes seconds)
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var scopes = new DirectoryInfo(home).GetDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0 && !d.Name.StartsWith(".") && !d.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                .Select(d => "SCOPE='file:" + d.FullName.Replace('\\', '/').Replace("'", "''") + "'").ToList();
            if (scopes.Count == 0) return list;
            string sql = "SELECT TOP " + (max * 2) + " System.ItemUrl, System.ItemType FROM SystemIndex WHERE (" + string.Join(" OR ", scopes) + ") AND System.ItemNameDisplay LIKE '%" + term + "%' ORDER BY System.DateModified DESC";
            using (var c = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
            {
                c.Open();
                using (var cmd = new OleDbCommand(sql, c))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read() && list.Count < max)
                    {
                        // ItemUrl holds the real path (ItemPathDisplay is translated, "C:\Benutzer\...")
                        var u = r[0] as string;
                        if (string.IsNullOrEmpty(u) || !u.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                        var p = u.Substring(5).Replace('/', '\\');
                        if (string.IsNullOrEmpty(p) || p.IndexOf("\\AppData\\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (p.IndexOf("\\.", StringComparison.Ordinal) >= 0 || p.IndexOf("\\node_modules\\", StringComparison.OrdinalIgnoreCase) >= 0) continue;   // hidden folders and packages
                        bool dir = string.Equals(r[1] as string, "Directory", StringComparison.OrdinalIgnoreCase) || Directory.Exists(p);
                        list.Add((p, dir));
                    }
                }
            }
            return list;
        }

        static List<(string path, bool dir)> scanned;
        static readonly object scanLock = new object();

        /// <summary>Without the index: the user's folders once, a few levels deep, then matched by name.</summary>
        static List<(string path, bool dir)> ScanSearch(string q, int max)
        {
            lock (scanLock)
            {
                if (scanned == null)
                {
                    scanned = new List<(string, bool)>();
                    var roots = new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos }
                        .Select(Environment.GetFolderPath).ToList();
                    roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                    foreach (var r in roots.Where(Directory.Exists)) Walk(r, 0);
                }
                return scanned.Where(x => Path.GetFileName(x.path).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).Take(max).ToList();
            }
        }

        static void Walk(string dir, int depth)
        {
            if (scanned.Count > 20000) return;
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if ((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || e.Name.StartsWith(".")) continue;
                    bool isDir = (e.Attributes & FileAttributes.Directory) != 0;
                    scanned.Add((e.FullName, isDir));
                    if (isDir && depth < 4 && (e.Attributes & FileAttributes.ReparsePoint) == 0 && e.Name != "node_modules") Walk(e.FullName, depth + 1);
                }
            }
            catch { }
        }
    }

    /// <summary>Spotlight's calculator: + − × ÷ ^ and brackets, decimal comma or point.</summary>
    internal static class SpotCalc
    {
        public static double? Eval(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Replace(" ", "").Replace('×', '*').Replace('÷', '/').Replace(':', '/').Replace('−', '-').Replace(',', '.');
            if (s.Length > 120 || !s.Any(char.IsDigit) || s.Any(ch => !char.IsDigit(ch) && "+-*/^().".IndexOf(ch) < 0)) return null;
            if (!s.Skip(1).Any(ch => "+-*/^".IndexOf(ch) >= 0)) return null;   // a lone number is no sum
            int i = 0;
            try
            {
                double v = Expr(s, ref i);
                if (i != s.Length || double.IsNaN(v) || double.IsInfinity(v)) return null;
                return v;
            }
            catch { return null; }
        }

        public static string Format(double v) => Math.Round(v, 10).ToString("#,##0.##########", new CultureInfo(Core.Loc.De ? "de-DE" : "en-US"));

        static double Expr(string s, ref int i)
        {
            double v = Term(s, ref i);
            while (i < s.Length && (s[i] == '+' || s[i] == '-')) { char op = s[i++]; double r = Term(s, ref i); v = op == '+' ? v + r : v - r; }
            return v;
        }

        static double Term(string s, ref int i)
        {
            double v = Power(s, ref i);
            while (i < s.Length && (s[i] == '*' || s[i] == '/')) { char op = s[i++]; double r = Power(s, ref i); v = op == '*' ? v * r : v / r; }
            return v;
        }

        static double Power(string s, ref int i)
        {
            double b = Unary(s, ref i);
            if (i < s.Length && s[i] == '^') { i++; return Math.Pow(b, Power(s, ref i)); }
            return b;
        }

        static double Unary(string s, ref int i)
        {
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) { char op = s[i++]; double v = Unary(s, ref i); return op == '-' ? -v : v; }
            return Atom(s, ref i);
        }

        static double Atom(string s, ref int i)
        {
            if (i < s.Length && s[i] == '(')
            {
                i++;
                double v = Expr(s, ref i);
                if (i >= s.Length || s[i] != ')') throw new FormatException();
                i++;
                return v;
            }
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (st == i) throw new FormatException();
            return double.Parse(s.Substring(st, i - st), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}

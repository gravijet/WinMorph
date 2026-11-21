using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    /// <summary>
    /// The macOS look's bars: the menu bar on top (Apple menu, the front app's name and menus, status items,
    /// date and time) and the Dock at the bottom (Finder, Launchpad, the apps, System Settings, Downloads,
    /// Trash) with running dots, name labels, the launch bounce and the Dock menus.
    /// </summary>
    public sealed partial class ShellHost
    {
        bool Mac => t.Layout == BarLayout.Mac;
        Color menuFg = C(0x1D1D1F);
        WinInfo frontWin;                 // the window whose app owns the menu bar
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        static string L(string de, string en) => De ? de : en;

        void MacBars()
        {
            menuFg = MacStyle.LightTop ? C(0x1D1D1F) : Colors.White;
            // the menu bar is see-through like on Tahoe; alpha 1 keeps the strip clickable
            NewBar(Edge.Top, MacStyle.MenuBarHeight, false, MenuBar(), B(0xFFFFFF, 1));
            double reserve = MacStyle.DockIcon + 12 + 8;
            startBar = NewBar(Edge.Bottom, reserve, false, MacDock(), Brushes.Transparent);
        }

        // ================= menu bar =================
        Action appleMenu;   // the Apple menu of the main monitor (Win+X)

        FrameworkElement MenuBar()
        {
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 6, 0), Height = MacStyle.MenuBarHeight };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            dock.Children.Add(left);
            var shade = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = menuFg == Colors.White ? 0.35 : 0.0, Color = Colors.Black };
            if (shade.Opacity > 0) { left.Effect = shade; right.Effect = shade.Clone(); }
            var openers = new List<Action>();   // this monitor's menu titles, for the arrow keys

            // the Apple menu (the mark itself is only drawn by Apple's own font)
            FrameworkElement mark = MacStyle.HasAppleMark
                ? (FrameworkElement)new TextBlock { Text = "", FontFamily = MacStyle.Font, FontSize = 15, Foreground = B(menuFg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) }
                : Logo4(13, menuFg, menuFg, menuFg, menuFg);
            left.Children.Add(MenuTitle(mark, "bar:apple", AppleMenu, openers, 11));
            if (building == 0) appleMenu = openers[0];

            var appMenus = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(appMenus);
            string shown = null;
            void Rebuild()
            {
                var w = FrontApp();
                string name = w == null ? "Finder" : AppTitle(w);
                frontWin = w;
                if (name == shown) return;
                shown = name;
                appMenus.Children.Clear();
                while (openers.Count > 1) openers.RemoveAt(openers.Count - 1);
                var title = MacStyle.Text(name, 13, menuFg, FontWeights.Bold);
                appMenus.Children.Add(MenuTitle(title, "bar:app", () => AppMenu(name), openers));
                bool finder = name == "Finder";
                foreach (var (text, key) in new[] { (L("Ablage", "File"), "file"), (L("Bearbeiten", "Edit"), "edit"), (L("Darstellung", "View"), "view"), (finder ? L("Gehe zu", "Go") : null, "go"), (L("Fenster", "Window"), "window"), (L("Hilfe", "Help"), "help") })
                {
                    if (text == null) continue;
                    var k = key;
                    appMenus.Children.Add(MenuTitle(MacStyle.Text(text, 13, menuFg), "bar:" + k, () => AppMenus(k, name), openers));
                }
            }
            refreshers.Add(Rebuild);

            // status items: battery, Wi-Fi, Spotlight, Control Center, then date and time
            Border Status(FrameworkElement content, string kind, Action click = null)
            {
                Border b = null;
                b = new Border { Child = content, Padding = new Thickness(7, 0, 7, 0), Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 0) };
                b.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (click != null) { click(); return; }
                    var bar = Window.GetWindow(b) as BarWindow;
                    var r = bar != null ? bar.ScreenRectOf(b) : new Rect(0, 0, 0, 0);
                    if (MacPanels.Toggle(t, kind, r, () => b.Background = Brushes.Transparent)) b.Background = B(Highlight);
                };
                right.Children.Add(b);
                return b;
            }
            if (SysStats.Battery(out _, out _))
            {
                var batHost = new Grid();
                Status(batHost, "bat");
                clockUpdaters.Add(() =>
                {
                    if (tickMenu % 5 != 0 && batHost.Children.Count > 0) return;
                    var bs = Hardware.Battery();
                    if (bs == null) return;
                    batHost.Children.Clear();
                    var g = new Grid { Width = 27, Height = 22 };
                    g.Children.Add(MacStyle.Sym("battery", 27, menuFg, bs.Percent));
                    if (bs.Charging || bs.Plugged) { var bolt = MacStyle.Sym("bolt", 10, menuFg == Colors.White ? C(0x1D1D1F) : Colors.White); bolt.Margin = new Thickness(0, 0, 2, 0); g.Children.Add(bolt); }
                    batHost.Children.Add(g);
                });
            }
            var wifiHost = new Grid { Width = 17 };
            Status(wifiHost, "net");
            clockUpdaters.Add(() =>
            {
                if (tickMenu % 3 != 0 && wifiHost.Children.Count > 0) return;
                wifiHost.Children.Clear();
                var cur = Wlan.Current;
                if (cur != null) wifiHost.Children.Add(MacStyle.Sym("wifi", 17, menuFg, Math.Min(2, cur.Signal / 34)));
                else if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) wifiHost.Children.Add(MacStyle.Sym("wifi", 17, menuFg, 2));
                else wifiHost.Children.Add(MacStyle.Sym("wifi.slash", 17, menuFg));
            });
            Status(MacStyle.Sym("magnifyingglass", 14, menuFg), "spotlight", () => MacSpotlight.Toggle());
            Status(MacStyle.Sym("controlcenter", 17, menuFg), "cc");
            var clockText = MacStyle.Text("", 13, menuFg);
            Status(clockText, "nc").Padding = new Thickness(8, 0, 6, 0);
            clockUpdaters.Add(() => { tickMenu++; clockText.Text = MacStyle.MenuClock(DateTime.Now); });
            return dock;
        }

        int tickMenu;
        Color Highlight => menuFg == Colors.White ? C(0xFFFFFF, 60) : C(0x000000, 28);

        Border MenuTitle(FrameworkElement content, string key, Func<List<Item>> items, List<Action> menuOpeners, double padX = 9)
        {
            var b = new Border { Child = content, Padding = new Thickness(padX, 0, padX, 0), Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            int index = menuOpeners.Count;
            void Open()
            {
                if (MacMenu.Current?.MenuKey == key) { MacMenu.Current.Dismiss(false); return; }
                if (MacMenu.ClosedKey == key && (DateTime.Now - MacMenu.ClosedAt).TotalMilliseconds < 250) return;
                MacPanels.CloseAll();
                var bar = Window.GetWindow(b) as BarWindow;
                if (bar == null) return;
                var r = bar.ScreenRectOf(b);
                var m = MacMenu.Show(items(), new Point(r.Left + 6, r.Bottom + 1), key);
                b.Background = B(Highlight);
                m.Closing2 += () => b.Background = Brushes.Transparent;
                m.Neighbour = d => { int j = ((index + d) % menuOpeners.Count + menuOpeners.Count) % menuOpeners.Count; menuOpeners[j](); };
            }
            menuOpeners.Add(Open);
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; Open(); };
            // with one menu open, moving over the other titles opens theirs (as on the Mac)
            b.MouseEnter += (s, e) => { var cur = MacMenu.Current; if (cur != null && cur.MenuKey != null && cur.MenuKey.StartsWith("bar:") && cur.MenuKey != key) Open(); };
            return b;
        }

        /// <summary>The app window in front; our own bars and menus keep the previous one.</summary>
        WinInfo FrontApp()
        {
            var fg = W32.GetForegroundWindow();
            W32.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == ownPid && !WindowTracker.OwnApps.Contains(fg)) return frontWin != null && W32.IsWindow(frontWin.Hwnd) ? frontWin : null;
            return tracker.Windows.FirstOrDefault(x => x.Hwnd == fg);
        }

        static string AppTitle(WinInfo w) => MacApps.AppName(w);

        static bool IsExplorer(WinInfo w) => w.ExePath != null && Path.GetFileName(w.ExePath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);

        List<WinInfo> WindowsOf(WinInfo w) => w == null ? new List<WinInfo>() : tracker.Windows.Where(x => x.Key == w.Key).ToList();

        /// <summary>Brings the app window back to the front and sends it a key combination.</summary>
        void SendToApp(params byte[] keys)
        {
            var w = frontWin;
            if (w == null || !W32.IsWindow(w.Hwnd)) return;
            W32.Activate(w.Hwnd);
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            tm.Tick += (s, e) => { tm.Stop(); W32.SendKeys(keys); };
            tm.Start();
        }

        const byte CTRL = 0x11, SHIFT = 0x10, WIN = 0x5B;

        List<Item> AppleMenu()
        {
            return new List<Item>
            {
                new Item { Text = L("Über diesen PC", "About This PC"), Run = MacApps.About },
                Item.Sep,
                new Item { Text = L("Systemeinstellungen …", "System Settings…"), Run = ShellActions.Settings },
                new Item { Text = "App Store …", Run = () => ShellActions.Run("ms-windows-store:") },
                Item.Sep,
                new Item { Text = L("Sofort beenden …", "Force Quit…"), Run = MacApps.ForceQuit },
                Item.Sep,
                new Item { Text = L("Ruhezustand", "Sleep"), Run = ShellActions.Sleep },
                new Item { Text = L("Neustart …", "Restart…"), Run = () => Confirm(L("Möchtest du deinen Computer jetzt neu starten?", "Are you sure you want to restart your computer now?"), L("Neustart", "Restart"), ShellActions.RestartNow) },
                new Item { Text = L("Ausschalten …", "Shut Down…"), Run = () => Confirm(L("Möchtest du deinen Computer jetzt ausschalten?", "Are you sure you want to shut down your computer now?"), L("Ausschalten", "Shut Down"), ShellActions.ShutdownNow) },
                Item.Sep,
                new Item { Text = L("Bildschirm sperren", "Lock Screen"), Run = ShellActions.Lock },
                new Item { Text = L("„" + ShellActions.UserName + "“ abmelden …", "Log Out " + ShellActions.UserName + "…"), Run = () => Confirm(L("Möchtest du dich wirklich abmelden?", "Are you sure you want to quit all apps and log out now?"), L("Abmelden", "Log Out"), () => ShellActions.Run("shutdown.exe", "/l")) },
            };
        }

        static void Confirm(string title, string action, Action run)
            => MacAlert.Ask(title, null, MacStyle.Own("finder"), new[] { (L("Abbrechen", "Cancel"), (Action)null, false), (action, run, true) });

        List<Item> AppMenu(string name)
        {
            var w = frontWin;
            var items = new List<Item>
            {
                new Item { Text = L("Über ", "About ") + name, Run = () => AboutApp(w, name) },
                Item.Sep,
                new Item { Text = name + L(" ausblenden", " Hide"), Shortcut = "⌃H", Enabled = w != null, Run = () => { foreach (var x in WindowsOf(w)) W32.Minimize(x.Hwnd); } },
                new Item { Text = L("Andere ausblenden", "Hide Others"), Run = () => { foreach (var x in tracker.Windows.Where(x => w == null || x.Key != w.Key)) W32.Minimize(x.Hwnd); } },
                new Item { Text = L("Alle einblenden", "Show All"), Run = () => { foreach (var x in tracker.Windows.Where(x => x.Minimized)) W32.ShowWindow(x.Hwnd, W32.SW_RESTORE); } },
                Item.Sep,
                new Item { Text = name + L(" beenden", " Quit"), Shortcut = "⌃Q", Enabled = w != null, Run = () => { foreach (var x in WindowsOf(w)) W32.Close(x.Hwnd); } },
            };
            return items;
        }

        void AboutApp(WinInfo w, string name)
        {
            string text = null;
            ImageSource icon = MacStyle.Own("finder");
            if (w != null)
            {
                icon = MacApps.IconOf(w.Hwnd) ?? MacStyle.Tile(EntryFor(w)?.Icon ?? w.Icon);
                try
                {
                    if (w.ExePath != null && MacApps.NameOf(w.Hwnd) == null && !IsExplorer(w))
                    {
                        var v = FileVersionInfo.GetVersionInfo(w.ExePath);
                        text = L("Version ", "Version ") + (v.ProductVersion ?? v.FileVersion) + (string.IsNullOrWhiteSpace(v.LegalCopyright) ? "" : "\n" + v.LegalCopyright.Trim());
                    }
                }
                catch { }
            }
            MacAlert.Ask(name, text, icon, new[] { ("OK", (Action)null, true) });
        }

        List<Item> AppMenus(string key, string name)
        {
            var w = frontWin;
            bool has = w != null;
            var items = new List<Item>();
            switch (key)
            {
                case "file":
                    items.Add(new Item { Text = name == "Finder" ? L("Neues Finder-Fenster", "New Finder Window") : L("Neues Fenster", "New Window"), Shortcut = "⌃N", Run = () => { if (name == "Finder" || !has) ShellActions.Files(); else SendToApp(CTRL, (byte)'N'); } });
                    items.Add(new Item { Text = L("Öffnen …", "Open…"), Shortcut = "⌃O", Enabled = has, Run = () => SendToApp(CTRL, (byte)'O') });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Fenster schließen", "Close Window"), Shortcut = "⌃W", Enabled = has, Run = () => { if (w != null) W32.Close(w.Hwnd); } });
                    items.Add(new Item { Text = L("Sichern", "Save"), Shortcut = "⌃S", Enabled = has, Run = () => SendToApp(CTRL, (byte)'S') });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Drucken …", "Print…"), Shortcut = "⌃P", Enabled = has, Run = () => SendToApp(CTRL, (byte)'P') });
                    break;
                case "edit":
                    items.Add(new Item { Text = L("Widerrufen", "Undo"), Shortcut = "⌃Z", Enabled = has, Run = () => SendToApp(CTRL, (byte)'Z') });
                    items.Add(new Item { Text = L("Wiederholen", "Redo"), Shortcut = "⌃Y", Enabled = has, Run = () => SendToApp(CTRL, (byte)'Y') });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Ausschneiden", "Cut"), Shortcut = "⌃X", Enabled = has, Run = () => SendToApp(CTRL, (byte)'X') });
                    items.Add(new Item { Text = L("Kopieren", "Copy"), Shortcut = "⌃C", Enabled = has, Run = () => SendToApp(CTRL, (byte)'C') });
                    items.Add(new Item { Text = L("Einsetzen", "Paste"), Shortcut = "⌃V", Enabled = has, Run = () => SendToApp(CTRL, (byte)'V') });
                    items.Add(new Item { Text = L("Alles auswählen", "Select All"), Shortcut = "⌃A", Enabled = has, Run = () => SendToApp(CTRL, (byte)'A') });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Suchen", "Find"), Shortcut = "⌃F", Enabled = has, Run = () => SendToApp(CTRL, (byte)'F') });
                    break;
                case "view":
                    items.Add(new Item { Text = L("Vergrößern", "Zoom In"), Shortcut = "⌃+", Enabled = has, Run = () => SendToApp(CTRL, 0xBB) });
                    items.Add(new Item { Text = L("Verkleinern", "Zoom Out"), Shortcut = "⌃−", Enabled = has, Run = () => SendToApp(CTRL, 0xBD) });
                    items.Add(new Item { Text = L("Originalgröße", "Actual Size"), Shortcut = "⌃0", Enabled = has, Run = () => SendToApp(CTRL, 0x30) });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Vollbildmodus", "Enter Full Screen"), Shortcut = "F11", Enabled = has, Run = () => SendToApp(0x7A) });
                    break;
                case "go":
                    void Go(string text, string path, string sc = null) => items.Add(new Item { Text = text, Shortcut = sc, Run = () => ShellActions.Files(path) });
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    items.Add(new Item { Text = L("Zurück", "Back"), Shortcut = "⌃[", Enabled = has, Run = () => SendToApp(0x12, 0x25) });
                    items.Add(new Item { Text = L("Vorwärts", "Forward"), Shortcut = "⌃]", Enabled = has, Run = () => SendToApp(0x12, 0x27) });
                    items.Add(Item.Sep);
                    Go(L("Dokumente", "Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    Go(L("Schreibtisch", "Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                    Go("Downloads", Path.Combine(home, "Downloads"));
                    Go(L("Benutzerordner", "Home"), home);
                    Go(L("Computer", "Computer"), "");
                    items.Add(new Item { Text = L("Programme", "Applications"), Run = () => ToggleMenu() });
                    items.Add(new Item { Text = L("Dienstprogramme", "Utilities"), Run = () => ShellActions.Run("control.exe") });
                    break;
                case "window":
                    items.Add(new Item { Text = L("Im Dock ablegen", "Minimize"), Shortcut = "⌃M", Enabled = has, Run = () => { if (w != null) W32.Minimize(w.Hwnd); } });
                    items.Add(new Item { Text = L("Zoomen", "Zoom"), Enabled = has, Run = () => { if (w != null) W32.ShowWindow(w.Hwnd, W32.IsZoomed(w.Hwnd) ? W32.SW_RESTORE : 3); } });
                    items.Add(new Item { Text = L("Fenster links anordnen", "Move Window to Left Side of Screen"), Enabled = has, Run = () => SendToApp(WIN, 0x25) });
                    items.Add(new Item { Text = L("Fenster rechts anordnen", "Move Window to Right Side of Screen"), Enabled = has, Run = () => SendToApp(WIN, 0x27) });
                    items.Add(Item.Sep);
                    items.Add(new Item { Text = L("Alle nach vorne bringen", "Bring All to Front"), Enabled = has, Run = () => ActivateApp(WindowsOf(w)) });
                    var list = WindowsOf(w);
                    if (list.Count > 0)
                    {
                        items.Add(Item.Sep);
                        foreach (var x in list) { var win = x; items.Add(new Item { Text = Short(W32.Text(win.Hwnd)), Checked = win.Hwnd == tracker.Foreground || win == w, Run = () => W32.Activate(win.Hwnd) }); }
                    }
                    break;
                default:
                    items.Add(new Item { Text = name + L("-Hilfe", " Help"), Enabled = has, Run = () => SendToApp(0x70) });
                    items.Add(new Item { Text = L("WinMorph-Hilfe", "WinMorph Help"), Run = ShellActions.WinMorphApp });
                    break;
            }
            return items;
        }

        static string Short(string s) => string.IsNullOrEmpty(s) ? "—" : s.Length > 54 ? s.Substring(0, 54) + "…" : s;

        /// <summary>The Dock's click on an app with windows: its windows come to the front, minimised ones only when nothing else is open.</summary>
        static void ActivateApp(List<WinInfo> wins)
        {
            var open = wins.Where(x => !W32.IsIconic(x.Hwnd)).ToList();
            if (open.Count == 0) { if (wins.Count > 0) W32.Activate(wins[0].Hwnd); return; }
            for (int i = open.Count - 1; i >= 0; i--) W32.Activate(open[i].Hwnd);
        }

        // ================= Dock =================
        FrameworkElement MacDock()
        {
            double icon = MacStyle.DockIcon;
            var apps = new StackPanel { Orientation = Orientation.Horizontal };
            var extras = new StackPanel { Orientation = Orientation.Horizontal };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 5, 0) };
            row.Children.Add(apps);
            row.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 10, 6, 12), Background = B(0x000000, 40) });
            row.Children.Add(extras);
            var pane = MacStyle.Glass(row, 22, C(0xFFFFFF, 92), shadow: true);
            pane.Height = icon + 12;
            pane.HorizontalAlignment = HorizontalAlignment.Center; pane.VerticalAlignment = VerticalAlignment.Bottom;
            pane.Margin = new Thickness(0, 0, 0, 6);

            string last = null;
            refreshers.Add(() =>
            {
                var pinned = AppCatalog.Pinned().Where(a => !a.Is("explorer.exe") && !a.Is("taskmgr.exe") && !IsSettings(a)).ToList();
                var sig = string.Join("|", pinned.Select(a => a.Name + (a.Icon != null))) + "#" + string.Join("|", tracker.Windows.Select(w => w.Key + ":" + w.Minimized));
                if (sig == last) return;
                last = sig;
                apps.Children.Clear();
                var used = new HashSet<IntPtr>();
                var finderWins = tracker.Windows.Where(IsExplorer).ToList();
                foreach (var w in finderWins) used.Add(w.Hwnd);
                apps.Children.Add(Tile(MacStyle.Own("finder"), "Finder", finderWins, () => { if (finderWins.Count == 0) ShellActions.Files(); else ActivateApp(finderWins); }, () => FinderMenu(finderWins)));
                apps.Children.Add(Tile(MacStyle.Own("launchpad"), "Launchpad", new List<WinInfo>(), () => ToggleMenu(), null));
                foreach (var app in pinned)
                {
                    var a = app;
                    var wins = tracker.Windows.Where(w => !used.Contains(w.Hwnd) && a.Matches(w)).ToList();
                    foreach (var w in wins) used.Add(w.Hwnd);
                    apps.Children.Add(Tile(MacStyle.Tile(a.Icon), a.Name, wins, () => { if (wins.Count == 0) a.Start(); else ActivateApp(wins); }, () => DockMenu(wins, a)));
                }
                var settingsWins = tracker.Windows.Where(w => !used.Contains(w.Hwnd) && IsSettingsWindow(w)).ToList();
                foreach (var w in settingsWins) used.Add(w.Hwnd);
                foreach (var grp in tracker.Windows.Where(w => !used.Contains(w.Hwnd)).GroupBy(w => w.Key))
                {
                    var wins = grp.ToList();
                    var first = wins[0];
                    var img = MacApps.IconOf(first.Hwnd) ?? MacStyle.Tile(EntryFor(first)?.Icon ?? first.Icon);
                    apps.Children.Add(Tile(img, AppTitle(first), wins, () => ActivateApp(wins), () => DockMenu(wins, MacApps.NameOf(first.Hwnd) != null ? null : EntryFor(first))));
                }
                apps.Children.Add(Tile(MacStyle.Own("settings"), L("Systemeinstellungen", "System Settings"), settingsWins, () => { if (settingsWins.Count == 0) ShellActions.Settings(); else ActivateApp(settingsWins); }, null));
            });

            // Downloads and Trash, right of the separator
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            extras.Children.Add(Tile(MacStyle.Own("downloads"), "Downloads", new List<WinInfo>(), () => ShellActions.Files(downloads), null));
            var trashImg = new Image();
            var trash = Tile(MacStyle.Own("trash"), L("Papierkorb", "Trash"), new List<WinInfo>(), MacFiles.OpenTrash, () =>
                MacMenu.AtCursor(new List<Item> { new Item { Text = L("Öffnen", "Open"), Run = MacFiles.OpenTrash }, Item.Sep, new Item { Text = L("Papierkorb entleeren", "Empty Trash"), Enabled = MacApps.TrashFull(), Run = MacFiles.EmptyTrash } }), trashImg);
            extras.Children.Add(trash);
            bool? full = null;
            clockUpdaters.Add(() =>
            {
                if (tickMenu % 4 != 0 && full != null) return;
                bool now = MacApps.TrashFull();
                if (now == full) return;
                full = now;
                trashImg.Source = MacStyle.Own(now ? "trash-full" : "trash");
            });

            var host = new Grid();
            host.Children.Add(pane);
            if (!preview) pane.Loaded += (s, e) =>
            {
                // the Dock slides up once when the look starts
                var tt = new TranslateTransform(0, icon + 30);
                pane.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = MacStyle.OutQuint });
            };
            return host;
        }

        static bool IsSettings(AppEntry a) => (a.ParsingPath ?? "").IndexOf("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase) >= 0;
        static bool IsSettingsWindow(WinInfo w) => (w.AppId ?? "").IndexOf("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase) >= 0 || (w.ExePath ?? "").EndsWith("SystemSettings.exe", StringComparison.OrdinalIgnoreCase);

        /// <summary>One Dock item: the icon, the running dot, the name above it while the pointer is there, the launch bounce.</summary>
        FrameworkElement Tile(ImageSource img, string name, List<WinInfo> wins, Action click, Action menu, Image reuse = null)
        {
            double icon = MacStyle.DockIcon;
            var g = new Grid { Width = icon + 2, Height = icon + 12, Background = Brushes.Transparent, Cursor = Cursors.Arrow };
            var im = reuse ?? new Image();
            im.Source = img; im.Width = icon; im.Height = icon; im.VerticalAlignment = VerticalAlignment.Top; im.Margin = new Thickness(0, 2, 0, 0);
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            var lift = new TranslateTransform();
            im.RenderTransform = lift;
            g.Children.Add(im);
            var press = new Rectangle { Width = icon, Height = icon, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), Fill = B(0x000000, 95), OpacityMask = new ImageBrush(img), Visibility = Visibility.Hidden, RenderTransform = lift, IsHitTestVisible = false };
            g.Children.Add(press);
            bool running = wins.Count > 0;
            if (running) g.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = B(0x1D1D1F, 200), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2.5) });

            // the name label floats above the Dock
            var label = new Border
            {
                Child = MacStyle.Text(name, 13, MacStyle.Label), Padding = new Thickness(10, 3, 10, 4), CornerRadius = new CornerRadius(7),
                Background = B(0xF2F2F4, 240), BorderBrush = B(0x000000, 30), BorderThickness = new Thickness(0.6), Margin = new Thickness(10),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = 0.22 },
            };
            var pop = new Popup { Child = label, PlacementTarget = g, Placement = PlacementMode.Custom, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, StaysOpen = true };
            pop.CustomPopupPlacementCallback = (popup, target, offset) => new[] { new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, -popup.Height + 4), PopupPrimaryAxis.Horizontal) };
            g.MouseEnter += (s, e) => { if (!preview && MacMenu.Current == null) pop.IsOpen = true; };
            g.MouseLeave += (s, e) => { pop.IsOpen = false; press.Visibility = Visibility.Hidden; };
            g.Unloaded += (s, e) => pop.IsOpen = false;   // the Dock was rebuilt under the pointer
            g.MouseLeftButtonDown += (s, e) => { press.Visibility = Visibility.Visible; e.Handled = true; };
            g.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (press.Visibility != Visibility.Visible) return;
                press.Visibility = Visibility.Hidden;
                pop.IsOpen = false;
                if (!running && name != "Launchpad") Bounce(lift);
                click?.Invoke();
            };
            g.MouseRightButtonUp += (s, e) => { e.Handled = true; pop.IsOpen = false; menu?.Invoke(); };
            g.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; click?.Invoke(); } };
            return g;
        }

        /// <summary>The launch bounce: the icon hops three times while the app starts.</summary>
        static void Bounce(TranslateTransform lift)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            double h = -MacStyle.DockIcon * 0.38, t0 = 0;
            for (int i = 0; i < 3; i++)
            {
                a.KeyFrames.Add(new EasingDoubleKeyFrame(h, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t0 + 210)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t0 + 420)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                t0 += 420;
            }
            lift.BeginAnimation(TranslateTransform.YProperty, a);
        }

        void DockMenu(List<WinInfo> wins, AppEntry app)
        {
            var items = new List<Item>();
            foreach (var w in wins) { var win = w; items.Add(new Item { Text = Short(W32.Text(win.Hwnd)), Checked = win.Hwnd == tracker.Foreground, Run = () => W32.Activate(win.Hwnd) }); }
            if (wins.Count > 0) items.Add(Item.Sep);
            if (app != null)
            {
                var a = app;
                if (AppCatalog.IsPinned(a)) items.Add(new Item { Text = L("Aus dem Dock entfernen", "Remove from Dock"), Run = () => AppCatalog.Unpin(a) });
                else items.Add(new Item { Text = L("Im Dock behalten", "Keep in Dock"), Run = () => AppCatalog.Pin(a) });
                string path = a.ExePath ?? (a.Launch != null && File.Exists(a.Launch) ? a.Launch : null);
                if (path != null) items.Add(new Item { Text = L("Im Finder zeigen", "Show in Finder"), Run = () => ShellActions.Files(Path.GetDirectoryName(path)) });
                items.Add(Item.Sep);
                if (wins.Count == 0) items.Add(new Item { Text = L("Öffnen", "Open"), Run = a.Start });
                else items.Add(new Item { Text = L("Neues Fenster", "New Window"), Run = a.Start });
            }
            if (wins.Count > 0)
            {
                items.Add(new Item { Text = L("Ausblenden", "Hide"), Run = () => { foreach (var w in wins) W32.Minimize(w.Hwnd); } });
                items.Add(new Item { Text = L("Beenden", "Quit"), Run = () => { foreach (var w in wins) W32.Close(w.Hwnd); } });
            }
            if (items.Count > 0) MacMenu.AtCursor(items);
        }

        void FinderMenu(List<WinInfo> wins)
        {
            var items = new List<Item>();
            foreach (var w in wins) { var win = w; items.Add(new Item { Text = Short(W32.Text(win.Hwnd)), Checked = win.Hwnd == tracker.Foreground, Run = () => W32.Activate(win.Hwnd) }); }
            if (wins.Count > 0) items.Add(Item.Sep);
            items.Add(new Item { Text = L("Neues Finder-Fenster", "New Finder Window"), Run = () => MacFiles.OpenNew(null) });
            if (wins.Count > 0) { items.Add(Item.Sep); items.Add(new Item { Text = L("Ausblenden", "Hide"), Run = () => { foreach (var w in wins) W32.Minimize(w.Hwnd); } }); }
            MacMenu.AtCursor(items);
        }
    }
}

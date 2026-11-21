using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Runs the WinMorph shell: hides the Windows taskbar, shows themed bars, docks and
    /// start menus, and recolours window frames. Everything is undone on exit.
    /// </summary>
    public sealed partial class ShellHost
    {
        readonly ShellTheme t;
        readonly WindowTracker tracker;
        readonly List<BarWindow> bars = new List<BarWindow>();
        readonly List<Action> refreshers = new List<Action>();
        readonly List<Action> clockUpdaters = new List<Action>();
        readonly DispatcherTimer clock, nativeGuard;
        FrameworkElement startAnchor;
        BarWindow startBar;
        StartMenu menu;
        DateTime menuClosed;
        bool peek;
        bool origAutoHide;
        KeyHook keys;
        Switcher switcher;
        volatile bool switching;
        int building; // monitor index while bars are built
        W32.WinEventProc nativeShow;   // kept in a field: the hook must not lose its delegate to the garbage collector
        IntPtr nativeHook;
        DispatcherTimer rebuildTimer, peekTimer;
        static bool De => Core.Loc.De;

        readonly bool preview;
        readonly List<(Edge edge, double size, UIElement content, Brush bg)> previewBars = new List<(Edge, double, UIElement, Brush)>();

        public ShellHost(ShellTheme theme, bool preview = false)
        {
            t = theme;
            this.preview = preview;
            tracker = preview ? WindowTracker.Demo(t) : new WindowTracker(t);
            tracker.Changed += () => { foreach (var r in refreshers) r(); };
            tracker.ForegroundChanged += () => { foreach (var r in refreshers) r(); CheckFullscreen(); };
            tracker.Changed += CheckFullscreen;
            if (!preview)
            {
                AppCatalog.Loaded += () => { foreach (var r in refreshers) r(); };
                AppCatalog.LoadAsync(Dispatcher.CurrentDispatcher);
            }

            if (preview) { BuildBars(); return; }

            ThemeToolTips();
            ShellActions.Own = t;   // every look brings its own Explorer, Task Manager and Run box
            if (ShellActions.Own != null)
            {
                // the look's own Explorer and Task Manager, also when opened from the Start menu, a desktop icon or Ctrl+Alt+Del
                AppEntry.OwnApp = a =>
                {
                    if (a.Is("explorer.exe")) { ShellActions.Files(); return true; }
                    if (a.Is("taskmgr.exe")) { ShellActions.TaskManager(); return true; }
                    return false;
                };
                OwnAppRedirect.Start();
            }
            ShellActions.Windows = () => tracker.ByRecentUse();
            CursorSkin.Apply(t);
            origAutoHide = ShellControl.TakeTaskbar();
            Core.Applier.SetAutoHideRaw(true);
            HideNative();

            BuildAllMonitors();

            clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) => { foreach (var c in clockUpdaters) c(); CheckFullscreen(); }, Dispatcher.CurrentDispatcher);
            nativeGuard = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (s, e) => { if (!peek) HideNative(); }, Dispatcher.CurrentDispatcher);
            // the moment Windows shows its taskbar again (Explorer, sign-in, a notification), it is hidden – no flash
            nativeShow = (hook, ev, hwnd, idObject, idChild, thread, time) =>
            {
                if (idObject != 0 || peek || hwnd == IntPtr.Zero) return;
                var cls = W32.ClassOf(hwnd);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") W32.ShowWindow(hwnd, W32.SW_HIDE);
            };
            nativeHook = W32.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, nativeShow, 0, 0, 0);
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            foreach (var r in refreshers) r();
            foreach (var c in clockUpdaters) c();
            keys = new KeyHook(OnKey);
            keys.Start();
            // Win+L → WinMorph lock screen (Windows would lock before the hook can stop it)
            if (LockSaverReady) Core.LockGuard.Arm(); else Core.LockGuard.Disarm();
            Core.LockGuard.WatchPower(() => LockSaverReady && keys != null);
            Core.LockGuard.Suspending -= CustomLock.Suspending;
            Core.LockGuard.Suspending += CustomLock.Suspending;
            if (LockSaverReady) MakeSaverHandOver();
            CustomLock.Closed -= AfterUnlock;
            CustomLock.Closed += AfterUnlock;
            RefreshLockRedirect();
            new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) =>
            {
                // also picks up the one-time setup done from the app while the shell runs
                if (keys != null && LockSaverReady && !Core.LockGuard.Armed && Core.LockGuard.Ready && !Core.LockGuard.Locked()) Core.LockGuard.Arm();
                RefreshLockRedirect();
            }, Dispatcher.CurrentDispatcher);
            AppCatalog.PinsChanged += () => { foreach (var r in refreshers) r(); };
            Notify.Use(t);   // the look's own notices (Zen focus timer, terminal timer); Windows notifications stay as they are
            // build the start menu in the background so the first open is instant
            AppCatalog.Loaded += () => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(WarmMenu), DispatcherPriority.ApplicationIdle);
        }

        bool Hacker => t.Start == StartKind.Terminal;

        void CheckFullscreen() { foreach (var b in bars) try { b.CheckFullscreen(); } catch { } }

        /// <summary>Tooltips in the colours and font of the look instead of the white Windows ones.</summary>
        void ThemeToolTips()
        {
            var st = new Style(typeof(ToolTip));
            bool zen = t.Id == "zen";
            if (Mac)
            {
                st.Setters.Add(new Setter(Control.BackgroundProperty, B(0xF4F4F6, 248)));
                st.Setters.Add(new Setter(Control.ForegroundProperty, B(0x1D1D1F)));
                st.Setters.Add(new Setter(Control.BorderBrushProperty, B(0x000000, 40)));
                st.Setters.Add(new Setter(Control.FontFamilyProperty, MacStyle.Font));
                st.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
                st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 3, 7, 4)));
                st.Setters.Add(new Setter(ToolTip.HasDropShadowProperty, true));
            }
            else
            {
                var bg = zen ? Color.FromArgb(245, 0xFA, 0xFC, 0xFB) : Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B);
                st.Setters.Add(new Setter(Control.BackgroundProperty, B(Hacker ? C(0x000000) : bg)));
                st.Setters.Add(new Setter(Control.ForegroundProperty, B(t.MenuFg)));
                st.Setters.Add(new Setter(Control.BorderBrushProperty, B(zen ? C(0xFFFFFF) : A(t.Accent, 200))));
                st.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(t.Font)));
                st.Setters.Add(new Setter(Control.PaddingProperty, zen ? new Thickness(12, 6, 12, 7) : new Thickness(8, 4, 8, 4)));
                if (zen) st.Setters.Add(new Setter(ToolTip.HasDropShadowProperty, true));
            }
            var tpl = new ControlTemplate(typeof(ToolTip));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(Mac ? 0.6 : 1));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Hacker ? 0 : zen ? 12 : Mac ? 5 : 6));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(TextElement.FontFamilyProperty, Mac ? MacStyle.Font : new FontFamily(t.Font));
            cp.SetValue(TextElement.ForegroundProperty, B(t.MenuFg));
            border.AppendChild(cp);
            tpl.VisualTree = border;
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            Application.Current.Resources[typeof(ToolTip)] = st;
        }
        HackerConsole console;

        void WarmMenu()
        {
            if (Hacker)
            {
                if (console != null) return;
                console = new HackerConsole(t, () => tracker.ByRecentUse());
                console.Hidden += () => menuClosed = DateTime.Now;
                console.ShowActivated = false; console.Show(); console.Hide(); console.ShowActivated = true;
                menuClosed = DateTime.MinValue;
                return;
            }
            if (menu != null) return;
            menu = new StartMenu(t, new Rect(0, 0, 0, 0), Edge.Bottom);
            menu.Hidden += () => menuClosed = DateTime.Now;
            menu.ShowActivated = false;   // warm up without taking focus from the user's window
            menu.Show();
            menu.CloseMenu();
            menu.ShowActivated = true;
            menuClosed = DateTime.MinValue;
        }

        /// <summary>One set of bars per monitor; primary is built last so it owns the Start anchor.</summary>
        void BuildAllMonitors()
        {
            var mons = W32.Monitors();
            for (int i = mons.Count - 1; i >= 0; i--) { building = i; BuildBars(); }
            building = 0;
            foreach (var b in bars)
            {
                b.ExplorerRestarted += () => { Core.Applier.SetAutoHideRaw(true); HideNative(); };
                b.DisplayChanged += ScheduleRebuild;
                b.Show();
            }
        }

        void ScheduleRebuild()
        {
            rebuildTimer?.Stop();
            rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            rebuildTimer.Tick += (s, e) =>
            {
                rebuildTimer.Stop();
                if (W32.Monitors().Count == bars.Select(b => b.MonitorIndex).Distinct().Count()) { foreach (var b in bars) b.Place(); return; }
                foreach (var b in bars.ToList()) try { b.Close(); } catch { }
                bars.Clear(); refreshers.Clear(); clockUpdaters.Clear();
                BuildAllMonitors();
                foreach (var r in refreshers) r();
                foreach (var c in clockUpdaters) c();
            };
            rebuildTimer.Start();
        }

        // ---------------- preview image (no system changes) ----------------
        public System.Windows.Media.Imaging.BitmapSource RenderPreview(Core.Preset p, int w, int h, bool withMenu, double outScale = 1)
        {
            foreach (var r in refreshers) r();
            foreach (var c in clockUpdaters) c();
            var root = new Grid { Width = w, Height = h, ClipToBounds = true };
            root.Children.Add(new Image { Source = Core.WallpaperArt.Render(p, (int)(w * outScale), (int)(h * outScale), false), Stretch = Stretch.Fill });
            var dock = new DockPanel { LastChildFill = true };
            foreach (var b in previewBars.OrderBy(x => x.edge == Edge.Left ? 1 : 0))
            {
                var host = new Border { Background = b.bg, Child = b.content };
                if (b.edge == Edge.Left || b.edge == Edge.Right) host.Width = b.size; else host.Height = b.size;
                DockPanel.SetDock(host, b.edge == Edge.Top ? System.Windows.Controls.Dock.Top : b.edge == Edge.Left ? System.Windows.Controls.Dock.Left : b.edge == Edge.Right ? System.Windows.Controls.Dock.Right : System.Windows.Controls.Dock.Bottom);
                dock.Children.Add(host);
            }
            var desk = new Grid();
            dock.Children.Add(desk);
            root.Children.Add(dock);

            if (withMenu && Hacker)
            {
                var hc = new HackerConsole(t, () => tracker.Windows);
                hc.PreviewFill();
                var content = (FrameworkElement)hc.Content;
                hc.Content = null;
                content.Width = Math.Min(1000, w * 0.62); content.Height = h * 0.58;
                content.HorizontalAlignment = HorizontalAlignment.Center; content.VerticalAlignment = VerticalAlignment.Top;
                content.Margin = new Thickness(0, t.BarHeight, 0, 0);
                ((Border)content).Background = B(0x000000);
                desk.Children.Add(content);
            }
            else if (withMenu)
            {
                var m = new StartMenu(t, new Rect(0, 0, 0, 0), Edge.Bottom);
                var content = (FrameworkElement)m.Content;
                m.Content = null;
                if (m.IsFullScreen) { content.Width = w; content.Height = h; root.Children.Add(content); }
                else
                {
                    content.HorizontalAlignment = t.Menu != MenuKind.Seven || t.Layout == BarLayout.Floating ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    content.VerticalAlignment = t.Menu != MenuKind.Seven ? VerticalAlignment.Top : (t.Layout == BarLayout.TerminalTop ? VerticalAlignment.Top : VerticalAlignment.Bottom);
                    if (t.Menu != MenuKind.Seven) content.Margin = new Thickness(0, h * 0.2, 0, 0);
                    desk.Children.Add(content);
                }
            }
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w * outScale), (int)(h * outScale), 96 * outScale, 96 * outScale, PixelFormats.Pbgra32);
            bmp.Render(root);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Releases the hooks a preview host created.</summary>
        public void DisposePreview()
        {
            tracker.Dispose();
            refreshers.Clear();
            clockUpdaters.Clear();
        }

        // ---------------- native taskbar ----------------
        static void HideNative() => ShellControl.HideNative();
        static void ShowNative() => ShellControl.ShowNative();

        void TogglePeek()
        {
            peek = !peek;
            if (peek)
            {
                Core.Applier.SetAutoHideRaw(false); ShowNative();
                // the Windows taskbar is only shown briefly (for its tray icons), then hidden again
                peekTimer?.Stop();
                peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                peekTimer.Tick += (s, e) => { peekTimer.Stop(); if (peek) TogglePeek(); };
                peekTimer.Start();
            }
            else { peekTimer?.Stop(); Core.Applier.SetAutoHideRaw(true); HideNative(); }
        }

        /// <summary>After unlocking (or a remote session) Windows shows its taskbar again – hide it at once.</summary>
        void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            winDown = false; switching = false;
            if (LockSaverReady) Core.LockGuard.Arm();
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                CancelSwitcher();
                foreach (var delay in new[] { 0, 300, 900, 2000, 4000 })
                {
                    var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delay)) };
                    tm.Tick += (s2, e2) => { tm.Stop(); Core.Applier.SetAutoHideRaw(true); HideNative(); foreach (var b in bars) try { b.Place(); } catch { } };
                    tm.Start();
                }
            }));
        }

        /// <summary>keepHidden: at sign-out/shutdown the taskbar stays hidden, so it does not flash up at the next sign-in.</summary>
        public void Shutdown(bool keepHidden = false)
        {
            try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
            clock.Stop(); nativeGuard.Stop();
            if (nativeHook != IntPtr.Zero) { W32.UnhookWinEvent(nativeHook); nativeHook = IntPtr.Zero; }
            keys?.Stop(); keys = null;
            Core.LockGuard.Disarm();   // Win+L belongs to Windows again
            HoverPreview.CloseNow();
            try { switcher?.Close(); } catch { }
            try { menu?.Close(); } catch { }
            try { console?.Close(); } catch { }
            HackerPanel.CloseAll();
            MacPanels.CloseAll(); MacMenu.Current?.Dismiss(true); MacSpotlight.CloseNow();
            Notify.Stop();
            Zen.Shutdown();
            foreach (var b in bars) try { b.Close(); } catch { }
            tracker.Dispose();
            ShellActions.Own = null; ShellActions.Windows = null;
            AppEntry.OwnApp = null;
            OwnAppRedirect.Stop();
            if (keepHidden) return;
            CursorSkin.Reset();
            ShowNative();
            Core.Applier.SetAutoHideRaw(origAutoHide);
        }

        // ---------------- keyboard: Windows key, Win+X, Win+C, Ctrl+Esc, Alt+Tab ----------------
        bool winDown, otherKey, swallowedDuringWin;

        void Ui(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

        /// <summary>Runs on the keyboard thread – must stay quick. Returns true to swallow the key.</summary>
        bool OnKey(bool down, bool up, uint vk, uint flags)
        {
            bool alt = (flags & 0x20) != 0;
            if (CustomLock.Active)
            {
                // locked: nothing may switch away from the lock screen
                if (vk == 0x5B || vk == 0x5C)
                {
                    winDown = false;
                    if (down) return true;
                    KeyHook.Tap(0xE8);   // the release goes through (so Windows does not think it is still held), without opening Start
                    return false;
                }
                if (alt && (vk == 0x09 || vk == 0x1B || vk == 0x73 || vk == 0x20)) return true;   // Alt+Tab, Alt+Esc, Alt+F4, Alt+Space
                if (vk == 0x1B && W32.KeyDown(0x11)) return true;                                 // Ctrl+Esc, Ctrl+Shift+Esc (Task Manager)
                if (vk == 0x86) return true;                                                      // Copilot key
                switching = false;
                return false;
            }
            if (vk == 0x5B || vk == 0x5C)
            {
                if (down) { if (!winDown) { winDown = true; otherKey = false; swallowedDuringWin = false; } return false; }
                if (up && winDown)
                {
                    winDown = false;
                    // a dummy key stops Windows from opening its own Start menu
                    if (!otherKey || swallowedDuringWin) KeyHook.Tap(0xE8);
                    if (!otherKey) Ui(() => ToggleMenu());
                }
                return false;
            }
            if (winDown && !W32.KeyDown(0x5B) && !W32.KeyDown(0x5C)) winDown = false;   // the release was missed (e.g. Win+L)
            if (switching && down && !alt && !W32.KeyDown(0x12)) { switching = false; Ui(CancelSwitcher); }
            // Win+L → own lock screen (still locks). Only while Windows' own Win+L is switched off –
            // otherwise Windows locks anyway and the WinMorph lock screen would follow after unlocking.
            if (down && winDown && vk == 0x4C && lockRedirect)
            {
                swallowedDuringWin = true; otherKey = true;
                Ui(ShellActions.Lock);
                return true;
            }
            if (down && winDown)
            {
                otherKey = true;
                if (vk == 0x58) { swallowedDuringWin = true; Ui(ShowQuickMenu); return true; }                 // Win+X
                if (Mac && (vk == 0x53 || vk == 0x20)) { swallowedDuringWin = true; Ui(() => MacSpotlight.Toggle()); return true; }   // Win+S, Win+Space → Spotlight
                if (ShellActions.Own != null)
                {
                    if (vk == 0x45) { swallowedDuringWin = true; Ui(() => ShellActions.Files()); return true; }      // Win+E → the look's explorer
                    if (vk == 0x52) { swallowedDuringWin = true; Ui(() => { console?.SafeHide(); ShellActions.RunDialog(); }); return true; } // Win+R → the look's run box
                }
                return false;
            }
            if (vk == 0x7B && down && alt && W32.KeyDown(0x11) && W32.KeyDown(0x10)) { Ui(ShellControl.EmergencyStop); return true; } // Ctrl+Alt+Shift+F12
            // volume keys: the look's own display instead of Windows' pop-up (keys the shell sends itself pass through)
            if ((vk == 0xAD || vk == 0xAE || vk == 0xAF) && (flags & 0x10) == 0)
            {
                if (down) Ui(() => VolumeKey(vk));
                return true;
            }

            if (vk == 0x09 && down && alt)                                                                   // Alt+Tab
            {
                bool back = W32.KeyDown(0x10);
                if (!switching) { switching = true; Ui(() => OpenSwitcher(back)); }
                else Ui(() => switcher?.Move(back ? -1 : 1));
                return true;
            }
            if (switching)
            {
                if (down && vk == 0x1B) { switching = false; Ui(CancelSwitcher); return true; }
                if (down && (vk == 0x25 || vk == 0x26)) { Ui(() => switcher?.Move(-1)); return true; }
                if (down && (vk == 0x27 || vk == 0x28)) { Ui(() => switcher?.Move(1)); return true; }
                if (up && (vk == 0xA4 || vk == 0xA5 || vk == 0x12)) { switching = false; KeyHook.Tap(0xE8); Ui(CommitSwitcher); return false; }
            }
            if (ShellActions.Own != null && vk == 0x1B && down && !alt && W32.KeyDown(0x11) && W32.KeyDown(0x10)) { Ui(ShellActions.TaskManager); return true; } // Ctrl+Shift+Esc → the look's task manager
            if (vk == 0x1B && down && !alt && W32.KeyDown(0x11) && !W32.KeyDown(0x10)) { Ui(() => ToggleMenu()); return true; } // Ctrl+Esc
            return false;
        }

        void VolumeKey(uint vk)
        {
            if (!Audio.Available) { KeyHook.Tap((byte)vk); return; }   // no endpoint to talk to: Windows handles it
            if (vk == 0xAD) Audio.Muted = !Audio.Muted; else Audio.Change(vk == 0xAF ? 2 : -2);
            if (Hacker) Osd.Volume(t, Audio.Level, Audio.Muted); else VolumeOsd.Show(t);
        }

        static bool LockSaverReady => ShellActions.SaverReady();

        /// <summary>
        /// The screen saver must not be "password protected": then Windows would show its own lock
        /// after it. WinMorph locks by itself instead (its own lock, or the real Windows lock as fallback).
        /// </summary>
        static void MakeSaverHandOver()
        {
            try
            {
                if (Core.Reg.Get(@"HKCU\Control Panel\Desktop", "ScreenSaverIsSecure") as string == "0") return;
                using (var k = Core.Reg.Create(@"HKCU\Control Panel\Desktop")) k.SetValue("ScreenSaverIsSecure", "0");
                Core.Native.SystemParametersInfo(0x0077 /* SPI_SETSCREENSAVESECURE */, 0, IntPtr.Zero, 3);
            }
            catch { }
        }

        void AfterUnlock()
        {
            winDown = false; switching = false;
            Core.Applier.SetAutoHideRaw(true); HideNative();
            foreach (var b in bars) try { b.Place(); } catch { }
        }
        // read on the keyboard thread: a cached flag, refreshed by a timer (registry reads are too slow there)
        volatile bool lockRedirect;
        void RefreshLockRedirect() => lockRedirect = LockSaverReady && Core.LockGuard.Armed;

        DispatcherTimer switcherDelay;

        void OpenSwitcher(bool back)
        {
            HoverPreview.CloseNow();
            var list = tracker.ByRecentUse();
            if (Mac) list = list.GroupBy(w => w.Key).Select(g => g.First()).ToList();   // Cmd+Tab: one entry per app
            if (list.Count == 0) { switching = false; return; }
            int start = list.Count > 1 ? (back ? list.Count - 1 : 1) : 0;
            CancelSwitcher();
            var sw = new Switcher(t, list, start);
            sw.Picked += () => { switching = false; CommitSwitcher(); };
            switcher = sw;
            if (!switching) { CommitSwitcher(); return; }   // Alt was already released
            // a quick Alt+Tab tap switches at once, like Windows: the switcher only appears when Alt is held
            switcherDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            switcherDelay.Tick += (s, e) => { switcherDelay.Stop(); if (switching && switcher == sw) sw.Show(); };
            switcherDelay.Start();
        }

        void CommitSwitcher()
        {
            switcherDelay?.Stop();
            var w = switcher?.Selected;
            try { switcher?.Close(); } catch { }
            switcher = null;
            if (w != null) W32.Activate(w.Hwnd);
        }

        void CancelSwitcher() { switcherDelay?.Stop(); try { switcher?.Close(); } catch { } switcher = null; }

        void ShowQuickMenu()
        {
            if (Hacker) { HackerPanel.Toggle("power", t, new Rect(0, 0, 0, 0)); return; }
            if (Mac) { menuOpeners.FirstOrDefault()?.Invoke(); return; }   // the Apple menu
            Rect anchor = startAnchor != null && startBar != null && startAnchor.IsLoaded ? startBar.ScreenRectOf(startAnchor) : new Rect(0, 0, 0, 0);
            var q = new QuickMenu(t, anchor, startBar?.Edge ?? Edge.Bottom);
            q.Show();
        }

        // ---------------- start menu ----------------
        public void ToggleMenu(FrameworkElement anchorEl = null, string search = null)
        {
            HoverPreview.CloseNow();
            if (Hacker)
            {
                HackerPanel.CloseAll();
                if (console == null) { console = new HackerConsole(t, () => tracker.ByRecentUse()); console.Hidden += () => menuClosed = DateTime.Now; }
                if (console.IsOpen) { console.SafeHide(); return; }
                if ((DateTime.Now - menuClosed).TotalMilliseconds < 250) return;
                console.Open(search);
                return;
            }
            if (menu != null && menu.IsOpen) { menu.CloseMenu(); return; }
            if ((DateTime.Now - menuClosed).TotalMilliseconds < 250) return; // the click that just closed it
            var el = anchorEl ?? startAnchor;
            var bar = (el != null ? Window.GetWindow(el) as BarWindow : null) ?? startBar;
            Rect anchor = el != null && bar != null && el.IsLoaded ? bar.ScreenRectOf(el) : new Rect(0, 0, 0, 0);
            if (menu == null)
            {
                menu = new StartMenu(t, anchor, bar?.Edge ?? Edge.Bottom);
                menu.Hidden += () => menuClosed = DateTime.Now;
            }
            menu.Open(anchor, bar?.Edge ?? Edge.Bottom, search);
        }

        // ---------------- bars ----------------
        void BuildBars()
        {
            switch (t.Layout)
            {
                case BarLayout.Bottom: ClassicBar(); break;
                case BarLayout.Floating: FloatingBar(); break;
                case BarLayout.TerminalTop: TerminalBar(); break;
                case BarLayout.Mac: MacBars(); break;
            }
        }

        BarWindow NewBar(Edge edge, double thickness, bool blur, UIElement content, Brush bg)
        {
            if (preview) { previewBars.Add((edge, thickness, content, bg)); return null; }
            uint abgr = 0;
            var w = new BarWindow(edge, thickness, blur, abgr, building) { Content = new Border { Background = bg, Child = content } };
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            bars.Add(w);
            return w;
        }

        // ---------- Windows 7 bottom bar ----------
        void ClassicBar()
        {
            var dock = new DockPanel { LastChildFill = true };
            var start = StartButton();
            DockPanel.SetDock(start, Dock.Left);
            dock.Children.Add(start);

            var tray = Tray();
            DockPanel.SetDock(tray, Dock.Right);
            dock.Children.Add(tray);

            if (t.SearchBox)
            {
                var sb = new Border { Width = 300, Margin = new Thickness(0, 0, 4, 0), Background = B(0xFFFFFF, 235), Cursor = Cursors.IBeam };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
                sp.Children.Add(Glyph("", 14, C(0x333333)));
                sp.Children.Add(T(t.SearchHint, t, 12.5, C(0x555555)));
                ((TextBlock)sp.Children[1]).Margin = new Thickness(10, 0, 0, 0);
                sb.Child = sp;
                sb.MouseLeftButtonUp += (s, e) => ToggleMenu(null, "");
                DockPanel.SetDock(sb, Dock.Left);
                dock.Children.Add(sb);
                var tv = Click(Glyph("", 16, t.BarFg), ShellActions.TaskView, t.BtnHover, pad: new Thickness(14, 0, 14, 0));
                DockPanel.SetDock(tv, Dock.Left);
                dock.Children.Add(tv);
            }

            if (t.QuickLaunch)
            {
                var ql = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                Action fillQl = () =>
                {
                    ql.Children.Clear();
                    foreach (var a in AppCatalog.Pinned().Take(3))
                    {
                        var app = a;
                        var b = Click(Img(app.Icon, 16), app.Start, t.BtnHover, pad: new Thickness(3));
                        b.ToolTip = app.Name;
                        ql.Children.Add(b);
                    }
                };
                fillQl();
                if (!preview) AppCatalog.Loaded += fillQl;
                DockPanel.SetDock(ql, Dock.Left);
                dock.Children.Add(ql);
            }

            FrameworkElement tasks = t.Btn == BtnStyle.Pill ? DockItems(Orientation.Horizontal, t.DockIcon * 0.72) : TaskButtons();
            if (t.CenterTasks) tasks.HorizontalAlignment = HorizontalAlignment.Center;
            dock.Children.Add(tasks);

            Brush bg = t.BarBg;
            UIElement content = dock;
            if (t.BarLine.A > 0)
                content = new Border { BorderBrush = B(t.BarLine), BorderThickness = new Thickness(0, 1, 0, 0), Child = dock };
            if (t.Id == "win7")
            {
                // glossy highlight on the upper half
                var g = new Grid();
                g.Children.Add(new Border { Background = V((0, C(0xFFFFFF, 46)), (0.5, C(0xFFFFFF, 14)), (0.51, C(0xFFFFFF, 0)), (1, C(0xFFFFFF, 0))), IsHitTestVisible = false });
                g.Children.Add(content);
                content = g;
            }
            startBar = NewBar(Edge.Bottom, t.BarHeight, t.Blur, content, bg);
        }



        // ---------- Zen: a frosted pill floating over the pond ----------
        void FloatingBar()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(StartButton());
            Border Gap() => new Border { Width = 1, Height = 22, Margin = new Thickness(8, 0, 8, 0), Background = B(A(t.BarFg, 34)), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Gap());
            row.Children.Add(DockItems(Orientation.Horizontal, t.DockIcon));
            row.Children.Add(Gap());
            row.Children.Add(Tray());
            var pill = ZenStyle.Pane(row, t.Radius, 150);
            pill.Height = t.BarHeight;
            pill.HorizontalAlignment = HorizontalAlignment.Center; pill.VerticalAlignment = VerticalAlignment.Center;
            startBar = NewBar(Edge.Bottom, t.BarHeight + 22, false, pill, Brushes.Transparent);
            if (!preview) pill.Loaded += (s, e) => ZenStyle.Ripple(pill, new Point(0.5, 1), 520);
        }

        // ---------- terminal status line (hacker look) ----------
        void TerminalBar()
        {
            var dock = new DockPanel();
            var start = StartButton();
            DockPanel.SetDock(start, System.Windows.Controls.Dock.Left);
            dock.Children.Add(start);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
            Color dim = t.BarFgDim;
            FrameworkElement Mod(TextBlock tb, string panel, string tip)
            {
                Border b = null;
                b = Click(tb, () => OpenPanel(panel, b), t.BtnHover, pad: new Thickness(7, 0, 7, 0));
                return b;
            }
            void Sep() => right.Children.Add(T("│", t, 12.5, A(dim, 150)));
            var net = T("", t, 12.5, dim); var cpu = T("", t, 12.5, t.BarFg); var mem = T("", t, 12.5, t.BarFg);
            var vol = T("", t, 12.5, t.BarFg); var bat = T("", t, 12.5, t.BarFg); var clockText = T("", t, 12.5, t.BarFg);
            right.Children.Add(Mod(net, "net", De ? "Netzwerk" : "Network")); Sep();
            right.Children.Add(Mod(cpu, "sys", De ? "System-Monitor" : "System monitor")); Sep();
            right.Children.Add(Mod(mem, "sys", De ? "System-Monitor" : "System monitor")); Sep();
            var volMod = Mod(vol, "vol", De ? "Lautstärke (Mausrad ändert)" : "Volume (mouse wheel changes it)");
            volMod.MouseWheel += (s, e) => { Audio.Change(e.Delta > 0 ? 2 : -2); UpdateVol(); Osd.Volume(t, Audio.Level, Audio.Muted); };
            right.Children.Add(volMod); Sep();
            bool hasBat = SysStats.Battery(out _, out _);
            if (hasBat) { right.Children.Add(Mod(bat, "power", De ? "Akku und Energie" : "Battery and power")); Sep(); }
            right.Children.Add(Mod(clockText, "cal", De ? "Kalender" : "Calendar")); Sep();
            right.Children.Add(Mod(T("[pwr]", t, 12.5, t.BarFg, FontWeights.Bold), "power", De ? "Herunterfahren, Neustart, Sperren …" : "Shut down, restart, lock …"));
            DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
            dock.Children.Add(right);
            void UpdateVol()
            {
                int l = Audio.Level;
                vol.Text = l < 0 ? "vol --" : (Audio.Muted ? "mute" : "vol " + l.ToString("00") + "%");
                vol.Foreground = B(Audio.Muted ? dim : t.BarFg);
            }
            int tick = 0;
            clockUpdaters.Add(() =>
            {
                clockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                double c = SysInfo.CpuSample(), m = SysStats.Mem();
                cpu.Text = "cpu " + c.ToString("00") + "% " + SysInfo.Sparkline(SysInfo.CpuHistory.Skip(Math.Max(0, SysInfo.CpuHistory.Count - 8)));
                cpu.Foreground = B(c > 85 ? C(0xFFB000) : t.BarFg);
                mem.Text = "mem " + m.ToString("00") + "%";
                mem.Foreground = B(m > 90 ? C(0xFFB000) : t.BarFg);
                SysInfo.NetRate(out double rx, out double tx);
                net.Text = "\u2193" + SysInfo.Bytes(rx).PadLeft(5) + " \u2191" + SysInfo.Bytes(tx).PadLeft(5);
                if (tick++ % 2 == 0) UpdateVol();
                if (hasBat && SysStats.Battery(out int pct, out bool ac))
                {
                    bat.Text = "bat " + pct + "%" + (ac ? "+" : "");
                    bat.Foreground = B(pct < 20 && !ac ? C(0xFF4D4D) : t.BarFg);
                }
            });
            dock.Children.Add(TaskButtons());
            startBar = NewBar(Edge.Top, t.BarHeight, false, new Border { BorderBrush = B(A(t.Accent, 120)), BorderThickness = new Thickness(0, 0, 0, 1), Child = dock }, t.BarBg);
        }



        void OpenPanel(string kind, FrameworkElement el)
        {
            HoverPreview.CloseNow();
            var bar = el != null ? Window.GetWindow(el) as BarWindow : null;
            var r = bar != null && el.IsLoaded ? bar.ScreenRectOf(el) : new Rect(0, 0, 0, 0);
            HackerPanel.Toggle(kind, t, r);
        }

        // ---------------- start buttons ----------------
        FrameworkElement StartButton()
        {
            FrameworkElement el;
            FrameworkElement self = null;
            string startText = De ? "Start" : "Start";
            switch (t.Start)
            {
                case StartKind.Orb:
                {
                    var orb = new Grid { Width = 40, Height = 40 };
                    var glass = new Ellipse_ { Fill = new RadialGradientBrush(C(0x6EC1FF), C(0x0B3C78)) { GradientOrigin = new Point(0.5, 0.25) }, Stroke = B(0xFFFFFF, 140) };
                    orb.Children.Add(glass.Make(36));
                    orb.Children.Add(new Ellipse_ { Fill = V((0, C(0xFFFFFF, 140)), (1, C(0xFFFFFF, 0))) }.Make(34, 17, top: true));
                    orb.Children.Add(Logo4(16, C(0xF35325), C(0x81BC06), C(0x05A6F0), C(0xFFBA08), -4));
                    var b = Click(orb, () => ToggleMenu(self), C(0x000000, 0), pad: new Thickness(6, 0, 8, 0));
                    b.MouseEnter += (s, e) => orb.Effect = new DropShadowEffect { Color = C(0x7FD4FF), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
                    b.MouseLeave += (s, e) => orb.Effect = null;
                    el = b;
                    break;
                }
                case StartKind.Terminal:
                    el = Click(T(" [λ] menu ", t, 12.5, C(0x000000), FontWeights.Bold), () => ToggleMenu(self), C(0x7CFF9B), B(t.Accent), pad: new Thickness(4, 0, 4, 0));
                    break;
                case StartKind.Zen:
                {
                    // the lotus on a white stone: the one coloured mark of the bar
                    var stone = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = ZenStyle.Lotus(22), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                    ZenStyle.Press(stone, C(0xFFFFFF, 170), C(0xFFFFFF, 240));
                    stone.MouseLeftButtonUp += (s, e) => { e.Handled = true; ToggleMenu(self); };
                    el = stone;
                    break;
                }
                default:
                {
                    var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = B(t.BarFg) };
                    el = Click(dot, () => ToggleMenu(self), t.BtnHover, radius: new CornerRadius(5), pad: new Thickness(12, 4, 8, 4));
                    break;
                }
            }
            el.ToolTip = De ? "Start (Windows-Taste)" : "Start (Windows key)";
            self = el;
            startAnchor = el;
            return el;
        }

        // ---------------- task buttons (one per window) ----------------
        FrameworkElement TaskButtons()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
            string last = null;
            refreshers.Add(() =>
            {
                // rebuild only when what the buttons show changed (a title only matters where it is written on the button)
                var sig = string.Join("|", tracker.Windows.Select(w => w.Hwnd + ":" + w.Minimized + ":" + (w.Hwnd == tracker.Foreground) + ":" + (t.Labels || t.Btn == BtnStyle.Text ? w.AppName ?? w.Title : "")));
                if (sig == last) return;
                last = sig;
                panel.Children.Clear();
                int i = 1;
                foreach (var w in tracker.Windows)
                {
                    bool active = w.Hwnd == tracker.Foreground && !w.Minimized;
                    var el = TaskButton(w, active, i++);
                    AttachExtras(el, () => new List<WinInfo> { w }, null, w);
                    panel.Children.Add(el);
                }
            });
            return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        void ToggleWindow(WinInfo w)
        {
            if (w.Hwnd == tracker.Foreground && !W32.IsIconic(w.Hwnd)) W32.Minimize(w.Hwnd);
            else W32.Activate(w.Hwnd);
        }

        void WindowMenu(WinInfo w) => ItemMenu(new List<WinInfo> { w }, null);

        AppEntry EntryFor(WinInfo w)
        {
            if (w == null) return null;
            var hit = w.AppId != null ? AppCatalog.Apps.FirstOrDefault(a => string.Equals(a.ParsingPath, w.AppId, StringComparison.OrdinalIgnoreCase)) : null;
            if (hit == null && w.ExePath != null) hit = AppCatalog.Apps.FirstOrDefault(a => a.ExePath != null && string.Equals(a.ExePath, w.ExePath, StringComparison.OrdinalIgnoreCase));
            if (hit == null && w.ExePath != null) hit = new AppEntry { Name = w.AppName, ExePath = w.ExePath, Launch = w.ExePath };
            return hit;
        }

        /// <summary>Right-click menu for a taskbar button or dock icon.</summary>
        void ItemMenu(List<WinInfo> wins, AppEntry app)
        {
            app = app ?? EntryFor(wins.FirstOrDefault());
            var items = new List<(string, Action)>();
            if (app != null)
            {
                var a = app;
                items.Add((De ? "Neues Fenster" : "New window", a.Start));
                if (AppCatalog.IsPinned(a)) items.Add((De ? "Von der Leiste lösen" : "Unpin from bar", () => AppCatalog.Unpin(a)));
                else items.Add((De ? "An die Leiste anheften" : "Pin to bar", () => AppCatalog.Pin(a)));
            }
            if (wins.Count > 0)
            {
                items.Add(("-", null));
                items.Add((De ? "Minimieren" : "Minimise", () => { foreach (var w in wins) W32.Minimize(w.Hwnd); }));
                items.Add((wins.Count > 1 ? (De ? "Alle Fenster schließen" : "Close all windows") : (De ? "Fenster schließen" : "Close window"), () => { foreach (var w in wins) W32.Close(w.Hwnd); }));
            }
            MiniMenu.Show(t, items);
        }

        /// <summary>Live preview on hover, new window on middle click, themed menu on right click.</summary>
        void AttachExtras(FrameworkElement el, Func<List<WinInfo>> wins, AppEntry app, WinInfo single)
        {
            if (Mac) return;
            el.MouseEnter += (s, e) => HoverPreview.Hover(t, el, () => wins().Where(w => W32.IsWindow(w.Hwnd)).ToList(), w => W32.Activate(w.Hwnd));
            el.MouseLeave += (s, e) => HoverPreview.Leave();
            el.PreviewMouseLeftButtonDown += (s, e) => HoverPreview.CloseNow();
            el.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { (app ?? EntryFor(single ?? wins().FirstOrDefault()))?.Start(); e.Handled = true; } };
            el.PreviewMouseRightButtonUp += (s, e) => { e.Handled = true; ItemMenu(wins(), app); };
        }

        FrameworkElement TaskButton(WinInfo w, bool active, int index)
        {
            double h = t.BarHeight;
            switch (t.Btn)
            {
                case BtnStyle.Glass:
                {
                    var content = t.Labels ? (UIElement)LabelContent(w, 22, 12) : Img(w.Icon, 26);
                    var bg = active ? V((0, C(0xFFFFFF, 90)), (0.5, C(0xFFFFFF, 40)), (1, C(0xFFFFFF, 60))) : V((0, C(0xFFFFFF, 40)), (1, C(0xFFFFFF, 12)));
                    var b = Click(content, () => ToggleWindow(w), C(0xFFFFFF, 70), bg, new CornerRadius(3), onRight: () => WindowMenu(w));
                    b.Width = t.Labels ? 170 : 58; b.Margin = new Thickness(1, 2, 1, 2);
                    b.BorderBrush = B(0xFFFFFF, active ? (byte)170 : (byte)70); b.BorderThickness = new Thickness(1);
                    if (active) b.Effect = new DropShadowEffect { Color = C(0x9FD8FF), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7 };
                    LiveTip(b, w);
                    return b;
                }
                case BtnStyle.Text:
                {
                    var text = " " + index + ":" + Short(w.AppName ?? w.Title, 14) + (w.Minimized ? "-" : (active ? "*" : " ")) + " ";
                    var b = Click(T(text, t, 12.5, active ? C(0x000000) : t.BarFg, active ? FontWeights.Bold : FontWeights.Normal), () => ToggleWindow(w), t.BtnHover, active ? B(t.Accent) : null, onRight: () => WindowMenu(w));
                    LiveTip(b, w);
                    return b;
                }
                default: // Flat (Windows 8/10, Mint)
                {
                    bool mint = t.Id == "mint";
                    var g = new Grid();
                    g.Children.Add(t.Labels ? (UIElement)LabelContent(w, 18, 12) : Img(w.Icon, 24));
                    var line = new Border { Height = mint ? 2 : 2, VerticalAlignment = mint ? VerticalAlignment.Top : VerticalAlignment.Bottom, Background = B(mint ? t.Accent : Core.Applier.Mix(t.Accent, Colors.White, 0.35)), Margin = new Thickness(active ? 0 : 6, 0, active ? 0 : 6, 0) };
                    g.Children.Add(line);
                    var b = Click(g, () => ToggleWindow(w), t.BtnHover, active ? B(t.BtnActive) : null, onRight: () => WindowMenu(w));
                    b.Width = t.Labels ? 170 : 48;
                    LiveTip(b, w);
                    return b;
                }
            }
        }

        /// <summary>The tooltip always shows the window's current title (the button is not rebuilt for title changes).</summary>
        static void LiveTip(FrameworkElement el, WinInfo w)
        {
            el.ToolTip = w.Title;
            el.ToolTipOpening += (s, e) => { var now = W32.Text(w.Hwnd); if (now.Length > 0) el.ToolTip = now; };
        }

        static string Short(string s, int n) => s.Length <= n ? s.ToLowerInvariant() : s.Substring(0, n).ToLowerInvariant();

        StackPanel LabelContent(WinInfo w, double icon, double font)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            sp.Children.Add(Img(w.Icon, icon));
            sp.Children.Add(new TextBlock { Text = w.Title, FontSize = font, Foreground = B(t.BtnFg), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 });
            return sp;
        }

        // ---------------- dock items (pinned + running) ----------------
        FrameworkElement DockItems(Orientation o, double icon, bool runningOnly = false)
        {
            var panel = new StackPanel { Orientation = o, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            List<AppEntry> pinned;
            string last = null;
            refreshers.Add(() =>
            {
                pinned = AppCatalog.Pinned();   // cached by the catalog; fresh after pinning or unpinning
                var sig = string.Join("|", pinned.Select(a => a.Name + (a.Icon != null))) + "#" + string.Join("|", tracker.Windows.Select(w => w.Hwnd + ":" + w.Minimized + ":" + (w.Hwnd == tracker.Foreground) + ":" + (w.Icon != null)));
                if (sig == last) return;
                last = sig;
                panel.Children.Clear();
                var used = new HashSet<IntPtr>();
                foreach (var app in pinned)
                {
                    var wins = tracker.Windows.Where(w => app.Matches(w)).ToList();
                    foreach (var w in wins) used.Add(w.Hwnd);
                    bool active = wins.Any(w => w.Hwnd == tracker.Foreground && !w.Minimized);
                    var a = app;
                    if (runningOnly) continue;
                    var tile = DockTile(app.Icon, null, app.Name, wins.Count > 0, active, () => { if (wins.Count == 0) a.Start(); else ToggleGroup(wins); }, icon, Colors.Gray, o == Orientation.Vertical);
                    AttachExtras(tile, () => wins, a, null);
                    panel.Children.Add(tile);
                }
                foreach (var grp in tracker.Windows.Where(w => !used.Contains(w.Hwnd)).GroupBy(w => w.Key))
                {
                    var wins = grp.ToList();
                    bool active = wins.Any(w => w.Hwnd == tracker.Foreground && !w.Minimized);
                    var tile = DockTile(wins[0].Icon, null, wins[0].Title, true, active, () => ToggleGroup(wins), icon, Colors.Gray, o == Orientation.Vertical, wins[0]);
                    AttachExtras(tile, () => wins, null, wins[0]);
                    panel.Children.Add(tile);
                }
            });
            return panel;
        }

        void ToggleGroup(List<WinInfo> wins)
        {
            var fg = wins.FirstOrDefault(w => w.Hwnd == tracker.Foreground && !W32.IsIconic(w.Hwnd));
            if (fg != null)
            {
                int i = wins.IndexOf(fg);
                if (wins.Count > 1) W32.Activate(wins[(i + 1) % wins.Count].Hwnd);
                else W32.Minimize(fg.Hwnd);
            }
            else W32.Activate(wins[0].Hwnd);
        }

        FrameworkElement DockTile(ImageSource icon, string glyph, string name, bool running, bool active, Action click, double size, Color glyphBg, bool vertical = false, WinInfo win = null)
        {
            var g = new Grid { Width = size + 10, Height = size + (vertical ? 10 : 12) };
            FrameworkElement img;
            if (icon == null && glyph == null) { glyph = ""; glyphBg = C(0x3A3D44); } // window without its own icon
            if (icon != null) img = Img(icon, size * 0.86);
            else img = new Border { Width = size * 0.86, Height = size * 0.86, CornerRadius = new CornerRadius(size * 0.22), Background = V((0, Core.Applier.Mix(glyphBg, Colors.White, 0.25)), (1, glyphBg)), Child = glyph == "dots" ? Dots(size * 0.38, Colors.White) : (UIElement)Glyph(glyph, size * 0.4, Colors.White) };
            img.RenderTransformOrigin = vertical ? new Point(0, 0.5) : new Point(0.5, 1);
            var scale = new ScaleTransform(1, 1);
            img.RenderTransform = scale;
            img.VerticalAlignment = VerticalAlignment.Center;
            img.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(img);
            if (running)
            {
                Color dotColor = t.Id == "zen" ? ZenStyle.Koi : C(0xFFFFFF);
                var dot = new Border
                {
                    Width = vertical ? 4 : (active ? 14 : 5), Height = vertical ? (active ? 14 : 5) : 4, CornerRadius = new CornerRadius(2), Background = B(dotColor),
                    VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom, HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                    Margin = new Thickness(vertical ? -2 : 0, 0, 0, vertical ? 0 : 0),
                };
                g.Children.Add(dot);
            }
            var b = new Border { Child = g, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name, CornerRadius = new CornerRadius(t.Id == "zen" ? 14 : 8) };
            if (active) b.Background = B(t.BtnActive.A > 0 ? A(t.BtnActive, t.BtnActive.A) : C(0xFFFFFF, 30));
            const bool magnify = false;
            b.MouseEnter += (s, e) =>
            {
                double to = magnify ? 1.35 : 1.0;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
                if (!magnify) b.Background = B(t.BtnHover);
            };
            b.MouseLeave += (s, e) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                if (!magnify) b.Background = active ? B(t.BtnActive.A > 0 ? t.BtnActive : C(0xFFFFFF, 30)) : Brushes.Transparent;
            };
            b.MouseLeftButtonUp += (s, e) => click();
            b.Margin = vertical ? new Thickness(4, 1, 4, 1) : new Thickness(1, 0, 1, 0);
            return b;
        }

        // ---------------- tray: the look's own symbols and panels (no Windows flyouts) ----------------
        void OpenFlyout(string kind, FrameworkElement el)
        {
            HoverPreview.CloseNow();
            var bar = el != null ? Window.GetWindow(el) as BarWindow : null;
            var r = bar != null && el.IsLoaded ? bar.ScreenRectOf(el) : new Rect(0, 0, 0, 0);
            Flyouts.Open(t, kind, r);
        }

        FrameworkElement Tray()
        {
            bool seven = t.Id == "win7";
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
            Color fg = t.BarFg, hover = t.BtnHover;
            var radius = new CornerRadius(seven ? 2 : 15);
            Border Item(UIElement child, string kind, string tip, double padX = 7)
            {
                Border b = null;
                b = Click(child, () => OpenFlyout(kind, b), hover, radius: radius, pad: new Thickness(padX, seven ? 0 : 6, padX, seven ? 0 : 6));
                if (!seven) b.VerticalAlignment = VerticalAlignment.Center;
                b.ToolTip = tip;
                sp.Children.Add(b);
                return b;
            }

            if (!seven)
            {
                // Zen: the focus timer only while it runs, and the lotus that opens the calm panel
                var timer = ZenStyle.Text("", 13, fg, FontWeights.SemiBold);
                var timerBtn = Item(timer, "zen", De ? "Fokus-Timer" : "Focus timer", 9);
                timerBtn.Visibility = Visibility.Collapsed;
                Item(Glyph("", 15, fg), "zen", De ? "Ruhe: Fokus, Atmen, Klang, Nicht stören" : "Calm: focus, breathing, sound, do not disturb", 8);
                if (!preview) clockUpdaters.Add(() => { Zen.Tick(); timer.Text = Zen.TimerText; timerBtn.Visibility = Zen.Running ? Visibility.Visible : Visibility.Collapsed; });
            }

            var net = new TrayGlyph(TrayGlyph.Kind.Wifi, seven, fg) { Level = 4 };
            var netBtn = Item(net, "net", De ? "Netzwerk" : "Network");
            var vol = new TrayGlyph(TrayGlyph.Kind.Sound, seven, fg) { Level = 60 };
            var volBtn = Item(vol, "vol", De ? "Lautstärke" : "Volume");
            volBtn.MouseWheel += (s, e) => { Audio.Change(e.Delta > 0 ? 2 : -2); VolumeOsd.Show(t); };
            TrayGlyph bat = null; Border batBtn = null;
            if (SysStats.Battery(out _, out _)) { bat = new TrayGlyph(TrayGlyph.Kind.Battery, seven, fg) { Level = 100 }; batBtn = Item(bat, "bat", De ? "Akku" : "Battery", seven ? 5 : 7); }

            var clockText = new TextBlock { Foreground = B(fg), FontFamily = seven ? new FontFamily("Segoe UI") : ZenStyle.Family, FontSize = seven ? 12 : 13.5, FontWeight = seven ? FontWeights.Normal : FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, LineHeight = seven ? 15 : double.NaN };
            var clockBtn = Item(clockText, "clock", "", seven ? 8 : 10);
            clockBtn.ToolTipOpening += (s, e) => clockBtn.ToolTip = DateTime.Now.ToString(De ? "dddd, d. MMMM yyyy" : "dddd, MMMM d, yyyy", De ? new CultureInfo("de-DE") : new CultureInfo("en-US"));

            int tick = 0;
            clockUpdaters.Add(() =>
            {
                var now = DateTime.Now;
                clockText.Text = seven ? now.ToString("HH:mm") + "\n" + now.ToString(De ? "dd.MM.yyyy" : "M/d/yyyy", CultureInfo.InvariantCulture) : now.ToString("HH:mm");
                if (tick++ % 3 != 0) return;
                // network: Wi-Fi strength, cable, or offline
                var cur = Wlan.Current;
                if (cur != null) { net.Set(TrayGlyph.Kind.Wifi, (int)Math.Ceiling(cur.Signal / 25.0)); netBtn.ToolTip = cur.Ssid + (De ? "\nInternetzugriff" : "\nInternet access"); }
                else if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) { net.Set(TrayGlyph.Kind.Wired, 4); netBtn.ToolTip = De ? "Verbunden über Kabel" : "Connected by cable"; }
                else { net.Set(TrayGlyph.Kind.Offline, 0); netBtn.ToolTip = De ? "Nicht verbunden" : "Not connected"; }
                int l = Audio.Level; bool m = Audio.Muted;
                vol.Set(TrayGlyph.Kind.Sound, Math.Max(0, l), m);
                volBtn.ToolTip = l < 0 ? (De ? "Kein Audiogerät" : "No audio device") : (De ? "Lautsprecher: " : "Speakers: ") + (m ? (De ? "stumm" : "muted") : l + " %");
                var b = Hardware.Battery();
                if (bat != null && b != null)
                {
                    bat.Set(TrayGlyph.Kind.Battery, b.Percent, false, b.Charging || b.Plugged);
                    batBtn.ToolTip = b.Percent + (De ? " % " : "% ") + (b.Charging ? (De ? "– wird geladen" : "– charging") : b.Left.HasValue ? (De ? "– noch " : "– ") + Hardware.Duration(b.Left.Value, De) + (De ? "" : " left") : "");
                }
            });

            if (seven)
            {
                // Windows 7's "show desktop" strip at the very end of the bar
                var desk = new Border { Width = 15, Background = V((0, C(0xFFFFFF, 50)), (1, C(0xFFFFFF, 18))), BorderBrush = B(0xFFFFFF, 90), BorderThickness = new Thickness(1, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = De ? "Desktop anzeigen" : "Show desktop", Margin = new Thickness(6, 0, 0, 0) };
                desk.MouseEnter += (s, e) => desk.Background = V((0, C(0xFFFFFF, 110)), (1, C(0xFFFFFF, 50)));
                desk.MouseLeave += (s, e) => desk.Background = V((0, C(0xFFFFFF, 50)), (1, C(0xFFFFFF, 18)));
                desk.MouseLeftButtonUp += (s, e) => ShellActions.ShowDesktop();
                sp.Children.Add(desk);
            }
            return sp;
        }
    }

    /// <summary>Tiny helper to create ellipses without a lot of code.</summary>
    internal sealed class Ellipse_
    {
        public Brush Fill; public Brush Stroke;
        public System.Windows.Shapes.Ellipse Make(double w, double h = 0, bool top = false)
            => new System.Windows.Shapes.Ellipse
            {
                Width = w, Height = h > 0 ? h : w, Fill = Fill, Stroke = Stroke, StrokeThickness = Stroke != null ? 1 : 0,
                VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = top ? new Thickness(0, 3, 0, 0) : new Thickness(0),
            };
    }

    /// <summary>CPU and memory usage for the terminal status line.</summary>
    internal static class SysStats
    {
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public uint len, load; public ulong total, avail, tp, ap, tv, av, ae; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
        static long li, lk, lu;

        static double lastCpu;
        static DateTime lastCpuAt;

        /// <summary>CPU load since the previous reading (cached, so several callers can share it).</summary>
        public static double Cpu()
        {
            if ((DateTime.Now - lastCpuAt).TotalMilliseconds < 900) return lastCpu;
            lastCpuAt = DateTime.Now;
            GetSystemTimes(out long i, out long k, out long u);
            long di = i - li, dt = (k - lk) + (u - lu);
            li = i; lk = k; lu = u;
            return lastCpu = dt <= 0 ? 0 : Math.Max(0, Math.Min(99, 100.0 * (dt - di) / dt));
        }

        [StructLayout(LayoutKind.Sequential)] struct POWERSTATUS { public byte ac, flag, percent, saver; public int life, full; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out POWERSTATUS s);

        /// <summary>False when the PC has no battery.</summary>
        public static bool Battery(out int percent, out bool charging)
        {
            percent = 0; charging = false;
            if (!GetSystemPowerStatus(out var p) || p.flag == 128 || p.flag == 255 || p.percent > 100) return false;
            percent = p.percent; charging = p.ac == 1;
            return true;
        }

        public static void MemTotals(out ulong total, out ulong avail)
        {
            var m = new MEMSTAT { len = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            GlobalMemoryStatusEx(ref m);
            total = m.total; avail = m.avail;
        }

        public static double Mem()
        {
            var m = new MEMSTAT { len = (uint)Marshal.SizeOf(typeof(MEMSTAT)) };
            GlobalMemoryStatusEx(ref m);
            return Math.Min(99, m.load);
        }
    }
}

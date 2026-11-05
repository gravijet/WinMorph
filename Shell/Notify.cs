using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Notification banners of the hacker look. App notifications are read from the Windows
    /// notification centre (they stay there); Windows' own banners are switched off by the look.
    /// </summary>
    internal static class Notify
    {
        static readonly List<Window> shown = new List<Window>();
        static ShellTheme theme;
        static DispatcherTimer poll;
        static HashSet<uint> seen;
        static bool busy;
        const int MaxShown = 4;

        static string AppKey(string aumid) => @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + aumid;

        /// <summary>Starts watching the notification centre (polling every 2 s: event registration needs a packaged app).</summary>
        public static void Start(ShellTheme t)
        {
            Stop();
            theme = t;
            var ui = Dispatcher.CurrentDispatcher;
            // off the UI thread: waiting there for the permission request could freeze the shell
            Task.Run(() =>
            {
                try
                {
                    var l = UserNotificationListener.Current;
                    if (l.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed && l.RequestAccessAsync().AsTask().Wait(30000) && l.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) return;
                    if (l.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) return;
                    ui.BeginInvoke(new Action(() =>
                    {
                        if (theme != t) return;   // stopped or restarted meanwhile
                        poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) => Poll(l), ui);
                        Poll(l);
                    }));
                }
                catch { }
            });
        }

        /// <summary>Own notices only (Zen, Gaming): banners in the look's colours, Windows' banners stay as they are.</summary>
        public static void Use(ShellTheme t) { Stop(); theme = t; }

        public static void Stop()
        {
            poll?.Stop(); poll = null; seen = null; theme = null;
            foreach (var w in shown.ToList()) try { w.Close(); } catch { }
            shown.Clear();
        }

        static void Poll(UserNotificationListener l)
        {
            if (busy || CustomLock.Active) return;
            busy = true;
            var ui = Dispatcher.CurrentDispatcher;
            Task.Run(() =>
            {
                var fresh = new List<(string app, string title, string body)>();
                try
                {
                    var list = l.GetNotificationsAsync(NotificationKinds.Toast).AsTask().Result;
                    var ids = new HashSet<uint>(list.Select(n => n.Id));
                    if (seen != null)
                        foreach (var n in list.Where(n => !seen.Contains(n.Id)))
                        {
                            string aumid = null, app = "app";
                            try { aumid = n.AppInfo.AppUserModelId; app = n.AppInfo.DisplayInfo.DisplayName; } catch { }
                            // Windows still shows its own banner for apps the look did not switch off – no double banner
                            if (aumid == null || !(Core.Reg.Get(AppKey(aumid), "ShowBanner") is int b && b == 0)) continue;
                            var bind = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                            var texts = bind?.GetTextElements().Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
                            fresh.Add((app, texts.FirstOrDefault() ?? "", string.Join("\n", texts.Skip(1))));
                        }
                    seen = ids;
                }
                catch { }
                ui.BeginInvoke(new Action(() => { busy = false; foreach (var f in fresh) Show(f.app, f.title, f.body); }));
            });
        }

        /// <summary>Shows a banner at the top right. Also used for the shell's own messages (Wi-Fi, battery, timer).</summary>
        public static void Show(string app, string title, string body = null, double seconds = 6)
        {
            var t = theme;
            if (t == null) return;
            bool term = t.Start == StartKind.Terminal;
            // the hacker look keeps its black terminal banner, the others use their menu colours
            Color g = term ? t.Accent : t.MenuFg, d = term ? t.BarFgDim : t.MenuSub, bg = term ? Colors.Black : Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B);
            var mono = new FontFamily(term ? "Consolas" : t.Font);
            var stack = new StackPanel { MaxWidth = 380 };
            stack.Children.Add(new TextBlock { Text = (term ? "[" + (app ?? "").ToLowerInvariant() + "]  " : (app ?? "") + "  ·  ") + DateTime.Now.ToString("HH:mm"), Foreground = B(d), FontFamily = mono, FontSize = 11.5 });
            if (!string.IsNullOrEmpty(title)) stack.Children.Add(new TextBlock { Text = title, Foreground = B(g), FontFamily = mono, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrEmpty(body)) stack.Children.Add(new TextBlock { Text = body.Length > 300 ? body.Substring(0, 299) + "…" : body, Foreground = B(term ? Core.Applier.Mix(g, Colors.White, 0.35) : Core.Applier.Mix(g, bg, 0.2)), FontFamily = mono, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, Background = B(bg), Cursor = Cursors.Hand,
                Content = new Border { BorderBrush = B(0x2A2A2A), BorderThickness = new Thickness(term ? 1 : 0), Padding = new Thickness(16, 11, 16, 13), Child = stack, MinWidth = 300 },
            };
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE);
                int corner = term ? 1 : 2; W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            close.Tick += (s, e) => { if (w.IsMouseOver) return; close.Stop(); w.Close(); };
            w.MouseLeftButtonUp += (s, e) => { close.Stop(); w.Close(); };
            w.Closed += (s, e) => { close.Stop(); shown.Remove(w); Arrange(); };
            RenderGuard.Watch(w);
            while (shown.Count >= MaxShown) shown[0].Close();
            shown.Add(w);
            w.Show();
            Arrange();
            close.Start();
        }

        static void Arrange()
        {
            var mi = W32.PrimaryMonitor(out double sc);
            double top = mi.rcWork.Top / sc + 8, right = mi.rcWork.Right / sc - 10;
            for (int i = shown.Count - 1; i >= 0; i--)   // newest on top
            {
                var w = shown[i];
                w.UpdateLayout();
                w.Left = right - w.ActualWidth; w.Top = top;
                top += w.ActualHeight + 8;
            }
        }
    }
}

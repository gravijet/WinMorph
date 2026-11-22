using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinMorph.Shell
{
    /// <summary>
    /// --ui-test &lt;look&gt; &lt;surface&gt; &lt;png&gt;: opens one surface of a look on the real screen (with its glass and
    /// shadow, which only the screen shows), photographs the area around it and closes it again. Changes nothing.
    /// </summary>
    internal static class UiTest
    {
        public static void Run(string look, string surface, string png, Action done)
        {
            var t = ShellThemes.For(look) ?? ShellThemes.For("zen");
            Window w;
            WindowTracker tracker = null;
            switch (surface)
            {
                case "menu":
                    w = MacMenu.Show(new System.Collections.Generic.List<MacMenu.Item>
                    {
                        new MacMenu.Item { Text = Core.Loc.De ? "Über diesen PC" : "About This PC" }, MacMenu.Item.Sep,
                        new MacMenu.Item { Text = Core.Loc.De ? "Systemeinstellungen …" : "System Settings…" }, new MacMenu.Item { Text = "App Store …" }, MacMenu.Item.Sep,
                        new MacMenu.Item { Text = Core.Loc.De ? "Kopieren" : "Copy", Shortcut = "⌃C", Checked = true }, new MacMenu.Item { Text = Core.Loc.De ? "Einsetzen" : "Paste", Shortcut = "⌃V", Enabled = false },
                    }, new Point(60, 40), "test");
                    break;
                case "alert":
                    w = MacAlert.Ask(Core.Loc.De ? "Möchtest du deinen Computer jetzt neu starten?" : "Are you sure you want to restart your computer now?", null, MacStyle.Own("finder"), new[] { (Core.Loc.De ? "Abbrechen" : "Cancel", (Action)null, false), (Core.Loc.De ? "Neustart" : "Restart", (Action)null, true) });
                    break;
                case "switcher":
                    tracker = new WindowTracker(t, styleFrames: false);
                    var list = tracker.ByRecentUse().GroupBy(x => x.Key).Select(g => g.First()).ToList();
                    var sw = new Switcher(t, list, Math.Min(1, list.Count - 1));
                    sw.Show();
                    w = sw;
                    break;
                case "banner":
                    w = MacPanels.Banner(new Notify.Item { When = DateTime.Now, App = "WLAN", Title = Core.Loc.De ? "Verbunden mit „Zuhause“" : "Connected to “Home”", Body = Core.Loc.De ? "Das WLAN-Netzwerk ist jetzt verbunden." : "The Wi‑Fi network is now connected." });
                    w.Show();
                    w.UpdateLayout();
                    var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
                    w.Left = mi.rcWork.Right / sc - w.ActualWidth + 6; w.Top = mi.rcWork.Top / sc;
                    MacPanels.Enter(w);
                    break;
                case "spotlight":
                case var sp when sp.StartsWith("spotlight:"):
                    // the apps first (as in the running shell), then the search; files take a moment
                    AppCatalog.Loaded += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var p = surface.Length > 10 ? surface.Substring(10) : null;
                        MacSpotlight.Toggle(p != null && p.StartsWith("#") ? null : p);
                        var tm2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
                        tm2.Tick += (s2, e2) => { tm2.Stop(); var sw2 = MacSpotlight.Current; try { Shoot(sw2, png); } catch (Exception ex) { System.IO.File.WriteAllText(png + ".txt", ex.ToString()); } MacSpotlight.CloseNow(); done(); };
                        tm2.Start();
                    }));
                    AppCatalog.LoadAsync(System.Windows.Application.Current.Dispatcher);
                    return;
                case "stack":
                {
                    var m2 = W32.Monitor(W32.MonitorAtCursor(), out double s2);
                    double cx = (m2.rcWork.Left + m2.rcWork.Right) / 2.0 / s2;
                    MacPanels.Toggle(t, "stack", new Rect(cx - 27, m2.rcWork.Bottom / s2 - 64, 54, 54), null);
                    w = MacPanels.Open;
                    break;
                }
                case "hud":
                    MacPanels.VolumeHud();
                    w = MacPanels.Hud;
                    break;
                default:
                    w = Flyouts.Open(t, surface, new Rect(0, 0, 0, 0));
                    break;
            }
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            tm.Tick += (s, e) =>
            {
                tm.Stop();
                try { Shoot(w, png); } catch (Exception ex) { System.IO.File.WriteAllText(png + ".txt", ex.ToString()); }
                try { w?.Close(); } catch { }
                tracker?.Dispose();
                done();
            };
            tm.Start();
        }

        static void Shoot(Window w, string png)
        {
            var h = new WindowInteropHelper(w).Handle;
            W32.GetWindowRect(h, out var r);
            int pad = 40;
            int x = r.Left - pad, y = r.Top - pad, cw = r.Right - r.Left + 2 * pad, ch = r.Bottom - r.Top + 2 * pad;
            using (var bmp = new System.Drawing.Bitmap(cw, ch))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(cw, ch));
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}

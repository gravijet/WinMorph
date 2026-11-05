using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMorph.Core;

namespace WinMorph
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var a = e.Args;
            Shell.RenderGuard.Start();
            try
            {
                // ---- screen saver mode (WinMorph.scr): the themed lock screen ----
                string a0 = a.Length > 0 ? a[0].ToLowerInvariant() : "";
                if (a0.StartsWith("/s"))
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    // the design shell shows the full WinMorph lock (with its own password line) – hand over to it
                    if (Shell.ShellControl.IsRunning && Shell.ShellControl.RequestLock(2500)) { Shutdown(0); return; }
                    try { Shell.LockScreen.Show(Core.Reg.Get(Shell.ShellControl.SettingsKey, "LastPreset") as string, () => Shutdown(0), lockOnExit: true); }
                    catch (Exception ex) { try { LockGuard.LockNow(); } catch { } File.AppendAllText(Path.Combine(Backup.DataDir, "lockscreen-errors.log"), DateTime.Now + " " + ex + Environment.NewLine); Shutdown(1); }
                    return;
                }
                if (a0.StartsWith("/p")) { Shutdown(0); return; }
                if (a0.StartsWith("/a")) { Shutdown(0); return; }
                if (a.Length >= 1 && (a[0] == "--restore-ui" || a[0] == "--undo-ui"))
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    History.Interactive(undo: a[0] == "--undo-ui");
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 2 && a[0] == "--switcher-demo")
                {
                    // shows the themed Alt+Tab switcher with the real windows for a few seconds (for testing)
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var th = Shell.ShellThemes.For(a[1]);
                    var tr = new Shell.WindowTracker(th, styleFrames: false);
                    var sw = new Shell.Switcher(th, tr.ByRecentUse(), 1);
                    sw.Show();
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                    tm.Tick += (s2, e2) => { tm.Stop(); sw.Close(); tr.Dispose(); Shutdown(0); };
                    tm.Start();
                    return;
                }
                if (a.Length >= 1 && a[0] == "--undo") { History.UndoLast(); Shutdown(0); return; }
                if (a.Length >= 3 && a[0] == "--lockscreen-preview")
                {
                    var bmp = Shell.LockScreen.Render(a[1], 1600, 900, 1);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var fs = File.Create(a[2])) enc.Save(fs);
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 3 && a[0] == "--lock-test")
                {
                    // --lock-test <png1> <png2>: pictures of the WinMorph lock (typing, wrong password); locks nothing
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    Shell.CustomLock.RenderTest(a[1], a[2], () => Shutdown(0));
                    return;
                }
                if (a.Length >= 3 && a[0] == "--lock-design")
                {
                    // --lock-design <presetId> <png>: the picture used for the real Windows lock screen
                    var f = Shell.LockScreen.RenderForWindows(Presets.Find(a[1]));
                    if (f != null) File.Copy(f, a[2], true);
                    Shutdown(f != null ? 0 : 2);
                    return;
                }
                if (a.Length >= 3 && a[0] == "--lockscreen") { Shutdown(Applier.ApplyLockScreen(a[1], a[2])); return; }
                if (a.Length >= 4 && a[0] == "--lock-setup")
                {
                    // elevated, one prompt: --lock-setup <user sid | -> <sign-in picture | -> <backup file>
                    int rc = 0;
                    if (a[1] != "-") rc |= LockGuard.SetupElevated(a[1]);
                    rc |= a[2] != "-" ? Applier.ApplyLockScreen(a[2], a[3]) : Applier.ClearLockScreen(a[3]);
                    Shutdown(rc);
                    return;
                }
                if (a.Length >= 2 && a[0] == "--restore-hklm")
                {
                    var r = Restorer.Run(new Backup(a[1]), machineOnly: true, restartExplorer: false);
                    Shutdown(r.Failed == 0 ? 0 : 1);
                    return;
                }
                if (a.Length >= 1 && a[0] == "--restore") { History.RestoreOriginal(); Shutdown(0); return; }
                if (a.Length >= 2 && a[0] == "--apply")
                {
                    var p = Presets.Find(a[1]);
                    if (p == null) { Shutdown(2); return; }
                    var b = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
                    var o = ApplyOptions.For(p, Presets.Parts(p.Id));
                    new Applier(b, Loc.De).Apply(p, o);
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 3 && a[0] == "--app-preview")
                {
                    // --app-preview <tasks|files|games> <png>: screenshot of an own app window (docs/testing)
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var th = Shell.ShellThemes.For(a[1] == "games" ? "gaming" : "terminal");
                    if (a[1] == "tasks") Shell.HackerTasks.Open(th);
                    else if (a[1] == "files") Shell.HackerFiles.Open(th, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    else Shell.Gaming.Library(th);
                    var shot = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    shot.Tick += (s2, e2) =>
                    {
                        shot.Stop();
                        var win = System.Linq.Enumerable.Last(System.Linq.Enumerable.Cast<Window>(Windows));
                        var root = (FrameworkElement)win.Content;
                        var bmp = new RenderTargetBitmap((int)root.ActualWidth * 2, (int)root.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
                        var bg = new DrawingVisual();
                        using (var dc = bg.RenderOpen()) dc.DrawRectangle(win.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        bmp.Render(bg); bmp.Render(root);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        using (var fs = File.Create(a[2])) enc.Save(fs);
                        Shutdown(0);
                    };
                    shot.Start();
                    return;
                }
                if (a.Length >= 3 && a[0] == "--shell-preview")
                {
                    // --shell-preview <presetId> <png> [menu]
                    var p = Presets.Find(a[1]);
                    var th = Shell.ShellThemes.For(a[1]);
                    if (p == null || th == null) { Shutdown(2); return; }
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var host = new Shell.ShellHost(th, preview: true);
                    Shell.AppCatalog.LoadAsync(Dispatcher);
                    bool menu = a.Length > 3 && a[3] == "menu";
                    Shell.AppCatalog.Loaded += () => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var bmp = host.RenderPreview(p, 1600, 900, menu);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        using (var fs = File.Create(a[2])) enc.Save(fs);
                        Shutdown(0);
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    return;
                }
                if (a.Length >= 1 && a[0] == "--live") { Live.LiveWall.RunHost(this); return; }
                if (a.Length >= 1 && a[0] == "--live-stop") { Live.LiveWall.Stop(); Shutdown(0); return; }
                if (a.Length >= 2 && a[0] == "--live-set")
                {
                    // --live-set <catalog id | off>: sets a live wallpaper (with backup), for testing and scripts
                    var it = Live.WallCatalog.Find(a[1]);
                    var b = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
                    if (it == null) { Live.LiveWall.Apply(b, "", null); Shutdown(0); return; }
                    if (it.IsVideo && !it.Downloaded) System.Threading.Tasks.Task.Run(() => Live.WallCatalog.EnsureDownloaded(it, null)).Wait();   // off the UI thread, or the download deadlocks
                    var mi = Shell.W32.PrimaryMonitor(out _);
                    var poster = Live.LiveWall.Poster(it, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                    Live.LiveWall.Apply(b, it.Spec, poster);
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 3 && a[0] == "--live-preview")
                {
                    // --live-preview <scene id> <png>: still picture of a scene
                    var bmp = Live.LiveView.Snapshot(a[1], 1600, 900, 4);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var fs = File.Create(a[2])) enc.Save(fs);
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 1 && a[0] == "--shell") { Shell.ShellControl.RunShell(this); return; }
                if (a.Length >= 1 && a[0] == "--shell-stop") { Shell.ShellControl.Stop(); Shutdown(0); return; }
                if (a.Length >= 2 && a[0] == "--selftest") { Shutdown(SelfTest.Run(a[1])); return; }
                if (a.Length >= 1 && a[0] == "--post-install") { Installer.PostInstall(desktopShortcut: Array.IndexOf(a, "desktop") > 0); Shutdown(0); return; }
                if (a.Length >= 1 && a[0] == "--uninstall") { ShutdownMode = ShutdownMode.OnExplicitShutdown; Installer.Uninstall(); Shutdown(0); return; }
                if (a.Length >= 2 && a[0] == "--wifi-list")
                {
                    // self-check of the native Wi-Fi list (changes nothing)
                    Shell.Wlan.Scan(); System.Threading.Thread.Sleep(4000);
                    File.WriteAllLines(a[1], System.Linq.Enumerable.Select(Shell.Wlan.Networks(), n => n.Ssid + "\t" + n.Signal + "%\tauth " + n.Auth + "\tsecured " + n.Secured + "\tprofile " + n.Profile + "\tconnected " + n.Connected));
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 2 && a[0] == "--make-icon") { MakeIcon(a[1]); Shutdown(0); return; }
                if (a.Length >= 2 && a[0] == "--render-walls")
                {
                    Directory.CreateDirectory(a[1]);
                    foreach (var p in Presets.All) WallpaperArt.RenderToFile(p, Path.Combine(a[1], p.Id + ".png"), 1600, 900);
                    Shutdown(0);
                    return;
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(Backup.DataDir, "cli-errors.log"), DateTime.Now + " " + string.Join(" ", a) + Environment.NewLine + ex + Environment.NewLine); } catch { }
                if (a.Length == 0 || a[0] == "--restore-ui" || a[0] == "--undo-ui") MessageBox.Show(ex.Message, "WinMorph");
                Shutdown(1);
                return;
            }

            // never close the app because of one failing preview or card: log it and carry on
            DispatcherUnhandledException += (s, ev) =>
            {
                try { File.AppendAllText(Path.Combine(Backup.DataDir, "ui-errors.log"), DateTime.Now + " " + ev.Exception + Environment.NewLine); } catch { }
                ev.Handled = true;
            };
            var w = new MainWindow();
            MainWindow = w;
            if (a.Length >= 2 && a[0] == "--screenshot") w.ScreenshotMode(a[1], a.Length > 2 ? a[2] : null, a.Length > 3 && a[3] == "de");
            w.Show();
        }

        /// <summary>Draws the app icon and saves it as a PNG-based .ico.</summary>
        static void MakeIcon(string path)
        {
            var sizes = new[] { 256, 64, 48, 32, 16 };
            var pngs = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var bg = new LinearGradientBrush(Color.FromRgb(0x7C, 0x8C, 0xFF), Color.FromRgb(0x9B, 0x4D, 0xFF), 45);
                    dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), s * 0.22, s * 0.22);
                    double q = s * 0.2, g = s * 0.05, x0 = s * 0.28, y0 = s * 0.28;
                    var w = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
                    var w2 = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                    dc.DrawRoundedRectangle(w, null, new Rect(x0, y0, q, q), q * 0.25, q * 0.25);
                    dc.DrawEllipse(w2, null, new Point(x0 + q * 1.5 + g, y0 + q / 2), q / 2, q / 2);
                    dc.DrawEllipse(w2, null, new Point(x0 + q / 2, y0 + q * 1.5 + g), q / 2, q / 2);
                    dc.DrawRoundedRectangle(w, null, new Rect(x0 + q + g, y0 + q + g, q, q), q * 0.5, q * 0.5);
                }
                var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var ms = new MemoryStream()) { enc.Save(ms); pngs[i] = ms.ToArray(); }
            }
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                    bw.Write(pngs[i].Length); bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) bw.Write(p);
            }
        }
    }
}

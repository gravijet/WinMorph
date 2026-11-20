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
            int language = Array.IndexOf(a, "--lang");
            if (language >= 0 && language + 1 < a.Length) Loc.De = a[language + 1] == "de";
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
                    // it moves on to the next window every 1.5 s, so the motion can be watched
                    int ticks = 0;
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    tm.Tick += (s2, e2) => { if (++ticks >= 5) { tm.Stop(); sw.Close(); tr.Dispose(); Shutdown(0); } else sw.Move(1); };
                    tm.Start();
                    return;
                }
                if (a.Length >= 1 && a[0] == "--undo") { History.UndoLast(); Shutdown(0); return; }
                if (a.Length >= 3 && a[0] == "--lockscreen-preview")
                {
                    var bmp = Shell.LockScreen.Render(a[1], 1600, 900, 2, still: true);
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
                if (a.Length >= 3 && a[0] == "--lock-setup")
                {
                    // elevated, one prompt: --lock-setup <user sid | -> <backup file>
                    // lets Win+L show the WinMorph lock screen and removes an all-users picture of older versions
                    int rc = 0;
                    if (a[1] != "-") rc |= LockGuard.SetupElevated(a[1]);
                    rc |= Applier.ClearLockScreen(a[2]);
                    Shutdown(rc);
                    return;
                }
                if (a.Length >= 2 && a[0] == "--restore-hklm")
                {
                    var r = Restorer.Run(new Backup(a[1]), machineOnly: true, restartExplorer: false);
                    Shutdown(r.Failed == 0 ? 0 : 1);
                    return;
                }
                if (a.Length >= 1 && a[0] == "--restore") { Shutdown(History.RestoreOriginal().Complete ? 0 : 1); return; }
                if (a.Length >= 2 && a[0] == "--apply")
                {
                    var p = Presets.Find(a[1]);
                    if (p == null) { Shutdown(2); return; }
                    var b = new Backup(Backup.DefaultFile) { Mirror = Backup.NewStep() };
                    new Applier(b, Loc.De).Apply(p, ApplyOptions.For(p));
                    Shutdown(0);
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
                        var bmp = host.RenderPreview(p, 1600, 900, menu, 2);
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
                    var bmp = Live.LiveView.Snapshot(a[1], 3200, 1800, 4, fullResolution: true);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var fs = File.Create(a[2])) enc.Save(fs);
                    Shutdown(0);
                    return;
                }
                if (a.Length >= 1 && a[0] == "--shell") { Shell.ShellControl.RunShell(this); return; }
                if (a.Length >= 1 && a[0] == "--shell-stop") { Shell.ShellControl.Stop(); Shutdown(0); return; }
                if (a.Length >= 2 && a[0] == "--selftest") { Shutdown(SelfTest.Run(a[1])); return; }
                if (a.Length >= 1 && a[0] == "--post-install") { Installer.PostInstall(); Shutdown(0); return; }
                if (a.Length >= 1 && a[0] == "--uninstall-cleanup") { Installer.Cleanup(); Shutdown(0); return; }
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
                    foreach (var p in Presets.All) WallpaperArt.RenderToFile(p, Path.Combine(a[1], p.Id + ".png"), 3840, 2160);
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
            SelfHeal();
            var w = new MainWindow();
            MainWindow = w;
            if (a.Length >= 2 && a[0] == "--screenshot") w.ScreenshotMode(a[1], a.Length > 2 ? a[2] : null, a.Length > 3 && a[3] == "de");
            w.Show();
        }

        /// <summary>
        /// Opening the app repairs what a crash or an old version may have left: a chosen look whose shell
        /// does not run is started again; a taskbar that is still taken without a look is given back.
        /// </summary>
        static void SelfHeal()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var k = Reg.Open(Shell.ShellControl.SettingsKey, true)) { k?.DeleteValue("MacDark", false); k?.DeleteSubKeyTree("Parts", false); }
                    if (Shell.ShellControl.IsRunning) return;
                    var look = Shell.ShellControl.CurrentPreset;
                    if (look != null && Shell.ShellThemes.For(look) != null) Shell.ShellControl.Start();
                    else if (Shell.ShellControl.TaskbarTaken || Shell.ShellControl.NativeHidden) Shell.ShellControl.Release();
                }
                catch (Exception ex) { try { File.AppendAllText(Path.Combine(Backup.DataDir, "cli-errors.log"), DateTime.Now + " self-heal " + ex + Environment.NewLine); } catch { } }
            });
        }

        static void MakeIcon(string path)
        {
            var resource = GetResourceStream(new Uri("pack://application:,,,/WinMorph.ico"));
            using (var source = resource.Stream)
            using (var target = File.Create(path)) source.CopyTo(target);
        }
    }
}

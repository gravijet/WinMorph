using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinMorph.Shell;

namespace WinMorph.Live
{
    /// <summary>
    /// The live wallpaper process (WinMorph.exe --live). It draws behind the desktop icons,
    /// pauses itself whenever a window covers the screen or the PC is locked (no lag in
    /// games), and everything it changes is recorded for "undo" and "restore original".
    /// </summary>
    public static class LiveWall
    {
        public const string RunValue = "WinMorph Live";
        const string MutexName = "WinMorphLive_v1", ExitName = "WinMorphLiveExit_v1", ReloadName = "WinMorphLiveReload_v1";
        static string Exe => Process.GetCurrentProcess().MainModule.FileName;

        public static string Spec => Core.Reg.Get(ShellControl.SettingsKey, "LiveWall") as string;

        public static bool IsRunning
        {
            get { try { using (Mutex.OpenExisting(MutexName)) return true; } catch { return false; } }
        }

        public static void Stop()
        {
            try { if (EventWaitHandle.TryOpenExisting(ExitName, out var ev)) { ev.Set(); ev.Dispose(); } } catch { }
            for (int i = 0; i < 40 && IsRunning; i++) Thread.Sleep(100);
        }

        public static void Start()
        {
            if (string.IsNullOrEmpty(Spec)) return;
            if (IsRunning) { Reload(); return; }
            var exe = Exe;
            if (exe.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(exe, "--live") { UseShellExecute = false }); } catch { }
        }

        static void Reload()
        {
            try { if (EventWaitHandle.TryOpenExisting(ReloadName, out var ev)) { ev.Set(); ev.Dispose(); } } catch { }
        }

        /// <summary>
        /// Sets (or with an empty spec removes) the live wallpaper. The previous values go into
        /// the backup first. posterFile becomes the normal wallpaper picture (also used when
        /// the live wallpaper is paused or not running).
        /// </summary>
        public static void Apply(Core.Backup bk, string spec, string posterFile, bool start = true)
        {
            bk.Reg(ShellControl.SettingsKey, "LiveWall");
            bk.Reg(ShellControl.RunKey, RunValue);
            if (posterFile != null && File.Exists(posterFile))
            {
                Core.Applier.RecordWallpaper(bk);
                using (var k = Core.Reg.Create(@"HKCU\Control Panel\Desktop")) { k.SetValue("WallpaperStyle", "10"); k.SetValue("TileWallpaper", "0"); }
                Core.Native.SystemParametersInfo(Core.Native.SPI_SETDESKWALLPAPER, 0, posterFile, Core.Native.SPIF_UPDATE);
            }
            bk.Save();
            if (string.IsNullOrEmpty(spec))
            {
                Stop();
                using (var k = Core.Reg.Open(ShellControl.SettingsKey, true)) k?.DeleteValue("LiveWall", false);
                using (var k = Core.Reg.Open(ShellControl.RunKey, true)) k?.DeleteValue(RunValue, false);
                return;
            }
            using (var k = Core.Reg.Create(ShellControl.SettingsKey)) k.SetValue("LiveWall", spec, RegistryValueKind.String);
            using (var k = Core.Reg.Create(ShellControl.RunKey)) k.SetValue(RunValue, "\"" + Exe + "\" --live", RegistryValueKind.String);
            if (start) Start();
        }

        /// <summary>Picture of the wallpaper as a file (the normal Windows wallpaper while live is active).</summary>
        public static string Poster(WallItem it, int w, int h)
        {
            try
            {
                var hc = ShellThemes.HackerColor;   // the hacker scenes follow the chosen colour
                var file = Path.Combine(Core.Backup.DataDir, "live-" + it.Id + "-" + w + "x" + h + "-" + hc.R.ToString("X2") + hc.G.ToString("X2") + hc.B.ToString("X2") + ".png");
                if (File.Exists(file)) return file;
                var bmp = LiveView.Snapshot(it.Scene ?? "matrix", w, h, 4);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(file)) enc.Save(fs);
                return file;
            }
            catch { return null; }
        }

        // =====================================================================
        // inside the --live process
        // =====================================================================
        [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref W32.POINT p);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out W32.RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        sealed class Host { public HwndSource Src; public IntPtr Hwnd; public IPausable View; public IntPtr Mon; public W32.RECT Rect; }
        static readonly List<Host> hosts = new List<Host>();
        static IntPtr parent;
        static Mutex mutex;
        static int monitorCount;

        public static void RunHost(Application app)
        {
            mutex = new Mutex(true, MutexName, out bool first);
            if (!first) { app.Shutdown(0); return; }
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var ui = Dispatcher.CurrentDispatcher;
            var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitName);
            var reload = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadName);
            Action quit = () => { Teardown(); RefreshDesktop(); app.Shutdown(0); };
            new Thread(() => { exit.WaitOne(); ui.BeginInvoke(quit); }) { IsBackground = true }.Start();
            new Thread(() => { while (true) { reload.WaitOne(); ui.BeginInvoke(new Action(() => { Teardown(); if (string.IsNullOrEmpty(Spec)) quit(); else Build(); })); } }) { IsBackground = true }.Start();
            app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
            app.SessionEnding += (s, e) => Teardown();

            Build();
            new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (s, e) => UpdatePause(), ui);
            new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) =>
            {
                bool broken = hosts.Count == 0 || !W32.IsWindow(parent) || W32.Monitors().Count != monitorCount;
                foreach (var h in hosts) if (!W32.IsWindow(h.Hwnd)) broken = true;
                if (broken) { Teardown(); Build(); }
                // the design shell was killed: give Win+L back to Windows
                if (Core.LockGuard.Armed && !ShellControl.IsRunning) Core.LockGuard.Disarm();
            }, ui);
        }

        static void Build()
        {
            var spec = Spec;
            if (string.IsNullOrEmpty(spec)) return;
            parent = FindHost(out var insertAfter);
            if (parent == IntPtr.Zero) return;
            var mons = W32.Monitors();
            monitorCount = mons.Count;
            for (int i = 0; i < mons.Count; i++)
            {
                try
                {
                    var mi = W32.Monitor(mons[i], out _);
                    var r = mi.rcMonitor;
                    var tl = new W32.POINT { X = r.Left, Y = r.Top };
                    ScreenToClient(parent, ref tl);
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    if (spec.StartsWith("scene:") || spec.StartsWith("video:") || spec.StartsWith("image:"))
                    {
                        // painted with GDI into a native window: independent of WPF/GPU, very light,
                        // and visible on every Windows version (WPF windows inside the desktop are not on 24H2+)
                        var id = spec.StartsWith("scene:") ? LiveView.Arg(spec) : spec;
                        var painter = new DesktopPainter(parent, insertAfter, tl.X, tl.Y, id, r.Left, r.Top, w, h, hud: i == 0 && (id == "matrix-hud" || id == "hacker-desk"));
                        hosts.Add(new Host { Hwnd = painter.Hwnd, View = painter, Mon = mons[i], Rect = r });
                        continue;
                    }
                    var p = new HwndSourceParameters("WinMorphLiveWall")
                    {
                        ParentWindow = parent, WindowStyle = 0x40000000 | 0x10000000 | 0x04000000, // WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS
                        PositionX = tl.X, PositionY = tl.Y, Width = w, Height = h,
                        UsesPerPixelOpacity = insertAfter != IntPtr.Zero,   // layered child inside Progman (24H2+)
                    };
                    var src = new HwndSource(p);
                    if (RenderGuard.Software && src.CompositionTarget != null) src.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
                    double s = src.CompositionTarget?.TransformToDevice.M11 ?? 1;
                    var lv = new LiveView(spec, mouse: MouseMode.Desktop) { Width = w / s, Height = h / s };
                    src.RootVisual = lv;
                    IPausable view = lv;
                    if (insertAfter != IntPtr.Zero) W32.SetWindowPos(src.Handle, insertAfter, tl.X, tl.Y, w, h, 0x0010);
                    else W32.SetWindowPos(src.Handle, IntPtr.Zero, tl.X, tl.Y, w, h, 0x0010 | 0x0004);
                    hosts.Add(new Host { Src = src, Hwnd = src.Handle, View = view, Mon = mons[i], Rect = r });
                }
                catch (Exception ex) { Log(ex); }
            }
        }

        static void Teardown()
        {
            foreach (var h in hosts) { try { (h.View as IDisposable)?.Dispose(); if (h.Src != null) { h.Src.RootVisual = null; h.Src.Dispose(); } } catch { } }
            hosts.Clear();
        }

        /// <summary>The window behind the desktop icons (WorkerW), also on Windows 11 24H2 and newer.</summary>
        static IntPtr FindHost(out IntPtr insertAfter)
        {
            insertAfter = IntPtr.Zero;
            var progman = W32.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            W32.SendMessageTimeout(progman, 0x052C, (IntPtr)0xD, (IntPtr)1, 0, 1000, out _);
            W32.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
            IntPtr worker = IntPtr.Zero;
            // Windows 11 24H2 and newer: the desktop icons (SHELLDLL_DefView) and the wallpaper (WorkerW)
            // are both children of Progman; our window goes between them.
            var defView = W32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero && W32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero)
            {
                insertAfter = defView;
                return progman;
            }
            W32.EnumWindows((h, l) =>
            {
                if (W32.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    var w = W32.FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                    if (w != IntPtr.Zero) worker = w;
                }
                return true;
            }, IntPtr.Zero);
            if (worker == IntPtr.Zero) worker = W32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null); // 24H2+: WorkerW lives inside Progman
            return worker;
        }

        static void UpdatePause()
        {
            if (hosts.Count == 0) return;
            bool locked = true;
            var d = OpenInputDesktop(0, false, 0x0100);
            if (d != IntPtr.Zero) { locked = false; CloseDesktop(d); }
            var covered = new HashSet<IntPtr>();
            if (!locked)
            {
                uint me = (uint)Process.GetCurrentProcess().Id;
                W32.EnumWindows((h, l) =>
                {
                    if (!W32.IsWindowVisible(h) || W32.IsIconic(h) || W32.IsCloaked(h)) return true;
                    int ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
                    if ((ex & W32.WS_EX_TOOLWINDOW) != 0) return true;
                    var cls = W32.ClassOf(h);
                    if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                    var mon = MonitorFromWindow(h, 0);
                    if (mon == IntPtr.Zero) return true;
                    if (W32.IsZoomed(h)) { covered.Add(mon); return true; }
                    if (GetWindowRect(h, out var r))
                        foreach (var hs in hosts)
                            if (hs.Mon == mon && r.Left <= hs.Rect.Left && r.Top <= hs.Rect.Top && r.Right >= hs.Rect.Right && r.Bottom >= hs.Rect.Bottom) covered.Add(mon);
                    return true;
                }, IntPtr.Zero);
            }
            foreach (var hs in hosts) hs.View.Paused = locked || covered.Contains(hs.Mon);
        }

        static void RefreshDesktop()
        {
            try
            {
                var sb = new StringBuilder(1024);
                Core.Native.SystemParametersInfo(Core.Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0);
                Core.Native.SystemParametersInfo(Core.Native.SPI_SETDESKWALLPAPER, 0, sb.ToString(), Core.Native.SPIF_UPDATE);
            }
            catch { }
        }

        internal static void LogError(Exception ex) => Log(ex);

        static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Core.Backup.DataDir, "live-errors.log"), DateTime.Now + "  " + ex + Environment.NewLine); } catch { }
        }
    }
}

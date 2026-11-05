using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinMorph.Live
{
    public interface IPausable { bool Paused { get; set; } }

    /// <summary>
    /// Draws a scene straight onto the desktop window with GDI. This does not depend on
    /// WPF rendering at all, so it also works when the graphics driver misbehaves, and it
    /// is very light on resources.
    /// </summary>
    public sealed class DesktopPainter : IPausable, IDisposable
    {
        readonly IntPtr hwnd;
        readonly Scene scene;
        readonly int outW, outH;
        readonly uint[] frame;     // scene + HUD
        readonly bool hud;
        readonly DispatcherTimer timer;
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly W32Rect screen;
        double last;
        bool paused, lmb;
        int fps, slow;
        BITMAPINFO bmi;

        struct W32Rect { public int L, T, W, H; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO { public BITMAPINFOHEADER h; public int c0; }
        [StructLayout(LayoutKind.Sequential)] struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public int l, t, r, b; public int fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgb; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr hdc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, uint[] bits, ref BITMAPINFO bmi, uint usage, uint rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr old);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS { public uint style; public WndProcDelegate lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WNDCLASS wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        static bool registered;
        static WndProcDelegate classProc;   // kept alive for the whole process
        static readonly System.Collections.Generic.Dictionary<IntPtr, DesktopPainter> byHwnd = new System.Collections.Generic.Dictionary<IntPtr, DesktopPainter>();

        public IntPtr Hwnd => hwnd;

        /// <summary>Creates the child window inside parent (clientX/Y = position in the parent) and starts painting.</summary>
        public DesktopPainter(IntPtr parent, IntPtr insertAfter, int clientX, int clientY, string sceneId, int screenX, int screenY, int w, int h, bool hud)
        {
            if (!registered)
            {
                classProc = StaticProc;
                var wc = new WNDCLASS { lpfnWndProc = classProc, hInstance = GetModuleHandle(null), lpszClassName = "WinMorphLiveGdi" };
                RegisterClass(ref wc);
                registered = true;
            }
            int ex = insertAfter != IntPtr.Zero ? 0x00080000 : 0;   // layered inside Progman (Windows 11 24H2+)
            hwnd = CreateWindowEx(ex | 0x08000000, "WinMorphLiveGdi", "WinMorphLiveWall", 0x40000000 | 0x10000000 | 0x04000000, clientX, clientY, w, h, parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            if (ex != 0) SetLayeredWindowAttributes(hwnd, 0, 255, 2);
            if (insertAfter != IntPtr.Zero) Shell.W32.SetWindowPos(hwnd, insertAfter, clientX, clientY, w, h, 0x0010);
            byHwnd[hwnd] = this;
            this.hud = hud;
            outW = w; outH = h;
            screen = new W32Rect { L = screenX, T = screenY, W = w, H = h };
            scene = Scene.Create(sceneId);
            double k = Math.Min(1, 1920.0 / Math.Max(1, w)) * scene.Resolution;
            scene.Setup((int)(w * k), (int)(h * k));
            frame = hud ? new uint[scene.W * scene.H] : null;
            bmi = new BITMAPINFO { h = new BITMAPINFOHEADER { biSize = 40, biWidth = scene.W, biHeight = -scene.H, biPlanes = 1, biBitCount = 32 } };
            if (hud) InitHud();
            fps = scene.Fps;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / fps) };
            timer.Tick += (s, e) => Frame();
            timer.Start();
        }

        public bool Paused
        {
            get => paused;
            set { if (paused == value) return; paused = value; scene.Pause(paused); if (paused) timer.Stop(); else { last = sw.Elapsed.TotalSeconds; timer.Start(); } }
        }

        static IntPtr StaticProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == 0x0014) return (IntPtr)1;                                       // WM_ERASEBKGND
            if (msg == 0x000F && byHwnd.TryGetValue(h, out var me))                    // WM_PAINT
            {
                var dc = BeginPaint(h, out var ps);
                me.Blit(dc);
                EndPaint(h, ref ps);
                return IntPtr.Zero;
            }
            if (msg == 0x0021) return (IntPtr)3;                                       // WM_MOUSEACTIVATE: never take focus
            return DefWindowProc(h, msg, w, l);
        }

        void Frame()
        {
            double now = sw.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - last);
            last = now;
            var t0 = sw.Elapsed.TotalMilliseconds;
            try
            {
                ReadMouse();
                scene.T += dt;
                scene.Step(dt);
                if (hud) { Array.Copy(scene.Px, frame, frame.Length); DrawHud(); }
                var dc = GetDC(hwnd);
                Blit(dc);
                ReleaseDC(hwnd, dc);
            }
            catch (Exception ex) { LiveWallLog(ex); }
            double cost = sw.Elapsed.TotalMilliseconds - t0;
            if (cost > 1000.0 / fps * 0.7) slow++; else slow = Math.Max(0, slow - 1);
            if (slow > 40 && fps > 15) { fps = fps > 24 ? 24 : 15; timer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps); slow = 0; }
        }

        void Blit(IntPtr dc)
        {
            if (dc == IntPtr.Zero) return;
            bool scaled = scene.W != outW || scene.H != outH;
            if (scaled) { SetStretchBltMode(dc, scene.Resolution < 0.9 ? 4 : 3); SetBrushOrgEx(dc, 0, 0, IntPtr.Zero); }  // HALFTONE for soft scenes
            StretchDIBits(dc, 0, 0, outW, outH, 0, 0, scene.W, scene.H, hud ? frame : scene.Px, ref bmi, 0, 0x00CC0020);
        }

        void ReadMouse()
        {
            GetCursorPos(out var p);
            var sb = new StringBuilder(64);
            GetClassName(WindowFromPoint(p), sb, 64);
            var c = sb.ToString();
            bool desk = c == "SysListView32" || c == "SHELLDLL_DefView" || c == "WorkerW" || c == "Progman" || c.StartsWith("HwndWrapper");
            bool down = (GetAsyncKeyState(1) & 0x8000) != 0;
            int x = p.X - screen.L, y = p.Y - screen.T;
            if (!desk || x < 0 || y < 0 || x >= screen.W || y >= screen.H) { scene.Mx = scene.My = -1; lmb = down; return; }
            scene.Mx = x * (double)scene.W / screen.W;
            scene.My = y * (double)scene.H / screen.H;
            if (down && !lmb) scene.Click = true;
            lmb = down;
        }

        // ---------------- HUD drawn into the picture (system monitor, like conky) ----------------
        GlyphAtlas small, big;
        string[] lines = new string[0];
        string clock = "", date = "";
        DateTime nextInfo;
        string ip = "", disks = "", wifi = "";
        int infoTick;

        Color accent; uint edgeColor;

        void InitHud()
        {
            accent = Shell.ShellThemes.HackerColor;
            var e = Core.Applier.Mix(accent, Colors.Black, 0.45);
            edgeColor = (uint)(e.R << 16 | e.G << 8 | e.B);
            const string chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~█░─↓↑äöüÄÖÜß·";
            int px = Math.Max(13, scene.H / 66);
            small = GlyphAtlas.Get(chars, "Consolas", px, false);
            big = GlyphAtlas.Get(" 0123456789:", "Consolas", px * 4, false);
        }

        void UpdateInfo()
        {
            var de = Core.Loc.De;
            var ci = de ? new CultureInfo("de-DE") : new CultureInfo("en-US");
            var now = DateTime.Now;
            clock = now.ToString("HH:mm:ss");
            date = now.ToString(de ? "dddd, dd. MMMM yyyy" : "dddd, MMMM dd yyyy", ci).ToLowerInvariant();
            if (infoTick++ % 10 == 0)
            {
                ip = Shell.SysInfo.LocalIp;
                disks = string.Join("  ", Shell.SysInfo.Disks().Take(3).Select(d => d.Name.TrimEnd('\\') + " " + (100 - d.TotalFreeSpace * 100 / Math.Max(1, d.TotalSize)) + "%"));
            }
            if (infoTick % 10 == 1) { try { var n = Shell.Wlan.Current; wifi = n != null ? n.Ssid + "  " + n.Signal + "%" : (de ? "nicht verbunden" : "not connected"); } catch { wifi = "-"; } }
            double cpu = Shell.SysInfo.CpuSample(), mem = Shell.SysStats.Mem();
            Shell.SysInfo.NetRate(out double rx, out double tx);
            string Bar(double v) => "[" + Shell.SysInfo.Bar(v, 20, '|', '·') + "]";
            string bat = Shell.SysStats.Battery(out int pct, out bool ac) ? Bar(pct) + " " + pct.ToString("00") + "%" + (ac ? (de ? " lädt" : " charging") : "") : null;
            lines = new[]
            {
                Environment.UserName.ToLowerInvariant() + "@" + Environment.MachineName.ToLowerInvariant(),
                new string('─', 30),
                "up     " + Shell.SysInfo.UptimeText,
                "cpu    " + Bar(cpu) + " " + cpu.ToString("00") + "%",
                "mem    " + Bar(mem) + " " + mem.ToString("00") + "%",
                "disk   " + disks,
                "net    ↓" + Shell.SysInfo.Bytes(rx).PadLeft(6) + "/s ↑" + Shell.SysInfo.Bytes(tx).PadLeft(6) + "/s",
                bat != null ? "bat    " + bat : null,
                "wifi   " + (wifi.Length > 30 ? wifi.Substring(0, 29) + "~" : wifi),
                "ip     " + ip,
                "proc   " + SafeProcCount(),
            }.Where(l => l != null).ToArray();
        }

        static int procCount; static DateTime procAt;
        static int SafeProcCount() { if ((DateTime.Now - procAt).TotalSeconds > 5) { procAt = DateTime.Now; try { procCount = Process.GetProcesses().Length; } catch { } } return procCount; }

        void DrawHud()
        {
            if (DateTime.Now >= nextInfo) { nextInfo = DateTime.Now.AddSeconds(1); UpdateInfo(); }
            int pad = small.CW * 2;
            int boxW = Math.Max(big.CW * 8, small.CW * Math.Max(34, Math.Max(date.Length, lines.Length == 0 ? 0 : lines.Max(l => l.Length)) + 1)) + pad * 2;
            int boxH = big.CH + small.CH * (lines.Length + 2) + pad * 2;
            int x0 = scene.W - boxW - scene.W / 32, y0 = scene.H / 14;
            // darkened panel with a thin green frame
            for (int y = y0; y < y0 + boxH && y < scene.H; y++)
            {
                int row = y * scene.W;
                for (int x = Math.Max(0, x0); x < x0 + boxW && x < scene.W; x++)
                {
                    uint v = frame[row + x];
                    bool edge = y == y0 || y == y0 + boxH - 1 || x == x0 || x == x0 + boxW - 1;
                    frame[row + x] = edge ? edgeColor : ((v & 0xFCFCFC) >> 2);
                }
            }
            var green = accent;
            var dim = Core.Applier.Mix(accent, Colors.Black, 0.4);
            int cx = x0 + pad, cy = y0 + pad;
            foreach (var ch in clock) { big.Draw(FrameScene, big.IndexOf(ch), cx, cy, green); cx += big.CW; }
            cy += big.CH;
            cx = x0 + pad;
            foreach (var ch in date) { small.Draw(FrameScene, small.IndexOf(ch), cx, cy, dim); cx += small.CW; }
            cy += small.CH * 2;
            foreach (var line in lines)
            {
                cx = x0 + pad;
                foreach (var ch in line) { small.Draw(FrameScene, small.IndexOf(ch), cx, cy, green); cx += small.CW; }
                cy += small.CH;
            }
        }

        // the glyph drawer works on a Scene; this wrapper points it at the HUD frame
        Scene frameScene;
        Scene FrameScene
        {
            get
            {
                if (frameScene == null) { frameScene = new BufferScene(); frameScene.W = scene.W; frameScene.H = scene.H; }
                frameScene.Px = frame;
                return frameScene;
            }
        }

        sealed class BufferScene : Scene { protected override void Init() { } public override void Step(double dt) { } }

        static void LiveWallLog(Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Core.Backup.DataDir, "live-errors.log"), DateTime.Now + "  " + ex + Environment.NewLine); } catch { }
        }

        public void Dispose()
        {
            timer.Stop();
            (scene as IDisposable)?.Dispose();
            byHwnd.Remove(hwnd);
            try { DestroyWindow(hwnd); } catch { }
        }
    }
}

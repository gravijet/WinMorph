using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinMorph.Shell
{
    public sealed class WinInfo
    {
        public IntPtr Hwnd;
        public string Title, ExePath, AppId;
        public ImageSource Icon;
        public bool Minimized;
        public string Key => AppId ?? ExePath ?? Hwnd.ToString();
        public string AppName => ExePath != null ? FileVersionInfoName(ExePath) : Title;

        static readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string FileVersionInfoName(string path)
        {
            if (names.TryGetValue(path, out var n)) return n;
            try { n = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { }
            if (string.IsNullOrWhiteSpace(n)) n = Path.GetFileNameWithoutExtension(path);
            return names[path] = n;
        }
    }

    /// <summary>Keeps a live list of normal application windows and recolours their frames.</summary>
    public sealed class WindowTracker : IDisposable
    {
        public event Action Changed;
        public List<WinInfo> Windows { get; private set; } = new List<WinInfo>();
        public string NameOf(WinInfo w) => w.AppName;
        public IntPtr Foreground { get; private set; }

        readonly Dispatcher ui;
        readonly DispatcherTimer debounce, poll; // null in demo mode
        readonly W32.WinEventProc proc;
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly Dictionary<IntPtr, ImageSource> iconCache = new Dictionary<IntPtr, ImageSource>();
        readonly Dictionary<IntPtr, (string exe, string appId)> identity = new Dictionary<IntPtr, (string, string)>();
        readonly List<IntPtr> mru = new List<IntPtr>();
        readonly HashSet<IntPtr> styled = new HashSet<IntPtr>();
        readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        readonly ShellTheme theme;
        IntPtr lastStyledActive;

        readonly bool styleFrames;

        /// <summary>Preview mode: shows example windows instead of the user's real ones.</summary>
        public static WindowTracker Demo(ShellTheme theme) => new WindowTracker(theme, demoWindows: 4);

        WindowTracker(ShellTheme theme, int demoWindows)
        {
            this.theme = theme;
            styleFrames = false;
            ui = Dispatcher.CurrentDispatcher;
            bool de = Core.Loc.De;
            var pins = AppCatalog.Pinned();
            string[] titles = { de ? "Dokumente" : "Documents", de ? "WinMorph – Startseite" : "WinMorph – Home", "Terminal", de ? "Notizen.txt – Editor" : "Notes.txt – Notepad" };
            for (int i = 0; i < Math.Min(pins.Count, titles.Length); i++)
                Windows.Add(new WinInfo { Hwnd = (IntPtr)(i + 1), Title = titles[i], ExePath = pins[i].ExePath, AppId = pins[i].ParsingPath, Icon = pins[i].Icon });
            Foreground = (IntPtr)2;
        }

        public WindowTracker(ShellTheme theme, bool styleFrames = true)
        {
            this.theme = theme;
            this.styleFrames = styleFrames;
            ui = Dispatcher.CurrentDispatcher;
            debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Background, (s, e) => { debounce.Stop(); Refresh(); }, ui);
            debounce.Stop();
            poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) => Refresh(), ui);
            proc = OnEvent;
            hooks.Add(W32.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, proc, 0, 0, 0)); // foreground
            hooks.Add(W32.SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, proc, 0, 0, 0)); // minimize start/end
            hooks.Add(W32.SetWinEventHook(0x8000, 0x8003, IntPtr.Zero, proc, 0, 0, 0)); // create, destroy, show, hide
            hooks.Add(W32.SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, proc, 0, 0, 0)); // name change
            Foreground = W32.GetForegroundWindow();
            Refresh();
            poll.Start();
        }

        void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;
            if (ev == 0x0003)
            {
                W32.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != ownPid)
                {
                    Foreground = hwnd; StyleActive(hwnd);
                    mru.Remove(hwnd); mru.Insert(0, hwnd);
                }
            }
            if (ev == 0x8002) StyleFrame(hwnd, hwnd == W32.GetForegroundWindow());
            if (!debounce.IsEnabled) debounce.Start();
        }

        /// <summary>WinMorph's own app windows (hacker task manager, file manager): listed like other apps.</summary>
        public static readonly HashSet<IntPtr> OwnApps = new HashSet<IntPtr>();

        public static bool IsAppWindow(IntPtr h, uint ownPid)
        {
            if (!W32.IsWindowVisible(h)) return false;
            int ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
            bool appWin = (ex & W32.WS_EX_APPWINDOW) != 0;
            if ((ex & W32.WS_EX_TOOLWINDOW) != 0 && !appWin) return false;
            if ((ex & W32.WS_EX_NOACTIVATE) != 0 && !appWin) return false;
            if (W32.GetWindow(h, W32.GW_OWNER) != IntPtr.Zero && !appWin) return false;
            if (W32.GetWindowTextLength(h) == 0) return false;
            if (W32.IsCloaked(h)) return false;
            W32.GetWindowThreadProcessId(h, out uint pid);
            if (pid == ownPid && !OwnApps.Contains(h)) return false;
            var cls = W32.ClassOf(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
            return true;
        }

        public void Refresh()
        {
            var list = new List<WinInfo>();
            W32.EnumWindows((h, l) =>
            {
                if (!IsAppWindow(h, ownPid)) return true;
                var info = new WinInfo { Hwnd = h, Title = W32.Text(h), Minimized = W32.IsIconic(h) };
                if (!identity.TryGetValue(h, out var id))
                {
                    // process path and app id never change for a window, so look them up only once
                    var exe = W32.ProcessPath(h, out _);
                    var appId = W32.AppId(h);
                    if (exe != null && exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) && appId == null) exe = null;
                    identity[h] = id = (exe, appId);
                }
                info.ExePath = id.exe;
                info.AppId = id.appId;
                info.Icon = GetIcon(h, info);
                list.Add(info);
                if (!styled.Contains(h)) StyleFrame(h, h == Foreground);
                return true;
            }, IntPtr.Zero);

            // keep a stable order: existing windows first, new ones appended
            var old = Windows.Select(w => w.Hwnd).ToList();
            list = list.OrderBy(w => { int i = old.IndexOf(w.Hwnd); return i < 0 ? int.MaxValue : i; }).ToList();

            var alive = new HashSet<IntPtr>(list.Select(w => w.Hwnd));
            foreach (var gone in iconCache.Keys.Where(k => !alive.Contains(k)).ToList()) { iconCache.Remove(gone); identity.Remove(gone); }
            mru.RemoveAll(h => !alive.Contains(h));
            foreach (var w in list) if (!mru.Contains(w.Hwnd)) mru.Add(w.Hwnd);
            styled.RemoveWhere(h => !W32.IsWindow(h));

            bool changed = list.Count != Windows.Count || list.Where((w, i) => w.Hwnd != Windows[i].Hwnd || w.Title != Windows[i].Title || w.Minimized != Windows[i].Minimized).Any();
            Windows = list;
            if (changed) Changed?.Invoke();
            else ForegroundChanged?.Invoke();
        }

        public event Action ForegroundChanged;

        /// <summary>Windows in most-recently-used order (for Alt+Tab).</summary>
        public List<WinInfo> ByRecentUse()
        {
            Refresh();
            return Windows.OrderBy(w => { int i = mru.IndexOf(w.Hwnd); return i < 0 ? int.MaxValue : i; }).ToList();
        }

        ImageSource GetIcon(IntPtr h, WinInfo info)
        {
            if (iconCache.TryGetValue(h, out var cached)) return cached;
            ImageSource icon = null;
            if (info.AppId != null && (info.ExePath == null || info.ExePath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0 || info.ExePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)))
                icon = W32.ShellItemIcon(@"shell:AppsFolder\" + info.AppId, 64);
            if (icon == null)
            {
                foreach (var t in new[] { 1, 2, 0 })
                {
                    W32.SendMessageTimeout(h, 0x007F, (IntPtr)t, IntPtr.Zero, 2, 60, out IntPtr hi); // WM_GETICON
                    if (hi != IntPtr.Zero) { icon = W32.IconFromHandle(hi, false); if (icon != null) break; }
                }
            }
            if (icon == null) icon = W32.IconFromHandle(W32.GetClassLongPtr(h, -14), false);
            if (icon == null && info.ExePath != null) icon = W32.FileIcon(info.ExePath);
            iconCache[h] = icon;
            return icon;
        }

        // ---------- window frames ----------
        static int Bgr(Color c) => c.R | (c.G << 8) | (c.B << 16);

        void StyleFrame(IntPtr h, bool active)
        {
            if (!styleFrames || theme.Frame == null || !IsAppWindow(h, ownPid)) return;
            var f = theme.Frame;
            // only the title bar is coloured: window borders keep Windows' own colour
            int cap = Bgr(active ? f.Caption : f.CaptionInactive), txt = Bgr(active ? f.Text : f.TextInactive);
            W32.DwmSetWindowAttribute(h, W32.DWMWA_CAPTION_COLOR, ref cap, 4);
            W32.DwmSetWindowAttribute(h, W32.DWMWA_TEXT_COLOR, ref txt, 4);
            int corner = f.Square ? 1 : 0;
            W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            styled.Add(h);
        }

        void StyleActive(IntPtr h)
        {
            if (!styleFrames || theme.Frame == null) return;
            if (lastStyledActive != IntPtr.Zero && lastStyledActive != h && W32.IsWindow(lastStyledActive)) StyleFrame(lastStyledActive, false);
            StyleFrame(h, true);
            lastStyledActive = h;
        }

        /// <summary>Gives every window its normal Windows frame back.</summary>
        public void ResetFrames()
        {
            int def = W32.DWM_COLOR_DEFAULT, corner = 0;
            foreach (var h in styled.ToList())
            {
                if (!W32.IsWindow(h)) continue;
                W32.DwmSetWindowAttribute(h, W32.DWMWA_CAPTION_COLOR, ref def, 4);
                W32.DwmSetWindowAttribute(h, W32.DWMWA_TEXT_COLOR, ref def, 4);
                W32.DwmSetWindowAttribute(h, W32.DWMWA_BORDER_COLOR, ref def, 4);
                W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            }
            styled.Clear();
        }

        public void Dispose()
        {
            poll?.Stop();
            foreach (var h in hooks) if (h != IntPtr.Zero) W32.UnhookWinEvent(h);
            hooks.Clear();
            ResetFrames();
        }
    }
}

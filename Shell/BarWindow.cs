using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinMorph.Shell
{
    public enum Edge { Left = 0, Top = 1, Right = 2, Bottom = 3 }

    /// <summary>
    /// A screen-edge bar registered as an AppBar, so maximised windows leave room for it
    /// exactly like they do for the real taskbar.
    /// </summary>
    public sealed class BarWindow : Window
    {
        public readonly Edge Edge;
        readonly double thickness;
        readonly bool blur;
        readonly uint blurColor;
        IntPtr hwnd;
        uint callbackMsg;
        bool registered;
        public double Scale { get; private set; } = 1;
        public event Action ExplorerRestarted;
        static readonly uint TaskbarCreated = W32.RegisterWindowMessage("TaskbarCreated");

        public readonly int MonitorIndex;
        public event Action DisplayChanged;

        public BarWindow(Edge edge, double thicknessDip, bool blur = false, uint blurAbgr = 0, int monitorIndex = 0)
        {
            MonitorIndex = monitorIndex;
            Edge = edge;
            thickness = thicknessDip;
            this.blur = blur;
            blurColor = blurAbgr;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            SourceInitialized += OnInit;
            RenderGuard.Watch(this);
            Closed += (s, e) => Unregister();
        }

        void OnInit(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            int ex = W32.GetWindowLong(hwnd, W32.GWL_EXSTYLE);
            SetWindowLong(hwnd, W32.GWL_EXSTYLE, (ex | W32.WS_EX_TOOLWINDOW) & ~W32.WS_EX_APPWINDOW);
            if (blur) W32.SetBlur(hwnd, 3, blurColor);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            callbackMsg = W32.RegisterWindowMessage("WinMorphAppBar");
            var abd = Data();
            W32.SHAppBarMessage(0, ref abd); // ABM_NEW
            registered = true;
            Place();
        }

        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        W32.APPBARDATA Data() => new W32.APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(W32.APPBARDATA)), hWnd = hwnd, uCallbackMessage = callbackMsg, uEdge = (uint)Edge };

        public void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var mons = W32.Monitors();
            var mi = W32.Monitor(MonitorIndex < mons.Count ? mons[MonitorIndex] : mons[0], out double scale);
            Scale = scale;
            int t = (int)Math.Round(thickness * scale);
            var abd = Data();
            abd.rc = mi.rcMonitor;
            switch (Edge)
            {
                case Edge.Top: abd.rc.Bottom = abd.rc.Top + t; break;
                case Edge.Bottom: abd.rc.Top = abd.rc.Bottom - t; break;
                case Edge.Left: abd.rc.Right = abd.rc.Left + t; break;
                case Edge.Right: abd.rc.Left = abd.rc.Right - t; break;
            }
            W32.SHAppBarMessage(2, ref abd); // ABM_QUERYPOS
            switch (Edge)
            {
                case Edge.Top: abd.rc.Bottom = abd.rc.Top + t; break;
                case Edge.Bottom: abd.rc.Top = abd.rc.Bottom - t; break;
                case Edge.Left: abd.rc.Right = abd.rc.Left + t; break;
                case Edge.Right: abd.rc.Left = abd.rc.Right - t; break;
            }
            W32.SHAppBarMessage(3, ref abd); // ABM_SETPOS
            W32.SetWindowPos(hwnd, (IntPtr)(-1), abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top, 0x0010 | 0x0040); // NOACTIVATE | SHOWWINDOW
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == TaskbarCreated)
            {
                // Explorer restarted: the AppBar registration is gone, so register again.
                var abd = Data();
                W32.SHAppBarMessage(0, ref abd);
                registered = true;
                Dispatcher.BeginInvoke(new Action(() => { Place(); ExplorerRestarted?.Invoke(); }));
            }
            else if (msg == callbackMsg)
            {
                if ((int)w == 1) Dispatcher.BeginInvoke(new Action(Place));                 // ABN_POSCHANGED
                else if ((int)w == 2)                                                         // ABN_FULLSCREENAPP
                {
                    bool full = l != IntPtr.Zero;
                    W32.SetWindowPos(hwnd, full ? (IntPtr)1 : (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    W32.ShowWindow(hwnd, full ? W32.SW_HIDE : W32.SW_SHOWNA);
                }
            }
            else if (msg == 0x0006 && registered) { var abd = Data(); W32.SHAppBarMessage(6, ref abd); }      // WM_ACTIVATE → ABM_ACTIVATE
            else if (msg == 0x0047 && registered) { var abd = Data(); W32.SHAppBarMessage(9, ref abd); }      // WM_WINDOWPOSCHANGED
            else if (msg == 0x007E || msg == 0x02E0) Dispatcher.BeginInvoke(new Action(() => { Place(); DisplayChanged?.Invoke(); })); // display / DPI change
            return IntPtr.Zero;
        }

        void Unregister()
        {
            if (!registered) return;
            var abd = Data();
            W32.SHAppBarMessage(1, ref abd); // ABM_REMOVE
            registered = false;
        }

        /// <summary>Screen position of an element in DIPs (for placing menus).</summary>
        public Rect ScreenRectOf(FrameworkElement el)
        {
            var p = el.PointToScreen(new Point(0, 0));
            return new Rect(p.X / Scale, p.Y / Scale, el.ActualWidth, el.ActualHeight);
        }
    }
}

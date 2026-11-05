using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinMorph.Shell
{
    /// <summary>A live, GPU-composited picture of another window (DWM thumbnail) drawn over an element.</summary>
    internal sealed class LiveThumb : IDisposable
    {
        IntPtr thumb;
        readonly Window host;
        readonly FrameworkElement target;

        public LiveThumb(Window host, IntPtr source, FrameworkElement target)
        {
            this.host = host; this.target = target;
            var h = new WindowInteropHelper(host).Handle;
            if (h != IntPtr.Zero && W32.DwmRegisterThumbnail(h, source, out thumb) == 0) Update();
            else thumb = IntPtr.Zero;
        }

        public bool Ok => thumb != IntPtr.Zero;

        public void Update()
        {
            if (thumb == IntPtr.Zero || !target.IsLoaded) return;
            var src = PresentationSource.FromVisual(host);
            if (src == null) return;
            double s = src.CompositionTarget.TransformToDevice.M11;
            Point p;
            try { p = target.TransformToAncestor(host).Transform(new Point(0, 0)); } catch { return; }
            double w = target.ActualWidth, hgt = target.ActualHeight;
            W32.DwmQueryThumbnailSourceSize(thumb, out var size);
            if (size.cx > 0 && size.cy > 0)
            {
                // keep the aspect ratio, centred inside the target
                double k = Math.Min(w / size.cx, hgt / size.cy);
                double dw = size.cx * k, dh = size.cy * k;
                p.X += (w - dw) / 2; p.Y += (hgt - dh) / 2; w = dw; hgt = dh;
            }
            var props = new W32.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = 0x1 | 0x4 | 0x8 | 0x10,
                rcDestination = new W32.RECT { Left = (int)(p.X * s), Top = (int)(p.Y * s), Right = (int)((p.X + w) * s), Bottom = (int)((p.Y + hgt) * s) },
                opacity = 255, fVisible = 1, fSourceClientAreaOnly = 0,
            };
            W32.DwmUpdateThumbnailProperties(thumb, ref props);
        }

        public void Dispose()
        {
            if (thumb != IntPtr.Zero) W32.DwmUnregisterThumbnail(thumb);
            thumb = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Low-level keyboard hook on its own thread, so typing never waits for the shell UI.
    /// The handler returns true to swallow a key.
    /// </summary>
    internal sealed class KeyHook
    {
        public delegate bool Handler(bool down, bool up, uint vk, uint flags);
        readonly Handler handler;
        W32.HookProc proc;
        uint threadId;
        Thread thread;

        public KeyHook(Handler h) { handler = h; }

        public void Start()
        {
            thread = new Thread(() =>
            {
                threadId = W32.GetCurrentThreadId();
                proc = (code, w, l) =>
                {
                    if (code >= 0)
                    {
                        var k = System.Runtime.InteropServices.Marshal.PtrToStructure<W32.KBDLLHOOKSTRUCT>(l);
                        int msg = (int)w;
                        bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                        bool swallow = false;
                        if ((k.flags & 0x10) == 0) // ignore keys we injected ourselves
                        {
                            try { swallow = handler(down, up, k.vkCode, k.flags); } catch { }
                        }
                        if (swallow) return (IntPtr)1;
                    }
                    return W32.CallNextHookEx(IntPtr.Zero, code, w, l);
                };
                var hook = W32.SetWindowsHookEx(13, proc, W32.GetModuleHandle(null), 0);
                while (W32.GetMessage(out var m, IntPtr.Zero, 0, 0) > 0) { W32.TranslateMessage(ref m); W32.DispatchMessage(ref m); }
                W32.UnhookWindowsHookEx(hook);
            }) { IsBackground = true, Name = "WinMorph keyboard" };
            thread.Start();
        }

        public void Stop()
        {
            if (threadId != 0) W32.PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        }

        public static void Tap(byte vk) { W32.keybd_event(vk, 0, 0, UIntPtr.Zero); W32.keybd_event(vk, 0, 2, UIntPtr.Zero); }
    }
}

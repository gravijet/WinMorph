using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinMorph.Shell
{
    /// <summary>
    /// Some graphics drivers occasionally stop presenting hardware-rendered WPF windows
    /// (they stay white or invisible). A tiny off-screen test picture with known colours is
    /// drawn at start-up and every 30 seconds; if it does not come out right, the whole
    /// process switches to software rendering so everything stays visible.
    /// </summary>
    public static class RenderGuard
    {
        public static bool Software { get; private set; }
        static DispatcherTimer timer;
        static bool started;
        static int fails;

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        /// <summary>Call once per process before showing windows.</summary>
        public static void Start()
        {
            if (started) return;
            started = true;
            // software rendering costs a lot of CPU and battery: only switch when a second test confirms it
            if (ProbeNow() == false && ProbeNow() == false) Switch("start-up test");
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
            timer.Tick += (s, e) => { if (!Software && !Core.LockGuard.Locked() && !CustomLock.Active) ProbeAsync(); };
            timer.Start();
        }

        /// <summary>Kept for existing call sites: new windows follow the current mode automatically.</summary>
        public static void Watch(Window w) { if (Software) w.SourceInitialized += (s, e) => Apply(w); }

        static Window MakeProbe()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)) });
            var blue = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0, 0, 255)) };
            Grid.SetColumn(blue, 1);
            g.Children.Add(blue);
            return new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                Width = 40, Height = 20, Left = -32000, Top = -32000, Content = g,
            };
        }

        static bool? Check(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                using (var bmp = new System.Drawing.Bitmap(60, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        var dc = gr.GetHdc();
                        bool ok = PrintWindow(h, dc, 2); // PW_RENDERFULLCONTENT
                        gr.ReleaseHdc(dc);
                        if (!ok) return null;
                    }
                    var src = PresentationSource.FromVisual(w);
                    double s = src?.CompositionTarget.TransformToDevice.M11 ?? 1;
                    var left = bmp.GetPixel((int)(8 * s), (int)(8 * s));
                    var right = bmp.GetPixel((int)(32 * s), (int)(8 * s));
                    bool good = left.R > 200 && left.G < 60 && left.B < 60 && right.B > 200 && right.R < 60 && right.G < 60;
                    if (good) return true;
                    // black or empty = not drawn yet (busy start, screen off) – no sign of a broken driver
                    if (left.A < 20 || left.R + left.G + left.B < 40) return null;
                    return false;
                }
            }
            catch { return null; }
        }

        /// <summary>Synchronous test (used at start-up).</summary>
        static bool? ProbeNow()
        {
            var app = Application.Current;
            var mode = app.ShutdownMode;
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown; // closing the probe must never end the app
            var w = MakeProbe();
            w.Show();
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (s, e) => { t.Stop(); frame.Continue = false; };   // first frame of a fresh window
            t.Start();
            Dispatcher.PushFrame(frame);
            var r = Check(w);
            w.Close();
            if (app.MainWindow == w) app.MainWindow = null;
            app.ShutdownMode = mode;
            return r;
        }

        static void ProbeAsync()
        {
            var app = Application.Current;
            var main = app.MainWindow;
            var w = MakeProbe();
            w.Show();
            if (app.MainWindow == w) app.MainWindow = main;
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var r = Check(w);
                w.Close();
                if (r == false) { if (++fails >= 2) Switch("runtime test"); }
                else if (r == true) fails = 0;
            };
            t.Start();
        }

        static void Switch(string why)
        {
            if (Software) return;
            Software = true;
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            if (Application.Current != null) foreach (Window x in Application.Current.Windows) Apply(x);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Core.Backup.DataDir, "render.log"), DateTime.Now + " graphics driver not presenting (" + why + ") – using software rendering" + Environment.NewLine); } catch { }
        }

        static void Apply(Window w)
        {
            if (PresentationSource.FromVisual(w) is HwndSource src && src.CompositionTarget != null)
            {
                src.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                if (w.Content is UIElement c) c.InvalidateVisual();
                w.InvalidateVisual();
                W32.SetWindowPos(src.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
        }
    }
}

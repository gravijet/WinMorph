using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinMorph.Live
{
    public enum MouseMode { None, Desktop, Anywhere }

    /// <summary>
    /// Plays a live wallpaper: "scene:&lt;id&gt;", "video:&lt;file or url&gt;" or "image:&lt;file&gt;".
    /// Used on the desktop, on the lock screen and as animated preview in the app.
    /// </summary>
    public sealed class LiveView : Grid, IPausable
    {
        public readonly string Spec;
        readonly bool hud;
        readonly MouseMode mouse;
        Scene scene;
        Image img;
        WriteableBitmap wb;
        DispatcherTimer timer, hudTimer;
        readonly Stopwatch sw = new Stopwatch();
        double last;
        MediaElement media;
        bool paused, started, lmb;
        int slowFrames, fps;
        ScaleTransform kbScale; TranslateTransform kbMove;

        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        public LiveView(string spec, bool hud = false, MouseMode mouse = MouseMode.Anywhere)
        {
            Spec = spec ?? "";
            this.hud = hud || Spec == "scene:matrix-hud" || Spec == "scene:hacker-desk";
            this.mouse = mouse;
            Background = Brushes.Black;
            ClipToBounds = true;
            Loaded += (s, e) => Begin();
            Unloaded += (s, e) => End();
            SizeChanged += (s, e) => { if (scene != null && e.NewSize.Width > 0 && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 40) { scene = null; Begin(); } };
        }

        public static string Kind(string spec) => spec == null ? "" : spec.Split(':')[0];
        public static string Arg(string spec) { int i = spec?.IndexOf(':') ?? -1; return i < 0 ? "" : spec.Substring(i + 1); }

        public bool Paused
        {
            get => paused;
            set
            {
                if (paused == value) return;
                paused = value;
                if (media != null) { if (paused) media.Pause(); else media.Play(); }
                if (timer != null) { if (paused) timer.Stop(); else { last = sw.Elapsed.TotalSeconds; timer.Start(); } }
            }
        }

        void Begin()
        {
            if (ActualWidth < 10 || ActualHeight < 10) return;
            if (started && scene != null) return;
            started = true;
            string kind = Kind(Spec), arg = Arg(Spec);
            Children.Clear();
            try
            {
                if (kind == "video") BeginVideo(arg);
                else if (kind == "image") BeginImage(arg);
                else BeginScene(kind == "scene" ? arg : "matrix");
            }
            catch (Exception ex) { Log(ex); }
            if (hud) Children.Add(Hud());
        }

        void BeginScene(string id)
        {
            scene = Scene.Create(id);
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            double pw = ActualWidth * dpi, ph = ActualHeight * dpi;
            double k = Math.Min(1, 1920.0 / Math.Max(pw, 1)) * scene.Resolution;
            scene.Setup((int)(pw * k), (int)(ph * k));
            wb = new WriteableBitmap(scene.W, scene.H, 96, 96, PixelFormats.Bgr32, null);
            img = new Image { Source = wb, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, scene.Resolution < 1 ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
            Children.Add(img);
            fps = scene.Fps;
            sw.Restart(); last = 0;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / fps) };
            timer.Tick += (s, e) => Frame();
            if (!paused) timer.Start();
            Frame();
        }

        void Frame()
        {
            if (scene == null) return;
            double now = sw.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - last);
            last = now;
            ReadMouse();
            var t0 = sw.Elapsed.TotalMilliseconds;
            try
            {
                scene.T += dt;
                scene.Step(dt);
                wb.WritePixels(new Int32Rect(0, 0, scene.W, scene.H), scene.Px, scene.W * 4, 0);
            }
            catch (Exception ex) { Log(ex); timer?.Stop(); return; }
            // adapt: if the PC cannot keep up, use fewer frames instead of lagging
            double cost = sw.Elapsed.TotalMilliseconds - t0;
            if (cost > 1000.0 / fps * 0.7) slowFrames++; else slowFrames = Math.Max(0, slowFrames - 1);
            if (slowFrames > 40 && fps > 15) { fps = fps > 24 ? 24 : 15; timer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps); slowFrames = 0; }
        }

        void ReadMouse()
        {
            if (mouse == MouseMode.None) { scene.Mx = scene.My = -1; return; }
            GetCursorPos(out var p);
            bool overDesktop = true;
            if (mouse == MouseMode.Desktop)
            {
                var sb = new StringBuilder(64);
                GetClassName(WindowFromPoint(p), sb, 64);
                var c = sb.ToString();
                overDesktop = c == "SysListView32" || c == "SHELLDLL_DefView" || c == "WorkerW" || c == "Progman" || c.StartsWith("HwndWrapper");
            }
            bool down = (GetAsyncKeyState(1) & 0x8000) != 0;
            if (!overDesktop) { scene.Mx = scene.My = -1; lmb = down; return; }
            Point local;
            try { local = PointFromScreen(new Point(p.X, p.Y)); } catch { scene.Mx = scene.My = -1; return; }
            if (local.X < 0 || local.Y < 0 || local.X >= ActualWidth || local.Y >= ActualHeight) { scene.Mx = scene.My = -1; lmb = down; return; }
            scene.Mx = local.X / ActualWidth * scene.W;
            scene.My = local.Y / ActualHeight * scene.H;
            if (down && !lmb) scene.Click = true;
            lmb = down;
        }

        void BeginVideo(string path)
        {
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // WPF's MediaElement crashes (NullReferenceException inside WPF) on web addresses on current
                // Windows builds – only local files are played; show the still picture instead
                var it = WallCatalog.All.FirstOrDefault(x => x.VideoUrl == path);
                if (it?.ThumbUrl != null)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.UriSource = new Uri(it.ThumbUrl.Replace("-thumb-360-", "-thumb-720-")); bi.EndInit();
                        Children.Add(new Image { Source = bi, Stretch = Stretch.UniformToFill });
                        return;
                    }
                    catch { }
                }
                BeginScene("matrix");
                return;
            }
            if (!File.Exists(path)) { BeginScene("matrix"); return; }
            media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, IsMuted = true, Stretch = Stretch.UniformToFill, Source = new Uri(path) };
            media.MediaEnded += (s, e) => { media.Position = TimeSpan.Zero; media.Play(); };
            media.MediaFailed += (s, e) => { Log(e.ErrorException); };
            Children.Add(media);
            if (!paused) media.Play();
        }

        void BeginImage(string path)
        {
            if (!File.Exists(path)) { BeginScene("plexus"); return; }
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.DecodePixelWidth = 2560; bi.EndInit(); bi.Freeze();
            var im = new Image { Source = bi, Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5) };
            kbScale = new ScaleTransform(1.08, 1.08); kbMove = new TranslateTransform();
            var g = new TransformGroup(); g.Children.Add(kbScale); g.Children.Add(kbMove);
            im.RenderTransform = g;
            Children.Add(im);
            // gentle "Ken Burns" zoom plus parallax that follows the mouse
            var zoom = new DoubleAnimation(1.08, 1.16, TimeSpan.FromSeconds(40)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            kbScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom); kbScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (s, e) =>
            {
                GetCursorPos(out var p);
                Point local;
                try { local = PointFromScreen(new Point(p.X, p.Y)); } catch { return; }
                double tx = -(local.X / Math.Max(1, ActualWidth) - 0.5) * ActualWidth * 0.03, ty = -(local.Y / Math.Max(1, ActualHeight) - 0.5) * ActualHeight * 0.03;
                kbMove.X += (tx - kbMove.X) * 0.08; kbMove.Y += (ty - kbMove.Y) * 0.08;
            };
            if (!paused) timer.Start();
        }

        void End()
        {
            timer?.Stop(); hudTimer?.Stop();
            try { media?.Stop(); media?.Close(); } catch { }
            scene = null; wb = null; media = null; started = false;
            Children.Clear();
        }

        // ---------------- HUD (system monitor in the corner, like conky) ----------------
        UIElement Hud()
        {
            var green = Shell.ShellThemes.HackerColor;
            var dim = Core.Applier.Mix(green, Colors.Black, 0.45);
            var font = new FontFamily("Consolas");
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, 60, 0), MinWidth = 380 };
            var bg = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(140, green.R, green.G, green.B)), BorderThickness = new Thickness(1), Padding = new Thickness(22, 16, 22, 18), Child = panel, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 70, 60, 0) };
            panel.Margin = new Thickness(0);
            TextBlock T(double size, Color c, FontWeight? w = null) { var t = new TextBlock { FontFamily = font, FontSize = size, Foreground = new SolidColorBrush(c), FontWeight = w ?? FontWeights.Normal }; panel.Children.Add(t); return t; }
            var clock = T(64, green, FontWeights.Bold);
            clock.Effect = new DropShadowEffect { Color = green, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.75 };
            var date = T(16, dim);
            date.Margin = new Thickness(2, -4, 0, 12);
            var info = T(15, green);
            info.LineHeight = 22;
            var de = Core.Loc.De;
            var ci = de ? new CultureInfo("de-DE") : new CultureInfo("en-US");
            string user = Environment.UserName.ToLowerInvariant(), host = Environment.MachineName.ToLowerInvariant();
            int tick = 0;
            string ip = "", diskText = "";
            Action update = () =>
            {
                var now = DateTime.Now;
                clock.Text = now.ToString("HH:mm:ss");
                date.Text = now.ToString(de ? "dddd, dd. MMMM yyyy" : "dddd, MMMM dd yyyy", ci).ToLowerInvariant();
                if (tick++ % 10 == 0)
                {
                    ip = Shell.SysInfo.LocalIp;
                    diskText = string.Join("  ", Shell.SysInfo.Disks().Take(2).Select(d => d.Name.TrimEnd('\\') + " " + (100 - d.TotalFreeSpace * 100 / Math.Max(1, d.TotalSize)) + "%"));
                }
                double cpu = Shell.SysInfo.CpuSample(), mem = Shell.SysStats.Mem();
                Shell.SysInfo.NetRate(out double rx, out double tx);
                info.Text =
                    user + "@" + host + "\n" +
                    "────────────────────────────\n" +
                    "up    " + Shell.SysInfo.UptimeText + "\n" +
                    "cpu   " + Shell.SysInfo.Bar(cpu, 16) + " " + cpu.ToString("00") + "%\n" +
                    "mem   " + Shell.SysInfo.Bar(mem, 16) + " " + mem.ToString("00") + "%\n" +
                    "disk  " + diskText + "\n" +
                    "net   ↓" + Shell.SysInfo.Bytes(rx).PadLeft(6) + "/s  ↑" + Shell.SysInfo.Bytes(tx).PadLeft(6) + "/s\n" +
                    "ip    " + ip + "\n" +
                    "load  " + Shell.SysInfo.Sparkline(Shell.SysInfo.CpuHistory.Skip(Math.Max(0, Shell.SysInfo.CpuHistory.Count - 28)));
            };
            update();
            hudTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => { if (!paused) update(); }, Dispatcher);
            return bg;
        }

        /// <summary>Renders a still picture of a scene (used as the normal wallpaper and for cards).</summary>
        public static BitmapSource Snapshot(string sceneId, int w, int h, double seconds = 3)
        {
            var sc = Scene.Create(sceneId);
            double k = Math.Min(1, 1920.0 / w) * sc.Resolution;
            sc.Setup((int)(w * k), (int)(h * k));
            int frames = (int)(seconds * 30);
            if (sc is MediaScene media)
            {
                // a video needs real time to decode: let the dispatcher run, then take the current frame
                var until = DateTime.Now.AddSeconds(seconds);
                while (DateTime.Now < until)
                {
                    var f = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => f.Continue = false));
                    Dispatcher.PushFrame(f);
                    System.Threading.Thread.Sleep(30);
                    sc.T += 0.03; sc.Step(0.03);
                }
                media.Dispose();
            }
            else for (int i = 0; i < frames; i++) { sc.T += 1 / 30.0; sc.Step(1 / 30.0); }
            var bmp = BitmapSource.Create(sc.W, sc.H, 96, 96, PixelFormats.Bgr32, null, sc.Px, sc.W * 4);
            if (sc.W == w && sc.H == h) { bmp.Freeze(); return bmp; }
            var scaled = new TransformedBitmap(bmp, new ScaleTransform(w / (double)sc.W, h / (double)sc.H));
            scaled.Freeze();
            return scaled;
        }

        static void Log(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(Core.Backup.DataDir, "live-errors.log"), DateTime.Now + "  " + ex + Environment.NewLine); } catch { }
        }
    }
}

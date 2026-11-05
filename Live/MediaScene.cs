using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Live
{
    /// <summary>
    /// A video (looping, muted) or a picture (slow zoom + parallax) drawn into the scene pixel buffer,
    /// so it can be painted with GDI behind the desktop icons like the animated scenes.
    /// (WPF windows inside the desktop stay invisible on Windows 11 24H2 and newer.)
    /// </summary>
    public sealed class MediaScene : Scene, IDisposable
    {
        readonly string path;
        readonly bool image;
        MediaPlayer player;
        BitmapSource still;
        DrawingVisual dv;
        RenderTargetBitmap rtb;
        bool haveFrame;
        double px, py;   // parallax offset

        public MediaScene(string path, bool image) { this.path = path; this.image = image; }

        // the catalogue videos are 720p: rendering more pixels only costs time
        public override double Resolution => image ? 1.0 : 0.667;
        public override int Fps => image ? 30 : 25;

        protected override void Init()
        {
            dv = new DrawingVisual();
            rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            if (image)
            {
                if (!File.Exists(path)) return;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.DecodePixelWidth = (int)(W * 1.25); bi.EndInit(); bi.Freeze();
                still = bi;
                return;
            }
            if (!File.Exists(path)) return;
            player = new MediaPlayer { IsMuted = true, Volume = 0, ScrubbingEnabled = false };
            player.MediaEnded += (s, e) => { player.Position = TimeSpan.Zero; player.Play(); };
            player.MediaFailed += (s, e) => LiveWall.LogError(e.ErrorException);
            player.Open(new Uri(path));
            player.Play();
        }

        public override void Pause(bool on)
        {
            if (player == null) return;
            if (on) player.Pause(); else player.Play();
        }

        static Rect Cover(double sw, double sh, double w, double h, double zoom, double ox, double oy)
        {
            double k = Math.Max(w / sw, h / sh) * zoom;
            double dw = sw * k, dh = sh * k;
            return new Rect((w - dw) / 2 + ox, (h - dh) / 2 + oy, dw, dh);
        }

        public override void Step(double dt)
        {
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, W, H));
                if (player != null)
                {
                    int vw = player.NaturalVideoWidth, vh = player.NaturalVideoHeight;
                    if (vw <= 0 || vh <= 0) { if (!haveFrame) return; }
                    else dc.DrawVideo(player, Cover(vw, vh, W, H, 1, 0, 0));
                }
                else if (still != null)
                {
                    // gentle "Ken Burns" zoom plus parallax that follows the mouse
                    double zoom = 1.08 + 0.05 * (1 - Math.Cos(T * 2 * Math.PI / 80)) / 2 * 2;
                    double tx = MouseIn ? -(Mx / W - 0.5) * W * 0.03 : 0, ty = MouseIn ? -(My / H - 0.5) * H * 0.03 : 0;
                    px += (tx - px) * 0.06; py += (ty - py) * 0.06;
                    dc.DrawImage(still, Cover(still.PixelWidth, still.PixelHeight, W, H, zoom, px, py));
                }
                else return;
            }
            rtb.Clear();
            rtb.Render(dv);
            rtb.CopyPixels(Px, W * 4, 0);
            haveFrame = true;
        }

        public void Dispose()
        {
            try { player?.Stop(); player?.Close(); } catch { }
            player = null;
        }
    }
}

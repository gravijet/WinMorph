using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinMorph.Shell
{
    /// <summary>
    /// --ui-test &lt;look&gt; &lt;surface&gt; &lt;png&gt;: opens one surface of a look on the real screen (with its glass and
    /// shadow, which only the screen shows), photographs the area around it and closes it again. Changes nothing.
    /// </summary>
    internal static class UiTest
    {
        public static void Run(string look, string surface, string png, Action done)
        {
            var t = ShellThemes.For(look) ?? ShellThemes.For("zen");
            Window w;
            switch (surface)
            {
                default:
                    w = Flyouts.Open(t, surface, new Rect(0, 0, 0, 0));
                    break;
            }
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            tm.Tick += (s, e) =>
            {
                tm.Stop();
                try { Shoot(w, png); } catch (Exception ex) { System.IO.File.WriteAllText(png + ".txt", ex.ToString()); }
                try { w?.Close(); } catch { }
                done();
            };
            tm.Start();
        }

        static void Shoot(Window w, string png)
        {
            var h = new WindowInteropHelper(w).Handle;
            W32.GetWindowRect(h, out var r);
            int pad = 40;
            int x = r.Left - pad, y = r.Top - pad, cw = r.Right - r.Left + 2 * pad, ch = r.Bottom - r.Top + 2 * pad;
            using (var bmp = new System.Drawing.Bitmap(cw, ch))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(cw, ch));
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}

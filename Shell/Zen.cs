using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    // =====================================================================
    // Zen: focus timer, breathing exercise, background sounds
    // =====================================================================
    internal static class Zen
    {
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        // ---------------- focus timer: 25 min focus, then 5 min break ----------------
        static DateTime end;
        static bool focus;          // true = focus phase, false = break
        public static bool Running => end > DateTime.Now;

        /// <summary>Text for the bar: remaining time or a short label.</summary>
        public static string TimerText
        {
            get
            {
                var left = end - DateTime.Now;
                if (left <= TimeSpan.Zero) return "";
                return (focus ? L("Fokus ", "Focus ") : L("Pause ", "Break ")) + ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
            }
        }

        public static void ToggleTimer()
        {
            if (Running) { end = DateTime.MinValue; Notify.Show("Zen", L("Fokus beendet", "Focus stopped")); return; }
            focus = true;
            end = DateTime.Now.AddMinutes(25);
            Notify.Show("Zen", L("Fokus läuft – 25 Minuten", "Focus started – 25 minutes"));
        }

        /// <summary>Called once a second by the bar.</summary>
        public static void Tick()
        {
            if (end == DateTime.MinValue || DateTime.Now < end) return;
            if (focus)
            {
                focus = false;
                end = DateTime.Now.AddMinutes(5);
                Notify.Show("Zen", L("Pause – 5 Minuten", "Break – 5 minutes"), null, 12);
            }
            else
            {
                end = DateTime.MinValue;
                Notify.Show("Zen", L("Pause vorbei", "Break is over"), null, 10);
            }
        }

        // ---------------- breathing: 4 s in, 4 s hold, 6 s out, 6 rounds ----------------
        static Window breath;

        public static void Breathe(ShellTheme t)
        {
            if (breath != null) { breath.Close(); return; }
            var mi = W32.PrimaryMonitor(out double sc);
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, AllowsTransparency = true,
                Background = B(A(Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B), 238)), Cursor = Cursors.Hand,
                Left = mi.rcMonitor.Left / sc, Top = mi.rcMonitor.Top / sc, Width = (mi.rcMonitor.Right - mi.rcMonitor.Left) / sc, Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / sc,
            };
            var grid = new Grid();
            var scale = new ScaleTransform(0.55, 0.55);
            var glow = new RadialGradientBrush(A(t.Accent, 200), A(t.Accent, 30));
            var circle = new Ellipse { Width = 340, Height = 340, Fill = glow, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var word = new TextBlock { FontFamily = new FontFamily("Segoe UI Light"), FontSize = 40, Foreground = B(t.MenuFg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var info = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 15, Foreground = B(t.MenuSub), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 80) };
            grid.Children.Add(circle); grid.Children.Add(word); grid.Children.Add(info);
            w.Content = grid;
            var phases = new[] { (L("Einatmen", "Breathe in"), 4.0, 1.0), (L("Halten", "Hold"), 4.0, 1.0), (L("Ausatmen", "Breathe out"), 6.0, 0.55) };
            int round = 0, phase = -1;
            const int rounds = 6;
            var timer = new DispatcherTimer();
            void Next()
            {
                phase++;
                if (phase == phases.Length) { phase = 0; round++; }
                if (round == rounds) { w.Close(); return; }
                var (text, secs, target) = phases[phase];
                word.Text = text;
                info.Text = L("Runde ", "Round ") + (round + 1) + " / " + rounds + "   ·   " + L("Esc oder Klick beendet", "Esc or click to stop");
                var anim = new DoubleAnimation(target, TimeSpan.FromSeconds(secs)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                timer.Interval = TimeSpan.FromSeconds(secs);
            }
            timer.Tick += (s, e) => Next();
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.MouseLeftButtonUp += (s, e) => w.Close();
            w.Closed += (s, e) => { timer.Stop(); breath = null; };
            w.Loaded += (s, e) => { Next(); timer.Start(); w.Activate(); };
            breath = w;
            w.Show();
        }

        // ---------------- background sounds (generated, no files, no internet) ----------------
        static SoundPlayer player;
        static int sound;   // 0 off, 1 noise, 2 rain
        public static int Sound => sound;

        public static void SetSound(int which)
        {
            sound = Math.Max(0, Math.Min(2, which));
            StopSound();
            if (sound == 0) return;
            try
            {
                player = new SoundPlayer(new MemoryStream(Wav(sound == 2)));
                player.PlayLooping();
            }
            catch { sound = 0; }
        }

        public static void StopSound() { try { player?.Stop(); player?.Dispose(); } catch { } player = null; }

        public static void Shutdown()
        {
            StopSound(); sound = 0;
            end = DateTime.MinValue;
            breath?.Close();
        }

        /// <summary>Soft brown noise, or rain (pink noise with drops). 12 s that loop without a seam.</summary>
        internal static byte[] Wav(bool rain)
        {
            const int rate = 22050, seconds = 12, fade = rate;   // 1 s cross-fade at the loop point
            int n = rate * seconds;
            var buf = new double[n + fade];
            var rnd = new Random(rain ? 2 : 1);
            double brown = 0, b0 = 0, b1 = 0, b2 = 0, drop = 0, dropPhase = 0, dropFreq = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double white = rnd.NextDouble() * 2 - 1;
                if (!rain)
                {
                    brown = (brown + 0.02 * white) / 1.02;
                    buf[i] = brown * 3.2;
                }
                else
                {
                    b0 = 0.99765 * b0 + white * 0.0990460; b1 = 0.96300 * b1 + white * 0.2965164; b2 = 0.57000 * b2 + white * 1.0526913;
                    double pink = (b0 + b1 + b2 + white * 0.1848) * 0.09;
                    if (drop < 0.01 && rnd.NextDouble() < 0.0009) { drop = 0.12 + rnd.NextDouble() * 0.2; dropFreq = 1800 + rnd.NextDouble() * 3000; dropPhase = 0; }
                    dropPhase += dropFreq * 2 * Math.PI / rate;
                    double d = drop * Math.Sin(dropPhase);
                    drop *= 0.9975;
                    buf[i] = pink + d;
                }
            }
            for (int i = 0; i < fade; i++) { double k = i / (double)fade; buf[i] = buf[i] * k + buf[n + i] * (1 - k); }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
                for (int i = 0; i < n; i++) w.Write((short)(Math.Max(-1, Math.Min(1, buf[i] * 0.35)) * 32767));
                return ms.ToArray();
            }
        }
    }

}

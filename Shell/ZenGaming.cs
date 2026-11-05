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
                if (left <= TimeSpan.Zero) return L("◔ Fokus", "◔ Focus");
                return (focus ? "◔ " : "☕ ") + ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
            }
        }

        public static void ToggleTimer()
        {
            if (Running) { end = DateTime.MinValue; Notify.Show("Zen", L("Fokus beendet", "Focus stopped")); return; }
            focus = true;
            end = DateTime.Now.AddMinutes(25);
            Notify.Show("Zen", L("25 Minuten Fokus", "25 minutes of focus"), L("Eine Sache. Ganz in Ruhe.", "One thing. Calmly."));
        }

        /// <summary>Called once a second by the bar.</summary>
        public static void Tick()
        {
            if (end == DateTime.MinValue || DateTime.Now < end) return;
            if (focus)
            {
                focus = false;
                end = DateTime.Now.AddMinutes(5);
                Notify.Show("Zen", L("Zeit für eine Pause", "Time for a break"), L("5 Minuten: aufstehen, trinken, aus dem Fenster schauen.", "5 minutes: stand up, drink, look out of the window."), 12);
            }
            else
            {
                end = DateTime.MinValue;
                Notify.Show("Zen", L("Pause vorbei", "Break is over"), L("Klick auf ◔, wenn du weitermachen möchtest.", "Click ◔ when you want to continue."), 10);
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
                if (round == rounds) { w.Close(); Notify.Show("Zen", L("Gut gemacht.", "Well done."), L("Eine Minute nur für dich.", "One minute just for you.")); return; }
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
        public static string SoundText => sound == 0 ? "♪" : sound == 1 ? L("♪ Rauschen", "♪ Noise") : L("♪ Regen", "♪ Rain");

        public static void NextSound()
        {
            sound = (sound + 1) % 3;
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

    // =====================================================================
    // Gaming: game library (Steam + Epic), performance mode, in-game overlay
    // =====================================================================
    internal static class Gaming
    {
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        public sealed class Game { public string Name, Launch, Image, Icon, Source; public long LastPlayed; }

        /// <summary>Installed games from Steam and the Epic Games Launcher.</summary>
        public static List<Game> Games()
        {
            var list = new List<Game>();
            try { list.AddRange(Steam()); } catch { }
            try { list.AddRange(Epic()); } catch { }
            return list.GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                       .OrderByDescending(g => g.LastPlayed).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static string Vdf(string text, string key) { var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : null; }

        static IEnumerable<Game> Steam()
        {
            var root = (Core.Reg.Get(@"HKCU\Software\Valve\Steam", "SteamPath") as string)?.Replace('/', '\\');
            if (root == null || !Directory.Exists(root)) yield break;
            var libs = new List<string> { root };
            var lf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(lf))
                foreach (Match m in Regex.Matches(File.ReadAllText(lf), "\"path\"\\s+\"([^\"]+)\"")) libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            var cache = System.IO.Path.Combine(root, "appcache", "librarycache");
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = System.IO.Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string text;
                    try { text = File.ReadAllText(acf); } catch { continue; }
                    var id = Vdf(text, "appid"); var name = Vdf(text, "name");
                    if (id == null || name == null) continue;
                    // runtimes and tools, not games
                    if (Regex.IsMatch(name, "Redistributable|Proton|Steam Linux Runtime|Steamworks|SteamVR|Soundtrack|Dedicated Server", RegexOptions.IgnoreCase)) continue;
                    long.TryParse(Vdf(text, "LastPlayed"), out long last);
                    string img = new[] { System.IO.Path.Combine(cache, id + "_header.jpg"), System.IO.Path.Combine(cache, id, "header.jpg") }.FirstOrDefault(File.Exists);
                    if (img == null && Directory.Exists(System.IO.Path.Combine(cache, id)))
                        img = Directory.GetFiles(System.IO.Path.Combine(cache, id), "*.jpg", SearchOption.AllDirectories).FirstOrDefault(f => f.EndsWith("header.jpg", StringComparison.OrdinalIgnoreCase));
                    yield return new Game { Name = name, Launch = "steam://rungameid/" + id, Image = img, Source = "Steam", LastPlayed = last };
                }
            }
        }

        static IEnumerable<Game> Epic()
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                string J(string key) { var m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""); return m.Success ? Regex.Unescape(m.Groups[1].Value) : null; }
                if (Regex.IsMatch(text, "\"bIsIncompleteInstall\"\\s*:\\s*true") || !Regex.IsMatch(text, "\"bIsApplication\"\\s*:\\s*true")) continue;
                string name = J("DisplayName"), app = J("AppName"), ns = J("CatalogNamespace"), item = J("CatalogItemId"), loc = J("InstallLocation"), exe = J("LaunchExecutable");
                if (name == null || app == null) continue;
                var exePath = loc != null && exe != null ? System.IO.Path.Combine(loc, exe) : null;
                yield return new Game
                {
                    Name = name, Source = "Epic", Icon = exePath != null && File.Exists(exePath) ? exePath : null,
                    Launch = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(ns ?? "") + "%3A" + Uri.EscapeDataString(item ?? "") + "%3A" + Uri.EscapeDataString(app) + "?action=launch&silent=true",
                    LastPlayed = File.GetLastAccessTimeUtc(f).Ticks / 10000000 - 62135596800,
                };
            }
        }

        // ---------------- performance mode: Windows "best performance", reset afterwards ----------------
        [DllImport("powrprof.dll")] static extern uint PowerGetActualOverlayScheme(out Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid g);
        static readonly Guid BestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        const string RestoreValue = "PerfRestore";   // in HKCU\Software\WinMorph: what to put back

        public static bool PerfOn => Core.Reg.Get(ShellControl.SettingsKey, RestoreValue) is string;

        public static void TogglePerf()
        {
            if (PerfOn) { RestorePerf(); Notify.Show("Gaming", L("Leistungsmodus aus", "Performance mode off"), L("Energieeinstellung wie vorher.", "Power setting as before.")); return; }
            string restore = null;
            try
            {
                if (PowerGetActualOverlayScheme(out var cur) == 0 && PowerSetActiveOverlayScheme(BestPerformance) == 0) restore = "overlay:" + cur;
            }
            catch { }
            if (restore == null)
            {
                // older or desktop PCs: switch the power plan instead
                var m = Regex.Match(PowerCfg("/getactivescheme"), "[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}");
                if (m.Success && m.Value != HighPerformancePlan) { PowerCfg("/setactive " + HighPerformancePlan); if (Regex.IsMatch(PowerCfg("/getactivescheme"), HighPerformancePlan)) restore = "scheme:" + m.Value; }
            }
            if (restore == null) { Notify.Show("Gaming", L("Leistungsmodus nicht verfügbar", "Performance mode not available"), L("Dieses Gerät erlaubt keine Änderung.", "This device does not allow a change.")); return; }
            using (var k = Core.Reg.Create(ShellControl.SettingsKey)) k.SetValue(RestoreValue, restore);
            Notify.Show("Gaming", L("Leistungsmodus an", "Performance mode on"), L("Windows nutzt jetzt die volle Leistung. Wird beim Beenden zurückgesetzt.", "Windows now uses full performance. Reset when you stop."));
        }

        /// <summary>Puts the power setting back (also after a crash: called when the shell starts).</summary>
        public static void RestorePerf()
        {
            if (!(Core.Reg.Get(ShellControl.SettingsKey, RestoreValue) is string v)) return;
            try
            {
                if (v.StartsWith("overlay:")) PowerSetActiveOverlayScheme(new Guid(v.Substring(8)));
                else if (v.StartsWith("scheme:")) PowerCfg("/setactive " + v.Substring(7));
            }
            catch { }
            using (var k = Core.Reg.Open(ShellControl.SettingsKey, true)) k?.DeleteValue(RestoreValue, false);
        }

        static string PowerCfg(string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000); return o; }
            }
            catch { return ""; }
        }

        /// <summary>Short live values for the bar and the overlay.</summary>
        public static string Stats()
        {
            double gpu = SysInfo.GpuLoad;
            int ping = SysInfo.PingMs;
            return "CPU " + SysInfo.CpuSample().ToString("0") + "%   GPU " + (gpu < 0 ? "–" : gpu.ToString("0") + "%") + "   RAM " + SysStats.Mem().ToString("0") + "%   " + (ping < 0 ? "offline" : ping + " ms");
        }

        // ---------------- overlay: small, click-through, always on top ----------------
        static Window overlay;

        public static void ToggleOverlay(ShellTheme t)
        {
            if (overlay != null) { overlay.Close(); return; }
            var text = new TextBlock { FontFamily = new FontFamily("Bahnschrift"), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B(0xF2F2F5) };
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false, AllowsTransparency = true,
                Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight, Left = 12, Top = 12,
                Content = new Border { Background = B(0x000000, 160), Padding = new Thickness(12, 6, 14, 7), Child = text },
            };
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) =>
            {
                text.Text = Stats() + "   " + DateTime.Now.ToString("HH:mm");
                var h = new WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero) W32.SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // stay above games
            }, w.Dispatcher);
            text.Text = Stats();
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                W32.SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE | 0x20 /* WS_EX_TRANSPARENT: clicks go through */);
            };
            w.Closed += (s, e) => { timer.Stop(); overlay = null; };
            overlay = w;
            w.Show();
        }

        public static void Shutdown() { overlay?.Close(); library?.Close(); RestorePerf(); }

        // ---------------- game library window ----------------
        static Window library;

        public static void Library(ShellTheme t)
        {
            if (library != null) { if (library.WindowState == WindowState.Minimized) library.WindowState = WindowState.Normal; library.Activate(); return; }
            var font = new FontFamily("Bahnschrift");
            Color red = t.Accent, fg = C(0xF2F2F5), sub = C(0x9A96A4);
            var w = new Window { Title = L("Spiele", "Games"), Width = 1080, Height = 700, MinWidth = 600, MinHeight = 400, Background = B(0x0C0C12), WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = font };
            try { w.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/WinMorph.ico")); } catch { }
            var root = new DockPanel { Margin = new Thickness(28, 22, 16, 16) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 12, 18) };
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = L("SPIELE", "GAMES"), FontFamily = font, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = B(fg) });
            var count = new TextBlock { FontFamily = font, FontSize = 13, Foreground = B(sub) };
            titles.Children.Add(count);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var search = new TextBox { Width = 220, Background = B(0x1A1A22), Foreground = B(fg), CaretBrush = B(red), BorderBrush = B(0x2A2A34), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6), FontFamily = font, FontSize = 13, Margin = new Thickness(0, 0, 10, 0) };
            right.Children.Add(search);
            Border Pill(string text, Action a, bool on = false)
            {
                var tb = new TextBlock { Text = text, FontFamily = font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B(on ? C(0xFFFFFF) : fg) };
                var b = Click(tb, a, A(red, 120), on ? B(red) : B(0x1A1A22), new CornerRadius(4), new Thickness(12, 7, 12, 7));
                b.Margin = new Thickness(0, 0, 8, 0);
                return b;
            }
            Border perf = null;
            void PerfButton()
            {
                if (perf != null) right.Children.Remove(perf);
                perf = Pill("⚡ " + L("Leistungsmodus: ", "Performance mode: ") + (PerfOn ? L("an", "on") : L("aus", "off")), () => { TogglePerf(); PerfButton(); }, PerfOn);
                right.Children.Insert(1, perf);
            }
            PerfButton();
            right.Children.Add(Pill("Steam", () => ShellActions.Run("steam://open/games")));
            right.Children.Add(Pill("Epic", () => ShellActions.Run("com.epicgames.launcher://store")));
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            head.Children.Add(titles);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var tiles = new WrapPanel();
            root.Children.Add(new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            w.Content = root;
            List<Game> games = new List<Game>();
            void Fill()
            {
                tiles.Children.Clear();
                var q = search.Text.Trim();
                var shown = games.Where(g => q.Length == 0 || g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                count.Text = games.Count == 0 ? L("Keine Steam- oder Epic-Spiele gefunden.", "No Steam or Epic games found.") : shown.Count + " / " + games.Count + L(" Spiele · Klick startet", " games · click to play");
                foreach (var g in shown) tiles.Children.Add(Tile(g));
            }
            FrameworkElement Tile(Game g)
            {
                var pic = new Border { Width = 230, Height = 108, Background = B(0x1A1A22) };
                ImageSource src = null;
                try
                {
                    if (g.Image != null) { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(g.Image); bi.DecodePixelWidth = 460; bi.EndInit(); bi.Freeze(); src = bi; }
                    else if (g.Icon != null) src = W32.FileIcon(g.Icon);
                }
                catch { }
                if (src != null) pic.Child = new Image { Source = src, Stretch = g.Image != null ? Stretch.UniformToFill : Stretch.None };
                else pic.Child = new TextBlock { Text = g.Name.Substring(0, 1).ToUpperInvariant(), FontFamily = font, FontSize = 44, FontWeight = FontWeights.Bold, Foreground = B(red), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var name = new TextBlock { Text = g.Name, FontFamily = font, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B(fg), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 7, 10, 0) };
                var src2 = new TextBlock { Text = g.Source.ToUpperInvariant(), FontFamily = font, FontSize = 11, Foreground = B(sub), Margin = new Thickness(10, 1, 10, 9) };
                var stack = new StackPanel(); stack.Children.Add(pic); stack.Children.Add(name); stack.Children.Add(src2);
                var card = new Border { Width = 234, Margin = new Thickness(0, 0, 14, 14), Background = B(0x14141B), BorderBrush = B(0x14141B), BorderThickness = new Thickness(2), Child = stack, Cursor = Cursors.Hand, ToolTip = g.Name };
                card.MouseEnter += (s, e) => card.BorderBrush = B(red);
                card.MouseLeave += (s, e) => card.BorderBrush = B(0x14141B);
                card.MouseLeftButtonUp += (s, e) => { ShellActions.Run(g.Launch); Notify.Show("Gaming", L("Starte ", "Starting ") + g.Name, PerfOn ? null : L("Tipp: ⚡ Leistungsmodus für mehr FPS.", "Tip: ⚡ performance mode for more FPS.")); };
                return card;
            }
            search.TextChanged += (s, e) => Fill();
            w.Closed += (s, e) => { WindowTracker.OwnApps.Remove(new WindowInteropHelper(w).Handle); library = null; };
            w.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                WindowTracker.OwnApps.Add(h);
                int on = 1, cap = 0x120C0C, txt = 0xF5F2F2;
                W32.DwmSetWindowAttribute(h, 20, ref on, 4);
                W32.DwmSetWindowAttribute(h, W32.DWMWA_CAPTION_COLOR, ref cap, 4);
                W32.DwmSetWindowAttribute(h, W32.DWMWA_TEXT_COLOR, ref txt, 4);
            };
            count.Text = L("Suche Spiele …", "Looking for games …");
            Task.Run(() => Games()).ContinueWith(tk => { games = tk.Result; Fill(); }, TaskScheduler.FromCurrentSynchronizationContext());
            RenderGuard.Watch(w);
            library = w;
            w.Show();
            w.Activate();
        }
    }
}

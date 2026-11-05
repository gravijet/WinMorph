using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// Drop-down panels of the hacker look (volume, network, system, time, power).
    /// They replace the standard Windows flyouts.
    /// </summary>
    public sealed class HackerPanel : Window
    {
        static HackerPanel open;
        static string openKind;
        static DateTime closedAt;
        readonly ShellTheme t;
        readonly StackPanel body = new StackPanel();
        readonly List<DispatcherTimer> timers = new List<DispatcherTimer>();
        readonly Rect anchor;
        bool done;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;
        Color G => t.Accent;
        Color D => t.BarFgDim;

        public static void Toggle(string kind, ShellTheme t, Rect anchor)
        {
            if (open != null && openKind == kind) { open.Done(); return; }
            if (openKind == kind && (DateTime.Now - closedAt).TotalMilliseconds < 300) return; // the click that just closed it
            open?.Done();
            var p = new HackerPanel(t, anchor);
            switch (kind)
            {
                case "vol": p.BuildVolume(); break;
                case "net": p.BuildNet(); break;
                case "sys": p.BuildSys(); break;
                case "cal": p.BuildCal(); break;
                case "run": p.centered = true; p.BuildRun(); break;
                default: p.BuildPower(); break;
            }
            open = p; openKind = kind;
            p.Show();
            p.Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(p).Handle);
        }

        public static void CloseAll() => open?.Done();

        HackerPanel(ShellTheme theme, Rect anchorDip)
        {
            t = theme; anchor = anchorDip;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
            Background = B(0x000000);
            FontFamily = new FontFamily("Consolas"); FontSize = 13;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = new Border { BorderBrush = B(0x2A2A2A), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 12), Child = body, MinWidth = 340 };
            Deactivated += (s, e) => Done();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Done(); };
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int corner = 1; W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            Loaded += (s, e) => { MonoAll(this); Place(); };
            LayoutUpdated += (s, e) => MonoAll(body);
            SizeChanged += (s, e) => Place();
            Closed += (s, e) => { foreach (var tm in timers) tm.Stop(); };
        }

        static readonly FontFamily MonoFont = new FontFamily("Consolas");

        /// <summary>The app's default text style would use a normal font – the hacker panels are monospace everywhere.</summary>
        static void MonoAll(DependencyObject root)
        {
            if (root is TextBlock tb) { if (tb.FontFamily != MonoFont) tb.FontFamily = MonoFont; return; }
            foreach (var child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject d) MonoAll(d);
        }

        bool centered;

        void Place()
        {
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            double wl = mi.rcWork.Left / s, wt = mi.rcWork.Top / s, wr = mi.rcWork.Right / s, wb = mi.rcWork.Bottom / s;
            if (centered) { Left = wl + (wr - wl - ActualWidth) / 2; Top = wt + (wb - wt) * 0.22; return; }
            double x = anchor.Width > 0 ? anchor.Right - ActualWidth : wr - ActualWidth - 8;
            double y = anchor.Height > 0 && anchor.Bottom <= wt + 4 ? anchor.Bottom + 2 : wt + 2;
            Left = Math.Max(wl + 4, Math.Min(x, wr - ActualWidth - 4));
            Top = Math.Max(wt, Math.Min(y, wb - ActualHeight));
        }

        void Done()
        {
            if (done) return;
            done = true;
            if (open == this) { open = null; closedAt = DateTime.Now; }
            try { Close(); } catch { }
        }

        void Every(double seconds, Action a)
        {
            a();
            var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            tm.Tick += (s, e) => { try { a(); } catch { } };
            tm.Start();
            timers.Add(tm);
        }

        // ---------------- building blocks ----------------
        void Head(string title)
        {
            body.Children.Add(new TextBlock { Text = "── " + title + " " + new string('─', Math.Max(4, 34 - title.Length)), Foreground = B(D), Margin = new Thickness(0, 0, 0, 8) });
        }

        TextBlock Line(string text = "", Color? c = null, double size = 13, Thickness? margin = null)
        {
            var tb = new TextBlock { Text = text, Foreground = B(c ?? G), FontSize = size, Margin = margin ?? new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.NoWrap };
            body.Children.Add(tb);
            return tb;
        }

        Border Btn(string text, Action a)
        {
            var tb = new TextBlock { Text = "[ " + text + " ]", Foreground = B(G) };
            var b = Click(tb, a, G, pad: new Thickness(0, 2, 0, 2));
            b.MouseEnter += (s, e) => tb.Foreground = B(0x000000);
            b.MouseLeave += (s, e) => tb.Foreground = B(G);
            b.Margin = new Thickness(0, 0, 8, 0);
            return b;
        }

        WrapPanel Buttons(params (string text, Action a)[] items)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), MaxWidth = 420 };
            foreach (var (text, a) in items) w.Children.Add(Btn(text, a));
            body.Children.Add(w);
            return w;
        }

        void Sep() => body.Children.Add(new TextBlock { Text = new string('─', 38), Foreground = B(A(D, 160)), Margin = new Thickness(0, 6, 0, 6) });

        static string Bar(double pct, int cells) => "[" + SysInfo.Bar(pct, cells, '|', '·') + "]";

        // ================= volume =================
        void BuildVolume()
        {
            Head(L("audio", "audio"));
            var dev = Line("dev  " + Short(Audio.DeviceName, 32), D);
            var cells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var parts = new List<Border>();
            TextBlock info = null;
            for (int i = 0; i < 25; i++)
            {
                int level = (i + 1) * 4;
                var c = new Border { Width = 11, Height = 20, Margin = new Thickness(0, 0, 2, 0) };
                c.MouseLeftButtonDown += (s, e) => { Audio.Level = level; Refresh(); };
                parts.Add(c); cells.Children.Add(c);
            }
            body.Children.Add(cells);
            info = Line("", G, 15);
            void Refresh()
            {
                int lvl = Audio.Level; bool mute = Audio.Muted;
                for (int i = 0; i < parts.Count; i++) parts[i].Background = B((i + 1) * 4 <= lvl ? (mute ? A(D, 200) : G) : A(D, 60));
                info.Text = lvl < 0 ? L("kein Ausgabegerät", "no output device") : (mute ? "muted  " : "vol    ") + lvl + "%";
                Osd.Volume(t, lvl, mute, show: false);
            }
            cells.MouseWheel += (s, e) => { Audio.Change(e.Delta > 0 ? 2 : -2); Refresh(); };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Left || e.Key == Key.Down) { Audio.Change(-5); Refresh(); e.Handled = true; }
                else if (e.Key == Key.Right || e.Key == Key.Up) { Audio.Change(5); Refresh(); e.Handled = true; }
                else if (e.Key == Key.M) { Audio.Muted = !Audio.Muted; Refresh(); e.Handled = true; }
            };
            Buttons(("-5", () => { Audio.Change(-5); Refresh(); }), ("+5", () => { Audio.Change(5); Refresh(); }), (L("stumm", "mute"), () => { Audio.Muted = !Audio.Muted; Refresh(); }),
                    ("0", () => { Audio.Level = 0; Refresh(); }), ("50", () => { Audio.Level = 50; Refresh(); }), ("100", () => { Audio.Level = 100; Refresh(); }));
            Line(L("← → ändern · M stumm · Mausrad", "← → change · M mute · mouse wheel"), D, 11.5, new Thickness(0, 8, 0, 0));
            Buttons((L("mixer", "mixer"), () => { Done(); ShellActions.Run("sndvol.exe"); }));
            Every(1, Refresh);
        }

        // ================= run (Win+R) =================
        static string RunHistoryFile => System.IO.Path.Combine(Core.Backup.DataDir, "run-history.txt");

        void BuildRun()
        {
            Head(L("ausführen", "run"));
            Line(L("programm, ordner, dokument oder internetadresse", "program, folder, document or internet address"), D, 12);
            var hist = new List<string>();
            try { if (System.IO.File.Exists(RunHistoryFile)) hist = System.IO.File.ReadAllLines(RunHistoryFile).Where(x => x.Trim().Length > 0).ToList(); } catch { }
            int pos = hist.Count;
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Width = 460 };
            var promptTb = new TextBlock { Text = "run> ", Foreground = B(G), VerticalAlignment = VerticalAlignment.Center, FontSize = 15 };
            DockPanel.SetDock(promptTb, Dock.Left);
            row.Children.Add(promptTb);
            var box = new TextBox
            {
                Background = Brushes.Transparent, Foreground = B(G), CaretBrush = B(G), BorderThickness = new Thickness(0), FontFamily = MonoFont, FontSize = 15,
                SelectionBrush = B(A(G, 120)), Text = hist.LastOrDefault() ?? "",
            };
            row.Children.Add(box);
            body.Children.Add(new Border { BorderBrush = B(A(G, 140)), BorderThickness = new Thickness(0, 0, 0, 1), Child = row, Padding = new Thickness(0, 0, 0, 4) });
            var err = Line("", C(0xFF4D4D), 12);
            err.TextWrapping = TextWrapping.Wrap; err.MaxWidth = 460;
            Line(L("enter = ausführen · strg+umschalt+enter = als admin · ↑↓ verlauf · esc", "enter = run · ctrl+shift+enter = as admin · ↑↓ history · esc"), D, 11.5, new Thickness(0, 6, 0, 0));
            void Go(bool admin)
            {
                var text = box.Text.Trim();
                if (text.Length == 0) return;
                var e = RunCommand(text, admin);
                if (e != null) { err.Text = e; box.SelectAll(); return; }
                hist.Remove(text); hist.Add(text);
                try { System.IO.File.WriteAllLines(RunHistoryFile, hist.Skip(Math.Max(0, hist.Count - 30))); } catch { }
                Done();
            }
            box.PreviewKeyDown += (s2, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Go(Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }
                else if (e.Key == Key.Up && hist.Count > 0) { e.Handled = true; pos = Math.Max(0, pos - 1); box.Text = hist[pos]; box.CaretIndex = box.Text.Length; }
                else if (e.Key == Key.Down && hist.Count > 0) { e.Handled = true; pos = Math.Min(hist.Count, pos + 1); box.Text = pos < hist.Count ? hist[pos] : ""; box.CaretIndex = box.Text.Length; }
            };
            box.TextChanged += (s2, e) => err.Text = "";
            Buttons(("ok", () => Go(false)), (L("als admin", "as admin"), () => Go(true)), (L("abbrechen", "cancel"), Done));
            Loaded += (s2, e) => { box.Focus(); box.SelectAll(); };
        }

        /// <summary>Starts a command line like the Windows Run box. Returns null or an error text.</summary>
        internal static string RunCommand(string text, bool admin)
        {
            var cmd = Environment.ExpandEnvironmentVariables(text.Trim());
            string file = cmd, args = "";
            if (cmd.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) file = "https://" + cmd;
            else if (!(System.IO.File.Exists(cmd) || System.IO.Directory.Exists(cmd) || cmd.Contains("://") || cmd.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)))
            {
                if (cmd.StartsWith("\"")) { int end = cmd.IndexOf('"', 1); if (end > 0) { file = cmd.Substring(1, end - 1); args = cmd.Substring(end + 1).Trim(); } }
                else { int sp = cmd.IndexOf(' '); if (sp > 0) { file = cmd.Substring(0, sp); args = cmd.Substring(sp + 1).Trim(); } }
            }
            try
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
                if (admin) psi.Verb = "runas";
                Process.Start(psi)?.Dispose();
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; }   // UAC prompt cancelled
            catch (Exception ex) { return "\"" + text + "\": " + ex.Message; }
        }

        // ================= network =================
        void BuildNet()
        {
            Head(L("netzwerk", "network"));
            var rate = Line("", G, 15);
            var spark = Line("", D);
            var down = new Queue<double>();
            var list = new StackPanel();
            body.Children.Add(list);
            var wifiBox = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            body.Children.Add(wifiBox);
            var output = Line("", D);
            output.TextWrapping = TextWrapping.Wrap; output.MaxWidth = 420;
            Every(1, () =>
            {
                SysInfo.NetRate(out double rx, out double tx);
                rate.Text = "↓ " + SysInfo.Bytes(rx).PadLeft(6) + "/s     ↑ " + SysInfo.Bytes(tx).PadLeft(6) + "/s";
                down.Enqueue(rx); while (down.Count > 40) down.Dequeue();
                double max = Math.Max(1, down.Max());
                spark.Text = SysInfo.Sparkline(down.Select(v => v / max * 100));
            });
            list.Children.Clear();
            foreach (var ni in SysInfo.Adapters())
            {
                var ip = ni.GetIPProperties();
                var v4 = ip.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "-";
                var gw = ip.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "-";
                var dns = string.Join(" ", ip.DnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Take(2));
                string speed = ni.Speed > 0 ? (ni.Speed >= 1000000000 ? (ni.Speed / 1000000000.0).ToString("0.#") + " Gbit/s" : (ni.Speed / 1000000) + " Mbit/s") : "";
                list.Children.Add(new TextBlock { Text = "", Margin = new Thickness(0, 4, 0, 0) });
                list.Children.Add(new TextBlock { Text = Short(ni.Name.ToLowerInvariant(), 20) + "  " + speed, Foreground = B(G) });
                list.Children.Add(new TextBlock { Text = "  inet " + v4 + "   gw " + gw, Foreground = B(D) });
                if (dns.Length > 0) list.Children.Add(new TextBlock { Text = "  dns  " + dns, Foreground = B(D) });
            }
            if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = L("offline – keine Verbindung", "offline – no connection"), Foreground = B(C(0xFF3B3B)) });
            BuildWifi(wifiBox);
            void Out(string s) => Dispatcher.BeginInvoke(new Action(() => output.Text = s));
            Buttons(("ping 1.1.1.1", () => { output.Text = "ping …"; Task.Run(() => { try { var p = new System.Net.NetworkInformation.Ping(); var list2 = new List<string>(); for (int i = 0; i < 3; i++) { var r = p.Send("1.1.1.1", 1500); list2.Add(r.Status == System.Net.NetworkInformation.IPStatus.Success ? r.RoundtripTime + " ms" : r.Status.ToString()); } Out("1.1.1.1: " + string.Join(" · ", list2)); } catch (Exception ex) { Out(ex.Message); } }); }),
                    (L("öffentliche ip", "public ip"), () => { output.Text = "…"; Task.Run(() => { try { using (var c = new System.Net.WebClient()) { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; Out("public ip: " + c.DownloadString("https://api.ipify.org").Trim()); } } catch (Exception ex) { Out(ex.Message); } }); }),
                    ("flush dns", () => { Task.Run(() => Out(RunQuiet("ipconfig", "/flushdns").Trim().Split('\n').Last())); }),
                    (L("adapter", "adapters"), () => { Done(); ShellActions.Run("ncpa.cpl"); }));
        }

        // ---------- Wi-Fi: see, disconnect, connect ----------
        bool askingPassword;

        void BuildWifi(StackPanel box)
        {
            void Draw(List<Wlan.Net> nets, string msg = null)
            {
                if (askingPassword) return;
                box.Children.Clear();
                box.Children.Add(new TextBlock { Text = "── wlan " + new string('─', 30), Foreground = B(D), Margin = new Thickness(0, 4, 0, 4) });
                if (nets == null) { box.Children.Add(new TextBlock { Text = L("kein wlan-adapter", "no wi-fi adapter"), Foreground = B(D) }); return; }
                foreach (var n in nets.Take(8))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var net = n;
                    var btn = n.Connected ? Btn(L("trennen", "disconnect"), () => { Wlan.Disconnect(); Refresh(L("getrennt", "disconnected")); })
                                          : Btn(L("verbinden", "connect"), () => Join(net));
                    btn.Margin = new Thickness(8, 0, 0, 0);
                    DockPanel.SetDock(btn, Dock.Right);
                    row.Children.Add(btn);
                    string bars = n.Signal >= 75 ? "▂▄▆█" : n.Signal >= 50 ? "▂▄▆ " : n.Signal >= 25 ? "▂▄  " : "▂   ";
                    row.Children.Add(new TextBlock
                    {
                        Text = (n.Connected ? "● " : "  ") + Short(n.Ssid, 22).PadRight(22) + " " + bars + " " + n.Signal.ToString().PadLeft(3) + "%" + (n.Secured ? "  🔒" : "") + (n.Profile != null && !n.Connected ? "  ★" : ""),
                        Foreground = B(n.Connected ? G : Core.Applier.Mix(G, D, 0.4)), VerticalAlignment = VerticalAlignment.Center,
                    });
                    box.Children.Add(row);
                }
                if (nets.Count == 0) box.Children.Add(new TextBlock { Text = L("keine netze gefunden", "no networks found"), Foreground = B(D) });
                if (msg != null) box.Children.Add(new TextBlock { Text = msg, Foreground = B(msg.StartsWith("!") ? C(0xFF4D4D) : D), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
                var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                w.Children.Add(Btn(L("suchen", "scan"), () => { Wlan.Scan(); Refresh(L("suche …", "scanning …"), 3500); }));
                w.Children.Add(Btn(L("wlan-einstellungen", "wi-fi settings"), () => { Done(); ShellActions.Run("ms-settings:network-wifi"); }));
                box.Children.Add(w);
            }
            void Refresh(string msg = null, int delayMs = 1500)
            {
                if (msg != null) Draw(Wlan.Available ? Wlan.Networks() : null, msg);
                Task.Delay(delayMs).ContinueWith(_ => { var n = Wlan.Available ? Wlan.Networks() : null; Dispatcher.BeginInvoke(new Action(() => Draw(n))); });
            }
            void Join(Wlan.Net n)
            {
                if (n.Profile != null) { var e = Wlan.Connect(n.Profile); Refresh(e == null ? L("verbinde mit ", "connecting to ") + n.Ssid + " …" : "! " + e, 4000); return; }
                if (!n.Personal) { Done(); ShellActions.Run("ms-settings:network-wifi"); return; }   // enterprise login: Windows asks for user + password
                if (!n.Secured) { var e = Wlan.ConnectNew(n, null); Refresh(e == null ? L("verbinde …", "connecting …") : "! " + e, 4000); return; }
                // password line inside the panel
                askingPassword = true;
                box.Children.Clear();
                box.Children.Add(new TextBlock { Text = "── wlan " + new string('─', 30), Foreground = B(D), Margin = new Thickness(0, 4, 0, 4) });
                box.Children.Add(new TextBlock { Text = L("passwort für ", "password for ") + n.Ssid + ":", Foreground = B(G) });
                var pw = new PasswordBox { Background = Brushes.Transparent, Foreground = B(G), CaretBrush = B(G), BorderBrush = B(A(G, 140)), BorderThickness = new Thickness(0, 0, 0, 1), FontFamily = MonoFont, FontSize = 14, Margin = new Thickness(0, 4, 0, 4), Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
                box.Children.Add(pw);
                void Ok()
                {
                    askingPassword = false;
                    var e = Wlan.ConnectNew(n, pw.Password);
                    pw.Clear();
                    Refresh(e == null ? L("verbinde mit ", "connecting to ") + n.Ssid + " …" : "! " + e, 5000);
                }
                pw.PreviewKeyDown += (s2, e2) => { if (e2.Key == Key.Enter) { e2.Handled = true; Ok(); } else if (e2.Key == Key.Escape) { e2.Handled = true; askingPassword = false; Refresh(""); } };
                var w = new WrapPanel();
                w.Children.Add(Btn("ok", Ok));
                w.Children.Add(Btn(L("abbrechen", "cancel"), () => { askingPassword = false; Refresh(""); }));
                box.Children.Add(w);
                pw.Focus();
            }
            Draw(Wlan.Available ? Wlan.Networks() : null);
            Task.Run(() => Wlan.Scan());   // fresh list a moment later
            Refresh(null, 3500);
            var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            tm.Tick += (s2, e2) => { if (!askingPassword) Task.Run(() => Wlan.Available ? Wlan.Networks() : null).ContinueWith(r => Dispatcher.BeginInvoke(new Action(() => Draw(r.Result)))); };
            tm.Start();
            timers.Add(tm);
        }

        internal static string RunQuiet(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage) };
                using (var p = Process.Start(psi)) { var s = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000); return s; }
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ================= system monitor (htop) =================
        void BuildSys()
        {
            Head(L("system", "system"));
            var cpu = Line(); var hist = Line("", D); var mem = Line(); var disks = Line(); var up = Line("", D);
            Line("");
            var header = Line("  PID  " + "NAME".PadRight(20) + "  CPU%     MEM", D);
            var rows = new StackPanel();
            body.Children.Add(rows);
            var status = Line("", D, 11.5);
            Every(1, () =>
            {
                double c = SysInfo.CpuSample(), m = SysStats.Mem();
                cpu.Text = "cpu  " + Bar(c, 22) + " " + c.ToString("0").PadLeft(3) + "%";
                hist.Text = "     " + SysInfo.Sparkline(SysInfo.CpuHistory.Skip(Math.Max(0, SysInfo.CpuHistory.Count - 24)));
                mem.Text = "mem  " + Bar(m, 22) + " " + m.ToString("0").PadLeft(3) + "%";
                disks.Text = string.Join("\n", SysInfo.Disks().Take(3).Select(d => { double u = 100 - d.TotalFreeSpace * 100.0 / Math.Max(1, d.TotalSize); return d.Name.Substring(0, 2).ToLower() + "   " + Bar(u, 22) + " " + u.ToString("0").PadLeft(3) + "%"; }));
                up.Text = "up " + SysInfo.UptimeText + "   " + SysInfo.Os.ToLowerInvariant();
            });
            Dictionary<int, TimeSpan> lastCpu = null; DateTime lastAt = DateTime.Now;
            bool busy = false;
            Every(2, () =>
            {
                if (busy) return;
                busy = true;
                Task.Run(() =>
                {
                    var now = DateTime.Now;
                    var cur = new Dictionary<int, TimeSpan>();
                    var info = new List<(int pid, string name, double cpu, long mem)>();
                    double sec = Math.Max(0.2, (now - lastAt).TotalSeconds);
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            var ct = p.TotalProcessorTime; cur[p.Id] = ct;
                            double pc = lastCpu != null && lastCpu.TryGetValue(p.Id, out var old) ? (ct - old).TotalSeconds / sec * 100 / Environment.ProcessorCount : 0;
                            info.Add((p.Id, p.ProcessName, pc, p.WorkingSet64));
                        }
                        catch { try { info.Add((p.Id, p.ProcessName, 0, p.WorkingSet64)); } catch { } }
                        finally { p.Dispose(); }
                    }
                    lastCpu = cur; lastAt = now;
                    var top = info.OrderByDescending(x => x.cpu).ThenByDescending(x => x.mem).Take(12).ToList();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        busy = false;
                        rows.Children.Clear();
                        foreach (var x in top)
                        {
                            var row = new DockPanel();
                            var pid = x.pid; var name = x.name;
                            var kill = Btn("kill", () => { status.Text = Kill(pid, name); });
                            kill.Margin = new Thickness(8, 0, 0, 0);
                            DockPanel.SetDock(kill, Dock.Right);
                            row.Children.Add(kill);
                            row.Children.Add(new TextBlock { Text = x.pid.ToString().PadLeft(5) + "  " + Short(x.name, 20).PadRight(20) + "  " + x.cpu.ToString("0.0").PadLeft(5) + "  " + SysInfo.Bytes(x.mem).PadLeft(6), Foreground = B(x.cpu > 20 ? C(0xFFB000) : G), VerticalAlignment = VerticalAlignment.Center });
                            rows.Children.Add(row);
                        }
                        status.Text = status.Text.Length == 0 ? info.Count + L(" Prozesse", " processes") : status.Text;
                    }));
                });
            });
            Buttons((L("task-manager", "task manager"), () => { Done(); ShellActions.TaskManager(); }), (L("ressourcen", "resources"), () => { Done(); ShellActions.Run("resmon.exe"); }));
        }

        static readonly string[] Protected = { "system", "idle", "csrss", "wininit", "winlogon", "lsass", "smss", "services", "svchost", "dwm", "fontdrvhost", "registry", "memory compression", "secure system" };

        internal static string Kill(int pid, string name)
        {
            if (Protected.Contains(name.ToLowerInvariant())) return L("geschützt: ", "protected: ") + name;
            if (pid == Process.GetCurrentProcess().Id) return L("das bin ich", "that's me");
            try { using (var p = Process.GetProcessById(pid)) { p.Kill(); } return "killed " + name + " (" + pid + ")"; }
            catch (Exception ex) { return "kill " + name + ": " + ex.Message; }
        }

        // ================= time / calendar =================
        void BuildCal()
        {
            var ci = De ? new CultureInfo("de-DE") : new CultureInfo("en-US");
            Head(L("zeit", "time"));
            var big = Line("", G, 38);
            big.FontWeight = FontWeights.Bold;
            var date = Line("", D);
            Line("");
            var month = DateTime.Today;
            var cal = new TextBlock { FontSize = 14, Margin = new Thickness(0, 2, 0, 2), LineHeight = 21 };
            body.Children.Add(cal);
            void Draw()
            {
                cal.Inlines.Clear();
                string title = month.ToString("MMMM yyyy", ci).ToLowerInvariant();
                cal.Inlines.Add(new Run(title.PadLeft((27 + title.Length) / 2) + "\n") { Foreground = B(G) });
                var names = De ? "mo di mi do fr sa so" : "mo tu we th fr sa su";
                cal.Inlines.Add(new Run(" " + names.Replace(" ", "  ") + "\n") { Foreground = B(D) });
                var first = new DateTime(month.Year, month.Month, 1);
                int offset = ((int)first.DayOfWeek + 6) % 7;
                cal.Inlines.Add(new Run(new string(' ', offset * 4)));
                int days = DateTime.DaysInMonth(month.Year, month.Month);
                for (int d = 1; d <= days; d++)
                {
                    bool today = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month && d == DateTime.Today.Day;
                    var text = d.ToString().PadLeft(2);
                    cal.Inlines.Add(new Run(" "));
                    cal.Inlines.Add(new Run(text) { Foreground = B(today ? C(0x000000) : (((offset + d - 1) % 7) >= 5 ? D : G)), Background = today ? B(G) : null });
                    cal.Inlines.Add(new Run(" "));
                    if ((offset + d) % 7 == 0 && d < days) cal.Inlines.Add(new Run("\n"));
                }
            }
            Draw();
            Buttons(("<", () => { month = month.AddMonths(-1); Draw(); }), (L("heute", "today"), () => { month = DateTime.Today; Draw(); }), (">", () => { month = month.AddMonths(1); Draw(); }));
            Sep();
            var up = Line("", D);
            Every(1, () =>
            {
                var now = DateTime.Now;
                big.Text = now.ToString("HH:mm:ss");
                int week = ci.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                date.Text = now.ToString(De ? "dddd, d. MMMM yyyy" : "dddd, MMMM d yyyy", ci).ToLowerInvariant() + L("  · kw ", "  · wk ") + week;
                up.Text = "uptime " + SysInfo.UptimeText + "   utc " + DateTime.UtcNow.ToString("HH:mm");
            });
        }

        // ================= power =================
        void BuildPower()
        {
            Head(L("energie", "power"));
            var list = new StackPanel();
            body.Children.Add(list);
            void Item(string key, string text, Action a, string confirm = null)
            {
                var tb = new TextBlock { Text = " > " + text.PadRight(26) + key, Foreground = B(G), FontSize = 14 };
                var b = Click(tb, () =>
                {
                    if (confirm == null) { Done(); a(); return; }
                    Confirm(confirm, a);
                }, G, pad: new Thickness(2, 4, 8, 4));
                b.MouseEnter += (s, e) => tb.Foreground = B(0x000000);
                b.MouseLeave += (s, e) => tb.Foreground = B(G);
                list.Children.Add(b);
            }
            Item("[l]", L("sperren", "lock"), ShellActions.Lock);
            Item("[s]", L("energie sparen", "sleep"), ShellActions.Sleep);
            if (Hibernate) Item("[h]", L("ruhezustand", "hibernate"), () => ShellActions.Run("shutdown.exe", "/h"));
            Item("[o]", L("abmelden", "sign out"), () => ShellActions.Run("shutdown.exe", "/l"), L("abmelden?", "sign out?"));
            Item("[r]", L("neu starten", "restart"), () => ShellActions.Run("shutdown.exe", "/r /t 0"), L("neu starten?", "restart?"));
            Item("[p]", L("herunterfahren", "shut down"), () => ShellActions.Run("shutdown.exe", "/s /t 0"), L("herunterfahren?", "shut down?"));
            list.Children.Add(new TextBlock { Text = new string('─', 38), Foreground = B(A(D, 160)), Margin = new Thickness(0, 6, 0, 6) });
            Item("", L("winmorph (looks)", "winmorph (looks)"), ShellActions.WinMorphApp);
            Item("", L("original-windows zurück", "restore original windows"), ShellActions.Restore);
            if (SysStats.Battery(out int pct, out bool ac)) Line((ac ? L("netzteil · ", "plugged in · ") : L("akku · ", "battery · ")) + pct + "%  " + Bar(pct, 20), pct < 20 && !ac ? C(0xFF3B3B) : D, 13, new Thickness(0, 10, 0, 0));
            Line(L("taste drücken: l s o r p", "press a key: l s o r p"), D, 11.5, new Thickness(0, 8, 0, 0));
            PreviewKeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.L: Done(); ShellActions.Lock(); break;
                    case Key.S: Done(); ShellActions.Sleep(); break;
                    case Key.H: if (Hibernate) { Done(); ShellActions.Run("shutdown.exe", "/h"); } break;
                    case Key.O: Confirm(L("abmelden?", "sign out?"), () => ShellActions.Run("shutdown.exe", "/l")); break;
                    case Key.R: Confirm(L("neu starten?", "restart?"), () => ShellActions.Run("shutdown.exe", "/r /t 0")); break;
                    case Key.P: Confirm(L("herunterfahren?", "shut down?"), () => ShellActions.Run("shutdown.exe", "/s /t 0")); break;
                }
            };
        }

        bool confirming;
        void Confirm(string question, Action a)
        {
            if (confirming) return;
            confirming = true;
            body.Children.Clear();
            Head(L("bestätigen", "confirm"));
            Line(question, C(0xFFB000), 16);
            Line(L("offene programme werden geschlossen. ungespeichertes geht verloren.", "open programs will close. unsaved work is lost."), D, 12);
            var w = Buttons((L("ja [y]", "yes [y]"), () => { Done(); a(); }), (L("nein [n]", "no [n]"), Done));
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Y || e.Key == Key.Z || e.Key == Key.J || e.Key == Key.Enter) { e.Handled = true; Done(); a(); }
                else if (e.Key == Key.N) { e.Handled = true; Done(); }
            };
        }

        static bool? hib;
        static bool Hibernate
        {
            get
            {
                if (hib != null) return hib.Value;
                try { hib = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 0) is int v && v == 1; } catch { hib = false; }
                return hib.Value;
            }
        }

        static string Short(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");
    }

    /// <summary>On-screen display for volume and notices (replaces the Windows volume pop-up).</summary>
    internal static class Osd
    {
        static Window w;
        static TextBlock tb;
        static DispatcherTimer hide;

        public static void Volume(ShellTheme t, int level, bool muted, bool show = true)
        {
            if (!show || level < 0) return;
            Show(t, (muted ? "mute " : "vol  ") + "[" + SysInfo.Bar(level, 25, '|', '·') + "] " + level.ToString().PadLeft(3) + "%");
        }

        public static void Show(ShellTheme t, string text, double seconds = 1.4)
        {
            if (w == null)
            {
                tb = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 16, Foreground = B(t.Accent), Margin = new Thickness(18, 10, 18, 10) };
                w = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight,
                    Background = B(0x000000), Content = new Border { BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1), Child = tb },
                };
                RenderGuard.Watch(w);
                w.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(w).Handle;
                    SetWindowLong(h, W32.GWL_EXSTYLE, W32.GetWindowLong(h, W32.GWL_EXSTYLE) | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE | 0x20);
                    int corner = 1; W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
                };
                hide = new DispatcherTimer();
                hide.Tick += (s, e) => { hide.Stop(); w.Hide(); };
            }
            tb.Text = text;
            if (!w.IsVisible) w.Show();
            w.UpdateLayout();
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
            w.Left = mi.rcWork.Left / sc + ((mi.rcWork.Right - mi.rcWork.Left) / sc - w.ActualWidth) / 2;
            w.Top = mi.rcWork.Top / sc + 40;
            hide.Stop(); hide.Interval = TimeSpan.FromSeconds(seconds); hide.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The start menu of the hacker look: a drop-down terminal with its own commands,
    /// app launcher, real cmd/PowerShell commands, history and Tab completion.
    /// </summary>
    public sealed partial class HackerConsole : Window
    {
        readonly ShellTheme t;
        readonly Func<List<WinInfo>> windows;
        readonly TextBlock output = new TextBlock();
        readonly ScrollViewer scroll;
        readonly TextBox input;
        readonly TextBlock prompt, hints, clock;
        readonly List<string> history = new List<string>();
        int histPos = -1, lineCount;
        string cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Process running;
        Action<string> answer;
        bool closing, bannerShown;
        List<string> tabList; int tabIndex; string tabBase;
        public event Action Hidden;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;
        Color G => t.Accent;
        Color D => t.BarFgDim;
        static readonly Color White = Color.FromRgb(0xD8, 0xFF, 0xE0), Amber = Color.FromRgb(0xFF, 0xB0, 0x00), Red = Color.FromRgb(0xFF, 0x4D, 0x4D), Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
        static string User => Environment.UserName.ToLowerInvariant().Replace(' ', '_');
        static string Host => Environment.MachineName.ToLowerInvariant();

        static readonly FontFamily Mono = new FontFamily("Consolas");
        sealed class Cmd { public string Name, De, En; public string[] Alias; public Action<string> Run; }
        readonly List<Cmd> cmds = new List<Cmd>();

        public HackerConsole(ShellTheme theme, Func<List<WinInfo>> windows)
        {
            t = theme; this.windows = windows;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            Background = B(0x000000); UseLayoutRounding = true;
            FontFamily = new FontFamily("Consolas"); FontSize = 14;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Left = -20000; Top = -20000; Width = 900; Height = 520;

            var head = new DockPanel { Margin = new Thickness(12, 6, 12, 6) };
            clock = new TextBlock { Foreground = B(D), FontFamily = Mono };
            DockPanel.SetDock(clock, Dock.Right);
            head.Children.Add(clock);
            head.Children.Add(new TextBlock { Text = "winmorph-sh  ·  " + User + "@" + Host + "  ·  " + L("esc schließt · help zeigt alles", "esc closes · help lists everything"), Foreground = B(D), FontFamily = Mono });
            var headBorder = new Border { Child = head, BorderBrush = B(A(G, 90)), BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(headBorder, Dock.Top);

            output.FontFamily = Mono; output.FontSize = 14;
            output.Foreground = B(G); output.TextWrapping = TextWrapping.Wrap; output.Margin = new Thickness(14, 8, 14, 4); output.LineHeight = 19;
            scroll = new ScrollViewer { Content = output, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

            prompt = new TextBlock { Foreground = B(G), VerticalAlignment = VerticalAlignment.Center, FontFamily = Mono, FontSize = 15 };
            input = new TextBox { Background = Brushes.Transparent, Foreground = B(White), CaretBrush = B(G), BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
            input.SelectionBrush = B(A(G, 120));
            var promptRow = new DockPanel { Margin = new Thickness(14, 4, 14, 2) };
            DockPanel.SetDock(prompt, Dock.Left);
            promptRow.Children.Add(prompt); promptRow.Children.Add(input);
            hints = new TextBlock { FontFamily = Mono, Foreground = B(D), Margin = new Thickness(14, 0, 14, 8), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
            var bottom = new StackPanel();
            bottom.Children.Add(new Border { Height = 1, Background = B(A(G, 60)) });
            BuildSuggest();
            bottom.Children.Add(promptRow); bottom.Children.Add(suggest); bottom.Children.Add(hints);
            DockPanel.SetDock(bottom, Dock.Bottom);

            var dock = new DockPanel();
            dock.Children.Add(headBorder); dock.Children.Add(bottom); dock.Children.Add(scroll);
            var rootBorder = new Border { BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1), Child = dock };
            TextElement.SetFontFamily(rootBorder, new FontFamily("Consolas"));
            TextElement.SetFontSize(rootBorder, 14);
            Content = rootBorder;

            input.PreviewKeyDown += OnKey;
            input.TextChanged += (s, e) => { if (tabList == null || !tabList.Contains(input.Text)) { tabList = null; } sel = 0; RefreshSuggest(); UpdateHints(); };
            Deactivated += (s, e) => SafeHide();
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int corner = 1; W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => { if (IsVisible) clock.Text = DateTime.Now.ToString("HH:mm:ss"); }, Dispatcher);
            Register();
            UpdatePrompt();
        }

        // ---------------- show / hide ----------------
        public bool IsOpen => IsVisible && !closing;

        public void Open(string text = null)
        {
            closing = false;
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            double wl = mi.rcWork.Left / s, wt = mi.rcWork.Top / s, ww = (mi.rcWork.Right - mi.rcWork.Left) / s, wh = (mi.rcWork.Bottom - mi.rcWork.Top) / s;
            Width = Math.Min(1180, Math.Max(760, ww * 0.62)); Height = Math.Min(720, Math.Max(420, wh * 0.6));
            Left = wl + (ww - Width) / 2; Top = wt;
            if (!IsVisible) Show();
            Activate();
            W32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            input.Focus(); Keyboard.Focus(input);
            if (!bannerShown) { bannerShown = true; Banner(); }
            if (!string.IsNullOrEmpty(text)) { input.Text = text; input.CaretIndex = text.Length; }
            clock.Text = DateTime.Now.ToString("HH:mm:ss");
            if (!RenderGuard.Software && Content is FrameworkElement c)
            {
                var tt = new TranslateTransform(0, -18);
                c.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            ScrollEnd();
        }

        public void SafeHide()
        {
            if (closing || !IsVisible) return;
            closing = true;
            try { Hide(); } catch { }
            Hidden?.Invoke();
        }

        /// <summary>Sample content for the preview picture in the app.</summary>
        public void PreviewFill()
        {
            bannerShown = true;
            Banner();
            Print(PromptText + "neofetch", White);
            Neofetch();
            input.Text = "fire";
            clock.Text = DateTime.Now.ToString("HH:mm:ss");
            UpdateHints();
        }

        // ---------------- output ----------------
        void Print(string text, Color? c = null)
        {
            output.Inlines.Add(new Run(text) { Foreground = B(c ?? G) });
            output.Inlines.Add(new LineBreak());
            lineCount += 1 + text.Count(ch => ch == '\n');
            while (lineCount > 700 && output.Inlines.Count > 2)
            {
                var first = output.Inlines.FirstInline;
                if (first is Run r) lineCount -= 1 + r.Text.Count(ch => ch == '\n');
                output.Inlines.Remove(first);
                if (output.Inlines.FirstInline is LineBreak lb) output.Inlines.Remove(lb);
            }
            ScrollEnd();
        }

        void PrintParts(params (string text, Color c)[] parts)
        {
            foreach (var (text, c) in parts) output.Inlines.Add(new Run(text) { Foreground = B(c) });
            output.Inlines.Add(new LineBreak());
            lineCount++;
            ScrollEnd();
        }

        void Async(string text, Color? c = null) => Dispatcher.BeginInvoke(new Action(() => Print(text, c)));
        void ScrollEnd() => Dispatcher.BeginInvoke(new Action(() => scroll.ScrollToEnd()), DispatcherPriority.Background);

        string PromptText => User + "@" + Host + ":" + ShortPath(cwd) + "$ ";
        void UpdatePrompt() => prompt.Text = answer != null ? "? " : PromptText;

        static string ShortPath(string p)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (p.StartsWith(home, StringComparison.OrdinalIgnoreCase)) p = "~" + p.Substring(home.Length);
            p = p.Replace('\\', '/');
            return p.Length > 40 ? "…" + p.Substring(p.Length - 39) : p;
        }

        void Banner()
        {
            string[] art =
            {
                "██╗    ██╗███╗   ███╗",
                "██║    ██║████╗ ████║",
                "██║ █╗ ██║██╔████╔██║",
                "██║███╗██║██║╚██╔╝██║",
                "╚███╔███╔╝██║ ╚═╝ ██║",
                " ╚══╝╚══╝ ╚═╝     ╚═╝",
            };
            foreach (var a in art) Print(" " + a, G);
            Print("");
            PrintParts((" winmorph-sh " + Core.Updater.Version, White), ("  ·  " + SysInfo.Os.ToLowerInvariant(), D));
            Print(L(" App-Namen tippen + Enter = starten · die Liste zeigt, was Enter macht · ↑↓ wählen · 'help' = alle Befehle",
                    " type an app name + enter to launch · the list shows what enter does · ↑↓ choose · 'help' = all commands"), D);
            Print(L(" !befehl = echter cmd-Befehl · '?' + Frage → ChatGPT · =2+2 rechnet", " !command = real cmd command · '?' + question → ChatGPT · =2+2 calculates"), D);
            Print("");
        }

        // ---------------- keys ----------------
        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            switch (e.Key)
            {
                case Key.Enter: e.Handled = true; RunSelected(); break;
                case Key.Escape: e.Handled = true; if (answer != null) { answer = null; UpdatePrompt(); Print(L("abgebrochen", "cancelled"), D); } else SafeHide(); break;
                case Key.Up: e.Handled = true; if (input.Text.Trim().Length > 0 && cands.Count > 1 && histPos < 0) MoveSelection(-1); else History(-1); break;
                case Key.Down: e.Handled = true; if (input.Text.Trim().Length > 0 && cands.Count > 1 && histPos < 0) MoveSelection(1); else History(1); break;
                case Key.Tab: e.Handled = true; if (input.Text.Contains(" ") || tabList != null || !CompleteSelected()) Complete(); break;
                case Key.L when ctrl: e.Handled = true; Clear(); break;
                case Key.C when ctrl && input.SelectionLength == 0:
                    e.Handled = true;
                    if (running != null) { try { running.Kill(); } catch { } Print("^C", Amber); }
                    else { Print(PromptText + input.Text + "^C", D); input.Text = ""; }
                    break;
            }
        }

        void History(int dir)
        {
            if (history.Count == 0) return;
            if (histPos < 0) histPos = history.Count;
            histPos = Math.Max(0, Math.Min(history.Count, histPos + dir));
            input.Text = histPos < history.Count ? history[histPos] : "";
            input.CaretIndex = input.Text.Length;
        }

        IEnumerable<string> CommandNames() => cmds.SelectMany(c => new[] { c.Name }.Concat(c.Alias ?? new string[0]));

        void UpdateHints()
        {
            var text = input.Text;
            if (answer != null) { hints.Text = L("antwort eingeben (y/n)", "type your answer (y/n)"); return; }
            if (text.Trim().Length == 0) { hints.Text = L("tipps: neofetch · top · apps · net · wall matrix · timer 5m · ? frage · !ipconfig", "tips: neofetch · top · apps · net · wall matrix · timer 5m · ? question · !ipconfig"); return; }
            hints.Text = L("enter = markierte zeile ausführen · ↑↓ wählen · tab ergänzen · !befehl = cmd · esc schließt", "enter = run the highlighted line · ↑↓ choose · tab completes · !command = cmd · esc closes");
        }

        void Complete()
        {
            var text = input.Text;
            if (tabList == null)
            {
                tabIndex = -1;
                int sp = text.LastIndexOf(' ');
                if (sp < 0)
                {
                    tabBase = "";
                    tabList = CommandNames().Where(n => n.StartsWith(text.ToLowerInvariant())).Distinct()
                        .Concat(AppCatalog.Apps.Where(a => a.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)).Select(a => a.Name)).Take(40).ToList();
                }
                else
                {
                    tabBase = text.Substring(0, sp + 1);
                    var part = text.Substring(sp + 1).Replace('/', '\\');
                    string dir = cwd, prefix = part;
                    int slash = part.LastIndexOf('\\');
                    if (slash >= 0) { dir = ResolvePath(part.Substring(0, slash + 1)); prefix = part.Substring(slash + 1); }
                    try
                    {
                        tabList = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName)
                            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(60)
                            .Select(n => (slash >= 0 ? part.Substring(0, slash + 1) : "") + (n.Contains(" ") ? "\"" + n + "\"" : n) + (Directory.Exists(Path.Combine(dir, n)) ? "\\" : "")).ToList();
                    }
                    catch { tabList = new List<string>(); }
                }
                if (tabList.Count == 0) { tabList = null; return; }
                if (tabList.Count > 1) Print(string.Join("   ", tabList.Take(30)), D);
            }
            tabIndex = (tabIndex + 1) % tabList.Count;
            input.Text = tabBase + tabList[tabIndex];
            input.CaretIndex = input.Text.Length;
            tabList = new List<string>(tabList); // keep cycling
        }

        void Clear() { output.Inlines.Clear(); lineCount = 0; }

        // ---------------- execution ----------------
        void Execute(string line)
        {
            if (answer != null)
            {
                var a = answer; answer = null;
                Print("? " + line, White);
                UpdatePrompt();
                a(line.Trim().ToLowerInvariant());
                return;
            }
            Print(PromptText + line, White);
            line = line.Trim();
            if (line.Length == 0) return;
            if (history.Count == 0 || history[history.Count - 1] != line) history.Add(line);
            histPos = -1;
            try { Run(line); }
            catch (Exception ex) { Print(L("fehler: ", "error: ") + ex.Message, Red); }
            UpdatePrompt();
        }

        void Run(string line)
        {
            if (line.StartsWith("!")) { Shell("cmd", line.Substring(1)); return; }
            if (line.StartsWith("?")) { Ask(line.Substring(1)); return; }
            if (line.StartsWith("=")) { Calc(line.Substring(1)); return; }
            int sp = line.IndexOf(' ');
            string name = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant(), args = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            var cmd = cmds.FirstOrDefault(c => c.Name == name || (c.Alias ?? new string[0]).Contains(name));
            if (cmd != null) { cmd.Run(args); return; }
            // an app name?
            var app = Intents.Match(line).FirstOrDefault();
            if (app != null && (app.Name.Equals(line, StringComparison.CurrentCultureIgnoreCase) || app.Name.StartsWith(line, StringComparison.CurrentCultureIgnoreCase)))
            {
                Print("→ " + L("starte ", "starting ") + app.Name, D);
                app.Start();
                SafeHide();
                return;
            }
            Shell("cmd", line);
        }

        void Shell(string kind, string command)
        {
            if (running != null) { Print(L("es läuft schon ein Befehl – Strg+C bricht ab", "a command is already running – ctrl+c stops it"), Amber); return; }
            ProcessStartInfo psi;
            if (kind == "ps")
                psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command.Replace("\"", "\\\"") + "\"") { StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            else
            {
                var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"") { StandardOutputEncoding = oem, StandardErrorEncoding = oem };
            }
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.RedirectStandardInput = true;
            psi.WorkingDirectory = Directory.Exists(cwd) ? cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Async(e.Data, G); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Async(e.Data, Red); };
            p.Exited += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                int code = 0; try { code = p.ExitCode; } catch { }
                if (code != 0) Print("[exit " + code + "]", D);
                running = null; p.Dispose();
            }));
            try { p.Start(); p.StandardInput.Close(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); running = p; }
            catch (Exception ex) { Print(ex.Message, Red); }
        }

        void Confirm(string question, Action yes)
        {
            Print(question + " [y/N]", Amber);
            answer = a => { if (a == "y" || a == "yes" || a == "j" || a == "ja") yes(); else Print(L("abgebrochen", "cancelled"), D); };
            UpdatePrompt(); UpdateHints();
        }

        string ResolvePath(string p)
        {
            p = (p ?? "").Trim().Trim('"');
            if (p.Length == 0 || p == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = Environment.ExpandEnvironmentVariables(p.Replace('/', '\\'));
            if (p.Length == 2 && p[1] == ':') p += "\\";
            try { return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(cwd, p)); } catch { return p; }
        }

        // ---------------- commands ----------------
        void Add(string name, string de, string en, Action<string> run, params string[] alias) => cmds.Add(new Cmd { Name = name, De = de, En = en, Run = run, Alias = alias });

        void Register()
        {
            Add("help", "alle Befehle", "all commands", a => Help(), "hilfe", "man");
            Add("clear", "Bildschirm leeren", "clear the screen", a => Clear(), "cls");
            Add("exit", "Terminal schließen", "close the terminal", a => SafeHide(), "quit");
            Add("apps", "Apps auflisten [filter]", "list apps [filter]", Apps, "programs");
            Add("open", "App, Datei, Ordner oder URL öffnen", "open app, file, folder or URL", OpenTarget, "run", "start", "xdg-open");
            Add("neofetch", "Systeminfo mit Logo", "system info with logo", a => Neofetch(), "fetch", "sysinfo", "info");
            Add("top", "Prozesse nach CPU/RAM", "processes by CPU/RAM", a => TopCmd(), "htop");
            Add("ps", "Prozesse [filter]", "processes [filter]", Ps);
            Add("kill", "Prozess beenden (pid oder name)", "end a process (pid or name)", Kill, "pkill", "taskkill");
            Add("sys", "System-Monitor öffnen", "open system monitor", a => Panel("sys"), "monitor");
            Add("net", "Netzwerk-Panel öffnen", "open network panel", a => Panel("net"), "network");
            Add("vol", "Lautstärke [0-100|up|down|mute]", "volume [0-100|up|down|mute]", Vol, "volume", "sound");
            Add("power", "Energie-Menü", "power menu", a => Panel("power"), "pwr");
            Add("cal", "Kalender", "calendar", a => Panel("cal"), "date-panel");
            Add("ip", "Netzwerkadressen", "network addresses", a => Ip(), "ifconfig", "ipconfig");
            Add("myip", "öffentliche IP", "public IP", a => Fetch("https://api.ipify.org", "public ip: "));
            Add("ping", "ping <host> [anzahl]", "ping <host> [count]", Ping);
            Add("dns", "DNS-Auflösung <host>", "DNS lookup <host>", Dns, "nslookup", "host");
            Add("wifi", "WLAN: wifi · wifi connect <name> · wifi off", "Wi-Fi: wifi · wifi connect <name> · wifi off", Wifi, "wlan");
            Add("weather", "Wetter [stadt]", "weather [city]", a => Fetch("https://wttr.in/" + Uri.EscapeDataString(a) + "?format=%l:+%c+%t+(feels+%f)+%w+%h&lang=" + (De ? "de" : "en"), ""), "wetter");
            Add("df", "Laufwerke", "drives", a => Df(), "disks");
            Add("uptime", "Laufzeit", "uptime", a => Print("up " + SysInfo.UptimeText));
            Add("whoami", "Benutzer", "user", a => Print(Environment.UserDomainName.ToLowerInvariant() + "\\" + Environment.UserName.ToLowerInvariant()));
            Add("hostname", "PC-Name", "PC name", a => Print(Environment.MachineName));
            Add("date", "Datum und Uhrzeit", "date and time", a => Print(DateTime.Now.ToString("dddd, dd.MM.yyyy HH:mm:ss", De ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture)), "time");
            Add("cd", "Ordner wechseln", "change folder", Cd);
            Add("pwd", "aktueller Ordner", "current folder", a => Print(cwd));
            Add("ls", "Ordnerinhalt [pfad]", "list folder [path]", Ls, "ll", "la");
            Add("cat", "Datei anzeigen", "show a file", Cat, "type", "less", "more");
            Add("find", "Dateien suchen <muster>", "find files <pattern>", Find, "search-files");
            Add("files", "Dateimanager öffnen [pfad] (Win+E)", "open the file manager [path] (Win+E)", a => { ShellActions.Files(a.Length > 0 ? ResolvePath(a) : cwd); SafeHide(); }, "explore", "explorer", "fm");
            Add("edit", "Datei bearbeiten", "edit a file", a => ShellActions.Run("notepad.exe", "\"" + ResolvePath(a) + "\""), "nano", "vim", "vi", "notepad");
            Add("hash", "Prüfsumme <datei> [md5|sha1|sha256]", "checksum <file> [md5|sha1|sha256]", Hash, "sha256sum", "md5sum");
            Add("clip", "Zwischenablage zeigen / clip <text> setzen", "show clipboard / clip <text> sets it", Clip, "pbcopy");
            Add("note", "Notiz speichern", "save a note", Note);
            Add("notes", "Notizen zeigen (notes clear = löschen)", "show notes (notes clear = delete)", Notes);
            Add("timer", "timer 5m [text] / 30s", "timer 5m [text] / 30s", Timer, "alarm");
            Add("calc", "rechnen, z. B. calc 2*(3+4)", "calculate, e.g. calc 2*(3+4)", Calc, "rechne");
            Add("ask", "ChatGPT fragen", "ask ChatGPT", Ask, "gpt", "chatgpt", "frag", "ai", "ki");
            Add("web", "im Web suchen", "search the web", a => { ShellActions.WebSearch(a); SafeHide(); }, "google", "suche");
            Add("windows", "offene Fenster", "open windows", a => Windows(), "wins", "lsw");
            Add("focus", "Fenster nach vorne <nr>", "bring window to front <no>", a => { if (WinAct(a, w => W32.Activate(w.Hwnd))) SafeHide(); }, "fg");
            Add("min", "Fenster minimieren <nr>", "minimise window <no>", a => WinAct(a, w => W32.Minimize(w.Hwnd)));
            Add("max", "Fenster maximieren <nr>", "maximise window <no>", a => WinAct(a, w => W32.ShowWindow(w.Hwnd, 3)));
            Add("closewin", "Fenster schließen <nr>", "close window <no>", a => WinAct(a, w => W32.Close(w.Hwnd)), "xkill");
            Add("wall", "Live-Hintergrund: wall list | wall <name> | wall off", "live wallpaper: wall list | wall <name> | wall off", Wall, "wallpaper", "bg");
            Add("matrix", "Matrix-Sperrbildschirm jetzt", "Matrix lock screen now", a => { SafeHide(); LockScreen.Show(t.Id, null); }, "screensaver");
            Add("hack", "Hollywood-Hacker-Show (nur Spaß)", "Hollywood hacker show (just for fun)", a => Hack(), "hollywood");
            Add("history", "Befehlsverlauf", "command history", a => { for (int i = 0; i < history.Count; i++) Print((i + 1).ToString().PadLeft(4) + "  " + history[i], D); });
            Add("sh", "Befehl in cmd", "run in cmd", a => Shell("cmd", a), "cmd");
            Add("ps1", "Befehl in PowerShell", "run in PowerShell", a => Shell("ps", a), "pwsh", "powershell");
            Add("terminal", "echtes Windows-Terminal öffnen", "open a real terminal", a => { OpenTerminal(); SafeHide(); }, "wt", "term");
            Add("taskmgr", "Task-Manager (Strg+Umschalt+Esc)", "task manager (Ctrl+Shift+Esc)", a => { ShellActions.TaskManager(); SafeHide(); }, "tm");
            Add("settings", "Windows-Einstellungen", "Windows settings", a => ShellActions.Settings());
            Add("lock", "sperren", "lock", a => ShellActions.Lock());
            Add("sleep", "Energie sparen", "sleep", a => ShellActions.Sleep(), "suspend");
            Add("logout", "abmelden", "sign out", a => Confirm(L("wirklich abmelden?", "really sign out?"), () => ShellActions.Run("shutdown.exe", "/l")), "logoff", "signout");
            Add("reboot", "neu starten", "restart", a => Confirm(L("wirklich neu starten?", "really restart?"), () => ShellActions.Run("shutdown.exe", "/r /t 0")), "restart", "neustart");
            Add("shutdown", "herunterfahren", "shut down", a => Confirm(L("wirklich herunterfahren?", "really shut down?"), () => ShellActions.Run("shutdown.exe", "/s /t 0")), "poweroff", "halt", "aus");
            Add("winmorph", "WinMorph-App (Looks)", "WinMorph app (looks)", a => { ShellActions.WinMorphApp(); SafeHide(); }, "theme", "looks");
            Add("restore", "Original-Windows wiederherstellen", "restore original Windows", a => { ShellActions.Restore(); SafeHide(); });
            Add("undo", "letzten Look rückgängig", "undo last look", a => { ShellActions.Run(Process.GetCurrentProcess().MainModule.FileName, "--undo-ui"); SafeHide(); });
        }

        void Help()
        {
            Print(L("befehle (aliase in klammern):", "commands (aliases in brackets):"), White);
            foreach (var c in cmds)
                PrintParts(("  " + c.Name.PadRight(10), G), ((De ? c.De : c.En).PadRight(46), D), (c.Alias != null && c.Alias.Length > 0 ? "(" + string.Join(", ", c.Alias.Take(3)) + ")" : "", A(D, 170)));
            Print("");
            Print(L("  !befehl   = direkt in cmd      ?frage = ChatGPT      =2+2 = rechnen", "  !command  = straight to cmd    ?question = ChatGPT   =2+2 = calculate"), D);
            Print(L("  app-name tippen: die liste zeigt, was enter macht (oberste zeile) · unbekanntes wird nie als befehl ausgeführt", "  type an app name: the list shows what enter does (top line) · unknown words never run as commands"), D);
            Print(L("  tasten: win = terminal · win+enter = windows-terminal · win+1..9 = fenster · win+shift+q = fenster schließen", "  keys: win = terminal · win+enter = windows terminal · win+1..9 = windows · win+shift+q = close window"), D);
        }

        void Panel(string kind) { SafeHide(); HackerPanel.Toggle(kind, t, new Rect(0, 0, 0, 0)); }

        void Apps(string filter)
        {
            var list = string.IsNullOrWhiteSpace(filter) ? AppCatalog.Apps : Intents.Match(filter).ToList();
            foreach (var a in list.Take(200)) Print("  " + a.Name, G);
            Print(list.Count + " apps" + (list.Count > 200 ? L(" (200 gezeigt)", " (200 shown)") : ""), D);
        }

        void OpenTarget(string a)
        {
            if (a.Length == 0) { Print(L("open <app|datei|ordner|url>", "open <app|file|folder|url>"), D); return; }
            var path = ResolvePath(a);
            if (File.Exists(path) || Directory.Exists(path)) { ShellActions.Run(path); Print("→ " + path, D); SafeHide(); return; }
            if (a.Contains("://") || a.StartsWith("www.")) { ShellActions.Run(a.StartsWith("www.") ? "https://" + a : a); SafeHide(); return; }
            var app = Intents.Match(a).FirstOrDefault();
            if (app != null) { Print("→ " + L("starte ", "starting ") + app.Name, D); app.Start(); SafeHide(); return; }
            ShellActions.Run(a);
        }

        void Wifi(string a)
        {
            var parts = a.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            if (!Wlan.Available) { Print(L("kein wlan-adapter", "no wi-fi adapter"), Red); return; }
            if (verb == "off" || verb == "disconnect" || verb == "trennen") { Print(Wlan.Disconnect() ? L("getrennt", "disconnected") : L("fehler", "error"), D); return; }
            if (verb == "connect" || verb == "verbinden")
            {
                var name = parts.Length > 1 ? parts[1].Trim() : "";
                var n = Wlan.Networks().FirstOrDefault(x => string.Equals(x.Ssid, name, StringComparison.OrdinalIgnoreCase))
                     ?? Wlan.Networks().FirstOrDefault(x => x.Ssid.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                if (n == null) { Print(L("netz nicht gefunden – 'wifi' zeigt alle", "network not found – 'wifi' lists all"), Red); return; }
                if (n.Profile == null && n.Secured) { Print(L("neues netz mit passwort: im netz-panel verbinden", "new network with password: connect in the net panel"), Amber); Panel("net"); return; }
                var e = n.Profile != null ? Wlan.Connect(n.Profile) : Wlan.ConnectNew(n, null);
                Print(e == null ? L("verbinde mit ", "connecting to ") + n.Ssid + " …" : e, e == null ? D : Red);
                return;
            }
            foreach (var n in Wlan.Networks().Take(12))
                PrintParts(((n.Connected ? "● " : "  ") + n.Ssid.PadRight(28), n.Connected ? White : G), ((n.Signal + "%").PadLeft(5) + (n.Secured ? "  wpa" : "  open") + (n.Profile != null ? L("  gespeichert", "  saved") : ""), D));
            Print(L("wifi connect <name> · wifi off · 'net' öffnet das panel", "wifi connect <name> · wifi off · 'net' opens the panel"), D);
        }

        void Neofetch()
        {
            string[] art =
            {
                "██╗    ██╗███╗   ███╗",
                "██║    ██║████╗ ████║",
                "██║ █╗ ██║██╔████╔██║",
                "██║███╗██║██║╚██╔╝██║",
                "╚███╔███╔╝██║ ╚═╝ ██║",
                " ╚══╝╚══╝ ╚═╝     ╚═╝",
                "", "", "", "", "", "", "",
            };
            SysStats.MemTotals(out ulong total, out ulong avail);
            var disk = SysInfo.Disks().FirstOrDefault();
            var mi = W32.PrimaryMonitor(out _);
            var info = new List<(string, string)>
            {
                (User + "@" + Host, ""),
                (new string('─', User.Length + Host.Length + 1), ""),
                ("os", SysInfo.Os),
                ("kernel", Environment.OSVersion.Version.ToString()),
                (L("laufzeit", "uptime"), SysInfo.UptimeText),
                ("shell", "winmorph-sh " + Core.Updater.Version),
                (L("design", "theme"), t.Name.ToLowerInvariant() + " · " + (Live.LiveWall.Spec ?? L("statisch", "static"))),
                (L("auflösung", "resolution"), (mi.rcMonitor.Right - mi.rcMonitor.Left) + "x" + (mi.rcMonitor.Bottom - mi.rcMonitor.Top)),
                ("cpu", SysInfo.Cpu),
                ("gpu", SysInfo.Gpu),
                ("memory", SysInfo.Bytes(total - avail) + " / " + SysInfo.Bytes(total)),
                ("disk", disk != null ? disk.Name.Substring(0, 2) + " " + SysInfo.Bytes(disk.TotalSize - disk.TotalFreeSpace) + " / " + SysInfo.Bytes(disk.TotalSize) : "-"),
                ("ip", SysInfo.LocalIp),
            };
            for (int i = 0; i < Math.Max(art.Length, info.Count); i++)
            {
                var left = " " + (i < art.Length ? art[i] : "").PadRight(24);
                if (i >= info.Count) { Print(left, G); continue; }
                var (k, v) = info[i];
                if (v == "") PrintParts((left, G), (k, White));
                else PrintParts((left, G), (k.PadRight(12), Cyan), (v, G));
            }
            var colors = new[] { C(0x000000), C(0xFF4D4D), C(0x00FF41), C(0xFFB000), C(0x3D8BFF), C(0xB45CFF), C(0x00E5FF), C(0xFFFFFF) };
            output.Inlines.Add(new Run(new string(' ', 25)));
            foreach (var c in colors) output.Inlines.Add(new Run("███") { Foreground = B(c) });
            output.Inlines.Add(new LineBreak()); lineCount++;
        }

        void TopCmd()
        {
            Print("  PID    " + "NAME".PadRight(26) + "    MEM      CPU-TIME", White);
            Task.Run(() =>
            {
                var list = new List<(int pid, string name, long mem, TimeSpan cpu)>();
                foreach (var p in Process.GetProcesses())
                {
                    try { list.Add((p.Id, p.ProcessName, p.WorkingSet64, SafeCpu(p))); } catch { }
                    p.Dispose();
                }
                var lines = list.OrderByDescending(x => x.mem).Take(18).Select(x => x.pid.ToString().PadLeft(6) + "   " + Cut(x.name, 26).PadRight(26) + SysInfo.Bytes(x.mem).PadLeft(8) + "   " + ((int)x.cpu.TotalHours).ToString("0") + ":" + x.cpu.Minutes.ToString("00") + ":" + x.cpu.Seconds.ToString("00")).ToList();
                Dispatcher.BeginInvoke(new Action(() => { foreach (var l in lines) Print(l, G); Print(list.Count + L(" Prozesse · 'sys' für Live-Ansicht · 'kill <pid>' beendet", " processes · 'sys' for live view · 'kill <pid>' ends one"), D); }));
            });
        }

        static TimeSpan SafeCpu(Process p) { try { return p.TotalProcessorTime; } catch { return TimeSpan.Zero; } }
        static string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        void Ps(string filter)
        {
            var all = Process.GetProcesses().Where(p => filter.Length == 0 || p.ProcessName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p.ProcessName).ToList();
            foreach (var p in all.Take(150))
            {
                string title = ""; try { title = p.MainWindowTitle; } catch { }
                long mem = 0; try { mem = p.WorkingSet64; } catch { }
                Print(p.Id.ToString().PadLeft(6) + "  " + Cut(p.ProcessName, 28).PadRight(28) + SysInfo.Bytes(mem).PadLeft(7) + "  " + Cut(title, 50), title.Length > 0 ? G : D);
            }
            Print(all.Count + L(" Prozesse", " processes"), D);
            foreach (var p in all) p.Dispose();
        }

        void Kill(string a)
        {
            if (a.Length == 0) { Print("kill <pid|name>", D); return; }
            if (int.TryParse(a, out int pid))
            {
                string name = "?"; try { using (var p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { Print(L("kein prozess ", "no process ") + pid, Red); return; }
                Print(HackerPanel.Kill(pid, name), Amber);
                return;
            }
            var n = a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a.Substring(0, a.Length - 4) : a;
            var list = Process.GetProcessesByName(n);
            if (list.Length == 0) { Print(L("kein prozess namens ", "no process named ") + n, Red); return; }
            Confirm(list.Length + "× " + n + L(" beenden?", " end?"), () => { foreach (var p in list) Print(HackerPanel.Kill(p.Id, p.ProcessName), Amber); });
        }

        void Vol(string a)
        {
            a = a.ToLowerInvariant();
            if (a == "up" || a == "+") Audio.Change(10);
            else if (a == "down" || a == "-") Audio.Change(-10);
            else if (a == "mute" || a == "stumm") Audio.Muted = !Audio.Muted;
            else if (int.TryParse(a.TrimEnd('%'), out int v)) Audio.Level = v;
            int l = Audio.Level;
            Print(l < 0 ? L("kein ausgabegerät", "no output device") : (Audio.Muted ? "muted " : "vol   ") + "[" + SysInfo.Bar(l, 30, '|', '·') + "] " + l + "%   " + Audio.DeviceName.ToLowerInvariant(), G);
        }

        void Ip()
        {
            bool any = false;
            foreach (var ni in SysInfo.Adapters())
            {
                any = true;
                var ip = ni.GetIPProperties();
                PrintParts((ni.Name.ToLowerInvariant(), White), ("  " + ni.NetworkInterfaceType.ToString().ToLowerInvariant() + "  " + string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("x2"))), D));
                foreach (var u in ip.UnicastAddresses) Print("    " + (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "inet  " : "inet6 ") + u.Address, G);
                foreach (var g in ip.GatewayAddresses) Print("    gw    " + g.Address, D);
                foreach (var d in ip.DnsAddresses.Take(3)) Print("    dns   " + d, D);
            }
            if (!any) Print("offline", Red);
        }

        void Fetch(string url, string prefix)
        {
            Print("…", D);
            Task.Run(() =>
            {
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                    using (var c = new System.Net.WebClient { Encoding = Encoding.UTF8 }) { c.Headers["User-Agent"] = "curl/8"; Async(prefix + c.DownloadString(url).Trim(), G); }
                }
                catch (Exception ex) { Async(ex.Message, Red); }
            });
        }

        void Ping(string a)
        {
            var parts = a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { Print("ping <host> [count]", D); return; }
            string host = parts[0]; int n = parts.Length > 1 && int.TryParse(parts[1], out int k) ? Math.Min(50, k) : 4;
            Task.Run(() =>
            {
                var p = new System.Net.NetworkInformation.Ping();
                int ok = 0; long sum = 0;
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var r = p.Send(host, 2000);
                        if (r.Status == System.Net.NetworkInformation.IPStatus.Success) { ok++; sum += r.RoundtripTime; Async("reply from " + r.Address + "  time=" + r.RoundtripTime + "ms  ttl=" + (r.Options?.Ttl ?? 0), G); }
                        else Async(r.Status.ToString().ToLowerInvariant(), Amber);
                    }
                    catch (Exception ex) { Async((ex.InnerException ?? ex).Message, Red); break; }
                    System.Threading.Thread.Sleep(600);
                }
                Async("--- " + host + ": " + ok + "/" + n + " ok" + (ok > 0 ? ", avg " + (sum / ok) + "ms" : ""), D);
            });
        }

        void Dns(string a)
        {
            if (a.Length == 0) { Print("dns <host>", D); return; }
            Task.Run(() =>
            {
                try { foreach (var ip in System.Net.Dns.GetHostAddresses(a)) Async(a + "  →  " + ip, G); }
                catch (Exception ex) { Async(ex.Message, Red); }
            });
        }

        void Df()
        {
            foreach (var d in SysInfo.Disks())
            {
                double used = 100 - d.TotalFreeSpace * 100.0 / Math.Max(1, d.TotalSize);
                string label = ""; try { label = d.VolumeLabel; } catch { }
                Print(d.Name.Substring(0, 2).ToLower() + "  [" + SysInfo.Bar(used, 30, '|', '·') + "] " + used.ToString("0").PadLeft(3) + "%  " + SysInfo.Bytes(d.TotalSize - d.TotalFreeSpace) + " / " + SysInfo.Bytes(d.TotalSize) + "  " + label.ToLowerInvariant(), used > 90 ? Amber : G);
            }
        }

        void Cd(string a)
        {
            var p = ResolvePath(a);
            if (Directory.Exists(p)) cwd = p;
            else Print("cd: " + L("nicht gefunden: ", "not found: ") + a, Red);
        }

        void Ls(string a)
        {
            var dir = a.Length > 0 && !a.StartsWith("-") ? ResolvePath(a) : cwd;
            if (!Directory.Exists(dir)) { Print("ls: " + L("nicht gefunden: ", "not found: ") + a, Red); return; }
            try
            {
                var di = new DirectoryInfo(dir);
                int n = 0;
                foreach (var d in di.EnumerateDirectories().OrderBy(x => x.Name).Take(300)) { PrintParts(("  " + d.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + "        <dir>  ", D), (d.Name + "/", Cyan)); n++; }
                foreach (var f in di.EnumerateFiles().OrderBy(x => x.Name).Take(300)) { PrintParts(("  " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + "  " + SysInfo.Bytes(f.Length).PadLeft(11) + "  ", D), (f.Name, f.Extension == ".exe" || f.Extension == ".bat" || f.Extension == ".cmd" ? White : G)); n++; }
                Print(n + L(" einträge", " entries"), D);
            }
            catch (Exception ex) { Print(ex.Message, Red); }
        }

        void Cat(string a)
        {
            var f = ResolvePath(a);
            if (!File.Exists(f)) { Print("cat: " + L("nicht gefunden: ", "not found: ") + a, Red); return; }
            var bytes = new byte[Math.Min(4096, new FileInfo(f).Length)];
            using (var fs = File.OpenRead(f)) fs.Read(bytes, 0, bytes.Length);
            if (bytes.Contains((byte)0)) { Print(L("binärdatei – 'hash' oder 'open' verwenden", "binary file – use 'hash' or 'open'"), Amber); return; }
            int i = 0;
            foreach (var line in File.ReadLines(f))
            {
                if (++i > 400) { Print(L("… gekürzt (400 zeilen)", "… truncated (400 lines)"), D); break; }
                Print(line, G);
            }
        }

        void Find(string a)
        {
            if (a.Length == 0) { Print("find <pattern>", D); return; }
            var root = cwd; var pattern = a.Trim('"');
            Print(L("suche in ", "searching ") + ShortPath(root) + " …", D);
            Task.Run(() =>
            {
                int found = 0;
                var stack = new Stack<string>(); stack.Push(root);
                var sw = Stopwatch.StartNew();
                while (stack.Count > 0 && found < 200 && sw.Elapsed.TotalSeconds < 20)
                {
                    var d = stack.Pop();
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(d)) { var n = Path.GetFileName(sub); if (!n.StartsWith(".") && n != "node_modules" && n != "AppData") stack.Push(sub); if (Match(n, pattern)) { found++; Async(ShortPath(sub) + "/", Cyan); } }
                        foreach (var f in Directory.EnumerateFiles(d)) if (Match(Path.GetFileName(f), pattern)) { found++; Async(ShortPath(f), G); if (found >= 200) break; }
                    }
                    catch { }
                }
                Async(found + L(" treffer", " hits") + (found >= 200 ? L(" (limit)", " (limit)") : ""), D);
            });
        }

        static bool Match(string name, string pattern)
        {
            if (pattern.Contains("*") || pattern.Contains("?"))
                return System.Text.RegularExpressions.Regex.IsMatch(name, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Hash(string a)
        {
            var parts = a.Split(' ');
            string algo = parts.Length > 1 ? parts[parts.Length - 1].ToLowerInvariant() : "sha256";
            string file = ResolvePath(algo == "md5" || algo == "sha1" || algo == "sha256" ? string.Join(" ", parts.Take(parts.Length - (parts.Length > 1 ? 1 : 0))) : a);
            if (!(algo == "md5" || algo == "sha1" || algo == "sha256")) algo = "sha256";
            if (!File.Exists(file)) { Print("hash: " + L("nicht gefunden", "not found"), Red); return; }
            Print(algo + " …", D);
            Task.Run(() =>
            {
                try
                {
                    using (var h = algo == "md5" ? (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.MD5.Create() : algo == "sha1" ? System.Security.Cryptography.SHA1.Create() : System.Security.Cryptography.SHA256.Create())
                    using (var fs = File.OpenRead(file))
                        Async(BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant() + "  " + Path.GetFileName(file), G);
                }
                catch (Exception ex) { Async(ex.Message, Red); }
            });
        }

        void Clip(string a)
        {
            try
            {
                if (a.Length > 0) { Clipboard.SetText(a); Print(L("in die zwischenablage kopiert", "copied to clipboard"), D); return; }
                var s = Clipboard.ContainsText() ? Clipboard.GetText() : L("(keine text-daten)", "(no text)");
                foreach (var l in s.Split('\n').Take(100)) Print(l.TrimEnd('\r'), G);
            }
            catch (Exception ex) { Print(ex.Message, Red); }
        }

        static string NotesFile => Path.Combine(Core.Backup.DataDir, "notes.txt");
        void Note(string a)
        {
            if (a.Length == 0) { Print("note <text>", D); return; }
            File.AppendAllText(NotesFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + a + Environment.NewLine);
            Print(L("gespeichert", "saved"), D);
        }

        void Notes(string a)
        {
            if (a == "clear") { Confirm(L("alle notizen löschen?", "delete all notes?"), () => { try { File.Delete(NotesFile); } catch { } Print(L("gelöscht", "deleted"), D); }); return; }
            if (!File.Exists(NotesFile)) { Print(L("noch keine notizen – note <text>", "no notes yet – note <text>"), D); return; }
            foreach (var l in File.ReadAllLines(NotesFile).Reverse().Take(60).Reverse()) Print(l, G);
        }

        void Timer(string a)
        {
            var parts = a.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { Print("timer 5m [text] · timer 30s · timer 1h", D); return; }
            var spec = parts[0].ToLowerInvariant();
            double mult = spec.EndsWith("h") ? 3600 : spec.EndsWith("s") ? 1 : 60;
            if (!double.TryParse(spec.TrimEnd('h', 'm', 's'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v <= 0) { Print(L("ungültige zeit", "invalid time"), Red); return; }
            var label = parts.Length > 1 ? parts[1] : "timer";
            var span = TimeSpan.FromSeconds(v * mult);
            var end = DateTime.Now + span;
            Print("⏱ " + label + " → " + end.ToString("HH:mm:ss"), G);
            var tm = new DispatcherTimer { Interval = span };
            tm.Tick += (s, e) =>
            {
                tm.Stop();
                Print("⏰ " + label + L(" – zeit ist um!", " – time is up!"), Amber);
                Notify.Show("timer", "⏰ " + label + L(" – zeit ist um!", " – time is up!"), null, 15);
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            };
            tm.Start();
        }

        void Calc(string a)
        {
            var expr = a.Trim().Replace(",", ".").Replace("^", "**");
            if (expr.Length == 0) { Print("calc 2*(3+4)", D); return; }
            try
            {
                object r = expr.Contains("**") ? (object)Pow(expr) : new System.Data.DataTable().Compute(expr, "");
                Print("= " + Convert.ToString(r, CultureInfo.InvariantCulture), White);
            }
            catch (Exception ex) { Print("calc: " + ex.Message, Red); }
        }

        static double Pow(string e) { var p = e.Split(new[] { "**" }, StringSplitOptions.None); return Math.Pow(Convert.ToDouble(new System.Data.DataTable().Compute(p[0], "")), Convert.ToDouble(new System.Data.DataTable().Compute(p[1], ""))); }

        void Ask(string q)
        {
            q = q.Trim();
            Print("→ ChatGPT" + (q.Length > 0 ? ": " + q : ""), D);
            ShellActions.AskAi(q);
            SafeHide();
        }

        List<WinInfo> wins = new List<WinInfo>();
        void Windows()
        {
            wins = windows();
            for (int i = 0; i < wins.Count; i++)
                PrintParts(((i + 1).ToString().PadLeft(3) + "  ", White), (Cut((wins[i].AppName ?? "").ToLowerInvariant(), 18).PadRight(18) + "  ", Cyan), (Cut(wins[i].Title, 70), G));
            Print(L("focus <nr> · min <nr> · max <nr> · closewin <nr>", "focus <no> · min <no> · max <no> · closewin <no>"), D);
        }

        bool WinAct(string a, Action<WinInfo> act)
        {
            if (wins.Count == 0) wins = windows();
            if (!int.TryParse(a, out int n) || n < 1 || n > wins.Count) { Print(L("nummer aus 'windows' angeben", "give a number from 'windows'"), Red); return false; }
            act(wins[n - 1]);
            return true;
        }

        void Wall(string a)
        {
            a = a.Trim().ToLowerInvariant();
            if (a.Length == 0 || a == "list" || a == "ls")
            {
                var cur = Live.LiveWall.Spec;
                foreach (var g in Live.WallCatalog.Groups)
                {
                    Print(g, White);
                    foreach (var it in Live.WallCatalog.All.Where(x => x.Group == g))
                        PrintParts(("  " + (it.Spec == cur ? "* " : "  ") + it.Id.PadRight(18), it.Spec == cur ? White : G), (it.Name(De) + " – " + it.Tag(De), D));
                }
                Print(L("wall <name> setzt · wall off schaltet aus", "wall <name> sets · wall off turns it off"), D);
                return;
            }
            var bk = new Core.Backup(Core.Backup.DefaultFile) { Mirror = Core.Backup.NewStep() };
            if (a == "off" || a == "aus") { Live.LiveWall.Apply(bk, "", null); Print(L("live-hintergrund aus", "live wallpaper off"), D); return; }
            var item = Live.WallCatalog.Find(a) ?? Live.WallCatalog.All.FirstOrDefault(x => x.Id.StartsWith(a) || x.Name(De).ToLowerInvariant().StartsWith(a));
            if (item == null) { Print(L("unbekannt – 'wall list' zeigt alle", "unknown – 'wall list' shows all"), Red); return; }
            Action apply = () =>
            {
                var mi = W32.PrimaryMonitor(out _);
                var poster = item.IsVideo ? null : Live.LiveWall.Poster(item, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                Task.Run(async () =>
                {
                    if (item.IsVideo) poster = await Live.WallCatalog.Thumb(item, true);
                    Live.LiveWall.Apply(bk, item.Spec, poster);
                    Async("✓ " + item.Name(De), G);
                });
            };
            if (item.IsVideo && !item.Downloaded)
            {
                Print(L("lade ", "downloading ") + item.Name(De) + " (" + item.SizeMb + " MB) …", D);
                int lastPct = -10;
                Live.WallCatalog.EnsureDownloaded(item, pct => { if (pct >= lastPct + 20) { lastPct = pct; Async("  " + pct + "%", D); } })
                    .ContinueWith(r => Dispatcher.BeginInvoke(new Action(() => { if (r.Result) apply(); else Print(L("download fehlgeschlagen", "download failed"), Red); })));
            }
            else apply();
        }

        void Hack()
        {
            var steps = new List<(string, Color)>
            {
                ("[*] initialising exploit framework …", D), ("[*] loading modules: recon, crypto, stealth", D),
                ("[+] target acquired: mainframe.local (10.13.37.1)", G), ("[*] bypassing firewall  [" + new string('|', 30) + "] 100%", G),
                ("[*] injecting payload 0x" + Guid.NewGuid().ToString("N").Substring(0, 8), D), ("[*] brute-forcing 4096-bit key …", D),
                ("    " + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), A(G, 160)), ("    " + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), A(G, 160)),
                ("[+] key found", G), ("[*] downloading the internet … 3%", Amber), ("[*] reticulating splines … done", D),
                ("", G), ("    ▄▀▄ ▄▀▀ ▄▀▀ ██▀ ▄▀▀ ▄▀▀    ▄▀  █▀▄ ▄▀▄ █▄ █ ▀█▀ ██▀ █▀▄", White), ("    █▀█ ▀▄▄ ▀▄▄ █▄▄ ▄██ ▄██    ▀▄█ █▀▄ █▀█ █ ▀█  █  █▄▄ █▄▀", White), ("", G),
                (L("(nur Show – es wurde nichts gehackt)", "(just a show – nothing was hacked)"), D),
            };
            int i = 0;
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            tm.Tick += (s, e) => { if (i >= steps.Count) { tm.Stop(); return; } Print(steps[i].Item1, steps[i].Item2); i++; };
            tm.Start();
        }

        public static void OpenTerminal()
        {
            var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
            if (File.Exists(wt)) ShellActions.Run(wt);
            else ShellActions.Run("cmd.exe");
        }
    }
}

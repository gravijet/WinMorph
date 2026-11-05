using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The launcher part of the hacker terminal: while typing, a list shows exactly what Enter
    /// will do (first line, highlighted). Apps come first; a WinMorph command only wins when the
    /// text really is that command, and unknown words are never sent to cmd by accident.
    /// </summary>
    public sealed partial class HackerConsole
    {
        sealed class Cand
        {
            public string Label, Kind;   // Kind: app, cmd, shell, web, ask, special
            public Action Run;
            public string Complete;      // text for Tab
        }

        List<Cand> cands = new List<Cand>();
        int sel;
        StackPanel suggest;
        const int MaxSuggest = 7;

        // commands that do nothing useful without arguments: typed alone, an app of that name wins
        static readonly HashSet<string> NeedsArgs = new HashSet<string> { "open", "ping", "dns", "kill", "find", "hash", "edit", "calc", "note", "timer", "focus", "min", "max", "closewin", "web", "sh", "ps1", "ask", "cat" };

        // common names → how the app is called in the list (German and English Windows)
        static readonly Dictionary<string, string[]> Synonyms = new Dictionary<string, string[]>
        {
            ["calc"] = new[] { "rechner", "calculator" }, ["calculator"] = new[] { "rechner" }, ["rechner"] = new[] { "calculator" },
            ["notepad"] = new[] { "editor", "notepad" }, ["editor"] = new[] { "notepad" },
            ["cmd"] = new[] { "eingabeaufforderung", "command prompt" }, ["powershell"] = new[] { "windows powershell", "powershell" },
            ["settings"] = new[] { "einstellungen" }, ["einstellungen"] = new[] { "settings" },
            ["taskmgr"] = new[] { "task-manager", "task manager" }, ["photos"] = new[] { "fotos" }, ["fotos"] = new[] { "photos" },
            ["store"] = new[] { "microsoft store" }, ["snip"] = new[] { "snipping tool", "ausschneiden" }, ["camera"] = new[] { "kamera" }, ["kamera"] = new[] { "camera" },
            ["mail"] = new[] { "outlook", "mail" }, ["word"] = new[] { "word" }, ["excel"] = new[] { "excel" }, ["vscode"] = new[] { "visual studio code" },
            ["clock"] = new[] { "uhr", "clock" }, ["uhr"] = new[] { "clock" }, ["paint"] = new[] { "paint" }, ["terminal"] = new[] { "terminal" },
        };

        static readonly HashSet<string> CmdBuiltins = new HashSet<string> { "dir", "echo", "set", "ver", "vol", "copy", "del", "erase", "move", "ren", "rename", "mkdir", "md", "rd", "rmdir", "path", "title", "color", "mklink", "pushd", "popd", "assoc", "ftype" };

        void BuildSuggest()
        {
            suggest = new StackPanel { Margin = new Thickness(14, 0, 14, 4) };
        }

        // ---------------- matching ----------------
        static string[] Words(string s) => s.Split(new[] { ' ', '-', '.', '(', ')', '_', '/', '&', '+' }, StringSplitOptions.RemoveEmptyEntries);

        static bool Subsequence(string text, string q)
        {
            int i = 0;
            foreach (var ch in text) if (i < q.Length && ch == q[i]) i++;
            return i == q.Length;
        }

        static List<AppEntry> AppMatches(string q, int max)
        {
            var ql = q.Trim().ToLowerInvariant();
            if (ql.Length == 0) return new List<AppEntry>();
            Synonyms.TryGetValue(ql, out var syn);
            var scored = new List<(AppEntry a, double s)>();
            foreach (var a in AppCatalog.Apps)
            {
                var name = a.Name.ToLowerInvariant();
                string exe = null;
                try { if (a.ExePath != null) exe = Path.GetFileNameWithoutExtension(a.ExePath).ToLowerInvariant(); } catch { }
                double s = 0;
                if (name == ql) s = 100;
                else if (exe == ql) s = 95;
                else if (syn != null && syn.Any(x => name == x)) s = 92;
                else if (name.StartsWith(ql)) s = 85;
                else if (syn != null && syn.Any(x => name.StartsWith(x))) s = 80;
                else if (Words(name).Any(w => w.StartsWith(ql))) s = 75;
                else if (exe != null && ql.Length >= 2 && exe.StartsWith(ql)) s = 70;
                else if (ql.Length >= 2 && new string(Words(name).Select(w => w[0]).ToArray()).StartsWith(ql)) s = 62;
                else if (ql.Length >= 3 && name.Contains(ql)) s = 50;
                else if (ql.Length >= 3 && !ql.Contains(' ') && Subsequence(name, ql)) s = 22;
                if (s > 0) scored.Add((a, s - name.Length * 0.02));
            }
            // loose matches ("fire" → "Office Language Preferences") only when nothing better exists
            if (scored.Any(x => x.s >= 50)) scored.RemoveAll(x => x.s < 45);
            return scored.OrderByDescending(x => x.s).Select(x => x.a).GroupBy(a => a.Name.ToLowerInvariant()).Select(g => g.First()).Take(max).ToList();
        }

        static readonly Dictionary<string, bool> exeCache = new Dictionary<string, bool>();

        /// <summary>A real program on the PATH (git, python, ipconfig …) or a cmd built-in.</summary>
        static bool IsProgram(string name)
        {
            name = name.ToLowerInvariant();
            if (CmdBuiltins.Contains(name)) return true;
            if (exeCache.TryGetValue(name, out var hit)) return hit;
            bool found = false;
            try
            {
                if (File.Exists(name) && Path.IsPathRooted(name)) found = true;
                else
                {
                    var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';');
                    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                    {
                        if (dir.Length == 0) continue;
                        foreach (var ext in exts)
                            if (File.Exists(Path.Combine(dir, name + ext.ToLowerInvariant())) || (Path.HasExtension(name) && File.Exists(Path.Combine(dir, name)))) { found = true; break; }
                        if (found) break;
                    }
                }
            }
            catch { }
            exeCache[name] = found;
            return found;
        }

        /// <summary>Everything the typed text could mean, best first. Enter runs the selected one.</summary>
        List<Cand> Resolve(string text)
        {
            var list = new List<Cand>();
            var q = (text ?? "").Trim();
            if (q.Length == 0) return list;
            if (q.StartsWith("!")) { list.Add(new Cand { Kind = "shell", Label = "cmd: " + q.Substring(1).Trim(), Run = () => Shell("cmd", q.Substring(1)) }); return list; }
            if (q.StartsWith("?")) { list.Add(new Cand { Kind = "ask", Label = L("ChatGPT fragen: ", "ask ChatGPT: ") + q.Substring(1).Trim(), Run = () => Ask(q.Substring(1)) }); return list; }
            if (q.StartsWith("=")) { list.Add(new Cand { Kind = "special", Label = L("rechnen: ", "calculate: ") + q.Substring(1).Trim(), Run = () => Calc(q.Substring(1)) }); return list; }

            int sp = q.IndexOf(' ');
            string name = (sp < 0 ? q : q.Substring(0, sp)).ToLowerInvariant(), args = sp < 0 ? "" : q.Substring(sp + 1).Trim();
            var cmd = cmds.FirstOrDefault(c => c.Name == name || (c.Alias ?? new string[0]).Contains(name));
            var apps = AppMatches(q, 6);
            bool appExact = apps.Count > 0 && (apps[0].Name.Equals(q, StringComparison.CurrentCultureIgnoreCase));

            Cand CmdCand(Cmd c, string a) => new Cand { Kind = "cmd", Label = c.Name + (a.Length > 0 ? " " + a : "") + "   – " + (De ? c.De : c.En), Run = () => c.Run(a), Complete = c.Name + " " };
            Cand AppCand(AppEntry a) => new Cand { Kind = "app", Label = a.Name, Run = () => { Print("→ " + L("starte ", "starting ") + a.Name, D); a.Start(); SafeHide(); }, Complete = a.Name };

            // 1. the exact name of an app always wins
            if (appExact) list.Add(AppCand(apps[0]));
            // 2. a WinMorph command – only when it is really meant (with arguments, or a command that needs none)
            if (cmd != null && (args.Length > 0 || !NeedsArgs.Contains(cmd.Name))) list.Add(CmdCand(cmd, args));
            // 3. a real program with arguments ("git status", "ipconfig /all")
            bool program = IsProgram(name);
            if (cmd == null && program && args.Length > 0) list.Add(new Cand { Kind = "shell", Label = "cmd: " + q, Run = () => Shell("cmd", q) });
            // 4. apps
            foreach (var a in apps) if (!list.Any(c => c.Kind == "app" && c.Label == a.Name)) list.Add(AppCand(a));
            // 5. a program without arguments ("ipconfig", "python")
            if (cmd == null && program && args.Length == 0) list.Add(new Cand { Kind = "shell", Label = "cmd: " + q, Run = () => Shell("cmd", q) });
            // 6. commands that start like this
            if (sp < 0)
                foreach (var c in cmds.Where(c => c != cmd && (c.Name.StartsWith(name) || (name.Length >= 2 && (c.Alias ?? new string[0]).Any(x => x.StartsWith(name))))).Take(3))
                    list.Add(CmdCand(c, ""));
            // 7. nothing found: search the web or ask – never run an unknown word as a command
            list.Add(new Cand { Kind = "web", Label = L("im Web suchen: ", "search the web: ") + q, Run = () => { ShellActions.WebSearch(q); SafeHide(); } });
            list.Add(new Cand { Kind = "ask", Label = L("ChatGPT fragen: ", "ask ChatGPT: ") + q, Run = () => Ask(q) });
            return list;
        }

        // ---------------- display ----------------
        void RefreshSuggest()
        {
            if (suggest == null) return;
            cands = answer != null ? new List<Cand>() : Resolve(input.Text);
            if (sel >= cands.Count) sel = 0;
            suggest.Children.Clear();
            if (cands.Count == 0) { suggest.Visibility = Visibility.Collapsed; return; }
            suggest.Visibility = Visibility.Visible;
            for (int i = 0; i < Math.Min(MaxSuggest, cands.Count); i++)
            {
                var c = cands[i];
                bool on = i == sel;
                string tag = c.Kind == "app" ? "app" : c.Kind == "cmd" ? "winmorph" : c.Kind == "shell" ? "cmd" : c.Kind == "web" ? "web" : c.Kind == "ask" ? "ai" : "";
                var tb = new TextBlock { FontFamily = Mono, FontSize = 14, Padding = new Thickness(6, 1, 6, 1), TextTrimming = TextTrimming.CharacterEllipsis };
                tb.Inlines.Add(new Run(on ? "▶ " : "  ") { Foreground = B(G) });
                tb.Inlines.Add(new Run(c.Label) { Foreground = B(on ? White : (c.Kind == "web" || c.Kind == "ask" ? D : G)) });
                var row = new DockPanel { Background = on ? B(A(G, 46)) : Brushes.Transparent, Cursor = Cursors.Hand };
                var tagTb = new TextBlock { Text = tag, FontFamily = Mono, FontSize = 12, Foreground = B(D), Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(tagTb, Dock.Right);
                row.Children.Add(tagTb); row.Children.Add(tb);
                int idx = i;
                row.MouseLeftButtonUp += (s, e) => { sel = idx; RunSelected(); };
                suggest.Children.Add(row);
            }
        }

        void MoveSelection(int dir)
        {
            if (cands.Count == 0) return;
            int n = Math.Min(MaxSuggest, cands.Count);
            sel = (sel + dir + n) % n;
            RefreshSuggest();
        }

        /// <summary>Enter: runs exactly the highlighted line.</summary>
        void RunSelected()
        {
            var line = input.Text;
            if (answer != null || line.Trim().Length == 0 || cands.Count == 0) { input.Text = ""; Execute(line); return; }
            var pick = cands[Math.Min(sel, cands.Count - 1)];
            input.Text = ""; tabList = null; sel = 0;
            Print(PromptText + line, White);
            var trimmed = line.Trim();
            if (history.Count == 0 || history[history.Count - 1] != trimmed) history.Add(trimmed);
            histPos = -1;
            try { pick.Run(); }
            catch (Exception ex) { Print("error: " + ex.Message, Red); }
            UpdatePrompt();
            RefreshSuggest();
        }

        bool CompleteSelected()
        {
            if (cands.Count == 0 || input.Text.Contains(" ")) return false;
            var c = cands[Math.Min(sel, cands.Count - 1)];
            if (c.Complete == null) return false;
            input.Text = c.Complete; input.CaretIndex = input.Text.Length;
            return true;
        }
    }
}

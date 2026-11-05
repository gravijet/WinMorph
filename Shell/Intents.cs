using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace WinMorph.Shell
{
    public sealed class Intent
    {
        public string Glyph, Title, Sub;
        public ImageSource Icon;
        public Action Run;
    }

    /// <summary>
    /// Understands what the user typed: start an app, run a system command, search the web,
    /// or ask ChatGPT (the fallback for everything else).
    /// </summary>
    public static class Intents
    {
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        static readonly string[] OpenWords = { "öffne ", "oeffne ", "starte ", "start ", "open ", "launch ", "run " };
        static readonly string[] SearchWords = { "suche ", "such ", "google ", "search ", "web " };
        static readonly string[] AskWords = { "frag ", "frage ", "ask ", "chatgpt ", "ki ", "ai " };

        static bool Has(string q, params string[] words) => words.Any(w => q == w || q.StartsWith(w + " ") || q.EndsWith(" " + w) || q.Contains(" " + w + " ") || q == w.Trim());

        public static List<Intent> For(string text, int maxApps = 5)
        {
            var list = new List<Intent>();
            var raw = (text ?? "").Trim();
            if (raw.Length == 0) return list;
            var q = raw.ToLowerInvariant();

            // "ask …" → ChatGPT first
            var ask = AskWords.FirstOrDefault(w => q.StartsWith(w));
            if (ask != null) { list.Add(AskGpt(raw.Substring(ask.Length))); }

            // "open X"
            var open = OpenWords.FirstOrDefault(w => q.StartsWith(w));
            string appQuery = open != null ? raw.Substring(open.Length) : raw;
            var apps = Match(appQuery).Take(maxApps).ToList();
            foreach (var a in apps)
                list.Add(new Intent { Icon = a.Icon, Title = open != null ? L("Öffne ", "Open ") + a.Name : a.Name, Sub = L("App", "App"), Run = a.Start });

            // system commands
            void Cmd(string glyph, string title, Action run, params string[] words)
            {
                if (words.Any(w => q == w || q.StartsWith(w) || (w.Length > 4 && q.Contains(w)))) list.Insert(Math.Min(list.Count, apps.Count > 0 && open != null ? 1 : 0), new Intent { Glyph = glyph, Title = title, Sub = L("Systembefehl", "System command"), Run = run });
            }
            Cmd("", L("Bildschirm sperren", "Lock screen"), ShellActions.Lock, "sperr", "lock");
            Cmd("", L("Neu starten", "Restart"), ShellActions.Restart, "neustart", "neu starten", "restart", "reboot");
            Cmd("", L("Herunterfahren", "Shut down"), ShellActions.Shutdown, "herunterfahren", "ausschalten", "shutdown", "shut down");
            Cmd("", L("Abmelden", "Sign out"), ShellActions.SignOut, "abmelden", "sign out", "logout", "log out");
            Cmd("", L("Energie sparen", "Sleep"), ShellActions.Sleep, "energie sparen", "schlaf", "sleep");
            Cmd("", L("Einstellungen öffnen", "Open Settings"), ShellActions.Settings, "einstellung", "settings");
            Cmd("", L("Dateien öffnen", "Open files"), () => ShellActions.Files(), "dateien", "explorer", "files", "ordner");
            Cmd("", L("Dunkles Design einschalten", "Switch to dark mode"), () => ShellActions.SetDark(true), "dunkel", "dark", "nachtmodus");
            Cmd("", L("Helles Design einschalten", "Switch to light mode"), () => ShellActions.SetDark(false), "hell", "light");
            Cmd("", L("Lauter", "Volume up"), () => ShellActions.Volume(+1), "lauter", "volume up", "louder");
            Cmd("", L("Leiser", "Volume down"), () => ShellActions.Volume(-1), "leiser", "volume down", "quieter");
            Cmd("", L("Ton aus/an", "Mute/unmute"), () => ShellActions.Volume(0), "stumm", "mute", "ton aus");
            Cmd("", L("Bildschirmfoto", "Screenshot"), ShellActions.Screenshot, "screenshot", "bildschirmfoto", "screen shot");
            Cmd("", L("Desktop anzeigen", "Show desktop"), ShellActions.ShowDesktop, "desktop");
            Cmd("", L("Task-Manager", "Task Manager"), ShellActions.TaskManager, "task", "taskmanager");
            Cmd("", L("WinMorph öffnen (Looks & Wiederherstellen)", "Open WinMorph (looks & restore)"), ShellActions.WinMorphApp, "winmorph", "wiederherstell", "restore", "design");

            var search = SearchWords.FirstOrDefault(w => q.StartsWith(w));
            if (search != null) list.Insert(0, Web(raw.Substring(search.Length)));

            if (ask == null) list.Add(AskGpt(raw));
            if (search == null) list.Add(Web(raw));
            return list;
        }

        public static Intent AskGpt(string q) => new Intent
        {
            Icon = AppCatalog.AiIcon(), Title = L("ChatGPT fragen: ", "Ask ChatGPT: ") + q.Trim(), Sub = "ChatGPT", Run = () => ShellActions.AskAi(q.Trim()),
        };

        public static Intent Web(string q) => new Intent
        {
            Glyph = "", Title = L("Im Web suchen: ", "Search the web: ") + q.Trim(), Sub = "Web", Run = () => ShellActions.WebSearch(q.Trim()),
        };

        public static IEnumerable<AppEntry> Match(string f)
        {
            f = (f ?? "").Trim();
            if (f.Length == 0) return Enumerable.Empty<AppEntry>();
            return AppCatalog.Apps.Where(a => a.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .OrderBy(a => a.Name.Equals(f, StringComparison.CurrentCultureIgnoreCase) ? 0 : a.Name.StartsWith(f, StringComparison.CurrentCultureIgnoreCase) ? 1 : 2)
                .ThenBy(a => a.Name.Length);
        }
    }
}

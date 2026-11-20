using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinMorph.Core
{
    /// <summary>One thing WinMorph changed, in plain words, with its original and its current state.</summary>
    public sealed class Change
    {
        public string Group, Title, Before, Now;
        /// <summary>True when the system no longer matches the original (the change is in effect).</summary>
        public bool Active;
        /// <summary>Raw entries behind this line (for verification counts).</summary>
        public int Values = 1;
    }

    /// <summary>
    /// Compares the original backup with the live system. The same list is the "what was changed"
    /// page before a rollback and the proof afterwards: after a complete rollback nothing is active.
    /// </summary>
    public static class Changes
    {
        static bool De => Loc.De;
        static string L(string de, string en) => De ? de : en;

        public static List<Change> Read(Backup b)
        {
            var list = new List<Change>();
            var shell = WinMorph.Shell.ShellControl.CurrentPreset;
            string look = Presets.Find(shell ?? "")?.Name ?? shell;

            // ---- run-time changes of the design shell (not in the backup file) ----
            bool taken = WinMorph.Shell.ShellControl.TaskbarTaken || WinMorph.Shell.ShellControl.NativeHidden;
            list.Add(new Change
            {
                Group = "shell", Title = L("Windows-Taskleiste", "Windows taskbar"), Active = taken,
                Before = L("sichtbar", "visible"),
                Now = taken ? L("ausgeblendet, ersetzt durch ", "hidden, replaced by ") + (look ?? "WinMorph") : L("sichtbar", "visible"),
            });
            bool winL = LockGuard.Armed;
            list.Add(new Change
            {
                Group = "lock", Title = L("Win+L", "Win+L"), Active = winL,
                Before = L("Windows sperrt direkt", "Windows locks directly"),
                Now = winL ? L("erst WinMorph-Sperrbildschirm, dann Windows-Sperre", "WinMorph lock screen first, then the Windows lock") : L("Windows sperrt direkt", "Windows locks directly"),
            });

            // window frames and pointers are drawn by the running shell only (nothing stored): they end with it
            bool running = WinMorph.Shell.ShellControl.IsRunning && shell != null;
            list.Add(new Change
            {
                Group = "shell", Title = L("Fensterrahmen", "Window frames"), Active = running,
                Before = L("Windows-Farben", "Windows colours"), Now = running ? L("Titelleisten im Stil von ", "title bars styled as ") + look : L("Windows-Farben", "Windows colours"),
            });
            bool pointers = running && (shell == "win7" || shell == "terminal");
            list.Add(new Change
            {
                Group = "shell", Title = L("Mauszeiger", "Mouse pointers"), Active = pointers,
                Before = L("dein Zeigerschema", "your pointer scheme"), Now = pointers ? (shell == "win7" ? L("Aero-Zeiger (nur solange der Look läuft)", "Aero pointers (only while the look runs)") : L("Hacker-Zeiger (nur solange der Look läuft)", "hacker pointers (only while the look runs)")) : L("dein Zeigerschema", "your pointer scheme"),
            });

            bool task = Autostart.Exists;
            list.Add(new Change
            {
                Group = "shell", Title = L("Start bei der Anmeldung (Aufgabe)", "Start at sign-in (task)"), Active = task,
                Before = L("keine Aufgabe", "no task"), Now = task ? L("Aufgabe „" + Autostart.TaskName + "“", "task “" + Autostart.TaskName + "”") : L("keine Aufgabe", "no task"),
            });

            // ---- everything recorded in the backup ----
            var groups = new Dictionary<string, Change>();
            int legacyActive = 0, legacyTotal = 0;
            foreach (var e in b.Entries)
            {
                var p = e.Key.Split('\t');
                bool active;
                try { active = IsActive(p, e.Value); } catch { active = false; }

                var known = Describe(p, look);
                if (known == null)
                {
                    if (p[0] == "VAL" && (p[1] == "WALL" || p[1] == "LOCKIMG")) continue;
                    legacyTotal++;
                    if (active) legacyActive++;
                    continue;
                }
                if (groups.TryGetValue(known.Title, out var g)) { g.Active |= active; g.Values++; continue; }
                known.Active = active;
                if (!active) known.Now = known.Before;
                groups[known.Title] = known;
                list.Add(known);
            }

            // pictures (no registry value to compare)
            if (b.Has("VAL\tWALL"))
            {
                var orig = b.GetValue("WALL") ?? "";
                var cur = Applier.CurrentWallpaper();
                bool active = !string.Equals(Path.GetFullPath(Norm(orig)), Path.GetFullPath(Norm(cur)), StringComparison.OrdinalIgnoreCase);
                var w = groups.TryGetValue(L("Hintergrundbild", "Wallpaper"), out var wg) ? wg : null;
                if (w == null) list.Add(w = new Change { Group = "desktop", Title = L("Hintergrundbild", "Wallpaper"), Before = Short(orig) });
                w.Active |= active;
                w.Before = Short(orig);
                w.Now = active ? Short(cur) : w.Before;
            }
            if (b.Has("VAL\tLOCKIMG"))
            {
                // the original picture is kept as a file; whether it is shown again is only known after a restore
                bool active = shell != null;
                list.Add(new Change { Group = "lock", Title = L("Sperrbildschirm-Bild", "Lock screen picture"), Active = active, Before = L("dein Bild", "your picture"), Now = active ? L("Bild des Looks", "picture of the look") : L("dein Bild", "your picture") });
            }
            if (legacyTotal > 0)
                list.Add(new Change
                {
                    Group = "legacy", Title = L("Windows-Einstellungen älterer WinMorph-Versionen", "Windows settings from older WinMorph versions"),
                    Active = legacyActive > 0, Values = legacyTotal,
                    Before = L("Original", "original"),
                    Now = legacyActive > 0 ? string.Format(L("{0} von {1} Werten geändert", "{0} of {1} values changed"), legacyActive, legacyTotal) : L("Original", "original"),
                });
            return list;
        }

        static string Norm(string path) => string.IsNullOrEmpty(path) ? Path.GetTempPath() + "\u0001none" : path;
        static string Short(string path) => string.IsNullOrEmpty(path) ? L("einfarbig", "solid colour") : Path.GetFileName(path);

        static bool IsActive(string[] p, string orig)
        {
            switch (p[0])
            {
                case "REG": return Reg.Snapshot(p[1], p[2]) != orig;
                case "KEY": return orig == "0" && Reg.KeyExists(p[1]);
                default: return false;   // VAL entries without a readable twin (fonts, animation …) are legacy
            }
        }

        const string Run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Plain words for the entries current versions write; null = setting of an older version.</summary>
        static Change Describe(string[] p, string look)
        {
            if (p[0] != "REG") return null;
            string path = p[1], name = p[2];
            bool Is(string k) => path.Equals(k, StringComparison.OrdinalIgnoreCase);
            if (Is(WinMorph.Shell.ShellControl.SettingsKey))
            {
                if (name == "ShellPreset") return new Change { Group = "shell", Title = L("Design-Shell", "Design shell"), Before = L("aus", "off"), Now = L("an: ", "on: ") + (look ?? "?") + L(" (Taskleiste, Startmenü, Alt+Tab, Fensterrahmen)", " (taskbar, Start menu, Alt+Tab, window frames)") };
                if (name == "LiveWall") return new Change { Group = "desktop", Title = L("Live-Desktop", "Live desktop"), Before = L("aus", "off"), Now = L("an", "on") };
                if (name == "LastPreset")
                {
                    var last = Reg.Get(path, name) as string;
                    return new Change { Group = "shell", Title = L("Zuletzt gewählter Look (für den Sperrbildschirm)", "Last chosen look (for the lock screen)"), Before = L("keiner", "none"), Now = Presets.Find(last ?? "")?.Name ?? last ?? L("keiner", "none") };
                }
                return null;
            }
            if (Is(Run)) return new Change { Group = "shell", Title = L("Autostart bei der Anmeldung", "Start at sign-in"), Before = L("ohne WinMorph", "without WinMorph"), Now = L("Design-Shell und Live-Desktop", "design shell and live desktop") };
            if (Is(Applier.Serialize)) return new Change { Group = "shell", Title = L("Autostart-Verzögerung", "Start-up delay"), Before = L("Windows-Standard", "Windows default"), Now = L("keine, Design erscheint sofort", "none, the design shows at once") };
            if (Is(@"HKCU\Control Panel\Desktop"))
            {
                if (name == "WallpaperStyle" || name == "TileWallpaper") return new Change { Group = "desktop", Title = L("Hintergrundbild", "Wallpaper"), Before = "", Now = "" };
                if (name == "SCRNSAVE.EXE" || name == "ScreenSaverIsSecure") return new Change { Group = "lock", Title = L("Bildschirmschoner-Bild", "Screen saver picture"), Before = L("dein Bildschirmschoner", "your screen saver"), Now = L("WinMorph-Sperrbildschirm", "WinMorph lock screen") };
                return null;
            }
            if (Is(@"HKCU\Control Panel\Colors") || Is(Applier.WallpapersKey) || Is(Applier.SpotlightKey)) return new Change { Group = "desktop", Title = L("Hintergrundbild", "Wallpaper"), Before = "", Now = "" };
            if (Is(Applier.Cdm)) return new Change { Group = "lock", Title = L("Windows-Blickpunkt (Werbung) am Sperrbildschirm", "Windows Spotlight (ads) on the lock screen"), Before = L("wie vorher", "as before"), Now = L("aus", "off") };
            return null;
        }

        /// <summary>Number of raw values checked and how many still differ from the original.</summary>
        public static (int Checked, int Different) Verify(Backup b)
        {
            var list = Read(b);
            return (list.Sum(c => c.Values), list.Where(c => c.Active).Sum(c => c.Values));
        }
    }
}

using System.Collections.Generic;
using System.Globalization;

namespace WinMorph.Core
{
    public static class Loc
    {
        static bool de = (Reg.Get(@"HKCU\Software\WinMorph", "Language") as string ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "de";
        public static bool De { get => de; set => de = value; }
        public static CultureInfo Culture => CultureInfo.GetCultureInfo(De ? "de-DE" : "en-GB");

        public static void SaveLanguage(bool german)
        {
            De = german;
            using (var k = Reg.Create(@"HKCU\Software\WinMorph")) k.SetValue("Language", german ? "de" : "en");
        }

        static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            ["settingshead"] = new[] { "ÄNDERT WINDOWS-EINSTELLUNGEN · STANDARD AUS · WIRD BEIM WECHSEL ZURÜCKGESETZT", "CHANGES WINDOWS SETTINGS · OFF BY DEFAULT · RESET WHEN YOU SWITCH" },
            ["a.Lock"] = new[] { "Sperrbildschirm", "Lock screen" },
            ["c.livewall"] = new[] { "Eigener Live-Hintergrund (spart Akku, pausiert bei Vollbild)", "Own live wallpaper (battery friendly, pauses in full screen)" },
            ["subtitle"] = new[] { "Looks per Klick", "Looks in one click" },
            ["all"] = new[] { "Alle Presets", "All presets" },
            ["windows"] = new[] { "Windows-Klassiker", "Windows classics" },
            ["apple"] = new[] { "Apple", "Apple" },
            ["linux"] = new[] { "Linux & Co.", "Linux & co." },
            ["future"] = new[] { "Zukunft & KI", "Future & AI" },
            ["special"] = new[] { "Spezial", "Special" },
            ["restore"] = new[] { "Original wiederherstellen", "Restore original" },
            ["undo"] = new[] { "↶  Letzten Look rückgängig", "↶  Undo last look" },
            ["noundo"] = new[] { "Es gibt keinen früheren Look.", "There is no earlier look." },
            ["undone"] = new[] { "Vorheriger Look ist zurück ({0} Werte).", "Previous look is back ({0} values)." },
            ["steps"] = new[] { "{0} Schritte rückgängig machbar", "{0} steps can be undone" },
            ["c.lockscreen"] = new[] { "eigener Sperrbildschirm im Stil (Win+L und nach 10 Min., mit Kennwortschutz)", "own lock screen in this style (Win+L and after 10 min, password protected)" },
            ["c.lockpic"] = new[] { "Windows-Sperrbild im Stil, ohne Werbung und Tipps", "Windows lock picture in this style, without ads and tips" },
            ["nobackup"] = new[] { "Noch nichts geändert.", "Nothing changed yet." },
            ["backupfrom"] = new[] { "Backup vom {0} · {1} Werte", "Backup from {0} · {1} values" },
            ["headline"] = new[] { "Wähle einen Look", "Pick a look" },
            ["live"] = new[] { "Live-Hintergründe", "Live wallpapers" },
            ["livehead"] = new[] { "Live-Hintergründe", "Live wallpapers" },
            ["livesub"] = new[] { "Nur den Hintergrund ändern: ruhig mit Live-Systeminfos oder animiert und interaktiv (reagiert auf Maus und Klicks). Läuft hinter den Desktop-Symbolen und pausiert automatisch, sobald ein Fenster den Bildschirm füllt – kein Lag in Spielen. Der Rest deines Looks bleibt, wie er ist.", "Change only the wallpaper: calm with live system info, or animated and interactive (reacts to mouse and clicks). Runs behind the desktop icons and pauses automatically when a window fills the screen – no lag in games. The rest of your look stays as it is." },
            ["lg.own"] = new[] { "Eigene", "Your own" },
            ["lg.calm"] = new[] { "Ruhig & sparsam (Zen, Gaming)", "Calm & light (Zen, Gaming)" },
            ["lg.hacker"] = new[] { "Hacker", "Hacker" },
            ["lg.space"] = new[] { "Weltall", "Space" },
            ["lg.city"] = new[] { "Stadt", "City" },
            ["lg.nature"] = new[] { "Natur", "Nature" },
            ["lg.abstract"] = new[] { "Abstrakt", "Abstract" },
            ["live.apply"] = new[] { "Als Hintergrund setzen", "Set as wallpaper" },
            ["live.off"] = new[] { "Kein Live-Hintergrund", "No live wallpaper" },
            ["live.off.tag"] = new[] { "Normales Hintergrundbild", "Normal wallpaper picture" },
            ["live.off.desc"] = new[] { "Beendet den Live-Hintergrund. Das zuletzt gezeigte Standbild bleibt als normales Hintergrundbild.", "Stops the live wallpaper. The last still picture stays as normal wallpaper." },
            ["live.own"] = new[] { "Eigene Datei …", "Your own file …" },
            ["live.own.tag"] = new[] { "Video (mp4, wmv, mov) oder Bild mit Parallax", "Video (mp4, wmv, mov) or picture with parallax" },
            ["live.own.desc"] = new[] { "Wähle ein eigenes Video – es läuft stumm in Endlosschleife – oder ein Bild, das sich langsam bewegt und der Maus folgt.", "Pick your own video – it plays muted in a loop – or a picture that slowly moves and follows the mouse." },
            ["live.desc.scene"] = new[] { "Eigene Animation von WinMorph, in Echtzeit berechnet. Bewege die Maus über den Desktop oder klicke, um sie zu beeinflussen. Braucht kaum Leistung und pausiert, wenn ein Fenster maximiert ist.", "WinMorph's own animation, calculated in real time. Move the mouse over the desktop or click to interact. Very light and pauses when a window is maximised." },
            ["live.desc.calm"] = new[] { "Dunkler, ruhiger Desktop in deiner Hacker-Farbe mit Uhr, CPU, RAM, Laufwerken, Netz, Akku, WLAN und IP. Zeichnet nur einmal pro Sekunde – spart Akku – und pausiert, wenn ein Fenster maximiert ist. Deine Desktop-Symbole bleiben.", "Dark, calm desktop in your hacker colour with clock, CPU, RAM, drives, network, battery, Wi-Fi and IP. Draws only once a second – saves battery – and pauses when a window is maximised. Your desktop icons stay." },
            ["live.desc.video"] = new[] { "Kostenloses Video von {0}. Wird beim ersten Setzen einmal heruntergeladen ({1} MB) und dann stumm in Endlosschleife abgespielt. Pausiert, wenn ein Fenster maximiert ist.", "Free video from {0}. Downloaded once when you first set it ({1} MB), then played muted in a loop. Pauses when a window is maximised." },
            ["live.loading"] = new[] { "Lade Video … {0}%", "Downloading video … {0}%" },
            ["live.failed"] = new[] { "Download fehlgeschlagen – bitte Internetverbindung prüfen.", "Download failed – please check the internet connection." },
            ["live.done"] = new[] { "Live-Hintergrund „{0}“ läuft. Rückgängig jederzeit über „Letzten Look rückgängig“.", "Live wallpaper “{0}” is running. Undo any time with “Undo last look”." },
            ["live.offdone"] = new[] { "Live-Hintergrund ist aus.", "Live wallpaper is off." },
            ["live.current"] = new[] { "● aktiv", "● active" },
            ["headsub"] = new[] { "Jeder Look ersetzt das Aussehen von Windows durch ein eigenes Design: Taskleiste, Startmenü, Fensterrahmen, Alt+Tab, Hintergrund und Sperrbildschirm. Deine Windows-Einstellungen bleiben dabei unverändert – was ein Look zusätzlich ändern darf, wählst du pro Look selbst. Alles lässt sich vollständig wiederherstellen.", "Each look replaces how Windows looks with its own design: taskbar, Start menu, window frames, Alt+Tab, wallpaper and lock screen. Your Windows settings stay as they are – you choose per look what else it may change. Everything can be fully restored." },
            ["changes"] = new[] { "WAS DIESER LOOK ANWENDET", "WHAT THIS LOOK APPLIES" },
            ["options"] = new[] { "OPTIONEN", "OPTIONS" },
            ["a.Shell"] = new[] { "Design-Oberfläche", "Design shell" },
            ["c.noshell"] = new[] { "Original-Windows-Taskleiste und -Startmenü (WinMorph-Shell aus)", "Original Windows taskbar and Start menu (WinMorph shell off)" },
            ["c.shell.bar.Bottom"] = new[] { "Eigene Taskleiste im Originalstil", "Own taskbar in the original style" },
            ["c.shell.bar.MacTopDock"] = new[] { "Menüleiste oben + Dock mit Vergrößerung", "Menu bar on top + magnifying dock" },
            ["c.shell.bar.GnomeTopLeftDock"] = new[] { "GNOME-Leiste oben + Dock links", "GNOME panel on top + dock on the left" },
            ["c.shell.bar.GnomeTopBottomDock"] = new[] { "GNOME-Leiste oben + Dock unten", "GNOME panel on top + dock at the bottom" },
            ["c.shell.bar.Floating"] = new[] { "Schwebende Leiste mit Leuchteffekt", "Floating glowing bar" },
            ["c.shell.bar.TerminalTop"] = new[] { "Statuszeile oben mit CPU/RAM", "Status line on top with CPU/RAM" },
            ["c.shell.menu.Classic"] = new[] { "klassisches Startmenü mit Seitenbanner", "classic Start menu with side banner" },
            ["c.shell.menu.Xp"] = new[] { "zweispaltiges XP-Startmenü", "two-column XP Start menu" },
            ["c.shell.menu.Seven"] = new[] { "Startmenü mit Suche und Ordner-Spalte", "Start menu with search and places column" },
            ["c.shell.menu.Ten"] = new[] { "App-Liste mit Kacheln", "app list with tiles" },
            ["c.shell.menu.Grid"] = new[] { "App-Raster (Launchpad/Übersicht)", "app grid (Launchpad/overview)" },
            ["c.shell.menu.Palette"] = new[] { "Befehlspalette mit Suche", "command palette with search" },
            ["c.shell.frames"] = new[] { "Titelleisten aller Programme im Stil (Rahmen bleiben neutral)", "title bars of all apps in this style (borders stay neutral)" },
            ["c.shell.winkey"] = new[] { "Windows-Taste, Strg+Esc und Win+X öffnen die neuen Menüs", "Windows key, Ctrl+Esc and Win+X open the new menus" },
            ["c.shell.alttab"] = new[] { "eigenes Alt+Tab mit Live-Vorschau", "own Alt+Tab with live previews" },
            ["c.shell.gpt"] = new[] { "KI-Fragen, Win+C und Copilot-Taste gehen an ChatGPT", "AI questions, Win+C and the Copilot key go to ChatGPT" },
            ["c.shell.bar.AiOnly"] = new[] { "nur eine schwebende KI-Befehlszeile", "just one floating AI command line" },
            ["c.shell.bar.Waybar"] = new[] { "schwebende Waybar oben mit Modulen", "floating Waybar on top with modules" },
            ["c.shell.menu.Hub"] = new[] { "Vollbild-KI-Hub, versteht Befehle", "full-screen AI hub that understands commands" },
            ["a.Taskbar"] = new[] { "Taskleiste", "Taskbar" },
            ["a.Colors"] = new[] { "Farben & Modus", "Colours & mode" },
            ["a.Windows"] = new[] { "Fenster & Bewegung", "Windows & motion" },
            ["a.Fonts"] = new[] { "Systemschrift", "System font" },
            ["a.Desktop"] = new[] { "Desktop & Hintergrund", "Desktop & wallpaper" },
            ["a.Sounds"] = new[] { "Systemklänge", "System sounds" },
            ["a.Cursor"] = new[] { "Mauszeiger", "Mouse pointer" },
            ["a.Start"] = new[] { "Startmenü", "Start menu" },
            ["opt.explorer"] = new[] { "Explorer automatisch neu starten (offene Explorer-Fenster schließen sich)", "Restart Explorer automatically (open Explorer windows close)" },
            ["opt.lock"] = new[] { "Auch Bild der Windows-Anmeldung für alle Benutzer festlegen (Admin-Abfrage)", "Also set the sign-in picture for all users (admin prompt)" },
            ["apply"] = new[] { "{0} anwenden", "Apply {0}" },
            ["applying"] = new[] { "Wird angewendet …", "Applying …" },
            ["restoring"] = new[] { "Wird wiederhergestellt …", "Restoring …" },
            ["done"] = new[] { "Fertig: {0} Einstellungen geändert.", "Done: {0} settings changed." },
            ["problems"] = new[] { "Nicht möglich: {0}", "Not possible: {0}" },
            ["restore.incomplete"] = new[] { "Noch nicht vollständig wiederhergestellt. Das Backup bleibt erhalten. Bitte erneut versuchen und eine nötige Admin-Abfrage bestätigen.", "Restoration is incomplete. Your backup has been kept. Please try again and accept an administrator prompt if needed." },
            ["restored"] = new[] { "Wiederhergestellt: {0} Werte. Dein ursprüngliches Windows ist zurück.", "Restored {0} values. Your original Windows is back." },
            ["confirm.title"] = new[] { "Preset anwenden", "Apply preset" },
            ["confirm.first"] = new[] { "WinMorph ändert jetzt das Aussehen von Windows.\n\nVorher wird automatisch ein Backup angelegt. Zurück geht es jederzeit über:\n• Letzten Look rückgängig (vorheriger Look)\n• Original wiederherstellen (Windows wie vorher)\n• Startmenü → WinMorph → Original wiederherstellen\n• Notfall: Strg+Alt+Umschalt+F12 beendet den Design-Shell sofort\n\nFortfahren?", "WinMorph will now change how Windows looks.\n\nA backup is created first. You can always go back via:\n• Undo last look (previous look)\n• Restore original (Windows as before)\n• Start menu → WinMorph → Restore original\n• Emergency: Ctrl+Alt+Shift+F12 stops the design shell at once\n\nContinue?" },
            ["confirm.restore"] = new[] { "Alles auf den Stand vor WinMorph zurücksetzen – inklusive Hintergrundbild (auch Windows-Blickpunkt oder Diashow) und Sperrbildschirm?", "Reset everything to how it was before WinMorph – including the wallpaper (also Windows Spotlight or slideshow) and the lock screen?" },
            ["footer"] = new[] { "Kostenlos & Open Source · ändert standardmäßig keine Windows-Einstellungen", "Free & open source · changes no Windows settings by default" },

            // updates
            ["upd.title"] = new[] { "Version {0} ist da", "Version {0} is here" },
            ["upd.loading"] = new[] { "Wird im Hintergrund geladen …", "Downloading in the background …" },
            ["upd.ready"] = new[] { "Bereit. Installiert sich in Sekunden – deine Looks bleiben, wie sie sind.", "Ready. Installs in seconds – your looks stay as they are." },
            ["upd.portable"] = new[] { "Diese Kopie ist nicht installiert. Das neue Setup gibt es auf der Website.", "This copy is not installed. Get the new setup from the website." },
            ["upd.install"] = new[] { "Jetzt aktualisieren", "Update now" },
            ["upd.download"] = new[] { "Zur Website", "Open website" },
            ["upd.progress"] = new[] { "Lade Update … {0} %", "Downloading update … {0} %" },
            ["upd.installing"] = new[] { "Wird installiert – WinMorph startet gleich neu …", "Installing – WinMorph restarts in a moment …" },
            ["upd.auto"] = new[] { "Updates automatisch installieren", "Install updates automatically" },
            ["upd.version"] = new[] { "Version {0}", "Version {0}" },

            // change descriptions
            ["c.left"] = new[] { "links", "left" }, ["c.center"] = new[] { "zentriert", "centred" },
            ["c.align"] = new[] { "Ausrichtung {0}", "{0} aligned" },
            ["c.combine0"] = new[] { "Fenster gruppiert", "Windows grouped" },
            ["c.combine2"] = new[] { "Fenster mit Beschriftung", "Labelled window buttons" },
            ["c.search0"] = new[] { "Suche aus", "Search hidden" }, ["c.search1"] = new[] { "Such-Symbol", "Search icon" },
            ["c.search2"] = new[] { "Suchfeld", "Search box" }, ["c.search3"] = new[] { "Such-Knopf mit Text", "Search button with label" },
            ["c.taskview"] = new[] { "Taskansicht", "Task View" }, ["c.widgets"] = new[] { "Widgets", "Widgets" }, ["c.copilot"] = new[] { "Copilot-Knopf", "Copilot button" },
            ["c.autohide"] = new[] { "automatisch ausblenden", "auto-hide" }, ["c.seconds"] = new[] { "Uhr mit Sekunden", "Clock with seconds" },
            ["c.dark"] = new[] { "Dunkel", "Dark" }, ["c.light"] = new[] { "Hell", "Light" },
            ["c.mode"] = new[] { "System {0}, Apps {1}", "System {0}, apps {1}" },
            ["c.transp"] = new[] { "Transparenz", "Transparency" }, ["c.notransp"] = new[] { "keine Transparenz", "no transparency" },
            ["c.accent"] = new[] { "Akzent {0}", "Accent {0}" },
            ["c.acctb"] = new[] { "farbige Taskleiste", "coloured taskbar" }, ["c.acctitle"] = new[] { "farbige Titelleisten", "coloured title bars" },
            ["c.anim"] = new[] { "Animationen", "Animations" }, ["c.noanim"] = new[] { "keine Animationen", "no animations" },
            ["c.outline"] = new[] { "Ziehen nur als Rahmen", "Drag as outline only" }, ["c.fulldrag"] = new[] { "Fensterinhalt beim Ziehen", "Show contents while dragging" },
            ["c.snap"] = new[] { "Andocken (Snap)", "Snap" }, ["c.nosnap"] = new[] { "kein Andocken", "no snapping" },
            ["c.noshadow"] = new[] { "keine Schatten", "no shadows" }, ["c.focus"] = new[] { "Fokus folgt Maus", "Focus follows mouse" },
            ["c.nocleartype"] = new[] { "ClearType aus", "ClearType off" }, ["c.menudelay"] = new[] { "Menüs ohne Verzögerung", "Instant menus" },
            ["c.ctx"] = new[] { "klassisches Rechtsklick-Menü", "classic right-click menu" }, ["c.compact"] = new[] { "kompakter Explorer", "compact Explorer" },
            ["c.font"] = new[] { "{0}{1} für Titelleisten, Menüs, Dialoge & Desktop-Symbole", "{0}{1} for title bars, menus, dialogs & desktop icons" },
            ["c.bold"] = new[] { " (fette Titel)", " (bold titles)" },
            ["c.wall"] = new[] { "Eigener generierter Hintergrund", "Custom generated wallpaper" },
            ["c.noicons"] = new[] { "Desktop-Symbole ausgeblendet", "Desktop icons hidden" }, ["c.thispc"] = new[] { "„Dieser PC“ auf dem Desktop", "“This PC” on the desktop" },
            ["c.scr"] = new[] { "Bildschirmschoner {0}", "Screensaver {0}" },
            ["c.silent"] = new[] { "Stumm (keine Systemklänge)", "Silent (no system sounds)" }, ["c.sounds"] = new[] { "Windows-Standardklänge", "Windows default sounds" },
            ["cur.Aero"] = new[] { "Windows Aero (Standard)", "Windows Aero (default)" }, ["cur.Classic"] = new[] { "Klassisch (95/XP)", "Classic (95/XP)" },
            ["cur.Black"] = new[] { "Schwarz", "Black" }, ["cur.Inverted"] = new[] { "Invertiert", "Inverted" },
            ["c.layout0"] = new[] { "Standard-Layout", "Default layout" }, ["c.layout1"] = new[] { "mehr angeheftete Apps", "more pinned apps" }, ["c.layout2"] = new[] { "mehr Empfehlungen", "more recommendations" },
            ["c.norec"] = new[] { "keine Empfehlungen", "no recommendations" },
            ["c.os"] = new[] { "Open-Shell: {0}", "Open-Shell: {0}" },
            ["os.Classic1"] = new[] { "klassisch (95)", "classic (95)" }, ["os.Classic2"] = new[] { "zweispaltig (XP)", "two columns (XP)" }, ["os.Win7"] = new[] { "Windows-7-Stil", "Windows 7 style" },
            ["c.epstart"] = new[] { "Windows-10-Startmenü (ExplorerPatcher)", "Windows 10 Start menu (ExplorerPatcher)" },
        };

        public static string Get(string key)
        {
            if (T.TryGetValue(key, out var v)) return De ? v[0] : v[1];
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(Get(key), args);
    }
}

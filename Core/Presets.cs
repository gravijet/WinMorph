using System.Collections.Generic;
using System.Windows.Media;

namespace WinMorph.Core
{
    public static class Presets
    {
        static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        public static readonly List<Preset> All = new List<Preset>
        {
            // ---------------- Windows ----------------
            new Preset {
                Id = "win11", Name = "Windows 11", Group = "windows",
                TagDe = "Microsoft-Standard", TagEn = "Microsoft defaults",
                DescDe = "Setzt alles auf die normalen Windows-11-Werte zurück: zentrierte Taskleiste, helles Design, blaue Blüte.",
                DescEn = "Puts everything back to stock Windows 11: centred taskbar, light theme, blue bloom.",
                Center = true, Search = 2, TaskView = true, Widgets = true, Accent = C(0x0078D4), Wall = WallKind.Bloom,
            },
            new Preset {
                Id = "win10", Name = "Windows 10", Group = "windows",
                TagDe = "Links, Suchfeld, dunkle Leiste", TagEn = "Left, search box, dark bar",
                DescDe = "Taskleiste links mit Suchfeld und Taskansicht, dunkle Taskleiste, helle Apps, klassisches Rechtsklick-Menü. Mit ExplorerPatcher: echte Windows-10-Taskleiste und -Startmenü.",
                DescEn = "Left taskbar with search box and Task View, dark taskbar, light apps, classic right-click menu. With ExplorerPatcher: the real Windows 10 taskbar and Start menu.",
                Center = false, Search = 2, TaskView = true, DarkSystem = true, Accent = C(0x0078D7), Wall = WallKind.Beams,
                Compact = true, ClassicContextMenu = true, StartLayout = 1, EpTaskbar = 1, EpClassicStart = true,
            },
            new Preset {
                Id = "win7", Name = "Windows 7", Group = "windows",
                TagDe = "Aero-Glas & Startkugel", TagEn = "Aero glass & start orb",
                DescDe = "Glasige blaue Taskleiste und Titelleisten, Aero-Hintergrund, „Dieser PC“ auf dem Desktop. Mit Open-Shell: Windows-7-Startmenü mit Startkugel.",
                DescEn = "Glassy blue taskbar and title bars, Aero wallpaper, “This PC” on the desktop. With Open-Shell: Windows 7 Start menu with orb.",
                Center = false, Search = 0, TaskView = false, DarkSystem = true, Accent = C(0x2B6CB3), AccentTaskbar = true, AccentTitle = true,
                Wall = WallKind.Aero, Compact = true, ClassicContextMenu = true, LaunchThisPC = true, ShowThisPC = true, ScreenSaver = "Bubbles.scr",
                OsStyle = "Win7", OsSkin = "Windows Aero", OsButton = "AeroButton", EpTaskbar = 1,
            },
            new Preset {
                Id = "xp", Name = "Windows XP", Group = "windows",
                TagDe = "Luna-Blau, grüner Hügel", TagEn = "Luna blue, green hill",
                DescDe = "Blaue Taskleiste mit beschrifteten Fenstern, blaue Titelleisten in Tahoma fett, klassische Mauszeiger, grüner Hügel. Mit Open-Shell: XP-Startmenü (Luna).",
                DescEn = "Blue taskbar with labelled buttons, blue bold Tahoma title bars, classic cursors, green hill. With Open-Shell: XP Start menu (Luna).",
                Center = false, Combine = 2, Search = 0, TaskView = false, DarkSystem = true, Transparency = false, Accent = C(0x245EDC),
                AccentTaskbar = true, AccentTitle = true, Wall = WallKind.Hills, Cursor = CursorKind.Classic, Font = "Tahoma", CaptionBold = true,
                Compact = true, ClassicContextMenu = true, LaunchThisPC = true, ShowThisPC = true, ScreenSaver = "Mystify.scr",
                OsStyle = "Classic2", OsSkin = "Windows XP Luna", OsButton = "ClassicButton", EpTaskbar = 1,
            },
            new Preset {
                Id = "win95", Name = "Windows 95", Group = "windows",
                TagDe = "Grau, Petrol, keine Effekte", TagEn = "Grey, teal, no effects",
                DescDe = "Petrol-Hintergrund, keine Animationen, Fenster werden nur als Rahmen gezogen, kein ClearType, Microsoft Sans Serif, kein Snap. Mit Open-Shell: klassisches Startmenü.",
                DescEn = "Teal desktop, no animations, windows drag as outlines, no ClearType, Microsoft Sans Serif, no snapping. With Open-Shell: classic Start menu.",
                Center = false, Combine = 2, Search = 0, TaskView = false, Transparency = false, Accent = C(0x000080), AccentTitle = true,
                Animations = false, FullDrag = false, MenuAnim = false, Shadows = false, Snap = false, ClearType = false,
                Wall = WallKind.Teal, Cursor = CursorKind.Classic, Font = "Microsoft Sans Serif", CaptionBold = true,
                Compact = true, ClassicContextMenu = true, LaunchThisPC = true, ShowThisPC = true, ScreenSaver = "Mystify.scr",
                OsStyle = "Classic1", OsSkin = "Classic Skin", OsButton = "ClassicButton", EpTaskbar = 1,
            },

            // ---------------- Apple ----------------
            new Preset {
                Id = "macos", Name = "macOS", Group = "apple",
                TagDe = "Menüleiste & Dock · hell oder dunkel", TagEn = "Menu bar & dock · light or dark",
                DescDe = "Menüleiste oben, Dock mit Vergrößerung unten, Launchpad, Wellen-Hintergrund. Hell oder dunkel – einfach unten umschalten.",
                DescEn = "Menu bar on top, magnifying dock at the bottom, Launchpad, wave wallpaper. Light or dark – just switch below.",
                Center = true, Search = 0, TaskView = false, AutoHide = true, Accent = C(0x007AFF), Wall = WallKind.Waves, Cursor = CursorKind.Black,
                Shake = false, StartLayout = 1, Recommendations = false,
            },
            // ---------------- Linux ----------------
            new Preset {
                Id = "ubuntu", Name = "Ubuntu", Group = "linux",
                TagDe = "Aubergine & Orange", TagEn = "Aubergine & orange",
                DescDe = "Dunkles Yaru-Gefühl mit orangem Akzent, Leiste links. Mit ExplorerPatcher: Leiste senkrecht am linken Rand wie das Ubuntu-Dock.",
                DescEn = "Dark Yaru feel with orange accent, bar on the left. With ExplorerPatcher: a vertical bar on the left edge like the Ubuntu dock.",
                Center = false, Search = 0, TaskView = true, DarkSystem = true, DarkApps = true, Accent = C(0xE95420),
                Wall = WallKind.Jelly, ClassicContextMenu = false, EpTaskbar = 1, EpEdge = 0, Recommendations = false,
            },
            new Preset {
                Id = "debian", Name = "Debian", Group = "linux",
                TagDe = "Schlicht, Debian-Rot", TagEn = "Plain, Debian red",
                DescDe = "Helles GNOME-Gefühl mit rotem Akzent, Verdana-Schrift und Spiral-Hintergrund. Mit ExplorerPatcher: Leiste oben wie die GNOME-Leiste.",
                DescEn = "Light GNOME feel with red accent, Verdana font and a swirl wallpaper. With ExplorerPatcher: bar at the top like the GNOME panel.",
                Center = true, Search = 1, TaskView = true, Accent = C(0xD70A53), Wall = WallKind.Swirl, Font = "Verdana",
                EpTaskbar = 1, EpEdge = 1, Recommendations = false,
            },
            new Preset {
                Id = "arch", Name = "Arch Linux", Group = "linux",
                TagDe = "Tiling, Waybar, Arch-Blau", TagEn = "Tiling, Waybar, Arch blue",
                DescDe = "Minimal wie ein Hyprland-Setup: schwebende Waybar oben mit Fenster-Nummern, CPU-, RAM-, Netz- und Uhr-Modulen, Rofi-artiger Starter, eckige Fenster mit blauem Rahmen, Cascadia-Mono-Schrift.",
                DescEn = "Minimal like a Hyprland setup: floating Waybar on top with window numbers, CPU, RAM, network and clock modules, a Rofi-style launcher, square windows with blue borders, Cascadia Mono font.",
                Center = false, Combine = 2, Search = 0, TaskView = false, DarkSystem = true, DarkApps = true, Transparency = true, Accent = C(0x1793D1),
                AccentTitle = false, Wall = WallKind.Arch, Font = "Cascadia Mono", MenuDelay = 0, Recommendations = false, ClassicContextMenu = true,
            },
            new Preset {
                Id = "mint", Name = "Linux Mint", Group = "linux",
                TagDe = "Cinnamon, Mint-Grün", TagEn = "Cinnamon, mint green",
                DescDe = "Klassische Leiste mit beschrifteten Fenstern, dunkle Leiste, mintgrüner Akzent. Mit Open-Shell: zweispaltiges Menü wie Cinnamon.",
                DescEn = "Classic bar with labelled windows, dark bar, mint green accent. With Open-Shell: two-column menu like Cinnamon.",
                Center = false, Combine = 2, Search = 0, TaskView = false, DarkSystem = true, Accent = C(0x6FA33A), Wall = WallKind.Mint,
                ClassicContextMenu = true, OsStyle = "Win7", OsSkin = "Midnight", OsButton = "ClassicButton", EpTaskbar = 1,
            },
            new Preset {
                Id = "chromeos", Name = "ChromeOS", Group = "linux",
                TagDe = "Hell, verspielt, rund", TagEn = "Light, playful, round",
                DescDe = "Zentrierte Leiste mit Such-Symbol, Google-Blau und weichen Pastellfarben.",
                DescEn = "Centred bar with a search icon, Google blue and soft pastel colours.",
                Center = true, Search = 1, TaskView = false, Accent = C(0x1A73E8), Wall = WallKind.Chrome, Recommendations = false,
            },

            // ---------------- Future / AI ----------------
            new Preset {
                Id = "nova", Name = "NovaOS", Group = "future",
                TagDe = "Erfunden: Aurora-Glas", TagEn = "Invented: aurora glass",
                DescDe = "Ein ausgedachtes Betriebssystem der Zukunft: tiefschwarz, violettes Glas, Bahnschrift-Schrift, Sekunden in der Uhr, aufgeräumtes Startmenü, Ribbons-Bildschirmschoner.",
                DescEn = "An imagined future OS: deep black, violet glass, Bahnschrift font, seconds on the clock, tidy Start menu, Ribbons screensaver.",
                Center = true, Search = 1, TaskView = false, DarkSystem = true, DarkApps = true, Accent = C(0x8B5CF6), AccentTitle = true,
                Seconds = true, Wall = WallKind.Aurora, Font = "Bahnschrift", StartLayout = 1, Recommendations = false, ScreenSaver = "Ribbons.scr",
            },
            new Preset {
                Id = "nexus", Name = "Nexus AI", Group = "future",
                TagDe = "Nur KI – ultramodern", TagEn = "AI only – ultra modern",
                DescDe = "Ein Betriebssystem, das nur aus KI besteht: keine klassische Taskleiste, sondern eine schwebende Befehlszeile. Schreib einfach, was du willst („öffne Firefox“, „dunkel“, „lauter“, „sperren“ oder eine Frage) – Apps starten sofort, alles andere beantwortet ChatGPT. Holografischer Hintergrund, KI-Sperrbildschirm.",
                DescEn = "An operating system made only of AI: no classic taskbar, just a floating command line. Type what you want (“open Firefox”, “dark”, “louder”, “lock” or a question) – apps start instantly, ChatGPT answers everything else. Holographic wallpaper, AI lock screen.",
                Center = true, Search = 0, TaskView = false, Widgets = false, DarkSystem = true, DarkApps = true, Accent = C(0x7C5CFF), AccentTitle = true,
                Wall = WallKind.Holo, Font = "Segoe UI Variable Display", StartLayout = 1, Recommendations = false, MenuDelay = 100, Silent = true,
            },
            new Preset {
                Id = "terminal", Name = "Hacker", Group = "future",
                TagDe = "Terminal-Look, Farbe frei wählbar", TagEn = "Terminal look, any colour you like",
                DescDe = "Der komplette Hacker-Look in deiner Farbe: Desktop mit Live-Systeminfos (Uhr, CPU, RAM, Akku, WLAN, IP) und deinen Symbolen, Windows-Taste öffnet ein echtes Terminal mit über 50 Befehlen, eigenes Win+R, eigene Benachrichtigungen, Statuszeile mit Netz/CPU/RAM/Lautstärke/Akku, WLAN-Panel zum Trennen und Verbinden, Alt+Tab als Prozess-Tabelle, Terminal-Sperrbildschirm, Tastenkürzel wie unter Linux (Win+Enter, Win+1…9, Win+Umschalt+Q). Spart Akku: keine Animationen auf dem Desktop.",
                DescEn = "The complete hacker look in your colour: a desktop with live system info (clock, CPU, RAM, battery, Wi-Fi, IP) and your icons, the Windows key opens a real terminal with 50+ commands, own Win+R, own notifications, status line with net/CPU/RAM/volume/battery, a Wi-Fi panel to disconnect and connect, Alt+Tab as a process table, terminal lock screen, Linux-style shortcuts (Win+Enter, Win+1…9, Win+Shift+Q). Saves battery: no animations on the desktop.",
                Live = "hacker-desk",
                Center = false, Combine = 2, Search = 0, TaskView = false, DarkSystem = true, DarkApps = true, Transparency = false, Accent = C(0x00FF41),
                AccentTaskbar = false, AccentTitle = false, Animations = false, MenuAnim = false, MenuDelay = 0, Shadows = false, FocusFollows = false,
                Wall = WallKind.Matrix, Font = "Consolas", Silent = true, Cursor = CursorKind.Black, Recommendations = false, ShowThisPC = true,
            },

            // ---------------- Special ----------------
            new Preset {
                Id = "zen", Name = "Zen", Group = "special",
                TagDe = "Ruhe, Fokus, Achtsamkeit", TagEn = "Calm, focus, mindfulness",
                DescDe = "Ein ganzes Achtsamkeits-System: Berglandschaft, deren Licht der Tageszeit folgt (Morgen, Tag, Abend, Nacht), große ruhige Uhr mit Spruch des Tages, Fokus-Timer (25/5 Minuten) in der Leiste, Atemübung im Vollbild, Hintergrundrauschen und Regen zum Konzentrieren, sanfte Hinweise statt Pop-ups, schlichter Sperrbildschirm. Keine Desktop-Symbole.",
                DescEn = "A whole mindfulness system: a mountain landscape whose light follows the time of day (morning, day, evening, night), a big calm clock with a quote of the day, a focus timer (25/5 minutes) in the bar, a full-screen breathing exercise, background noise and rain for concentration, gentle notices instead of pop-ups, a plain lock screen. No desktop icons.",
                Live = "zen-desk",
                Center = true, Search = 0, TaskView = false, AutoHide = true, Accent = C(0xA0805A), HideDesktopIcons = true, Silent = true,
                Wall = WallKind.Zen, Recommendations = false, StartLayout = 1,
            },
            new Preset {
                Id = "gaming", Name = "Gaming", Group = "special",
                TagDe = "Spiele-Bibliothek, Leistung, Overlay", TagEn = "Game library, performance, overlay",
                DescDe = "Ein Gaming-System: Spiele-Bibliothek mit allen Steam- und Epic-Spielen (Start mit einem Klick), Leistungsmodus per Klick (Höchstleistung, wird beim Beenden zurückgesetzt), Leiste mit CPU, GPU, RAM und Ping, Leistungs-Overlay im Spiel (Win+Umschalt+F), Desktop mit Live-Systemwerten, roter Sperrbildschirm.",
                DescEn = "A gaming system: a game library with all your Steam and Epic games (start with one click), performance mode in one click (maximum performance, reset when you stop), a bar with CPU, GPU, RAM and ping, an in-game performance overlay (Win+Shift+F), a desktop with live system values, a red lock screen.",
                Live = "gaming-desk",
                Center = true, Search = 0, TaskView = false, DarkSystem = true, DarkApps = true, Transparency = false, Accent = C(0xE81E4A),
                AccentTitle = true, Animations = false, MenuAnim = false, Shadows = false, MenuDelay = 0, Wall = WallKind.Stripes,
                Font = "Bahnschrift", Recommendations = false,
            },
        };

        // looks that were merged into others
        static string Alias(string id) => id == "macos-dark" ? "macos" : id == "synapse" ? "nexus" : id;

        public static Preset Find(string id) { id = Alias(id); return All.Find(p => p.Id == id); }

        // ---------------- per-look settings ----------------
        const string PartsKey = @"HKCU\Software\WinMorph\Parts";

        /// <summary>What a look applies. By default only WinMorph's own design – no Windows settings.</summary>
        public static Area Parts(string id)
            => Reg.Get(PartsKey, Alias(id)) is int v ? (Area)v & Area.All : Area.Design;

        public static void SetParts(string id, Area a)
        {
            using (var k = Reg.Create(PartsKey)) k.SetValue(Alias(id), (int)a, Microsoft.Win32.RegistryValueKind.DWord);
        }

        /// <summary>macOS in dark mode (HKCU\Software\WinMorph\MacDark = 1).</summary>
        public static bool MacDark
        {
            get => Reg.Get(@"HKCU\Software\WinMorph", "MacDark") is int v ? v == 1 : Reg.Get(@"HKCU\Software\WinMorph", "ShellPreset") as string == "macos-dark";
            set { using (var k = Reg.Create(@"HKCU\Software\WinMorph")) k.SetValue("MacDark", value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); MacMode(); }
        }

        static Presets() => MacMode();

        /// <summary>Puts the light or dark values into the macOS look.</summary>
        static void MacMode()
        {
            var m = All.Find(p => p.Id == "macos");
            bool dark = MacDark;
            m.DarkSystem = m.DarkApps = dark;
            m.Accent = dark ? C(0x0A84FF) : C(0x007AFF);
            m.Wall = dark ? WallKind.WavesDark : WallKind.Waves;
        }
    }
}

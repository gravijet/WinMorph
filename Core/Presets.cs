using System.Collections.Generic;
using System.Windows.Media;

namespace WinMorph.Core
{
    public static class Presets
    {
        static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        public static readonly List<Preset> All = new List<Preset>
        {
            new Preset {
                Id = "win7", Name = "Windows 7",
                TagDe = "Aero-Glas & Startkugel", TagEn = "Aero glass & start orb",
                DescDe = "Glasige Taskleiste mit leuchtender Startkugel, das zweispaltige Windows-7-Startmenü mit Suche, Aero-Alt+Tab mit Live-Vorschau, blaue Glas-Titelleisten und der Aero-Sperrbildschirm.",
                DescEn = "A glassy taskbar with a glowing start orb, the two-column Windows 7 Start menu with search, Aero Alt+Tab with live previews, blue glass title bars and the Aero lock screen.",
                Accent = C(0x2B6CB3), Wall = WallKind.Aero,
            },
            new Preset {
                Id = "zen", Name = "Zen",
                TagDe = "Ruhe, Licht, Fokus", TagEn = "Calm, light, focus",
                DescDe = "Eine schwebende, helle Leiste, Berglandschaft mit Licht nach Tageszeit, ruhige Uhr mit Spruch des Tages, Fokus-Timer, Atemübung und Regen-Geräusch in der Leiste, ein Alt+Tab mit Shōji-Schiebetüren und Ema-Täfelchen und ein stiller Sperrbildschirm.",
                DescEn = "A floating light bar, a mountain landscape lit by the time of day, a calm clock with a quote of the day, focus timer, breathing exercise and rain sound in the bar, an Alt+Tab with shoji sliding doors and ema plaques and a quiet lock screen.",
                Accent = C(0xA0805A), Wall = WallKind.Zen, Live = "zen-desk",
            },
            new Preset {
                Id = "terminal", Name = "Hacker",
                TagDe = "Terminal-Look in deiner Farbe", TagEn = "Terminal look in your colour",
                DescDe = "Statuszeile oben mit Netz, CPU, RAM, Lautstärke, Akku und Uhr, die Windows-Taste öffnet ein echtes Terminal, Alt+Tab als Prozess-Tabelle mit Live-Vorschau, dunkle Fensterrahmen, Desktop mit Live-Systeminfos und ein Terminal-Sperrbildschirm.",
                DescEn = "A status line on top with network, CPU, RAM, volume, battery and clock, the Windows key opens a real terminal, Alt+Tab as a process table with live preview, dark window frames, a desktop with live system info and a terminal lock screen.",
                Accent = C(0x00FF41), Wall = WallKind.Matrix, Live = "hacker-desk",
            },
        };

        /// <summary>Looks from older versions are gone; null makes the app and shell ignore them.</summary>
        public static Preset Find(string id) => All.Find(p => p.Id == id);
    }
}

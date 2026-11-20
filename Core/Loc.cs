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
            ["restore"] = new[] { "Original wiederherstellen", "Restore original" },
            ["restore.incomplete"] = new[] { "Noch nicht vollständig wiederhergestellt. Das Backup bleibt erhalten. Bitte erneut versuchen und eine nötige Admin-Abfrage bestätigen.", "Restoration is incomplete. Your backup has been kept. Please try again and accept an administrator prompt if needed." },
        };

        public static string Get(string key)
        {
            if (T.TryGetValue(key, out var v)) return De ? v[0] : v[1];
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(Get(key), args);
    }
}

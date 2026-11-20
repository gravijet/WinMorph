using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WinMorph.Core
{
    /// <summary>Checks backup/restore on a private test key only. Never touches real settings.</summary>
    internal static class SelfTest
    {
        public static int Run(string report)
        {
            var log = new StringBuilder();
            int fails = 0;
            void Check(bool ok, string what) { log.AppendLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }
            const string key = @"HKCU\Software\WinMorphSelfTest";
            var file = Path.Combine(Path.GetTempPath(), "winmorph-selftest-backup.txt");
            try
            {
                if (File.Exists(file)) File.Delete(file);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\WinMorphSelfTest", false);
                using (var k = Reg.Create(key))
                {
                    k.SetValue("dw", 42, RegistryValueKind.DWord);
                    k.SetValue("sz", "hallo\twelt ü", RegistryValueKind.String);
                    k.SetValue("ex", @"%SystemRoot%\cursors\a.cur", RegistryValueKind.ExpandString);
                    k.SetValue("bin", new byte[] { 1, 2, 3, 250 }, RegistryValueKind.Binary);
                }
                var b = new Backup(file);
                foreach (var n in new[] { "dw", "sz", "ex", "bin", "absent" }) b.Reg(key, n);
                b.Key(key + @"\NewKey");
                b.Value("SPI:MenuShowDelay", "400");
                b.Save();

                using (var k = Reg.Create(key))
                {
                    k.SetValue("dw", 1, RegistryValueKind.DWord);
                    k.SetValue("sz", "x"); k.SetValue("ex", "y"); k.SetValue("bin", new byte[] { 9 }); k.SetValue("absent", 5);
                }
                Reg.Create(key + @"\NewKey\Sub").Dispose();

                var b2 = new Backup(file);
                Check(b2.Count == 7, "backup reloads 7 entries (" + b2.Count + ")");
                // the changes page sees what differs from the original …
                var before = Changes.Read(b2).Find(c => c.Group == "legacy");
                Check(before != null && before.Active && before.Values == 7 && before.Now.Contains("6"), "changes page: 6 of 7 values differ (" + before?.Now + ")");
                // restore only the test entries
                foreach (var e in b2.Entries)
                {
                    var p = e.Key.Split('\t');
                    if (p[0] == "REG") Reg.RestoreSnapshot(p[1], p[2], e.Value);
                    if (p[0] == "KEY" && e.Value == "0") { var root = Reg.Root(p[1], out var sub); root.DeleteSubKeyTree(sub, false); }
                }
                using (var k = Reg.Open(key))
                {
                    Check((int)k.GetValue("dw") == 42, "DWORD restored");
                    Check((string)k.GetValue("sz") == "hallo\twelt ü", "string with tab/umlaut restored");
                    Check(k.GetValueKind("ex") == RegistryValueKind.ExpandString && (string)k.GetValue("ex", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == @"%SystemRoot%\cursors\a.cur", "expand string restored unexpanded");
                    var bin = (byte[])k.GetValue("bin");
                    Check(bin.Length == 4 && bin[3] == 250, "binary restored");
                    Check(k.GetValue("absent") == null, "value that did not exist is removed");
                }
                Check(!Reg.KeyExists(key + @"\NewKey"), "key that did not exist is removed");
                // … and proves the rollback: nothing differs any more
                var after = Changes.Read(b2).Find(c => c.Group == "legacy");
                Check(after != null && !after.Active, "changes page: all values back to the original after the rollback");
                Check(b2.GetValue("SPI:MenuShowDelay") == "400", "misc value stored");

                // the undo step records what is set now, for every entry of the original backup
                var step = new Backup(file + ".step");
                Applier.CaptureCurrent(step, b2);
                Check(step.Has("REG\t" + key + "\tdw") && step.Has("KEY\t" + key + @"\NewKey"), "undo step captures the current values");

                // hacker pointers: the generated .cur file loads as a real cursor (nothing is set system-wide here)
                Check(WinMorph.Shell.CursorSkin.SelfCheck(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winmorph-test.cur")), "generated mouse pointer loads");
                // every look has its own start menu kind; Zen's is the hanging scroll
                Check(WinMorph.Shell.ShellThemes.For("zen").Menu == WinMorph.Shell.MenuKind.Zen && WinMorph.Shell.ShellThemes.For("win7").Menu == WinMorph.Shell.MenuKind.Seven, "start menu per look");

                // looks: exactly Windows 7, Zen and Hacker, each with its own bar, Alt+Tab and lock screen
                Check(string.Join(",", Presets.All.ConvertAll(x => x.Id)) == "win7,zen,terminal", "three looks: win7, zen, terminal");
                var themes = Presets.All.ConvertAll(x => WinMorph.Shell.ShellThemes.For(x.Id));
                Check(themes.TrueForAll(x => x != null) && new System.Collections.Generic.HashSet<WinMorph.Shell.SwitchKind>(themes.ConvertAll(x => x.Switch)).Count == 3
                    && new System.Collections.Generic.HashSet<WinMorph.Shell.LockKind>(themes.ConvertAll(x => x.Lock)).Count == 3, "every look has its own Alt+Tab and lock screen");
                Check(Presets.Find("macos") == null && WinMorph.Shell.ShellThemes.For("win11") == null && WinMorph.Shell.ShellThemes.ForLock("nexus")?.Id == "win7", "looks of older versions are ignored safely");
                // a backup of today's looks holds no Windows settings; one of older versions does (then Explorer reloads them)
                var design = new Backup(file + ".design");
                design.Reg(@"HKCU\Control Panel\Desktop", "WallpaperStyle"); design.Reg(Applier.Cdm, "RotatingLockScreenEnabled"); design.Reg(WinMorph.Shell.ShellControl.SettingsKey, "ShellPreset"); design.Value("WALL", "");
                Check(!Restorer.HasLegacySettings(design) && Restorer.HasLegacySettings(b2), "design looks change no Windows settings; old settings are recognised");

                // sign-in start: a well-formed task for this user, normal priority, restarted on failure, path escaped
                var taskXml = new System.Xml.XmlDocument();
                taskXml.LoadXml(Autostart.Xml(@"C:\Program Files\A&B\WinMorph.exe").Substring(Autostart.Xml("x").IndexOf("<Task", StringComparison.Ordinal)));
                Check(taskXml.InnerXml.Contains("<Priority>4</Priority>") && taskXml.InnerXml.Contains("<LogonTrigger>") && taskXml.InnerXml.Contains("RestartOnFailure")
                    && taskXml.DocumentElement["Actions"].InnerText.Contains(@"A&B\WinMorph.exe"), "sign-in task definition");

                Check(new RestoreResult().Complete && !new RestoreResult { Failed = 1 }.Complete
                    && !new RestoreResult { NeedsElevation = true }.Complete, "failed or unelevated restores must keep their backup");

                // Language changes cover future shell processes and cached previews, too.
                bool originalLanguage = Loc.De;
                try
                {
                    var translations = (System.Collections.Generic.Dictionary<string, string[]>)typeof(Loc)
                        .GetField("T", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                    foreach (var pair in translations)
                        Check(pair.Value.Length == 2 && !string.IsNullOrWhiteSpace(pair.Value[0]) && !string.IsNullOrWhiteSpace(pair.Value[1]), "DE/EN translation: " + pair.Key);
                    Loc.De = true;
                    Check(Loc.Get("restore") == "Original wiederherstellen" && Loc.Culture.TwoLetterISOLanguageName == "de", "German UI and formatting");
                    Loc.De = false;
                    Check(Loc.Get("restore") == "Restore original" && Loc.Culture.TwoLetterISOLanguageName == "en", "English UI and formatting");
                }
                finally { Loc.De = originalLanguage; }

                // updates: only a well-formed answer of our own site counts, and only newer versions
                const string sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                string Rel(string v, string url, string h, string size) => "{\"version\":\"" + v + "\",\"files\":{\"setup\":{\"url\":\"" + url + "\",\"size\":" + size + ",\"sha256\":\"" + h + "\"}}}";
                var ur = Updater.Parse(Rel("9.1.0", "/download/setup", sha, "123456"));
                Check(ur != null && ur.Version == "9.1.0" && ur.Size == 123456 && ur.Sha256 == sha, "update answer is read");
                Check(Updater.Parse(Rel("9.1.0", "https://evil.example/x.exe", sha, "1")) == null, "update from another address is refused");
                Check(Updater.Parse(Rel("9.1.0", "/download/setup", "nope", "1")) == null && Updater.Parse(Rel("9.1.0", "/download/setup", sha, "0")) == null, "update without checksum or size is refused");
                Check(Updater.Parse("{\"error\":\"unavailable\"}") == null, "error answer is no update");
                Check(Updater.Newer("5.10.0", "5.9.3") && !Updater.Newer("5.2.0", "5.2.0") && !Updater.Newer("5.1.9", "5.2.0") && !Updater.Newer("latest", "5.2.0"), "version comparison");

                // Zen sounds: valid looping WAV
                var wav = WinMorph.Shell.Zen.Wav(true);
                Check(wav.Length == 44 + 12 * 22050 * 2 && wav[0] == 'R' && wav[8] == 'W', "Zen rain sound is a valid WAV (" + wav.Length + " bytes)");

                // read-only system checks
                var m = Native.GetNcm();
                Check(!string.IsNullOrEmpty(m.lfCaptionFont.lfFaceName), "read window metrics (caption font: " + m.lfCaptionFont.lfFaceName + ")");
                var round = Native.FromBytes<Native.NONCLIENTMETRICS>(Native.ToBytes(m));
                Check(round.lfMenuFont.lfFaceName == m.lfMenuFont.lfFaceName && round.iCaptionHeight == m.iCaptionHeight, "window metrics byte round-trip");
                var lockFile = Path.Combine(Path.GetTempPath(), "winmorph-selftest-lock.img");
                bool lockOk = LockImage.SaveCurrent(lockFile);
                Check(lockOk, "read current lock screen picture (" + (lockOk ? new FileInfo(lockFile).Length + " bytes" : "failed") + ")");
                if (File.Exists(lockFile)) File.Delete(lockFile);
                foreach (var s in Applier.SpiValues) log.AppendLine("     SPI " + s.Name + " = " + Native.GetInt(s.Get));
                log.AppendLine("     AutoHide = " + Applier.GetAutoHide());
                log.AppendLine("     OpenShell=" + Tools.OpenShellInstalled + " EP=" + Tools.ExplorerPatcherInstalled + " Build=" + Tools.Build + " Admin=" + Tools.IsAdmin);
            }
            catch (Exception ex) { Check(false, "exception: " + ex); }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\WinMorphSelfTest", false);
                if (File.Exists(file)) File.Delete(file);
                if (File.Exists(file + ".step")) File.Delete(file + ".step");
            }
            File.WriteAllText(report, log.ToString());
            return fails;
        }
    }
}

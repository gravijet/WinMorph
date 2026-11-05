using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinMorph.Core
{
    /// <summary>
    /// Updates from winmorph.benjaminberger.at: /api/release names the newest version with size and SHA-256,
    /// /download/setup is the setup. The verified setup runs silently (/S /update): it stops WinMorph, copies
    /// the new files and starts the shell and the live wallpaper again.
    /// </summary>
    public static class Updater
    {
        public const string Site = "https://winmorph.benjaminberger.at";
        public static readonly string Version = typeof(Updater).Assembly.GetName().Version.ToString(3);
        const string Key = Shell.ShellControl.SettingsKey;
        const long MaxSize = 200L * 1024 * 1024;

        public sealed class Release
        {
            public string Version, Url, Sha256, Published;
            public long Size;
        }

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinMorph");
        static string Exe => Process.GetCurrentProcess().MainModule.FileName;
        static string Dir { get { var d = Path.Combine(Backup.DataDir, "update"); Directory.CreateDirectory(d); return d; } }

        /// <summary>Only the installed copy updates itself; a portable copy gets a link to the website.</summary>
        public static bool Installed => string.Equals(Path.GetDirectoryName(Exe), InstallDir, StringComparison.OrdinalIgnoreCase);

        /// <summary>Install new versions automatically in the background (default on).</summary>
        public static bool Auto
        {
            get => Reg.GetDword(Key, "AutoUpdate") != 0;
            set { using (var k = Reg.Create(Key)) k.SetValue("AutoUpdate", value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); }
        }

        /// <summary>The newest release if it is newer than this program, else null. Throws when the site is unreachable.</summary>
        public static Release Check()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json;
            using (var c = new WebClient { Encoding = System.Text.Encoding.UTF8 })
            {
                c.Headers[HttpRequestHeader.UserAgent] = "WinMorph/" + Version;
                c.Headers[HttpRequestHeader.Accept] = "application/json";
                json = c.DownloadString(Site + "/api/release");
            }
            using (var k = Reg.Create(Key)) k.SetValue("UpdateChecked", DateTime.UtcNow.ToString("o"));
            var r = Parse(json);
            return r != null && Newer(r.Version, Version) ? r : null;
        }

        /// <summary>Reads the site's answer; anything unexpected (no setup, bad checksum, odd address) is no release.</summary>
        public static Release Parse(string json)
        {
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null || !(d.TryGetValue("files", out var f) && f is Dictionary<string, object> files)) return null;
            if (!(files.TryGetValue("setup", out var s) && s is Dictionary<string, object> setup)) return null;
            var r = new Release
            {
                Version = d.TryGetValue("version", out var v) ? v as string : null,
                Published = d.TryGetValue("published", out var p) ? p as string : null,
                Url = setup.TryGetValue("url", out var u) ? u as string : null,
                Sha256 = (setup.TryGetValue("sha256", out var h) ? h as string : null)?.ToLowerInvariant(),
                Size = setup.TryGetValue("size", out var z) && z != null ? Convert.ToInt64(z) : 0,
            };
            bool ok = r.Version != null && System.Version.TryParse(r.Version, out _)
                && r.Url != null && r.Url.StartsWith("/download/") && r.Url.IndexOfAny(new[] { '?', '#', '\\' }) < 0 && !r.Url.Contains("..")
                && r.Sha256 != null && r.Sha256.Length == 64 && Array.TrueForAll(r.Sha256.ToCharArray(), Uri.IsHexDigit)
                && r.Size > 0 && r.Size < MaxSize;
            return ok ? r : null;
        }

        public static bool Newer(string candidate, string current)
            => System.Version.TryParse(candidate, out var a) && System.Version.TryParse(current, out var b) && a > b;

        /// <summary>Downloads the setup and checks size and SHA-256; returns the verified file (kept for a second try).</summary>
        public static async Task<string> Download(Release r, Action<int> progress)
        {
            var file = Path.Combine(Dir, "WinMorph-Setup-" + r.Version + ".exe");
            if (File.Exists(file) && Verify(file, r)) return file;
            var part = file + ".part";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var c = new WebClient())
                {
                    c.Headers[HttpRequestHeader.UserAgent] = "WinMorph/" + Version;
                    if (progress != null) c.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                    await c.DownloadFileTaskAsync(new Uri(Site + r.Url), part);
                }
                if (!Verify(part, r)) throw new InvalidDataException(Loc.De ? "Die Prüfsumme des Updates stimmt nicht." : "The update's checksum does not match.");
                if (File.Exists(file)) File.Delete(file);
                File.Move(part, file);
                return file;
            }
            finally { try { File.Delete(part); } catch { } }
        }

        static bool Verify(string file, Release r)
        {
            if (new FileInfo(file).Length != r.Size) return false;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant() == r.Sha256;
        }

        /// <summary>Runs the verified setup silently. With relaunch, the app opens again afterwards.</summary>
        public static void Install(string setup, bool relaunch)
            => Process.Start(new ProcessStartInfo(setup, "/S /update" + (relaunch ? " /launch" : "")) { UseShellExecute = false });

        /// <summary>Removes downloaded setups of versions that are installed by now.</summary>
        public static void Cleanup()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Dir))
                {
                    var v = Path.GetFileNameWithoutExtension(f).Replace("WinMorph-Setup-", "").Replace(".exe", "");
                    if (!Newer(v, Version)) File.Delete(f);
                }
            }
            catch { }
        }

        // ---------- the main window is open: it shows the update itself, the shell waits ----------
        const string UiMutex = "WinMorphUi_v1";
        static Mutex ui;
        public static void MarkUiOpen() { try { ui = new Mutex(false, UiMutex); } catch { } }
        static bool UiOpen { get { try { using (Mutex.OpenExisting(UiMutex)) return true; } catch { return false; } } }

        /// <summary>
        /// In the shell process: looks for a new version a few minutes after sign-in and then every six hours,
        /// and installs it once nobody has used the PC for two minutes (the taskbar is gone for a few seconds).
        /// </summary>
        public static void RunInShell()
        {
            new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromMinutes(3));
                Cleanup();
                while (true)
                {
                    try
                    {
                        if (Auto && Installed && !UiOpen)
                        {
                            var r = Check();
                            if (r != null)
                            {
                                var file = Download(r, null).GetAwaiter().GetResult();
                                while (IdleSeconds() < 120 || UiOpen) Thread.Sleep(TimeSpan.FromSeconds(30));
                                if (Auto) { Install(file, relaunch: false); return; }
                            }
                        }
                    }
                    catch (Exception ex) { try { File.AppendAllText(Path.Combine(Backup.DataDir, "update-errors.log"), DateTime.Now + " " + ex.Message + Environment.NewLine); } catch { } }
                    Thread.Sleep(TimeSpan.FromHours(6));
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            return GetLastInputInfo(ref li) ? unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0 : 0;
        }
    }
}

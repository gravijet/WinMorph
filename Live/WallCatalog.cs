using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace WinMorph.Live
{
    public sealed class WallItem
    {
        public string Id, NameDe, NameEn, TagDe, TagEn, Group;
        public string Scene;          // built-in animation
        public string VideoUrl;       // internet video
        public string ThumbUrl;
        public bool Interactive;
        public int SizeMb;
        public string Credit;

        public string Name(bool de) => de ? NameDe : NameEn;
        public string Tag(bool de) => de ? TagDe : TagEn;
        public bool IsVideo => VideoUrl != null;
        public string LocalVideo => Path.Combine(WallCatalog.VideoDir, Id + ".mp4");
        public bool Downloaded => IsVideo && File.Exists(LocalVideo) && new FileInfo(LocalVideo).Length > 100000;
        public string Spec => Scene != null ? "scene:" + Scene : "video:" + LocalVideo;
    }

    /// <summary>All live wallpapers: own animations plus free videos from the internet (Mixkit, free licence).</summary>
    public static class WallCatalog
    {
        public static string VideoDir { get { var d = Path.Combine(Core.Backup.DataDir, "videos"); Directory.CreateDirectory(d); return d; } }
        public static string ThumbDir { get { var d = Path.Combine(Core.Backup.DataDir, "thumbs"); Directory.CreateDirectory(d); return d; } }

        public static readonly string[] Groups = { "calm", "hacker", "abstract" };

        static WallItem S(string id, string group, string de, string en, string tagDe, string tagEn, bool interactive = true)
            => new WallItem { Id = id, Scene = id, Group = group, NameDe = de, NameEn = en, TagDe = tagDe, TagEn = tagEn, Interactive = interactive };

        static WallItem M(int id, string group, string de, string en, int mb)
            => new WallItem
            {
                Id = "mixkit-" + id, Group = group, NameDe = de, NameEn = en, SizeMb = mb,
                TagDe = "Video aus dem Internet · " + mb + " MB", TagEn = "Video from the internet · " + mb + " MB",
                VideoUrl = "https://assets.mixkit.co/videos/" + id + "/" + id + "-720.mp4",
                ThumbUrl = "https://assets.mixkit.co/videos/" + id + "/" + id + "-thumb-360-0.jpg",
                Credit = "Mixkit (mixkit.co) – free licence",
            };

        public static readonly List<WallItem> All = new List<WallItem>
        {
            // ---- calm desktops of the Zen and Gaming looks ----
            S("zen-desk", "calm", "Zen-Landschaft", "Zen landscape", "Licht folgt der Tageszeit, Uhr & Gedanke des Tages", "Light follows the time of day, clock & daily thought", interactive: false),
            S("gaming-desk", "calm", "Gaming-HUD", "Gaming HUD", "CPU, GPU, RAM, Ping live · spart Akku", "CPU, GPU, RAM, ping live · saves battery", interactive: false),
            // ---- hacker ----
            S("hacker-desk", "hacker", "Hacker-Desktop", "Hacker desktop", "Ruhig, mit Live-Systeminfos · spart Akku", "Calm, with live system info · saves battery", interactive: false),
            S("matrix-hud", "hacker", "Matrix + System-HUD", "Matrix + system HUD", "Code-Regen mit Live-Systemmonitor · interaktiv", "Code rain with live system monitor · interactive"),
            S("matrix", "hacker", "Matrix", "Matrix", "Digitaler Regen · Maus beschleunigt, Klick = Ausbruch", "Digital rain · mouse speeds up, click = burst"),
            S("matrix-red", "hacker", "Matrix Rot", "Matrix red", "Roter Alarm-Code · interaktiv", "Red alert code · interactive"),
            S("matrix-cyan", "hacker", "Matrix Cyan", "Matrix cyan", "Kühler Cyber-Code · interaktiv", "Cool cyber code · interactive"),
            S("terminal-log", "hacker", "Terminal-Log", "Terminal log", "Endlose Hacker-Sitzung tippt live", "Endless hacker session typing live"),
            S("radar", "hacker", "Radar", "Radar", "Radarschirm · Maus wird verfolgt, Klick = Ziel markieren", "Radar scope · tracks the mouse, click = lock target"),
            S("plexus-green", "hacker", "Netzwerk Grün", "Network green", "Knoten-Netz · Maus zieht Linien, Klick = Schockwelle", "Node network · mouse pulls lines, click = shock wave"),
            S("starfield", "abstract", "Sternenflug", "Star flight", "Flug durchs All · Maus lenkt, Klick = Warp", "Flight through space · mouse steers, click = warp"),
            S("bokeh", "abstract", "Lichter", "Lights", "Weiche Lichter steigen auf · weichen der Maus aus", "Soft lights rise · avoid the mouse"),
            // ---- abstract ----
            S("plexus", "abstract", "Netzwerk", "Network", "Knoten-Netz · Maus zieht Linien", "Node network · mouse pulls lines"),
        };

        public static WallItem Find(string id) => All.FirstOrDefault(x => x.Id == id);

        /// <summary>The catalogue entry for a stored spec ("scene:matrix", "video:…\mixkit-123.mp4").</summary>
        public static WallItem FromSpec(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            var arg = LiveView.Arg(spec);
            if (spec.StartsWith("scene:")) return Find(arg);
            return All.FirstOrDefault(x => x.IsVideo && string.Equals(x.LocalVideo, arg, StringComparison.OrdinalIgnoreCase));
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new WebClient();
            c.Headers[HttpRequestHeader.UserAgent] = "WinMorph/5.1";
            return c;
        }

        /// <summary>Downloads the video once (with progress 0..100).</summary>
        public static async Task<bool> EnsureDownloaded(WallItem it, Action<int> progress)
        {
            if (!it.IsVideo || it.Downloaded) return true;
            var tmp = it.LocalVideo + ".part";
            try
            {
                using (var c = Client())
                {
                    c.DownloadProgressChanged += (s, e) => progress?.Invoke(e.ProgressPercentage);
                    await c.DownloadFileTaskAsync(new Uri(it.VideoUrl), tmp);
                }
                if (File.Exists(it.LocalVideo)) File.Delete(it.LocalVideo);
                File.Move(tmp, it.LocalVideo);
                return true;
            }
            catch { try { File.Delete(tmp); } catch { } return false; }
        }

        /// <summary>Preview picture for a video (downloaded once, cached).</summary>
        public static async Task<string> Thumb(WallItem it, bool large = false)
        {
            if (it.ThumbUrl == null) return null;
            var url = large ? it.ThumbUrl.Replace("-thumb-360-", "-thumb-720-") : it.ThumbUrl;
            var file = Path.Combine(ThumbDir, it.Id + (large ? "-720" : "") + ".jpg");
            if (File.Exists(file) && new FileInfo(file).Length > 1000) return file;
            try { using (var c = Client()) await c.DownloadFileTaskAsync(new Uri(url), file); return file; }
            catch { try { File.Delete(file); } catch { } return null; }
        }
    }
}

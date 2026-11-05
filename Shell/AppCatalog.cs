using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinMorph.Shell
{
    public sealed class AppEntry
    {
        public string Name, ParsingPath;   // ParsingPath = item path inside shell:AppsFolder
        public string ExePath;             // when known (for matching running windows)
        public string Launch;              // optional custom launch target (exe or URI)
        ImageSource icon; bool loaded;
        public ImageSource FallbackIcon;

        public ImageSource Icon
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    if (ParsingPath != null) icon = W32.ShellItemIcon(@"shell:AppsFolder\" + ParsingPath, 64);
                    if (icon == null && ExePath != null && File.Exists(ExePath)) icon = W32.FileIcon(ExePath);
                }
                return icon ?? FallbackIcon;
            }
        }

        /// <summary>Loads the icon now (called on the background thread so menus open instantly).</summary>
        public void Prefetch() { var _ = Icon; }

        public string PinKey => ParsingPath != null ? "app:" + ParsingPath : "exe:" + (Launch ?? ExePath);

        public void Start()
        {
            try
            {
                if (Launch != null) Process.Start(new ProcessStartInfo(Launch) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + ParsingPath) { UseShellExecute = true });
            }
            catch { }
        }

        public bool Matches(WinInfo w)
        {
            if (ParsingPath != null && w.AppId != null && string.Equals(ParsingPath, w.AppId, StringComparison.OrdinalIgnoreCase)) return true;
            return ExePath != null && w.ExePath != null && string.Equals(Path.GetFileName(ExePath), Path.GetFileName(w.ExePath), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>All apps from shell:AppsFolder (desktop programs and Store apps).</summary>
    public static class AppCatalog
    {
        public static List<AppEntry> Apps { get; private set; } = new List<AppEntry>();
        public static event Action Loaded;
        static readonly string[] junk = { "uninstall", "deinstall", "readme", "liesmich", "website", "help", "hilfe", "documentation", "release notes" };

        public static void LoadAsync(System.Windows.Threading.Dispatcher ui)
        {
            var t = new Thread(() =>
            {
                var list = new List<AppEntry>();
                try
                {
                    var type = Type.GetTypeFromProgID("Shell.Application");
                    object shell = Activator.CreateInstance(type);
                    object folder = type.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
                    object items = folder.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, folder, null);
                    int count = (int)items.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, items, null);
                    for (int i = 0; i < count; i++)
                    {
                        object it = items.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, items, new object[] { i });
                        string name = (string)it.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, it, null);
                        string path = (string)it.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, it, null);
                        if (string.IsNullOrWhiteSpace(name) || junk.Any(j => name.ToLowerInvariant().Contains(j))) continue;
                        var e = new AppEntry { Name = name, ParsingPath = path };
                        if (path != null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) e.ExePath = ResolveKnownFolder(path);
                        list.Add(e);
                    }
                }
                catch { }
                list = list.GroupBy(a => a.Name).Select(g => g.First()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                // screenshots on the build machine: only everyday apps instead of its developer tools
                var demo = Environment.GetEnvironmentVariable("WINMORPH_DEMO_APPS");
                if (!string.IsNullOrEmpty(demo)) { var keep = new HashSet<string>(demo.Split(';'), StringComparer.OrdinalIgnoreCase); list = list.Where(a => keep.Contains(a.Name)).ToList(); }
                ui.BeginInvoke(new Action(() => { Apps = list; Loaded?.Invoke(); }));
                // warm the icon cache in the background
                foreach (var a in list) { try { a.Prefetch(); } catch { } }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static readonly Dictionary<string, Environment.SpecialFolder> known = new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
        {
            ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.SpecialFolder.ProgramFiles,
            ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.SpecialFolder.ProgramFilesX86,
            ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.SpecialFolder.System,
            ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.SpecialFolder.Windows,
        };

        static string ResolveKnownFolder(string p)
        {
            int i = p.IndexOf('}');
            if (p.StartsWith("{") && i > 0 && known.TryGetValue(p.Substring(0, i + 1), out var f))
                return Environment.GetFolderPath(f) + p.Substring(i + 1);
            return p;
        }

        // ---------- pinned defaults for docks and quick launch ----------
        const string PinsKey = @"HKCU\Software\WinMorph";

        static List<string> PinKeys()
        {
            var keys = (Core.Reg.Get(PinsKey, "Pins") as string[])?.ToList();
            if (keys != null && Core.Reg.Get(PinsKey, "PinsV4") == null)
            {
                keys.RemoveAll(k => k.IndexOf("chatgpt.com", StringComparison.OrdinalIgnoreCase) >= 0);
                SavePins(keys);
                using (var k = Core.Reg.Create(PinsKey)) k.SetValue("PinsV4", 1, RegistryValueKind.DWord);
            }
            return keys;
        }

        static void SavePins(List<string> keys)
        {
            using (var k = Core.Reg.Create(PinsKey)) k.SetValue("Pins", keys.ToArray(), RegistryValueKind.MultiString);
        }

        public static bool IsPinned(AppEntry a) => Pinned().Any(p => p.PinKey == a.PinKey);

        public static void Pin(AppEntry a)
        {
            var keys = PinKeys() ?? Defaults().Select(p => p.PinKey).ToList();
            if (!keys.Contains(a.PinKey)) keys.Add(a.PinKey);
            SavePins(keys);
            PinsChanged?.Invoke();
        }

        public static void Unpin(AppEntry a)
        {
            var keys = PinKeys() ?? Defaults().Select(p => p.PinKey).ToList();
            keys.Remove(a.PinKey);
            SavePins(keys);
            PinsChanged?.Invoke();
        }

        public static event Action PinsChanged;
        static List<AppEntry> pinCache;
        static int pinCacheCount = -1;

        /// <summary>The user's pinned apps (or sensible defaults).</summary>
        public static List<AppEntry> Pinned()
        {
            if (pinCache != null && pinCacheCount == Apps.Count && !dirty) return pinCache;
            dirty = false;
            var keys = PinKeys();
            List<AppEntry> list;
            if (keys == null) list = Defaults();
            else
            {
                var defaults = Defaults();
                list = new List<AppEntry>();
                foreach (var k in keys)
                {
                    var hit = defaults.FirstOrDefault(d => d.PinKey == k)
                        ?? Apps.FirstOrDefault(a => a.PinKey == k);
                    if (hit == null && k.StartsWith("exe:")) { var exe = k.Substring(4); hit = new AppEntry { Name = WinInfoName(exe), ExePath = exe, Launch = exe }; }
                    if (hit != null) list.Add(hit);
                }
            }
            pinCacheCount = Apps.Count;
            return pinCache = list;
        }

        static bool dirty;
        static AppCatalog() { PinsChanged += () => dirty = true; }

        /// <summary>ChatGPT: the desktop app if installed, otherwise the website.</summary>
        public static AppEntry ChatGpt()
        {
            var app = Apps.FirstOrDefault(a => a.Name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase));
            return app ?? new AppEntry { Name = "ChatGPT", Launch = "https://chatgpt.com/", FallbackIcon = AiIcon() };
        }

        static ImageSource aiIcon;
        /// <summary>A neutral "AI" icon for the ChatGPT website entry.</summary>
        public static ImageSource AiIcon()
        {
            if (aiIcon != null) return aiIcon;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0x10, 0xA3, 0x7F), Color.FromRgb(0x0B, 0x6E, 0x58), 90), null, new System.Windows.Rect(0, 0, 64, 64), 16, 16);
                var ft = new FormattedText("✦", System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), 38, Brushes.White, 1.0);
                dc.DrawText(ft, new System.Windows.Point((64 - ft.Width) / 2, (64 - ft.Height) / 2));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return aiIcon = bmp;
        }

        /// <summary>
        /// Default pins = exactly what the user pinned to the Windows taskbar (plus File Explorer).
        /// WinMorph never adds icons of its own.
        /// </summary>
        static List<AppEntry> Defaults()
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var list = new List<AppEntry>
            {
                new AppEntry { Name = Core.Loc.De ? "Explorer" : "File Explorer", ExePath = Path.Combine(win, "explorer.exe"), Launch = Path.Combine(win, "explorer.exe") },
            };
            foreach (var p in TaskbarPins())
                if (!list.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase) || (x.ExePath != null && p.ExePath != null && string.Equals(Path.GetFileName(x.ExePath), Path.GetFileName(p.ExePath), StringComparison.OrdinalIgnoreCase))))
                    list.Add(p);
            if (list.Count == 1)
            {
                var browser = DefaultBrowser();
                if (browser != null) list.Add(new AppEntry { Name = WinInfoName(browser), ExePath = browser, Launch = browser });
            }
            return list;
        }

        static List<AppEntry> taskbarPins;
        static int taskbarPinsFor = -1;

        /// <summary>The shortcuts the user pinned to the normal Windows taskbar.</summary>
        static List<AppEntry> TaskbarPins()
        {
            if (taskbarPins != null && taskbarPinsFor == Apps.Count) return taskbarPins;
            var result = new List<AppEntry>();
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (Directory.Exists(dir))
                    foreach (var lnk in Directory.GetFiles(dir, "*.lnk").OrderBy(File.GetCreationTimeUtc))
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        if (name.Equals("File Explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("Explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("Datei-Explorer", StringComparison.OrdinalIgnoreCase)) continue;
                        var hit = Apps.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (hit != null) { result.Add(hit); continue; }
                        var target = LinkTarget(lnk);
                        result.Add(new AppEntry { Name = name, ExePath = target != null && File.Exists(target) ? target : null, Launch = lnk });
                    }
            }
            catch { }
            taskbarPinsFor = Apps.Count;
            return taskbarPins = result;
        }

        static string LinkTarget(string lnk)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(type);
                object sc = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                return sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
            }
            catch { return null; }
        }

        static AppEntry FindOr(string aumid, string name, string fallbackExe)
        {
            var hit = Apps.FirstOrDefault(a => string.Equals(a.ParsingPath, aumid, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { if (hit.ExePath == null) hit.ExePath = fallbackExe; return hit; }
            return new AppEntry { Name = name, ExePath = fallbackExe, Launch = fallbackExe };
        }

        static string WinInfoName(string exe)
        {
            try { var d = FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrWhiteSpace(d)) return d; } catch { }
            return Path.GetFileNameWithoutExtension(exe);
        }

        static string DefaultBrowser()
        {
            try
            {
                var progId = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId", null) as string;
                if (progId == null) return null;
                var cmd = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + progId + @"\shell\open\command", "", null) as string;
                if (cmd == null) return null;
                cmd = cmd.Trim();
                string exe = cmd.StartsWith("\"") ? cmd.Substring(1, cmd.IndexOf('"', 1) - 1) : cmd.Split(' ')[0];
                return File.Exists(exe) ? exe : null;
            }
            catch { return null; }
        }
    }
}

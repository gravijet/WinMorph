using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorph.Shell
{
    /// <summary>The user's real Windows account picture (or Windows' default one), for lock screens and start menus.</summary>
    internal static class AccountPicture
    {
        static readonly Dictionary<int, ImageSource> cache = new Dictionary<int, ImageSource>();

        /// <summary>The picture at least <paramref name="size"/> pixels wide, or null when Windows has none.</summary>
        public static ImageSource Get(int size)
        {
            if (cache.TryGetValue(size, out var hit)) return hit;
            ImageSource img = null;
            try
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
                var key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid;
                var files = new[] { 32, 40, 48, 64, 96, 192, 208, 240, 424, 448, 1080 }
                    .Select(n => (n, path: Core.Reg.Get(key, "Image" + n) as string))
                    .Where(x => x.path != null && File.Exists(x.path)).ToList();
                var best = files.FirstOrDefault(x => x.n >= size).path ?? files.LastOrDefault().path;
                if (best == null)
                {
                    var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\User Account Pictures", size <= 48 ? "user-48.png" : "user-192.png");
                    if (File.Exists(def)) best = def;
                }
                if (best != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(best); bi.DecodePixelWidth = Math.Max(size, 64); bi.EndInit(); bi.Freeze();
                    img = bi;
                }
            }
            catch { }
            return cache[size] = img;
        }

        /// <summary>The edition as Windows 7 named it on its lock screen ("Professional", "Home Premium", "Ultimate").</summary>
        public static string SevenEdition
        {
            get
            {
                var id = Core.Reg.Get(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID") as string ?? "";
                if (id.StartsWith("Core", StringComparison.OrdinalIgnoreCase)) return "Home Premium";
                if (id.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase)) return "Enterprise";
                if (id.StartsWith("Education", StringComparison.OrdinalIgnoreCase)) return "Ultimate";
                return "Professional";
            }
        }
    }
}

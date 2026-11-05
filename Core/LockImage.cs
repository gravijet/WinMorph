using System;
using System.IO;
using Windows.Storage;
using Windows.System.UserProfile;

namespace WinMorph.Core
{
    /// <summary>Per-user lock screen picture through the official Windows API (no admin needed).</summary>
    public static class LockImage
    {
        /// <summary>Saves the current lock screen picture so it can be restored later.</summary>
        public static bool SaveCurrent(string target)
        {
            try
            {
                var stream = LockScreen.GetImageStream();
                if (stream == null) return false;
                using (var input = stream.AsStreamForRead())
                using (var output = File.Create(target))
                    input.CopyTo(output);
                return new FileInfo(target).Length > 0;
            }
            catch { return false; }
        }

        public static bool Set(string file)
        {
            try
            {
                if (!File.Exists(file)) return false;
                var sf = StorageFile.GetFileFromPathAsync(Path.GetFullPath(file)).AsTask().Result;
                return LockScreen.SetImageFileAsync(sf).AsTask().Wait(15000);
            }
            catch { return false; }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinMorph.Core
{
    /// <summary>Win32 calls used to change system settings live.</summary>
    internal static class Native
    {
        public const uint SPIF_UPDATE = 0x01 | 0x02; // UPDATEINIFILE | SENDCHANGE

        public const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049;
        public const uint SPI_GETNONCLIENTMETRICS = 0x0029, SPI_SETNONCLIENTMETRICS = 0x002A;
        public const uint SPI_GETICONTITLELOGFONT = 0x001F, SPI_SETICONTITLELOGFONT = 0x0022;
        public const uint SPI_SETDESKWALLPAPER = 0x0014, SPI_GETDESKWALLPAPER = 0x0073;
        public const uint SPI_SETCURSORS = 0x0057;

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOGFONT
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NONCLIENTMETRICS
        {
            public int cbSize, iBorderWidth, iScrollWidth, iScrollHeight, iCaptionWidth, iCaptionHeight;
            public LOGFONT lfCaptionFont;
            public int iSmCaptionWidth, iSmCaptionHeight;
            public LOGFONT lfSmCaptionFont;
            public int iMenuWidth, iMenuHeight;
            public LOGFONT lfMenuFont, lfStatusFont, lfMessageFont;
            public int iPaddedBorderWidth;
        }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref int pvParam, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref ANIMATIONINFO pvParam, uint flags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref NONCLIENTMETRICS pvParam, uint flags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref LOGFONT pvParam, uint flags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint uiParam, string pvParam, uint flags);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint uiParam, StringBuilder pvParam, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam;
        }
        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        public const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_COLORIZATION_PARAMS
        {
            public uint clrColor, clrAfterGlow, nIntensity, clrAfterGlowBalance, clrBlurBalance, clrGlassReflectionIntensity;
            public int fOpaque;
        }
        // Undocumented but long-standing DWM exports used to update the window colour live.
        [DllImport("dwmapi.dll", EntryPoint = "#127")] public static extern int DwmGetColorizationParameters(out DWM_COLORIZATION_PARAMS p);
        [DllImport("dwmapi.dll", EntryPoint = "#131")] public static extern int DwmSetColorizationParameters(ref DWM_COLORIZATION_PARAMS p, uint unknown);

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        public static void Broadcast(string area)
        {
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, area, 0x0002, 3000, out _);
        }

        public static bool GetBool(uint getAction)
        {
            int v = 0;
            SystemParametersInfo(getAction, 0, ref v, 0);
            return v != 0;
        }

        public static int GetInt(uint getAction)
        {
            int v = 0;
            SystemParametersInfo(getAction, 0, ref v, 0);
            return v;
        }

        public static NONCLIENTMETRICS GetNcm()
        {
            var m = new NONCLIENTMETRICS { cbSize = Marshal.SizeOf(typeof(NONCLIENTMETRICS)) };
            SystemParametersInfo(SPI_GETNONCLIENTMETRICS, (uint)m.cbSize, ref m, 0);
            return m;
        }

        public static LOGFONT GetIconFont()
        {
            var f = new LOGFONT();
            SystemParametersInfo(SPI_GETICONTITLELOGFONT, (uint)Marshal.SizeOf(typeof(LOGFONT)), ref f, 0);
            return f;
        }

        public static byte[] ToBytes<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            var bytes = new byte[size];
            IntPtr p = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(value, p, false); Marshal.Copy(p, bytes, 0, size); }
            finally { Marshal.FreeHGlobal(p); }
            return bytes;
        }

        public static T FromBytes<T>(byte[] bytes) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr p = Marshal.AllocHGlobal(size);
            try { Marshal.Copy(bytes, 0, p, Math.Min(size, bytes.Length)); return (T)Marshal.PtrToStructure(p, typeof(T)); }
            finally { Marshal.FreeHGlobal(p); }
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace WinMorph.Shell
{
    /// <summary>Master volume of the default speakers via Windows Core Audio (no Windows pop-up).</summary>
    internal static class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        }

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int n);
            [PreserveSig] int GetAt(int i, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT v);
        }

        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
            [PreserveSig] int GetChannelCount(out uint n);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static IMMDevice Device()
        {
            var e = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            return e.GetDefaultAudioEndpoint(0, 1, out var d) == 0 ? d : null; // eRender, eMultimedia
        }

        static IAudioEndpointVolume Endpoint()
        {
            try
            {
                var d = Device();
                if (d == null) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                return d.Activate(ref iid, 23, IntPtr.Zero, out var o) == 0 ? (IAudioEndpointVolume)o : null;
            }
            catch { return null; }
        }

        public static bool Available => Endpoint() != null;

        /// <summary>0..100, or -1 when there is no output device.</summary>
        public static int Level
        {
            get { var ep = Endpoint(); if (ep == null) return -1; ep.GetMasterVolumeLevelScalar(out float v); return (int)Math.Round(v * 100); }
            set { var ep = Endpoint(); if (ep == null) return; var g = Guid.Empty; ep.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(100, value)) / 100f, ref g); if (value > 0) ep.SetMute(false, ref g); }
        }

        public static bool Muted
        {
            get { var ep = Endpoint(); if (ep == null) return false; ep.GetMute(out bool m); return m; }
            set { var ep = Endpoint(); if (ep == null) return; var g = Guid.Empty; ep.SetMute(value, ref g); }
        }

        public static void Change(int delta) { int l = Level; if (l >= 0) Level = l + delta; }

        public static string DeviceName
        {
            get
            {
                try
                {
                    var d = Device();
                    if (d == null || d.OpenPropertyStore(0, out var store) != 0) return "";
                    var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                    if (store.GetValue(ref key, out var v) != 0) return "";
                    var s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : "";
                    PropVariantClear(ref v);
                    return s ?? "";
                }
                catch { return ""; }
            }
        }
    }
}

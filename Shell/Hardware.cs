using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WinMorph.Shell
{
    /// <summary>The system controls the looks' own panels change directly: brightness, power plans, battery, radios.</summary>
    internal static class Hardware
    {
        // ---------------- brightness (laptop screens; -1 elsewhere) ----------------
        public static int Brightness()
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in s.Get()) using (o) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        static int pendingBrightness = -1;
        static bool settingBrightness;

        /// <summary>Sets the brightness in the background; while a change is running only the newest value is kept.</summary>
        public static void SetBrightness(int value)
        {
            pendingBrightness = Math.Max(0, Math.Min(100, value));
            if (settingBrightness) return;
            settingBrightness = true;
            Task.Run(() =>
            {
                try
                {
                    while (pendingBrightness >= 0)
                    {
                        int v = pendingBrightness; pendingBrightness = -1;
                        using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                            foreach (ManagementObject o in s.Get()) using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
                    }
                }
                catch { }
                finally { settingBrightness = false; }
            });
        }

        // ---------------- battery ----------------
        [StructLayout(LayoutKind.Sequential)] struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        public sealed class BatteryState { public int Percent; public bool Plugged, Charging, Saver; public TimeSpan? Left; }

        /// <summary>Null on a PC without a battery.</summary>
        public static BatteryState Battery()
        {
            if (!GetSystemPowerStatus(out var p) || p.BatteryFlag == 128 || p.BatteryFlag == 255 || p.BatteryLifePercent > 100) return null;
            return new BatteryState
            {
                Percent = p.BatteryLifePercent, Plugged = p.ACLineStatus == 1, Charging = (p.BatteryFlag & 8) != 0, Saver = p.SystemStatusFlag == 1,
                Left = p.BatteryLifeTime > 0 && p.ACLineStatus != 1 ? TimeSpan.FromSeconds(p.BatteryLifeTime) : (TimeSpan?)null,
            };
        }

        public static string Duration(TimeSpan t, bool de)
            => t.TotalHours >= 1 ? (int)t.TotalHours + (de ? " Std. " : " h ") + t.Minutes.ToString("00") + " Min." : t.Minutes + " Min.";

        // ---------------- power plans ----------------
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr sub, IntPtr set, uint flags, uint index, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

        public static List<(Guid id, string name)> PowerPlans()
        {
            var list = new List<(Guid, string)>();
            try
            {
                for (uint i = 0; i < 32; i++)
                {
                    var buf = new byte[16]; uint size = 16;
                    if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, buf, ref size) != 0) break;
                    var id = new Guid(buf);
                    uint n = 0;
                    PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref n);
                    var nb = new byte[Math.Max(2, n)];
                    PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, nb, ref n);
                    list.Add((id, System.Text.Encoding.Unicode.GetString(nb).TrimEnd('\0')));
                }
            }
            catch { }
            return list;
        }

        public static Guid ActivePlan
        {
            get
            {
                try
                {
                    if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return Guid.Empty;
                    var g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
                    LocalFree(p);
                    return g;
                }
                catch { return Guid.Empty; }
            }
            set { try { PowerSetActiveScheme(IntPtr.Zero, ref value); } catch { } }
        }

        // ---------------- radios (Wi-Fi, Bluetooth) ----------------
        public enum RadioKind { WiFi, Bluetooth }

        static async Task<global::Windows.Devices.Radios.Radio> Find(RadioKind kind)
        {
            var access = await global::Windows.Devices.Radios.Radio.RequestAccessAsync();
            if (access != global::Windows.Devices.Radios.RadioAccessStatus.Allowed) return null;
            var all = await global::Windows.Devices.Radios.Radio.GetRadiosAsync();
            var want = kind == RadioKind.WiFi ? global::Windows.Devices.Radios.RadioKind.WiFi : global::Windows.Devices.Radios.RadioKind.Bluetooth;
            return all.FirstOrDefault(r => r.Kind == want);
        }

        /// <summary>null = this PC has no such radio.</summary>
        public static async Task<bool?> RadioOn(RadioKind kind)
        {
            try { var r = await Find(kind); return r == null ? (bool?)null : r.State == global::Windows.Devices.Radios.RadioState.On; }
            catch { return null; }
        }

        public static async Task<bool> SetRadio(RadioKind kind, bool on)
        {
            try
            {
                var r = await Find(kind);
                if (r == null) return false;
                var res = await r.SetStateAsync(on ? global::Windows.Devices.Radios.RadioState.On : global::Windows.Devices.Radios.RadioState.Off);
                return res == global::Windows.Devices.Radios.RadioAccessStatus.Allowed;
            }
            catch { return false; }
        }
    }
}

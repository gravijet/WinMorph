using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace WinMorph.Shell
{
    /// <summary>Wi-Fi through the native WLAN API (wlanapi.dll): list, connect, disconnect. No netsh, no admin.</summary>
    internal static class Wlan
    {
        public sealed class Net
        {
            public string Ssid, Profile;
            public int Signal;            // 0..100
            public bool Connected, Secured;
            public int Auth;              // DOT11_AUTH_ALGORITHM
            public bool Personal => Auth == 1 || Auth == 4 || Auth == 7 || Auth == 9 || Auth == 10;   // open, WPA-PSK, WPA2-PSK, WPA3-SAE, OWE
        }

        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint ver, IntPtr res, out uint neg, out IntPtr h);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr h, IntPtr res);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr h, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanGetAvailableNetworkList(IntPtr h, ref Guid iface, uint flags, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr h, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr res);
        [DllImport("wlanapi.dll")] static extern int WlanDisconnect(IntPtr h, ref Guid iface, IntPtr res);
        [DllImport("wlanapi.dll")] static extern int WlanConnect(IntPtr h, ref Guid iface, ref CONNPARAMS p, IntPtr res);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanSetProfile(IntPtr h, ref Guid iface, uint flags, string xml, string security, bool overwrite, IntPtr res, out uint reason);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CONNPARAMS { public int mode; [MarshalAs(UnmanagedType.LPWStr)] public string profile; public IntPtr ssid, bssids; public int bssType; public uint flags; }

        const int IfaceSize = 532, NetSize = 628;

        /// <summary>Runs f with an open handle and the first Wi-Fi interface. False when there is no Wi-Fi.</summary>
        static bool With(Func<IntPtr, Guid, bool> f)
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return false;
            try
            {
                if (WlanEnumInterfaces(h, IntPtr.Zero, out var list) != 0) return false;
                try
                {
                    if (Marshal.ReadInt32(list) == 0) return false;
                    var g = (Guid)Marshal.PtrToStructure(list + 8, typeof(Guid));
                    return f(h, g);
                }
                finally { WlanFreeMemory(list); }
            }
            catch { return false; }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        public static bool Available => With((h, g) => true);

        /// <summary>Visible networks, strongest first, one entry per name (the one with a saved profile wins).</summary>
        public static List<Net> Networks()
        {
            var res = new List<Net>();
            With((h, g) =>
            {
                if (WlanGetAvailableNetworkList(h, ref g, 0, IntPtr.Zero, out var list) != 0) return false;
                try
                {
                    int n = Marshal.ReadInt32(list);
                    for (int i = 0; i < n; i++)
                    {
                        var p = list + 8 + i * NetSize;
                        int len = Math.Min(32, Marshal.ReadInt32(p + 512));
                        var raw = new byte[len];
                        Marshal.Copy(p + 516, raw, 0, len);
                        var ssid = Encoding.UTF8.GetString(raw);
                        if (ssid.Length == 0) continue;   // hidden network
                        uint flags = (uint)Marshal.ReadInt32(p + 620);
                        res.Add(new Net
                        {
                            Ssid = ssid,
                            Profile = (flags & 2) != 0 ? Marshal.PtrToStringUni(p) : null,
                            Signal = Marshal.ReadInt32(p + 604),
                            Secured = Marshal.ReadInt32(p + 608) != 0,
                            Auth = Marshal.ReadInt32(p + 612),
                            Connected = (flags & 1) != 0,
                        });
                    }
                }
                finally { WlanFreeMemory(list); }
                return true;
            });
            return res.GroupBy(x => x.Ssid)
                      .Select(gr => gr.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Profile != null).First())
                      .OrderByDescending(x => x.Connected).ThenByDescending(x => x.Signal).ToList();
        }

        public static Net Current => Networks().FirstOrDefault(x => x.Connected);

        public static void Scan() => With((h, g) => WlanScan(h, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0);
        public static bool Disconnect() => With((h, g) => WlanDisconnect(h, ref g, IntPtr.Zero) == 0);

        /// <summary>Connects with a saved profile. Returns null on success or an error text.</summary>
        public static string Connect(string profile)
        {
            int rc = -1;
            With((h, g) =>
            {
                var p = new CONNPARAMS { mode = 0, profile = profile, bssType = 1 };
                rc = WlanConnect(h, ref g, ref p, IntPtr.Zero);
                return true;
            });
            return rc == 0 ? null : "error " + rc;
        }

        /// <summary>Saves a new profile for a personal (or open) network and connects. The password never touches the disk.</summary>
        public static string ConnectNew(Net n, string password)
        {
            string auth, enc;
            switch (n.Auth)
            {
                case 1: auth = "open"; enc = "none"; break;
                case 10: auth = "OWE"; enc = "AES"; break;
                case 4: auth = "WPAPSK"; enc = "AES"; break;
                case 9: auth = "WPA3SAE"; enc = "AES"; break;
                default: auth = "WPA2PSK"; enc = "AES"; break;
            }
            if (n.Auth == 1 && n.Secured) return "WEP";   // outdated, not supported here
            string hex = BitConverter.ToString(Encoding.UTF8.GetBytes(n.Ssid)).Replace("-", "");
            string name = SecurityElement.Escape(n.Ssid);
            string key = auth == "open" || auth == "OWE" ? "" :
                "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>" + SecurityElement.Escape(password ?? "") + "</keyMaterial></sharedKey>";
            string xml = "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>" + name + "</name>" +
                "<SSIDConfig><SSID><hex>" + hex + "</hex><name>" + name + "</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode>" +
                "<MSM><security><authEncryption><authentication>" + auth + "</authentication><encryption>" + enc + "</encryption><useOneX>false</useOneX></authEncryption>" + key + "</security></MSM></WLANProfile>";
            int rc = -1; uint reason = 0;
            With((h, g) => { rc = WlanSetProfile(h, ref g, 0, xml, null, true, IntPtr.Zero, out reason); return true; });
            if (rc != 0) return "profile error " + rc + (reason != 0 ? " (" + reason + ")" : "");
            return Connect(n.Ssid);
        }
    }
}

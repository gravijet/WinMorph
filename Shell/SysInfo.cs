using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace WinMorph.Shell
{
    /// <summary>System facts for the hacker status line, panels, HUD and neofetch.</summary>
    internal static class SysInfo
    {
        static string os, cpu, gpu;

        public static string Os
        {
            get
            {
                if (os != null) return os;
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        var name = k?.GetValue("ProductName") as string ?? "Windows";
                        var build = k?.GetValue("CurrentBuild") as string ?? "";
                        if (int.TryParse(build, out int b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                        var disp = k?.GetValue("DisplayVersion") as string;
                        os = name + (disp != null ? " " + disp : "") + " (build " + build + ")";
                    }
                }
                catch { os = "Windows"; }
                return os;
            }
        }

        public static string Cpu
        {
            get
            {
                if (cpu != null) return cpu;
                try { cpu = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "") as string ?? "").Trim(); } catch { }
                cpu = System.Text.RegularExpressions.Regex.Replace(cpu ?? "", @"\s+", " ");
                if (cpu.Length == 0) cpu = "unknown";
                return cpu + " (" + Environment.ProcessorCount + ")";
            }
        }

        public static string Gpu
        {
            get
            {
                if (gpu != null) return gpu;
                var names = new List<string>();
                try
                {
                    using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                        if (cls != null)
                            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4))
                                using (var k = cls.OpenSubKey(sub))
                                {
                                    var d = k?.GetValue("DriverDesc") as string;
                                    if (!string.IsNullOrWhiteSpace(d) && !names.Contains(d) && !d.Contains("Basic Display") && !d.Contains("Remote")) names.Add(d);
                                }
                }
                catch { }
                return gpu = names.Count > 0 ? string.Join(", ", names) : "unknown";
            }
        }

        public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue);
        public static string UptimeText { get { var u = Uptime; return (u.Days > 0 ? u.Days + "d " : "") + u.Hours + "h " + u.Minutes.ToString("00") + "m"; } }

        public static string LocalIp
        {
            get
            {
                try
                {
                    foreach (var ni in Adapters())
                        foreach (var a in ni.GetIPProperties().UnicastAddresses)
                            if (a.Address.AddressFamily == AddressFamily.InterNetwork) return a.Address.ToString();
                }
                catch { }
                return "offline";
            }
        }

        public static IEnumerable<NetworkInterface> Adapters()
        {
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); } catch { yield break; }
            foreach (var ni in all)
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                    && ni.GetIPProperties().GatewayAddresses.Count > 0)
                    yield return ni;
        }

        // ---- network speed (bytes per second), measured between calls ----
        static long lastRx = -1, lastTx;
        static DateTime lastNet;
        static double rx, tx;

        public static void NetRate(out double down, out double up)
        {
            var now = DateTime.Now;
            if ((now - lastNet).TotalMilliseconds < 800) { down = rx; up = tx; return; }
            long r = 0, t = 0;
            try { foreach (var ni in Adapters()) { var s = ni.GetIPStatistics(); r += s.BytesReceived; t += s.BytesSent; } } catch { }
            double sec = Math.Max(0.1, (now - lastNet).TotalSeconds);
            if (lastRx >= 0) { rx = Math.Max(0, (r - lastRx) / sec); tx = Math.Max(0, (t - lastTx) / sec); }
            lastRx = r; lastTx = t; lastNet = now;
            down = rx; up = tx;
        }

        public static string Bytes(double b)
        {
            if (b < 1024) return b.ToString("0") + "B";
            if (b < 1024 * 1024) return (b / 1024).ToString("0") + "K";
            if (b < 1024.0 * 1024 * 1024) return (b / 1024 / 1024).ToString("0.0") + "M";
            return (b / 1024 / 1024 / 1024).ToString("0.0") + "G";
        }

        public static IEnumerable<DriveInfo> Disks()
        {
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); } catch { yield break; }
            foreach (var d in all)
            {
                bool ok = false;
                try { ok = d.IsReady && d.DriveType == DriveType.Fixed; } catch { }
                if (ok) yield return d;
            }
        }

        /// <summary>A text bar like ▮▮▮▮▯▯▯▯ for 0..100.</summary>
        public static string Bar(double pct, int cells, char on = '█', char off = '░')
        {
            int n = (int)Math.Round(Math.Max(0, Math.Min(100, pct)) / 100 * cells);
            return new string(on, n) + new string(off, cells - n);
        }

        static readonly char[] Spark = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };
        public static string Sparkline(IEnumerable<double> values)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in values) sb.Append(Spark[(int)Math.Max(0, Math.Min(7, Math.Round(v / 100 * 7)))]);
            return sb.ToString();
        }

        // ---------- GPU load and ping (gaming look): measured on a background thread, only while someone asks ----------
        static double gpuLoad = -1;
        static int ping = -1;
        static DateTime gameAsked;
        static int gameLoop;

        /// <summary>3D load of all graphics cards in percent (-1 = unknown).</summary>
        public static double GpuLoad { get { GameStats(); return gpuLoad; } }
        /// <summary>Round trip to 1.1.1.1 in milliseconds (-1 = offline).</summary>
        public static int PingMs { get { GameStats(); return ping; } }

        static void GameStats()
        {
            gameAsked = DateTime.Now;
            if (System.Threading.Interlocked.Exchange(ref gameLoop, 1) == 1) return;
            new System.Threading.Thread(() =>
            {
                var counters = new List<System.Diagnostics.PerformanceCounter>();
                var listed = DateTime.MinValue;
                using (var p = new Ping())
                    while ((DateTime.Now - gameAsked).TotalSeconds < 15)   // nobody looks any more: stop measuring
                    {
                        try
                        {
                            if ((DateTime.Now - listed).TotalSeconds > 20)   // games start and stop: list the engines again
                            {
                                foreach (var c in counters) c.Dispose();
                                counters.Clear();
                                foreach (var n in new System.Diagnostics.PerformanceCounterCategory("GPU Engine").GetInstanceNames())
                                    if (n.EndsWith("engtype_3D")) { var c = new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n, true); c.NextValue(); counters.Add(c); }
                                listed = DateTime.Now;
                            }
                            double sum = 0;
                            foreach (var c in counters) try { sum += c.NextValue(); } catch { }
                            gpuLoad = Math.Min(100, sum);
                        }
                        catch { gpuLoad = -1; }
                        try { var r = p.Send("1.1.1.1", 1000); ping = r.Status == IPStatus.Success ? (int)r.RoundtripTime : -1; } catch { ping = -1; }
                        System.Threading.Thread.Sleep(2000);
                    }
                foreach (var c in counters) c.Dispose();
                gameLoop = 0;
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
        }

        /// <summary>Last 60 CPU readings (one per second while something asks for them).</summary>
        public static readonly Queue<double> CpuHistory = new Queue<double>();
        static DateTime lastHist;
        public static double CpuSample()
        {
            double c = SysStats.Cpu();
            if ((DateTime.Now - lastHist).TotalMilliseconds > 900)
            {
                lastHist = DateTime.Now;
                CpuHistory.Enqueue(c);
                while (CpuHistory.Count > 60) CpuHistory.Dequeue();
            }
            return c;
        }
    }
}

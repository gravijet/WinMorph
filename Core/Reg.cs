using System;
using System.Text;
using Microsoft.Win32;

namespace WinMorph.Core
{
    /// <summary>Registry helpers that take full paths like "HKCU\Software\...".</summary>
    internal static class Reg
    {
        public static RegistryKey Root(string fullPath, out string sub)
        {
            int i = fullPath.IndexOf('\\');
            string hive = fullPath.Substring(0, i).ToUpperInvariant();
            sub = fullPath.Substring(i + 1);
            switch (hive)
            {
                case "HKCU": return Registry.CurrentUser;
                case "HKLM": return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                default: throw new ArgumentException("Unknown hive " + hive);
            }
        }

        public static RegistryKey Open(string fullPath, bool write = false)
        {
            var root = Root(fullPath, out var sub);
            return root.OpenSubKey(sub, write);
        }

        public static RegistryKey Create(string fullPath)
        {
            var root = Root(fullPath, out var sub);
            return root.CreateSubKey(sub, true);
        }

        public static bool KeyExists(string fullPath)
        {
            using (var k = Open(fullPath)) return k != null;
        }

        public static object Get(string fullPath, string name)
        {
            using (var k = Open(fullPath)) return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public static int? GetDword(string fullPath, string name)
        {
            var v = Get(fullPath, name);
            return v is int i ? i : (int?)null;
        }

        /// <summary>Serialises the current value as "kind\tbase64", or "ABSENT".</summary>
        public static string Snapshot(string fullPath, string name)
        {
            using (var k = Open(fullPath))
            {
                if (k == null) return "ABSENT";
                var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v == null) return "ABSENT";
                var kind = k.GetValueKind(name);
                byte[] data;
                switch (kind)
                {
                    case RegistryValueKind.DWord: data = BitConverter.GetBytes((int)v); break;
                    case RegistryValueKind.QWord: data = BitConverter.GetBytes((long)v); break;
                    case RegistryValueKind.Binary: data = (byte[])v; break;
                    case RegistryValueKind.MultiString: data = Encoding.UTF8.GetBytes(string.Join("\0", (string[])v)); break;
                    default: data = Encoding.UTF8.GetBytes(v.ToString()); break;
                }
                return kind + "\t" + Convert.ToBase64String(data);
            }
        }

        public static void RestoreSnapshot(string fullPath, string name, string snap)
        {
            if (snap == "ABSENT")
            {
                using (var k = Open(fullPath, true)) k?.DeleteValue(name, false);
                return;
            }
            var parts = snap.Split('\t');
            var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), parts[0]);
            var data = Convert.FromBase64String(parts.Length > 1 ? parts[1] : "");
            object value;
            switch (kind)
            {
                case RegistryValueKind.DWord: value = BitConverter.ToInt32(data, 0); break;
                case RegistryValueKind.QWord: value = BitConverter.ToInt64(data, 0); break;
                case RegistryValueKind.Binary: value = data; break;
                case RegistryValueKind.MultiString: value = Encoding.UTF8.GetString(data).Split('\0'); break;
                default: value = Encoding.UTF8.GetString(data); break;
            }
            using (var k = Create(fullPath)) k.SetValue(name, value, kind);
        }
    }
}

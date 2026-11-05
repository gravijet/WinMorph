using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WinMorph.Core
{
    /// <summary>
    /// Remembers the ORIGINAL value of every setting the first time WinMorph touches it.
    /// Later presets never overwrite an entry, so "Restore" always returns to the state
    /// before WinMorph was first used.
    /// </summary>
    public sealed class Backup
    {
        public static string DataDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMorph");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string DefaultFile => Path.Combine(DataDir, "backup.txt");

        public readonly string File;
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, string> map = new Dictionary<string, string>();
        public DateTime? Created { get; private set; }

        public Backup(string file)
        {
            File = file;
            if (!System.IO.File.Exists(file)) return;
            foreach (var line in System.IO.File.ReadAllLines(file, Encoding.UTF8))
            {
                if (line.StartsWith("#CREATED\t"))
                {
                    if (DateTime.TryParse(line.Substring(9), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) Created = dt;
                    continue;
                }
                int i = line.IndexOf('\u001F');
                if (i <= 0) continue;
                var key = line.Substring(0, i);
                if (map.ContainsKey(key)) continue;
                order.Add(key);
                map[key] = line.Substring(i + 1);
            }
        }

        public bool HasData => map.Count > 0;
        public int Count => map.Count;
        public bool HasMachineEntries => order.Any(k => k.StartsWith("REG\tHKLM") || k.StartsWith("KEY\tHKLM"));

        public bool Has(string key) => map.ContainsKey(key);

        /// <summary>
        /// A second backup that receives the same entries: the "undo step" for this one change,
        /// while this backup keeps the original state from before WinMorph was first used.
        /// </summary>
        public Backup Mirror;

        public static string StepsDir { get { var d = Path.Combine(DataDir, "steps"); Directory.CreateDirectory(d); return d; } }
        public static Backup NewStep() => new Backup(Path.Combine(StepsDir, "step-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt"));
        public static string LatestStep() => Directory.Exists(Path.Combine(DataDir, "steps")) ? Directory.GetFiles(StepsDir, "step-*.txt").OrderByDescending(x => x).FirstOrDefault() : null;
        public static int StepCount => Directory.Exists(Path.Combine(DataDir, "steps")) ? Directory.GetFiles(StepsDir, "step-*.txt").Length : 0;
        public static void ClearSteps() { try { foreach (var x in Directory.GetFiles(StepsDir, "step-*")) System.IO.File.Delete(x); } catch { } }

        /// <summary>Records a value only in this backup (not the mirror).</summary>
        public void ValueOnly(string name, string value) => PutLocal("VAL	" + name, value ?? "");

        void Put(string key, string value)
        {
            Mirror?.PutLocal(key, value);
            PutLocal(key, value);
        }

        void PutLocal(string key, string value)
        {
            if (map.ContainsKey(key)) return;
            if (Created == null) Created = DateTime.Now;
            order.Add(key);
            map[key] = value;
        }

        public void Reg(string path, string name) => Put("REG\t" + path + "\t" + name, Core.Reg.Snapshot(path, name));
        public void Key(string path) => Put("KEY\t" + path, Core.Reg.KeyExists(path) ? "1" : "0");
        public void Value(string name, string value) => Put("VAL\t" + name, value ?? "");

        public string GetValue(string name) => map.TryGetValue("VAL\t" + name, out var v) ? v : null;

        public IEnumerable<KeyValuePair<string, string>> Entries => order.Select(k => new KeyValuePair<string, string>(k, map[k]));

        public void Save()
        {
            var sb = new StringBuilder();
            sb.Append("#CREATED\t").AppendLine((Created ?? DateTime.Now).ToString("o"));
            foreach (var k in order) sb.Append(k).Append('\u001F').AppendLine(map[k]);
            System.IO.File.WriteAllText(File, sb.ToString(), Encoding.UTF8);
            Mirror?.Save();
        }

        /// <summary>Moves the file aside after a successful restore.</summary>
        public void Retire()
        {
            if (!System.IO.File.Exists(File)) return;
            var target = Path.Combine(Path.GetDirectoryName(File), "backup-restored-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            System.IO.File.Move(File, target);
        }
    }
}

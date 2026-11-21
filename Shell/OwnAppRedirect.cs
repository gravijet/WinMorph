using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace WinMorph.Shell
{
    /// <summary>
    /// Looks with their own apps replace Windows' Explorer and Task Manager however they are opened:
    /// from a desktop icon, another program, Ctrl+Alt+Del or a link. The Windows window is hidden the
    /// moment it appears and the look's own app opens at the same place. Windows that only Windows can
    /// show (Control Panel, Recycle Bin, network places) come back unchanged.
    /// </summary>
    internal static class OwnAppRedirect
    {
        static W32.WinEventProc proc;   // kept in a field so the hook keeps its delegate
        static IntPtr hook;
        static readonly HashSet<IntPtr> busy = new HashSet<IntPtr>();
        static readonly HashSet<IntPtr> leave = new HashSet<IntPtr>();   // windows that stay Windows' own
        static DateTime bypassUntil;

        const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", Home = "::{F874310E-B6B7-47DC-BC84-B9E6B38F5903}";

        public static void Start()
        {
            if (hook != IntPtr.Zero) return;
            proc = OnShow;
            hook = W32.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, proc, 0, 0, 0);   // EVENT_OBJECT_SHOW
        }

        public static void Stop()
        {
            if (hook != IntPtr.Zero) W32.UnhookWinEvent(hook);
            hook = IntPtr.Zero;
            busy.Clear(); leave.Clear();
        }

        /// <summary>The next few seconds an explicitly requested Windows window is left alone.</summary>
        public static void Bypass() => bypassUntil = DateTime.Now.AddSeconds(4);

        static void OnShow(IntPtr h, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero || ShellActions.Own == null || CustomLock.Active) return;
            if (DateTime.Now < bypassUntil || busy.Contains(hwnd) || leave.Contains(hwnd)) return;
            var cls = W32.ClassOf(hwnd);
            if (cls == "CabinetWClass") Explorer(hwnd);
            else if (cls == "TaskManagerWindow") TaskManager(hwnd);
        }

        static void Explorer(IntPtr hwnd)
        {
            busy.Add(hwnd);
            W32.ShowWindow(hwnd, W32.SW_HIDE);
            int tries = 0;
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            tm.Tick += (s, e) =>
            {
                tries++;
                var path = FolderOf(hwnd);
                if (path == null && tries < 30 && W32.IsWindow(hwnd)) return;   // the folder is not known yet
                tm.Stop();
                busy.Remove(hwnd);
                if (!W32.IsWindow(hwnd)) return;
                string target = path == null ? null
                    : path.Equals(ThisPc, StringComparison.OrdinalIgnoreCase) || path.Equals(Home, StringComparison.OrdinalIgnoreCase) ? ""
                    : Directory.Exists(path) ? path : null;
                if (target == null) { leave.Add(hwnd); W32.ShowWindow(hwnd, W32.SW_SHOW); W32.Activate(hwnd); return; }
                W32.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
                if (target.Length == 0) ShellActions.ThisPC(); else ShellActions.Files(target);
            };
            tm.Start();
        }

        static void TaskManager(IntPtr hwnd)
        {
            // an elevated Task Manager cannot be hidden by a normal process: then it stays as it is
            if (!W32.ShowWindow(hwnd, W32.SW_HIDE) && W32.IsWindowVisible(hwnd)) { leave.Add(hwnd); return; }
            W32.GetWindowThreadProcessId(hwnd, out uint pid);
            W32.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            tm.Tick += (s, e) =>
            {
                tm.Stop();
                try { using (var p = Process.GetProcessById((int)pid)) if (!p.HasExited && p.ProcessName.Equals("Taskmgr", StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
            };
            tm.Start();
            ShellActions.TaskManager();
        }

        /// <summary>The folder an Explorer window shows (a file system path or a shell id like ::{GUID}), or null.</summary>
        static string FolderOf(IntPtr hwnd)
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                var shell = Activator.CreateInstance(type);
                try
                {
                    var windows = Call(shell, "Windows");
                    int count = (int)Get(windows, "Count");
                    for (int i = 0; i < count; i++)
                    {
                        var w = Call(windows, "Item", i);
                        if (w == null) continue;
                        try
                        {
                            if (Convert.ToInt64(Get(w, "HWND")) != hwnd.ToInt64()) continue;
                            var doc = Get(w, "Document");
                            var folder = doc != null ? Get(doc, "Folder") : null;
                            var self = folder != null ? Get(folder, "Self") : null;
                            return self != null ? Get(self, "Path") as string : null;
                        }
                        catch { }
                    }
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
            }
            catch { }
            return null;
        }

        static object Get(object o, string name) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, o, null);
        static object Call(object o, string name, params object[] args) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, o, args);
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace WinMorph.Core
{
    /// <summary>
    /// Starts the design shell right at sign-in. A scheduled task with a logon trigger runs before the
    /// delayed "Run" entries of Explorer, at normal priority, is restarted by Windows if it ever fails, and
    /// is not affected when "Startup apps" are switched off. The Run entry stays as a second way in;
    /// the shell's mutex makes the later one quit at once.
    /// </summary>
    public static class Autostart
    {
        public const string TaskName = "WinMorph Shell";
        const string TreeKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\" + TaskName;

        public static bool Exists { get { try { return Reg.KeyExists(TreeKey); } catch { return false; } } }

        static string User => Environment.UserDomainName + "\\" + Environment.UserName;

        /// <summary>The task definition (also checked by the self-test).</summary>
        public static string Xml(string exe)
        {
            string u = SecurityElement.Escape(User), x = SecurityElement.Escape(exe);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
                + "<RegistrationInfo><Description>WinMorph: design shell (taskbar, Start menu, Alt+Tab) at sign-in</Description></RegistrationInfo>"
                + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + u + "</UserId></LogonTrigger></Triggers>"
                + "<Principals><Principal id=\"Author\"><UserId>" + u + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>"
                + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
                + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable>"
                + "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>"
                + "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>"
                + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority>"   // 4 = normal; the default (7) would start it slowly
                + "<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>"
                + "<Actions Context=\"Author\"><Exec><Command>\"" + x + "\"</Command><Arguments>--shell</Arguments></Exec></Actions>"
                + "</Task>";
        }

        /// <summary>Creates or updates the task for this user (no admin needed). False if Windows refused.</summary>
        public static bool Register(string exe)
        {
            var file = Path.Combine(Path.GetTempPath(), "winmorph-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(file, Xml(exe), Encoding.Unicode);
                return Run("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F") == 0;
            }
            catch { return false; }
            finally { try { File.Delete(file); } catch { } }
        }

        public static void Remove()
        {
            if (Exists) Run("/Delete /TN \"" + TaskName + "\" /F");
        }

        static int Run(string args)
        {
            using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                return p.WaitForExit(15000) ? p.ExitCode : -1;
            }
        }
    }
}

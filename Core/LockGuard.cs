using System;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace WinMorph.Core
{
    /// <summary>
    /// Lets WinMorph show its own lock screen on Win+L.
    /// Windows handles Win+L itself (a keyboard hook cannot stop it), so while the design shell
    /// runs, the per-user policy "DisableLockWorkstation" is set. WinMorph then catches Win+L,
    /// shows its lock screen and locks for real with <see cref="LockNow"/>, which lifts the policy
    /// for that moment. The policy is removed whenever the screen turns off, the PC goes to sleep
    /// or the shell stops, so Windows' own locking always works then.
    /// Normal users may not write the policy key, so it is unlocked once with an admin prompt
    /// (<see cref="SetupElevated"/>).
    /// </summary>
    public static class LockGuard
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        const string Val = "DisableLockWorkstation";
        const string Mark = "LockGuard";   // in HKCU\Software\WinMorph: 1 = the policy value is ours

        [DllImport("user32.dll", SetLastError = true)] static extern bool LockWorkStation();
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
        [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);

        static RegistryKey OpenRw() =>
            Registry.CurrentUser.OpenSubKey(Key, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.QueryValues | RegistryRights.SetValue);

        /// <summary>True when this user may change the policy value (after the one-time setup).</summary>
        public static bool Ready
        {
            get { try { using (var k = OpenRw()) return k != null; } catch { return false; } }
        }

        static bool Ours => Reg.Get(Shell.ShellControl.SettingsKey, Mark) is int i && i == 1;
        static void SetOurs(bool on)
        {
            try { using (var k = Reg.Create(Shell.ShellControl.SettingsKey)) { if (on) k.SetValue(Mark, 1, RegistryValueKind.DWord); else k.DeleteValue(Mark, false); } } catch { }
        }

        static int Current
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k?.GetValue(Val) is int i ? i : 0; } catch { return 0; } }
        }

        /// <summary>Win+L goes to WinMorph from now on.</summary>
        static volatile bool screenOff;

        public static bool Arm()
        {
            if (screenOff) return false;   // screen off / sleep: locking stays with Windows
            try
            {
                if (Current == 1) return Ours;          // already set (by us, or by an administrator – then leave it alone)
                using (var k = OpenRw())
                {
                    if (k == null) return false;
                    k.SetValue(Val, 1, RegistryValueKind.DWord);
                }
                SetOurs(true);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Windows handles Win+L itself again. Only removes the value WinMorph set.</summary>
        public static void Disarm()
        {
            if (!Ours) return;
            try { using (var k = OpenRw()) k?.DeleteValue(Val, false); } catch { }
            SetOurs(false);
        }

        public static bool Armed => Ours && Current == 1;

        /// <summary>True while the Windows lock / sign-in screen is shown.</summary>
        public static bool Locked()
        {
            try
            {
                if (WTSQuerySessionInformation(IntPtr.Zero, -1, 25 /* WTSSessionInfoEx */, out var buf, out int n) && buf != IntPtr.Zero)
                {
                    try
                    {
                        if (n >= 20 && Marshal.ReadInt32(buf) == 1)
                        {
                            int flags = Marshal.ReadInt32(buf, 16);   // WTSINFOEX_LEVEL1.SessionFlags: 0 = locked, 1 = unlocked
                            if (flags == 0) return true;
                            if (flags == 1) return false;
                        }
                    }
                    finally { WTSFreeMemory(buf); }
                }
            }
            catch { }
            var h = OpenInputDesktop(0, false, 0x0100 /* DESKTOP_SWITCHDESKTOP */);
            if (h == IntPtr.Zero) return true;
            CloseDesktop(h);
            return false;
        }

        /// <summary>
        /// Locks the PC with the real, secure Windows lock – also while Win+L is redirected.
        /// Waits (up to 4 s) until Windows has locked. Returns false if it did not lock.
        /// </summary>
        public static bool LockNow()
        {
            bool wasArmed = Armed;
            if (wasArmed) { try { using (var k = OpenRw()) k?.DeleteValue(Val, false); } catch { } }
            bool ok = false;
            for (int attempt = 0; attempt < 2 && !ok; attempt++)
            {
                LockWorkStation();
                for (int i = 0; i < 40; i++)
                {
                    if (Locked()) { ok = true; break; }
                    Thread.Sleep(100);
                }
            }
            // put the redirect back for the next Win+L (the lock itself is not affected by it)
            if (wasArmed && ok) { try { using (var k = OpenRw()) k?.SetValue(Val, 1, RegistryValueKind.DWord); } catch { } }
            else if (wasArmed) SetOurs(false);   // something is wrong: leave locking to Windows
            return ok;
        }

        /// <summary>Runs elevated (one admin prompt): lets the user change their own DisableLockWorkstation value.</summary>
        public static int SetupElevated(string sid)
        {
            try
            {
                var id = new SecurityIdentifier(sid);
                using (var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default))
                using (var k = users.CreateSubKey(sid + @"\" + Key, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    var sec = k.GetAccessControl();
                    sec.AddAccessRule(new RegistryAccessRule(id, RegistryRights.ReadKey | RegistryRights.SetValue, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
                    k.SetAccessControl(sec);
                }
                return 0;
            }
            catch { return 1; }
        }

        public static string UserSid => WindowsIdentity.GetCurrent().User?.Value;

        // ---------------- screen off / sleep: give locking back to Windows ----------------
        static System.Windows.Interop.HwndSource power;
        static Guid displayState = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");   // GUID_CONSOLE_DISPLAY_STATE
        [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);

        /// <summary>Call on the UI thread of the shell. Removes the redirect while the screen is off or the PC sleeps.</summary>
        /// <summary>The PC is about to sleep.</summary>
        public static event Action Suspending;

        public static void WatchPower(Func<bool> wanted)
        {
            if (power != null) return;
            try
            {
                var prm = new System.Windows.Interop.HwndSourceParameters("WinMorphPower") { ParentWindow = (IntPtr)(-3), Width = 0, Height = 0 };
                power = new System.Windows.Interop.HwndSource(prm);
                RegisterPowerSettingNotification(power.Handle, ref displayState, 0);
                power.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg != 0x0218) return IntPtr.Zero;                       // WM_POWERBROADCAST
                    int ev = w.ToInt32();
                    if (ev == 0x0004) { screenOff = true; Disarm(); try { Suspending?.Invoke(); } catch { } }   // PBT_APMSUSPEND
                    else if (ev == 0x0012 || ev == 0x0007) { screenOff = false; if (wanted()) Arm(); } // resume
                    else if (ev == 0x8013 && l != IntPtr.Zero)                   // PBT_POWERSETTINGCHANGE
                    {
                        var g = (Guid)Marshal.PtrToStructure(l, typeof(Guid));
                        if (g == displayState)
                        {
                            int state = Marshal.ReadInt32(l, 20);                // 0 off, 1 on, 2 dimmed
                            if (state == 0) { screenOff = true; Disarm(); }
                            else if (state == 1) { screenOff = false; if (wanted()) Arm(); }
                        }
                    }
                    return IntPtr.Zero;
                });
            }
            catch { }
        }
    }
}

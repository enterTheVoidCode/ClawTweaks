using System;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.Windows
{
    internal static class PowrProf
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        // Power Plan enumeration APIs
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerEnumerate(
            IntPtr RootPowerKey,
            IntPtr SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            uint AccessFlags,
            uint Index,
            IntPtr Buffer,
            ref uint BufferSize);

        [DllImport("powrprof.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint PowerReadFriendlyName(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid,
            IntPtr Buffer,
            ref uint BufferSize);

        // Access flags for PowerEnumerate
        public const uint ACCESS_SCHEME = 16;

        // Power Overlay Scheme APIs (Windows 10/11 power mode slider)
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerGetActualOverlayScheme(out Guid ActualOverlayGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerReadDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint DcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint DcValueIndex);

        /// <summary>
        /// Suspends or hibernates the system.
        /// </summary>
        /// <param name="bHibernate">If true, hibernates. If false, sleeps.</param>
        /// <param name="bForce">If true, forces the suspension even if apps refuse.</param>
        /// <param name="bWakeupEventsDisabled">If true, disables wake events.</param>
        /// <returns>True if successful, false otherwise.</returns>
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern bool SetSuspendState(bool bHibernate, bool bForce, bool bWakeupEventsDisabled);

        // ── Modern Standby (S0) entry via display-off ────────────────────────────────────────────
        // Microsoft's official position: there is NO API to make Windows enter Modern Standby — it
        // only starts from a user/kernel path (power button, lid, idle, Start-menu Sleep), and
        // SetSuspendState fails/hibernates on Modern-Standby systems. The documented community
        // workaround (PowerToys / Open-Shell) is to turn the display OFF, which on a Modern-Standby
        // handheld is exactly the power-button short-press trigger that lets the system drift into
        // S0 DRIPS. This is NOT a forced power-state transition, so it cannot crash on wake the way
        // NtInitiatePowerAction(S1) did.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        /// <summary>
        /// Turns the display off (lParam=2). On a Modern-Standby device this triggers entry into
        /// S0 low-power idle, the same as a power-button short-press. Safe: no forced power state.
        /// </summary>
        public static void TurnOffDisplayForModernStandby()
        {
            // lParam = 2 → "display is being turned off". SendMessageTimeout (not SendMessage) so an
            // unresponsive top-level window can't hang the helper.
            SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2,
                SMTO_ABORTIFHUNG, 1000, out _);
        }
    }
}

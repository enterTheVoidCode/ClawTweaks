using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Post-install helper orchestration. Since the setup is elevated it can query/run the helper's
    /// scheduled task and kick the Game Bar so the widget (re)deploys + starts the helper.
    ///
    /// We deliberately do NOT create the scheduled task or copy the helper exe from here: the helper
    /// deploys ITSELF via its own signed --setup (one UAC) on first widget open. That compiled, signed
    /// path is far less likely to trip Defender's persistence ML than a setup writing an exe + task
    /// (the exact reason Install.ps1 stopped doing script-driven persistence). So our job is: open
    /// Game Bar, then wait for the helper to come up.
    /// </summary>
    public static class HelperControl
    {
        private const string TaskName = @"ClawTweaks\ClawTweaksHelper";
        private const string HelperProcess = "XboxGamingBarHelper";

        public static int HelperCount() => Process.GetProcessesByName(HelperProcess).Length;
        public static bool HelperRunning() => HelperCount() > 0;

        /// <summary>PIDs of currently running helper processes — a stale instance from before an
        /// update lingers (Add-AppxPackage's -ForceApplicationShutdown doesn't reach it, it's a plain
        /// exe, not an app-lifecycle-managed process), so "any helper running" alone is a false
        /// positive for "the fresh post-install helper came up". Snapshot this before triggering a
        /// (re)install and only treat a PID outside the snapshot as the real signal.</summary>
        public static int[] GetHelperPids() => Process.GetProcessesByName(HelperProcess).Select(p => p.Id).ToArray();

        /// <summary>Best-effort: true while a UAC elevation prompt is up on the secure desktop (e.g.
        /// the helper's own first-run --setup elevation). Lets a caller tell the user to go confirm it.</summary>
        public static bool IsUacPromptShowing() => Process.GetProcessesByName("consent").Length > 0;

        /// <summary>
        /// True if the given process is actually running elevated (High/System integrity), checked
        /// via its token's TokenElevation — NOT just "a process with this name exists". A new
        /// XboxGamingBarHelper PID can appear before the widget's own elevation request is even shown,
        /// let alone confirmed (e.g. an initial unelevated instance that then requests --setup
        /// elevation itself), so "PID exists" alone is not proof the UAC was confirmed. This is the
        /// actual, verifiable signal instead of guessing from timing.
        /// </summary>
        public static bool IsProcessElevated(int pid)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            IntPtr tokenInfo = IntPtr.Zero;
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!OpenProcessToken(proc.Handle, TOKEN_QUERY, out tokenHandle)) return false;

                tokenInfo = Marshal.AllocHGlobal(sizeof(int));
                if (!GetTokenInformation(tokenHandle, TokenElevation, tokenInfo, sizeof(int), out _)) return false;

                return Marshal.ReadInt32(tokenInfo) != 0;
            }
            catch { return false; }
            finally
            {
                if (tokenInfo != IntPtr.Zero) Marshal.FreeHGlobal(tokenInfo);
                if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle);
            }
        }

        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20; // TOKEN_INFORMATION_CLASS.TokenElevation

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
            IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>True if the helper's scheduled task is registered.</summary>
        public static bool ScheduledTaskExists()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>Runs the helper's scheduled task if it exists. Returns true if launched.</summary>
        public static bool RunScheduledTask()
        {
            if (!ScheduledTaskExists()) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Run /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
                return p != null && p.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>Best-effort: open the Xbox Game Bar so the widget loads and deploys the helper.</summary>
        public static bool OpenGameBar()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-gamingoverlay://",
                    UseShellExecute = true,
                });
                return true;
            }
            catch { return false; }
        }

        /// <summary>Polls until the helper process appears or the timeout elapses.</summary>
        public static async Task<bool> WaitForHelperAsync(int timeoutMs, IProgress<int> percent = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (HelperRunning()) { percent?.Report(100); return true; }
                percent?.Report((int)Math.Min(99, sw.ElapsedMilliseconds * 100 / timeoutMs));
                await Task.Delay(500);
            }
            return HelperRunning();
        }

        /// <summary>Best-effort: closes the Game Bar overlay again by simulating Win+G a second time —
        /// there's no dedicated "close" URI, Win+G is a toggle. Used right after OpenGameBar() so the
        /// underlying window is visible again quickly instead of staying covered by the overlay.</summary>
        public static void CloseGameBarBestEffort()
        {
            try
            {
                const byte VK_LWIN = 0x5B, VK_G = 0x47;
                const uint KEYEVENTF_KEYUP = 0x0002;
                keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                keybd_event(VK_G, 0, 0, UIntPtr.Zero);
                keybd_event(VK_G, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}

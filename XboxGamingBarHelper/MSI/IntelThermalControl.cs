using System;
using System.Diagnostics;
using System.Management;
using System.ServiceProcess;
using System.Text;
using NLog;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Experimental on-device lever for the MSI Claw (Lunar Lake) fan "panic" latch.
    ///
    /// On LNL the Intel Innovation Platform Framework (IPF, the successor to DPTF/DTT) runs a
    /// second, independent fan controller ABOVE the MSI EC: the IPF "Fan Participant" device
    /// (ACPI\INTC106A\TFN1) under the IPF Manager (ACPI\INTC1068\IETM). Under sustained full load
    /// IPF can seize the fan and latch it at max — independent of our EC table and the AP.7 /
    /// 152.7 bits, which is why neither lowering our curve nor handing fan control back to the
    /// firmware releases it.
    ///
    /// This class lets a tester proactively stop the Intel thermal stack (services ipfsvc + dptftcs
    /// and the TFN1 fan participant) so the EC table becomes the sole fan controller, and start it
    /// again afterwards. It also reports whether the stack is currently running. This mirrors the
    /// standalone Fan-Panic-Stop/Start scripts but from inside the already-elevated helper.
    /// </summary>
    internal static class IntelThermalControl
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Intel thermal-framework services on Lunar Lake (renamed from the old esif_uf/DPTF stack).
        private static readonly string[] ServiceNames = { "ipfsvc", "dptftcs" };

        // IPF Fan Participant: the device IPF uses to command the fan directly, above the EC.
        private const string FanParticipantInstanceId = @"ACPI\INTC106A\TFN1";

        // Overall state tokens sent to the widget.
        private const string StateRunning = "running";   // Intel thermal stack active (normal)
        private const string StateStopped = "stopped";   // fully stopped (test mode: EC is sole fan owner)
        private const string StatePartial = "partial";   // some services up, some down
        private const string StateError   = "error";

        /// <summary>
        /// Builds the status payload "&lt;state&gt;|&lt;detail&gt;" where state is running/stopped/partial/error
        /// and detail is a short human-readable summary. Service state is the primary signal; the fan
        /// participant state is reported best-effort.
        /// </summary>
        public static string GetStatusPayload()
        {
            try
            {
                int running = 0, stopped = 0, missing = 0;
                var sb = new StringBuilder();

                foreach (string name in ServiceNames)
                {
                    string s = QueryServiceState(name);
                    if (s == "running") running++;
                    else if (s == "missing") missing++;
                    else stopped++;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(name).Append('=').Append(s);
                }

                bool? fanDisabled = IsFanParticipantDisabled();
                if (fanDisabled.HasValue)
                    sb.Append(", fan(TFN1)=").Append(fanDisabled.Value ? "disabled" : "enabled");

                string state;
                if (running == 0 && stopped + missing == ServiceNames.Length) state = StateStopped;
                else if (running == ServiceNames.Length) state = StateRunning;
                else state = StatePartial;

                return state + "|" + sb;
            }
            catch (Exception ex)
            {
                Logger.Warn($"IntelThermalControl.GetStatusPayload failed: {ex.Message}");
                return StateError + "|" + ex.Message;
            }
        }

        /// <summary>
        /// Stops the Intel thermal services and disables the IPF fan participant so the EC table is the
        /// sole fan controller. Returns the resulting status payload. Reversible via <see cref="Start"/>.
        /// </summary>
        public static string Stop()
        {
            Logger.Info("IntelThermalControl.Stop: stopping Intel thermal stack (test mode)");
            foreach (string name in ServiceNames)
                StopService(name);
            // Disable the fan participant last; the framework is already down, so it won't re-grab it.
            SetFanParticipant(false);
            string payload = GetStatusPayload();
            Logger.Info($"IntelThermalControl.Stop: result {payload}");
            return payload;
        }

        /// <summary>
        /// Re-enables the IPF fan participant and starts the Intel thermal services again, restoring the
        /// normal Intel thermal stack. Returns the resulting status payload.
        /// </summary>
        public static string Start()
        {
            Logger.Info("IntelThermalControl.Start: restoring Intel thermal stack");
            // Enable the participant first so the framework finds it on start.
            SetFanParticipant(true);
            foreach (string name in ServiceNames)
                StartService(name);
            string payload = GetStatusPayload();
            Logger.Info($"IntelThermalControl.Start: result {payload}");
            return payload;
        }

        // ── services ───────────────────────────────────────────────────────────────

        private static string QueryServiceState(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    return sc.Status == ServiceControllerStatus.Running ? "running" : "stopped";
                }
            }
            catch (InvalidOperationException)
            {
                return "missing"; // service not installed on this device
            }
            catch (Exception ex)
            {
                Logger.Debug($"QueryServiceState({name}): {ex.Message}");
                return "unknown";
            }
        }

        private static void StopService(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) { Logger.Info($"  {name}: already stopped"); return; }
                    if (sc.CanStop)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                        Logger.Info($"  {name}: stopped");
                    }
                    else Logger.Warn($"  {name}: cannot be stopped (CanStop=false)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"  {name}: stop failed - {ex.Message}");
            }
        }

        private static void StartService(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Running) { Logger.Info($"  {name}: already running"); return; }
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
                    Logger.Info($"  {name}: started");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"  {name}: start failed - {ex.Message}");
            }
        }

        // ── IPF fan participant (TFN1) via pnputil ───────────────────────────────────

        private static void SetFanParticipant(bool enable)
        {
            string verb = enable ? "/enable-device" : "/disable-device";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = $"{verb} \"{FanParticipantInstanceId}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { Logger.Warn("  TFN1: pnputil did not start"); return; }
                    string outp = p.StandardOutput.ReadToEnd();
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);
                    int code = p.HasExited ? p.ExitCode : -1;
                    Logger.Info($"  TFN1: pnputil {verb} exit={code} {outp.Trim()} {err.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"  TFN1: pnputil {verb} failed - {ex.Message}");
            }
        }

        /// <summary>True = disabled, false = enabled, null = unknown/not present. ConfigManagerErrorCode 22 = disabled.</summary>
        private static bool? IsFanParticipantDisabled()
        {
            try
            {
                string wql = "SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID = '" +
                             FanParticipantInstanceId.Replace("\\", "\\\\") + "'";
                using (var searcher = new ManagementObjectSearcher(wql))
                foreach (ManagementObject mo in searcher.Get())
                {
                    object code = mo["ConfigManagerErrorCode"];
                    if (code == null) return null;
                    return Convert.ToInt32(code) == 22; // CM_PROB_DISABLED
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"IsFanParticipantDisabled: {ex.Message}");
            }
            return null;
        }
    }
}

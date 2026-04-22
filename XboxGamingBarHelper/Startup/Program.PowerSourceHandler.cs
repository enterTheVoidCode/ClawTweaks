using NLog;
using Shared.Enums;
using System;
using System.Collections.Generic;
using XboxGamingBarHelper.Power;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Helper-side AC/DC transition handling. The widget is UWP and stops receiving
    /// PowerManager.PowerSourceChanged callbacks while Game Bar is dismissed, so any
    /// AC↔DC transition that happens between Game Bar sessions used to be dropped
    /// (issue #72). The helper mirrors the three widget settings that drive its power-plan
    /// auto-switch decision, subscribes to SystemManager.PowerSourceChanged, and does the
    /// same work the widget would have done — independent of widget lifecycle.
    /// </summary>
    internal partial class Program
    {
        private static class PowerSourceProfileState
        {
            public static bool AutoSwitchEnabled = false;
            public static Guid AcGuid = Guid.Empty;
            public static Guid DcGuid = Guid.Empty;
        }

        internal static void ApplyPowerSourceProfileConfig(string configJson)
        {
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(configJson);
                if (cfg == null) return;

                if (cfg.TryGetValue("AutoSwitchEnabled", out var autoEl) &&
                    (autoEl.ValueKind == System.Text.Json.JsonValueKind.True || autoEl.ValueKind == System.Text.Json.JsonValueKind.False))
                {
                    PowerSourceProfileState.AutoSwitchEnabled = autoEl.GetBoolean();
                }
                if (cfg.TryGetValue("AcGuid", out var acEl) && acEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    Guid.TryParse(acEl.GetString(), out var ac);
                    PowerSourceProfileState.AcGuid = ac;
                }
                if (cfg.TryGetValue("DcGuid", out var dcEl) && dcEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    Guid.TryParse(dcEl.GetString(), out var dc);
                    PowerSourceProfileState.DcGuid = dc;
                }

                Logger.Info($"Applied PowerSourceProfileConfig (autoSwitch={PowerSourceProfileState.AutoSwitchEnabled}, ac={PowerSourceProfileState.AcGuid}, dc={PowerSourceProfileState.DcGuid})");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyPowerSourceProfileConfig: {ex.Message}");
            }
        }

        private static void SystemManager_PowerSourceChanged(object sender, global::Windows.System.Power.PowerSupplyStatus newStatus)
        {
            // Adequate == plugged in and charging at expected rate. Anything else (NotPresent /
            // Inadequate) is treated as "on battery" by the widget, so mirror that here.
            bool isOnAC = newStatus == global::Windows.System.Power.PowerSupplyStatus.Adequate;

            // 1) Apply the user's selected AC/DC power plan, if auto-switch is on.
            if (PowerSourceProfileState.AutoSwitchEnabled)
            {
                Guid planToApply = isOnAC ? PowerSourceProfileState.AcGuid : PowerSourceProfileState.DcGuid;
                if (planToApply != Guid.Empty)
                {
                    try
                    {
                        bool ok = PowerManager.SetActivePowerPlan(planToApply);
                        Logger.Info($"Helper-side AC/DC handler: applied {(isOnAC ? "AC" : "DC")} power plan {planToApply} (success={ok})");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Helper-side AC/DC handler: SetActivePowerPlan threw: {ex.Message}");
                    }
                }
                else
                {
                    Logger.Debug($"Helper-side AC/DC handler: no {(isOnAC ? "AC" : "DC")} plan configured, skipping plan switch");
                }
            }

            // 2) Re-push current TDP to hardware. Mirrors what the widget's 5-second
            // SchedulePowerSourceTdpReapply eventually asks the helper to do — but here we
            // don't need the widget to be awake. Skipped when AutoTDP is driving TDP (it
            // overwrites every tick anyway) or when TDP value is unset.
            try
            {
                if (performanceManager != null && !performanceManager.IsAutoTDPActive)
                {
                    int currentTdp = performanceManager.TDP.Value;
                    if (currentTdp > 0)
                    {
                        Logger.Info($"Helper-side AC/DC handler: re-pushing current TDP {currentTdp}W to hardware");
                        performanceManager.SetTDP(currentTdp);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Helper-side AC/DC handler: reapply TDP threw: {ex.Message}");
            }
        }
    }
}

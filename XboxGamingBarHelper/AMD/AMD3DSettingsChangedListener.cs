using NLog;
using System;

namespace XboxGamingBarHelper.AMD
{
    internal class AMD3DSettingsChangedListener : IADLX3DSettingsChangedListener
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected AMDManager amdManager;

        // Cooldown to prevent reading stale values immediately after we set them
        // The AMD driver may fire the callback before the change is fully applied
        private DateTime lastAFMFChange = DateTime.MinValue;
        private DateTime lastRSRChange = DateTime.MinValue;
        private DateTime lastRISChange = DateTime.MinValue;
        private const int CHANGE_COOLDOWN_MS = 2000; // 2 second cooldown

        internal AMD3DSettingsChangedListener(AMDManager inAMDManager) : base()
        {
            amdManager = inAMDManager;
        }

        public void NotifyAFMFChanged()
        {
            lastAFMFChange = DateTime.Now;
            Logger.Debug("AFMF change notified - cooldown started");
        }

        public void NotifyRSRChanged()
        {
            lastRSRChange = DateTime.Now;
            Logger.Debug("RSR change notified - cooldown started");
        }

        public void NotifyRISChanged()
        {
            lastRISChange = DateTime.Now;
            Logger.Debug("RIS change notified - cooldown started");
        }

        public override bool On3DSettingsChanged(IADLX3DSettingsChangedEvent p3DSettingsChangedEvent)
        {
            //try
            //{
            //    var p3DSettingsChangedEvent2 = (IADLX3DSettingsChangedEvent2)p3DSettingsChangedEvent;
            //    Logger.Info($"AMD 3D settings changed event 2 {p3DSettingsChangedEvent2.IsAMDFluidMotionFramesChanged()}.");
            //}
            //catch (InvalidCastException)
            //{
            //    Logger.Info("AMD 3D settings changed event is not IADLX3DSettingsChangedEvent2.");
            //}

            //try
            //{
            //    var p3DSettingsChangedEvent1 = (IADLX3DSettingsChangedEvent1)p3DSettingsChangedEvent;
            //    Logger.Info($"AMD 3D settings changed event 1 {p3DSettingsChangedEvent1.IsAMDFluidMotionFramesChanged()}.");
            //}
            //catch (InvalidCastException)
            //{
            //    Logger.Info("AMD 3D settings changed event is not IADLX3DSettingsChangedEvent1.");
            //}

            if (p3DSettingsChangedEvent.IsAntiLagChanged())
            {
                var isEnabled = amdManager.AMDRadeonAntiLagSetting.IsEnabled();
                if (amdManager.AMDRadeonAntiLagEnabled != isEnabled)
                {
                    amdManager.AMDRadeonAntiLagEnabled.SetValue(isEnabled);
                }
            }

            if (p3DSettingsChangedEvent.IsChillChanged())
            {
                var isEnabled = amdManager.AMDRadeonChillSetting.IsEnabled();
                if (amdManager.AMDRadeonChillEnabled != isEnabled)
                {
                    amdManager.AMDRadeonChillEnabled.SetValue(isEnabled);
                }
            }

            if (p3DSettingsChangedEvent.IsRadeonSuperResolutionChanged())
            {
                // Skip if we recently made a change to avoid reading stale values
                if ((DateTime.Now - lastRSRChange).TotalMilliseconds < CHANGE_COOLDOWN_MS)
                {
                    Logger.Debug("Skipping RSR read from driver - still in cooldown period");
                }
                else
                {
                    var isEnabled = amdManager.AMDRadeonSuperResolutionSetting.IsEnabled();
                    if (amdManager.AMDRadeonSuperResolutionEnabled != isEnabled)
                    {
                        Logger.Info($"RSR state changed externally to {isEnabled}");
                        amdManager.AMDRadeonSuperResolutionEnabled.SetValue(isEnabled);
                    }
                }
            }

            if (p3DSettingsChangedEvent.IsBoostChanged())
            {
                var isEnabled = amdManager.AMDRadeonBoostSetting.IsEnabled();
                if (amdManager.AMDRadeonBoostEnabled != isEnabled)
                {
                    amdManager.AMDRadeonBoostEnabled.SetValue(isEnabled);
                }
            }

            if (p3DSettingsChangedEvent.IsImageSharpeningChanged())
            {
                // Skip if we recently made a change to avoid reading stale values
                if ((DateTime.Now - lastRISChange).TotalMilliseconds < CHANGE_COOLDOWN_MS)
                {
                    Logger.Debug("Skipping RIS read from driver - still in cooldown period");
                }
                else
                {
                    var isEnabled = amdManager.AMDImageSharpeningSetting.IsEnabled();
                    if (amdManager.AMDImageSharpeningEnabled != isEnabled)
                    {
                        Logger.Info($"RIS state changed externally to {isEnabled}");
                        amdManager.AMDImageSharpeningEnabled.SetValue(isEnabled);
                    }
                }
            }

            // AFMF is always checked (not gated by IsAMDFluidMotionFramesChanged)
            // Skip if we recently made a change to avoid reading stale values
            if ((DateTime.Now - lastAFMFChange).TotalMilliseconds < CHANGE_COOLDOWN_MS)
            {
                Logger.Debug("Skipping AFMF read from driver - still in cooldown period");
            }
            else
            {
                var isAMDFluidMotionFramesEnabled = amdManager.AMDFluidMotionFrameSetting.IsEnabled();
                if (amdManager.AMDFluidMotionFrameEnabled != isAMDFluidMotionFramesEnabled)
                {
                    Logger.Info($"AFMF state changed externally to {isAMDFluidMotionFramesEnabled}");
                    amdManager.AMDFluidMotionFrameEnabled.SetValue(isAMDFluidMotionFramesEnabled);
                }
            }

            return true;
        }
    }
}

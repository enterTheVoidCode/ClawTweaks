namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDFluidMotionFrameSetting : AMDSetting<IADLX3DAMDFluidMotionFrames>
    {
        public AMDFluidMotionFrameSetting(IADLX3DAMDFluidMotionFrames setting) : base(setting)
        {
        }

        public override bool IsSupported()
        {
            return AMDUtilities.GetBoolValue(adlxSetting.IsSupported);
        }

        public override bool IsEnabled()
        {
            return AMDUtilities.GetBoolValue(adlxSetting.IsEnabled);
        }

        public override void SetEnabled(bool enabled)
        {
            adlxSetting.SetEnabled(enabled);
        }
    }
}

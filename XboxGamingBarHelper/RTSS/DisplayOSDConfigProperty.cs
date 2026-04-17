using System;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.RTSS
{
    /// <summary>
    /// Property for receiving display/OSD configuration from the widget.
    /// Routes position shift settings to RTSSManager and adaptive brightness to SystemManager.
    /// </summary>
    internal class DisplayOSDConfigProperty : HelperProperty<string, RTSSManager>
    {
        private readonly Action<bool> setAdaptiveBrightness;

        public DisplayOSDConfigProperty(RTSSManager inManager, Action<bool> adaptiveBrightnessCallback)
            : base("", null, Function.OLEDConfig, inManager)
        {
            setAdaptiveBrightness = adaptiveBrightnessCallback;
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            var result = base.SetValue(newValue, updatedTime);
            if (result && manager != null && newValue is string configString)
            {
                manager.ParseDisplayOSDConfig(configString, setAdaptiveBrightness);
            }
            return result;
        }
    }
}

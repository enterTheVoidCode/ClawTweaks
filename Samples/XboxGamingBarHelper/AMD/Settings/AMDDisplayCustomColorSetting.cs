using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDDisplayCustomColorSetting : IDisposable
    {
        private readonly IADLXDisplayCustomColor adlxSetting;

        public AMDDisplayCustomColorSetting(IADLXDisplayCustomColor setting)
        {
            adlxSetting = setting;
        }

        // Brightness
        public bool IsBrightnessSupported() => AMDUtilities.GetBoolValue(adlxSetting.IsBrightnessSupported);
        public int GetBrightness() => AMDUtilities.GetIntValue(adlxSetting.GetBrightness);
        public void SetBrightness(int value) => adlxSetting.SetBrightness(value);

        // Contrast
        public bool IsContrastSupported() => AMDUtilities.GetBoolValue(adlxSetting.IsContrastSupported);
        public int GetContrast() => AMDUtilities.GetIntValue(adlxSetting.GetContrast);
        public void SetContrast(int value) => adlxSetting.SetContrast(value);

        // Saturation
        public bool IsSaturationSupported() => AMDUtilities.GetBoolValue(adlxSetting.IsSaturationSupported);
        public int GetSaturation() => AMDUtilities.GetIntValue(adlxSetting.GetSaturation);
        public void SetSaturation(int value) => adlxSetting.SetSaturation(value);

        // Temperature
        public bool IsTemperatureSupported() => AMDUtilities.GetBoolValue(adlxSetting.IsTemperatureSupported);
        public int GetTemperature() => AMDUtilities.GetIntValue(adlxSetting.GetTemperature);
        public void SetTemperature(int value) => adlxSetting.SetTemperature(value);

        ~AMDDisplayCustomColorSetting()
        {
            adlxSetting?.Dispose();
        }

        public virtual int Release()
        {
            if (adlxSetting == null) return 0;
            return adlxSetting.Release();
        }

        public void Dispose()
        {
            adlxSetting?.Dispose();
        }
    }
}

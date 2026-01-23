using NLog;
using System;

namespace XboxGamingBarHelper.AMD.Settings
{
    internal class AMDDisplayCustomColorSetting : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IADLXDisplayCustomColor adlxSetting;

        public AMDDisplayCustomColorSetting(IADLXDisplayCustomColor setting)
        {
            adlxSetting = setting;
        }

        // Brightness
        public bool IsBrightnessSupported()
        {
            if (adlxSetting == null) return false;
            return AMDUtilities.GetBoolValue(adlxSetting.IsBrightnessSupported);
        }
        public int GetBrightness()
        {
            if (adlxSetting == null) return 0;
            return AMDUtilities.GetIntValue(adlxSetting.GetBrightness);
        }
        public void SetBrightness(int value)
        {
            if (adlxSetting == null)
            {
                Logger.Warn("AMDDisplayCustomColorSetting.SetBrightness: adlxSetting is null");
                return;
            }
            adlxSetting.SetBrightness(value);
        }

        // Contrast
        public bool IsContrastSupported()
        {
            if (adlxSetting == null) return false;
            return AMDUtilities.GetBoolValue(adlxSetting.IsContrastSupported);
        }
        public int GetContrast()
        {
            if (adlxSetting == null) return 0;
            return AMDUtilities.GetIntValue(adlxSetting.GetContrast);
        }
        public void SetContrast(int value)
        {
            if (adlxSetting == null)
            {
                Logger.Warn("AMDDisplayCustomColorSetting.SetContrast: adlxSetting is null");
                return;
            }
            adlxSetting.SetContrast(value);
        }

        // Saturation
        public bool IsSaturationSupported()
        {
            if (adlxSetting == null) return false;
            return AMDUtilities.GetBoolValue(adlxSetting.IsSaturationSupported);
        }
        public int GetSaturation()
        {
            if (adlxSetting == null) return 0;
            return AMDUtilities.GetIntValue(adlxSetting.GetSaturation);
        }
        public void SetSaturation(int value)
        {
            if (adlxSetting == null)
            {
                Logger.Warn("AMDDisplayCustomColorSetting.SetSaturation: adlxSetting is null");
                return;
            }
            adlxSetting.SetSaturation(value);
        }

        // Temperature
        public bool IsTemperatureSupported()
        {
            if (adlxSetting == null) return false;
            return AMDUtilities.GetBoolValue(adlxSetting.IsTemperatureSupported);
        }
        public int GetTemperature()
        {
            if (adlxSetting == null) return 0;
            return AMDUtilities.GetIntValue(adlxSetting.GetTemperature);
        }
        public void SetTemperature(int value)
        {
            if (adlxSetting == null)
            {
                Logger.Warn("AMDDisplayCustomColorSetting.SetTemperature: adlxSetting is null");
                return;
            }
            adlxSetting.SetTemperature(value);
        }

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

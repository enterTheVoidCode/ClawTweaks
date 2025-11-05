using System;

namespace XboxGamingBarHelper.AMD
{
    internal abstract class AMDSetting<SettingType> : IDisposable where SettingType : IADLXInterface
    {
        public abstract bool IsSupported();
        public abstract bool IsEnabled();
        public abstract void SetEnabled(bool enabled);

        protected SettingType adlxSetting;
        public SettingType ADLXSetting { get {  return adlxSetting; } }

        public AMDSetting(SettingType setting)
        {
            adlxSetting = setting;
        }

        ~AMDSetting()
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

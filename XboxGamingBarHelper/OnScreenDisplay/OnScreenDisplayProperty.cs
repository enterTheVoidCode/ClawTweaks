using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.OnScreenDisplay
{
    internal class OnScreenDisplayProperty : HelperProperty<int, OnScreenDisplayManager>
    {
        public OnScreenDisplayProperty(int inValue, IProperty inParentProperty, OnScreenDisplayManager inManager) : base(LoadSavedLevel(inValue), inParentProperty, Function.OSD, inManager)
        {
            // Apply the loaded level to the manager
            manager?.SetLevel(Value);
            Logger.Info($"OSD level initialized to {Value} (loaded from settings)");
        }

        private static int LoadSavedLevel(int defaultValue)
        {
            try
            {
                int savedLevel = Properties.Settings.Default.OSDLevel;
                if (savedLevel >= 0 && savedLevel <= 4)
                {
                    Logger.Info($"Loaded OSD level {savedLevel} from settings");
                    return savedLevel;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load OSD level from settings: {ex.Message}");
            }
            return defaultValue;
        }

        private void SaveLevel(int level)
        {
            try
            {
                Properties.Settings.Default.OSDLevel = level;
                Properties.Settings.Default.Save();
                Logger.Debug($"Saved OSD level {level} to settings");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save OSD level to settings: {ex.Message}");
            }
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            manager?.SetLevel(Value);
            SaveLevel(Value);
        }

        public virtual void ChangeManager(OnScreenDisplayManager inManager)
        {
            if (inManager == null)
            {
                Logger.Warn($"Can't change On-Screen Display's manager to null.");
                return;
            }

            if (inManager == manager)
            {
                Logger.Warn($"On-Screen Display's manager is already the same instance.");
                return;
            }

            // Before changing manager, set the previous manager's level to 0 to clean the old OSD.
            manager.SetLevel(0);
            manager.IsInUsed = false;
            manager = inManager;
            manager.SetLevel(Value);
            manager.IsInUsed = true;
        }
    }
}

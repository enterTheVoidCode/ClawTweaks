using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{
    internal partial class LegionManager
    {
        private int touchpadVibrationLevel = 3;  // 1=Off, 2=Low, 3=Medium, 4=High

        /// <summary>
        /// Sets the touchpad vibration (haptic feedback) level.
        /// This is a GLOBAL setting, not per-game.
        /// </summary>
        /// <param name="level">1=Off, 2=Low, 3=Medium, 4=High</param>
        public void SetTouchpadVibration(int level)
        {
            try
            {
                if (level < 1 || level > 4)
                {
                    Logger.Warn($"Invalid touchpad vibration level: {level}");
                    return;
                }

                using var controller = new LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set touchpad vibration: controller not connected");
                    return;
                }

                var vibLevel = (TouchpadVibrationLevel)level;
                bool success = controller.SetTouchpadVibration(vibLevel);
                if (success)
                {
                    touchpadVibrationLevel = level;
                    string levelName = level switch
                    {
                        1 => "Off",
                        2 => "Low",
                        3 => "Medium",
                        4 => "High",
                        _ => "Unknown"
                    };
                    Logger.Info($"Touchpad vibration set to {levelName}");
                }
                else
                {
                    Logger.Error($"Failed to set touchpad vibration to level {level}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting touchpad vibration: {ex.Message}");
            }
        }

    }
}

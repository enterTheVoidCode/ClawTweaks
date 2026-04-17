using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void LegionHairTriggers_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            bool enabled = LegionHairTriggersToggle?.IsOn ?? false;

            if (enabled)
            {
                // Hair triggers: Start=0 (no dead zone), End=99 (full press at 1% travel)
                // HID command end% is offset from 100%, so end=99 means trigger fully pressed at 1% travel
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = 0;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = "0%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = 99;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = "99%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = 0;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = "0%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = 99;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = "99%";
                }
            }
            else
            {
                // Disable hair triggers: Reset to full travel (0% for all = full trigger press required)
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = 0;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = "0%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = 0;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = "0%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = 0;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = "0%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = 0;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = "0%";
                }
            }

            // Enable/disable sliders based on hair triggers state
            UpdateTriggerSlidersEnabled(!enabled);

            Logger.Info($"Hair Triggers toggled: {enabled}");

            // Save the profile
            ControllerSettingChanged(sender, e);
        }

        private void UpdateTriggerSlidersEnabled(bool enabled)
        {
            if (LegionLeftTriggerStartSlider != null)
                LegionLeftTriggerStartSlider.IsEnabled = enabled;
            if (LegionLeftTriggerEndSlider != null)
                LegionLeftTriggerEndSlider.IsEnabled = enabled;
            if (LegionRightTriggerStartSlider != null)
                LegionRightTriggerStartSlider.IsEnabled = enabled;
            if (LegionRightTriggerEndSlider != null)
                LegionRightTriggerEndSlider.IsEnabled = enabled;
        }

    }
}

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

        private void LoadProfileCustomizationSettings()
        {
            isLoadingProfileSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load values from settings
                _saveTDP = settings.Values.ContainsKey("ProfileSaveTDP") ? (bool)settings.Values["ProfileSaveTDP"] : true;
                _saveCPUBoost = settings.Values.ContainsKey("ProfileSaveCPUBoost") ? (bool)settings.Values["ProfileSaveCPUBoost"] : true;
                _saveCPUEPP = settings.Values.ContainsKey("ProfileSaveCPUEPP") ? (bool)settings.Values["ProfileSaveCPUEPP"] : true;
                _saveCPUState = settings.Values.ContainsKey("ProfileSaveCPUState") ? (bool)settings.Values["ProfileSaveCPUState"] : true;
                _saveAMDFeatures = settings.Values.ContainsKey("ProfileSaveAMDFeatures") ? (bool)settings.Values["ProfileSaveAMDFeatures"] : false;
                _saveFPSLimit = settings.Values.ContainsKey("ProfileSaveFPSLimit") ? (bool)settings.Values["ProfileSaveFPSLimit"] : true;
                _saveAutoTDP = settings.Values.ContainsKey("ProfileSaveAutoTDP") ? (bool)settings.Values["ProfileSaveAutoTDP"] : true;
                _saveOSPowerMode = settings.Values.ContainsKey("ProfileSaveOSPowerMode") ? (bool)settings.Values["ProfileSaveOSPowerMode"] : true;
                // HDR and Resolution - check for new separate settings first, fall back to combined setting for migration
                if (settings.Values.ContainsKey("ProfileSaveHDR"))
                {
                    _saveHDR = (bool)settings.Values["ProfileSaveHDR"];
                }
                else if (settings.Values.ContainsKey("ProfileSaveHDRResolution"))
                {
                    _saveHDR = (bool)settings.Values["ProfileSaveHDRResolution"]; // Migrate from combined setting
                }
                else
                {
                    _saveHDR = false;
                }

                if (settings.Values.ContainsKey("ProfileSaveResolution"))
                {
                    _saveResolution = (bool)settings.Values["ProfileSaveResolution"];
                }
                else if (settings.Values.ContainsKey("ProfileSaveHDRResolution"))
                {
                    _saveResolution = (bool)settings.Values["ProfileSaveHDRResolution"]; // Migrate from combined setting
                }
                else
                {
                    _saveResolution = false;
                }

                _saveRefreshRate = settings.Values.ContainsKey("ProfileSaveRefreshRate") ? (bool)settings.Values["ProfileSaveRefreshRate"] : false;
                _saveStickyTDP = settings.Values.ContainsKey("ProfileSaveStickyTDP") ? (bool)settings.Values["ProfileSaveStickyTDP"] : false;
                _saveOverlayLevel = settings.Values.ContainsKey("ProfileSaveOverlayLevel") ? (bool)settings.Values["ProfileSaveOverlayLevel"] : false;
                _saveCPUAffinity = settings.Values.ContainsKey("ProfileSaveCPUAffinity") ? (bool)settings.Values["ProfileSaveCPUAffinity"] : false;

                // Update UI checkboxes
                if (ProfileSaveTDPCheckBox != null) ProfileSaveTDPCheckBox.IsChecked = _saveTDP;
                if (ProfileSaveCPUBoostCheckBox != null) ProfileSaveCPUBoostCheckBox.IsChecked = _saveCPUBoost;
                if (ProfileSaveCPUEPPCheckBox != null) ProfileSaveCPUEPPCheckBox.IsChecked = _saveCPUEPP;
                if (ProfileSaveCPUStateCheckBox != null) ProfileSaveCPUStateCheckBox.IsChecked = _saveCPUState;
                if (ProfileSaveAMDFeaturesCheckBox != null) ProfileSaveAMDFeaturesCheckBox.IsChecked = _saveAMDFeatures;
                if (ProfileSaveFPSLimitCheckBox != null) ProfileSaveFPSLimitCheckBox.IsChecked = _saveFPSLimit;
                if (ProfileSaveAutoTDPCheckBox != null) ProfileSaveAutoTDPCheckBox.IsChecked = _saveAutoTDP;
                if (ProfileSaveOSPowerModeCheckBox != null) ProfileSaveOSPowerModeCheckBox.IsChecked = _saveOSPowerMode;
                if (ProfileSaveHDRCheckBox != null) ProfileSaveHDRCheckBox.IsChecked = _saveHDR;
                if (ProfileSaveResolutionCheckBox != null) ProfileSaveResolutionCheckBox.IsChecked = _saveResolution;
                if (ProfileSaveRefreshRateCheckBox != null) ProfileSaveRefreshRateCheckBox.IsChecked = _saveRefreshRate;
                if (ProfileSaveStickyTDPCheckBox != null) ProfileSaveStickyTDPCheckBox.IsChecked = _saveStickyTDP;
                if (ProfileSaveOverlayLevelCheckBox != null) ProfileSaveOverlayLevelCheckBox.IsChecked = _saveOverlayLevel;
                if (ProfileSaveCPUAffinityCheckBox != null) ProfileSaveCPUAffinityCheckBox.IsChecked = _saveCPUAffinity;
            }
            finally
            {
                isLoadingProfileSettings = false;
            }
        }

        private void SaveProfileCustomizationSettings()
        {
            if (isLoadingProfileSettings) return;

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ProfileSaveTDP"] = ProfileSaveTDPCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveCPUBoost"] = ProfileSaveCPUBoostCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveCPUEPP"] = ProfileSaveCPUEPPCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveCPUState"] = ProfileSaveCPUStateCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveAMDFeatures"] = ProfileSaveAMDFeaturesCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveFPSLimit"] = ProfileSaveFPSLimitCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveAutoTDP"] = ProfileSaveAutoTDPCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveOSPowerMode"] = ProfileSaveOSPowerModeCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveHDR"] = ProfileSaveHDRCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveResolution"] = ProfileSaveResolutionCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveRefreshRate"] = ProfileSaveRefreshRateCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveStickyTDP"] = ProfileSaveStickyTDPCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveOverlayLevel"] = ProfileSaveOverlayLevelCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveCPUAffinity"] = ProfileSaveCPUAffinityCheckBox?.IsChecked ?? false;
        }

        private void ProfileSettingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingProfileSettings) return;

            // Update backing fields from UI checkboxes
            SyncProfileSettingsBackingFields();
            SaveProfileCustomizationSettings();
            Logger.Info($"Profile customization settings updated");
        }

        private void ProfileSettingsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingProfileSettings) return;

            // Update backing fields from UI checkboxes
            SyncProfileSettingsBackingFields();
            SaveProfileCustomizationSettings();
            Logger.Info($"Profile customization settings updated");
        }

        /// <summary>
        /// Sync backing fields from UI checkboxes. Called when checkboxes change.
        /// This ensures the backing fields are always in sync with the UI.
        /// </summary>
        private void SyncProfileSettingsBackingFields()
        {
            _saveTDP = ProfileSaveTDPCheckBox?.IsChecked ?? true;
            _saveCPUBoost = ProfileSaveCPUBoostCheckBox?.IsChecked ?? true;
            _saveCPUEPP = ProfileSaveCPUEPPCheckBox?.IsChecked ?? true;
            _saveCPUState = ProfileSaveCPUStateCheckBox?.IsChecked ?? true;
            _saveAMDFeatures = ProfileSaveAMDFeaturesCheckBox?.IsChecked ?? false;
            _saveFPSLimit = ProfileSaveFPSLimitCheckBox?.IsChecked ?? true;
            _saveAutoTDP = ProfileSaveAutoTDPCheckBox?.IsChecked ?? true;
            _saveOSPowerMode = ProfileSaveOSPowerModeCheckBox?.IsChecked ?? true;
            _saveHDR = ProfileSaveHDRCheckBox?.IsChecked ?? false;
            _saveResolution = ProfileSaveResolutionCheckBox?.IsChecked ?? false;
            _saveRefreshRate = ProfileSaveRefreshRateCheckBox?.IsChecked ?? false;
            _saveStickyTDP = ProfileSaveStickyTDPCheckBox?.IsChecked ?? false;
            _saveOverlayLevel = ProfileSaveOverlayLevelCheckBox?.IsChecked ?? false;
            _saveCPUAffinity = ProfileSaveCPUAffinityCheckBox?.IsChecked ?? false;
        }

    }
}

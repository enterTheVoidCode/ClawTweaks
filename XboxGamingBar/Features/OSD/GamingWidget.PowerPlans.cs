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
        private async void LoadPowerPlans()
        {
            isLoadingPowerPlans = true;

            try
            {
                // Request power plans from helper
                if (App.IsConnected)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("GetPowerPlans", true);

                    var response = await App.SendMessageAsync(request);

                    if (response != null)
                    {
                        availablePowerPlans.Clear();

                        // Parse response: "GUID1|Name1;GUID2|Name2;..."
                        if (response.TryGetValue("PowerPlans", out object plansValue) && plansValue is string plansStr)
                        {
                            var planParts = plansStr.Split(';');
                            foreach (var part in planParts)
                            {
                                if (string.IsNullOrWhiteSpace(part)) continue;

                                var segments = part.Split('|');
                                if (segments.Length >= 2 && Guid.TryParse(segments[0], out Guid planGuid))
                                {
                                    availablePowerPlans.Add(new PowerPlanItem
                                    {
                                        Guid = planGuid,
                                        Name = segments[1]
                                    });
                                }
                            }
                        }

                        // Get currently active plan
                        if (response.TryGetValue("ActivePowerPlan", out object activeValue) && activeValue is string activeStr)
                        {
                            if (Guid.TryParse(activeStr, out Guid activeGuid))
                            {
                                // If no saved preferences, use current active plan as default
                                if (acPowerPlanGuid == Guid.Empty)
                                {
                                    acPowerPlanGuid = activeGuid;
                                }
                                if (dcPowerPlanGuid == Guid.Empty)
                                {
                                    dcPowerPlanGuid = activeGuid;
                                }
                            }
                        }

                        Logger.Info($"Received {availablePowerPlans.Count} power plans from helper");
                    }
                }

                // Fallback to well-known plans if helper didn't respond
                if (availablePowerPlans.Count == 0)
                {
                    Logger.Warn("No power plans received from helper, using defaults");
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
                        Name = "Balanced"
                    });
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
                        Name = "High Performance"
                    });
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
                        Name = "Power Saver"
                    });
                }

                // Populate ComboBoxes
                if (ACPowerPlanComboBox != null)
                {
                    ACPowerPlanComboBox.Items.Clear();
                    foreach (var plan in availablePowerPlans)
                    {
                        ACPowerPlanComboBox.Items.Add(new ComboBoxItem { Content = plan.Name, Tag = plan.Guid.ToString() });
                    }

                    // Select saved or default
                    SelectPowerPlanInComboBox(ACPowerPlanComboBox, acPowerPlanGuid);
                }

                if (DCPowerPlanComboBox != null)
                {
                    DCPowerPlanComboBox.Items.Clear();
                    foreach (var plan in availablePowerPlans)
                    {
                        DCPowerPlanComboBox.Items.Add(new ComboBoxItem { Content = plan.Name, Tag = plan.Guid.ToString() });
                    }

                    // Select saved or default
                    SelectPowerPlanInComboBox(DCPowerPlanComboBox, dcPowerPlanGuid);
                }

                // Update toggle state
                if (PowerPlanAutoSwitchToggle != null)
                {
                    PowerPlanAutoSwitchToggle.IsOn = powerPlanAutoSwitch;
                }

                Logger.Info($"Loaded {availablePowerPlans.Count} power plans");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plans: {ex.Message}");
            }
            finally
            {
                isLoadingPowerPlans = false;
            }
        }

        private void SelectPowerPlanInComboBox(ComboBox comboBox, Guid planGuid)
        {
            if (comboBox == null) return;

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is string guidStr)
                {
                    if (Guid.TryParse(guidStr, out Guid itemGuid) && itemGuid == planGuid)
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            // Default to first item (Balanced) if not found
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void ACPowerPlanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            if (ACPowerPlanComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string guidStr)
            {
                if (Guid.TryParse(guidStr, out Guid planGuid))
                {
                    acPowerPlanGuid = planGuid;
                    SavePowerPlanSettings();

                    // If currently on AC power, apply the plan immediately
                    if (powerPlanAutoSwitch && PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate)
                    {
                        ApplyPowerPlan(planGuid);
                    }

                    Logger.Info($"AC Power Plan set to: {selected.Content} ({planGuid})");
                }
            }
        }

        private void DCPowerPlanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            if (DCPowerPlanComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string guidStr)
            {
                if (Guid.TryParse(guidStr, out Guid planGuid))
                {
                    dcPowerPlanGuid = planGuid;
                    SavePowerPlanSettings();

                    // If currently on battery, apply the plan immediately
                    if (powerPlanAutoSwitch && PowerManager.PowerSupplyStatus != PowerSupplyStatus.Adequate)
                    {
                        ApplyPowerPlan(planGuid);
                    }

                    Logger.Info($"DC Power Plan set to: {selected.Content} ({planGuid})");
                }
            }
        }

        private void PowerPlanAutoSwitchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            powerPlanAutoSwitch = PowerPlanAutoSwitchToggle?.IsOn ?? false;
            SavePowerPlanSettings();

            Logger.Info($"Power Plan auto-switch set to: {powerPlanAutoSwitch}");
        }

    }
}

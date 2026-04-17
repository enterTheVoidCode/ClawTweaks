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
        private int _pawnIOCoAllValue = 0;
        private int _pawnIOCoGfxValue = 0;
        private int _pawnIOGfxClkValue = 800;
        private int _pawnIOTctlValue = 95;

        private void EnableDebugToolsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (PawnIODebugTools != null)
            {
                PawnIODebugTools.Visibility = EnableDebugToolsToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

                if (EnableDebugToolsToggle.IsOn)
                {
                    // Request CPU info from helper
                    _ = UpdatePawnIOCpuInfo();
                }
            }
        }

        private async Task UpdatePawnIOCpuInfo()
        {
            try
            {
                if (!App.IsConnected)
                {
                    PawnIOCpuInfoText.Text = "CPU: Helper not connected";
                    return;
                }

                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Get);
                message.Add("Function", (int)Shared.Enums.Function.PawnIOGetCpuInfo);
                var response = await App.SendMessageAsync(message);

                if (response != null && response.TryGetValue("Content", out object contentObj))
                {
                    PawnIOCpuInfoText.Text = $"CPU: {contentObj}";
                }
                else
                {
                    PawnIOCpuInfoText.Text = "CPU: PawnIO not available";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get PawnIO CPU info: {ex.Message}");
                PawnIOCpuInfoText.Text = "CPU: Error";
            }
        }

        private void PawnIOCoAllMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoAllValue = Math.Max(-30, _pawnIOCoAllValue - 1);
            PawnIOCoAllValue.Text = _pawnIOCoAllValue.ToString();
        }

        private void PawnIOCoAllPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoAllValue = Math.Min(30, _pawnIOCoAllValue + 1);
            PawnIOCoAllValue.Text = _pawnIOCoAllValue.ToString();
        }

        private void PawnIOCoGfxMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoGfxValue = Math.Max(-30, _pawnIOCoGfxValue - 1);
            PawnIOCoGfxValue.Text = _pawnIOCoGfxValue.ToString();
        }

        private void PawnIOCoGfxPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoGfxValue = Math.Min(30, _pawnIOCoGfxValue + 1);
            PawnIOCoGfxValue.Text = _pawnIOCoGfxValue.ToString();
        }

        private void PawnIOGfxClkMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOGfxClkValue = Math.Max(100, _pawnIOGfxClkValue - 50);
            PawnIOGfxClkValue.Text = _pawnIOGfxClkValue.ToString();
        }

        private void PawnIOGfxClkPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOGfxClkValue = Math.Min(3000, _pawnIOGfxClkValue + 50);
            PawnIOGfxClkValue.Text = _pawnIOGfxClkValue.ToString();
        }

        private void PawnIOTctlMinus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOTctlValue = Math.Max(60, _pawnIOTctlValue - 1);
            PawnIOTctlValue.Text = _pawnIOTctlValue.ToString();
        }

        private void PawnIOTctlPlus_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOTctlValue = Math.Min(105, _pawnIOTctlValue + 1);
            PawnIOTctlValue.Text = _pawnIOTctlValue.ToString();
        }

        private async void PawnIOApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PawnIOApplyButton.IsEnabled = false;
                PawnIODebugStatusText.Text = "Applying...";

                if (!App.IsConnected)
                {
                    PawnIODebugStatusText.Text = "Helper not connected";
                    PawnIOApplyButton.IsEnabled = true;
                    return;
                }

                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.PawnIOApplySettings);
                message.Add("CoAll", _pawnIOCoAllValue);
                message.Add("CoGfx", _pawnIOCoGfxValue);
                message.Add("GfxClk", _pawnIOGfxClkValue);
                message.Add("TctlTemp", _pawnIOTctlValue);

                var response = await App.SendMessageAsync(message);

                if (response != null && response.TryGetValue("Content", out object contentObj))
                {
                    PawnIODebugStatusText.Text = contentObj?.ToString() ?? "Applied";
                }
                else
                {
                    PawnIODebugStatusText.Text = "No response from helper";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"PawnIO apply failed: {ex.Message}");
                PawnIODebugStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                PawnIOApplyButton.IsEnabled = true;
            }
        }

        private void PawnIOResetButton_Click(object sender, RoutedEventArgs e)
        {
            _pawnIOCoAllValue = 0;
            _pawnIOCoGfxValue = 0;
            _pawnIOGfxClkValue = 800;
            _pawnIOTctlValue = 95;

            PawnIOCoAllValue.Text = "0";
            PawnIOCoGfxValue.Text = "0";
            PawnIOGfxClkValue.Text = "800";
            PawnIOTctlValue.Text = "95";
            PawnIODebugStatusText.Text = "Values reset (not applied)";
        }

    }
}

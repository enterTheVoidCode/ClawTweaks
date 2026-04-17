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

        private void RegisterChillFPSHandlers()
        {
            if (!chillFPSHandlersRegistered)
            {
                Logger.Info("Registering Chill FPS PropertyChanged handlers after sync...");
                amdRadeonChillMinFPSProperty.PropertyChanged += AmdRadeonChillFPSChanged;
                amdRadeonChillMaxFPSProperty.PropertyChanged += AmdRadeonChillFPSChanged;
                chillFPSHandlersRegistered = true;
                Logger.Info("Chill FPS handlers registered.");
            }
        }

        private void AmdRadeonChillFPSChanged(object sender, PropertyChangedEventArgs e)
        {
            // Only notify if both properties are initialized to avoid crash during sync
            // The binding will evaluate RadeonChillOnText which accesses both properties
            if (amdRadeonChillMinFPSProperty != null && amdRadeonChillMaxFPSProperty != null)
            {
                try
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadeonChillOnText)));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in AmdRadeonChillFPSChanged: {ex.Message}");
                }
            }
        }

    }
}

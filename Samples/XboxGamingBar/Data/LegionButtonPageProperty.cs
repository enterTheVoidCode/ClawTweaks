using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Page button remap (JSON ButtonMapping format).
    /// Default: Win+Tab (Task View)
    /// Not auto-bound to UI - call SendMapping() explicitly to send to helper.
    /// </summary>
    internal class LegionButtonPageProperty : WidgetControlProperty<string, ComboBox>
    {
        public LegionButtonPageProperty(ComboBox inUI, Page inOwner) : base("", Function.LegionButtonPage, inUI, inOwner)
        {
            // Don't auto-bind to ComboBox - we'll manually send via SendMapping()
        }

        /// <summary>
        /// Sends the button mapping JSON to the helper.
        /// </summary>
        public void SendMapping(string json)
        {
            if (!string.IsNullOrEmpty(json) && json != Value)
            {
                Logger.Info($"{Function} sending mapping: {json}");
                SetValue(json);
            }
        }
    }
}

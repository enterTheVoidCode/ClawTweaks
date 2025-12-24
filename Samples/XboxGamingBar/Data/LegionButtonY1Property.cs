using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Y1 button remap (JSON ButtonMapping format).
    /// Not auto-bound to UI - call SendMapping() explicitly to send to helper.
    /// </summary>
    internal class LegionButtonY1Property : WidgetControlProperty<string, ComboBox>
    {
        public LegionButtonY1Property(ComboBox inUI, Page inOwner) : base("", Function.LegionButtonY1, inUI, inOwner)
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

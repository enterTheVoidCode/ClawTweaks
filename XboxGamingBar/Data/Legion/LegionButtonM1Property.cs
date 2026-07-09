using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion M1 button remap (JSON ButtonMapping format).
    /// Not auto-bound to UI - call SendMapping() explicitly to send to helper.
    /// </summary>
    internal class LegionButtonM1Property : WidgetControlProperty<string, ComboBox>
    {
        public LegionButtonM1Property(ComboBox inUI, Page inOwner) : base("", Function.LegionButtonM1, inUI, inOwner)
        {
            // Don't auto-bind to ComboBox - we'll manually send via SendMapping()
        }

        /// <summary>
        /// Sends the button mapping JSON to the helper.
        /// Sends even for "Disabled" state to clear the button mapping.
        /// </summary>
        /// <param name="force">
        /// Bypass the unchanged-value skip. Needed when re-pushing after a helper restart
        /// (ResendActiveControllerProfileToHelper): the helper's ClawButtonMonitor loses its
        /// in-memory M1 mapping on restart, but the WIDGET's cached Value never changed, so
        /// the normal dedup check would silently skip the resend and leave M1 unmapped.
        /// </param>
        public void SendMapping(string json, bool force = false)
        {
            if (json == null) return;
            if (force)
            {
                Logger.Info($"{Function} force-sending mapping: {json}");
                ForceSetValue(json);
            }
            else if (json != Value)
            {
                Logger.Info($"{Function} sending mapping: {json}");
                SetValue(json);
            }
        }
    }
}

using System;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using XboxGamingBar.QuickSettings;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Optional double-click action for the Left MSI (CLAW) button. The helper detects two
        // quick presses (within the configured window) and fires this action; a single press fires
        // the normal Left-MSI action after the window elapses. Stored globally (LocalSettings) and
        // pushed to the helper, which owns the detection (works while the Game Bar is closed too).
        private const string LeftMsiDcEnabledKey = "LeftMsiDoubleClickEnabled";
        private const string LeftMsiDcDelayKey   = "LeftMsiDoubleClickDelayMs";
        private const string LeftMsiDcActionKey  = "LeftMsiDoubleClickTileAction";
        private const string LeftMsiDcParamKey   = "LeftMsiDoubleClickActionParam";

        private bool _leftMsiDcLoading;

        private void InitializeLeftMsiDoubleClick()
        {
            try
            {
                _leftMsiDcLoading = true;
                var s = ApplicationData.Current.LocalSettings.Values;

                bool enabled = s.TryGetValue(LeftMsiDcEnabledKey, out var en) && en is bool b && b;
                int delay = s.TryGetValue(LeftMsiDcDelayKey, out var dl) && dl is int di ? di : 300;
                int action = s.TryGetValue(LeftMsiDcActionKey, out var ac) && ac is int ai ? ai : 0;
                string param = s.TryGetValue(LeftMsiDcParamKey, out var pa) && pa is string ps ? ps : "";

                if (LeftMsiDoubleClickToggle != null) LeftMsiDoubleClickToggle.IsOn = enabled;
                if (LeftMsiDoubleClickPanel != null) LeftMsiDoubleClickPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (LeftMsiDoubleDelaySlider != null) LeftMsiDoubleDelaySlider.Value = delay;
                if (LeftMsiDoubleDelayValue != null) LeftMsiDoubleDelayValue.Text = $"{delay} ms";

                // Populate + select the double-click action.
                if (LeftMsiDoubleActionComboBox != null)
                {
                    FillActionComboBox(LeftMsiDoubleActionComboBox);
                    SelectDoubleActionInCombo((TileActionType)action, param);
                }
            }
            catch (Exception ex) { Logger.Debug($"InitializeLeftMsiDoubleClick: {ex.Message}"); }
            finally { _leftMsiDcLoading = false; }
        }

        private void SelectDoubleActionInCombo(TileActionType type, string param)
        {
            if (LeftMsiDoubleActionComboBox == null) return;
            for (int i = 0; i < LeftMsiDoubleActionComboBox.Items.Count; i++)
            {
                if (LeftMsiDoubleActionComboBox.Items[i] is ComboBoxItem ci &&
                    ci.Tag is ActionChoice choice &&
                    (int)choice.Type == (int)type &&
                    (string.IsNullOrEmpty(choice.Param) || string.Equals(choice.Param, param, StringComparison.OrdinalIgnoreCase)))
                {
                    LeftMsiDoubleActionComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private void LeftMsiDoubleClickToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_leftMsiDcLoading) return;
            bool on = LeftMsiDoubleClickToggle?.IsOn ?? false;
            ApplicationData.Current.LocalSettings.Values[LeftMsiDcEnabledKey] = on;
            if (LeftMsiDoubleClickPanel != null) LeftMsiDoubleClickPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SendLeftMsiDoubleClickToHelper();
        }

        private void LeftMsiDoubleAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_leftMsiDcLoading) return;
            if (LeftMsiDoubleActionComboBox?.SelectedItem is ComboBoxItem ci && ci.Tag is ActionChoice choice)
            {
                ApplicationData.Current.LocalSettings.Values[LeftMsiDcActionKey] = (int)choice.Type;
                ApplicationData.Current.LocalSettings.Values[LeftMsiDcParamKey] = choice.Param ?? "";
                SendLeftMsiDoubleClickToHelper();
            }
        }

        private void LeftMsiDoubleDelay_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (LeftMsiDoubleDelayValue != null) LeftMsiDoubleDelayValue.Text = $"{(int)e.NewValue} ms";
            if (_leftMsiDcLoading) return;
            ApplicationData.Current.LocalSettings.Values[LeftMsiDcDelayKey] = (int)e.NewValue;
            SendLeftMsiDoubleClickToHelper();
        }

        private async void SendLeftMsiDoubleClickToHelper()
        {
            try
            {
                if (!App.IsConnected) return;
                var s = ApplicationData.Current.LocalSettings.Values;
                bool enabled = s.TryGetValue(LeftMsiDcEnabledKey, out var en) && en is bool b && b;
                int delay = LeftMsiDoubleDelaySlider != null ? (int)LeftMsiDoubleDelaySlider.Value : 300;
                int action = s.TryGetValue(LeftMsiDcActionKey, out var ac) && ac is int ai ? ai : 0;
                string param = s.TryGetValue(LeftMsiDcParamKey, out var pa) && pa is string ps ? ps : "";
                string pj = (param ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                string json = $"{{\"enabled\":{(enabled ? "true" : "false")},\"delayMs\":{delay},\"action\":{action},\"param\":\"{pj}\"}}";
                var msg = new Windows.Foundation.Collections.ValueSet { { "LeftMsiDoubleClick", json } };
                await App.SendMessageAsync(msg);
                Logger.Info($"SendLeftMsiDoubleClickToHelper: {json}");
            }
            catch (Exception ex) { Logger.Warn($"SendLeftMsiDoubleClickToHelper: {ex.Message}"); }
        }

        /// <summary>Helper pushed a resolved CLAW-button click ("single"/"double") — flash the
        /// detection indicator so the user can fine-tune the window.</summary>
        internal void OnClawClickDetected(string kind)
        {
            if (LeftMsiDoubleDetectText == null) return;
            bool dbl = string.Equals(kind, "double", StringComparison.OrdinalIgnoreCase);
            LeftMsiDoubleDetectText.Text = dbl ? "✓ Double-click detected!" : "• Single click detected";
            LeftMsiDoubleDetectText.Foreground = new SolidColorBrush(dbl
                ? Color.FromArgb(255, 90, 230, 130)   // green
                : Color.FromArgb(255, 150, 184, 226)); // light blue
        }
    }
}

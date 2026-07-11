using System;
using System.Collections.Generic;
using System.Linq;
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
        //
        // The double-click supports the same two modes as the single click:
        //   • Action  (mode=1) — a TileActionType picked from the shared action dropdown.
        //   • Keyboard(mode=0) — a custom key combo; sent to the helper as the built-in
        //                        KeyboardShortcut action (id 1) whose param is a "+"-joined token
        //                        string the helper replays via its input injector.
        private const string LeftMsiDcEnabledKey = "LeftMsiDoubleClickEnabled";
        private const string LeftMsiDcDelayKey   = "LeftMsiDoubleClickDelayMs";
        private const string LeftMsiDcActionKey  = "LeftMsiDoubleClickTileAction";
        private const string LeftMsiDcParamKey   = "LeftMsiDoubleClickActionParam";
        private const string LeftMsiDcModeKey    = "LeftMsiDoubleClickMode";   // 0=Keyboard, 1=Action
        private const string LeftMsiDcKeysKey    = "LeftMsiDoubleClickKeys";   // CSV of HID key codes

        private const int LeftMsiDcDefaultDelay = 220;

        private bool _leftMsiDcLoading;
        private readonly List<int> _leftMsiDoubleKeys = new List<int>();

        private void InitializeLeftMsiDoubleClick()
        {
            try
            {
                _leftMsiDcLoading = true;
                var s = ApplicationData.Current.LocalSettings.Values;

                bool enabled = s.TryGetValue(LeftMsiDcEnabledKey, out var en) && en is bool b && b;
                int delay = s.TryGetValue(LeftMsiDcDelayKey, out var dl) && dl is int di ? di : LeftMsiDcDefaultDelay;
                int action = s.TryGetValue(LeftMsiDcActionKey, out var ac) && ac is int ai ? ai : 0;
                string param = s.TryGetValue(LeftMsiDcParamKey, out var pa) && pa is string ps ? ps : "";
                int mode = s.TryGetValue(LeftMsiDcModeKey, out var mo) && mo is int mi ? mi : 1; // default Action

                // Restore saved keyboard keys.
                _leftMsiDoubleKeys.Clear();
                if (s.TryGetValue(LeftMsiDcKeysKey, out var kv) && kv is string kcsv && !string.IsNullOrWhiteSpace(kcsv))
                {
                    foreach (var part in kcsv.Split(','))
                        if (int.TryParse(part.Trim(), out int code) && code > 0) _leftMsiDoubleKeys.Add(code);
                }

                if (LeftMsiDoubleClickToggle != null) LeftMsiDoubleClickToggle.IsOn = enabled;
                if (LeftMsiDoubleClickPanel != null) LeftMsiDoubleClickPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (LeftMsiDoubleDelaySlider != null) LeftMsiDoubleDelaySlider.Value = delay;
                if (LeftMsiDoubleDelayValue != null) LeftMsiDoubleDelayValue.Text = $"{delay} ms";

                // Mode selector (0=Keyboard, 1=Action) + the dependent controls' visibility.
                if (LeftMsiDoubleTypeComboBox != null) LeftMsiDoubleTypeComboBox.SelectedIndex = mode == 0 ? 0 : 1;
                ApplyDoubleModeVisibility(mode);

                // Populate + select the double-click action (Action mode).
                if (LeftMsiDoubleActionComboBox != null)
                {
                    FillActionComboBox(LeftMsiDoubleActionComboBox);
                    SelectDoubleActionInCombo((TileActionType)action, param);
                }

                RenderDoubleKeyTags();
            }
            catch (Exception ex) { Logger.Debug($"InitializeLeftMsiDoubleClick: {ex.Message}"); }
            finally { _leftMsiDcLoading = false; }
        }

        private void ApplyDoubleModeVisibility(int mode)
        {
            bool keyboard = mode == 0;
            if (LeftMsiDoubleActionComboBox != null)
                LeftMsiDoubleActionComboBox.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
            if (LeftMsiDoubleKeyboardPanel != null)
                LeftMsiDoubleKeyboardPanel.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
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

        private void LeftMsiDoubleType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_leftMsiDcLoading) return;
            int mode = (LeftMsiDoubleTypeComboBox?.SelectedIndex ?? 1) == 0 ? 0 : 1;
            ApplicationData.Current.LocalSettings.Values[LeftMsiDcModeKey] = mode;
            ApplyDoubleModeVisibility(mode);
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

        // Grouped key picker → value-based add for the left-MSI double-click keyboard shortcut.
        private void AddLeftMsiDoubleKey(int keyCode)
        {
            if (_leftMsiDcLoading) return;
            if (keyCode <= 0) return;
            if (_leftMsiDoubleKeys.Count >= 5) return; // max 5
            if (_leftMsiDoubleKeys.Contains(keyCode)) return;

            _leftMsiDoubleKeys.Add(keyCode);
            PersistDoubleKeys();
            RenderDoubleKeyTags();
            SendLeftMsiDoubleClickToHelper();
        }

        private void RemoveDoubleKey(int keyCode)
        {
            _leftMsiDoubleKeys.Remove(keyCode);
            PersistDoubleKeys();
            RenderDoubleKeyTags();
            if (!_leftMsiDcLoading) SendLeftMsiDoubleClickToHelper();
        }

        private void LeftMsiDoubleDelay_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (LeftMsiDoubleDelayValue != null) LeftMsiDoubleDelayValue.Text = $"{(int)e.NewValue} ms";
            if (_leftMsiDcLoading) return;
            ApplicationData.Current.LocalSettings.Values[LeftMsiDcDelayKey] = (int)e.NewValue;
            SendLeftMsiDoubleClickToHelper();
        }

        private void PersistDoubleKeys()
        {
            ApplicationData.Current.LocalSettings.Values[LeftMsiDcKeysKey] = string.Join(",", _leftMsiDoubleKeys);
        }

        private void RenderDoubleKeyTags()
        {
            if (LeftMsiDoubleKeyTags == null) return;
            LeftMsiDoubleKeyTags.Children.Clear();

            foreach (var key in _leftMsiDoubleKeys)
            {
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var keyText = new TextBlock
                {
                    Text = GetKeyDisplayName(key),
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };
                int keyCode = key;
                removeButton.Click += (s, e) => RemoveDoubleKey(keyCode);

                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                LeftMsiDoubleKeyTags.Children.Add(tagBorder);
            }
        }

        /// <summary>Convert a stored HID key list into a "+"-joined token string the helper's
        /// input injector understands (e.g. "LCtrl+LShift+Esc").</summary>
        private string BuildDoubleKeyboardToken()
        {
            return string.Join("+", _leftMsiDoubleKeys.Select(HidToInjectorToken).Where(t => !string.IsNullOrEmpty(t)));
        }

        private string HidToInjectorToken(int hid)
        {
            switch (hid)
            {
                case 0x80: return "VOLUME_UP";
                case 0x81: return "VOLUME_DOWN";
                case 0x7F: return "VOLUME_MUTE";
                default: return GetKeyDisplayName(hid); // display names map 1:1 to injector tokens
            }
        }

        private async void SendLeftMsiDoubleClickToHelper()
        {
            try
            {
                if (!App.IsConnected) return;
                var s = ApplicationData.Current.LocalSettings.Values;
                bool enabled = s.TryGetValue(LeftMsiDcEnabledKey, out var en) && en is bool b && b;
                int delay = LeftMsiDoubleDelaySlider != null ? (int)LeftMsiDoubleDelaySlider.Value : LeftMsiDcDefaultDelay;
                int mode = (LeftMsiDoubleTypeComboBox?.SelectedIndex ?? 1) == 0 ? 0 : 1;

                int action;
                string param;
                if (mode == 0)
                {
                    // Keyboard mode → built-in KeyboardShortcut action (id 1), param = token string.
                    action = (int)TileActionType.KeyboardShortcut;
                    param = BuildDoubleKeyboardToken();
                }
                else
                {
                    action = s.TryGetValue(LeftMsiDcActionKey, out var ac) && ac is int ai ? ai : 0;
                    param = s.TryGetValue(LeftMsiDcParamKey, out var pa) && pa is string ps ? ps : "";
                }

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

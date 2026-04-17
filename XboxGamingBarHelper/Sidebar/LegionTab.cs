using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XboxGamingBarHelper.Sidebar
{
    internal class LegionTab : SidebarTab
    {
        private static readonly string[] LightModeNames = { "Off", "Solid", "Pulse", "Dynamic", "Spiral" };
        private static readonly string[] VibrationLevelNames = { "Off", "Weak", "Medium", "Strong" };
        private static readonly string[] VibrationModeNames = { "FPS", "Racing", "AVG", "SPG", "RPG" };

        private readonly StackPanel _contentPanel;
        private readonly Border[] _focusableControls;

        // Controls — Fan
        private readonly Border _fanFullSpeedToggleBorder;
        private readonly TextBlock _fanFullSpeedToggleText;

        // Controls — Battery
        private readonly Border _chargeLimitToggleBorder;
        private readonly TextBlock _chargeLimitToggleText;

        // Controls — Touchpad
        private readonly Border _touchpadToggleBorder;
        private readonly TextBlock _touchpadToggleText;

        // Controls — Light
        private readonly Border _powerLightToggleBorder;
        private readonly TextBlock _powerLightToggleText;
        private readonly TextBlock _lightModeText;
        private readonly TextBlock _lightSpeedValueText;
        private readonly Slider _lightSpeedSlider;
        private readonly TextBlock _lightBrightnessValueText;
        private readonly Slider _lightBrightnessSlider;

        // Controls — Vibration
        private readonly TextBlock _vibrationLevelText;
        private readonly TextBlock _vibrationModeText;

        // State
        private bool _fanFullSpeedState;
        private bool _chargeLimitState;
        private bool _touchpadState;
        private bool _powerLightState;
        private int _lightModeIndex;
        private int _vibrationLevelIndex = 2; // Medium default
        private int _vibrationModeIndex;      // FPS default (0-based, value = index+1)
        private bool _suppressSliderEvent;

        // Events
        internal event Action<bool> OnFanFullSpeedChanged;
        internal event Action<bool> OnChargeLimitChanged;
        internal event Action<bool> OnTouchpadChanged;
        internal event Action<bool> OnPowerLightChanged;
        internal event Action<int> OnLightModeChanged;
        internal event Action<int> OnLightSpeedChanged;
        internal event Action<int> OnLightBrightnessChanged;
        internal event Action<int> OnVibrationLevelChanged;
        internal event Action<int> OnVibrationModeChanged;

        internal LegionTab()
        {
            _contentPanel = new StackPanel();

            // ── SECTION: Fan ──
            _contentPanel.Children.Add(CreateSectionHeader("Fan"));

            // [0] Fan Full Speed toggle
            var fanBorder = CreateControlCard(out var fanContent);
            fanContent.Children.Add(CreateToggleRow("Fan Full Speed", out _fanFullSpeedToggleBorder, out _fanFullSpeedToggleText));
            _contentPanel.Children.Add(fanBorder);

            // ── SECTION: Battery ──
            _contentPanel.Children.Add(CreateSectionHeader("Battery"));

            // [1] Charge Limit toggle
            var chargeBorder = CreateControlCard(out var chargeContent);
            chargeContent.Children.Add(CreateToggleRow("Charge Limit", out _chargeLimitToggleBorder, out _chargeLimitToggleText));
            _contentPanel.Children.Add(chargeBorder);

            // ── SECTION: Touchpad ──
            _contentPanel.Children.Add(CreateSectionHeader("Touchpad"));

            // [2] Touchpad toggle
            var touchpadBorder = CreateControlCard(out var touchpadContent);
            touchpadContent.Children.Add(CreateToggleRow("Touchpad", out _touchpadToggleBorder, out _touchpadToggleText));
            _contentPanel.Children.Add(touchpadBorder);

            // ── SECTION: Light ──
            _contentPanel.Children.Add(CreateSectionHeader("Light"));

            // [3] Power Light toggle
            var powerLightBorder = CreateControlCard(out var powerLightContent);
            powerLightContent.Children.Add(CreateToggleRow("Power Light", out _powerLightToggleBorder, out _powerLightToggleText));
            _contentPanel.Children.Add(powerLightBorder);

            // [4] Light Mode selector
            var lightModeBorder = CreateControlCard(out var lightModeContent);
            lightModeContent.Children.Add(CreateModeHeader("Light Mode", out _lightModeText, "Off"));
            _contentPanel.Children.Add(lightModeBorder);

            // [5] Light Speed slider
            var lightSpeedBorder = CreateControlCard(out var lightSpeedContent);
            lightSpeedContent.Children.Add(CreateSliderHeader("Light Speed", out _lightSpeedValueText, "50%"));
            _lightSpeedSlider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = 50,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _lightSpeedSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _lightSpeedValueText.Text = (int)e.NewValue + "%";
            };
            lightSpeedContent.Children.Add(_lightSpeedSlider);
            _contentPanel.Children.Add(lightSpeedBorder);

            // [6] Light Brightness slider
            var lightBrightBorder = CreateControlCard(out var lightBrightContent);
            lightBrightContent.Children.Add(CreateSliderHeader("Light Brightness", out _lightBrightnessValueText, "0%"));
            _lightBrightnessSlider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = 0,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _lightBrightnessSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _lightBrightnessValueText.Text = (int)e.NewValue + "%";
            };
            lightBrightContent.Children.Add(_lightBrightnessSlider);
            _contentPanel.Children.Add(lightBrightBorder);

            // ── SECTION: Vibration ──
            _contentPanel.Children.Add(CreateSectionHeader("Vibration"));

            // [7] Vibration Level selector
            var vibLevelBorder = CreateControlCard(out var vibLevelContent);
            vibLevelContent.Children.Add(CreateModeHeader("Vibration", out _vibrationLevelText, "Medium"));
            _contentPanel.Children.Add(vibLevelBorder);

            // [8] Vibration Mode selector
            var vibModeBorder = CreateControlCard(out var vibModeContent);
            vibModeContent.Children.Add(CreateModeHeader("Vibration Mode", out _vibrationModeText, "FPS"));
            _contentPanel.Children.Add(vibModeBorder);

            _focusableControls = new Border[]
            {
                fanBorder,          // 0  Fan Full Speed toggle
                chargeBorder,       // 1  Charge Limit toggle
                touchpadBorder,     // 2  Touchpad toggle
                powerLightBorder,   // 3  Power Light toggle
                lightModeBorder,    // 4  Light Mode selector
                lightSpeedBorder,   // 5  Light Speed slider
                lightBrightBorder,  // 6  Light Brightness slider
                vibLevelBorder,     // 7  Vibration Level selector
                vibModeBorder,      // 8  Vibration Mode selector
            };
        }

        internal override StackPanel ContentPanel => _contentPanel;
        internal override Border[] FocusableControls => _focusableControls;

        internal override void AdjustLeft(int focusIndex)
        {
            switch (focusIndex)
            {
                case 4:
                    if (_lightModeIndex > 0)
                    {
                        _lightModeIndex--;
                        _lightModeText.Text = LightModeNames[_lightModeIndex];
                    }
                    break;
                case 5: if (_lightSpeedSlider.Value > _lightSpeedSlider.Minimum) _lightSpeedSlider.Value--; break;
                case 6: if (_lightBrightnessSlider.Value > _lightBrightnessSlider.Minimum) _lightBrightnessSlider.Value--; break;
                case 7:
                    if (_vibrationLevelIndex > 0)
                    {
                        _vibrationLevelIndex--;
                        _vibrationLevelText.Text = VibrationLevelNames[_vibrationLevelIndex];
                    }
                    break;
                case 8:
                    if (_vibrationModeIndex > 0)
                    {
                        _vibrationModeIndex--;
                        _vibrationModeText.Text = VibrationModeNames[_vibrationModeIndex];
                    }
                    break;
            }
        }

        internal override void AdjustRight(int focusIndex)
        {
            switch (focusIndex)
            {
                case 4:
                    if (_lightModeIndex < LightModeNames.Length - 1)
                    {
                        _lightModeIndex++;
                        _lightModeText.Text = LightModeNames[_lightModeIndex];
                    }
                    break;
                case 5: if (_lightSpeedSlider.Value < _lightSpeedSlider.Maximum) _lightSpeedSlider.Value++; break;
                case 6: if (_lightBrightnessSlider.Value < _lightBrightnessSlider.Maximum) _lightBrightnessSlider.Value++; break;
                case 7:
                    if (_vibrationLevelIndex < VibrationLevelNames.Length - 1)
                    {
                        _vibrationLevelIndex++;
                        _vibrationLevelText.Text = VibrationLevelNames[_vibrationLevelIndex];
                    }
                    break;
                case 8:
                    if (_vibrationModeIndex < VibrationModeNames.Length - 1)
                    {
                        _vibrationModeIndex++;
                        _vibrationModeText.Text = VibrationModeNames[_vibrationModeIndex];
                    }
                    break;
            }
        }

        internal override void Activate(int focusIndex, ref bool isAdjusting)
        {
            switch (focusIndex)
            {
                // Toggles: flip immediately
                case 0:
                    _fanFullSpeedState = !_fanFullSpeedState;
                    UpdateToggleVisual(_fanFullSpeedToggleBorder, _fanFullSpeedToggleText, _fanFullSpeedState);
                    OnFanFullSpeedChanged?.Invoke(_fanFullSpeedState);
                    break;
                case 1:
                    _chargeLimitState = !_chargeLimitState;
                    UpdateToggleVisual(_chargeLimitToggleBorder, _chargeLimitToggleText, _chargeLimitState);
                    OnChargeLimitChanged?.Invoke(_chargeLimitState);
                    break;
                case 2:
                    _touchpadState = !_touchpadState;
                    UpdateToggleVisual(_touchpadToggleBorder, _touchpadToggleText, _touchpadState);
                    OnTouchpadChanged?.Invoke(_touchpadState);
                    break;
                case 3:
                    _powerLightState = !_powerLightState;
                    UpdateToggleVisual(_powerLightToggleBorder, _powerLightToggleText, _powerLightState);
                    OnPowerLightChanged?.Invoke(_powerLightState);
                    break;

                // Mode selectors + sliders: toggle adjust mode
                case 4: case 5: case 6: case 7: case 8:
                    if (isAdjusting)
                    {
                        isAdjusting = false;
                        switch (focusIndex)
                        {
                            case 4: OnLightModeChanged?.Invoke(_lightModeIndex); break;
                            case 5: OnLightSpeedChanged?.Invoke((int)_lightSpeedSlider.Value); break;
                            case 6: OnLightBrightnessChanged?.Invoke((int)_lightBrightnessSlider.Value); break;
                            case 7: OnVibrationLevelChanged?.Invoke(_vibrationLevelIndex); break;
                            case 8: OnVibrationModeChanged?.Invoke(_vibrationModeIndex + 1); break;
                        }
                    }
                    else
                    {
                        isAdjusting = true;
                    }
                    break;
            }
        }

        internal override void Refresh() { }

        internal override ControlType GetControlType(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0: case 1: case 2: case 3: return ControlType.Toggle;
                case 4: case 7: case 8: return ControlType.ModeSelector;
                case 5: case 6: return ControlType.Slider;
                default: return ControlType.Tile;
            }
        }

        internal override Slider GetSlider(int focusIndex)
        {
            switch (focusIndex)
            {
                case 5: return _lightSpeedSlider;
                case 6: return _lightBrightnessSlider;
                default: return null;
            }
        }

        internal override void CommitSliderValue(int focusIndex)
        {
            switch (focusIndex)
            {
                case 5: OnLightSpeedChanged?.Invoke((int)_lightSpeedSlider.Value); break;
                case 6: OnLightBrightnessChanged?.Invoke((int)_lightBrightnessSlider.Value); break;
            }
        }

        internal override void PointerCycleForward(int focusIndex)
        {
            switch (focusIndex)
            {
                case 4:
                    _lightModeIndex = (_lightModeIndex + 1) % LightModeNames.Length;
                    _lightModeText.Text = LightModeNames[_lightModeIndex];
                    OnLightModeChanged?.Invoke(_lightModeIndex);
                    break;
                case 7:
                    _vibrationLevelIndex = (_vibrationLevelIndex + 1) % VibrationLevelNames.Length;
                    _vibrationLevelText.Text = VibrationLevelNames[_vibrationLevelIndex];
                    OnVibrationLevelChanged?.Invoke(_vibrationLevelIndex);
                    break;
                case 8:
                    _vibrationModeIndex = (_vibrationModeIndex + 1) % VibrationModeNames.Length;
                    _vibrationModeText.Text = VibrationModeNames[_vibrationModeIndex];
                    OnVibrationModeChanged?.Invoke(_vibrationModeIndex + 1);
                    break;
            }
        }

        #region External Updates

        internal void UpdateFanFullSpeed(bool enabled)
        {
            _fanFullSpeedState = enabled;
            UpdateToggleVisual(_fanFullSpeedToggleBorder, _fanFullSpeedToggleText, enabled);
        }

        internal void UpdateChargeLimit(bool enabled)
        {
            _chargeLimitState = enabled;
            UpdateToggleVisual(_chargeLimitToggleBorder, _chargeLimitToggleText, enabled);
        }

        internal void UpdateTouchpad(bool enabled)
        {
            _touchpadState = enabled;
            UpdateToggleVisual(_touchpadToggleBorder, _touchpadToggleText, enabled);
        }

        internal void UpdatePowerLight(bool enabled)
        {
            _powerLightState = enabled;
            UpdateToggleVisual(_powerLightToggleBorder, _powerLightToggleText, enabled);
        }

        internal void UpdateLightMode(int index)
        {
            _lightModeIndex = Math.Max(0, Math.Min(LightModeNames.Length - 1, index));
            _lightModeText.Text = LightModeNames[_lightModeIndex];
        }

        internal void UpdateLightSpeed(int value)
        {
            _suppressSliderEvent = true;
            _lightSpeedSlider.Value = Math.Max(0, Math.Min(100, value));
            _suppressSliderEvent = false;
            _lightSpeedValueText.Text = value + "%";
        }

        internal void UpdateLightBrightness(int value)
        {
            _suppressSliderEvent = true;
            _lightBrightnessSlider.Value = Math.Max(0, Math.Min(100, value));
            _suppressSliderEvent = false;
            _lightBrightnessValueText.Text = value + "%";
        }

        internal void UpdateVibrationLevel(int value)
        {
            _vibrationLevelIndex = Math.Max(0, Math.Min(VibrationLevelNames.Length - 1, value));
            _vibrationLevelText.Text = VibrationLevelNames[_vibrationLevelIndex];
        }

        internal void UpdateVibrationMode(int value)
        {
            // value is 1-based (1=FPS..5=RPG), convert to 0-based index
            _vibrationModeIndex = Math.Max(0, Math.Min(VibrationModeNames.Length - 1, value - 1));
            _vibrationModeText.Text = VibrationModeNames[_vibrationModeIndex];
        }

        #endregion
    }
}

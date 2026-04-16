using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XboxGamingBarHelper.Sidebar.Audio;

namespace XboxGamingBarHelper.Sidebar
{
    internal class QuickTab : SidebarTab
    {
        // Performance mode value mapping: display index → Legion value
        private static readonly int[] PerfModeToLegion = { 1, 2, 3, 255 };

        private static readonly string[] PerfModeNames = { "Quiet", "Balanced", "Performance", "Custom" };
        private static readonly string[] OverlayNames = { "Off", "Basic", "Detailed", "Full" };

        private static readonly Color OrangeColor = (Color)ColorConverter.ConvertFromString("#FF9800");
        private static readonly Color RedColor = (Color)ColorConverter.ConvertFromString("#F44336");

        private readonly StackPanel _contentPanel;
        private readonly Border[] _focusableControls;

        // Tile controls (row 0-1 existing, row 2-3 new)
        private readonly Border _volumeTile;
        private readonly TextBlock _volumeTileState;
        private readonly Border _brightnessTile;
        private readonly TextBlock _brightnessTileState;
        private readonly Border _tdpModeTile;
        private readonly TextBlock _tdpModeTileState;
        private readonly Border _ctrlEmuTile;
        private readonly TextBlock _ctrlEmuTileState;
        private readonly Border _cpuBoostTile;
        private readonly TextBlock _cpuBoostTileState;
        private readonly Border _autoTdpTile;
        private readonly TextBlock _autoTdpTileState;
        private readonly Border _overlayTile;
        private readonly TextBlock _overlayTileState;
        private readonly Border _fanMaxTile;
        private readonly TextBlock _fanMaxTileState;

        // Battery card (non-focusable)
        private readonly TextBlock _batteryPercentText;
        private readonly TextBlock _batteryTimeText;
        private readonly TextBlock _batteryRateText;
        private readonly TextBlock _batteryIconText;

        // Metrics section (non-focusable)
        private readonly TextBlock _cpuUsageText;
        private readonly TextBlock _cpuTempText;
        private readonly TextBlock _gpuUsageText;
        private readonly TextBlock _gpuTempText;
        private readonly TextBlock _ramUsageText;
        private readonly TextBlock _fpsText;

        // Slider controls
        private readonly TextBlock _audioOutputText;
        private readonly TextBlock _volumeValueText;
        private readonly Slider _volumeSlider;
        private readonly TextBlock _brightnessValueText;
        private readonly Slider _brightnessSlider;
        private readonly TextBlock _tdpValueText;
        internal readonly Slider _tdpSlider;

        // State
        private int _audioDeviceIndex;
        private List<AudioDevice> _audioDevices = new List<AudioDevice>();
        private int _perfModeIndex;
        private bool _ctrlEmuState;
        private bool _cpuBoostState;
        private bool _autoTdpState;
        private int _overlayIndex;
        private bool _fanMaxState;
        private bool _suppressSliderEvent;

        // Events
        internal event Action<int> OnVolumeChanged;
        internal event Action<int> OnBrightnessChanged;
        internal event Action<int> OnAudioDeviceChanged;
        internal event Action<int> OnPerformanceModeChanged;
        internal event Action<int> OnTDPChanged;
        internal event Action<bool> OnControllerEmulationChanged;
        internal event Action<bool> OnCPUBoostChanged;
        internal event Action<bool> OnAutoTDPChanged;
        internal event Action<int> OnPerformanceOverlayChanged;
        internal event Action<bool> OnFanFullSpeedChanged;

        internal QuickTab()
        {
            _contentPanel = new StackPanel();

            // ── TILE GRID (4x2) ──
            var tileGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // [0] Volume tile
            _volumeTile = CreateTile("\uE767", "Volume", out _volumeTileState, "50%");
            _volumeTile.Margin = new Thickness(0, 2, 4, 2);
            Grid.SetRow(_volumeTile, 0);
            Grid.SetColumn(_volumeTile, 0);
            tileGrid.Children.Add(_volumeTile);

            // [1] Brightness tile
            _brightnessTile = CreateTile("\uE706", "Brightness", out _brightnessTileState, "50%");
            _brightnessTile.Margin = new Thickness(4, 2, 0, 2);
            Grid.SetRow(_brightnessTile, 0);
            Grid.SetColumn(_brightnessTile, 1);
            tileGrid.Children.Add(_brightnessTile);

            // [2] TDP Mode tile
            _tdpModeTile = CreateTile("\uE945", "TDP Mode", out _tdpModeTileState, "Balanced");
            _tdpModeTile.Margin = new Thickness(0, 2, 4, 2);
            Grid.SetRow(_tdpModeTile, 1);
            Grid.SetColumn(_tdpModeTile, 0);
            tileGrid.Children.Add(_tdpModeTile);

            // [3] Controller Emulation tile
            _ctrlEmuTile = CreateTile("\uEA8C", "Ctrl Emu", out _ctrlEmuTileState, "OFF");
            _ctrlEmuTile.Margin = new Thickness(4, 2, 0, 2);
            Grid.SetRow(_ctrlEmuTile, 1);
            Grid.SetColumn(_ctrlEmuTile, 1);
            tileGrid.Children.Add(_ctrlEmuTile);

            // [4] CPU Boost tile
            _cpuBoostTile = CreateTile("\uE7F4", "CPU Boost", out _cpuBoostTileState, "OFF");
            _cpuBoostTile.Margin = new Thickness(0, 2, 4, 2);
            Grid.SetRow(_cpuBoostTile, 2);
            Grid.SetColumn(_cpuBoostTile, 0);
            tileGrid.Children.Add(_cpuBoostTile);

            // [5] AutoTDP tile
            _autoTdpTile = CreateTile("\uE9F5", "AutoTDP", out _autoTdpTileState, "OFF");
            _autoTdpTile.Margin = new Thickness(4, 2, 0, 2);
            Grid.SetRow(_autoTdpTile, 2);
            Grid.SetColumn(_autoTdpTile, 1);
            tileGrid.Children.Add(_autoTdpTile);

            // [6] Overlay tile
            _overlayTile = CreateTile("\uE7B3", "Overlay", out _overlayTileState, "Off");
            _overlayTile.Margin = new Thickness(0, 2, 4, 2);
            Grid.SetRow(_overlayTile, 3);
            Grid.SetColumn(_overlayTile, 0);
            tileGrid.Children.Add(_overlayTile);

            // [7] Fan Max tile
            _fanMaxTile = CreateTile("\uE9CA", "Fan Max", out _fanMaxTileState, "OFF");
            _fanMaxTile.Margin = new Thickness(4, 2, 0, 2);
            Grid.SetRow(_fanMaxTile, 3);
            Grid.SetColumn(_fanMaxTile, 1);
            tileGrid.Children.Add(_fanMaxTile);

            _contentPanel.Children.Add(tileGrid);

            // ── BATTERY CARD (non-focusable) ──
            _contentPanel.Children.Add(CreateSectionHeader("Battery"));

            var batteryCard = new Border
            {
                Background = new SolidColorBrush(CardColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 3, 0, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(CardBorderColor),
            };

            var batteryGrid = new Grid();
            batteryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            batteryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _batteryIconText = new TextBlock
            {
                Text = "\uE83F",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 28,
                Foreground = new SolidColorBrush(GreenColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(_batteryIconText, 0);
            batteryGrid.Children.Add(_batteryIconText);

            var batteryInfoStack = new StackPanel();
            _batteryPercentText = new TextBlock
            {
                Text = "--%",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextColor),
            };
            batteryInfoStack.Children.Add(_batteryPercentText);

            _batteryTimeText = new TextBlock
            {
                Text = "--:--",
                FontSize = 12,
                Foreground = new SolidColorBrush(SubtextColor),
            };
            batteryInfoStack.Children.Add(_batteryTimeText);

            _batteryRateText = new TextBlock
            {
                Text = "-- W",
                FontSize = 12,
                Foreground = new SolidColorBrush(SubtextColor),
            };
            batteryInfoStack.Children.Add(_batteryRateText);

            Grid.SetColumn(batteryInfoStack, 1);
            batteryGrid.Children.Add(batteryInfoStack);
            batteryCard.Child = batteryGrid;
            _contentPanel.Children.Add(batteryCard);

            // ── METRICS SECTION (non-focusable) ──
            _contentPanel.Children.Add(CreateSectionHeader("Metrics"));

            var metricsCard = new Border
            {
                Background = new SolidColorBrush(CardColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 3, 0, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(CardBorderColor),
            };

            var metricsGrid = new Grid();
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _cpuUsageText = CreateMetricTextBlock("CPU: --%");
            Grid.SetRow(_cpuUsageText, 0); Grid.SetColumn(_cpuUsageText, 0);
            metricsGrid.Children.Add(_cpuUsageText);

            _cpuTempText = CreateMetricTextBlock("CPU: --\u00B0C");
            Grid.SetRow(_cpuTempText, 0); Grid.SetColumn(_cpuTempText, 1);
            metricsGrid.Children.Add(_cpuTempText);

            _gpuUsageText = CreateMetricTextBlock("GPU: --%");
            Grid.SetRow(_gpuUsageText, 1); Grid.SetColumn(_gpuUsageText, 0);
            metricsGrid.Children.Add(_gpuUsageText);

            _gpuTempText = CreateMetricTextBlock("GPU: --\u00B0C");
            Grid.SetRow(_gpuTempText, 1); Grid.SetColumn(_gpuTempText, 1);
            metricsGrid.Children.Add(_gpuTempText);

            _ramUsageText = CreateMetricTextBlock("RAM: --%");
            Grid.SetRow(_ramUsageText, 2); Grid.SetColumn(_ramUsageText, 0);
            metricsGrid.Children.Add(_ramUsageText);

            _fpsText = CreateMetricTextBlock("FPS: --");
            Grid.SetRow(_fpsText, 2); Grid.SetColumn(_fpsText, 1);
            metricsGrid.Children.Add(_fpsText);

            metricsCard.Child = metricsGrid;
            _contentPanel.Children.Add(metricsCard);

            // ── SECTION: Controls ──
            _contentPanel.Children.Add(CreateSectionHeader("Controls"));

            // [8] Audio Output selector
            var audioOutputBorder = CreateControlCard(out var audioOutputContent);
            audioOutputContent.Children.Add(CreateModeHeader("Audio Output", out _audioOutputText, "Default"));
            _contentPanel.Children.Add(audioOutputBorder);

            // [9] Volume slider
            var volumeBorder = CreateControlCard(out var volumeContent);
            volumeContent.Children.Add(CreateSliderHeader("Volume", out _volumeValueText, "50%"));
            _volumeSlider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = 50,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _volumeSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent)
                {
                    _volumeValueText.Text = (int)e.NewValue + "%";
                    _volumeTileState.Text = (int)e.NewValue + "%";
                }
            };
            volumeContent.Children.Add(_volumeSlider);
            _contentPanel.Children.Add(volumeBorder);

            // [10] Brightness slider
            var brightnessBorder = CreateControlCard(out var brightnessContent);
            brightnessContent.Children.Add(CreateSliderHeader("Brightness", out _brightnessValueText, "50%"));
            _brightnessSlider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = 50,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _brightnessSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent)
                {
                    _brightnessValueText.Text = (int)e.NewValue + "%";
                    _brightnessTileState.Text = (int)e.NewValue + "%";
                }
            };
            brightnessContent.Children.Add(_brightnessSlider);
            _contentPanel.Children.Add(brightnessBorder);

            // [11] TDP slider
            var tdpBorder = CreateControlCard(out var tdpContent);
            tdpContent.Children.Add(CreateSliderHeader("TDP", out _tdpValueText, "15W"));
            _tdpSlider = new Slider
            {
                Minimum = 4, Maximum = 35, Value = 15,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _tdpSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _tdpValueText.Text = (int)e.NewValue + "W";
            };
            tdpContent.Children.Add(_tdpSlider);
            _contentPanel.Children.Add(tdpBorder);

            _focusableControls = new Border[]
            {
                _volumeTile,        // 0  Volume tile
                _brightnessTile,    // 1  Brightness tile
                _tdpModeTile,       // 2  TDP Mode tile
                _ctrlEmuTile,       // 3  Controller Emu tile
                _cpuBoostTile,      // 4  CPU Boost tile
                _autoTdpTile,       // 5  AutoTDP tile
                _overlayTile,       // 6  Overlay tile
                _fanMaxTile,        // 7  Fan Max tile
                audioOutputBorder,  // 8  Audio Output
                volumeBorder,       // 9  Volume slider
                brightnessBorder,   // 10 Brightness slider
                tdpBorder,          // 11 TDP slider
            };
        }

        private static TextBlock CreateMetricTextBlock(string defaultText)
        {
            return new TextBlock
            {
                Text = defaultText,
                FontSize = 13,
                Foreground = new SolidColorBrush(TextColor),
                Margin = new Thickness(0, 2, 0, 2),
            };
        }

        internal override StackPanel ContentPanel => _contentPanel;
        internal override Border[] FocusableControls => _focusableControls;

        internal override void AdjustLeft(int focusIndex)
        {
            switch (focusIndex)
            {
                case 8:
                    if (_audioDevices.Count > 0 && _audioDeviceIndex > 0)
                    {
                        _audioDeviceIndex--;
                        _audioOutputText.Text = _audioDevices[_audioDeviceIndex].FriendlyName;
                    }
                    break;
                case 9: if (_volumeSlider.Value > _volumeSlider.Minimum) _volumeSlider.Value--; break;
                case 10: if (_brightnessSlider.Value > _brightnessSlider.Minimum) _brightnessSlider.Value--; break;
                case 11: if (_tdpSlider.Value > _tdpSlider.Minimum) _tdpSlider.Value--; break;
            }
        }

        internal override void AdjustRight(int focusIndex)
        {
            switch (focusIndex)
            {
                case 8:
                    if (_audioDevices.Count > 0 && _audioDeviceIndex < _audioDevices.Count - 1)
                    {
                        _audioDeviceIndex++;
                        _audioOutputText.Text = _audioDevices[_audioDeviceIndex].FriendlyName;
                    }
                    break;
                case 9: if (_volumeSlider.Value < _volumeSlider.Maximum) _volumeSlider.Value++; break;
                case 10: if (_brightnessSlider.Value < _brightnessSlider.Maximum) _brightnessSlider.Value++; break;
                case 11: if (_tdpSlider.Value < _tdpSlider.Maximum) _tdpSlider.Value++; break;
            }
        }

        internal override void Activate(int focusIndex, ref bool isAdjusting)
        {
            switch (focusIndex)
            {
                // Tiles: immediate action (no adjust mode)
                case 0: // Volume tile — no action, adjust via slider
                    break;
                case 1: // Brightness tile — no action, adjust via slider
                    break;
                case 2: // TDP Mode tile — cycle modes
                    _perfModeIndex = (_perfModeIndex + 1) % PerfModeNames.Length;
                    UpdateTileState(_tdpModeTile, _tdpModeTileState, PerfModeNames[_perfModeIndex], true);
                    OnPerformanceModeChanged?.Invoke(PerfModeToLegion[_perfModeIndex]);
                    break;
                case 3: // Controller Emulation tile — toggle
                    _ctrlEmuState = !_ctrlEmuState;
                    UpdateTileState(_ctrlEmuTile, _ctrlEmuTileState, _ctrlEmuState ? "ON" : "OFF", _ctrlEmuState);
                    OnControllerEmulationChanged?.Invoke(_ctrlEmuState);
                    break;
                case 4: // CPU Boost tile — toggle
                    _cpuBoostState = !_cpuBoostState;
                    UpdateTileState(_cpuBoostTile, _cpuBoostTileState, _cpuBoostState ? "ON" : "OFF", _cpuBoostState);
                    OnCPUBoostChanged?.Invoke(_cpuBoostState);
                    break;
                case 5: // AutoTDP tile — toggle
                    _autoTdpState = !_autoTdpState;
                    UpdateTileState(_autoTdpTile, _autoTdpTileState, _autoTdpState ? "ON" : "OFF", _autoTdpState);
                    OnAutoTDPChanged?.Invoke(_autoTdpState);
                    break;
                case 6: // Overlay tile — cycle Off→1→2→3
                    _overlayIndex = (_overlayIndex + 1) % OverlayNames.Length;
                    UpdateTileState(_overlayTile, _overlayTileState, OverlayNames[_overlayIndex], _overlayIndex > 0);
                    OnPerformanceOverlayChanged?.Invoke(_overlayIndex);
                    break;
                case 7: // Fan Max tile — toggle
                    _fanMaxState = !_fanMaxState;
                    UpdateTileState(_fanMaxTile, _fanMaxTileState, _fanMaxState ? "ON" : "OFF", _fanMaxState);
                    OnFanFullSpeedChanged?.Invoke(_fanMaxState);
                    break;

                // Sliders / selectors: toggle adjust mode
                case 8: case 9: case 10: case 11:
                    if (isAdjusting)
                    {
                        isAdjusting = false;
                        switch (focusIndex)
                        {
                            case 8:
                                if (_audioDevices.Count > 0)
                                    OnAudioDeviceChanged?.Invoke(_audioDeviceIndex);
                                break;
                            case 9: OnVolumeChanged?.Invoke((int)_volumeSlider.Value); break;
                            case 10: OnBrightnessChanged?.Invoke((int)_brightnessSlider.Value); break;
                            case 11: OnTDPChanged?.Invoke((int)_tdpSlider.Value); break;
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
                case 0: case 1: return ControlType.Tile;
                case 2: case 6: return ControlType.TileCycle;
                case 3: case 4: case 5: case 7: return ControlType.Toggle;
                case 8: return ControlType.ModeSelector;
                case 9: case 10: case 11: return ControlType.Slider;
                default: return ControlType.Tile;
            }
        }

        internal override Slider GetSlider(int focusIndex)
        {
            switch (focusIndex)
            {
                case 9: return _volumeSlider;
                case 10: return _brightnessSlider;
                case 11: return _tdpSlider;
                default: return null;
            }
        }

        internal override void CommitSliderValue(int focusIndex)
        {
            switch (focusIndex)
            {
                case 9: OnVolumeChanged?.Invoke((int)_volumeSlider.Value); break;
                case 10: OnBrightnessChanged?.Invoke((int)_brightnessSlider.Value); break;
                case 11: OnTDPChanged?.Invoke((int)_tdpSlider.Value); break;
            }
        }

        internal override void PointerCycleForward(int focusIndex)
        {
            switch (focusIndex)
            {
                case 2:
                    _perfModeIndex = (_perfModeIndex + 1) % PerfModeNames.Length;
                    UpdateTileState(_tdpModeTile, _tdpModeTileState, PerfModeNames[_perfModeIndex], true);
                    OnPerformanceModeChanged?.Invoke(PerfModeToLegion[_perfModeIndex]);
                    break;
                case 6:
                    _overlayIndex = (_overlayIndex + 1) % OverlayNames.Length;
                    UpdateTileState(_overlayTile, _overlayTileState, OverlayNames[_overlayIndex], _overlayIndex > 0);
                    OnPerformanceOverlayChanged?.Invoke(_overlayIndex);
                    break;
                case 8:
                    if (_audioDevices.Count > 0)
                    {
                        _audioDeviceIndex = (_audioDeviceIndex + 1) % _audioDevices.Count;
                        _audioOutputText.Text = _audioDevices[_audioDeviceIndex].FriendlyName;
                        OnAudioDeviceChanged?.Invoke(_audioDeviceIndex);
                    }
                    break;
            }
        }

        #region External Updates

        internal void UpdateVolume(int value)
        {
            _suppressSliderEvent = true;
            _volumeSlider.Value = Math.Max(0, Math.Min(100, value));
            _suppressSliderEvent = false;
            _volumeValueText.Text = value + "%";
            _volumeTileState.Text = value + "%";
        }

        internal void UpdateBrightness(int value)
        {
            _suppressSliderEvent = true;
            _brightnessSlider.Value = Math.Max(0, Math.Min(100, value));
            _suppressSliderEvent = false;
            _brightnessValueText.Text = value + "%";
            _brightnessTileState.Text = value + "%";
        }

        internal void UpdateAudioDevices(List<AudioDevice> devices, int defaultIndex)
        {
            _audioDevices = devices ?? new List<AudioDevice>();
            _audioDeviceIndex = Math.Max(0, Math.Min(_audioDevices.Count - 1, defaultIndex));
            if (_audioDevices.Count > 0)
                _audioOutputText.Text = _audioDevices[_audioDeviceIndex].FriendlyName;
            else
                _audioOutputText.Text = "No devices";
        }

        internal void UpdatePerformanceMode(int legionValue)
        {
            _perfModeIndex = PerformanceTab.LegionToPerfIndex(legionValue);
            _tdpModeTileState.Text = PerfModeNames[_perfModeIndex];
            UpdateTileState(_tdpModeTile, _tdpModeTileState, PerfModeNames[_perfModeIndex], true);
        }

        internal void UpdateTDP(int value, int min, int max)
        {
            _suppressSliderEvent = true;
            _tdpSlider.Minimum = min;
            _tdpSlider.Maximum = max;
            _tdpSlider.Value = Math.Max(min, Math.Min(max, value));
            _suppressSliderEvent = false;
            _tdpValueText.Text = value + "W";
        }

        internal void UpdateControllerEmulation(bool enabled)
        {
            _ctrlEmuState = enabled;
            UpdateTileState(_ctrlEmuTile, _ctrlEmuTileState, enabled ? "ON" : "OFF", enabled);
        }

        internal void UpdateCPUBoost(bool enabled)
        {
            _cpuBoostState = enabled;
            UpdateTileState(_cpuBoostTile, _cpuBoostTileState, enabled ? "ON" : "OFF", enabled);
        }

        internal void UpdateAutoTDP(bool enabled)
        {
            _autoTdpState = enabled;
            UpdateTileState(_autoTdpTile, _autoTdpTileState, enabled ? "ON" : "OFF", enabled);
        }

        internal void UpdatePerformanceOverlay(int level)
        {
            _overlayIndex = Math.Max(0, Math.Min(OverlayNames.Length - 1, level));
            UpdateTileState(_overlayTile, _overlayTileState, OverlayNames[_overlayIndex], _overlayIndex > 0);
        }

        internal void UpdateFanFullSpeed(bool enabled)
        {
            _fanMaxState = enabled;
            UpdateTileState(_fanMaxTile, _fanMaxTileState, enabled ? "ON" : "OFF", enabled);
        }

        internal void UpdateBattery(float pct, float drainW, float chargeW, float timeRemainingS, float timeToFullS, bool isCharging)
        {
            int pctInt = (int)Math.Round(pct);
            _batteryPercentText.Text = pctInt + "%";

            if (isCharging)
            {
                if (timeToFullS > 0)
                {
                    int h = (int)(timeToFullS / 3600);
                    int m = (int)((timeToFullS % 3600) / 60);
                    _batteryTimeText.Text = $"Full in ~{h}:{m:D2}";
                }
                else
                {
                    _batteryTimeText.Text = "Charging";
                }
                _batteryRateText.Text = chargeW > 0 ? $"+{chargeW:F1} W" : "";
            }
            else
            {
                if (timeRemainingS > 0)
                {
                    int h = (int)(timeRemainingS / 3600);
                    int m = (int)((timeRemainingS % 3600) / 60);
                    _batteryTimeText.Text = $"~{h}:{m:D2} left";
                }
                else
                {
                    _batteryTimeText.Text = "On battery";
                }
                _batteryRateText.Text = drainW > 0 ? $"-{drainW:F1} W" : "";
            }

            // Color coding
            Color color;
            if (pct > 50) color = GreenColor;
            else if (pct > 20) color = OrangeColor;
            else color = RedColor;

            _batteryIconText.Foreground = new SolidColorBrush(color);
            _batteryPercentText.Foreground = new SolidColorBrush(color);
        }

        internal void UpdateMetrics(float cpuUse, float cpuTemp, float gpuUse, float gpuTemp, float memUse, float fps)
        {
            _cpuUsageText.Text = $"CPU: {cpuUse:F0}%";
            _cpuTempText.Text = $"CPU: {cpuTemp:F0}\u00B0C";
            _gpuUsageText.Text = $"GPU: {gpuUse:F0}%";
            _gpuTempText.Text = $"GPU: {gpuTemp:F0}\u00B0C";
            _ramUsageText.Text = $"RAM: {memUse:F0}%";
            _fpsText.Text = fps > 0 ? $"FPS: {fps:F0}" : "FPS: --";
        }

        internal List<AudioDevice> GetAudioDevices() => _audioDevices;
        internal int GetAudioDeviceIndex() => _audioDeviceIndex;

        #endregion
    }
}

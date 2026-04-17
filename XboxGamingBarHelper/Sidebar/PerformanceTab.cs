using System;
using System.Windows;
using System.Windows.Controls;

namespace XboxGamingBarHelper.Sidebar
{
    internal class PerformanceTab : SidebarTab
    {
        // Performance mode value mapping: display index → Legion value
        private static readonly int[] PerfModeToLegion = { 1, 2, 3, 255 };
        internal static int LegionToPerfIndex(int v) => v == 255 ? 3 : v == 3 ? 2 : v == 2 ? 1 : 0;

        private static readonly string[] PerfModeNames = { "Quiet", "Balanced", "Performance", "Custom" };
        private static readonly string[] AutoTdpControllerNames = { "PID", "Q-Learning", "SARSA" };
        private static readonly string[] OsPowerModeNames = { "Efficiency", "Balanced", "Performance" };
        private static readonly string[] OverlayNames = { "Off", "Basic", "Detailed", "Full" };

        private readonly StackPanel _contentPanel;
        private readonly Border[] _focusableControls;

        // Controls — Performance Mode (top)
        private readonly TextBlock _perfModeText;

        // Controls — TDP group
        private readonly TextBlock _tdpValueText;
        internal readonly Slider _tdpSlider;
        private readonly Border _tdpBoostToggleBorder;
        private readonly TextBlock _tdpBoostToggleText;

        // Controls — AutoTDP group
        private readonly Border _autoTdpToggleBorder;
        private readonly TextBlock _autoTdpToggleText;
        private readonly TextBlock _autoTdpTargetFpsValueText;
        private readonly Slider _autoTdpTargetFpsSlider;
        private readonly TextBlock _autoTdpMinValueText;
        private readonly Slider _autoTdpMinSlider;
        private readonly TextBlock _autoTdpMaxValueText;
        private readonly Slider _autoTdpMaxSlider;
        private readonly TextBlock _autoTdpControllerText;

        // Controls — Overlay
        private readonly TextBlock _overlayText;

        // Controls — CPU group
        private readonly Border _cpuBoostToggleBorder;
        private readonly TextBlock _cpuBoostToggleText;
        private readonly TextBlock _cpuEppValueText;
        private readonly Slider _cpuEppSlider;

        // Controls — Power group
        private readonly TextBlock _osPowerModeText;
        private readonly TextBlock _fpsLimitValueText;
        private readonly Slider _fpsLimitSlider;

        // Controls — Device group
        private readonly Border _ctrlEmuToggleBorder;
        private readonly TextBlock _ctrlEmuToggleText;

        // State
        private int _perfModeIndex;
        private bool _tdpBoostState;
        private bool _autoTdpState;
        private int _autoTdpControllerIndex;
        private int _overlayIndex;
        private bool _cpuBoostState;
        private int _osPowerModeIndex;
        private bool _ctrlEmuState;
        private bool _suppressSliderEvent;

        // Events
        internal event Action<int> OnPerformanceModeChanged;
        internal event Action<int> OnTDPChanged;
        internal event Action<bool> OnTDPBoostChanged;
        internal event Action<bool> OnAutoTDPChanged;
        internal event Action<int> OnAutoTDPTargetFPSChanged;
        internal event Action<int> OnAutoTDPMinTDPChanged;
        internal event Action<int> OnAutoTDPMaxTDPChanged;
        internal event Action<int> OnAutoTDPControllerChanged;
        internal event Action<int> OnPerformanceOverlayChanged;
        internal event Action<bool> OnCPUBoostChanged;
        internal event Action<int> OnCPUEPPChanged;
        internal event Action<int> OnOSPowerModeChanged;
        internal event Action<int> OnFPSLimitChanged;
        internal event Action<bool> OnControllerEmulationChanged;

        internal PerformanceTab()
        {
            _contentPanel = new StackPanel();

            // [0] Performance Mode selector (at top)
            var perfBorder = CreateControlCard(out var perfContent);
            perfContent.Children.Add(CreateModeHeader("Performance Mode", out _perfModeText, "Balanced"));
            _contentPanel.Children.Add(perfBorder);

            // ── SECTION: TDP ──
            _contentPanel.Children.Add(CreateSectionHeader("TDP"));

            // [1] TDP slider
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

            // [2] TDP Boost toggle
            var tdpBoostBorder = CreateControlCard(out var tdpBoostContent);
            tdpBoostContent.Children.Add(CreateToggleRow("TDP Boost", out _tdpBoostToggleBorder, out _tdpBoostToggleText));
            _contentPanel.Children.Add(tdpBoostBorder);

            // ── SECTION: AutoTDP ──
            _contentPanel.Children.Add(CreateSectionHeader("AutoTDP"));

            // [3] AutoTDP toggle
            var autoTdpBorder = CreateControlCard(out var autoTdpContent);
            autoTdpContent.Children.Add(CreateToggleRow("AutoTDP", out _autoTdpToggleBorder, out _autoTdpToggleText));
            _contentPanel.Children.Add(autoTdpBorder);

            // [4] AutoTDP Target FPS slider
            var autoTdpFpsBorder = CreateControlCard(out var autoTdpFpsContent);
            autoTdpFpsContent.Children.Add(CreateSliderHeader("Target FPS", out _autoTdpTargetFpsValueText, "60"));
            _autoTdpTargetFpsSlider = new Slider
            {
                Minimum = 20, Maximum = 240, Value = 60,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _autoTdpTargetFpsSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _autoTdpTargetFpsValueText.Text = ((int)e.NewValue).ToString();
            };
            autoTdpFpsContent.Children.Add(_autoTdpTargetFpsSlider);
            _contentPanel.Children.Add(autoTdpFpsBorder);

            // [5] AutoTDP Min TDP slider
            var autoTdpMinBorder = CreateControlCard(out var autoTdpMinContent);
            autoTdpMinContent.Children.Add(CreateSliderHeader("Min TDP", out _autoTdpMinValueText, "4W"));
            _autoTdpMinSlider = new Slider
            {
                Minimum = 4, Maximum = 35, Value = 4,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _autoTdpMinSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _autoTdpMinValueText.Text = (int)e.NewValue + "W";
            };
            autoTdpMinContent.Children.Add(_autoTdpMinSlider);
            _contentPanel.Children.Add(autoTdpMinBorder);

            // [6] AutoTDP Max TDP slider
            var autoTdpMaxBorder = CreateControlCard(out var autoTdpMaxContent);
            autoTdpMaxContent.Children.Add(CreateSliderHeader("Max TDP", out _autoTdpMaxValueText, "35W"));
            _autoTdpMaxSlider = new Slider
            {
                Minimum = 4, Maximum = 85, Value = 35,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _autoTdpMaxSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _autoTdpMaxValueText.Text = (int)e.NewValue + "W";
            };
            autoTdpMaxContent.Children.Add(_autoTdpMaxSlider);
            _contentPanel.Children.Add(autoTdpMaxBorder);

            // [7] AutoTDP Controller mode selector
            var autoTdpCtrlBorder = CreateControlCard(out var autoTdpCtrlContent);
            autoTdpCtrlContent.Children.Add(CreateModeHeader("Controller", out _autoTdpControllerText, "PID"));
            _contentPanel.Children.Add(autoTdpCtrlBorder);

            // ── SECTION: Overlay ──
            _contentPanel.Children.Add(CreateSectionHeader("Overlay"));

            // [8] Performance Overlay selector
            var overlayBorder = CreateControlCard(out var overlayContent);
            overlayContent.Children.Add(CreateModeHeader("Performance Overlay", out _overlayText, "Off"));
            _contentPanel.Children.Add(overlayBorder);

            // ── SECTION: CPU ──
            _contentPanel.Children.Add(CreateSectionHeader("CPU"));

            // [9] CPU Boost toggle
            var cpuBoostBorder = CreateControlCard(out var cpuBoostContent);
            cpuBoostContent.Children.Add(CreateToggleRow("CPU Boost", out _cpuBoostToggleBorder, out _cpuBoostToggleText));
            _contentPanel.Children.Add(cpuBoostBorder);

            // [10] CPU EPP slider
            var cpuEppBorder = CreateControlCard(out var cpuEppContent);
            cpuEppContent.Children.Add(CreateSliderHeader("CPU EPP", out _cpuEppValueText, "50"));
            _cpuEppSlider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = 50,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _cpuEppSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent) _cpuEppValueText.Text = ((int)e.NewValue).ToString();
            };
            cpuEppContent.Children.Add(_cpuEppSlider);
            _contentPanel.Children.Add(cpuEppBorder);

            // ── SECTION: Power ──
            _contentPanel.Children.Add(CreateSectionHeader("Power"));

            // [11] OS Power Mode selector
            var osPowerBorder = CreateControlCard(out var osPowerContent);
            osPowerContent.Children.Add(CreateModeHeader("OS Power Mode", out _osPowerModeText, "Balanced"));
            _contentPanel.Children.Add(osPowerBorder);

            // [12] FPS Limit slider
            var fpsLimitBorder = CreateControlCard(out var fpsLimitContent);
            fpsLimitContent.Children.Add(CreateSliderHeader("FPS Limit", out _fpsLimitValueText, "OFF"));
            _fpsLimitSlider = new Slider
            {
                Minimum = 0, Maximum = 240, Value = 0,
                IsSnapToTickEnabled = true, TickFrequency = 1,
                Margin = new Thickness(0, 8, 0, 0),
            };
            _fpsLimitSlider.ValueChanged += (s, e) =>
            {
                if (!_suppressSliderEvent)
                    _fpsLimitValueText.Text = (int)e.NewValue == 0 ? "OFF" : (int)e.NewValue + " FPS";
            };
            fpsLimitContent.Children.Add(_fpsLimitSlider);
            _contentPanel.Children.Add(fpsLimitBorder);

            // ── SECTION: Device ──
            _contentPanel.Children.Add(CreateSectionHeader("Device"));

            // [13] Controller Emulation toggle
            var ctrlEmuBorder = CreateControlCard(out var ctrlEmuContent);
            ctrlEmuContent.Children.Add(CreateToggleRow("Controller Emulation", out _ctrlEmuToggleBorder, out _ctrlEmuToggleText));
            _contentPanel.Children.Add(ctrlEmuBorder);

            _focusableControls = new Border[]
            {
                perfBorder,          // 0  Performance Mode
                tdpBorder,           // 1  TDP slider
                tdpBoostBorder,      // 2  TDP Boost toggle
                autoTdpBorder,       // 3  AutoTDP toggle
                autoTdpFpsBorder,    // 4  AutoTDP Target FPS
                autoTdpMinBorder,    // 5  AutoTDP Min TDP
                autoTdpMaxBorder,    // 6  AutoTDP Max TDP
                autoTdpCtrlBorder,   // 7  AutoTDP Controller
                overlayBorder,       // 8  Performance Overlay
                cpuBoostBorder,      // 9  CPU Boost toggle
                cpuEppBorder,        // 10 CPU EPP slider
                osPowerBorder,       // 11 OS Power Mode
                fpsLimitBorder,      // 12 FPS Limit slider
                ctrlEmuBorder,       // 13 Controller Emulation
            };
        }

        internal override StackPanel ContentPanel => _contentPanel;
        internal override Border[] FocusableControls => _focusableControls;

        internal override void AdjustLeft(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0:
                    if (_perfModeIndex > 0)
                    {
                        _perfModeIndex--;
                        _perfModeText.Text = PerfModeNames[_perfModeIndex];
                    }
                    break;
                case 1: if (_tdpSlider.Value > _tdpSlider.Minimum) _tdpSlider.Value--; break;
                case 4: if (_autoTdpTargetFpsSlider.Value > _autoTdpTargetFpsSlider.Minimum) _autoTdpTargetFpsSlider.Value--; break;
                case 5: if (_autoTdpMinSlider.Value > _autoTdpMinSlider.Minimum) _autoTdpMinSlider.Value--; break;
                case 6: if (_autoTdpMaxSlider.Value > _autoTdpMaxSlider.Minimum) _autoTdpMaxSlider.Value--; break;
                case 7:
                    if (_autoTdpControllerIndex > 0)
                    {
                        _autoTdpControllerIndex--;
                        _autoTdpControllerText.Text = AutoTdpControllerNames[_autoTdpControllerIndex];
                    }
                    break;
                case 8:
                    if (_overlayIndex > 0)
                    {
                        _overlayIndex--;
                        _overlayText.Text = OverlayNames[_overlayIndex];
                    }
                    break;
                case 10: if (_cpuEppSlider.Value > _cpuEppSlider.Minimum) _cpuEppSlider.Value--; break;
                case 11:
                    if (_osPowerModeIndex > 0)
                    {
                        _osPowerModeIndex--;
                        _osPowerModeText.Text = OsPowerModeNames[_osPowerModeIndex];
                    }
                    break;
                case 12: if (_fpsLimitSlider.Value > _fpsLimitSlider.Minimum) _fpsLimitSlider.Value--; break;
            }
        }

        internal override void AdjustRight(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0:
                    if (_perfModeIndex < PerfModeNames.Length - 1)
                    {
                        _perfModeIndex++;
                        _perfModeText.Text = PerfModeNames[_perfModeIndex];
                    }
                    break;
                case 1: if (_tdpSlider.Value < _tdpSlider.Maximum) _tdpSlider.Value++; break;
                case 4: if (_autoTdpTargetFpsSlider.Value < _autoTdpTargetFpsSlider.Maximum) _autoTdpTargetFpsSlider.Value++; break;
                case 5: if (_autoTdpMinSlider.Value < _autoTdpMinSlider.Maximum) _autoTdpMinSlider.Value++; break;
                case 6: if (_autoTdpMaxSlider.Value < _autoTdpMaxSlider.Maximum) _autoTdpMaxSlider.Value++; break;
                case 7:
                    if (_autoTdpControllerIndex < AutoTdpControllerNames.Length - 1)
                    {
                        _autoTdpControllerIndex++;
                        _autoTdpControllerText.Text = AutoTdpControllerNames[_autoTdpControllerIndex];
                    }
                    break;
                case 8:
                    if (_overlayIndex < OverlayNames.Length - 1)
                    {
                        _overlayIndex++;
                        _overlayText.Text = OverlayNames[_overlayIndex];
                    }
                    break;
                case 10: if (_cpuEppSlider.Value < _cpuEppSlider.Maximum) _cpuEppSlider.Value++; break;
                case 11:
                    if (_osPowerModeIndex < OsPowerModeNames.Length - 1)
                    {
                        _osPowerModeIndex++;
                        _osPowerModeText.Text = OsPowerModeNames[_osPowerModeIndex];
                    }
                    break;
                case 12: if (_fpsLimitSlider.Value < _fpsLimitSlider.Maximum) _fpsLimitSlider.Value++; break;
            }
        }

        internal override void Activate(int focusIndex, ref bool isAdjusting)
        {
            switch (focusIndex)
            {
                // Sliders and mode selectors: toggle adjust mode
                case 0: case 1: case 4: case 5: case 6: case 7: case 8: case 10: case 11: case 12:
                    if (isAdjusting)
                    {
                        isAdjusting = false;
                        switch (focusIndex)
                        {
                            case 0: OnPerformanceModeChanged?.Invoke(PerfModeToLegion[_perfModeIndex]); break;
                            case 1: OnTDPChanged?.Invoke((int)_tdpSlider.Value); break;
                            case 4: OnAutoTDPTargetFPSChanged?.Invoke((int)_autoTdpTargetFpsSlider.Value); break;
                            case 5: OnAutoTDPMinTDPChanged?.Invoke((int)_autoTdpMinSlider.Value); break;
                            case 6: OnAutoTDPMaxTDPChanged?.Invoke((int)_autoTdpMaxSlider.Value); break;
                            case 7: OnAutoTDPControllerChanged?.Invoke(_autoTdpControllerIndex); break;
                            case 8: OnPerformanceOverlayChanged?.Invoke(_overlayIndex); break;
                            case 10: OnCPUEPPChanged?.Invoke((int)_cpuEppSlider.Value); break;
                            case 11: OnOSPowerModeChanged?.Invoke(_osPowerModeIndex); break;
                            case 12: OnFPSLimitChanged?.Invoke((int)_fpsLimitSlider.Value); break;
                        }
                    }
                    else
                    {
                        isAdjusting = true;
                    }
                    break;

                // Toggles: flip immediately
                case 2:
                    _tdpBoostState = !_tdpBoostState;
                    UpdateToggleVisual(_tdpBoostToggleBorder, _tdpBoostToggleText, _tdpBoostState);
                    OnTDPBoostChanged?.Invoke(_tdpBoostState);
                    break;
                case 3:
                    _autoTdpState = !_autoTdpState;
                    UpdateToggleVisual(_autoTdpToggleBorder, _autoTdpToggleText, _autoTdpState);
                    OnAutoTDPChanged?.Invoke(_autoTdpState);
                    break;
                case 9:
                    _cpuBoostState = !_cpuBoostState;
                    UpdateToggleVisual(_cpuBoostToggleBorder, _cpuBoostToggleText, _cpuBoostState);
                    OnCPUBoostChanged?.Invoke(_cpuBoostState);
                    break;
                case 13:
                    _ctrlEmuState = !_ctrlEmuState;
                    UpdateToggleVisual(_ctrlEmuToggleBorder, _ctrlEmuToggleText, _ctrlEmuState);
                    OnControllerEmulationChanged?.Invoke(_ctrlEmuState);
                    break;
            }
        }

        internal override void Refresh() { }

        internal override ControlType GetControlType(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0: case 7: case 8: case 11: return ControlType.ModeSelector;
                case 1: case 4: case 5: case 6: case 10: case 12: return ControlType.Slider;
                case 2: case 3: case 9: case 13: return ControlType.Toggle;
                default: return ControlType.Tile;
            }
        }

        internal override Slider GetSlider(int focusIndex)
        {
            switch (focusIndex)
            {
                case 1: return _tdpSlider;
                case 4: return _autoTdpTargetFpsSlider;
                case 5: return _autoTdpMinSlider;
                case 6: return _autoTdpMaxSlider;
                case 10: return _cpuEppSlider;
                case 12: return _fpsLimitSlider;
                default: return null;
            }
        }

        internal override void CommitSliderValue(int focusIndex)
        {
            switch (focusIndex)
            {
                case 1: OnTDPChanged?.Invoke((int)_tdpSlider.Value); break;
                case 4: OnAutoTDPTargetFPSChanged?.Invoke((int)_autoTdpTargetFpsSlider.Value); break;
                case 5: OnAutoTDPMinTDPChanged?.Invoke((int)_autoTdpMinSlider.Value); break;
                case 6: OnAutoTDPMaxTDPChanged?.Invoke((int)_autoTdpMaxSlider.Value); break;
                case 10: OnCPUEPPChanged?.Invoke((int)_cpuEppSlider.Value); break;
                case 12: OnFPSLimitChanged?.Invoke((int)_fpsLimitSlider.Value); break;
            }
        }

        internal override void PointerCycleForward(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0:
                    _perfModeIndex = (_perfModeIndex + 1) % PerfModeNames.Length;
                    _perfModeText.Text = PerfModeNames[_perfModeIndex];
                    OnPerformanceModeChanged?.Invoke(PerfModeToLegion[_perfModeIndex]);
                    break;
                case 7:
                    _autoTdpControllerIndex = (_autoTdpControllerIndex + 1) % AutoTdpControllerNames.Length;
                    _autoTdpControllerText.Text = AutoTdpControllerNames[_autoTdpControllerIndex];
                    OnAutoTDPControllerChanged?.Invoke(_autoTdpControllerIndex);
                    break;
                case 8:
                    _overlayIndex = (_overlayIndex + 1) % OverlayNames.Length;
                    _overlayText.Text = OverlayNames[_overlayIndex];
                    OnPerformanceOverlayChanged?.Invoke(_overlayIndex);
                    break;
                case 11:
                    _osPowerModeIndex = (_osPowerModeIndex + 1) % OsPowerModeNames.Length;
                    _osPowerModeText.Text = OsPowerModeNames[_osPowerModeIndex];
                    OnOSPowerModeChanged?.Invoke(_osPowerModeIndex);
                    break;
            }
        }

        #region External Updates

        internal void UpdatePerformanceMode(int legionValue)
        {
            _perfModeIndex = LegionToPerfIndex(legionValue);
            _perfModeText.Text = PerfModeNames[_perfModeIndex];
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

        internal void UpdateTDPBoost(bool enabled)
        {
            _tdpBoostState = enabled;
            UpdateToggleVisual(_tdpBoostToggleBorder, _tdpBoostToggleText, enabled);
        }

        internal void UpdateAutoTDP(bool enabled)
        {
            _autoTdpState = enabled;
            UpdateToggleVisual(_autoTdpToggleBorder, _autoTdpToggleText, enabled);
        }

        internal void UpdateAutoTDPTargetFPS(int value)
        {
            _suppressSliderEvent = true;
            _autoTdpTargetFpsSlider.Value = Math.Max(20, Math.Min(240, value));
            _suppressSliderEvent = false;
            _autoTdpTargetFpsValueText.Text = value.ToString();
        }

        internal void UpdateAutoTDPMinTDP(int value)
        {
            _suppressSliderEvent = true;
            _autoTdpMinSlider.Value = Math.Max(4, Math.Min(35, value));
            _suppressSliderEvent = false;
            _autoTdpMinValueText.Text = value + "W";
        }

        internal void UpdateAutoTDPMaxTDP(int value)
        {
            _suppressSliderEvent = true;
            _autoTdpMaxSlider.Value = Math.Max(4, Math.Min(85, value));
            _suppressSliderEvent = false;
            _autoTdpMaxValueText.Text = value + "W";
        }

        internal void UpdateAutoTDPController(int index)
        {
            _autoTdpControllerIndex = Math.Max(0, Math.Min(AutoTdpControllerNames.Length - 1, index));
            _autoTdpControllerText.Text = AutoTdpControllerNames[_autoTdpControllerIndex];
        }

        internal void UpdatePerformanceOverlay(int level)
        {
            _overlayIndex = Math.Max(0, Math.Min(OverlayNames.Length - 1, level));
            _overlayText.Text = OverlayNames[_overlayIndex];
        }

        internal void UpdateCPUBoost(bool enabled)
        {
            _cpuBoostState = enabled;
            UpdateToggleVisual(_cpuBoostToggleBorder, _cpuBoostToggleText, enabled);
        }

        internal void UpdateCPUEPP(int value)
        {
            _suppressSliderEvent = true;
            _cpuEppSlider.Value = Math.Max(0, Math.Min(100, value));
            _suppressSliderEvent = false;
            _cpuEppValueText.Text = value.ToString();
        }

        internal void UpdateOSPowerMode(int index)
        {
            _osPowerModeIndex = Math.Max(0, Math.Min(OsPowerModeNames.Length - 1, index));
            _osPowerModeText.Text = OsPowerModeNames[_osPowerModeIndex];
        }

        internal void UpdateFPSLimit(int value)
        {
            _suppressSliderEvent = true;
            _fpsLimitSlider.Value = Math.Max(0, Math.Min(240, value));
            _suppressSliderEvent = false;
            _fpsLimitValueText.Text = value == 0 ? "OFF" : value + " FPS";
        }

        internal void UpdateControllerEmulation(bool enabled)
        {
            _ctrlEmuState = enabled;
            UpdateToggleVisual(_ctrlEmuToggleBorder, _ctrlEmuToggleText, enabled);
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XboxGamingBarHelper.Sidebar
{
    internal class DisplayTab : SidebarTab
    {
        private static readonly string[] OrientationNames = { "Landscape", "Portrait", "Landscape Flipped", "Portrait Flipped" };

        private readonly StackPanel _contentPanel;
        private Border[] _focusableControls;

        // Controls — Display
        private readonly TextBlock _resolutionText;
        private readonly TextBlock _refreshRateText;

        // Controls — Settings
        private readonly Border _hdrToggleBorder;
        private readonly TextBlock _hdrToggleText;
        private readonly Border _hdrBorder;
        private readonly TextBlock _orientationText;

        // State
        private List<string> _resolutions = new List<string>();
        private int _resolutionIndex;
        private List<int> _refreshRates = new List<int>();
        private int _refreshRateIndex;
        private bool _hdrState;
        private bool _hdrSupported;
        private int _orientationIndex;

        // Events
        internal event Action<string> OnResolutionChanged;
        internal event Action<int> OnRefreshRateChanged;
        internal event Action<bool> OnHDRChanged;
        internal event Action<int> OnOrientationChanged;

        internal DisplayTab()
        {
            _contentPanel = new StackPanel();

            // ── SECTION: Display ──
            _contentPanel.Children.Add(CreateSectionHeader("Display"));

            // [0] Resolution selector
            var resBorder = CreateControlCard(out var resContent);
            resContent.Children.Add(CreateModeHeader("Resolution", out _resolutionText, "---"));
            _contentPanel.Children.Add(resBorder);

            // [1] Refresh Rate selector
            var rrBorder = CreateControlCard(out var rrContent);
            rrContent.Children.Add(CreateModeHeader("Refresh Rate", out _refreshRateText, "---"));
            _contentPanel.Children.Add(rrBorder);

            // ── SECTION: Settings ──
            _contentPanel.Children.Add(CreateSectionHeader("Settings"));

            // [2] HDR toggle
            _hdrBorder = CreateControlCard(out var hdrContent);
            hdrContent.Children.Add(CreateToggleRow("HDR", out _hdrToggleBorder, out _hdrToggleText));
            _contentPanel.Children.Add(_hdrBorder);

            // [3] Display Orientation selector
            var orientBorder = CreateControlCard(out var orientContent);
            orientContent.Children.Add(CreateModeHeader("Orientation", out _orientationText, "Landscape"));
            _contentPanel.Children.Add(orientBorder);

            _focusableControls = new Border[]
            {
                resBorder,     // 0  Resolution
                rrBorder,      // 1  Refresh Rate
                _hdrBorder,    // 2  HDR toggle
                orientBorder,  // 3  Orientation
            };
        }

        internal override StackPanel ContentPanel => _contentPanel;
        internal override Border[] FocusableControls => _focusableControls;

        internal override void AdjustLeft(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0:
                    if (_resolutions.Count > 0 && _resolutionIndex > 0)
                    {
                        _resolutionIndex--;
                        _resolutionText.Text = _resolutions[_resolutionIndex];
                    }
                    break;
                case 1:
                    if (_refreshRates.Count > 0 && _refreshRateIndex > 0)
                    {
                        _refreshRateIndex--;
                        _refreshRateText.Text = _refreshRates[_refreshRateIndex] + " Hz";
                    }
                    break;
                case 3:
                    if (_orientationIndex > 0)
                    {
                        _orientationIndex--;
                        _orientationText.Text = OrientationNames[_orientationIndex];
                    }
                    break;
            }
        }

        internal override void AdjustRight(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0:
                    if (_resolutions.Count > 0 && _resolutionIndex < _resolutions.Count - 1)
                    {
                        _resolutionIndex++;
                        _resolutionText.Text = _resolutions[_resolutionIndex];
                    }
                    break;
                case 1:
                    if (_refreshRates.Count > 0 && _refreshRateIndex < _refreshRates.Count - 1)
                    {
                        _refreshRateIndex++;
                        _refreshRateText.Text = _refreshRates[_refreshRateIndex] + " Hz";
                    }
                    break;
                case 3:
                    if (_orientationIndex < OrientationNames.Length - 1)
                    {
                        _orientationIndex++;
                        _orientationText.Text = OrientationNames[_orientationIndex];
                    }
                    break;
            }
        }

        internal override void Activate(int focusIndex, ref bool isAdjusting)
        {
            switch (focusIndex)
            {
                // Mode selectors: toggle adjust mode
                case 0: case 1: case 3:
                    if (isAdjusting)
                    {
                        isAdjusting = false;
                        switch (focusIndex)
                        {
                            case 0:
                                if (_resolutions.Count > 0)
                                    OnResolutionChanged?.Invoke(_resolutions[_resolutionIndex]);
                                break;
                            case 1:
                                if (_refreshRates.Count > 0)
                                    OnRefreshRateChanged?.Invoke(_refreshRates[_refreshRateIndex]);
                                break;
                            case 3:
                                OnOrientationChanged?.Invoke(_orientationIndex);
                                break;
                        }
                    }
                    else
                    {
                        isAdjusting = true;
                    }
                    break;

                // HDR toggle
                case 2:
                    if (_hdrSupported)
                    {
                        _hdrState = !_hdrState;
                        UpdateToggleVisual(_hdrToggleBorder, _hdrToggleText, _hdrState);
                        OnHDRChanged?.Invoke(_hdrState);
                    }
                    break;
            }
        }

        internal override void Refresh() { }

        internal override ControlType GetControlType(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0: case 1: case 3: return ControlType.ModeSelector;
                case 2: return ControlType.Toggle;
                default: return ControlType.Tile;
            }
        }

        internal override void PointerCycleForward(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0:
                    if (_resolutions.Count > 0)
                    {
                        _resolutionIndex = (_resolutionIndex + 1) % _resolutions.Count;
                        _resolutionText.Text = _resolutions[_resolutionIndex];
                        OnResolutionChanged?.Invoke(_resolutions[_resolutionIndex]);
                    }
                    break;
                case 1:
                    if (_refreshRates.Count > 0)
                    {
                        _refreshRateIndex = (_refreshRateIndex + 1) % _refreshRates.Count;
                        _refreshRateText.Text = _refreshRates[_refreshRateIndex] + " Hz";
                        OnRefreshRateChanged?.Invoke(_refreshRates[_refreshRateIndex]);
                    }
                    break;
                case 3:
                    _orientationIndex = (_orientationIndex + 1) % OrientationNames.Length;
                    _orientationText.Text = OrientationNames[_orientationIndex];
                    OnOrientationChanged?.Invoke(_orientationIndex);
                    break;
            }
        }

        #region External Updates

        internal void UpdateResolutions(List<string> resolutions, string current)
        {
            _resolutions = resolutions ?? new List<string>();
            _resolutionIndex = 0;
            for (int i = 0; i < _resolutions.Count; i++)
            {
                if (_resolutions[i] == current)
                {
                    _resolutionIndex = i;
                    break;
                }
            }
            _resolutionText.Text = _resolutions.Count > 0 ? _resolutions[_resolutionIndex] : "---";
        }

        internal void UpdateResolution(string current)
        {
            for (int i = 0; i < _resolutions.Count; i++)
            {
                if (_resolutions[i] == current)
                {
                    _resolutionIndex = i;
                    _resolutionText.Text = current;
                    return;
                }
            }
            _resolutionText.Text = current ?? "---";
        }

        internal void UpdateRefreshRates(List<int> rates, int current)
        {
            _refreshRates = rates ?? new List<int>();
            _refreshRateIndex = 0;
            for (int i = 0; i < _refreshRates.Count; i++)
            {
                if (_refreshRates[i] == current)
                {
                    _refreshRateIndex = i;
                    break;
                }
            }
            _refreshRateText.Text = _refreshRates.Count > 0 ? _refreshRates[_refreshRateIndex] + " Hz" : "---";
        }

        internal void UpdateRefreshRate(int current)
        {
            for (int i = 0; i < _refreshRates.Count; i++)
            {
                if (_refreshRates[i] == current)
                {
                    _refreshRateIndex = i;
                    _refreshRateText.Text = current + " Hz";
                    return;
                }
            }
            _refreshRateText.Text = current + " Hz";
        }

        internal void UpdateHDR(bool enabled, bool supported)
        {
            _hdrState = enabled;
            _hdrSupported = supported;
            UpdateToggleVisual(_hdrToggleBorder, _hdrToggleText, enabled);
            _hdrBorder.Opacity = supported ? 1.0 : 0.4;
        }

        internal void UpdateOrientation(int index)
        {
            _orientationIndex = Math.Max(0, Math.Min(OrientationNames.Length - 1, index));
            _orientationText.Text = OrientationNames[_orientationIndex];
        }

        #endregion
    }
}

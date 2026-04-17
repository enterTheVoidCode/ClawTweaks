using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XboxGamingBar.QuickSettings
{
    /// <summary>
    /// Types of quick settings tiles
    /// </summary>
    public enum TileType
    {
        TDPMode,            // 4-state cycling (Silence → Balanced → Performance → Custom)
        LosslessScaling,    // Toggle (On/Off)
        RSR,                // Toggle for AMD Radeon Super Resolution
        AntiLag,            // Toggle for AMD Anti-Lag
        RadeonChill,        // Toggle for AMD Radeon Chill
        OnScreenKeyboard,   // Trigger button to launch on-screen keyboard
        CPUBoost,           // Toggle (On/Off)
        EPP,                // 4-state cycling (0 → 30 → 80 → 100)
        PerformanceOverlay, // 4-state cycling (Off → 1 → 2 → 3)
        CustomShortcut      // Custom keyboard shortcut tile
    }

    /// <summary>
    /// Represents a single quick settings tile
    /// </summary>
    public class QuickSettingsTile : INotifyPropertyChanged
    {
        private string _id;
        private TileType _type;
        private string _name;
        private string _icon;
        private int _currentState;
        private int _order;
        private bool _isVisible;
        private string _customShortcut;
        private string _customColor;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Unique identifier for the tile
        /// </summary>
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Type of tile (determines behavior)
        /// </summary>
        public TileType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display name for the tile
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Icon glyph (Segoe MDL2 Assets)
        /// </summary>
        public string Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Current state index (0-based)
        /// For toggles: 0=Off, 1=On
        /// For cycling: index into states array
        /// </summary>
        public int CurrentState
        {
            get => _currentState;
            set { _currentState = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(IsOn)); }
        }

        /// <summary>
        /// Display order in the grid (lower = first)
        /// </summary>
        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether the tile is visible in the grid
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// For CustomShortcut tiles: the keyboard shortcut string (e.g., "Alt+S", "Ctrl+Shift+T")
        /// </summary>
        public string CustomShortcut
        {
            get => _customShortcut;
            set { _customShortcut = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Optional custom accent color for the tile (hex string)
        /// </summary>
        public string CustomColor
        {
            get => _customColor;
            set { _customColor = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the state labels for this tile type
        /// </summary>
        public string[] GetStateLabels()
        {
            switch (Type)
            {
                case TileType.TDPMode:
                    return new[] { "Quiet", "Balanced", "Performance", "Custom" };
                case TileType.EPP:
                    return new[] { "0", "30", "80", "100" };
                case TileType.PerformanceOverlay:
                    return new[] { "Off", "1", "2", "3" };
                case TileType.LosslessScaling:
                case TileType.RSR:
                case TileType.AntiLag:
                case TileType.RadeonChill:
                case TileType.CPUBoost:
                    return new[] { "Off", "On" };
                case TileType.OnScreenKeyboard:
                    return new[] { "Open" };
                case TileType.CustomShortcut:
                    return new[] { "Run" };
                default:
                    return new[] { "Unknown" };
            }
        }

        /// <summary>
        /// Gets the current state as display text
        /// </summary>
        public string StateText
        {
            get
            {
                var labels = GetStateLabels();
                if (CurrentState >= 0 && CurrentState < labels.Length)
                    return labels[CurrentState];
                return "";
            }
        }

        /// <summary>
        /// For toggle tiles, returns whether the tile is in the "on" state
        /// </summary>
        public bool IsOn => CurrentState > 0;

        /// <summary>
        /// Returns the total number of states for this tile
        /// </summary>
        public int StateCount => GetStateLabels().Length;

        /// <summary>
        /// Creates a default tile of the specified type
        /// </summary>
        public static QuickSettingsTile CreateDefault(TileType type, int order)
        {
            var tile = new QuickSettingsTile
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Order = order,
                IsVisible = true,
                CurrentState = 0
            };

            // Set default name and icon based on type
            switch (type)
            {
                case TileType.TDPMode:
                    tile.Name = "TDP Mode";
                    tile.Icon = "\uE945"; // Performance icon
                    break;
                case TileType.LosslessScaling:
                    tile.Name = "Lossless Scaling";
                    tile.Icon = "\uE740"; // Full screen icon
                    break;
                case TileType.RSR:
                    tile.Name = "RSR";
                    tile.Icon = "\uE8B3"; // Image icon
                    break;
                case TileType.AntiLag:
                    tile.Name = "Anti-Lag";
                    tile.Icon = "\uE916"; // Timer icon
                    break;
                case TileType.RadeonChill:
                    tile.Name = "Chill";
                    tile.Icon = "\uE9CA"; // Cool/fan icon
                    break;
                case TileType.OnScreenKeyboard:
                    tile.Name = "Keyboard";
                    tile.Icon = "\uE765"; // Keyboard icon
                    break;
                case TileType.CPUBoost:
                    tile.Name = "CPU Boost";
                    tile.Icon = "\uE7F4"; // Processor icon
                    break;
                case TileType.EPP:
                    tile.Name = "EPP";
                    tile.Icon = "\uE83E"; // Settings icon
                    break;
                case TileType.PerformanceOverlay:
                    tile.Name = "Overlay";
                    tile.Icon = "\uE7B3"; // Monitor icon
                    break;
                case TileType.CustomShortcut:
                    tile.Name = "Custom";
                    tile.Icon = "\uE768"; // Keyboard shortcut icon
                    break;
            }

            return tile;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

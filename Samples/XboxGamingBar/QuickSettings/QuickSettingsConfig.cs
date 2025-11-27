using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Windows.Storage;
using NLog;

namespace XboxGamingBar.QuickSettings
{
    /// <summary>
    /// Configuration and state management for Quick Settings tiles
    /// Handles persistence to LocalSettings
    /// </summary>
    public class QuickSettingsConfig : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string StorageKey = "QuickSettingsConfig";
        private const string TilesStorageKey = "QuickSettingsTiles";

        private static QuickSettingsConfig _instance;
        private ObservableCollection<QuickSettingsTile> _tiles;
        private bool _isEditMode;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static QuickSettingsConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new QuickSettingsConfig();
                    _instance.Load();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Collection of all tiles (visible and hidden)
        /// </summary>
        public ObservableCollection<QuickSettingsTile> Tiles
        {
            get => _tiles;
            private set
            {
                _tiles = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibleTiles));
            }
        }

        /// <summary>
        /// Collection of only visible tiles, ordered
        /// </summary>
        public IEnumerable<QuickSettingsTile> VisibleTiles =>
            Tiles?.Where(t => t.IsVisible).OrderBy(t => t.Order) ?? Enumerable.Empty<QuickSettingsTile>();

        /// <summary>
        /// Whether the customization panel is in edit mode
        /// </summary>
        public bool IsEditMode
        {
            get => _isEditMode;
            set { _isEditMode = value; OnPropertyChanged(); }
        }

        private QuickSettingsConfig()
        {
            _tiles = new ObservableCollection<QuickSettingsTile>();
        }

        /// <summary>
        /// Initialize with default tiles
        /// </summary>
        public void InitializeDefaults()
        {
            Tiles.Clear();

            // Create default tiles in order
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.TDPMode, 0));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.LosslessScaling, 1));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.RSR, 2));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.AntiLag, 3));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.RadeonChill, 4));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.OnScreenKeyboard, 5));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.CPUBoost, 6));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.EPP, 7));
            Tiles.Add(QuickSettingsTile.CreateDefault(TileType.PerformanceOverlay, 8));

            Logger.Info("Initialized Quick Settings with default tiles");
        }

        /// <summary>
        /// Load configuration from LocalSettings
        /// </summary>
        public void Load()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue(TilesStorageKey, out var tilesJson) && tilesJson is string json)
                {
                    var tileDataList = JsonSerializer.Deserialize<List<TileData>>(json);
                    if (tileDataList != null && tileDataList.Count > 0)
                    {
                        Tiles.Clear();
                        foreach (var data in tileDataList)
                        {
                            var tile = new QuickSettingsTile
                            {
                                Id = data.Id ?? Guid.NewGuid().ToString(),
                                Type = (TileType)data.Type,
                                Name = data.Name,
                                Icon = data.Icon,
                                CurrentState = data.CurrentState,
                                Order = data.Order,
                                IsVisible = data.IsVisible,
                                CustomShortcut = data.CustomShortcut,
                                CustomColor = data.CustomColor
                            };
                            Tiles.Add(tile);
                        }
                        Logger.Info($"Loaded {Tiles.Count} Quick Settings tiles from storage");
                        OnPropertyChanged(nameof(VisibleTiles));
                        return;
                    }
                }

                // No saved config, initialize defaults
                InitializeDefaults();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading Quick Settings config: {ex.Message}");
                InitializeDefaults();
            }
        }

        /// <summary>
        /// Save configuration to LocalSettings
        /// </summary>
        public void Save()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                var tileDataList = Tiles.Select(t => new TileData
                {
                    Id = t.Id,
                    Type = (int)t.Type,
                    Name = t.Name,
                    Icon = t.Icon,
                    CurrentState = t.CurrentState,
                    Order = t.Order,
                    IsVisible = t.IsVisible,
                    CustomShortcut = t.CustomShortcut,
                    CustomColor = t.CustomColor
                }).ToList();

                var json = JsonSerializer.Serialize(tileDataList);
                settings.Values[TilesStorageKey] = json;

                Logger.Info($"Saved {Tiles.Count} Quick Settings tiles to storage");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving Quick Settings config: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a custom shortcut tile
        /// </summary>
        public QuickSettingsTile AddCustomTile(string name, string icon, string shortcut, string color = null)
        {
            var tile = new QuickSettingsTile
            {
                Id = Guid.NewGuid().ToString(),
                Type = TileType.CustomShortcut,
                Name = name,
                Icon = icon ?? "\uE768",
                CurrentState = 0,
                Order = Tiles.Count,
                IsVisible = true,
                CustomShortcut = shortcut,
                CustomColor = color
            };

            Tiles.Add(tile);
            Save();
            OnPropertyChanged(nameof(VisibleTiles));

            Logger.Info($"Added custom tile: {name} with shortcut {shortcut}");
            return tile;
        }

        /// <summary>
        /// Remove a tile by ID
        /// </summary>
        public bool RemoveTile(string id)
        {
            var tile = Tiles.FirstOrDefault(t => t.Id == id);
            if (tile != null && tile.Type == TileType.CustomShortcut)
            {
                Tiles.Remove(tile);
                ReorderTiles();
                Save();
                OnPropertyChanged(nameof(VisibleTiles));
                Logger.Info($"Removed custom tile: {tile.Name}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Move a tile to a new position
        /// </summary>
        public void MoveTile(int fromIndex, int toIndex)
        {
            var visibleTiles = VisibleTiles.ToList();
            if (fromIndex < 0 || fromIndex >= visibleTiles.Count ||
                toIndex < 0 || toIndex >= visibleTiles.Count)
                return;

            var tile = visibleTiles[fromIndex];
            visibleTiles.RemoveAt(fromIndex);
            visibleTiles.Insert(toIndex, tile);

            // Update order values
            for (int i = 0; i < visibleTiles.Count; i++)
            {
                visibleTiles[i].Order = i;
            }

            Save();
            OnPropertyChanged(nameof(VisibleTiles));
            Logger.Info($"Moved tile from {fromIndex} to {toIndex}");
        }

        /// <summary>
        /// Toggle tile visibility
        /// </summary>
        public void SetTileVisibility(string id, bool isVisible)
        {
            var tile = Tiles.FirstOrDefault(t => t.Id == id);
            if (tile != null)
            {
                tile.IsVisible = isVisible;
                if (isVisible)
                {
                    // Add to end of visible tiles
                    tile.Order = VisibleTiles.Count() - 1;
                }
                ReorderTiles();
                Save();
                OnPropertyChanged(nameof(VisibleTiles));
            }
        }

        /// <summary>
        /// Update tile state
        /// </summary>
        public void UpdateTileState(string id, int state)
        {
            var tile = Tiles.FirstOrDefault(t => t.Id == id);
            if (tile != null)
            {
                tile.CurrentState = state;
                // State changes are transient, don't save immediately
            }
        }

        /// <summary>
        /// Get tile by ID
        /// </summary>
        public QuickSettingsTile GetTile(string id)
        {
            return Tiles.FirstOrDefault(t => t.Id == id);
        }

        /// <summary>
        /// Get tile by type (first match)
        /// </summary>
        public QuickSettingsTile GetTileByType(TileType type)
        {
            return Tiles.FirstOrDefault(t => t.Type == type);
        }

        /// <summary>
        /// Recalculate order values for visible tiles
        /// </summary>
        private void ReorderTiles()
        {
            var visibleTiles = Tiles.Where(t => t.IsVisible).OrderBy(t => t.Order).ToList();
            for (int i = 0; i < visibleTiles.Count; i++)
            {
                visibleTiles[i].Order = i;
            }
        }

        /// <summary>
        /// Export configuration as JSON string
        /// </summary>
        public string ExportConfig()
        {
            var tileDataList = Tiles.Select(t => new TileData
            {
                Id = t.Id,
                Type = (int)t.Type,
                Name = t.Name,
                Icon = t.Icon,
                CurrentState = 0, // Don't export current state
                Order = t.Order,
                IsVisible = t.IsVisible,
                CustomShortcut = t.CustomShortcut,
                CustomColor = t.CustomColor
            }).ToList();

            return JsonSerializer.Serialize(tileDataList, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Import configuration from JSON string
        /// </summary>
        public bool ImportConfig(string json)
        {
            try
            {
                var tileDataList = JsonSerializer.Deserialize<List<TileData>>(json);
                if (tileDataList != null && tileDataList.Count > 0)
                {
                    Tiles.Clear();
                    foreach (var data in tileDataList)
                    {
                        var tile = new QuickSettingsTile
                        {
                            Id = data.Id ?? Guid.NewGuid().ToString(),
                            Type = (TileType)data.Type,
                            Name = data.Name,
                            Icon = data.Icon,
                            CurrentState = 0,
                            Order = data.Order,
                            IsVisible = data.IsVisible,
                            CustomShortcut = data.CustomShortcut,
                            CustomColor = data.CustomColor
                        };
                        Tiles.Add(tile);
                    }
                    Save();
                    OnPropertyChanged(nameof(VisibleTiles));
                    Logger.Info($"Imported {Tiles.Count} Quick Settings tiles");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error importing Quick Settings config: {ex.Message}");
            }
            return false;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Serialization data class for tiles
        /// </summary>
        private class TileData
        {
            public string Id { get; set; }
            public int Type { get; set; }
            public string Name { get; set; }
            public string Icon { get; set; }
            public int CurrentState { get; set; }
            public int Order { get; set; }
            public bool IsVisible { get; set; }
            public string CustomShortcut { get; set; }
            public string CustomColor { get; set; }
        }
    }
}

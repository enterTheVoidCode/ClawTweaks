using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        // ── Controller button-binding state (Customize Tiles panel) ──────────────
        private Flyout _btnBindFlyout;
        private string _btnBindTileId;
        private Button _btnBindSaveButton;
        private List<uint> _btnBindSelectedBits = new List<uint>();
        private StackPanel _btnBindTagsPanel;

        /// <summary>
        /// All MSI Claw controller buttons available for tile hotkey binding.
        /// Bits 0x0001–0x8000 are standard XInput wButtons bits.
        /// Bits 0x10000 (M1) and 0x20000 (M2) are MSI Claw OEM buttons
        /// (reserved — helper-side detection added separately).
        /// </summary>
        private static readonly (string Label, uint Bit)[] ClawButtonDefs =
        {
            ("A",         0x1000u), ("B",         0x2000u), ("X",         0x4000u), ("Y",         0x8000u),
            ("Start",     0x0010u), ("Select",    0x0020u),
            ("LB",        0x0100u), ("RB",        0x0200u),
            ("LS",        0x0040u), ("RS",        0x0080u),
            ("D-Pad Up",  0x0001u), ("D-Pad Down",0x0002u), ("D-Pad Left",0x0004u), ("D-Pad Right",0x0008u),
            ("M1",        0x10000u),("M2",        0x20000u),
        };

        /// <summary>
        /// Build sortable grid for tile customization
        /// </summary>
        private void BuildSortableGrid()
        {
            if (TileSortableGrid == null) return;

            TileSortableGrid.Children.Clear();

            // Get all tiles sorted by order (including hidden ones)
            var allTiles = qsTileDefinitions
                .Where(t => !ShouldSkipTile(t))
                .OrderBy(t => t.Order)
                .ToList();

            // Sort panel always uses 3 columns (split-tiles are too small at 4)
            const int sortCols = 3;
            Grid currentRow = null;
            int colIndex = 0;

            for (int i = 0; i < allTiles.Count; i++)
            {
                if (colIndex == 0)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    for (int c = 0; c < sortCols; c++)
                    {
                        if (c > 0) currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });  // Spacer
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    TileSortableGrid.Children.Add(currentRow);
                }

                var tile = allTiles[i];
                var miniTile = CreateMiniTileForSort(tile, i);
                Grid.SetColumn(miniTile, colIndex * 2);
                currentRow.Children.Add(miniTile);

                colIndex++;
                if (colIndex >= sortCols)
                {
                    colIndex = 0;
                }
            }
        }

        /// <summary>
        /// Create a split mini tile for the sortable grid.
        /// Top: main button (select/swap). Bottom: Show/Hide + Button-bind sub-row.
        /// </summary>
        private FrameworkElement CreateMiniTileForSort(TileDefinition tile, int index)
        {
            bool isSelected = qsSelectedTileForMove?.Id == tile.Id;

            var fgColor = tile.IsVisible ? Windows.UI.Colors.White : Windows.UI.Colors.Gray;
            var mainBg = isSelected
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180))
                : (tile.IsVisible
                    ? tileOffBrush
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(128, 26, 28, 30)));

            // \u2500\u2500 Main button (icon + name + order badge) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(BuildTileIconElement(tile.ActionType, tile.Id, tile.Glyph, 18, new SolidColorBrush(fgColor)));
            stack.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(fgColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var mainContent = new Grid();
            mainContent.Children.Add(stack);

            // Order number badge (bottom-left)
            mainContent.Children.Add(new TextBlock
            {
                Text = (index + 1).ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 2)
            });

            // Custom shortcut pin indicator (bottom-right)
            if (!string.IsNullOrEmpty(tile.CustomShortcut))
            {
                mainContent.Children.Add(new FontIcon
                {
                    Glyph = "\uE932",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 200, 100)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 4, 2)
                });
            }

            var mainButton = new Button
            {
                Tag = tile.Id,
                MinHeight = 52,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = mainBg,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                UseSystemFocusVisuals = true,
                FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                TabIndex = index * 3,
                Content = mainContent
            };
            mainButton.Click += SortableTile_Click;

            // \u2500\u2500 Sub-row background (slightly darker/shifted for selected) \u2500\u2500\u2500\u2500\u2500
            var subBg = new SolidColorBrush(isSelected
                ? Windows.UI.Color.FromArgb(255, 0, 95, 145)
                : Windows.UI.Color.FromArgb(255, 30, 34, 38));

            var dividerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 80, 85, 92));

            // Visibility toggle button (left sub-button)
            var visContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            visContent.Children.Add(new FontIcon
            {
                Glyph = tile.IsVisible ? "\uE7B3" : "\uED1A",  // Eye / Eye-off
                FontSize = 11,
                Foreground = new SolidColorBrush(tile.IsVisible
                    ? Windows.UI.Color.FromArgb(255, 100, 200, 100)
                    : Windows.UI.Color.FromArgb(255, 200, 100, 100)),
                Margin = new Thickness(0, 0, 4, 0)
            });
            visContent.Children.Add(new TextBlock
            {
                Text = tile.IsVisible ? "Hide" : "Show",
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 220, 220, 220)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var visButton = new Button
            {
                Tag = tile.Id + "_vis",
                Height = 28,
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = subBg,
                BorderBrush = dividerBrush,
                BorderThickness = new Thickness(0, 1, 0.5, 0),
                CornerRadius = new CornerRadius(0, 0, 0, 7),
                UseSystemFocusVisuals = true,
                FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                TabIndex = index * 3 + 1,
                Content = visContent
            };
            visButton.Click += SortableTileVisibility_Click;

            // Button-bind sub-button (right): shows "Button" when unbound, combo string when bound
            bool hasBtnBind = !string.IsNullOrEmpty(tile.ControllerHotkey);
            string btnBindLabel = hasBtnBind
                ? XInputMaskToDisplayString(ParseHotkeyMaskUInt(tile.ControllerHotkey))
                : "Button";
            var btnBindColor = hasBtnBind
                ? Windows.UI.Color.FromArgb(255, 80, 220, 200)   // teal when bound
                : Windows.UI.Color.FromArgb(140, 200, 200, 200);  // grey when unbound

            var btnContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnContent.Children.Add(new FontIcon
            {
                Glyph = "\uE7FC",  // Gamepad
                FontSize = 11,
                Foreground = new SolidColorBrush(btnBindColor),
                Margin = new Thickness(0, 0, 4, 0)
            });
            btnContent.Children.Add(new TextBlock
            {
                Text = btnBindLabel,
                FontSize = 10,
                Foreground = new SolidColorBrush(btnBindColor),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var btnBindButton = new Button
            {
                Tag = tile.Id + "_btn",
                Height = 28,
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = subBg,
                BorderBrush = dividerBrush,
                BorderThickness = new Thickness(0.5, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 7, 0),
                UseSystemFocusVisuals = true,
                FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                TabIndex = index * 3 + 2,
                Content = btnContent
            };
            btnBindButton.Click += SortableTileButtonBind_Click;

            // ── Layout ────────────────────────────────────────────────────────────────
            // Custom tiles  (IsTrigger):
            //   Row 0  Tile main button  (click = select/move)
            //   Row 1  [Hide/Show]  [Delete]
            //   Row 2  [Button-bind label — full width]
            //
            // Built-in tiles:
            //   Row 0  Tile main button
            //   Row 1  [Hide/Show]  [Button-bind]

            var innerGrid = new Grid();
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            Grid.SetRow(mainButton, 0);
            innerGrid.Children.Add(mainButton);

            if (tile.IsTrigger)
            {
                // Row 1: Hide/Show | Delete
                visButton.CornerRadius    = new CornerRadius(0);
                visButton.BorderThickness = new Thickness(0, 1, 0.5, 0);

                btnBindButton.CornerRadius    = new CornerRadius(0, 0, 7, 7);
                btnBindButton.BorderThickness = new Thickness(0, 1, 0, 0);

                var deleteButton = new Button
                {
                    Tag = tile.Id,
                    Height = 28,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 200, 60, 60)),
                    BorderBrush = dividerBrush,
                    BorderThickness = new Thickness(0.5, 1, 0, 0),
                    CornerRadius = new CornerRadius(0),
                    UseSystemFocusVisuals = true,
                    FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                    FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    TabIndex = index * 4 + 2,
                    Content = new FontIcon { Glyph = "", FontSize = 12 }  // Trash
                };
                deleteButton.Click += SortableTileDelete_Click;

                // Row 1: Hide/Show (left) | Delete (right)
                var actionRow = new Grid { Height = 28 };
                actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(visButton,    0);
                Grid.SetColumn(deleteButton, 1);
                actionRow.Children.Add(visButton);
                actionRow.Children.Add(deleteButton);

                // Row 2: button mapping — full width, bottom-rounded
                btnBindButton.TabIndex = index * 4 + 3;

                innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                Grid.SetRow(actionRow,     1);
                Grid.SetRow(btnBindButton, 2);
                innerGrid.Children.Add(actionRow);
                innerGrid.Children.Add(btnBindButton);
            }
            else
            {
                // Row 1 for built-in tiles: Hide/Show | Button-bind (2/3 for readable label)
                var subRow = new Grid { Height = 28 };
                subRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                subRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                Grid.SetColumn(visButton,     0);
                Grid.SetColumn(btnBindButton, 1);
                subRow.Children.Add(visButton);
                subRow.Children.Add(btnBindButton);
                Grid.SetRow(subRow, 1);
                innerGrid.Children.Add(subRow);
            }
            // Outer border provides unified corner radius + selection highlight
            return new Border
            {
                Tag = tile.Id,
                BorderBrush = isSelected
                    ? new SolidColorBrush(Windows.UI.Colors.White)
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Child = innerGrid
            };
        }

        /// <summary>
        /// Handle delete button click on sortable tile for custom shortcuts
        /// </summary>
        private void SortableTileDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tileId))
                    return;

                if (!qsTileMap.TryGetValue(tileId, out var tile))
                    return;

                DeleteCustomShortcutTile(tile);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling sortable tile delete: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle tap on sortable tile - select, swap, or toggle visibility
        /// </summary>
        private void SortableTile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tileId))
                    return;

                if (!qsTileMap.TryGetValue(tileId, out var clickedTile))
                    return;

                if (qsSelectedTileForMove == null)
                {
                    // First tap: select tile - just update visuals, don't rebuild
                    qsSelectedTileForMove = clickedTile;
                    UpdateSelectedTileIndicator(clickedTile);
                    UpdateSortableGridVisuals(tileId);
                }
                else if (qsSelectedTileForMove.Id == clickedTile.Id)
                {
                    // Tap same tile: toggle visibility - need rebuild for eye icon change
                    clickedTile.IsVisible = !clickedTile.IsVisible;
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGridPreserveScroll(tileId);
                    Logger.Info($"Toggled visibility for {clickedTile.Name}: {clickedTile.IsVisible}");
                }
                else
                {
                    // Tap different tile: swap Order values - need rebuild for reorder
                    int tempOrder = qsSelectedTileForMove.Order;
                    qsSelectedTileForMove.Order = clickedTile.Order;
                    clickedTile.Order = tempOrder;

                    Logger.Info($"Swapped tile order: {qsSelectedTileForMove.Name} <-> {clickedTile.Name}");

                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGridPreserveScroll(tileId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling sortable tile click: {ex.Message}");
            }
        }

        /// <summary>
        /// Build sortable grid while preserving scroll position and focus
        /// </summary>
        private void BuildSortableGridPreserveScroll(string focusTileId = null)
        {
            // Save scroll position
            double scrollOffset = 0;
            if (QuickSettingsScrollViewer != null)
            {
                scrollOffset = QuickSettingsScrollViewer.VerticalOffset;
            }

            BuildSortableGrid();

            // Restore scroll position and focus after layout update
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                if (QuickSettingsScrollViewer != null && scrollOffset > 0)
                {
                    QuickSettingsScrollViewer.ChangeView(null, scrollOffset, null, true);
                }

                // Restore focus to the specified tile's main button
                if (!string.IsNullOrEmpty(focusTileId) && TileSortableGrid != null)
                {
                    foreach (var child in TileSortableGrid.Children)
                    {
                        if (!(child is Grid row)) continue;
                        foreach (var cell in row.Children)
                        {
                            // New split-tile: Border > Grid > main Button (row 0)
                            if (cell is Border border && border.Tag is string bid && bid == focusTileId)
                            {
                                if (border.Child is Grid ig && ig.Children.Count > 0 &&
                                    ig.Children[0] is Button mainBtn)
                                    mainBtn.Focus(FocusState.Programmatic);
                                return;
                            }
                            // Legacy plain Button fallback
                            if (cell is Button btn && btn.Tag is string id && id == focusTileId)
                            {
                                btn.Focus(FocusState.Programmatic);
                                return;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Update visual state of sortable tiles without rebuilding (for selection changes).
        /// Supports both the new split-tile (Border container) and legacy plain Button.
        /// </summary>
        private void UpdateSortableGridVisuals(string focusTileId = null)
        {
            if (TileSortableGrid == null) return;

            foreach (var child in TileSortableGrid.Children)
            {
                if (!(child is Grid row)) continue;
                foreach (var cell in row.Children)
                {
                    string id = null;
                    Border tileContainer = null;
                    Button mainBtn = null;

                    if (cell is Border border && border.Tag is string bid)
                    {
                        id = bid;
                        tileContainer = border;
                        // Border > Grid > main Button at row 0
                        if (border.Child is Grid ig && ig.Children.Count > 0)
                            mainBtn = ig.Children[0] as Button;
                    }
                    else if (cell is Button legacyBtn && legacyBtn.Tag is string legacyId)
                    {
                        id = legacyId;
                        mainBtn = legacyBtn;
                    }

                    if (id == null || !qsTileMap.TryGetValue(id, out var tile)) continue;

                    bool isSelected = qsSelectedTileForMove?.Id == id;
                    var bg = isSelected
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180))
                        : (tile.IsVisible
                            ? tileOffBrush
                            : new SolidColorBrush(Windows.UI.Color.FromArgb(128, 26, 28, 30)));

                    if (tileContainer != null)
                    {
                        tileContainer.BorderBrush = isSelected
                            ? new SolidColorBrush(Windows.UI.Colors.White)
                            : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92));
                        tileContainer.BorderThickness = new Thickness(isSelected ? 2 : 1);
                        if (mainBtn != null) mainBtn.Background = bg;
                    }
                    else if (mainBtn != null)
                    {
                        mainBtn.Background = bg;
                        mainBtn.BorderBrush = isSelected
                            ? new SolidColorBrush(Windows.UI.Colors.White)
                            : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92));
                        mainBtn.BorderThickness = new Thickness(isSelected ? 2 : 1);
                    }

                    if (!string.IsNullOrEmpty(focusTileId) && id == focusTileId)
                        mainBtn?.Focus(FocusState.Programmatic);
                }
            }
        }

        /// <summary>
        /// Update the selected tile indicator text
        /// </summary>
        private void UpdateSelectedTileIndicator(TileDefinition tile)
        {
            if (SelectedTileIndicator == null || SelectedTileText == null)
                return;

            if (tile == null)
            {
                SelectedTileIndicator.Visibility = Visibility.Collapsed;
                if (DeleteSelectedTileButton != null)
                    DeleteSelectedTileButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                SelectedTileIndicator.Visibility = Visibility.Visible;
                SelectedTileText.Text = $"Selected: {tile.Name}\nTap another tile to swap, or tap again to toggle visibility";

                // Show delete button for custom shortcuts (identified by having a CustomShortcut value)
                if (DeleteSelectedTileButton != null)
                {
                    DeleteSelectedTileButton.Visibility = !string.IsNullOrEmpty(tile.CustomShortcut)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Handle delete button click in the selected tile indicator
        /// </summary>
        private void DeleteSelectedTile_Click(object sender, RoutedEventArgs e)
        {
            if (qsSelectedTileForMove != null && !string.IsNullOrEmpty(qsSelectedTileForMove.CustomShortcut))
            {
                DeleteCustomShortcutTile(qsSelectedTileForMove);
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile
        /// </summary>
        private void DeleteCustomShortcutTile(TileDefinition tile)
        {
            try
            {
                // Remove from QuickSettingsConfig persistent storage first
                // Need to find the matching config tile by custom shortcut path
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var configTile = config.Tiles.FirstOrDefault(t =>
                    t.Type == QuickSettings.TileType.CustomShortcut &&
                    t.CustomShortcut == tile.CustomShortcut);
                if (configTile != null)
                {
                    config.RemoveTile(configTile.Id);
                }

                // Remove from local lists
                qsTileDefinitions.Remove(tile);
                qsTileMap.Remove(tile.Id);
                qsCustomShortcuts.Remove(tile);

                // Clear selection if we deleted the selected tile
                if (qsSelectedTileForMove?.Id == tile.Id)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                }

                BuildSortableGridPreserveScroll();
                // Don't rebuild main tiles here - they'll update when panel closes

                Logger.Info($"Deleted custom shortcut tile: {tile.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile (button click handler - legacy)
        /// </summary>
        private void DeleteCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string tileId)
                {
                    var tile = qsTileDefinitions.FirstOrDefault(t => t.Id == tileId);
                    if (tile != null)
                    {
                        DeleteCustomShortcutTile(tile);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        // ────────────────────────────────────────────────────────────────────────────
        // Controller button-binding Flyout  (dropdown-based, same pattern as keyboard shortcuts)
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Open the controller-button binding Flyout.
        /// User picks buttons from a dropdown — avoids the physical-button-press approach
        /// which dismisses the Game Bar overlay when RB/LB/etc. are pressed.
        /// </summary>
        private void OpenBtnBindFlyout(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button btn) || !(btn.Tag is string tagId)) return;
                string tileId = tagId.EndsWith("_btn") ? tagId.Substring(0, tagId.Length - 4) : tagId;
                if (!qsTileMap.TryGetValue(tileId, out var tile)) return;

                _btnBindFlyout?.Hide();
                _btnBindTileId = tileId;

                // Start with an EMPTY selection so a newly picked combo fully REPLACES the
                // previous one instead of being added on top of it. Pre-seeding the existing
                // binding made the default Start+Select on the built-in "Mode" tile sticky:
                // users who changed it ended up with Start+Select still baked into the saved
                // combo (it kept firing). Empty start = clean replace, Start+Select freed.
                _btnBindSelectedBits.Clear();

                // ── Build flyout UI ──────────────────────────────────────────────
                var panel = new StackPanel { Padding = new Thickness(16), MinWidth = 268 };

                panel.Children.Add(new TextBlock
                {
                    Text = tile.Name + " — Controller Combo",
                    FontSize = 13,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    Margin = new Thickness(0, 0, 0, 4)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = "Select 2 or more buttons, then Save",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 200, 200, 200)),
                    Margin = new Thickness(0, 0, 0, 10)
                });

                // Tag chips — show currently selected buttons
                _btnBindTagsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    MinHeight = 28,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                panel.Children.Add(_btnBindTagsPanel);

                // Dropdown: pick a button to add
                var combo = new ComboBox
                {
                    PlaceholderText = "+ Add button",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 14)
                };
                foreach (var (label, _) in ClawButtonDefs)
                    combo.Items.Add(label);
                combo.SelectionChanged += BtnBindCombo_SelectionChanged;
                panel.Children.Add(combo);

                // Button row: Save  Clear  Cancel
                var btnRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                _btnBindSaveButton = new Button
                {
                    Content = "Save",
                    IsEnabled = false,
                    Margin = new Thickness(3),
                    MinWidth = 72
                };
                var clearButton  = new Button { Content = "Clear",  Margin = new Thickness(3), MinWidth = 72 };
                var cancelButton = new Button { Content = "Cancel", Margin = new Thickness(3), MinWidth = 72 };

                _btnBindSaveButton.Click += (s, ev) => BtnBindCommit();
                clearButton.Click        += (s, ev) => BtnBindClear(tile);
                cancelButton.Click       += (s, ev) => _btnBindFlyout?.Hide();

                btnRow.Children.Add(_btnBindSaveButton);
                btnRow.Children.Add(clearButton);
                btnRow.Children.Add(cancelButton);
                panel.Children.Add(btnRow);

                _btnBindFlyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Top };
                _btnBindFlyout.ShowAt(btn);

                UpdateBtnBindTagsDisplay();
                UpdateBtnBindSaveEnabled();
                Logger.Info($"OpenBtnBindFlyout: opened for tile '{tile.Name}'");
            }
            catch (Exception ex)
            {
                Logger.Error($"OpenBtnBindFlyout: {ex.Message}");
            }
        }

        /// <summary>Save the selected combo to the tile and sync with the helper.</summary>
        private void BtnBindCommit()
        {
            try
            {
                if (!qsTileMap.TryGetValue(_btnBindTileId, out var tile)) return;

                uint maskToSave = 0;
                foreach (var bit in _btnBindSelectedBits)
                    maskToSave |= bit;

                if (maskToSave == 0) return;

                tile.ControllerHotkey = maskToSave.ToString();

                var configTile = QuickSettings.QuickSettingsConfig.Instance.GetTile(tile.Id);
                if (configTile != null)
                {
                    // Custom tile: persist via QuickSettingsConfig
                    configTile.ControllerHotkey = tile.ControllerHotkey;
                    QuickSettings.QuickSettingsConfig.Instance.Save();
                }
                else
                {
                    // Built-in tile: persist directly to LocalSettings
                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    settings.Values[$"QS_{tile.Id}_Hotkey"] = tile.ControllerHotkey;
                }

                Logger.Info($"BtnBindCommit: '{tile.Name}' -> 0x{maskToSave:X} ({XInputMaskToDisplayString(maskToSave)})");

                _btnBindFlyout?.Hide();
                BuildSortableGridPreserveScroll(_btnBindTileId);
                _ = SendTileHotkeysToHelper();
            }
            catch (Exception ex)
            {
                Logger.Error($"BtnBindCommit: {ex.Message}");
            }
        }

        /// <summary>Clear any existing hotkey binding for the tile.</summary>
        private void BtnBindClear(TileDefinition tile)
        {
            try
            {
                _btnBindSelectedBits.Clear();
                tile.ControllerHotkey = null;

                var configTile = QuickSettings.QuickSettingsConfig.Instance.GetTile(tile.Id);
                if (configTile != null)
                {
                    configTile.ControllerHotkey = null;
                    QuickSettings.QuickSettingsConfig.Instance.Save();
                }

                Logger.Info($"BtnBindClear: '{tile.Name}' hotkey cleared");
                _btnBindFlyout?.Hide();
                BuildSortableGridPreserveScroll(tile.Id);
                _ = SendTileHotkeysToHelper();
            }
            catch (Exception ex)
            {
                Logger.Error($"BtnBindClear: {ex.Message}");
            }
        }

        // ── Static utilities ──────────────────────────────────────────────────────

        private static uint ParseHotkeyMaskUInt(string hotkey)
        {
            if (string.IsNullOrEmpty(hotkey)) return 0;
            return uint.TryParse(hotkey, out uint m) ? m : 0u;
        }

        /// <summary>Convert an XInput button bitmask to a compact combo string for mini-tiles.</summary>
        private static string XInputMaskToDisplayString(uint mask)
        {
            if (mask == 0) return "--";
            var parts = new List<string>();
            // Modifier-style buttons first (rendered left of the combo)
            if ((mask & 0x0010) != 0) parts.Add("Strt");    // Start  → Strt
            if ((mask & 0x0020) != 0) parts.Add("Sel");     // Select → Sel
            // DPad
            if ((mask & 0x0001) != 0) parts.Add("d Up");
            if ((mask & 0x0002) != 0) parts.Add("d Dn");
            if ((mask & 0x0004) != 0) parts.Add("d Lt");
            if ((mask & 0x0008) != 0) parts.Add("d Rt");
            // Shoulder buttons
            if ((mask & 0x0100) != 0) parts.Add("LB");
            if ((mask & 0x0200) != 0) parts.Add("RB");
            // Thumbstick clicks
            if ((mask & 0x0040) != 0) parts.Add("LS");
            if ((mask & 0x0080) != 0) parts.Add("RS");
            // Face buttons
            if ((mask & 0x1000) != 0) parts.Add("A");
            if ((mask & 0x2000) != 0) parts.Add("B");
            if ((mask & 0x4000) != 0) parts.Add("X");
            if ((mask & 0x8000) != 0) parts.Add("Y");
            // Extra buttons
            if ((mask & 0x10000) != 0) parts.Add("M1");
            if ((mask & 0x20000) != 0) parts.Add("M2");
            return parts.Count > 0 ? string.Join("+", parts) : "--";
        }

        /// <summary>
        /// Serialize all tiles that have a <see cref="TileDefinition.ControllerHotkey"/> assigned
        /// into a JSON array and send it to the elevated helper via IPC.
        /// The helper registers the combos in its XInput polling loop.
        /// </summary>
        private async Task SendTileHotkeysToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var sb = new System.Text.StringBuilder("[");
                bool first = true;
                foreach (var tile in qsTileDefinitions)
                {
                    if (string.IsNullOrEmpty(tile.ControllerHotkey)) continue;
                    if (!uint.TryParse(tile.ControllerHotkey, out uint mask) || mask == 0) continue;

                    if (!first) sb.Append(",");
                    first = false;

                    string tileName = (tile.Name          ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string shortcut = (tile.CustomShortcut ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string param    = (tile.ActionParam    ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                    int    actionType = (int)tile.ActionType;

                    sb.Append($"{{\"id\":\"{tile.Id}\",\"name\":\"{tileName}\",\"mask\":{mask},\"actionType\":{actionType},\"shortcut\":\"{shortcut}\",\"param\":\"{param}\"}}");
                }
                sb.Append("]");

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "UpdateTileHotkeys", sb.ToString() }
                };
                await App.SendMessageAsync(request);
                Logger.Info("SendTileHotkeysToHelper: tile hotkey update sent to helper");
            }
            catch (Exception ex)
            {
                Logger.Warn($"SendTileHotkeysToHelper: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the user selects an item from the button-picker dropdown.
        /// Adds the button bit to the selection list (no duplicates), then resets the combo.
        /// </summary>
        private void BtnBindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox combo) || combo.SelectedIndex < 0) return;
            int idx = combo.SelectedIndex;
            if (idx >= ClawButtonDefs.Length) return;

            uint bit = ClawButtonDefs[idx].Bit;
            if (!_btnBindSelectedBits.Contains(bit))
            {
                _btnBindSelectedBits.Add(bit);
                UpdateBtnBindTagsDisplay();
                UpdateBtnBindSaveEnabled();
            }
            combo.SelectedIndex = -1;   // reset so the same button can be picked again after removal
        }

        /// <summary>Rebuild the chip row showing currently selected buttons.</summary>
        private void UpdateBtnBindTagsDisplay()
        {
            if (_btnBindTagsPanel == null) return;
            _btnBindTagsPanel.Children.Clear();

            foreach (var bit in _btnBindSelectedBits)
            {
                uint capBit = bit;

                var tagBorder = new Border
                {
                    Background   = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding      = new Thickness(6, 2, 6, 2),
                    Margin       = new Thickness(0, 0, 4, 0)
                };
                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };

                tagPanel.Children.Add(new TextBlock
                {
                    Text              = ControllerButtonDisplayName(capBit),
                    Foreground        = new SolidColorBrush(Windows.UI.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize          = 12
                });

                var removeBtn = new Button
                {
                    Content           = "x",
                    Padding           = new Thickness(4, 0, 4, 0),
                    Margin            = new Thickness(4, 0, 0, 0),
                    Background        = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    Foreground        = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 220, 220, 220)),
                    FontSize          = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };
                removeBtn.Click += (s, ev) =>
                {
                    _btnBindSelectedBits.Remove(capBit);
                    UpdateBtnBindTagsDisplay();
                    UpdateBtnBindSaveEnabled();
                };

                tagPanel.Children.Add(removeBtn);
                tagBorder.Child = tagPanel;
                _btnBindTagsPanel.Children.Add(tagBorder);
            }
        }

        /// <summary>Enable the Save button only when 2 or more buttons are selected.</summary>
        private void UpdateBtnBindSaveEnabled()
        {
            if (_btnBindSaveButton != null)
                _btnBindSaveButton.IsEnabled = _btnBindSelectedBits.Count >= 2;
        }

        /// <summary>Return the short display label for a button bit (e.g. 0x1000 -> "A").</summary>
        private static string ControllerButtonDisplayName(uint bit)
        {
            foreach (var (label, b) in ClawButtonDefs)
                if (b == bit) return label;
            return $"0x{bit:X}";
        }

        /// <summary>
        /// Check if a tile should be skipped based on hardware detection
        /// </summary>
        private bool ShouldSkipTile(TileDefinition tile)
        {
            // Skip Legion tiles if not detected
            if ((tile.Id == "LegionTouchpad" || tile.Id == "LegionLightMode" ||
                 tile.Id == "LegionDesktopControls" || tile.Id == "LegionRemapControls" ||
                 tile.Id == "LegionChargeLimit" || tile.Id == "LegionPowerLight") &&
                (legionGoDetected?.Value != true))
            {
                return true;
            }

            // TDP Mode tile is now available for all devices (Legion uses hardware presets, generic uses TDP values)

            // Skip Lossless Scaling tile if not installed
            if (tile.Id == "LosslessScaling" && (losslessScalingInstalled?.Value != true))
            {
                return true;
            }

            // MSI Claw detection: LegionGoDetected is true on BOTH Legion Go AND MSI Claw
            // (LegionManager enables its UI for MSI Claw controller remapping). Use DeviceDisplayName
            // which the helper sets to "MSI Claw" on MSI hardware and "Legion Go" / "Legion Go 2" etc. on Lenovo.
            bool isMsiClaw = deviceDisplayName?.Value?.IndexOf("Claw", StringComparison.OrdinalIgnoreCase) >= 0;

            // Skip Controller Emulation tile if helper has reported the backend as unavailable
            // (handheld-agnostic emulation requires LegionGo / GPD / similar, gated by the helper).
            // Also skip on MSI Claw — replaced by the dedicated MSIClawDesktopMode tile.
            if (tile.Id == "ControllerEmulation" &&
                ((controllerEmulationAvailable?.Value != true) || isMsiClaw))
            {
                return true;
            }

            // MSIClawDesktopMode tile is only relevant on MSI Claw.
            // Show it when MSI Claw is detected (controllerEmulationAvailable + not Legion Go).
            if (tile.Id == "MSIClawDesktopMode" && !isMsiClaw)
            {
                return true;
            }

            return false;
        }

    }
}

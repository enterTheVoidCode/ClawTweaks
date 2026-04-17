using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shared.Data;

namespace XboxGamingBarHelper.Sidebar
{
    internal class ProfilesTab : SidebarTab
    {
        private readonly StackPanel _contentPanel;
        private Border[] _focusableControls;

        // Info displays (non-focusable)
        private readonly TextBlock _currentProfileText;
        private readonly TextBlock _detectedGameText;

        // Per-game profile toggle
        private readonly Border _perGameToggleBorder;
        private readonly TextBlock _perGameToggleText;
        private bool _perGameState;

        // Saved profiles list
        private readonly StackPanel _profileListPanel;

        // Events
        internal event Action<bool> OnPerGameProfileChanged;

        internal ProfilesTab()
        {
            _contentPanel = new StackPanel();

            // ── SECTION: Current ──
            _contentPanel.Children.Add(CreateSectionHeader("Current"));

            // Current profile indicator (non-focusable card)
            var profileInfoBorder = new Border
            {
                Background = new SolidColorBrush(CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 3, 0, 3),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(CardBorderColor),
            };
            var profileInfoStack = new StackPanel();

            var profileLabel = new TextBlock
            {
                Text = "Active Profile",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtextColor),
                Margin = new Thickness(0, 0, 0, 4),
            };
            profileInfoStack.Children.Add(profileLabel);

            _currentProfileText = new TextBlock
            {
                Text = "Global",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(GreenColor),
            };
            profileInfoStack.Children.Add(_currentProfileText);
            profileInfoBorder.Child = profileInfoStack;
            _contentPanel.Children.Add(profileInfoBorder);

            // Detected game indicator (non-focusable card)
            var gameInfoBorder = new Border
            {
                Background = new SolidColorBrush(CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 3, 0, 3),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(CardBorderColor),
            };
            var gameInfoStack = new StackPanel();

            var gameLabel = new TextBlock
            {
                Text = "Detected Game",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtextColor),
                Margin = new Thickness(0, 0, 0, 4),
            };
            gameInfoStack.Children.Add(gameLabel);

            _detectedGameText = new TextBlock
            {
                Text = "None",
                FontSize = 14,
                Foreground = new SolidColorBrush(TextColor),
            };
            gameInfoStack.Children.Add(_detectedGameText);
            gameInfoBorder.Child = gameInfoStack;
            _contentPanel.Children.Add(gameInfoBorder);

            // ── SECTION: Settings ──
            _contentPanel.Children.Add(CreateSectionHeader("Settings"));

            // [0] Per-game profile toggle
            var perGameBorder = CreateControlCard(out var perGameContent);
            perGameContent.Children.Add(CreateToggleRow("Per-Game Profile", out _perGameToggleBorder, out _perGameToggleText));
            _contentPanel.Children.Add(perGameBorder);

            // ── SECTION: Saved Profiles ──
            _contentPanel.Children.Add(CreateSectionHeader("Saved Profiles"));

            _profileListPanel = new StackPanel();
            _contentPanel.Children.Add(_profileListPanel);

            _focusableControls = new Border[] { perGameBorder };
        }

        internal override StackPanel ContentPanel => _contentPanel;
        internal override Border[] FocusableControls => _focusableControls;

        internal override void AdjustLeft(int focusIndex) { }
        internal override void AdjustRight(int focusIndex) { }

        internal override void Activate(int focusIndex, ref bool isAdjusting)
        {
            if (focusIndex == 0) // Per-game toggle
            {
                _perGameState = !_perGameState;
                UpdateToggleVisual(_perGameToggleBorder, _perGameToggleText, _perGameState);
                OnPerGameProfileChanged?.Invoke(_perGameState);
            }
        }

        internal override void Refresh() { }

        internal override ControlType GetControlType(int focusIndex)
        {
            return focusIndex == 0 ? ControlType.Toggle : ControlType.Tile;
        }

        #region External Updates

        internal void UpdateCurrentProfile(string profileName)
        {
            _currentProfileText.Text = string.IsNullOrEmpty(profileName) || profileName == "global"
                ? "Global"
                : profileName;
        }

        internal void UpdateDetectedGame(string gameName)
        {
            _detectedGameText.Text = string.IsNullOrEmpty(gameName)
                ? "None"
                : gameName;
        }

        internal void UpdatePerGameProfile(bool enabled)
        {
            _perGameState = enabled;
            UpdateToggleVisual(_perGameToggleBorder, _perGameToggleText, enabled);
        }

        internal void UpdateSavedProfiles(IReadOnlyDictionary<GameId, GameProfile> profiles)
        {
            _profileListPanel.Children.Clear();

            if (profiles == null || profiles.Count == 0)
            {
                _profileListPanel.Children.Add(new TextBlock
                {
                    Text = "No saved profiles",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(SubtextColor),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                return;
            }

            foreach (var kvp in profiles)
            {
                var profile = kvp.Value;
                if (profile.IsGlobalProfile) continue;

                var card = new Border
                {
                    Background = new SolidColorBrush(CardColor),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 3, 0, 3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(profile.Use ? AccentColor : CardBorderColor),
                };

                var stack = new StackPanel();

                // ── Header: Game name ──
                var nameText = new TextBlock
                {
                    Text = profile.GameId.Name ?? "Unknown",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(TextColor),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                stack.Children.Add(nameText);

                // ── Row: Performance Mode + TDP ──
                var perfParts = new List<string>();
                string modeName = GetPerfModeName(profile.LegionPerformanceMode);
                if (modeName != null) perfParts.Add(modeName);
                perfParts.Add($"{profile.TDP}W");
                if (profile.TDP_DC.HasValue && profile.TDP_DC.Value != profile.TDP)
                    perfParts.Add($"DC {profile.TDP_DC}W");
                if (profile.TDPBoostEnabled) perfParts.Add("Boost");
                stack.Children.Add(CreateInfoRow(string.Join(" \u00B7 ", perfParts)));

                // ── Row: AutoTDP (conditional) ──
                if (profile.AutoTDPEnabled)
                {
                    var atdpParts = new List<string>();
                    atdpParts.Add($"AutoTDP {profile.AutoTDPTargetFPS}fps");
                    atdpParts.Add($"{profile.AutoTDPMinTDP}-{profile.AutoTDPMaxTDP}W");
                    stack.Children.Add(CreateInfoRow(string.Join(" \u00B7 ", atdpParts), GreenColor));
                }

                // ── Row: CPU + FPS ──
                var cpuParts = new List<string>();
                if (profile.CPUBoost) cpuParts.Add("CPU Boost");
                cpuParts.Add($"EPP {profile.CPUEPP}");
                if (profile.FPSLimit > 0) cpuParts.Add($"FPS {profile.FPSLimit}");
                if (profile.FPSLimit_DC.HasValue && profile.FPSLimit_DC.Value != profile.FPSLimit && profile.FPSLimit_DC.Value > 0)
                    cpuParts.Add($"DC FPS {profile.FPSLimit_DC}");
                stack.Children.Add(CreateInfoRow(string.Join(" \u00B7 ", cpuParts)));

                // ── Row: Display (conditional) ──
                var displayParts = new List<string>();
                if (!string.IsNullOrEmpty(profile.Resolution)) displayParts.Add(profile.Resolution);
                if (profile.RefreshRate.HasValue) displayParts.Add($"{profile.RefreshRate}Hz");
                if (profile.HDREnabled) displayParts.Add("HDR");
                if (displayParts.Count > 0)
                    stack.Children.Add(CreateInfoRow(string.Join(" \u00B7 ", displayParts)));

                card.Child = stack;
                _profileListPanel.Children.Add(card);
            }
        }

        private static string GetPerfModeName(int? legionMode)
        {
            if (!legionMode.HasValue) return null;
            switch (legionMode.Value)
            {
                case 1: return "Quiet";
                case 2: return "Balanced";
                case 3: return "Performance";
                case 255: return "Custom";
                default: return null;
            }
        }

        private static TextBlock CreateInfoRow(string text, Color? color = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(color ?? SubtextColor),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        #endregion
    }
}

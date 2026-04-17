using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shared.Enums;
using Windows.Foundation.Collections;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Property that tracks multiple foreground applications (up to 5).
    /// Returns pipe-separated paths. Skips blacklisted apps.
    /// </summary>
    internal class ForegroundAppProperty : HelperProperty<string, SystemManager>
    {
        private const char Separator = '|';
        private const int MaxApps = 5;

        public ForegroundAppProperty(SystemManager inManager) : base("", null, Function.ForegroundApp, inManager)
        {
        }

        /// <summary>
        /// Override AddValueSetContent to detect foreground apps when the widget requests it.
        /// This is called when helper receives a "Get" command from the widget.
        /// </summary>
        public override ValueSet AddValueSetContent(in ValueSet inValueSet)
        {
            // Detect foreground apps now, before returning the value
            var apps = DetectForegroundApps();
            var result = string.Join(Separator.ToString(), apps);
            Logger.Info($"ForegroundApp AddValueSetContent: detected {apps.Count} apps");
            value = result; // Update silently

            // Now call base to add the updated value to the response
            return base.AddValueSetContent(inValueSet);
        }

        /// <summary>
        /// Detects up to 5 foreground applications.
        /// Skips Game Bar, blacklisted apps, and currently detected games. Foreground app is first in list.
        /// </summary>
        private List<string> DetectForegroundApps()
        {
            var result = new List<string>();

            try
            {
                var processWindows = new Dictionary<int, ProcessWindow>();
                User32.GetOpenWindows(processWindows);

                if (processWindows.Count == 0)
                {
                    return result;
                }

                // Get blacklist from settings
                var blacklistProperty = SettingsManager.GetInstance()?.ProfileBlacklistPaths;
                var blacklistPaths = blacklistProperty?.GetPaths();
                Logger.Debug($"ForegroundApp: Blacklist has {blacklistPaths?.Count ?? 0} paths");

                // Get currently detected game to exclude it from the list
                var currentGamePath = manager?.RunningGame?.Value.GameId.Path ?? "";
                if (!string.IsNullOrEmpty(currentGamePath))
                {
                    Logger.Debug($"ForegroundApp: Will exclude current game {Path.GetFileName(currentGamePath)}");
                }

                string foregroundPath = null;
                var otherPaths = new List<string>();

                foreach (var pw in processWindows.Values)
                {
                    if (string.IsNullOrEmpty(pw.Path)) continue;

                    // Skip Game Bar (since that's what we're viewing the widget through)
                    bool isGameBar = (pw.ProcessName ?? "").IndexOf("GameBar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     pw.Path.IndexOf("GameBar", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isGameBar) continue;

                    // Skip blacklisted apps
                    if (blacklistProperty != null && blacklistProperty.ContainsPath(pw.Path))
                    {
                        Logger.Debug($"ForegroundApp: Skipping blacklisted app {Path.GetFileName(pw.Path)}");
                        continue;
                    }

                    // Skip apps that are already detected as games
                    if (!string.IsNullOrEmpty(currentGamePath) && pw.Path.Equals(currentGamePath, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"ForegroundApp: Skipping current game {Path.GetFileName(pw.Path)}");
                        continue;
                    }

                    // Skip duplicates
                    if (result.Contains(pw.Path) || otherPaths.Contains(pw.Path) || pw.Path == foregroundPath)
                    {
                        continue;
                    }

                    // Track foreground app separately to put it first
                    if (pw.IsForeground && foregroundPath == null)
                    {
                        foregroundPath = pw.Path;
                    }
                    else
                    {
                        otherPaths.Add(pw.Path);
                    }
                }

                // Build result: foreground first, then others
                if (!string.IsNullOrEmpty(foregroundPath))
                {
                    result.Add(foregroundPath);
                }

                foreach (var path in otherPaths)
                {
                    if (result.Count >= MaxApps) break;
                    result.Add(path);
                }

                Logger.Debug($"ForegroundApp detected {result.Count} apps: {string.Join(", ", result.Select(p => Path.GetFileName(p)))}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error detecting foreground apps: {ex.Message}");
            }

            return result;
        }
    }
}

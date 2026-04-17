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
        // Steam library cache: maps install directory paths to Steam App IDs
        // Cached on first use for performance
        private static Dictionary<string, string> _steamInstallCache;
        private static bool _steamCacheInitialized = false;

        /// <summary>
        /// Builds a cache of Steam game installations by parsing appmanifest files.
        /// Maps install directory paths to Steam App IDs.
        /// </summary>
        private static Dictionary<string, string> BuildSteamInstallCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Get all Steam library folders
                var libraryFolders = GetSteamLibraryFolders();

                foreach (var libraryPath in libraryFolders)
                {
                    var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamAppsPath))
                        continue;

                    // Parse each appmanifest_*.acf file
                    var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                    foreach (var manifestFile in manifestFiles)
                    {
                        try
                        {
                            var content = File.ReadAllText(manifestFile);

                            // Parse AppID
                            var appIdMatch = Regex.Match(content, @"""appid""\s+""(\d+)""");
                            if (!appIdMatch.Success) continue;
                            var appId = appIdMatch.Groups[1].Value;

                            // Parse install directory name
                            var installDirMatch = Regex.Match(content, @"""installdir""\s+""([^""]+)""");
                            if (!installDirMatch.Success) continue;
                            var installDir = installDirMatch.Groups[1].Value;

                            // Build full path to game install
                            var fullPath = Path.Combine(steamAppsPath, "common", installDir);
                            if (Directory.Exists(fullPath))
                            {
                                cache[fullPath] = appId;
                            }
                        }
                        catch
                        {
                            // Skip manifest files we can't parse
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to build Steam install cache: {ex.Message}");
            }

            return cache;
        }

        /// <summary>
        /// Gets all Steam library folder paths from libraryfolders.vdf.
        /// </summary>
        private static List<string> GetSteamLibraryFolders()
        {
            var folders = new List<string>();

            // Common Steam install locations
            var steamPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };

            string steamPath = null;
            foreach (var path in steamPaths)
            {
                if (Directory.Exists(path))
                {
                    steamPath = path;
                    break;
                }
            }

            if (steamPath == null)
            {
                return folders;
            }

            // Add the main Steam folder
            folders.Add(steamPath);

            // Parse libraryfolders.vdf for additional library locations
            var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersPath))
            {
                try
                {
                    var content = File.ReadAllText(libraryFoldersPath);

                    // Match "path" entries in the VDF file
                    var pathMatches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match match in pathMatches)
                    {
                        var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(libPath) && !folders.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                        {
                            folders.Add(libPath);
                        }
                    }
                }
                catch
                {
                    // Ignore errors parsing library folders
                }
            }

            return folders;
        }

        /// <summary>
        /// Gets the Steam App ID for a game executable by walking up the directory tree.
        /// </summary>
        private static string GetSteamAppIdFromPath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            // Initialize cache on first use
            if (!_steamCacheInitialized)
            {
                _steamInstallCache = BuildSteamInstallCache();
                _steamCacheInitialized = true;
            }

            if (_steamInstallCache == null || _steamInstallCache.Count == 0)
                return null;

            try
            {
                // Walk up the directory tree to find a cached Steam install path
                var searchDir = Path.GetDirectoryName(exePath);
                while (!string.IsNullOrEmpty(searchDir))
                {
                    if (_steamInstallCache.TryGetValue(searchDir, out var appId))
                    {
                        return appId;
                    }

                    var parent = Directory.GetParent(searchDir);
                    if (parent == null)
                        break;
                    searchDir = parent.FullName;
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Gets the local Steam icon path for a game by its Steam App ID.
        /// Looks in Steam's library cache folder for the game's icon.
        /// </summary>
        private static string GetSteamIconPath(string steamAppId)
        {
            if (string.IsNullOrEmpty(steamAppId))
                return null;

            // Steam caches icons locally - try common Steam install locations
            var steamPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };

            foreach (var steamPath in steamPaths)
            {
                // Steam stores assets in folders named by AppID
                var cacheFolder = Path.Combine(steamPath, "appcache", "librarycache", steamAppId);
                if (!Directory.Exists(cacheFolder))
                    continue;

                try
                {
                    // The icon is a small hash-named .jpg file (typically ~1KB)
                    // Look for the smallest jpg that isn't a known named file
                    var jpgFiles = Directory.GetFiles(cacheFolder, "*.jpg");
                    string iconPath = null;
                    long smallestSize = long.MaxValue;

                    foreach (var file in jpgFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        // Skip known large files
                        if (fileName == "header.jpg" || fileName == "library_600x900.jpg" ||
                            fileName == "library_hero.jpg" || fileName == "library_hero_blur.jpg")
                            continue;

                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Length < smallestSize && fileInfo.Length < 5000) // Icons are typically < 2KB
                        {
                            smallestSize = fileInfo.Length;
                            iconPath = file;
                        }
                    }

                    if (iconPath != null)
                        return iconPath;

                    // Fall back to logo.png (transparent game logo)
                    var logoPath = Path.Combine(cacheFolder, "logo.png");
                    if (File.Exists(logoPath))
                        return logoPath;
                }
                catch
                {
                    // Ignore errors and try next Steam path
                }
            }

            return null;
        }

        /// <summary>
        /// Loads the game icon for the current game and updates the UI.
        /// Uses helper-extracted icon if available, falls back to Steam lookup.
        /// Must be called from background thread - dispatches to UI thread.
        /// </summary>
        /// <param name="exePath">Path to the game executable</param>
        /// <param name="helperIconPath">Optional icon path from helper (extracted via Shell API)</param>
        private async void LoadCurrentGameIcon(string exePath, string helperIconPath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (CurrentGameIcon != null)
                    {
                        CurrentGameIcon.Source = null;
                        CurrentGameIcon.Visibility = Visibility.Collapsed;
                    }
                    if (LegionControllerProfileGameIcon != null)
                    {
                        LegionControllerProfileGameIcon.Source = null;
                        LegionControllerProfileGameIcon.Visibility = Visibility.Collapsed;
                    }
                });
                return;
            }

            try
            {
                Logger.Info($"LoadCurrentGameIcon: Starting for {exePath}");

                string iconPath = null;

                // Priority 1: Use helper-extracted icon if available
                if (!string.IsNullOrEmpty(helperIconPath) && File.Exists(helperIconPath))
                {
                    iconPath = helperIconPath;
                    Logger.Info($"LoadCurrentGameIcon: Using helper icon: {iconPath}");
                }
                else
                {
                    // Priority 2: Fall back to Steam icon lookup
                    var steamAppId = GetSteamAppIdFromPath(exePath);
                    if (!string.IsNullOrEmpty(steamAppId))
                    {
                        Logger.Info($"LoadCurrentGameIcon: Found Steam App ID {steamAppId}");
                        iconPath = GetSteamIconPath(steamAppId);
                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            Logger.Info($"LoadCurrentGameIcon: Using Steam icon: {iconPath}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(iconPath))
                {
                    Logger.Info($"LoadCurrentGameIcon: No icon found for {exePath}");
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        if (CurrentGameIcon != null)
                        {
                            CurrentGameIcon.Source = null;
                            CurrentGameIcon.Visibility = Visibility.Collapsed;
                        }
                        if (LegionControllerProfileGameIcon != null)
                        {
                            LegionControllerProfileGameIcon.Source = null;
                            LegionControllerProfileGameIcon.Visibility = Visibility.Collapsed;
                        }
                    });
                    return;
                }

                // Load icon and update UI - must be done on UI thread
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(iconPath);
                        using (var stream = await file.OpenAsync(FileAccessMode.Read))
                        {
                            var bitmapImage = new BitmapImage();
                            await bitmapImage.SetSourceAsync(stream);

                            if (CurrentGameIcon != null)
                            {
                                CurrentGameIcon.Source = bitmapImage;
                                CurrentGameIcon.Visibility = Visibility.Visible;
                            }
                            if (LegionControllerProfileGameIcon != null)
                            {
                                LegionControllerProfileGameIcon.Source = bitmapImage;
                                LegionControllerProfileGameIcon.Visibility = Visibility.Visible;
                            }
                            Logger.Info($"LoadCurrentGameIcon: Icon loaded successfully");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"LoadCurrentGameIcon: Failed to load bitmap - {ex.Message}");
                        if (CurrentGameIcon != null)
                        {
                            CurrentGameIcon.Source = null;
                            CurrentGameIcon.Visibility = Visibility.Collapsed;
                        }
                        if (LegionControllerProfileGameIcon != null)
                        {
                            LegionControllerProfileGameIcon.Source = null;
                            LegionControllerProfileGameIcon.Visibility = Visibility.Collapsed;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Info($"LoadCurrentGameIcon: Failed - {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the game icon for a saved profile.
        /// Checks helper-extracted cache first, falls back to Steam lookup.
        /// Returns a BitmapImage if found, null otherwise.
        /// </summary>
        private async Task<BitmapImage> LoadSavedProfileIconAsync(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            try
            {
                string iconPath = null;

                // Priority 1: Check helper-extracted icon cache
                var cachedIconPath = GetCachedIconPath(exePath);
                if (!string.IsNullOrEmpty(cachedIconPath) && File.Exists(cachedIconPath))
                {
                    iconPath = cachedIconPath;
                }
                else
                {
                    // Priority 2: Fall back to Steam icon lookup
                    var steamAppId = GetSteamAppIdFromPath(exePath);
                    if (!string.IsNullOrEmpty(steamAppId))
                    {
                        iconPath = GetSteamIconPath(steamAppId);
                    }
                }

                if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                    return null;

                // Load icon on UI thread using TaskCompletionSource
                var tcs = new TaskCompletionSource<BitmapImage>();

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(iconPath);
                        using (var stream = await file.OpenAsync(FileAccessMode.Read))
                        {
                            var bitmapImage = new BitmapImage();
                            await bitmapImage.SetSourceAsync(stream);
                            tcs.TrySetResult(bitmapImage);
                        }
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                });

                return await tcs.Task;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the cached icon path for an executable from the helper's icon cache.
        /// Uses the same MD5 hash-based naming scheme as the helper.
        /// </summary>
        private string GetCachedIconPath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            try
            {
                // Get the icon cache folder - helper stores icons in LocalCache, not LocalState
                var cacheFolder = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path,
                    "icons");

                if (!Directory.Exists(cacheFolder))
                    return null;

                // Generate cache filename using same algorithm as helper
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(exePath.ToLowerInvariant()));
                    var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                    var exeName = Path.GetFileNameWithoutExtension(exePath);
                    foreach (var c in Path.GetInvalidFileNameChars())
                    {
                        exeName = exeName.Replace(c, '_');
                    }
                    if (exeName.Length > 32)
                        exeName = exeName.Substring(0, 32);

                    var cacheFileName = $"{exeName}_{hashString.Substring(0, 8)}.png";
                    var cachePath = Path.Combine(cacheFolder, cacheFileName);

                    return File.Exists(cachePath) ? cachePath : null;
                }
            }
            catch
            {
                return null;
            }
        }

    }
}

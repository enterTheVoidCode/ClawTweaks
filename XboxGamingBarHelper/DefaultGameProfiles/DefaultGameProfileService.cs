using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using NLog;
using Shared.Data;

namespace XboxGamingBarHelper.DefaultGameProfiles
{
    /// <summary>
    /// Core service for managing Microsoft Default Game Profiles.
    /// Provides profile lookup, hardware-based selection, and auto-enable logic.
    /// </summary>
    internal class DefaultGameProfileService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, GameProfileEntry> _profiles;
        private readonly LegionGoVariant _hardwareVariant;
        private readonly string _primaryProfileKey;
        private string _forcedProfileKey;

        // Steam library cache: maps install directory paths to Steam App IDs
        private readonly Dictionary<string, string> _steamInstallCache;

        /// <summary>
        /// Number of profiles loaded from registry.
        /// </summary>
        public int ProfileCount => _profiles.Count;

        /// <summary>
        /// Detected hardware variant.
        /// </summary>
        public LegionGoVariant HardwareVariant => _hardwareVariant;

        /// <summary>
        /// Primary profile key for this hardware (OMNI or HORSEM4N).
        /// </summary>
        public string PrimaryProfileKey => _primaryProfileKey;

        /// <summary>
        /// Effective profile key (forced key if set, otherwise primary key).
        /// </summary>
        public string EffectiveProfileKey => _forcedProfileKey ?? _primaryProfileKey;

        /// <summary>
        /// Sets a forced profile key to use instead of the detected hardware key.
        /// Use null to clear and use detected hardware.
        /// </summary>
        public void SetForcedProfileKey(string key)
        {
            _forcedProfileKey = key;
            Logger.Info($"DefaultGameProfileService: Forced profile key set to '{key ?? "null"}'");
        }

        public DefaultGameProfileService()
        {
            // Detect hardware variant on construction
            _hardwareVariant = CpuDetector.DetectVariant();
            _primaryProfileKey = CpuDetector.GetProfileKey(_hardwareVariant);

            Logger.Info($"DefaultGameProfileService: Hardware variant = {_hardwareVariant}, Profile key = {_primaryProfileKey ?? "none"}");

            // Build Steam library cache from appmanifest files
            _steamInstallCache = BuildSteamInstallCache();
            Logger.Info($"DefaultGameProfileService: Cached {_steamInstallCache.Count} Steam games");

            // Load all profiles from registry
            _profiles = ProfileParser.LoadAllProfiles();
            Logger.Info($"DefaultGameProfileService: Loaded {_profiles.Count} games from registry");

            // Log some sample profiles for debugging
            var sampleProfiles = _profiles.Take(5).ToList();
            foreach (var kvp in sampleProfiles)
            {
                var hwModels = string.Join(", ", kvp.Value.Profiles.Keys);
                Logger.Debug($"  Sample profile: {kvp.Key} -> {kvp.Value.GameName} [{hwModels}]");
            }
        }

        /// <summary>
        /// Tries to find a matching profile for the given game executable or AUMID.
        /// Uses fallback order: exact hardware match -> other hardware -> ArmouryCrate -> GamingServices default.
        /// </summary>
        /// <param name="exePath">Full path to the game executable.</param>
        /// <param name="aumId">Optional AUMID for Xbox/MSIXVC games (e.g., "Microsoft.HalifaxBaseGame_8wekyb3d8bbwe!Game").</param>
        /// <param name="profile">Output profile if found.</param>
        /// <returns>True if a profile was found.</returns>
        public bool TryGetProfile(string exePath, out DefaultGameProfile profile, string aumId = null)
        {
            profile = default;

            // First, try to match by AUMID/PFN for Xbox games
            if (!string.IsNullOrEmpty(aumId))
            {
                var pfn = ExtractPackageFamilyName(aumId);
                if (!string.IsNullOrEmpty(pfn) && _profiles.TryGetValue(pfn.ToLowerInvariant(), out var aumEntry))
                {
                    Logger.Debug($"Matched by PFN: {pfn}");
                    profile = SelectBestProfile(aumEntry);
                    if (profile.IsValid())
                    {
                        return true;
                    }
                }
            }

            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            // Try Steam App ID matching first (most reliable for Steam games)
            var steamAppId = TryGetSteamAppId(exePath);
            if (!string.IsNullOrEmpty(steamAppId))
            {
                var steamKey = $"steam:{steamAppId}";
                if (_profiles.TryGetValue(steamKey, out var steamEntry))
                {
                    Logger.Debug($"Matched by Steam App ID: {steamAppId}");
                    profile = SelectBestProfile(steamEntry);
                    if (profile.IsValid())
                    {
                        return true;
                    }
                }
            }

            // Extract exe name from path
            var exeName = Path.GetFileName(exePath).ToLowerInvariant();

            // Also try without .exe extension
            var exeNameWithoutExt = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();

            // Look up by exe name
            GameProfileEntry entry;
            if (!_profiles.TryGetValue(exeName, out entry))
            {
                // Try without extension
                if (!_profiles.TryGetValue(exeNameWithoutExt + ".exe", out entry))
                {
                    // Try fuzzy match
                    entry = TryFuzzyMatch(exeNameWithoutExt);
                    if (!entry.IsValid())
                    {
                        return false;
                    }
                }
            }

            // Select best profile using fallback order
            profile = SelectBestProfile(entry);
            return profile.IsValid();
        }

        /// <summary>
        /// Extracts Package Family Name from an AUMID.
        /// E.g., "Microsoft.HalifaxBaseGame_8wekyb3d8bbwe!Game" -> "Microsoft.HalifaxBaseGame_8wekyb3d8bbwe"
        /// </summary>
        private static string ExtractPackageFamilyName(string aumId)
        {
            if (string.IsNullOrEmpty(aumId))
                return null;

            // AUMID format: PackageFamilyName!AppId (e.g., Microsoft.HalifaxBaseGame_8wekyb3d8bbwe!Game)
            var exclamationIndex = aumId.IndexOf('!');
            if (exclamationIndex > 0)
            {
                return aumId.Substring(0, exclamationIndex);
            }

            // Might already be just the PFN
            return aumId;
        }

        /// <summary>
        /// Builds a cache of Steam install directories to App IDs using Windows registry.
        /// Steam creates uninstall entries at HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {AppID}
        /// </summary>
        private static Dictionary<string, string> BuildSteamInstallCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using (var uninstallKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstallKey == null)
                    {
                        Logger.Debug("BuildSteamInstallCache: Uninstall registry key not found");
                        return cache;
                    }

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        // Steam apps have keys like "Steam App 1551360"
                        if (!subKeyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var appId = subKeyName.Substring(10); // Extract AppID after "Steam App "

                        try
                        {
                            using (var appKey = uninstallKey.OpenSubKey(subKeyName))
                            {
                                var installLocation = appKey?.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(installLocation))
                                {
                                    cache[installLocation] = appId;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Failed to read Steam app {appId}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to build Steam install cache: {ex.Message}");
            }

            sw.Stop();
            Logger.Debug($"BuildSteamInstallCache: Completed in {sw.ElapsedMilliseconds}ms with {cache.Count} games");
            return cache;
        }

        /// <summary>
        /// Tries to get Steam App ID from a game's executable path using the install cache.
        /// </summary>
        private string TryGetSteamAppId(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            try
            {
                // Walk up the directory tree to find a cached Steam install path
                var searchDir = Path.GetDirectoryName(exePath);
                while (!string.IsNullOrEmpty(searchDir))
                {
                    if (_steamInstallCache.TryGetValue(searchDir, out var appId))
                    {
                        Logger.Debug($"Found Steam App ID {appId} for path {searchDir}");
                        return appId;
                    }

                    var parent = Directory.GetParent(searchDir);
                    if (parent == null)
                        break;
                    searchDir = parent.FullName;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error looking up Steam App ID: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Tries a fuzzy match for the exe name.
        /// </summary>
        private GameProfileEntry TryFuzzyMatch(string exeNameWithoutExt)
        {
            // Minimum length to avoid false positives (e.g., "the" matching "davethediver")
            const int MIN_MATCH_LENGTH = 5;
            // Minimum length for acronym prefix matching (e.g., "lop" matching "lop-win64-shipping")
            const int MIN_ACRONYM_LENGTH = 3;

            // Try partial matching for common patterns
            foreach (var kvp in _profiles)
            {
                // Skip non-exe keys (PFNs and Steam keys) - only match against exe name keys
                // PFNs like "microsoft.624f8b84b80_8wekyb3d8bbwe" would incorrectly extract to just "microsoft"
                // via Path.GetFileNameWithoutExtension, causing false matches
                if (kvp.Key.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) ||
                    (kvp.Key.Contains("_") && !kvp.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var profileExeName = Path.GetFileNameWithoutExtension(kvp.Key).ToLowerInvariant();

                // Special case: exe name starts with short profile name (acronym matching)
                // E.g., "lop-win64-shipping" starts with "lop" (Lies of P)
                // This is more reliable than "contains" because it requires prefix match
                if (profileExeName.Length >= MIN_ACRONYM_LENGTH && profileExeName.Length < MIN_MATCH_LENGTH)
                {
                    if (exeNameWithoutExt.StartsWith(profileExeName) &&
                        (exeNameWithoutExt.Length == profileExeName.Length ||
                         !char.IsLetterOrDigit(exeNameWithoutExt[profileExeName.Length])))
                    {
                        // Must be followed by non-alphanumeric (like "-") or be exact match
                        Logger.Debug($"Fuzzy matched {exeNameWithoutExt} to {kvp.Key} (exe starts with acronym)");
                        return kvp.Value;
                    }
                    continue; // Skip short names for other matching
                }

                // Skip very short profile names to avoid false positives
                if (profileExeName.Length < MIN_MATCH_LENGTH)
                {
                    continue;
                }

                // Check if exe name contains or is contained in profile name
                // For "contains" match, require the contained string to be at least MIN_MATCH_LENGTH
                if (profileExeName.Contains(exeNameWithoutExt) && exeNameWithoutExt.Length >= MIN_MATCH_LENGTH)
                {
                    Logger.Debug($"Fuzzy matched {exeNameWithoutExt} to {kvp.Key} (profile contains exe)");
                    return kvp.Value;
                }

                if (exeNameWithoutExt.Contains(profileExeName))
                {
                    Logger.Debug($"Fuzzy matched {exeNameWithoutExt} to {kvp.Key} (exe contains profile)");
                    return kvp.Value;
                }
            }

            return default;
        }

        /// <summary>
        /// Selects the best profile from an entry using the fallback order.
        /// </summary>
        private DefaultGameProfile SelectBestProfile(GameProfileEntry entry)
        {
            if (!entry.IsValid() || entry.Profiles.Count == 0)
            {
                return default;
            }

            // Use effective key (forced or detected)
            var effectiveKey = EffectiveProfileKey;

            // Fallback order:
            // 1. Exact hardware match (OMNI for Z1, HORSEM4N for Z2, or forced key)
            if (!string.IsNullOrEmpty(effectiveKey) &&
                entry.Profiles.TryGetValue(effectiveKey, out var exactMatch))
            {
                Logger.Info($"Found exact profile match for {entry.GameName}: {effectiveKey}");
                return exactMatch;
            }

            // 2. Other hardware model (HORSEM4N if we're OMNI, OMNI if we're HORSEM4N)
            string fallbackKey = effectiveKey == "OMNI" ? "HORSEM4N" : "OMNI";
            if (entry.Profiles.TryGetValue(fallbackKey, out var otherHwMatch))
            {
                Logger.Info($"Using other hardware profile for {entry.GameName}: {fallbackKey} (device is {effectiveKey})");
                return otherHwMatch;
            }

            // 3. ArmouryCrate profile (ASUS ROG devices)
            if (entry.Profiles.TryGetValue("ArmouryCrate", out var armoury))
            {
                Logger.Info($"Using ArmouryCrate fallback for {entry.GameName}");
                return armoury;
            }

            // 4. GamingServices default profile
            if (entry.Profiles.TryGetValue("GamingServices", out var gsDefault))
            {
                Logger.Info($"Using GamingServices default for {entry.GameName}");
                return gsDefault;
            }

            if (entry.Profiles.TryGetValue("Default", out var defaultProfile))
            {
                Logger.Info($"Using Default profile for {entry.GameName}");
                return defaultProfile;
            }

            // 5. Any available profile
            if (entry.Profiles.Count > 0)
            {
                var first = entry.Profiles.First();
                Logger.Info($"Using first available profile for {entry.GameName}: {first.Key}");
                return first.Value;
            }

            return default;
        }

        /// <summary>
        /// Determines if default profile should be auto-enabled based on power state and user preference.
        /// </summary>
        /// <param name="userPreference">User's explicit preference: true=enabled, false=disabled, null=use default</param>
        /// <param name="isOnBattery">Whether device is currently on battery power</param>
        /// <returns>True if default profile should be enabled</returns>
        public bool ShouldAutoEnable(bool? userPreference, bool isOnBattery)
        {
            // User has explicitly set preference - respect it
            if (userPreference.HasValue)
            {
                Logger.Debug($"Using user preference for default profile: {userPreference.Value}");
                return userPreference.Value;
            }

            // Default: disabled on both AC and DC until user explicitly enables it
            Logger.Debug($"No user preference set, defaulting to disabled (isOnBattery={isOnBattery})");
            return false;
        }

        /// <summary>
        /// Gets all profile keys (exe names) that have default profiles.
        /// For debugging purposes.
        /// </summary>
        public IEnumerable<string> GetAllProfileKeys()
        {
            return _profiles.Keys;
        }

        /// <summary>
        /// Gets information about a specific game's profiles for debugging.
        /// </summary>
        public string GetProfileDebugInfo(string exeName)
        {
            if (_profiles.TryGetValue(exeName, out var entry))
            {
                var hwModels = string.Join(", ", entry.Profiles.Keys);
                var profiles = string.Join("; ", entry.Profiles.Select(p =>
                    $"{p.Key}: {p.Value.TDP}W/{p.Value.FrameCap}fps"));
                return $"{entry.GameName} [{hwModels}]: {profiles}";
            }
            return $"No profile found for {exeName}";
        }
    }
}

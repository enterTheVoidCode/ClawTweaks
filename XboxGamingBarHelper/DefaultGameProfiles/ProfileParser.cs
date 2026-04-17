using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NLog;
using Shared.Data;

namespace XboxGamingBarHelper.DefaultGameProfiles
{
    /// <summary>
    /// Parses Microsoft Default Game Profiles from Windows registry.
    /// Handles MSIXVC, Steam, and Ubisoft profile sources.
    /// Falls back to bundled profiles when registry keys don't exist (Xbox FSE never enabled).
    /// </summary>
    internal static class ProfileParser
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Registry paths for Microsoft Gaming Services game profiles
        private const string REGISTRY_BASE = @"SOFTWARE\Microsoft\GamingServices\GameProfiles";
        private const string MSIXVC_PATH = REGISTRY_BASE + @"\MSIXVC";
        private const string STEAM_PATH = REGISTRY_BASE + @"\Steam";
        private const string UBISOFT_PATH = REGISTRY_BASE + @"\Ubisoft";

        // Embedded resource name for bundled profiles
        private const string BUNDLED_PROFILES_RESOURCE = "XboxGamingBarHelper.Resources.DefaultGameProfiles.bundled_profiles.json";

        /// <summary>
        /// Loads all game profiles from registry into a lookup dictionary.
        /// Falls back to bundled profiles if registry keys don't exist.
        /// Key: lowercase exe name, Value: GameProfileEntry with all hardware variants.
        /// </summary>
        public static Dictionary<string, GameProfileEntry> LoadAllProfiles()
        {
            var profiles = new Dictionary<string, GameProfileEntry>(StringComparer.OrdinalIgnoreCase);
            int totalLoaded = 0;

            // Check if registry keys exist (Xbox FSE has been enabled)
            bool registryExists = RegistryProfilesExist();

            if (!registryExists)
            {
                Logger.Info("Registry game profile keys not found (Xbox FSE never enabled). Loading bundled profiles...");
                return LoadBundledProfiles();
            }

            try
            {
                var steamCount = LoadSteamProfiles(profiles);
                totalLoaded += steamCount;
                Logger.Info($"Loaded {steamCount} Steam profiles");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load Steam profiles: {ex.Message}");
            }

            try
            {
                var msixvcCount = LoadMsixvcProfiles(profiles);
                totalLoaded += msixvcCount;
                Logger.Info($"Loaded {msixvcCount} MSIXVC profiles");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load MSIXVC profiles: {ex.Message}");
            }

            try
            {
                var ubisoftCount = LoadUbisoftProfiles(profiles);
                totalLoaded += ubisoftCount;
                Logger.Info($"Loaded {ubisoftCount} Ubisoft profiles");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load Ubisoft profiles: {ex.Message}");
            }

            // If registry exists but no profiles found, fall back to bundled
            if (totalLoaded == 0)
            {
                Logger.Info("No profiles found in registry. Falling back to bundled profiles...");
                return LoadBundledProfiles();
            }

            Logger.Info($"Total default game profiles loaded from registry: {totalLoaded} games");
            return profiles;
        }

        /// <summary>
        /// Checks if the registry base path for game profiles exists.
        /// </summary>
        private static bool RegistryProfilesExist()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(REGISTRY_BASE))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads profiles from the bundled embedded resource JSON file.
        /// </summary>
        private static Dictionary<string, GameProfileEntry> LoadBundledProfiles()
        {
            var profiles = new Dictionary<string, GameProfileEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(BUNDLED_PROFILES_RESOURCE))
                {
                    if (stream == null)
                    {
                        Logger.Warn($"Bundled profiles resource not found: {BUNDLED_PROFILES_RESOURCE}");
                        return profiles;
                    }

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var json = reader.ReadToEnd();
                        using (var doc = JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;

                            // Check version
                            if (root.TryGetProperty("version", out var versionProp))
                            {
                                var version = versionProp.GetInt32();
                                Logger.Debug($"Bundled profiles version: {version}");
                            }

                            // Parse games array
                            if (root.TryGetProperty("games", out var gamesArray))
                            {
                                foreach (var gameElement in gamesArray.EnumerateArray())
                                {
                                    try
                                    {
                                        var entry = ParseBundledGameEntry(gameElement);
                                        if (entry.HasValue && entry.Value.IsValid())
                                        {
                                            AddProfileEntry(profiles, entry.Value);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Debug($"Failed to parse bundled game entry: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                Logger.Info($"Loaded {profiles.Count} game profiles from bundled resource");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load bundled profiles: {ex.Message}");
            }

            return profiles;
        }

        /// <summary>
        /// Parses a game entry from the bundled JSON format.
        /// </summary>
        private static GameProfileEntry? ParseBundledGameEntry(JsonElement element)
        {
            var entry = new GameProfileEntry
            {
                Identifiers = new List<GameIdentifier>(),
                Profiles = new Dictionary<string, DefaultGameProfile>(StringComparer.OrdinalIgnoreCase)
            };

            // Get game name
            if (element.TryGetProperty("gameName", out var gameNameProp))
            {
                entry.GameName = gameNameProp.GetString();
            }

            if (string.IsNullOrEmpty(entry.GameName))
            {
                return null;
            }

            // Parse identifiers
            if (element.TryGetProperty("identifiers", out var identifiersProp))
            {
                foreach (var idElement in identifiersProp.EnumerateArray())
                {
                    var identifier = new GameIdentifier();

                    if (idElement.TryGetProperty("platform", out var platformProp))
                    {
                        identifier.Platform = platformProp.GetString();
                    }

                    if (idElement.TryGetProperty("gameUid", out var uidProp))
                    {
                        identifier.GameUid = uidProp.GetString();
                    }

                    entry.Identifiers.Add(identifier);
                }
            }

            // Extract Steam App ID from identifiers
            string steamAppId = null;
            foreach (var id in entry.Identifiers)
            {
                if (id.Platform == "Steam" && !string.IsNullOrEmpty(id.GameUid))
                {
                    steamAppId = id.GameUid;
                    break;
                }
            }

            // Parse profiles
            if (element.TryGetProperty("profiles", out var profilesProp))
            {
                foreach (var profileElement in profilesProp.EnumerateArray())
                {
                    try
                    {
                        var profile = new DefaultGameProfile
                        {
                            GameName = entry.GameName,
                            SteamAppId = steamAppId
                        };

                        if (profileElement.TryGetProperty("hardwareModel", out var hwModelProp))
                        {
                            profile.HardwareModel = hwModelProp.GetString();
                        }

                        if (profileElement.TryGetProperty("provider", out var providerProp))
                        {
                            profile.Provider = providerProp.GetString();
                        }

                        if (profileElement.TryGetProperty("tdp", out var tdpProp))
                        {
                            profile.TDP = tdpProp.GetInt32();
                        }

                        if (profileElement.TryGetProperty("frameCap", out var frameCapProp))
                        {
                            if (frameCapProp.ValueKind == JsonValueKind.Number)
                            {
                                profile.FrameCap = frameCapProp.GetInt32();
                            }
                            else if (frameCapProp.ValueKind == JsonValueKind.Null)
                            {
                                profile.FrameCap = null;
                            }
                        }

                        if (profileElement.TryGetProperty("resolutionCap", out var resCapProp))
                        {
                            profile.ResolutionCap = resCapProp.GetString();
                        }

                        if (profileElement.TryGetProperty("controlMode", out var controlProp))
                        {
                            profile.ControlMode = controlProp.GetString();
                        }

                        // Add profile using hardware model as key
                        var hwModel = profile.HardwareModel ?? profile.Provider ?? "Default";
                        if (profile.TDP > 0 || !entry.Profiles.ContainsKey(hwModel))
                        {
                            entry.Profiles[hwModel] = profile;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to parse bundled profile for {entry.GameName}: {ex.Message}");
                    }
                }
            }

            return entry;
        }

        /// <summary>
        /// Loads Steam profiles from the ALL_GAMES registry value (JSON array).
        /// </summary>
        private static int LoadSteamProfiles(Dictionary<string, GameProfileEntry> profiles)
        {
            int count = 0;

            using (var key = Registry.LocalMachine.OpenSubKey(STEAM_PATH))
            {
                if (key == null)
                {
                    Logger.Debug("Steam profiles registry key not found");
                    return 0;
                }

                var allGamesJson = key.GetValue("ALL_GAMES") as string;
                if (string.IsNullOrEmpty(allGamesJson))
                {
                    Logger.Debug("Steam ALL_GAMES value is empty");
                    return 0;
                }

                try
                {
                    using (var doc = JsonDocument.Parse(allGamesJson))
                    {
                        foreach (var gameElement in doc.RootElement.EnumerateArray())
                        {
                            try
                            {
                                var entry = ParseGameEntry(gameElement, "Steam");
                                if (entry.HasValue && entry.Value.IsValid())
                                {
                                    AddProfileEntry(profiles, entry.Value);
                                    count++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to parse Steam game entry: {ex.Message}");
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Logger.Error($"Failed to parse Steam ALL_GAMES JSON: {ex.Message}");
                }
            }

            return count;
        }

        /// <summary>
        /// Loads MSIXVC (Xbox/Game Pass) profiles from registry subkeys.
        /// </summary>
        private static int LoadMsixvcProfiles(Dictionary<string, GameProfileEntry> profiles)
        {
            int count = 0;

            using (var key = Registry.LocalMachine.OpenSubKey(MSIXVC_PATH))
            {
                if (key == null)
                {
                    Logger.Debug("MSIXVC profiles registry key not found");
                    return 0;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            if (subKey == null) continue;

                            var profileJson = subKey.GetValue("Profile") as string;
                            if (string.IsNullOrEmpty(profileJson)) continue;

                            using (var doc = JsonDocument.Parse(profileJson))
                            {
                                var entry = ParseGameEntry(doc.RootElement, "MSIXVC");
                                if (entry.HasValue && entry.Value.IsValid())
                                {
                                    AddProfileEntry(profiles, entry.Value);
                                    count++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to parse MSIXVC profile {subKeyName}: {ex.Message}");
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Loads Ubisoft profiles from registry subkeys.
        /// </summary>
        private static int LoadUbisoftProfiles(Dictionary<string, GameProfileEntry> profiles)
        {
            int count = 0;

            using (var key = Registry.LocalMachine.OpenSubKey(UBISOFT_PATH))
            {
                if (key == null)
                {
                    Logger.Debug("Ubisoft profiles registry key not found");
                    return 0;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            if (subKey == null) continue;

                            var profileJson = subKey.GetValue("Profile") as string;
                            if (string.IsNullOrEmpty(profileJson)) continue;

                            using (var doc = JsonDocument.Parse(profileJson))
                            {
                                var entry = ParseGameEntry(doc.RootElement, "Ubisoft");
                                if (entry.HasValue && entry.Value.IsValid())
                                {
                                    AddProfileEntry(profiles, entry.Value);
                                    count++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to parse Ubisoft profile {subKeyName}: {ex.Message}");
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Parses a game entry from JSON element.
        /// Expected format:
        /// {
        ///   "gameName": "Game Title",
        ///   "gameIdentifiers": [{"gamePlatform": "Steam", "gameUid": "12345"}],
        ///   "profiles": [{"provider": "ArmouryCrate", "hardwareModels": ["OMNI"], "settings": "base64..."}]
        /// }
        /// </summary>
        private static GameProfileEntry? ParseGameEntry(JsonElement element, string defaultPlatform)
        {
            var entry = new GameProfileEntry
            {
                Identifiers = new List<GameIdentifier>(),
                Profiles = new Dictionary<string, DefaultGameProfile>(StringComparer.OrdinalIgnoreCase)
            };

            // Get game name
            if (element.TryGetProperty("gameName", out var gameNameProp))
            {
                entry.GameName = gameNameProp.GetString();
            }

            if (string.IsNullOrEmpty(entry.GameName))
            {
                return null;
            }

            // Parse game identifiers
            if (element.TryGetProperty("gameIdentifiers", out var identifiersProp))
            {
                foreach (var idElement in identifiersProp.EnumerateArray())
                {
                    var identifier = new GameIdentifier
                    {
                        Platform = defaultPlatform
                    };

                    if (idElement.TryGetProperty("gamePlatform", out var platformProp))
                    {
                        identifier.Platform = platformProp.GetString() ?? defaultPlatform;
                    }

                    if (idElement.TryGetProperty("gameUid", out var uidProp))
                    {
                        identifier.GameUid = uidProp.GetString();
                    }

                    // For Steam, try to extract exe name from common patterns
                    if (identifier.Platform == "Steam" && !string.IsNullOrEmpty(identifier.GameUid))
                    {
                        // Steam identifiers might include executable path hints
                        identifier.ExeName = ExtractExeNameFromGameName(entry.GameName);
                    }

                    entry.Identifiers.Add(identifier);
                }
            }

            // Extract Steam App ID from identifiers (for icon loading)
            string steamAppId = null;
            foreach (var id in entry.Identifiers)
            {
                if (id.Platform == "Steam" && !string.IsNullOrEmpty(id.GameUid))
                {
                    steamAppId = id.GameUid;
                    break;
                }
            }

            // Parse profiles for different hardware models
            if (element.TryGetProperty("profiles", out var profilesProp))
            {
                foreach (var profileElement in profilesProp.EnumerateArray())
                {
                    try
                    {
                        var provider = "";
                        if (profileElement.TryGetProperty("provider", out var providerProp))
                        {
                            provider = providerProp.GetString() ?? "";
                        }

                        // Get hardware models this profile applies to
                        var hardwareModels = new List<string>();
                        if (profileElement.TryGetProperty("hardwareModels", out var hwModelsProp))
                        {
                            foreach (var modelElement in hwModelsProp.EnumerateArray())
                            {
                                var model = modelElement.GetString();
                                if (!string.IsNullOrEmpty(model))
                                {
                                    hardwareModels.Add(model);
                                }
                            }
                        }

                        // Decode settings
                        if (profileElement.TryGetProperty("settings", out var settingsProp))
                        {
                            var settingsBase64 = settingsProp.GetString();
                            if (!string.IsNullOrEmpty(settingsBase64))
                            {
                                var profile = DecodeSettings(settingsBase64, entry.GameName, provider, steamAppId);

                                // Add profile for each hardware model
                                // Only overwrite if new profile has TDP or existing profile doesn't have TDP
                                // This prevents GamingServices (resolution-only) from overwriting ArmouryCrate (TDP+FPS)
                                foreach (var hwModel in hardwareModels)
                                {
                                    profile.HardwareModel = hwModel;
                                    if (profile.IsValid() ||
                                        !entry.Profiles.TryGetValue(hwModel, out var existing) ||
                                        !existing.IsValid())
                                    {
                                        entry.Profiles[hwModel] = profile;
                                    }
                                }

                                // If no hardware models specified, use provider as key
                                if (hardwareModels.Count == 0 && !string.IsNullOrEmpty(provider))
                                {
                                    entry.Profiles[provider] = profile;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to parse profile for {entry.GameName}: {ex.Message}");
                    }
                }
            }

            return entry;
        }

        /// <summary>
        /// Decodes base64-encoded settings JSON to a DefaultGameProfile.
        /// Expected format: {"controlMode":"Gamepad","TDP":17,"frameCap":"60","resolutionCap":"720P"}
        /// </summary>
        public static DefaultGameProfile DecodeSettings(string base64Settings, string gameName, string provider, string steamAppId = null)
        {
            var profile = new DefaultGameProfile
            {
                GameName = gameName,
                Provider = provider,
                SteamAppId = steamAppId
            };

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Settings));
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    // TDP (required)
                    if (root.TryGetProperty("TDP", out var tdpProp))
                    {
                        if (tdpProp.ValueKind == JsonValueKind.Number)
                        {
                            profile.TDP = tdpProp.GetInt32();
                        }
                        else if (tdpProp.ValueKind == JsonValueKind.String)
                        {
                            int.TryParse(tdpProp.GetString(), out var tdp);
                            profile.TDP = tdp;
                        }
                    }

                    // Frame cap (optional)
                    if (root.TryGetProperty("frameCap", out var fpsCapProp))
                    {
                        if (fpsCapProp.ValueKind == JsonValueKind.Number)
                        {
                            profile.FrameCap = fpsCapProp.GetInt32();
                        }
                        else if (fpsCapProp.ValueKind == JsonValueKind.String)
                        {
                            var fpsStr = fpsCapProp.GetString();
                            if (!string.IsNullOrEmpty(fpsStr) && int.TryParse(fpsStr, out var fps) && fps > 0)
                            {
                                profile.FrameCap = fps;
                            }
                        }
                    }

                    // Resolution cap (optional)
                    if (root.TryGetProperty("resolutionCap", out var resCapProp))
                    {
                        profile.ResolutionCap = resCapProp.GetString();
                    }

                    // Control mode (optional)
                    if (root.TryGetProperty("controlMode", out var controlProp))
                    {
                        profile.ControlMode = controlProp.GetString();
                    }
                }

                Logger.Debug($"Decoded profile for {gameName}: TDP={profile.TDP}W, FPS={profile.FrameCap}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to decode settings for {gameName}: {ex.Message}");
            }

            return profile;
        }

        /// <summary>
        /// Adds a profile entry to the dictionary, keyed by potential exe names and Package Family Names.
        /// </summary>
        private static void AddProfileEntry(Dictionary<string, GameProfileEntry> profiles, GameProfileEntry entry)
        {
            // Generate potential exe names from game name
            var keys = GeneratePotentialExeNames(entry.GameName);

            // Also add identifiers' exe names, Package Family Names, and Steam App IDs
            foreach (var identifier in entry.Identifiers)
            {
                if (!string.IsNullOrEmpty(identifier.ExeName))
                {
                    keys.Add(identifier.ExeName.ToLowerInvariant());
                }

                // Add Package Family Name for MSIXVC games (Xbox Game Pass)
                // e.g., "Microsoft.HalifaxBaseGame_8wekyb3d8bbwe"
                if (identifier.Platform == "MSIXVC" && !string.IsNullOrEmpty(identifier.GameUid))
                {
                    keys.Add(identifier.GameUid.ToLowerInvariant());
                }

                // Add Steam App ID for Steam games
                // e.g., "steam:1868140" for Dave the Diver
                if (identifier.Platform == "Steam" && !string.IsNullOrEmpty(identifier.GameUid))
                {
                    keys.Add($"steam:{identifier.GameUid}");
                }
            }

            // Add entry for each potential key (exe name or PFN)
            foreach (var key in keys)
            {
                if (!profiles.ContainsKey(key))
                {
                    profiles[key] = entry;
                }
                else
                {
                    // Merge profiles if entry already exists
                    var existing = profiles[key];
                    foreach (var kvp in entry.Profiles)
                    {
                        if (!existing.Profiles.ContainsKey(kvp.Key))
                        {
                            existing.Profiles[kvp.Key] = kvp.Value;
                        }
                    }
                    profiles[key] = existing;
                }
            }
        }

        /// <summary>
        /// Generates potential executable names from a game title.
        /// </summary>
        private static HashSet<string> GeneratePotentialExeNames(string gameName)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(gameName))
                return names;

            // Clean game name
            var cleanName = gameName
                .Replace(":", "")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace("!", "")
                .Replace("?", "")
                .Replace("&", "and")
                .Replace("  ", " ")
                .Trim();

            // Strip common edition suffixes to get core game name
            var coreName = StripEditionSuffixes(cleanName);

            // Generate names for both full name and core name
            GenerateExeNamesFromTitle(names, cleanName);
            if (coreName != cleanName)
            {
                GenerateExeNamesFromTitle(names, coreName);
            }

            return names;
        }

        /// <summary>
        /// Strips common edition/bundle suffixes from game names.
        /// E.g., "Lies of P Overture Bundle" -> "Lies of P"
        /// </summary>
        private static string StripEditionSuffixes(string gameName)
        {
            // Common edition/bundle suffixes (order matters - longer matches first)
            var suffixes = new[]
            {
                "Digital Premium Edition",
                "Game of the Year Edition",
                "Definitive Edition",
                "Ultimate Edition",
                "Deluxe Edition",
                "Premium Edition",
                "Gold Edition",
                "Complete Edition",
                "Enhanced Edition",
                "Overture Bundle",
                "Special Edition",
                "Anniversary Edition",
                "Remastered",
                "Remake",
                "GOTY",
                "(PC)",
                "PC"
            };

            var result = gameName;
            foreach (var suffix in suffixes)
            {
                if (result.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length - 1).Trim();
                }
                else if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length).Trim();
                }
            }

            return result;
        }

        /// <summary>
        /// Generates exe name variations from a title string.
        /// </summary>
        private static void GenerateExeNamesFromTitle(HashSet<string> names, string title)
        {
            if (string.IsNullOrEmpty(title))
                return;

            var commonWords = new HashSet<string> { "the", "a", "an", "of", "and", "in", "on", "to", "for" };

            // Basic variations
            names.Add(title.ToLowerInvariant().Replace(" ", "") + ".exe");  // NoSpaces.exe
            names.Add(title.ToLowerInvariant().Replace(" ", "_") + ".exe"); // Under_scores.exe
            names.Add(title.ToLowerInvariant().Replace(" ", "-") + ".exe"); // Hy-phens.exe

            // First word only (common pattern) - but avoid short/common words
            var firstWord = title.Split(' ')[0].ToLowerInvariant();
            if (firstWord.Length >= 4 && !commonWords.Contains(firstWord))
            {
                names.Add(firstWord + ".exe");
            }

            // CamelCase version
            var words = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                var camelCase = string.Join("", words.Select(w =>
                    char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "")));
                names.Add(camelCase + ".exe");
            }

            // Common suffix patterns
            if (!title.EndsWith("Game", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(title.ToLowerInvariant().Replace(" ", "") + "game.exe");
            }

            // Acronym/initials version (e.g., "Battlefield 6" -> "bf6.exe", "Grand Theft Auto V" -> "gtav.exe")
            var acronym = GenerateAcronym(title, commonWords);
            if (!string.IsNullOrEmpty(acronym) && acronym.Length >= 2)
            {
                names.Add(acronym + ".exe");
            }
        }

        /// <summary>
        /// Generates an acronym from a game title.
        /// E.g., "Battlefield 6" -> "bf6", "Grand Theft Auto V" -> "gtav", "Call of Duty" -> "cod"
        /// Note: Only skips articles (the, a, an), NOT prepositions like "of" since they're
        /// commonly included in game acronyms (COD, LOL, LOP, GOW).
        /// </summary>
        private static string GenerateAcronym(string title, HashSet<string> skipWords)
        {
            // For acronyms, only skip articles - include prepositions like "of"
            // COD = Call of Duty, LOL = League of Legends, LOP = Lies of P, GOW = God of War
            var acronymSkipWords = new HashSet<string> { "the", "a", "an" };

            var words = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var acronym = new StringBuilder();

            foreach (var word in words)
            {
                var lowerWord = word.ToLowerInvariant();

                // Skip only articles (the, a, an) unless it's the only word
                if (acronymSkipWords.Contains(lowerWord) && words.Length > 1)
                {
                    continue;
                }

                // Check if it's a number or Roman numeral
                if (IsNumberOrRomanNumeral(word, out var numericValue))
                {
                    // Append the number (convert Roman numerals to Arabic)
                    acronym.Append(numericValue);
                }
                else if (word.Length > 0)
                {
                    // Append first letter
                    acronym.Append(char.ToLowerInvariant(word[0]));
                }
            }

            return acronym.ToString();
        }

        /// <summary>
        /// Checks if a word is a number or Roman numeral and returns its numeric value.
        /// </summary>
        private static bool IsNumberOrRomanNumeral(string word, out string numericValue)
        {
            numericValue = null;

            // Check if it's already a number
            if (int.TryParse(word, out var num))
            {
                numericValue = num.ToString();
                return true;
            }

            // Check for Roman numerals
            var romanNumerals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "I", "1" }, { "II", "2" }, { "III", "3" }, { "IV", "4" }, { "V", "5" },
                { "VI", "6" }, { "VII", "7" }, { "VIII", "8" }, { "IX", "9" }, { "X", "10" },
                { "XI", "11" }, { "XII", "12" }, { "XIII", "13" }, { "XIV", "14" }, { "XV", "15" }
            };

            if (romanNumerals.TryGetValue(word, out var arabic))
            {
                numericValue = arabic;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extracts potential exe name from game name for Steam games.
        /// </summary>
        private static string ExtractExeNameFromGameName(string gameName)
        {
            if (string.IsNullOrEmpty(gameName))
                return null;

            // Generate the most likely exe name
            var cleanName = gameName
                .Replace(":", "")
                .Replace("'", "")
                .Replace("\"", "")
                .ToLowerInvariant()
                .Replace(" ", "")
                .Trim();

            return cleanName + ".exe";
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NLog;

namespace XboxGamingBarHelper.DefaultGameProfiles
{
    /// <summary>
    /// Utility to export registry game profiles to a bundled JSON file.
    /// Run this on a system that has Xbox FSE enabled to generate the bundled profiles.
    /// </summary>
    internal static class BundledProfileExporter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Registry paths for Microsoft Gaming Services game profiles
        private const string REGISTRY_BASE = @"SOFTWARE\Microsoft\GamingServices\GameProfiles";
        private const string MSIXVC_PATH = REGISTRY_BASE + @"\MSIXVC";
        private const string STEAM_PATH = REGISTRY_BASE + @"\Steam";
        private const string UBISOFT_PATH = REGISTRY_BASE + @"\Ubisoft";

        /// <summary>
        /// Data structure for serializing bundled profiles.
        /// </summary>
        public class BundledProfileData
        {
            public int Version { get; set; } = 1;
            public DateTime ExportedAt { get; set; }
            public string ExportedFrom { get; set; }
            public List<BundledGameEntry> Games { get; set; } = new List<BundledGameEntry>();
        }

        public class BundledGameEntry
        {
            public string GameName { get; set; }
            public List<BundledIdentifier> Identifiers { get; set; } = new List<BundledIdentifier>();
            public List<BundledProfile> Profiles { get; set; } = new List<BundledProfile>();
        }

        public class BundledIdentifier
        {
            public string Platform { get; set; }
            public string GameUid { get; set; }
        }

        public class BundledProfile
        {
            public string HardwareModel { get; set; }
            public string Provider { get; set; }
            public int TDP { get; set; }
            public int? FrameCap { get; set; }
            public string ResolutionCap { get; set; }
            public string ControlMode { get; set; }
        }

        /// <summary>
        /// Exports all registry profiles to a JSON file.
        /// </summary>
        /// <param name="outputPath">Path to write the JSON file.</param>
        /// <returns>Number of games exported.</returns>
        public static int ExportToJson(string outputPath)
        {
            var data = new BundledProfileData
            {
                ExportedAt = DateTime.UtcNow,
                ExportedFrom = Environment.MachineName
            };

            var uniqueGames = new Dictionary<string, BundledGameEntry>(StringComparer.OrdinalIgnoreCase);

            // Export Steam profiles
            ExportSteamProfiles(uniqueGames);

            // Export MSIXVC profiles
            ExportMsixvcProfiles(uniqueGames);

            // Export Ubisoft profiles
            ExportUbisoftProfiles(uniqueGames);

            data.Games = uniqueGames.Values.ToList();

            // Write JSON with indentation for readability
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(outputPath, json, Encoding.UTF8);

            Logger.Info($"Exported {data.Games.Count} game profiles to {outputPath}");
            return data.Games.Count;
        }

        private static void ExportSteamProfiles(Dictionary<string, BundledGameEntry> uniqueGames)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(STEAM_PATH))
                {
                    if (key == null)
                    {
                        Logger.Debug("Steam profiles registry key not found");
                        return;
                    }

                    var allGamesJson = key.GetValue("ALL_GAMES") as string;
                    if (string.IsNullOrEmpty(allGamesJson))
                    {
                        return;
                    }

                    using (var doc = JsonDocument.Parse(allGamesJson))
                    {
                        foreach (var gameElement in doc.RootElement.EnumerateArray())
                        {
                            try
                            {
                                var entry = ParseGameElement(gameElement, "Steam");
                                if (entry != null)
                                {
                                    MergeEntry(uniqueGames, entry);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Debug($"Failed to parse Steam game entry: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to export Steam profiles: {ex.Message}");
            }
        }

        private static void ExportMsixvcProfiles(Dictionary<string, BundledGameEntry> uniqueGames)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(MSIXVC_PATH))
                {
                    if (key == null)
                    {
                        Logger.Debug("MSIXVC profiles registry key not found");
                        return;
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
                                    var entry = ParseGameElement(doc.RootElement, "MSIXVC");
                                    if (entry != null)
                                    {
                                        MergeEntry(uniqueGames, entry);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Failed to parse MSIXVC profile {subKeyName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to export MSIXVC profiles: {ex.Message}");
            }
        }

        private static void ExportUbisoftProfiles(Dictionary<string, BundledGameEntry> uniqueGames)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UBISOFT_PATH))
                {
                    if (key == null)
                    {
                        Logger.Debug("Ubisoft profiles registry key not found");
                        return;
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
                                    var entry = ParseGameElement(doc.RootElement, "Ubisoft");
                                    if (entry != null)
                                    {
                                        MergeEntry(uniqueGames, entry);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Failed to parse Ubisoft profile {subKeyName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to export Ubisoft profiles: {ex.Message}");
            }
        }

        private static BundledGameEntry ParseGameElement(JsonElement element, string defaultPlatform)
        {
            var entry = new BundledGameEntry();

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
                    var identifier = new BundledIdentifier
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

                    entry.Identifiers.Add(identifier);
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
                                var profile = DecodeSettings(settingsBase64, provider);

                                // Add profile for each hardware model
                                foreach (var hwModel in hardwareModels)
                                {
                                    var bundledProfile = new BundledProfile
                                    {
                                        HardwareModel = hwModel,
                                        Provider = profile.Provider,
                                        TDP = profile.TDP,
                                        FrameCap = profile.FrameCap,
                                        ResolutionCap = profile.ResolutionCap,
                                        ControlMode = profile.ControlMode
                                    };
                                    entry.Profiles.Add(bundledProfile);
                                }

                                // If no hardware models specified, use provider as key
                                if (hardwareModels.Count == 0 && !string.IsNullOrEmpty(provider))
                                {
                                    var bundledProfile = new BundledProfile
                                    {
                                        HardwareModel = provider,
                                        Provider = provider,
                                        TDP = profile.TDP,
                                        FrameCap = profile.FrameCap,
                                        ResolutionCap = profile.ResolutionCap,
                                        ControlMode = profile.ControlMode
                                    };
                                    entry.Profiles.Add(bundledProfile);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to parse profile for {entry.GameName}: {ex.Message}");
                    }
                }
            }

            return entry.Profiles.Count > 0 ? entry : null;
        }

        private static (string Provider, int TDP, int? FrameCap, string ResolutionCap, string ControlMode) DecodeSettings(string base64Settings, string provider)
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Settings));
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    int tdp = 0;
                    int? frameCap = null;
                    string resolutionCap = null;
                    string controlMode = null;

                    // TDP
                    if (root.TryGetProperty("TDP", out var tdpProp))
                    {
                        if (tdpProp.ValueKind == JsonValueKind.Number)
                            tdp = tdpProp.GetInt32();
                        else if (tdpProp.ValueKind == JsonValueKind.String)
                            int.TryParse(tdpProp.GetString(), out tdp);
                    }

                    // Frame cap
                    if (root.TryGetProperty("frameCap", out var fpsCapProp))
                    {
                        if (fpsCapProp.ValueKind == JsonValueKind.Number)
                            frameCap = fpsCapProp.GetInt32();
                        else if (fpsCapProp.ValueKind == JsonValueKind.String)
                        {
                            if (int.TryParse(fpsCapProp.GetString(), out var fps) && fps > 0)
                                frameCap = fps;
                        }
                    }

                    // Resolution cap
                    if (root.TryGetProperty("resolutionCap", out var resCapProp))
                        resolutionCap = resCapProp.GetString();

                    // Control mode
                    if (root.TryGetProperty("controlMode", out var controlProp))
                        controlMode = controlProp.GetString();

                    return (provider, tdp, frameCap, resolutionCap, controlMode);
                }
            }
            catch
            {
                return (provider, 0, null, null, null);
            }
        }

        private static void MergeEntry(Dictionary<string, BundledGameEntry> uniqueGames, BundledGameEntry entry)
        {
            // Use game name as key for deduplication
            var key = entry.GameName.ToLowerInvariant();

            if (uniqueGames.TryGetValue(key, out var existing))
            {
                // Merge identifiers
                foreach (var id in entry.Identifiers)
                {
                    if (!existing.Identifiers.Any(e => e.Platform == id.Platform && e.GameUid == id.GameUid))
                    {
                        existing.Identifiers.Add(id);
                    }
                }

                // Merge profiles (prefer profiles with TDP > 0)
                foreach (var profile in entry.Profiles)
                {
                    var existingProfile = existing.Profiles.FirstOrDefault(p => p.HardwareModel == profile.HardwareModel);
                    if (existingProfile == null)
                    {
                        existing.Profiles.Add(profile);
                    }
                    else if (profile.TDP > 0 && existingProfile.TDP == 0)
                    {
                        // Replace with profile that has TDP
                        existing.Profiles.Remove(existingProfile);
                        existing.Profiles.Add(profile);
                    }
                }
            }
            else
            {
                uniqueGames[key] = entry;
            }
        }

        /// <summary>
        /// Checks if registry profile paths exist (Xbox FSE has been enabled).
        /// </summary>
        public static bool RegistryProfilesExist()
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
    }
}

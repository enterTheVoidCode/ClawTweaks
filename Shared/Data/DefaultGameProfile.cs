using System.Collections.Generic;
using System.Xml.Serialization;

namespace Shared.Data
{
    /// <summary>
    /// Decoded settings from a Microsoft Default Game Profile.
    /// Contains TDP/FPS recommendations for a specific hardware model.
    /// </summary>
    [XmlRoot("DefaultGameProfile")]
    public struct DefaultGameProfile
    {
        /// <summary>TDP in watts (e.g., 17, 25, 30)</summary>
        [XmlElement("TDP")]
        public int TDP;

        /// <summary>FPS cap (null = unlimited)</summary>
        [XmlElement("FrameCap")]
        public int? FrameCap;

        /// <summary>Resolution cap (e.g., "720P", "1080P", null = native)</summary>
        [XmlElement("ResolutionCap")]
        public string ResolutionCap;

        /// <summary>Control mode (e.g., "Gamepad", "Touch")</summary>
        [XmlElement("ControlMode")]
        public string ControlMode;

        /// <summary>Game display name from the profile</summary>
        [XmlElement("GameName")]
        public string GameName;

        /// <summary>Hardware model this profile is for (OMNI, HORSEM4N, etc.)</summary>
        [XmlElement("HardwareModel")]
        public string HardwareModel;

        /// <summary>Provider of the profile (ArmouryCrate, GamingServices)</summary>
        [XmlElement("Provider")]
        public string Provider;

        /// <summary>Steam App ID for icon loading (null for non-Steam games)</summary>
        [XmlElement("SteamAppId")]
        public string SteamAppId;

        /// <summary>
        /// Returns true if this profile has valid TDP data.
        /// </summary>
        public bool IsValid() => TDP > 0;

        /// <summary>
        /// Gets the local Steam icon path if this is a Steam game.
        /// Steam caches game assets in appcache/librarycache/{appid}/ folders.
        /// </summary>
        public string GetSteamIconPath()
        {
            if (string.IsNullOrEmpty(SteamAppId))
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
                var cacheFolder = System.IO.Path.Combine(steamPath, "appcache", "librarycache", SteamAppId);
                if (!System.IO.Directory.Exists(cacheFolder))
                    continue;

                try
                {
                    // The icon is a small hash-named .jpg file (typically ~1KB)
                    // Look for the smallest jpg that isn't a known named file
                    var jpgFiles = System.IO.Directory.GetFiles(cacheFolder, "*.jpg");
                    string iconPath = null;
                    long smallestSize = long.MaxValue;

                    foreach (var file in jpgFiles)
                    {
                        var fileName = System.IO.Path.GetFileName(file);
                        // Skip known large files
                        if (fileName == "header.jpg" || fileName == "library_600x900.jpg" ||
                            fileName == "library_hero.jpg" || fileName == "library_hero_blur.jpg")
                            continue;

                        var fileInfo = new System.IO.FileInfo(file);
                        if (fileInfo.Length < smallestSize && fileInfo.Length < 5000) // Icons are typically < 2KB
                        {
                            smallestSize = fileInfo.Length;
                            iconPath = file;
                        }
                    }

                    if (iconPath != null)
                        return iconPath;

                    // Fall back to logo.png (transparent game logo)
                    var logoPath = System.IO.Path.Combine(cacheFolder, "logo.png");
                    if (System.IO.File.Exists(logoPath))
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
        /// Returns a display string for the UI (e.g., "17W - 60fps").
        /// </summary>
        public string ToDisplayString()
        {
            if (FrameCap.HasValue && FrameCap.Value > 0)
                return $"{TDP}W - {FrameCap}fps";
            return $"{TDP}W";
        }

        public override string ToString()
        {
            return $"DefaultGameProfile[{GameName}, {TDP}W, {FrameCap}fps, {HardwareModel}]";
        }
    }

    /// <summary>
    /// Entry in the registry containing multiple hardware-specific profiles for a game.
    /// </summary>
    public struct GameProfileEntry
    {
        /// <summary>Display name of the game</summary>
        public string GameName;

        /// <summary>List of identifiers for matching (exe names, paths, app IDs)</summary>
        public List<GameIdentifier> Identifiers;

        /// <summary>Profiles keyed by hardware model (OMNI, HORSEM4N, etc.)</summary>
        public Dictionary<string, DefaultGameProfile> Profiles;

        public bool IsValid() => !string.IsNullOrEmpty(GameName) && Profiles != null && Profiles.Count > 0;
    }

    /// <summary>
    /// Identifier used to match a game from registry profiles.
    /// </summary>
    public struct GameIdentifier
    {
        /// <summary>Platform source (Steam, MSIXVC, Ubisoft)</summary>
        public string Platform;

        /// <summary>Unique identifier (Steam App ID or Package Family Name)</summary>
        public string GameUid;

        /// <summary>Extracted executable name for matching (e.g., "game.exe")</summary>
        public string ExeName;
    }

    /// <summary>
    /// Legion Go hardware variant for profile selection.
    /// </summary>
    public enum LegionGoVariant
    {
        /// <summary>Unknown or unsupported hardware</summary>
        Unknown = 0,

        /// <summary>Legion Go 1 / Legion Go S with Z1 Extreme (Phoenix) - uses OMNI profiles</summary>
        Z1Extreme = 1,

        /// <summary>Legion Go 2 with Z2 Extreme (Strix Point) - uses HORSEM4N profiles</summary>
        Z2Extreme = 2
    }
}

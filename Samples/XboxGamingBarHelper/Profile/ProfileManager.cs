using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.ApplicationModel.AppService;
using Windows.Storage;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Profile
{
    internal class ProfileManager : Manager
    {
        private const string PROFILE_FOLDER_NAME = "profiles";
        private const string XML_EXTENSION = ".xml";

        public readonly GameProfile GlobalProfile;

        private readonly Dictionary<GameId, GameProfile> gameProfiles;
        public IReadOnlyDictionary<GameId, GameProfile> GameProfiles
        {
            get { return gameProfiles; }
        }

        private readonly PerGameProfileProperty perGameProfile;
        public PerGameProfileProperty PerGameProfile
        {
            get { return  perGameProfile; }
        }
        
        private readonly GameProfileProperty currentProfile;
        public GameProfileProperty CurrentProfile
        {
            get { return currentProfile; }
        }

        public ProfileManager(AppServiceConnection connection) : base(connection)
        {
            gameProfiles = new Dictionary<GameId, GameProfile>();

            Logger.Info("Initialize global profile.");
            // Load global profile.
            var globalProfilePath = GetGlobalProfilePath();
            if (!File.Exists(globalProfilePath))
            {
                // Create global profile path when it's not previously exist.
                GlobalProfile = new GameProfile(GameProfile.GLOBAL_PROFILE_NAME, GameProfile.GLOBAL_PROFILE_NAME, true, 25, true, 80, 0, globalProfilePath, gameProfiles);
                GlobalProfile.Save();
            }
            else
            {
                GlobalProfile = XmlHelper.FromXMLFile<GameProfile>(globalProfilePath);
                GlobalProfile.Path = globalProfilePath;
            }

            Logger.Info("Create game profiles folder.");
            // Make sure game profiles folder is created.
            var gameProfilesFolder = GetGameProfilesFolder();
            if (!Directory.Exists(gameProfilesFolder))
            {
                Directory.CreateDirectory(gameProfilesFolder);
            }

            Logger.Info("Load game profiles.");
            // Read all existing game profiles.
            var xmlFiles = Directory.GetFiles(gameProfilesFolder, $"*{XML_EXTENSION}");
            foreach (string filePath in xmlFiles)
            {
                try
                {
                    var gameProfile = XmlHelper.FromXMLFile<GameProfile>(filePath);
                    gameProfile.Path = filePath;
                    gameProfile.Cache = gameProfiles;
                    gameProfiles.Add(gameProfile.GameId, gameProfile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading or deserializing XML file '{filePath}': {ex.Message}.");
                }
            }

            Logger.Info("Initialize own properties.");
            perGameProfile = new PerGameProfileProperty(null, this);
            currentProfile = new GameProfileProperty(GlobalProfile, this);
        }

        public static string GetGameProfilesFolder()
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, PROFILE_FOLDER_NAME);
        }

        public static string GetGlobalProfilePath()
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, $"{GameProfile.GLOBAL_PROFILE_NAME}{XML_EXTENSION}");
        }

        public bool TryGetProfile(GameId gameId, out GameProfile gameProfile)
        {
            return gameProfiles.TryGetValue(gameId, out gameProfile);
        }

        public GameProfile AddNewProfile(GameId gameId)
        {
            if (TryGetProfile(gameId, out var gameProfile))
            {
                Logger.Warn($"Already have profile for {gameId.Name}.");
                return gameProfile;
            }

            var newGameProfilePath = Path.Combine(GetGameProfilesFolder(), $"{Path.GetFileNameWithoutExtension(gameId.Path)}{XML_EXTENSION}");
            var newGameProfile = new GameProfile(gameId.Name, gameId.Path, true, CurrentProfile.TDP, CurrentProfile.CPUBoost, CurrentProfile.CPUEPP, CurrentProfile.CPUClock, newGameProfilePath, gameProfiles);
            newGameProfile.Save();
            Logger.Info($"Add new profile for {gameId.Name} at {newGameProfilePath}.");
            return newGameProfile;
        }
    }
}

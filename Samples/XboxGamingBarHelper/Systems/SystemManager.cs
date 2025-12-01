using NLog;
using RTSSSharedMemoryNET;
using System;
using System.IO;
using System.Linq;
using Shared.Data;
using XboxGamingBarHelper.Windows;
using XboxGamingBarHelper.Core;
using Windows.ApplicationModel.AppService;
using System.Collections.Generic;
using Shared.Utilities;

namespace XboxGamingBarHelper.Systems
{
    internal class SystemManager : Manager
    {
        private static readonly string[] IgnoredProcesses =
        {
            "rustdesk.exe",
            "anydesk.exe",
            "parsecd.exe",
            "unity.exe",
            "unrealeditor.exe",
            "eacefsubprocess.exe",
            "rider64.exe",
        };

        // Some games might not be detected by Xbox Game Bar, emulated games using RetroArch, MelonDS, Citra, etc.
        private static readonly string[] GameProcesses =
        {
            "azahar.exe",
            "cemu.exe",
            "citron.exe",
            "dolphin.exe",
            "duckstation-qt-x64-releaseltcg.exe",
            "duckstation.exe",
            "eden.exe",
            "melonds.exe",
            "pcsx2-qtx64.exe",
            "pcsx2-qt.exe",
            "pcsx2.exe",
            "ppssppwindows64.exe",
            "ppssppwindows.exe",
            "ppsspp.exe",
            "retroarch.exe",
            "rpcs3.exe",
            "ryujinx.exe",
            "scummvm.exe",
            "shadps4.exe",
            "vita3k.exe",
            "xemu.exe",
            "xenia_canary.exe",
        };

        private readonly RunningGameProperty runningGame;
        public RunningGameProperty RunningGame
        {
            get { return runningGame; }
        }

        private readonly RefreshRatesProperty refreshRates;
        public RefreshRatesProperty RefreshRates
        {
            get { return refreshRates; }
        }

        private readonly RefreshRateProperty refreshRate;
        public RefreshRateProperty RefreshRate
        {
            get { return refreshRate; }
        }

        private readonly ResolutionsProperty resolutions;
        public ResolutionsProperty Resolutions
        {
            get { return resolutions; }
        }

        private readonly ResolutionProperty resolution;
        public ResolutionProperty Resolution
        {
            get { return resolution; }
        }

        private readonly HDRSupportedProperty hdrSupported;
        public HDRSupportedProperty HDRSupported
        {
            get { return hdrSupported; }
        }

        private readonly HDREnabledProperty hdrEnabled;
        public HDREnabledProperty HDREnabled
        {
            get { return hdrEnabled; }
        }

        private readonly TrackedGameProperty trackedGame;
        public TrackedGameProperty TrackedGame
        {
            get { return trackedGame; }
        }

        private IReadOnlyDictionary<GameId, GameProfile> Profiles { get; }

        // Keep track to current opening windows to determine currently running game.
        private Dictionary<int, ProcessWindow> ProcessWindows { get; }
        private Dictionary<int, AppEntry> AppEntries { get; }

        public SystemManager(AppServiceConnection connection, IReadOnlyDictionary<GameId, GameProfile> profiles) : base(connection)
        {
            Logger.Info("Create process windows.");
            ProcessWindows = new Dictionary<int, ProcessWindow>();
            Logger.Info("Create app entries.");
            AppEntries = new Dictionary<int, AppEntry>();
            Logger.Info("Save profiles for detecting games.");
            Profiles = profiles;

            trackedGame = new TrackedGameProperty(this);
            Logger.Info("Check current running game.");
            runningGame = new RunningGameProperty(this);
            Logger.Info("Check supported refresh rates.");
            refreshRates = new RefreshRatesProperty(User32.GetSupportedRefreshRates(), this);
            Logger.Info("Check current refresh rate.");
            refreshRate = new RefreshRateProperty(User32.GetCurrentRefreshRate(), this);
            Logger.Info("Check supported resolutions.");
            resolutions = new ResolutionsProperty(User32.GetSupportedResolutions(), this);
            Logger.Info("Check current resolution.");
            resolution = new ResolutionProperty(User32.GetCurrentResolution(), this);
            Logger.Info("Check HDR status.");
            var hdrStatus = User32.GetHDRStatus();
            hdrSupported = new HDRSupportedProperty(hdrStatus.Supported, this);
            hdrEnabled = new HDREnabledProperty(hdrStatus.Enabled, this);
        }

        private RunningGame GetRunningGame()
        {
            try
            {
                User32.GetOpenWindows(ProcessWindows);
            }
            catch (Exception e)
            {
                Logger.Error($"Can't get open windows: {e}");
                return new RunningGame();
            }
            if (ProcessWindows.Count == 0)
            {
                Logger.Debug("There is not any opening window, so no game detected");
                return new RunningGame();
            }

            AppEntries.Clear();
            AppEntry[] appEntries = Array.Empty<AppEntry>();
            if (RTSSHelper.IsRunning())
            {
                try
                {
                    appEntries = OSD.GetAppEntries();
                    Logger.Debug($"RTSS returned {appEntries.Length} app entries");
                    foreach (var entry in appEntries)
                    {
                        Logger.Debug($"RTSS AppEntry: ProcessId={entry.ProcessId}, Name={entry.Name}, InstantaneousFrames={entry.InstantaneousFrames}");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Can't connect to Rivatuner Statistics Server: {e}");
                }
            }
            else
            {
                Logger.Debug("Rivatuner Statistics Server is not running, can't determine current game.");
            }

            foreach (var appEntry in appEntries)
            {
                AppEntries[appEntry.ProcessId] = appEntry;
            }

            Logger.Debug($"ProcessWindows count: {ProcessWindows.Count}, AppEntries count: {AppEntries.Count}");

            var possibleGames = new List<RunningGame>();
            if (ProcessWindows.Count > 0)
            {
                foreach (var processWindow in ProcessWindows)
                {
                    var processPath = processWindow.Value.Path;
                    var processExecutable = Path.GetFileName(processPath).ToLower();
                    if (IgnoredProcesses.Contains(processExecutable))
                    {
                        Logger.Debug($"Window {processWindow.Value.Path} is ignored");
                        continue;
                    }

                    if (trackedGame.IsValid() && trackedGame.DisplayName == processWindow.Value.Title)
                    {
                        Logger.Debug($"Found window \"{processWindow.Value.Title}\" running {(processWindow.Value.IsForeground ? "foreground" : "background")} process id {processWindow.Key} at path \"{processWindow.Value.Path}\" named \"{processWindow.Value.ProcessName}\" matches the xbox game bar widget app tracker target.");
                        possibleGames.Add(new RunningGame(processWindow.Value.ProcessId, processWindow.Value.Title, processWindow.Value.Path, 0, processWindow.Value.IsForeground));
                    }

                    if (Profiles.ContainsKey(new GameId(processWindow.Value.Title, processWindow.Value.Path)))
                    {
                        Logger.Debug($"Found window \"{processWindow.Value.Title}\" running {(processWindow.Value.IsForeground ? "foreground" : "background")} process id {processWindow.Key} at path \"{processWindow.Value.Path}\" named \"{processWindow.Value.ProcessName}\" has profile, use it.");
                        possibleGames.Add(new RunningGame(processWindow.Value.ProcessId, processWindow.Value.Title, processWindow.Value.Path, 0, processWindow.Value.IsForeground));
                        continue;
                    }

                    if (AppEntries.TryGetValue(processWindow.Value.ProcessId, out var appEntry))
                    {
                        if (appEntry.InstantaneousFrames > 0)
                        {
                            Logger.Debug($"Found window \"{processWindow.Value.Title}\" running {(processWindow.Value.IsForeground ? "foreground" : "background")} process id {processWindow.Key} at path \"{processWindow.Value.Path}\" named \"{processWindow.Value.ProcessName}\" has {appEntry.InstantaneousFrames} FPS, use it.");
                            possibleGames.Add(new RunningGame(processWindow.Value.ProcessId, processWindow.Value.Title, processWindow.Value.Path, appEntry.InstantaneousFrames, processWindow.Value.IsForeground));
                            continue;
                        }
                        else
                        {
                            Logger.Debug($"Window \"{processWindow.Value.Title}\" ProcessId={processWindow.Value.ProcessId} found in RTSS but has 0 FPS (not rendering?)");
                        }
                    }
                    else if (processWindow.Value.IsForeground && AppEntries.Count > 0)
                    {
                        // Log potential ProcessId mismatch - foreground window not found in RTSS entries
                        Logger.Debug($"Foreground window \"{processWindow.Value.Title}\" ProcessId={processWindow.Value.ProcessId} NOT found in RTSS AppEntries. RTSS ProcessIds: {string.Join(", ", AppEntries.Keys)}");
                    }

                    if (GameProcesses.Contains(processExecutable))
                    {
                        Logger.Debug($"Found window \"{processWindow.Value.Title}\" running {(processWindow.Value.IsForeground ? "foreground" : "background")} process id {processWindow.Key} at path \"{processPath}\" named \"{processWindow.Value.ProcessName}\" in pre-defined list.");
                        possibleGames.Add(new RunningGame(processWindow.Value.ProcessId, processWindow.Value.Title, processPath, 0, processWindow.Value.IsForeground));
                        continue;
                    }

                    Logger.Debug($"Window \"{processWindow.Value.Title}\" at path {processWindow.Value.Path} doesn't have profile nor FPS.");
                }
            }

            if (possibleGames.Count == 0)
            {
                Logger.Debug("Not found any game running.");
                return new RunningGame();
            }
            else if (possibleGames.Count == 1)
            {
                Logger.Debug($"Found single running game {possibleGames[0].GameId.Name}.");
                return possibleGames[0];
            }
            else
            {
                RunningGame highestFPSGame = new RunningGame();
                foreach (var possibleGame in possibleGames)
                {
                    if (possibleGame.IsForeground)
                    {
                        Logger.Debug($"Found foreground running game {possibleGames[0].GameId.Name} in multiple running game.");
                        return possibleGame;
                    }

                    if (!highestFPSGame.IsValid())
                    {
                        highestFPSGame = possibleGame;
                    }
                    else if (highestFPSGame.FPS <= possibleGame.FPS)
                    {
                        highestFPSGame = possibleGame;
                    }
                }

                Logger.Debug($"Found highest FPS ({highestFPSGame.FPS}) game {highestFPSGame.GameId.Name} in multiple games.");
                return highestFPSGame;
            }
        }

        public override void Update()
        {
            base.Update();

            var currentRunningGame = GetRunningGame();
            var previousRunningGame = RunningGame.Value;

            // RunningGame equality now compares both GameId and IsForeground
            if (previousRunningGame != currentRunningGame)
            {
                bool gameChanged = previousRunningGame.GameId != currentRunningGame.GameId;
                bool foregroundChanged = previousRunningGame.IsForeground != currentRunningGame.IsForeground;

                if (gameChanged)
                {
                    if (currentRunningGame.GameId.IsValid())
                    {
                        Logger.Info($"Detect new running game {currentRunningGame.GameId.Name}.");
                    }
                    else
                    {
                        Logger.Info($"Running game {previousRunningGame.GameId.Name} stopped.");
                    }
                }
                else if (foregroundChanged)
                {
                    Logger.Info($"Game {currentRunningGame.GameId.Name} foreground status changed to {currentRunningGame.IsForeground}.");
                }
                RunningGame.SetValue(currentRunningGame);
            }
        }
    }
}

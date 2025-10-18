using NLog;
using RTSSSharedMemoryNET;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Shared.Data;
using XboxGamingBarHelper.Windows;
using XboxGamingBarHelper.Core;
using Windows.ApplicationModel.AppService;
using System.Collections.Generic;

namespace XboxGamingBarHelper.Systems
{
    internal class SystemManager : Manager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly string[] IgnoredProcesses =
        {
            "rustdesk.exe",
            "anydesk.exe",
            "parsecd.exe",
            "unity.exe",
            "unrealeditor.exe",
            "eacefsubprocess.exe",
            "rider64.exe"
        };

        private readonly RunningGameProperty runningGame;
        public RunningGameProperty RunningGame
        {
            get { return runningGame; }
        }

        private IReadOnlyDictionary<GameId, GameProfile> Profiles { get; }

        // Keep track to current opening windows to determine currently running game.
        private Dictionary<int, ProcessWindow> ProcessWindows { get; }
        private Dictionary<int, AppEntry> AppEntries { get; }

        public SystemManager(AppServiceConnection connection, IReadOnlyDictionary<GameId, GameProfile> profiles) : base(connection)
        {
            ProcessWindows = new Dictionary<int, ProcessWindow>();
            AppEntries = new Dictionary<int, AppEntry>();
            Profiles = profiles;
            runningGame = new RunningGameProperty(GetRunningGame(), this);
        }

        private RunningGame GetRunningGame()
        {
            Win32.GetOpenWindows(ProcessWindows);
            if (ProcessWindows.Count == 0)
            {
                Logger.Debug("There is not any opening window, so no game detected");
                return new RunningGame();
            }

            var appEntries = OSD.GetAppEntries();
            AppEntries.Clear();
            foreach (var appEntry in appEntries)
            {
                var appPath = appEntry.Name;
                // Ignore some unwanted processes.
                var appExecutableFileName = Path.GetFileName(appPath);
                if (IgnoredProcesses.Contains(appExecutableFileName.ToLower()))
                {
                    Logger.Debug($"Process {appPath} is ignored");
                    continue;
                }

                AppEntries[appEntry.ProcessId] = appEntry;
            }

            var possibleGames = new List<RunningGame>();
            foreach (var processWindow in ProcessWindows)
            {
                if (IgnoredProcesses.Contains(processWindow.Value.Path))
                {
                    Logger.Debug($"Window {processWindow.Value.Path} is ignored");
                    continue;
                }

                if (Profiles.ContainsKey(new GameId(processWindow.Value.Title, processWindow.Value.Path)))
                {
                    Logger.Debug($"Found window \"{processWindow.Value.Title}\" running {(processWindow.Value.IsForeground ? "foreground" : "background")} process id {processWindow.Key} at path \"{processWindow.Value.Path}\" named \"{processWindow.Value.ProcessName}\" has profile, use it.");
                    possibleGames.Add(new RunningGame(processWindow.Value.ProcessId, processWindow.Value.Title, processWindow.Value.Path, 0, processWindow.Value.IsForeground));
                    continue;
                }

                if (AppEntries.TryGetValue(processWindow.Value.ProcessId, out var appEntry) && appEntry.InstantaneousFrames > 0)
                {
                    Logger.Debug($"Found window \"{processWindow.Value.Title}\" running {(processWindow.Value.IsForeground ? "foreground" : "background")} process id {processWindow.Key} at path \"{processWindow.Value.Path}\" named \"{processWindow.Value.ProcessName}\" has {appEntry.InstantaneousFrames} FPS, use it.");
                    possibleGames.Add(new RunningGame(processWindow.Value.ProcessId, processWindow.Value.Title, processWindow.Value.Path, appEntry.InstantaneousFrames, processWindow.Value.IsForeground));
                    continue;
                }

                Logger.Debug($"Window \"{processWindow.Value.Title}\" at path {processWindow.Value.Path} doesn't have profile nor FPS.");
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
            if (RunningGame != currentRunningGame)
            {
                if (currentRunningGame.GameId.IsValid())
                {
                    Logger.Info($"Detect new running game {currentRunningGame.GameId.Name}.");
                }
                else
                {
                    Logger.Info($"Running game {RunningGame.Value.GameId.Name} stopped.");
                }
                RunningGame.Value = currentRunningGame;
            }
        }
    }
}

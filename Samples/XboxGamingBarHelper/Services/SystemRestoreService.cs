using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Services;

namespace XboxGamingBarHelper.Services
{
    /// <summary>
    /// Service to save original system values before modification and restore them on uninstall.
    /// This ensures users can cleanly uninstall the app without leftover system changes.
    /// </summary>
    internal static class SystemRestoreService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string RestoreFilePath;
        private static SystemRestoreData _restoreData;
        private static bool _isLoaded = false;

        static SystemRestoreService()
        {
            // Store in LocalState folder
            string localStatePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg",
                "LocalState"
            );
            RestoreFilePath = Path.Combine(localStatePath, "system_restore_data.json");
        }

        /// <summary>
        /// Data class for storing original system values
        /// </summary>
        public class SystemRestoreData
        {
            public int Version { get; set; } = 1;
            public DateTime FirstRunDate { get; set; }

            // CPU Boost original values
            public bool? OriginalCpuBoostAC { get; set; }
            public bool? OriginalCpuBoostDC { get; set; }
            public bool CpuBoostSaved { get; set; } = false;

            // EPP original values
            public int? OriginalEppAC { get; set; }
            public int? OriginalEppDC { get; set; }
            public bool EppSaved { get; set; } = false;

            // DAService original state
            public bool? OriginalDAServiceEnabled { get; set; }
            public bool DAServiceSaved { get; set; } = false;

            // Scheduled task was created
            public bool ScheduledTaskCreated { get; set; } = false;
        }

        /// <summary>
        /// Loads the restore data from disk, or creates new if doesn't exist.
        /// </summary>
        public static void Initialize()
        {
            if (_isLoaded) return;

            try
            {
                if (File.Exists(RestoreFilePath))
                {
                    string json = File.ReadAllText(RestoreFilePath);
                    _restoreData = JsonSerializer.Deserialize<SystemRestoreData>(json);
                    Logger.Info($"Loaded system restore data from {RestoreFilePath}");
                }
                else
                {
                    _restoreData = new SystemRestoreData
                    {
                        FirstRunDate = DateTime.Now
                    };
                    Logger.Info("Created new system restore data (first run)");
                }
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load system restore data: {ex.Message}");
                _restoreData = new SystemRestoreData
                {
                    FirstRunDate = DateTime.Now
                };
                _isLoaded = true;
            }
        }

        /// <summary>
        /// Saves the restore data to disk.
        /// </summary>
        private static void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(RestoreFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_restoreData, options);
                File.WriteAllText(RestoreFilePath, json);
                Logger.Debug("Saved system restore data");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save system restore data: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the original CPU Boost values before first modification.
        /// Call this BEFORE changing CPU Boost for the first time.
        /// </summary>
        public static void SaveOriginalCpuBoost(bool currentAC, bool currentDC)
        {
            Initialize();

            if (_restoreData.CpuBoostSaved)
            {
                Logger.Debug("CPU Boost original values already saved, skipping");
                return;
            }

            _restoreData.OriginalCpuBoostAC = currentAC;
            _restoreData.OriginalCpuBoostDC = currentDC;
            _restoreData.CpuBoostSaved = true;
            Save();

            Logger.Info($"Saved original CPU Boost values: AC={currentAC}, DC={currentDC}");
        }

        /// <summary>
        /// Saves the original EPP values before first modification.
        /// Call this BEFORE changing EPP for the first time.
        /// </summary>
        public static void SaveOriginalEpp(int currentAC, int currentDC)
        {
            Initialize();

            if (_restoreData.EppSaved)
            {
                Logger.Debug("EPP original values already saved, skipping");
                return;
            }

            _restoreData.OriginalEppAC = currentAC;
            _restoreData.OriginalEppDC = currentDC;
            _restoreData.EppSaved = true;
            Save();

            Logger.Info($"Saved original EPP values: AC={currentAC}, DC={currentDC}");
        }

        /// <summary>
        /// Saves the original DAService state before first modification.
        /// </summary>
        public static void SaveOriginalDAServiceState(bool wasEnabled)
        {
            Initialize();

            if (_restoreData.DAServiceSaved)
            {
                Logger.Debug("DAService original state already saved, skipping");
                return;
            }

            _restoreData.OriginalDAServiceEnabled = wasEnabled;
            _restoreData.DAServiceSaved = true;
            Save();

            Logger.Info($"Saved original DAService state: Enabled={wasEnabled}");
        }

        /// <summary>
        /// Marks that a scheduled task was created.
        /// </summary>
        public static void MarkScheduledTaskCreated()
        {
            Initialize();
            _restoreData.ScheduledTaskCreated = true;
            Save();
        }

        /// <summary>
        /// Prepares the system for uninstall by reverting all changes.
        /// </summary>
        /// <returns>A summary of actions taken</returns>
        public static string PrepareForUninstall()
        {
            Initialize();

            var results = new System.Text.StringBuilder();
            results.AppendLine("=== Prepare for Uninstall ===");
            results.AppendLine();

            // 1. Remove scheduled task
            try
            {
                Logger.Info("Uninstall: Removing scheduled task...");
                ScheduledTaskService.RemoveTask();
                ScheduledTaskService.RemoveLegacyTaskIfExists();
                results.AppendLine("✓ Scheduled task removed");
            }
            catch (Exception ex)
            {
                Logger.Error($"Uninstall: Failed to remove scheduled task: {ex.Message}");
                results.AppendLine($"✗ Failed to remove scheduled task: {ex.Message}");
            }

            // 2. Restore CPU Boost
            if (_restoreData.CpuBoostSaved && _restoreData.OriginalCpuBoostAC.HasValue)
            {
                try
                {
                    Logger.Info($"Uninstall: Restoring CPU Boost to AC={_restoreData.OriginalCpuBoostAC}, DC={_restoreData.OriginalCpuBoostDC}");
                    PowerManager.SetCpuBoostMode(true, _restoreData.OriginalCpuBoostAC.Value);
                    if (_restoreData.OriginalCpuBoostDC.HasValue)
                    {
                        PowerManager.SetCpuBoostMode(false, _restoreData.OriginalCpuBoostDC.Value);
                    }
                    results.AppendLine($"✓ CPU Boost restored to: AC={_restoreData.OriginalCpuBoostAC}, DC={_restoreData.OriginalCpuBoostDC}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Uninstall: Failed to restore CPU Boost: {ex.Message}");
                    results.AppendLine($"✗ Failed to restore CPU Boost: {ex.Message}");
                }
            }
            else
            {
                results.AppendLine("- CPU Boost: No original value saved (was not modified)");
            }

            // 3. Restore EPP
            if (_restoreData.EppSaved && _restoreData.OriginalEppAC.HasValue)
            {
                try
                {
                    Logger.Info($"Uninstall: Restoring EPP to AC={_restoreData.OriginalEppAC}, DC={_restoreData.OriginalEppDC}");
                    PowerManager.SetEppValue(true, (uint)_restoreData.OriginalEppAC.Value);
                    if (_restoreData.OriginalEppDC.HasValue)
                    {
                        PowerManager.SetEppValue(false, (uint)_restoreData.OriginalEppDC.Value);
                    }
                    results.AppendLine($"✓ EPP restored to: AC={_restoreData.OriginalEppAC}, DC={_restoreData.OriginalEppDC}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Uninstall: Failed to restore EPP: {ex.Message}");
                    results.AppendLine($"✗ Failed to restore EPP: {ex.Message}");
                }
            }
            else
            {
                results.AppendLine("- EPP: No original value saved (was not modified)");
            }

            // 4. Re-enable DAService if it was originally enabled
            if (_restoreData.DAServiceSaved)
            {
                if (_restoreData.OriginalDAServiceEnabled == true)
                {
                    try
                    {
                        Logger.Info("Uninstall: Re-enabling DAService (Legion Space)...");

                        // First enable the service startup type using sc.exe
                        var enableProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "sc.exe",
                                Arguments = "config DAService start= auto",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true
                            }
                        };
                        enableProcess.Start();
                        enableProcess.WaitForExit(5000);
                        Logger.Info($"Uninstall: DAService startup enabled (exit code: {enableProcess.ExitCode})");

                        // Then start the service
                        using (var sc = new ServiceController("DAService"))
                        {
                            if (sc.Status == ServiceControllerStatus.Stopped)
                            {
                                Logger.Info("Uninstall: Starting DAService...");
                                sc.Start();
                                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                                Logger.Info("Uninstall: DAService started");
                            }
                        }

                        results.AppendLine("✓ DAService (Legion Space) re-enabled and started");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Uninstall: Failed to re-enable DAService: {ex.Message}");
                        results.AppendLine($"✗ Failed to re-enable DAService: {ex.Message}");
                    }
                }
                else
                {
                    results.AppendLine("- DAService: Was already disabled before app install");
                }
            }
            else
            {
                results.AppendLine("- DAService: No original state saved (was not modified)");
            }

            results.AppendLine();
            results.AppendLine("Uninstall preparation complete.");
            results.AppendLine("You can now uninstall the app from Windows Settings.");

            string resultText = results.ToString();
            Logger.Info(resultText);

            return resultText;
        }

        /// <summary>
        /// Gets a summary of what original values are saved.
        /// </summary>
        public static string GetSavedValuesStatus()
        {
            Initialize();

            var status = new System.Text.StringBuilder();
            status.AppendLine("Saved Original Values:");

            if (_restoreData.CpuBoostSaved)
                status.AppendLine($"  CPU Boost: AC={_restoreData.OriginalCpuBoostAC}, DC={_restoreData.OriginalCpuBoostDC}");
            else
                status.AppendLine("  CPU Boost: Not saved");

            if (_restoreData.EppSaved)
                status.AppendLine($"  EPP: AC={_restoreData.OriginalEppAC}, DC={_restoreData.OriginalEppDC}");
            else
                status.AppendLine("  EPP: Not saved");

            if (_restoreData.DAServiceSaved)
                status.AppendLine($"  DAService: Originally enabled={_restoreData.OriginalDAServiceEnabled}");
            else
                status.AppendLine("  DAService: Not saved");

            status.AppendLine($"  Scheduled Task Created: {_restoreData.ScheduledTaskCreated}");
            status.AppendLine($"  First Run: {_restoreData.FirstRunDate}");

            return status.ToString();
        }

        /// <summary>
        /// Checks if any original values have been saved.
        /// </summary>
        public static bool HasSavedValues()
        {
            Initialize();
            return _restoreData.CpuBoostSaved || _restoreData.EppSaved || _restoreData.DAServiceSaved;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Handles self-elevation via scheduled task to avoid repeated UAC prompts.
    /// On first run, creates a scheduled task with elevated privileges.
    /// Subsequent launches use the task to self-elevate without UAC.
    /// </summary>
    public static class ElevationBootstrapper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string TaskName = "GoTweaksHelper";
        private const int ProcessStartTimeoutMs = 5000;

        /// <summary>
        /// Ensures the helper is running with admin privileges.
        /// Returns true if execution should continue, false if we're relaunching elevated.
        /// </summary>
        public static bool EnsureElevated(string[] args)
        {
            try
            {
                // Check if another elevated instance is already running FIRST
                // This prevents triggering UAC when helper is already alive
                const string mutexName = "Global\\XboxGamingBarHelper_SingleInstance";
                try
                {
                    using (var testMutex = Mutex.OpenExisting(mutexName))
                    {
                        // Mutex exists = another instance is running
                        Logger.Info("Another helper instance is running (mutex exists). Exiting gracefully without elevation.");
                        return false; // Don't continue, don't relaunch
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // Mutex doesn't exist = no other instance running, we can proceed
                    Logger.Debug("No existing helper instance detected (mutex not found)");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Mutex check failed: {ex.Message}");
                    // Continue anyway - let the normal mutex in Main() handle duplicates
                }

                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Logger.Error("Could not determine executable path");
                    return true; // Continue anyway, may fail later
                }

                // Already running as admin - continue normally
                if (IsRunningAsAdmin())
                {
                    Logger.Info("Already running as administrator");

                    // If we're admin, ensure the task exists with correct path (create if needed)
                    // Run this check asynchronously to avoid blocking startup (~7.8s for task creation)
                    EnsureScheduledTaskAsync(exePath);

                    return true; // Continue with normal execution immediately
                }

                Logger.Info($"Not running as administrator, checking for scheduled task. ExePath={exePath}");

                // Check if task exists with correct path
                if (TaskExistsWithCorrectPath(exePath))
                {
                    Logger.Info("Scheduled task exists with correct path, launching via task (no UAC)...");
                    if (LaunchViaTask())
                    {
                        Logger.Info("Launched via scheduled task, exiting current instance");
                        LogManager.Flush(); // Ensure logs are written before exit
                        return false; // Exit this non-elevated instance
                    }
                    else
                    {
                        Logger.Warn("Failed to launch via task, falling back to direct UAC elevation");
                    }
                }
                else
                {
                    Logger.Info("Scheduled task not found or path mismatch, requesting UAC elevation...");
                }

                // Fall back to direct elevation (will trigger UAC)
                Logger.Info("Attempting direct UAC elevation...");
                if (LaunchElevated(exePath, args))
                {
                    Logger.Info("Launched elevated process via UAC, exiting current instance");
                    LogManager.Flush(); // Ensure logs are written before exit
                    return false; // Exit this non-elevated instance
                }

                Logger.Error("Failed to elevate - continuing without admin rights (some features may not work)");
                LogManager.Flush();
                return true; // Continue anyway, but warn user
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in elevation bootstrapper");
                return true; // Continue anyway on error
            }
        }

        /// <summary>
        /// Asynchronously ensures the scheduled task exists with the correct path.
        /// Runs in a background thread to avoid blocking startup.
        /// </summary>
        private static void EnsureScheduledTaskAsync(string exePath)
        {
            // Run task check and creation in background thread
            Thread taskThread = new Thread(() =>
            {
                try
                {
                    Logger.Info("Background: Checking scheduled task...");
                    if (!TaskExistsWithCorrectPath(exePath))
                    {
                        Logger.Info("Background: Creating scheduled task for future launches...");
                        var timer = Stopwatch.StartNew();
                        if (CreateScheduledTask(exePath))
                        {
                            timer.Stop();
                            Logger.Info($"Background: Scheduled task created successfully in {timer.ElapsedMilliseconds}ms");
                        }
                        else
                        {
                            Logger.Warn("Background: Failed to create scheduled task - UAC will be required on future launches");
                        }
                    }
                    else
                    {
                        Logger.Info("Background: Scheduled task already exists with correct path");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Background: Error ensuring scheduled task: {ex.Message}");
                }
            })
            {
                IsBackground = true,
                Name = "ScheduledTaskCreator"
            };
            taskThread.Start();
        }

        /// <summary>
        /// Checks if the current process is running with administrator privileges.
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking admin status");
                return false;
            }
        }

        /// <summary>
        /// Checks if the scheduled task exists and points to the correct executable path.
        /// </summary>
        private static bool TaskExistsWithCorrectPath(string exePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\" /V /FO LIST",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Logger.Error("TaskExistsWithCorrectPath: Failed to start schtasks process");
                        return false;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(ProcessStartTimeoutMs);

                    Logger.Debug($"TaskExistsWithCorrectPath: schtasks ExitCode={process.ExitCode}");

                    if (process.ExitCode != 0)
                    {
                        Logger.Info($"Scheduled task does not exist. Error: {error.Trim()}");
                        return false;
                    }

                    // Parse "Task To Run:" line from verbose output
                    // Format: Task To Run:                          "C:\path\to\exe.exe"
                    var match = Regex.Match(output, @"Task To Run:\s+""?([^""]+)""?", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string taskPath = match.Groups[1].Value.Trim().Trim('"');
                        Logger.Debug($"TaskExistsWithCorrectPath: Parsed taskPath={taskPath}");

                        // Compare paths (case-insensitive on Windows)
                        if (string.Equals(taskPath, exePath, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Info($"Scheduled task exists with correct path: {taskPath}");
                            return true;
                        }
                        else
                        {
                            Logger.Info($"Scheduled task path mismatch. Task: {taskPath}, Current: {exePath}");
                            // Delete the old task so it can be recreated with correct path
                            UnregisterTask();
                            return false;
                        }
                    }

                    // Log first 500 chars of output to help debug regex issues
                    Logger.Warn($"Could not parse task path from schtasks output. First 500 chars: {output.Substring(0, Math.Min(500, output.Length))}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking scheduled task");
                return false;
            }
        }

        /// <summary>
        /// Creates the scheduled task with elevated privileges.
        /// Must be called from an elevated process.
        /// </summary>
        private static bool CreateScheduledTask(string exePath)
        {
            try
            {
                // First, delete any existing task
                UnregisterTask();

                // Create task with schtasks.exe
                // /SC ONLOGON - run at user logon
                // /RL HIGHEST - run with highest privileges (requires UAC to create, but auto-starts at logon)
                // /F - force create (overwrite if exists)
                // /DELAY 0000:30 - delay 30 seconds after logon to let system settle
                var createInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F /DELAY 0000:30",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(createInfo))
                {
                    if (process == null) return false;

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(ProcessStartTimeoutMs);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"schtasks /Create failed: {error}");
                        return false;
                    }

                    Logger.Debug($"schtasks /Create output: {output}");
                }

                // Configure additional settings and add wake trigger via PowerShell
                // Settings: battery, time limit, multiple instances
                // Triggers: keep logon trigger + add wake from sleep event trigger
                var wakeEventXml = "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]</Select></Query></QueryList>";
                var psScript = $@"
                    $task = Get-ScheduledTask -TaskName '{TaskName}' -ErrorAction SilentlyContinue
                    if ($task) {{
                        $task.Settings.DisallowStartIfOnBatteries = $false
                        $task.Settings.StopIfGoingOnBatteries = $false
                        $task.Settings.ExecutionTimeLimit = 'PT0S'
                        $task.Settings.MultipleInstances = 'IgnoreNew'
                        $task.Settings.StartWhenAvailable = $true
                        Set-ScheduledTask -InputObject $task | Out-Null
                    }}
                ";

                // Separate script for adding wake trigger (event-based triggers need special handling)
                var psWakeTriggerScript = $@"
                    $xml = @'
{wakeEventXml}
'@
                    $CIMClass = Get-CimClass -ClassName MSFT_TaskEventTrigger -Namespace Root/Microsoft/Windows/TaskScheduler
                    $trigger = New-CimInstance -CimClass $CIMClass -ClientOnly
                    $trigger.Subscription = $xml
                    $trigger.Enabled = $true
                    $trigger.Delay = 'PT10S'
                    $task = Get-ScheduledTask -TaskName '{TaskName}'
                    $allTriggers = @($task.Triggers) + $trigger
                    Set-ScheduledTask -TaskName '{TaskName}' -Trigger $allTriggers | Out-Null
                ";

                var psInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psInfo))
                {
                    if (process == null)
                    {
                        Logger.Warn("Could not start PowerShell to configure task settings");
                        return true; // Task was created, just settings failed
                    }

                    process.WaitForExit(ProcessStartTimeoutMs);

                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        Logger.Warn($"PowerShell task settings configuration warning: {error}");
                    }
                }

                // Add wake from sleep trigger (separate execution for cleaner error handling)
                var psWakeInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psWakeTriggerScript.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psWakeInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(ProcessStartTimeoutMs);

                        if (process.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            Logger.Warn($"PowerShell wake trigger configuration warning: {error}");
                            // Don't fail - task works, just wake trigger might not be added
                        }
                        else
                        {
                            Logger.Info("Wake from sleep trigger added successfully");
                        }
                    }
                }

                Logger.Info("Scheduled task created and configured successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating scheduled task");
                return false;
            }
        }

        /// <summary>
        /// Launches the helper via the scheduled task (no UAC prompt).
        /// </summary>
        private static bool LaunchViaTask()
        {
            try
            {
                Logger.Info("Attempting to run scheduled task...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Run /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Logger.Error("schtasks /Run: Failed to start process");
                        return false;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(ProcessStartTimeoutMs);

                    Logger.Info($"schtasks /Run: ExitCode={process.ExitCode}, Output={output.Trim()}, Error={error.Trim()}");

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"schtasks /Run failed with exit code {process.ExitCode}: {error}");
                        return false;
                    }

                    // Give the task a moment to start the process
                    Thread.Sleep(1000);
                    Logger.Info("Scheduled task run command completed successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error launching via scheduled task");
                return false;
            }
        }

        /// <summary>
        /// Launches the helper with direct UAC elevation (will prompt user).
        /// </summary>
        private static bool LaunchElevated(string exePath, string[] args)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args.Length > 0 ? string.Join(" ", args) : "",
                    UseShellExecute = true,
                    Verb = "runas" // This triggers UAC
                };

                var process = Process.Start(startInfo);
                return process != null;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                Logger.Warn("User cancelled UAC elevation prompt");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error launching elevated process");
                return false;
            }
        }

        /// <summary>
        /// Removes the scheduled task. Call this during uninstall.
        /// </summary>
        public static void UnregisterTask()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{TaskName}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return;

                    process.WaitForExit(ProcessStartTimeoutMs);

                    if (process.ExitCode == 0)
                    {
                        Logger.Info("Scheduled task removed successfully");
                    }
                    else
                    {
                        // Exit code 1 typically means task doesn't exist, which is fine
                        Logger.Debug("Scheduled task removal - task may not have existed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error removing scheduled task");
            }
        }
    }
}

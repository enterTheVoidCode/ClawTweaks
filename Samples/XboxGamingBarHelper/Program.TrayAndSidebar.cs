using NLog;
using Shared.Constants;
using Shared.Data;
using Shared.IPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.LosslessScaling;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Systems;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.DefaultGameProfiles;
using XboxGamingBarHelper.Labs;
using Shared.Enums;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {

        private static void TryStartTrayIndicator()
        {
            if (_isRunningAsService || _trayIndicator != null)
            {
                return;
            }

            try
            {
                _trayIndicator = new HelperTrayIndicator(OnTrayRestartRequested, OnTrayExitRequested);
                if (_trayIndicator.Start())
                {
                    Logger.Info("Tray indicator started");
                    return;
                }

                _trayIndicator.Dispose();
                _trayIndicator = null;
                Logger.Warn("Tray indicator failed to start");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Tray indicator startup failed: {ex.Message}");
                _trayIndicator = null;
            }
        }

        private static void TryStartSidebar()
        {
            try
            {
                // Load persisted sidebar state
                sidebarMenuEnabled = Properties.Settings.Default.SidebarMenuEnabled;
                Logger.Info($"Sidebar: Loaded persisted state: {sidebarMenuEnabled}");

                sidebarManager = new Sidebar.SidebarManager();
                if (sidebarManager.Start())
                {
                    Logger.Info("Sidebar manager started");
                }
                else
                {
                    Logger.Warn("Sidebar manager failed to start");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Sidebar manager startup failed: {ex.Message}");
                sidebarManager = null;
            }
        }

        private static void DisposeTrayIndicator()
        {
            try
            {
                _trayIndicator?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug($"Tray indicator dispose failed: {ex.Message}");
            }
            finally
            {
                _trayIndicator = null;
            }
        }

        private static void OnTrayExitRequested()
        {
            if (_isShuttingDown)
            {
                return;
            }

            Logger.Info("Tray: Exit requested");
            _isShuttingDown = true;
            LogManager.Flush();
        }

        private static void OnTrayRestartRequested()
        {
            if (_isShuttingDown)
            {
                return;
            }

            Logger.Info("Tray: Restart requested");
            _restartInProgress = true;

            bool restartScriptStarted = TryRestartHelperProcess();
            if (!restartScriptStarted)
            {
                Logger.Warn("Tray restart script did not start; attempting scheduled task fallback");
                try
                {
                    Services.ScheduledTaskService.RunTaskNow();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Tray restart fallback via scheduled task failed");
                }
            }

            _isShuttingDown = true;
            LogManager.Flush();

            // Some helper components keep foreground threads alive briefly after the main loop exits.
            // Force process termination so the detached restart script can relaunch reliably.
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                if (_restartInProgress)
                {
                    Logger.Info("Tray restart: forcing helper process exit");
                    LogManager.Flush();
                    Environment.Exit(0);
                }
            });
        }

        private static bool TryRestartHelperProcess()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                string currentExePath = currentProcess.MainModule?.FileName;
                int currentPid = currentProcess.Id;

                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    Logger.Error($"Restart requested but helper executable path is invalid: '{currentExePath}'");
                    return false;
                }

                string escapedExePath = currentExePath.Replace("'", "''");
                string tempScriptPath = Path.Combine(Path.GetTempPath(), $"GoTweaksRestart_{currentPid}.ps1");
                string script = $@"$pidToWait = {currentPid}
$exePath = '{escapedExePath}'

while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {{
    Start-Sleep -Milliseconds 250
}}

try {{
    Start-Process -FilePath $exePath -ErrorAction Stop | Out-Null
}}
catch {{
    schtasks.exe /Run /TN 'GoTweaks\GoTweaksHelper' | Out-Null
}}

Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";

                File.WriteAllText(tempScriptPath, script);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                Logger.Info($"Restart request completed: helper relaunch script started ({tempScriptPath})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to relaunch helper after tray restart request");
                return false;
            }
        }

        private sealed class HelperTrayIndicator : IDisposable
        {
            private readonly Action _onRestartRequested;
            private readonly Action _onExitRequested;
            private readonly ManualResetEventSlim _startupSignal = new ManualResetEventSlim(false);
            private Thread _uiThread;
            private Exception _startupException;
            private uint _threadId;
            private bool _disposed;

            internal HelperTrayIndicator(Action onRestartRequested, Action onExitRequested)
            {
                _onRestartRequested = onRestartRequested;
                _onExitRequested = onExitRequested;
            }

            internal bool Start()
            {
                _uiThread = new Thread(TrayUiThreadMain)
                {
                    IsBackground = true,
                    Name = "GoTweaksHelperTray",
                };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();

                if (!_startupSignal.Wait(5000))
                {
                    _startupException = new System.TimeoutException("Timed out waiting for tray thread to initialize.");
                }

                return _startupException == null;
            }

            private void TrayUiThreadMain()
            {
                _threadId = GetCurrentThreadId();

                try
                {
                    using (var menu = new global::System.Windows.Forms.ContextMenuStrip())
                    {
                        var restartItem = new global::System.Windows.Forms.ToolStripMenuItem("Restart");
                        restartItem.Click += (sender, args) => _onRestartRequested?.Invoke();

                        var exitItem = new global::System.Windows.Forms.ToolStripMenuItem("Exit");
                        exitItem.Click += (sender, args) => _onExitRequested?.Invoke();

                        menu.Items.Add(restartItem);
                        menu.Items.Add(new global::System.Windows.Forms.ToolStripSeparator());
                        menu.Items.Add(exitItem);

                        using (var notifyIcon = new global::System.Windows.Forms.NotifyIcon())
                        {
                            notifyIcon.Text = "GoTweaks Helper";
                            notifyIcon.Icon = global::System.Drawing.SystemIcons.Application;
                            notifyIcon.ContextMenuStrip = menu;
                            notifyIcon.Visible = true;

                            _startupSignal.Set();
                            global::System.Windows.Forms.Application.Run();

                            notifyIcon.Visible = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _startupException = ex;
                    _startupSignal.Set();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    if (_threadId != 0)
                    {
                        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                catch
                {
                    // Ignore shutdown messaging failures.
                }

                try
                {
                    _uiThread?.Join(2000);
                }
                catch
                {
                    // Ignore join failures; process shutdown will clean up if needed.
                }

                _startupSignal.Dispose();
            }
        }

    }
}

using NLog;
using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Windows Service wrapper for GoTweaks Helper.
    /// Allows the helper to run as a Windows Service for automatic startup
    /// and elevated privileges without UAC prompts.
    /// </summary>
    public class GoTweaksService : ServiceBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private CancellationTokenSource _cancellationTokenSource;
        private Task _runningTask;

        public GoTweaksService()
        {
            ServiceName = "GoTweaksHelper";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            Logger.Info("GoTweaksService OnStart called");

            _cancellationTokenSource = new CancellationTokenSource();

            // Start the helper logic in a background task
            _runningTask = Task.Run(async () =>
            {
                try
                {
                    await Program.RunAsService(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Logger.Info("Service task was cancelled");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Service task encountered an error");
                    throw;
                }
            });

            Logger.Info("GoTweaksService started successfully");
        }

        protected override void OnStop()
        {
            Logger.Info("GoTweaksService OnStop called");

            try
            {
                // Signal cancellation
                _cancellationTokenSource?.Cancel();

                // Wait for the task to complete (with timeout)
                if (_runningTask != null && !_runningTask.IsCompleted)
                {
                    var completed = _runningTask.Wait(TimeSpan.FromSeconds(30));
                    if (!completed)
                    {
                        Logger.Warn("Service task did not complete within timeout");
                    }
                }

                // Cleanup
                Program.Shutdown();

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                Logger.Info("GoTweaksService stopped successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during service stop");
            }
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            Logger.Info($"Session change: {changeDescription.Reason}, SessionId: {changeDescription.SessionId}");
            base.OnSessionChange(changeDescription);
        }
    }
}

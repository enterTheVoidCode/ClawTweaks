using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using NLog;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Virtual Xbox 360 controller using ViGEmBus driver.
    /// Used to send Xbox Guide button presses when Legion L is pressed.
    /// </summary>
    internal class ViGEmController : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private ViGEmClient client;
        private IXbox360Controller controller;
        private bool isConnected = false;
        private bool isDisposed = false;

        public bool IsPluggedIn => isConnected && controller != null;

        public bool Connect()
        {
            try
            {
                client = new ViGEmClient();
                Logger.Info("ViGEmController: Connected to ViGEmBus");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Failed to connect - {ex.Message}");
                return false;
            }
        }

        public bool PlugIn()
        {
            if (client == null) return false;
            try
            {
                controller = client.CreateXbox360Controller();
                controller.Connect();
                isConnected = true;
                Logger.Info("ViGEmController: Virtual Xbox 360 controller plugged in");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Failed to plug in - {ex.Message}");
                return false;
            }
        }

        public bool Unplug()
        {
            if (controller == null) return true;
            try
            {
                controller.Disconnect();
                isConnected = false;
                Logger.Info("ViGEmController: Virtual controller unplugged");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: Failed to unplug - {ex.Message}");
                return false;
            }
        }

        public bool EnsureConnected()
        {
            if (IsPluggedIn) return true;
            Logger.Info("ViGEmController: Reconnecting...");
            Dispose();
            isDisposed = false;
            return Connect() && PlugIn();
        }

        public bool SetGuide(bool pressed)
        {
            if (!IsPluggedIn) return false;
            try
            {
                controller.SetButtonState(Xbox360Button.Guide, pressed);
                controller.SubmitReport();
                Logger.Debug($"ViGEmController: SetGuide({pressed})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ViGEmController: SetGuide failed - {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            try { controller?.Disconnect(); } catch { }
            try { client?.Dispose(); } catch { }

            controller = null;
            client = null;
            isConnected = false;
            Logger.Info("ViGEmController: Disposed");
        }
    }
}

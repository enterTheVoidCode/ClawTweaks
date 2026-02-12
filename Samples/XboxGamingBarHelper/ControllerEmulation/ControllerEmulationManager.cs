using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Labs;
using XboxGamingBarHelper.Settings;
using SharedDeviceType = Shared.Enums.DeviceType;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Handheld-agnostic controller emulation settings manager.
    /// This manager owns the shared gyro source + emulation mode settings and
    /// forwards configuration to the active device backend.
    /// </summary>
    internal class ControllerEmulationManager : Manager
    {
        private readonly SharedDeviceType deviceType;
        private readonly bool isSupported;

        private int gyroSource;
        private int mode;

        private ViGEmController virtualController;
        private readonly ControllerSuppressionManager suppressionManager;
        private IGyroSourceAdapter gyroSourceAdapter;
        private Thread forwardingThread;
        private volatile bool forwardingRunning;
        private int? virtualXboxUserIndex;
        private bool suppressionActive;
        private readonly uint[] lastPacketByController = new uint[4];
        private float mouseCarryX;
        private float mouseCarryY;

        private const int ForwardingIntervalMs = 8;
        private const uint ERROR_SUCCESS = 0;
        private const float GyroDs4MaxDegPerSecond = 2000.0f;
        private const float AccelDs4MaxG = 4.0f;
        private const float MouseDeadzoneDegPerSecond = 1.25f;
        private const float MousePixelsPerDegPerSecond = 0.06f;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        private delegate uint XInputGetStateDelegate(uint dwUserIndex, ref XINPUT_STATE pState);
        private static XInputGetStateDelegate xInputGetState;

        public readonly ControllerEmulationAvailableProperty ControllerEmulationAvailable;
        public readonly ControllerEmulationGyroSourceProperty ControllerEmulationGyroSource;
        public readonly ControllerEmulationModeProperty ControllerEmulationMode;

        public IEnumerable<IProperty> Properties
        {
            get
            {
                yield return ControllerEmulationAvailable;
                yield return ControllerEmulationGyroSource;
                yield return ControllerEmulationMode;
            }
        }

        public ControllerEmulationManager(LegionManager inLegionManager, GPDManager inGpdManager)
        {
            suppressionManager = new ControllerSuppressionManager();

            var deviceInfo = DeviceDetector.DetectDevice();
            deviceType = deviceInfo?.DeviceType ?? SharedDeviceType.Generic;
            isSupported = IsSupportedDevice(deviceType);

            LoadSettings();

            ControllerEmulationAvailable = new ControllerEmulationAvailableProperty(isSupported, this);
            ControllerEmulationGyroSource = new ControllerEmulationGyroSourceProperty(gyroSource, this);
            ControllerEmulationMode = new ControllerEmulationModeProperty(mode, this);

            Logger.Info($"ControllerEmulationManager initialized. DeviceType={deviceType}, Supported={isSupported}, GyroSource={gyroSource}, Mode={mode}");

            // Apply persisted settings on startup when supported.
            ApplyCurrentConfiguration("startup");
        }

        public void SetGyroSource(int source)
        {
            int normalized = NormalizeGyroSource(source);
            if (gyroSource == normalized)
            {
                return;
            }

            gyroSource = normalized;
            SaveSettings();
            ApplyCurrentConfiguration("gyro source changed");
        }

        public void SetMode(int inMode)
        {
            int normalized = NormalizeMode(inMode);
            if (mode == normalized)
            {
                return;
            }

            mode = normalized;
            SaveSettings();
            ApplyCurrentConfiguration("mode changed");
        }

        private static bool IsSupportedDevice(SharedDeviceType inDeviceType)
        {
            switch (inDeviceType)
            {
                case SharedDeviceType.LegionGo:
                case SharedDeviceType.LegionGo2:
                case SharedDeviceType.GPDWin5:
                    return true;
                default:
                    return false;
            }
        }

        private static int NormalizeGyroSource(int source)
        {
            return source == 1 ? 1 : 0;
        }

        private static int NormalizeMode(int inMode)
        {
            if (inMode < 0)
            {
                return 0;
            }

            if (inMode > 3)
            {
                return 3;
            }

            return inMode;
        }

        private void LoadSettings()
        {
            try
            {
                // Prefer new handheld-agnostic keys, fall back to legacy GPD keys.
                if (LocalSettingsHelper.TryGetValue("ControllerEmulationGyroSource", out int savedGyroSource))
                {
                    gyroSource = NormalizeGyroSource(savedGyroSource);
                }
                else if (LocalSettingsHelper.TryGetValue("GPDControllerEmulationGyroSource", out int legacyGyroSource))
                {
                    gyroSource = NormalizeGyroSource(legacyGyroSource);
                }
                else
                {
                    gyroSource = 0;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMode", out int savedMode))
                {
                    mode = NormalizeMode(savedMode);
                }
                else if (LocalSettingsHelper.TryGetValue("GPDControllerEmulationMode", out int legacyMode))
                {
                    mode = NormalizeMode(legacyMode);
                }
                else
                {
                    mode = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Controller emulation settings load failed: {ex.Message}");
                gyroSource = 0;
                mode = 0;
            }
        }

        private void SaveSettings()
        {
            try
            {
                LocalSettingsHelper.SetValue("ControllerEmulationGyroSource", gyroSource);
                LocalSettingsHelper.SetValue("ControllerEmulationMode", mode);

                // Keep legacy keys in sync for compatibility with older builds.
                LocalSettingsHelper.SetValue("GPDControllerEmulationGyroSource", gyroSource);
                LocalSettingsHelper.SetValue("GPDControllerEmulationMode", mode);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Controller emulation settings save failed: {ex.Message}");
            }
        }

        private void ApplyCurrentConfiguration(string reason)
        {
            if (!isSupported)
            {
                StopForwarding();
                Logger.Debug($"Skipping controller emulation apply ({reason}): unsupported device type {deviceType}");
                return;
            }

            // Controller emulation is intentionally software-only and independent from
            // per-device firmware gyro settings exposed in the Legion tab.
            bool backendApplied = true;
            Logger.Debug($"Controller emulation using software path only ({reason}); skipping device firmware gyro mapping");

            bool forwardingApplied = ConfigureForwarding(reason);

            if (backendApplied || forwardingApplied)
            {
                Logger.Info($"Controller emulation applied ({reason}): source={gyroSource}, mode={mode}, device={deviceType}, backend={backendApplied}, forwarding={forwardingApplied}");
            }
            else
            {
                Logger.Warn($"Controller emulation apply not completed ({reason}): source={gyroSource}, mode={mode}, device={deviceType}, backend={backendApplied}, forwarding={forwardingApplied}");
            }
        }

        private bool ConfigureForwarding(string reason)
        {
            if (!RequiresSoftwareForwarding(mode))
            {
                StopForwarding();
                Logger.Info($"Controller emulation forwarding disabled ({reason}) for mode {mode}");
                return true;
            }

            bool gyroReady = ConfigureGyroSourceAdapter(reason);
            bool needsVirtualGamepad = RequiresVirtualGamepad(mode);

            if (!needsVirtualGamepad)
            {
                virtualXboxUserIndex = null;
                if (virtualController != null)
                {
                    try { virtualController.Dispose(); } catch { }
                    virtualController = null;
                }

                DisableSuppression();
                StartForwarding();
                return gyroReady;
            }

            if (!EnsureXInputLoaded())
            {
                Logger.Warn("Controller emulation forwarding unavailable: XInput not loaded");
                return false;
            }

            if (virtualController == null)
            {
                virtualController = new ViGEmController();
            }

            var targetType = mode == 1
                ? ViGEmController.VirtualGamepadType.Xbox360
                : ViGEmController.VirtualGamepadType.DualShock4;

            if (!virtualController.EnsureConnected(targetType))
            {
                Logger.Warn($"Controller emulation forwarding: failed to connect virtual {targetType} controller");
                return false;
            }

            virtualXboxUserIndex = targetType == ViGEmController.VirtualGamepadType.Xbox360
                ? virtualController.VirtualXboxUserIndex
                : null;

            StartForwarding();

            bool suppressionReady = EnableSuppression(reason);
            if (!suppressionReady)
            {
                Logger.Warn($"Controller emulation suppression unavailable ({reason}); forwarding continues without HidHide cloaking");
            }

            return gyroReady;
        }

        private static bool RequiresSoftwareForwarding(int selectedMode)
        {
            return selectedMode >= 0 && selectedMode <= 3;
        }

        private static bool RequiresVirtualGamepad(int selectedMode)
        {
            return selectedMode == 1 || selectedMode == 2 || selectedMode == 3;
        }

        private bool EnsureXInputLoaded()
        {
            if (xInputGetState != null)
            {
                return true;
            }

            try
            {
                var state = new XINPUT_STATE();
                XInputGetState14(0, ref state);
                xInputGetState = XInputGetState14;
                Logger.Info("Controller emulation using xinput1_4.dll");
                return true;
            }
            catch
            {
                try
                {
                    var state = new XINPUT_STATE();
                    XInputGetState910(0, ref state);
                    xInputGetState = XInputGetState910;
                    Logger.Info("Controller emulation using xinput9_1_0.dll");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Controller emulation failed to load XInput: {ex.Message}");
                    xInputGetState = null;
                    return false;
                }
            }
        }

        private void StartForwarding()
        {
            if (forwardingRunning)
            {
                return;
            }

            mouseCarryX = 0.0f;
            mouseCarryY = 0.0f;
            forwardingRunning = true;
            forwardingThread = new Thread(ForwardingThreadProc)
            {
                IsBackground = true,
                Name = "ControllerEmulationForwarder",
                Priority = ThreadPriority.AboveNormal,
            };
            forwardingThread.Start();
            Logger.Info("Controller emulation forwarding thread started");
        }

        private void StopForwarding()
        {
            forwardingRunning = false;
            if (forwardingThread != null && forwardingThread.IsAlive)
            {
                forwardingThread.Join(500);
            }

            forwardingThread = null;
            virtualXboxUserIndex = null;
            mouseCarryX = 0.0f;
            mouseCarryY = 0.0f;

            if (virtualController != null)
            {
                try
                {
                    virtualController.Dispose();
                }
                catch
                {
                    // Ignore disposal failures on shutdown.
                }
                virtualController = null;
            }

            DisableSuppression();
            StopGyroSourceAdapter();
        }

        private void ForwardingThreadProc()
        {
            Logger.Info("Controller emulation forwarding loop running");
            while (forwardingRunning)
            {
                try
                {
                    if (mode == 0)
                    {
                        if (TryReadGyroSample(out GyroSample mouseSample))
                        {
                            ApplyMouseFromGyro(mouseSample);
                        }

                        Thread.Sleep(ForwardingIntervalMs);
                        continue;
                    }

                    if (!TryReadPhysicalControllerState(out XINPUT_STATE state))
                    {
                        Thread.Sleep(ForwardingIntervalMs);
                        continue;
                    }

                    bool ok = false;
                    if (mode == 1)
                    {
                        ok = virtualController?.SubmitXboxState(
                            state.Gamepad.wButtons,
                            state.Gamepad.bLeftTrigger,
                            state.Gamepad.bRightTrigger,
                            state.Gamepad.sThumbLX,
                            state.Gamepad.sThumbLY,
                            state.Gamepad.sThumbRX,
                            state.Gamepad.sThumbRY) ?? false;
                    }
                    else if (mode == 2)
                    {
                        short gyroX = 0;
                        short gyroY = 0;
                        short gyroZ = 0;
                        short accelX = 0;
                        short accelY = 0;
                        short accelZ = 0;

                        if (TryReadGyroSample(out GyroSample motionSample))
                        {
                            gyroX = ConvertSignedRangeToInt16(motionSample.GyroXDegPerSecond, GyroDs4MaxDegPerSecond);
                            gyroY = ConvertSignedRangeToInt16(motionSample.GyroYDegPerSecond, GyroDs4MaxDegPerSecond);
                            gyroZ = ConvertSignedRangeToInt16(motionSample.GyroZDegPerSecond, GyroDs4MaxDegPerSecond);
                            accelX = ConvertSignedRangeToInt16(motionSample.AccelXG, AccelDs4MaxG);
                            accelY = ConvertSignedRangeToInt16(motionSample.AccelYG, AccelDs4MaxG);
                            accelZ = ConvertSignedRangeToInt16(motionSample.AccelZG, AccelDs4MaxG);
                        }

                        ok = virtualController?.SubmitDualShock4StateRaw(
                            state.Gamepad.wButtons,
                            state.Gamepad.bLeftTrigger,
                            state.Gamepad.bRightTrigger,
                            state.Gamepad.sThumbLX,
                            state.Gamepad.sThumbLY,
                            state.Gamepad.sThumbRX,
                            state.Gamepad.sThumbRY,
                            gyroX,
                            gyroY,
                            gyroZ,
                            accelX,
                            accelY,
                            accelZ) ?? false;
                    }
                    else if (mode == 3)
                    {
                        ok = virtualController?.SubmitDualShock4State(
                            state.Gamepad.wButtons,
                            state.Gamepad.bLeftTrigger,
                            state.Gamepad.bRightTrigger,
                            state.Gamepad.sThumbLX,
                            state.Gamepad.sThumbLY,
                            state.Gamepad.sThumbRX,
                            state.Gamepad.sThumbRY) ?? false;
                    }

                    if (!ok)
                    {
                        Logger.Debug("Controller emulation forwarding submit failed");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Controller emulation forwarding loop error: {ex.Message}");
                }

                Thread.Sleep(ForwardingIntervalMs);
            }

            Logger.Info("Controller emulation forwarding loop stopped");
        }

        private void ApplyMouseFromGyro(GyroSample sample)
        {
            // Horizontal: yaw; Vertical: pitch (inverted for typical mouse look behavior).
            float horizontal = sample.GyroYDegPerSecond;
            float vertical = sample.GyroXDegPerSecond;

            if (Math.Abs(horizontal) < MouseDeadzoneDegPerSecond)
            {
                horizontal = 0.0f;
            }

            if (Math.Abs(vertical) < MouseDeadzoneDegPerSecond)
            {
                vertical = 0.0f;
            }

            float moveX = (horizontal * MousePixelsPerDegPerSecond) + mouseCarryX;
            float moveY = ((-vertical) * MousePixelsPerDegPerSecond) + mouseCarryY;

            int deltaX = (int)Math.Round(moveX);
            int deltaY = (int)Math.Round(moveY);

            mouseCarryX = moveX - deltaX;
            mouseCarryY = moveY - deltaY;

            if (deltaX != 0 || deltaY != 0)
            {
                mouse_event(MOUSEEVENTF_MOVE, deltaX, deltaY, 0, UIntPtr.Zero);
            }
        }

        private bool ConfigureGyroSourceAdapter(string reason)
        {
            if (mode != 0 && mode != 2)
            {
                StopGyroSourceAdapter();
                return true;
            }

            StopGyroSourceAdapter();
            gyroSourceAdapter = BuildGyroSourceAdapter();
            if (gyroSourceAdapter == null)
            {
                Logger.Warn("Controller emulation gyro mode requested but no gyro source adapter is available");
                return false;
            }

            if (!gyroSourceAdapter.Start())
            {
                Logger.Warn($"Gyro adapter '{gyroSourceAdapter.Name}' failed to start");
                gyroSourceAdapter.Dispose();
                gyroSourceAdapter = null;

                // Fallback path for Legion: if internal sensor is unavailable, fall back to controller IMU.
                if ((deviceType == SharedDeviceType.LegionGo || deviceType == SharedDeviceType.LegionGo2) && gyroSource == 0)
                {
                    gyroSourceAdapter = new LegionControllerGyroSourceAdapter(false);
                    if (gyroSourceAdapter.Start())
                    {
                        Logger.Info($"Controller emulation motion fallback active ({reason}): {gyroSourceAdapter.Name}");
                        return true;
                    }

                    gyroSourceAdapter.Dispose();
                    gyroSourceAdapter = null;
                }

                return false;
            }

            Logger.Info($"Controller emulation motion source active ({reason}): {gyroSourceAdapter.Name}");
            return true;
        }

        private IGyroSourceAdapter BuildGyroSourceAdapter()
        {
            switch (deviceType)
            {
                case SharedDeviceType.LegionGo:
                case SharedDeviceType.LegionGo2:
                    if (gyroSource == 0)
                    {
                        return new WindowsSensorGyroSourceAdapter("Legion Internal Gyro");
                    }

                    return new LegionControllerGyroSourceAdapter(false);

                case SharedDeviceType.GPDWin5:
                    // Win5 firmware gyro packet path is still being finalized in this codebase.
                    // Internal Windows sensor path keeps motion mode functional until a native adapter is added.
                    return new WindowsSensorGyroSourceAdapter("GPD Internal Gyro");

                default:
                    return null;
            }
        }

        private void StopGyroSourceAdapter()
        {
            if (gyroSourceAdapter == null)
            {
                return;
            }

            try
            {
                gyroSourceAdapter.Stop();
                gyroSourceAdapter.Dispose();
            }
            catch
            {
                // Ignore shutdown exceptions.
            }

            gyroSourceAdapter = null;
        }

        private bool TryReadGyroSample(out GyroSample sample)
        {
            sample = default;
            var adapter = gyroSourceAdapter;
            if (adapter == null)
            {
                return false;
            }

            try
            {
                return adapter.TryGetLatestSample(out sample);
            }
            catch
            {
                return false;
            }
        }

        private bool EnableSuppression(string reason)
        {
            if (suppressionManager == null || suppressionActive)
            {
                return suppressionActive;
            }

            suppressionActive = suppressionManager.Enable(deviceType);
            if (suppressionActive)
            {
                Logger.Info($"Controller suppression enabled ({reason})");
            }

            return suppressionActive;
        }

        private void DisableSuppression()
        {
            if (!suppressionActive || suppressionManager == null)
            {
                return;
            }

            suppressionManager.Disable();
            suppressionActive = false;
            Logger.Info("Controller suppression disabled");
        }

        private static short ConvertSignedRangeToInt16(float value, float maxMagnitude)
        {
            if (maxMagnitude <= 0.0f)
            {
                return 0;
            }

            float clamped = Math.Max(-maxMagnitude, Math.Min(maxMagnitude, value));
            float normalized = clamped / maxMagnitude;
            return (short)Math.Round(normalized * short.MaxValue);
        }

        private bool TryReadPhysicalControllerState(out XINPUT_STATE selectedState)
        {
            selectedState = default;
            if (xInputGetState == null)
            {
                return false;
            }

            bool foundAnyConnected = false;
            XINPUT_STATE firstConnectedState = default;
            bool hasFirstConnected = false;

            for (uint index = 0; index < 4; index++)
            {
                if (virtualXboxUserIndex.HasValue && index == (uint)virtualXboxUserIndex.Value)
                {
                    continue;
                }

                XINPUT_STATE state = default;
                uint result = xInputGetState(index, ref state);
                if (result != ERROR_SUCCESS)
                {
                    continue;
                }

                foundAnyConnected = true;
                if (!hasFirstConnected)
                {
                    firstConnectedState = state;
                    hasFirstConnected = true;
                }

                // Prefer states that changed since last sample.
                if (state.dwPacketNumber != lastPacketByController[index])
                {
                    lastPacketByController[index] = state.dwPacketNumber;
                    selectedState = state;
                    return true;
                }
            }

            if (foundAnyConnected && hasFirstConnected)
            {
                selectedState = firstConnectedState;
                return true;
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopForwarding();
                suppressionManager?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

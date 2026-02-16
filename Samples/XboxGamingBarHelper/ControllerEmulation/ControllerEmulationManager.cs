using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
using XboxGamingBarHelper.Windows;
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
        private static ControllerEmulationManager activeInstance;

        private readonly SharedDeviceType deviceType;
        private readonly bool isSupported;
        private readonly SettingsManager settingsManager;

        private bool enabled;
        private int gyroSource;
        private int mode;
        private int gyroActivationMode;
        private int gyroActivationButton;
        private int ds4Orientation;
        private int mouseSensitivity;
        private int mouseThreshold;
        private int mouseAxis;
        private bool mouseInvertX;
        private bool mouseInvertY;
        private int mouseGainX;
        private int mouseGainY;
        private int stickSensitivity;
        private int stickThreshold;
        private int stickAxis;
        private bool stickInvertX;
        private bool stickInvertY;
        private int stickGainX;
        private int stickGainY;
        private int stickSelect;
        private bool stickExcessMove;
        private int stickRange;
        private bool stickOnlyJoystickData;
        private int virtualAbxyLayout;
        private bool hideStockController;
        private int hideTarget;
        private bool ps4TouchpadEnabled;

        private ViGEmController virtualController;
        private readonly ControllerSuppressionManager suppressionManager;
        private IGyroSourceAdapter gyroSourceAdapter;
        private Thread forwardingThread;
        private volatile bool forwardingRunning;
        private int? virtualXboxUserIndex;
        private int? physicalXboxUserIndex;
        private bool suppressionActive;
        private bool suppressionPausedForGameBar;
        private long suppressionPauseUntilTicksUtc;
        private bool hasWidgetForegroundSignal;
        private bool widgetForegroundSignal;
        private int gameBarForegroundConsecutiveTicks;
        private int nonGameBarForegroundConsecutiveTicks;
        private readonly HashSet<string> virtualXboxBridgeDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object virtualControllerSync = new object();
        private readonly uint[] lastPacketByController = new uint[4];
        private float mouseCarryX;
        private float mouseCarryY;
        private float mouseFilteredHorizontal;
        private float mouseFilteredVertical;
        private float mouseFilteredDerivativeHorizontal;
        private float mouseFilteredDerivativeVertical;
        private bool mouseFilterInitialized;
        private long mouseLastSampleTicksUtc;
        private short lastGyroStickX;
        private short lastGyroStickY;
        private bool hasLastGyroStick;
        private long lastGyroStickTicksUtc;
        private bool gyroToggleActive;
        private bool lastGyroActivationButtonPressed;
        private readonly INPUT[] mouseMoveInputBuffer = new INPUT[1];

        private const int ForwardingIntervalMs = 4;
        private const uint ERROR_SUCCESS = 0;
        private const float GyroDs4MaxDegPerSecond = 2000.0f;
        private const float AccelDs4MaxG = 4.0f;
        private const float MousePixelsPerDegree = 24.0f;
        private const float MouseSensitivityPower = 1.35f;
        private const float MouseOneEuroMinCutoff = 1.2f;
        private const float MouseOneEuroBeta = 0.25f;
        private const float MouseOneEuroDerivativeCutoff = 1.5f;
        private const float MouseResidualCutoffDegPerSecond = 0.12f;
        private const float MouseOutlierMaxDeltaDegPerSecond = 420.0f;
        private const float MouseMaxDegPerSecond = 720.0f;
        private const int MouseMaxPixelsPerFrame = 220;
        private const float DefaultDeltaSeconds = 1.0f / 250.0f;
        private const float MinDeltaSeconds = 0.002f;
        private const float MaxDeltaSeconds = 0.05f;
        private const float StickDegreesPerSecondAtFullDeflection = 220.0f;
        private const long StickOutputStaleTicks = TimeSpan.TicksPerSecond / 4; // 250ms
        private const float LegionTouchMaxX = 1023.0f;
        private const float LegionTouchMaxY = 1023.0f;
        private const float Ds4TouchMaxX = 1919.0f;
        private const float Ds4TouchMaxY = 943.0f;
        private const int GameBarForegroundStableTicks = 1;
        private const int GameBarBackgroundStableTicks = 2;
        private const int GuideSuppressionPauseSeconds = 25;
        // Temporary diagnostic switch: keep suppression control tied to foreground detection only.
        private static readonly bool GuideTriggeredSuppressionPauseEnabled = false;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_START = 0x0010;
        private const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;
        private const ushort XINPUT_GAMEPAD_X = 0x4000;
        private const ushort XINPUT_GAMEPAD_Y = 0x8000;
        private const byte XINPUT_TRIGGER_THRESHOLD = 30;
        private static readonly int MouseInputStructSize = Marshal.SizeOf(typeof(INPUT));
        private static readonly HashSet<string> GameBarForegroundProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "GameBar",
                "GameBarFTServer",
                "GameBarElevatedFT",
                "XboxGameBarWidgets",
                // App-mode widget host process (ms-gamebarwidget app package entry point).
                "XboxGamingBar",
                // Legacy/alternate naming seen on some builds.
                "XboxGameBar",
            };

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

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private delegate uint XInputGetStateDelegate(uint dwUserIndex, ref XINPUT_STATE pState);
        private static XInputGetStateDelegate xInputGetState;

        public readonly ControllerEmulationAvailableProperty ControllerEmulationAvailable;
        public readonly ControllerEmulationEnabledProperty ControllerEmulationEnabled;
        public readonly ControllerEmulationHideStockControllerProperty ControllerEmulationHideStockController;
        public readonly ControllerEmulationHideTargetProperty ControllerEmulationHideTarget;
        public readonly ControllerEmulationGyroSourceProperty ControllerEmulationGyroSource;
        public readonly ControllerEmulationModeProperty ControllerEmulationMode;
        public readonly ControllerEmulationGyroActivationModeProperty ControllerEmulationGyroActivationMode;
        public readonly ControllerEmulationGyroActivationButtonProperty ControllerEmulationGyroActivationButton;
        public readonly ControllerEmulationDs4OrientationProperty ControllerEmulationDs4Orientation;
        public readonly ControllerEmulationPs4TouchpadEnabledProperty ControllerEmulationPs4TouchpadEnabled;
        public readonly ControllerEmulationMouseSensitivityProperty ControllerEmulationMouseSensitivity;
        public readonly ControllerEmulationMouseThresholdProperty ControllerEmulationMouseThreshold;
        public readonly ControllerEmulationMouseAxisProperty ControllerEmulationMouseAxis;
        public readonly ControllerEmulationMouseInvertXProperty ControllerEmulationMouseInvertX;
        public readonly ControllerEmulationMouseInvertYProperty ControllerEmulationMouseInvertY;
        public readonly ControllerEmulationMouseGainXProperty ControllerEmulationMouseGainX;
        public readonly ControllerEmulationMouseGainYProperty ControllerEmulationMouseGainY;
        public readonly ControllerEmulationStickSensitivityProperty ControllerEmulationStickSensitivity;
        public readonly ControllerEmulationStickThresholdProperty ControllerEmulationStickThreshold;
        public readonly ControllerEmulationStickAxisProperty ControllerEmulationStickAxis;
        public readonly ControllerEmulationStickInvertXProperty ControllerEmulationStickInvertX;
        public readonly ControllerEmulationStickInvertYProperty ControllerEmulationStickInvertY;
        public readonly ControllerEmulationStickGainXProperty ControllerEmulationStickGainX;
        public readonly ControllerEmulationStickGainYProperty ControllerEmulationStickGainY;
        public readonly ControllerEmulationStickSelectProperty ControllerEmulationStickSelect;
        public readonly ControllerEmulationStickExcessMoveProperty ControllerEmulationStickExcessMove;
        public readonly ControllerEmulationStickRangeProperty ControllerEmulationStickRange;
        public readonly ControllerEmulationStickOnlyJoystickDataProperty ControllerEmulationStickOnlyJoystickData;
        public readonly ControllerEmulationVirtualABXYLayoutProperty ControllerEmulationVirtualABXYLayout;

        public IEnumerable<IProperty> Properties
        {
            get
            {
                yield return ControllerEmulationAvailable;
                yield return ControllerEmulationEnabled;
                yield return ControllerEmulationHideStockController;
                yield return ControllerEmulationHideTarget;
                yield return ControllerEmulationGyroSource;
                yield return ControllerEmulationMode;
                yield return ControllerEmulationGyroActivationMode;
                yield return ControllerEmulationGyroActivationButton;
                yield return ControllerEmulationDs4Orientation;
                yield return ControllerEmulationPs4TouchpadEnabled;
                yield return ControllerEmulationMouseSensitivity;
                yield return ControllerEmulationMouseThreshold;
                yield return ControllerEmulationMouseAxis;
                yield return ControllerEmulationMouseInvertX;
                yield return ControllerEmulationMouseInvertY;
                yield return ControllerEmulationMouseGainX;
                yield return ControllerEmulationMouseGainY;
                yield return ControllerEmulationStickSensitivity;
                yield return ControllerEmulationStickThreshold;
                yield return ControllerEmulationStickAxis;
                yield return ControllerEmulationStickInvertX;
                yield return ControllerEmulationStickInvertY;
                yield return ControllerEmulationStickGainX;
                yield return ControllerEmulationStickGainY;
                yield return ControllerEmulationStickSelect;
                yield return ControllerEmulationStickExcessMove;
                yield return ControllerEmulationStickRange;
                yield return ControllerEmulationStickOnlyJoystickData;
                yield return ControllerEmulationVirtualABXYLayout;
            }
        }

        public ControllerEmulationManager(LegionManager inLegionManager, GPDManager inGpdManager, SettingsManager inSettingsManager)
        {
            activeInstance = this;
            suppressionManager = new ControllerSuppressionManager();
            settingsManager = inSettingsManager;

            var deviceInfo = DeviceDetector.DetectDevice();
            deviceType = deviceInfo?.DeviceType ?? SharedDeviceType.Generic;
            isSupported = IsSupportedDevice(deviceType);

            LoadSettings();

            ControllerEmulationAvailable = new ControllerEmulationAvailableProperty(isSupported, this);
            ControllerEmulationEnabled = new ControllerEmulationEnabledProperty(enabled, this);
            ControllerEmulationHideStockController = new ControllerEmulationHideStockControllerProperty(hideStockController, this);
            ControllerEmulationHideTarget = new ControllerEmulationHideTargetProperty(hideTarget, this);
            ControllerEmulationGyroSource = new ControllerEmulationGyroSourceProperty(gyroSource, this);
            ControllerEmulationMode = new ControllerEmulationModeProperty(mode, this);
            ControllerEmulationGyroActivationMode = new ControllerEmulationGyroActivationModeProperty(gyroActivationMode, this);
            ControllerEmulationGyroActivationButton = new ControllerEmulationGyroActivationButtonProperty(gyroActivationButton, this);
            ControllerEmulationDs4Orientation = new ControllerEmulationDs4OrientationProperty(ds4Orientation, this);
            ControllerEmulationPs4TouchpadEnabled = new ControllerEmulationPs4TouchpadEnabledProperty(ps4TouchpadEnabled, this);
            ControllerEmulationMouseSensitivity = new ControllerEmulationMouseSensitivityProperty(mouseSensitivity, this);
            ControllerEmulationMouseThreshold = new ControllerEmulationMouseThresholdProperty(mouseThreshold, this);
            ControllerEmulationMouseAxis = new ControllerEmulationMouseAxisProperty(mouseAxis, this);
            ControllerEmulationMouseInvertX = new ControllerEmulationMouseInvertXProperty(mouseInvertX, this);
            ControllerEmulationMouseInvertY = new ControllerEmulationMouseInvertYProperty(mouseInvertY, this);
            ControllerEmulationMouseGainX = new ControllerEmulationMouseGainXProperty(mouseGainX, this);
            ControllerEmulationMouseGainY = new ControllerEmulationMouseGainYProperty(mouseGainY, this);
            ControllerEmulationStickSensitivity = new ControllerEmulationStickSensitivityProperty(stickSensitivity, this);
            ControllerEmulationStickThreshold = new ControllerEmulationStickThresholdProperty(stickThreshold, this);
            ControllerEmulationStickAxis = new ControllerEmulationStickAxisProperty(stickAxis, this);
            ControllerEmulationStickInvertX = new ControllerEmulationStickInvertXProperty(stickInvertX, this);
            ControllerEmulationStickInvertY = new ControllerEmulationStickInvertYProperty(stickInvertY, this);
            ControllerEmulationStickGainX = new ControllerEmulationStickGainXProperty(stickGainX, this);
            ControllerEmulationStickGainY = new ControllerEmulationStickGainYProperty(stickGainY, this);
            ControllerEmulationStickSelect = new ControllerEmulationStickSelectProperty(stickSelect, this);
            ControllerEmulationStickExcessMove = new ControllerEmulationStickExcessMoveProperty(stickExcessMove, this);
            ControllerEmulationStickRange = new ControllerEmulationStickRangeProperty(stickRange, this);
            ControllerEmulationStickOnlyJoystickData = new ControllerEmulationStickOnlyJoystickDataProperty(stickOnlyJoystickData, this);
            ControllerEmulationVirtualABXYLayout = new ControllerEmulationVirtualABXYLayoutProperty(virtualAbxyLayout, this);

            SubscribeForegroundSignal();

            Logger.Info($"ControllerEmulationManager initialized. DeviceType={deviceType}, Supported={isSupported}, Enabled={enabled}, HideStockController={hideStockController}, HideTarget={hideTarget}, GyroSource={gyroSource}, Mode={mode}, GyroActivationMode={gyroActivationMode}, GyroActivationButton={gyroActivationButton}, Ds4Orientation={ds4Orientation}, Ps4TouchpadEnabled={ps4TouchpadEnabled}");

            // Apply persisted settings on startup when supported.
            ApplyCurrentConfiguration("startup");
        }

        public override void Update()
        {
            base.Update();

            try
            {
                MonitorGameBarSuppressionState();
            }
            catch (Exception ex)
            {
                Logger.Debug($"Controller emulation suppression foreground monitor failed: {ex.Message}");
            }
        }

        internal static bool TrySetGuideFromExternal(bool pressed)
        {
            var instance = activeInstance;
            return instance?.TrySetGuideFromExternalInternal(pressed) ?? false;
        }

        internal static bool CanHandleExternalGuide()
        {
            var instance = activeInstance;
            return instance?.CanHandleExternalGuideInternal() ?? false;
        }

        private bool CanHandleExternalGuideInternal()
        {
            return enabled && mode == 1 && virtualController != null;
        }

        private bool TrySetGuideFromExternalInternal(bool pressed)
        {
            if (!CanHandleExternalGuideInternal())
            {
                return false;
            }

            var controller = virtualController;
            if (controller == null)
            {
                return false;
            }

            lock (virtualControllerSync)
            {
                if (!ReferenceEquals(controller, virtualController))
                {
                    return false;
                }

                bool ok = controller.SetGuide(pressed);
                if (ok)
                {
                    if (pressed && ShouldManageSuppression())
                    {
                        if (GuideTriggeredSuppressionPauseEnabled)
                        {
                            suppressionPauseUntilTicksUtc = DateTime.UtcNow.AddSeconds(GuideSuppressionPauseSeconds).Ticks;
                            Logger.Info($"Controller suppression guide pause armed for {GuideSuppressionPauseSeconds}s");
                            TryPauseSuppressionForForegroundGameBar("guide pressed");
                        }
                        else
                        {
                            suppressionPauseUntilTicksUtc = 0;
                            Logger.Debug("Controller suppression guide-triggered unhide skipped (foreground-only test mode)");
                        }
                    }

                    Logger.Debug($"Controller emulation guide routed to active virtual controller: pressed={pressed}");
                }

                return ok;
            }
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

        public void SetGyroActivationMode(int value)
        {
            int normalized = NormalizeGyroActivationMode(value);
            if (gyroActivationMode == normalized)
            {
                return;
            }

            gyroActivationMode = normalized;
            ResetGyroActivationRuntimeState();
            SaveSettings();
            Logger.Info($"Controller emulation gyro activation mode set to {gyroActivationMode}");
        }

        public void SetGyroActivationButton(int value)
        {
            int normalized = NormalizeGyroActivationButton(value);
            if (gyroActivationButton == normalized)
            {
                return;
            }

            gyroActivationButton = normalized;
            ResetGyroActivationRuntimeState();
            SaveSettings();
            Logger.Info($"Controller emulation gyro activation button set to {gyroActivationButton}");
        }

        public void SetDs4Orientation(int value)
        {
            int normalized = NormalizeDs4Orientation(value);
            if (ds4Orientation == normalized)
            {
                return;
            }

            ds4Orientation = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation DS4 motion orientation set to {ds4Orientation}");
        }

        public void SetPs4TouchpadEnabled(bool value)
        {
            if (ps4TouchpadEnabled == value)
            {
                return;
            }

            ps4TouchpadEnabled = value;
            SaveSettings();
            Logger.Info($"Controller emulation PS4 touchpad forwarding set to {ps4TouchpadEnabled}");
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            SaveSettings();
            ApplyCurrentConfiguration(enabled ? "enabled changed: on" : "enabled changed: off");
        }

        public void SetHideStockController(bool value)
        {
            if (hideStockController == value)
            {
                return;
            }

            hideStockController = value;
            SaveSettings();
            ApplySuppressionConfiguration(hideStockController ? "hide stock controller changed: on" : "hide stock controller changed: off");
        }

        public void SetHideTarget(int value)
        {
            int normalized = NormalizeHideTarget(value);
            if (hideTarget == normalized)
            {
                return;
            }

            hideTarget = normalized;
            SaveSettings();
            ApplySuppressionConfiguration($"hide target changed: {hideTarget}");
        }

        public void SetMouseSensitivity(int value)
        {
            int normalized = NormalizeMouseSensitivity(value);
            if (mouseSensitivity == normalized)
            {
                return;
            }

            mouseSensitivity = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse sensitivity set to {mouseSensitivity}");
        }

        public void SetMouseThreshold(int value)
        {
            int normalized = NormalizeMouseThreshold(value);
            if (mouseThreshold == normalized)
            {
                return;
            }

            mouseThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse threshold set to {mouseThreshold}");
        }

        public void SetMouseAxis(int value)
        {
            int normalized = NormalizeMouseAxis(value);
            if (mouseAxis == normalized)
            {
                return;
            }

            mouseAxis = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse axis set to {mouseAxis}");
        }

        public void SetMouseInvertX(bool value)
        {
            if (mouseInvertX == value)
            {
                return;
            }

            mouseInvertX = value;
            SaveSettings();
            Logger.Info($"Controller emulation mouse invert X set to {mouseInvertX}");
        }

        public void SetMouseInvertY(bool value)
        {
            if (mouseInvertY == value)
            {
                return;
            }

            mouseInvertY = value;
            SaveSettings();
            Logger.Info($"Controller emulation mouse invert Y set to {mouseInvertY}");
        }

        public void SetMouseGainX(int value)
        {
            int normalized = NormalizeMouseGain(value);
            if (mouseGainX == normalized)
            {
                return;
            }

            mouseGainX = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse gain X set to {mouseGainX}");
        }

        public void SetMouseGainY(int value)
        {
            int normalized = NormalizeMouseGain(value);
            if (mouseGainY == normalized)
            {
                return;
            }

            mouseGainY = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse gain Y set to {mouseGainY}");
        }

        public void SetStickSensitivity(int value)
        {
            int normalized = NormalizeStickSensitivity(value);
            if (stickSensitivity == normalized)
            {
                return;
            }

            stickSensitivity = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick sensitivity set to {stickSensitivity}");
        }

        public void SetStickThreshold(int value)
        {
            int normalized = NormalizeStickThreshold(value);
            if (stickThreshold == normalized)
            {
                return;
            }

            stickThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick threshold set to {stickThreshold}");
        }

        public void SetStickAxis(int value)
        {
            int normalized = NormalizeStickAxis(value);
            if (stickAxis == normalized)
            {
                return;
            }

            stickAxis = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick axis set to {stickAxis}");
        }

        public void SetStickInvertX(bool value)
        {
            if (stickInvertX == value)
            {
                return;
            }

            stickInvertX = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick invert X set to {stickInvertX}");
        }

        public void SetStickInvertY(bool value)
        {
            if (stickInvertY == value)
            {
                return;
            }

            stickInvertY = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick invert Y set to {stickInvertY}");
        }

        public void SetStickGainX(int value)
        {
            int normalized = NormalizeStickGain(value);
            if (stickGainX == normalized)
            {
                return;
            }

            stickGainX = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick gain X set to {stickGainX}");
        }

        public void SetStickGainY(int value)
        {
            int normalized = NormalizeStickGain(value);
            if (stickGainY == normalized)
            {
                return;
            }

            stickGainY = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick gain Y set to {stickGainY}");
        }

        public void SetStickSelect(int value)
        {
            int normalized = NormalizeStickSelect(value);
            if (stickSelect == normalized)
            {
                return;
            }

            stickSelect = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick select set to {stickSelect}");
        }

        public void SetStickExcessMove(bool value)
        {
            if (stickExcessMove == value)
            {
                return;
            }

            stickExcessMove = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick excess move set to {stickExcessMove}");
        }

        public void SetStickRange(int value)
        {
            int normalized = NormalizeStickRange(value);
            if (stickRange == normalized)
            {
                return;
            }

            stickRange = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick range set to {stickRange}");
        }

        public void SetStickOnlyJoystickData(bool value)
        {
            if (stickOnlyJoystickData == value)
            {
                return;
            }

            stickOnlyJoystickData = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick only joystick data set to {stickOnlyJoystickData}");
        }

        public void SetVirtualABXYLayout(int value)
        {
            int normalized = NormalizeVirtualAbxyLayout(value);
            if (virtualAbxyLayout == normalized)
            {
                return;
            }

            virtualAbxyLayout = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation virtual ABXY layout set to {virtualAbxyLayout}");
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

        private static int NormalizeHideTarget(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 3)
            {
                return 3;
            }

            return value;
        }

        private static int NormalizeGyroActivationMode(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 2)
            {
                return 2;
            }

            return value;
        }

        private static int NormalizeGyroActivationButton(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 16)
            {
                return 16;
            }

            return value;
        }

        private static int NormalizeDs4Orientation(int value)
        {
            return value == 1 ? 1 : 0;
        }

        private static int NormalizeMouseSensitivity(int value)
        {
            if (value < 1)
            {
                return 1;
            }

            if (value > 400)
            {
                return 400;
            }

            return value;
        }

        private static int NormalizeMouseThreshold(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 20)
            {
                return 20;
            }

            return value;
        }

        private static int NormalizeMouseAxis(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 2)
            {
                return 2;
            }

            return value;
        }

        private static int NormalizeMouseGain(int value)
        {
            if (value < 25)
            {
                return 25;
            }

            if (value > 400)
            {
                return 400;
            }

            return value;
        }

        private static int NormalizeStickSensitivity(int value)
        {
            if (value < 1)
            {
                return 1;
            }

            if (value > 400)
            {
                return 400;
            }

            return value;
        }

        private static int NormalizeStickThreshold(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 20)
            {
                return 20;
            }

            return value;
        }

        private static int NormalizeStickAxis(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 2)
            {
                return 2;
            }

            return value;
        }

        private static int NormalizeStickGain(int value)
        {
            if (value < 25)
            {
                return 25;
            }

            if (value > 400)
            {
                return 400;
            }

            return value;
        }

        private static int NormalizeStickSelect(int value)
        {
            return value == 0 ? 0 : 1;
        }

        private static int NormalizeStickRange(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 200)
            {
                return 200;
            }

            return value;
        }

        private static int NormalizeVirtualAbxyLayout(int value)
        {
            return value == 1 ? 1 : 0;
        }

        private void LoadSettings()
        {
            try
            {
                if (LocalSettingsHelper.TryGetValue("ControllerEmulationEnabled", out bool savedEnabled))
                {
                    enabled = savedEnabled;
                }
                else
                {
                    // Backward compatibility: existing installs behaved as enabled.
                    enabled = true;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationHideStockController", out bool savedHideStockController))
                {
                    hideStockController = savedHideStockController;
                }
                else
                {
                    // Preserve current behavior for existing installs where suppression was always attempted.
                    hideStockController = true;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationHideTarget", out int savedHideTarget))
                {
                    hideTarget = NormalizeHideTarget(savedHideTarget);
                }
                else
                {
                    hideTarget = 0;
                }

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

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationGyroActivationMode", out int savedGyroActivationMode))
                {
                    gyroActivationMode = NormalizeGyroActivationMode(savedGyroActivationMode);
                }
                else
                {
                    gyroActivationMode = 0;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationGyroActivationButton", out int savedGyroActivationButton))
                {
                    gyroActivationButton = NormalizeGyroActivationButton(savedGyroActivationButton);
                }
                else
                {
                    gyroActivationButton = 1;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationDs4Orientation", out int savedDs4Orientation))
                {
                    ds4Orientation = NormalizeDs4Orientation(savedDs4Orientation);
                }
                else
                {
                    ds4Orientation = 0;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationPs4TouchpadEnabled", out bool savedPs4TouchpadEnabled))
                {
                    ps4TouchpadEnabled = savedPs4TouchpadEnabled;
                }
                else
                {
                    ps4TouchpadEnabled = true;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseSensitivity", out int savedSensitivity))
                {
                    mouseSensitivity = NormalizeMouseSensitivity(savedSensitivity);
                }
                else
                {
                    mouseSensitivity = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseThreshold", out int savedThreshold))
                {
                    mouseThreshold = NormalizeMouseThreshold(savedThreshold);
                }
                else
                {
                    mouseThreshold = 2;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseAxis", out int savedAxis))
                {
                    mouseAxis = NormalizeMouseAxis(savedAxis);
                }
                else
                {
                    mouseAxis = 0;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseInvertX", out bool savedInvertX))
                {
                    mouseInvertX = savedInvertX;
                }
                else
                {
                    mouseInvertX = false;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseInvertY", out bool savedInvertY))
                {
                    mouseInvertY = savedInvertY;
                }
                else
                {
                    mouseInvertY = false;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseGainX", out int savedGainX))
                {
                    mouseGainX = NormalizeMouseGain(savedGainX);
                }
                else
                {
                    mouseGainX = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationMouseGainY", out int savedGainY))
                {
                    mouseGainY = NormalizeMouseGain(savedGainY);
                }
                else
                {
                    mouseGainY = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickSensitivity", out int savedStickSensitivity))
                {
                    stickSensitivity = NormalizeStickSensitivity(savedStickSensitivity);
                }
                else
                {
                    stickSensitivity = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickThreshold", out int savedStickThreshold))
                {
                    stickThreshold = NormalizeStickThreshold(savedStickThreshold);
                }
                else
                {
                    stickThreshold = 2;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickAxis", out int savedStickAxis))
                {
                    stickAxis = NormalizeStickAxis(savedStickAxis);
                }
                else
                {
                    stickAxis = 0;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickInvertX", out bool savedStickInvertX))
                {
                    stickInvertX = savedStickInvertX;
                }
                else
                {
                    stickInvertX = false;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickInvertY", out bool savedStickInvertY))
                {
                    stickInvertY = savedStickInvertY;
                }
                else
                {
                    stickInvertY = false;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickGainX", out int savedStickGainX))
                {
                    stickGainX = NormalizeStickGain(savedStickGainX);
                }
                else
                {
                    stickGainX = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickGainY", out int savedStickGainY))
                {
                    stickGainY = NormalizeStickGain(savedStickGainY);
                }
                else
                {
                    stickGainY = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickSelect", out int savedStickSelect))
                {
                    stickSelect = NormalizeStickSelect(savedStickSelect);
                }
                else
                {
                    stickSelect = 1;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickExcessMove", out bool savedStickExcessMove))
                {
                    stickExcessMove = savedStickExcessMove;
                }
                else
                {
                    stickExcessMove = false;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickRange", out int savedStickRange))
                {
                    stickRange = NormalizeStickRange(savedStickRange);
                }
                else
                {
                    stickRange = 100;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationStickOnlyJoystickData", out bool savedStickOnlyJoystickData))
                {
                    stickOnlyJoystickData = savedStickOnlyJoystickData;
                }
                else
                {
                    stickOnlyJoystickData = false;
                }

                if (LocalSettingsHelper.TryGetValue("ControllerEmulationVirtualABXYLayout", out int savedVirtualAbxyLayout))
                {
                    virtualAbxyLayout = NormalizeVirtualAbxyLayout(savedVirtualAbxyLayout);
                }
                else
                {
                    virtualAbxyLayout = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Controller emulation settings load failed: {ex.Message}");
                enabled = true;
                hideStockController = true;
                hideTarget = 0;
                gyroSource = 0;
                mode = 0;
                gyroActivationMode = 0;
                gyroActivationButton = 1;
                ds4Orientation = 0;
                ps4TouchpadEnabled = true;
                mouseSensitivity = 100;
                mouseThreshold = 2;
                mouseAxis = 0;
                mouseInvertX = false;
                mouseInvertY = false;
                mouseGainX = 100;
                mouseGainY = 100;
                stickSensitivity = 100;
                stickThreshold = 2;
                stickAxis = 0;
                stickInvertX = false;
                stickInvertY = false;
                stickGainX = 100;
                stickGainY = 100;
                stickSelect = 1;
                stickExcessMove = false;
                stickRange = 100;
                stickOnlyJoystickData = false;
                virtualAbxyLayout = 0;
            }
        }

        private void SaveSettings()
        {
            try
            {
                LocalSettingsHelper.SetValue("ControllerEmulationEnabled", enabled);
                LocalSettingsHelper.SetValue("ControllerEmulationHideStockController", hideStockController);
                LocalSettingsHelper.SetValue("ControllerEmulationHideTarget", hideTarget);
                LocalSettingsHelper.SetValue("ControllerEmulationGyroSource", gyroSource);
                LocalSettingsHelper.SetValue("ControllerEmulationMode", mode);
                LocalSettingsHelper.SetValue("ControllerEmulationGyroActivationMode", gyroActivationMode);
                LocalSettingsHelper.SetValue("ControllerEmulationGyroActivationButton", gyroActivationButton);
                LocalSettingsHelper.SetValue("ControllerEmulationDs4Orientation", ds4Orientation);
                LocalSettingsHelper.SetValue("ControllerEmulationPs4TouchpadEnabled", ps4TouchpadEnabled);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseSensitivity", mouseSensitivity);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseThreshold", mouseThreshold);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseAxis", mouseAxis);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseInvertX", mouseInvertX);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseInvertY", mouseInvertY);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseGainX", mouseGainX);
                LocalSettingsHelper.SetValue("ControllerEmulationMouseGainY", mouseGainY);
                LocalSettingsHelper.SetValue("ControllerEmulationStickSensitivity", stickSensitivity);
                LocalSettingsHelper.SetValue("ControllerEmulationStickThreshold", stickThreshold);
                LocalSettingsHelper.SetValue("ControllerEmulationStickAxis", stickAxis);
                LocalSettingsHelper.SetValue("ControllerEmulationStickInvertX", stickInvertX);
                LocalSettingsHelper.SetValue("ControllerEmulationStickInvertY", stickInvertY);
                LocalSettingsHelper.SetValue("ControllerEmulationStickGainX", stickGainX);
                LocalSettingsHelper.SetValue("ControllerEmulationStickGainY", stickGainY);
                LocalSettingsHelper.SetValue("ControllerEmulationStickSelect", stickSelect);
                LocalSettingsHelper.SetValue("ControllerEmulationStickExcessMove", stickExcessMove);
                LocalSettingsHelper.SetValue("ControllerEmulationStickRange", stickRange);
                LocalSettingsHelper.SetValue("ControllerEmulationStickOnlyJoystickData", stickOnlyJoystickData);
                LocalSettingsHelper.SetValue("ControllerEmulationVirtualABXYLayout", virtualAbxyLayout);

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
                suppressionPausedForGameBar = false;
                suppressionPauseUntilTicksUtc = 0;
                Logger.Debug($"Skipping controller emulation apply ({reason}): unsupported device type {deviceType}");
                return;
            }

            if (!enabled)
            {
                StopForwarding();
                suppressionPausedForGameBar = false;
                suppressionPauseUntilTicksUtc = 0;
                Logger.Info($"Controller emulation disabled ({reason}); forwarding stopped");
                return;
            }

            // Controller emulation is intentionally software-only and independent from
            // per-device firmware gyro settings exposed in the Legion tab.
            bool backendApplied = true;
            Logger.Debug($"Controller emulation using software path only ({reason}); skipping device firmware gyro mapping");

            // Rebuild forwarding/runtime state for every apply-triggering setting change.
            // This avoids races between live submit loop and virtual device reconnect/dispose.
            // Keep suppression active across virtual-mode switches to avoid unhide/rehide port churn.
            bool preserveSuppressionAcrossRestart =
                suppressionActive &&
                hideStockController &&
                RequiresSoftwareForwarding(mode) &&
                RequiresVirtualGamepad(mode);
            StopForwarding(preserveSuppressionAcrossRestart);
            bool forwardingApplied = ConfigureForwarding(reason);

            if (backendApplied || forwardingApplied)
            {
                Logger.Info($"Controller emulation applied ({reason}): source={gyroSource}, mode={mode}, gyroActivationMode={gyroActivationMode}, gyroActivationButton={gyroActivationButton}, ds4Orientation={ds4Orientation}, ps4TouchpadEnabled={ps4TouchpadEnabled}, hideStockController={hideStockController}, hideTarget={hideTarget}, device={deviceType}, backend={backendApplied}, forwarding={forwardingApplied}");
            }
            else
            {
                Logger.Warn($"Controller emulation apply not completed ({reason}): source={gyroSource}, mode={mode}, gyroActivationMode={gyroActivationMode}, gyroActivationButton={gyroActivationButton}, ds4Orientation={ds4Orientation}, ps4TouchpadEnabled={ps4TouchpadEnabled}, hideStockController={hideStockController}, hideTarget={hideTarget}, device={deviceType}, backend={backendApplied}, forwarding={forwardingApplied}");
            }
        }

        private bool ConfigureForwarding(string reason)
        {
            if (!RequiresSoftwareForwarding(mode))
            {
                Logger.Info($"Controller emulation forwarding disabled ({reason}) for mode {mode}");
                return true;
            }

            bool gyroReady = ConfigureGyroSourceAdapter(reason);
            bool needsVirtualGamepad = RequiresVirtualGamepad(mode);

            if (!needsVirtualGamepad)
            {
                if (gyroActivationMode != 0 && !EnsureXInputLoaded())
                {
                    Logger.Warn("Controller emulation gyro activation is set to Hold/Toggle but XInput is unavailable");
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

            var targetType = mode == 1
                ? ViGEmController.VirtualGamepadType.Xbox360
                : ViGEmController.VirtualGamepadType.DualShock4;

            int? preferredPhysicalIndex = null;
            IReadOnlyCollection<string> xboxBridgeIdsBeforeVirtualConnect = null;
            if (targetType == ViGEmController.VirtualGamepadType.Xbox360)
            {
                preferredPhysicalIndex = DiscoverPreferredPhysicalXboxIndex(null);
                xboxBridgeIdsBeforeVirtualConnect = ControllerSuppressionManager.QueryXboxBridgeDeviceIds();
            }

            if (virtualController == null)
            {
                virtualController = new ViGEmController();
            }

            if (!virtualController.EnsureConnected(targetType))
            {
                Logger.Warn($"Controller emulation forwarding: failed to connect virtual {targetType} controller");
                return false;
            }

            if (targetType == ViGEmController.VirtualGamepadType.Xbox360)
            {
                UpdateVirtualXboxBridgeDeviceIds(xboxBridgeIdsBeforeVirtualConnect);

                virtualXboxUserIndex = TryGetVirtualXboxUserIndexSafe();
                if (!virtualXboxUserIndex.HasValue)
                {
                    Logger.Warn("Controller emulation virtual Xbox user index not reported yet; forwarding will continue with fallback physical source selection");
                }

                if (preferredPhysicalIndex.HasValue &&
                    (!virtualXboxUserIndex.HasValue || preferredPhysicalIndex.Value != virtualXboxUserIndex.Value))
                {
                    physicalXboxUserIndex = preferredPhysicalIndex;
                }
                else
                {
                    physicalXboxUserIndex = null;
                }
            }
            else
            {
                virtualXboxUserIndex = null;
                physicalXboxUserIndex = null;
                virtualXboxBridgeDeviceIds.Clear();
            }

            StartForwarding();

            if (!hideStockController)
            {
                suppressionPausedForGameBar = false;
                suppressionPauseUntilTicksUtc = 0;
                DisableSuppression();
                Logger.Info($"Controller emulation suppression disabled by setting ({reason})");
                return gyroReady;
            }

            if (TryPauseSuppressionForForegroundGameBar(reason))
            {
                return gyroReady;
            }

            bool suppressionReady = EnableSuppression(reason);
            if (!suppressionReady)
            {
                Logger.Warn($"Controller emulation suppression unavailable ({reason}); forwarding continues without HidHide cloaking");
            }

            return gyroReady;
        }

        private void ApplySuppressionConfiguration(string reason)
        {
            if (!isSupported || !enabled || !RequiresSoftwareForwarding(mode) || !RequiresVirtualGamepad(mode))
            {
                suppressionPausedForGameBar = false;
                suppressionPauseUntilTicksUtc = 0;
                DisableSuppression();
                Logger.Info($"Controller emulation suppression skipped ({reason})");
                return;
            }

            if (!hideStockController)
            {
                suppressionPausedForGameBar = false;
                suppressionPauseUntilTicksUtc = 0;
                DisableSuppression();
                Logger.Info($"Controller emulation suppression disabled by setting ({reason})");
                return;
            }

            if (TryPauseSuppressionForForegroundGameBar(reason))
            {
                return;
            }

            bool suppressionReady = EnableSuppression(reason);
            if (!suppressionReady)
            {
                Logger.Warn($"Controller emulation suppression unavailable ({reason}); forwarding continues without HidHide cloaking");
            }
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

            ResetMouseRuntimeState();
            ResetStickRuntimeState();
            ResetGyroActivationRuntimeState();
            physicalXboxUserIndex = null;
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

        private void StopForwarding(bool preserveSuppression = false)
        {
            forwardingRunning = false;
            if (forwardingThread != null && forwardingThread.IsAlive)
            {
                forwardingThread.Join(500);
            }

            forwardingThread = null;
            virtualXboxUserIndex = null;
            physicalXboxUserIndex = null;
            virtualXboxBridgeDeviceIds.Clear();
            ResetMouseRuntimeState();
            ResetStickRuntimeState();
            ResetGyroActivationRuntimeState();

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

            if (!preserveSuppression)
            {
                DisableSuppression();
            }
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
                        XINPUT_STATE activationState = default;
                        bool hasActivationState = false;
                        if (gyroActivationMode != 0)
                        {
                            if (xInputGetState == null)
                            {
                                EnsureXInputLoaded();
                            }

                            if (xInputGetState != null)
                            {
                                hasActivationState = TryReadPhysicalControllerState(out activationState);
                            }
                        }

                        bool mouseGyroActive = IsGyroActivationEnabled(hasActivationState ? activationState.Gamepad : (XINPUT_GAMEPAD?)null);
                        if (mouseGyroActive && TryReadGyroSample(out GyroSample mouseSample))
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

                    ushort forwardedButtons = ApplyVirtualAbxyLayout(state.Gamepad.wButtons);
                    byte forwardedLeftTrigger = state.Gamepad.bLeftTrigger;
                    byte forwardedRightTrigger = state.Gamepad.bRightTrigger;
                    short forwardedLeftX = state.Gamepad.sThumbLX;
                    short forwardedLeftY = state.Gamepad.sThumbLY;
                    short forwardedRightX = state.Gamepad.sThumbRX;
                    short forwardedRightY = state.Gamepad.sThumbRY;
                    bool ds4TouchActive = false;
                    ushort ds4TouchX = 0;
                    ushort ds4TouchY = 0;
                    bool gyroActive = IsGyroActivationEnabled(state.Gamepad);

                    if (mode == 1 || mode == 3)
                    {
                        if (gyroActive && TryReadGyroSample(out GyroSample stickSample))
                        {
                            ApplyStickFromGyro(stickSample, out lastGyroStickX, out lastGyroStickY);
                            hasLastGyroStick = true;
                            lastGyroStickTicksUtc = stickSample.TimestampTicksUtc > 0
                                ? stickSample.TimestampTicksUtc
                                : DateTime.UtcNow.Ticks;
                        }
                        else if (!gyroActive)
                        {
                            lastGyroStickX = 0;
                            lastGyroStickY = 0;
                            hasLastGyroStick = false;
                            lastGyroStickTicksUtc = 0;
                        }

                        if (hasLastGyroStick)
                        {
                            long nowTicksUtc = DateTime.UtcNow.Ticks;
                            if (lastGyroStickTicksUtc > 0 && (nowTicksUtc - lastGyroStickTicksUtc) > StickOutputStaleTicks)
                            {
                                lastGyroStickX = 0;
                                lastGyroStickY = 0;
                                hasLastGyroStick = false;
                            }
                        }

                        if (hasLastGyroStick)
                        {
                            if (stickSelect == 0)
                            {
                                MergeStickVectors(
                                    forwardedLeftX,
                                    forwardedLeftY,
                                    lastGyroStickX,
                                    lastGyroStickY,
                                    out forwardedLeftX,
                                    out forwardedLeftY);
                            }
                            else
                            {
                                MergeStickVectors(
                                    forwardedRightX,
                                    forwardedRightY,
                                    lastGyroStickX,
                                    lastGyroStickY,
                                    out forwardedRightX,
                                    out forwardedRightY);
                            }
                        }
                    }

                    if ((mode == 1 || mode == 3) && stickOnlyJoystickData)
                    {
                        forwardedButtons = 0;
                        forwardedLeftTrigger = 0;
                        forwardedRightTrigger = 0;
                    }

                    if ((mode == 2 || mode == 3) && ps4TouchpadEnabled)
                    {
                        TryReadDs4TouchSample(out ds4TouchActive, out ds4TouchX, out ds4TouchY);
                    }

                    bool ok = false;
                    if (mode == 1)
                    {
                        ok = SubmitXboxState(
                            forwardedButtons,
                            forwardedLeftTrigger,
                            forwardedRightTrigger,
                            forwardedLeftX,
                            forwardedLeftY,
                            forwardedRightX,
                            forwardedRightY);
                    }
                    else if (mode == 2)
                    {
                        short gyroX = 0;
                        short gyroY = 0;
                        short gyroZ = 0;
                        short accelX = 0;
                        short accelY = 0;
                        short accelZ = 0;

                        if (gyroActive && TryReadGyroSample(out GyroSample motionSample))
                        {
                            float gyroXValue = motionSample.GyroXDegPerSecond;
                            float gyroYValue = motionSample.GyroYDegPerSecond;
                            float gyroZValue = motionSample.GyroZDegPerSecond;
                            float accelXValue = motionSample.AccelXG;
                            float accelYValue = motionSample.AccelYG;
                            float accelZValue = motionSample.AccelZG;

                            ApplyDs4Orientation(
                                ref gyroXValue,
                                ref gyroYValue,
                                ref gyroZValue,
                                ref accelXValue,
                                ref accelYValue,
                                ref accelZValue);

                            gyroX = ConvertSignedRangeToInt16(gyroXValue, GyroDs4MaxDegPerSecond);
                            gyroY = ConvertSignedRangeToInt16(gyroYValue, GyroDs4MaxDegPerSecond);
                            gyroZ = ConvertSignedRangeToInt16(gyroZValue, GyroDs4MaxDegPerSecond);
                            accelX = ConvertSignedRangeToInt16(accelXValue, AccelDs4MaxG);
                            accelY = ConvertSignedRangeToInt16(accelYValue, AccelDs4MaxG);
                            accelZ = ConvertSignedRangeToInt16(accelZValue, AccelDs4MaxG);
                        }

                        ok = SubmitDualShock4StateRaw(
                            forwardedButtons,
                            forwardedLeftTrigger,
                            forwardedRightTrigger,
                            forwardedLeftX,
                            forwardedLeftY,
                            forwardedRightX,
                            forwardedRightY,
                            gyroX,
                            gyroY,
                            gyroZ,
                            accelX,
                            accelY,
                            accelZ,
                            ds4TouchActive,
                            ds4TouchX,
                            ds4TouchY);
                    }
                    else if (mode == 3)
                    {
                        ok = SubmitDualShock4StateRaw(
                            forwardedButtons,
                            forwardedLeftTrigger,
                            forwardedRightTrigger,
                            forwardedLeftX,
                            forwardedLeftY,
                            forwardedRightX,
                            forwardedRightY,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            ds4TouchActive,
                            ds4TouchX,
                            ds4TouchY);
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

        private void ResetMouseRuntimeState()
        {
            mouseCarryX = 0.0f;
            mouseCarryY = 0.0f;
            mouseFilteredHorizontal = 0.0f;
            mouseFilteredVertical = 0.0f;
            mouseFilteredDerivativeHorizontal = 0.0f;
            mouseFilteredDerivativeVertical = 0.0f;
            mouseFilterInitialized = false;
            mouseLastSampleTicksUtc = 0;
        }

        private void ResetStickRuntimeState()
        {
            lastGyroStickX = 0;
            lastGyroStickY = 0;
            hasLastGyroStick = false;
            lastGyroStickTicksUtc = 0;
        }

        private void ResetGyroActivationRuntimeState()
        {
            gyroToggleActive = false;
            lastGyroActivationButtonPressed = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ApplyDeadzone(float value, float deadzone)
        {
            float magnitude = Math.Abs(value);
            if (magnitude <= deadzone)
            {
                return 0.0f;
            }

            return Math.Sign(value) * (magnitude - deadzone);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeOneEuroAlpha(float cutoff, float deltaSeconds)
        {
            if (cutoff <= 0.0f)
            {
                return 1.0f;
            }

            float dt = Math.Max(0.0005f, deltaSeconds);
            float tau = 1.0f / (2.0f * (float)Math.PI * cutoff);
            return 1.0f / (1.0f + (tau / dt));
        }

        private static float ApplyOneEuroAxis(
            float rawValue,
            float previousFilteredValue,
            float previousFilteredDerivative,
            float deltaSeconds,
            out float filteredValue,
            out float filteredDerivative)
        {
            float dx = (rawValue - previousFilteredValue) / Math.Max(0.0005f, deltaSeconds);

            float derivativeAlpha = ComputeOneEuroAlpha(MouseOneEuroDerivativeCutoff, deltaSeconds);
            filteredDerivative = previousFilteredDerivative + ((dx - previousFilteredDerivative) * derivativeAlpha);

            float dynamicCutoff = MouseOneEuroMinCutoff + (MouseOneEuroBeta * Math.Abs(filteredDerivative));
            float valueAlpha = ComputeOneEuroAlpha(dynamicCutoff, deltaSeconds);
            filteredValue = previousFilteredValue + ((rawValue - previousFilteredValue) * valueAlpha);
            return filteredValue;
        }

        private void SubmitRelativeMouseMove(int deltaX, int deltaY)
        {
            if (deltaX == 0 && deltaY == 0)
            {
                return;
            }

            mouseMoveInputBuffer[0] = new INPUT
            {
                type = INPUT_MOUSE,
                U = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = deltaX,
                        dy = deltaY,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero,
                    },
                },
            };

            SendInput(1, mouseMoveInputBuffer, MouseInputStructSize);
        }

        private void ApplyMouseFromGyro(GyroSample sample)
        {
            // Axis selection:
            // 0 = Yaw/Pitch (Y->X, X->Y)
            // 1 = Yaw/Roll  (Y->X, Z->Y)
            // 2 = Roll/Pitch (Z->X, X->Y)
            float horizontal;
            float vertical;
            switch (mouseAxis)
            {
                case 1:
                    horizontal = sample.GyroYDegPerSecond;
                    vertical = sample.GyroZDegPerSecond;
                    break;
                case 2:
                    horizontal = sample.GyroZDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
                default:
                    horizontal = sample.GyroYDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
            }

            if (mouseInvertX)
            {
                horizontal = -horizontal;
            }

            if (mouseInvertY)
            {
                vertical = -vertical;
            }

            float threshold = Math.Max(0.0f, mouseThreshold);
            horizontal = ApplyDeadzone(horizontal, threshold);
            vertical = ApplyDeadzone(vertical, threshold);
            horizontal = Math.Max(-MouseMaxDegPerSecond, Math.Min(MouseMaxDegPerSecond, horizontal));
            vertical = Math.Max(-MouseMaxDegPerSecond, Math.Min(MouseMaxDegPerSecond, vertical));

            long sampleTicks = sample.TimestampTicksUtc > 0 ? sample.TimestampTicksUtc : DateTime.UtcNow.Ticks;
            float deltaSeconds = DefaultDeltaSeconds;
            if (mouseLastSampleTicksUtc > 0)
            {
                long deltaTicks = sampleTicks - mouseLastSampleTicksUtc;
                if (deltaTicks > 0 && deltaTicks < TimeSpan.TicksPerSecond)
                {
                    deltaSeconds = deltaTicks / (float)TimeSpan.TicksPerSecond;
                }
            }

            mouseLastSampleTicksUtc = sampleTicks;
            if (deltaSeconds < MinDeltaSeconds)
            {
                deltaSeconds = MinDeltaSeconds;
            }
            else if (deltaSeconds > MaxDeltaSeconds)
            {
                deltaSeconds = MaxDeltaSeconds;
            }

            if (!mouseFilterInitialized)
            {
                mouseFilteredHorizontal = horizontal;
                mouseFilteredVertical = vertical;
                mouseFilteredDerivativeHorizontal = 0.0f;
                mouseFilteredDerivativeVertical = 0.0f;
                mouseFilterInitialized = true;
            }
            else
            {
                // Reject single-sample axis spikes that can cause large cursor jumps.
                horizontal = Math.Max(mouseFilteredHorizontal - MouseOutlierMaxDeltaDegPerSecond, Math.Min(mouseFilteredHorizontal + MouseOutlierMaxDeltaDegPerSecond, horizontal));
                vertical = Math.Max(mouseFilteredVertical - MouseOutlierMaxDeltaDegPerSecond, Math.Min(mouseFilteredVertical + MouseOutlierMaxDeltaDegPerSecond, vertical));

                ApplyOneEuroAxis(
                    horizontal,
                    mouseFilteredHorizontal,
                    mouseFilteredDerivativeHorizontal,
                    deltaSeconds,
                    out mouseFilteredHorizontal,
                    out mouseFilteredDerivativeHorizontal);

                ApplyOneEuroAxis(
                    vertical,
                    mouseFilteredVertical,
                    mouseFilteredDerivativeVertical,
                    deltaSeconds,
                    out mouseFilteredVertical,
                    out mouseFilteredDerivativeVertical);
            }

            if (Math.Abs(mouseFilteredHorizontal) < MouseResidualCutoffDegPerSecond)
            {
                mouseFilteredHorizontal = 0.0f;
            }

            if (Math.Abs(mouseFilteredVertical) < MouseResidualCutoffDegPerSecond)
            {
                mouseFilteredVertical = 0.0f;
            }

            float normalizedSensitivity = Math.Max(0.05f, mouseSensitivity / 100.0f);
            float sensitivityScale = (float)Math.Pow(normalizedSensitivity, MouseSensitivityPower);
            float gainXScale = mouseGainX / 100.0f;
            float gainYScale = mouseGainY / 100.0f;

            float moveX = (mouseFilteredHorizontal * deltaSeconds * MousePixelsPerDegree * sensitivityScale * gainXScale) + mouseCarryX;
            float moveY = ((-mouseFilteredVertical) * deltaSeconds * MousePixelsPerDegree * sensitivityScale * gainYScale) + mouseCarryY;

            int deltaX = (int)Math.Round(moveX);
            int deltaY = (int)Math.Round(moveY);

            bool clampedX = false;
            bool clampedY = false;
            if (deltaX > MouseMaxPixelsPerFrame)
            {
                deltaX = MouseMaxPixelsPerFrame;
                clampedX = true;
            }
            else if (deltaX < -MouseMaxPixelsPerFrame)
            {
                deltaX = -MouseMaxPixelsPerFrame;
                clampedX = true;
            }

            if (deltaY > MouseMaxPixelsPerFrame)
            {
                deltaY = MouseMaxPixelsPerFrame;
                clampedY = true;
            }
            else if (deltaY < -MouseMaxPixelsPerFrame)
            {
                deltaY = -MouseMaxPixelsPerFrame;
                clampedY = true;
            }

            mouseCarryX = clampedX ? 0.0f : (moveX - deltaX);
            mouseCarryY = clampedY ? 0.0f : (moveY - deltaY);

            if (deltaX != 0 || deltaY != 0)
            {
                SubmitRelativeMouseMove(deltaX, deltaY);
            }
        }

        private void ApplyStickFromGyro(
            GyroSample sample,
            out short outputX,
            out short outputY)
        {
            outputX = 0;
            outputY = 0;

            float horizontal;
            float vertical;

            // Axis selection:
            // 0 = XY (Yaw)        => Y -> X, X -> Y
            // 1 = XZ (Roll)       => Z -> X, X -> Y
            // 2 = Yaw + Pitch     => Y -> X, X -> Y (explicit yaw/pitch mode)
            switch (stickAxis)
            {
                case 1:
                    horizontal = sample.GyroZDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
                case 2:
                    horizontal = sample.GyroYDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
                default:
                    horizontal = sample.GyroYDegPerSecond;
                    vertical = sample.GyroXDegPerSecond;
                    break;
            }

            if (stickInvertX)
            {
                horizontal = -horizontal;
            }

            if (stickInvertY)
            {
                vertical = -vertical;
            }

            float threshold = Math.Max(0.0f, stickThreshold);
            horizontal = ApplyDeadzone(horizontal, threshold);
            vertical = ApplyDeadzone(vertical, threshold);

            horizontal = Math.Max(-MouseMaxDegPerSecond, Math.Min(MouseMaxDegPerSecond, horizontal));
            vertical = Math.Max(-MouseMaxDegPerSecond, Math.Min(MouseMaxDegPerSecond, vertical));

            float sensitivityScale = Math.Max(0.05f, stickSensitivity / 100.0f);
            float gainXScale = stickGainX / 100.0f;
            float gainYScale = stickGainY / 100.0f;
            float rangeScale = stickRange / 100.0f;

            float normalizedX = (horizontal / StickDegreesPerSecondAtFullDeflection) * sensitivityScale * gainXScale * rangeScale;
            float normalizedY = ((-vertical) / StickDegreesPerSecondAtFullDeflection) * sensitivityScale * gainYScale * rangeScale;

            if (stickExcessMove)
            {
                normalizedX = Math.Max(-1.0f, Math.Min(1.0f, normalizedX));
                normalizedY = Math.Max(-1.0f, Math.Min(1.0f, normalizedY));
            }
            else
            {
                float magnitude = (float)Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
                if (magnitude > 1.0f && magnitude > 0.0f)
                {
                    float invMagnitude = 1.0f / magnitude;
                    normalizedX *= invMagnitude;
                    normalizedY *= invMagnitude;
                }
            }

            outputX = ConvertNormalizedToInt16(normalizedX);
            outputY = ConvertNormalizedToInt16(normalizedY);
        }

        private void ApplyDs4Orientation(
            ref float gyroX,
            ref float gyroY,
            ref float gyroZ,
            ref float accelX,
            ref float accelY,
            ref float accelZ)
        {
            if (ds4Orientation != 1)
            {
                return;
            }

            // Orthogonal mode rotates around Y so DS4 motion orientation matches
            // users holding the handheld in a perpendicular posture.
            float originalGyroX = gyroX;
            float originalGyroZ = gyroZ;
            gyroX = originalGyroZ;
            gyroZ = -originalGyroX;

            float originalAccelX = accelX;
            float originalAccelZ = accelZ;
            accelX = originalAccelZ;
            accelZ = -originalAccelX;
        }

        private static short ConvertNormalizedToInt16(float normalized)
        {
            float clamped = Math.Max(-1.0f, Math.Min(1.0f, normalized));
            return (short)Math.Round(clamped * short.MaxValue);
        }

        private static void MergeStickVectors(
            short physicalX,
            short physicalY,
            short gyroX,
            short gyroY,
            out short mergedX,
            out short mergedY)
        {
            float sumX = physicalX + gyroX;
            float sumY = physicalY + gyroY;
            float magnitude = (float)Math.Sqrt((sumX * sumX) + (sumY * sumY));
            if (magnitude > short.MaxValue && magnitude > 0.0f)
            {
                float scale = short.MaxValue / magnitude;
                sumX *= scale;
                sumY *= scale;
            }

            mergedX = ClampToInt16(sumX);
            mergedY = ClampToInt16(sumY);
        }

        private static short ClampToInt16(float value)
        {
            if (value > short.MaxValue)
            {
                return short.MaxValue;
            }

            if (value < short.MinValue)
            {
                return short.MinValue;
            }

            return (short)Math.Round(value);
        }

        private bool IsGyroActivationEnabled(XINPUT_GAMEPAD? gamepad)
        {
            switch (gyroActivationMode)
            {
                case 1:
                    if (!gamepad.HasValue)
                    {
                        lastGyroActivationButtonPressed = false;
                        return false;
                    }

                    bool holdPressed = IsGyroActivationButtonPressed(gamepad.Value);
                    lastGyroActivationButtonPressed = holdPressed;
                    return holdPressed;

                case 2:
                    bool togglePressed = gamepad.HasValue && IsGyroActivationButtonPressed(gamepad.Value);
                    if (togglePressed && !lastGyroActivationButtonPressed)
                    {
                        gyroToggleActive = !gyroToggleActive;
                    }

                    lastGyroActivationButtonPressed = togglePressed;
                    return gyroToggleActive;

                default:
                    lastGyroActivationButtonPressed = gamepad.HasValue && IsGyroActivationButtonPressed(gamepad.Value);
                    return true;
            }
        }

        private bool IsGyroActivationButtonPressed(XINPUT_GAMEPAD gamepad)
        {
            switch (gyroActivationButton)
            {
                case 1:
                    return gamepad.bRightTrigger > XINPUT_TRIGGER_THRESHOLD;
                case 2:
                    return gamepad.bLeftTrigger > XINPUT_TRIGGER_THRESHOLD;
                case 3:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case 4:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                case 5:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_A) != 0;
                case 6:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_B) != 0;
                case 7:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_X) != 0;
                case 8:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_Y) != 0;
                case 9:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_THUMB) != 0;
                case 10:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_LEFT_THUMB) != 0;
                case 11:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_UP) != 0;
                case 12:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_DOWN) != 0;
                case 13:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_LEFT) != 0;
                case 14:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0;
                case 15:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_START) != 0;
                case 16:
                    return (gamepad.wButtons & XINPUT_GAMEPAD_BACK) != 0;
                default:
                    return false;
            }
        }

        private ushort ApplyVirtualAbxyLayout(ushort buttons)
        {
            if (virtualAbxyLayout != 1)
            {
                return buttons;
            }

            bool aPressed = (buttons & XINPUT_GAMEPAD_A) != 0;
            bool bPressed = (buttons & XINPUT_GAMEPAD_B) != 0;
            bool xPressed = (buttons & XINPUT_GAMEPAD_X) != 0;
            bool yPressed = (buttons & XINPUT_GAMEPAD_Y) != 0;

            ushort remapped = (ushort)(buttons & ~(XINPUT_GAMEPAD_A | XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_X | XINPUT_GAMEPAD_Y));
            if (bPressed)
            {
                remapped |= XINPUT_GAMEPAD_A;
            }

            if (aPressed)
            {
                remapped |= XINPUT_GAMEPAD_B;
            }

            if (yPressed)
            {
                remapped |= XINPUT_GAMEPAD_X;
            }

            if (xPressed)
            {
                remapped |= XINPUT_GAMEPAD_Y;
            }

            return remapped;
        }

        private bool ConfigureGyroSourceAdapter(string reason)
        {
            if (mode < 0 || mode > 3)
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

        private bool TryReadDs4TouchSample(out bool isTouching, out ushort x, out ushort y)
        {
            isTouching = false;
            x = 0;
            y = 0;

            if (deviceType != SharedDeviceType.LegionGo && deviceType != SharedDeviceType.LegionGo2)
            {
                return false;
            }

            if (!LegionButtonMonitor.TryGetLatestRightTouchpadSample(out LegionTouchpadSample touchSample))
            {
                return false;
            }

            // Legion touch parser normalizes raw touch coordinates to 0..1023 on both axes.
            float normalizedX = Math.Max(0.0f, Math.Min(LegionTouchMaxX, touchSample.RawX)) / LegionTouchMaxX;
            float normalizedY = Math.Max(0.0f, Math.Min(LegionTouchMaxY, touchSample.RawY)) / LegionTouchMaxY;
            x = (ushort)Math.Round(normalizedX * Ds4TouchMaxX);
            y = (ushort)Math.Round(normalizedY * Ds4TouchMaxY);
            isTouching = touchSample.IsTouching;
            return true;
        }

        private bool EnableSuppression(string reason)
        {
            if (suppressionManager == null)
            {
                return false;
            }

            suppressionPausedForGameBar = false;
            suppressionPauseUntilTicksUtc = 0;
            bool wasActive = suppressionActive;
            IReadOnlyCollection<string> excludedIds = virtualXboxBridgeDeviceIds.Count > 0
                ? virtualXboxBridgeDeviceIds
                : null;
            suppressionActive = suppressionManager.Enable(deviceType, hideTarget, excludedIds);
            if (suppressionActive)
            {
                Logger.Info($"Controller suppression {(wasActive ? "updated" : "enabled")} ({reason}, target={hideTarget})");
            }
            else if (wasActive)
            {
                Logger.Info($"Controller suppression cleared ({reason}, target={hideTarget})");
            }

            return suppressionActive;
        }

        private bool ShouldManageSuppression()
        {
            return isSupported &&
                enabled &&
                hideStockController &&
                RequiresSoftwareForwarding(mode) &&
                RequiresVirtualGamepad(mode);
        }

        private static bool IsForegroundXboxGameBarProcess()
        {
            int foregroundProcessId = User32.GetForegroundProcessId();
            if (foregroundProcessId <= 0)
            {
                return false;
            }

            try
            {
                using Process process = Process.GetProcessById(foregroundProcessId);
                string processName = process.ProcessName;
                if (GameBarForegroundProcessNames.Contains(processName))
                {
                    return true;
                }

                if (processName.IndexOf("XboxGamingBar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    processName.IndexOf("XboxGameBar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                try
                {
                    string processPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(processPath) &&
                        (processPath.IndexOf("XboxGamingOverlay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         processPath.IndexOf("XboxGamingBar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         processPath.IndexOf("XboxGameBar", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Access to MainModule may fail for protected processes; process name check is sufficient.
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private bool IsWidgetForegroundSignalActive()
        {
            return hasWidgetForegroundSignal && widgetForegroundSignal;
        }

        private bool TryPauseSuppressionForForegroundGameBar(string reason)
        {
            if (!ShouldManageSuppression())
            {
                return false;
            }

            bool isForegroundGameBar = IsWidgetForegroundSignalActive() || IsForegroundXboxGameBarProcess();
            bool isGuidePauseActive = suppressionPauseUntilTicksUtc > DateTime.UtcNow.Ticks;
            if (!isForegroundGameBar && !isGuidePauseActive)
            {
                return false;
            }

            bool wasPaused = suppressionPausedForGameBar;
            suppressionPausedForGameBar = true;
            DisableSuppression();

            if (!wasPaused)
            {
                if (isForegroundGameBar)
                {
                    Logger.Info($"Controller suppression temporarily disabled while Xbox Game Bar is foreground ({reason})");
                }
                else
                {
                    Logger.Info($"Controller suppression temporarily disabled after guide press ({reason})");
                }
            }

            return true;
        }

        private void MonitorGameBarSuppressionState()
        {
            if (!ShouldManageSuppression())
            {
                gameBarForegroundConsecutiveTicks = 0;
                nonGameBarForegroundConsecutiveTicks = 0;
                suppressionPausedForGameBar = false;
                suppressionPauseUntilTicksUtc = 0;
                return;
            }

            if (IsWidgetForegroundSignalActive() || IsForegroundXboxGameBarProcess())
            {
                gameBarForegroundConsecutiveTicks++;
                nonGameBarForegroundConsecutiveTicks = 0;

                if (!suppressionPausedForGameBar &&
                    gameBarForegroundConsecutiveTicks >= GameBarForegroundStableTicks)
                {
                    suppressionPausedForGameBar = true;
                    DisableSuppression();
                    Logger.Info("Controller suppression temporarily disabled while Xbox Game Bar is foreground");
                }

                return;
            }

            if (suppressionPauseUntilTicksUtc > DateTime.UtcNow.Ticks)
            {
                gameBarForegroundConsecutiveTicks = 0;
                nonGameBarForegroundConsecutiveTicks = 0;

                if (!suppressionPausedForGameBar)
                {
                    suppressionPausedForGameBar = true;
                    DisableSuppression();
                    Logger.Info("Controller suppression temporarily disabled due to active guide pause");
                }

                return;
            }

            nonGameBarForegroundConsecutiveTicks++;
            gameBarForegroundConsecutiveTicks = 0;

            if (suppressionPausedForGameBar &&
                nonGameBarForegroundConsecutiveTicks >= GameBarBackgroundStableTicks)
            {
                suppressionPausedForGameBar = false;
                Logger.Info("Xbox Game Bar no longer foreground; restoring controller suppression");
                ApplySuppressionConfiguration("game bar no longer foreground");
            }
        }

        private void SubscribeForegroundSignal()
        {
            try
            {
                if (settingsManager != null && settingsManager.IsForeground != null)
                {
                    settingsManager.IsForeground.PropertyChanged += OnWidgetForegroundPropertyChanged;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Controller emulation failed to subscribe to widget foreground signal: {ex.Message}");
            }
        }

        private void UnsubscribeForegroundSignal()
        {
            try
            {
                if (settingsManager != null && settingsManager.IsForeground != null)
                {
                    settingsManager.IsForeground.PropertyChanged -= OnWidgetForegroundPropertyChanged;
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        private void OnWidgetForegroundPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e?.PropertyName) &&
                !string.Equals(e.PropertyName, "value", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!(sender is Settings.IsForegroundProperty foregroundProperty))
            {
                return;
            }

            hasWidgetForegroundSignal = true;
            widgetForegroundSignal = foregroundProperty.Value;
            Logger.Info($"Controller emulation Game Bar foreground signal: {widgetForegroundSignal}");

            if (!ShouldManageSuppression())
            {
                return;
            }

            if (widgetForegroundSignal)
            {
                TryPauseSuppressionForForegroundGameBar("widget foreground signal");
                return;
            }

            if (suppressionPausedForGameBar && suppressionPauseUntilTicksUtc <= DateTime.UtcNow.Ticks)
            {
                ApplySuppressionConfiguration("widget background signal");
            }
        }

        private void UpdateVirtualXboxBridgeDeviceIds(IReadOnlyCollection<string> bridgeIdsBeforeVirtualConnect)
        {
            virtualXboxBridgeDeviceIds.Clear();

            var before = bridgeIdsBeforeVirtualConnect == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(bridgeIdsBeforeVirtualConnect, StringComparer.OrdinalIgnoreCase);

            IReadOnlyCollection<string> after = ControllerSuppressionManager.QueryXboxBridgeDeviceIds();
            foreach (string id in after)
            {
                if (!before.Contains(id))
                {
                    virtualXboxBridgeDeviceIds.Add(id);
                }
            }

            Logger.Info($"Controller emulation tracked virtual Xbox bridge HID instance count: {virtualXboxBridgeDeviceIds.Count}");
        }

        private void DisableSuppression()
        {
            if (suppressionManager == null)
            {
                return;
            }

            bool shouldForceCleanup =
                !hideStockController ||
                !enabled ||
                !RequiresSoftwareForwarding(mode) ||
                !RequiresVirtualGamepad(mode);
            if (!suppressionActive && !shouldForceCleanup)
            {
                return;
            }

            bool wasActive = suppressionActive;
            suppressionManager.Disable();
            suppressionActive = false;
            if (wasActive)
            {
                Logger.Info("Controller suppression disabled");
            }
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

        private int? TryGetVirtualXboxUserIndexSafe()
        {
            if (virtualController == null)
            {
                return null;
            }

            try
            {
                return virtualController.VirtualXboxUserIndex;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Controller emulation virtual Xbox index read failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private bool SubmitXboxState(
            ushort buttons,
            byte leftTrigger,
            byte rightTrigger,
            short leftThumbX,
            short leftThumbY,
            short rightThumbX,
            short rightThumbY)
        {
            var controller = virtualController;
            if (controller == null)
            {
                return false;
            }

            lock (virtualControllerSync)
            {
                if (!ReferenceEquals(controller, virtualController))
                {
                    return false;
                }

                return controller.SubmitXboxState(
                    buttons,
                    leftTrigger,
                    rightTrigger,
                    leftThumbX,
                    leftThumbY,
                    rightThumbX,
                    rightThumbY);
            }
        }

        private bool SubmitDualShock4StateRaw(
            ushort buttons,
            byte leftTrigger,
            byte rightTrigger,
            short leftThumbX,
            short leftThumbY,
            short rightThumbX,
            short rightThumbY,
            short gyroXRaw,
            short gyroYRaw,
            short gyroZRaw,
            short accelXRaw,
            short accelYRaw,
            short accelZRaw,
            bool touchActive,
            ushort touchX,
            ushort touchY)
        {
            var controller = virtualController;
            if (controller == null)
            {
                return false;
            }

            lock (virtualControllerSync)
            {
                if (!ReferenceEquals(controller, virtualController))
                {
                    return false;
                }

                return controller.SubmitDualShock4StateRaw(
                    buttons,
                    leftTrigger,
                    rightTrigger,
                    leftThumbX,
                    leftThumbY,
                    rightThumbX,
                    rightThumbY,
                    gyroXRaw,
                    gyroYRaw,
                    gyroZRaw,
                    accelXRaw,
                    accelYRaw,
                    accelZRaw,
                    touchActive,
                    touchX,
                    touchY);
            }
        }

        private int? DiscoverPreferredPhysicalXboxIndex(int? excludedIndex)
        {
            if (xInputGetState == null)
            {
                return null;
            }

            for (uint index = 0; index < 4; index++)
            {
                if (excludedIndex.HasValue && index == (uint)excludedIndex.Value)
                {
                    continue;
                }

                XINPUT_STATE state = default;
                if (xInputGetState(index, ref state) == ERROR_SUCCESS)
                {
                    lastPacketByController[index] = state.dwPacketNumber;
                    return (int)index;
                }
            }

            return null;
        }

        private bool TryReadPhysicalControllerState(out XINPUT_STATE selectedState)
        {
            selectedState = default;
            if (xInputGetState == null)
            {
                return false;
            }

            if (!virtualXboxUserIndex.HasValue && mode == 1 && virtualController != null)
            {
                int? reportedVirtualIndex = TryGetVirtualXboxUserIndexSafe();
                if (reportedVirtualIndex.HasValue)
                {
                    virtualXboxUserIndex = reportedVirtualIndex;
                    if (physicalXboxUserIndex.HasValue && physicalXboxUserIndex.Value == virtualXboxUserIndex.Value)
                    {
                        physicalXboxUserIndex = null;
                    }

                    Logger.Info($"Controller emulation virtual Xbox index resolved at runtime: {virtualXboxUserIndex.Value}");
                }
            }

            bool virtualIndexUnknown = mode == 1 && virtualController != null && !virtualXboxUserIndex.HasValue;
            if (virtualIndexUnknown && !physicalXboxUserIndex.HasValue)
            {
                // Avoid selecting the virtual Xbox device as physical input while its XInput index
                // is still being reported by ViGEm. This prevents self-feedback/stuck input loops.
                return false;
            }

            bool hasLockedState = false;
            XINPUT_STATE lockedState = default;
            uint lockedIndex = 0;
            if (physicalXboxUserIndex.HasValue)
            {
                lockedIndex = (uint)physicalXboxUserIndex.Value;
                if (!virtualXboxUserIndex.HasValue || lockedIndex != (uint)virtualXboxUserIndex.Value)
                {
                    if (xInputGetState(lockedIndex, ref lockedState) == ERROR_SUCCESS)
                    {
                        hasLockedState = true;
                    }
                }

                if (!hasLockedState)
                {
                    physicalXboxUserIndex = null;
                }
            }

            bool foundAnyConnected = false;
            XINPUT_STATE firstConnectedState = default;
            uint firstConnectedIndex = 0;
            bool hasFirstConnected = false;
            XINPUT_STATE changedState = default;
            uint changedIndex = 0;
            bool hasChangedState = false;

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
                    firstConnectedIndex = index;
                    hasFirstConnected = true;
                }

                bool packetChanged = state.dwPacketNumber != lastPacketByController[index];
                if (!packetChanged)
                {
                    continue;
                }

                // Prefer states that changed since last sample.
                // If a locked index is active, keep it when it changes.
                if (hasLockedState && index == lockedIndex)
                {
                    changedState = state;
                    changedIndex = index;
                    hasChangedState = true;
                    break;
                }

                // Otherwise keep the first changed candidate and continue scanning in case
                // the locked index also changed in this cycle.
                if (!hasChangedState)
                {
                    changedState = state;
                    changedIndex = index;
                    hasChangedState = true;
                }
            }

            if (hasChangedState)
            {
                lastPacketByController[changedIndex] = changedState.dwPacketNumber;
                if (!physicalXboxUserIndex.HasValue || physicalXboxUserIndex.Value != (int)changedIndex)
                {
                    Logger.Debug($"Controller emulation physical source switched to XInput index {changedIndex}");
                }

                physicalXboxUserIndex = (int)changedIndex;
                selectedState = changedState;
                return true;
            }

            if (hasLockedState)
            {
                // Keep using the locked index when nothing else changed this poll.
                lastPacketByController[lockedIndex] = lockedState.dwPacketNumber;
                physicalXboxUserIndex = (int)lockedIndex;
                selectedState = lockedState;
                return true;
            }

            if (foundAnyConnected && hasFirstConnected)
            {
                lastPacketByController[firstConnectedIndex] = firstConnectedState.dwPacketNumber;
                physicalXboxUserIndex = (int)firstConnectedIndex;
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
                UnsubscribeForegroundSignal();
                if (ReferenceEquals(activeInstance, this))
                {
                    activeInstance = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}

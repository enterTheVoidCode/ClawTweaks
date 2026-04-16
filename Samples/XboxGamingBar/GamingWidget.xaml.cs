using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace XboxGamingBar
{
    /// <summary>
    /// Profile settings storage
    /// </summary>
    public class PerformanceProfile
    {
        public double TDP { get; set; } = 15;
        public bool CPUBoost { get; set; } = false;
        public double CPUEPP { get; set; } = 0;
        public int MaxCPUState { get; set; } = 100;
        public int MinCPUState { get; set; } = 5;
        public bool FluidMotionFrames { get; set; } = false;
        public bool RadeonSuperResolution { get; set; } = false;
        public double RadeonSuperResolutionSharpness { get; set; } = 80;
        public bool ImageSharpening { get; set; } = false;
        public double ImageSharpeningSharpness { get; set; } = 80;
        public bool RadeonAntiLag { get; set; } = false;
        public bool RadeonBoost { get; set; } = false;
        public double RadeonBoostResolution { get; set; } = 0;
        public bool RadeonChill { get; set; } = false;
        public double RadeonChillMinFPS { get; set; } = 30;
        public double RadeonChillMaxFPS { get; set; } = 60;
        // FPS Limit settings
        public bool FPSLimitEnabled { get; set; } = false;
        public int FPSLimitValue { get; set; } = 60;
        // AutoTDP settings
        public bool AutoTDPEnabled { get; set; } = false;
        public int AutoTDPTargetFPS { get; set; } = 60;
        public int AutoTDPMinTDP { get; set; } = 8;
        public int AutoTDPMaxTDP { get; set; } = 30;
        public bool AutoTDPUseMLMode { get; set; } = false;
        public int AutoTDPControllerType { get; set; } = 0;  // 0=PID, 1=Q-Learning, 2=SARSA
        // OS Power Mode (0=Best Power Efficiency, 1=Balanced, 2=Best Performance)
        public int OSPowerMode { get; set; } = 1;
        // Legion Performance Mode (1=Quiet, 2=Balanced, 3=Performance, 255=Custom)
        public int LegionPerformanceMode { get; set; } = 2;
        // TDP Mode Index in the TDPModeComboBox (for user-made presets)
        // -1 means use LegionPerformanceMode to determine index (backwards compatibility)
        public int TDPModeIndex { get; set; } = -1;
        // TDP Boost toggle state (per-profile)
        public bool TDPBoostEnabled { get; set; } = false;
        // HDR and Resolution settings (per-profile)
        public bool HDREnabled { get; set; } = false;
        public string Resolution { get; set; } = "";
        public int? RefreshRate { get; set; } = null;
        // Sticky TDP settings (per-profile)
        public bool StickyTDPEnabled { get; set; } = true;
        public int StickyTDPInterval { get; set; } = 5;
        // Overlay Level (0=Off, 1-4 for RTSS/AMD)
        public int OverlayLevel { get; set; } = 0;
        // CPU Affinity as "pCores,eCores" string
        public string CPUAffinity { get; set; } = "";

        public PerformanceProfile Clone()
        {
            return new PerformanceProfile
            {
                TDP = this.TDP,
                CPUBoost = this.CPUBoost,
                CPUEPP = this.CPUEPP,
                MaxCPUState = this.MaxCPUState,
                MinCPUState = this.MinCPUState,
                FluidMotionFrames = this.FluidMotionFrames,
                RadeonSuperResolution = this.RadeonSuperResolution,
                RadeonSuperResolutionSharpness = this.RadeonSuperResolutionSharpness,
                ImageSharpening = this.ImageSharpening,
                ImageSharpeningSharpness = this.ImageSharpeningSharpness,
                RadeonAntiLag = this.RadeonAntiLag,
                RadeonBoost = this.RadeonBoost,
                RadeonBoostResolution = this.RadeonBoostResolution,
                RadeonChill = this.RadeonChill,
                RadeonChillMinFPS = this.RadeonChillMinFPS,
                RadeonChillMaxFPS = this.RadeonChillMaxFPS,
                FPSLimitEnabled = this.FPSLimitEnabled,
                FPSLimitValue = this.FPSLimitValue,
                AutoTDPEnabled = this.AutoTDPEnabled,
                AutoTDPTargetFPS = this.AutoTDPTargetFPS,
                AutoTDPMinTDP = this.AutoTDPMinTDP,
                AutoTDPMaxTDP = this.AutoTDPMaxTDP,
                AutoTDPUseMLMode = this.AutoTDPUseMLMode,
                AutoTDPControllerType = this.AutoTDPControllerType,
                OSPowerMode = this.OSPowerMode,
                LegionPerformanceMode = this.LegionPerformanceMode,
                TDPModeIndex = this.TDPModeIndex,
                TDPBoostEnabled = this.TDPBoostEnabled,
                HDREnabled = this.HDREnabled,
                Resolution = this.Resolution,
                RefreshRate = this.RefreshRate,
                StickyTDPEnabled = this.StickyTDPEnabled,
                StickyTDPInterval = this.StickyTDPInterval,
                OverlayLevel = this.OverlayLevel,
                CPUAffinity = this.CPUAffinity
            };
        }
    }

    /// <summary>
    /// Theme color palette definition
    /// </summary>
    public class ThemeColors
    {
        public string Name { get; set; }
        public Windows.UI.Color PageBackground { get; set; }
        public Windows.UI.Color CardBackground { get; set; }
        public Windows.UI.Color CardBorder { get; set; }
        public Windows.UI.Color AccentColor { get; set; }
        public Windows.UI.Color TextPrimary { get; set; }
        public Windows.UI.Color TextSecondary { get; set; }
        public Windows.UI.Color ButtonBackground { get; set; }
        public Windows.UI.Color ButtonBorder { get; set; }
        public Windows.UI.Color TileOff { get; set; }
        public Windows.UI.Color TileOn { get; set; }
    }

    /// <summary>
    /// View model for OSD item in the reorderable list
    /// </summary>
    public class OSDItemViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        private bool _canMoveUp;
        public bool CanMoveUp
        {
            get => _canMoveUp;
            set { _canMoveUp = value; OnPropertyChanged(); }
        }

        private bool _canMoveDown;
        public bool CanMoveDown
        {
            get => _canMoveDown;
            set { _canMoveDown = value; OnPropertyChanged(); }
        }

        private string _labelColor = "DEFAULT";
        public string LabelColor
        {
            get => _labelColor;
            set { _labelColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(LabelColorBrush)); }
        }

        public Windows.UI.Xaml.Media.SolidColorBrush LabelColorBrush
        {
            get
            {
                if (string.IsNullOrEmpty(_labelColor) || _labelColor == "DEFAULT")
                    return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Gray);
                try
                {
                    byte r = Convert.ToByte(_labelColor.Substring(0, 2), 16);
                    byte g = Convert.ToByte(_labelColor.Substring(2, 2), 16);
                    byte b = Convert.ToByte(_labelColor.Substring(4, 2), 16);
                    return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                }
                catch { return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Gray); }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Hotkey action types for Xbox controller button combos
    /// </summary>
    public enum HotkeyAction
    {
        Disabled = 0,
        KeyboardKey = 1,
        KeyboardShortcut = 2,
        ToggleOSD = 3,
        Screenshot = 4,
        AltTab = 5,
        AltF4 = 6,
        OpenKeyboard = 7,
        CtrlAltDel = 8,
        TaskManager = 9,
        FocusGoTweaks = 10
    }

    /// <summary>
    /// View model for saved controller profile display
    /// </summary>
    public class SavedProfileInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public string ProfileKey { get; set; }
        public string GameName { get; set; }
        public string SettingsSummary { get; set; }
        public bool IsGlobal { get; set; }
        public Windows.UI.Xaml.Visibility CanDelete => IsGlobal ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;

        // Game exe path for icon loading
        public string GameExePath { get; set; }

        // Game icon support
        private Windows.UI.Xaml.Media.ImageSource _iconSource;
        public Windows.UI.Xaml.Media.ImageSource IconSource
        {
            get => _iconSource;
            set
            {
                _iconSource = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconSource)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconVisibility)));
            }
        }
        public Windows.UI.Xaml.Visibility IconVisibility => IconSource != null ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Button mapping with type support (Gamepad, Keyboard, Mouse)
    /// </summary>
    public class ButtonMapping
    {
        /// <summary>Mapping type: 0=Gamepad, 1=Keyboard, 2=Mouse</summary>
        public int Type { get; set; } = 0;
        /// <summary>Gamepad action index (0-24, into RemapAction)</summary>
        public int GamepadAction { get; set; } = 0;
        /// <summary>Gamepad remap mode: 0=Single, 1=Combo</summary>
        public int GamepadMode { get; set; } = 0;
        /// <summary>Gamepad combo action indices (into RemapAction)</summary>
        public List<int> GamepadActions { get; set; } = new List<int>();
        /// <summary>Turbo on/off while held (improved emulation path)</summary>
        public bool Turbo { get; set; } = false;
        /// <summary>Keyboard key codes (up to 5 keys)</summary>
        public List<int> KeyboardKeys { get; set; } = new List<int>();
        /// <summary>Mouse button code (1-7)</summary>
        public int MouseButton { get; set; } = 0;

        /// <summary>
        /// Returns true if this mapping represents "no mapping" / default state.
        /// A mapping is default if Type=0 (Gamepad) with GamepadAction=0 (Disabled).
        /// </summary>
        public bool IsDefault => Type == 0 && GamepadAction == 0 && (GamepadActions == null || GamepadActions.Count == 0);

        public ButtonMapping Clone() => new ButtonMapping
        {
            Type = this.Type,
            GamepadAction = this.GamepadAction,
            GamepadMode = this.GamepadMode,
            GamepadActions = new List<int>(this.GamepadActions),
            Turbo = this.Turbo,
            KeyboardKeys = new List<int>(this.KeyboardKeys),
            MouseButton = this.MouseButton
        };

        /// <summary>
        /// Serializes to JSON string format for IPC/storage.
        /// Format: {"Type":0,"GamepadAction":5,"KeyboardKeys":[4,5],"MouseButton":0}
        /// </summary>
        public string ToJson()
        {
            var gamepadActions = GamepadActions.Count > 0 ? string.Join(",", GamepadActions) : "";
            var keys = KeyboardKeys.Count > 0 ? string.Join(",", KeyboardKeys) : "";
            string turboJson = Turbo ? "true" : "false";
            return $"{{\"Type\":{Type},\"GamepadAction\":{GamepadAction},\"GamepadMode\":{GamepadMode},\"GamepadActions\":[{gamepadActions}],\"Turbo\":{turboJson},\"KeyboardKeys\":[{keys}],\"MouseButton\":{MouseButton}}}";
        }

        /// <summary>
        /// Deserializes from JSON string. Returns new ButtonMapping if parsing fails.
        /// </summary>
        public static ButtonMapping FromJson(string json)
        {
            var result = new ButtonMapping();
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                // Parse Type
                var typeMatch = System.Text.RegularExpressions.Regex.Match(json, "\"Type\"\\s*:\\s*(-?\\d+)");
                if (typeMatch.Success && int.TryParse(typeMatch.Groups[1].Value, out int type))
                    result.Type = type;

                // Parse GamepadAction
                var gamepadMatch = System.Text.RegularExpressions.Regex.Match(json, "\"GamepadAction\"\\s*:\\s*(-?\\d+)");
                if (gamepadMatch.Success && int.TryParse(gamepadMatch.Groups[1].Value, out int gamepadAction))
                    result.GamepadAction = gamepadAction;

                // Parse GamepadMode
                var gamepadModeMatch = System.Text.RegularExpressions.Regex.Match(json, "\"GamepadMode\"\\s*:\\s*(-?\\d+)");
                if (gamepadModeMatch.Success && int.TryParse(gamepadModeMatch.Groups[1].Value, out int gamepadMode))
                    result.GamepadMode = gamepadMode;

                // Parse Turbo
                var turboMatch = System.Text.RegularExpressions.Regex.Match(json, "\"Turbo\"\\s*:\\s*(true|false|0|1)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (turboMatch.Success)
                {
                    string rawTurbo = turboMatch.Groups[1].Value;
                    result.Turbo = string.Equals(rawTurbo, "true", StringComparison.OrdinalIgnoreCase) || rawTurbo == "1";
                }

                // Parse MouseButton
                var mouseMatch = System.Text.RegularExpressions.Regex.Match(json, "\"MouseButton\"\\s*:\\s*(-?\\d+)");
                if (mouseMatch.Success && int.TryParse(mouseMatch.Groups[1].Value, out int mouseButton))
                    result.MouseButton = mouseButton;

                // Parse GamepadActions array
                var gamepadActionsMatch = System.Text.RegularExpressions.Regex.Match(json, "\"GamepadActions\"\\s*:\\s*\\[([^\\]]*)\\]");
                if (gamepadActionsMatch.Success)
                {
                    var actionsStr = gamepadActionsMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(actionsStr))
                    {
                        foreach (var part in actionsStr.Split(','))
                        {
                            if (int.TryParse(part.Trim(), out int action))
                                result.GamepadActions.Add(action);
                        }
                    }
                }

                // Parse KeyboardKeys array
                var keysMatch = System.Text.RegularExpressions.Regex.Match(json, "\"KeyboardKeys\"\\s*:\\s*\\[([^\\]]*)\\]");
                if (keysMatch.Success)
                {
                    var keysStr = keysMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(keysStr))
                    {
                        foreach (var part in keysStr.Split(','))
                        {
                            if (int.TryParse(part.Trim(), out int key))
                                result.KeyboardKeys.Add(key);
                        }
                    }
                }
            }
            catch
            {
                // Return default on any parse error
            }

            if (result.GamepadAction <= 0 && result.GamepadActions.Count > 0)
            {
                result.GamepadAction = result.GamepadActions[0];
            }
            else if (result.GamepadActions.Count == 0 && result.GamepadAction > 0)
            {
                result.GamepadActions.Add(result.GamepadAction);
            }

            return result;
        }
    }

    /// <summary>
    /// Controller profile settings storage for per-game button remapping
    /// </summary>
    public class ControllerProfile
    {
        public ButtonMapping ButtonY1 { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonY2 { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonY3 { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonM1 { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonM2 { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonM3 { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonDesktop { get; set; } = new ButtonMapping();
        public ButtonMapping ButtonPage { get; set; } = new ButtonMapping();
        public bool NintendoLayout { get; set; } = false;
        public int VibrationLevel { get; set; } = 2;  // Medium
        public int VibrationMode { get; set; } = 1;   // FPS

        // Gyro settings (per-game profile)
        public int GyroTarget { get; set; } = 0;           // Disabled
        public int GyroSensitivityX { get; set; } = 50;
        public int GyroSensitivityY { get; set; } = 50;
        public bool GyroInvertX { get; set; } = false;
        public bool GyroInvertY { get; set; } = false;
        public int GyroMappingType { get; set; } = 0;      // Instant
        public int GyroActivationMode { get; set; } = 0;   // Hold
        public int GyroActivationButton { get; set; } = 0; // None

        // Advanced gyro settings (per-game profile)
        public int GyroDeadzone { get; set; } = 10;         // 1-100

        // Stick deadzones (per-game profile)
        public int LeftStickDeadzone { get; set; } = 4;    // Default 4%
        public int RightStickDeadzone { get; set; } = 4;

        // Trigger travel (per-game profile)
        public int LeftTriggerStart { get; set; } = 0;     // Start % (0-100)
        public int LeftTriggerEnd { get; set; } = 0;       // End % from full (0-100)
        public int RightTriggerStart { get; set; } = 0;
        public int RightTriggerEnd { get; set; } = 0;
        public bool HairTriggers { get; set; } = false;    // Hair triggers preset (0%/1%)

        // Joystick as mouse (per-game profile)
        public int JoystickAsMouseMode { get; set; } = 0;  // 0=Disabled, 1=Left Stick, 2=Right Stick
        public int JoystickMouseSens { get; set; } = 50;   // 10-100

        // Gamepad button remapping (per-game profile)
        public Dictionary<string, ButtonMapping> GamepadButtonMappings { get; set; } = new Dictionary<string, ButtonMapping>();

        // Desktop Controls preset (per-game profile)
        public bool DesktopControlsEnabled { get; set; } = false;

        // Lighting (per-game profile)
        public int LightMode { get; set; } = 1;          // 0=Off, 1=Solid, 2=Pulse, 3=Dynamic, 4=Spiral
        public byte LightColorR { get; set; } = 255;     // RGB color for Solid/Pulse modes
        public byte LightColorG { get; set; } = 255;
        public byte LightColorB { get; set; } = 255;
        public int LightSpeed { get; set; } = 50;        // Animation speed for dynamic modes
        public int LightBrightness { get; set; } = 50;   // Brightness level 0-100
        public bool PowerLight { get; set; } = true;     // Power button light on/off
        public bool HasExplicitLighting { get; set; } = false;  // True if lighting was explicitly saved in this profile

        public ControllerProfile Clone()
        {
            return new ControllerProfile
            {
                ButtonY1 = this.ButtonY1.Clone(),
                ButtonY2 = this.ButtonY2.Clone(),
                ButtonY3 = this.ButtonY3.Clone(),
                ButtonM1 = this.ButtonM1.Clone(),
                ButtonM2 = this.ButtonM2.Clone(),
                ButtonM3 = this.ButtonM3.Clone(),
                ButtonDesktop = this.ButtonDesktop.Clone(),
                ButtonPage = this.ButtonPage.Clone(),
                NintendoLayout = this.NintendoLayout,
                VibrationLevel = this.VibrationLevel,
                VibrationMode = this.VibrationMode,
                // Gyro settings
                GyroTarget = this.GyroTarget,
                GyroSensitivityX = this.GyroSensitivityX,
                GyroSensitivityY = this.GyroSensitivityY,
                GyroInvertX = this.GyroInvertX,
                GyroInvertY = this.GyroInvertY,
                GyroMappingType = this.GyroMappingType,
                GyroActivationMode = this.GyroActivationMode,
                GyroActivationButton = this.GyroActivationButton,
                // Advanced gyro settings
                GyroDeadzone = this.GyroDeadzone,
                // Stick deadzones
                LeftStickDeadzone = this.LeftStickDeadzone,
                RightStickDeadzone = this.RightStickDeadzone,
                // Trigger travel
                LeftTriggerStart = this.LeftTriggerStart,
                LeftTriggerEnd = this.LeftTriggerEnd,
                RightTriggerStart = this.RightTriggerStart,
                RightTriggerEnd = this.RightTriggerEnd,
                HairTriggers = this.HairTriggers,
                // Joystick as mouse
                JoystickAsMouseMode = this.JoystickAsMouseMode,
                JoystickMouseSens = this.JoystickMouseSens,
                // Gamepad button mappings
                GamepadButtonMappings = this.GamepadButtonMappings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Clone()),
                // Desktop Controls preset
                DesktopControlsEnabled = this.DesktopControlsEnabled,
                // Lighting
                LightMode = this.LightMode,
                LightColorR = this.LightColorR,
                LightColorG = this.LightColorG,
                LightColorB = this.LightColorB,
                LightSpeed = this.LightSpeed,
                LightBrightness = this.LightBrightness,
                PowerLight = this.PowerLight,
                HasExplicitLighting = this.HasExplicitLighting
            };
        }
    }

    /// <summary>
    /// Represents a power plan for UI binding
    /// </summary>
    public class PowerPlanItem
    {
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GamingWidget : Page, INotifyPropertyChanged
    {
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly List<string> BlackListAppTrackerNames = new List<string>()
        {
            "App Installer", //Somehow App Installer shows up as a game sometimes
        };

        // Theme definitions
        private static readonly Dictionary<string, ThemeColors> WidgetThemes = new Dictionary<string, ThemeColors>
        {
            { "Default", new ThemeColors {
                Name = "Default",
                PageBackground = Windows.UI.Color.FromArgb(255, 37, 40, 44),      // #25282C
                CardBackground = Windows.UI.Color.FromArgb(255, 48, 52, 58),      // #30343A
                CardBorder = Windows.UI.Color.FromArgb(255, 80, 85, 92),          // #50555C
                AccentColor = Windows.UI.Color.FromArgb(255, 0, 200, 255),        // #00C8FF
                TextPrimary = Windows.UI.Color.FromArgb(255, 255, 255, 255),      // #FFFFFF
                TextSecondary = Windows.UI.Color.FromArgb(255, 160, 160, 160),    // #A0A0A0
                ButtonBackground = Windows.UI.Color.FromArgb(255, 62, 67, 75),    // #3E434B
                ButtonBorder = Windows.UI.Color.FromArgb(255, 107, 117, 132),     // #6B7584
                TileOff = Windows.UI.Color.FromArgb(255, 26, 28, 30),             // #1A1C1E
                TileOn = Windows.UI.Color.FromArgb(255, 26, 46, 31)               // #1A2E1F
            }},
            { "Dark", new ThemeColors {
                Name = "Dark",
                PageBackground = Windows.UI.Color.FromArgb(255, 26, 26, 26),      // #1A1A1A
                CardBackground = Windows.UI.Color.FromArgb(255, 37, 37, 37),      // #252525
                CardBorder = Windows.UI.Color.FromArgb(255, 58, 58, 58),          // #3A3A3A
                AccentColor = Windows.UI.Color.FromArgb(255, 0, 200, 255),        // #00C8FF
                TextPrimary = Windows.UI.Color.FromArgb(255, 255, 255, 255),      // #FFFFFF
                TextSecondary = Windows.UI.Color.FromArgb(255, 144, 144, 144),    // #909090
                ButtonBackground = Windows.UI.Color.FromArgb(255, 50, 50, 50),    // #323232
                ButtonBorder = Windows.UI.Color.FromArgb(255, 80, 80, 80),        // #505050
                TileOff = Windows.UI.Color.FromArgb(255, 20, 20, 20),             // #141414
                TileOn = Windows.UI.Color.FromArgb(255, 20, 40, 25)               // #142819
            }},
            { "OLED", new ThemeColors {
                Name = "OLED",
                PageBackground = Windows.UI.Color.FromArgb(255, 0, 0, 0),         // #000000
                CardBackground = Windows.UI.Color.FromArgb(255, 10, 10, 10),      // #0A0A0A
                CardBorder = Windows.UI.Color.FromArgb(255, 26, 26, 26),          // #1A1A1A
                AccentColor = Windows.UI.Color.FromArgb(255, 0, 200, 255),        // #00C8FF
                TextPrimary = Windows.UI.Color.FromArgb(255, 255, 255, 255),      // #FFFFFF
                TextSecondary = Windows.UI.Color.FromArgb(255, 128, 128, 128),    // #808080
                ButtonBackground = Windows.UI.Color.FromArgb(255, 20, 20, 20),    // #141414
                ButtonBorder = Windows.UI.Color.FromArgb(255, 40, 40, 40),        // #282828
                TileOff = Windows.UI.Color.FromArgb(255, 5, 5, 5),                // #050505
                TileOn = Windows.UI.Color.FromArgb(255, 10, 30, 15)               // #0A1E0F
            }},
            { "Dracula", new ThemeColors {
                Name = "Dracula",
                PageBackground = Windows.UI.Color.FromArgb(255, 40, 42, 54),      // #282A36
                CardBackground = Windows.UI.Color.FromArgb(255, 68, 71, 90),      // #44475A
                CardBorder = Windows.UI.Color.FromArgb(255, 98, 114, 164),        // #6272A4
                AccentColor = Windows.UI.Color.FromArgb(255, 189, 147, 249),      // #BD93F9
                TextPrimary = Windows.UI.Color.FromArgb(255, 248, 248, 242),      // #F8F8F2
                TextSecondary = Windows.UI.Color.FromArgb(255, 98, 114, 164),     // #6272A4
                ButtonBackground = Windows.UI.Color.FromArgb(255, 68, 71, 90),    // #44475A
                ButtonBorder = Windows.UI.Color.FromArgb(255, 98, 114, 164),      // #6272A4
                TileOff = Windows.UI.Color.FromArgb(255, 33, 34, 44),             // #21222C
                TileOn = Windows.UI.Color.FromArgb(255, 80, 250, 123)             // #50FA7B (green)
            }},
            { "Nord", new ThemeColors {
                Name = "Nord",
                PageBackground = Windows.UI.Color.FromArgb(255, 46, 52, 64),      // #2E3440
                CardBackground = Windows.UI.Color.FromArgb(255, 59, 66, 82),      // #3B4252
                CardBorder = Windows.UI.Color.FromArgb(255, 76, 86, 106),         // #4C566A
                AccentColor = Windows.UI.Color.FromArgb(255, 136, 192, 208),      // #88C0D0
                TextPrimary = Windows.UI.Color.FromArgb(255, 236, 239, 244),      // #ECEFF4
                TextSecondary = Windows.UI.Color.FromArgb(255, 216, 222, 233),    // #D8DEE9
                ButtonBackground = Windows.UI.Color.FromArgb(255, 67, 76, 94),    // #434C5E
                ButtonBorder = Windows.UI.Color.FromArgb(255, 76, 86, 106),       // #4C566A
                TileOff = Windows.UI.Color.FromArgb(255, 36, 40, 50),             // #242832
                TileOn = Windows.UI.Color.FromArgb(255, 163, 190, 140)            // #A3BE8C (green)
            }},
            { "Catppuccin", new ThemeColors {
                Name = "Catppuccin",
                PageBackground = Windows.UI.Color.FromArgb(255, 30, 30, 46),      // #1E1E2E
                CardBackground = Windows.UI.Color.FromArgb(255, 49, 50, 68),      // #313244
                CardBorder = Windows.UI.Color.FromArgb(255, 69, 71, 90),          // #45475A
                AccentColor = Windows.UI.Color.FromArgb(255, 203, 166, 247),      // #CBA6F7
                TextPrimary = Windows.UI.Color.FromArgb(255, 205, 214, 244),      // #CDD6F4
                TextSecondary = Windows.UI.Color.FromArgb(255, 166, 173, 200),    // #A6ADC8
                ButtonBackground = Windows.UI.Color.FromArgb(255, 49, 50, 68),    // #313244
                ButtonBorder = Windows.UI.Color.FromArgb(255, 88, 91, 112),       // #585B70
                TileOff = Windows.UI.Color.FromArgb(255, 24, 24, 37),             // #181825
                TileOn = Windows.UI.Color.FromArgb(255, 166, 227, 161)            // #A6E3A1 (green)
            }}
        };

        private string currentThemeName = "Default";

        // Xbox Game Bar logic
        private XboxGameBarWidget widget = null;
        private XboxGameBarWidgetActivity widgetActivity = null;
        private XboxGameBarWidgetNotificationManager notificationManager = null;
        public XboxGameBarWidgetActivity WidgetActivity { get { return widgetActivity; } }
        private XboxGameBarAppTargetTracker appTargetTracker = null;
        private bool appIsInBackground = false;

        private SolidColorBrush widgetDarkThemeBrush = null;
        private SolidColorBrush widgetLightThemeBrush = null;

        // Compact mode detection (based on window width)
        private bool isCompactMode = false;
        private const double CompactModeWidthThreshold = 400;

        // Widget unloading flag - prevents UI updates during shutdown
        private bool isUnloading = false;

        // Sticky TDP monitoring
        private DispatcherTimer stickyTDPTimer = null;
        private double targetTDPLimit = 15; // Stores the TDP limit we want to maintain
        private int stickyTDPCheckIntervalSeconds = 5;
        private bool isStickyTDPReapplying = false; // Prevents slider flicker during reapply

        // Power source change TDP reapply timer
        private DispatcherTimer powerSourceTdpReapplyTimer = null;

        // Labs section - DAService status timer
        private DispatcherTimer daServiceStatusTimer = null;
        private bool daServiceIsRunning = false;
        private bool labsSectionInitialized = false;

        // Update check
        private string _pendingUpdateZipUrl = null;
        private string _pendingUpdateVersion = null;

        // Helper launch guard - prevents duplicate launches and UAC prompts
        private static bool isLaunchingHelper = false;
        private static DateTime lastLaunchAttempt = DateTime.MinValue;
        private const int MinLaunchIntervalMs = 5000; // 5 seconds between launch attempts
        private const int HeartbeatStaleThresholdSeconds = 5;
        private const int ReconnectionTimeoutSeconds = 5;
        private DispatcherTimer reconnectionTimeoutTimer = null;

        // Hotkey watchers for Xbox controller button combos
        private XboxGameBarHotkeyWatcher hotkeyMenuA = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuB = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuX = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuY = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuDpadUp = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuDpadDown = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuDpadLeft = null;
        private XboxGameBarHotkeyWatcher hotkeyMenuDpadRight = null;
        private bool isLoadingHotkeys = false;
        private bool isHotkeysExpanded = false;
        private readonly Dictionary<string, DateTime> hotkeyLastExecuted = new Dictionary<string, DateTime>();
        private const int HotkeyDebounceMs = 300; // Minimum ms between hotkey executions
        private int lastNonZeroOsdLevel = 1; // Track last OSD level for toggle (default to Basic)

        // Properties
        private readonly OSDProperty osd;
        private readonly TDPProperty tdp;
        private readonly CurrentTDPProperty currentTdp;
        private readonly RunningGameProperty runningGame;
        private readonly PerGameProfileProperty perGameProfile;
        private readonly CPUBoostProperty cpuBoost;
        private readonly CPUEPPProperty cpuEPP;
        private readonly MaxCPUStateProperty maxCPUState;
        private readonly MinCPUStateProperty minCPUState;
        // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
        //private readonly LimitGPUClockProperty limitGPUClock;
        //private readonly GPUClockMinProperty gpuClockMin;
        //private readonly GPUClockMaxProperty gpuClockMax;
        private readonly RefreshRatesProperty refreshRates;
        private readonly RefreshRateProperty refreshRate;
        private readonly ResolutionsProperty resolutions;
        private readonly ResolutionProperty resolution;
        private readonly DisplayOrientationProperty displayOrientation;
        private readonly HDRSupportedProperty hdrSupported;
        private readonly HDREnabledProperty hdrEnabled;
        private readonly TrackedGameProperty trackedGame;
        private readonly RTSSInstalledProperty rtssInstalled;
        private readonly IsForegroundProperty isForeground;

        // AMD properties
        private readonly AMDRadeonSuperResolutionEnabledProperty amdRadeonSuperResolutionEnabled;
        private readonly AMDRadeonSuperResolutionSupportedProperty amdRadeonSuperResolutionSupported;
        private readonly AMDRadeonSuperResolutionSharpnessProperty amdRadeonSuperResolutionSharpness;
        private readonly AMDFluidMotionFrameEnabledProperty amdFluidMotionFrameEnabled;
        private readonly AMDFluidMotionFrameSupportedProperty amdFluidMotionFrameSupported;
        private readonly AMDRadeonAntiLagEnabledProperty amdRadeonAntiLagEnabled;
        private readonly AMDRadeonAntiLagSupportedProperty amdRadeonAntiLagSupported;
        private readonly AMDRadeonBoostEnabledProperty amdRadeonBoostEnabled;
        private readonly AMDRadeonBoostSupportedProperty amdRadeonBoostSupported;
        private readonly AMDRadeonBoostResolutionProperty amdRadeonBoostResolution;
        private readonly AMDRadeonChillEnabledProperty amdRadeonChillEnabled;
        private readonly AMDRadeonChillSupportedProperty amdRadeonChillSupported;
        private readonly AMDRadeonChillMinFPSProperty amdRadeonChillMinFPSProperty;
        private readonly AMDRadeonChillMaxFPSProperty amdRadeonChillMaxFPSProperty;
        private readonly AMDImageSharpeningEnabledProperty amdImageSharpeningEnabled;
        private readonly AMDImageSharpeningSupportedProperty amdImageSharpeningSupported;
        private readonly AMDImageSharpeningSharpnessProperty amdImageSharpeningSharpness;
        private readonly AMDDisplayBrightnessSupportedProperty amdDisplayBrightnessSupported;
        private readonly AMDDisplayBrightnessProperty amdDisplayBrightness;
        private readonly AMDDisplayContrastSupportedProperty amdDisplayContrastSupported;
        private readonly AMDDisplayContrastProperty amdDisplayContrast;
        private readonly AMDDisplaySaturationSupportedProperty amdDisplaySaturationSupported;
        private readonly AMDDisplaySaturationProperty amdDisplaySaturation;
        private readonly AMDDisplayTemperatureSupportedProperty amdDisplayTemperatureSupported;
        private readonly AMDDisplayTemperatureProperty amdDisplayTemperature;

        // Lossless Scaling properties
        private readonly LosslessScalingInstalledProperty losslessScalingInstalled;
        private readonly LosslessScalingRunningProperty losslessScalingRunning;
        private readonly LosslessScalingEnabledProperty losslessScalingEnabled;
        private readonly LosslessScalingCurrentProfileProperty losslessScalingCurrentProfile;
        private readonly LosslessScalingScalingTypeProperty losslessScalingScalingType;
        private readonly LosslessScalingSharpnessProperty losslessScalingSharpness;
        private readonly LosslessScalingFSROptimizeProperty losslessScalingFSROptimize;
        private readonly LosslessScalingAnime4KSizeProperty losslessScalingAnime4KSize;
        private readonly LosslessScalingAnime4KVRSProperty losslessScalingAnime4KVRS;
        private readonly LosslessScalingScaleModeProperty losslessScalingScaleMode;
        private readonly LosslessScalingScaleFactorProperty losslessScalingScaleFactor;
        private readonly LosslessScalingAspectRatioProperty losslessScalingAspectRatio;
        private readonly LosslessScalingFrameGenTypeProperty losslessScalingFrameGenType;
        private readonly LosslessScalingLSFG3ModeProperty losslessScalingLSFG3Mode;
        private readonly LosslessScalingLSFG3MultiplierProperty losslessScalingLSFG3Multiplier;
        private readonly LosslessScalingLSFG3TargetProperty losslessScalingLSFG3Target;
        private readonly LosslessScalingLSFG2ModeProperty losslessScalingLSFG2Mode;
        private readonly LosslessScalingFlowScaleProperty losslessScalingFlowScale;
        private readonly LosslessScalingSizeProperty losslessScalingSize;
        private readonly LosslessScalingAutoScaleProperty losslessScalingAutoScale;
        private readonly LosslessScalingAutoScaleDelayProperty losslessScalingAutoScaleDelay;
        private readonly LosslessScalingSaveAndRestartProperty losslessScalingSaveAndRestart;
        private readonly LosslessScalingCreateProfileProperty losslessScalingCreateProfile;
        private readonly LosslessScalingBringToForegroundProperty losslessScalingBringToForeground;
        private readonly LosslessScalingLaunchProperty losslessScalingLaunch;

        // Legion Go properties
        private readonly LegionGoDetectedProperty legionGoDetected;
        private readonly LegionTouchpadEnabledProperty legionTouchpadEnabled;
        private readonly LegionLightModeProperty legionLightMode;
        private readonly LegionLightColorProperty legionLightColor;
        private readonly LegionLightBrightnessProperty legionLightBrightness;
        private readonly LegionLightSpeedProperty legionLightSpeed;
        private readonly LegionPerformanceModeProperty legionPerformanceMode;
        private readonly LegionCustomTDPSlowProperty legionCustomTDPSlow;
        private readonly LegionCustomTDPFastProperty legionCustomTDPFast;
        private readonly LegionCustomTDPPeakProperty legionCustomTDPPeak;
        private readonly LegionFanFullSpeedProperty legionFanFullSpeed;
        private readonly LegionFanCurveGraphProperty legionFanCurveGraph;
        private readonly LegionCPUTempProperty legionCPUTemp;
        private readonly LegionFanSensorTempProperty legionFanSensorTemp;
        private readonly LegionCPUFanRPMProperty legionCPUFanRPM;
        private readonly LegionFanCurveVisibleProperty legionFanCurveVisible;
        private readonly LegionGyroEnabledProperty legionGyroEnabled;
        private readonly LegionVibrationProperty legionVibration;
        private readonly LegionPowerLightProperty legionPowerLight;
        private readonly LegionChargeLimitProperty legionChargeLimit;

        // Legion controller remapping properties
        private readonly LegionButtonY1Property legionButtonY1;
        private readonly LegionButtonY2Property legionButtonY2;
        private readonly LegionButtonY3Property legionButtonY3;
        private readonly LegionButtonM1Property legionButtonM1;
        private readonly LegionButtonM2Property legionButtonM2;
        private readonly LegionButtonM3Property legionButtonM3;
        private readonly LegionButtonDesktopProperty legionButtonDesktop;
        private readonly LegionButtonPageProperty legionButtonPage;
        private readonly LegionNintendoLayoutProperty legionNintendoLayout;
        private readonly LegionVibrationModeProperty legionVibrationMode;
        private readonly LegionControllerProfileProperty legionControllerProfile;

        // Gyro properties
        private readonly LegionGyroTargetProperty legionGyroTarget;
        private readonly LegionGyroSensitivityXProperty legionGyroSensitivityX;
        private readonly LegionGyroSensitivityYProperty legionGyroSensitivityY;
        private readonly LegionGyroInvertXProperty legionGyroInvertX;
        private readonly LegionGyroInvertYProperty legionGyroInvertY;
        private readonly LegionGyroMappingTypeProperty legionGyroMappingType;
        private readonly LegionGyroActivationModeProperty legionGyroActivationMode;
        private readonly LegionGyroActivationButtonProperty legionGyroActivationButton;

        // Advanced gyro properties
        private readonly LegionGyroDeadzoneProperty legionGyroDeadzone;

        // Stick deadzone properties
        private readonly LegionLeftStickDeadzoneProperty legionLeftStickDeadzone;
        private readonly LegionRightStickDeadzoneProperty legionRightStickDeadzone;

        // Trigger travel properties
        private readonly LegionLeftTriggerStartProperty legionLeftTriggerStart;
        private readonly LegionLeftTriggerEndProperty legionLeftTriggerEnd;
        private readonly LegionRightTriggerStartProperty legionRightTriggerStart;
        private readonly LegionRightTriggerEndProperty legionRightTriggerEnd;
        private readonly LegionHairTriggersProperty legionHairTriggers;

        // Touchpad vibration property (GLOBAL setting)
        private readonly LegionTouchpadVibrationProperty legionTouchpadVibration;

        // Joystick as mouse properties
        private readonly LegionJoystickAsMouseModeProperty legionJoystickAsMouseMode;
        private readonly LegionJoystickMouseSensProperty legionJoystickMouseSens;

        // Gamepad button mapping property
        private readonly LegionGamepadMappingProperty legionGamepadMapping;
        private Dictionary<string, ButtonMapping> gamepadButtonMappings = new Dictionary<string, ButtonMapping>();

        // Desktop controls preset (synced from helper for hotkey support)
        private readonly LegionDesktopControlsProperty legionDesktopControls;

        // Controller battery properties (from HID input reports)
        private readonly ControllerBatteryLeftProperty controllerBatteryLeft;
        private readonly ControllerBatteryRightProperty controllerBatteryRight;
        private readonly ControllerChargingLeftProperty controllerChargingLeft;
        private readonly ControllerChargingRightProperty controllerChargingRight;
        private readonly ControllerConnectedLeftProperty controllerConnectedLeft;
        private readonly ControllerConnectedRightProperty controllerConnectedRight;
        private readonly ControllerVidPidProperty controllerVidPid;

        // Device capability properties (for UI visibility based on device features)
        private readonly DeviceDisplayNameProperty deviceDisplayName;
        private readonly DeviceSupportsControllerRemapProperty deviceSupportsControllerRemap;
        private readonly DeviceSupportsRgbLightingProperty deviceSupportsRgbLighting;
        private readonly DeviceSupportsGyroProperty deviceSupportsGyro;
        private readonly DeviceHasScrollWheelProperty deviceHasScrollWheel;
        private readonly DeviceHasDetachableControllersProperty deviceHasDetachableControllers;
        private readonly DeviceHasTouchpadProperty deviceHasTouchpad;

        // GPD properties
        private readonly GPDDetectedProperty gpdDetected;
        private readonly GPDWin5ConnectedProperty gpdWin5Connected;
        private readonly GPDWin5HidDebugProperty gpdWin5HidDebug;
        private readonly GPDWin5HidDevicesProperty gpdWin5HidDevices;
        private readonly GPDDeviceNameProperty gpdDeviceName;
        private readonly GPDSupportsFanControlProperty gpdSupportsFanControl;
        private readonly GPDFanSpeedProperty gpdFanSpeed;
        private readonly GPDFanRPMProperty gpdFanRPM;
        private readonly GPDFanModeProperty gpdFanMode;
        private readonly GPDRestoreDefaultsProperty gpdRestoreDefaults;
        private readonly GPDApplyMappingsProperty gpdApplyMappings;
        private readonly GPDButtonL4Property gpdButtonL4;
        private readonly GPDButtonR4Property gpdButtonR4;
        private readonly GPDButtonProperty gpdButtonA;
        private readonly GPDButtonProperty gpdButtonB;
        private readonly GPDButtonProperty gpdButtonX;
        private readonly GPDButtonProperty gpdButtonY;
        private readonly GPDButtonProperty gpdButtonDPadUp;
        private readonly GPDButtonProperty gpdButtonDPadDown;
        private readonly GPDButtonProperty gpdButtonDPadLeft;
        private readonly GPDButtonProperty gpdButtonDPadRight;
        private readonly GPDButtonProperty gpdButtonL3;
        private readonly GPDButtonProperty gpdButtonR3;
        private readonly GPDButtonProperty gpdButtonLSLeft;
        private readonly GPDButtonProperty gpdButtonLSRight;
        private readonly ControllerEmulationAvailableProperty controllerEmulationAvailable;
        private readonly ControllerEmulationEnabledProperty controllerEmulationEnabled;
        private readonly ControllerEmulationHideStockControllerProperty controllerEmulationHideStockController;
        private readonly ControllerEmulationImprovedInputProperty controllerEmulationImprovedInput;
        private readonly ControllerEmulationHideTargetProperty controllerEmulationHideTarget;
        private readonly ControllerEmulationGyroSourceProperty controllerEmulationGyroSource;
        private readonly ControllerEmulationModeProperty controllerEmulationMode;
        private readonly ControllerEmulationRumbleProfileProperty controllerEmulationRumbleProfile;
        private readonly ControllerEmulationGyroActivationModeProperty controllerEmulationGyroActivationMode;
        private readonly ControllerEmulationGyroActivationButtonProperty controllerEmulationGyroActivationButton;
        private readonly ControllerEmulationDs4OrientationProperty controllerEmulationDs4Orientation;
        private readonly ControllerEmulationPs4TouchpadEnabledProperty controllerEmulationPs4TouchpadEnabled;
        private readonly ControllerEmulationLedForwardingEnabledProperty controllerEmulationLedForwardingEnabled;
        private readonly ControllerEmulationMouseSensitivityProperty controllerEmulationMouseSensitivity;
        private readonly ControllerEmulationMouseThresholdProperty controllerEmulationMouseThreshold;
        private readonly ControllerEmulationMouseAxisProperty controllerEmulationMouseAxis;
        private readonly ControllerEmulationMouseInvertXProperty controllerEmulationMouseInvertX;
        private readonly ControllerEmulationMouseInvertYProperty controllerEmulationMouseInvertY;
        private readonly ControllerEmulationMouseGainXProperty controllerEmulationMouseGainX;
        private readonly ControllerEmulationMouseGainYProperty controllerEmulationMouseGainY;
        private readonly ControllerEmulationStickInvertXProperty controllerEmulationStickInvertX;
        private readonly ControllerEmulationStickInvertYProperty controllerEmulationStickInvertY;
        private readonly ControllerEmulationStickSelectProperty controllerEmulationStickSelect;
        private readonly ControllerEmulationStickOnlyJoystickDataProperty controllerEmulationStickOnlyJoystickData;
        private readonly ControllerEmulationVirtualABXYLayoutProperty controllerEmulationVirtualAbxyLayout;
        private readonly ControllerEmulationStickMinGyroSpeedProperty controllerEmulationStickMinGyroSpeed;
        private readonly ControllerEmulationStickMaxGyroSpeedProperty controllerEmulationStickMaxGyroSpeed;
        private readonly ControllerEmulationStickMinOutputProperty controllerEmulationStickMinOutput;
        private readonly ControllerEmulationStickMaxOutputProperty controllerEmulationStickMaxOutput;
        private readonly ControllerEmulationStickPowerCurveProperty controllerEmulationStickPowerCurve;
        private readonly ControllerEmulationStickSensitivityV2Property controllerEmulationStickSensitivityV2;
        private readonly ControllerEmulationStickDeadzoneProperty controllerEmulationStickDeadzone;
        private readonly ControllerEmulationStickPrecisionSpeedProperty controllerEmulationStickPrecisionSpeed;
        private readonly ControllerEmulationStickOutputMixProperty controllerEmulationStickOutputMix;
        private readonly ControllerEmulationStickOrientationV2Property controllerEmulationStickOrientationV2;
        private readonly ControllerEmulationStickConversionProperty controllerEmulationStickConversion;
        private bool isGyroActivationExpanded;
        private bool isFeaturesExpanded;
        private bool isJoystickOutputExpanded;
        private bool controllerEmulationSupported = false;
        private bool isApplyingGpdRestoreDefaults = false;
        private readonly GPDFanCurveGraphProperty gpdFanCurveGraph;
        private readonly GPDCPUTempProperty gpdCPUTemp;
        private readonly GPDFanCurveVisibleProperty gpdFanCurveVisible;
        private readonly GPDFanCurveEnabledProperty gpdFanCurveEnabled;

        // Default Game Profile properties (Microsoft Gaming Services profiles)
        private readonly DefaultGameProfileAvailableProperty defaultGameProfileAvailable;
        private readonly DefaultGameProfileDataProperty defaultGameProfileData;
        private readonly DefaultGameProfileEnabledProperty defaultGameProfileEnabled;

        // Settings properties
        private readonly TdpMethodProperty tdpMethod;
        private readonly WinRing0AvailableProperty winRing0Available;
        private readonly PawnIOInstalledProperty pawnIOInstalled;
        private readonly InstallPawnIOProperty installPawnIO;
        private readonly ViGEmBusInstalledProperty vigemBusInstalled;
        private readonly InstallViGEmBusProperty installViGEmBus;
        private readonly HidHideInstalledProperty hidHideInstalled;
        private readonly InstallHidHideProperty installHidHide;
        private readonly AutoHibernateEnabledProperty autoHibernateEnabled;
        private readonly AutoHibernateIdleMinutesProperty autoHibernateIdleMinutes;

        // AutoTDP properties
        private readonly AutoTDPEnabledProperty autoTDPEnabled;
        private readonly AutoTDPTargetFPSProperty autoTDPTargetFPS;
        private readonly AutoTDPCurrentFPSProperty autoTDPCurrentFPS;
        private readonly AutoTDPMinTDPProperty autoTDPMinTDP;
        private readonly AutoTDPMaxTDPProperty autoTDPMaxTDP;
        private readonly AutoTDPUseMLModeProperty autoTDPUseMLMode;  // DEPRECATED: use autoTDPControllerType
        private readonly AutoTDPControllerTypeProperty autoTDPControllerType;  // 0=PID, 1=Q-Learning, 2=SARSA
        private readonly AutoTDPMLStatusProperty autoTDPMLStatus;
        private readonly AutoTDPLearnedGameDataProperty autoTDPLearnedGameData;
        private readonly AutoTDPResetMLProperty autoTDPResetML;
        private readonly AutoTDPPauseWhenUnfocusedProperty autoTDPPauseWhenUnfocused;
        private readonly TDPLimitsProperty tdpLimits;

        // AutoTDP slider debounce timers (delay sending to helper until user stops sliding)
        private DispatcherTimer autoTDPTargetFPSDebounceTimer;
        private DispatcherTimer autoTDPMinDebounceTimer;
        private DispatcherTimer autoTDPMaxDebounceTimer;
        private int pendingAutoTDPTargetFPS;
        private int pendingAutoTDPMinTDP;
        private int pendingAutoTDPMaxTDP;

        private readonly CPUCoreConfigProperty cpuCoreConfig;
        private readonly CPUCoreActiveConfigProperty cpuCoreActiveConfig;
        private readonly CoreParkingPercentProperty coreParkingPercent;
        private readonly ForceParkModeProperty forceParkMode;
        private readonly ForceDefaultGameProfileProperty forceDefaultGameProfile;

        // TDP Boost properties
        private readonly TDPBoostEnabledProperty tdpBoostEnabled;
        private readonly TDPBoostSPPTProperty tdpBoostSPPT;
        private readonly TDPBoostFPPTProperty tdpBoostFPPT;
        private bool isTDPBoostExpanded = false;
        private bool isLoadingTDPBoostSettings = false;
        private bool isLoadingStickyTDPSettings = false;
        private bool isUpdatingTDPMode = false; // Prevents saving toggle states during mode changes

        // OS Power Mode
        private readonly OSPowerModeProperty osPowerMode;
        private bool isLoadingOSPowerMode = false;

        // Profile Detection Settings
        private readonly ProfileMatchByExeProperty profileMatchByExe;
        private readonly ProfileGamesOnlyProperty profileGamesOnly;
        // DISABLED: Custom games, blacklist, and current apps features - caused user confusion
        // private readonly ProfileCustomGamePathProperty profileCustomGamePath;
        // private readonly ProfileBlacklistPathsProperty profileBlacklistPaths;
        // private readonly ForegroundAppProperty foregroundApp;

        // FPS Limit (RTSS)
        private readonly FPSLimitProperty fpsLimit;
        private DispatcherTimer fpsLimitDebounceTimer;
        private int fpsLimitPendingValue;
        private const int FPS_LIMIT_DEBOUNCE_MS = 300;

        // Profile management
        private PerformanceProfile globalProfile = new PerformanceProfile();
        private PerformanceProfile acProfile = new PerformanceProfile();
        private PerformanceProfile dcProfile = new PerformanceProfile();
        private PerformanceProfile gameProfile = new PerformanceProfile();
        private PerformanceProfile gameACProfile = new PerformanceProfile();
        private PerformanceProfile gameDCProfile = new PerformanceProfile();
        private string currentProfileName = ""; // Empty so first SwitchProfile() loads settings
        private string currentGameName = "";
        private string currentGameExePath = "";
        private string currentGameIconPath = ""; // Cache icon path to preserve it across foreground changes
        private bool isLoadingProfile = false;
        private bool isSwitchingProfile = false;
        private bool isApplyingHelperUpdate = false; // Prevents saves when helper echoes values back
        private bool isInitialSync = true; // Prevents saves during initial app startup sync
        private bool isCleanInstall = false; // True if no saved Global profile existed on startup
        private bool isInternalToggleDisable = false; // Indicates toggle is being disabled internally (game close)
        private bool isUserInitiatedProfileToggle = false; // Indicates user clicked Profile tile to toggle
        private bool isUserInitiatedTDPModeChange = false; // Indicates user clicked TDP Mode tile in Quick Tab
        private int savedLegionPerformanceMode = -1; // Stores Legion mode before per-game profile (-1 = not saved)
        private bool isUpdatingPowerSourceProfileToggle = false; // Prevents programmatic toggle sync from triggering profile writes
        private const string GlobalPowerSourceProfileSettingKey = "PowerSourceProfileEnabled";
        private const string PerGamePowerSourceProfileSettingPrefix = "PerGamePowerSourceProfileEnabled_";

        // Controller profile state
        private ControllerProfile globalControllerProfile = new ControllerProfile();
        private ControllerProfile gameControllerProfile = new ControllerProfile();
        private bool isLoadingControllerProfile = false;
        private bool isSwitchingControllerProfile = false;
        private DateTime lastProfileApplyTime = DateTime.MinValue; // Prevents duplicate sends from queued UI events
        private int profileSwitchEpoch = 0; // Incremented on each LoadProfileSettings; used to skip stale deferred callbacks
        private string lastSentGamepadMappingsJson = null; // Tracks last sent mappings to avoid duplicates

        // Helper to check if we have a valid game (not null, not empty, not "No game detected")
        private bool HasValidGame(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return false;

            string normalized = gameName.Trim();

            // Case-insensitive check for "No game detected" (handles any capitalization)
            return !normalized.Equals("No game detected", StringComparison.OrdinalIgnoreCase);
        }

        // Sanitize game name for consistent storage
        private string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "";

            // Trim whitespace, normalize spaces
            return gameName.Trim();
        }

        private string GetPerGamePowerSourceProfileSettingKey(string gameName)
        {
            return $"{PerGamePowerSourceProfileSettingPrefix}{gameName}";
        }

        private bool HasGameSingleProfile(string gameName)
        {
            if (!HasValidGame(gameName))
                return false;

            var settings = ApplicationData.Current.LocalSettings;
            return settings.Containers.ContainsKey($"Profile_Game_{gameName}");
        }

        private bool HasGamePowerSplitProfiles(string gameName)
        {
            if (!HasValidGame(gameName))
                return false;

            var settings = ApplicationData.Current.LocalSettings;
            return settings.Containers.ContainsKey($"Profile_Game_{gameName}_AC")
                || settings.Containers.ContainsKey($"Profile_Game_{gameName}_DC");
        }

        private bool HasAnyGameProfile(string gameName)
        {
            return HasGameSingleProfile(gameName) || HasGamePowerSplitProfiles(gameName);
        }

        private bool GetGlobalPowerSourceProfileEnabled()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue(GlobalPowerSourceProfileSettingKey, out object val) && val is bool enabled)
            {
                return enabled;
            }

            return false;
        }

        private bool GetPerGamePowerSourceProfileEnabled(string gameName)
        {
            if (!HasValidGame(gameName))
            {
                return GetGlobalPowerSourceProfileEnabled();
            }

            var settings = ApplicationData.Current.LocalSettings;
            string settingKey = GetPerGamePowerSourceProfileSettingKey(gameName);
            if (settings.Values.TryGetValue(settingKey, out object val) && val is bool enabled)
            {
                return enabled;
            }

            bool hasSplitProfiles = HasGamePowerSplitProfiles(gameName);
            bool hasSingleProfile = HasGameSingleProfile(gameName);

            // Prefer single-profile mode when both profile shapes exist and no explicit
            // per-game split setting has been saved yet.
            if (hasSingleProfile)
            {
                return false;
            }

            if (hasSplitProfiles)
            {
                return true;
            }

            return GetGlobalPowerSourceProfileEnabled();
        }

        private void SavePerGamePowerSourceProfileSetting(string gameName, bool enabled)
        {
            if (!HasValidGame(gameName))
            {
                return;
            }

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[GetPerGamePowerSourceProfileSettingKey(gameName)] = enabled;
        }

        private bool GetPowerSourceProfileEnabledForCurrentContext()
        {
            bool perGameEnabled = PerGameProfileToggle?.IsOn == true;
            if (perGameEnabled && HasValidGame(currentGameName))
            {
                return GetPerGamePowerSourceProfileEnabled(currentGameName);
            }

            return GetGlobalPowerSourceProfileEnabled();
        }

        private void UpdateGlobalProfileDisplayMode()
        {
            bool globalPowerSourceSplit = GetGlobalPowerSourceProfileEnabled();

            if (GlobalProfileSimple != null)
            {
                GlobalProfileSimple.Visibility = globalPowerSourceSplit ? Visibility.Collapsed : Visibility.Visible;
            }

            if (GlobalProfileACDC != null)
            {
                GlobalProfileACDC.Visibility = globalPowerSourceSplit ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdatePowerSourceProfileScopeText()
        {
            if (PowerSourceProfileScopeText == null)
            {
                return;
            }

            bool hasGame = HasValidGame(currentGameName);
            bool perGameContext = PerGameProfileToggle?.IsOn == true && hasGame;

            if (perGameContext)
            {
                bool splitEnabled = GetPerGamePowerSourceProfileEnabled(currentGameName);
                PowerSourceProfileScopeText.Text = $"Scope: {currentGameName} (per-game). AC/DC split: {(splitEnabled ? "On" : "Off")} (auto-saved).";
            }
            else
            {
                bool splitEnabled = GetGlobalPowerSourceProfileEnabled();
                PowerSourceProfileScopeText.Text = $"Scope: Global profiles. AC/DC split: {(splitEnabled ? "On" : "Off")}. Enable Per-Game Profile to set this per game.";
            }
        }

        private void SyncPowerSourceProfileToggleForCurrentContext()
        {
            if (PowerSourceProfileToggle == null)
            {
                return;
            }

            bool targetValue = GetPowerSourceProfileEnabledForCurrentContext();
            if (PowerSourceProfileToggle.IsOn != targetValue)
            {
                isUpdatingPowerSourceProfileToggle = true;
                try
                {
                    PowerSourceProfileToggle.IsOn = targetValue;
                }
                finally
                {
                    isUpdatingPowerSourceProfileToggle = false;
                }
            }

            UpdateGlobalProfileDisplayMode();
            UpdatePowerSourceProfileScopeText();
        }

        // Profile save settings - backed by fields to avoid UI thread access issues
        // These are updated in LoadProfileCustomizationSettings and ProfileSettingCheckBox_Changed
        private bool _saveTDP = true;
        private bool _saveCPUBoost = true;
        private bool _saveCPUEPP = true;
        private bool _saveCPUState = true;
        private bool _saveAMDFeatures = false;
        private bool _saveFPSLimit = true;
        private bool _saveAutoTDP = true;
        private bool _saveOSPowerMode = true;
        private bool _saveHDR = false;
        private bool _saveResolution = false;
        private bool _saveRefreshRate = false;
        private bool _saveStickyTDP = false;
        private bool _saveOverlayLevel = false;
        private bool _saveCPUAffinity = false;

        private bool SaveTDP => _saveTDP;
        private bool SaveCPUBoost => _saveCPUBoost;
        private bool SaveCPUEPP => _saveCPUEPP;
        private bool SaveCPUState => _saveCPUState;
        private bool SaveAMDFeatures => _saveAMDFeatures;
        private bool SaveFPSLimit => _saveFPSLimit;
        private bool SaveAutoTDP => _saveAutoTDP;
        private bool SaveOSPowerMode => _saveOSPowerMode;
        private bool SaveHDR => _saveHDR;
        private bool SaveResolution => _saveResolution;
        private bool SaveRefreshRate => _saveRefreshRate;
        private bool SaveStickyTDP => _saveStickyTDP;
        private bool SaveOverlayLevel => _saveOverlayLevel;
        private bool SaveCPUAffinity => _saveCPUAffinity;

        private bool isLoadingProfileSettings = false;

        private string RadeonChillOnText
        {
            get
            {
                try
                {
                    // Safety check: ensure both properties are initialized before accessing values
                    if (amdRadeonChillMinFPSProperty == null || amdRadeonChillMaxFPSProperty == null)
                        return "Enabled";

                    return string.Format("Idle FPS: {0} - Max FPS: {1}",
                        amdRadeonChillMinFPSProperty.Value,
                        amdRadeonChillMaxFPSProperty.Value);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in RadeonChillOnText getter: {ex.Message}");
                    return "Enabled";
                }
            }
        }

        private readonly WidgetProperties properties;

        public event PropertyChangedEventHandler PropertyChanged;

        public GamingWidget()
        {
            var constructorTimer = Stopwatch.StartNew();
            Logger.Info($"=== GamingWidget constructor START === Instance hash: {this.GetHashCode()}");

            // Prevent OSD checkbox events from saving during XAML initialization
            isLoadingOSDConfig = true;
            // Prevent profile settings checkbox events from saving during XAML initialization
            isLoadingProfileSettings = true;
            // Prevent TDP limits slider events from saving during XAML initialization
            isLoadingTDPLimits = true;
            // Prevent controller profile events from saving during XAML initialization
            // This MUST be set before InitializeComponent() to prevent button ComboBox events
            // from overwriting stored button mappings with defaults
            isLoadingControllerProfile = true;

            var xamlTimer = Stopwatch.StartNew();
            InitializeComponent();
            xamlTimer.Stop();
            Logger.Info($"[TIMING] InitializeComponent: {xamlTimer.ElapsedMilliseconds}ms");

            // Register for lifecycle events
            this.Loaded += GamingWidget_Loaded;
            this.Unloaded += GamingWidget_Unloaded;
            Logger.Info("Registered Loaded and Unloaded event handlers.");

            // Register for LT/RT tab navigation (PreviewKeyDown to intercept before scrolling)
            this.PreviewKeyDown += GamingWidget_PreviewKeyDown;

            var propertiesTimer = Stopwatch.StartNew();
            tdp = new TDPProperty(4, TDPSlider, this);
            currentTdp = new CurrentTDPProperty(CurrentTDPValueText, this);
            osd = new OSDProperty(0, PerformanceOverlaySlider, this);
            runningGame = new RunningGameProperty(RunningGameText, PerGameProfileToggle, DetectedGameText, this);
            runningGame.SetGameDetectionCallback(UpdatePerformanceTabXYNavigation);
            perGameProfile = new PerGameProfileProperty(PerGameProfileToggle, this);
            cpuBoost = new CPUBoostProperty(CPUBoostToggle, this);
            cpuEPP = new CPUEPPProperty(80, CPUEPPSlider, this);
            maxCPUState = new MaxCPUStateProperty();
            minCPUState = new MinCPUStateProperty();
            // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
            //limitGPUClock = new LimitGPUClockProperty(LimitGPUClockToggle, this);
            //gpuClockMin = new GPUClockMinProperty(GPUClockMinSlider, this);
            //gpuClockMax = new GPUClockMaxProperty(GPUClockMaxSlider, this);
            refreshRates = new RefreshRatesProperty(RefreshRatesComboBox, this);
            refreshRate = new RefreshRateProperty(RefreshRatesComboBox, this);
            refreshRates.SetRefreshRateProperty(refreshRate); // Wire up for selection restoration on dock/undock
            resolutions = new ResolutionsProperty(ResolutionComboBox, this);
            resolution = new ResolutionProperty(ResolutionComboBox, this);
            resolutions.SetResolutionProperty(resolution); // Wire up for selection restoration on dock/undock
            displayOrientation = new DisplayOrientationProperty();
            hdrSupported = new HDRSupportedProperty(HDRToggle, this);
            hdrEnabled = new HDREnabledProperty(HDRToggle, this);
            trackedGame = new TrackedGameProperty(new TrackedGame());
            rtssInstalled = new RTSSInstalledProperty(PerformanceOverlaySlider, this);
            rtssInstalled.SetAdditionalCallback(UpdateFPSLimitControls);
            isForeground = new IsForegroundProperty();
            amdRadeonSuperResolutionEnabled = new AMDRadeonSuperResolutionEnabledProperty(AMDRadeonSuperResolutionToggle, this);
            amdRadeonSuperResolutionSupported = new AMDRadeonSuperResolutionSupportedProperty(AMDRadeonSuperResolutionToggle, this);
            amdRadeonSuperResolutionSharpness = new AMDRadeonSuperResolutionSharpnessProperty(AMDRadeonSuperResolutionSharpnessSlider, this);
            amdFluidMotionFrameEnabled = new AMDFluidMotionFrameEnabledProperty(AMDFluidMotionFrameToggle, this);
            amdFluidMotionFrameSupported = new AMDFluidMotionFrameSupportedProperty(AMDFluidMotionFrameToggle, this);
            amdRadeonAntiLagEnabled = new AMDRadeonAntiLagEnabledProperty(AMDRadeonAntiLagToggle, this);
            amdRadeonAntiLagSupported = new AMDRadeonAntiLagSupportedProperty(AMDRadeonAntiLagToggle, this);
            amdRadeonBoostEnabled = new AMDRadeonBoostEnabledProperty(AMDRadeonBoostToggle, this);
            amdRadeonBoostSupported = new AMDRadeonBoostSupportedProperty(AMDRadeonBoostToggle, this);
            amdRadeonBoostResolution = new AMDRadeonBoostResolutionProperty(AMDRadeonBoostResolutionSlider, this);
            amdRadeonChillEnabled = new AMDRadeonChillEnabledProperty(AMDRadeonChillToggle, this);
            amdRadeonChillSupported = new AMDRadeonChillSupportedProperty(AMDRadeonChillToggle, this);
            amdRadeonChillMinFPSProperty = new AMDRadeonChillMinFPSProperty(AMDRadeonChillMinFPSSlider, this);
            amdRadeonChillMaxFPSProperty = new AMDRadeonChillMaxFPSProperty(AMDRadeonChillMaxFPSSlider, this);
            amdImageSharpeningEnabled = new AMDImageSharpeningEnabledProperty(AMDImageSharpeningToggle, this);
            amdImageSharpeningSupported = new AMDImageSharpeningSupportedProperty(AMDImageSharpeningToggle, this);
            amdImageSharpeningSharpness = new AMDImageSharpeningSharpnessProperty(AMDImageSharpeningSlider, this);
            amdDisplayBrightnessSupported = new AMDDisplayBrightnessSupportedProperty(AMDDisplayBrightnessSlider, this);
            amdDisplayBrightness = new AMDDisplayBrightnessProperty(AMDDisplayBrightnessSlider, this);
            amdDisplayContrastSupported = new AMDDisplayContrastSupportedProperty(AMDDisplayContrastSlider, this);
            amdDisplayContrast = new AMDDisplayContrastProperty(AMDDisplayContrastSlider, this);
            amdDisplaySaturationSupported = new AMDDisplaySaturationSupportedProperty(AMDDisplaySaturationSlider, this);
            amdDisplaySaturation = new AMDDisplaySaturationProperty(AMDDisplaySaturationSlider, this);
            amdDisplayTemperatureSupported = new AMDDisplayTemperatureSupportedProperty(AMDDisplayTemperatureSlider, this);
            amdDisplayTemperature = new AMDDisplayTemperatureProperty(AMDDisplayTemperatureSlider, this);

            // Lossless Scaling properties
            losslessScalingInstalled = new LosslessScalingInstalledProperty(this);
            losslessScalingRunning = new LosslessScalingRunningProperty();
            losslessScalingEnabled = new LosslessScalingEnabledProperty(LosslessScalingEnabledToggle, this);
            losslessScalingCurrentProfile = new LosslessScalingCurrentProfileProperty();
            losslessScalingScalingType = new LosslessScalingScalingTypeProperty(LosslessScalingScalingTypeComboBox, this);
            losslessScalingSharpness = new LosslessScalingSharpnessProperty(50, LosslessScalingSharpnessSlider, this);
            losslessScalingFSROptimize = new LosslessScalingFSROptimizeProperty(LosslessScalingFSROptimizeToggle, this);
            losslessScalingAnime4KSize = new LosslessScalingAnime4KSizeProperty(LosslessScalingAnime4KSizeComboBox, this);
            losslessScalingAnime4KVRS = new LosslessScalingAnime4KVRSProperty(LosslessScalingAnime4KVRSToggle, this);
            losslessScalingScaleMode = new LosslessScalingScaleModeProperty(LosslessScalingScaleModeComboBox, this);
            losslessScalingScaleFactor = new LosslessScalingScaleFactorProperty(2, LosslessScalingScaleFactorSlider, this);
            losslessScalingAspectRatio = new LosslessScalingAspectRatioProperty(LosslessScalingAspectRatioComboBox, this);
            losslessScalingFrameGenType = new LosslessScalingFrameGenTypeProperty(LosslessScalingFrameGenTypeComboBox, this);
            losslessScalingLSFG3Mode = new LosslessScalingLSFG3ModeProperty(LosslessScalingLSFG3ModeComboBox, this);
            losslessScalingLSFG3Multiplier = new LosslessScalingLSFG3MultiplierProperty(LosslessScalingLSFG3MultiplierComboBox, this);
            losslessScalingLSFG3Target = new LosslessScalingLSFG3TargetProperty(120, LosslessScalingLSFG3TargetSlider, this);
            losslessScalingLSFG2Mode = new LosslessScalingLSFG2ModeProperty(LosslessScalingLSFG2ModeComboBox, this);
            losslessScalingFlowScale = new LosslessScalingFlowScaleProperty(50, LosslessScalingFlowScaleSlider, this);
            losslessScalingSize = new LosslessScalingSizeProperty(LosslessScalingSizeToggle, this);
            losslessScalingAutoScale = new LosslessScalingAutoScaleProperty(LosslessScalingAutoScaleToggle, this);
            losslessScalingAutoScaleDelay = new LosslessScalingAutoScaleDelayProperty(0, LosslessScalingAutoScaleDelaySlider, this);
            losslessScalingSaveAndRestart = new LosslessScalingSaveAndRestartProperty();
            losslessScalingCreateProfile = new LosslessScalingCreateProfileProperty();
            losslessScalingBringToForeground = new LosslessScalingBringToForegroundProperty();
            losslessScalingLaunch = new LosslessScalingLaunchProperty();

            // Legion Go properties
            legionGoDetected = new LegionGoDetectedProperty(this);
            legionTouchpadEnabled = new LegionTouchpadEnabledProperty(LegionTouchpadToggle, this);
            legionLightMode = new LegionLightModeProperty(LegionLightModeComboBox, this);
            legionLightColor = new LegionLightColorProperty(LegionColorPicker, this);
            legionLightBrightness = new LegionLightBrightnessProperty(LegionBrightnessSlider, this);
            legionLightSpeed = new LegionLightSpeedProperty(LegionSpeedSlider, this);
            legionPerformanceMode = new LegionPerformanceModeProperty(LegionPerformanceModeComboBox, this);
            legionCustomTDPSlow = new LegionCustomTDPSlowProperty(LegionCustomTDPSlowSlider, this);
            legionCustomTDPFast = new LegionCustomTDPFastProperty(LegionCustomTDPFastSlider, this);
            legionCustomTDPPeak = new LegionCustomTDPPeakProperty(LegionCustomTDPPeakSlider, this);
            legionFanFullSpeed = new LegionFanFullSpeedProperty(LegionFanFullSpeedToggle, this);

            // Fan curve graph properties
            legionFanCurveGraph = new LegionFanCurveGraphProperty(this);
            legionFanCurveGraph.SetGraphUpdateCallback(OnFanCurveUpdated);
            legionCPUTemp = new LegionCPUTempProperty(this);
            legionCPUTemp.SetTempUpdateCallback(OnCPUTempUpdated);
            legionFanSensorTemp = new LegionFanSensorTempProperty(this);
            legionFanSensorTemp.SetTempUpdateCallback(OnFanSensorTempUpdated);
            legionCPUFanRPM = new LegionCPUFanRPMProperty(this);
            legionCPUFanRPM.SetRPMUpdateCallback(OnFanRPMUpdated);
            legionFanCurveVisible = new LegionFanCurveVisibleProperty();

            legionGyroEnabled = new LegionGyroEnabledProperty(null, this); // Gyro removed from UI, kept for backwards compatibility
            legionVibration = new LegionVibrationProperty(LegionVibrationComboBox, this);
            legionPowerLight = new LegionPowerLightProperty(LegionPowerLightToggle, this);
            legionChargeLimit = new LegionChargeLimitProperty(LegionChargeLimitToggle, this);

            // Controller remapping properties (not auto-bound, use SendMapping())
            legionButtonY1 = new LegionButtonY1Property(LegionButtonY1ComboBox, this);
            legionButtonY2 = new LegionButtonY2Property(LegionButtonY2ComboBox, this);
            legionButtonY3 = new LegionButtonY3Property(LegionButtonY3ComboBox, this);
            legionButtonM1 = new LegionButtonM1Property(LegionButtonM1ComboBox, this);
            legionButtonM2 = new LegionButtonM2Property(LegionButtonM2ComboBox, this);
            legionButtonM3 = new LegionButtonM3Property(LegionButtonM3ComboBox, this);
            legionButtonDesktop = new LegionButtonDesktopProperty(LegionButtonDesktopComboBox, this);
            legionButtonPage = new LegionButtonPageProperty(LegionButtonPageComboBox, this);
            legionNintendoLayout = new LegionNintendoLayoutProperty(LegionNintendoLayoutToggle, this);
            legionVibrationMode = new LegionVibrationModeProperty(LegionVibrationModeComboBox, this);
            legionControllerProfile = new LegionControllerProfileProperty(LegionControllerProfileToggle, this);

            // Gyro properties
            legionGyroTarget = new LegionGyroTargetProperty(LegionGyroTargetComboBox, this);
            legionGyroSensitivityX = new LegionGyroSensitivityXProperty(LegionGyroSensitivityXSlider, this);
            legionGyroSensitivityY = new LegionGyroSensitivityYProperty(LegionGyroSensitivityYSlider, this);
            legionGyroInvertX = new LegionGyroInvertXProperty(LegionGyroInvertXToggle, this);
            legionGyroInvertY = new LegionGyroInvertYProperty(LegionGyroInvertYToggle, this);
            legionGyroMappingType = new LegionGyroMappingTypeProperty(LegionGyroMappingTypeComboBox, this);
            legionGyroActivationMode = new LegionGyroActivationModeProperty(LegionGyroActivationModeComboBox, this);
            legionGyroActivationButton = new LegionGyroActivationButtonProperty(LegionGyroActivationButtonComboBox, this);

            // Advanced gyro properties
            legionGyroDeadzone = new LegionGyroDeadzoneProperty(LegionGyroDeadzoneSlider, this);

            // Stick deadzone properties
            legionLeftStickDeadzone = new LegionLeftStickDeadzoneProperty(LegionLeftStickDeadzoneSlider, this);
            legionRightStickDeadzone = new LegionRightStickDeadzoneProperty(LegionRightStickDeadzoneSlider, this);

            // Trigger travel properties
            legionLeftTriggerStart = new LegionLeftTriggerStartProperty(LegionLeftTriggerStartSlider, this);
            legionLeftTriggerEnd = new LegionLeftTriggerEndProperty(LegionLeftTriggerEndSlider, this);
            legionRightTriggerStart = new LegionRightTriggerStartProperty(LegionRightTriggerStartSlider, this);
            legionRightTriggerEnd = new LegionRightTriggerEndProperty(LegionRightTriggerEndSlider, this);
            legionHairTriggers = new LegionHairTriggersProperty(LegionHairTriggersToggle, this);

            // Touchpad vibration property (GLOBAL setting)
            legionTouchpadVibration = new LegionTouchpadVibrationProperty(LegionTouchpadVibrationComboBox, this);

            // Joystick as mouse properties
            legionJoystickAsMouseMode = new LegionJoystickAsMouseModeProperty(LegionJoystickAsMouseComboBox, LegionJoystickMouseSensGrid, this);
            legionJoystickMouseSens = new LegionJoystickMouseSensProperty(LegionJoystickMouseSensSlider, this);

            // Gamepad button mapping property
            legionGamepadMapping = new LegionGamepadMappingProperty();

            // Desktop controls preset (synced from helper for hotkey)
            legionDesktopControls = new LegionDesktopControlsProperty(LegionDesktopControlsToggle, this);

            // Controller battery properties (read-only)
            controllerBatteryLeft = new ControllerBatteryLeftProperty();
            controllerBatteryRight = new ControllerBatteryRightProperty();
            controllerChargingLeft = new ControllerChargingLeftProperty();
            controllerChargingRight = new ControllerChargingRightProperty();
            controllerConnectedLeft = new ControllerConnectedLeftProperty();
            controllerConnectedRight = new ControllerConnectedRightProperty();
            controllerVidPid = new ControllerVidPidProperty();

            // Device capability properties (for UI visibility)
            deviceDisplayName = new DeviceDisplayNameProperty(this);
            deviceSupportsControllerRemap = new DeviceSupportsControllerRemapProperty(this);
            deviceSupportsRgbLighting = new DeviceSupportsRgbLightingProperty(this);
            deviceSupportsGyro = new DeviceSupportsGyroProperty(this);
            deviceHasScrollWheel = new DeviceHasScrollWheelProperty(this);
            deviceHasDetachableControllers = new DeviceHasDetachableControllersProperty(this);
            deviceHasTouchpad = new DeviceHasTouchpadProperty(this);

            // GPD properties
            gpdDetected = new GPDDetectedProperty(this);
            gpdWin5Connected = new GPDWin5ConnectedProperty(this);
            gpdWin5HidDebug = new GPDWin5HidDebugProperty();
            gpdWin5HidDevices = new GPDWin5HidDevicesProperty(this);
            gpdDeviceName = new GPDDeviceNameProperty(this);
            gpdSupportsFanControl = new GPDSupportsFanControlProperty(this);
            gpdFanSpeed = new GPDFanSpeedProperty(this);
            gpdFanRPM = new GPDFanRPMProperty(this);
            gpdFanMode = new GPDFanModeProperty(this);
            gpdRestoreDefaults = new GPDRestoreDefaultsProperty();
            gpdApplyMappings = new GPDApplyMappingsProperty();
            gpdButtonL4 = new GPDButtonL4Property(this);
            gpdButtonR4 = new GPDButtonR4Property(this);
            gpdButtonA = new GPDButtonProperty(this, Function.GPDButtonA);
            gpdButtonB = new GPDButtonProperty(this, Function.GPDButtonB);
            gpdButtonX = new GPDButtonProperty(this, Function.GPDButtonX);
            gpdButtonY = new GPDButtonProperty(this, Function.GPDButtonY);
            gpdButtonDPadUp = new GPDButtonProperty(this, Function.GPDButtonDPadUp);
            gpdButtonDPadDown = new GPDButtonProperty(this, Function.GPDButtonDPadDown);
            gpdButtonDPadLeft = new GPDButtonProperty(this, Function.GPDButtonDPadLeft);
            gpdButtonDPadRight = new GPDButtonProperty(this, Function.GPDButtonDPadRight);
            gpdButtonL3 = new GPDButtonProperty(this, Function.GPDButtonL3);
            gpdButtonR3 = new GPDButtonProperty(this, Function.GPDButtonR3);
            gpdButtonLSLeft = new GPDButtonProperty(this, Function.GPDButtonLSLeft);
            gpdButtonLSRight = new GPDButtonProperty(this, Function.GPDButtonLSRight);
            controllerEmulationAvailable = new ControllerEmulationAvailableProperty(this);
            controllerEmulationEnabled = new ControllerEmulationEnabledProperty(ControllerEmulationEnabledToggle, this);
            controllerEmulationHideStockController = new ControllerEmulationHideStockControllerProperty(ControllerEmulationHideStockControllerToggle, this);
            controllerEmulationImprovedInput = new ControllerEmulationImprovedInputProperty(ControllerEmulationImprovedInputToggle, this);
            controllerEmulationImprovedInput.PropertyChanged += ControllerEmulationImprovedInput_PropertyChanged;
            controllerEmulationHideTarget = new ControllerEmulationHideTargetProperty(ControllerEmulationHideTargetComboBox, this);
            controllerEmulationGyroSource = new ControllerEmulationGyroSourceProperty(ControllerEmulationGyroSourceComboBox, this);
            controllerEmulationMode = new ControllerEmulationModeProperty(ControllerEmulationModeComboBox, this);
            controllerEmulationRumbleProfile = new ControllerEmulationRumbleProfileProperty(ControllerEmulationRumbleProfileComboBox, this);
            controllerEmulationGyroActivationMode = new ControllerEmulationGyroActivationModeProperty(ControllerEmulationGyroActivationModeComboBox, this);
            controllerEmulationGyroActivationButton = new ControllerEmulationGyroActivationButtonProperty(ControllerEmulationGyroActivationButtonComboBox, this);
            controllerEmulationDs4Orientation = new ControllerEmulationDs4OrientationProperty(ControllerEmulationDs4OrientationComboBox, this);
            controllerEmulationPs4TouchpadEnabled = new ControllerEmulationPs4TouchpadEnabledProperty(ControllerEmulationPs4TouchpadToggle, this);
            controllerEmulationLedForwardingEnabled = new ControllerEmulationLedForwardingEnabledProperty(ControllerEmulationLedForwardingToggle, this);
            controllerEmulationMouseSensitivity = new ControllerEmulationMouseSensitivityProperty(ControllerEmulationMouseSensitivitySlider, this);
            controllerEmulationMouseThreshold = new ControllerEmulationMouseThresholdProperty(ControllerEmulationMouseThresholdSlider, this);
            controllerEmulationMouseAxis = new ControllerEmulationMouseAxisProperty(ControllerEmulationMouseAxisComboBox, this);
            controllerEmulationMouseInvertX = new ControllerEmulationMouseInvertXProperty(ControllerEmulationMouseInvertXToggle, this);
            controllerEmulationMouseInvertY = new ControllerEmulationMouseInvertYProperty(ControllerEmulationMouseInvertYToggle, this);
            controllerEmulationMouseGainX = new ControllerEmulationMouseGainXProperty(ControllerEmulationMouseGainXSlider, this);
            controllerEmulationMouseGainY = new ControllerEmulationMouseGainYProperty(ControllerEmulationMouseGainYSlider, this);
            controllerEmulationStickInvertX = new ControllerEmulationStickInvertXProperty(ControllerEmulationStickInvertXToggle, this);
            controllerEmulationStickInvertY = new ControllerEmulationStickInvertYProperty(ControllerEmulationStickInvertYToggle, this);
            controllerEmulationStickSelect = new ControllerEmulationStickSelectProperty(ControllerEmulationStickSelectComboBox, this);
            controllerEmulationStickOnlyJoystickData = new ControllerEmulationStickOnlyJoystickDataProperty(ControllerEmulationStickOnlyJoystickToggle, this);
            controllerEmulationStickMinGyroSpeed = new ControllerEmulationStickMinGyroSpeedProperty(StickMinGyroSpeedSlider, this);
            controllerEmulationStickMaxGyroSpeed = new ControllerEmulationStickMaxGyroSpeedProperty(StickMaxGyroSpeedSlider, this);
            controllerEmulationStickMinOutput = new ControllerEmulationStickMinOutputProperty(StickMinOutputSlider, this);
            controllerEmulationStickMaxOutput = new ControllerEmulationStickMaxOutputProperty(StickMaxOutputSlider, this);
            controllerEmulationStickPowerCurve = new ControllerEmulationStickPowerCurveProperty(StickPowerCurveSlider, this);
            controllerEmulationStickSensitivityV2 = new ControllerEmulationStickSensitivityV2Property(StickSensitivityV2Slider, this);
            controllerEmulationStickDeadzone = new ControllerEmulationStickDeadzoneProperty(StickDeadzoneSlider, this);
            controllerEmulationStickPrecisionSpeed = new ControllerEmulationStickPrecisionSpeedProperty(StickPrecisionSpeedSlider, this);
            controllerEmulationStickOutputMix = new ControllerEmulationStickOutputMixProperty(StickOutputMixSlider, this);
            controllerEmulationStickOrientationV2 = new ControllerEmulationStickOrientationV2Property(StickOrientationV2ComboBox, this);
            controllerEmulationStickConversion = new ControllerEmulationStickConversionProperty(StickConversionComboBox, this);
            controllerEmulationVirtualAbxyLayout = new ControllerEmulationVirtualABXYLayoutProperty(ControllerEmulationVirtualAbxyLayoutComboBox, this);
            gpdFanCurveGraph = new GPDFanCurveGraphProperty(this);
            gpdFanCurveGraph.SetGraphUpdateCallback(OnGPDFanCurveUpdated);
            gpdCPUTemp = new GPDCPUTempProperty(this);
            gpdCPUTemp.SetTempUpdateCallback(OnGPDCPUTempUpdated);
            gpdFanCurveVisible = new GPDFanCurveVisibleProperty();
            gpdFanCurveEnabled = new GPDFanCurveEnabledProperty(this);

            // Default Game Profile properties
            defaultGameProfileAvailable = new DefaultGameProfileAvailableProperty(this);
            defaultGameProfileData = new DefaultGameProfileDataProperty(this);
            defaultGameProfileEnabled = new DefaultGameProfileEnabledProperty(this);

            // Set up Default Game Profile callbacks
            defaultGameProfileAvailable.SetVisibilityCallback(SetDefaultProfileCardVisibility);
            defaultGameProfileData.SetDataCallback(UpdateDefaultProfileDisplay);
            defaultGameProfileEnabled.BindToggle(DefaultProfileToggle);
            defaultGameProfileEnabled.SetEnabledCallback(OnDefaultProfileEnabledChanged);

            // Settings properties
            tdpMethod = new TdpMethodProperty(TdpMethodComboBox, this);
            winRing0Available = new WinRing0AvailableProperty(this);
            pawnIOInstalled = new PawnIOInstalledProperty(this);
            installPawnIO = new InstallPawnIOProperty(this);
            vigemBusInstalled = new ViGEmBusInstalledProperty(this);
            installViGEmBus = new InstallViGEmBusProperty(this);
            hidHideInstalled = new HidHideInstalledProperty(this);
            installHidHide = new InstallHidHideProperty(this);
            autoHibernateEnabled = new AutoHibernateEnabledProperty(AutoHibernateToggle, this);
            autoHibernateIdleMinutes = new AutoHibernateIdleMinutesProperty(15, AutoHibernateTimeoutSlider, this);

            // Set up callbacks for TDP method availability
            winRing0Available.SetAvailabilityCallback(UpdateWinRing0Visibility);
            pawnIOInstalled.SetInstalledCallback(UpdatePawnIOInstalledUI);
            vigemBusInstalled.SetInstalledCallback(UpdateViGEmBusInstalledUI);
            hidHideInstalled.SetInstalledCallback(UpdateHidHideInstalledUI);

            // AutoTDP properties
            autoTDPEnabled = new AutoTDPEnabledProperty(false);
            autoTDPTargetFPS = new AutoTDPTargetFPSProperty(60);
            autoTDPCurrentFPS = new AutoTDPCurrentFPSProperty(0);
            autoTDPMinTDP = new AutoTDPMinTDPProperty(8);
            autoTDPMaxTDP = new AutoTDPMaxTDPProperty(30);
            autoTDPUseMLMode = new AutoTDPUseMLModeProperty(false);
            autoTDPControllerType = new AutoTDPControllerTypeProperty(0);  // 0=PID
            autoTDPMLStatus = new AutoTDPMLStatusProperty("");
            autoTDPLearnedGameData = new AutoTDPLearnedGameDataProperty("");
            autoTDPResetML = new AutoTDPResetMLProperty(false);
            autoTDPPauseWhenUnfocused = new AutoTDPPauseWhenUnfocusedProperty(true); // Default: enabled
            tdpLimits = new TDPLimitsProperty("4,35");
            cpuCoreConfig = new CPUCoreConfigProperty("");
            cpuCoreActiveConfig = new CPUCoreActiveConfigProperty("");
            coreParkingPercent = new CoreParkingPercentProperty(100); // 100% = all cores active
            forceParkMode = new ForceParkModeProperty(false);
            forceDefaultGameProfile = new ForceDefaultGameProfileProperty(false);

            // TDP Boost properties (defaults: enabled=false, SPPT=1W, FPPT=3W)
            tdpBoostEnabled = new TDPBoostEnabledProperty(false);
            tdpBoostSPPT = new TDPBoostSPPTProperty(1);
            tdpBoostFPPT = new TDPBoostFPPTProperty(3);

            // OS Power Mode property
            osPowerMode = new OSPowerModeProperty();

            // FPS Limit property
            fpsLimit = new FPSLimitProperty();

            // Profile Detection Settings
            profileMatchByExe = new ProfileMatchByExeProperty(ProfileMatchByExeToggle, this);
            profileGamesOnly = new ProfileGamesOnlyProperty(ProfileGamesOnlyToggle, this);
            // DISABLED: Custom games, blacklist, and current apps features - caused user confusion
            // profileCustomGamePath = new ProfileCustomGamePathProperty(CustomGamesList, CustomGamesEmptyText, this);
            // profileBlacklistPaths = new ProfileBlacklistPathsProperty(BlacklistList, BlacklistEmptyText, this);
            // foregroundApp = new ForegroundAppProperty(ForegroundAppsContainer, this);
            // foregroundApp.OnAppsChanged = UpdateForegroundAppsList;

            // Set up Legion tab visibility callback
            legionGoDetected.SetVisibilityCallback(SetLegionTabVisibility);

            // Set up Scale tab visibility callback (hide when Lossless Scaling not installed)
            losslessScalingInstalled.SetVisibilityCallback(SetScaleTabVisibility);

            // Set up custom TDP visibility callback
            legionPerformanceMode.SetCustomTDPVisibilityCallback(SetCustomTDPVisibility);

            // Set up device capability callbacks for Legion tab section visibility
            deviceDisplayName.SetDisplayNameCallback(SetLegionDeviceName);
            deviceSupportsControllerRemap.SetVisibilityCallback(SetControllerRemappingSectionVisibility);
            deviceSupportsRgbLighting.SetVisibilityCallback(SetLightingSectionVisibility);
            deviceSupportsGyro.SetVisibilityCallback(SetGyroSectionVisibility);
            deviceHasScrollWheel.SetVisibilityCallback(SetScrollWheelSectionVisibility);
            deviceHasDetachableControllers.SetVisibilityCallback(SetControllerBatterySectionVisibility);
            deviceHasTouchpad.SetVisibilityCallback(SetTouchpadVibrationSectionVisibility);

            // GPD tab callbacks
            gpdDetected.SetVisibilityCallback(SetGPDTabVisibility);
            gpdDeviceName.SetNameCallback(SetGPDDeviceName);
            gpdSupportsFanControl.SetVisibilityCallback(SetGPDFanControlVisibility);
            gpdWin5Connected.SetConnectionCallback(SetGPDButtonRemapVisibility);
            controllerEmulationAvailable.SetAvailabilityCallback(SetControllerEmulationAvailability);
            gpdFanRPM.SetRPMCallback(UpdateGPDFanRPM);
            gpdFanMode.SetModeCallback(UpdateGPDFanMode);

            // NOTE: Event handlers for Chill FPS will be registered AFTER first sync
            // to avoid crash when binding evaluates RadeonChillOnText before both values are ready
            // See RegisterChillFPSHandlers() called after sync completes

            propertiesTimer.Stop();
            Logger.Info($"[TIMING] Property creation: {propertiesTimer.ElapsedMilliseconds}ms");

            var widgetPropsTimer = Stopwatch.StartNew();
            properties = new WidgetProperties(
                osd,
                tdp,
                runningGame,
                perGameProfile,
                cpuBoost,
                cpuEPP,
                maxCPUState,
                minCPUState,
                // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
                //limitGPUClock,
                //gpuClockMin,
                //gpuClockMax,
                refreshRates,
                refreshRate,
                resolutions,
                resolution,
                displayOrientation,
                hdrSupported,
                hdrEnabled,
                trackedGame,
                rtssInstalled,
                isForeground,
                amdRadeonSuperResolutionEnabled,
                amdRadeonSuperResolutionSupported,
                amdRadeonSuperResolutionSharpness,
                amdFluidMotionFrameEnabled,
                amdFluidMotionFrameSupported,
                amdRadeonAntiLagEnabled,
                amdRadeonAntiLagSupported,
                amdRadeonBoostEnabled,
                amdRadeonBoostSupported,
                amdRadeonBoostResolution,
                amdRadeonChillEnabled,
                amdRadeonChillSupported,
                amdRadeonChillMinFPSProperty,
                amdRadeonChillMaxFPSProperty,
                amdImageSharpeningEnabled,
                amdImageSharpeningSupported,
                amdImageSharpeningSharpness,
                amdDisplayBrightnessSupported,
                amdDisplayBrightness,
                amdDisplayContrastSupported,
                amdDisplayContrast,
                amdDisplaySaturationSupported,
                amdDisplaySaturation,
                amdDisplayTemperatureSupported,
                amdDisplayTemperature,
                losslessScalingInstalled,
                losslessScalingRunning,
                losslessScalingEnabled,
                losslessScalingCurrentProfile,
                losslessScalingScalingType,
                losslessScalingSharpness,
                losslessScalingFSROptimize,
                losslessScalingAnime4KSize,
                losslessScalingAnime4KVRS,
                losslessScalingScaleMode,
                losslessScalingScaleFactor,
                losslessScalingAspectRatio,
                losslessScalingFrameGenType,
                losslessScalingLSFG3Mode,
                losslessScalingLSFG3Multiplier,
                losslessScalingLSFG3Target,
                losslessScalingLSFG2Mode,
                losslessScalingFlowScale,
                losslessScalingSize,
                losslessScalingAutoScale,
                losslessScalingAutoScaleDelay,
                losslessScalingSaveAndRestart,
                losslessScalingCreateProfile,
                currentTdp,
                legionGoDetected,
                legionTouchpadEnabled,
                legionLightMode,
                legionLightColor,
                legionLightBrightness,
                legionPerformanceMode,
                legionCustomTDPSlow,
                legionCustomTDPFast,
                legionCustomTDPPeak,
                legionFanFullSpeed,
                legionFanCurveGraph,
                legionCPUTemp,
                legionFanSensorTemp,
                legionCPUFanRPM,
                legionFanCurveVisible,
                legionGyroEnabled,
                legionVibration,
                legionPowerLight,
                legionChargeLimit,
                tdpMethod,
                winRing0Available,
                pawnIOInstalled,
                installPawnIO,
                vigemBusInstalled,
                installViGEmBus,
                hidHideInstalled,
                installHidHide,
                autoHibernateEnabled,
                autoHibernateIdleMinutes,
                autoTDPEnabled,
                autoTDPTargetFPS,
                autoTDPCurrentFPS,
                autoTDPMinTDP,
                autoTDPMaxTDP,
                autoTDPUseMLMode,
                autoTDPControllerType,
                autoTDPPauseWhenUnfocused,
                autoTDPMLStatus,
                autoTDPLearnedGameData,
                autoTDPResetML,
                fpsLimit,
                osPowerMode,
                tdpLimits,
                cpuCoreConfig,
                cpuCoreActiveConfig,
                coreParkingPercent,
                forceParkMode,
                tdpBoostEnabled,
                tdpBoostSPPT,
                tdpBoostFPPT,
                legionDesktopControls,
                legionJoystickAsMouseMode,
                legionJoystickMouseSens,
                controllerBatteryLeft,
                controllerBatteryRight,
                controllerChargingLeft,
                controllerChargingRight,
                controllerConnectedLeft,
                controllerConnectedRight,
                controllerVidPid,
                // Device capability properties (for UI visibility)
                deviceDisplayName,
                deviceSupportsControllerRemap,
                deviceSupportsRgbLighting,
                deviceSupportsGyro,
                deviceHasScrollWheel,
                deviceHasDetachableControllers,
                deviceHasTouchpad,
                // GPD properties
                gpdDetected,
                gpdWin5Connected,
                gpdWin5HidDebug,
                gpdWin5HidDevices,
                gpdDeviceName,
                gpdSupportsFanControl,
                gpdFanSpeed,
                gpdFanRPM,
                gpdFanMode,
                gpdRestoreDefaults,
                gpdApplyMappings,
                gpdButtonL4,
                gpdButtonR4,
                gpdButtonA,
                gpdButtonB,
                gpdButtonX,
                gpdButtonY,
                gpdButtonDPadUp,
                gpdButtonDPadDown,
                gpdButtonDPadLeft,
                gpdButtonDPadRight,
                gpdButtonL3,
                gpdButtonR3,
                gpdButtonLSLeft,
                gpdButtonLSRight,
                controllerEmulationAvailable,
                controllerEmulationEnabled,
                controllerEmulationHideStockController,
                controllerEmulationImprovedInput,
                controllerEmulationHideTarget,
                controllerEmulationGyroSource,
                controllerEmulationMode,
                controllerEmulationRumbleProfile,
                controllerEmulationGyroActivationMode,
                controllerEmulationGyroActivationButton,
                controllerEmulationDs4Orientation,
                controllerEmulationPs4TouchpadEnabled,
                controllerEmulationLedForwardingEnabled,
                controllerEmulationMouseSensitivity,
                controllerEmulationMouseThreshold,
                controllerEmulationMouseAxis,
                controllerEmulationMouseInvertX,
                controllerEmulationMouseInvertY,
                controllerEmulationMouseGainX,
                controllerEmulationMouseGainY,
                controllerEmulationStickInvertX,
                controllerEmulationStickInvertY,
                controllerEmulationStickSelect,
                controllerEmulationStickOnlyJoystickData,
                controllerEmulationStickMinGyroSpeed,
                controllerEmulationStickMaxGyroSpeed,
                controllerEmulationStickMinOutput,
                controllerEmulationStickMaxOutput,
                controllerEmulationStickPowerCurve,
                controllerEmulationStickSensitivityV2,
                controllerEmulationStickDeadzone,
                controllerEmulationStickPrecisionSpeed,
                controllerEmulationStickOutputMix,
                controllerEmulationStickOrientationV2,
                controllerEmulationStickConversion,
                controllerEmulationVirtualAbxyLayout,
                gpdFanCurveGraph,
                gpdCPUTemp,
                gpdFanCurveVisible,
                gpdFanCurveEnabled,
                defaultGameProfileAvailable,
                defaultGameProfileData,
                defaultGameProfileEnabled,
                forceDefaultGameProfile,
                // Profile Detection Settings
                profileMatchByExe,
                profileGamesOnly
                // DISABLED: Custom games, blacklist, and current apps features
                // profileCustomGamePath,
                // profileBlacklistPaths,
                // foregroundApp
            );
            widgetPropsTimer.Stop();
            Logger.Info($"[TIMING] WidgetProperties creation: {widgetPropsTimer.ElapsedMilliseconds}ms");

            // Register card focus handlers for all interactive controls
            RegisterCardFocusHandlers();

            constructorTimer.Stop();
            Logger.Info($"=== GamingWidget constructor END === Total: {constructorTimer.ElapsedMilliseconds}ms, Instance hash: {this.GetHashCode()}");
        }

        // Track the currently focused card
        private Border currentFocusedCard = null;
        private SolidColorBrush cardDefaultBorderBrush;
        private SolidColorBrush cardFocusBorderBrush;

        private void RegisterCardFocusHandlers()
        {
            // Get brushes from resources
            cardDefaultBorderBrush = (SolidColorBrush)Resources["CardBorderBrush"];
            cardFocusBorderBrush = (SolidColorBrush)Resources["CardFocusBorderBrush"];

            // Register focus handler on navigation items to clear card focus when tabs get focus
            foreach (var item in MainNavPanel.Children)
            {
                if (item is RadioButton radioButton)
                {
                    radioButton.GotFocus += NavItem_GotFocus;
                }
            }

            // Register GotFocus/LostFocus on interactive controls
            // Performance tab - Active Profile card
            PerGameProfileToggle.GotFocus += Control_GotFocus;
            PerGameProfileToggle.LostFocus += Control_LostFocus;

            // Performance tab - Default Game Profile card
            DefaultProfileToggle.GotFocus += Control_GotFocus;
            DefaultProfileToggle.LostFocus += Control_LostFocus;

            // Performance tab - Performance Overlay card
            PerformanceOverlayComboBox.GotFocus += Control_GotFocus;
            PerformanceOverlayComboBox.LostFocus += Control_LostFocus;

            // Performance tab - TDP Mode card (Legion only)
            TDPModeComboBox.GotFocus += Control_GotFocus;
            TDPModeComboBox.LostFocus += Control_LostFocus;

            // Performance tab - TDP card
            TDPSlider.GotFocus += Control_GotFocus;
            TDPSlider.LostFocus += Control_LostFocus;

            // Performance tab - AutoTDP card
            AutoTDPToggle.GotFocus += Control_GotFocus;
            AutoTDPToggle.LostFocus += Control_LostFocus;
            AutoTDPTargetFPSSlider.GotFocus += Control_GotFocus;
            AutoTDPTargetFPSSlider.LostFocus += Control_LostFocus;

            // Performance tab - CPU Boost card
            CPUBoostToggle.GotFocus += Control_GotFocus;
            CPUBoostToggle.LostFocus += Control_LostFocus;

            // Performance tab - CPU EPP card
            CPUEPPSlider.GotFocus += Control_GotFocus;
            CPUEPPSlider.LostFocus += Control_LostFocus;

            // Performance tab - CPU State card
            MinCPUStateComboBox.GotFocus += Control_GotFocus;
            MinCPUStateComboBox.LostFocus += Control_LostFocus;
            MaxCPUStateComboBox.GotFocus += Control_GotFocus;
            MaxCPUStateComboBox.LostFocus += Control_LostFocus;

            // Performance tab - FPS Limit card
            FPSLimitToggle.GotFocus += Control_GotFocus;
            FPSLimitToggle.LostFocus += Control_LostFocus;
            FPSLimitSlider.GotFocus += Control_GotFocus;
            FPSLimitSlider.LostFocus += Control_LostFocus;

            // Performance tab - OS Power Mode card
            OSPowerModeComboBox.GotFocus += Control_GotFocus;
            OSPowerModeComboBox.LostFocus += Control_LostFocus;

            // Profiles tab - Power Source Profile card
            PowerSourceProfileToggle.GotFocus += Control_GotFocus;
            PowerSourceProfileToggle.LostFocus += Control_LostFocus;

            // Graphics tab - Resolution card
            ResolutionComboBox.GotFocus += Control_GotFocus;
            ResolutionComboBox.LostFocus += Control_LostFocus;

            // Graphics tab - Refresh Rate card
            RefreshRatesComboBox.GotFocus += Control_GotFocus;
            RefreshRatesComboBox.LostFocus += Control_LostFocus;

            // Graphics tab - HDR card
            HDRToggle.GotFocus += Control_GotFocus;
            HDRToggle.LostFocus += Control_LostFocus;

            // Graphics tab - AMD cards
            AMDRadeonSuperResolutionToggle.GotFocus += Control_GotFocus;
            AMDRadeonSuperResolutionToggle.LostFocus += Control_LostFocus;
            AMDRadeonSuperResolutionSharpnessSlider.GotFocus += Control_GotFocus;
            AMDRadeonSuperResolutionSharpnessSlider.LostFocus += Control_LostFocus;

            // Graphics tab - Image Sharpening card
            AMDImageSharpeningToggle.GotFocus += Control_GotFocus;
            AMDImageSharpeningToggle.LostFocus += Control_LostFocus;
            AMDImageSharpeningSlider.GotFocus += Control_GotFocus;
            AMDImageSharpeningSlider.LostFocus += Control_LostFocus;

            // Graphics tab - Color Settings card
            ColorSettingsExpandButton.GotFocus += Control_GotFocus;
            ColorSettingsExpandButton.LostFocus += Control_LostFocus;
            AMDDisplayBrightnessSlider.GotFocus += Control_GotFocus;
            AMDDisplayBrightnessSlider.LostFocus += Control_LostFocus;
            AMDDisplayContrastSlider.GotFocus += Control_GotFocus;
            AMDDisplayContrastSlider.LostFocus += Control_LostFocus;
            AMDDisplaySaturationSlider.GotFocus += Control_GotFocus;
            AMDDisplaySaturationSlider.LostFocus += Control_LostFocus;
            AMDDisplayTemperatureSlider.GotFocus += Control_GotFocus;
            AMDDisplayTemperatureSlider.LostFocus += Control_LostFocus;
            AMDFluidMotionFrameToggle.GotFocus += Control_GotFocus;
            AMDFluidMotionFrameToggle.LostFocus += Control_LostFocus;
            AMDRadeonAntiLagToggle.GotFocus += Control_GotFocus;
            AMDRadeonAntiLagToggle.LostFocus += Control_LostFocus;
            AMDRadeonBoostToggle.GotFocus += Control_GotFocus;
            AMDRadeonBoostToggle.LostFocus += Control_LostFocus;
            AMDRadeonBoostResolutionSlider.GotFocus += Control_GotFocus;
            AMDRadeonBoostResolutionSlider.LostFocus += Control_LostFocus;
            AMDRadeonChillToggle.GotFocus += Control_GotFocus;
            AMDRadeonChillToggle.LostFocus += Control_LostFocus;
            AMDRadeonChillMinFPSSlider.GotFocus += Control_GotFocus;
            AMDRadeonChillMinFPSSlider.LostFocus += Control_LostFocus;
            AMDRadeonChillMaxFPSSlider.GotFocus += Control_GotFocus;
            AMDRadeonChillMaxFPSSlider.LostFocus += Control_LostFocus;

            // System tab - Profile Settings card (checkboxes have individual focus, not card focus)
            // These use FocusableCheckBoxStyle which shows its own focus visual
            ProfileSaveTDPCheckBox.GotFocus += StandaloneControl_GotFocus;
            ProfileSaveCPUBoostCheckBox.GotFocus += StandaloneControl_GotFocus;
            ProfileSaveCPUEPPCheckBox.GotFocus += StandaloneControl_GotFocus;
            ProfileSaveCPUStateCheckBox.GotFocus += StandaloneControl_GotFocus;
            ProfileSaveAMDFeaturesCheckBox.GotFocus += StandaloneControl_GotFocus;

            // System tab - Sticky TDP card
            StickyTDPToggle.GotFocus += Control_GotFocus;
            StickyTDPToggle.LostFocus += Control_LostFocus;
            StickyTDPIntervalSlider.GotFocus += Control_GotFocus;
            StickyTDPIntervalSlider.LostFocus += Control_LostFocus;

            // System tab - TDP Method card
            TdpMethodComboBox.GotFocus += Control_GotFocus;
            TdpMethodComboBox.LostFocus += Control_LostFocus;

            // System tab - TDP Settings card
            TDPSettingsExpandButton.GotFocus += Control_GotFocus;
            TDPSettingsExpandButton.LostFocus += Control_LostFocus;
            TDPLimitsMinSlider.GotFocus += Control_GotFocus;
            TDPLimitsMinSlider.LostFocus += Control_LostFocus;
            TDPLimitsMaxSlider.GotFocus += Control_GotFocus;
            TDPLimitsMaxSlider.LostFocus += Control_LostFocus;

            // Performance tab - Advanced card (Power Plan controls)
            ACPowerPlanComboBox.GotFocus += Control_GotFocus;
            ACPowerPlanComboBox.LostFocus += Control_LostFocus;
            DCPowerPlanComboBox.GotFocus += Control_GotFocus;
            DCPowerPlanComboBox.LostFocus += Control_LostFocus;
            PowerPlanAutoSwitchToggle.GotFocus += Control_GotFocus;
            PowerPlanAutoSwitchToggle.LostFocus += Control_LostFocus;

            // System tab - OSD Customization card
            OSDCustomizeExpandButton.GotFocus += Control_GotFocus;
            OSDCustomizeExpandButton.LostFocus += Control_LostFocus;

            // System tab - Controller Emulation card
            ControllerEmulationExpandButton.GotFocus += Control_GotFocus;
            ControllerEmulationExpandButton.LostFocus += Control_LostFocus;
            ControllerEmulationInputNotesExpandButton.GotFocus += Control_GotFocus;
            ControllerEmulationInputNotesExpandButton.LostFocus += Control_LostFocus;
            ControllerEmulationEnabledToggle.GotFocus += Control_GotFocus;
            ControllerEmulationEnabledToggle.LostFocus += Control_LostFocus;
            ControllerEmulationImprovedInputToggle.GotFocus += Control_GotFocus;
            ControllerEmulationImprovedInputToggle.LostFocus += Control_LostFocus;
            ControllerEmulationHideStockControllerToggle.GotFocus += Control_GotFocus;
            ControllerEmulationHideStockControllerToggle.LostFocus += Control_LostFocus;
            ControllerEmulationHideTargetComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationHideTargetComboBox.LostFocus += Control_LostFocus;
            ControllerEmulationGyroSourceComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationGyroSourceComboBox.LostFocus += Control_LostFocus;
            ControllerEmulationModeComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationModeComboBox.LostFocus += Control_LostFocus;
            ControllerEmulationGyroActivationModeComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationGyroActivationModeComboBox.LostFocus += Control_LostFocus;
            ControllerEmulationGyroActivationButtonComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationGyroActivationButtonComboBox.LostFocus += Control_LostFocus;
            ControllerEmulationPs4TouchpadToggle.GotFocus += Control_GotFocus;
            ControllerEmulationPs4TouchpadToggle.LostFocus += Control_LostFocus;
            ControllerEmulationLedForwardingToggle.GotFocus += Control_GotFocus;
            ControllerEmulationLedForwardingToggle.LostFocus += Control_LostFocus;
            ControllerEmulationMouseSensitivitySlider.GotFocus += Control_GotFocus;
            ControllerEmulationMouseSensitivitySlider.LostFocus += Control_LostFocus;
            ControllerEmulationMouseThresholdSlider.GotFocus += Control_GotFocus;
            ControllerEmulationMouseThresholdSlider.LostFocus += Control_LostFocus;
            ControllerEmulationMouseAxisComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationMouseAxisComboBox.LostFocus += Control_LostFocus;
            ControllerEmulationMouseInvertXToggle.GotFocus += Control_GotFocus;
            ControllerEmulationMouseInvertXToggle.LostFocus += Control_LostFocus;
            ControllerEmulationMouseInvertYToggle.GotFocus += Control_GotFocus;
            ControllerEmulationMouseInvertYToggle.LostFocus += Control_LostFocus;
            ControllerEmulationMouseGainXSlider.GotFocus += Control_GotFocus;
            ControllerEmulationMouseGainXSlider.LostFocus += Control_LostFocus;
            ControllerEmulationMouseGainYSlider.GotFocus += Control_GotFocus;
            ControllerEmulationMouseGainYSlider.LostFocus += Control_LostFocus;
            StickConversionComboBox.GotFocus += Control_GotFocus;
            StickConversionComboBox.LostFocus += Control_LostFocus;
            StickOrientationV2ComboBox.GotFocus += Control_GotFocus;
            StickOrientationV2ComboBox.LostFocus += Control_LostFocus;
            StickSensitivityV2Slider.GotFocus += Control_GotFocus;
            StickSensitivityV2Slider.LostFocus += Control_LostFocus;
            ControllerEmulationStickInvertXToggle.GotFocus += Control_GotFocus;
            ControllerEmulationStickInvertXToggle.LostFocus += Control_LostFocus;
            ControllerEmulationStickInvertYToggle.GotFocus += Control_GotFocus;
            ControllerEmulationStickInvertYToggle.LostFocus += Control_LostFocus;
            StickMinGyroSpeedSlider.GotFocus += Control_GotFocus;
            StickMinGyroSpeedSlider.LostFocus += Control_LostFocus;
            StickMaxGyroSpeedSlider.GotFocus += Control_GotFocus;
            StickMaxGyroSpeedSlider.LostFocus += Control_LostFocus;
            StickMinOutputSlider.GotFocus += Control_GotFocus;
            StickMinOutputSlider.LostFocus += Control_LostFocus;
            StickMaxOutputSlider.GotFocus += Control_GotFocus;
            StickMaxOutputSlider.LostFocus += Control_LostFocus;
            StickPowerCurveSlider.GotFocus += Control_GotFocus;
            StickPowerCurveSlider.LostFocus += Control_LostFocus;
            StickDeadzoneSlider.GotFocus += Control_GotFocus;
            StickDeadzoneSlider.LostFocus += Control_LostFocus;
            StickPrecisionSpeedSlider.GotFocus += Control_GotFocus;
            StickPrecisionSpeedSlider.LostFocus += Control_LostFocus;
            StickOutputMixSlider.GotFocus += Control_GotFocus;
            StickOutputMixSlider.LostFocus += Control_LostFocus;
            ControllerEmulationStickSelectComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationStickSelectComboBox.LostFocus += Control_LostFocus;
            GyroActivationExpandToggle.GotFocus += Control_GotFocus;
            GyroActivationExpandToggle.LostFocus += Control_LostFocus;
            FeaturesExpandToggle.GotFocus += Control_GotFocus;
            FeaturesExpandToggle.LostFocus += Control_LostFocus;
            JoystickOutputExpandToggle.GotFocus += Control_GotFocus;
            JoystickOutputExpandToggle.LostFocus += Control_LostFocus;
            ControllerEmulationStickOnlyJoystickToggle.GotFocus += Control_GotFocus;
            ControllerEmulationStickOnlyJoystickToggle.LostFocus += Control_LostFocus;
            ControllerEmulationVirtualAbxyLayoutComboBox.GotFocus += Control_GotFocus;
            ControllerEmulationVirtualAbxyLayoutComboBox.LostFocus += Control_LostFocus;

            // System tab - Advanced card
            AdvancedExpandButton.GotFocus += Control_GotFocus;
            AdvancedExpandButton.LostFocus += Control_LostFocus;

            // Scaling tab - Status card buttons
            ShowLosslessScalingWindowButton.GotFocus += Control_GotFocus;
            ShowLosslessScalingWindowButton.LostFocus += Control_LostFocus;
            LaunchLosslessScalingButton.GotFocus += Control_GotFocus;
            LaunchLosslessScalingButton.LostFocus += Control_LostFocus;

            // Scaling tab - Current Profile card
            LosslessScalingCreateProfileButton.GotFocus += Control_GotFocus;
            LosslessScalingCreateProfileButton.LostFocus += Control_LostFocus;

            // Scaling tab - Scale and Save buttons (not in cards, clear focus)
            LosslessScalingEnabledToggle.GotFocus += StandaloneControl_GotFocus;
            LosslessScalingSaveSettingsButton.GotFocus += StandaloneControl_GotFocus;

            // Scaling tab - AutoScale card
            LosslessScalingAutoScaleToggle.GotFocus += Control_GotFocus;
            LosslessScalingAutoScaleToggle.LostFocus += Control_LostFocus;
            LosslessScalingAutoScaleDelaySlider.GotFocus += Control_GotFocus;
            LosslessScalingAutoScaleDelaySlider.LostFocus += Control_LostFocus;

            // Scaling tab - Scaling Type card
            LosslessScalingScalingTypeComboBox.GotFocus += Control_GotFocus;
            LosslessScalingScalingTypeComboBox.LostFocus += Control_LostFocus;
            LosslessScalingSharpnessSlider.GotFocus += Control_GotFocus;
            LosslessScalingSharpnessSlider.LostFocus += Control_LostFocus;
            LosslessScalingScaleModeComboBox.GotFocus += Control_GotFocus;
            LosslessScalingScaleModeComboBox.LostFocus += Control_LostFocus;
            LosslessScalingScaleFactorSlider.GotFocus += Control_GotFocus;
            LosslessScalingScaleFactorSlider.LostFocus += Control_LostFocus;
            LosslessScalingFrameGenTypeComboBox.GotFocus += Control_GotFocus;
            LosslessScalingFrameGenTypeComboBox.LostFocus += Control_LostFocus;
            LosslessScalingLSFG3ModeComboBox.GotFocus += Control_GotFocus;
            LosslessScalingLSFG3ModeComboBox.LostFocus += Control_LostFocus;
            LosslessScalingLSFG3MultiplierComboBox.GotFocus += Control_GotFocus;
            LosslessScalingLSFG3MultiplierComboBox.LostFocus += Control_LostFocus;
            LosslessScalingLSFG3TargetSlider.GotFocus += Control_GotFocus;
            LosslessScalingLSFG3TargetSlider.LostFocus += Control_LostFocus;
            LosslessScalingFlowScaleSlider.GotFocus += Control_GotFocus;
            LosslessScalingFlowScaleSlider.LostFocus += Control_LostFocus;
            LosslessScalingSizeToggle.GotFocus += Control_GotFocus;
            LosslessScalingSizeToggle.LostFocus += Control_LostFocus;
            LosslessScalingLSFG2ModeComboBox.GotFocus += Control_GotFocus;
            LosslessScalingLSFG2ModeComboBox.LostFocus += Control_LostFocus;

            // Legion tab - Touchpad card
            LegionTouchpadToggle.GotFocus += Control_GotFocus;
            LegionTouchpadToggle.LostFocus += Control_LostFocus;

            // Legion tab - Vibration card
            LegionVibrationComboBox.GotFocus += Control_GotFocus;
            LegionVibrationComboBox.LostFocus += Control_LostFocus;

            // Legion tab - Light Mode card
            LegionLightModeComboBox.GotFocus += Control_GotFocus;
            LegionLightModeComboBox.LostFocus += Control_LostFocus;

            // Legion tab - Light Color card (ColorPicker)
            LegionColorExpandButton.GotFocus += Control_GotFocus;
            LegionColorExpandButton.LostFocus += Control_LostFocus;
            LegionColorPicker.GotFocus += Control_GotFocus;
            LegionColorPicker.LostFocus += Control_LostFocus;

            // Legion tab - Brightness card
            LegionBrightnessSlider.GotFocus += Control_GotFocus;
            LegionBrightnessSlider.LostFocus += Control_LostFocus;

            // Legion tab - Performance Mode card
            LegionPerformanceModeComboBox.GotFocus += Control_GotFocus;
            LegionPerformanceModeComboBox.LostFocus += Control_LostFocus;

            // Legion tab - Custom TDP card
            LegionCustomTDPSlowSlider.GotFocus += Control_GotFocus;
            LegionCustomTDPSlowSlider.LostFocus += Control_LostFocus;
            LegionCustomTDPFastSlider.GotFocus += Control_GotFocus;
            LegionCustomTDPFastSlider.LostFocus += Control_LostFocus;
            LegionCustomTDPPeakSlider.GotFocus += Control_GotFocus;
            LegionCustomTDPPeakSlider.LostFocus += Control_LostFocus;

            // Legion tab - Fan Full Speed card
            LegionFanFullSpeedToggle.GotFocus += Control_GotFocus;
            LegionFanFullSpeedToggle.LostFocus += Control_LostFocus;

            // Legion tab - Power Light card
            LegionPowerLightToggle.GotFocus += Control_GotFocus;
            LegionPowerLightToggle.LostFocus += Control_LostFocus;

            // Legion tab - Charge Limit card
            LegionChargeLimitToggle.GotFocus += Control_GotFocus;
            LegionChargeLimitToggle.LostFocus += Control_LostFocus;
        }

        private void Control_GotFocus(object sender, RoutedEventArgs e)
        {
            // Card focus highlighting disabled - only controls show focus visuals
        }

        private void Control_LostFocus(object sender, RoutedEventArgs e)
        {
            // Don't clear immediately - let GotFocus of next control handle it
            // This prevents flicker when focus moves between controls in same card
        }

        private void NavItem_GotFocus(object sender, RoutedEventArgs e)
        {
            // Clear card highlight when navigation tabs get focus
            ClearCardFocus();
        }

        private void StandaloneControl_GotFocus(object sender, RoutedEventArgs e)
        {
            // Clear card highlight when standalone controls (not in cards) get focus
            ClearCardFocus();
        }

        private void ClearCardFocus()
        {
            if (currentFocusedCard != null)
            {
                currentFocusedCard.BorderBrush = cardDefaultBorderBrush;
                currentFocusedCard = null;
            }
        }

        private Border FindParentCard(DependencyObject element)
        {
            while (element != null)
            {
                if (element is Border border && border.Style == (Style)Resources["CardStyle"])
                {
                    return border;
                }
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private void GamingWidget_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Info($"GamingWidget_Loaded called. Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, Pipe connected: {App.IsConnected}");

            // Set initial navigation selection (first RadioButton - Quick tab)
            QuickNavItem.IsChecked = true;

            // Load profile customization settings
            LoadProfileCustomizationSettings();

            // Load TDP preset customization settings
            LoadTdpPresetsSettings();

            // Initialize CPU State comboboxes with percentage values
            InitializeCPUStateComboBoxes();

            // Check if this is a clean install (no saved Global profile)
            var settings = ApplicationData.Current.LocalSettings;
            isCleanInstall = !settings.Containers.ContainsKey("Profile_Global");
            if (isCleanInstall)
            {
                Logger.Info("Clean install detected - will initialize profile with current system values after helper sync");
            }

            // Load profiles from storage
            LoadProfileFromStorage("Global", globalProfile);
            LoadProfileFromStorage("AC", acProfile);
            LoadProfileFromStorage("DC", dcProfile);

            // Load global controller profile from storage
            LoadControllerProfileFromStorage("Global", globalControllerProfile);

            // Clean up any invalid "No game detected" profiles
            CleanupInvalidProfiles();

            // Load saved Power Source Profile toggle state BEFORE attaching event handler
            LoadPowerSourceProfileSetting();

            // Initialize power source profile
            PowerSourceProfileToggle.Toggled += PowerSourceProfileToggle_Toggled;
            SyncPowerSourceProfileToggleForCurrentContext();

            UpdateActiveProfileIndicator();

            // Subscribe to power source changes
            PowerManager.PowerSupplyStatusChanged += PowerManager_PowerSourceChanged;

            // Subscribe to checkbox changes to save settings
            // Event handlers are now in XAML (ProfileSettingsCheckBox_Changed)

            // IMPORTANT: Set loading flag BEFORE subscribing to events to prevent
            // UI initialization events from triggering saves that overwrite stored values
            isLoadingControllerProfile = true;

            // Subscribe to settings changes for auto-save
            SubscribeToSettingsChanges();

            // Apply global controller profile to UI controls
            // (ApplyControllerProfile will clear isLoadingControllerProfile when done)
            ApplyControllerProfile(globalControllerProfile);

            // Subscribe to power source profile toggle changes to update game profile card
            PowerSourceProfileToggle.Toggled += PowerSourceToggle_Changed;

            // Subscribe to per-game profile toggle changes
            PerGameProfileToggle.Toggled += PerGameProfileToggle_Changed;

            // Subscribe to Lossless Scaling FrameGenType ComboBox for showing/hiding LSFG settings
            LosslessScalingFrameGenTypeComboBox.SelectionChanged += LosslessScalingFrameGenTypeComboBox_SelectionChanged;
            AMDFluidMotionFrameToggle.Toggled += AMDFluidMotionFrameToggle_Toggled;

            // Subscribe to Lossless Scaling property changes for status updates
            if (losslessScalingInstalled != null)
                losslessScalingInstalled.PropertyChanged += LosslessScalingStatus_PropertyChanged;
            if (losslessScalingRunning != null)
                losslessScalingRunning.PropertyChanged += LosslessScalingStatus_PropertyChanged;
            if (losslessScalingCurrentProfile != null)
                losslessScalingCurrentProfile.PropertyChanged += LosslessScalingCurrentProfile_PropertyChanged;

            // Subscribe to running game changes to get exe path for Lossless Scaling profiles
            if (runningGame != null)
                runningGame.PropertyChanged += RunningGame_PropertyChanged;

            // Subscribe to game text changes
            RunningGameText.RegisterPropertyChangedCallback(TextBlock.TextProperty, OnGameTextChanged);

            // Subscribe to window size changes for compact mode detection
            this.SizeChanged += GamingWidget_SizeChanged;

            // Initialize compact mode based on current size
            UpdateCompactMode(this.ActualWidth);

            // Update profile display
            UpdateProfileDisplay();
            UpdateGameProfileCardVisibility();

            // Load Device TDP limits (must be before AutoTDP settings)
            LoadTDPLimitsFromStorage();

            // Load AutoTDP settings and subscribe to current FPS updates
            LoadAutoTDPSettings();
            if (autoTDPCurrentFPS != null)
                autoTDPCurrentFPS.PropertyChanged += AutoTDPCurrentFPS_PropertyChanged;
            if (autoTDPMLStatus != null)
                autoTDPMLStatus.PropertyChanged += AutoTDPMLStatus_PropertyChanged;
            if (autoTDPLearnedGameData != null)
                autoTDPLearnedGameData.PropertyChanged += AutoTDPLearnedGameData_PropertyChanged;
            if (autoTDPUseMLMode != null)
                autoTDPUseMLMode.PropertyChanged += AutoTDPUseMLMode_PropertyChanged;
            if (autoTDPControllerType != null)
                autoTDPControllerType.PropertyChanged += AutoTDPControllerType_PropertyChanged;
            if (autoTDPEnabled != null)
                autoTDPEnabled.PropertyChanged += AutoTDPEnabled_PropertyChanged;
            if (autoTDPTargetFPS != null)
                autoTDPTargetFPS.PropertyChanged += AutoTDPTargetFPS_PropertyChanged;

            // Load TDP Boost settings (SPPT/FPPT from LocalSettings)
            LoadTDPBoostSettings();

            // Load Sticky TDP settings (defaults to enabled on new installs)
            LoadStickyTDPSettings();

            // Subscribe to TDP Boost property changes from helper (profile sync)
            if (tdpBoostEnabled != null)
                tdpBoostEnabled.PropertyChanged += TDPBoostEnabled_PropertyChanged;

            // Subscribe to property changes that affect Quick Settings tiles
            SubscribeToQuickSettingsPropertyChanges();

            // Initialize Quick Settings tiles (loads custom shortcuts into qsTileMap)
            InitializeQuickSettings();

            // Load OSD customization settings
            LoadOSDConfigFromStorage();
            LoadOSDOptionsForLevel(1); // Load Basic level options by default

            // Load Display and OSD settings
            LoadDisplayOSDSettingsFromStorage();

            // Load Performance Overlay setting
            LoadPerformanceOverlaySetting();

            // Load Power Plan settings
            LoadPowerPlanSettings();

            // Load Force Default Game Profile setting
            LoadForceDefaultGameProfileSetting();

            // Send OSD config to helper on startup
            SendOSDConfigToHelper();
            _ = SendDisplayOSDConfigToHelper();

            // Initialize Labs section (DAService status polling)
            InitializeLabsSection();
        }

        private void AutoTDPCurrentFPS_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (AutoTDPCurrentFPSValue != null && autoTDPCurrentFPS != null)
                {
                    int fps = autoTDPCurrentFPS.Value;
                    AutoTDPCurrentFPSValue.Text = fps > 0 ? $"{fps} FPS" : "-- FPS";
                }
            });
        }

        private void SubscribeToQuickSettingsPropertyChanges()
        {
            // Subscribe to properties that affect Quick Settings tile states
            if (legionPerformanceMode != null)
                legionPerformanceMode.PropertyChanged += QuickSettingsProperty_Changed;
            if (tdp != null)
                tdp.PropertyChanged += QuickSettingsProperty_Changed;
            if (osd != null)
                osd.PropertyChanged += QuickSettingsProperty_Changed;
            if (perGameProfile != null)
                perGameProfile.PropertyChanged += QuickSettingsProperty_Changed;
            if (runningGame != null)
                runningGame.PropertyChanged += QuickSettingsProperty_Changed;
            if (fpsLimit != null)
                fpsLimit.PropertyChanged += QuickSettingsProperty_Changed;
            if (osPowerMode != null)
                osPowerMode.PropertyChanged += OSPowerMode_PropertyChanged;
            if (resolution != null)
            {
                resolution.PropertyChanged += QuickSettingsProperty_Changed;
                resolution.PropertyChanged += Resolution_PropertyChanged_OSD;
            }
            if (hdrEnabled != null)
                hdrEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (hdrSupported != null)
                hdrSupported.PropertyChanged += QuickSettingsProperty_Changed;
            if (displayOrientation != null)
                displayOrientation.PropertyChanged += QuickSettingsProperty_Changed;
            if (losslessScalingEnabled != null)
                losslessScalingEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (amdFluidMotionFrameEnabled != null)
                amdFluidMotionFrameEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (amdRadeonSuperResolutionEnabled != null)
                amdRadeonSuperResolutionEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (amdRadeonAntiLagEnabled != null)
                amdRadeonAntiLagEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (amdRadeonChillEnabled != null)
                amdRadeonChillEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (amdRadeonBoostEnabled != null)
                amdRadeonBoostEnabled.PropertyChanged += QuickSettingsProperty_Changed;
            if (autoTDPEnabled != null)
                autoTDPEnabled.PropertyChanged += QuickSettingsProperty_Changed;

            // Controller battery properties - update tile when battery status changes
            if (controllerBatteryLeft != null)
            {
                controllerBatteryLeft.PropertyChanged += QuickSettingsProperty_Changed;
                controllerBatteryLeft.PropertyChanged += LegionControllerBattery_PropertyChanged;
            }
            if (controllerBatteryRight != null)
            {
                controllerBatteryRight.PropertyChanged += QuickSettingsProperty_Changed;
                controllerBatteryRight.PropertyChanged += LegionControllerBattery_PropertyChanged;
            }
            if (controllerChargingLeft != null)
            {
                controllerChargingLeft.PropertyChanged += QuickSettingsProperty_Changed;
                controllerChargingLeft.PropertyChanged += LegionControllerBattery_PropertyChanged;
            }
            if (controllerChargingRight != null)
            {
                controllerChargingRight.PropertyChanged += QuickSettingsProperty_Changed;
                controllerChargingRight.PropertyChanged += LegionControllerBattery_PropertyChanged;
            }
            if (controllerConnectedLeft != null)
            {
                controllerConnectedLeft.PropertyChanged += LegionControllerBattery_PropertyChanged;
            }
            if (controllerConnectedRight != null)
            {
                controllerConnectedRight.PropertyChanged += LegionControllerBattery_PropertyChanged;
            }
            if (controllerVidPid != null)
            {
                controllerVidPid.PropertyChanged += LegionControllerVidPid_PropertyChanged;
            }

            // Subscribe to CPU core config changes
            if (cpuCoreConfig != null)
                cpuCoreConfig.PropertyChanged += CPUCoreConfig_PropertyChanged;

            Logger.Info("Subscribed to Quick Settings property changes");
        }

        private void CPUCoreConfig_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (cpuCoreConfig != null && !string.IsNullOrEmpty(cpuCoreConfig.Value))
                {
                    // Parse "pCores,eCores,isHybrid" format
                    var parts = cpuCoreConfig.Value.Split(',');
                    if (parts.Length >= 3 &&
                        int.TryParse(parts[0], out int pCores) &&
                        int.TryParse(parts[1], out int eCores) &&
                        bool.TryParse(parts[2], out bool isHybrid))
                    {
                        Logger.Info($"Received CPU core config from helper: {pCores}P + {eCores}E cores, hybrid={isHybrid}");
                        SetupCPUCoreConfigUI(pCores, eCores);
                    }
                }
            });
        }

        private void QuickSettingsProperty_Changed(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateQuickSettingsTileStates();
            });
        }

        private void LegionControllerBattery_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateLegionControllerBatteryDisplay();
                // Also refresh VID:PID in case it was missed during initial sync
                UpdateLegionControllerVidPidDisplay();
            });
        }

        private void UpdateLegionControllerBatteryDisplay()
        {
            try
            {
                // Update left controller battery
                int leftBattery = controllerBatteryLeft?.Value ?? -1;
                bool leftCharging = controllerChargingLeft?.Value ?? false;
                bool leftConnected = controllerConnectedLeft?.Value ?? false;

                if (leftBattery >= 0)
                {
                    LeftControllerBatteryText.Text = $"{leftBattery}%";
                    LeftControllerBatteryText.Foreground = GetBatteryBrush(leftBattery);
                }
                else
                {
                    LeftControllerBatteryText.Text = "N/A";
                    LeftControllerBatteryText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x88, 0x88, 0x88));
                }
                LeftControllerChargingIcon.Visibility = leftCharging ? Visibility.Visible : Visibility.Collapsed;
                LeftControllerConnectionText.Text = leftConnected ? "Attached" : "Detached";

                // Update right controller battery
                int rightBattery = controllerBatteryRight?.Value ?? -1;
                bool rightCharging = controllerChargingRight?.Value ?? false;
                bool rightConnected = controllerConnectedRight?.Value ?? false;

                if (rightBattery >= 0)
                {
                    RightControllerBatteryText.Text = $"{rightBattery}%";
                    RightControllerBatteryText.Foreground = GetBatteryBrush(rightBattery);
                }
                else
                {
                    RightControllerBatteryText.Text = "N/A";
                    RightControllerBatteryText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x88, 0x88, 0x88));
                }
                RightControllerChargingIcon.Visibility = rightCharging ? Visibility.Visible : Visibility.Collapsed;
                RightControllerConnectionText.Text = rightConnected ? "Attached" : "Detached";
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating Legion controller battery display: {ex.Message}");
            }
        }

        private Windows.UI.Xaml.Media.SolidColorBrush GetBatteryBrush(int percentage)
        {
            if (percentage <= 20)
                return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF4, 0x43, 0x36)); // Red
            else if (percentage <= 40)
                return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x98, 0x00)); // Orange
            else
                return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4C, 0xAF, 0x50)); // Green
        }

        private void LegionControllerVidPid_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateLegionControllerVidPidDisplay();
            });
        }

        private void UpdateLegionControllerVidPidDisplay()
        {
            try
            {
                string vidPid = controllerVidPid?.Value ?? "";
                Logger.Info($"UpdateLegionControllerVidPidDisplay: vidPid='{vidPid}'");
                if (!string.IsNullOrEmpty(vidPid))
                {
                    LegionControllerPidVidText.Text = $"VID:PID {vidPid}";
                }
                else
                {
                    LegionControllerPidVidText.Text = "VID:PID --";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating Legion controller VID:PID display: {ex.Message}");
            }
        }

        private void Resolution_PropertyChanged_OSD(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // When resolution changes, reload OSD config for the new resolution and send to helper
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Logger.Info($"Resolution changed to {resolution?.Value}, reloading OSD config");
                LoadOSDConfigFromStorage();
                SendOSDConfigToHelper();
            });
        }

        private void GamingWidget_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCompactMode(e.NewSize.Width);
        }

        private void PowerSourceToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateGameProfileCardVisibility();
        }

        private void PerGameProfileToggle_Changed(object sender, RoutedEventArgs e)
        {
            // Skip when toggle is being updated by helper pipe sync (not user interaction).
            // This prevents creating profiles for the wrong game when currentGameName
            // hasn't been updated yet (RunningGame pipe message may still be queued).
            // The helper already manages profile switching; OnGameTextChanged will update the UI.
            if (perGameProfile?.IsUpdatingUI == true)
            {
                Logger.Info($"Skipping PerGameProfileToggle_Changed - toggle set by helper sync (currentGameName='{currentGameName}')");
                SyncPowerSourceProfileToggleForCurrentContext();
                UpdateGameProfileCardVisibility();
                UpdateProfileDisplay();
                return;
            }

            // Protect entire toggle change sequence from auto-saves
            isSwitchingProfile = true;

            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (PerGameProfileToggle.IsOn)
                {
                    // Per-game profiles enabled - only proceed if we have a valid game
                    if (HasValidGame(currentGameName))
                    {
                        // Clear the disabled preference since user is enabling it
                        string disabledKey = $"PerGameProfileDisabled_{currentGameName}";
                        if (settings.Values.ContainsKey(disabledKey))
                        {
                            settings.Values.Remove(disabledKey);
                            Logger.Info($"Cleared per-game profile disabled preference for {currentGameName}");
                        }
                        LoadOrCreateGameProfiles();

                        // Show notification for per-game profile
                        _ = ShowProfileNotificationAsync(currentGameName, isPerGameProfile: true);
                    }
                    else
                    {
                        // No valid game, turn toggle back off
                        Logger.Warn($"Cannot enable per-game profile without a valid game (currentGameName='{currentGameName}'), forcing toggle OFF");
                        PerGameProfileToggle.IsOn = false;
                        return;
                    }
                }
                else
                {
                    // Toggle is being turned OFF - check if we should reject this
                    // (Prevents helper messages from disabling auto-enabled toggle)
                    // But ALLOW internal disables (when game closes) and user-initiated toggles
                    if (HasValidGame(currentGameName) && isApplyingHelperUpdate && !isInternalToggleDisable && !isUserInitiatedProfileToggle)
                    {
                        bool hasExistingProfile = HasAnyGameProfile(currentGameName);

                        if (hasExistingProfile)
                        {
                            Logger.Info($"Ignoring helper request to disable toggle - game '{currentGameName}' has saved profile");
                            PerGameProfileToggle.IsOn = true; // Keep it on
                            return;
                        }
                    }

                    // Save user's preference to disable per-game profile for this game
                    // (only for valid games and user-initiated toggles, not internal/game-close disables)
                    if (HasValidGame(currentGameName) && !isInternalToggleDisable)
                    {
                        string disabledKey = $"PerGameProfileDisabled_{currentGameName}";
                        settings.Values[disabledKey] = true;
                        Logger.Info($"Saved per-game profile disabled preference for {currentGameName}");
                    }
                }

                SyncPowerSourceProfileToggleForCurrentContext();
                UpdateActiveProfileIndicator();
            }
            finally
            {
                isSwitchingProfile = false;
            }
        }

        private void OnGameTextChanged(DependencyObject sender, DependencyProperty dp)
        {
            string rawGameName = RunningGameText.Text;
            string sanitizedName = SanitizeGameName(rawGameName);

            // Validate the game name - if invalid, use empty string instead
            string newGameName = HasValidGame(sanitizedName) ? sanitizedName : "";

            if (newGameName != currentGameName)
            {
                Logger.Info($"Game changed from '{currentGameName}' to '{newGameName}' (raw: '{rawGameName}')");

                // IMPORTANT: Disable toggle BEFORE changing currentGameName
                // This prevents race condition where profile switching happens with invalid state
                if (!HasValidGame(newGameName) && PerGameProfileToggle?.IsOn == true)
                {
                    Logger.Info($"No valid game detected (was: '{rawGameName}'), disabling per-game toggle BEFORE updating game name");
                    isInternalToggleDisable = true; // Flag this as internal disable
                    PerGameProfileToggle.IsOn = false;  // This triggers UpdateActiveProfileIndicator which switches to Global
                    isInternalToggleDisable = false;

                    // Show notification for global profile revert
                    _ = ShowProfileNotificationAsync("", isPerGameProfile: false);
                }

                // When game closes (going from valid game to no game), refresh display settings
                // This ensures the resolution tile updates after a game that changed resolution exits
                if (HasValidGame(currentGameName) && !HasValidGame(newGameName))
                {
                    _ = RequestDisplaySettingsRefreshAsync();
                }

                // Now safe to update currentGameName
                currentGameName = newGameName;

                // Check if we have a valid game
                if (HasValidGame(currentGameName))
                {
                    // Valid game detected
                    var settings = ApplicationData.Current.LocalSettings;
                    bool hasExistingProfile = HasAnyGameProfile(currentGameName);

                    // Check if user explicitly disabled per-game profile for this game
                    string disabledKey = $"PerGameProfileDisabled_{currentGameName}";
                    bool userDisabledProfile = settings.Values.ContainsKey(disabledKey) && (bool)settings.Values[disabledKey];

                    // Auto-enable per-game toggle ONLY if profile already exists for this game
                    // Do NOT carry over toggle state from previous game - that causes unwanted profile creation
                    // Respect user's preference if they explicitly disabled it
                    if (hasExistingProfile && !userDisabledProfile)
                    {
                        if (!PerGameProfileToggle.IsOn)
                        {
                            Logger.Info($"Auto-enabling per-game profile for {currentGameName} (profile exists)");
                            PerGameProfileToggle.IsOn = true;  // This will trigger PerGameProfileToggle_Changed
                        }
                        else
                        {
                            // Already on, need to switch to new game's profile
                            // Protect this sequence from auto-saves
                            isSwitchingProfile = true;
                            try
                            {
                                LoadOrCreateGameProfiles();
                                UpdateActiveProfileIndicator();  // Critical: switch to the new game's profile!

                                // Show notification for per-game profile
                                _ = ShowProfileNotificationAsync(currentGameName, isPerGameProfile: true);
                            }
                            finally
                            {
                                isSwitchingProfile = false;
                            }
                        }
                    }
                    else if (PerGameProfileToggle?.IsOn == true)
                    {
                        // New game doesn't have a profile - turn off toggle to prevent auto-creation
                        Logger.Info($"Disabling per-game toggle for {currentGameName} (no existing profile)");
                        isInternalToggleDisable = true;
                        PerGameProfileToggle.IsOn = false;
                        isInternalToggleDisable = false;
                    }
                }

                SyncPowerSourceProfileToggleForCurrentContext();

                // Update game profile card visibility and display
                UpdateGameProfileCardVisibility();
                UpdateProfileDisplay();

                // Update Lossless Scaling Create Profile button state
                if (LosslessScalingCreateProfileButton != null)
                {
                    bool isInstalled = losslessScalingInstalled?.Value ?? false;
                    LosslessScalingCreateProfileButton.IsEnabled = isInstalled && HasValidGame(currentGameName);
                }

                // Update controller profile toggle and game name display
                UpdateControllerProfileForGameChange(newGameName);
            }
        }

        /// <summary>
        /// Shows a Game Bar notification when a profile is applied.
        /// </summary>
        private async Task ShowProfileNotificationAsync(string gameName, bool isPerGameProfile)
        {
            if (notificationManager == null || widget == null)
                return;

            try
            {
                // Check if notifications are enabled
                if (notificationManager.Setting == XboxGameBarWidgetNotificationSetting.DisabledByUser)
                {
                    Logger.Debug("Profile notifications disabled by user");
                    return;
                }

                string title = isPerGameProfile ? "Profile Applied" : "Global Profile";
                string content = isPerGameProfile
                    ? $"Loaded settings for {gameName}"
                    : "Reverted to global settings";

                var builder = new XboxGameBarWidgetNotificationBuilder(title)
                    .Content(content);

                var notification = builder.BuildNotification();
                var result = await notificationManager.TryShowAsync(notification);

                Logger.Info($"Profile notification shown: {title} - {content} (result: {result})");
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to show profile notification: {ex.Message}");
            }
        }

        private void UpdateControllerProfileForGameChange(string newGameName)
        {
            // Update the game name display in controller profile card
            if (LegionControllerProfileGameText != null)
            {
                LegionControllerProfileGameText.Text = HasValidGame(newGameName) ? newGameName : "No game detected";
            }

            // Enable/disable the toggle based on game detection
            if (LegionControllerProfileToggle != null)
            {
                LegionControllerProfileToggle.IsEnabled = HasValidGame(newGameName);

                if (!HasValidGame(newGameName))
                {
                    // No valid game, turn off and disable toggle, switch to global profile
                    if (LegionControllerProfileToggle.IsOn)
                    {
                        isSwitchingControllerProfile = true;
                        try
                        {
                            LegionControllerProfileToggle.IsOn = false;
                            LoadControllerProfileFromStorage("Global", globalControllerProfile);
                            ApplyControllerProfile(globalControllerProfile);
                            Logger.Info("Game closed - switched to global controller profile");
                        }
                        finally
                        {
                            isSwitchingControllerProfile = false;
                        }
                    }
                }
                else
                {
                    // Valid game detected - check for existing controller profile
                    var settings = ApplicationData.Current.LocalSettings;
                    bool hasExistingControllerProfile = settings.Containers.ContainsKey($"ControllerProfile_Game_{newGameName}");

                    // Check if user explicitly disabled controller profile for this game
                    string disabledKey = $"ControllerProfileDisabled_{newGameName}";
                    bool userDisabledProfile = settings.Values.ContainsKey(disabledKey) && (bool)settings.Values[disabledKey];

                    // Auto-enable if profile exists and user hasn't disabled it
                    if (hasExistingControllerProfile && !userDisabledProfile)
                    {
                        if (!LegionControllerProfileToggle.IsOn)
                        {
                            // Setting toggle to true triggers LegionControllerProfileToggle_Toggled
                            // which handles loading and applying the profile
                            LegionControllerProfileToggle.IsOn = true;
                            Logger.Info($"Auto-enabled controller profile for {newGameName}");
                        }
                        else
                        {
                            // Toggle already on, need to switch to new game's profile
                            isSwitchingControllerProfile = true;
                            try
                            {
                                LoadControllerProfileFromStorage($"Game_{newGameName}", gameControllerProfile);
                                ApplyControllerProfile(gameControllerProfile);
                                Logger.Info($"Switched to controller profile for {newGameName}");
                            }
                            finally
                            {
                                isSwitchingControllerProfile = false;
                            }
                        }
                    }
                    else if (LegionControllerProfileToggle.IsOn)
                    {
                        // Toggle was on for previous game - only keep it on if new game has existing profile
                        if (!hasExistingControllerProfile)
                        {
                            // No profile for new game - turn off toggle instead of auto-creating
                            // Setting toggle to false triggers LegionControllerProfileToggle_Toggled
                            // which handles switching to global profile
                            LegionControllerProfileToggle.IsOn = false;
                            Logger.Info($"Disabled controller profile toggle for {newGameName} (no existing profile)");
                        }
                        else
                        {
                            // Profile exists, switch to it
                            isSwitchingControllerProfile = true;
                            try
                            {
                                LoadControllerProfileFromStorage($"Game_{newGameName}", gameControllerProfile);
                                ApplyControllerProfile(gameControllerProfile);
                                Logger.Info($"Switched to controller profile for {newGameName}");
                            }
                            finally
                            {
                                isSwitchingControllerProfile = false;
                            }
                        }
                    }
                }
            }
        }

        private void LoadOrCreateGameProfiles()
        {
            if (!HasValidGame(currentGameName))
                return;

            var settings = ApplicationData.Current.LocalSettings;
            bool splitEnabled = GetPerGamePowerSourceProfileEnabled(currentGameName);
            bool hasSingle = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}");
            bool hasAC = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_AC");
            bool hasDC = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_DC");

            if (splitEnabled)
            {
                // Ensure AC/DC game profiles exist. If only a single profile exists, seed both from it.
                PerformanceProfile seedProfile = null;
                if (hasSingle)
                {
                    seedProfile = new PerformanceProfile();
                    LoadProfileFromStorage($"Game_{currentGameName}", seedProfile);
                }

                if (!hasAC)
                {
                    gameACProfile = (seedProfile ?? acProfile).Clone();
                    SaveProfileToStorage($"Game_{currentGameName}_AC", gameACProfile);
                    Logger.Info($"Initialized game AC profile for {currentGameName} (seed={(seedProfile != null ? "single profile" : "global AC")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_AC", gameACProfile);
                }

                if (!hasDC)
                {
                    gameDCProfile = (seedProfile ?? dcProfile).Clone();
                    SaveProfileToStorage($"Game_{currentGameName}_DC", gameDCProfile);
                    Logger.Info($"Initialized game DC profile for {currentGameName} (seed={(seedProfile != null ? "single profile" : "global DC")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_DC", gameDCProfile);
                }

                Logger.Info($"Loaded game AC/DC profiles for {currentGameName}");
            }
            else
            {
                // Ensure single game profile exists. If only AC/DC exists, seed from active power source profile.
                if (!hasSingle)
                {
                    PerformanceProfile seedProfile = null;
                    if (hasAC || hasDC)
                    {
                        var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                        bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

                        string sourceProfileName;
                        if (isOnAC && hasAC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_AC";
                        }
                        else if (!isOnAC && hasDC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_DC";
                        }
                        else if (hasAC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_AC";
                        }
                        else
                        {
                            sourceProfileName = $"Game_{currentGameName}_DC";
                        }

                        seedProfile = new PerformanceProfile();
                        LoadProfileFromStorage(sourceProfileName, seedProfile);
                        Logger.Info($"Seeding single game profile for {currentGameName} from {sourceProfileName}");
                    }

                    if (seedProfile == null && GetGlobalPowerSourceProfileEnabled())
                    {
                        var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                        bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;
                        seedProfile = (isOnAC ? acProfile : dcProfile).Clone();
                        Logger.Info($"Seeding single game profile for {currentGameName} from global {(isOnAC ? "AC" : "DC")} profile");
                    }

                    gameProfile = (seedProfile ?? globalProfile).Clone();
                    SaveProfileToStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Initialized game profile for {currentGameName} (seed={(seedProfile != null ? "active profile" : "global profile")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Loaded existing game profile for {currentGameName}");
                }
            }
        }

        private void SubscribeToSettingsChanges()
        {
            // Performance settings
            TDPSlider.ValueChanged += SettingChanged;
            CPUBoostToggle.Toggled += SettingChanged;
            CPUEPPSlider.ValueChanged += SettingChanged;
            MinCPUStateComboBox.SelectionChanged += SettingChanged;
            MaxCPUStateComboBox.SelectionChanged += SettingChanged;
            FPSLimitToggle.Toggled += FPSLimitToggle_Toggled;
            FPSLimitSlider.ValueChanged += FPSLimitSlider_ValueChanged;

            // Graphics settings (HDR and Resolution for profile feature)
            HDRToggle.Toggled += SettingChanged;
            ResolutionComboBox.SelectionChanged += SettingChanged;

            // AMD settings
            AMDFluidMotionFrameToggle.Toggled += SettingChanged;
            AMDRadeonSuperResolutionToggle.Toggled += AMDRadeonSuperResolutionToggle_Toggled;
            AMDRadeonSuperResolutionSharpnessSlider.ValueChanged += SettingChanged;
            AMDImageSharpeningToggle.Toggled += AMDImageSharpeningToggle_Toggled;
            AMDImageSharpeningSlider.ValueChanged += SettingChanged;
            AMDRadeonAntiLagToggle.Toggled += AMDRadeonAntiLagToggle_Toggled;
            AMDRadeonBoostToggle.Toggled += AMDRadeonBoostToggle_Toggled;
            AMDRadeonBoostResolutionSlider.ValueChanged += SettingChanged;
            AMDRadeonChillToggle.Toggled += AMDRadeonChillToggle_Toggled;
            AMDRadeonChillMinFPSSlider.ValueChanged += SettingChanged;
            AMDRadeonChillMaxFPSSlider.ValueChanged += SettingChanged;

            // Legion controller button mapping settings
            InitializeButtonMappingEvents("Y1");
            InitializeButtonMappingEvents("Y2");
            InitializeButtonMappingEvents("Y3");
            InitializeButtonMappingEvents("M1");
            InitializeButtonMappingEvents("M2");
            InitializeButtonMappingEvents("M3");
            InitializeButtonMappingEvents("Desktop");
            InitializeButtonMappingEvents("Page");

            if (LegionNintendoLayoutToggle != null)
                LegionNintendoLayoutToggle.Toggled += LegionNintendoLayout_Toggled;
            if (LegionDesktopControlsToggle != null)
                LegionDesktopControlsToggle.Toggled += LegionDesktopControls_Toggled;
            if (LegionVibrationComboBox != null)
                LegionVibrationComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionVibrationModeComboBox != null)
                LegionVibrationModeComboBox.SelectionChanged += ControllerSettingChanged;

            // Gyro settings (per-game profile)
            if (LegionGyroTargetComboBox != null)
                LegionGyroTargetComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroSensitivityXSlider != null)
                LegionGyroSensitivityXSlider.ValueChanged += ControllerSettingChanged;
            if (LegionGyroSensitivityYSlider != null)
                LegionGyroSensitivityYSlider.ValueChanged += ControllerSettingChanged;
            if (LegionGyroInvertXToggle != null)
                LegionGyroInvertXToggle.Toggled += ControllerSettingChanged;
            if (LegionGyroInvertYToggle != null)
                LegionGyroInvertYToggle.Toggled += ControllerSettingChanged;
            if (LegionGyroMappingTypeComboBox != null)
                LegionGyroMappingTypeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroActivationModeComboBox != null)
                LegionGyroActivationModeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionGyroActivationButtonComboBox != null)
                LegionGyroActivationButtonComboBox.SelectionChanged += ControllerSettingChanged;

            // Advanced gyro settings (per-game profile)
            if (LegionGyroDeadzoneSlider != null)
                LegionGyroDeadzoneSlider.ValueChanged += ControllerSettingChanged;

            // Stick deadzones (per-game profile)
            if (LegionLeftStickDeadzoneSlider != null)
                LegionLeftStickDeadzoneSlider.ValueChanged += ControllerSettingChanged;
            if (LegionRightStickDeadzoneSlider != null)
                LegionRightStickDeadzoneSlider.ValueChanged += ControllerSettingChanged;

            // Trigger travel (per-game profile)
            if (LegionLeftTriggerStartSlider != null)
                LegionLeftTriggerStartSlider.ValueChanged += ControllerSettingChanged;
            if (LegionLeftTriggerEndSlider != null)
                LegionLeftTriggerEndSlider.ValueChanged += ControllerSettingChanged;
            if (LegionRightTriggerStartSlider != null)
                LegionRightTriggerStartSlider.ValueChanged += ControllerSettingChanged;
            if (LegionRightTriggerEndSlider != null)
                LegionRightTriggerEndSlider.ValueChanged += ControllerSettingChanged;
            if (LegionHairTriggersToggle != null)
                LegionHairTriggersToggle.Toggled += LegionHairTriggers_Toggled;

            // Joystick as mouse (per-game profile)
            if (LegionJoystickAsMouseComboBox != null)
                LegionJoystickAsMouseComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionJoystickMouseSensSlider != null)
                LegionJoystickMouseSensSlider.ValueChanged += ControllerSettingChanged;

            // Lighting settings (per-game profile)
            if (LegionPowerLightToggle != null)
                LegionPowerLightToggle.Toggled += ControllerSettingChanged;
            if (LegionLightModeComboBox != null)
                LegionLightModeComboBox.SelectionChanged += ControllerSettingChanged;
            if (LegionColorPicker != null)
                LegionColorPicker.ColorChanged += ControllerSettingChanged;
            if (LegionBrightnessSlider != null)
                LegionBrightnessSlider.ValueChanged += ControllerSettingChanged;
            if (LegionSpeedSlider != null)
                LegionSpeedSlider.ValueChanged += ControllerSettingChanged;

            // Gamepad button remapping (per-game profile)
            if (LegionGamepadButtonSelectorComboBox != null)
                LegionGamepadButtonSelectorComboBox.SelectionChanged += LegionGamepadButtonSelector_SelectionChanged;
            if (LegionGamepadTypeComboBox != null)
                LegionGamepadTypeComboBox.SelectionChanged += LegionGamepadMapping_Changed;
            if (LegionGamepadActionComboBox != null)
                LegionGamepadActionComboBox.SelectionChanged += LegionGamepadMapping_Changed;
            if (LegionGamepadMouseComboBox != null)
                LegionGamepadMouseComboBox.SelectionChanged += LegionGamepadMapping_Changed;
            if (LegionGamepadKeyComboBox != null)
                LegionGamepadKeyComboBox.SelectionChanged += LegionGamepadKey_SelectionChanged;
            if (LegionGamepadResetAllButton != null)
                LegionGamepadResetAllButton.Click += LegionGamepadResetAll_Click;

            if (ControllerEmulationImprovedInputToggle != null)
                ControllerEmulationImprovedInputToggle.Toggled += ControllerEmulationImprovedInputToggle_Toggled;

            foreach (string buttonName in LegionRemapButtonNames)
            {
                UpdateButtonGamepadComboControls(buttonName);
            }
        }

        private void SettingChanged(object sender, object e)
        {
            // Update Sticky TDP target if TDP slider changed and Sticky TDP is enabled
            // But ONLY if the change is from the user, not from helper sync/updates
            if (sender == TDPSlider && StickyTDPToggle?.IsOn == true && !isApplyingHelperUpdate)
            {
                targetTDPLimit = TDPSlider.Value;
                Logger.Info($"Sticky TDP target updated to: {targetTDPLimit}W (user change)");
            }

            // Don't save during profile loading, switching, initial sync, when helper is updating values,
            // when any property is syncing from helper pipe, or when Default Game Profile is active
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync
                || WidgetSliderProperty.HelperSyncCount > 0 || defaultGameProfileEnabled?.Value == true)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate}, initialSync={isInitialSync}, defaultGameProfile={defaultGameProfileEnabled?.Value})");
                return;
            }

            // Auto-save to current profile
            SaveCurrentSettingsToProfile(currentProfileName);
        }

        private void PerformanceOverlayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PerformanceOverlayComboBox != null && PerformanceOverlaySlider != null)
            {
                // Sync the hidden slider value with the selected combobox item
                int index = PerformanceOverlayComboBox.SelectedIndex;
                if (index >= 0)
                {
                    if (osdProvider == 1) // AMD
                    {
                        // For AMD: index 0 = Off, index 1-3 maps to AMD levels
                        if (index == 0 && amdOverlayLevel > 0)
                        {
                            // Turn off AMD overlay
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 0;
                            SaveAMDOverlayLevel();
                            Logger.Info("AMD Overlay toggled OFF via ComboBox");
                        }
                        else if (index > 0 && amdOverlayLevel == 0)
                        {
                            // Turn on AMD overlay (starts at level 1)
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 1;
                            SaveAMDOverlayLevel();
                            Logger.Info("AMD Overlay toggled ON via ComboBox");
                        }
                        // Note: We can't set specific AMD levels directly, only cycle
                        UpdateQuickSettingsTileStates();
                    }
                    else // RTSS
                    {
                        PerformanceOverlaySlider.Value = index;
                    }
                    // Save the setting (but not during initial load)
                    if (!isLoadingPerformanceOverlaySetting)
                    {
                        SavePerformanceOverlaySetting();

                        // Also update current profile's OverlayLevel if SaveOverlayLevel is enabled
                        // This ensures the profile stays in sync with the user's selection
                        if (SaveOverlayLevel && !string.IsNullOrEmpty(currentProfileName))
                        {
                            var profile = GetProfile(currentProfileName);
                            if (profile != null)
                            {
                                profile.OverlayLevel = index;
                                SaveProfileToStorage(currentProfileName, profile);
                                Logger.Debug($"Updated profile '{currentProfileName}' OverlayLevel to {index}");
                            }
                        }
                    }
                }
            }
        }

        private void LoadPerformanceOverlaySetting()
        {
            try
            {
                if (PerformanceOverlayComboBox == null) return;
                isLoadingPerformanceOverlaySetting = true;
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("PerformanceOverlayLevel", out object val) && val is int level)
                {
                    if (level >= 0 && level < PerformanceOverlayComboBox.Items.Count)
                    {
                        PerformanceOverlayComboBox.SelectedIndex = level;
                        // Also set the osd property value directly to avoid debounce delay
                        // This ensures Quick Settings and helper have the correct value immediately
                        if (osd != null)
                        {
                            osd.SetValue(level);
                        }
                        Logger.Debug($"Loaded PerformanceOverlayLevel: {level}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading PerformanceOverlay setting: {ex.Message}");
            }
            finally
            {
                isLoadingPerformanceOverlaySetting = false;
            }
        }

        private void SavePerformanceOverlaySetting()
        {
            try
            {
                if (PerformanceOverlayComboBox == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                int level = PerformanceOverlayComboBox.SelectedIndex;
                settings.Values["PerformanceOverlayLevel"] = level;
                Logger.Debug($"Saved PerformanceOverlayLevel: {level}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving PerformanceOverlay setting: {ex.Message}");
            }
        }

        private void SaveAMDOverlayLevel()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["AMD_OverlayLevel"] = amdOverlayLevel;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving AMD overlay level: {ex.Message}");
            }
        }

        private void PerformanceOverlaySlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (PerformanceOverlaySlider != null && PerformanceOverlayComboBox != null)
            {
                // Sync the ComboBox selection when slider value changes
                // (e.g., from property loading or helper updates)
                int newIndex = (int)Math.Round(e.NewValue);

                if (PerformanceOverlayComboBox.SelectedIndex != newIndex)
                {
                    PerformanceOverlayComboBox.SelectedIndex = newIndex;
                }
            }
        }

        private void PowerSourceProfileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (PowerSourceProfileToggle == null)
            {
                return;
            }

            if (isUpdatingPowerSourceProfileToggle)
            {
                UpdateGlobalProfileDisplayMode();
                UpdatePowerSourceProfileScopeText();
                return;
            }

            bool enabled = PowerSourceProfileToggle.IsOn;
            bool perGameContext = PerGameProfileToggle?.IsOn == true && HasValidGame(currentGameName);

            if (perGameContext)
            {
                SavePerGamePowerSourceProfileSetting(currentGameName, enabled);
                Logger.Info($"PowerSourceProfileToggle toggled for game '{currentGameName}' to: {enabled}");
                LoadOrCreateGameProfiles();
            }
            else
            {
                Logger.Info($"PowerSourceProfileToggle toggled globally to: {enabled}");
                SavePowerSourceProfileSetting(enabled);
            }

            UpdateGlobalProfileDisplayMode();
            UpdateGameProfileCardVisibility();
            UpdateActiveProfileIndicator();
            UpdateProfileDisplay();
        }

        private void LoadPowerSourceProfileSetting()
        {
            try
            {
                if (PowerSourceProfileToggle == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                bool enabled = false;
                if (settings.Values.TryGetValue(GlobalPowerSourceProfileSettingKey, out object val) && val is bool saved)
                {
                    enabled = saved;
                }

                isUpdatingPowerSourceProfileToggle = true;
                try
                {
                    PowerSourceProfileToggle.IsOn = enabled;
                }
                finally
                {
                    isUpdatingPowerSourceProfileToggle = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading PowerSourceProfile setting: {ex.Message}");
            }
        }

        private void SavePowerSourceProfileSetting(bool enabled)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[GlobalPowerSourceProfileSettingKey] = enabled;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving PowerSourceProfile setting: {ex.Message}");
            }
        }

        private void StickyTDPToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Skip during initialization - don't capture TDP or start timer until profile loads
            if (isLoadingStickyTDPSettings) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;

            Logger.Info($"StickyTDPToggle toggled to: {StickyTDPToggle.IsOn}");

            // Save setting to LocalSettings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["StickyTDPEnabled"] = StickyTDPToggle.IsOn;

            if (StickyTDPToggle.IsOn)
            {
                // Store current TDP limit as target
                targetTDPLimit = TDPSlider.Value;
                Logger.Info($"Sticky TDP enabled - monitoring TDP limit: {targetTDPLimit}W");

                // Start the monitoring timer
                StartStickyTDPTimer();
            }
            else
            {
                // Stop the monitoring timer
                StopStickyTDPTimer();
                Logger.Info("Sticky TDP disabled");
            }

            // Trigger profile save if SaveStickyTDP is enabled
            SettingChanged(sender, e);
        }

        private void StickyTDPIntervalSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickyTDPIntervalSlider == null) return;
            if (isLoadingStickyTDPSettings) return;

            stickyTDPCheckIntervalSeconds = (int)Math.Round(e.NewValue);
            Logger.Info($"Sticky TDP check interval changed to: {stickyTDPCheckIntervalSeconds}s");

            // Save setting to LocalSettings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["StickyTDPInterval"] = stickyTDPCheckIntervalSeconds;

            // Update the value display
            if (StickyTDPIntervalValue != null)
            {
                StickyTDPIntervalValue.Text = $"{stickyTDPCheckIntervalSeconds}s";
            }

            // Restart timer with new interval if it's running
            if (StickyTDPToggle?.IsOn == true)
            {
                StopStickyTDPTimer();
                StartStickyTDPTimer();
            }

            // Trigger profile save if SaveStickyTDP is enabled
            SettingChanged(sender, e);
        }

        private void StartStickyTDPTimer()
        {
            if (stickyTDPTimer == null)
            {
                stickyTDPTimer = new DispatcherTimer();
                stickyTDPTimer.Tick += StickyTDPTimer_Tick;
            }

            stickyTDPTimer.Interval = TimeSpan.FromSeconds(stickyTDPCheckIntervalSeconds);
            stickyTDPTimer.Start();
            Logger.Info($"Sticky TDP timer started with {stickyTDPCheckIntervalSeconds}s interval");
        }

        private void StopStickyTDPTimer()
        {
            if (stickyTDPTimer != null)
            {
                stickyTDPTimer.Stop();
                Logger.Info("Sticky TDP timer stopped");
            }
        }

        private async void StickyTDPTimer_Tick(object sender, object e)
        {
            try
            {
                // Skip Sticky TDP in non-Custom modes - preset modes manage TDP automatically
                if (legionGoDetected?.Value == true && legionPerformanceMode?.Value != 255)
                {
                    Logger.Debug($"Sticky TDP: Skipping - using {GetLegionModeShortName(legionPerformanceMode?.Value ?? 0)} preset mode");
                    return;
                }

                // Smart check: Only reapply if current hardware TDP differs from target
                // Parse STAPM limit from currentTdp (format: "STAPM:21W FAST:21W SLOW:21W")
                int currentStapmLimit = -1;
                if (currentTdp != null && !string.IsNullOrEmpty(currentTdp.Value))
                {
                    var parts = currentTdp.Value.Split(' ');
                    foreach (var part in parts)
                    {
                        if (part.StartsWith("STAPM:"))
                        {
                            var valueStr = part.Substring(6).Replace("W", "");
                            if (int.TryParse(valueStr, out currentStapmLimit))
                            {
                                break;
                            }
                        }
                    }
                }

                // Check if hardware TDP matches our target
                if (currentStapmLimit == (int)targetTDPLimit)
                {
                    Logger.Info($"Sticky TDP: Hardware STAPM limit ({currentStapmLimit}W) matches target ({targetTDPLimit}W), no action needed.");
                    return;
                }

                // Hardware TDP differs from target - need to reapply
                Logger.Info($"Sticky TDP: Hardware STAPM limit ({currentStapmLimit}W) differs from target ({targetTDPLimit}W), reapplying...");

                // Set flag to prevent slider UI flicker during reapply
                isStickyTDPReapplying = true;

                // To force the helper to actually apply the TDP (even if its internal value matches),
                // we need to change the value first, then set it to the target.
                // This triggers NotifyPropertyChanged -> Manager.SetTDP() in the helper.
                if (App.IsConnected)
                {
                    // Calculate a different value to force a change
                    int tempValue = (int)targetTDPLimit == 15 ? 16 : (int)targetTDPLimit - 1;

                    // First, set to temp value to force a change
                    var tempRequest = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.TDP },
                        { "Content", tempValue },
                        { "UpdatedTime", DateTimeOffset.Now.Ticks }
                    };
                    await App.SendMessageAsync(tempRequest);

                    // Small delay to ensure the temp value is processed
                    await Task.Delay(50);

                    // Then set to actual target value
                    var targetRequest = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.TDP },
                        { "Content", (int)targetTDPLimit },
                        { "UpdatedTime", DateTimeOffset.Now.Ticks }
                    };

                    var response = await App.SendMessageAsync(targetRequest);
                    if (response != null)
                    {
                        Logger.Info($"Sticky TDP: Successfully reapplied TDP {targetTDPLimit}W to hardware.");
                    }
                    else
                    {
                        Logger.Warn($"Sticky TDP: Got no response from helper when setting TDP.");
                    }

                    // Small delay to ensure helper messages are processed before clearing flag
                    await Task.Delay(100);
                }
                else
                {
                    Logger.Warn("Sticky TDP: No connection to helper app.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in Sticky TDP timer: {ex.Message}");
            }
            finally
            {
                // Clear flag to allow normal slider updates
                isStickyTDPReapplying = false;
            }
        }
        private async void PowerManager_PowerSourceChanged(object sender, object e)
        {
            if (isUnloading) return;

            // Small delay to allow system to update power status
            await System.Threading.Tasks.Task.Delay(100);

            if (isUnloading) return;

            // Update the active profile indicator when power source changes
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (isUnloading) return;

                var batteryStatus = PowerManager.BatteryStatus;
                var powerSupplyStatus = PowerManager.PowerSupplyStatus;

                Logger.Info($"Power source event - Battery: {batteryStatus}, PowerSupply: {powerSupplyStatus}");

                UpdateActiveProfileIndicator();

                // Auto-switch power plan based on power source
                if (powerPlanAutoSwitch)
                {
                    bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;
                    Guid planToApply = isOnAC ? acPowerPlanGuid : dcPowerPlanGuid;
                    if (planToApply != Guid.Empty)
                    {
                        ApplyPowerPlan(planToApply);
                        Logger.Info($"Auto-switched power plan to {(isOnAC ? "AC" : "DC")}: {planToApply}");
                    }
                }

                // Only reapply TDP after power source change if:
                // 1. On Legion Go in Custom mode (255) - system changes TDP, need to restore
                // 2. Power Source Profile toggle is enabled - user wants different profiles per power state
                // For Legion preset modes (Quiet=1, Balanced=2, Performance=3), let the system handle TDP
                // Skip TDP reapply when DGP is active - DGP controls TDP regardless of power source
                bool isLegionCustomMode = legionGoDetected?.Value == true && legionPerformanceMode?.Value == 255;
                bool powerSourceProfileEnabled = GetPowerSourceProfileEnabledForCurrentContext();
                bool dgpActive = defaultGameProfileEnabled?.Value == true;

                if ((isLegionCustomMode || powerSourceProfileEnabled) && !dgpActive)
                {
                    SchedulePowerSourceTdpReapply();
                }
                else if (dgpActive)
                {
                    Logger.Info("Power source change: Skipping TDP reapply - Default Game Profile is active");
                }
            });
        }

        /// <summary>
        /// Schedules a TDP reapply 5 seconds after power source changes.
        /// This ensures the TDP is properly applied after the system settles.
        /// </summary>
        private void SchedulePowerSourceTdpReapply()
        {
            try
            {
                // Cancel existing timer if any
                if (powerSourceTdpReapplyTimer != null)
                {
                    powerSourceTdpReapplyTimer.Stop();
                }

                // Create and start timer
                powerSourceTdpReapplyTimer = new DispatcherTimer();
                powerSourceTdpReapplyTimer.Interval = TimeSpan.FromSeconds(5);
                powerSourceTdpReapplyTimer.Tick += async (s, args) =>
                {
                    powerSourceTdpReapplyTimer.Stop();

                    // Skip TDP reapply if DGP is active - DGP controls TDP regardless of power source
                    if (defaultGameProfileEnabled?.Value == true)
                    {
                        Logger.Info("Power source change: Skipping TDP reapply - Default Game Profile is active");
                        return;
                    }

                    // Skip TDP reapply if not in Custom mode - preset modes manage TDP automatically
                    if (legionGoDetected?.Value == true && legionPerformanceMode?.Value != 255)
                    {
                        Logger.Info($"Power source change: Skipping TDP reapply - using {GetLegionModeShortName(legionPerformanceMode?.Value ?? 0)} preset mode");
                        return;
                    }

                    // Read TDP value NOW (at timer fire time), not when scheduled
                    // This ensures we use the new profile's TDP after profile switch completes
                    int currentTdpValue = (int)TDPSlider.Value;

                    // Reapply TDP - use the current Performance tab TDP value
                    if (tdp != null)
                    {
                        // Set guard flag to prevent saving TDP-1 to profile
                        isApplyingHelperUpdate = true;
                        try
                        {
                            // Force reapply by sending different value to helper first, then the real value
                            // This ensures the helper doesn't skip due to "equals current value"
                            tdp.SetValue(currentTdpValue - 1);
                            await System.Threading.Tasks.Task.Delay(100);
                            tdp.SetValue(currentTdpValue);
                            Logger.Info($"Power source change: Reapplied TDP {currentTdpValue}W after 5 seconds");
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }
                };
                powerSourceTdpReapplyTimer.Start();
                Logger.Info($"Power source change: Scheduled TDP reapply in 5 seconds");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scheduling power source TDP reapply: {ex.Message}");
            }
        }

        private void UpdateActiveProfileIndicator()
        {
            bool hasGame = HasValidGame(currentGameName);
            bool perGameEnabled = PerGameProfileToggle?.IsOn ?? false;

            // Check if we should use per-game profiles
            if (perGameEnabled && hasGame)
            {
                // Per-game profile is active
                if (GetPerGamePowerSourceProfileEnabled(currentGameName))
                {
                    // Check power status for AC/DC
                    // Only consider DC (battery) when power supply is NotPresent (actually unplugged)
                    // Inadequate means charger is connected but can't keep up - still treat as AC
                    var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                    bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

                    if (isOnAC)
                    {
                        ActiveProfileText.Text = $"{currentGameName} (AC)";
                    }
                    else
                    {
                        ActiveProfileText.Text = $"{currentGameName} (DC)";
                    }
                }
                else
                {
                    // Game profile without power source split
                    ActiveProfileText.Text = currentGameName;
                }
            }
            else
            {
                // Global profiles
                if (!GetGlobalPowerSourceProfileEnabled())
                {
                    // Power source profiles disabled, show global
                    ActiveProfileText.Text = "Global Settings";
                }
                else
                {
                    // Check power status
                    // Only consider DC (battery) when power supply is NotPresent (actually unplugged)
                    // Inadequate means charger is connected but can't keep up - still treat as AC
                    var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                    var remainingCharge = PowerManager.RemainingChargePercent;

                    Logger.Info($"Power status - PowerSupply: {powerSupplyStatus}, Charge: {remainingCharge}%");

                    bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

                    if (isOnAC)
                    {
                        ActiveProfileText.Text = "AC Profile (Plugged In)";
                    }
                    else
                    {
                        ActiveProfileText.Text = "DC Profile (Battery)";
                    }
                }
            }

            Logger.Info($"Active profile updated to: {ActiveProfileText.Text}");

            // Start scrolling animation if text is too long
            UpdateActiveProfileScrollAnimation();

            // Switch profile if needed
            SwitchProfile();
        }

        /// <summary>
        /// Previously handled scrolling animation for the active profile text.
        /// Now a no-op since we use TextTrimming instead.
        /// </summary>
        private void UpdateActiveProfileScrollAnimation()
        {
            // No longer needed - using TextTrimming instead of scrolling animation
        }

        private void SwitchProfile()
        {
            string targetProfile = GetTargetProfileName();

            if (targetProfile != currentProfileName)
            {
                Logger.Info($"Switching from '{currentProfileName}' to '{targetProfile}' profile");

                // Set flag to prevent auto-saves during transition
                isSwitchingProfile = true;

                try
                {
                    // Save current profile before switching, but SKIP for game-related transitions.
                    // 1. FROM a game profile (game close): helper already pushed global values to the
                    //    widget UI (AutoTDP=false, Mode=Quiet, etc.) BEFORE sending PerGameProfile=false.
                    //    Saving now would capture global values and corrupt the game profile.
                    // 2. TO a game profile (game open): helper sends game values (Mode=Custom, AutoTDP=true)
                    //    BEFORE the profile switch. Saving now would capture game values and corrupt Global.
                    // Individual toggle/slider handlers already save user changes immediately,
                    // so skipping here is safe — the profile is always up-to-date.
                    if (!currentProfileName.StartsWith("Game_") && !targetProfile.StartsWith("Game_"))
                    {
                        SaveCurrentSettingsToProfile(currentProfileName);
                    }

                    // Switch to new profile
                    currentProfileName = targetProfile;

                    // Load settings from new profile (explicit switch - apply HDR/Resolution)
                    LoadProfileSettings(currentProfileName, isExplicitSwitch: true);
                }
                finally
                {
                    // Always clear the flag
                    isSwitchingProfile = false;
                }
            }
        }

        private string GetTargetProfileName()
        {
            bool hasGame = HasValidGame(currentGameName);
            bool perGameEnabled = PerGameProfileToggle?.IsOn ?? false;

            // Only consider DC (battery) when power supply is NotPresent (actually unplugged)
            // Inadequate means charger is connected but can't keep up - still treat as AC
            var powerSupplyStatus = PowerManager.PowerSupplyStatus;
            bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

            // IMPORTANT: Never create profile names for invalid games
            // If per-game is enabled but no valid game, fall back to global profiles
            if (perGameEnabled && hasGame)
            {
                // Per-game profile - only if we have a VALID game name AND the profile
                // storage container already exists. This prevents switching to ghost profiles
                // for fuzzy-matched launcher names that were never explicitly created by the user
                // or LoadOrCreateGameProfiles(). Without this check, deferred events after
                // SwitchProfile can auto-save to a non-existent profile, creating it accidentally.
                var settings = ApplicationData.Current.LocalSettings;
                string candidateProfile;
                bool perGamePowerSourceSplit = GetPerGamePowerSourceProfileEnabled(currentGameName);
                if (perGamePowerSourceSplit)
                {
                    candidateProfile = isOnAC ? $"Game_{currentGameName}_AC" : $"Game_{currentGameName}_DC";
                }
                else
                {
                    candidateProfile = $"Game_{currentGameName}";
                }

                if (settings.Containers.ContainsKey($"Profile_{candidateProfile}"))
                {
                    Logger.Info($"Using per-game profile for: {currentGameName}");
                    return candidateProfile;
                }

                Logger.Warn($"Per-game toggle is ON but no saved profile exists for '{candidateProfile}', using global profile instead");
                // Fall through to global profile below
            }
            else if (perGameEnabled && !hasGame)
            {
                Logger.Warn($"Per-game toggle is ON but no valid game detected, using global profile instead");
            }

            // Global profiles (used when: no valid game, per-game disabled, or game profile doesn't exist yet)
            if (!GetGlobalPowerSourceProfileEnabled())
            {
                return "Global";
            }
            else
            {
                return isOnAC ? "AC" : "DC";
            }
        }

        private void SaveCurrentSettingsToProfile(string profileName)
        {
            // Guard against null profile name during XAML initialization
            if (string.IsNullOrEmpty(profileName))
            {
                return;
            }

            // Don't save during helper updates - prevents race conditions
            if (isApplyingHelperUpdate)
            {
                Logger.Debug($"Skipping profile save for {profileName} - isApplyingHelperUpdate is true");
                return;
            }

            // Don't save during initial sync - prevents stale widget values from overwriting
            // the helper's actual hardware state in the profile
            if (isInitialSync)
            {
                Logger.Debug($"Skipping profile save for {profileName} - isInitialSync is true");
                return;
            }

            // Don't save when Default Game Profile is active - prevents overwriting user's profile
            if (defaultGameProfileEnabled?.Value == true)
            {
                Logger.Debug($"Skipping profile save for {profileName} - Default Game Profile is active");
                return;
            }

            // Don't save during DGP restoration - toggle handlers would save wrong values during state restore
            if (isRestoringFromDefaultProfile)
            {
                Logger.Debug($"Skipping profile save for {profileName} - restoring from Default Game Profile");
                return;
            }

            // Never save to "No game detected" profile (case-insensitive check)
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to save to invalid profile name: {profileName}, skipping");
                return;
            }

            // Don't auto-save to game profiles that haven't been explicitly created.
            // Only LoadOrCreateGameProfiles() should create new game profile storage containers.
            // Without this guard, deferred UI events after SwitchProfile can accidentally create
            // ghost profiles for fuzzy-matched launcher names (e.g., "Game_Hollow Knight: Silksong").
            if (profileName.StartsWith("Game_"))
            {
                var settings2 = ApplicationData.Current.LocalSettings;
                if (!settings2.Containers.ContainsKey($"Profile_{profileName}"))
                {
                    Logger.Warn($"Skipping auto-save to non-existent game profile '{profileName}' (profile must be created via LoadOrCreateGameProfiles first)");
                    return;
                }
            }

            var profile = GetProfile(profileName);

            // Save only enabled settings
            if (SaveTDP && TDPSlider != null && TDPModeComboBox != null)
            {
                int selectedIndex = TDPModeComboBox.SelectedIndex;
                if (selectedIndex >= 0)
                {
                    // Always save the TDP mode index for proper restoration with custom presets
                    profile.TDPModeIndex = selectedIndex;

                    // Get the Legion mode value from the current selection
                    int legionModeValue = GetCurrentPresetLegionMode();
                    profile.LegionPerformanceMode = legionModeValue;

                    // Only save TDP slider value if in actual Custom mode (slider-controlled)
                    // Not for user-made presets (which also have legionModeValue=255 but aren't Custom)
                    if (IsCustomTdpModeSelected())
                    {
                        profile.TDP = TDPSlider.Value;
                        // Also update savedCustomTDP for consistency
                        savedCustomTDP = TDPSlider.Value;
                    }
                    // For preset modes (including user-made), keep the profile's existing TDP value
                }
            }
            if (SaveCPUBoost && CPUBoostToggle != null)
            {
                profile.CPUBoost = CPUBoostToggle.IsOn;
            }
            if (SaveCPUEPP && CPUEPPSlider != null)
            {
                profile.CPUEPP = CPUEPPSlider.Value;
            }
            if (SaveCPUState && MaxCPUStateComboBox != null && MinCPUStateComboBox != null)
            {
                profile.MaxCPUState = GetSelectedCPUStateValue(MaxCPUStateComboBox);
                profile.MinCPUState = GetSelectedCPUStateValue(MinCPUStateComboBox);
            }
            if (SaveAMDFeatures && AMDFluidMotionFrameToggle != null)
            {
                profile.FluidMotionFrames = AMDFluidMotionFrameToggle.IsOn;
                profile.RadeonSuperResolution = AMDRadeonSuperResolutionToggle.IsOn;
                profile.RadeonSuperResolutionSharpness = AMDRadeonSuperResolutionSharpnessSlider.Value;
                profile.ImageSharpening = AMDImageSharpeningToggle.IsOn;
                profile.ImageSharpeningSharpness = AMDImageSharpeningSlider.Value;
                profile.RadeonAntiLag = AMDRadeonAntiLagToggle.IsOn;
                profile.RadeonBoost = AMDRadeonBoostToggle.IsOn;
                profile.RadeonBoostResolution = AMDRadeonBoostResolutionSlider.Value;
                profile.RadeonChill = AMDRadeonChillToggle.IsOn;
                profile.RadeonChillMinFPS = AMDRadeonChillMinFPSSlider.Value;
                profile.RadeonChillMaxFPS = AMDRadeonChillMaxFPSSlider.Value;
            }
            if (SaveFPSLimit && FPSLimitToggle != null && FPSLimitSlider != null)
            {
                profile.FPSLimitEnabled = FPSLimitToggle.IsOn;
                profile.FPSLimitValue = (int)FPSLimitSlider.Value;
            }
            if (SaveAutoTDP && AutoTDPToggle != null && AutoTDPTargetFPSSlider != null && AutoTDPMinSlider != null && AutoTDPMaxSlider != null)
            {
                profile.AutoTDPEnabled = AutoTDPToggle.IsOn;
                profile.AutoTDPTargetFPS = (int)AutoTDPTargetFPSSlider.Value;
                profile.AutoTDPMinTDP = (int)AutoTDPMinSlider.Value;
                profile.AutoTDPMaxTDP = (int)AutoTDPMaxSlider.Value;
                // Save the controller type (0=PID, 1=Q-Learning, 2=SARSA)
                profile.AutoTDPControllerType = AutoTDPControllerModeComboBox?.SelectedIndex ?? 0;
                // Also update deprecated field for backwards compatibility
                profile.AutoTDPUseMLMode = AutoTDPControllerModeComboBox?.SelectedIndex > 0;
            }
            if (SaveOSPowerMode && OSPowerModeComboBox != null)
            {
                profile.OSPowerMode = OSPowerModeComboBox.SelectedIndex;
            }
            // TDP Boost is always saved with TDP (they go together)
            if (SaveTDP && TDPBoostToggle != null)
            {
                profile.TDPBoostEnabled = TDPBoostToggle.IsOn;
            }
            // HDR
            if (SaveHDR)
            {
                profile.HDREnabled = HDRToggle?.IsOn ?? false;
            }
            // Resolution
            if (SaveResolution)
            {
                profile.Resolution = ResolutionComboBox?.SelectedItem?.ToString() ?? "";
            }
            // Refresh Rate
            if (SaveRefreshRate)
            {
                profile.RefreshRate = refreshRate?.Value;
            }
            // Sticky TDP
            if (SaveStickyTDP && StickyTDPToggle != null)
            {
                profile.StickyTDPEnabled = StickyTDPToggle.IsOn;
                profile.StickyTDPInterval = (int)(StickyTDPIntervalSlider?.Value ?? 5);
            }
            // Overlay Level
            if (SaveOverlayLevel && PerformanceOverlayComboBox != null)
            {
                profile.OverlayLevel = PerformanceOverlayComboBox.SelectedIndex;
            }
            // CPU Affinity
            if (SaveCPUAffinity)
            {
                profile.CPUAffinity = $"{activePCores},{activeECores}";
            }

            // Persist to storage
            Logger.Info($"Saving profile {profileName}: TDP={profile.TDP}W");
            SaveProfileToStorage(profileName, profile);

            // Update profile display
            UpdateProfileDisplay();
        }

        private void LoadProfileSettings(string profileName, bool isExplicitSwitch = false)
        {
            if (isLoadingProfile) return;
            isLoadingProfile = true;
            profileSwitchEpoch++; // Invalidate any deferred PropertyChanged callbacks queued before this switch

            try
            {
                var profile = GetProfile(profileName);

                // For Legion devices: check if we need to switch to Custom mode BEFORE sending any TDP-related settings
                // This prevents TDP/TDPBoost/EPP from being ignored when helper is still in preset mode
                bool legionNeedsModeChange = false;
                bool legionSwitchingToCustom = false;
                if (legionGoDetected?.Value == true && defaultGameProfileEnabled?.Value != true && !isInitialSync)
                {
                    int profileMode = profile.LegionPerformanceMode;
                    int modeIndex = GetProfileTDPModeIndex(profile);
                    if (modeIndex >= 0 && legionPerformanceMode?.Value != profileMode)
                    {
                        legionNeedsModeChange = true;
                        legionSwitchingToCustom = profileMode == 255;
                    }
                }

                // Apply only enabled settings to UI controls
                // Skip TDP loading when DGP is active - DGP controls TDP
                if (SaveTDP && defaultGameProfileEnabled?.Value != true)
                {
                    // Set IsUpdatingUI to prevent the slider's ValueChanged from starting debounce timer.
                    // Without this, the slider fires ValueChanged → debounce timer → sends stale LocalSettings
                    // value back to helper, corrupting the per-game profile.
                    if (tdp != null) tdp.IsUpdatingUI = true;
                    try
                    {
                        TDPSlider.Value = profile.TDP;
                    }
                    finally
                    {
                        if (tdp != null) tdp.IsUpdatingUI = false;
                    }
                    // Only initialize savedCustomTDP from profile's TDP value if the profile was saved in Custom mode
                    // Otherwise we'd be saving a preset's TDP value as the custom TDP value
                    if (profile.LegionPerformanceMode == 255)
                    {
                        savedCustomTDP = profile.TDP;
                        Logger.Debug($"Initialized savedCustomTDP from profile (Custom mode): {savedCustomTDP}W");
                    }

                    // For Legion devices: TDP value will be sent AFTER TDP mode is applied (see Legion-specific handling below)
                    // This prevents TDP from being ignored when switching from preset mode to Custom mode
                    // For non-Legion devices: send TDP value immediately
                    // Skip sending when helper triggered the profile switch (isApplyingHelperUpdate) —
                    // the helper already sent the correct TDP via pipe, don't overwrite with stale LocalSettings value
                    if (legionGoDetected?.Value != true && !isApplyingHelperUpdate)
                    {
                        tdp?.ForceSetValue((int)profile.TDP);
                    }
                    // Update Sticky TDP target when loading profile
                    if (StickyTDPToggle?.IsOn == true)
                    {
                        targetTDPLimit = profile.TDP;
                        Logger.Info($"Sticky TDP target updated to: {targetTDPLimit}W (profile load)");
                    }
                    // Load TDP Boost toggle state from profile
                    // For Legion devices switching to Custom mode: defer sending to helper until mode change is applied
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsOn = profile.TDPBoostEnabled;
                        if (!legionSwitchingToCustom && !isApplyingHelperUpdate)
                        {
                            tdpBoostEnabled?.SetValue(profile.TDPBoostEnabled);
                        }
                        Logger.Info($"TDP Boost loaded from profile: {profile.TDPBoostEnabled} (deferred={legionSwitchingToCustom})");
                    }
                }
                if (SaveCPUBoost)
                {
                    CPUBoostToggle.IsOn = profile.CPUBoost;
                    // Send to helper explicitly — skip when helper triggered the switch
                    if (!isApplyingHelperUpdate)
                    {
                        cpuBoost?.SetValue(profile.CPUBoost);
                    }
                }
                if (SaveCPUEPP)
                {
                    // Set IsUpdatingUI to prevent EPP slider debounce timer
                    if (cpuEPP != null) cpuEPP.IsUpdatingUI = true;
                    try
                    {
                        CPUEPPSlider.Value = profile.CPUEPP;
                    }
                    finally
                    {
                        if (cpuEPP != null) cpuEPP.IsUpdatingUI = false;
                    }
                    // Send to helper explicitly (cast to int for property type)
                    // For Legion devices switching to Custom mode: defer sending to helper until mode change is applied
                    // Skip when helper triggered the switch
                    if (!legionSwitchingToCustom && !isApplyingHelperUpdate)
                    {
                        cpuEPP?.SetValue((int)profile.CPUEPP);
                    }
                }
                if (SaveCPUState)
                {
                    SetCPUStateComboBoxValue(MaxCPUStateComboBox, profile.MaxCPUState);
                    SetCPUStateComboBoxValue(MinCPUStateComboBox, profile.MinCPUState);
                    // Send to helper explicitly — skip when helper triggered the switch
                    if (!isApplyingHelperUpdate)
                    {
                        maxCPUState?.SetValue(profile.MaxCPUState);
                        minCPUState?.SetValue(profile.MinCPUState);
                    }
                    // Update CPU Boost enabled state based on Max CPU State
                    UpdateCPUBoostEnabledState();
                }
                if (SaveAMDFeatures)
                {
                    // RSR and RIS are mutually exclusive - if both are enabled in profile, prefer RSR
                    bool rsrEnabled = profile.RadeonSuperResolution;
                    bool risEnabled = profile.ImageSharpening;
                    if (rsrEnabled && risEnabled)
                    {
                        Logger.Warn("Profile has both RSR and RIS enabled - disabling RIS (mutually exclusive)");
                        risEnabled = false;
                    }

                    // Chill is mutually exclusive with Anti-Lag and Boost - if Chill is enabled, disable the others
                    bool antiLagEnabled = profile.RadeonAntiLag;
                    bool boostEnabled = profile.RadeonBoost;
                    bool chillEnabled = profile.RadeonChill;
                    if (chillEnabled && (antiLagEnabled || boostEnabled))
                    {
                        Logger.Warn("Profile has Chill with Anti-Lag/Boost enabled - disabling Anti-Lag and Boost (mutually exclusive)");
                        antiLagEnabled = false;
                        boostEnabled = false;
                    }

                    AMDFluidMotionFrameToggle.IsOn = profile.FluidMotionFrames;
                    AMDRadeonSuperResolutionToggle.IsOn = rsrEnabled;
                    AMDRadeonSuperResolutionSharpnessSlider.Value = profile.RadeonSuperResolutionSharpness;
                    AMDImageSharpeningToggle.IsOn = risEnabled;
                    AMDImageSharpeningSlider.Value = profile.ImageSharpeningSharpness;
                    AMDRadeonAntiLagToggle.IsOn = antiLagEnabled;
                    AMDRadeonBoostToggle.IsOn = boostEnabled;
                    AMDRadeonBoostResolutionSlider.Value = profile.RadeonBoostResolution;
                    AMDRadeonChillToggle.IsOn = chillEnabled;
                    AMDRadeonChillMinFPSSlider.Value = profile.RadeonChillMinFPS;
                    AMDRadeonChillMaxFPSSlider.Value = profile.RadeonChillMaxFPS;
                    // Send to helper explicitly using ForceSetValue to ensure AMD driver state is synchronized
                    // even if the cached value appears unchanged (driver state may differ from cache)
                    // Send RIS first (to disable it if needed), then RSR
                    // Send Anti-Lag and Boost first (to disable them if needed), then Chill
                    amdFluidMotionFrameEnabled?.ForceSetValue(profile.FluidMotionFrames);
                    amdImageSharpeningEnabled?.ForceSetValue(risEnabled);
                    amdImageSharpeningSharpness?.ForceSetValue((int)profile.ImageSharpeningSharpness);
                    amdRadeonSuperResolutionEnabled?.ForceSetValue(rsrEnabled);
                    amdRadeonSuperResolutionSharpness?.ForceSetValue((int)profile.RadeonSuperResolutionSharpness);
                    amdRadeonAntiLagEnabled?.ForceSetValue(antiLagEnabled);
                    amdRadeonBoostEnabled?.ForceSetValue(boostEnabled);
                    amdRadeonBoostResolution?.ForceSetValue((int)profile.RadeonBoostResolution);
                    amdRadeonChillEnabled?.ForceSetValue(chillEnabled);
                    amdRadeonChillMinFPSProperty?.ForceSetValue((int)profile.RadeonChillMinFPS);
                    amdRadeonChillMaxFPSProperty?.ForceSetValue((int)profile.RadeonChillMaxFPS);
                }
                if (SaveFPSLimit)
                {
                    FPSLimitToggle.IsOn = profile.FPSLimitEnabled;
                    FPSLimitSlider.Value = profile.FPSLimitValue;
                    // Send to helper explicitly (toggle/slider handlers may be blocked by flags)
                    int fpsLimitValue = profile.FPSLimitEnabled ? profile.FPSLimitValue : 0;
                    fpsLimit?.SetValue(fpsLimitValue);
                }
                if (SaveAutoTDP)
                {
                    // Set loading flag to prevent toggled event from sending to helper
                    isLoadingAutoTDPSettings = true;
                    try
                    {
                        // For game profiles, use the helper's synced property value.
                        // The widget's profile may be stale (e.g., AutoTDP=false saved during game close
                        // when helper restored global values). The helper is the source of truth.
                        bool autoTDPState = profileName.StartsWith("Game_") && autoTDPEnabled != null
                            ? autoTDPEnabled.Value
                            : profile.AutoTDPEnabled;
                        AutoTDPToggle.IsOn = autoTDPState;
                        AutoTDPTargetFPSSlider.Value = profile.AutoTDPTargetFPS;
                        AutoTDPMinSlider.Value = profile.AutoTDPMinTDP;
                        AutoTDPMaxSlider.Value = profile.AutoTDPMaxTDP;
                        // Update text displays explicitly
                        if (AutoTDPTargetFPSValue != null)
                        {
                            AutoTDPTargetFPSValue.Text = $"{profile.AutoTDPTargetFPS} FPS";
                        }
                        if (AutoTDPMinValue != null)
                        {
                            AutoTDPMinValue.Text = $"{profile.AutoTDPMinTDP}W";
                        }
                        if (AutoTDPMaxValue != null)
                        {
                            AutoTDPMaxValue.Text = $"{profile.AutoTDPMaxTDP}W";
                        }
                        // Update controller type selection (0=PID, 1=Q-Learning, 2=SARSA)
                        if (AutoTDPControllerModeComboBox != null)
                        {
                            AutoTDPControllerModeComboBox.SelectedIndex = profile.AutoTDPControllerType;
                            UpdateAutoTDPMLInfoPanelVisibility();
                        }
                        // NOTE: Do NOT send to helper here - helper is source of truth for profile values
                        // Helper will apply profile values and sync back to widget
                    }
                    finally
                    {
                        isLoadingAutoTDPSettings = false;
                    }
                }
                if (SaveOSPowerMode)
                {
                    isLoadingOSPowerMode = true;
                    try
                    {
                        OSPowerModeComboBox.SelectedIndex = profile.OSPowerMode;
                        if (profile.OSPowerMode >= 0 && profile.OSPowerMode < OSPowerModeNames.Length)
                        {
                            OSPowerModeValue.Text = OSPowerModeNames[profile.OSPowerMode];
                        }
                        // Send to helper explicitly
                        osPowerMode?.SetValue(profile.OSPowerMode);
                    }
                    finally
                    {
                        isLoadingOSPowerMode = false;
                    }
                }
                // Legion Performance Mode handling
                // Skip TDP mode loading when:
                // - Default Game Profile is active (DGP controls TDP)
                // - Initial sync is in progress (let helper's value take precedence - DGP state not yet known)
                Logger.Info($"LoadProfileSettings Legion check: legionGoDetected={legionGoDetected?.Value}, LegionPerformanceModeComboBox={LegionPerformanceModeComboBox != null}, TDPModeComboBox={TDPModeComboBox != null}, defaultGameProfileEnabled={defaultGameProfileEnabled?.Value}, isInitialSync={isInitialSync}");
                if (legionGoDetected?.Value == true && LegionPerformanceModeComboBox != null && TDPModeComboBox != null && defaultGameProfileEnabled?.Value != true && !isInitialSync)
                {
                    int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom

                    if (profileName.StartsWith("Game_"))
                    {
                        // Loading a game profile: save the source profile's TDP mode (not the current UI state)
                        // This ensures we restore to the intended profile mode when the game closes
                        if (savedLegionPerformanceMode < 0)
                        {
                            // Save from the correct source profile based on Power Source Profile toggle
                            if (GetGlobalPowerSourceProfileEnabled())
                            {
                                var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                                bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;
                                savedLegionPerformanceMode = isOnAC ? acProfile.LegionPerformanceMode : dcProfile.LegionPerformanceMode;
                                Logger.Info($"Saved Legion Performance Mode from {(isOnAC ? "AC" : "DC")} profile: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) before game profile");
                            }
                            else
                            {
                                savedLegionPerformanceMode = globalProfile.LegionPerformanceMode;
                                Logger.Info($"Saved Legion Performance Mode from global profile: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) before game profile");
                            }
                        }

                        // Apply game profile's TDP Mode if SaveTDP is enabled
                        if (SaveTDP)
                        {
                            int profileMode = profile.LegionPerformanceMode;
                            int modeIndex = GetProfileTDPModeIndex(profile);

                            // For game profiles, the helper manages LegionPerformanceMode in PerGameProfile_PropertyChanged:
                            // it applies the saved mode from the helper's profile (or Custom for new profiles).
                            // Don't send mode to helper here — that would override the helper's mode and cause
                            // "switches to Custom then immediately back" when profiles have stale/corrupted modes.
                            // Just update lastTDPModeIndex so the handler doesn't treat the helper's mode update as a "change".
                            if (modeIndex >= 0)
                                lastTDPModeIndex = modeIndex;
                            Logger.Info($"Game profile: LegionPerformanceMode deferred to helper. Widget profile has: {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName}");
                        }
                        else
                        {
                            // SaveTDP disabled: let helper manage mode (it defaults to Custom for new profiles)
                            lastTDPModeIndex = 3; // Custom mode index
                            Logger.Info($"SaveTDP disabled - deferring mode to helper for game profile: {profileName}");
                        }
                    }
                    else if (savedLegionPerformanceMode >= 0)
                    {
                        // Loading Global/AC/DC profile and we have a saved mode to restore
                        int index = Array.IndexOf(modeValues, savedLegionPerformanceMode);
                        bool modeChanged = false;
                        if (index >= 0 && (legionPerformanceMode.Value != savedLegionPerformanceMode || TDPModeComboBox.SelectedIndex != index))
                        {
                            if (LegionPerformanceModeComboBox.SelectedIndex != index)
                                LegionPerformanceModeComboBox.SelectedIndex = index;
                            if (TDPModeComboBox.SelectedIndex != index)
                            {
                                lastTDPModeIndex = index;
                                TDPModeComboBox.SelectedIndex = index;
                            }
                            legionPerformanceMode?.ForceSetValue(savedLegionPerformanceMode);
                            modeChanged = true;
                            Logger.Info($"Restored Legion Performance Mode: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) after game closed");
                        }
                        // Also restore the TDP slider to the profile's TDP value
                        // This is needed because the slider may still show the game profile's TDP
                        if (SaveTDP && TDPSlider.Value != profile.TDP)
                        {
                            TDPSlider.Value = profile.TDP;
                            Logger.Info($"Restored TDP slider to {profile.TDP}W after game closed");
                        }
                        // If restoring to Custom mode (255), send deferred settings after mode change
                        if (SaveTDP && savedLegionPerformanceMode == 255)
                        {
                            if (modeChanged)
                            {
                                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                                {
                                    await Task.Delay(500); // Allow mode change to propagate to helper
                                    // Send deferred TDP-related settings
                                    if (TDPBoostToggle != null)
                                    {
                                        tdpBoostEnabled?.ForceSetValue(profile.TDPBoostEnabled);
                                    }
                                    if (SaveCPUEPP)
                                    {
                                        cpuEPP?.ForceSetValue((int)profile.CPUEPP);
                                    }
                                    tdp?.ForceSetValue((int)profile.TDP);
                                    Logger.Info($"Restored TDP settings after mode change: TDP={profile.TDP}W, Boost={profile.TDPBoostEnabled}, EPP={profile.CPUEPP}");
                                });
                            }
                            else
                            {
                                tdp?.ForceSetValue((int)profile.TDP);
                                Logger.Info($"Restored TDP value (already in Custom mode): {profile.TDP}W");
                            }
                        }
                        savedLegionPerformanceMode = -1; // Clear saved mode
                    }
                    else if (SaveTDP)
                    {
                        // Loading Global profile directly (not returning from game) - apply profile's TDP Mode
                        int profileMode = profile.LegionPerformanceMode;
                        int modeIndex = GetProfileTDPModeIndex(profile);
                        Logger.Info($"LoadProfileSettings: profileMode={profileMode}, modeIndex={modeIndex}, legionPerformanceMode.Value={legionPerformanceMode?.Value}, TDPModeComboBox.SelectedIndex={TDPModeComboBox?.SelectedIndex}");

                        // Always update UI to match profile when loading Global profile
                        // The internal value may already match (set by helper) but UI may be stale
                        bool modeChanged = false;
                        if (modeIndex >= 0)
                        {
                            // Update lastTDPModeIndex FIRST to prevent TDPModeComboBox_SelectionChanged
                            // from treating the profile load as a user-initiated change
                            lastTDPModeIndex = modeIndex;

                            modeChanged = legionPerformanceMode.Value != profileMode;
                            if (LegionPerformanceModeComboBox.SelectedIndex != modeIndex)
                                LegionPerformanceModeComboBox.SelectedIndex = modeIndex;
                            if (TDPModeComboBox.SelectedIndex != modeIndex)
                                TDPModeComboBox.SelectedIndex = modeIndex;
                            legionPerformanceMode?.ForceSetValue(profileMode);
                            Logger.Info($"Applied profile TDP Mode: {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName}");

                            // If Custom mode (255), send deferred settings after mode change
                            if (profileMode == 255)
                            {
                                if (modeChanged)
                                {
                                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                                    {
                                        await Task.Delay(500); // Allow mode change to propagate to helper
                                        // Send deferred TDP-related settings
                                        if (TDPBoostToggle != null)
                                        {
                                            tdpBoostEnabled?.ForceSetValue(profile.TDPBoostEnabled);
                                        }
                                        if (SaveCPUEPP)
                                        {
                                            cpuEPP?.ForceSetValue((int)profile.CPUEPP);
                                        }
                                        tdp?.ForceSetValue((int)profile.TDP);
                                        Logger.Info($"Applied profile TDP settings after mode change: TDP={profile.TDP}W, Boost={profile.TDPBoostEnabled}, EPP={profile.CPUEPP} for {profileName}");
                                    });
                                }
                                else
                                {
                                    tdp?.ForceSetValue((int)profile.TDP);
                                    Logger.Info($"Applied profile TDP value (already in Custom mode): {profile.TDP}W for {profileName}");
                                }
                            }
                        }
                    }

                    // Update TDP slider enabled state based on mode
                    // Skip for game profiles: helper manages TDP mode, and the ComboBox hasn't been
                    // updated yet (it still shows the old global mode). Running UpdateTDPSliderEnabledState
                    // now would see the wrong mode and send incorrect values to the helper (e.g.,
                    // AutoTDP=false when the game profile has AutoTDP=true, because the old mode is
                    // non-Custom). UpdateTDPSliderEnabledState runs naturally when the helper sends its
                    // mode via pipe → ComboBox updates → TDPModeComboBox_SelectionChanged.
                    if (!profileName.StartsWith("Game_"))
                    {
                        UpdateTDPSliderEnabledState();
                    }
                }
                // Generic device TDP Mode handling
                else if (legionGoDetected?.Value != true && TDPModeComboBox != null && defaultGameProfileEnabled?.Value != true && !isInitialSync)
                {
                    // Load TDP Mode from profile for generic devices
                    int profileMode = profile.LegionPerformanceMode;
                    int modeIndex = GetProfileTDPModeIndex(profile); // Already defaults to Balanced if not found

                    // Always sync lastTDPModeIndex to match the profile's mode.
                    // Without this, lastTDPModeIndex retains a stale value from the previous
                    // user session, causing TDPModeComboBox_SelectionChanged to skip the first
                    // mode change (selectedIndex == lastTDPModeIndex early return).
                    if (SaveTDP)
                    {
                        lastTDPModeIndex = modeIndex;

                        if (TDPModeComboBox.SelectedIndex != modeIndex)
                        {
                            TDPModeComboBox.SelectedIndex = modeIndex;
                            Logger.Info($"Applied generic device TDP Mode: index {modeIndex} (mode {profileMode}) for {profileName}");

                            // For Custom mode, the TDP slider value was already set above
                            // For preset modes, apply the preset TDP value
                            if (profileMode != 255)
                            {
                                int[] genericTDPValues = { 8, 15, 25 }; // Quiet, Balanced, Performance TDP values
                                if (modeIndex >= 0 && modeIndex < genericTDPValues.Length)
                                {
                                    int presetTDP = genericTDPValues[modeIndex];
                                    TDPSlider.Value = presetTDP;
                                    tdp?.ForceSetValue(presetTDP);
                                    Logger.Info($"Applied generic device preset TDP: {presetTDP}W");
                                }
                            }
                        }
                    }

                    // Update TDP slider enabled state based on mode
                    UpdateTDPSliderEnabledState();
                }

                // HDR
                if (SaveHDR)
                {
                    // Only apply HDR if:
                    // 1. This is an explicit profile switch (game detected/closed), OR
                    // 2. No game is currently running (returning to desktop)
                    bool shouldApplyHDR = isExplicitSwitch || !HasValidGame(currentGameName);

                    if (shouldApplyHDR)
                    {
                        if (HDRToggle != null && hdrSupported?.Value == true)
                        {
                            HDRToggle.IsOn = profile.HDREnabled;
                            hdrEnabled?.SetValue(profile.HDREnabled);
                        }
                    }
                    else
                    {
                        Logger.Info($"Skipping HDR application - game is running and not an explicit switch");
                    }
                }

                // Resolution
                if (SaveResolution)
                {
                    // Only apply Resolution if:
                    // 1. This is an explicit profile switch (game detected/closed), OR
                    // 2. No game is currently running (returning to desktop)
                    bool shouldApplyResolution = isExplicitSwitch || !HasValidGame(currentGameName);

                    if (shouldApplyResolution)
                    {
                        if (ResolutionComboBox != null && !string.IsNullOrEmpty(profile.Resolution))
                        {
                            // Find and select matching resolution
                            for (int i = 0; i < ResolutionComboBox.Items.Count; i++)
                            {
                                if (ResolutionComboBox.Items[i]?.ToString() == profile.Resolution)
                                {
                                    ResolutionComboBox.SelectedIndex = i;
                                    resolution?.SetValue(profile.Resolution);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger.Info($"Skipping Resolution application - game is running and not an explicit switch");
                    }
                }

                // Refresh Rate
                if (SaveRefreshRate)
                {
                    // Only apply Refresh Rate if:
                    // 1. This is an explicit profile switch (game detected/closed), OR
                    // 2. No game is currently running (returning to desktop)
                    bool shouldApplyRefreshRate = isExplicitSwitch || !HasValidGame(currentGameName);

                    if (shouldApplyRefreshRate)
                    {
                        if (RefreshRatesComboBox != null && profile.RefreshRate.HasValue)
                        {
                            // Find and select matching refresh rate
                            for (int i = 0; i < RefreshRatesComboBox.Items.Count; i++)
                            {
                                if (RefreshRatesComboBox.Items[i] is int rate && rate == profile.RefreshRate.Value)
                                {
                                    RefreshRatesComboBox.SelectedIndex = i;
                                    refreshRate?.SetValue(profile.RefreshRate.Value);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger.Info($"Skipping RefreshRate application - game is running and not an explicit switch");
                    }
                }

                // Sticky TDP
                if (SaveStickyTDP && StickyTDPToggle != null)
                {
                    isLoadingStickyTDPSettings = true;
                    try
                    {
                        StickyTDPToggle.IsOn = profile.StickyTDPEnabled;
                        if (StickyTDPIntervalSlider != null)
                        {
                            StickyTDPIntervalSlider.Value = profile.StickyTDPInterval;
                            stickyTDPCheckIntervalSeconds = profile.StickyTDPInterval;
                        }
                        if (StickyTDPIntervalValue != null)
                        {
                            StickyTDPIntervalValue.Text = $"{profile.StickyTDPInterval}s";
                        }
                        // Update timer state based on profile
                        if (profile.StickyTDPEnabled)
                        {
                            targetTDPLimit = profile.TDP;
                            StartStickyTDPTimer();
                        }
                        else
                        {
                            StopStickyTDPTimer();
                        }
                    }
                    finally
                    {
                        isLoadingStickyTDPSettings = false;
                    }
                }

                // Overlay Level
                if (SaveOverlayLevel && PerformanceOverlayComboBox != null)
                {
                    int level = profile.OverlayLevel;
                    if (level >= 0 && level < PerformanceOverlayComboBox.Items.Count)
                    {
                        PerformanceOverlayComboBox.SelectedIndex = level;
                        // The SelectionChanged handler will update PerformanceOverlaySlider and send to system
                    }
                }

                // CPU Affinity
                if (SaveCPUAffinity && !string.IsNullOrEmpty(profile.CPUAffinity))
                {
                    var parts = profile.CPUAffinity.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int pCores) && int.TryParse(parts[1], out int eCores))
                    {
                        // Validate that at least one core type is active
                        if (pCores > 0 || eCores > 0)
                        {
                            isLoadingCPUCoreConfig = true;
                            try
                            {
                                activePCores = pCores;
                                activeECores = eCores;
                                // Update UI controls
                                UpdatePCoreComboBox();
                                UpdateECoreComboBox();
                            }
                            finally
                            {
                                isLoadingCPUCoreConfig = false;
                            }
                            // Send to helper
                            SendCPUCoreConfigToHelper();
                            Logger.Info($"Applied CPU Affinity from profile: P={pCores}, E={eCores}");
                        }
                    }
                }

                // Update profile display to show correct TDP mode in Profiles tab
                UpdateProfileDisplay();

                // Safety check: If AutoTDP is enabled but we're not in Custom mode, switch to Custom mode
                // This handles profiles that were saved with incorrect mode values before the fix.
                // Skip for game profiles: helper manages TDP mode for game profiles, and the toggle
                // may show true from autoTDPEnabled.Value which belongs to a DIFFERENT game's profile.
                if (SaveAutoTDP && AutoTDPToggle?.IsOn == true && legionGoDetected?.Value == true && !profileName.StartsWith("Game_"))
                {
                    int customIndex = GetCustomTdpModeIndex();
                    if (TDPModeComboBox != null && !IsCustomTdpModeSelected())
                    {
                        Logger.Info($"AutoTDP enabled but not in Custom mode - fixing mode to Custom");
                        isUpdatingTDPMode = true;
                        try
                        {
                            lastTDPModeIndex = customIndex;
                            TDPModeComboBox.SelectedIndex = customIndex;
                            if (LegionPerformanceModeComboBox != null)
                                LegionPerformanceModeComboBox.SelectedIndex = customIndex;
                            legionPerformanceMode?.SetValue(255);
                            UpdateTDPSliderEnabledState();
                        }
                        finally
                        {
                            isUpdatingTDPMode = false;
                        }
                    }
                }
            }
            finally
            {
                isLoadingProfile = false;
            }
        }

        private PerformanceProfile GetProfile(string profileName)
        {
            // Never return a game profile for invalid game names (case-insensitive check)
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to get invalid profile: {profileName}, returning global profile");
                return globalProfile;
            }

            // Handle game profiles
            if (profileName.StartsWith("Game_"))
            {
                if (profileName.EndsWith("_AC"))
                    return gameACProfile;
                else if (profileName.EndsWith("_DC"))
                    return gameDCProfile;
                else
                    return gameProfile;
            }

            // Handle global profiles
            switch (profileName)
            {
                case "AC": return acProfile;
                case "DC": return dcProfile;
                default: return globalProfile;
            }
        }

        private void SaveProfileToStorage(string profileName, PerformanceProfile profile)
        {
            // Never save to "No game detected" profile (case-insensitive check)
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to save to storage with invalid profile name: {profileName}, skipping");
                return;
            }

            var settings = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer($"Profile_{profileName}", ApplicationDataCreateDisposition.Always);

            container.Values["TDP"] = profile.TDP;
            container.Values["CPUBoost"] = profile.CPUBoost;
            container.Values["CPUEPP"] = profile.CPUEPP;
            container.Values["MaxCPUState"] = profile.MaxCPUState;
            container.Values["MinCPUState"] = profile.MinCPUState;
            container.Values["FluidMotionFrames"] = profile.FluidMotionFrames;
            container.Values["RadeonSuperResolution"] = profile.RadeonSuperResolution;
            container.Values["RadeonSuperResolutionSharpness"] = profile.RadeonSuperResolutionSharpness;
            container.Values["ImageSharpening"] = profile.ImageSharpening;
            container.Values["ImageSharpeningSharpness"] = profile.ImageSharpeningSharpness;
            container.Values["RadeonAntiLag"] = profile.RadeonAntiLag;
            container.Values["RadeonBoost"] = profile.RadeonBoost;
            container.Values["RadeonBoostResolution"] = profile.RadeonBoostResolution;
            container.Values["RadeonChill"] = profile.RadeonChill;
            container.Values["RadeonChillMinFPS"] = profile.RadeonChillMinFPS;
            container.Values["RadeonChillMaxFPS"] = profile.RadeonChillMaxFPS;
            container.Values["FPSLimitEnabled"] = profile.FPSLimitEnabled;
            container.Values["FPSLimitValue"] = profile.FPSLimitValue;
            container.Values["AutoTDPEnabled"] = profile.AutoTDPEnabled;
            container.Values["AutoTDPTargetFPS"] = profile.AutoTDPTargetFPS;
            container.Values["AutoTDPMinTDP"] = profile.AutoTDPMinTDP;
            container.Values["AutoTDPMaxTDP"] = profile.AutoTDPMaxTDP;
            container.Values["AutoTDPUseMLMode"] = profile.AutoTDPUseMLMode;
            container.Values["AutoTDPControllerType"] = profile.AutoTDPControllerType;
            container.Values["OSPowerMode"] = profile.OSPowerMode;
            container.Values["LegionPerformanceMode"] = profile.LegionPerformanceMode;
            container.Values["TDPModeIndex"] = profile.TDPModeIndex;
            container.Values["TDPBoostEnabled"] = profile.TDPBoostEnabled;
            container.Values["HDREnabled"] = profile.HDREnabled;
            container.Values["Resolution"] = profile.Resolution;
            if (profile.RefreshRate.HasValue)
                container.Values["RefreshRate"] = profile.RefreshRate.Value;
            else
                container.Values.Remove("RefreshRate");
            container.Values["StickyTDPEnabled"] = profile.StickyTDPEnabled;
            container.Values["StickyTDPInterval"] = profile.StickyTDPInterval;
            container.Values["OverlayLevel"] = profile.OverlayLevel;
            container.Values["CPUAffinity"] = profile.CPUAffinity;
        }

        private void LoadProfileFromStorage(string profileName, PerformanceProfile profile)
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey($"Profile_{profileName}"))
            {
                var container = settings.Containers[$"Profile_{profileName}"];

                profile.TDP = container.Values.ContainsKey("TDP") ? (double)container.Values["TDP"] : 15;
                // Use current system values as defaults for EPP and CPU Boost (synced from helper)
                profile.CPUBoost = container.Values.ContainsKey("CPUBoost") ? (bool)container.Values["CPUBoost"] : (cpuBoost?.Value ?? false);
                profile.CPUEPP = container.Values.ContainsKey("CPUEPP") ? (double)container.Values["CPUEPP"] : (cpuEPP?.Value ?? 80);
                profile.MaxCPUState = container.Values.ContainsKey("MaxCPUState") ? (int)container.Values["MaxCPUState"] : 100;
                profile.MinCPUState = container.Values.ContainsKey("MinCPUState") ? (int)container.Values["MinCPUState"] : 5;
                profile.FluidMotionFrames = container.Values.ContainsKey("FluidMotionFrames") ? (bool)container.Values["FluidMotionFrames"] : false;
                profile.RadeonSuperResolution = container.Values.ContainsKey("RadeonSuperResolution") ? (bool)container.Values["RadeonSuperResolution"] : false;
                profile.RadeonSuperResolutionSharpness = container.Values.ContainsKey("RadeonSuperResolutionSharpness") ? (double)container.Values["RadeonSuperResolutionSharpness"] : 80;
                profile.ImageSharpening = container.Values.ContainsKey("ImageSharpening") ? (bool)container.Values["ImageSharpening"] : false;
                profile.ImageSharpeningSharpness = container.Values.ContainsKey("ImageSharpeningSharpness") ? (double)container.Values["ImageSharpeningSharpness"] : 80;
                profile.RadeonAntiLag = container.Values.ContainsKey("RadeonAntiLag") ? (bool)container.Values["RadeonAntiLag"] : false;
                profile.RadeonBoost = container.Values.ContainsKey("RadeonBoost") ? (bool)container.Values["RadeonBoost"] : false;
                profile.RadeonBoostResolution = container.Values.ContainsKey("RadeonBoostResolution") ? (double)container.Values["RadeonBoostResolution"] : 0;
                profile.RadeonChill = container.Values.ContainsKey("RadeonChill") ? (bool)container.Values["RadeonChill"] : false;
                profile.RadeonChillMinFPS = container.Values.ContainsKey("RadeonChillMinFPS") ? (double)container.Values["RadeonChillMinFPS"] : 30;
                profile.RadeonChillMaxFPS = container.Values.ContainsKey("RadeonChillMaxFPS") ? (double)container.Values["RadeonChillMaxFPS"] : 60;
                profile.FPSLimitEnabled = container.Values.ContainsKey("FPSLimitEnabled") ? (bool)container.Values["FPSLimitEnabled"] : false;
                profile.FPSLimitValue = container.Values.ContainsKey("FPSLimitValue") ? (int)container.Values["FPSLimitValue"] : 60;
                profile.AutoTDPEnabled = container.Values.ContainsKey("AutoTDPEnabled") ? (bool)container.Values["AutoTDPEnabled"] : false;
                profile.AutoTDPTargetFPS = container.Values.ContainsKey("AutoTDPTargetFPS") ? (int)container.Values["AutoTDPTargetFPS"] : 60;
                profile.AutoTDPMinTDP = container.Values.ContainsKey("AutoTDPMinTDP") ? (int)container.Values["AutoTDPMinTDP"] : 8;
                profile.AutoTDPMaxTDP = container.Values.ContainsKey("AutoTDPMaxTDP") ? (int)container.Values["AutoTDPMaxTDP"] : 30;
                profile.AutoTDPUseMLMode = container.Values.ContainsKey("AutoTDPUseMLMode") ? (bool)container.Values["AutoTDPUseMLMode"] : false;
                profile.AutoTDPControllerType = container.Values.ContainsKey("AutoTDPControllerType") ? (int)container.Values["AutoTDPControllerType"] : 0;
                profile.OSPowerMode = container.Values.ContainsKey("OSPowerMode") ? (int)container.Values["OSPowerMode"] : 1;
                // Only load LegionPerformanceMode if it exists in storage - keep profile's existing value otherwise
                // This preserves the default (Balanced=2) for new profiles but doesn't override if storage key is missing
                if (container.Values.ContainsKey("LegionPerformanceMode"))
                {
                    profile.LegionPerformanceMode = (int)container.Values["LegionPerformanceMode"];
                }
                // Load TDPModeIndex for custom presets (-1 means use LegionPerformanceMode to determine index)
                profile.TDPModeIndex = container.Values.ContainsKey("TDPModeIndex") ? (int)container.Values["TDPModeIndex"] : -1;
                profile.TDPBoostEnabled = container.Values.ContainsKey("TDPBoostEnabled") ? (bool)container.Values["TDPBoostEnabled"] : false;
                profile.HDREnabled = container.Values.ContainsKey("HDREnabled") ? (bool)container.Values["HDREnabled"] : false;
                profile.Resolution = container.Values.ContainsKey("Resolution") ? (string)container.Values["Resolution"] : "";
                profile.RefreshRate = container.Values.ContainsKey("RefreshRate") ? (int?)container.Values["RefreshRate"] : null;
                profile.StickyTDPEnabled = container.Values.ContainsKey("StickyTDPEnabled") ? (bool)container.Values["StickyTDPEnabled"] : true;
                profile.StickyTDPInterval = container.Values.ContainsKey("StickyTDPInterval") ? (int)container.Values["StickyTDPInterval"] : 5;
                profile.OverlayLevel = container.Values.ContainsKey("OverlayLevel") ? (int)container.Values["OverlayLevel"] : 0;
                profile.CPUAffinity = container.Values.ContainsKey("CPUAffinity") ? (string)container.Values["CPUAffinity"] : "";

                Logger.Info($"Loaded {profileName} profile from storage");
            }
        }
        private void UpdateProfileDisplay()
        {
            // Guard against calls during XAML initialization when controls aren't ready
            if (GlobalProfileTDPModeLabel == null) return;

            // Determine visibility based on save settings
            var tdpModeVisibility = (legionGoDetected?.Value == true && SaveTDP) ? Visibility.Visible : Visibility.Collapsed;
            var tdpVisibility = SaveTDP ? Visibility.Visible : Visibility.Collapsed;
            var cpuBoostVisibility = SaveCPUBoost ? Visibility.Visible : Visibility.Collapsed;
            var cpuEPPVisibility = SaveCPUEPP ? Visibility.Visible : Visibility.Collapsed;
            var cpuStateVisibility = SaveCPUState ? Visibility.Visible : Visibility.Collapsed;
            var fpsLimitVisibility = SaveFPSLimit ? Visibility.Visible : Visibility.Collapsed;
            var autoTDPVisibility = SaveAutoTDP ? Visibility.Visible : Visibility.Collapsed;
            var powerModeVisibility = SaveOSPowerMode ? Visibility.Visible : Visibility.Collapsed;
            var amdVisibility = SaveAMDFeatures ? Visibility.Visible : Visibility.Collapsed;
            var hdrVisibility = SaveHDR ? Visibility.Visible : Visibility.Collapsed;
            var resolutionVisibility = SaveResolution ? Visibility.Visible : Visibility.Collapsed;
            var stickyTDPVisibility = SaveStickyTDP ? Visibility.Visible : Visibility.Collapsed;

            // Update Global profile display (simple mode)
            GlobalProfileTDPModeLabel.Visibility = tdpModeVisibility;
            GlobalProfileTDPModeText.Visibility = tdpModeVisibility;
            GlobalProfileTDPModeText.Text = GetProfileTDPModeName(globalProfile);

            GlobalProfileTDPLabel.Visibility = tdpVisibility;
            GlobalProfileTDPText.Visibility = tdpVisibility;
            GlobalProfileTDPText.Text = $"{globalProfile.TDP}W";

            GlobalProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
            GlobalProfileCPUBoostText.Visibility = cpuBoostVisibility;
            GlobalProfileCPUBoostText.Text = globalProfile.CPUBoost ? "On" : "Off";

            GlobalProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
            GlobalProfileCPUEPPText.Visibility = cpuEPPVisibility;
            GlobalProfileCPUEPPText.Text = $"{globalProfile.CPUEPP}";

            GlobalProfileCPUStateLabel.Visibility = cpuStateVisibility;
            GlobalProfileCPUStateText.Visibility = cpuStateVisibility;
            GlobalProfileCPUStateText.Text = $"{globalProfile.MinCPUState}-{globalProfile.MaxCPUState}%";

            GlobalProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            GlobalProfileFPSLimitText.Visibility = fpsLimitVisibility;
            GlobalProfileFPSLimitText.Text = globalProfile.FPSLimitEnabled ? $"{globalProfile.FPSLimitValue}" : "Off";

            GlobalProfileAutoTDPLabel.Visibility = autoTDPVisibility;
            GlobalProfileAutoTDPText.Visibility = autoTDPVisibility;
            GlobalProfileAutoTDPText.Text = globalProfile.AutoTDPEnabled ? $"{globalProfile.AutoTDPTargetFPS}fps" : "Off";

            GlobalProfilePowerModeLabel.Visibility = powerModeVisibility;
            GlobalProfilePowerModeText.Visibility = powerModeVisibility;
            GlobalProfilePowerModeText.Text = GetPowerModeShortName(globalProfile.OSPowerMode);

            GlobalProfileAMDLabel.Visibility = amdVisibility;
            GlobalProfileAMDText.Visibility = amdVisibility;
            var globalAmdFeatures = GetAMDFeaturesShortString(globalProfile);
            GlobalProfileAMDText.Text = string.IsNullOrEmpty(globalAmdFeatures) ? "Off" : globalAmdFeatures;

            GlobalProfileHDRLabel.Visibility = hdrVisibility;
            GlobalProfileHDRText.Visibility = hdrVisibility;
            GlobalProfileHDRText.Text = globalProfile.HDREnabled ? "On" : "Off";

            GlobalProfileResolutionLabel.Visibility = resolutionVisibility;
            GlobalProfileResolutionText.Visibility = resolutionVisibility;
            GlobalProfileResolutionText.Text = string.IsNullOrEmpty(globalProfile.Resolution) ? "Native" : globalProfile.Resolution;

            GlobalProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
            GlobalProfileStickyTDPText.Visibility = stickyTDPVisibility;
            GlobalProfileStickyTDPText.Text = globalProfile.StickyTDPEnabled ? "On" : "Off";

            // Update AC/DC profile display
            ACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
            ACProfileTDPModeText.Visibility = tdpModeVisibility;
            DCProfileTDPModeText.Visibility = tdpModeVisibility;
            ACProfileTDPModeText.Text = GetProfileTDPModeName(acProfile);
            DCProfileTDPModeText.Text = GetProfileTDPModeName(dcProfile);

            ACDCProfileTDPLabel.Visibility = tdpVisibility;
            ACProfileTDPText.Visibility = tdpVisibility;
            DCProfileTDPText.Visibility = tdpVisibility;
            ACProfileTDPText.Text = $"{acProfile.TDP}W";
            DCProfileTDPText.Text = $"{dcProfile.TDP}W";

            ACDCProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
            ACProfileCPUBoostText.Visibility = cpuBoostVisibility;
            DCProfileCPUBoostText.Visibility = cpuBoostVisibility;
            ACProfileCPUBoostText.Text = acProfile.CPUBoost ? "On" : "Off";
            DCProfileCPUBoostText.Text = dcProfile.CPUBoost ? "On" : "Off";

            ACDCProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
            ACProfileCPUEPPText.Visibility = cpuEPPVisibility;
            DCProfileCPUEPPText.Visibility = cpuEPPVisibility;
            ACProfileCPUEPPText.Text = $"{acProfile.CPUEPP}";
            DCProfileCPUEPPText.Text = $"{dcProfile.CPUEPP}";

            ACDCProfileCPUStateLabel.Visibility = cpuStateVisibility;
            ACProfileCPUStateText.Visibility = cpuStateVisibility;
            DCProfileCPUStateText.Visibility = cpuStateVisibility;
            ACProfileCPUStateText.Text = $"{acProfile.MinCPUState}-{acProfile.MaxCPUState}%";
            DCProfileCPUStateText.Text = $"{dcProfile.MinCPUState}-{dcProfile.MaxCPUState}%";

            ACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Visibility = fpsLimitVisibility;
            DCProfileFPSLimitText.Visibility = fpsLimitVisibility;
            ACProfileFPSLimitText.Text = acProfile.FPSLimitEnabled ? $"{acProfile.FPSLimitValue}" : "Off";
            DCProfileFPSLimitText.Text = dcProfile.FPSLimitEnabled ? $"{dcProfile.FPSLimitValue}" : "Off";

            ACDCProfileAutoTDPLabel.Visibility = autoTDPVisibility;
            ACProfileAutoTDPText.Visibility = autoTDPVisibility;
            DCProfileAutoTDPText.Visibility = autoTDPVisibility;
            ACProfileAutoTDPText.Text = acProfile.AutoTDPEnabled ? $"{acProfile.AutoTDPTargetFPS}fps" : "Off";
            DCProfileAutoTDPText.Text = dcProfile.AutoTDPEnabled ? $"{dcProfile.AutoTDPTargetFPS}fps" : "Off";

            ACDCProfilePowerModeLabel.Visibility = powerModeVisibility;
            ACProfilePowerModeText.Visibility = powerModeVisibility;
            DCProfilePowerModeText.Visibility = powerModeVisibility;
            ACProfilePowerModeText.Text = GetPowerModeShortName(acProfile.OSPowerMode);
            DCProfilePowerModeText.Text = GetPowerModeShortName(dcProfile.OSPowerMode);

            ACDCProfileAMDLabel.Visibility = amdVisibility;
            ACProfileAMDText.Visibility = amdVisibility;
            DCProfileAMDText.Visibility = amdVisibility;
            var acAmdFeatures = GetAMDFeaturesShortString(acProfile);
            var dcAmdFeatures = GetAMDFeaturesShortString(dcProfile);
            ACProfileAMDText.Text = string.IsNullOrEmpty(acAmdFeatures) ? "Off" : acAmdFeatures;
            DCProfileAMDText.Text = string.IsNullOrEmpty(dcAmdFeatures) ? "Off" : dcAmdFeatures;

            ACDCProfileHDRLabel.Visibility = hdrVisibility;
            ACProfileHDRText.Visibility = hdrVisibility;
            DCProfileHDRText.Visibility = hdrVisibility;
            ACProfileHDRText.Text = acProfile.HDREnabled ? "On" : "Off";
            DCProfileHDRText.Text = dcProfile.HDREnabled ? "On" : "Off";

            ACDCProfileResolutionLabel.Visibility = resolutionVisibility;
            ACProfileResolutionText.Visibility = resolutionVisibility;
            DCProfileResolutionText.Visibility = resolutionVisibility;
            ACProfileResolutionText.Text = string.IsNullOrEmpty(acProfile.Resolution) ? "Native" : acProfile.Resolution;
            DCProfileResolutionText.Text = string.IsNullOrEmpty(dcProfile.Resolution) ? "Native" : dcProfile.Resolution;

            ACDCProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
            ACProfileStickyTDPText.Visibility = stickyTDPVisibility;
            DCProfileStickyTDPText.Visibility = stickyTDPVisibility;
            ACProfileStickyTDPText.Text = acProfile.StickyTDPEnabled ? "On" : "Off";
            DCProfileStickyTDPText.Text = dcProfile.StickyTDPEnabled ? "On" : "Off";

            // Update game profile display (if game is running)
            if (HasValidGame(currentGameName))
            {
                if (GetPerGamePowerSourceProfileEnabled(currentGameName))
                {
                    // Show AC/DC game profiles - TDP Mode (Legion only)
                    GameACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameDCProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Text = GetProfileTDPModeName(gameACProfile);
                    GameDCProfileTDPModeText.Text = GetProfileTDPModeName(gameDCProfile);

                    // TDP
                    GameACDCProfileTDPLabel.Visibility = tdpVisibility;
                    GameACProfileTDPText.Visibility = tdpVisibility;
                    GameDCProfileTDPText.Visibility = tdpVisibility;
                    GameACProfileTDPText.Text = $"{gameACProfile.TDP}W";
                    GameDCProfileTDPText.Text = $"{gameDCProfile.TDP}W";

                    // CPU Boost
                    GameACDCProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
                    GameACProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameDCProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameACProfileCPUBoostText.Text = gameACProfile.CPUBoost ? "On" : "Off";
                    GameDCProfileCPUBoostText.Text = gameDCProfile.CPUBoost ? "On" : "Off";

                    // CPU EPP
                    GameACDCProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
                    GameACProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameDCProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameACProfileCPUEPPText.Text = $"{gameACProfile.CPUEPP}";
                    GameDCProfileCPUEPPText.Text = $"{gameDCProfile.CPUEPP}";

                    // CPU State
                    GameACDCProfileCPUStateLabel.Visibility = cpuStateVisibility;
                    GameACProfileCPUStateText.Visibility = cpuStateVisibility;
                    GameDCProfileCPUStateText.Visibility = cpuStateVisibility;
                    GameACProfileCPUStateText.Text = $"{gameACProfile.MinCPUState}-{gameACProfile.MaxCPUState}%";
                    GameDCProfileCPUStateText.Text = $"{gameDCProfile.MinCPUState}-{gameDCProfile.MaxCPUState}%";

                    // FPS Limit
                    GameACDCProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameACProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameDCProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameACProfileFPSLimitText.Text = gameACProfile.FPSLimitEnabled ? $"{gameACProfile.FPSLimitValue}" : "Off";
                    GameDCProfileFPSLimitText.Text = gameDCProfile.FPSLimitEnabled ? $"{gameDCProfile.FPSLimitValue}" : "Off";

                    // AutoTDP
                    GameACDCProfileAutoTDPLabel.Visibility = autoTDPVisibility;
                    GameACProfileAutoTDPText.Visibility = autoTDPVisibility;
                    GameDCProfileAutoTDPText.Visibility = autoTDPVisibility;
                    GameACProfileAutoTDPText.Text = gameACProfile.AutoTDPEnabled ? $"{gameACProfile.AutoTDPTargetFPS}fps" : "Off";
                    GameDCProfileAutoTDPText.Text = gameDCProfile.AutoTDPEnabled ? $"{gameDCProfile.AutoTDPTargetFPS}fps" : "Off";

                    // Power Mode
                    GameACDCProfilePowerModeLabel.Visibility = powerModeVisibility;
                    GameACProfilePowerModeText.Visibility = powerModeVisibility;
                    GameDCProfilePowerModeText.Visibility = powerModeVisibility;
                    GameACProfilePowerModeText.Text = GetPowerModeShortName(gameACProfile.OSPowerMode);
                    GameDCProfilePowerModeText.Text = GetPowerModeShortName(gameDCProfile.OSPowerMode);

                    // AMD Features
                    GameACDCProfileAMDLabel.Visibility = amdVisibility;
                    GameACProfileAMDText.Visibility = amdVisibility;
                    GameDCProfileAMDText.Visibility = amdVisibility;
                    var gameACAmdFeatures = GetAMDFeaturesShortString(gameACProfile);
                    var gameDCAmdFeatures = GetAMDFeaturesShortString(gameDCProfile);
                    GameACProfileAMDText.Text = string.IsNullOrEmpty(gameACAmdFeatures) ? "Off" : gameACAmdFeatures;
                    GameDCProfileAMDText.Text = string.IsNullOrEmpty(gameDCAmdFeatures) ? "Off" : gameDCAmdFeatures;

                    // HDR
                    GameACDCProfileHDRLabel.Visibility = hdrVisibility;
                    GameACProfileHDRText.Visibility = hdrVisibility;
                    GameDCProfileHDRText.Visibility = hdrVisibility;
                    GameACProfileHDRText.Text = gameACProfile.HDREnabled ? "On" : "Off";
                    GameDCProfileHDRText.Text = gameDCProfile.HDREnabled ? "On" : "Off";

                    // Resolution
                    GameACDCProfileResolutionLabel.Visibility = resolutionVisibility;
                    GameACProfileResolutionText.Visibility = resolutionVisibility;
                    GameDCProfileResolutionText.Visibility = resolutionVisibility;
                    GameACProfileResolutionText.Text = string.IsNullOrEmpty(gameACProfile.Resolution) ? "Native" : gameACProfile.Resolution;
                    GameDCProfileResolutionText.Text = string.IsNullOrEmpty(gameDCProfile.Resolution) ? "Native" : gameDCProfile.Resolution;

                    // Sticky TDP
                    GameACDCProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
                    GameACProfileStickyTDPText.Visibility = stickyTDPVisibility;
                    GameDCProfileStickyTDPText.Visibility = stickyTDPVisibility;
                    GameACProfileStickyTDPText.Text = gameACProfile.StickyTDPEnabled ? "On" : "Off";
                    GameDCProfileStickyTDPText.Text = gameDCProfile.StickyTDPEnabled ? "On" : "Off";
                }
                else
                {
                    // Show single game profile - TDP Mode (Legion only)
                    GameProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameProfileTDPModeText.Text = GetProfileTDPModeName(gameProfile);

                    // TDP
                    GameProfileTDPLabel.Visibility = tdpVisibility;
                    GameProfileTDPText.Visibility = tdpVisibility;
                    GameProfileTDPText.Text = $"{gameProfile.TDP}W";

                    // TDP Boost (saved with TDP)
                    GameProfileTDPBoostLabel.Visibility = tdpVisibility;
                    GameProfileTDPBoostText.Visibility = tdpVisibility;
                    GameProfileTDPBoostText.Text = gameProfile.TDPBoostEnabled ? "On" : "Off";

                    // CPU Boost
                    GameProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
                    GameProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameProfileCPUBoostText.Text = gameProfile.CPUBoost ? "On" : "Off";

                    // CPU EPP
                    GameProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
                    GameProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameProfileCPUEPPText.Text = $"{gameProfile.CPUEPP}";

                    // CPU State
                    GameProfileCPUStateLabel.Visibility = cpuStateVisibility;
                    GameProfileCPUStateText.Visibility = cpuStateVisibility;
                    GameProfileCPUStateText.Text = $"{gameProfile.MinCPUState}-{gameProfile.MaxCPUState}%";

                    // FPS Limit
                    GameProfileFPSLimitLabel.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Visibility = fpsLimitVisibility;
                    GameProfileFPSLimitText.Text = gameProfile.FPSLimitEnabled ? $"{gameProfile.FPSLimitValue}" : "Off";

                    // AutoTDP
                    GameProfileAutoTDPLabel.Visibility = autoTDPVisibility;
                    GameProfileAutoTDPText.Visibility = autoTDPVisibility;
                    GameProfileAutoTDPText.Text = gameProfile.AutoTDPEnabled ? $"{gameProfile.AutoTDPTargetFPS}fps" : "Off";

                    // Power Mode
                    GameProfilePowerModeLabel.Visibility = powerModeVisibility;
                    GameProfilePowerModeText.Visibility = powerModeVisibility;
                    GameProfilePowerModeText.Text = GetPowerModeShortName(gameProfile.OSPowerMode);

                    // AMD Features
                    GameProfileAMDLabel.Visibility = amdVisibility;
                    GameProfileAMDText.Visibility = amdVisibility;
                    var gameAmdFeatures = GetAMDFeaturesShortString(gameProfile);
                    GameProfileAMDText.Text = string.IsNullOrEmpty(gameAmdFeatures) ? "Off" : gameAmdFeatures;

                    // HDR
                    GameProfileHDRLabel.Visibility = hdrVisibility;
                    GameProfileHDRText.Visibility = hdrVisibility;
                    GameProfileHDRText.Text = gameProfile.HDREnabled ? "On" : "Off";

                    // Resolution
                    GameProfileResolutionLabel.Visibility = resolutionVisibility;
                    GameProfileResolutionText.Visibility = resolutionVisibility;
                    GameProfileResolutionText.Text = string.IsNullOrEmpty(gameProfile.Resolution) ? "Native" : gameProfile.Resolution;

                    // Sticky TDP
                    GameProfileStickyTDPLabel.Visibility = stickyTDPVisibility;
                    GameProfileStickyTDPText.Visibility = stickyTDPVisibility;
                    GameProfileStickyTDPText.Text = gameProfile.StickyTDPEnabled ? "On" : "Off";
                }
            }

            // Update all saved game profiles display
            UpdateAllGameProfilesDisplay();
        }

        private static string GetPowerModeShortName(int mode)
        {
            switch (mode)
            {
                case 0: return "Efficiency";
                case 1: return "Balanced";
                case 2: return "Performance";
                default: return "Balanced";
            }
        }

        private static string GetLegionModeShortName(int mode)
        {
            switch (mode)
            {
                case 1: return "Quiet";
                case 2: return "Balanced";
                case 3: return "Performance";
                case 255: return "Custom";
                default: return "Balanced";
            }
        }

        /// <summary>
        /// Gets the TDP mode display name from a profile, accounting for custom presets.
        /// </summary>
        private string GetProfileTDPModeName(PerformanceProfile profile)
        {
            // If TDPModeIndex is set and we have custom presets, use the preset name
            if (profile.TDPModeIndex >= 0 && useCustomTDPPresets && tdpPresets != null)
            {
                if (profile.TDPModeIndex < tdpPresets.Count)
                {
                    return tdpPresets[profile.TDPModeIndex].Name;
                }
                else if (profile.TDPModeIndex == tdpPresets.Count)
                {
                    return "Custom"; // The actual Custom mode after all presets
                }
            }
            // Fall back to legacy mode name
            return GetLegionModeShortName(profile.LegionPerformanceMode);
        }

        /// <summary>
        /// Gets the TDPModeComboBox index from a profile, accounting for custom presets.
        /// Returns the index to use for TDPModeComboBox.SelectedIndex.
        /// </summary>
        private int GetProfileTDPModeIndex(PerformanceProfile profile)
        {
            // If TDPModeIndex is set, use it directly (for custom presets)
            if (profile.TDPModeIndex >= 0)
            {
                // Validate the index is still valid with current preset configuration
                int maxIndex = useCustomTDPPresets && tdpPresets != null ? tdpPresets.Count : 3;
                if (profile.TDPModeIndex <= maxIndex)
                {
                    return profile.TDPModeIndex;
                }
            }
            // Fall back to legacy: convert LegionPerformanceMode to index
            int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
            int index = Array.IndexOf(modeValues, profile.LegionPerformanceMode);
            return index >= 0 ? index : 1; // Default to Balanced if not found
        }

        /// <summary>
        /// Initializes CPU State comboboxes with percentage values (5%, 10%, 15%... 100%)
        /// </summary>
        private void InitializeCPUStateComboBoxes()
        {
            MinCPUStateComboBox.Items.Clear();
            MaxCPUStateComboBox.Items.Clear();

            // Add values from 5 to 100 in 5% increments
            for (int i = 5; i <= 100; i += 5)
            {
                MinCPUStateComboBox.Items.Add(new ComboBoxItem { Content = $"{i}%", Tag = i });
                MaxCPUStateComboBox.Items.Add(new ComboBoxItem { Content = $"{i}%", Tag = i });
            }

            // Set defaults: Min=5%, Max=100%
            MinCPUStateComboBox.SelectedIndex = 0; // 5%
            MaxCPUStateComboBox.SelectedIndex = 19; // 100%

            // Enable the comboboxes
            MinCPUStateComboBox.IsEnabled = true;
            MaxCPUStateComboBox.IsEnabled = true;
        }

        /// <summary>
        /// Gets the CPU state percentage value from a ComboBox selection
        /// </summary>
        private int GetSelectedCPUStateValue(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is int value)
            {
                return value;
            }
            // Default values: 100 for max, 5 for min
            return comboBox == MaxCPUStateComboBox ? 100 : 5;
        }

        /// <summary>
        /// Sets the CPU State ComboBox to the specified percentage value
        /// </summary>
        private void SetCPUStateComboBoxValue(ComboBox comboBox, int value)
        {
            // Clamp to valid range and round to nearest 5
            value = Math.Max(5, Math.Min(100, value));
            value = (int)(Math.Round(value / 5.0) * 5);
            if (value < 5) value = 5;

            // Find and select the matching item
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is int itemValue && itemValue == value)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Handler for Min CPU State ComboBox selection change
        /// </summary>
        private void MinCPUStateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (MinCPUStateComboBox == null || MaxCPUStateComboBox == null)
                return;

            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync)
                return;

            int minValue = GetSelectedCPUStateValue(MinCPUStateComboBox);
            int maxValue = GetSelectedCPUStateValue(MaxCPUStateComboBox);

            // Ensure min doesn't exceed max
            if (minValue > maxValue)
            {
                SetCPUStateComboBoxValue(MaxCPUStateComboBox, minValue);
            }

            // Send to helper
            minCPUState?.SetValue(minValue);

            Logger.Info($"Min CPU State changed to {minValue}%");
        }

        /// <summary>
        /// Handler for Max CPU State ComboBox selection change
        /// </summary>
        private void MaxCPUStateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (MinCPUStateComboBox == null || MaxCPUStateComboBox == null)
                return;

            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync)
                return;

            int minValue = GetSelectedCPUStateValue(MinCPUStateComboBox);
            int maxValue = GetSelectedCPUStateValue(MaxCPUStateComboBox);

            // Ensure max doesn't go below min
            if (maxValue < minValue)
            {
                SetCPUStateComboBoxValue(MinCPUStateComboBox, maxValue);
            }

            // Send to helper
            maxCPUState?.SetValue(maxValue);

            // Update CPU Boost toggle enabled state
            UpdateCPUBoostEnabledState();

            Logger.Info($"Max CPU State changed to {maxValue}%");
        }

        /// <summary>
        /// Updates the CPU Boost toggle enabled state based on Max CPU State.
        /// When Max CPU State is below 100%, CPU Boost cannot work (Windows prevents boosting beyond the limit).
        /// </summary>
        private void UpdateCPUBoostEnabledState()
        {
            if (CPUBoostToggle == null || MaxCPUStateComboBox == null) return;

            int maxCPUStateValue = GetSelectedCPUStateValue(MaxCPUStateComboBox);
            bool canBoost = maxCPUStateValue >= 100;

            CPUBoostToggle.IsEnabled = canBoost;

            // If boost is now disabled and was on, turn it off and notify helper
            if (!canBoost && CPUBoostToggle.IsOn)
            {
                CPUBoostToggle.IsOn = false;
                cpuBoost?.SetValue(false);
                Logger.Info("CPU Boost disabled automatically - Max CPU State is below 100%");
            }
        }

        /// <summary>
        /// Gets a short string representation of enabled AMD features for display in profile cards
        /// </summary>
        private static string GetAMDFeaturesShortString(PerformanceProfile profile)
        {
            var features = new List<string>();

            if (profile.FluidMotionFrames) features.Add("AFMF");
            if (profile.RadeonSuperResolution) features.Add("RSR");
            if (profile.ImageSharpening) features.Add("RIS");
            if (profile.RadeonAntiLag) features.Add("AL");
            if (profile.RadeonBoost) features.Add("Boost");
            if (profile.RadeonChill) features.Add("Chill");

            return string.Join(",", features);
        }

        private void UpdateGameProfileCardVisibility()
        {
            bool hasGame = HasValidGame(currentGameName);
            bool powerSourceEnabled = hasGame && GetPerGamePowerSourceProfileEnabled(currentGameName);
            UpdatePowerSourceProfileScopeText();

            if (hasGame)
            {
                GameProfileCard.Visibility = Visibility.Visible;

                if (powerSourceEnabled)
                {
                    GameProfileWithPowerSource.Visibility = Visibility.Visible;
                    GameProfileWithoutPowerSource.Visibility = Visibility.Collapsed;
                    GameProfileTitleWithPower.Text = currentGameName;
                }
                else
                {
                    GameProfileWithPowerSource.Visibility = Visibility.Collapsed;
                    GameProfileWithoutPowerSource.Visibility = Visibility.Visible;
                    GameProfileTitleNoPower.Text = currentGameName;
                }
            }
            else
            {
                GameProfileCard.Visibility = Visibility.Collapsed;
            }
        }

        private List<string> GetAllSavedGameProfiles()
        {
            var gameNames = new HashSet<string>();
            var settings = ApplicationData.Current.LocalSettings;

            // Enumerate all containers looking for game profiles
            foreach (var containerName in settings.Containers.Keys)
            {
                if (containerName.StartsWith("Profile_Game_"))
                {
                    // Extract game name from container key
                    string gameName = containerName.Substring("Profile_Game_".Length);

                    // Remove _AC or _DC suffix if present
                    if (gameName.EndsWith("_AC"))
                    {
                        gameName = gameName.Substring(0, gameName.Length - 3);
                    }
                    else if (gameName.EndsWith("_DC"))
                    {
                        gameName = gameName.Substring(0, gameName.Length - 3);
                    }

                    gameNames.Add(gameName);
                } else
                {
                    Logger.Info("Found no profile that starts with Profile_Game_");
                    Logger.Info(containerName);
                }
            }

            return gameNames.OrderBy(name => name).ToList();
        }

        private void UpdateAllGameProfilesDisplay()
        {
            if (AllGameProfilesContainer == null)
                return;

            // Clear existing game profile cards
            AllGameProfilesContainer.Children.Clear();

            var savedGames = GetAllSavedGameProfiles();

            if (savedGames.Count == 0)
            {
                // Show "No saved game profiles" message
                var noProfilesText = new TextBlock
                {
                    Text = "No saved game profiles yet. Play a game with Per-Game Profiles enabled to create profiles.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                AllGameProfilesContainer.Children.Add(noProfilesText);
                return;
            }

            // Create a grid to display game profiles (2 columns)
            var gridIndex = 0;
            Grid currentRow = null;

            foreach (var gameName in savedGames)
            {
                // Skip current game as it's already displayed above
                if (gameName == currentGameName && HasValidGame(currentGameName))
                    continue;

                var columnIndex = gridIndex % 2;

                // Create new row every 2 items
                if (columnIndex == 0)
                {
                    currentRow = new Grid
                    {
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    AllGameProfilesContainer.Children.Add(currentRow);
                }

                // Load profiles
                var settings = ApplicationData.Current.LocalSettings;
                bool hasAC = settings.Containers.ContainsKey($"Profile_Game_{gameName}_AC");
                bool hasDC = settings.Containers.ContainsKey($"Profile_Game_{gameName}_DC");
                bool hasACDC = hasAC || hasDC;
                bool hasSingle = settings.Containers.ContainsKey($"Profile_Game_{gameName}");
                bool gamePowerSourceSplit = GetPerGamePowerSourceProfileEnabled(gameName);

                Border profileCard = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 42, 26)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 58, 58)),
                    BorderThickness = new Thickness(1)
                };

                var stackPanel = new StackPanel();
                profileCard.Child = stackPanel;

                // Title row with delete button
                var titleGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleText = new TextBlock
                {
                    Text = gameName,
                    FontSize = 13,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(titleText, 0);
                titleGrid.Children.Add(titleText);

                // Delete button
                var deleteButton = new Button
                {
                    Content = "🗑️",
                    FontSize = 12,
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 0, 0)),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = gameName,  // Store game name for delete handler
                    BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(2)
                };
                deleteButton.Click += DeleteProfileButton_Click;
                deleteButton.GotFocus += (s, args) =>
                {
                    deleteButton.BorderBrush = new SolidColorBrush(Windows.UI.Colors.White);
                    deleteButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 50, 50));
                };
                deleteButton.LostFocus += (s, args) =>
                {
                    deleteButton.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
                    deleteButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 0, 0));
                };
                Grid.SetColumn(deleteButton, 1);
                titleGrid.Children.Add(deleteButton);

                stackPanel.Children.Add(titleGrid);
                stackPanel.Children.Add(new TextBlock
                {
                    Text = $"AC/DC split: {(gamePowerSourceSplit ? "On" : "Off")}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    Margin = new Thickness(0, 0, 0, 6)
                });

                if (gamePowerSourceSplit && hasACDC)
                {
                    // Load AC/DC profiles
                    var gameAC = new PerformanceProfile();
                    var gameDC = new PerformanceProfile();
                    if (hasAC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_AC", gameAC);
                    }
                    else if (hasSingle)
                    {
                        LoadProfileFromStorage($"Game_{gameName}", gameAC);
                    }

                    if (hasDC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_DC", gameDC);
                    }
                    else if (hasSingle)
                    {
                        LoadProfileFromStorage($"Game_{gameName}", gameDC);
                    }

                    // Create AC/DC comparison grid
                    var acDcGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    // Add rows dynamically based on enabled settings
                    for (int i = 0; i < 20; i++) // Max rows
                        acDcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    int rowIndex = 0;

                    // Headers
                    AddTextBlock(acDcGrid, rowIndex, 1, "AC", 10, "#FFD700", horizontalAlignment: HorizontalAlignment.Center);
                    AddTextBlock(acDcGrid, rowIndex, 2, "DC", 10, "#FF6B6B", horizontalAlignment: HorizontalAlignment.Center);
                    rowIndex++;

                    // TDP Mode (Legion only)
                    if (legionGoDetected?.Value == true && SaveTDP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Mode", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, GetProfileTDPModeName(gameAC), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, GetProfileTDPModeName(gameDC), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // TDP
                    if (SaveTDP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "TDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, $"{gameAC.TDP}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, $"{gameDC.TDP}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;

                        // TDP Boost (saved with TDP)
                        AddTextBlock(acDcGrid, rowIndex, 0, "TDP Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.TDPBoostEnabled ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.TDPBoostEnabled ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Boost
                    if (SaveCPUBoost)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.CPUBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.CPUBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // EPP
                    if (SaveCPUEPP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "EPP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, $"{gameAC.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, $"{gameDC.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // CPU State
                    if (SaveCPUState)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "CPU St", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, $"{gameAC.MinCPUState}-{gameAC.MaxCPUState}%", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, $"{gameDC.MinCPUState}-{gameDC.MaxCPUState}%", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // FPS Limit (if enabled)
                    if (SaveFPSLimit)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "FPS Lim", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.FPSLimitEnabled ? $"{gameAC.FPSLimitValue}" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.FPSLimitEnabled ? $"{gameDC.FPSLimitValue}" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // AutoTDP (if enabled)
                    if (SaveAutoTDP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "AutoTDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.AutoTDPEnabled ? $"{gameAC.AutoTDPTargetFPS}fps" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.AutoTDPEnabled ? $"{gameDC.AutoTDPTargetFPS}fps" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Power Mode (if enabled)
                    if (SaveOSPowerMode)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Power", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, GetPowerModeShortName(gameAC.OSPowerMode), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, GetPowerModeShortName(gameDC.OSPowerMode), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // AMD Features (if enabled)
                    if (SaveAMDFeatures)
                    {
                        // Build AMD features string for AC profile
                        var acAmdFeatures = GetAMDFeaturesShortString(gameAC);
                        var dcAmdFeatures = GetAMDFeaturesShortString(gameDC);

                        if (!string.IsNullOrEmpty(acAmdFeatures) || !string.IsNullOrEmpty(dcAmdFeatures))
                        {
                            AddTextBlock(acDcGrid, rowIndex, 0, "AMD", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                            AddTextBlock(acDcGrid, rowIndex, 1, string.IsNullOrEmpty(acAmdFeatures) ? "Off" : acAmdFeatures, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            AddTextBlock(acDcGrid, rowIndex, 2, string.IsNullOrEmpty(dcAmdFeatures) ? "Off" : dcAmdFeatures, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                            rowIndex++;
                        }
                    }

                    // HDR (if enabled)
                    if (SaveHDR)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "HDR", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.HDREnabled ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.HDREnabled ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Resolution (if enabled)
                    if (SaveResolution && (!string.IsNullOrEmpty(gameAC.Resolution) || !string.IsNullOrEmpty(gameDC.Resolution)))
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Res", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, string.IsNullOrEmpty(gameAC.Resolution) ? "-" : gameAC.Resolution, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, string.IsNullOrEmpty(gameDC.Resolution) ? "-" : gameDC.Resolution, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    // Sticky TDP (if enabled)
                    if (SaveStickyTDP)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "Sticky", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.StickyTDPEnabled ? $"{gameAC.StickyTDPInterval}s" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.StickyTDPEnabled ? $"{gameDC.StickyTDPInterval}s" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        rowIndex++;
                    }

                    stackPanel.Children.Add(acDcGrid);
                }
                else
                {
                    // Load single profile
                    var game = new PerformanceProfile();
                    if (hasSingle)
                    {
                        LoadProfileFromStorage($"Game_{gameName}", game);
                    }
                    else if (hasAC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_AC", game);
                    }
                    else if (hasDC)
                    {
                        LoadProfileFromStorage($"Game_{gameName}_DC", game);
                    }
                    else
                    {
                        continue;
                    }

                    // Create simple grid
                    var singleGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    // Add rows dynamically based on enabled settings
                    for (int i = 0; i < 20; i++) // Max rows
                        singleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    int rowIndex = 0;

                    // TDP Mode (Legion only)
                    if (legionGoDetected?.Value == true && SaveTDP)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "TDP Mode", 10, "#AAAAAA");
                        AddTextBlock(singleGrid, rowIndex, 1, GetProfileTDPModeName(game), 10, "#FFFFFF");
                        rowIndex++;
                    }

                    // TDP
                    if (SaveTDP)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "TDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, $"{game.TDP}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;

                        // TDP Boost (saved with TDP)
                        AddTextBlock(singleGrid, rowIndex, 0, "TDP Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.TDPBoostEnabled ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // CPU Boost
                    if (SaveCPUBoost)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "CPU Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.CPUBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // CPU EPP
                    if (SaveCPUEPP)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "CPU EPP", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, $"{game.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // CPU State
                    if (SaveCPUState)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "CPU State", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, $"{game.MinCPUState}-{game.MaxCPUState}%", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // FPS Limit (if enabled)
                    if (SaveFPSLimit)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "FPS Limit", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.FPSLimitEnabled ? $"{game.FPSLimitValue}" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // AutoTDP (if enabled)
                    if (SaveAutoTDP)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "AutoTDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.AutoTDPEnabled ? $"{game.AutoTDPTargetFPS}fps" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // Power Mode (if enabled)
                    if (SaveOSPowerMode)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "Power Mode", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, GetPowerModeShortName(game.OSPowerMode), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // AMD Features (if enabled)
                    if (SaveAMDFeatures)
                    {
                        var amdFeatures = GetAMDFeaturesShortString(game);
                        AddTextBlock(singleGrid, rowIndex, 0, "AMD", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, string.IsNullOrEmpty(amdFeatures) ? "Off" : amdFeatures, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // HDR (if enabled)
                    if (SaveHDR)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "HDR", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.HDREnabled ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // Resolution (if enabled)
                    if (SaveResolution && !string.IsNullOrEmpty(game.Resolution))
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "Resolution", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.Resolution, 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    // Sticky TDP (if enabled)
                    if (SaveStickyTDP)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "Sticky TDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.StickyTDPEnabled ? $"{game.StickyTDPInterval}s" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
                        rowIndex++;
                    }

                    stackPanel.Children.Add(singleGrid);
                }

                Grid.SetColumn(profileCard, columnIndex * 2);
                currentRow?.Children.Add(profileCard);

                gridIndex++;
            }
        }

        private void AddTextBlock(Grid grid, int row, int column, string text, int fontSize, string colorHex,
            Thickness? margin = null, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(ParseColor(colorHex)),
                Margin = margin ?? new Thickness(0),
                HorizontalAlignment = horizontalAlignment
            };
            Grid.SetRow(textBlock, row);
            Grid.SetColumn(textBlock, column);
            grid.Children.Add(textBlock);
        }

        private Windows.UI.Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            return Windows.UI.Colors.White;
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string gameName)
            {
                Logger.Info($"Delete button clicked for game: {gameName}");
                DeleteGameProfile(gameName);
            }
        }

        private void CleanupInvalidProfiles()
        {
            var settings = ApplicationData.Current.LocalSettings;
            var profilesToDelete = new List<string>();
            var perGameSplitKeysToDelete = new List<string>();

            // Find all containers with invalid game names (case-insensitive check)
            foreach (var containerName in settings.Containers.Keys)
            {
                if (containerName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    profilesToDelete.Add(containerName);
                }
            }

            // Find all per-game split settings with invalid game names
            foreach (var key in settings.Values.Keys)
            {
                if (key.StartsWith(PerGamePowerSourceProfileSettingPrefix, StringComparison.OrdinalIgnoreCase) &&
                    key.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    perGameSplitKeysToDelete.Add(key);
                }
            }

            // Delete invalid profiles
            foreach (var containerName in profilesToDelete)
            {
                settings.DeleteContainer(containerName);
                Logger.Info($"Cleaned up invalid profile container: {containerName}");
            }

            foreach (var key in perGameSplitKeysToDelete)
            {
                settings.Values.Remove(key);
                Logger.Info($"Cleaned up invalid per-game power split key: {key}");
            }

            if (profilesToDelete.Count > 0)
            {
                Logger.Info($"Cleaned up {profilesToDelete.Count} invalid profile(s)");
            }
        }

        private void DeleteGameProfile(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return;

            var settings = ApplicationData.Current.LocalSettings;
            bool profileDeleted = false;

            // Try to delete single profile
            if (settings.Containers.ContainsKey($"Profile_Game_{gameName}"))
            {
                settings.DeleteContainer($"Profile_Game_{gameName}");
                Logger.Info($"Deleted game profile for {gameName}");
                profileDeleted = true;
            }

            // Try to delete AC/DC profiles
            if (settings.Containers.ContainsKey($"Profile_Game_{gameName}_AC"))
            {
                settings.DeleteContainer($"Profile_Game_{gameName}_AC");
                Logger.Info($"Deleted game AC profile for {gameName}");
                profileDeleted = true;
            }

            if (settings.Containers.ContainsKey($"Profile_Game_{gameName}_DC"))
            {
                settings.DeleteContainer($"Profile_Game_{gameName}_DC");
                Logger.Info($"Deleted game DC profile for {gameName}");
                profileDeleted = true;
            }

            string splitKey = GetPerGamePowerSourceProfileSettingKey(gameName);
            if (settings.Values.ContainsKey(splitKey))
            {
                settings.Values.Remove(splitKey);
                Logger.Info($"Deleted per-game power split setting for {gameName}");
            }

            if (profileDeleted)
            {
                // If we deleted the current game's profile, disable per-game toggle
                if (gameName == currentGameName && PerGameProfileToggle?.IsOn == true)
                {
                    Logger.Info($"Deleted profile for current game {gameName}, disabling per-game toggle");
                    PerGameProfileToggle.IsOn = false;
                }

                // Refresh the display
                UpdateProfileDisplay();
            }
        }

        private void LoadProfileCustomizationSettings()
        {
            isLoadingProfileSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load values from settings
                _saveTDP = settings.Values.ContainsKey("ProfileSaveTDP") ? (bool)settings.Values["ProfileSaveTDP"] : true;
                _saveCPUBoost = settings.Values.ContainsKey("ProfileSaveCPUBoost") ? (bool)settings.Values["ProfileSaveCPUBoost"] : true;
                _saveCPUEPP = settings.Values.ContainsKey("ProfileSaveCPUEPP") ? (bool)settings.Values["ProfileSaveCPUEPP"] : true;
                _saveCPUState = settings.Values.ContainsKey("ProfileSaveCPUState") ? (bool)settings.Values["ProfileSaveCPUState"] : true;
                _saveAMDFeatures = settings.Values.ContainsKey("ProfileSaveAMDFeatures") ? (bool)settings.Values["ProfileSaveAMDFeatures"] : false;
                _saveFPSLimit = settings.Values.ContainsKey("ProfileSaveFPSLimit") ? (bool)settings.Values["ProfileSaveFPSLimit"] : true;
                _saveAutoTDP = settings.Values.ContainsKey("ProfileSaveAutoTDP") ? (bool)settings.Values["ProfileSaveAutoTDP"] : true;
                _saveOSPowerMode = settings.Values.ContainsKey("ProfileSaveOSPowerMode") ? (bool)settings.Values["ProfileSaveOSPowerMode"] : true;
                // HDR and Resolution - check for new separate settings first, fall back to combined setting for migration
                if (settings.Values.ContainsKey("ProfileSaveHDR"))
                {
                    _saveHDR = (bool)settings.Values["ProfileSaveHDR"];
                }
                else if (settings.Values.ContainsKey("ProfileSaveHDRResolution"))
                {
                    _saveHDR = (bool)settings.Values["ProfileSaveHDRResolution"]; // Migrate from combined setting
                }
                else
                {
                    _saveHDR = false;
                }

                if (settings.Values.ContainsKey("ProfileSaveResolution"))
                {
                    _saveResolution = (bool)settings.Values["ProfileSaveResolution"];
                }
                else if (settings.Values.ContainsKey("ProfileSaveHDRResolution"))
                {
                    _saveResolution = (bool)settings.Values["ProfileSaveHDRResolution"]; // Migrate from combined setting
                }
                else
                {
                    _saveResolution = false;
                }

                _saveRefreshRate = settings.Values.ContainsKey("ProfileSaveRefreshRate") ? (bool)settings.Values["ProfileSaveRefreshRate"] : false;
                _saveStickyTDP = settings.Values.ContainsKey("ProfileSaveStickyTDP") ? (bool)settings.Values["ProfileSaveStickyTDP"] : false;
                _saveOverlayLevel = settings.Values.ContainsKey("ProfileSaveOverlayLevel") ? (bool)settings.Values["ProfileSaveOverlayLevel"] : false;
                _saveCPUAffinity = settings.Values.ContainsKey("ProfileSaveCPUAffinity") ? (bool)settings.Values["ProfileSaveCPUAffinity"] : false;

                // Update UI checkboxes
                if (ProfileSaveTDPCheckBox != null) ProfileSaveTDPCheckBox.IsChecked = _saveTDP;
                if (ProfileSaveCPUBoostCheckBox != null) ProfileSaveCPUBoostCheckBox.IsChecked = _saveCPUBoost;
                if (ProfileSaveCPUEPPCheckBox != null) ProfileSaveCPUEPPCheckBox.IsChecked = _saveCPUEPP;
                if (ProfileSaveCPUStateCheckBox != null) ProfileSaveCPUStateCheckBox.IsChecked = _saveCPUState;
                if (ProfileSaveAMDFeaturesCheckBox != null) ProfileSaveAMDFeaturesCheckBox.IsChecked = _saveAMDFeatures;
                if (ProfileSaveFPSLimitCheckBox != null) ProfileSaveFPSLimitCheckBox.IsChecked = _saveFPSLimit;
                if (ProfileSaveAutoTDPCheckBox != null) ProfileSaveAutoTDPCheckBox.IsChecked = _saveAutoTDP;
                if (ProfileSaveOSPowerModeCheckBox != null) ProfileSaveOSPowerModeCheckBox.IsChecked = _saveOSPowerMode;
                if (ProfileSaveHDRCheckBox != null) ProfileSaveHDRCheckBox.IsChecked = _saveHDR;
                if (ProfileSaveResolutionCheckBox != null) ProfileSaveResolutionCheckBox.IsChecked = _saveResolution;
                if (ProfileSaveRefreshRateCheckBox != null) ProfileSaveRefreshRateCheckBox.IsChecked = _saveRefreshRate;
                if (ProfileSaveStickyTDPCheckBox != null) ProfileSaveStickyTDPCheckBox.IsChecked = _saveStickyTDP;
                if (ProfileSaveOverlayLevelCheckBox != null) ProfileSaveOverlayLevelCheckBox.IsChecked = _saveOverlayLevel;
                if (ProfileSaveCPUAffinityCheckBox != null) ProfileSaveCPUAffinityCheckBox.IsChecked = _saveCPUAffinity;
            }
            finally
            {
                isLoadingProfileSettings = false;
            }
        }

        private void SaveProfileCustomizationSettings()
        {
            if (isLoadingProfileSettings) return;

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ProfileSaveTDP"] = ProfileSaveTDPCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveCPUBoost"] = ProfileSaveCPUBoostCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveCPUEPP"] = ProfileSaveCPUEPPCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveCPUState"] = ProfileSaveCPUStateCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveAMDFeatures"] = ProfileSaveAMDFeaturesCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveFPSLimit"] = ProfileSaveFPSLimitCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveAutoTDP"] = ProfileSaveAutoTDPCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveOSPowerMode"] = ProfileSaveOSPowerModeCheckBox?.IsChecked ?? true;
            settings.Values["ProfileSaveHDR"] = ProfileSaveHDRCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveResolution"] = ProfileSaveResolutionCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveRefreshRate"] = ProfileSaveRefreshRateCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveStickyTDP"] = ProfileSaveStickyTDPCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveOverlayLevel"] = ProfileSaveOverlayLevelCheckBox?.IsChecked ?? false;
            settings.Values["ProfileSaveCPUAffinity"] = ProfileSaveCPUAffinityCheckBox?.IsChecked ?? false;
        }

        private void ProfileSettingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingProfileSettings) return;

            // Update backing fields from UI checkboxes
            SyncProfileSettingsBackingFields();
            SaveProfileCustomizationSettings();
            Logger.Info($"Profile customization settings updated");
        }

        private void ProfileSettingsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingProfileSettings) return;

            // Update backing fields from UI checkboxes
            SyncProfileSettingsBackingFields();
            SaveProfileCustomizationSettings();
            Logger.Info($"Profile customization settings updated");
        }

        /// <summary>
        /// Sync backing fields from UI checkboxes. Called when checkboxes change.
        /// This ensures the backing fields are always in sync with the UI.
        /// </summary>
        private void SyncProfileSettingsBackingFields()
        {
            _saveTDP = ProfileSaveTDPCheckBox?.IsChecked ?? true;
            _saveCPUBoost = ProfileSaveCPUBoostCheckBox?.IsChecked ?? true;
            _saveCPUEPP = ProfileSaveCPUEPPCheckBox?.IsChecked ?? true;
            _saveCPUState = ProfileSaveCPUStateCheckBox?.IsChecked ?? true;
            _saveAMDFeatures = ProfileSaveAMDFeaturesCheckBox?.IsChecked ?? false;
            _saveFPSLimit = ProfileSaveFPSLimitCheckBox?.IsChecked ?? true;
            _saveAutoTDP = ProfileSaveAutoTDPCheckBox?.IsChecked ?? true;
            _saveOSPowerMode = ProfileSaveOSPowerModeCheckBox?.IsChecked ?? true;
            _saveHDR = ProfileSaveHDRCheckBox?.IsChecked ?? false;
            _saveResolution = ProfileSaveResolutionCheckBox?.IsChecked ?? false;
            _saveRefreshRate = ProfileSaveRefreshRateCheckBox?.IsChecked ?? false;
            _saveStickyTDP = ProfileSaveStickyTDPCheckBox?.IsChecked ?? false;
            _saveOverlayLevel = ProfileSaveOverlayLevelCheckBox?.IsChecked ?? false;
            _saveCPUAffinity = ProfileSaveCPUAffinityCheckBox?.IsChecked ?? false;
        }

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";

                // Hide all sections
                QuickSettingsScrollViewer.Visibility = Visibility.Collapsed;
                PerformanceScrollViewer.Visibility = Visibility.Collapsed;
                GameScrollViewer.Visibility = Visibility.Collapsed;
                AMDScrollViewer.Visibility = Visibility.Collapsed;
                ScalingScrollViewer.Visibility = Visibility.Collapsed;
                LegionScrollViewer.Visibility = Visibility.Collapsed;
                GPDScrollViewer.Visibility = Visibility.Collapsed;
                SystemScrollViewer.Visibility = Visibility.Collapsed;

                // Stop fan curve updates when leaving Legion tab (will be re-enabled if Legion is selected)
                legionFanCurveVisible?.SetVisible(false);

                // Stop DAService status polling when leaving Legion tab
                daServiceStatusTimer?.Stop();

                // Show selected section and scroll to top
                switch (tag)
                {
                    case "Quick":
                        QuickSettingsScrollViewer.Visibility = Visibility.Visible;
                        QuickSettingsScrollViewer.ChangeView(null, 0, null, true);
                        UpdateQuickSettingsTileStates();
                        break;
                    case "Performance":
                        PerformanceScrollViewer.Visibility = Visibility.Visible;
                        PerformanceScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "Game":
                        GameScrollViewer.Visibility = Visibility.Visible;
                        GameScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "AMD":
                        AMDScrollViewer.Visibility = Visibility.Visible;
                        AMDScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "Scaling":
                        ScalingScrollViewer.Visibility = Visibility.Visible;
                        ScalingScrollViewer.ChangeView(null, 0, null, true);
                        UpdateLosslessScalingStatus();
                        break;
                    case "Legion":
                        LegionScrollViewer.Visibility = Visibility.Visible;
                        LegionScrollViewer.ChangeView(null, 0, null, true);
                        // Update fan curve visibility when switching to Legion tab
                        legionFanCurveVisible?.SetVisible(isFanCurveExpanded);
                        // Start DAService status polling when on Legion tab
                        if (daServiceStatusTimer != null)
                        {
                            UpdateDAServiceStatus(); // Immediate update
                            daServiceStatusTimer.Start();
                        }
                        // Request ViGEmBus status for button remap section
                        RequestViGEmBusStatus();
                        // Force remap UI refresh when Legion tab becomes active.
                        RefreshLegionEnhancedRemapUi();
                        break;
                    case "GPD":
                        GPDScrollViewer.Visibility = Visibility.Visible;
                        GPDScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "System":
                        SystemScrollViewer.Visibility = Visibility.Visible;
                        SystemScrollViewer.ChangeView(null, 0, null, true);
                        RequestControllerEmulationDriverStatus();
                        break;
                }

                // Re-apply theme to newly visible tab (StaticResources don't update dynamically)
                // Defer with delay to ensure visual tree is fully loaded
                if (currentThemeName != "Default")
                {
                    _ = ApplyThemeToCurrentTabAsync();
                }
            }
        }

        private async Task ApplyThemeToCurrentTabAsync()
        {
            // Wait for visual tree to fully load
            await Task.Delay(50);
            ApplyThemeToCurrentTab();
        }

        private void ApplyThemeToCurrentTab()
        {
            if (!WidgetThemes.TryGetValue(currentThemeName, out var theme)) return;

            var cardBgBrush = new SolidColorBrush(theme.CardBackground);
            var cardBorderBrush = new SolidColorBrush(theme.CardBorder);
            var accentBrush = new SolidColorBrush(theme.AccentColor);
            var textSecondaryBrush = new SolidColorBrush(theme.TextSecondary);

            // Apply to all scroll viewers (only visible ones will have loaded content)
            ApplyThemeToVisualTree(QuickSettingsScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(PerformanceScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(GameScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(AMDScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(ScalingScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(LegionScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(SystemScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
        }

        private void GamingWidget_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Handle LT (Left Trigger) and RT (Right Trigger) for tab navigation
            // Using PreviewKeyDown to intercept before ScrollViewer handles it
            if (e.Key == VirtualKey.GamepadLeftTrigger)
            {
                NavigateToPreviousTab();
                e.Handled = true;
                return;
            }
            else if (e.Key == VirtualKey.GamepadRightTrigger)
            {
                NavigateToNextTab();
                e.Handled = true;
                return;
            }
            // Handle D-pad down from nav items to focus content area (for overflow menu items)
            else if (e.Key == VirtualKey.GamepadDPadDown)
            {
                var focusedElement = FocusManager.GetFocusedElement() as FrameworkElement;
                // Check if focus is on a nav RadioButton or within the nav area
                if (focusedElement != null && IsInNavigationArea(focusedElement))
                {
                    // Mark as handled immediately to prevent default XY navigation
                    e.Handled = true;

                    // Use TryMoveFocus to move to the first focusable element downward
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down);
                }
            }
        }

        private bool IsInNavigationArea(FrameworkElement element)
        {
            // Check if any of our nav items has focus
            // This works regardless of whether the item is in the main bar or overflow menu
            if (QuickNavItem.FocusState != FocusState.Unfocused) return true;
            if (PerformanceNavItem.FocusState != FocusState.Unfocused) return true;
            if (ProfilesNavItem.FocusState != FocusState.Unfocused) return true;
            if (GraphicsNavItem.FocusState != FocusState.Unfocused) return true;
            if (ScalingNavItem.FocusState != FocusState.Unfocused) return true;
            if (LegionNavItem.FocusState != FocusState.Unfocused) return true;
            if (GPDNavItem.FocusState != FocusState.Unfocused) return true;
            if (SystemNavItem.FocusState != FocusState.Unfocused) return true;

            // Fallback: walk visual tree for other nav-related elements
            var current = element;
            while (current != null)
            {
                // Check if we're in the nav panel
                if (current == MainNavPanel)
                    return true;
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return false;
        }

        private void NavigateToPreviousTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            // Find currently checked item
            var currentItem = visibleItems.FirstOrDefault(rb => rb.IsChecked == true);
            int currentIndex = currentItem != null ? visibleItems.IndexOf(currentItem) : 0;

            if (currentIndex > 0)
            {
                visibleItems[currentIndex - 1].IsChecked = true;
            }
            else
            {
                // Wrap around to last tab
                visibleItems[visibleItems.Count - 1].IsChecked = true;
            }
        }

        private void NavigateToNextTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            // Find currently checked item
            var currentItem = visibleItems.FirstOrDefault(rb => rb.IsChecked == true);
            int currentIndex = currentItem != null ? visibleItems.IndexOf(currentItem) : 0;

            if (currentIndex < visibleItems.Count - 1)
            {
                visibleItems[currentIndex + 1].IsChecked = true;
            }
            else
            {
                // Wrap around to first tab
                visibleItems[0].IsChecked = true;
            }
        }

        private List<RadioButton> GetVisibleNavigationItems()
        {
            var visibleItems = new List<RadioButton>();
            foreach (var item in MainNavPanel.Children)
            {
                if (item is RadioButton radioButton && radioButton.Visibility == Visibility.Visible)
                {
                    visibleItems.Add(radioButton);
                }
            }
            return visibleItems;
        }

        private void GamingWidget_Unloaded(object sender, RoutedEventArgs e)
        {
            // Set flag immediately to prevent any pending async operations from updating UI
            isUnloading = true;

            Logger.Info($"GamingWidget_Unloaded called. Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, Pipe connected: {App.IsConnected}");

            // Unsubscribe from power source changes
            PowerManager.PowerSupplyStatusChanged -= PowerManager_PowerSourceChanged;
            if (PowerSourceProfileToggle != null)
            {
                PowerSourceProfileToggle.Toggled -= PowerSourceProfileToggle_Toggled;
            }

            if (widget != null)
            {
                widget.RequestedThemeChanged -= GamingWidget_RequestedThemeChanged;
                widget.SettingsClicked -= GamingWidget_SettingsClicked;
                widget.VisibleChanged -= GamingWidget_VisibleChanged;
                widget.GameBarDisplayModeChanged -= GamingWidget_GameBarDisplayModeChanged;
            }

            // Stop Sticky TDP timer
            StopStickyTDPTimer();
            if (stickyTDPTimer != null)
            {
                stickyTDPTimer.Tick -= StickyTDPTimer_Tick;
                stickyTDPTimer = null;
            }

            // Stop power source TDP reapply timer
            if (powerSourceTdpReapplyTimer != null)
            {
                powerSourceTdpReapplyTimer.Stop();
                powerSourceTdpReapplyTimer = null;
            }

            // Stop reconnection timeout timer
            StopReconnectionTimeoutTimer();

            // Unregister this instance as the active widget
            Logger.Info("Unregistering this GamingWidget instance as the active widget.");
            App.UnregisterActiveGamingWidget(this);
            Logger.Info("GamingWidget instance unregistered.");

            // Unsubscribe from Lossless Scaling property changes
            if (losslessScalingInstalled != null)
            {
                losslessScalingInstalled.PropertyChanged -= LosslessScalingStatus_PropertyChanged;
            }
            if (losslessScalingRunning != null)
            {
                losslessScalingRunning.PropertyChanged -= LosslessScalingStatus_PropertyChanged;
            }
            Logger.Info("Event handlers unregistered.");

            // Clean up properties (stop debounce timers, unregister slider events)
            Logger.Info("Cleaning up properties...");
            properties.Cleanup();
            Logger.Info("Properties cleaned up.");

            // Clean up widget activity - capture to local var to avoid race condition
            var activity = widgetActivity;
            if (activity != null)
            {
                Logger.Info("Completing widget activity during page unload.");
                try
                {
                    activity.Complete();
                    Logger.Info("Widget activity completed and disposed.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing widget activity during unload: {ex.Message}");
                }
                finally
                {
                    widgetActivity = null;
                }
            }
            else
            {
                Logger.Info("No widget activity to clean up during unload.");
            }

            // Reset Quick Settings initialized flag so next instance starts fresh
            quickSettingsInitialized = false;

            Logger.Info("GamingWidget_Unloaded completed.");
        }

        public void OnDeactivated()
        {
            Logger.Info($"=== GamingWidget.OnDeactivated START === Instance hash: {this.GetHashCode()}");
            try
            {
                // Must run on UI thread since DispatcherTimer is UI-bound
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        properties.StopPendingUpdates();
                        Logger.Info("Pending updates stopped.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error stopping pending updates: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during deactivation: {ex.Message}");
            }
        }

        private bool chillFPSHandlersRegistered = false;

        private void RegisterChillFPSHandlers()
        {
            if (!chillFPSHandlersRegistered)
            {
                Logger.Info("Registering Chill FPS PropertyChanged handlers after sync...");
                amdRadeonChillMinFPSProperty.PropertyChanged += AmdRadeonChillFPSChanged;
                amdRadeonChillMaxFPSProperty.PropertyChanged += AmdRadeonChillFPSChanged;
                chillFPSHandlersRegistered = true;
                Logger.Info("Chill FPS handlers registered.");
            }
        }

        private void AmdRadeonChillFPSChanged(object sender, PropertyChangedEventArgs e)
        {
            // Only notify if both properties are initialized to avoid crash during sync
            // The binding will evaluate RadeonChillOnText which accesses both properties
            if (amdRadeonChillMinFPSProperty != null && amdRadeonChillMaxFPSProperty != null)
            {
                try
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadeonChillOnText)));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in AmdRadeonChillFPSChanged: {ex.Message}");
                }
            }
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            Logger.Info("=== OnNavigatedTo START ===");
            Logger.Info($"Parameter type: {e.Parameter?.GetType().FullName ?? "null"}");
            Logger.Info($"Current state - Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, Pipe connected: {App.IsConnected}");

            base.OnNavigatedTo(e);

            // Show loading banner immediately to give user feedback during initialization
            ShowConnectionBanner(BannerState.Loading);

            // Yield to UI thread to allow banner to render before async operations block
            await Task.Yield();

            // Register this instance as the active widget to handle AppService messages
            Logger.Info("Registering this GamingWidget instance as the active widget.");
            App.RegisterActiveGamingWidget(this);
            Logger.Info("GamingWidget instance registered as active.");

            //while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    await Task.Delay(500);
            //}

            Logger.Info("Creating theme brushes...");
            widgetDarkThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 40, 44));
            widgetLightThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

            // Load saved theme preference
            LoadThemeSetting();

            // Only update widget if new parameter is an XboxGameBarWidget, preserve existing otherwise
            if (e.Parameter is XboxGameBarWidget newWidget)
            {
                widget = newWidget;
            }

            if (widget != null)
            {
                Logger.Info($"Running as a Xbox Game Bar widget. Widget type: {widget.GetType().FullName}");

                // Update max window size at runtime to override any cached manifest values
                widget.MaxWindowSize = new Windows.Foundation.Size(900, 1080);

                Logger.Info("Calling widget.CenterWindowAsync()...");
                await widget.CenterWindowAsync();
                Logger.Info("widget.CenterWindowAsync() completed.");

                Logger.Info("Registering widget event handlers (RequestedThemeChanged, SettingsClicked, VisibleChanged, GameBarDisplayModeChanged)...");
                widget.RequestedThemeChanged += GamingWidget_RequestedThemeChanged;
                widget.SettingsClicked += GamingWidget_SettingsClicked;
                widget.VisibleChanged += GamingWidget_VisibleChanged;
                widget.GameBarDisplayModeChanged += GamingWidget_GameBarDisplayModeChanged;
                Logger.Info("Widget event handlers registered.");

                UpdateGameBarForegroundSignal("OnNavigatedTo");

                // Initialize notification manager for profile notifications
                notificationManager = new XboxGameBarWidgetNotificationManager(widget);
                Logger.Info("Widget notification manager initialized.");

                // Create widget activity if we have a widget but no activity yet
                if (widgetActivity == null)
                {
                    Logger.Info("Widget is available but activity not created yet. Creating now.");
                    await CreateWidgetActivity();
                }
                else
                {
                    Logger.Info($"WidgetActivity already exists, skipping creation.");
                }

                // Create app target tracker if not already created
                if (appTargetTracker == null)
                {
                    Logger.Info("AppTargetTracker is null, creating now.");
                    await CreateAppTargetTracker();
                }
                else
                {
                    Logger.Info("AppTargetTracker already exists, skipping creation.");
                }

                // Initialize hotkey watchers for controller button combos
                InitializeHotkeyWatchers();
            }
            else
            {
                Logger.Info("XboxGameBarWidget not available, probably running as an app instead of widget.");
            }

            if (!App.IsConnected && ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                Logger.Info("Not connected to helper. Launching full trust process.");

                // Launch helper with guards (checks heartbeat, enforces rate limiting)
                await LaunchHelperWithGuardsAsync("OnNavigatedTo - initial connection");
            }
            else
            {
                Logger.Info($"Not launching full trust process. App.IsConnected: {App.IsConnected}, FullTrustAppContract present: {ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0)}");

                // If connection already exists, sync properties
                if (App.IsConnected)
                {
                    Logger.Info("Already connected to helper via pipe.");

                    // Hide connection status banner since we're connected
                    HideConnectionBanner();
                    Logger.Info("Connection status banner hidden - already connected.");

                    // Sync properties since we're already connected
                    // The helper is the source of truth for hardware state (TDP mode, etc.)
                    Logger.Info("Syncing properties with helper since connection already exists...");
                    try
                    {
                        isApplyingHelperUpdate = true;

                        // Accept helper's LegionPerformanceMode as-is - helper is source of truth

                        // Skip OS Power Mode sync if profile has it saved
                        // This prevents sync from overwriting profile-loaded OS Power Mode with hardware state
                        if (SaveOSPowerMode && osPowerMode != null)
                        {
                            osPowerMode.SkipSync = true;
                            Logger.Info("OSPowerMode sync will be skipped - profile has OS Power Mode saved");
                        }

                        await properties.Sync();
                        Logger.Info("Property sync completed.");

                        // Update Legion Controller battery display after sync (values come from helper)
                        UpdateLegionControllerBatteryDisplay();
                        UpdateLegionControllerVidPidDisplay();

                        // Update FPS Limit controls based on RTSS installed status
                        UpdateFPSLimitControls();

                        // Register Chill FPS handlers after first sync to prevent crash
                        RegisterChillFPSHandlers();
                    }
                    finally
                    {
                        isApplyingHelperUpdate = false;
                    }

                    // Stop any pending slider updates from the sync
                    properties.StopPendingUpdates();

                    // Re-enable OS Power Mode sync for future syncs
                    if (osPowerMode != null)
                    {
                        osPowerMode.SkipSync = false;
                    }

                    // Don't apply profile TDP mode to helper - helper is source of truth
                    // Helper already has correct mode from its own profile

                    // Sync TDPModeComboBox with helper's LegionPerformanceMode
                    if (legionGoDetected?.Value == true && LegionPerformanceModeComboBox != null && TDPModeComboBox != null)
                    {
                        if (TDPModeComboBox.SelectedIndex != LegionPerformanceModeComboBox.SelectedIndex)
                        {
                            Logger.Info($"Syncing TDPModeComboBox to match helper's mode: index {LegionPerformanceModeComboBox.SelectedIndex}");
                            lastTDPModeIndex = LegionPerformanceModeComboBox.SelectedIndex;
                            TDPModeComboBox.SelectedIndex = LegionPerformanceModeComboBox.SelectedIndex;
                        }

                        // Initialize savedCustomTDP from helper's synced TDP value
                        if (IsCustomTdpModeIndex(LegionPerformanceModeComboBox.SelectedIndex) && tdp != null)
                        {
                            savedCustomTDP = tdp.Value;
                            Logger.Info($"Initialized savedCustomTDP from helper's TDP: {savedCustomTDP}W");
                        }
                    }

                    // Update TDP slider to show helper's actual TDP value
                    if (tdp != null && TDPSlider != null)
                    {
                        double helperTDP = tdp.Value;
                        if (Math.Abs(TDPSlider.Value - helperTDP) > 0.5)
                        {
                            Logger.Info($"Updating TDP slider from stale {TDPSlider.Value}W to helper's {helperTDP}W");
                            TDPSlider.Value = helperTDP;
                        }
                    }

                    // Update TDP display text and enabled state based on current mode
                    UpdateTDPSliderEnabledState();
                    Logger.Info($"Updated TDP slider enabled state after sync");

                    // Update profile display now that legionGoDetected has been synced from helper
                    // This ensures TDP Mode shows in Profiles tab on fresh start
                    UpdateProfileDisplay();
                    RefreshLegionEnhancedRemapUi();
                    Logger.Info("Profile display updated after sync - legionGoDetected=" + (legionGoDetected?.Value.ToString() ?? "null"));

                    // Clear initial sync flag - profile is loaded and applied, user changes should now save
                    // Add a small delay to let any pending ValueChanged events settle first
                    await Task.Delay(200);
                    isInitialSync = false;
                    Logger.Info("Initial sync complete - profile saves are now enabled");
                }
            }

            // Auto-check for updates on startup (if enabled)
            _ = CheckForUpdatesOnStartupAsync();

            Logger.Info("=== OnNavigatedTo END ===");
        }

        public async Task GamingWidget_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_LeavingBackground called. Widget is null: {widget == null}, Pipe connected: {App.IsConnected}, WidgetActivity is null: {widgetActivity == null}");

            if (widget != null)
            {
                await widget.CenterWindowAsync();
            }

            if (App.IsConnected)
            {
                Logger.Info("GamingWidget LeavingBackground, syncing UI properties with helper.");

                // Show syncing banner while attempting sync (handles stale connections after sleep)
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ShowConnectionBanner(BannerState.Syncing);
                });

                // Set flag to prevent auto-save during sync (same pattern as OnNavigatedTo)
                bool syncSucceeded = false;
                isApplyingHelperUpdate = true;
                try
                {
                    // Use timeout to detect stale connections after sleep/hibernate
                    var syncTask = properties.Sync();
                    if (await Task.WhenAny(syncTask, Task.Delay(3000)) == syncTask)
                    {
                        await syncTask; // Ensure completion and propagate any exceptions
                        syncSucceeded = true;
                        Logger.Info("Property sync completed successfully.");
                    }
                    else
                    {
                        Logger.Warn("Property sync timed out - connection may be stale after sleep/hibernate.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Property sync failed - connection may be stale: {ex.Message}");
                }
                finally
                {
                    isApplyingHelperUpdate = false;
                }

                // Handle sync failure - trigger reconnection
                if (!syncSucceeded)
                {
                    Logger.Info("Sync failed, triggering helper reconnection...");
                    // Force relaunch helper - ignore heartbeat since we know connection is broken
                    // Helper has mutex protection so it will restart cleanly
                    await LaunchHelperWithGuardsAsync("LeavingBackground - sync failed", forceLaunch: true);
                    return; // Exit early, let AppServiceConnected handle the rest
                }

                // Sync succeeded - hide banner and continue
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    HideConnectionBanner();
                });

                // Update FPS Limit controls based on RTSS installed status
                UpdateFPSLimitControls();

                // Register Chill FPS handlers after sync to prevent crash
                RegisterChillFPSHandlers();

                // Re-evaluate which profile should be active and reload its settings
                // This is needed because the game may have closed while widget was in background
                // and the UI may still show stale game profile values
                // Must run on UI thread since GetTargetProfileName and LoadProfileSettings access UI controls
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        // Skip reloading profile settings if Default Game Profile is active
                        // The Default Game Profile settings are already applied and should not be overwritten
                        if (defaultGameProfileEnabled?.Value == true)
                        {
                            Logger.Info("Skipping profile reload - Default Game Profile is active");
                            return;
                        }

                        string expectedProfile = GetTargetProfileName();
                        if (expectedProfile != currentProfileName)
                        {
                            // Profile changed (game started/closed) - explicit switch, apply HDR/Resolution
                            Logger.Info($"Profile changed while in background: '{currentProfileName}' -> '{expectedProfile}'");
                            currentProfileName = expectedProfile;
                            LoadProfileSettings(currentProfileName, isExplicitSwitch: true);
                        }
                        else
                        {
                            // Same profile, just reloading UI - don't override game's resolution
                            // (e.g., TDP slider may show game value instead of global profile value)
                            Logger.Info($"Reloading profile settings after returning from background: {currentProfileName}");
                            LoadProfileSettings(currentProfileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error reloading profile after returning from background: {ex.Message}");
                    }
                });
            }
            else
            {
                Logger.Info("GamingWidget LeavingBackground but not connected to the full trust process.");
            }

            appIsInBackground = false;
            UpdateGameBarForegroundSignal("LeavingBackground");
            Logger.Info("GamingWidget_LeavingBackground completed.");
        }

        public void GamingWidget_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_EnteredBackground called. WidgetActivity is null: {widgetActivity == null}");
            appIsInBackground = true;
            UpdateGameBarForegroundSignal("EnteredBackground");
        }

        private async Task CreateWidgetActivity()
        {
            Logger.Info("=== CreateWidgetActivity START ===");

            if (widget == null)
            {
                Logger.Warn("Cannot create widget activity - widget is null!");
                Logger.Info("=== CreateWidgetActivity END (skipped - no widget) ===");
                return;
            }

            if (widgetActivity != null)
            {
                Logger.Info("Widget activity already exists, skipping creation.");
                Logger.Info("=== CreateWidgetActivity END (skipped - already exists) ===");
                return;
            }

            try
            {
                // Use a unique activity ID to avoid conflicts when the widget is reopened
                string activityId = $"XboxGamingBarActivity_{Guid.NewGuid():N}";
                Logger.Info($"Attempting to create XboxGameBarWidgetActivity with activityId='{activityId}'");
                Logger.Info($"Widget object details - Type: {widget.GetType().FullName}, Widget.ToString(): {widget.ToString()}");

                Logger.Info("Calling XboxGameBarWidgetActivity constructor...");
                widgetActivity = new XboxGameBarWidgetActivity(widget, activityId);
                Logger.Info("XboxGameBarWidgetActivity constructor completed.");

                Logger.Info($"Successfully created widget activity with ID '{activityId}' to keep the widget running in the background.");
            }
            catch (ArgumentException argumentException)
            {
                Logger.Error($"ArgumentException when creating widget activity: {argumentException}");
                Logger.Error($"Exception details - Message: {argumentException.Message}, ParamName: {argumentException.ParamName}, StackTrace: {argumentException.StackTrace}");
                Logger.Warn("Widget activity creation failed, but widget may still function. Continuing...");
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected exception when creating widget activity: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
                Logger.Warn("Widget activity creation failed, but widget may still function. Continuing...");
            }

            Logger.Info("=== CreateWidgetActivity END ===");
        }

        private async Task CreateAppTargetTracker()
        {
            Logger.Info("=== CreateAppTargetTracker START ===");

            if (widget == null)
            {
                Logger.Warn("Cannot create app target tracker - widget is null!");
                Logger.Info("=== CreateAppTargetTracker END (skipped - no widget) ===");
                return;
            }

            if (appTargetTracker != null)
            {
                Logger.Info("AppTargetTracker already exists, skipping creation.");
                Logger.Info("=== CreateAppTargetTracker END (skipped - already exists) ===");
                return;
            }

            try
            {
                Logger.Info("Creating XboxGameBarAppTargetTracker...");
                appTargetTracker = new XboxGameBarAppTargetTracker(widget);
                appTargetTracker.SettingChanged += AppTargetTracker_TargetChanged;
                Logger.Info("XboxGameBarAppTargetTracker created.");

                if (appTargetTracker.Setting == XboxGameBarAppTargetSetting.Enabled)
                {
                    Logger.Info("App target tracker is enabled. Getting initial target...");
                    var initialTarget = appTargetTracker.GetTarget();

                    if (initialTarget.IsGame)
                    {
                        Logger.Info($"Initial tracked game DisplayName={initialTarget.DisplayName} AumId={initialTarget.AumId} TitleId={initialTarget.TitleId} IsFullscreen={initialTarget.IsFullscreen}");
                        trackedGame.SetValue(new TrackedGame(initialTarget.AumId, initialTarget.DisplayName, StringHelper.CleanStringForSerialization(initialTarget.TitleId), initialTarget.IsFullscreen));
                    }
                    else
                    {
                        trackedGame.SetValue(new TrackedGame());
                        Logger.Info("No initial game target found.");
                    }

                    Logger.Info("Registering TargetChanged event handler...");
                    appTargetTracker.TargetChanged += AppTargetTracker_TargetChanged;
                    Logger.Info("TargetChanged event handler registered.");
                }
                else
                {
                    Logger.Info($"App target tracker created but not enabled. Setting: {appTargetTracker.Setting}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating app target tracker: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }

            Logger.Info("=== CreateAppTargetTracker END ===");
        }

        /// <summary>
        /// Apply default hotkey settings if not already configured
        /// </summary>
        private void ApplyHotkeyDefaults()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Check if defaults have already been applied
                if (settings.Values.ContainsKey("Hotkey_DefaultsApplied"))
                    return;

                // Apply defaults:
                // View + A: Disabled (0) - was Ctrl+Alt+Del but that cannot be simulated
                // View + B: Open Virtual Keyboard (7)
                // View + X: Screenshot (4)
                // View + Y: Focus GoTweaks (10)
                settings.Values["Hotkey_MenuA_Action"] = (int)HotkeyAction.Disabled;
                settings.Values["Hotkey_MenuB_Action"] = (int)HotkeyAction.OpenKeyboard;
                settings.Values["Hotkey_MenuX_Action"] = (int)HotkeyAction.Screenshot;
                settings.Values["Hotkey_MenuY_Action"] = (int)HotkeyAction.FocusGoTweaks;
                settings.Values["Hotkey_DefaultsApplied"] = true;

                Logger.Info("Hotkey defaults applied: A=Disabled, B=OpenKeyboard, X=Screenshot, Y=FocusGoTweaks");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying hotkey defaults: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize hotkey watchers for Xbox controller button combinations.
        /// These work even when the widget is not visible.
        /// </summary>
        private void InitializeHotkeyWatchers()
        {
            if (widget == null)
            {
                Logger.Warn("Cannot initialize hotkey watchers - widget is null");
                return;
            }

            // Skip if already initialized
            if (hotkeyMenuA != null)
            {
                Logger.Info("Hotkey watchers already initialized, skipping");
                return;
            }

            // Apply default hotkey settings if not already set
            ApplyHotkeyDefaults();

            try
            {
                // Menu+A
                var keysA = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadA };
                hotkeyMenuA = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysA);
                hotkeyMenuA.HotkeySetStateChanged += HotkeyMenuA_StateChanged;
                hotkeyMenuA.Start();

                // Menu+B
                var keysB = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadB };
                hotkeyMenuB = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysB);
                hotkeyMenuB.HotkeySetStateChanged += HotkeyMenuB_StateChanged;
                hotkeyMenuB.Start();

                // Menu+X
                var keysX = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadX };
                hotkeyMenuX = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysX);
                hotkeyMenuX.HotkeySetStateChanged += HotkeyMenuX_StateChanged;
                hotkeyMenuX.Start();

                // Menu+Y
                var keysY = new List<VirtualKey> { VirtualKey.GamepadView, VirtualKey.GamepadY };
                hotkeyMenuY = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysY);
                hotkeyMenuY.HotkeySetStateChanged += HotkeyMenuY_StateChanged;
                hotkeyMenuY.Start();

                // Menu+DpadUp
                var keysDpadUp = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadUp };
                hotkeyMenuDpadUp = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadUp);
                hotkeyMenuDpadUp.HotkeySetStateChanged += HotkeyMenuDpadUp_StateChanged;
                hotkeyMenuDpadUp.Start();

                // Menu+DpadDown
                var keysDpadDown = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadDown };
                hotkeyMenuDpadDown = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadDown);
                hotkeyMenuDpadDown.HotkeySetStateChanged += HotkeyMenuDpadDown_StateChanged;
                hotkeyMenuDpadDown.Start();

                // Menu+DpadLeft
                var keysDpadLeft = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadLeft };
                hotkeyMenuDpadLeft = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadLeft);
                hotkeyMenuDpadLeft.HotkeySetStateChanged += HotkeyMenuDpadLeft_StateChanged;
                hotkeyMenuDpadLeft.Start();

                // Menu+DpadRight
                var keysDpadRight = new List<VirtualKey> { VirtualKey.GamepadMenu, VirtualKey.GamepadDPadRight };
                hotkeyMenuDpadRight = XboxGameBarHotkeyWatcher.CreateWatcher(widget, keysDpadRight);
                hotkeyMenuDpadRight.HotkeySetStateChanged += HotkeyMenuDpadRight_StateChanged;
                hotkeyMenuDpadRight.Start();

                Logger.Info("Hotkey watchers initialized for View+A/B/X/Y and Menu+Dpad");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize hotkey watchers: {ex.Message}");
            }
        }
        private void AppTargetTracker_TargetChanged(XboxGameBarAppTargetTracker sender, object args)
        {
            var settingEnabled = appTargetTracker.Setting == XboxGameBarAppTargetSetting.Enabled;

            XboxGameBarAppTarget target = null;
            if (settingEnabled)
            {
                target = appTargetTracker.GetTarget();
            }

            if (target == null)
            {
                Logger.Info("Found no target.");
                trackedGame.SetValue(new TrackedGame());
            }
            else
            {
                if (target.IsGame && !BlackListAppTrackerNames.Contains(target.DisplayName))
                {
                    Logger.Info($"Tracked game DisplayName={target.DisplayName} AumId={target.AumId} TitleId={target.TitleId} IsFullscreen={target.IsFullscreen}");
                    trackedGame.SetValue(new TrackedGame(target.AumId, target.DisplayName, StringHelper.CleanStringForSerialization(target.TitleId), target.IsFullscreen));
                }
                else
                {
                    Logger.Info($"Tracked non-game DisplayName={target.DisplayName} AumId={target.AumId} TitleId={target.TitleId} IsFullscreen={target.IsFullscreen}");
                    trackedGame.SetValue(new TrackedGame());
                }
            }
        }

        /// <summary>
        /// Banner state types for different connection status messages.
        /// </summary>
        private enum BannerState
        {
            /// <summary>Red banner - Not connected to helper (error state)</summary>
            Disconnected,
            /// <summary>Blue banner - Syncing properties (info state)</summary>
            Syncing,
            /// <summary>Orange banner - Reconnecting to helper (warning state)</summary>
            Reconnecting,
            /// <summary>Blue banner - Launching helper process (info state)</summary>
            Launching,
            /// <summary>Blue banner - Loading widget (initial state)</summary>
            Loading,
            /// <summary>Purple banner - Initial setup in progress (requires UAC)</summary>
            InitialSetup,
            /// <summary>Blue banner - Upgrading helper (UAC-free upgrade in progress)</summary>
            Upgrading
        }

        /// <summary>
        /// Shows the connection status banner with appropriate color and message based on state.
        /// </summary>
        /// <param name="state">The banner state to display</param>
        private void ShowConnectionBanner(BannerState state)
        {
            Logger.Info($"[BANNER] ShowConnectionBanner called: state={state}");
            if (ConnectionStatusBanner == null || ConnectionStatusText == null)
            {
                Logger.Warn($"[BANNER] ShowConnectionBanner: Banner controls are null! ConnectionStatusBanner={ConnectionStatusBanner != null}, ConnectionStatusText={ConnectionStatusText != null}");
                return;
            }

            switch (state)
            {
                case BannerState.Disconnected:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 51, 51)); // #CC3333
                    ConnectionStatusText.Text = "Not connected to helper";
                    break;
                case BannerState.Syncing:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Syncing...";
                    break;
                case BannerState.Reconnecting:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 102, 51)); // #CC6633
                    ConnectionStatusText.Text = "Reconnecting...";
                    break;
                case BannerState.Launching:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Launching helper...";
                    break;
                case BannerState.Loading:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 102, 204)); // #3366CC
                    ConnectionStatusText.Text = "Loading...";
                    break;
                case BannerState.InitialSetup:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 51, 153)); // #663399 Purple
                    ConnectionStatusText.Text = "Initial setup in progress - please wait...";
                    break;
                case BannerState.Upgrading:
                    ConnectionStatusBanner.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 153, 102)); // #339966 Green
                    ConnectionStatusText.Text = "Upgrading helper...";
                    break;
            }
            ConnectionStatusBanner.Visibility = Visibility.Visible;

            // Also update the small status indicator dot
            switch (state)
            {
                case BannerState.Disconnected:
                    UpdateHelperStatusIndicator(HelperStatus.Disconnected);
                    break;
                case BannerState.Syncing:
                case BannerState.Reconnecting:
                case BannerState.Launching:
                case BannerState.Loading:
                case BannerState.InitialSetup:
                case BannerState.Upgrading:
                    UpdateHelperStatusIndicator(HelperStatus.Connecting);
                    break;
            }
        }

        /// <summary>
        /// Hides the connection status banner.
        /// </summary>
        private void HideConnectionBanner()
        {
            Logger.Info("[BANNER] HideConnectionBanner called");
            if (ConnectionStatusBanner != null)
            {
                ConnectionStatusBanner.Visibility = Visibility.Collapsed;
                Logger.Info("[BANNER] ConnectionStatusBanner visibility set to Collapsed");
            }
            else
            {
                Logger.Warn("[BANNER] HideConnectionBanner: ConnectionStatusBanner is null!");
            }

            // When banner is hidden, we're connected - show green status
            UpdateHelperStatusIndicator(HelperStatus.Connected);
        }

        /// <summary>
        /// Helper connection status for the status indicator dot.
        /// </summary>
        private enum HelperStatus
        {
            Disconnected,  // Red - not connected
            Connecting,    // Yellow - connecting/syncing/launching
            Connected      // Green - fully connected
        }

        /// <summary>
        /// Updates the small helper status indicator dot in the Quick tab corner.
        /// </summary>
        private void UpdateHelperStatusIndicator(HelperStatus status)
        {
            if (HelperStatusDot == null) return;

            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                switch (status)
                {
                    case HelperStatus.Disconnected:
                        HelperStatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 85, 85)); // #FF5555 Red
                        if (HelperStatusText != null) HelperStatusText.Text = "Disconnected";
                        break;
                    case HelperStatus.Connecting:
                        HelperStatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 50)); // #FFC832 Yellow/Orange
                        if (HelperStatusText != null) HelperStatusText.Text = "Connecting...";
                        break;
                    case HelperStatus.Connected:
                        HelperStatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 200, 85)); // #55C855 Green
                        if (HelperStatusText != null) HelperStatusText.Text = "Connected";
                        break;
                }
            });
        }

        /// <summary>
        /// Handle tap on helper status indicator to show/hide status text.
        /// </summary>
        private void HelperStatusIndicator_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (HelperStatusText != null)
            {
                HelperStatusText.Visibility = HelperStatusText.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        /// <summary>
        /// Applies the current profile's TDP value and TDP Mode to the helper.
        /// Called after connection is established since profile loads before connection.
        /// </summary>
        private async Task ApplyProfileTDPToHelper()
        {
            try
            {
                // Skip when Default Game Profile is active - DGP controls TDP, not the profile
                if (defaultGameProfileEnabled?.Value == true)
                {
                    Logger.Info("Skipping ApplyProfileTDPToHelper - Default Game Profile is active");
                    return;
                }

                var profile = GetProfile(currentProfileName);
                if (profile == null)
                {
                    Logger.Warn($"Cannot apply profile TDP - profile '{currentProfileName}' not found");
                    return;
                }

                // Run on UI thread since we're touching UI controls (including SaveTDP which accesses checkbox)
                bool needsDelayForModeChange = false;
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (!SaveTDP) return;

                    // Apply Legion TDP Mode first (for Legion devices)
                    if (legionGoDetected?.Value == true && legionPerformanceMode != null)
                    {
                        int profileMode = profile.LegionPerformanceMode;
                        int modeIndex = GetProfileTDPModeIndex(profile);

                        if (modeIndex >= 0)
                        {
                            // During initial sync, always apply the profile's TDP mode to ensure
                            // hardware matches profile (hardware may have been set to Custom by TDP sync)
                            bool needsUIUpdate = (TDPModeComboBox != null && TDPModeComboBox.SelectedIndex != modeIndex) ||
                                                 (LegionPerformanceModeComboBox != null && LegionPerformanceModeComboBox.SelectedIndex != modeIndex);

                            Logger.Info($"Applying profile TDP Mode to helper: {GetLegionModeShortName(profileMode)} ({profileMode}) (profile: {currentProfileName})");

                            // Update lastTDPModeIndex FIRST before any ComboBox changes to prevent
                            // TDPModeComboBox_SelectionChanged from treating this as a user-initiated change
                            lastTDPModeIndex = modeIndex;

                            // Update UI combo boxes if needed
                            if (LegionPerformanceModeComboBox != null && LegionPerformanceModeComboBox.SelectedIndex != modeIndex)
                                LegionPerformanceModeComboBox.SelectedIndex = modeIndex;
                            if (TDPModeComboBox != null && TDPModeComboBox.SelectedIndex != modeIndex)
                                TDPModeComboBox.SelectedIndex = modeIndex;

                            // Always force send to helper during startup to ensure hardware matches profile
                            // This is necessary because TDP sync may have triggered Custom mode on hardware
                            legionPerformanceMode.ForceSetValue(profileMode);

                            // Update TDP slider enabled state
                            UpdateTDPSliderEnabledState();

                            // If switching to Custom mode, we need a delay before applying TDP value
                            // to allow the mode change to propagate to the helper first
                            if (profileMode == 255)
                            {
                                needsDelayForModeChange = true;
                            }
                        }
                    }
                });

                // If we just changed to Custom mode, wait for mode change to propagate to helper
                // before sending TDP value (mode change involves WMI calls that take time)
                if (needsDelayForModeChange)
                {
                    Logger.Info("Waiting for Custom mode to propagate to helper before applying TDP...");
                    await Task.Delay(300);
                }

                // Second dispatcher call for TDP value application
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (!SaveTDP) return;

                    // Apply TDP value ONLY in Custom mode (255)
                    // Quiet/Balanced/Performance modes use hardware presets and don't accept TDP values
                    bool isCustomMode = legionGoDetected?.Value != true || profile.LegionPerformanceMode == 255;

                    if (tdp != null && isCustomMode)
                    {
                        int targetTDP = (int)profile.TDP;
                        Logger.Info($"Applying profile TDP to helper: {targetTDP}W (profile: {currentProfileName})");

                        // Invalidate cached value first to force send even if values match
                        // This is needed because profile was loaded before connection, so the cached
                        // value matches but was never sent to hardware
                        tdp.SetValueSilent(-1);
                        tdp.SetValue(targetTDP, DateTime.Now.Ticks);

                        // Update Sticky TDP target if enabled
                        if (StickyTDPToggle?.IsOn == true)
                        {
                            targetTDPLimit = profile.TDP;
                            Logger.Info($"Sticky TDP target set to: {targetTDPLimit}W");
                        }
                    }
                    else if (tdp != null && !isCustomMode)
                    {
                        Logger.Info($"Skipping TDP value application - using {GetLegionModeShortName(profile.LegionPerformanceMode)} preset (profile: {currentProfileName})");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying profile TDP to helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if helper is alive by reading its heartbeat file.
        /// Returns true if heartbeat file exists and is recent (less than HeartbeatStaleThresholdSeconds old).
        /// Also checks version match - if helper version doesn't match widget version, requests helper exit.
        /// </summary>
        private async Task<bool> IsHelperAliveAsync()
        {
            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var heartbeatFile = await localFolder.TryGetItemAsync("helper_heartbeat.json");

                if (heartbeatFile == null)
                {
                    Logger.Info("Heartbeat file not found - helper not running");
                    return false;
                }

                string content = await Windows.Storage.FileIO.ReadTextAsync((Windows.Storage.StorageFile)heartbeatFile);

                // Simple JSON parsing without external dependency
                // Format: {"pid":1234,"timestamp":1234567890,"connected":true,"elevated":true,"version":"0.3.1430.0"}
                var timestampMatch = System.Text.RegularExpressions.Regex.Match(content, @"""timestamp"":(\d+)");
                if (!timestampMatch.Success)
                {
                    Logger.Warn("Could not parse heartbeat timestamp");
                    return false;
                }

                long timestamp = long.Parse(timestampMatch.Groups[1].Value);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long age = now - timestamp;

                if (age > HeartbeatStaleThresholdSeconds)
                {
                    // Heartbeat stale, but check if process is still running (e.g., after sleep/hibernation)
                    var pidMatch = System.Text.RegularExpressions.Regex.Match(content, @"""pid"":(\d+)");
                    if (pidMatch.Success)
                    {
                        int pid = int.Parse(pidMatch.Groups[1].Value);
                        try
                        {
                            var process = System.Diagnostics.Process.GetProcessById(pid);
                            if (process != null && !process.HasExited)
                            {
                                Logger.Info($"Heartbeat stale ({age}s old) but process {pid} still running - helper likely resuming from sleep");
                                return true; // Helper is alive, just needs time to resume
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Process not found - it has exited
                            Logger.Info($"Heartbeat stale ({age}s old) and process {pid} not found - helper is dead");
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Error checking process {pid}: {ex.Message}");
                        }
                    }

                    Logger.Info($"Heartbeat is stale ({age}s old) - helper may be hung or dead");
                    return false;
                }

                // Check version match - if helper is outdated, request it to exit
                var versionMatch = System.Text.RegularExpressions.Regex.Match(content, @"""version"":""([^""]+)""");
                if (versionMatch.Success)
                {
                    string helperVersion = versionMatch.Groups[1].Value;
                    string widgetVersion = GetWidgetVersion();

                    if (helperVersion != widgetVersion)
                    {
                        Logger.Info($"Helper version mismatch: helper={helperVersion}, widget={widgetVersion} - requesting helper restart");

                        // Show upgrading banner before requesting exit
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            ShowConnectionBanner(BannerState.Upgrading);
                        });

                        await RequestHelperExitAsync();

                        // Wait for the old helper to fully exit
                        // The helper has a 7 second force-exit timeout after receiving ExitHelper.
                        // We can't reliably check process exit from UWP (sandbox restrictions on elevated processes).
                        // Instead, wait for the force-exit timeout plus buffer to guarantee the old helper is dead.
                        const int forceExitTimeoutMs = 7000; // Helper's force-exit timeout
                        const int bufferMs = 1500; // Extra buffer for pipe cleanup
                        const int totalWaitMs = forceExitTimeoutMs + bufferMs;

                        Logger.Info($"Waiting {totalWaitMs}ms for old helper force-exit timeout...");
                        await Task.Delay(totalWaitMs);
                        Logger.Info("Old helper should now be fully exited");

                        return false; // Return false so a new helper will be launched
                    }
                }

                Logger.Info($"Helper is alive (heartbeat {age}s old)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error reading heartbeat: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the current widget/package version as a string.
        /// </summary>
        private string GetWidgetVersion()
        {
            try
            {
                var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
            }
            catch
            {
                return "0.0.0.0";
            }
        }

        /// <summary>
        /// Request the helper to exit gracefully. Used when version mismatch is detected.
        /// </summary>
        private async Task RequestHelperExitAsync()
        {
            try
            {
                bool exitSent = false;

                // Try via Named Pipe
                if (App.PipeClient?.IsConnected == true)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("ExitHelper", true);
                    await App.SendMessageAsync(request);
                    Logger.Info("Sent ExitHelper via Named Pipe");
                    exitSent = true;
                }
                // Not connected - try to establish a temporary pipe connection just to send ExitHelper
                else
                {
                    Logger.Info("Not connected to helper - attempting temporary pipe connection to send ExitHelper");
                    try
                    {
                        // Create a temporary pipe client just for this purpose
                        using (var tempPipe = new System.IO.Pipes.NamedPipeClientStream(".", "GoTweaksHelper", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
                        {
                            // Try to connect with short timeout
                            var connectTask = tempPipe.ConnectAsync(2000);
                            if (await Task.WhenAny(connectTask, Task.Delay(2500)) == connectTask)
                            {
                                // Connected - send ExitHelper as simple JSON
                                using (var writer = new System.IO.StreamWriter(tempPipe, System.Text.Encoding.UTF8, 4096, leaveOpen: true))
                                {
                                    writer.AutoFlush = true;
                                    await writer.WriteLineAsync("{\"RequestId\":0,\"ExitHelper\":true}");
                                }
                                Logger.Info("Sent ExitHelper via temporary pipe connection");
                                exitSent = true;
                            }
                            else
                            {
                                Logger.Warn("Timed out connecting to helper pipe for ExitHelper");
                            }
                        }
                    }
                    catch (Exception pipeEx)
                    {
                        Logger.Warn($"Failed to establish temporary pipe connection: {pipeEx.Message}");
                    }
                }

                if (!exitSent)
                {
                    Logger.Warn("Could not send ExitHelper to helper - no connection available");
                }

                // Give helper time to exit
                await Task.Delay(1000);

                // Delete stale heartbeat file
                try
                {
                    var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    var heartbeatFile = await localFolder.TryGetItemAsync("helper_heartbeat.json");
                    if (heartbeatFile != null)
                    {
                        await heartbeatFile.DeleteAsync();
                        Logger.Info("Deleted stale heartbeat file after requesting helper exit");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Could not delete heartbeat file: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to request helper exit: {ex.Message}");
            }
        }

        /// <summary>
        /// Request the helper to perform a UAC-free upgrade.
        /// Sends the MSIX source path so the elevated helper can copy new files and restart.
        /// This avoids UAC prompts during upgrades since the running helper is already elevated.
        /// </summary>
        /// <returns>True if upgrade succeeded and new helper is running, false otherwise</returns>
        private async Task<bool> RequestHelperUpgradeAsync()
        {
            try
            {
                // Show upgrading banner
                ShowConnectionBanner(BannerState.Upgrading);

                // Get the MSIX helper source path
                string msixSourcePath = GetMsixHelperSourcePath();
                if (string.IsNullOrEmpty(msixSourcePath))
                {
                    Logger.Warn("Could not determine MSIX helper source path - falling back to ExitHelper");
                    await RequestHelperExitAsync();
                    return false;
                }

                Logger.Info($"Requesting UAC-free upgrade with source: {msixSourcePath}");
                bool upgradeSent = false;

                // Try via Named Pipe (primary method)
                if (App.PipeClient?.IsConnected == true)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("UpgradeHelper", msixSourcePath);
                    await App.SendMessageAsync(request);
                    Logger.Info("Sent UpgradeHelper via Named Pipe (already connected)");
                    upgradeSent = true;
                }
                // Not connected - try to establish a temporary pipe connection
                else
                {
                    Logger.Info("Not connected to helper - attempting temporary pipe connection for UpgradeHelper");
                    try
                    {
                        using (var tempPipe = new System.IO.Pipes.NamedPipeClientStream(".", "GoTweaksHelper", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
                        {
                            var connectTask = tempPipe.ConnectAsync(2000);
                            if (await Task.WhenAny(connectTask, Task.Delay(2500)) == connectTask)
                            {
                                using (var writer = new System.IO.StreamWriter(tempPipe, System.Text.Encoding.UTF8, 4096, leaveOpen: true))
                                {
                                    writer.AutoFlush = true;
                                    // Send as JSON with UpgradeHelper in Extra
                                    string json = $"{{\"RequestId\":0,\"Extra\":{{\"UpgradeHelper\":\"{msixSourcePath.Replace("\\", "\\\\")}\"}}}}";
                                    await writer.WriteLineAsync(json);
                                }
                                Logger.Info("Sent UpgradeHelper via temporary pipe connection");
                                upgradeSent = true;
                            }
                            else
                            {
                                Logger.Warn("Timed out connecting to helper pipe for UpgradeHelper");
                            }
                        }
                    }
                    catch (Exception pipeEx)
                    {
                        Logger.Warn($"Failed to establish temporary pipe connection: {pipeEx.Message}");
                    }
                }

                if (!upgradeSent)
                {
                    Logger.Warn("Could not send UpgradeHelper - falling back to ExitHelper");
                    await RequestHelperExitAsync();
                    return false;
                }

                // Delete stale heartbeat file first
                try
                {
                    var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    var heartbeatFile = await localFolder.TryGetItemAsync("helper_heartbeat.json");
                    if (heartbeatFile != null)
                    {
                        await heartbeatFile.DeleteAsync();
                        Logger.Info("Deleted stale heartbeat file before waiting for upgrade");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Could not delete heartbeat file: {ex.Message}");
                }

                // Wait for the upgrade to complete and new helper to start
                // The batch script needs time to: wait for old helper to exit, copy files, run task
                // Poll for new heartbeat file to confirm new helper is running
                Logger.Info("Waiting for new helper to start after upgrade...");
                bool newHelperStarted = false;
                for (int i = 0; i < 15; i++) // Wait up to 15 seconds
                {
                    await Task.Delay(1000);

                    try
                    {
                        var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                        var heartbeatFile = await localFolder.TryGetItemAsync("helper_heartbeat.json");
                        if (heartbeatFile != null)
                        {
                            // Heartbeat file exists - check if it's from new helper
                            var file = heartbeatFile as Windows.Storage.StorageFile;
                            if (file != null)
                            {
                                string content = await Windows.Storage.FileIO.ReadTextAsync(file);
                                // Check if version matches new version
                                var versionMatch = System.Text.RegularExpressions.Regex.Match(content, @"""version"":""([^""]+)""");
                                if (versionMatch.Success)
                                {
                                    string helperVersion = versionMatch.Groups[1].Value;
                                    string widgetVersion = GetWidgetVersion();
                                    if (helperVersion == widgetVersion)
                                    {
                                        Logger.Info($"New helper v{helperVersion} started successfully after upgrade");
                                        newHelperStarted = true;
                                        break;
                                    }
                                    else
                                    {
                                        Logger.Debug($"Heartbeat version {helperVersion} doesn't match widget {widgetVersion} yet");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error checking heartbeat during upgrade wait: {ex.Message}");
                    }
                }

                if (newHelperStarted)
                {
                    Logger.Info("UAC-free upgrade completed successfully");
                    HideConnectionBanner();
                    return true;
                }
                else
                {
                    Logger.Warn("New helper did not start within timeout - may need manual intervention");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to request helper upgrade: {ex.Message} - falling back to ExitHelper");
                await RequestHelperExitAsync();
                return false;
            }
        }

        /// <summary>
        /// Gets the MSIX helper source path (in WindowsApps folder).
        /// </summary>
        private string GetMsixHelperSourcePath()
        {
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                var installPath = package.InstalledLocation.Path;
                return System.IO.Path.Combine(installPath, "XboxGamingBarHelper");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not get MSIX helper path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Launch helper with guards to prevent duplicate launches and unnecessary UAC prompts.
        /// Checks if helper is already alive (via heartbeat) and enforces minimum interval between launches.
        /// </summary>
        /// <param name="reason">Description of why we're launching (for logging)</param>
        /// <param name="forceLaunch">If true, ignore heartbeat check (use when sync explicitly failed)</param>
        /// <returns>True if launch was attempted, false if skipped</returns>
        private async Task<bool> LaunchHelperWithGuardsAsync(string reason, bool forceLaunch = false)
        {
            // Check if already launching
            if (isLaunchingHelper)
            {
                Logger.Info($"Skipping launch ({reason}) - already launching");
                // Keep existing Launching banner visible
                return false;
            }

            // Check minimum interval (skip if force launching)
            var timeSinceLastLaunch = (DateTime.Now - lastLaunchAttempt).TotalMilliseconds;
            if (!forceLaunch && timeSinceLastLaunch < MinLaunchIntervalMs)
            {
                Logger.Info($"Skipping launch ({reason}) - too soon since last attempt ({timeSinceLastLaunch:F0}ms)");
                // Show reconnecting banner since we're rate-limited but trying to connect
                ShowConnectionBanner(BannerState.Reconnecting);
                return false;
            }

            // Check if helper is already alive (skip if force launching - we know connection is broken)
            if (!forceLaunch)
            {
                bool helperAlive = await IsHelperAliveAsync();
                if (helperAlive)
                {
                    Logger.Info($"Skipping launch ({reason}) - helper is already alive, trying pipe connection");
                    // Show reconnecting banner since we're waiting for helper to reconnect
                    ShowConnectionBanner(BannerState.Reconnecting);

                    // Immediately try to connect via Named Pipe (don't wait for timeout)
                    _ = TryConnectPipeAsync();

                    // Start timeout timer as backup - if pipe doesn't connect within timeout, force launch
                    StartReconnectionTimeoutTimer();
                    return false;
                }
            }
            else
            {
                Logger.Info($"Force launching helper ({reason}) - ignoring heartbeat check");
            }

            try
            {
                isLaunchingHelper = true;
                lastLaunchAttempt = DateTime.Now;

                Logger.Info($"Launching helper ({reason})...");
                ShowConnectionBanner(BannerState.Launching);
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                Logger.Info("Helper launch completed");

                // Try to connect via Named Pipe (works even when helper is elevated)
                // Brief delay for helper to start, then fast retry loop handles the rest
                await Task.Delay(200);
                _ = TryConnectPipeAsync();

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching helper: {ex.Message}");
                return false;
            }
            finally
            {
                isLaunchingHelper = false;
            }
        }

        /// <summary>
        /// Attempts to connect to the helper via Named Pipe.
        /// Runs in background and triggers PipeConnected event on success.
        /// Uses longer retry duration to handle elevation scenario where helper
        /// goes through setup mode (UAC prompt, task creation) before pipe server starts.
        /// </summary>
        private async Task TryConnectPipeAsync()
        {
            // Retry duration must cover the elevation scenario:
            // - MSIX helper launches, checks elevation, launches --setup (UAC prompt)
            // - User approves UAC
            // - Setup helper deploys, creates task, runs task, exits
            // - Elevated helper starts from deployed location
            // - Pipe server starts
            // This can take 15-30 seconds total
            //
            // Use fast retries (500ms timeout + 250ms delay) for the first 10 attempts (~7.5s)
            // to minimize reconnection latency when helper is already running.
            // Then slow down for the remaining attempts to cover UAC/setup scenarios.
            const int maxAttempts = 80;
            const int fastTimeoutMs = 500;
            const int slowTimeoutMs = 1500;
            const int fastDelayMs = 250;
            const int slowDelayMs = 1000;
            const int fastAttempts = 10; // ~7.5s of fast retries
            var startTime = DateTime.Now;
            bool shownInitialSetupBanner = false;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (App.PipeClient?.IsConnected == true)
                {
                    Logger.Info("Pipe already connected");
                    return;
                }

                bool isFastPhase = attempt <= fastAttempts;
                int timeoutMs = isFastPhase ? fastTimeoutMs : slowTimeoutMs;
                int delayMs = isFastPhase ? fastDelayMs : slowDelayMs;

                // Only log every 10 attempts to reduce noise
                if (attempt == 1 || attempt % 10 == 0)
                {
                    Logger.Info($"Attempting pipe connection ({attempt}/{maxAttempts}, timeout={timeoutMs}ms)...");
                }

                // After 8 seconds, show InitialSetup banner (likely UAC/setup in progress)
                if (!shownInitialSetupBanner && (DateTime.Now - startTime).TotalSeconds >= 8)
                {
                    shownInitialSetupBanner = true;
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        ShowConnectionBanner(BannerState.InitialSetup);
                    });
                }

                bool connected = await App.ConnectPipeAsync(timeoutMs);

                if (connected)
                {
                    Logger.Info($"Connected to helper via Named Pipe! (attempt {attempt}, {(DateTime.Now - startTime).TotalMilliseconds:F0}ms elapsed)");

                    // Register for pipe messages
                    App.PipeMessageReceived -= PipeClient_MessageReceived;
                    App.PipeMessageReceived += PipeClient_MessageReceived;
                    App.PipeDisconnected -= PipeClient_Disconnected;
                    App.PipeDisconnected += PipeClient_Disconnected;

                    // Trigger connection success flow
                    await OnPipeConnectedAsync();
                    return;
                }

                // Wait before next attempt
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs);
                }
            }

            Logger.Warn($"Failed to connect via pipe after {maxAttempts} attempts ({(DateTime.Now - startTime).TotalSeconds:F1}s)");

            // Show disconnected banner so user knows connection failed
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ShowConnectionBanner(BannerState.Disconnected);
            });
        }

        /// <summary>
        /// Starts a timer that will force-launch the helper if connection isn't established within timeout.
        /// Call this when helper is detected as alive but not connected.
        /// </summary>
        private void StartReconnectionTimeoutTimer()
        {
            // Stop any existing timer
            StopReconnectionTimeoutTimer();

            // Don't start timer if already connected (via AppService or pipe)
            if (App.IsConnected)
            {
                Logger.Info("Reconnection timeout timer not started - already connected");
                return;
            }

            Logger.Info($"Starting reconnection timeout timer ({ReconnectionTimeoutSeconds}s)");

            reconnectionTimeoutTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(ReconnectionTimeoutSeconds)
            };
            reconnectionTimeoutTimer.Tick += ReconnectionTimeoutTimer_Tick;
            reconnectionTimeoutTimer.Start();
        }

        /// <summary>
        /// Stops the reconnection timeout timer if running.
        /// </summary>
        private void StopReconnectionTimeoutTimer()
        {
            if (reconnectionTimeoutTimer != null)
            {
                Logger.Info("Stopping reconnection timeout timer");
                reconnectionTimeoutTimer.Stop();
                reconnectionTimeoutTimer.Tick -= ReconnectionTimeoutTimer_Tick;
                reconnectionTimeoutTimer = null;
            }
        }

        /// <summary>
        /// Called when reconnection timeout fires - force launch the helper.
        /// </summary>
        private async void ReconnectionTimeoutTimer_Tick(object sender, object e)
        {
            // Stop the timer first (it's a one-shot)
            StopReconnectionTimeoutTimer();

            // Check if we're now connected (race condition check)
            if (App.IsConnected)
            {
                Logger.Info("Reconnection timeout fired but already connected - skipping force launch");
                HideConnectionBanner();
                return;
            }

            Logger.Info("Reconnection timeout fired - force launching helper");
            await LaunchHelperWithGuardsAsync("Reconnection timeout", forceLaunch: true);
        }

        private async void GamingWidget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundColor();
            });
        }

        private async void GamingWidget_SettingsClicked(XboxGameBarWidget sender, object args)
        {
            await widget.ActivateSettingsAsync();
        }

        private bool hasAppliedInitialSize = false;

        private async void GamingWidget_VisibleChanged(XboxGameBarWidget sender, object args)
        {
            try
            {
                bool isVisible = sender?.Visible ?? false;
                Logger.Info($"GamingWidget_VisibleChanged: Visible={isVisible}, DisplayMode={sender?.GameBarDisplayMode.ToString() ?? "Unknown"}");
                UpdateGameBarForegroundSignal("VisibleChanged");

                // Resize to full height on first activation.
                // Delay to let Game Bar finish restoring its cached layout first.
                if (isVisible && !hasAppliedInitialSize && sender != null)
                {
                    hasAppliedInitialSize = true;
                    await Task.Delay(500);
                    var success = await sender.TryResizeWindowAsync(new Windows.Foundation.Size(464, 1080));
                    Logger.Info($"Initial TryResizeWindowAsync(464x1080): success={success}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GamingWidget_VisibleChanged failed: {ex.Message}");
            }
        }

        private void GamingWidget_GameBarDisplayModeChanged(XboxGameBarWidget sender, object args)
        {
            try
            {
                Logger.Info($"GamingWidget_GameBarDisplayModeChanged: DisplayMode={sender?.GameBarDisplayMode.ToString() ?? "Unknown"}, Visible={sender?.Visible ?? false}");
                UpdateGameBarForegroundSignal("GameBarDisplayModeChanged");
            }
            catch (Exception ex)
            {
                Logger.Error($"GamingWidget_GameBarDisplayModeChanged failed: {ex.Message}");
            }
        }

        private void UpdateGameBarForegroundSignal(string source)
        {
            try
            {
                bool widgetVisible = widget?.Visible ?? false;
                XboxGameBarDisplayMode displayMode = widget?.GameBarDisplayMode ?? XboxGameBarDisplayMode.Foreground;

                // Full Game Bar visibility signal:
                // - true when the overlay is foreground (even if this specific widget tab is not selected)
                // - false when app is backgrounded or Game Bar is not in foreground mode
                bool gameBarForeground = !appIsInBackground && displayMode == XboxGameBarDisplayMode.Foreground;
                isForeground.ForceSetValue(gameBarForeground);

                Logger.Info($"GameBar foreground signal update ({source}): value={gameBarForeground}, displayMode={displayMode}, widgetVisible={widgetVisible}, appBackground={appIsInBackground}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"UpdateGameBarForegroundSignal failed ({source}): {ex.Message}");
            }
        }

        private void SetBackgroundColor()
        {
            this.RequestedTheme = widget.RequestedTheme;
            this.Background = (widget.RequestedTheme == ElementTheme.Dark) ? widgetDarkThemeBrush : widgetLightThemeBrush;
        }

        private void UpdateCompactMode(double width)
        {
            bool wasCompactMode = isCompactMode;
            isCompactMode = width < CompactModeWidthThreshold;

            if (wasCompactMode != isCompactMode)
            {
                Logger.Info($"Compact mode changed: {isCompactMode} (width: {width})");
                UpdateFontSizes();
            }
        }

        private void UpdateFontSizes()
        {
            // Update all TextBlocks dynamically based on compact mode
            UpdateTextBlockStyles(PerformanceScrollViewer);
            UpdateTextBlockStyles(GameScrollViewer);
            UpdateTextBlockStyles(AMDScrollViewer);
            UpdateTextBlockStyles(SystemScrollViewer);
        }

        private void UpdateTextBlockStyles(DependencyObject parent)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is TextBlock textBlock)
                {
                    // Update font size based on current style and compact mode
                    if (textBlock.Style != null)
                    {
                        string styleKey = textBlock.Style.TargetType == typeof(TextBlock) ? GetStyleKey(textBlock) : null;
                        if (styleKey != null)
                        {
                            string compactStyleKey = isCompactMode ? styleKey + "Compact" : styleKey;
                            if (Resources.ContainsKey(compactStyleKey))
                            {
                                textBlock.Style = Resources[compactStyleKey] as Style;
                            }
                        }
                    }
                }

                // Recurse through visual tree
                UpdateTextBlockStyles(child);
            }
        }

        private string GetStyleKey(TextBlock textBlock)
        {
            // Determine which style is currently applied
            if (textBlock.Style == Resources["CardTitleStyle"] as Style ||
                textBlock.Style == Resources["CardTitleStyleCompact"] as Style)
                return "CardTitleStyle";
            if (textBlock.Style == Resources["CardCaptionStyle"] as Style ||
                textBlock.Style == Resources["CardCaptionStyleCompact"] as Style)
                return "CardCaptionStyle";
            if (textBlock.Style == Resources["CardValueStyle"] as Style ||
                textBlock.Style == Resources["CardValueStyleCompact"] as Style)
                return "CardValueStyle";
            return null;
        }

        /// <summary>
        /// Focus this widget using XboxGameBarWidgetControl API.
        /// Called when helper sends Labs_FocusWidget command.
        /// </summary>
        private async Task FocusThisWidgetAsync()
        {
            try
            {
                if (widget != null)
                {
                    // Create widget control and activate this widget
                    var widgetControl = new XboxGameBarWidgetControl(widget);
                    await widgetControl.ActivateAsync("GamingWidget");
                    Logger.Info("Widget focused successfully via XboxGameBarWidgetControl");
                }
                else
                {
                    Logger.Warn("Cannot focus widget - widget object is null");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to focus widget: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles messages received from helper via Named Pipe.
        /// </summary>
        private async void PipeClient_MessageReceived(object sender, IPC.PipeMessageEventArgs e)
        {
            try
            {
                // Only process messages if this is the active widget instance
                var activeWidget = App.GetActiveGamingWidget();
                if (activeWidget != null && activeWidget != this)
                {
                    Logger.Debug("Widget received pipe message but this is NOT the active instance. Ignoring.");
                    return;
                }

                // Parse the JSON message to ValueSet
                var message = ParsePipeMessageToValueSet(e.Message);
                if (message == null)
                {
                    Logger.Warn("Failed to parse pipe message");
                    return;
                }

                Logger.Debug($"Widget received pipe message: Function={message["Function"]}");

                // Check for focus widget request from helper
                if (message.TryGetValue("Function", out object funcObj) &&
                    Convert.ToInt32(funcObj) == (int)Shared.Enums.Function.Labs_FocusWidget)
                {
                    Logger.Info("Focus widget request received from helper via pipe");
                    await FocusThisWidgetAsync();
                    return;
                }

                // Check for Quick Metrics push from helper
                if (message.TryGetValue("Function", out object qmFuncObj) &&
                    Convert.ToInt32(qmFuncObj) == (int)Shared.Enums.Function.QuickMetrics)
                {
                    if (message.TryGetValue("Content", out object content) && content is string metricsJson)
                    {
                        UpdateQuickMetrics(metricsJson);
                    }
                    return;
                }

                // Skip TDP and CurrentTDP updates during Sticky TDP reapply
                if (isStickyTDPReapplying && message.ContainsKey("Function"))
                {
                    var function = Convert.ToInt32(message["Function"]);
                    if (function == (int)Shared.Enums.Function.TDP || function == (int)Shared.Enums.Function.CurrentTDP)
                    {
                        Logger.Debug("Skipping TDP/CurrentTDP pipe update during Sticky TDP reapply");
                        return;
                    }
                }

                // Set flag to prevent auto-save when helper updates slider values
                isApplyingHelperUpdate = true;
                try
                {
                    // Handle the message via the properties system
                    properties.HandlePipeMessage(message);
                    await Task.Delay(50);
                }
                finally
                {
                    isApplyingHelperUpdate = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing pipe message from helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses a pipe JSON message into a ValueSet.
        /// </summary>
        private Windows.Foundation.Collections.ValueSet ParsePipeMessageToValueSet(string json)
        {
            try
            {
                var result = new Windows.Foundation.Collections.ValueSet();
                json = json.Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}"))
                    return null;

                // Simple JSON parsing
                var matches = System.Text.RegularExpressions.Regex.Matches(json,
                    @"""(\w+)""\s*:\s*(""[^""\\]*(\\.[^""\\]*)*""|-?\d+\.?\d*|true|false|null|\{[^{}]*\}|\[[^\[\]]*\])");

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var key = match.Groups[1].Value;
                    var value = match.Groups[2].Value;

                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        result[key] = value.Substring(1, value.Length - 2)
                            .Replace("\\\"", "\"").Replace("\\\\", "\\")
                            .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
                    }
                    else if (value == "true")
                    {
                        result[key] = true;
                    }
                    else if (value == "false")
                    {
                        result[key] = false;
                    }
                    else if (value == "null")
                    {
                        result[key] = null;
                    }
                    else if (value.StartsWith("{") || value.StartsWith("["))
                    {
                        result[key] = value;
                    }
                    else if (value.Contains("."))
                    {
                        if (double.TryParse(value, out var d))
                            result[key] = d;
                    }
                    else
                    {
                        if (int.TryParse(value, out var i))
                            result[key] = i;
                        else if (long.TryParse(value, out var l))
                            result[key] = l;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing pipe message JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Handles Named Pipe disconnection from the helper.
        /// </summary>
        private async void PipeClient_Disconnected(object sender, EventArgs e)
        {
            Logger.Info("Named pipe disconnected from helper");

            // Ignore disconnects from inactive/unloading widget instances.
            // A new active instance will own reconnection.
            if (isUnloading || App.GetActiveGamingWidget() != this)
            {
                Logger.Info($"Skipping reconnect handling (isUnloading={isUnloading}, isActive={App.GetActiveGamingWidget() == this})");
                return;
            }

            // Unregister handlers
            App.PipeMessageReceived -= PipeClient_MessageReceived;
            App.PipeDisconnected -= PipeClient_Disconnected;

            // Show reconnecting state and trigger guarded reconnect flow.
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                ShowConnectionBanner(BannerState.Reconnecting);
            });

            Logger.Info("Pipe disconnected - starting automatic helper reconnection");
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _ = LaunchHelperWithGuardsAsync("Pipe disconnected");
            });
        }

        /// <summary>
        /// Called when pipe connection is established.
        /// </summary>
        private async Task OnPipeConnectedAsync()
        {
            Logger.Info("=== OnPipeConnectedAsync START ===");
            Logger.Info($"[PIPE] OnPipeConnectedAsync called. Widget hash: {this.GetHashCode()}, App.IsConnected: {App.IsConnected}");

            // Stop reconnection timeout timer - connection established
            StopReconnectionTimeoutTimer();

            // Verify we connected to the correct helper version by reading heartbeat
            // This prevents issues during upgrades where we connect to the old dying helper
            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var heartbeatFile = await localFolder.TryGetItemAsync("helper_heartbeat.json");

                if (heartbeatFile != null)
                {
                    string content = await Windows.Storage.FileIO.ReadTextAsync((Windows.Storage.StorageFile)heartbeatFile);
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(content, @"""version"":""([^""]+)""");

                    if (versionMatch.Success)
                    {
                        string helperVersion = versionMatch.Groups[1].Value;
                        string widgetVersion = GetWidgetVersion();

                        if (helperVersion != widgetVersion)
                        {
                            Logger.Warn($"Connected to wrong helper version: helper={helperVersion}, widget={widgetVersion} - disconnecting and retrying");

                            // Disconnect and trigger reconnection
                            App.PipeClient?.Dispose();

                            // Wait a bit for the old helper to finish exiting
                            await Task.Delay(3000);

                            // Show reconnecting banner and retry
                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            {
                                ShowConnectionBanner(BannerState.Upgrading);
                            });

                            // Retry connection
                            _ = TryConnectPipeAsync();
                            return;
                        }

                        Logger.Info($"Connected to correct helper version: {helperVersion}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Version check failed (may be expected during startup): {ex.Message}");
                // Continue anyway - version check is optional safety measure
            }

            // Register as active widget
            App.RegisterActiveGamingWidget(this);
            Logger.Info("Registered as active widget for pipe communication");

            // Hide the connection banner early - the pipe is connected and UI is already visible.
            // Property sync happens in the background; no need to show "Reconnecting" during it.
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                HideConnectionBanner();
            });

            // Create widget activity and app target tracker if widget is available
            if (widget != null)
            {
                await CreateWidgetActivity();
                await CreateAppTargetTracker();
            }

            // Sync properties now that pipe is connected
            // The helper is the source of truth for hardware state (TDP mode, etc.)
            // Always accept the helper's current LegionPerformanceMode during sync
            // Using App.HasEverConnectedToHelper (static) so it persists across widget instance recreations
            bool isReconnection = App.HasEverConnectedToHelper;
            Logger.Info($"Starting property sync via pipe... (isReconnection={isReconnection})");
            try
            {
                isApplyingHelperUpdate = true;

                // Never suppress LegionPerformanceMode - always accept helper's current TDP mode
                // The helper loads the correct mode from its profile on startup

                await properties.Sync();
                Logger.Info("Property sync via pipe completed successfully.");

                RegisterChillFPSHandlers();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during property sync via pipe: {ex}");
            }
            finally
            {
                isApplyingHelperUpdate = false;
            }

            try
            {
                Logger.Info("[PIPE] Starting post-sync initialization...");
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    properties.StopPendingUpdates();
                });

                Logger.Info("[PIPE] Sending OSD config to helper...");
                SendOSDConfigToHelper();

                // Don't apply profile TDP mode to helper - the helper is the source of truth
                // It already has the correct mode from its own profile (global.xml)
                Logger.Info("[PIPE] Skipping profile TDP mode apply - helper is source of truth");

                // Sync TDPModeComboBox with helper's LegionPerformanceMode
                // This is needed because LegionPerformanceModeComboBox_SelectionChanged skips
                // TDPModeComboBox sync during isInitialSync
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (LegionPerformanceModeComboBox != null && TDPModeComboBox != null)
                    {
                        if (legionGoDetected?.Value == true)
                        {
                            if (TDPModeComboBox.SelectedIndex != LegionPerformanceModeComboBox.SelectedIndex)
                            {
                                Logger.Info($"[PIPE] Syncing TDPModeComboBox to match helper's mode: index {LegionPerformanceModeComboBox.SelectedIndex}");
                                lastTDPModeIndex = LegionPerformanceModeComboBox.SelectedIndex;
                                TDPModeComboBox.SelectedIndex = LegionPerformanceModeComboBox.SelectedIndex;
                            }
                            else
                            {
                                // Always sync lastTDPModeIndex even when indices already match
                                // to prevent stale values from a previous session
                                lastTDPModeIndex = TDPModeComboBox.SelectedIndex;
                            }

                            // Initialize savedCustomTDP from helper's synced TDP value
                            // This prevents stale savedCustomTDP (default 15W) from overriding
                            // the helper's actual TDP when TDPModeComboBox_SelectionChanged fires
                            if (IsCustomTdpModeIndex(LegionPerformanceModeComboBox.SelectedIndex) && tdp != null)
                            {
                                savedCustomTDP = tdp.Value;
                                Logger.Info($"[PIPE] Initialized savedCustomTDP from helper's TDP: {savedCustomTDP}W");
                            }
                        }
                        else
                        {
                            // Generic device: sync lastTDPModeIndex from current ComboBox state
                            // to prevent stale values from causing the first mode change to be skipped
                            lastTDPModeIndex = TDPModeComboBox.SelectedIndex;
                            Logger.Info($"[PIPE] Synced lastTDPModeIndex for generic device: {lastTDPModeIndex}");
                        }
                    }

                    // Update the TDP slider to show helper's actual TDP value
                    // The slider may have been set to a stale profile value during LoadProfileSettings
                    if (tdp != null && TDPSlider != null)
                    {
                        double helperTDP = tdp.Value;
                        if (Math.Abs(TDPSlider.Value - helperTDP) > 0.5)
                        {
                            Logger.Info($"[PIPE] Updating TDP slider from stale {TDPSlider.Value}W to helper's {helperTDP}W");
                            TDPSlider.Value = helperTDP;
                        }
                    }

                    // Update TDP display text and enabled state based on current mode
                    // Without this, TDPValueText/CurrentTDPValueText show stale "Balanced mode" from XAML defaults
                    UpdateTDPSliderEnabledState();
                    Logger.Info($"[PIPE] Updated TDP slider enabled state after sync");
                });

                // Send Quick Metrics and Screen Saver enabled states to helper (fire-and-forget)
                Logger.Info("[PIPE] Sending Quick Metrics and Screen Saver enabled states to helper...");
                SendQuickMetricsEnabledToHelper();
                SendScreenSaverEnabledToHelper();
                SendSidebarMenuEnabledToHelper();

                await Task.Delay(200);
                isInitialSync = false;
                App.HasEverConnectedToHelper = true;
                Logger.Info("Initial sync via pipe complete - profile saves are now enabled");

                Logger.Info("[PIPE] About to hide connection banner and update profile display...");
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Logger.Info("[PIPE] Inside dispatcher - calling HideConnectionBanner()");
                    HideConnectionBanner();

                    // Update current profile's TDP and mode from helper's synced values
                    // This prevents stale profile values from being loaded in subsequent LoadProfileSettings calls
                    if (tdp != null && !string.IsNullOrEmpty(currentProfileName))
                    {
                        var profile = GetProfile(currentProfileName);
                        if (profile != null && legionPerformanceMode != null)
                        {
                            int helperMode = legionPerformanceMode.Value;
                            double helperTDP = tdp.Value;
                            if (profile.LegionPerformanceMode != helperMode || Math.Abs(profile.TDP - helperTDP) > 0.5)
                            {
                                Logger.Info($"[PIPE] Syncing profile '{currentProfileName}' with helper: TDP {profile.TDP}→{helperTDP}W, Mode {profile.LegionPerformanceMode}→{helperMode}");
                                profile.LegionPerformanceMode = helperMode;
                                profile.TDP = helperTDP;
                                SaveProfileToStorage(currentProfileName, profile);
                            }
                        }
                    }

                    Logger.Info("[PIPE] Inside dispatcher - calling UpdateProfileDisplay()");
                    UpdateProfileDisplay();
                    RefreshLegionEnhancedRemapUi();
                    Logger.Info("[PIPE] Inside dispatcher - post-sync UI updates complete");
                });
                Logger.Info("[PIPE] Post-sync initialization complete");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in post-sync initialization via pipe: {ex}");
                Logger.Error($"[PIPE] Stack trace: {ex.StackTrace}");
                // Show error banner so user knows something went wrong
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ShowConnectionBanner(BannerState.Disconnected);
                });
            }

            Logger.Info("=== OnPipeConnectedAsync END ===");
        }

        // Lossless Scaling Helper Methods

        private async void UpdateLosslessScalingStatus()
        {
            try
            {
                bool isInstalled = losslessScalingInstalled?.Value ?? false;
                bool isRunning = losslessScalingRunning?.Value ?? false;

                Logger.Info($"UpdateLosslessScalingStatus called. Installed: {isInstalled}, Running: {isRunning}");

                // Marshal UI updates to the dispatcher thread
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        // Check if UI elements exist (may not be loaded yet)
                        if (LosslessScalingStatusText == null || LaunchLosslessScalingButton == null || ShowLosslessScalingWindowButton == null)
                        {
                            Logger.Warn("LosslessScaling UI elements not loaded yet, skipping status update");
                            return;
                        }

                        // Enable controls only when LS is installed
                        bool enableControls = isInstalled;
                        bool enableSaveButton = isInstalled && isRunning;

                        if (!isInstalled)
                        {
                            LosslessScalingStatusText.Text = "Not Installed";
                            LosslessScalingStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
                            LaunchLosslessScalingButton.Visibility = Visibility.Collapsed;
                            ShowLosslessScalingWindowButton.Visibility = Visibility.Collapsed;
                        }
                        else if (!isRunning)
                        {
                            LosslessScalingStatusText.Text = "Installed (Not Running)";
                            LosslessScalingStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                            LaunchLosslessScalingButton.Visibility = Visibility.Visible;
                            ShowLosslessScalingWindowButton.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            LosslessScalingStatusText.Text = "Installed and Running";
                            LosslessScalingStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Green);
                            LaunchLosslessScalingButton.Visibility = Visibility.Collapsed;
                            ShowLosslessScalingWindowButton.Visibility = Visibility.Visible;
                        }

                        // Enable/disable all Lossless Scaling controls
                        if (LosslessScalingEnabledToggle != null) LosslessScalingEnabledToggle.IsEnabled = enableControls;
                        if (LosslessScalingAutoScaleToggle != null) LosslessScalingAutoScaleToggle.IsEnabled = enableControls;
                        if (LosslessScalingAutoScaleDelaySlider != null) LosslessScalingAutoScaleDelaySlider.IsEnabled = enableControls;
                        if (LosslessScalingScalingTypeComboBox != null) LosslessScalingScalingTypeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingFrameGenTypeComboBox != null) LosslessScalingFrameGenTypeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingLSFG3ModeComboBox != null) LosslessScalingLSFG3ModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingLSFG3MultiplierComboBox != null) LosslessScalingLSFG3MultiplierComboBox.IsEnabled = enableControls;
                        if (LosslessScalingLSFG3TargetSlider != null) LosslessScalingLSFG3TargetSlider.IsEnabled = enableControls;
                        if (LosslessScalingLSFG2ModeComboBox != null) LosslessScalingLSFG2ModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingFlowScaleSlider != null) LosslessScalingFlowScaleSlider.IsEnabled = enableControls;
                        if (LosslessScalingSizeToggle != null) LosslessScalingSizeToggle.IsEnabled = enableControls;
                        if (LosslessScalingSaveSettingsButton != null)
                        {
                            LosslessScalingSaveSettingsButton.IsEnabled = enableSaveButton;
                            // Update XY navigation to skip disabled Save button
                            LosslessScalingEnabledToggle.XYFocusDown = enableSaveButton ? LosslessScalingSaveSettingsButton : (DependencyObject)LosslessScalingAutoScaleToggle;
                            LosslessScalingAutoScaleToggle.XYFocusUp = enableSaveButton ? LosslessScalingSaveSettingsButton : (DependencyObject)LosslessScalingEnabledToggle;
                        }
                        if (LosslessScalingCreateProfileButton != null)
                        {
                            bool enableCreateProfile = enableControls && HasValidGame(currentGameName);
                            LosslessScalingCreateProfileButton.IsEnabled = enableCreateProfile;

                            // Update XY navigation for Scale toggle based on Create Profile button state
                            // When Create Profile is disabled, Scale should go up to Launch/ShowWindow button
                            if (isRunning)
                            {
                                // Show Window is visible
                                LosslessScalingEnabledToggle.XYFocusUp = enableCreateProfile ? LosslessScalingCreateProfileButton : (DependencyObject)ShowLosslessScalingWindowButton;
                            }
                            else if (isInstalled)
                            {
                                // Launch is visible
                                LosslessScalingEnabledToggle.XYFocusUp = enableCreateProfile ? LosslessScalingCreateProfileButton : (DependencyObject)LaunchLosslessScalingButton;
                            }
                            else
                            {
                                // Neither button visible, go to nav
                                LosslessScalingEnabledToggle.XYFocusUp = ScalingNavItem;
                            }
                        }

                        // New Scaling Algorithm controls
                        if (LosslessScalingSharpnessSlider != null) LosslessScalingSharpnessSlider.IsEnabled = enableControls;
                        if (LosslessScalingFSROptimizeToggle != null) LosslessScalingFSROptimizeToggle.IsEnabled = enableControls;
                        if (LosslessScalingAnime4KSizeComboBox != null) LosslessScalingAnime4KSizeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingAnime4KVRSToggle != null) LosslessScalingAnime4KVRSToggle.IsEnabled = enableControls;
                        if (LosslessScalingScaleModeComboBox != null) LosslessScalingScaleModeComboBox.IsEnabled = enableControls;
                        if (LosslessScalingScaleFactorSlider != null) LosslessScalingScaleFactorSlider.IsEnabled = enableControls;
                        if (LosslessScalingAspectRatioComboBox != null) LosslessScalingAspectRatioComboBox.IsEnabled = enableControls;

                        Logger.Info("LosslessScaling status UI updated successfully");
                    }
                    catch (Exception innerEx)
                    {
                        Logger.Error($"Error updating LosslessScaling status UI: {innerEx.Message}");
                        Logger.Error($"Stack trace: {innerEx.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateLosslessScalingStatus: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private async void LaunchLosslessScalingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Launch Lossless Scaling button clicked");
                LaunchLosslessScalingButton.Content = "Launching...";
                LaunchLosslessScalingButton.IsEnabled = false;

                // Trigger launch via the helper service (which has permissions to launch exe directly)
                // Reset to false first, then set to true to ensure the change is detected
                losslessScalingLaunch.SetValue(false);
                losslessScalingLaunch.SetValue(true);
                Logger.Info("Sent launch request to helper");

                // Wait a bit and update status
                await Task.Delay(3000);
                UpdateLosslessScalingStatus();
                LaunchLosslessScalingButton.Content = "Launch";
                LaunchLosslessScalingButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Lossless Scaling: {ex.Message}");
                LaunchLosslessScalingButton.Content = "Launch";
                LaunchLosslessScalingButton.IsEnabled = true;
            }
        }

        private void ShowLosslessScalingWindowButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Show Lossless Scaling Window button clicked");
                // Reset to false first, then set to true to ensure the change is detected
                losslessScalingBringToForeground.SetValue(false);
                losslessScalingBringToForeground.SetValue(true);
                Logger.Info("Sent bring to foreground request to helper");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error showing Lossless Scaling window: {ex.Message}");
            }
        }

        private void LosslessScalingStatus_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update status when installed/running state changes
            UpdateLosslessScalingStatus();
        }

        private void RunningGame_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (runningGame?.Value != null && runningGame.Value.IsValid())
                {
                    string exePath = runningGame.Value.GameId.Path;
                    string iconPath = runningGame.Value.GameId.IconPath;

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // Check if this is the same game (preserve cached icon path if new one is empty)
                        bool isSameGame = exePath.Equals(currentGameExePath, StringComparison.OrdinalIgnoreCase);

                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            // New icon path provided - cache it
                            currentGameIconPath = iconPath;
                        }
                        else if (isSameGame && !string.IsNullOrEmpty(currentGameIconPath))
                        {
                            // Same game but no icon path in update - use cached path
                            iconPath = currentGameIconPath;
                            Logger.Info($"Using cached icon path for {exePath}");
                        }

                        currentGameExePath = exePath;
                        Logger.Info($"Updated currentGameExePath: {currentGameExePath}");

                        // Load the game icon for the Profiles tab
                        // Use helper-extracted icon if available, otherwise fall back to Steam lookup
                        LoadCurrentGameIcon(exePath, iconPath);

                        // Also update the Default Game Profile icon (may have been empty during initial sync)
                        UpdateDefaultProfileGameIcon();
                    }
                    else
                    {
                        currentGameExePath = "";
                        currentGameIconPath = "";
                        Logger.Info("Cleared currentGameExePath (no path in RunningGame)");

                        // Clear the game icon
                        LoadCurrentGameIcon(null, null);
                    }
                }
                else
                {
                    currentGameExePath = "";
                    currentGameIconPath = "";
                    Logger.Info("Cleared currentGameExePath (no running game)");

                    // Clear the game icon
                    LoadCurrentGameIcon(null, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in RunningGame_PropertyChanged: {ex.Message}");
            }
        }

        // Conflict resolution: Lossless Scaling Frame Gen vs AMD Fluid Motion Frames
        private bool isHandlingConflict = false; // Prevents infinite loop

        private void LosslessScalingFrameGenTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedType = LosslessScalingFrameGenTypeComboBox.SelectedItem as string ?? "Off";
                bool isFrameGenEnabled = selectedType != "Off";
                bool showLSFG3 = selectedType == "LSFG3";
                bool showLSFG2 = selectedType == "LSFG2";

                // Show/hide LSFG3 settings card
                if (LSFG3SettingsCard != null)
                {
                    LSFG3SettingsCard.Visibility = showLSFG3 ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide LSFG2 settings card
                if (LSFG2SettingsCard != null)
                {
                    LSFG2SettingsCard.Visibility = showLSFG2 ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update XY navigation based on visible controls
                if (showLSFG3)
                {
                    // LSFG3: FrameGen -> LSFG3 Mode
                    LosslessScalingFrameGenTypeComboBox.XYFocusDown = LosslessScalingLSFG3ModeComboBox;
                }
                else if (showLSFG2)
                {
                    // LSFG2: FrameGen -> LSFG2 Mode
                    LosslessScalingFrameGenTypeComboBox.XYFocusDown = LosslessScalingLSFG2ModeComboBox;
                }
                else
                {
                    // No extra controls - remove XYFocusDown (end of list)
                    LosslessScalingFrameGenTypeComboBox.XYFocusDown = null;
                }

                // Handle conflict with AMD Fluid Motion Frames
                if (isHandlingConflict) return;

                if (isFrameGenEnabled && AMDFluidMotionFrameToggle.IsOn)
                {
                    Logger.Info("Lossless Scaling Frame Gen enabled - auto-disabling AMD Fluid Motion Frames");
                    isHandlingConflict = true;
                    AMDFluidMotionFrameToggle.IsOn = false;
                    isHandlingConflict = false;

                    // Show conflict warning
                    if (LSConflictWarningBorder != null && LSConflictWarningText != null)
                    {
                        LSConflictWarningBorder.Visibility = Visibility.Visible;
                        LSConflictWarningText.Text = "AMD Fluid Motion Frames has been automatically disabled because it conflicts with Lossless Scaling Frame Generation.";
                    }
                }
                else if (!isFrameGenEnabled)
                {
                    // Hide warning when LS Frame Gen is disabled
                    if (LSConflictWarningBorder != null)
                    {
                        LSConflictWarningBorder.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingFrameGenTypeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void LosslessScalingScalingTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedType = LosslessScalingScalingTypeComboBox.SelectedItem as string ?? "Off";

                // Show/hide Sharpness panel (for FSR, NIS, SGSR, BCAS)
                bool showSharpness = selectedType == "FSR" || selectedType == "NIS" || selectedType == "SGSR" || selectedType == "BCAS";
                bool showFSROptimize = selectedType == "FSR";
                bool showAnime4K = selectedType == "Anime4K";

                if (LosslessScalingSharpnessPanel != null)
                {
                    LosslessScalingSharpnessPanel.Visibility = showSharpness ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide FSR Optimize panel (FSR only)
                if (LosslessScalingFSROptimizePanel != null)
                {
                    LosslessScalingFSROptimizePanel.Visibility = showFSROptimize ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Anime4K panel
                if (LosslessScalingAnime4KPanel != null)
                {
                    LosslessScalingAnime4KPanel.Visibility = showAnime4K ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update XY navigation based on visible controls
                // ScalingTypeComboBox down: Sharpness -> FSROptimize -> Anime4K -> ScaleMode
                if (showFSROptimize)
                {
                    // FSR: Type -> Sharpness -> FSROptimize -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingSharpnessSlider;
                    LosslessScalingSharpnessSlider.XYFocusDown = LosslessScalingFSROptimizeToggle;
                    LosslessScalingFSROptimizeToggle.XYFocusDown = LosslessScalingScaleModeComboBox;
                }
                else if (showSharpness)
                {
                    // NIS, SGSR, BCAS: Type -> Sharpness -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingSharpnessSlider;
                    LosslessScalingSharpnessSlider.XYFocusDown = LosslessScalingScaleModeComboBox;
                }
                else if (showAnime4K)
                {
                    // Anime4K: Type -> Size -> VRS -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingAnime4KSizeComboBox;
                }
                else
                {
                    // No extra controls: Type -> ScaleMode
                    LosslessScalingScalingTypeComboBox.XYFocusDown = LosslessScalingScaleModeComboBox;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingScalingTypeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void LosslessScalingScaleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedMode = LosslessScalingScaleModeComboBox.SelectedItem as string ?? "Auto";
                bool showAuto = selectedMode == "Auto";
                bool showCustom = selectedMode == "Custom";

                // Show/hide Auto mode panel
                if (LosslessScalingAutoModePanel != null)
                {
                    LosslessScalingAutoModePanel.Visibility = showAuto ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Custom mode panel
                if (LosslessScalingCustomModePanel != null)
                {
                    LosslessScalingCustomModePanel.Visibility = showCustom ? Visibility.Visible : Visibility.Collapsed;
                }

                // Update XY navigation based on visible controls
                if (showAuto)
                {
                    // Auto: ScaleMode -> AspectRatio -> FrameGen
                    LosslessScalingScaleModeComboBox.XYFocusDown = LosslessScalingAspectRatioComboBox;
                    LosslessScalingAspectRatioComboBox.XYFocusDown = LosslessScalingFrameGenTypeComboBox;
                }
                else if (showCustom)
                {
                    // Custom: ScaleMode -> ScaleFactor -> FrameGen
                    LosslessScalingScaleModeComboBox.XYFocusDown = LosslessScalingScaleFactorSlider;
                    LosslessScalingScaleFactorSlider.XYFocusDown = LosslessScalingFrameGenTypeComboBox;
                }
                else
                {
                    // No extra controls: ScaleMode -> FrameGen
                    LosslessScalingScaleModeComboBox.XYFocusDown = LosslessScalingFrameGenTypeComboBox;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingScaleModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void LosslessScalingLSFG3ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string selectedMode = LosslessScalingLSFG3ModeComboBox.SelectedItem as string ?? "FIXED";
                bool isAdaptive = selectedMode == "ADAPTIVE";

                // Hide multiplier when Adaptive mode is selected
                if (LosslessScalingLSFG3MultiplierPanel != null)
                {
                    LosslessScalingLSFG3MultiplierPanel.Visibility = isAdaptive ? Visibility.Collapsed : Visibility.Visible;
                }

                // Update XY navigation based on visible controls
                if (isAdaptive)
                {
                    // ADAPTIVE: Mode -> Target -> FlowScale -> SizeToggle (skip Multiplier)
                    LosslessScalingLSFG3ModeComboBox.XYFocusDown = LosslessScalingLSFG3TargetSlider;
                    LosslessScalingLSFG3TargetSlider.XYFocusUp = LosslessScalingLSFG3ModeComboBox;
                }
                else
                {
                    // FIXED: Mode -> Multiplier -> Target -> FlowScale -> SizeToggle
                    LosslessScalingLSFG3ModeComboBox.XYFocusDown = LosslessScalingLSFG3MultiplierComboBox;
                    LosslessScalingLSFG3TargetSlider.XYFocusUp = LosslessScalingLSFG3MultiplierComboBox;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingLSFG3ModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void AMDFluidMotionFrameToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                if (isHandlingConflict) return;

                string selectedType = LosslessScalingFrameGenTypeComboBox.SelectedItem as string ?? "Off";
                bool isLSFrameGenEnabled = selectedType != "Off";

                if (AMDFluidMotionFrameToggle.IsOn && isLSFrameGenEnabled)
                {
                    Logger.Info("AMD Fluid Motion Frames enabled - auto-disabling Lossless Scaling Frame Gen");
                    isHandlingConflict = true;
                    LosslessScalingFrameGenTypeComboBox.SelectedIndex = 0; // Set to "Off"
                    isHandlingConflict = false;

                    // Show conflict warning
                    if (LSConflictWarningBorder != null && LSConflictWarningText != null)
                    {
                        LSConflictWarningBorder.Visibility = Visibility.Visible;
                        LSConflictWarningText.Text = "Lossless Scaling Frame Generation has been automatically disabled because it conflicts with AMD Fluid Motion Frames.";
                    }
                }
                else if (!AMDFluidMotionFrameToggle.IsOn)
                {
                    // Hide warning if both are now off
                    if (LSConflictWarningBorder != null && !isLSFrameGenEnabled)
                    {
                        LSConflictWarningBorder.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in AMDFluidMotionFrameToggle_Toggled: {ex.Message}");
            }
        }

        private void LosslessScalingCurrentProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (LosslessScalingCurrentProfileText != null && losslessScalingCurrentProfile != null)
                    {
                        LosslessScalingCurrentProfileText.Text = losslessScalingCurrentProfile.Value ?? "Default";
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingCurrentProfile_PropertyChanged: {ex.Message}");
            }
        }

        private void LosslessScalingCreateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(currentGameName))
                {
                    // Format: "GameName<||>WindowFilter" - use game name as window title filter for Lossless Scaling profile matching
                    string profileData = $"{currentGameName}<||>{currentGameName}";
                    losslessScalingCreateProfile.SetValue(profileData);
                    Logger.Info($"Creating Lossless Scaling profile for: {currentGameName}");
                }
                else
                {
                    Logger.Warn("Cannot create profile - no game detected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingCreateProfileButton_Click: {ex.Message}");
            }
        }

        private void LosslessScalingSaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Trigger save and restart
                losslessScalingSaveAndRestart.SetValue(true);
                Logger.Info("Saving Lossless Scaling settings and restarting");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LosslessScalingSaveSettingsButton_Click: {ex.Message}");
            }
        }
    }
}

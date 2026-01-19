using Microsoft.Gaming.XboxGameBar;
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
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
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
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.QuickSettings;
using Shared.Enums;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

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
        // OS Power Mode (0=Best Power Efficiency, 1=Balanced, 2=Best Performance)
        public int OSPowerMode { get; set; } = 1;
        // Legion Performance Mode (1=Quiet, 2=Balanced, 3=Performance, 255=Custom)
        public int LegionPerformanceMode { get; set; } = 2;
        // TDP Boost toggle state (per-profile)
        public bool TDPBoostEnabled { get; set; } = false;
        // HDR and Resolution settings (per-profile)
        public bool HDREnabled { get; set; } = false;
        public string Resolution { get; set; } = "";
        // Sticky TDP settings (per-profile)
        public bool StickyTDPEnabled { get; set; } = true;
        public int StickyTDPInterval { get; set; } = 5;

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
                OSPowerMode = this.OSPowerMode,
                LegionPerformanceMode = this.LegionPerformanceMode,
                TDPBoostEnabled = this.TDPBoostEnabled,
                HDREnabled = this.HDREnabled,
                Resolution = this.Resolution,
                StickyTDPEnabled = this.StickyTDPEnabled,
                StickyTDPInterval = this.StickyTDPInterval
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
        /// <summary>Keyboard key codes (up to 5 keys)</summary>
        public List<int> KeyboardKeys { get; set; } = new List<int>();
        /// <summary>Mouse button code (1-7)</summary>
        public int MouseButton { get; set; } = 0;

        /// <summary>
        /// Returns true if this mapping represents "no mapping" / default state.
        /// A mapping is default if Type=0 (Gamepad) with GamepadAction=0 (Disabled).
        /// </summary>
        public bool IsDefault => Type == 0 && GamepadAction == 0;

        public ButtonMapping Clone() => new ButtonMapping
        {
            Type = this.Type,
            GamepadAction = this.GamepadAction,
            KeyboardKeys = new List<int>(this.KeyboardKeys),
            MouseButton = this.MouseButton
        };

        /// <summary>
        /// Serializes to JSON string format for IPC/storage.
        /// Format: {"Type":0,"GamepadAction":5,"KeyboardKeys":[4,5],"MouseButton":0}
        /// </summary>
        public string ToJson()
        {
            var keys = KeyboardKeys.Count > 0 ? string.Join(",", KeyboardKeys) : "";
            return $"{{\"Type\":{Type},\"GamepadAction\":{GamepadAction},\"KeyboardKeys\":[{keys}],\"MouseButton\":{MouseButton}}}";
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

                // Parse MouseButton
                var mouseMatch = System.Text.RegularExpressions.Regex.Match(json, "\"MouseButton\"\\s*:\\s*(-?\\d+)");
                if (mouseMatch.Success && int.TryParse(mouseMatch.Groups[1].Value, out int mouseButton))
                    result.MouseButton = mouseButton;

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
        public XboxGameBarWidgetActivity WidgetActivity { get { return widgetActivity; } }
        private XboxGameBarAppTargetTracker appTargetTracker = null;

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
        private const int MinLaunchIntervalMs = 10000; // 10 seconds between launch attempts
        private const int HeartbeatStaleThresholdSeconds = 5;
        private const int ReconnectionTimeoutSeconds = 5;
        private DispatcherTimer reconnectionTimeoutTimer = null;

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

        // AutoTDP properties
        private readonly AutoTDPEnabledProperty autoTDPEnabled;
        private readonly AutoTDPTargetFPSProperty autoTDPTargetFPS;
        private readonly AutoTDPCurrentFPSProperty autoTDPCurrentFPS;
        private readonly AutoTDPMinTDPProperty autoTDPMinTDP;
        private readonly AutoTDPMaxTDPProperty autoTDPMaxTDP;
        private readonly TDPLimitsProperty tdpLimits;
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

        // Controller profile state
        private ControllerProfile globalControllerProfile = new ControllerProfile();
        private ControllerProfile gameControllerProfile = new ControllerProfile();
        private bool isLoadingControllerProfile = false;
        private bool isSwitchingControllerProfile = false;
        private DateTime lastProfileApplyTime = DateTime.MinValue; // Prevents duplicate sends from queued UI events
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
        private bool _saveStickyTDP = false;

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
        private bool SaveStickyTDP => _saveStickyTDP;

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
            Logger.Info("GamingWidget constructor called - creating new instance.");

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

            // Set up callbacks for TDP method availability
            winRing0Available.SetAvailabilityCallback(UpdateWinRing0Visibility);
            pawnIOInstalled.SetInstalledCallback(UpdatePawnIOInstalledUI);
            vigemBusInstalled.SetInstalledCallback(UpdateViGEmBusInstalledUI);

            // AutoTDP properties
            autoTDPEnabled = new AutoTDPEnabledProperty(false);
            autoTDPTargetFPS = new AutoTDPTargetFPSProperty(60);
            autoTDPCurrentFPS = new AutoTDPCurrentFPSProperty(0);
            autoTDPMinTDP = new AutoTDPMinTDPProperty(8);
            autoTDPMaxTDP = new AutoTDPMaxTDPProperty(30);
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
                autoTDPEnabled,
                autoTDPTargetFPS,
                autoTDPCurrentFPS,
                autoTDPMinTDP,
                autoTDPMaxTDP,
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
            Logger.Info($"[TIMING] Constructor total: {constructorTimer.ElapsedMilliseconds}ms");
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
            foreach (var item in MainNavigationView.MenuItems)
            {
                if (item is NavigationViewItem navItem)
                {
                    navItem.GotFocus += NavItem_GotFocus;
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

            // System tab - Device TDP Limits card
            TDPLimitsExpandButton.GotFocus += Control_GotFocus;
            TDPLimitsExpandButton.LostFocus += Control_LostFocus;
            TDPLimitsMinSlider.GotFocus += Control_GotFocus;
            TDPLimitsMinSlider.LostFocus += Control_LostFocus;
            TDPLimitsMaxSlider.GotFocus += Control_GotFocus;
            TDPLimitsMaxSlider.LostFocus += Control_LostFocus;

            // System tab - Power Plan Settings card
            PowerPlanExpandButton.GotFocus += Control_GotFocus;
            PowerPlanExpandButton.LostFocus += Control_LostFocus;
            ACPowerPlanComboBox.GotFocus += Control_GotFocus;
            ACPowerPlanComboBox.LostFocus += Control_LostFocus;
            DCPowerPlanComboBox.GotFocus += Control_GotFocus;
            DCPowerPlanComboBox.LostFocus += Control_LostFocus;
            PowerPlanAutoSwitchToggle.GotFocus += Control_GotFocus;
            PowerPlanAutoSwitchToggle.LostFocus += Control_LostFocus;

            // System tab - OSD Customization card
            OSDCustomizeExpandButton.GotFocus += Control_GotFocus;
            OSDCustomizeExpandButton.LostFocus += Control_LostFocus;

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
            Logger.Info($"GamingWidget_Loaded called. Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, App.Connection is null: {App.Connection == null}");

            // Set initial navigation selection
            if (MainNavigationView.MenuItems.Count > 0)
            {
                MainNavigationView.SelectedItem = MainNavigationView.MenuItems[0];
            }

            // Load profile customization settings
            LoadProfileCustomizationSettings();

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

            // Set initial visibility for Global Profile display mode
            if (PowerSourceProfileToggle.IsOn)
            {
                GlobalProfileSimple.Visibility = Visibility.Collapsed;
                GlobalProfileACDC.Visibility = Visibility.Visible;
            }
            else
            {
                GlobalProfileSimple.Visibility = Visibility.Visible;
                GlobalProfileACDC.Visibility = Visibility.Collapsed;
            }

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
                        bool hasExistingProfile = false;

                        if (PowerSourceProfileToggle?.IsOn == true)
                        {
                            hasExistingProfile = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_AC");
                        }
                        else
                        {
                            hasExistingProfile = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}");
                        }

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
                    bool hasExistingProfile = false;

                    if (PowerSourceProfileToggle?.IsOn == true)
                    {
                        hasExistingProfile = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_AC");
                    }
                    else
                    {
                        hasExistingProfile = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}");
                    }

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

            if (PowerSourceProfileToggle?.IsOn == true)
            {
                // Check if game profiles exist in storage
                if (!settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_AC"))
                {
                    // Initialize new game profiles from current AC/DC profiles
                    gameACProfile = acProfile.Clone();
                    gameDCProfile = dcProfile.Clone();
                    SaveProfileToStorage($"Game_{currentGameName}_AC", gameACProfile);
                    SaveProfileToStorage($"Game_{currentGameName}_DC", gameDCProfile);
                    Logger.Info($"Initialized game AC/DC profiles for {currentGameName}");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_AC", gameACProfile);
                    LoadProfileFromStorage($"Game_{currentGameName}_DC", gameDCProfile);
                    Logger.Info($"Loaded existing game AC/DC profiles for {currentGameName}");
                }
            }
            else
            {
                // Check if game profile exists in storage
                if (!settings.Containers.ContainsKey($"Profile_Game_{currentGameName}"))
                {
                    // Initialize new game profile from global profile
                    gameProfile = globalProfile.Clone();
                    SaveProfileToStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Initialized game profile for {currentGameName} from global");
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
            // or when Default Game Profile is active (to avoid contaminating user's profile with DGP values)
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync || defaultGameProfileEnabled?.Value == true)
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
                    // Save the setting
                    SavePerformanceOverlaySetting();
                }
            }
        }

        private void LoadPerformanceOverlaySetting()
        {
            try
            {
                if (PerformanceOverlayComboBox == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("PerformanceOverlayLevel", out object val) && val is int level)
                {
                    if (level >= 0 && level < PerformanceOverlayComboBox.Items.Count)
                    {
                        PerformanceOverlayComboBox.SelectedIndex = level;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading PerformanceOverlay setting: {ex.Message}");
            }
        }

        private void SavePerformanceOverlaySetting()
        {
            try
            {
                if (PerformanceOverlayComboBox == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["PerformanceOverlayLevel"] = PerformanceOverlayComboBox.SelectedIndex;
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
            Logger.Info($"PowerSourceProfileToggle toggled to: {PowerSourceProfileToggle.IsOn}");

            // Save the setting
            SavePowerSourceProfileSetting();

            // Toggle Global Profile display mode
            if (PowerSourceProfileToggle.IsOn)
            {
                // Show AC/DC mode, hide simple mode
                GlobalProfileSimple.Visibility = Visibility.Collapsed;
                GlobalProfileACDC.Visibility = Visibility.Visible;
            }
            else
            {
                // Show simple mode, hide AC/DC mode
                GlobalProfileSimple.Visibility = Visibility.Visible;
                GlobalProfileACDC.Visibility = Visibility.Collapsed;
            }

            UpdateActiveProfileIndicator();
        }

        private void LoadPowerSourceProfileSetting()
        {
            try
            {
                if (PowerSourceProfileToggle == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("PowerSourceProfileEnabled", out object val) && val is bool enabled)
                {
                    PowerSourceProfileToggle.IsOn = enabled;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading PowerSourceProfile setting: {ex.Message}");
            }
        }

        private void SavePowerSourceProfileSetting()
        {
            try
            {
                if (PowerSourceProfileToggle == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["PowerSourceProfileEnabled"] = PowerSourceProfileToggle.IsOn;
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
                if (App.Connection != null)
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
                    await App.Connection.SendMessageAsync(tempRequest);

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

                    var response = await App.Connection.SendMessageAsync(targetRequest);
                    if (response != null && response.Message != null)
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

        #region AutoTDP

        private bool isLoadingAutoTDPSettings = false;

        private void AutoTDPToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoTDPToggle == null) return;
            if (isApplyingHelperUpdate) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;
            // Skip during AutoTDP settings load to prevent overwriting saved values
            if (isLoadingAutoTDPSettings) return;

            Logger.Info($"AutoTDP toggled to: {AutoTDPToggle.IsOn}");

            // If enabling AutoTDP on Legion Go and not in Custom mode, switch to Custom mode
            // AutoTDP requires Custom mode to control TDP values directly
            if (AutoTDPToggle.IsOn && legionGoDetected?.Value == true && legionPerformanceMode?.Value != 255)
            {
                Logger.Info($"AutoTDP enabled but Legion Go is in mode {legionPerformanceMode?.Value} (not Custom). Switching to Custom mode.");
                legionPerformanceMode?.SetValue(255);
                // Update the UI dropdown if available
                if (LegionPerformanceModeComboBox != null)
                {
                    // Custom mode is index 3 (Quiet=0, Balanced=1, Performance=2, Custom=3)
                    LegionPerformanceModeComboBox.SelectedIndex = 3;
                }
            }

            // Update XY focus navigation based on toggle state
            UpdateAutoTDPFocusNavigation();

            // Send to helper
            autoTDPEnabled?.SetValue(AutoTDPToggle.IsOn);

            // Save global setting
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["AutoTDPEnabled"] = AutoTDPToggle.IsOn;

            // Save to profile if AutoTDP saving is enabled
            if (SaveAutoTDP && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void UpdateAutoTDPFocusNavigation()
        {
            if (AutoTDPToggle == null) return;

            // When AutoTDP is on, focus down goes to the slider
            // When AutoTDP is off, focus down goes to TdpMethodComboBox
            if (AutoTDPToggle.IsOn && AutoTDPTargetFPSSlider != null)
            {
                AutoTDPToggle.XYFocusDown = AutoTDPTargetFPSSlider;
            }
            else if (TdpMethodComboBox != null)
            {
                AutoTDPToggle.XYFocusDown = TdpMethodComboBox;
            }
        }

        private void AutoTDPTargetFPSSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (AutoTDPTargetFPSSlider == null || AutoTDPToggle == null) return;
            if (isLoadingAutoTDPSettings) return; // Don't save during load
            if (isApplyingHelperUpdate) return;

            int targetFPS = (int)Math.Round(e.NewValue);
            Logger.Info($"AutoTDP target FPS changed to: {targetFPS}");

            // Update display
            if (AutoTDPTargetFPSValue != null)
            {
                AutoTDPTargetFPSValue.Text = $"{targetFPS} FPS";
            }

            // Send to helper
            autoTDPTargetFPS?.SetValue(targetFPS);

            // Save global setting
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["AutoTDPTargetFPS"] = targetFPS;

            // Save to profile if AutoTDP saving is enabled
            if (SaveAutoTDP && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void AutoTDPMinSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (AutoTDPMinSlider == null || AutoTDPToggle == null) return;
            if (isLoadingAutoTDPSettings) return;
            if (isApplyingHelperUpdate) return;

            int minTDP = (int)Math.Round(e.NewValue);

            // Ensure min doesn't exceed max
            if (AutoTDPMaxSlider != null && minTDP > AutoTDPMaxSlider.Value)
            {
                minTDP = (int)AutoTDPMaxSlider.Value;
                AutoTDPMinSlider.Value = minTDP;
                return;
            }

            Logger.Info($"AutoTDP min TDP changed to: {minTDP}W");

            // Update display
            if (AutoTDPMinValue != null)
            {
                AutoTDPMinValue.Text = $"{minTDP}W";
            }

            // Send to helper
            autoTDPMinTDP?.SetValue(minTDP);

            // Save global setting
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["AutoTDPMinTDP"] = minTDP;

            // Save to profile if AutoTDP saving is enabled
            if (SaveAutoTDP && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void AutoTDPMaxSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            // Guard against early XAML initialization calls
            if (AutoTDPMaxSlider == null || AutoTDPToggle == null) return;
            if (isLoadingAutoTDPSettings) return;
            if (isApplyingHelperUpdate) return;

            int maxTDP = (int)Math.Round(e.NewValue);

            // Ensure max doesn't go below min
            if (AutoTDPMinSlider != null && maxTDP < AutoTDPMinSlider.Value)
            {
                maxTDP = (int)AutoTDPMinSlider.Value;
                AutoTDPMaxSlider.Value = maxTDP;
                return;
            }

            Logger.Info($"AutoTDP max TDP changed to: {maxTDP}W");

            // Update display
            if (AutoTDPMaxValue != null)
            {
                AutoTDPMaxValue.Text = $"{maxTDP}W";
            }

            // Send to helper
            autoTDPMaxTDP?.SetValue(maxTDP);

            // Save global setting
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["AutoTDPMaxTDP"] = maxTDP;

            // Save to profile if AutoTDP saving is enabled
            if (SaveAutoTDP && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void LoadAutoTDPSettings()
        {
            isLoadingAutoTDPSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load enabled state (default to OFF if not saved)
                if (settings.Values.TryGetValue("AutoTDPEnabled", out object enabledObj) && enabledObj is bool enabled)
                {
                    AutoTDPToggle.IsOn = enabled;
                }
                else
                {
                    AutoTDPToggle.IsOn = false;
                }

                // Load target FPS
                if (settings.Values.TryGetValue("AutoTDPTargetFPS", out object targetObj) && targetObj is int target)
                {
                    AutoTDPTargetFPSSlider.Value = target;
                    AutoTDPTargetFPSValue.Text = $"{target} FPS";
                }

                // Load min TDP
                if (settings.Values.TryGetValue("AutoTDPMinTDP", out object minObj) && minObj is int minTDP)
                {
                    AutoTDPMinSlider.Value = minTDP;
                    AutoTDPMinValue.Text = $"{minTDP}W";
                    autoTDPMinTDP?.SetValue(minTDP);
                }

                // Load max TDP
                if (settings.Values.TryGetValue("AutoTDPMaxTDP", out object maxObj) && maxObj is int maxTDP)
                {
                    AutoTDPMaxSlider.Value = maxTDP;
                    AutoTDPMaxValue.Text = $"{maxTDP}W";
                    autoTDPMaxTDP?.SetValue(maxTDP);
                }

                // Update focus navigation after loading settings
                UpdateAutoTDPFocusNavigation();
            }
            finally
            {
                isLoadingAutoTDPSettings = false;
            }
        }

        private void LoadStickyTDPSettings()
        {
            isLoadingStickyTDPSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Default to TRUE for new installs (key won't exist)
                bool enabled = true;
                if (settings.Values.TryGetValue("StickyTDPEnabled", out object enabledVal) && enabledVal is bool val)
                {
                    enabled = val;
                }
                StickyTDPToggle.IsOn = enabled;

                // Load interval setting (default 5 seconds)
                if (settings.Values.TryGetValue("StickyTDPInterval", out object intervalVal) && intervalVal is int interval)
                {
                    stickyTDPCheckIntervalSeconds = interval;
                    StickyTDPIntervalSlider.Value = interval;
                    if (StickyTDPIntervalValue != null)
                    {
                        StickyTDPIntervalValue.Text = $"{interval}s";
                    }
                }

                Logger.Info($"Loaded Sticky TDP settings: Enabled={enabled}, Interval={stickyTDPCheckIntervalSeconds}s");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading Sticky TDP settings: {ex.Message}");
            }
            finally
            {
                isLoadingStickyTDPSettings = false;
            }
        }

        #endregion

        #region OSD Customization

        // OSD configuration per level - stores which items are enabled
        // Level 1 (Basic): FPS, Battery, Time - 3 columns
        // Level 2 (Detailed): Time, FPS, Battery, CPU, GPU, Fan - 1 column
        // Level 3 (Full): All options - 1 column
        private Dictionary<int, Dictionary<string, bool>> osdLevelConfig = new Dictionary<int, Dictionary<string, bool>>
        {
            { 1, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", false }, { "Memory", false }, { "VRAM", false }, { "CPU", false }, { "CPUClock", false }, { "GPU", false }, { "GPUClock", false }, { "Fan", false }, { "AutoTDP", false }, { "FrametimeGraph", false } } },
            { 2, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", false }, { "Memory", false }, { "VRAM", false }, { "CPU", true }, { "CPUClock", false }, { "GPU", true }, { "GPUClock", false }, { "Fan", true }, { "AutoTDP", false }, { "FrametimeGraph", true } } },
            { 3, new Dictionary<string, bool> { { "AppName", true }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", true }, { "Memory", true }, { "VRAM", true }, { "CPU", true }, { "CPUClock", true }, { "GPU", true }, { "GPUClock", true }, { "Fan", true }, { "AutoTDP", true }, { "FrametimeGraph", true } } }
        };

        private Dictionary<int, string> osdCustomTags = new Dictionary<int, string>
        {
            { 1, "" },
            { 2, "" },
            { 3, "" }
        };

        // Per-level column settings (Basic=3, Detailed=1, Full=1)
        private Dictionary<int, int> osdLevelColumns = new Dictionary<int, int>
        {
            { 1, 3 },  // Basic: 3 columns
            { 2, 1 },  // Detailed: 1 column
            { 3, 1 }   // Full: 1 column
        };

        // Current OSD customization level (1=Basic, 2=Detailed, 3=Full)
        private int osdCustomizeLevel = 1;

        // Per-level item order (list of item IDs in display order)
        private Dictionary<int, List<string>> osdLevelOrder = new Dictionary<int, List<string>>
        {
            { 1, new List<string> { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" } },
            { 2, new List<string> { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" } },
            { 3, new List<string> { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" } }
        };

        // Per-level item label colors (DEFAULT = use global text color)
        private Dictionary<int, Dictionary<string, string>> osdItemLabelColors = new Dictionary<int, Dictionary<string, string>>
        {
            { 1, new Dictionary<string, string>() },
            { 2, new Dictionary<string, string>() },
            { 3, new Dictionary<string, string>() }
        };

        // Item display names for UI
        private static readonly Dictionary<string, string> osdItemDisplayNames = new Dictionary<string, string>
        {
            { "AppName", "App Name (D3D11, Vulkan, etc.)" },
            { "Time", "Time (12-hour)" },
            { "FPS", "FPS & Frametime" },
            { "Battery", "Battery" },
            { "ControllerBattery", "Controller Battery (L/R)" },
            { "Memory", "Memory (RAM)" },
            { "VRAM", "VRAM (GPU Memory)" },
            { "CPU", "CPU (Usage, Wattage, Temp)" },
            { "CPUClock", "CPU Clock Speed" },
            { "GPU", "GPU (Usage, Wattage, Temp)" },
            { "GPUClock", "GPU Clock Speed" },
            { "Fan", "Fan Speed" },
            { "AutoTDP", "AutoTDP Status" },
            { "TDPLimits", "TDP Limits (SPL/SPPT/FPPT)" },
            { "FrametimeGraph", "Frametime Graph" }
        };

        // Observable collection for OSD items UI
        private ObservableCollection<OSDItemViewModel> osdItemViewModels = new ObservableCollection<OSDItemViewModel>();

        // Global OSD layout settings
        private int osdTextSize = 100;    // Percentage: 50=Small, 100=Medium, 150=Large, 200=X-Large, 250=XX-Large, 300=XXX-Large
        private string osdTextColor = "DYNAMIC";  // DYNAMIC = value-based colors, or hex color code
        private string osdLabelColor = "DEFAULT";  // DEFAULT = use item-specific colors, or hex color code
        private int osdProvider = 0;  // 0=RTSS, 1=AMD
        private int amdOverlayLevel = 0;  // Track AMD overlay level: 0=Off, 1-4=Level 1-4 (can't query from AMD)
        private bool isOSDCustomizeExpanded = false;
        private bool isProfileDetectionExpanded = false;
        private bool isProfileSettingsExpanded = false;
        private bool isTDPLimitsExpanded = false;
        private bool isPowerPlanExpanded = false;
        private bool isColorSettingsExpanded = false;
        private bool isButtonRemappingExpanded = false;
        private bool isGyroSettingsExpanded = false;
        private bool isSavedProfilesExpanded = false;
        private bool isStickDeadzonesExpanded = false;
        private bool isTouchpadVibrationExpanded = false;
        private bool isLightingExpanded = false;
        private bool isFanCurveExpanded = false;
        private bool fanCurveGraphInitialized = false;

        // Display and OSD settings
        private bool adaptiveBrightnessEnabled = false;
        private bool osdPositionShiftEnabled = false;
        private int osdOpacity = 100; // percentage 10-100
        private bool isLoadingOLEDSettings = false;
        private readonly Windows.UI.Xaml.Shapes.Ellipse[] fanCurvePoints = new Windows.UI.Xaml.Shapes.Ellipse[10];
        private int[] currentFanCurveValues = new int[10];
        private int draggedPointIndex = -1;
        private bool isDraggingPoint = false;

        // Legion Go fan curve temperature thresholds (°C) - FIXED by EC at 10°C increments
        private static readonly int[] FanCurveTemperatures = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        // Minimum fan speeds - set to 0 since EC enforces its own thermal protection floor
        // EC override floor: 0-44°C=0%, 45°C=27%, 50°C=40%, 55°C=55%, 60°C=65%, 70°C=85%, 80+°C=100%
        private static readonly int[] FanCurveMinSpeeds = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        // EC Override Floor points: (temperature, minimum fan %) - what the EC enforces regardless of user curve
        private static readonly (int temp, int floor)[] ECFloorPoints = new[]
        {
            (10, 0), (20, 0), (30, 0), (44, 0),
            (45, 27), (50, 40), (55, 55), (60, 65), (70, 85),
            (80, 100), (90, 100), (100, 100)
        };
        // Fan curve preset definitions (values are fan % for temps 10,20,30,40,50,60,70,80,90,100°C)
        private static readonly Dictionary<string, int[]> FanCurvePresets = new Dictionary<string, int[]>
        {
            { "Silent", new int[] { 0, 0, 0, 27, 30, 40, 55, 65, 80, 100 } },       // Silent (Safe)
            { "Balanced", new int[] { 0, 0, 25, 30, 35, 45, 55, 70, 85, 100 } },    // Balanced
            { "Performance", new int[] { 30, 35, 40, 45, 50, 60, 70, 80, 90, 100 } }, // Performance
            { "MaxCooling", new int[] { 40, 45, 50, 55, 60, 70, 80, 90, 100, 100 } }  // Max Cooling
        };
        private string currentFanCurvePreset = "Custom";
        private bool isFanCurvePresetLoading = false;
        private bool isTDPExtrasExpanded = false;
        private bool isCPUExtrasExpanded = false;
        private bool isDebugExpanded = false;
        private bool isLoadingTDPLimits = false;
        private bool isLoadingPowerPlans = false;
        private List<PowerPlanItem> availablePowerPlans = new List<PowerPlanItem>();
        private Guid acPowerPlanGuid = Guid.Empty;
        private Guid dcPowerPlanGuid = Guid.Empty;
        private bool powerPlanAutoSwitch = false; // Default to OFF - will be loaded from settings
        private int deviceTDPMin = 4;
        private int deviceTDPMax = 35;
        private DispatcherTimer tdpLimitsDebounceTimer;
        private const int TDP_LIMITS_DEBOUNCE_MS = 300;

        private bool isLoadingOSDConfig = false;

        private void OSDCustomizeLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Don't process during initialization - LoadOSDConfigFromStorage will handle it
            if (isLoadingOSDConfig) return;

            if (OSDCustomizeLevelComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int level))
                {
                    LoadOSDOptionsForLevel(level);
                    // Note: This is only for RTSS customization - AMD overlay doesn't have configurable levels
                }
            }
        }

        private void OSDProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (OSDProviderComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int provider))
                {
                    int previousProvider = osdProvider;
                    osdProvider = provider;

                    // Save to storage
                    try
                    {
                        ApplicationData.Current.LocalSettings.Values["OSD_Provider"] = osdProvider;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error saving OSD provider: {ex.Message}");
                    }

                    // Update UI visibility
                    UpdateOSDProviderUI();

                    // When switching providers, disable the other one
                    if (provider == 0) // RTSS
                    {
                        // Disable AMD overlay if it was enabled (send Ctrl+Shift+O to toggle off)
                        if (previousProvider == 1 && amdOverlayLevel > 0)
                        {
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 0;
                            SaveAMDOverlayLevel();
                        }
                        // Enable RTSS OSD by sending config
                        SendOSDConfigToHelper();
                    }
                    else if (provider == 1) // AMD
                    {
                        // Disable RTSS OSD by setting level to 0
                        if (osd != null)
                        {
                            osd.SetValue(0);
                        }
                        // Don't auto-toggle AMD overlay - we can't know its actual state
                        // User should manually enable via Quick Settings tile if needed
                    }

                    // Update Quick Settings tiles
                    UpdateQuickSettingsTileStates();

                    Logger.Info($"OSD Provider changed to: {(provider == 0 ? "RTSS" : "AMD")}");
                }
            }
        }

        private void UpdateOSDProviderUI()
        {
            if (RTSSOptionsPanel != null)
            {
                RTSSOptionsPanel.Visibility = osdProvider == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AMDOptionsPanel != null)
            {
                AMDOptionsPanel.Visibility = osdProvider == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void SendAMDOverlayToggle()
        {
            // Send Ctrl+Shift+O to toggle AMD Adrenaline's metrics overlay on/off
            // Use helper's InputInjector since UWP widget can't use SendInput directly
            try
            {
                if (App.Connection != null)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("SendKeyboardShortcut", "Ctrl+Shift+O");
                    await App.Connection.SendMessageAsync(request);
                    Logger.Info("Sent AMD overlay toggle hotkey (Ctrl+Shift+O) via helper");
                }
                else
                {
                    Logger.Warn("Cannot send AMD overlay toggle - not connected to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending AMD overlay toggle: {ex.Message}");
            }
        }

        private async void CycleAMDOverlayLevel()
        {
            // Send Ctrl+Shift+X to cycle AMD Adrenaline's metrics overlay levels
            // Use helper's InputInjector since UWP widget can't use SendInput directly
            try
            {
                if (App.Connection != null)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("SendKeyboardShortcut", "Ctrl+Shift+X");
                    await App.Connection.SendMessageAsync(request);
                    Logger.Info("Sent AMD overlay cycle hotkey (Ctrl+Shift+X) via helper");
                }
                else
                {
                    Logger.Warn("Cannot cycle AMD overlay level - not connected to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error cycling AMD overlay level: {ex.Message}");
            }
        }

        private void LoadOSDOptionsForLevel(int level)
        {
            if (!osdLevelConfig.ContainsKey(level)) return;

            isLoadingOSDConfig = true;
            try
            {
                // Update the current level
                osdCustomizeLevel = level;

                // Refresh the OSD items control with current level's order and states
                RefreshOSDItemsControl();

                if (OSDCustomTagsTextBox != null) OSDCustomTagsTextBox.Text = osdCustomTags.GetValueOrDefault(level, "");

                // Load columns for this level
                int columns = osdLevelColumns.GetValueOrDefault(level, 3);
                if (OSDColumnsComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDColumnsComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == columns)
                        {
                            OSDColumnsComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            finally
            {
                isLoadingOSDConfig = false;
            }
        }

        private void OSDOption_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            SaveCurrentOSDConfig();
        }

        /// <summary>
        /// Refreshes the OSD items control with the current level's order and enabled states
        /// </summary>
        private void RefreshOSDItemsControl()
        {
            if (OSDItemsControl == null) return;

            int currentLevel = osdCustomizeLevel;
            if (!osdLevelOrder.ContainsKey(currentLevel)) return;

            var order = osdLevelOrder[currentLevel];
            if (!osdLevelConfig.ContainsKey(currentLevel))
            {
                osdLevelConfig[currentLevel] = new Dictionary<string, bool>();
            }
            var config = osdLevelConfig[currentLevel];

            osdItemViewModels.Clear();
            var labelColors = osdItemLabelColors.ContainsKey(currentLevel) ? osdItemLabelColors[currentLevel] : new Dictionary<string, string>();
            for (int i = 0; i < order.Count; i++)
            {
                var id = order[i];
                osdItemViewModels.Add(new OSDItemViewModel
                {
                    Id = id,
                    DisplayName = osdItemDisplayNames.ContainsKey(id) ? osdItemDisplayNames[id] : id,
                    IsEnabled = config.ContainsKey(id) && config[id],
                    CanMoveUp = i > 0,
                    CanMoveDown = i < order.Count - 1,
                    LabelColor = labelColors.ContainsKey(id) ? labelColors[id] : "DEFAULT"
                });
            }

            OSDItemsControl.ItemsSource = osdItemViewModels;
        }

        private void OSDItemCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (sender is CheckBox cb && cb.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                if (!osdLevelConfig.ContainsKey(currentLevel))
                {
                    osdLevelConfig[currentLevel] = new Dictionary<string, bool>();
                }
                osdLevelConfig[currentLevel][itemId] = cb.IsChecked == true;

                SaveOSDConfigToStorage();
                SendOSDConfigToHelper();
            }
        }

        private void OSDItemMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                var order = osdLevelOrder[currentLevel];
                int index = order.IndexOf(itemId);
                if (index > 0)
                {
                    order.RemoveAt(index);
                    order.Insert(index - 1, itemId);
                    RefreshOSDItemsControl();
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
        }

        private void OSDItemMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                var order = osdLevelOrder[currentLevel];
                int index = order.IndexOf(itemId);
                if (index >= 0 && index < order.Count - 1)
                {
                    order.RemoveAt(index);
                    order.Insert(index + 1, itemId);
                    RefreshOSDItemsControl();
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
        }

        private void OSDItemLabelColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (sender is ComboBox cb && cb.Tag is string itemId && cb.SelectedItem is ComboBoxItem selected && selected.Tag is string colorTag)
            {
                int currentLevel = osdCustomizeLevel;
                if (!osdItemLabelColors.ContainsKey(currentLevel))
                {
                    osdItemLabelColors[currentLevel] = new Dictionary<string, string>();
                }
                osdItemLabelColors[currentLevel][itemId] = colorTag;

                // Update the view model to refresh the preview
                var vm = osdItemViewModels.FirstOrDefault(v => v.Id == itemId);
                if (vm != null) vm.LabelColor = colorTag;

                SaveOSDConfigToStorage();
                SendOSDConfigToHelper();
            }
        }

        private void OSDCustomTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            SaveCurrentOSDConfig();
        }

        private void SaveCurrentOSDConfig()
        {
            int level = osdCustomizeLevel;

            // Item enabled states are already in osdLevelConfig (updated by OSDItemCheckBox_Changed)
            // Just save custom tags and columns here

            osdCustomTags[level] = OSDCustomTagsTextBox?.Text ?? "";

            // Save columns for this level
            if (OSDColumnsComboBox?.SelectedItem is ComboBoxItem colItem && colItem.Tag is string colTag)
            {
                if (int.TryParse(colTag, out int cols))
                {
                    osdLevelColumns[level] = cols;
                }
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void SaveOSDConfigToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                foreach (var level in osdLevelConfig.Keys)
                {
                    var config = osdLevelConfig[level];
                    foreach (var item in config)
                    {
                        settings.Values[$"OSD_L{level}_{item.Key}"] = item.Value;
                    }
                    settings.Values[$"OSD_L{level}_CustomTags"] = osdCustomTags.GetValueOrDefault(level, "");
                    settings.Values[$"OSD_L{level}_Columns"] = osdLevelColumns.GetValueOrDefault(level, 3);

                    // Save item order
                    if (osdLevelOrder.ContainsKey(level))
                    {
                        settings.Values[$"OSD_L{level}_Order"] = string.Join(",", osdLevelOrder[level]);
                    }

                    // Save item label colors
                    if (osdItemLabelColors.ContainsKey(level))
                    {
                        foreach (var colorItem in osdItemLabelColors[level])
                        {
                            settings.Values[$"OSD_L{level}_{colorItem.Key}_Color"] = colorItem.Value;
                        }
                    }
                }

                // Save global layout settings (text size is per-resolution)
                string currentRes = resolution?.Value ?? "default";
                settings.Values[$"OSD_TextSize_{currentRes}"] = osdTextSize;
                settings.Values["OSD_TextColor"] = osdTextColor;
                settings.Values["OSD_LabelColor"] = osdLabelColor;
                settings.Values["OSD_Opacity"] = osdOpacity;

                Logger.Info($"OSD configuration saved to storage (resolution: {currentRes}, text size: {osdTextSize}, opacity: {osdOpacity})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving OSD config: {ex.Message}");
            }
        }

        private void LoadOSDConfigFromStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var itemKeys = new[] { "AppName", "Time", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP", "TDPLimits", "FrametimeGraph" };

                foreach (var level in new[] { 1, 2, 3 })
                {
                    if (!osdLevelConfig.ContainsKey(level))
                    {
                        osdLevelConfig[level] = new Dictionary<string, bool>();
                    }

                    foreach (var key in itemKeys)
                    {
                        string settingKey = $"OSD_L{level}_{key}";
                        if (settings.Values.TryGetValue(settingKey, out object val) && val is bool enabled)
                        {
                            osdLevelConfig[level][key] = enabled;
                        }
                    }

                    string customTagsKey = $"OSD_L{level}_CustomTags";
                    if (settings.Values.TryGetValue(customTagsKey, out object tagsVal) && tagsVal is string tags)
                    {
                        osdCustomTags[level] = tags;
                    }

                    // Load per-level columns
                    string columnsKey = $"OSD_L{level}_Columns";
                    if (settings.Values.TryGetValue(columnsKey, out object colsVal) && colsVal is int levelCols)
                    {
                        osdLevelColumns[level] = levelCols;
                    }

                    // Load per-level order
                    string orderKey = $"OSD_L{level}_Order";
                    if (settings.Values.TryGetValue(orderKey, out object orderVal) && orderVal is string orderStr)
                    {
                        var orderList = orderStr.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        if (orderList.Count == itemKeys.Length)
                        {
                            osdLevelOrder[level] = orderList;
                        }
                    }

                    // Load per-level item label colors
                    if (!osdItemLabelColors.ContainsKey(level))
                    {
                        osdItemLabelColors[level] = new Dictionary<string, string>();
                    }
                    foreach (var key in itemKeys)
                    {
                        string colorKey = $"OSD_L{level}_{key}_Color";
                        if (settings.Values.TryGetValue(colorKey, out object colorVal) && colorVal is string color)
                        {
                            osdItemLabelColors[level][key] = color;
                        }
                    }
                }

                // Load global layout settings (text size is per-resolution)
                string currentRes = resolution?.Value ?? "default";
                string textSizeKey = $"OSD_TextSize_{currentRes}";
                if (settings.Values.TryGetValue(textSizeKey, out object sizeVal) && sizeVal is int size)
                {
                    osdTextSize = size;
                    Logger.Info($"Loaded OSD text size {osdTextSize} for resolution {currentRes}");
                }
                else
                {
                    // Default to 100 if no per-resolution setting exists
                    osdTextSize = 100;
                    Logger.Info($"No OSD text size saved for resolution {currentRes}, using default 100");
                }
                if (settings.Values.TryGetValue("OSD_TextColor", out object textColorVal) && textColorVal is string textColor)
                {
                    osdTextColor = textColor;
                }
                if (settings.Values.TryGetValue("OSD_LabelColor", out object labelColorVal) && labelColorVal is string labelColor)
                {
                    osdLabelColor = labelColor;
                }
                if (settings.Values.TryGetValue("OSD_Opacity", out object opacityVal) && opacityVal is int opacity)
                {
                    osdOpacity = opacity;
                }
                if (settings.Values.TryGetValue("OSD_Provider", out object providerVal) && providerVal is int provider)
                {
                    osdProvider = provider;
                }
                if (settings.Values.TryGetValue("AMD_OverlayLevel", out object amdLevelVal) && amdLevelVal is int amdLevel)
                {
                    amdOverlayLevel = amdLevel;
                }

                // Update layout UI
                UpdateOSDLayoutUI();

                Logger.Info("OSD configuration loaded from storage");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading OSD config: {ex.Message}");
            }
        }

        private async void SendOSDConfigToHelper()
        {
            try
            {
                if (App.Connection == null) return;

                // Build config string to send to helper
                var configParts = new List<string>();

                // Add global layout settings
                configParts.Add($"TextSize:{osdTextSize}");
                configParts.Add($"TextColor:{osdTextColor}");
                configParts.Add($"LabelColor:{osdLabelColor}");
                configParts.Add($"Opacity:{osdOpacity}");

                // Add per-level item configuration
                foreach (var level in osdLevelConfig.Keys)
                {
                    var config = osdLevelConfig[level];
                    var enabledItems = new List<string>();
                    foreach (var item in config)
                    {
                        if (item.Value)
                        {
                            enabledItems.Add(item.Key);
                        }
                    }
                    configParts.Add($"L{level}:{string.Join(",", enabledItems)}");

                    if (!string.IsNullOrWhiteSpace(osdCustomTags.GetValueOrDefault(level, "")))
                    {
                        configParts.Add($"L{level}_Custom:{osdCustomTags[level]}");
                    }

                    // Add per-level columns
                    configParts.Add($"L{level}_Columns:{osdLevelColumns.GetValueOrDefault(level, 3)}");

                    // Add per-level order
                    if (osdLevelOrder.ContainsKey(level))
                    {
                        configParts.Add($"L{level}_Order:{string.Join(",", osdLevelOrder[level])}");
                    }

                    // Add per-level item label colors
                    if (osdItemLabelColors.ContainsKey(level))
                    {
                        var colors = osdItemLabelColors[level];
                        foreach (var colorItem in colors)
                        {
                            if (!string.IsNullOrEmpty(colorItem.Value) && colorItem.Value != "DEFAULT")
                            {
                                configParts.Add($"L{level}_{colorItem.Key}_Color:{colorItem.Value}");
                            }
                        }
                    }
                }

                var configString = string.Join(";", configParts);
                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.OSDConfig },
                    { "Content", configString },
                    { "UpdatedTime", DateTimeOffset.Now.Ticks }
                };
                await App.Connection.SendMessageAsync(request);

                Logger.Info($"OSD config sent to helper: {configString}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending OSD config to helper: {ex.Message}");
            }
        }

        private void OSDCustomizeExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isOSDCustomizeExpanded = !isOSDCustomizeExpanded;

            if (OSDCustomizeContent != null)
            {
                OSDCustomizeContent.Visibility = isOSDCustomizeExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (OSDCustomizeExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                OSDCustomizeExpandIcon.Glyph = isOSDCustomizeExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void OSDOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;
            osdOpacity = (int)Math.Round(e.NewValue);
            if (OSDOpacityValue != null)
                OSDOpacityValue.Text = $"{osdOpacity}%";
            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        #region Display and OSD Settings Handlers

        private async void AdaptiveBrightnessToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOLEDSettings) return;
            adaptiveBrightnessEnabled = AdaptiveBrightnessToggle.IsOn;
            SaveDisplayOSDSettingsToStorage();
            await SendDisplayOSDConfigToHelper();
        }

        private async void OSDPositionShiftToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOLEDSettings) return;
            osdPositionShiftEnabled = OSDPositionShiftToggle.IsOn;
            SaveDisplayOSDSettingsToStorage();
            await SendDisplayOSDConfigToHelper();
        }

        private void SaveDisplayOSDSettingsToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["OLED_AdaptiveBrightness"] = adaptiveBrightnessEnabled;
                settings.Values["OLED_PositionShift"] = osdPositionShiftEnabled;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving display/OSD settings: {ex.Message}");
            }
        }

        private void LoadDisplayOSDSettingsFromStorage()
        {
            isLoadingOLEDSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("OLED_AdaptiveBrightness", out object adaptiveBrightness) && adaptiveBrightness is bool ab)
                    adaptiveBrightnessEnabled = ab;
                if (settings.Values.TryGetValue("OLED_PositionShift", out object posShift) && posShift is bool ps)
                    osdPositionShiftEnabled = ps;
                if (settings.Values.TryGetValue("OSD_Opacity", out object opacity) && opacity is int op)
                    osdOpacity = op;

                // Update UI
                if (AdaptiveBrightnessToggle != null) AdaptiveBrightnessToggle.IsOn = adaptiveBrightnessEnabled;
                if (OSDPositionShiftToggle != null) OSDPositionShiftToggle.IsOn = osdPositionShiftEnabled;
                if (OSDOpacitySlider != null) OSDOpacitySlider.Value = osdOpacity;
                if (OSDOpacityValue != null) OSDOpacityValue.Text = $"{osdOpacity}%";
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading display/OSD settings: {ex.Message}");
            }
            finally
            {
                isLoadingOLEDSettings = false;
            }
        }

        private async Task SendDisplayOSDConfigToHelper()
        {
            try
            {
                if (App.Connection == null) return;

                var configString = $"AdaptiveBrightness:{(adaptiveBrightnessEnabled ? 1 : 0)};" +
                                   $"PositionShift:{(osdPositionShiftEnabled ? 1 : 0)}";

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.OLEDConfig },
                    { "Content", configString },
                    { "UpdatedTime", DateTimeOffset.Now.Ticks }
                };
                await App.Connection.SendMessageAsync(request);

                Logger.Info($"Display/OSD config sent to helper: {configString}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending display/OSD config to helper: {ex.Message}");
            }
        }

        #endregion

        private void ProfileSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileSettingsExpanded = !isProfileSettingsExpanded;

            if (ProfileSettingsContent != null)
            {
                ProfileSettingsContent.Visibility = isProfileSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileSettingsExpandIcon.Glyph = isProfileSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ProfileDetectionExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileDetectionExpanded = !isProfileDetectionExpanded;

            if (ProfileDetectionContent != null)
            {
                ProfileDetectionContent.Visibility = isProfileDetectionExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileDetectionExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileDetectionExpandIcon.Glyph = isProfileDetectionExpanded ? "\uE70E" : "\uE70D";
            }
        }

        /* DISABLED: Custom games, blacklist, and current apps features - caused user confusion
        private async void CustomGameAddButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add(".exe");
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    profileCustomGamePath?.AddPath(file.Path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding custom game: {ex.Message}");
            }
        }

        private void CustomGameRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string path)
            {
                profileCustomGamePath?.RemovePath(path);
            }
        }

        private void BlacklistRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string path)
            {
                profileBlacklistPaths?.RemovePath(path);
            }
        }

        private async void ForegroundAppAddCustom_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var path = button?.Tag as string;
            if (!string.IsNullOrEmpty(path))
            {
                profileBlacklistPaths?.RemovePath(path);
                profileCustomGamePath?.AddPath(path);
                await System.Threading.Tasks.Task.Delay(200);
                await foregroundApp?.Sync();
            }
        }

        private async void ForegroundAppAddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var path = button?.Tag as string;
            if (!string.IsNullOrEmpty(path))
            {
                profileCustomGamePath?.RemovePath(path);
                profileBlacklistPaths?.AddPath(path);
                await System.Threading.Tasks.Task.Delay(200);
                await foregroundApp?.Sync();
            }
        }

        private void UpdateForegroundAppsList(List<string> paths)
        {
            // ... method body removed for brevity ...
        }

        private Border CreateForegroundAppRow(string path)
        {
            // ... method body removed for brevity ...
            return null;
        }
        END DISABLED */

        private void ButtonRemappingExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isButtonRemappingExpanded = !isButtonRemappingExpanded;

            if (ButtonRemappingContent != null)
            {
                ButtonRemappingContent.Visibility = isButtonRemappingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ButtonRemappingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ButtonRemappingExpandIcon.Glyph = isButtonRemappingExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void GyroSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isGyroSettingsExpanded = !isGyroSettingsExpanded;

            if (GyroSettingsContent != null)
            {
                GyroSettingsContent.Visibility = isGyroSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GyroSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                GyroSettingsExpandIcon.Glyph = isGyroSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void SavedProfilesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isSavedProfilesExpanded = !isSavedProfilesExpanded;

            if (SavedProfilesContent != null)
            {
                SavedProfilesContent.Visibility = isSavedProfilesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SavedProfilesExpandIcon != null)
            {
                SavedProfilesExpandIcon.Glyph = isSavedProfilesExpanded ? "\uE70E" : "\uE70D";
            }

            // Refresh the list when expanding
            if (isSavedProfilesExpanded)
            {
                RefreshSavedProfilesList();
            }
        }

        // Gamepad action names for profile summary display
        private static readonly string[] GamepadActionShortNames = new[]
        {
            "-", "LSC", "LSU", "LSD", "LSL", "LSR", "RSC", "RSU", "RSD", "RSL", "RSR",
            "DU", "DD", "DL", "DR", "A", "B", "X", "Y", "LB", "LT", "RB", "RT", "View", "Menu"
        };

        private void RefreshSavedProfilesList()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var savedProfiles = new List<SavedProfileInfo>();

                // Look for all controller profile containers
                foreach (var containerName in settings.Containers.Keys)
                {
                    if (!containerName.StartsWith("ControllerProfile_"))
                        continue;

                    var container = settings.Containers[containerName];
                    string displayName;
                    bool isGlobal = false;

                    if (containerName == "ControllerProfile_Global")
                    {
                        displayName = "Global (Default)";
                        isGlobal = true;
                    }
                    else if (containerName.StartsWith("ControllerProfile_Game_"))
                    {
                        // Extract game name: "ControllerProfile_Game_{gameName}"
                        displayName = containerName.Substring("ControllerProfile_Game_".Length).Replace("_", " ");
                    }
                    else
                    {
                        continue; // Unknown format
                    }

                    // Build settings summary
                    var summaryParts = new List<string>();

                    // Check for custom button mappings and show which buttons are remapped
                    var remapParts = new List<string>();
                    foreach (var btnName in new[] { "Y1", "Y2", "Y3", "M1", "M2", "M3", "Desktop", "Page" })
                    {
                        if (container.Values.TryGetValue($"Button{btnName}", out var mappingVal) && mappingVal is string mappingJson)
                        {
                            var mapping = ButtonMapping.FromJson(mappingJson);
                            if (mapping != null)
                            {
                                if (mapping.Type == 0 && mapping.GamepadAction > 0 && mapping.GamepadAction < GamepadActionShortNames.Length)
                                {
                                    // Gamepad remap
                                    remapParts.Add($"{btnName}:{GamepadActionShortNames[mapping.GamepadAction]}");
                                }
                                else if (mapping.Type == 1 && mapping.KeyboardKeys != null && mapping.KeyboardKeys.Count > 0)
                                {
                                    // Keyboard remap - show actual keys
                                    var keyNames = mapping.KeyboardKeys.Select(k => GetKeyDisplayName(k));
                                    remapParts.Add($"{btnName}:{string.Join("+", keyNames)}");
                                }
                                else if (mapping.Type == 2 && mapping.MouseButton > 0)
                                {
                                    // Mouse remap - show which button
                                    var mouseButtons = new[] { "", "Left", "Right", "Middle", "Back", "Forward" };
                                    var mouseName = mapping.MouseButton < mouseButtons.Length ? mouseButtons[mapping.MouseButton] : "Mouse";
                                    remapParts.Add($"{btnName}:{mouseName}Click");
                                }
                            }
                        }
                    }
                    if (remapParts.Count > 0)
                    {
                        summaryParts.Add(string.Join(" ", remapParts));
                    }

                    // Check gyro settings
                    if (container.Values.TryGetValue("GyroTarget", out var gyroTarget) && (int)gyroTarget > 0)
                    {
                        var gyroTargets = new[] { "", "LStick", "RStick", "Mouse" };
                        var targetIdx = (int)gyroTarget;
                        if (targetIdx > 0 && targetIdx < gyroTargets.Length)
                            summaryParts.Add($"Gyro:{gyroTargets[targetIdx]}");
                    }

                    // Check deadzones
                    if (container.Values.TryGetValue("LeftStickDeadzone", out var lsDz) && (int)lsDz != 4)
                    {
                        summaryParts.Add($"LDZ:{lsDz}%");
                    }
                    if (container.Values.TryGetValue("RightStickDeadzone", out var rsDz) && (int)rsDz != 4)
                    {
                        summaryParts.Add($"RDZ:{rsDz}%");
                    }

                    // Check joystick as mouse
                    if (container.Values.TryGetValue("JoystickAsMouseMode", out var jamMode) && (int)jamMode > 0)
                    {
                        summaryParts.Add("JoyMouse");
                    }

                    // Check RGB lighting settings
                    if (container.Values.TryGetValue("LightMode", out var lightModeVal))
                    {
                        int lightMode = (int)lightModeVal;
                        if (lightMode > 0) // 0 = Off
                        {
                            var lightModes = new[] { "Off", "Solid", "Breathe", "Rainbow", "Spiral" };
                            string modeName = lightMode < lightModes.Length ? lightModes[lightMode] : $"Mode{lightMode}";

                            // Get color if solid or breathe mode
                            if (lightMode == 1 || lightMode == 2) // Solid or Breathe
                            {
                                if (container.Values.TryGetValue("LightColorR", out var r) &&
                                    container.Values.TryGetValue("LightColorG", out var g) &&
                                    container.Values.TryGetValue("LightColorB", out var b))
                                {
                                    summaryParts.Add($"RGB:{modeName}({r},{g},{b})");
                                }
                                else
                                {
                                    summaryParts.Add($"RGB:{modeName}");
                                }
                            }
                            else
                            {
                                summaryParts.Add($"RGB:{modeName}");
                            }
                        }
                    }

                    // Check brightness
                    if (container.Values.TryGetValue("LightBrightness", out var brightnessVal) && (int)brightnessVal != 50)
                    {
                        summaryParts.Add($"Bright:{brightnessVal}%");
                    }

                    // Check power light
                    if (container.Values.TryGetValue("PowerLight", out var powerLightVal) && !(bool)powerLightVal)
                    {
                        summaryParts.Add("PwrLight:Off");
                    }

                    var summary = summaryParts.Count > 0 ? string.Join(" | ", summaryParts) : "Default settings";

                    // Get stored game exe path for icon loading
                    string gameExePath = null;
                    if (!isGlobal && container.Values.TryGetValue("GameExePath", out var exePathObj) && exePathObj is string exePath)
                    {
                        gameExePath = exePath;
                    }

                    savedProfiles.Add(new SavedProfileInfo
                    {
                        ProfileKey = containerName,
                        GameName = displayName,
                        SettingsSummary = summary,
                        IsGlobal = isGlobal,
                        GameExePath = gameExePath
                    });
                }

                // Sort: Global first, then alphabetically by game name
                savedProfiles.Sort((a, b) =>
                {
                    if (a.IsGlobal && !b.IsGlobal) return -1;
                    if (!a.IsGlobal && b.IsGlobal) return 1;
                    return string.Compare(a.GameName, b.GameName, StringComparison.OrdinalIgnoreCase);
                });

                // Update UI
                SavedProfilesList.ItemsSource = savedProfiles;
                NoSavedProfilesText.Visibility = savedProfiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Load icons asynchronously for saved profiles
                _ = LoadSavedProfileIconsAsync(savedProfiles);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to refresh saved profiles list: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads icons for saved profiles asynchronously.
        /// </summary>
        private async Task LoadSavedProfileIconsAsync(List<SavedProfileInfo> profiles)
        {
            Logger.Info($"LoadSavedProfileIconsAsync: Loading icons for {profiles.Count} profiles");

            foreach (var profile in profiles)
            {
                if (profile.IsGlobal)
                {
                    Logger.Debug($"LoadSavedProfileIconsAsync: Skipping global profile");
                    continue;
                }

                if (string.IsNullOrEmpty(profile.GameExePath))
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: No exe path for {profile.GameName}");
                    continue;
                }

                try
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: Loading icon for {profile.GameName} from {profile.GameExePath}");
                    var icon = await LoadSavedProfileIconAsync(profile.GameExePath);
                    if (icon != null)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            profile.IconSource = icon;
                        });
                        Logger.Info($"LoadSavedProfileIconsAsync: Icon loaded for {profile.GameName}");
                    }
                    else
                    {
                        Logger.Info($"LoadSavedProfileIconsAsync: No icon found for {profile.GameName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"LoadSavedProfileIconsAsync: Error loading icon for {profile.GameName}: {ex.Message}");
                }
            }
        }

        private void DeleteSavedProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string profileKey)
            {
                try
                {
                    // Don't allow deleting Global profile
                    if (profileKey == "ControllerProfile_Global")
                    {
                        Logger.Warn("Cannot delete Global controller profile");
                        return;
                    }

                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                    // Delete the controller profile container
                    if (settings.Containers.ContainsKey(profileKey))
                    {
                        settings.DeleteContainer(profileKey);
                        Logger.Info($"Deleted controller profile: {profileKey}");
                    }

                    // Refresh the list
                    RefreshSavedProfilesList();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to delete profile {profileKey}: {ex.Message}");
                }
            }
        }

        private void StickDeadzonesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isStickDeadzonesExpanded = !isStickDeadzonesExpanded;

            if (StickDeadzonesContent != null)
            {
                StickDeadzonesContent.Visibility = isStickDeadzonesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (StickDeadzonesExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                StickDeadzonesExpandIcon.Glyph = isStickDeadzonesExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TouchpadVibrationExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isTouchpadVibrationExpanded = !isTouchpadVibrationExpanded;

            if (TouchpadVibrationContent != null)
            {
                TouchpadVibrationContent.Visibility = isTouchpadVibrationExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TouchpadVibrationExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TouchpadVibrationExpandIcon.Glyph = isTouchpadVibrationExpanded ? "\uE70E" : "\uE70D";
            }
        }

        #region Quick Settings Action Helpers

        /// <summary>
        /// Toggles the Legion Power Light on/off.
        /// </summary>
        private void ToggleLegionPowerLight()
        {
            if (legionPowerLight == null) return;

            // Toggle the current state
            bool newState = !legionPowerLight.Value;
            legionPowerLight.SetValue(newState);

            Logger.Info($"Power Light toggled: {(newState ? "On" : "Off")}");
        }

        /// <summary>
        /// Puts the system into hibernation via helper.
        /// </summary>
        private async void ExecuteHibernate()
        {
            Logger.Info("Hibernate action triggered");

            try
            {
                if (App.Connection != null)
                {
                    // Send hibernate request to helper (UWP can't execute shutdown directly)
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("Hibernate", true);
                    await App.Connection.SendMessageAsync(message);
                    Logger.Info("Hibernate request sent to helper");
                }
                else
                {
                    Logger.Warn("Cannot hibernate - helper not connected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to hibernate: {ex.Message}");
            }
        }

        #endregion

        private void LightingExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isLightingExpanded = !isLightingExpanded;

            if (LightingContent != null)
            {
                LightingContent.Visibility = isLightingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (LightingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                LightingExpandIcon.Glyph = isLightingExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void FanCurveExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isFanCurveExpanded = !isFanCurveExpanded;

            if (FanCurveContent != null)
            {
                FanCurveContent.Visibility = isFanCurveExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FanCurveExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                FanCurveExpandIcon.Glyph = isFanCurveExpanded ? "\uE70E" : "\uE70D";
            }

            // Initialize graph on first expand
            if (isFanCurveExpanded && !fanCurveGraphInitialized)
            {
                InitializeFanCurveGraph();
            }

            // Tell helper whether to push CPU temp/RPM updates
            legionFanCurveVisible?.SetVisible(isFanCurveExpanded);
        }

        #region Fan Curve Graph

        private void InitializeFanCurveGraph()
        {
            if (FanCurveCanvas == null || fanCurveGraphInitialized)
                return;

            // Initialize with current values from property
            currentFanCurveValues = legionFanCurveGraph.GetCurveValues();

            // Create 10 control point ellipses
            for (int i = 0; i < 10; i++)
            {
                var ellipse = new Windows.UI.Xaml.Shapes.Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 0, 170, 255)),
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White),
                    StrokeThickness = 2,
                    Tag = i
                };
                fanCurvePoints[i] = ellipse;
                FanCurveCanvas.Children.Add(ellipse);
            }

            fanCurveGraphInitialized = true;

            // Load saved preset selection
            LoadFanCurvePresetSetting();

            // Draw the graph
            DrawGridLines();
            UpdateFanCurveGraph();
        }

        private void FanCurvePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isFanCurvePresetLoading) return;

            if (FanCurvePresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string presetName)
            {
                if (presetName == "Custom") return; // User manually selected Custom, no action needed

                if (FanCurvePresets.TryGetValue(presetName, out int[] presetValues))
                {
                    currentFanCurvePreset = presetName;
                    currentFanCurveValues = (int[])presetValues.Clone();
                    UpdateFanCurveGraph();

                    // Send to helper
                    legionFanCurveGraph?.SetCurveValuesDebounced(currentFanCurveValues);

                    // Save preset selection
                    SaveFanCurvePresetSetting(presetName);
                }
            }
        }

        private void SwitchToCustomPreset()
        {
            if (currentFanCurvePreset != "Custom")
            {
                currentFanCurvePreset = "Custom";
                isFanCurvePresetLoading = true;
                SelectPresetInComboBox("Custom");
                isFanCurvePresetLoading = false;
                SaveFanCurvePresetSetting("Custom");
            }
        }

        private void SelectPresetInComboBox(string presetName)
        {
            if (FanCurvePresetComboBox == null) return;
            foreach (ComboBoxItem item in FanCurvePresetComboBox.Items)
            {
                if (item.Tag is string tag && tag == presetName)
                {
                    FanCurvePresetComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SaveFanCurvePresetSetting(string presetName)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["FanCurvePreset"] = presetName;
            }
            catch { }
        }

        private void LoadFanCurvePresetSetting()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("FanCurvePreset", out object saved) && saved is string presetName)
                {
                    currentFanCurvePreset = presetName;
                    isFanCurvePresetLoading = true;
                    SelectPresetInComboBox(presetName);
                    isFanCurvePresetLoading = false;
                }
            }
            catch { }
        }

        private void DrawGridLines()
        {
            if (FanCurveCanvas == null) return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Draw horizontal grid lines (at 25%, 50%, 75%)
            for (int i = 1; i <= 3; i++)
            {
                double y = height - (height * i * 0.25);
                var line = new Windows.UI.Xaml.Shapes.Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetZIndex(line, -1);
                FanCurveCanvas.Children.Add(line);
            }

            // Draw vertical grid lines (at 20%, 40%, 60%, 80%)
            for (int i = 1; i <= 4; i++)
            {
                double x = width * i * 0.2;
                var line = new Windows.UI.Xaml.Shapes.Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.ColorHelper.FromArgb(50, 255, 255, 255)),
                    StrokeThickness = 1
                };
                Canvas.SetZIndex(line, -1);
                FanCurveCanvas.Children.Add(line);
            }

            // Draw EC floor line after grid lines
            DrawECFloorLine();
        }

        private void DrawECFloorLine()
        {
            if (ECFloorPolyline == null || FanCurveCanvas == null) return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();

            foreach (var (temp, floor) in ECFloorPoints)
            {
                // Map temperature to X position (10-100°C range)
                double x = (temp - 10.0) / 90.0 * width;
                // Map fan % to Y position (inverted)
                double y = height - (floor / 100.0 * height);
                points.Add(new Windows.Foundation.Point(x, y));
            }

            ECFloorPolyline.Points = points;
        }

        private void UpdateFanCurveGraph()
        {
            if (FanCurveCanvas == null || FanCurvePolyline == null || FanCurveFill == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var points = new Windows.UI.Xaml.Media.PointCollection();
            var fillPoints = new Windows.UI.Xaml.Media.PointCollection();

            // Legion Go temperature thresholds: 10, 20, 30, 40, 50, 60, 70, 80, 90, 100°C (FIXED by EC)
            // Map to 0-100% of width (10-100°C range = 90°C)
            for (int i = 0; i < 10; i++)
            {
                int temp = FanCurveTemperatures[i];
                double x = (temp - 10.0) / 90.0 * width; // Normalize 10-100 to 0-width
                double y = height - (currentFanCurveValues[i] / 100.0 * height);

                points.Add(new Windows.Foundation.Point(x, y));
                fillPoints.Add(new Windows.Foundation.Point(x, y));

                // Position control point
                if (fanCurvePoints[i] != null)
                {
                    Canvas.SetLeft(fanCurvePoints[i], x - 8); // Center the 16px ellipse
                    Canvas.SetTop(fanCurvePoints[i], y - 8);
                }
            }

            FanCurvePolyline.Points = points;

            // Add bottom corners for fill polygon
            fillPoints.Add(new Windows.Foundation.Point(width, height));
            fillPoints.Add(new Windows.Foundation.Point(0, height));
            FanCurveFill.Points = fillPoints;
        }

        private void UpdateTemperatureIndicator(int tempC)
        {
            if (TempIndicatorLine == null || FanCurveCanvas == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Clamp temp to 10-100 range (Legion Go fan curve range, FIXED by EC)
            tempC = Math.Max(10, Math.Min(100, tempC));

            // Calculate X position (10-100°C range = 90°C span)
            double x = (tempC - 10.0) / 90.0 * width;

            TempIndicatorLine.X1 = x;
            TempIndicatorLine.X2 = x;
            TempIndicatorLine.Y1 = 0;
            TempIndicatorLine.Y2 = height;
            TempIndicatorLine.Visibility = Visibility.Visible;
        }

        private void OnFanCurveUpdated(int[] values)
        {
            if (values == null || values.Length != 10) return;

            currentFanCurveValues = values;
            UpdateFanCurveGraph();
        }

        private void OnCPUTempUpdated(int tempC)
        {
            // CPU temp is shown as reference only, fan sensor temp is used for graph indicator
            // (CPU temp is typically 10-17°C higher than fan sensor temp)
        }

        private void OnFanSensorTempUpdated(int tempC)
        {
            // Update temperature label (this is the temp the EC uses for fan curve)
            if (CurrentTempLabel != null)
            {
                CurrentTempLabel.Text = $"{tempC}°C";
            }
            // Update temperature indicator on graph (fan sensor temp matches the curve's X-axis)
            UpdateTemperatureIndicator(tempC);
        }

        private void OnFanRPMUpdated(int rpm)
        {
            if (FanRPMLabel != null)
            {
                FanRPMLabel.Text = $"{rpm} RPM";
            }

            // Update RPM indicator line on graph
            UpdateRPMIndicator(rpm);
        }

        private void UpdateRPMIndicator(int rpm)
        {
            if (RPMIndicatorLine == null || FanCurveCanvas == null)
                return;

            double width = FanCurveCanvas.ActualWidth;
            double height = FanCurveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Convert RPM to percentage (max 7500 RPM for Legion Go EC scale)
            const int MAX_RPM = 7500;
            double percent = Math.Max(0, Math.Min(100, (double)rpm / MAX_RPM * 100));

            // Calculate Y position (inverted - 0% at bottom, 100% at top)
            double y = height - (percent / 100.0 * height);

            RPMIndicatorLine.X1 = 0;
            RPMIndicatorLine.X2 = width;
            RPMIndicatorLine.Y1 = y;
            RPMIndicatorLine.Y2 = y;
            RPMIndicatorLine.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        private void FanCurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (fanCurveGraphInitialized)
            {
                // Clear old grid lines
                var toRemove = new System.Collections.Generic.List<Windows.UI.Xaml.UIElement>();
                foreach (var child in FanCurveCanvas.Children)
                {
                    if (child is Windows.UI.Xaml.Shapes.Line line && line != TempIndicatorLine && line != RPMIndicatorLine)
                    {
                        toRemove.Add(child);
                    }
                }
                foreach (var item in toRemove)
                {
                    FanCurveCanvas.Children.Remove(item);
                }

                DrawGridLines();
                UpdateFanCurveGraph();

                // Re-update temp indicator if we have a value (fan sensor temp is used for graph)
                if (legionFanSensorTemp != null && legionFanSensorTemp.Value > 0)
                {
                    UpdateTemperatureIndicator(legionFanSensorTemp.Value);
                }
            }
        }

        private void FanCurveCanvas_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (FanCurveCanvas == null) return;

            var point = e.GetCurrentPoint(FanCurveCanvas).Position;

            // Find the closest control point
            double minDist = double.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < 10; i++)
            {
                if (fanCurvePoints[i] == null) continue;

                double px = Canvas.GetLeft(fanCurvePoints[i]) + 8;
                double py = Canvas.GetTop(fanCurvePoints[i]) + 8;

                double dist = Math.Sqrt(Math.Pow(point.X - px, 2) + Math.Pow(point.Y - py, 2));
                if (dist < minDist && dist < 30) // 30px hit area
                {
                    minDist = dist;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                draggedPointIndex = closestIndex;
                isDraggingPoint = true;
                FanCurveCanvas.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void FanCurveCanvas_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!isDraggingPoint || draggedPointIndex < 0 || FanCurveCanvas == null)
                return;

            var point = e.GetCurrentPoint(FanCurveCanvas).Position;
            double height = FanCurveCanvas.ActualHeight;

            // Calculate new fan speed (invert Y since 0 is at top)
            double fanSpeed = (1.0 - point.Y / height) * 100.0;

            // Enforce minimum fan speed for this temperature threshold
            int minSpeed = FanCurveMinSpeeds[draggedPointIndex];
            fanSpeed = Math.Max(minSpeed, Math.Min(100, fanSpeed));

            // Update the value
            currentFanCurveValues[draggedPointIndex] = (int)Math.Round(fanSpeed);

            // Redraw the graph
            UpdateFanCurveGraph();

            e.Handled = true;
        }

        private void FanCurveCanvas_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (isDraggingPoint && FanCurveCanvas != null)
            {
                FanCurveCanvas.ReleasePointerCapture(e.Pointer);

                // Switch to Custom preset when manually dragging
                SwitchToCustomPreset();

                // Send the updated values to the helper (debounced)
                legionFanCurveGraph.SetCurveValuesDebounced(currentFanCurveValues);
            }

            draggedPointIndex = -1;
            isDraggingPoint = false;
            e.Handled = true;
        }

        #endregion

        private void TDPExtrasExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isTDPExtrasExpanded = !isTDPExtrasExpanded;

            if (TDPExtrasContent != null)
            {
                TDPExtrasContent.Visibility = isTDPExtrasExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TDPExtrasExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TDPExtrasExpandIcon.Glyph = isTDPExtrasExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void CPUExtrasExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isCPUExtrasExpanded = !isCPUExtrasExpanded;

            if (CPUExtrasContent != null)
            {
                CPUExtrasContent.Visibility = isCPUExtrasExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (CPUExtrasExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                CPUExtrasExpandIcon.Glyph = isCPUExtrasExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TDPLimitsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isTDPLimitsExpanded = !isTDPLimitsExpanded;

            if (TDPLimitsContent != null)
            {
                TDPLimitsContent.Visibility = isTDPLimitsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TDPLimitsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TDPLimitsExpandIcon.Glyph = isTDPLimitsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        #region TDP Boost Handlers

        private void TDPBoostExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isTDPBoostExpanded = !isTDPBoostExpanded;

            if (TDPBoostContent != null)
            {
                TDPBoostContent.Visibility = isTDPBoostExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TDPBoostExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TDPBoostExpandIcon.Glyph = isTDPBoostExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TDPBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (TDPBoostToggle == null) return;
            if (isApplyingHelperUpdate) return;
            // Skip during mode changes - don't save forced-off state
            if (isUpdatingTDPMode) return;

            Logger.Info($"TDP Boost toggled to: {TDPBoostToggle.IsOn}");

            // Send to helper
            tdpBoostEnabled?.SetValue(TDPBoostToggle.IsOn);

            // Save to local settings for persistence across widget restarts
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostEnabled"] = TDPBoostToggle.IsOn;

            // When enabling boost, also send current SPPT/FPPT values to ensure helper has them
            if (TDPBoostToggle.IsOn)
            {
                int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                tdpBoostSPPT?.SetValue(spptBoost);
                tdpBoostFPPT?.SetValue(fpptBoost);
                Logger.Info($"TDP Boost enabled - sent SPPT={spptBoost}W, FPPT={fpptBoost}W to helper");
            }

            // Save to profile if not loading
            if (!isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        private void TDPBoostSPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostSPPTSlider == null) return;

            int spptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost SPPT changed to: {spptBoost}W");

            if (TDPBoostSPPTValue != null)
            {
                TDPBoostSPPTValue.Text = $"{spptBoost}W";
            }

            // Send to helper
            tdpBoostSPPT?.SetValue(spptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostSPPT"] = spptBoost;
        }

        private void TDPBoostFPPTSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPBoostSettings) return;
            if (TDPBoostFPPTSlider == null) return;

            int fpptBoost = (int)Math.Round(e.NewValue);
            Logger.Info($"TDP Boost FPPT changed to: {fpptBoost}W");

            if (TDPBoostFPPTValue != null)
            {
                TDPBoostFPPTValue.Text = $"{fpptBoost}W";
            }

            // Send to helper
            tdpBoostFPPT?.SetValue(fpptBoost);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["TDPBoostFPPT"] = fpptBoost;
        }

        private void LoadTDPBoostSettings()
        {
            isLoadingTDPBoostSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load TDP Boost enabled state (default OFF)
                if (settings.Values.TryGetValue("TDPBoostEnabled", out object enabledObj) && enabledObj is bool enabled)
                {
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsOn = enabled;
                    }
                    tdpBoostEnabled?.SetValue(enabled);
                    Logger.Info($"TDP Boost enabled state loaded from settings: {enabled}");
                }

                // Load SPPT boost (default 1W)
                int spptBoost = 1; // Default
                if (settings.Values.TryGetValue("TDPBoostSPPT", out object spptObj) && spptObj != null)
                {
                    try
                    {
                        spptBoost = Convert.ToInt32(spptObj);
                    }
                    catch
                    {
                        spptBoost = 1;
                    }
                }
                if (TDPBoostSPPTSlider != null)
                {
                    TDPBoostSPPTSlider.Value = spptBoost;
                }
                if (TDPBoostSPPTValue != null)
                {
                    TDPBoostSPPTValue.Text = $"{spptBoost}W";
                }
                tdpBoostSPPT?.SetValue(spptBoost);
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostSPPT"] = spptBoost;

                // Load FPPT boost (default 3W)
                int fpptBoost = 3; // Default
                if (settings.Values.TryGetValue("TDPBoostFPPT", out object fpptObj) && fpptObj != null)
                {
                    try
                    {
                        fpptBoost = Convert.ToInt32(fpptObj);
                    }
                    catch
                    {
                        fpptBoost = 3;
                    }
                }
                if (TDPBoostFPPTSlider != null)
                {
                    TDPBoostFPPTSlider.Value = fpptBoost;
                }
                if (TDPBoostFPPTValue != null)
                {
                    TDPBoostFPPTValue.Text = $"{fpptBoost}W";
                }
                tdpBoostFPPT?.SetValue(fpptBoost);
                // Ensure value is saved (in case it was missing or converted)
                settings.Values["TDPBoostFPPT"] = fpptBoost;

                Logger.Info($"TDP Boost settings loaded - SPPT: {spptBoost}W, FPPT: {fpptBoost}W");
            }
            finally
            {
                isLoadingTDPBoostSettings = false;
            }
        }

        private void TDPBoostEnabled_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // NOTE: This callback is triggered when helper syncs TDPBoostEnabled.
            // We do NOT update the toggle from this callback because:
            // 1. The widget (LocalSettings) is the source of truth for this setting
            // 2. The helper doesn't persist TDPBoostEnabled, so it always sends False on fresh start
            // 3. Profile loading explicitly sets the toggle in LoadProfileSettings()
            //
            // If boost is enabled, we just need to ensure SPPT/FPPT values are sent to helper.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (TDPBoostToggle == null || tdpBoostEnabled == null) return;

                // Only send SPPT/FPPT to helper if boost is currently enabled in the UI
                // (regardless of what the helper sent us)
                if (TDPBoostToggle.IsOn)
                {
                    int spptBoost = (int)(TDPBoostSPPTSlider?.Value ?? 1);
                    int fpptBoost = (int)(TDPBoostFPPTSlider?.Value ?? 3);
                    tdpBoostSPPT?.SetValue(spptBoost);
                    tdpBoostFPPT?.SetValue(fpptBoost);
                    Logger.Debug($"TDP Boost PropertyChanged - ensuring SPPT={spptBoost}W, FPPT={fpptBoost}W sent to helper");
                }
            });
        }

        #endregion

        private void PowerPlanExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isPowerPlanExpanded = !isPowerPlanExpanded;

            if (PowerPlanOptionsPanel != null)
            {
                PowerPlanOptionsPanel.Visibility = isPowerPlanExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PowerPlanExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                PowerPlanExpandIcon.Glyph = isPowerPlanExpanded ? "\uE70E" : "\uE70D";
            }

            // Load power plans when expanding for the first time
            if (isPowerPlanExpanded && availablePowerPlans.Count == 0)
            {
                LoadPowerPlans();
            }
        }

        private async void LoadPowerPlans()
        {
            isLoadingPowerPlans = true;

            try
            {
                // Request power plans from helper
                if (App.Connection != null)
                {
                    var request = new Windows.Foundation.Collections.ValueSet();
                    request.Add("GetPowerPlans", true);

                    var response = await App.Connection.SendMessageAsync(request);

                    if (response?.Message != null)
                    {
                        availablePowerPlans.Clear();

                        // Parse response: "GUID1|Name1;GUID2|Name2;..."
                        if (response.Message.TryGetValue("PowerPlans", out object plansValue) && plansValue is string plansStr)
                        {
                            var planParts = plansStr.Split(';');
                            foreach (var part in planParts)
                            {
                                if (string.IsNullOrWhiteSpace(part)) continue;

                                var segments = part.Split('|');
                                if (segments.Length >= 2 && Guid.TryParse(segments[0], out Guid planGuid))
                                {
                                    availablePowerPlans.Add(new PowerPlanItem
                                    {
                                        Guid = planGuid,
                                        Name = segments[1]
                                    });
                                }
                            }
                        }

                        // Get currently active plan
                        if (response.Message.TryGetValue("ActivePowerPlan", out object activeValue) && activeValue is string activeStr)
                        {
                            if (Guid.TryParse(activeStr, out Guid activeGuid))
                            {
                                // If no saved preferences, use current active plan as default
                                if (acPowerPlanGuid == Guid.Empty)
                                {
                                    acPowerPlanGuid = activeGuid;
                                }
                                if (dcPowerPlanGuid == Guid.Empty)
                                {
                                    dcPowerPlanGuid = activeGuid;
                                }
                            }
                        }

                        Logger.Info($"Received {availablePowerPlans.Count} power plans from helper");
                    }
                }

                // Fallback to well-known plans if helper didn't respond
                if (availablePowerPlans.Count == 0)
                {
                    Logger.Warn("No power plans received from helper, using defaults");
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
                        Name = "Balanced"
                    });
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
                        Name = "High Performance"
                    });
                    availablePowerPlans.Add(new PowerPlanItem
                    {
                        Guid = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"),
                        Name = "Power Saver"
                    });
                }

                // Populate ComboBoxes
                if (ACPowerPlanComboBox != null)
                {
                    ACPowerPlanComboBox.Items.Clear();
                    foreach (var plan in availablePowerPlans)
                    {
                        ACPowerPlanComboBox.Items.Add(new ComboBoxItem { Content = plan.Name, Tag = plan.Guid.ToString() });
                    }

                    // Select saved or default
                    SelectPowerPlanInComboBox(ACPowerPlanComboBox, acPowerPlanGuid);
                }

                if (DCPowerPlanComboBox != null)
                {
                    DCPowerPlanComboBox.Items.Clear();
                    foreach (var plan in availablePowerPlans)
                    {
                        DCPowerPlanComboBox.Items.Add(new ComboBoxItem { Content = plan.Name, Tag = plan.Guid.ToString() });
                    }

                    // Select saved or default
                    SelectPowerPlanInComboBox(DCPowerPlanComboBox, dcPowerPlanGuid);
                }

                // Update toggle state
                if (PowerPlanAutoSwitchToggle != null)
                {
                    PowerPlanAutoSwitchToggle.IsOn = powerPlanAutoSwitch;
                }

                Logger.Info($"Loaded {availablePowerPlans.Count} power plans");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plans: {ex.Message}");
            }
            finally
            {
                isLoadingPowerPlans = false;
            }
        }

        private void SelectPowerPlanInComboBox(ComboBox comboBox, Guid planGuid)
        {
            if (comboBox == null) return;

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is string guidStr)
                {
                    if (Guid.TryParse(guidStr, out Guid itemGuid) && itemGuid == planGuid)
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            // Default to first item (Balanced) if not found
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void ACPowerPlanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            if (ACPowerPlanComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string guidStr)
            {
                if (Guid.TryParse(guidStr, out Guid planGuid))
                {
                    acPowerPlanGuid = planGuid;
                    SavePowerPlanSettings();

                    // If currently on AC power, apply the plan immediately
                    if (powerPlanAutoSwitch && PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate)
                    {
                        ApplyPowerPlan(planGuid);
                    }

                    Logger.Info($"AC Power Plan set to: {selected.Content} ({planGuid})");
                }
            }
        }

        private void DCPowerPlanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            if (DCPowerPlanComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string guidStr)
            {
                if (Guid.TryParse(guidStr, out Guid planGuid))
                {
                    dcPowerPlanGuid = planGuid;
                    SavePowerPlanSettings();

                    // If currently on battery, apply the plan immediately
                    if (powerPlanAutoSwitch && PowerManager.PowerSupplyStatus != PowerSupplyStatus.Adequate)
                    {
                        ApplyPowerPlan(planGuid);
                    }

                    Logger.Info($"DC Power Plan set to: {selected.Content} ({planGuid})");
                }
            }
        }

        private void PowerPlanAutoSwitchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingPowerPlans) return;

            powerPlanAutoSwitch = PowerPlanAutoSwitchToggle?.IsOn ?? false;
            SavePowerPlanSettings();

            Logger.Info($"Power Plan auto-switch set to: {powerPlanAutoSwitch}");
        }

        private void ApplyPowerPlan(Guid planGuid)
        {
            if (planGuid == Guid.Empty) return;

            // Send message to helper to apply the power plan
            // Format: "PowerPlan:GUID"
            try
            {
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("PowerPlan", planGuid.ToString());
                _ = SendHelperMessageAsync(message);
                Logger.Info($"Sent power plan change request: {planGuid}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying power plan: {ex.Message}");
            }
        }

        private async Task SendHelperMessageAsync(Windows.Foundation.Collections.ValueSet message)
        {
            if (App.Connection != null)
            {
                try
                {
                    await App.Connection.SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error sending message to helper: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Send a keyboard shortcut via the helper process.
        /// This is required because UWP apps cannot use SendInput directly due to sandboxing.
        /// </summary>
        private async Task SendKeyboardShortcutViaHelper(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                Logger.Warn("Empty shortcut string provided to SendKeyboardShortcutViaHelper");
                return;
            }

            try
            {
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("SendKeyboardShortcut", shortcut);
                await SendHelperMessageAsync(message);
                Logger.Info($"Sent keyboard shortcut request to helper: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending keyboard shortcut via helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Request the helper to refresh display settings (resolution, refresh rate, HDR).
        /// Called when a game closes to ensure the resolution tile shows the correct value.
        /// </summary>
        private async Task RequestDisplaySettingsRefreshAsync()
        {
            try
            {
                Logger.Info("Requesting display settings refresh from helper");
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("RefreshDisplaySettings", true);
                await SendHelperMessageAsync(message);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error requesting display settings refresh: {ex.Message}");
            }
        }

        /// <summary>
        /// Send a custom shortcut by first closing Game Bar (if in widget mode), then sending the shortcut.
        /// Sequence: Win+G (close Game Bar) → Custom shortcut
        /// </summary>
        private async Task SendCustomShortcutAsync(string shortcut, string tileName)
        {
            try
            {
                Logger.Info($"Custom shortcut tile clicked: {tileName} -> {shortcut}");

                // Only close Game Bar if we're running as a widget
                if (widget != null)
                {
                    // First close Game Bar with Win+G
                    await SendKeyboardShortcutViaHelper("Win+G");
                    Logger.Debug("Win+G sent to close Game Bar");

                    // Wait for Game Bar to close
                    await Task.Delay(150);
                }

                // Now send the actual shortcut
                await SendKeyboardShortcutViaHelper(shortcut);
                Logger.Info($"Custom shortcut sent: {shortcut}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending custom shortcut '{shortcut}': {ex.Message}");
            }
        }

        private void SavePowerPlanSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["PowerPlan_AC"] = acPowerPlanGuid.ToString();
                settings.Values["PowerPlan_DC"] = dcPowerPlanGuid.ToString();
                settings.Values["PowerPlan_AutoSwitch"] = powerPlanAutoSwitch;
                Logger.Info("Power plan settings saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving power plan settings: {ex.Message}");
            }
        }

        private void LoadPowerPlanSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("PowerPlan_AC", out object acVal) && acVal is string acStr)
                {
                    if (Guid.TryParse(acStr, out Guid acGuid))
                    {
                        acPowerPlanGuid = acGuid;
                    }
                }

                if (settings.Values.TryGetValue("PowerPlan_DC", out object dcVal) && dcVal is string dcStr)
                {
                    if (Guid.TryParse(dcStr, out Guid dcGuid))
                    {
                        dcPowerPlanGuid = dcGuid;
                    }
                }

                if (settings.Values.TryGetValue("PowerPlan_AutoSwitch", out object autoVal))
                {
                    // Handle different possible types stored in settings
                    if (autoVal is bool autoSwitch)
                    {
                        powerPlanAutoSwitch = autoSwitch;
                    }
                    else if (autoVal is string autoStr)
                    {
                        powerPlanAutoSwitch = autoStr.Equals("True", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        Logger.Warn($"PowerPlan_AutoSwitch has unexpected type: {autoVal?.GetType().Name ?? "null"}");
                    }
                }
                else
                {
                    Logger.Info("PowerPlan_AutoSwitch not found in settings, using default (OFF)");
                }

                // Note: If GUIDs are empty, LoadPowerPlans() will use the current active plan as default

                Logger.Info($"Power plan settings loaded: AC={acPowerPlanGuid}, DC={dcPowerPlanGuid}, AutoSwitch={powerPlanAutoSwitch}");

                // Immediately sync the toggle UI to the loaded value
                // Use isLoadingPowerPlans flag to prevent Toggled event from triggering a save
                isLoadingPowerPlans = true;
                try
                {
                    if (PowerPlanAutoSwitchToggle != null)
                    {
                        PowerPlanAutoSwitchToggle.IsOn = powerPlanAutoSwitch;
                        Logger.Info($"PowerPlanAutoSwitchToggle UI synced to {powerPlanAutoSwitch}");
                    }
                }
                finally
                {
                    isLoadingPowerPlans = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plan settings: {ex.Message}");
            }
        }

        private void LoadForceDefaultGameProfileSetting()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("ForceDefaultGameProfile", out object val) && val is bool enabled)
                {
                    if (ForceDefaultGameProfileToggle != null)
                    {
                        ForceDefaultGameProfileToggle.IsOn = enabled;
                    }
                    // Send to helper on startup
                    forceDefaultGameProfile?.SetValue(enabled);
                    Logger.Info($"Loaded Force Default Game Profile setting: {enabled}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading Force Default Game Profile setting: {ex.Message}");
            }
        }

        private void ColorSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isColorSettingsExpanded = !isColorSettingsExpanded;

            if (ColorSettingsContent != null)
            {
                ColorSettingsContent.Visibility = isColorSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ColorSettingsExpandButton != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ColorSettingsExpandButton.Content = isColorSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TDPLimitsMinSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPLimits) return;
            if (TDPLimitsMinSlider == null || TDPLimitsMaxSlider == null) return;

            int minValue = (int)Math.Round(e.NewValue);

            // Ensure min doesn't exceed max
            if (minValue > TDPLimitsMaxSlider.Value)
            {
                TDPLimitsMinSlider.Value = TDPLimitsMaxSlider.Value;
                return;
            }

            deviceTDPMin = minValue;

            if (TDPLimitsMinValue != null)
            {
                TDPLimitsMinValue.Text = $"{minValue}W";
            }

            // Update TDP slider bounds immediately (for UI responsiveness)
            UpdateTDPSliderBounds();

            // Debounce save and send to helper
            StartTDPLimitsDebounce();
        }

        private void TDPLimitsMaxSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingTDPLimits) return;
            if (TDPLimitsMinSlider == null || TDPLimitsMaxSlider == null) return;

            int maxValue = (int)Math.Round(e.NewValue);

            // Ensure max doesn't go below min
            if (maxValue < TDPLimitsMinSlider.Value)
            {
                TDPLimitsMaxSlider.Value = TDPLimitsMinSlider.Value;
                return;
            }

            deviceTDPMax = maxValue;

            if (TDPLimitsMaxValue != null)
            {
                TDPLimitsMaxValue.Text = $"{maxValue}W";
            }

            // Update TDP slider bounds immediately (for UI responsiveness)
            UpdateTDPSliderBounds();

            // Debounce save and send to helper
            StartTDPLimitsDebounce();
        }

        private void StartTDPLimitsDebounce()
        {
            // Initialize debounce timer if needed
            if (tdpLimitsDebounceTimer == null)
            {
                tdpLimitsDebounceTimer = new DispatcherTimer();
                tdpLimitsDebounceTimer.Interval = TimeSpan.FromMilliseconds(TDP_LIMITS_DEBOUNCE_MS);
                tdpLimitsDebounceTimer.Tick += TDPLimitsDebounceTimer_Tick;
            }

            // Restart the debounce timer
            tdpLimitsDebounceTimer.Stop();
            tdpLimitsDebounceTimer.Start();
        }

        private void TDPLimitsDebounceTimer_Tick(object sender, object e)
        {
            tdpLimitsDebounceTimer?.Stop();

            // Save and send to helper after debounce
            SaveTDPLimitsToStorage();
            SendTDPLimitsToHelper();
        }

        private void UpdateTDPSliderBounds()
        {
            // Update Performance tab TDP slider
            if (TDPSlider != null)
            {
                TDPSlider.Minimum = deviceTDPMin;
                TDPSlider.Maximum = deviceTDPMax;

                // Clamp current value if out of bounds
                if (TDPSlider.Value < deviceTDPMin)
                    TDPSlider.Value = deviceTDPMin;
                else if (TDPSlider.Value > deviceTDPMax)
                    TDPSlider.Value = deviceTDPMax;
            }

            // Update AutoTDP Min slider bounds
            if (AutoTDPMinSlider != null)
            {
                AutoTDPMinSlider.Minimum = deviceTDPMin;
                AutoTDPMinSlider.Maximum = deviceTDPMax;

                // Clamp current value if out of bounds
                if (AutoTDPMinSlider.Value < deviceTDPMin)
                    AutoTDPMinSlider.Value = deviceTDPMin;
                else if (AutoTDPMinSlider.Value > deviceTDPMax)
                    AutoTDPMinSlider.Value = deviceTDPMax;
            }

            // Update AutoTDP Max slider bounds
            if (AutoTDPMaxSlider != null)
            {
                AutoTDPMaxSlider.Minimum = deviceTDPMin;
                AutoTDPMaxSlider.Maximum = deviceTDPMax;

                // Clamp current value if out of bounds
                if (AutoTDPMaxSlider.Value < deviceTDPMin)
                    AutoTDPMaxSlider.Value = deviceTDPMin;
                else if (AutoTDPMaxSlider.Value > deviceTDPMax)
                    AutoTDPMaxSlider.Value = deviceTDPMax;
            }
        }

        private void ApplyTDPLimits()
        {
            // Update TDP slider bounds
            UpdateTDPSliderBounds();

            // Send limits to helper for AutoTDP
            SendTDPLimitsToHelper();
        }

        private void SendTDPLimitsToHelper()
        {
            try
            {
                string limitsString = $"{deviceTDPMin},{deviceTDPMax}";
                tdpLimits?.SetValue(limitsString);
                Logger.Info($"Sent TDP limits to helper: Min={deviceTDPMin}W, Max={deviceTDPMax}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send TDP limits to helper: {ex.Message}");
            }
        }

        private void SaveTDPLimitsToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["DeviceTDPMin"] = deviceTDPMin;
                settings.Values["DeviceTDPMax"] = deviceTDPMax;
                Logger.Info($"Saved TDP limits: Min={deviceTDPMin}W, Max={deviceTDPMax}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save TDP limits: {ex.Message}");
            }
        }

        private void LoadTDPLimitsFromStorage()
        {
            isLoadingTDPLimits = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("DeviceTDPMin", out object minObj) && minObj is int min)
                {
                    deviceTDPMin = min;
                }

                if (settings.Values.TryGetValue("DeviceTDPMax", out object maxObj) && maxObj is int max)
                {
                    deviceTDPMax = max;
                }

                // Update UI
                if (TDPLimitsMinSlider != null)
                {
                    TDPLimitsMinSlider.Value = deviceTDPMin;
                    if (TDPLimitsMinValue != null)
                        TDPLimitsMinValue.Text = $"{deviceTDPMin}W";
                }

                if (TDPLimitsMaxSlider != null)
                {
                    TDPLimitsMaxSlider.Value = deviceTDPMax;
                    if (TDPLimitsMaxValue != null)
                        TDPLimitsMaxValue.Text = $"{deviceTDPMax}W";
                }

                // Apply to TDP slider
                ApplyTDPLimits();

                Logger.Info($"Loaded TDP limits: Min={deviceTDPMin}W, Max={deviceTDPMax}W");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load TDP limits: {ex.Message}");
            }
            finally
            {
                isLoadingTDPLimits = false;
            }
        }

        #region Advanced (Core Parking & Affinity)

        private bool isAdvancedExpanded = false;
        private bool isLoadingCPUCoreConfig = false;
        private int totalPCores = 3;  // Default for Z2E
        private int totalECores = 5;  // Default for Z2E
        private int totalCores = 8;   // Total logical cores
        private int activePCores = 3;
        private int activeECores = 5;
        private int parkedCores = 0;  // Number of cores to park (0 = all active)
        private bool isHybridCPU = false;
        private bool isLoadingCoreParking = false;

        private void AdvancedExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isAdvancedExpanded = !isAdvancedExpanded;

            if (AdvancedContent != null)
            {
                AdvancedContent.Visibility = isAdvancedExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AdvancedExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                AdvancedExpandIcon.Glyph = isAdvancedExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void CoreParkingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingCoreParking) return;
            if (CoreParkingComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int activeCores))
                {
                    parkedCores = totalCores - activeCores;
                    UpdateCoreParkingDescription(activeCores);
                    UpdateCPUCoreConfigSummary();
                    SaveCoreParkingToStorage();
                    SendCoreParkingToHelper(activeCores);
                    Logger.Info($"Core parking changed to: {activeCores} active cores ({parkedCores} parked)");
                }
            }
        }

        private void UpdateCoreParkingDescription(int activeCores)
        {
            if (CoreParkingDescription != null)
            {
                if (activeCores >= totalCores)
                {
                    CoreParkingDescription.Text = "All cores active";
                }
                else
                {
                    CoreParkingDescription.Text = $"{totalCores - activeCores} cores parked";
                }
            }
        }

        private void SetupCoreParkingUI()
        {
            isLoadingCoreParking = true;
            try
            {
                // Get total logical processor count
                totalCores = Environment.ProcessorCount;

                if (CoreParkingComboBox != null)
                {
                    CoreParkingComboBox.Items.Clear();

                    // Add "All" option first
                    var allItem = new ComboBoxItem { Content = $"All ({totalCores})", Tag = totalCores.ToString() };
                    CoreParkingComboBox.Items.Add(allItem);

                    // Add options for reducing cores (by 2s for larger counts)
                    int step = totalCores > 8 ? 2 : 1;
                    for (int i = totalCores - step; i >= 2; i -= step)
                    {
                        var item = new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() };
                        CoreParkingComboBox.Items.Add(item);
                    }

                    // Load saved setting
                    LoadCoreParkingFromStorage();
                }

                Logger.Info($"Core Parking UI setup: {totalCores} total cores");
            }
            finally
            {
                isLoadingCoreParking = false;
            }
        }

        private void SaveCoreParkingToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["CoreParkingActiveCores"] = totalCores - parkedCores;
                Logger.Info($"Saved core parking: {totalCores - parkedCores} active");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save core parking: {ex.Message}");
            }
        }

        private void LoadCoreParkingFromStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                int activeCores = totalCores; // Default to all active

                if (settings.Values.TryGetValue("CoreParkingActiveCores", out object val) && val is int saved)
                {
                    activeCores = Math.Min(saved, totalCores); // Clamp to current max
                }

                parkedCores = totalCores - activeCores;

                // Select the matching item
                if (CoreParkingComboBox != null)
                {
                    foreach (ComboBoxItem item in CoreParkingComboBox.Items)
                    {
                        if (item.Tag is string tagStr && int.TryParse(tagStr, out int tagVal) && tagVal == activeCores)
                        {
                            CoreParkingComboBox.SelectedItem = item;
                            break;
                        }
                    }

                    // If no match, select first (all cores)
                    if (CoreParkingComboBox.SelectedItem == null && CoreParkingComboBox.Items.Count > 0)
                    {
                        CoreParkingComboBox.SelectedIndex = 0;
                    }
                }

                UpdateCoreParkingDescription(activeCores);
                Logger.Info($"Loaded core parking: {activeCores} active");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load core parking: {ex.Message}");
            }
        }

        private void SendCoreParkingToHelper(int activeCores)
        {
            // Calculate percentage for CPMAXCORES
            // activeCores / totalCores * 100 = percentage of cores that can be unparked
            int percent = (int)Math.Ceiling((double)activeCores / totalCores * 100);
            percent = Math.Clamp(percent, 1, 100); // At least 1%, max 100%

            if (coreParkingPercent != null)
            {
                coreParkingPercent.SetValue(percent);
                Logger.Info($"Core parking: set {percent}% ({activeCores}/{totalCores} cores)");
            }
        }

        private void PCoreCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingCPUCoreConfig) return;
            if (PCoreCountComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int count))
                {
                    // Prevent both P-Cores and E-Cores from being 0
                    if (count == 0 && activeECores == 0)
                    {
                        Logger.Warn("Cannot disable both P-Cores and E-Cores, reverting selection");
                        // Revert to previous value
                        isLoadingCPUCoreConfig = true;
                        UpdatePCoreComboBox();
                        isLoadingCPUCoreConfig = false;
                        return;
                    }

                    activePCores = count;
                    UpdateCPUCoreConfigSummary();
                    SaveCPUCoreConfigToStorage();
                    SendCPUCoreConfigToHelper();
                    Logger.Info($"P-Core count changed to: {activePCores}");
                }
            }
        }

        private void ECoreCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingCPUCoreConfig) return;
            if (ECoreCountComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int count))
                {
                    // Prevent both P-Cores and E-Cores from being 0
                    if (count == 0 && activePCores == 0)
                    {
                        Logger.Warn("Cannot disable both P-Cores and E-Cores, reverting selection");
                        // Revert to previous value
                        isLoadingCPUCoreConfig = true;
                        UpdateECoreComboBox();
                        isLoadingCPUCoreConfig = false;
                        return;
                    }

                    activeECores = count;
                    UpdateCPUCoreConfigSummary();
                    SaveCPUCoreConfigToStorage();
                    SendCPUCoreConfigToHelper();
                    Logger.Info($"E-Core count changed to: {activeECores}");
                }
            }
        }

        private void SendCPUCoreConfigToHelper()
        {
            if (cpuCoreActiveConfig != null && isHybridCPU)
            {
                // Send affinity config
                string configString = $"{activePCores},{activeECores}";
                cpuCoreActiveConfig.SetValue(configString);
                Logger.Info($"Sent CPU core config to helper: {configString}");

                // Also send core parking percentage based on total active cores
                // For hybrid: active cores = activePCores threads + activeECores threads
                // Assuming SMT: P-Cores have 2 threads, E-Cores have 1 thread (AMD Z2E)
                int activeThreads = (activePCores * 2) + activeECores;
                int percent = (int)Math.Ceiling((double)activeThreads / totalCores * 100);
                percent = Math.Clamp(percent, 1, 100);

                if (coreParkingPercent != null)
                {
                    coreParkingPercent.SetValue(percent);
                    Logger.Info($"Core parking: set {percent}% ({activeThreads}/{totalCores} threads)");
                }
            }
        }

        private void ForceParkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ForceParkModeToggle == null) return;
            if (isLoadingCPUCoreConfig) return;

            bool enabled = ForceParkModeToggle.IsOn;
            Logger.Info($"Force Park Mode toggled to: {enabled}");

            // Send to helper
            forceParkMode?.SetValue(enabled);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ForceParkMode"] = enabled;
        }

        private void ForceDefaultGameProfileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ForceDefaultGameProfileToggle == null) return;

            bool enabled = ForceDefaultGameProfileToggle.IsOn;
            Logger.Info($"Force Default Game Profile toggled to: {enabled}");

            // Send to helper
            forceDefaultGameProfile?.SetValue(enabled);

            // Save to local settings
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ForceDefaultGameProfile"] = enabled;
        }

        #region Debug Panel Handlers

        private void DebugExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isDebugExpanded = !isDebugExpanded;

            if (DebugContent != null)
            {
                DebugContent.Visibility = isDebugExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (DebugExpandIcon != null)
            {
                DebugExpandIcon.Glyph = isDebugExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private bool isThemeInitialized = false;

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isThemeInitialized) return; // Don't save until initial load completes

            if (ThemeComboBox?.SelectedItem is ComboBoxItem item)
            {
                string themeName = item.Content?.ToString() ?? "Default";
                ApplyTheme(themeName);
                SaveThemeSetting(themeName);
            }
        }

        private void ApplyTheme(string themeName)
        {
            if (!WidgetThemes.TryGetValue(themeName, out var theme))
            {
                Logger.Warn($"Theme '{themeName}' not found, using Default");
                theme = WidgetThemes["Default"];
                themeName = "Default";
            }

            currentThemeName = themeName;
            Logger.Info($"Applying theme: {themeName}");

            // Update page background
            this.Background = new SolidColorBrush(theme.PageBackground);
            widgetDarkThemeBrush = new SolidColorBrush(theme.PageBackground);

            // Update resource brushes (for new elements)
            try
            {
                Resources["PageBackgroundBrush"] = new SolidColorBrush(theme.PageBackground);
                Resources["CardBackgroundBrush"] = new SolidColorBrush(theme.CardBackground);
                Resources["CardBorderBrush"] = new SolidColorBrush(theme.CardBorder);
                Resources["ButtonBackground"] = new SolidColorBrush(theme.ButtonBackground);
                Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
                Resources["TileOffBackground"] = new SolidColorBrush(theme.TileOff);
                Resources["TileOnBackground"] = new SolidColorBrush(theme.TileOn);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating theme resources: {ex.Message}");
            }

            // Manually update existing elements (StaticResource doesn't update at runtime)
            try
            {
                var cardBgBrush = new SolidColorBrush(theme.CardBackground);
                var cardBorderBrush = new SolidColorBrush(theme.CardBorder);
                var accentBrush = new SolidColorBrush(theme.AccentColor);
                var textSecondaryBrush = new SolidColorBrush(theme.TextSecondary);

                // Update all Border elements (cards)
                ApplyThemeToVisualTree(this, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);

                Logger.Info($"Theme '{themeName}' applied to visual tree");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying theme to visual tree: {ex.Message}");
            }
        }

        private void ApplyThemeToVisualTree(DependencyObject parent, ThemeColors theme,
            SolidColorBrush cardBgBrush, SolidColorBrush cardBorderBrush,
            SolidColorBrush accentBrush, SolidColorBrush textSecondaryBrush)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Update Border elements (cards use CardStyle with specific properties)
                if (child is Border border)
                {
                    // Check if this looks like a card (has corner radius and padding typical of CardStyle)
                    // Skip borders with LinearGradientBrush backgrounds (custom gradients for "smart" features like DGP card)
                    if (border.CornerRadius.TopLeft == 8 && border.Padding.Left == 12 &&
                        !(border.Background is LinearGradientBrush))
                    {
                        border.Background = cardBgBrush;
                        border.BorderBrush = cardBorderBrush;
                    }
                }

                // Update accent-colored TextBlocks (section headers, card values)
                if (child is TextBlock textBlock)
                {
                    if (textBlock.Foreground is SolidColorBrush brush)
                    {
                        // Check for cyan accent color (#00C8FF) - update to new accent
                        if (brush.Color.R == 0 && brush.Color.G == 200 && brush.Color.B == 255)
                        {
                            textBlock.Foreground = accentBrush;
                        }
                        // Check for secondary text color (#A0A0A0)
                        else if (brush.Color.R == 160 && brush.Color.G == 160 && brush.Color.B == 160)
                        {
                            textBlock.Foreground = textSecondaryBrush;
                        }
                    }
                }

                // Recurse into children
                ApplyThemeToVisualTree(child, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            }
        }

        private async Task ApplyThemeOnLoadAsync(string themeName)
        {
            // Wait for UI to fully initialize
            await Task.Delay(100);

            try
            {
                // Set ComboBox selection (isThemeInitialized is still false, so this won't trigger save)
                if (ThemeComboBox != null)
                {
                    for (int i = 0; i < ThemeComboBox.Items.Count; i++)
                    {
                        if (ThemeComboBox.Items[i] is ComboBoxItem item && item.Content?.ToString() == themeName)
                        {
                            ThemeComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                ApplyTheme(themeName);

                // Apply to all tabs to prevent flash when switching
                ApplyThemeToCurrentTab();
            }
            finally
            {
                // Now allow saves on future changes
                isThemeInitialized = true;
            }
        }

        private void SaveThemeSetting(string themeName)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values["WidgetTheme"] = themeName;
                Logger.Info($"Theme setting saved: {themeName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save theme setting: {ex.Message}");
            }
        }

        private void LoadThemeSetting()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("WidgetTheme", out var saved) && saved is string themeName)
                {
                    currentThemeName = themeName;
                    Logger.Info($"Theme loaded from settings: {themeName}");

                    // Defer visual updates until UI is fully ready
                    _ = ApplyThemeOnLoadAsync(themeName);
                }
                else
                {
                    // No saved theme - mark as initialized so user can save their choice
                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    {
                        isThemeInitialized = true;
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load theme setting: {ex.Message}");
                isThemeInitialized = true; // Allow saves even on error
            }
        }

        private bool isAboutExpanded = false;

        private void AboutExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isAboutExpanded = !isAboutExpanded;

            if (AboutContent != null)
            {
                AboutContent.Visibility = isAboutExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AboutExpandIcon != null)
            {
                AboutExpandIcon.Glyph = isAboutExpanded ? "\uE70E" : "\uE70D";
            }

            // Update version text dynamically
            if (isAboutExpanded && AboutVersionText != null)
            {
                try
                {
                    var version = Windows.ApplicationModel.Package.Current.Id.Version;
                    AboutVersionText.Text = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
                catch
                {
                    // Keep default version text
                }
            }
        }

        private async void DonateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Send message to helper to launch URL (Game Bar blocks direct URL launching)
                if (App.Connection != null)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("LaunchUrl", "https://paypal.me/corando98");
                    await App.Connection.SendMessageAsync(message);
                    Logger.Info("Sent LaunchUrl request to helper");
                }
                else
                {
                    Logger.Warn("Cannot launch donate URL - no connection to helper");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send donate link request: {ex.Message}");
            }
        }

        private async void RestartHelperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RestartHelperButton.IsEnabled = false;
                RestartHelperButton.Content = "Restarting...";

                // Send exit command to helper via AppServiceConnection
                if (App.Connection != null)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExitHelper", true);

                    Logger.Info("Sending ExitHelper command to helper");
                    var response = await App.Connection.SendMessageAsync(message);

                    if (response.Status == AppServiceResponseStatus.Success)
                    {
                        Logger.Info("Helper acknowledged exit command");
                    }
                }

                // Wait for helper to exit and release mutex
                await Task.Delay(1500);

                // Launch new helper instance
                Logger.Info("Launching new helper instance");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();

                await Task.Delay(2000);
                RestartHelperButton.Content = "Restart Helper";
                RestartHelperButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restart helper: {ex.Message}");
                RestartHelperButton.Content = "Restart Helper";
                RestartHelperButton.IsEnabled = true;
            }
        }

        private async void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportLogsButton.IsEnabled = false;
                ExportLogsButton.Content = "Exporting...";

                // Send export logs command to helper via AppServiceConnection
                if (App.Connection != null)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExportLogs", true);

                    Logger.Info("Sending ExportLogs command to helper");
                    var response = await App.Connection.SendMessageAsync(message);

                    if (response.Status == AppServiceResponseStatus.Success)
                    {
                        bool success = false;
                        if (response.Message.TryGetValue("Success", out object successObj) && successObj is bool successVal)
                            success = successVal;

                        if (success)
                        {
                            var path = response.Message.TryGetValue("Path", out object pathObj) ? pathObj as string : "Desktop";
                            Logger.Info($"Logs exported successfully to: {path}");
                            ExportLogsButton.Content = "Exported!";
                        }
                        else
                        {
                            var error = response.Message.TryGetValue("Error", out object errorObj) ? errorObj as string : "Unknown error";
                            Logger.Error($"Export logs failed: {error}");
                            ExportLogsButton.Content = "Export Failed";
                        }
                    }
                    else
                    {
                        Logger.Error($"Export logs request failed: {response.Status}");
                        ExportLogsButton.Content = "Export Failed";
                    }
                }
                else
                {
                    Logger.Error("Cannot export logs - no connection to helper");
                    ExportLogsButton.Content = "No Helper";
                }

                await Task.Delay(2000);
                ExportLogsButton.Content = "Export Logs";
                ExportLogsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export logs: {ex.Message}");
                ExportLogsButton.Content = "Export Failed";
                await Task.Delay(2000);
                ExportLogsButton.Content = "Export Logs";
                ExportLogsButton.IsEnabled = true;
            }
        }

        private async void KillGoTweaksButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Kill GoTweaks requested by user");

                // Send exit command to helper first
                if (App.Connection != null)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("ExitHelper", true);

                    Logger.Info("Sending ExitHelper command before killing widget");
                    await App.Connection.SendMessageAsync(message);

                    // Give helper time to exit
                    await Task.Delay(500);
                }

                // Exit the widget application
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to kill GoTweaks: {ex.Message}");
                // Still try to exit even if helper communication failed
                Application.Current.Exit();
            }
        }

        /// <summary>
        /// Compares two version strings (e.g., "v0.3.902" vs "v0.3.1001.0").
        /// Returns true if latestVersion is newer than currentVersion.
        /// </summary>
        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            // Strip 'v' prefix if present
            var latest = latestVersion.TrimStart('v', 'V');
            var current = currentVersion.TrimStart('v', 'V');

            // Split into parts
            var latestParts = latest.Split('.');
            var currentParts = current.Split('.');

            // Compare each part numerically
            int maxLength = Math.Max(latestParts.Length, currentParts.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int latestNum = 0;
                int currentNum = 0;

                if (i < latestParts.Length && int.TryParse(latestParts[i], out int lp))
                    latestNum = lp;
                if (i < currentParts.Length && int.TryParse(currentParts[i], out int cp))
                    currentNum = cp;

                if (latestNum > currentNum)
                    return true;
                if (latestNum < currentNum)
                    return false;
            }

            return false; // Versions are equal
        }

        private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckForUpdateButton.IsEnabled = false;
                CheckForUpdateButton.Content = "Checking...";
                UpdateStatusText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "Checking for updates...";
                UpdateButton.Visibility = Visibility.Collapsed;
                _pendingUpdateZipUrl = null;
                _pendingUpdateVersion = null;

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "GoTweaks-UpdateChecker");
                    var response = await httpClient.GetStringAsync("https://api.github.com/repos/corando98/GoTweaks/releases/latest");

                    // Parse JSON response using Windows.Data.Json
                    var jsonObject = Windows.Data.Json.JsonObject.Parse(response);
                    var latestVersion = jsonObject.GetNamedString("tag_name", "");

                    // Get current version from package
                    var packageVersion = Package.Current.Id.Version;
                    var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";

                    Logger.Info($"Update check: current={currentVersion}, latest={latestVersion}");

                    if (!string.IsNullOrEmpty(latestVersion) && IsNewerVersion(latestVersion, currentVersion))
                    {
                        // Find the .zip asset download URL
                        string zipUrl = null;
                        if (jsonObject.ContainsKey("assets"))
                        {
                            var assets = jsonObject.GetNamedArray("assets");
                            foreach (var asset in assets)
                            {
                                var assetObj = asset.GetObject();
                                var name = assetObj.GetNamedString("name", "");
                                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    zipUrl = assetObj.GetNamedString("browser_download_url", "");
                                    break;
                                }
                            }
                        }

                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                        UpdateStatusText.Text = $"New version available: {latestVersion}\nCurrent: {currentVersion}";

                        if (!string.IsNullOrEmpty(zipUrl))
                        {
                            _pendingUpdateZipUrl = zipUrl;
                            _pendingUpdateVersion = latestVersion;
                            UpdateButton.Visibility = Visibility.Visible;
                            Logger.Info($"Update zip URL found: {zipUrl}");
                        }
                        else
                        {
                            UpdateStatusText.Text += "\n(No zip asset found in release)";
                            Logger.Warn("No zip asset found in latest release");
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
                        UpdateStatusText.Text = $"You're up to date! ({currentVersion})";
                    }
                }

                CheckForUpdateButton.Content = "Check for Update";
                CheckForUpdateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Failed to check for updates: {ex.Message}";
                CheckForUpdateButton.Content = "Check for Update";
                CheckForUpdateButton.IsEnabled = true;
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingUpdateZipUrl))
            {
                Logger.Warn("Update clicked but no pending update URL");
                return;
            }

            try
            {
                UpdateButton.IsEnabled = false;
                UpdateButton.Content = "Downloading...";
                UpdateStatusText.Text = $"Downloading {_pendingUpdateVersion}...";

                if (App.Connection != null)
                {
                    var message = new Windows.Foundation.Collections.ValueSet();
                    message.Add("DownloadAndInstallUpdate", _pendingUpdateZipUrl);
                    var result = await App.Connection.SendMessageAsync(message);

                    if (result.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                    {
                        if (result.Message.TryGetValue("UpdateStatus", out object status))
                        {
                            var statusStr = status?.ToString() ?? "";
                            if (statusStr == "Installing")
                            {
                                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                                UpdateStatusText.Text = "Installing update... Please follow the installer prompts.";
                                UpdateButton.Content = "Installing...";
                            }
                            else if (statusStr.StartsWith("Error"))
                            {
                                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                                UpdateStatusText.Text = statusStr;
                                UpdateButton.Content = "Update";
                                UpdateButton.IsEnabled = true;
                            }
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = "Failed to communicate with helper";
                        UpdateButton.Content = "Update";
                        UpdateButton.IsEnabled = true;
                    }
                }
                else
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Helper not connected";
                    UpdateButton.Content = "Update";
                    UpdateButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Update failed: {ex.Message}";
                UpdateButton.Content = "Update";
                UpdateButton.IsEnabled = true;
            }
        }

        private async void CheckForUpdateDebugButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckForUpdateDebugButton.IsEnabled = false;
                CheckForUpdateDebugButton.Content = "Checking...";
                UpdateStatusText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "Checking local AppPackages...";
                UpdateButton.Visibility = Visibility.Collapsed;
                _pendingUpdateZipUrl = null;
                _pendingUpdateVersion = null;

                if (App.Connection == null)
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Helper not connected";
                    CheckForUpdateDebugButton.Content = "Check for Update (Debug)";
                    CheckForUpdateDebugButton.IsEnabled = true;
                    return;
                }

                // Ask helper to check for local updates (helper has file system access)
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("CheckLocalUpdate", true);
                var result = await App.Connection.SendMessageAsync(message);

                if (result.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (result.Message.TryGetValue("Error", out object errorObj))
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = errorObj?.ToString() ?? "Unknown error";
                    }
                    else if (result.Message.TryGetValue("LatestVersion", out object versionObj) &&
                             result.Message.TryGetValue("MsixbundlePath", out object pathObj))
                    {
                        var foundVersionStr = versionObj?.ToString();
                        var msixbundlePath = pathObj?.ToString();
                        var folderName = result.Message.TryGetValue("FolderName", out object folderObj) ? folderObj?.ToString() : "";

                        // Get current version
                        var packageVersion = Package.Current.Id.Version;
                        var currentVersion = $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
                        var foundVersion = $"v{foundVersionStr}";

                        Logger.Info($"Debug update check: current={currentVersion}, found={foundVersion}, path={msixbundlePath}");

                        // Compare versions
                        var currentVer = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
                        if (Version.TryParse(foundVersionStr, out var latestVersion) && latestVersion > currentVer)
                        {
                            UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                            UpdateStatusText.Text = $"[DEBUG] New version found: {foundVersion}\nCurrent: {currentVersion}\n{folderName}";
                            _pendingUpdateZipUrl = msixbundlePath; // Local path to msixbundle
                            _pendingUpdateVersion = foundVersion;
                            UpdateButton.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
                            UpdateStatusText.Text = $"[DEBUG] You're up to date! ({currentVersion})\nLatest in AppPackages: {foundVersion}";
                        }
                    }
                    else
                    {
                        UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        UpdateStatusText.Text = "Invalid response from helper";
                    }
                }
                else
                {
                    UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    UpdateStatusText.Text = "Failed to communicate with helper";
                }

                CheckForUpdateDebugButton.Content = "Check for Update (Debug)";
                CheckForUpdateDebugButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check for debug update: {ex.Message}");
                UpdateStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                UpdateStatusText.Text = $"Failed: {ex.Message}";
                CheckForUpdateDebugButton.Content = "Check for Update (Debug)";
                CheckForUpdateDebugButton.IsEnabled = true;
            }
        }

        private async void ExportDGPsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportDGPsButton.IsEnabled = false;
                ExportDGPsButton.Content = "Exporting...";

                if (App.Connection == null)
                {
                    ExportDGPsButton.Content = "Helper not connected";
                    await Task.Delay(2000);
                    ExportDGPsButton.Content = "Export DGPs (Desktop)";
                    ExportDGPsButton.IsEnabled = true;
                    return;
                }

                // Send request to helper to export DGPs
                var message = new Windows.Foundation.Collections.ValueSet();
                message.Add("Command", (int)Shared.Enums.Command.Set);
                message.Add("Function", (int)Shared.Enums.Function.Debug_ExportDGPs);
                var result = await App.Connection.SendMessageAsync(message);

                if (result.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (result.Message.TryGetValue("ExportPath", out object pathObj))
                    {
                        ExportDGPsButton.Content = $"Exported!";
                        Logger.Info($"DGPs exported to: {pathObj}");
                    }
                    else if (result.Message.TryGetValue("Error", out object errorObj))
                    {
                        ExportDGPsButton.Content = $"Error: {errorObj}";
                    }
                }
                else
                {
                    ExportDGPsButton.Content = "Failed";
                }

                await Task.Delay(2000);
                ExportDGPsButton.Content = "Export DGPs (Desktop)";
                ExportDGPsButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export DGPs: {ex.Message}");
                ExportDGPsButton.Content = "Export DGPs (Desktop)";
                ExportDGPsButton.IsEnabled = true;
            }
        }

        #endregion

        private void UpdateCPUCoreConfigSummary()
        {
            // Update the Advanced card summary with current settings
            if (AdvancedSummary != null)
            {
                int activeCoresParking = totalCores - parkedCores;
                if (isHybridCPU)
                {
                    if (parkedCores > 0)
                    {
                        AdvancedSummary.Text = $"Parking: {activeCoresParking}/{totalCores} cores | Affinity: {activePCores}P + {activeECores}E";
                    }
                    else
                    {
                        AdvancedSummary.Text = $"Affinity: {activePCores}P + {activeECores}E cores";
                    }
                }
                else
                {
                    if (parkedCores > 0)
                    {
                        AdvancedSummary.Text = $"Core parking: {activeCoresParking}/{totalCores} cores active";
                    }
                    else
                    {
                        AdvancedSummary.Text = "Core parking and affinity settings";
                    }
                }
            }
        }

        private void SaveCPUCoreConfigToStorage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["ActivePCores"] = activePCores;
                settings.Values["ActiveECores"] = activeECores;
                Logger.Info($"Saved CPU core config: P={activePCores}, E={activeECores}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save CPU core config: {ex.Message}");
            }
        }

        private void LoadCPUCoreConfigFromStorage()
        {
            isLoadingCPUCoreConfig = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("ActivePCores", out object pObj) && pObj is int pCores)
                {
                    activePCores = pCores;
                }

                if (settings.Values.TryGetValue("ActiveECores", out object eObj) && eObj is int eCores)
                {
                    activeECores = eCores;
                }

                // Load Force Park Mode setting
                if (settings.Values.TryGetValue("ForceParkMode", out object fpObj) && fpObj is bool fpEnabled)
                {
                    if (ForceParkModeToggle != null)
                    {
                        ForceParkModeToggle.IsOn = fpEnabled;
                    }
                    // Send to helper on startup
                    forceParkMode?.SetValue(fpEnabled);
                    Logger.Info($"Loaded Force Park Mode: {fpEnabled}");
                }

                // Update UI
                UpdatePCoreComboBox();
                UpdateECoreComboBox();
                UpdateCPUCoreConfigSummary();

                Logger.Info($"Loaded CPU core config: P={activePCores}, E={activeECores}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load CPU core config: {ex.Message}");
            }
            finally
            {
                isLoadingCPUCoreConfig = false;
            }
        }

        private void UpdatePCoreComboBox()
        {
            if (PCoreCountComboBox == null) return;

            foreach (ComboBoxItem item in PCoreCountComboBox.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out int val) && val == activePCores)
                {
                    PCoreCountComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void UpdateECoreComboBox()
        {
            if (ECoreCountComboBox == null) return;

            foreach (ComboBoxItem item in ECoreCountComboBox.Items)
            {
                if (item.Tag is string tagStr && int.TryParse(tagStr, out int val) && val == activeECores)
                {
                    ECoreCountComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SetupCPUCoreConfigUI(int pCoreCount, int eCoreCount)
        {
            isLoadingCPUCoreConfig = true;
            try
            {
                totalPCores = pCoreCount;
                totalECores = eCoreCount;
                isHybridCPU = pCoreCount > 0 && eCoreCount > 0;

                // For hybrid CPUs: show affinity section, hide core parking dropdown
                // For non-hybrid: show core parking dropdown, hide affinity section
                if (CoreAffinitySection != null)
                {
                    CoreAffinitySection.Visibility = isHybridCPU ? Visibility.Visible : Visibility.Collapsed;
                }
                if (CoreParkingSection != null)
                {
                    CoreParkingSection.Visibility = isHybridCPU ? Visibility.Collapsed : Visibility.Visible;
                }

                // Setup core parking UI for non-hybrid CPUs
                if (!isHybridCPU)
                {
                    SetupCoreParkingUI();
                }

                if (!isHybridCPU) return;

                // Populate P-Core combobox
                if (PCoreCountComboBox != null)
                {
                    PCoreCountComboBox.Items.Clear();
                    for (int i = 0; i <= pCoreCount; i++)
                    {
                        var item = new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() };
                        PCoreCountComboBox.Items.Add(item);
                    }
                }

                // Populate E-Core combobox
                if (ECoreCountComboBox != null)
                {
                    ECoreCountComboBox.Items.Clear();
                    for (int i = 0; i <= eCoreCount; i++)
                    {
                        var item = new ComboBoxItem { Content = i.ToString(), Tag = i.ToString() };
                        ECoreCountComboBox.Items.Add(item);
                    }
                }

                // Load saved config or use defaults (all cores active)
                LoadCPUCoreConfigFromStorage();

                // Ensure at least 1 core total is active
                if (activePCores == 0 && activeECores == 0)
                {
                    activePCores = pCoreCount;
                    activeECores = eCoreCount;
                }

                UpdatePCoreComboBox();
                UpdateECoreComboBox();
                UpdateCPUCoreConfigSummary();

                // Send the saved config to helper to apply on startup
                SendCPUCoreConfigToHelper();

                Logger.Info($"CPU Core Config UI setup: {pCoreCount}P + {eCoreCount}E cores (hybrid={isHybridCPU})");
            }
            finally
            {
                isLoadingCPUCoreConfig = false;
            }
        }

        #endregion

        private void OSDLayoutOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            // Get text size (global setting)
            if (OSDTextSizeComboBox?.SelectedItem is ComboBoxItem sizeItem && sizeItem.Tag is string sizeTag)
            {
                if (int.TryParse(sizeTag, out int size))
                {
                    osdTextSize = size;
                }
            }

            // Columns are per-level, handled by SaveCurrentOSDConfig
            SaveCurrentOSDConfig();
        }

        private void OSDTextColorDynamic_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            bool isDynamic = OSDTextColorDynamicCheckBox?.IsChecked == true;
            if (isDynamic)
            {
                osdTextColor = "DYNAMIC";
                UpdateOSDTextColorPreview();
            }
            else
            {
                // Use current color picker color
                if (OSDTextColorPicker != null)
                {
                    var color = OSDTextColorPicker.Color;
                    osdTextColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                }
                else
                {
                    osdTextColor = "FFFFFF";
                }
                UpdateOSDTextColorPreview();
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void OSDTextColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OSDTextColorPicker != null)
                {
                    bool isExpanded = OSDTextColorPicker.Visibility == Visibility.Visible;
                    OSDTextColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    if (OSDTextColorExpandButton != null)
                    {
                        OSDTextColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDTextColorExpandButton_Click: {ex.Message}");
            }
        }

        private void OSDTextColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (isLoadingOSDConfig) return;

            try
            {
                // Update preview
                if (OSDTextColorPreview != null)
                {
                    OSDTextColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                // Only update color if not in Dynamic mode
                if (OSDTextColorDynamicCheckBox?.IsChecked != true)
                {
                    osdTextColor = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDTextColorPicker_ColorChanged: {ex.Message}");
            }
        }

        private void OSDLabelColorDefault_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            bool isDefault = OSDLabelColorDefaultCheckBox?.IsChecked == true;
            if (isDefault)
            {
                osdLabelColor = "DEFAULT";
                UpdateOSDLabelColorPreview();
            }
            else
            {
                // Use current color picker color
                if (OSDLabelColorPicker != null)
                {
                    var color = OSDLabelColorPicker.Color;
                    osdLabelColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                }
                else
                {
                    osdLabelColor = "00FFFF";  // Cyan default
                }
                UpdateOSDLabelColorPreview();
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void OSDLabelColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OSDLabelColorPicker != null)
                {
                    bool isExpanded = OSDLabelColorPicker.Visibility == Visibility.Visible;
                    OSDLabelColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    if (OSDLabelColorExpandButton != null)
                    {
                        OSDLabelColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDLabelColorExpandButton_Click: {ex.Message}");
            }
        }

        private void OSDLabelColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (isLoadingOSDConfig) return;

            try
            {
                // Update preview
                if (OSDLabelColorPreview != null)
                {
                    OSDLabelColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                // Only update color if not in Default mode
                if (OSDLabelColorDefaultCheckBox?.IsChecked != true)
                {
                    osdLabelColor = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDLabelColorPicker_ColorChanged: {ex.Message}");
            }
        }

        private void UpdateOSDTextColorPreview()
        {
            if (OSDTextColorPreview == null) return;

            try
            {
                if (osdTextColor == "DYNAMIC")
                {
                    // Show gradient for dynamic color preview (blue to green to yellow to red)
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                    gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 128, 255), Offset = 0 });    // Blue (cold)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 0.33 });   // Green (good)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 255, 0), Offset = 0.66 }); // Yellow (warm)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 0, 0), Offset = 1 });      // Red (hot)
                    OSDTextColorPreview.Background = gradient;
                }
                else if (osdTextColor.Length == 6)
                {
                    var color = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(osdTextColor.Substring(0, 2), 16),
                        Convert.ToByte(osdTextColor.Substring(2, 2), 16),
                        Convert.ToByte(osdTextColor.Substring(4, 2), 16));
                    OSDTextColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch { }
        }

        private void UpdateOSDLabelColorPreview()
        {
            if (OSDLabelColorPreview == null) return;

            try
            {
                if (osdLabelColor == "DEFAULT")
                {
                    // Show gradient to indicate default (each item has its own color)
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                    gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 255), Offset = 0 });    // Cyan
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 165, 0), Offset = 0.5 });  // Orange
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 1 });      // Green
                    OSDLabelColorPreview.Background = gradient;
                }
                else if (osdLabelColor.Length == 6)
                {
                    var color = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(osdLabelColor.Substring(0, 2), 16),
                        Convert.ToByte(osdLabelColor.Substring(2, 2), 16),
                        Convert.ToByte(osdLabelColor.Substring(4, 2), 16));
                    OSDLabelColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch { }
        }

        private void UpdateOSDLayoutUI()
        {
            isLoadingOSDConfig = true;
            try
            {
                // Set OSD provider combobox
                if (OSDProviderComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDProviderComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == osdProvider)
                        {
                            OSDProviderComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Update provider-specific UI visibility
                UpdateOSDProviderUI();

                // Columns are per-level, loaded in LoadOSDOptionsForLevel

                // Set text size combobox
                if (OSDTextSizeComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDTextSizeComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == osdTextSize)
                        {
                            OSDTextSizeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Set text color checkbox and color picker
                if (OSDTextColorDynamicCheckBox != null)
                {
                    OSDTextColorDynamicCheckBox.IsChecked = (osdTextColor == "DYNAMIC");
                }
                if (OSDTextColorPicker != null && osdTextColor != "DYNAMIC" && osdTextColor.Length == 6)
                {
                    try
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(osdTextColor.Substring(0, 2), 16),
                            Convert.ToByte(osdTextColor.Substring(2, 2), 16),
                            Convert.ToByte(osdTextColor.Substring(4, 2), 16));
                        OSDTextColorPicker.Color = color;
                    }
                    catch { }
                }
                UpdateOSDTextColorPreview();

                // Set label color checkbox and color picker
                if (OSDLabelColorDefaultCheckBox != null)
                {
                    OSDLabelColorDefaultCheckBox.IsChecked = (osdLabelColor == "DEFAULT");
                }
                if (OSDLabelColorPicker != null && osdLabelColor != "DEFAULT" && osdLabelColor.Length == 6)
                {
                    try
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(osdLabelColor.Substring(0, 2), 16),
                            Convert.ToByte(osdLabelColor.Substring(2, 2), 16),
                            Convert.ToByte(osdLabelColor.Substring(4, 2), 16));
                        OSDLabelColorPicker.Color = color;
                    }
                    catch { }
                }
                UpdateOSDLabelColorPreview();

            }
            finally
            {
                isLoadingOSDConfig = false;
            }
        }

        #endregion

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
                bool powerSourceProfileEnabled = PowerSourceProfileToggle?.IsOn == true;
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
                if (PowerSourceProfileToggle.IsOn)
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
                if (!PowerSourceProfileToggle.IsOn)
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
                    // Save current profile before switching
                    // (isApplyingHelperUpdate check inside prevents race conditions)
                    SaveCurrentSettingsToProfile(currentProfileName);

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
                // Per-game profile - only if we have a VALID game name
                Logger.Info($"Using per-game profile for: {currentGameName}");

                if (PowerSourceProfileToggle.IsOn)
                {
                    return isOnAC ? $"Game_{currentGameName}_AC" : $"Game_{currentGameName}_DC";
                }
                else
                {
                    return $"Game_{currentGameName}";
                }
            }
            else
            {
                // Global profiles (used when: no valid game OR per-game disabled)
                if (perGameEnabled && !hasGame)
                {
                    Logger.Warn($"Per-game toggle is ON but no valid game detected, using global profile instead");
                }

                if (!PowerSourceProfileToggle.IsOn)
                {
                    return "Global";
                }
                else
                {
                    return isOnAC ? "AC" : "DC";
                }
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

            var profile = GetProfile(profileName);

            // Save only enabled settings
            if (SaveTDP && TDPSlider != null)
            {
                // Save TDP Mode for Legion devices
                if (legionGoDetected?.Value == true && TDPModeComboBox != null)
                {
                    int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
                    int selectedIndex = TDPModeComboBox.SelectedIndex;
                    if (selectedIndex >= 0 && selectedIndex < modeValues.Length)
                    {
                        profile.LegionPerformanceMode = modeValues[selectedIndex];

                        // Only save TDP slider value if in Custom mode (255)
                        // Preset modes (Quiet/Balanced/Performance) use hardware-defined TDP values
                        if (modeValues[selectedIndex] == 255)
                        {
                            profile.TDP = TDPSlider.Value;
                        }
                        // For preset modes, keep the profile's existing TDP value for when Custom mode is used later
                    }
                }
                else
                {
                    // Non-Legion devices: always save TDP
                    profile.TDP = TDPSlider.Value;
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
            // Sticky TDP
            if (SaveStickyTDP && StickyTDPToggle != null)
            {
                profile.StickyTDPEnabled = StickyTDPToggle.IsOn;
                profile.StickyTDPInterval = (int)(StickyTDPIntervalSlider?.Value ?? 5);
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

            try
            {
                var profile = GetProfile(profileName);

                // For Legion devices: check if we need to switch to Custom mode BEFORE sending any TDP-related settings
                // This prevents TDP/TDPBoost/EPP from being ignored when helper is still in preset mode
                bool legionNeedsModeChange = false;
                bool legionSwitchingToCustom = false;
                if (legionGoDetected?.Value == true && defaultGameProfileEnabled?.Value != true && !isInitialSync)
                {
                    int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
                    int profileMode = profile.LegionPerformanceMode;
                    int modeIndex = Array.IndexOf(modeValues, profileMode);
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
                    TDPSlider.Value = profile.TDP;
                    // For Legion devices: TDP value will be sent AFTER TDP mode is applied (see Legion-specific handling below)
                    // This prevents TDP from being ignored when switching from preset mode to Custom mode
                    // For non-Legion devices: send TDP value immediately
                    if (legionGoDetected?.Value != true)
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
                        if (!legionSwitchingToCustom)
                        {
                            tdpBoostEnabled?.SetValue(profile.TDPBoostEnabled);
                        }
                        Logger.Info($"TDP Boost loaded from profile: {profile.TDPBoostEnabled} (deferred={legionSwitchingToCustom})");
                    }
                }
                if (SaveCPUBoost)
                {
                    CPUBoostToggle.IsOn = profile.CPUBoost;
                    // Send to helper explicitly
                    cpuBoost?.SetValue(profile.CPUBoost);
                }
                if (SaveCPUEPP)
                {
                    CPUEPPSlider.Value = profile.CPUEPP;
                    // Send to helper explicitly (cast to int for property type)
                    // For Legion devices switching to Custom mode: defer sending to helper until mode change is applied
                    if (!legionSwitchingToCustom)
                    {
                        cpuEPP?.SetValue((int)profile.CPUEPP);
                    }
                }
                if (SaveCPUState)
                {
                    SetCPUStateComboBoxValue(MaxCPUStateComboBox, profile.MaxCPUState);
                    SetCPUStateComboBoxValue(MinCPUStateComboBox, profile.MinCPUState);
                    // Send to helper explicitly
                    maxCPUState?.SetValue(profile.MaxCPUState);
                    minCPUState?.SetValue(profile.MinCPUState);
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
                        AutoTDPToggle.IsOn = profile.AutoTDPEnabled;
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
                            if (PowerSourceProfileToggle.IsOn)
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
                            int modeIndex = Array.IndexOf(modeValues, profileMode);
                            if (modeIndex >= 0 && (legionPerformanceMode.Value != profileMode || TDPModeComboBox.SelectedIndex != modeIndex))
                            {
                                // Update lastTDPModeIndex FIRST to prevent TDPModeComboBox_SelectionChanged
                                // from treating the profile load as a user-initiated change
                                lastTDPModeIndex = modeIndex;

                                if (LegionPerformanceModeComboBox.SelectedIndex != modeIndex)
                                    LegionPerformanceModeComboBox.SelectedIndex = modeIndex;
                                if (TDPModeComboBox.SelectedIndex != modeIndex)
                                    TDPModeComboBox.SelectedIndex = modeIndex;
                                legionPerformanceMode?.ForceSetValue(profileMode);
                                Logger.Info($"Applied game profile TDP Mode: {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName}");

                                // If switching to Custom mode (255), send deferred settings with delay to allow mode change to propagate
                                if (profileMode == 255)
                                {
                                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                                    {
                                        await Task.Delay(500); // Allow mode change to propagate to helper (increased for reliability)
                                        // Send deferred TDP-related settings that were skipped earlier
                                        if (SaveTDP && TDPBoostToggle != null)
                                        {
                                            tdpBoostEnabled?.ForceSetValue(profile.TDPBoostEnabled);
                                            Logger.Info($"Applied deferred TDP Boost after mode change: {profile.TDPBoostEnabled}");
                                        }
                                        if (SaveCPUEPP)
                                        {
                                            cpuEPP?.ForceSetValue((int)profile.CPUEPP);
                                            Logger.Info($"Applied deferred CPU EPP after mode change: {profile.CPUEPP}");
                                        }
                                        // Send TDP value last
                                        tdp?.ForceSetValue((int)profile.TDP);
                                        Logger.Info($"Applied game profile TDP value after mode change: {profile.TDP}W for {profileName}");
                                    });
                                }
                            }
                            else if (profileMode == 255)
                            {
                                // Already in Custom mode, send TDP value immediately
                                tdp?.ForceSetValue((int)profile.TDP);
                                Logger.Info($"Applied game profile TDP value (already in Custom mode): {profile.TDP}W for {profileName}");
                            }
                        }
                        else
                        {
                            // SaveTDP disabled: default to Custom mode to allow manual TDP adjustment
                            if (TDPModeComboBox.SelectedIndex != 3)
                            {
                                if (LegionPerformanceModeComboBox.SelectedIndex != 3)
                                    LegionPerformanceModeComboBox.SelectedIndex = 3;
                                lastTDPModeIndex = 3;
                                TDPModeComboBox.SelectedIndex = 3;
                                legionPerformanceMode?.SetValue(255);
                                Logger.Info($"SaveTDP disabled - using Custom Legion mode for game profile: {profileName}");
                            }
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
                        int modeIndex = Array.IndexOf(modeValues, profileMode);
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

                // Update profile display to show correct TDP mode in Profiles tab
                UpdateProfileDisplay();
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
            container.Values["OSPowerMode"] = profile.OSPowerMode;
            container.Values["LegionPerformanceMode"] = profile.LegionPerformanceMode;
            container.Values["TDPBoostEnabled"] = profile.TDPBoostEnabled;
            container.Values["HDREnabled"] = profile.HDREnabled;
            container.Values["Resolution"] = profile.Resolution;
            container.Values["StickyTDPEnabled"] = profile.StickyTDPEnabled;
            container.Values["StickyTDPInterval"] = profile.StickyTDPInterval;
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
                profile.OSPowerMode = container.Values.ContainsKey("OSPowerMode") ? (int)container.Values["OSPowerMode"] : 1;
                // Only load LegionPerformanceMode if it exists in storage - keep profile's existing value otherwise
                // This preserves the default (Balanced=2) for new profiles but doesn't override if storage key is missing
                if (container.Values.ContainsKey("LegionPerformanceMode"))
                {
                    profile.LegionPerformanceMode = (int)container.Values["LegionPerformanceMode"];
                }
                profile.TDPBoostEnabled = container.Values.ContainsKey("TDPBoostEnabled") ? (bool)container.Values["TDPBoostEnabled"] : false;
                profile.HDREnabled = container.Values.ContainsKey("HDREnabled") ? (bool)container.Values["HDREnabled"] : false;
                profile.Resolution = container.Values.ContainsKey("Resolution") ? (string)container.Values["Resolution"] : "";
                profile.StickyTDPEnabled = container.Values.ContainsKey("StickyTDPEnabled") ? (bool)container.Values["StickyTDPEnabled"] : true;
                profile.StickyTDPInterval = container.Values.ContainsKey("StickyTDPInterval") ? (int)container.Values["StickyTDPInterval"] : 5;

                Logger.Info($"Loaded {profileName} profile from storage");
            }
        }

        #region Controller Profile Storage

        private void SaveControllerProfileToStorage(string profileName, ControllerProfile profile)
        {
            // Never save to "No game detected" profile
            if (profileName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Warn($"Attempted to save controller profile with invalid name: {profileName}, skipping");
                return;
            }

            var settings = ApplicationData.Current.LocalSettings;
            var container = settings.CreateContainer($"ControllerProfile_{profileName}", ApplicationDataCreateDisposition.Always);

            // Button mappings (serialized as JSON)
            var y1Json = profile.ButtonY1.ToJson();
            var y2Json = profile.ButtonY2.ToJson();
            var desktopJson = profile.ButtonDesktop.ToJson();
            container.Values["ButtonY1"] = y1Json;
            container.Values["ButtonY2"] = y2Json;
            container.Values["ButtonY3"] = profile.ButtonY3.ToJson();
            container.Values["ButtonM1"] = profile.ButtonM1.ToJson();
            container.Values["ButtonM2"] = profile.ButtonM2.ToJson();
            container.Values["ButtonM3"] = profile.ButtonM3.ToJson();
            container.Values["ButtonDesktop"] = desktopJson;
            container.Values["ButtonPage"] = profile.ButtonPage.ToJson();
            Logger.Info($"SaveControllerProfile: {profileName} ButtonY1={y1Json}, ButtonY2={y2Json}, ButtonDesktop={desktopJson}");
            container.Values["NintendoLayout"] = profile.NintendoLayout;
            container.Values["VibrationLevel"] = profile.VibrationLevel;
            container.Values["VibrationMode"] = profile.VibrationMode;

            // Gyro settings
            container.Values["GyroTarget"] = profile.GyroTarget;
            container.Values["GyroSensitivityX"] = profile.GyroSensitivityX;
            container.Values["GyroSensitivityY"] = profile.GyroSensitivityY;
            container.Values["GyroInvertX"] = profile.GyroInvertX;
            container.Values["GyroInvertY"] = profile.GyroInvertY;
            container.Values["GyroMappingType"] = profile.GyroMappingType;
            container.Values["GyroActivationMode"] = profile.GyroActivationMode;
            container.Values["GyroActivationButton"] = profile.GyroActivationButton;

            // Advanced gyro settings
            container.Values["GyroDeadzone"] = profile.GyroDeadzone;

            // Stick deadzones
            container.Values["LeftStickDeadzone"] = profile.LeftStickDeadzone;
            container.Values["RightStickDeadzone"] = profile.RightStickDeadzone;

            // Trigger travel
            container.Values["LeftTriggerStart"] = profile.LeftTriggerStart;
            container.Values["LeftTriggerEnd"] = profile.LeftTriggerEnd;
            container.Values["RightTriggerStart"] = profile.RightTriggerStart;
            container.Values["RightTriggerEnd"] = profile.RightTriggerEnd;
            container.Values["HairTriggers"] = profile.HairTriggers;

            // Joystick as mouse
            container.Values["JoystickAsMouseMode"] = profile.JoystickAsMouseMode;
            container.Values["JoystickMouseSens"] = profile.JoystickMouseSens;

            // Gamepad button mappings (serialize dictionary as JSON)
            if (profile.GamepadButtonMappings != null && profile.GamepadButtonMappings.Count > 0)
            {
                var gamepadMappingsJson = SerializeGamepadButtonMappings(profile.GamepadButtonMappings);
                container.Values["GamepadButtonMappings"] = gamepadMappingsJson;
            }
            else
            {
                container.Values["GamepadButtonMappings"] = "";
            }

            // Desktop Controls preset
            container.Values["DesktopControlsEnabled"] = profile.DesktopControlsEnabled;

            // Lighting
            container.Values["LightMode"] = profile.LightMode;
            container.Values["LightColorR"] = profile.LightColorR;
            container.Values["LightColorG"] = profile.LightColorG;
            container.Values["LightColorB"] = profile.LightColorB;
            container.Values["LightSpeed"] = profile.LightSpeed;
            container.Values["LightBrightness"] = profile.LightBrightness;
            container.Values["PowerLight"] = profile.PowerLight;

            // Store the game exe path for game profiles (used for loading icons)
            if (profileName.StartsWith("Game_") && !string.IsNullOrEmpty(currentGameExePath))
            {
                container.Values["GameExePath"] = currentGameExePath;
            }

            Logger.Info($"Saved controller profile: {profileName}, LightMode={profile.LightMode}, Color=#{profile.LightColorR:X2}{profile.LightColorG:X2}{profile.LightColorB:X2}, Brightness={profile.LightBrightness}");
        }

        private ButtonMapping LoadButtonMapping(ApplicationDataContainer container, string key)
        {
            if (!container.Values.ContainsKey(key))
                return new ButtonMapping();

            var value = container.Values[key];

            // Handle backwards compatibility: old format stored int, new format stores JSON string
            if (value is int intValue)
            {
                // Old format: convert simple int to ButtonMapping with gamepad type
                return new ButtonMapping { Type = 0, GamepadAction = intValue };
            }
            else if (value is string jsonValue)
            {
                return ButtonMapping.FromJson(jsonValue);
            }

            return new ButtonMapping();
        }

        private void LoadControllerProfileFromStorage(string profileName, ControllerProfile profile)
        {
            var settings = ApplicationData.Current.LocalSettings;
            var containerKey = $"ControllerProfile_{profileName}";
            if (settings.Containers.ContainsKey(containerKey))
            {
                var container = settings.Containers[containerKey];

                // Log what's in the container for debugging
                var y1Raw = container.Values.ContainsKey("ButtonY1") ? container.Values["ButtonY1"]?.ToString() : "(not found)";
                var y2Raw = container.Values.ContainsKey("ButtonY2") ? container.Values["ButtonY2"]?.ToString() : "(not found)";
                var desktopRaw = container.Values.ContainsKey("ButtonDesktop") ? container.Values["ButtonDesktop"]?.ToString() : "(not found)";
                Logger.Info($"LoadControllerProfile: {profileName} raw values: ButtonY1={y1Raw}, ButtonY2={y2Raw}, ButtonDesktop={desktopRaw}");

                // Button mappings (with backwards compatibility for old int format)
                profile.ButtonY1 = LoadButtonMapping(container, "ButtonY1");
                profile.ButtonY2 = LoadButtonMapping(container, "ButtonY2");
                profile.ButtonY3 = LoadButtonMapping(container, "ButtonY3");
                profile.ButtonM1 = LoadButtonMapping(container, "ButtonM1");
                profile.ButtonM2 = LoadButtonMapping(container, "ButtonM2");
                profile.ButtonM3 = LoadButtonMapping(container, "ButtonM3");
                profile.ButtonDesktop = LoadButtonMapping(container, "ButtonDesktop");
                profile.ButtonPage = LoadButtonMapping(container, "ButtonPage");

                Logger.Info($"LoadControllerProfile: {profileName} parsed: Y1={FormatButtonMapping(profile.ButtonY1)}, Y2={FormatButtonMapping(profile.ButtonY2)}, Desktop={FormatButtonMapping(profile.ButtonDesktop)}");
                profile.NintendoLayout = container.Values.ContainsKey("NintendoLayout") ? (bool)container.Values["NintendoLayout"] : false;
                profile.VibrationLevel = container.Values.ContainsKey("VibrationLevel") ? (int)container.Values["VibrationLevel"] : 2;
                profile.VibrationMode = container.Values.ContainsKey("VibrationMode") ? (int)container.Values["VibrationMode"] : 1;

                // Gyro settings
                profile.GyroTarget = container.Values.ContainsKey("GyroTarget") ? (int)container.Values["GyroTarget"] : 0;
                profile.GyroSensitivityX = container.Values.ContainsKey("GyroSensitivityX") ? (int)container.Values["GyroSensitivityX"] : 50;
                profile.GyroSensitivityY = container.Values.ContainsKey("GyroSensitivityY") ? (int)container.Values["GyroSensitivityY"] : 50;
                profile.GyroInvertX = container.Values.ContainsKey("GyroInvertX") ? (bool)container.Values["GyroInvertX"] : false;
                profile.GyroInvertY = container.Values.ContainsKey("GyroInvertY") ? (bool)container.Values["GyroInvertY"] : false;
                profile.GyroMappingType = container.Values.ContainsKey("GyroMappingType") ? (int)container.Values["GyroMappingType"] : 0;
                profile.GyroActivationMode = container.Values.ContainsKey("GyroActivationMode") ? (int)container.Values["GyroActivationMode"] : 0;
                profile.GyroActivationButton = container.Values.ContainsKey("GyroActivationButton") ? (int)container.Values["GyroActivationButton"] : 0;

                // Advanced gyro settings
                profile.GyroDeadzone = container.Values.ContainsKey("GyroDeadzone") ? (int)container.Values["GyroDeadzone"] : 10;

                // Stick deadzones
                profile.LeftStickDeadzone = container.Values.ContainsKey("LeftStickDeadzone") ? (int)container.Values["LeftStickDeadzone"] : 4;
                profile.RightStickDeadzone = container.Values.ContainsKey("RightStickDeadzone") ? (int)container.Values["RightStickDeadzone"] : 4;

                // Trigger travel
                profile.LeftTriggerStart = container.Values.ContainsKey("LeftTriggerStart") ? (int)container.Values["LeftTriggerStart"] : 0;
                profile.LeftTriggerEnd = container.Values.ContainsKey("LeftTriggerEnd") ? (int)container.Values["LeftTriggerEnd"] : 0;
                profile.RightTriggerStart = container.Values.ContainsKey("RightTriggerStart") ? (int)container.Values["RightTriggerStart"] : 0;
                profile.RightTriggerEnd = container.Values.ContainsKey("RightTriggerEnd") ? (int)container.Values["RightTriggerEnd"] : 0;
                profile.HairTriggers = container.Values.ContainsKey("HairTriggers") ? (bool)container.Values["HairTriggers"] : false;

                // Joystick as mouse
                profile.JoystickAsMouseMode = container.Values.ContainsKey("JoystickAsMouseMode") ? (int)container.Values["JoystickAsMouseMode"] : 0;
                profile.JoystickMouseSens = container.Values.ContainsKey("JoystickMouseSens") ? (int)container.Values["JoystickMouseSens"] : 50;

                // Gamepad button mappings (deserialize from JSON)
                profile.GamepadButtonMappings = new Dictionary<string, ButtonMapping>();
                if (container.Values.ContainsKey("GamepadButtonMappings"))
                {
                    var gamepadMappingsJson = container.Values["GamepadButtonMappings"] as string;
                    if (!string.IsNullOrEmpty(gamepadMappingsJson))
                    {
                        try
                        {
                            profile.GamepadButtonMappings = DeserializeGamepadButtonMappings(gamepadMappingsJson);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error loading gamepad button mappings: {ex.Message}");
                        }
                    }
                }

                // Desktop Controls preset
                profile.DesktopControlsEnabled = container.Values.ContainsKey("DesktopControlsEnabled")
                    ? (bool)container.Values["DesktopControlsEnabled"]
                    : false;

                // Lighting - only load if explicitly saved (to avoid defaulting to white for old profiles)
                profile.HasExplicitLighting = container.Values.ContainsKey("LightColorR");
                profile.LightMode = container.Values.ContainsKey("LightMode") ? (int)container.Values["LightMode"] : 1;
                if (profile.HasExplicitLighting)
                {
                    profile.LightColorR = (byte)container.Values["LightColorR"];
                    profile.LightColorG = container.Values.ContainsKey("LightColorG") ? (byte)container.Values["LightColorG"] : (byte)255;
                    profile.LightColorB = container.Values.ContainsKey("LightColorB") ? (byte)container.Values["LightColorB"] : (byte)255;
                }
                profile.LightSpeed = container.Values.ContainsKey("LightSpeed") ? (int)container.Values["LightSpeed"] : 50;
                profile.LightBrightness = container.Values.ContainsKey("LightBrightness") ? (int)container.Values["LightBrightness"] : 50;
                profile.PowerLight = container.Values.ContainsKey("PowerLight") ? (bool)container.Values["PowerLight"] : true;

                Logger.Info($"Loaded controller profile: {profileName} (HasExplicitLighting={profile.HasExplicitLighting}, LightMode={profile.LightMode}, Color=#{profile.LightColorR:X2}{profile.LightColorG:X2}{profile.LightColorB:X2}, Brightness={profile.LightBrightness})");
            }
            else
            {
                Logger.Warn($"Controller profile container not found: {containerKey} - using defaults");
            }
        }

        private void InitializeButtonMappingEvents(string buttonName)
        {
            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;
            var keyCombo = FindName($"LegionButton{buttonName}KeyComboBox") as ComboBox;

            if (typeCombo != null)
            {
                typeCombo.SelectionChanged += (s, e) => OnButtonTypeChanged(buttonName);
            }
            if (gamepadCombo != null)
            {
                gamepadCombo.SelectionChanged += ControllerSettingChanged;
            }
            if (mouseCombo != null)
            {
                mouseCombo.SelectionChanged += ControllerSettingChanged;
            }
            if (keyCombo != null)
            {
                keyCombo.SelectionChanged += (s, e) => OnKeyboardKeySelected(buttonName);
            }
        }

        private void OnButtonTypeChanged(string buttonName)
        {
            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;
            var keyboardPanel = FindName($"LegionButton{buttonName}KeyboardPanel") as StackPanel;

            if (typeCombo == null) return;
            int type = typeCombo.SelectedIndex;

            // Show/hide appropriate controls
            if (gamepadCombo != null)
                gamepadCombo.Visibility = type == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (mouseCombo != null)
                mouseCombo.Visibility = type == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (keyboardPanel != null)
                keyboardPanel.Visibility = type == 1 ? Visibility.Visible : Visibility.Collapsed;

            // Update the profile and send command
            if (!isLoadingControllerProfile && !isSwitchingControllerProfile)
            {
                ControllerSettingChanged(typeCombo, null);
            }
        }

        private void OnKeyboardKeySelected(string buttonName)
        {
            var keyCombo = FindName($"LegionButton{buttonName}KeyComboBox") as ComboBox;
            if (keyCombo == null || keyCombo.SelectedIndex <= 0) return;  // 0 is "+ Key"

            // Get the key code from the dropdown index
            int keyCode = GetKeyCodeFromDropdownIndex(keyCombo.SelectedIndex);
            if (keyCode == 0) return;

            // Get current keys and add the new one (max 5)
            var keys = GetStoredKeyboardKeys(buttonName);
            if (keys.Count >= 5)
            {
                keyCombo.SelectedIndex = 0;
                return;  // Max 5 keys
            }

            if (!keys.Contains(keyCode))
            {
                keys.Add(keyCode);
                SetStoredKeyboardKeys(buttonName, keys);
                UpdateKeyboardKeyTags(buttonName, keys);

                // Trigger profile save and command send
                if (!isLoadingControllerProfile && !isSwitchingControllerProfile)
                {
                    ControllerSettingChanged(keyCombo, null);
                }
            }

            // Reset dropdown
            keyCombo.SelectedIndex = 0;
        }

        private int GetKeyCodeFromDropdownIndex(int index)
        {
            // Map dropdown index to HID key code
            // Index 0 is "+ Key" placeholder
            // Index 1-26 are A-Z (0x04-0x1D)
            // Index 27-36 are 1-0 (0x1E-0x27)
            // Index 37-48 are F1-F12 (0x3A-0x45)
            // Index 49-53 are Enter, Esc, Space, Tab, Backspace (0x28-0x2C)
            // Index 54-57 are Up, Down, Left, Right (0x52, 0x51, 0x50, 0x4F)
            // Index 58-65 are modifier keys (0xE0-0xE7)

            if (index <= 0) return 0;
            if (index <= 26) return 0x04 + (index - 1);   // A-Z: indices 1-26 → 0x04-0x1D
            if (index <= 36) return 0x1E + (index - 27);  // 1-0: indices 27-36 → 0x1E-0x27
            if (index <= 48) return 0x3A + (index - 37);  // F1-F12: indices 37-48 → 0x3A-0x45
            if (index == 49) return 0x28;  // Enter
            if (index == 50) return 0x29;  // Esc
            if (index == 51) return 0x2C;  // Space
            if (index == 52) return 0x2B;  // Tab
            if (index == 53) return 0x2A;  // Backspace
            if (index == 54) return 0x52;  // Up
            if (index == 55) return 0x51;  // Down
            if (index == 56) return 0x50;  // Left
            if (index == 57) return 0x4F;  // Right
            // Modifier keys
            if (index == 58) return 0xE0;  // LCtrl
            if (index == 59) return 0xE1;  // LShift
            if (index == 60) return 0xE2;  // LAlt
            if (index == 61) return 0xE3;  // LMeta
            if (index == 62) return 0xE4;  // RCtrl
            if (index == 63) return 0xE5;  // RShift
            if (index == 64) return 0xE6;  // RAlt
            if (index == 65) return 0xE7;  // RMeta

            return 0;
        }

        private void RemoveKeyFromButton(string buttonName, int keyCode)
        {
            var keys = GetStoredKeyboardKeys(buttonName);
            keys.Remove(keyCode);
            SetStoredKeyboardKeys(buttonName, keys);
            UpdateKeyboardKeyTags(buttonName, keys);

            // Trigger profile save and command send
            if (!isLoadingControllerProfile && !isSwitchingControllerProfile)
            {
                ControllerSettingChanged(null, null);
            }
        }

        private string FormatButtonMapping(ButtonMapping mapping)
        {
            if (mapping == null) return "none";
            switch (mapping.Type)
            {
                case 0: return $"GP:{mapping.GamepadAction}";
                case 1: return $"KB:[{string.Join(",", mapping.KeyboardKeys)}]";
                case 2: return $"MS:{mapping.MouseButton}";
                default: return "?";
            }
        }

        private void ApplyButtonMappingToUI(string buttonName, ButtonMapping mapping)
        {
            // Find the controls by name using reflection-like approach
            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;
            var keyboardPanel = FindName($"LegionButton{buttonName}KeyboardPanel") as StackPanel;

            if (mapping == null) mapping = new ButtonMapping();

            // Set type dropdown
            if (typeCombo != null)
                typeCombo.SelectedIndex = mapping.Type;

            // Show/hide appropriate controls
            if (gamepadCombo != null)
            {
                gamepadCombo.Visibility = mapping.Type == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (mapping.Type == 0)
                    gamepadCombo.SelectedIndex = mapping.GamepadAction;
            }
            if (mouseCombo != null)
            {
                mouseCombo.Visibility = mapping.Type == 2 ? Visibility.Visible : Visibility.Collapsed;
                if (mapping.Type == 2)
                    mouseCombo.SelectedIndex = mapping.MouseButton;
            }
            if (keyboardPanel != null)
            {
                keyboardPanel.Visibility = mapping.Type == 1 ? Visibility.Visible : Visibility.Collapsed;
                if (mapping.Type == 1)
                    UpdateKeyboardKeyTags(buttonName, mapping.KeyboardKeys);
            }
        }

        private void UpdateKeyboardKeyTags(string buttonName, List<int> keys)
        {
            var keyTags = FindName($"LegionButton{buttonName}KeyTags") as StackPanel;
            if (keyTags == null) return;

            keyTags.Children.Clear();
            if (keys == null) return;

            foreach (var key in keys)
            {
                // Create a tag with the key name and X button to remove
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var keyText = new TextBlock
                {
                    Text = GetKeyDisplayName(key),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };

                // Capture the key code for the click handler
                int keyCode = key;
                string btnName = buttonName;
                removeButton.Click += (s, e) => RemoveKeyFromButton(btnName, keyCode);

                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                keyTags.Children.Add(tagBorder);
            }
        }

        private string GetKeyDisplayName(int keyCode)
        {
            // Map key codes to display names
            var keyNames = new Dictionary<int, string>
            {
                { 0x04, "A" }, { 0x05, "B" }, { 0x06, "C" }, { 0x07, "D" }, { 0x08, "E" },
                { 0x09, "F" }, { 0x0A, "G" }, { 0x0B, "H" }, { 0x0C, "I" }, { 0x0D, "J" },
                { 0x0E, "K" }, { 0x0F, "L" }, { 0x10, "M" }, { 0x11, "N" }, { 0x12, "O" },
                { 0x13, "P" }, { 0x14, "Q" }, { 0x15, "R" }, { 0x16, "S" }, { 0x17, "T" },
                { 0x18, "U" }, { 0x19, "V" }, { 0x1A, "W" }, { 0x1B, "X" }, { 0x1C, "Y" },
                { 0x1D, "Z" }, { 0x1E, "1" }, { 0x1F, "2" }, { 0x20, "3" }, { 0x21, "4" },
                { 0x22, "5" }, { 0x23, "6" }, { 0x24, "7" }, { 0x25, "8" }, { 0x26, "9" },
                { 0x27, "0" }, { 0x28, "Enter" }, { 0x29, "Esc" }, { 0x2A, "Backspace" },
                { 0x2B, "Tab" }, { 0x2C, "Space" }, { 0x2D, "-" }, { 0x2E, "=" },
                { 0x2F, "[" }, { 0x30, "]" }, { 0x31, "\\" }, { 0x33, ";" }, { 0x34, "'" },
                { 0x35, "`" }, { 0x36, "," }, { 0x37, "." }, { 0x38, "/" }, { 0x39, "CapsLock" },
                { 0x3A, "F1" }, { 0x3B, "F2" }, { 0x3C, "F3" }, { 0x3D, "F4" }, { 0x3E, "F5" },
                { 0x3F, "F6" }, { 0x40, "F7" }, { 0x41, "F8" }, { 0x42, "F9" }, { 0x43, "F10" },
                { 0x44, "F11" }, { 0x45, "F12" }, { 0x46, "PrtSc" }, { 0x47, "ScrLk" },
                { 0x48, "Pause" }, { 0x49, "Ins" }, { 0x4A, "Home" }, { 0x4B, "PgUp" },
                { 0x4C, "Del" }, { 0x4D, "End" }, { 0x4E, "PgDn" }, { 0x4F, "Right" },
                { 0x50, "Left" }, { 0x51, "Down" }, { 0x52, "Up" },
                // Modifier keys
                { 0xE0, "LCtrl" }, { 0xE1, "LShift" }, { 0xE2, "LAlt" }, { 0xE3, "LMeta" },
                { 0xE4, "RCtrl" }, { 0xE5, "RShift" }, { 0xE6, "RAlt" }, { 0xE7, "RMeta" }
            };
            return keyNames.TryGetValue(keyCode, out var name) ? name : $"0x{keyCode:X2}";
        }

        private ButtonMapping GetButtonMappingFromUI(string buttonName)
        {
            var mapping = new ButtonMapping();

            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;

            mapping.Type = typeCombo?.SelectedIndex ?? 0;
            mapping.GamepadAction = gamepadCombo?.SelectedIndex ?? 0;
            mapping.MouseButton = mouseCombo?.SelectedIndex ?? 0;

            // Get keyboard keys from the stored list (maintained separately)
            var keyList = GetStoredKeyboardKeys(buttonName);
            mapping.KeyboardKeys = keyList;

            return mapping;
        }

        private Dictionary<string, List<int>> _buttonKeyboardKeys = new Dictionary<string, List<int>>();

        private List<int> GetStoredKeyboardKeys(string buttonName)
        {
            if (_buttonKeyboardKeys.TryGetValue(buttonName, out var keys))
                return new List<int>(keys);
            return new List<int>();
        }

        private void SetStoredKeyboardKeys(string buttonName, List<int> keys)
        {
            _buttonKeyboardKeys[buttonName] = new List<int>(keys ?? new List<int>());
        }

        private void ApplyControllerProfile(ControllerProfile profile)
        {
            isLoadingControllerProfile = true;

            try
            {
                // Store keyboard keys before applying UI
                SetStoredKeyboardKeys("Y1", profile.ButtonY1?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("Y2", profile.ButtonY2?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("Y3", profile.ButtonY3?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("M1", profile.ButtonM1?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("M2", profile.ButtonM2?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("M3", profile.ButtonM3?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("Desktop", profile.ButtonDesktop?.KeyboardKeys ?? new List<int>());
                SetStoredKeyboardKeys("Page", profile.ButtonPage?.KeyboardKeys ?? new List<int>());

                // Apply button mappings (with full type support)
                ApplyButtonMappingToUI("Y1", profile.ButtonY1);
                ApplyButtonMappingToUI("Y2", profile.ButtonY2);
                ApplyButtonMappingToUI("Y3", profile.ButtonY3);
                ApplyButtonMappingToUI("M1", profile.ButtonM1);
                ApplyButtonMappingToUI("M2", profile.ButtonM2);
                ApplyButtonMappingToUI("M3", profile.ButtonM3);
                ApplyButtonMappingToUI("Desktop", profile.ButtonDesktop);
                ApplyButtonMappingToUI("Page", profile.ButtonPage);

                // Apply Nintendo layout (with event unsubscription to prevent handler firing)
                if (LegionNintendoLayoutToggle != null)
                {
                    LegionNintendoLayoutToggle.Toggled -= LegionNintendoLayout_Toggled;
                    try
                    {
                        LegionNintendoLayoutToggle.IsOn = profile.NintendoLayout;
                        // Apply or clear Nintendo layout mappings to match toggle state
                        if (profile.NintendoLayout)
                        {
                            ApplyNintendoLayoutMappings();
                        }
                        else
                        {
                            ClearNintendoLayoutMappings();
                        }
                    }
                    finally
                    {
                        LegionNintendoLayoutToggle.Toggled += LegionNintendoLayout_Toggled;
                    }
                }

                // Apply vibration settings
                if (LegionVibrationComboBox != null)
                    LegionVibrationComboBox.SelectedIndex = profile.VibrationLevel;
                if (LegionVibrationModeComboBox != null)
                    LegionVibrationModeComboBox.SelectedIndex = profile.VibrationMode - 1; // Mode is 1-based, index is 0-based

                // Apply gyro settings
                if (LegionGyroTargetComboBox != null)
                    LegionGyroTargetComboBox.SelectedIndex = profile.GyroTarget;
                if (LegionGyroSensitivityXSlider != null)
                {
                    LegionGyroSensitivityXSlider.Value = profile.GyroSensitivityX;
                    if (LegionGyroSensitivityXValue != null)
                        LegionGyroSensitivityXValue.Text = profile.GyroSensitivityX.ToString();
                }
                if (LegionGyroSensitivityYSlider != null)
                {
                    LegionGyroSensitivityYSlider.Value = profile.GyroSensitivityY;
                    if (LegionGyroSensitivityYValue != null)
                        LegionGyroSensitivityYValue.Text = profile.GyroSensitivityY.ToString();
                }
                if (LegionGyroInvertXToggle != null)
                    LegionGyroInvertXToggle.IsOn = profile.GyroInvertX;
                if (LegionGyroInvertYToggle != null)
                    LegionGyroInvertYToggle.IsOn = profile.GyroInvertY;
                if (LegionGyroMappingTypeComboBox != null)
                    LegionGyroMappingTypeComboBox.SelectedIndex = profile.GyroMappingType;
                if (LegionGyroActivationModeComboBox != null)
                    LegionGyroActivationModeComboBox.SelectedIndex = profile.GyroActivationMode;
                if (LegionGyroActivationButtonComboBox != null)
                    LegionGyroActivationButtonComboBox.SelectedIndex = profile.GyroActivationButton;

                // Apply advanced gyro settings
                if (LegionGyroDeadzoneSlider != null)
                {
                    LegionGyroDeadzoneSlider.Value = profile.GyroDeadzone;
                    if (LegionGyroDeadzoneValue != null)
                        LegionGyroDeadzoneValue.Text = profile.GyroDeadzone.ToString();
                }

                // Apply stick deadzones
                if (LegionLeftStickDeadzoneSlider != null)
                {
                    LegionLeftStickDeadzoneSlider.Value = profile.LeftStickDeadzone;
                    if (LegionLeftStickDeadzoneValue != null)
                        LegionLeftStickDeadzoneValue.Text = $"{profile.LeftStickDeadzone}%";
                }
                if (LegionRightStickDeadzoneSlider != null)
                {
                    LegionRightStickDeadzoneSlider.Value = profile.RightStickDeadzone;
                    if (LegionRightStickDeadzoneValue != null)
                        LegionRightStickDeadzoneValue.Text = $"{profile.RightStickDeadzone}%";
                }

                // Apply trigger travel settings
                if (LegionHairTriggersToggle != null)
                {
                    LegionHairTriggersToggle.Toggled -= LegionHairTriggers_Toggled;
                    try
                    {
                        LegionHairTriggersToggle.IsOn = profile.HairTriggers;
                        UpdateTriggerSlidersEnabled(!profile.HairTriggers);
                    }
                    finally
                    {
                        LegionHairTriggersToggle.Toggled += LegionHairTriggers_Toggled;
                    }
                }
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = profile.LeftTriggerStart;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = $"{profile.LeftTriggerStart}%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = profile.LeftTriggerEnd;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = $"{profile.LeftTriggerEnd}%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = profile.RightTriggerStart;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = $"{profile.RightTriggerStart}%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = profile.RightTriggerEnd;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = $"{profile.RightTriggerEnd}%";
                }

                // Apply joystick as mouse settings
                if (LegionJoystickAsMouseComboBox != null)
                {
                    // Set UI first
                    if (LegionJoystickAsMouseComboBox.Items.Count > profile.JoystickAsMouseMode)
                    {
                        LegionJoystickAsMouseComboBox.SelectedIndex = profile.JoystickAsMouseMode;
                    }
                    // Show/hide sensitivity grid based on mode
                    if (LegionJoystickMouseSensGrid != null)
                        LegionJoystickMouseSensGrid.Visibility = profile.JoystickAsMouseMode > 0
                            ? Windows.UI.Xaml.Visibility.Visible
                            : Windows.UI.Xaml.Visibility.Collapsed;
                    // Send value to helper (SetValue instead of SetValueSilent)
                    legionJoystickAsMouseMode?.SetValue(profile.JoystickAsMouseMode);
                }
                if (LegionJoystickMouseSensSlider != null)
                {
                    LegionJoystickMouseSensSlider.Value = profile.JoystickMouseSens;
                    if (LegionJoystickMouseSensValue != null)
                        LegionJoystickMouseSensValue.Text = profile.JoystickMouseSens.ToString();
                }

                // Apply gamepad button mappings
                gamepadButtonMappings = profile.GamepadButtonMappings?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Clone()) ?? new Dictionary<string, ButtonMapping>();

                // Update UI to show current selected button's mapping
                if (LegionGamepadButtonSelectorComboBox != null && LegionGamepadButtonSelectorComboBox.SelectedIndex >= 0)
                {
                    LoadGamepadMappingToUI(GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex));
                }

                // Update the remapped buttons summary display
                UpdateGamepadMappingSummary();

                // Apply Desktop Controls toggle state (with event unsubscription to prevent handler firing)
                if (LegionDesktopControlsToggle != null)
                {
                    LegionDesktopControlsToggle.Toggled -= LegionDesktopControls_Toggled;
                    try
                    {
                        LegionDesktopControlsToggle.IsOn = profile.DesktopControlsEnabled;
                        // Apply/clear Desktop Controls mappings
                        if (profile.DesktopControlsEnabled)
                        {
                            // Override Joystick as Mouse to Right Stick for Desktop Controls preset
                            if (LegionJoystickAsMouseComboBox != null)
                                LegionJoystickAsMouseComboBox.SelectedIndex = 2; // Right Stick
                            if (LegionJoystickMouseSensGrid != null)
                                LegionJoystickMouseSensGrid.Visibility = Windows.UI.Xaml.Visibility.Visible;
                            // Send joystick as mouse mode to helper
                            legionJoystickAsMouseMode?.SetValue(2);
                            // Apply the desktop control button mappings to the controller
                            ApplyDesktopControlMappings();
                        }
                        else
                        {
                            // Clear the desktop control button mappings from the controller
                            // Note: JoystickAsMouseMode is preserved from profile (already applied above)
                            ClearDesktopControlMappings();
                        }
                    }
                    finally
                    {
                        LegionDesktopControlsToggle.Toggled += LegionDesktopControls_Toggled;
                    }
                }

                // Apply lighting settings
                if (LegionLightModeComboBox != null)
                {
                    LegionLightModeComboBox.SelectionChanged -= LegionLightModeComboBox_SelectionChanged;
                    try
                    {
                        LegionLightModeComboBox.SelectedIndex = profile.LightMode;
                    }
                    finally
                    {
                        LegionLightModeComboBox.SelectionChanged += LegionLightModeComboBox_SelectionChanged;
                    }
                }
                if (LegionColorPicker != null)
                {
                    LegionColorPicker.ColorChanged -= LegionColorPicker_ColorChanged;
                    try
                    {
                        LegionColorPicker.Color = Windows.UI.Color.FromArgb(255, profile.LightColorR, profile.LightColorG, profile.LightColorB);
                        if (LegionColorPreview != null)
                        {
                            LegionColorPreview.Background = new SolidColorBrush(LegionColorPicker.Color);
                        }
                    }
                    finally
                    {
                        LegionColorPicker.ColorChanged += LegionColorPicker_ColorChanged;
                    }
                }
                if (LegionSpeedSlider != null)
                {
                    LegionSpeedSlider.Value = profile.LightSpeed;
                }
                if (LegionBrightnessSlider != null)
                {
                    LegionBrightnessSlider.Value = profile.LightBrightness;
                    if (LegionBrightnessValue != null)
                        LegionBrightnessValue.Text = $"{profile.LightBrightness}%";
                }
                if (LegionPowerLightToggle != null)
                {
                    LegionPowerLightToggle.IsOn = profile.PowerLight;
                }

                Logger.Info($"Applied controller profile: Y1={FormatButtonMapping(profile.ButtonY1)}, Y2={FormatButtonMapping(profile.ButtonY2)}, Y3={FormatButtonMapping(profile.ButtonY3)}, M1={FormatButtonMapping(profile.ButtonM1)}, M2={FormatButtonMapping(profile.ButtonM2)}, M3={FormatButtonMapping(profile.ButtonM3)}, Nintendo={profile.NintendoLayout}, Vib={profile.VibrationLevel}, VibMode={profile.VibrationMode}, GyroTarget={profile.GyroTarget}, LDZ={profile.LeftStickDeadzone}, RDZ={profile.RightStickDeadzone}, GamepadMappings={profile.GamepadButtonMappings?.Count ?? 0}, DesktopControls={profile.DesktopControlsEnabled}, LightMode={profile.LightMode}");

                // Set timestamp BEFORE sending to prevent any queued events from causing duplicate sends
                // Use 2 second window since HID commands take ~1.5s to complete (50ms per button × ~30 buttons)
                lastProfileApplyTime = DateTime.Now;

                // Send button mappings to helper
                SendButtonMappingsToHelper(profile);

                // Send controller settings to helper (gyro, deadzone, vibration, triggers)
                SendControllerSettingsToHelper(profile);

                // Send lighting settings to helper
                SendLightingToHelper(profile);
            }
            finally
            {
                isLoadingControllerProfile = false;
            }
        }

        private ControllerProfile GetCurrentControllerProfileFromUI()
        {
            return new ControllerProfile
            {
                ButtonY1 = GetButtonMappingFromUI("Y1"),
                ButtonY2 = GetButtonMappingFromUI("Y2"),
                ButtonY3 = GetButtonMappingFromUI("Y3"),
                ButtonM1 = GetButtonMappingFromUI("M1"),
                ButtonM2 = GetButtonMappingFromUI("M2"),
                ButtonM3 = GetButtonMappingFromUI("M3"),
                ButtonDesktop = GetButtonMappingFromUI("Desktop"),
                ButtonPage = GetButtonMappingFromUI("Page"),
                NintendoLayout = LegionNintendoLayoutToggle?.IsOn ?? false,
                VibrationLevel = LegionVibrationComboBox?.SelectedIndex ?? 2,
                VibrationMode = (LegionVibrationModeComboBox?.SelectedIndex ?? 0) + 1, // Index is 0-based, mode is 1-based
                // Gyro settings
                GyroTarget = LegionGyroTargetComboBox?.SelectedIndex ?? 0,
                GyroSensitivityX = (int)(LegionGyroSensitivityXSlider?.Value ?? 50),
                GyroSensitivityY = (int)(LegionGyroSensitivityYSlider?.Value ?? 50),
                GyroInvertX = LegionGyroInvertXToggle?.IsOn ?? false,
                GyroInvertY = LegionGyroInvertYToggle?.IsOn ?? false,
                GyroMappingType = LegionGyroMappingTypeComboBox?.SelectedIndex ?? 0,
                GyroActivationMode = LegionGyroActivationModeComboBox?.SelectedIndex ?? 0,
                GyroActivationButton = LegionGyroActivationButtonComboBox?.SelectedIndex ?? 0,
                // Advanced gyro settings
                GyroDeadzone = (int)(LegionGyroDeadzoneSlider?.Value ?? 10),
                // Stick deadzones
                LeftStickDeadzone = (int)(LegionLeftStickDeadzoneSlider?.Value ?? 4),
                RightStickDeadzone = (int)(LegionRightStickDeadzoneSlider?.Value ?? 4),
                // Trigger travel
                LeftTriggerStart = (int)(LegionLeftTriggerStartSlider?.Value ?? 0),
                LeftTriggerEnd = (int)(LegionLeftTriggerEndSlider?.Value ?? 0),
                RightTriggerStart = (int)(LegionRightTriggerStartSlider?.Value ?? 0),
                RightTriggerEnd = (int)(LegionRightTriggerEndSlider?.Value ?? 0),
                HairTriggers = LegionHairTriggersToggle?.IsOn ?? false,
                // Joystick as mouse
                JoystickAsMouseMode = LegionJoystickAsMouseComboBox?.SelectedIndex ?? 0,
                JoystickMouseSens = (int)(LegionJoystickMouseSensSlider?.Value ?? 50),
                // Gamepad button mappings
                GamepadButtonMappings = gamepadButtonMappings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Clone()),
                // Desktop Controls preset
                DesktopControlsEnabled = LegionDesktopControlsToggle?.IsOn ?? false,
                // Lighting
                LightMode = LegionLightModeComboBox?.SelectedIndex ?? 1,
                LightColorR = LegionColorPicker?.Color.R ?? 255,
                LightColorG = LegionColorPicker?.Color.G ?? 255,
                LightColorB = LegionColorPicker?.Color.B ?? 255,
                LightSpeed = (int)(LegionSpeedSlider?.Value ?? 50),
                LightBrightness = (int)(LegionBrightnessSlider?.Value ?? 50),
                PowerLight = LegionPowerLightToggle?.IsOn ?? true,
                HasExplicitLighting = true  // Mark as having explicit lighting since we're capturing from UI
            };
        }

        private void LegionControllerProfileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Protect entire toggle change sequence
            isSwitchingControllerProfile = true;

            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                if (LegionControllerProfileToggle.IsOn)
                {
                    // Per-game controller profiles enabled - only proceed if we have a valid game
                    if (HasValidGame(currentGameName))
                    {
                        // Clear the disabled preference since user is enabling it
                        string disabledKey = $"ControllerProfileDisabled_{currentGameName}";
                        if (settings.Values.ContainsKey(disabledKey))
                        {
                            settings.Values.Remove(disabledKey);
                            Logger.Info($"Cleared controller profile disabled preference for {currentGameName}");
                        }

                        // Load or create game controller profile
                        string profileKey = $"ControllerProfile_Game_{currentGameName}";
                        if (!settings.Containers.ContainsKey(profileKey))
                        {
                            // Initialize new game controller profile from current UI state (global)
                            gameControllerProfile = GetCurrentControllerProfileFromUI();
                            SaveControllerProfileToStorage($"Game_{currentGameName}", gameControllerProfile);
                            Logger.Info($"Initialized game controller profile for {currentGameName} from current settings");

                            // Refresh saved profiles list if expanded
                            if (isSavedProfilesExpanded)
                            {
                                RefreshSavedProfilesList();
                            }
                        }
                        else
                        {
                            // Load existing game controller profile
                            LoadControllerProfileFromStorage($"Game_{currentGameName}", gameControllerProfile);
                            ApplyControllerProfile(gameControllerProfile);
                            Logger.Info($"Loaded existing controller profile for {currentGameName}");
                        }
                    }
                    else
                    {
                        // No valid game, turn toggle back off
                        Logger.Warn($"Cannot enable per-game controller profile without a valid game, forcing toggle OFF");
                        LegionControllerProfileToggle.IsOn = false;
                        return;
                    }
                }
                else
                {
                    // Toggle is being turned OFF
                    if (HasValidGame(currentGameName))
                    {
                        // Save user's preference to disable per-game controller profile for this game
                        string disabledKey = $"ControllerProfileDisabled_{currentGameName}";
                        settings.Values[disabledKey] = true;
                        Logger.Info($"Saved controller profile disabled preference for {currentGameName}");
                    }

                    // Switch back to global controller profile
                    LoadControllerProfileFromStorage("Global", globalControllerProfile);
                    ApplyControllerProfile(globalControllerProfile);
                    Logger.Info("Switched to global controller profile");
                }
            }
            finally
            {
                isSwitchingControllerProfile = false;
            }
        }

        private void ControllerSettingChanged(object sender, object e)
        {
            // Update slider value displays
            UpdateControllerSliderDisplays(sender);

            // Don't save during profile loading, switching, widget unloading, or helper sync
            if (isLoadingControllerProfile || isSwitchingControllerProfile || isUnloading || isApplyingHelperUpdate)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
                return;

            // Get current profile from UI
            ControllerProfile profile;
            if (LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName))
            {
                // Save to game controller profile
                gameControllerProfile = GetCurrentControllerProfileFromUI();
                SaveControllerProfileToStorage($"Game_{currentGameName}", gameControllerProfile);
                profile = gameControllerProfile;
            }
            else
            {
                // Save to global controller profile
                globalControllerProfile = GetCurrentControllerProfileFromUI();
                SaveControllerProfileToStorage("Global", globalControllerProfile);
                profile = globalControllerProfile;
            }

            // Send button mappings to helper
            SendButtonMappingsToHelper(profile);

            // Send lighting settings to helper (so they get saved to helper's profile XML)
            SendLightingToHelper(profile);
        }

        /// <summary>
        /// Sends all button mappings to the helper via IPC.
        /// Only sends mappings that have actual values (not default/disabled).
        /// </summary>
        private void SendButtonMappingsToHelper(ControllerProfile profile)
        {
            try
            {
                // Only send button mappings that are not default (Type=0, GamepadAction=0)
                // Sending default mappings causes the helper to clear existing button mappings,
                // which is not desired when loading profiles that don't have explicit mappings set.
                if (profile.ButtonY1 != null && !profile.ButtonY1.IsDefault)
                    legionButtonY1?.SendMapping(profile.ButtonY1.ToJson());
                if (profile.ButtonY2 != null && !profile.ButtonY2.IsDefault)
                    legionButtonY2?.SendMapping(profile.ButtonY2.ToJson());
                if (profile.ButtonY3 != null && !profile.ButtonY3.IsDefault)
                    legionButtonY3?.SendMapping(profile.ButtonY3.ToJson());
                if (profile.ButtonM1 != null && !profile.ButtonM1.IsDefault)
                    legionButtonM1?.SendMapping(profile.ButtonM1.ToJson());
                if (profile.ButtonM2 != null && !profile.ButtonM2.IsDefault)
                    legionButtonM2?.SendMapping(profile.ButtonM2.ToJson());
                if (profile.ButtonM3 != null && !profile.ButtonM3.IsDefault)
                    legionButtonM3?.SendMapping(profile.ButtonM3.ToJson());
                if (profile.ButtonDesktop != null && !profile.ButtonDesktop.IsDefault)
                    legionButtonDesktop?.SendMapping(profile.ButtonDesktop.ToJson());
                if (profile.ButtonPage != null && !profile.ButtonPage.IsDefault)
                    legionButtonPage?.SendMapping(profile.ButtonPage.ToJson());

                // Send gamepad button mappings as JSON dictionary
                // During profile loading, use gamepadButtonMappings (includes desktop control changes)
                // Otherwise use profile.GamepadButtonMappings
                var mappingsToSend = isLoadingControllerProfile ? gamepadButtonMappings : profile.GamepadButtonMappings;
                if (mappingsToSend != null && mappingsToSend.Count > 0)
                {
                    var gamepadMappingsJson = SerializeGamepadButtonMappings(mappingsToSend);
                    legionGamepadMapping?.SetValue(gamepadMappingsJson);
                }
                else
                {
                    legionGamepadMapping?.SetValue("");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending button mappings: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends lighting settings to the helper via IPC
        /// </summary>
        private void SendLightingToHelper(ControllerProfile profile)
        {
            try
            {
                // Only send lighting if the profile has explicit lighting settings saved
                // This prevents old profiles (created before per-game lighting) from resetting to white
                if (!profile.HasExplicitLighting)
                {
                    Logger.Info($"Skipping lighting update - profile has no explicit lighting settings");
                    return;
                }

                // Send light mode
                legionLightMode?.SetValue(profile.LightMode);

                // Send light color as hex string (RRGGBB format)
                string colorHex = $"{profile.LightColorR:X2}{profile.LightColorG:X2}{profile.LightColorB:X2}";
                legionLightColor?.SetValue(colorHex);

                // Send light speed
                legionLightSpeed?.SetValue(profile.LightSpeed);

                // Send brightness
                legionLightBrightness?.SetValue(profile.LightBrightness);

                // Send power light
                legionPowerLight?.SetValue(profile.PowerLight);

                Logger.Info($"Sent lighting to helper: Mode={profile.LightMode}, Color=#{colorHex}, Speed={profile.LightSpeed}, Brightness={profile.LightBrightness}, PowerLight={profile.PowerLight}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending lighting settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends all controller settings (gyro, deadzone, vibration, triggers) to the helper via IPC.
        /// This ensures the helper has the full profile even when the widget is closed.
        /// </summary>
        private void SendControllerSettingsToHelper(ControllerProfile profile)
        {
            try
            {
                // Vibration settings
                legionVibration?.SetValue(profile.VibrationLevel);
                legionVibrationMode?.SetValue(profile.VibrationMode);

                // Gyro settings
                legionGyroTarget?.SetValue(profile.GyroTarget);
                legionGyroSensitivityX?.SetValue(profile.GyroSensitivityX);
                legionGyroSensitivityY?.SetValue(profile.GyroSensitivityY);
                legionGyroInvertX?.SetValue(profile.GyroInvertX);
                legionGyroInvertY?.SetValue(profile.GyroInvertY);
                legionGyroMappingType?.SetValue(profile.GyroMappingType);
                legionGyroActivationMode?.SetValue(profile.GyroActivationMode);
                legionGyroActivationButton?.SetValue(profile.GyroActivationButton);
                legionGyroDeadzone?.SetValue(profile.GyroDeadzone);

                // Stick deadzone settings
                legionLeftStickDeadzone?.SetValue(profile.LeftStickDeadzone);
                legionRightStickDeadzone?.SetValue(profile.RightStickDeadzone);

                // Trigger travel settings
                legionLeftTriggerStart?.SetValue(profile.LeftTriggerStart);
                legionLeftTriggerEnd?.SetValue(profile.LeftTriggerEnd);
                legionRightTriggerStart?.SetValue(profile.RightTriggerStart);
                legionRightTriggerEnd?.SetValue(profile.RightTriggerEnd);
                legionHairTriggers?.SetValue(profile.HairTriggers);

                Logger.Info($"Sent controller settings to helper: Vib={profile.VibrationLevel}, VibMode={profile.VibrationMode}, GyroTarget={profile.GyroTarget}, LDZ={profile.LeftStickDeadzone}, RDZ={profile.RightStickDeadzone}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending controller settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the display text for controller setting sliders
        /// </summary>
        private void UpdateControllerSliderDisplays(object sender)
        {
            try
            {
                // Gyro sensitivity sliders
                if (sender == LegionGyroSensitivityXSlider && LegionGyroSensitivityXValue != null)
                    LegionGyroSensitivityXValue.Text = ((int)LegionGyroSensitivityXSlider.Value).ToString();
                else if (sender == LegionGyroSensitivityYSlider && LegionGyroSensitivityYValue != null)
                    LegionGyroSensitivityYValue.Text = ((int)LegionGyroSensitivityYSlider.Value).ToString();
                // Advanced gyro sliders
                else if (sender == LegionGyroDeadzoneSlider && LegionGyroDeadzoneValue != null)
                    LegionGyroDeadzoneValue.Text = ((int)LegionGyroDeadzoneSlider.Value).ToString();
                // Stick deadzone sliders
                else if (sender == LegionLeftStickDeadzoneSlider && LegionLeftStickDeadzoneValue != null)
                    LegionLeftStickDeadzoneValue.Text = $"{(int)LegionLeftStickDeadzoneSlider.Value}%";
                else if (sender == LegionRightStickDeadzoneSlider && LegionRightStickDeadzoneValue != null)
                    LegionRightStickDeadzoneValue.Text = $"{(int)LegionRightStickDeadzoneSlider.Value}%";
                // Trigger travel sliders
                else if (sender == LegionLeftTriggerStartSlider && LegionLeftTriggerStartValue != null)
                    LegionLeftTriggerStartValue.Text = $"{(int)LegionLeftTriggerStartSlider.Value}%";
                else if (sender == LegionLeftTriggerEndSlider && LegionLeftTriggerEndValue != null)
                    LegionLeftTriggerEndValue.Text = $"{(int)LegionLeftTriggerEndSlider.Value}%";
                else if (sender == LegionRightTriggerStartSlider && LegionRightTriggerStartValue != null)
                    LegionRightTriggerStartValue.Text = $"{(int)LegionRightTriggerStartSlider.Value}%";
                else if (sender == LegionRightTriggerEndSlider && LegionRightTriggerEndValue != null)
                    LegionRightTriggerEndValue.Text = $"{(int)LegionRightTriggerEndSlider.Value}%";
                // Joystick as mouse sensitivity slider
                else if (sender == LegionJoystickMouseSensSlider && LegionJoystickMouseSensValue != null)
                    LegionJoystickMouseSensValue.Text = ((int)LegionJoystickMouseSensSlider.Value).ToString();
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error updating controller slider display: {ex.Message}");
            }
        }

        #region Gamepad Button Remapping

        // Map of button index to button name (matches dropdown order in XAML)
        private static readonly string[] GamepadButtonNames = new[]
        {
            "LSClick", "LSUp", "LSDown", "LSLeft", "LSRight",
            "RSClick", "RSUp", "RSDown", "RSLeft", "RSRight",
            "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
            "A", "B", "X", "Y",
            "LB", "LT", "RB", "RT",
            "Start", "Select"
        };

        /// <summary>
        /// Serializes gamepad button mappings dictionary to JSON string.
        /// Format: {"ButtonName":{Type:0,GamepadAction:5,...},...}
        /// </summary>
        private string SerializeGamepadButtonMappings(Dictionary<string, ButtonMapping> mappings)
        {
            if (mappings == null || mappings.Count == 0)
                return "{}";

            // Output nested JSON objects (not escaped strings)
            var entries = mappings.Select(kvp =>
                $"\"{kvp.Key}\":{kvp.Value.ToJson()}");
            return "{" + string.Join(",", entries) + "}";
        }

        /// <summary>
        /// Deserializes JSON string to gamepad button mappings dictionary.
        /// Format: {"ButtonName":{Type:0,...},...}
        /// </summary>
        private Dictionary<string, ButtonMapping> DeserializeGamepadButtonMappings(string json)
        {
            var result = new Dictionary<string, ButtonMapping>();
            if (string.IsNullOrEmpty(json) || json == "{}")
                return result;

            // Match patterns like "ButtonName":{...}
            var regex = new System.Text.RegularExpressions.Regex("\"(\\w+)\"\\s*:\\s*(\\{[^}]+\\})");
            var matches = regex.Matches(json);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    var buttonName = match.Groups[1].Value;
                    var mappingJson = match.Groups[2].Value;
                    result[buttonName] = ButtonMapping.FromJson(mappingJson);
                }
            }

            return result;
        }

        private string GetGamepadButtonNameFromIndex(int index)
        {
            if (index >= 0 && index < GamepadButtonNames.Length)
                return GamepadButtonNames[index];
            return "LSClick"; // Default
        }

        private void LoadGamepadMappingToUI(string buttonName)
        {
            if (LegionGamepadTypeComboBox == null || LegionGamepadActionComboBox == null)
                return;

            ButtonMapping mapping;
            if (gamepadButtonMappings.TryGetValue(buttonName, out mapping))
            {
                // Set type dropdown
                LegionGamepadTypeComboBox.SelectedIndex = mapping.Type;

                // Show appropriate action dropdown based on type
                UpdateGamepadMappingUI(mapping.Type);

                // Set action value
                if (mapping.Type == 0 && LegionGamepadActionComboBox != null)
                    LegionGamepadActionComboBox.SelectedIndex = mapping.GamepadAction;
                else if (mapping.Type == 2 && LegionGamepadMouseComboBox != null)
                    LegionGamepadMouseComboBox.SelectedIndex = mapping.MouseButton;
                else if (mapping.Type == 1)
                    UpdateGamepadKeyboardKeyTags(mapping.KeyboardKeys);
            }
            else
            {
                // No mapping exists - set to default (Gamepad, Disabled)
                LegionGamepadTypeComboBox.SelectedIndex = 0;
                UpdateGamepadMappingUI(0);
                if (LegionGamepadActionComboBox != null)
                    LegionGamepadActionComboBox.SelectedIndex = 0;
            }
        }

        private void UpdateGamepadMappingUI(int type)
        {
            // Type: 0=Gamepad, 1=Keyboard, 2=Mouse
            if (LegionGamepadActionComboBox != null)
                LegionGamepadActionComboBox.Visibility = type == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (LegionGamepadMouseComboBox != null)
                LegionGamepadMouseComboBox.Visibility = type == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (LegionGamepadKeyboardPanel != null)
                LegionGamepadKeyboardPanel.Visibility = type == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateGamepadKeyboardKeyTags(List<int> keys)
        {
            if (LegionGamepadKeyTags == null) return;

            LegionGamepadKeyTags.Children.Clear();
            if (keys == null) return;

            foreach (var key in keys)
            {
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var keyText = new TextBlock
                {
                    Text = GetKeyDisplayName(key),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };
                removeButton.Click += (s, e) => RemoveGamepadKeyboardKey(key);

                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                LegionGamepadKeyTags.Children.Add(tagBorder);
            }
        }

        private void RemoveGamepadKeyboardKey(int key)
        {
            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
            if (gamepadButtonMappings.TryGetValue(buttonName, out var mapping))
            {
                mapping.KeyboardKeys.Remove(key);
                UpdateGamepadKeyboardKeyTags(mapping.KeyboardKeys);
                SaveAndSendGamepadMappings();
            }
        }

        private void LegionGamepadButtonSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
            LoadGamepadMappingToUI(buttonName);
        }

        private void LegionGamepadMapping_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
                return;

            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
            var type = LegionGamepadTypeComboBox?.SelectedIndex ?? 0;

            // Update UI visibility if type changed
            if (sender == LegionGamepadTypeComboBox)
            {
                UpdateGamepadMappingUI(type);
            }

            // Build new mapping from UI
            var mapping = new ButtonMapping { Type = type };
            if (type == 0 && LegionGamepadActionComboBox != null)
                mapping.GamepadAction = LegionGamepadActionComboBox.SelectedIndex;
            else if (type == 2 && LegionGamepadMouseComboBox != null)
                mapping.MouseButton = LegionGamepadMouseComboBox.SelectedIndex;
            else if (type == 1)
            {
                // Keep existing keyboard keys if we have them
                if (gamepadButtonMappings.TryGetValue(buttonName, out var existingMapping))
                    mapping.KeyboardKeys = new List<int>(existingMapping.KeyboardKeys ?? new List<int>());
            }

            gamepadButtonMappings[buttonName] = mapping;
            SaveAndSendGamepadMappings();
        }

        private void LegionGamepadKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
                return;

            if (LegionGamepadKeyComboBox == null || LegionGamepadKeyComboBox.SelectedIndex <= 0)
                return; // Index 0 is "+ Key" placeholder

            if (LegionGamepadButtonSelectorComboBox == null || LegionGamepadButtonSelectorComboBox.SelectedIndex < 0)
                return;

            var buttonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);

            // Get or create mapping
            if (!gamepadButtonMappings.TryGetValue(buttonName, out var mapping))
            {
                mapping = new ButtonMapping { Type = 1, KeyboardKeys = new List<int>() };
                gamepadButtonMappings[buttonName] = mapping;
            }

            // Add the key (LegionGamepadKeyComboBox is 1-indexed since 0 is "+ Key")
            // The key code is based on the combo box item order
            var keyCode = GetKeyCodeFromDropdownIndex(LegionGamepadKeyComboBox.SelectedIndex);
            if (!mapping.KeyboardKeys.Contains(keyCode))
            {
                mapping.KeyboardKeys.Add(keyCode);
                UpdateGamepadKeyboardKeyTags(mapping.KeyboardKeys);
            }

            // Reset dropdown to "+ Key"
            LegionGamepadKeyComboBox.SelectedIndex = 0;

            SaveAndSendGamepadMappings();
        }

        private void SaveAndSendGamepadMappings()
        {
            // Don't save during profile loading - we're just applying the profile, not modifying it
            // The profile will be fully applied and any saves will happen after isLoadingControllerProfile is cleared
            if (isLoadingControllerProfile)
            {
                // Skip sending if this is a duplicate call during profile loading
                // (the main send will happen via SendButtonMappingsToHelper at the end of ApplyControllerProfile)
                return;
            }

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            // HID commands take ~1.5s to complete, so use 2 second window
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info("SaveAndSendGamepadMappings skipped - profile was just applied");
                return;
            }

            // Get current profile
            ControllerProfile currentProfile;
            if (LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName))
            {
                gameControllerProfile = GetCurrentControllerProfileFromUI();
                SaveControllerProfileToStorage($"Game_{currentGameName}", gameControllerProfile);
                currentProfile = gameControllerProfile;
            }
            else
            {
                globalControllerProfile = GetCurrentControllerProfileFromUI();
                SaveControllerProfileToStorage("Global", globalControllerProfile);
                currentProfile = globalControllerProfile;
            }

            // Send to helper
            SendButtonMappingsToHelper(currentProfile);

            // Update the summary display
            UpdateGamepadMappingSummary();
        }

        #region Trigger Travel

        private void LegionHairTriggers_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            bool enabled = LegionHairTriggersToggle?.IsOn ?? false;

            if (enabled)
            {
                // Hair triggers: Start=0 (no dead zone), End=99 (full press at 1% travel)
                // HID command end% is offset from 100%, so end=99 means trigger fully pressed at 1% travel
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = 0;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = "0%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = 99;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = "99%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = 0;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = "0%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = 99;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = "99%";
                }
            }
            else
            {
                // Disable hair triggers: Reset to full travel (0% for all = full trigger press required)
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = 0;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = "0%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = 0;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = "0%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = 0;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = "0%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = 0;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = "0%";
                }
            }

            // Enable/disable sliders based on hair triggers state
            UpdateTriggerSlidersEnabled(!enabled);

            Logger.Info($"Hair Triggers toggled: {enabled}");

            // Save the profile
            ControllerSettingChanged(sender, e);
        }

        private void UpdateTriggerSlidersEnabled(bool enabled)
        {
            if (LegionLeftTriggerStartSlider != null)
                LegionLeftTriggerStartSlider.IsEnabled = enabled;
            if (LegionLeftTriggerEndSlider != null)
                LegionLeftTriggerEndSlider.IsEnabled = enabled;
            if (LegionRightTriggerStartSlider != null)
                LegionRightTriggerStartSlider.IsEnabled = enabled;
            if (LegionRightTriggerEndSlider != null)
                LegionRightTriggerEndSlider.IsEnabled = enabled;
        }

        #endregion

        #region Desktop Controls Preset

        private void LegionDesktopControls_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info("Desktop Controls toggled event skipped - profile was just applied");
                return;
            }

            bool enabled = LegionDesktopControlsToggle?.IsOn ?? false;

            if (enabled)
            {
                // Apply desktop controls preset
                // 1. Set Right Stick as Mouse (for cursor movement)
                if (LegionJoystickAsMouseComboBox != null)
                    LegionJoystickAsMouseComboBox.SelectedIndex = 2; // Right Stick

                // 2. Apply button mappings (DPAD, LS scroll, LB/LT clicks)
                ApplyDesktopControlMappings();
            }
            else
            {
                // Reset to defaults
                if (LegionJoystickAsMouseComboBox != null)
                    LegionJoystickAsMouseComboBox.SelectedIndex = 0; // Disabled

                // Clear the desktop control button mappings
                ClearDesktopControlMappings();
            }

            Logger.Info($"Desktop Controls toggled: {enabled}");

            // Save the updated profile
            if (!isLoadingControllerProfile && !isSwitchingControllerProfile)
            {
                if (LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName))
                {
                    gameControllerProfile = GetCurrentControllerProfileFromUI();
                    SaveControllerProfileToStorage($"Game_{currentGameName}", gameControllerProfile);
                    Logger.Info($"Saved Desktop Controls state to game profile: {currentGameName}");
                }
                else
                {
                    globalControllerProfile = GetCurrentControllerProfileFromUI();
                    SaveControllerProfileToStorage("Global", globalControllerProfile);
                    Logger.Info("Saved Desktop Controls state to global profile");
                }
            }
        }

        private void ApplyDesktopControlMappings()
        {
            // Desktop Controls preset - uses LB/LT for clicks to avoid firmware drag-drop bug with triggers
            // HID key codes: Up=0x52, Down=0x51, Left=0x50, Right=0x4F, Enter=0x28, Escape=0x29, LeftGUI(Win)=0xE3
            // MouseButton dropdown index: 0=Left, 1=Right, 2=Middle, 3=ScrollUp, 4=ScrollDown

            // DPAD → Arrow keys (Type=1 Keyboard)
            gamepadButtonMappings["DPadUp"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x52 } };
            gamepadButtonMappings["DPadDown"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x51 } };
            gamepadButtonMappings["DPadLeft"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x50 } };
            gamepadButtonMappings["DPadRight"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x4F } };

            // Left Stick Up/Down → Arrow Up/Down (Type=1 Keyboard)
            gamepadButtonMappings["LSUp"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x52 } };    // Up Arrow
            gamepadButtonMappings["LSDown"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x51 } }; // Down Arrow

            // LSClick → Windows Key (Type=1 Keyboard)
            gamepadButtonMappings["LSClick"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0xE3 } }; // Left GUI (Win)

            // A → Enter, B → Escape (Type=1 Keyboard)
            gamepadButtonMappings["A"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x28 } };  // Enter
            gamepadButtonMappings["B"] = new ButtonMapping { Type = 1, KeyboardKeys = new List<int> { 0x29 } };  // Escape

            // LB → Left Click, LT → Right Click (Type=2 Mouse)
            gamepadButtonMappings["LB"] = new ButtonMapping { Type = 2, MouseButton = 0 };     // Left Click
            gamepadButtonMappings["LT"] = new ButtonMapping { Type = 2, MouseButton = 1 };     // Right Click

            // During profile loading, just update the dictionary - SendButtonMappingsToHelper will send once at the end
            if (!isLoadingControllerProfile)
            {
                SaveAndSendGamepadMappings();
            }
            UpdateGamepadMappingSummary();

            Logger.Info("Applied desktop control mappings: DPAD/LS→Arrows, LSClick→Win, A→Enter, B→Esc, LB→LClick, LT→RClick");
        }

        private void ClearDesktopControlMappings()
        {
            var desktopButtons = new[] { "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "LSUp", "LSDown", "LSClick", "A", "B", "LB", "LT" };

            // Set each button to reset state (Type=0, GamepadAction=0) to trigger HID reset
            foreach (var button in desktopButtons)
            {
                gamepadButtonMappings[button] = new ButtonMapping { Type = 0, GamepadAction = 0 };
            }

            // During profile loading, just update the dictionary - SendButtonMappingsToHelper will send once at the end
            if (!isLoadingControllerProfile)
            {
                SaveAndSendGamepadMappings();

                // Remove from dictionary after sending reset (only when not loading profile)
                foreach (var button in desktopButtons)
                {
                    gamepadButtonMappings.Remove(button);
                }
            }
            // When loading profile, keep Type=0 entries in dictionary so they get sent with other mappings

            UpdateGamepadMappingSummary();

            Logger.Info("Cleared desktop control mappings for DPAD, LS, A, B, LB, LT");
        }

        #endregion

        #region Nintendo Layout Preset

        private void LegionNintendoLayout_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile || isSwitchingControllerProfile)
                return;

            // Skip if a profile was just applied (prevents duplicate sends from queued UI events)
            if ((DateTime.Now - lastProfileApplyTime).TotalMilliseconds < 2000)
            {
                Logger.Info("Nintendo Layout toggled event skipped - profile was just applied");
                return;
            }

            bool enabled = LegionNintendoLayoutToggle?.IsOn ?? false;

            if (enabled)
            {
                // Apply Nintendo layout: swap A↔B and X↔Y
                ApplyNintendoLayoutMappings();
            }
            else
            {
                // Reset face buttons to default
                ClearNintendoLayoutMappings();
            }

            Logger.Info($"Nintendo Layout toggled: {enabled}");
        }

        private void ApplyNintendoLayoutMappings()
        {
            // GamepadAction uses dropdown index (from RemapActionHelper):
            // Index 15 = A (0x12), Index 16 = B (0x13), Index 17 = X (0x14), Index 18 = Y (0x15)
            // A → B: Type=0 (Gamepad), GamepadAction=16 (B)
            gamepadButtonMappings["A"] = new ButtonMapping { Type = 0, GamepadAction = 16 };
            // B → A: Type=0 (Gamepad), GamepadAction=15 (A)
            gamepadButtonMappings["B"] = new ButtonMapping { Type = 0, GamepadAction = 15 };
            // X → Y: Type=0 (Gamepad), GamepadAction=18 (Y)
            gamepadButtonMappings["X"] = new ButtonMapping { Type = 0, GamepadAction = 18 };
            // Y → X: Type=0 (Gamepad), GamepadAction=17 (X)
            gamepadButtonMappings["Y"] = new ButtonMapping { Type = 0, GamepadAction = 17 };

            SaveAndSendGamepadMappings();
            UpdateGamepadMappingSummary();

            Logger.Info("Applied Nintendo layout mappings: A→B, B→A, X→Y, Y→X");
        }

        private void ClearNintendoLayoutMappings()
        {
            var nintendoButtons = new[] { "A", "B", "X", "Y" };

            // Set each button to reset state (Type=0, GamepadAction=0) to trigger HID reset
            foreach (var button in nintendoButtons)
            {
                gamepadButtonMappings[button] = new ButtonMapping { Type = 0, GamepadAction = 0 };
            }
            SaveAndSendGamepadMappings();

            // Remove from dictionary after sending reset
            foreach (var button in nintendoButtons)
            {
                gamepadButtonMappings.Remove(button);
            }

            UpdateGamepadMappingSummary();

            Logger.Info("Cleared Nintendo layout mappings for A, B, X, Y");
        }

        #endregion

        private void LegionGamepadResetAll_Click(object sender, RoutedEventArgs e)
        {
            // Reset ALL gamepad buttons (including LS and RS stick directions) to their defaults
            // This ensures any button that might have been remapped is reset, not just those in the dictionary
            foreach (var buttonName in GamepadButtonNames)
            {
                gamepadButtonMappings[buttonName] = new ButtonMapping { Type = 0, GamepadAction = 0 };
            }

            // Send reset commands for all buttons - helper will clear then remap to self
            SaveAndSendGamepadMappings();

            Logger.Info($"Sent reset HID commands for all {GamepadButtonNames.Length} gamepad buttons");

            // Now clear the dictionary (buttons are now at default, no need to track them)
            gamepadButtonMappings.Clear();

            // Reset UI to defaults
            if (LegionGamepadTypeComboBox != null)
                LegionGamepadTypeComboBox.SelectedIndex = 0;
            if (LegionGamepadActionComboBox != null)
                LegionGamepadActionComboBox.SelectedIndex = 0;
            UpdateGamepadMappingUI(0);
            if (LegionGamepadKeyTags != null)
                LegionGamepadKeyTags.Children.Clear();

            // Update summary display (now empty)
            UpdateGamepadMappingSummary();

            Logger.Info("Reset all gamepad button mappings");
        }

        /// <summary>
        /// Updates the summary display showing which gamepad buttons are remapped.
        /// </summary>
        private void UpdateGamepadMappingSummary()
        {
            if (LegionGamepadRemappedTags == null || LegionGamepadRemappedLabel == null || LegionGamepadNoRemapsLabel == null)
                return;

            // Get list of remapped buttons (those with non-default mappings)
            var remappedButtons = gamepadButtonMappings
                .Where(kvp => IsButtonRemapped(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            // Clear existing tags
            LegionGamepadRemappedTags.Items.Clear();

            if (remappedButtons.Count > 0)
            {
                LegionGamepadRemappedLabel.Visibility = Visibility.Visible;
                LegionGamepadNoRemapsLabel.Visibility = Visibility.Collapsed;

                foreach (var buttonName in remappedButtons)
                {
                    var mapping = gamepadButtonMappings[buttonName];
                    var tag = CreateRemappedButtonTag(buttonName, mapping);
                    LegionGamepadRemappedTags.Items.Add(tag);
                }
            }
            else
            {
                LegionGamepadRemappedLabel.Visibility = Visibility.Collapsed;
                LegionGamepadNoRemapsLabel.Visibility = Visibility.Visible;
            }
        }

        private bool IsButtonRemapped(ButtonMapping mapping)
        {
            if (mapping == null) return false;
            // Type 0 (Gamepad) with action 0 (Disabled) means default/cleared
            // Keyboard or Mouse type means remapped
            // Gamepad type with action > 0 means remapped
            return mapping.Type != 0 || mapping.GamepadAction > 0 ||
                   (mapping.KeyboardKeys != null && mapping.KeyboardKeys.Count > 0);
        }

        private Border CreateRemappedButtonTag(string buttonName, ButtonMapping mapping)
        {
            var displayName = GetGamepadButtonDisplayName(buttonName);
            var mappingDesc = GetMappingDescription(mapping);

            var tagBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 70, 90)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 4, 0)
            };

            var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var buttonText = new TextBlock
            {
                Text = $"{displayName} → {mappingDesc}",
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            var clearButton = new Button
            {
                Content = "×",
                FontSize = 10,
                Padding = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                MinHeight = 0,
                Tag = buttonName
            };
            clearButton.Click += (s, e) => ClearSingleGamepadMapping((string)((Button)s).Tag);

            tagPanel.Children.Add(buttonText);
            tagPanel.Children.Add(clearButton);
            tagBorder.Child = tagPanel;

            // Click on tag to select that button
            tagBorder.Tapped += (s, e) =>
            {
                var index = Array.IndexOf(GamepadButtonNames, buttonName);
                if (index >= 0 && LegionGamepadButtonSelectorComboBox != null)
                    LegionGamepadButtonSelectorComboBox.SelectedIndex = index;
            };

            return tagBorder;
        }

        private void ClearSingleGamepadMapping(string buttonName)
        {
            if (gamepadButtonMappings.ContainsKey(buttonName))
            {
                // Set to reset state (Type=0, GamepadAction=0) to trigger HID command
                // This maps the button back to itself (default behavior)
                gamepadButtonMappings[buttonName] = new ButtonMapping { Type = 0, GamepadAction = 0 };
                SaveAndSendGamepadMappings();

                // Now remove from dictionary (button is at default, no need to track)
                gamepadButtonMappings.Remove(buttonName);

                // Update the summary display
                UpdateGamepadMappingSummary();

                // If this was the currently selected button, reload UI
                if (LegionGamepadButtonSelectorComboBox != null)
                {
                    var currentButtonName = GetGamepadButtonNameFromIndex(LegionGamepadButtonSelectorComboBox.SelectedIndex);
                    if (currentButtonName == buttonName)
                        LoadGamepadMappingToUI(buttonName);
                }

                Logger.Info($"Cleared gamepad button mapping for {buttonName} (sent HID reset command)");
            }
        }

        private string GetGamepadButtonDisplayName(string buttonName)
        {
            // Convert internal names to display names
            switch (buttonName)
            {
                case "LSClick": return "LS Click";
                case "LSUp": return "LS Up";
                case "LSDown": return "LS Down";
                case "LSLeft": return "LS Left";
                case "LSRight": return "LS Right";
                case "RSClick": return "RS Click";
                case "RSUp": return "RS Up";
                case "RSDown": return "RS Down";
                case "RSLeft": return "RS Left";
                case "RSRight": return "RS Right";
                case "DPadUp": return "D-Up";
                case "DPadDown": return "D-Down";
                case "DPadLeft": return "D-Left";
                case "DPadRight": return "D-Right";
                default: return buttonName;
            }
        }

        private string GetMappingDescription(ButtonMapping mapping)
        {
            if (mapping == null) return "Default";

            switch (mapping.Type)
            {
                case 0: // Gamepad
                    if (mapping.GamepadAction == 0) return "Disabled";
                    return GetGamepadActionName(mapping.GamepadAction);
                case 1: // Keyboard
                    if (mapping.KeyboardKeys == null || mapping.KeyboardKeys.Count == 0)
                        return "Keys";
                    return string.Join("+", mapping.KeyboardKeys.Select(k => GetKeyDisplayName(k)));
                case 2: // Mouse
                    return GetMouseActionName(mapping.MouseButton);
                default:
                    return "Unknown";
            }
        }

        private string GetGamepadActionName(int action)
        {
            string[] names = { "Disabled", "LS Click", "LS Up", "LS Down", "LS Left", "LS Right",
                              "RS Click", "RS Up", "RS Down", "RS Left", "RS Right",
                              "D-Up", "D-Down", "D-Left", "D-Right",
                              "A", "B", "X", "Y", "LB", "LT", "RB", "RT", "View", "Menu" };
            return action >= 0 && action < names.Length ? names[action] : $"Action{action}";
        }

        private string GetMouseActionName(int action)
        {
            string[] names = { "Left Click", "Right Click", "Middle Click",
                              "Scroll Up", "Scroll Down", "Scroll Left", "Scroll Right" };
            return action >= 0 && action < names.Length ? names[action] : $"Mouse{action}";
        }

        #endregion

        #endregion

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
            GlobalProfileTDPModeText.Text = GetLegionModeShortName(globalProfile.LegionPerformanceMode);

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
            ACProfileTDPModeText.Text = GetLegionModeShortName(acProfile.LegionPerformanceMode);
            DCProfileTDPModeText.Text = GetLegionModeShortName(dcProfile.LegionPerformanceMode);

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
                if (PowerSourceProfileToggle?.IsOn == true)
                {
                    // Show AC/DC game profiles - TDP Mode (Legion only)
                    GameACDCProfileTDPModeLabel.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameDCProfileTDPModeText.Visibility = tdpModeVisibility;
                    GameACProfileTDPModeText.Text = GetLegionModeShortName(gameACProfile.LegionPerformanceMode);
                    GameDCProfileTDPModeText.Text = GetLegionModeShortName(gameDCProfile.LegionPerformanceMode);

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
                    GameProfileTDPModeText.Text = GetLegionModeShortName(gameProfile.LegionPerformanceMode);

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
            bool powerSourceEnabled = PowerSourceProfileToggle?.IsOn == true;

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
            bool powerSourceEnabled = PowerSourceProfileToggle?.IsOn == true;

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
                bool hasACDC = settings.Containers.ContainsKey($"Profile_Game_{gameName}_AC");
                bool hasSingle = settings.Containers.ContainsKey($"Profile_Game_{gameName}");

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

                if (hasACDC)
                {
                    // Load AC/DC profiles
                    var gameAC = new PerformanceProfile();
                    var gameDC = new PerformanceProfile();
                    LoadProfileFromStorage($"Game_{gameName}_AC", gameAC);
                    LoadProfileFromStorage($"Game_{gameName}_DC", gameDC);

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
                        AddTextBlock(acDcGrid, rowIndex, 1, GetLegionModeShortName(gameAC.LegionPerformanceMode), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, GetLegionModeShortName(gameDC.LegionPerformanceMode), 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
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
                else if (hasSingle)
                {
                    // Load single profile
                    var game = new PerformanceProfile();
                    LoadProfileFromStorage($"Game_{gameName}", game);

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
                        AddTextBlock(singleGrid, rowIndex, 1, GetLegionModeShortName(game.LegionPerformanceMode), 10, "#FFFFFF");
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

            // Find all containers with invalid game names (case-insensitive check)
            foreach (var containerName in settings.Containers.Keys)
            {
                if (containerName.IndexOf("No game detected", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    profilesToDelete.Add(containerName);
                }
            }

            // Delete invalid profiles
            foreach (var containerName in profilesToDelete)
            {
                settings.DeleteContainer(containerName);
                Logger.Info($"Cleaned up invalid profile container: {containerName}");
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

                _saveStickyTDP = settings.Values.ContainsKey("ProfileSaveStickyTDP") ? (bool)settings.Values["ProfileSaveStickyTDP"] : false;

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
                if (ProfileSaveStickyTDPCheckBox != null) ProfileSaveStickyTDPCheckBox.IsChecked = _saveStickyTDP;
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
            settings.Values["ProfileSaveStickyTDP"] = ProfileSaveStickyTDPCheckBox?.IsChecked ?? false;
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
            _saveStickyTDP = ProfileSaveStickyTDPCheckBox?.IsChecked ?? false;
        }

        private void MainNavigationView_SelectionChanged(object sender, object args)
        {
            if (args is NavigationViewSelectionChangedEventArgs navArgs && navArgs.SelectedItem is NavigationViewItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";

                // Hide all sections
                QuickSettingsScrollViewer.Visibility = Visibility.Collapsed;
                PerformanceScrollViewer.Visibility = Visibility.Collapsed;
                GameScrollViewer.Visibility = Visibility.Collapsed;
                AMDScrollViewer.Visibility = Visibility.Collapsed;
                ScalingScrollViewer.Visibility = Visibility.Collapsed;
                LegionScrollViewer.Visibility = Visibility.Collapsed;
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
                        break;
                    case "System":
                        SystemScrollViewer.Visibility = Visibility.Visible;
                        SystemScrollViewer.ChangeView(null, 0, null, true);
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
                // Check if focus is on a NavigationViewItem or within the nav area
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
            if (SystemNavItem.FocusState != FocusState.Unfocused) return true;

            // Fallback: walk visual tree for other nav-related elements
            var current = element;
            while (current != null)
            {
                if (current is NavigationViewItem)
                    return true;
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return false;
        }

        private void NavigateToPreviousTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            int currentIndex = visibleItems.IndexOf(MainNavigationView.SelectedItem as NavigationViewItem);
            if (currentIndex > 0)
            {
                MainNavigationView.SelectedItem = visibleItems[currentIndex - 1];
            }
            else
            {
                // Wrap around to last tab
                MainNavigationView.SelectedItem = visibleItems[visibleItems.Count - 1];
            }
        }

        private void NavigateToNextTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            int currentIndex = visibleItems.IndexOf(MainNavigationView.SelectedItem as NavigationViewItem);
            if (currentIndex < visibleItems.Count - 1)
            {
                MainNavigationView.SelectedItem = visibleItems[currentIndex + 1];
            }
            else
            {
                // Wrap around to first tab
                MainNavigationView.SelectedItem = visibleItems[0];
            }
        }

        private List<NavigationViewItem> GetVisibleNavigationItems()
        {
            var visibleItems = new List<NavigationViewItem>();
            foreach (var item in MainNavigationView.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Visibility == Visibility.Visible)
                {
                    visibleItems.Add(navItem);
                }
            }
            return visibleItems;
        }

        private void GamingWidget_Unloaded(object sender, RoutedEventArgs e)
        {
            // Set flag immediately to prevent any pending async operations from updating UI
            isUnloading = true;

            Logger.Info($"GamingWidget_Unloaded called. Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, App.Connection is null: {App.Connection == null}");

            // Unsubscribe from power source changes
            PowerManager.PowerSupplyStatusChanged -= PowerManager_PowerSourceChanged;
            if (PowerSourceProfileToggle != null)
            {
                PowerSourceProfileToggle.Toggled -= PowerSourceProfileToggle_Toggled;
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

            // Unregister from static events to prevent memory leaks and duplicate handlers
            Logger.Info("Unregistering event handlers...");
            App.AppServiceConnected -= GamingWidget_AppServiceConnected;
            App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
            App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;

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
            Logger.Info("GamingWidget being deactivated - stopping pending updates and unsubscribing from events.");
            try
            {
                properties.StopPendingUpdates();
                Logger.Info("Pending updates stopped.");

                // Unsubscribe from AppService events to prevent this deactivated instance from receiving messages
                App.AppServiceConnected -= GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
                App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;
                Logger.Info("Event handlers unsubscribed.");
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
            Logger.Info($"Current state - Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}, App.Connection is null: {App.Connection == null}");

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

            widget = e.Parameter as XboxGameBarWidget;
            if (widget != null)
            {
                Logger.Info($"Running as a Xbox Game Bar widget. Widget type: {widget.GetType().FullName}");

                Logger.Info("Calling widget.CenterWindowAsync()...");
                await widget.CenterWindowAsync();
                Logger.Info("widget.CenterWindowAsync() completed.");

                Logger.Info("Registering widget event handlers (RequestedThemeChanged, SettingsClicked)...");
                widget.RequestedThemeChanged += GamingWidget_RequestedThemeChanged;
                widget.SettingsClicked += GamingWidget_SettingsClicked;
                Logger.Info("Widget event handlers registered.");

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
            }
            else
            {
                Logger.Info("XboxGameBarWidget not available, probably running as an app instead of widget.");
            }

            if (App.Connection == null && ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                Logger.Info("App.Connection is null. Registering event handlers and launching full trust process.");
                // Use -= before += to ensure we don't register duplicate handlers
                App.AppServiceConnected -= GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
                App.AppServiceConnected += GamingWidget_AppServiceConnected;
                App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;

                // Launch helper with guards (checks heartbeat, enforces rate limiting)
                await LaunchHelperWithGuardsAsync("OnNavigatedTo - initial connection");
            }
            else
            {
                Logger.Info($"Not launching full trust process. App.Connection is null: {App.Connection == null}, FullTrustAppContract present: {ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0)}");

                // If connection already exists, register event handlers and sync properties
                if (App.Connection != null)
                {
                    Logger.Info("AppService connection already exists. Ensuring event handlers are registered.");

                    // Hide connection status banner since we're connected
                    HideConnectionBanner();
                    Logger.Info("Connection status banner hidden - already connected.");

                    // Use -= before += to ensure we don't register duplicate handlers
                    Logger.Info("Unregistering existing event handlers (if any)...");
                    App.AppServiceConnected -= GamingWidget_AppServiceConnected;
                    App.AppServiceDisconnected -= GamingWidget_AppServiceDisconnected;
                    App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;

                    Logger.Info("Registering event handlers...");
                    App.AppServiceConnected += GamingWidget_AppServiceConnected;
                    App.AppServiceDisconnected += GamingWidget_AppServiceDisconnected;
                    App.AppServiceRequestReceived += AppServiceConnection_RequestReceived;
                    Logger.Info("Event handlers registered.");

                    // Sync properties since we're already connected
                    Logger.Info("Syncing properties with helper since connection already exists...");
                    try
                    {
                        isApplyingHelperUpdate = true;

                        // Suppress LegionPerformanceMode value updates during sync - we'll apply profile mode afterward
                        // This prevents helper's cached Custom mode from overwriting the profile's mode
                        if (legionPerformanceMode != null)
                        {
                            legionPerformanceMode.SuppressUpdates = true;
                        }

                        // Skip TDP sync if profile uses a preset mode (not Custom)
                        // This prevents the TDP sync from triggering Custom mode on the hardware LED
                        try
                        {
                            var profile = GetProfile(currentProfileName);
                            if (profile != null)
                            {
                                bool isPresetMode = profile.LegionPerformanceMode != 255; // Not Custom
                                if (tdp != null && isPresetMode)
                                {
                                    tdp.SkipSync = true;
                                    Logger.Info($"TDP sync will be skipped - profile uses {GetLegionModeShortName(profile.LegionPerformanceMode)} mode");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Could not check profile for TDP sync skip: {ex.Message}");
                        }

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

                    // Stop any pending slider updates from the sync - we'll apply profile values instead
                    properties.StopPendingUpdates();

                    // Re-enable updates for LegionPerformanceMode now that profile is applied
                    if (legionPerformanceMode != null)
                    {
                        legionPerformanceMode.SuppressUpdates = false;
                    }

                    // Re-enable TDP sync for future syncs
                    if (tdp != null)
                    {
                        tdp.SkipSync = false;
                    }

                    // Re-enable OS Power Mode sync for future syncs
                    if (osPowerMode != null)
                    {
                        osPowerMode.SkipSync = false;
                    }

                    // Apply profile TDP to helper now that we're synced
                    // Profile was loaded in constructor before connection, so TDP may not have been applied
                    await ApplyProfileTDPToHelper();

                    // Update profile display now that legionGoDetected has been synced from helper
                    // This ensures TDP Mode shows in Profiles tab on fresh start
                    UpdateProfileDisplay();
                    Logger.Info("Profile display updated after sync - legionGoDetected=" + (legionGoDetected?.Value.ToString() ?? "null"));

                    // Clear initial sync flag - profile is loaded and applied, user changes should now save
                    // Add a small delay to let any pending ValueChanged events settle first
                    await Task.Delay(200);
                    isInitialSync = false;
                    Logger.Info("Initial sync complete - profile saves are now enabled");
                }
            }

            Logger.Info("=== OnNavigatedTo END ===");
        }

        public async Task GamingWidget_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_LeavingBackground called. Widget is null: {widget == null}, App.Connection is null: {App.Connection == null}, WidgetActivity is null: {widgetActivity == null}");

            if (widget != null)
            {
                await widget.CenterWindowAsync();
            }

            if (App.Connection != null)
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

            isForeground.SetValue(true);
            Logger.Info("GamingWidget_LeavingBackground completed.");
        }

        public void GamingWidget_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            Logger.Info($"GamingWidget_EnteredBackground called. WidgetActivity is null: {widgetActivity == null}");
            isForeground.SetValue(false);
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
        /// When the desktop process is connected, get ready to send/receive requests
        /// </summary>
        private async void GamingWidget_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            Logger.Info("=== GamingWidget_AppServiceConnected START ===");

            // Stop reconnection timeout timer - connection established
            StopReconnectionTimeoutTimer();

            Logger.Info($"Widget is null: {widget == null}, WidgetActivity is null: {widgetActivity == null}");

            if (widget != null)
            {
                Logger.Info($"Widget state - RequestedTheme: {widget.RequestedTheme}");

                // Create widget activity if needed
                Logger.Info("Checking if widget activity needs to be created...");
                await CreateWidgetActivity();

                // Create app target tracker if needed
                Logger.Info("Checking if app target tracker needs to be created...");
                await CreateAppTargetTracker();
            }
            else
            {
                Logger.Info("Widget is null in AppServiceConnected - likely running as standalone app.");
            }

            // Register for request received events via the App-level relay
            Logger.Info("Registering for AppServiceRequestReceived events...");
            App.AppServiceRequestReceived -= AppServiceConnection_RequestReceived;
            App.AppServiceRequestReceived += AppServiceConnection_RequestReceived;
            Logger.Info("AppServiceRequestReceived handler registered.");

            Logger.Info("Starting property sync with helper...");
            try
            {
                // Set flag to prevent Sticky TDP target from updating during sync
                isApplyingHelperUpdate = true;

                // Suppress LegionPerformanceMode value updates during sync - we'll apply profile mode afterward
                // This prevents the helper's cached mode (e.g., Custom) from overwriting the profile's mode
                if (legionPerformanceMode != null)
                {
                    legionPerformanceMode.SuppressUpdates = true;
                }

                // Skip TDP sync if profile uses a preset mode (not Custom)
                // This prevents the TDP sync from triggering Custom mode on the hardware LED
                try
                {
                    var profile = GetProfile(currentProfileName);
                    if (profile != null)
                    {
                        bool isPresetMode = profile.LegionPerformanceMode != 255; // Not Custom
                        if (tdp != null && isPresetMode)
                        {
                            tdp.SkipSync = true;
                            Logger.Info($"TDP sync will be skipped - profile uses {GetLegionModeShortName(profile.LegionPerformanceMode)} mode");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not check profile for TDP sync skip: {ex.Message}");
                }

                // Skip OS Power Mode sync if profile has it saved
                // This prevents sync from overwriting profile-loaded OS Power Mode with hardware state
                if (SaveOSPowerMode && osPowerMode != null)
                {
                    osPowerMode.SkipSync = true;
                    Logger.Info("OSPowerMode sync will be skipped - profile has OS Power Mode saved");
                }

                await properties.Sync();
                Logger.Info("Property sync completed successfully.");

                // Register Chill FPS handlers after first sync to prevent crash
                RegisterChillFPSHandlers();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during property sync: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }
            finally
            {
                isApplyingHelperUpdate = false;
            }

            try
            {
                // Stop any pending slider updates from the sync - we'll apply profile values instead
                // Must run on UI thread since DispatcherTimer is UI-bound
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    properties.StopPendingUpdates();
                });
                Logger.Info("Stopped pending updates after sync.");

                // Re-enable updates for LegionPerformanceMode BEFORE applying profile
                // so the profile's mode correctly updates the UI and internal value
                if (legionPerformanceMode != null)
                {
                    legionPerformanceMode.SuppressUpdates = false;
                }

                // Re-enable TDP sync for future syncs
                if (tdp != null)
                {
                    tdp.SkipSync = false;
                }

                // Re-enable OS Power Mode sync for future syncs
                if (osPowerMode != null)
                {
                    osPowerMode.SkipSync = false;
                }

                // Send OSD config to helper now that connection is established
                SendOSDConfigToHelper();

                // Apply profile TDP to helper now that connection is established
                // Profile was loaded in constructor before connection, so TDP wasn't actually applied
                await ApplyProfileTDPToHelper();

                // Clear initial sync flag - profile is loaded and applied, user changes should now save
                // Add a small delay to let any pending ValueChanged events settle first
                await Task.Delay(200);
                isInitialSync = false;
                Logger.Info("Initial sync complete - profile saves are now enabled");

                // On clean install, initialize profile with current system values instead of defaults
                // This prevents overwriting user's current CPU Boost and EPP settings with defaults
                if (isCleanInstall)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        // Get current system values from helper
                        bool currentCPUBoost = cpuBoost?.Value ?? true;
                        double currentCPUEPP = cpuEPP?.Value ?? 50;

                        Logger.Info($"Clean install: initializing profiles with current system values - CPUBoost={currentCPUBoost}, CPUEPP={currentCPUEPP}");

                        // Update all profiles with current values
                        globalProfile.CPUBoost = currentCPUBoost;
                        globalProfile.CPUEPP = currentCPUEPP;
                        acProfile.CPUBoost = currentCPUBoost;
                        acProfile.CPUEPP = currentCPUEPP;
                        dcProfile.CPUBoost = currentCPUBoost;
                        dcProfile.CPUEPP = currentCPUEPP;

                        // Save the profiles with current values
                        SaveProfileToStorage("Global", globalProfile);
                        SaveProfileToStorage("AC", acProfile);
                        SaveProfileToStorage("DC", dcProfile);

                        // Update UI to reflect current values
                        CPUBoostToggle.IsOn = currentCPUBoost;
                        CPUEPPSlider.Value = currentCPUEPP;

                        isCleanInstall = false; // Only do this once
                        Logger.Info("Clean install initialization complete - profiles saved with current system values");
                    });
                }

                // Hide connection status banner and update profile display now that we're connected
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    // Verify connection still exists (could have been cleared by disconnect during sync)
                    if (App.Connection == null)
                    {
                        Logger.Warn("Connection was cleared during sync - keeping banner visible");
                        return;
                    }

                    HideConnectionBanner();
                    Logger.Info("Connection status banner hidden - connected to helper.");

                    // Update profile display now that legionGoDetected has been synced from helper
                    // This ensures TDP Mode shows in Profiles tab on fresh start
                    UpdateProfileDisplay();
                    Logger.Info("Profile display updated after sync - legionGoDetected=" + (legionGoDetected?.Value.ToString() ?? "null"));
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in post-sync initialization: {ex}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }

            Logger.Info("=== GamingWidget_AppServiceConnected END ===");
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
            Loading
        }

        /// <summary>
        /// Shows the connection status banner with appropriate color and message based on state.
        /// </summary>
        /// <param name="state">The banner state to display</param>
        private void ShowConnectionBanner(BannerState state)
        {
            if (ConnectionStatusBanner == null || ConnectionStatusText == null)
                return;

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
            }
            ConnectionStatusBanner.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Hides the connection status banner.
        /// </summary>
        private void HideConnectionBanner()
        {
            if (ConnectionStatusBanner != null)
            {
                ConnectionStatusBanner.Visibility = Visibility.Collapsed;
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
                        int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
                        int modeIndex = Array.IndexOf(modeValues, profileMode);

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
                // Format: {"pid":1234,"timestamp":1234567890,"connected":true,"elevated":true}
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
                    Logger.Info($"Skipping launch ({reason}) - helper is already alive, waiting for reconnection");
                    // Show reconnecting banner since we're waiting for helper to reconnect
                    ShowConnectionBanner(BannerState.Reconnecting);

                    // Start timeout timer - if helper doesn't reconnect within timeout, force launch
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
        /// Starts a timer that will force-launch the helper if connection isn't established within timeout.
        /// Call this when helper is detected as alive but not connected.
        /// </summary>
        private void StartReconnectionTimeoutTimer()
        {
            // Stop any existing timer
            StopReconnectionTimeoutTimer();

            // Don't start timer if already connected
            if (App.Connection != null)
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
            if (App.Connection != null)
            {
                Logger.Info("Reconnection timeout fired but already connected - skipping force launch");
                HideConnectionBanner();
                return;
            }

            Logger.Info("Reconnection timeout fired - force launching helper");
            await LaunchHelperWithGuardsAsync("Reconnection timeout", forceLaunch: true);
        }

        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void GamingWidget_AppServiceDisconnected(object sender, EventArgs e)
        {
            var eventArgs = e as BackgroundTaskCancellationEventArgs;
            Logger.Info($"GamingWidget_AppServiceDisconnected called. Reason: {eventArgs?.Reason.ToString() ?? "Unknown"}. WidgetActivity is null: {widgetActivity == null}, Widget is null: {widget == null}");

            // Unregister as active widget
            Logger.Info("Unregistering this widget as active due to disconnect.");
            App.UnregisterActiveGamingWidget(this);

            // Clean up properties on UI thread to avoid RPC_E_WRONG_THREAD error
            Logger.Info("Cleaning up properties during disconnect...");
            try
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        properties.Cleanup();
                        Logger.Info("Properties cleaned up.");

                        // Show connection status banner since we're disconnected
                        ShowConnectionBanner(BannerState.Disconnected);
                        Logger.Info("Connection status banner shown - disconnected from helper.");
                    }
                    catch (Exception cleanupEx)
                    {
                        Logger.Error($"Error in properties cleanup: {cleanupEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error dispatching properties cleanup: {ex.Message}");
            }

            // Clean up widget activity - capture to local var to avoid race condition
            var activity = widgetActivity;
            if (activity != null)
            {
                Logger.Info("Completing and disposing widget activity.");
                try
                {
                    activity.Complete();
                    Logger.Info("Widget activity stopped successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing widget activity: {ex.Message}");
                }
                finally
                {
                    widgetActivity = null;
                }
            }
            else
            {
                Logger.Info("WidgetActivity was already null during disconnect.");
            }

            // Relaunch if we're running as a widget (not standalone app)
            // AND we don't already have a connection (prevents duplicate launches)
            // Accept any disconnection reason since helper may exit gracefully (e.g., PawnIO install, restart)
            bool shouldRelaunch = widget != null && App.Connection == null;

            if (shouldRelaunch)
            {
                Logger.Info($"Widget disconnected (reason: {eventArgs?.Reason.ToString() ?? "Unknown"}), waiting for helper reconnection...");

                // Wait for helper to reconnect naturally (it has a 1-second retry loop)
                // This avoids triggering unnecessary UAC prompts when helper is still running
                await Task.Delay(3000);

                // Check if reconnected during the wait
                if (App.Connection != null)
                {
                    Logger.Info("Helper reconnected during wait period, no relaunch needed.");
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        HideConnectionBanner();
                    });
                    return;
                }

                // Force launch helper since we waited and connection didn't recover
                // Helper has mutex protection so it will restart cleanly
                await LaunchHelperWithGuardsAsync("AppServiceDisconnected - reconnection timeout", forceLaunch: true);
            }
            else
            {
                Logger.Info($"Skipping relaunch. Widget is null: {widget == null}, Reason: {eventArgs?.Reason.ToString() ?? "Unknown"}, Connection exists: {App.Connection != null}");
            }
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
        /// Handle calculation request from desktop process
        /// (dummy scenario to show that connection is bi-directional)
        /// </summary>
        private async void AppServiceConnection_RequestReceived(object sender, AppServiceRequestReceivedEventArgs args)
        {
            try
            {
                // Only process messages if this is the active widget instance
                // This prevents multiple instances from handling the same message
                var activeWidget = App.GetActiveGamingWidget();
                if (activeWidget != null && activeWidget != this)
                {
                    Logger.Info($"Widget received message {args.Request.Message.ToDebugString()} from helper, but this is NOT the active instance. Ignoring.");
                    return;
                }

                Logger.Info($"Widget received message {args.Request.Message.ToDebugString()} from helper.");

                // Check for focus widget request from helper
                if (args.Request.Message.TryGetValue("Function", out object funcObj) &&
                    (int)funcObj == (int)Shared.Enums.Function.Labs_FocusWidget)
                {
                    Logger.Info("Focus widget request received from helper");
                    await FocusThisWidgetAsync();
                    return;
                }

                // Skip TDP and CurrentTDP updates during Sticky TDP reapply to prevent flicker and race conditions
                if (isStickyTDPReapplying && args.Request.Message.ContainsKey("Function"))
                {
                    var function = (int)args.Request.Message["Function"];
                    if (function == (int)Shared.Enums.Function.TDP)
                    {
                        Logger.Info("Skipping TDP slider update during Sticky TDP reapply to prevent flicker.");
                        return;
                    }
                    if (function == (int)Shared.Enums.Function.CurrentTDP)
                    {
                        Logger.Info("Skipping CurrentTDP update during Sticky TDP reapply to prevent race condition.");
                        return;
                    }
                }

                // Set flag to prevent auto-save when helper updates slider values
                isApplyingHelperUpdate = true;
                try
                {
                    await properties.OnRequestReceived(args.Request);

                    // Wait a bit for async ValueChanged events to complete before clearing the flag
                    // This prevents race condition where ValueChanged fires after flag is cleared
                    await Task.Delay(50);
                }
                finally
                {
                    isApplyingHelperUpdate = false;
                }

                Logger.Info($"Widget finished processing message {args.Request.Message.ToDebugString()}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing message from helper: {ex.Message}");
                Logger.Error($"Exception Type: {ex.GetType().FullName}");
                Logger.Error($"Stack Trace: {ex.StackTrace}");
            }
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

        #region Scale Tab Visibility

        /// <summary>
        /// Shows or hides the Scale tab and Lossless Scaling tile based on Lossless Scaling installation
        /// </summary>
        private void SetScaleTabVisibility(bool installed)
        {
            if (ScalingNavItem != null)
            {
                ScalingNavItem.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Scale tab visibility set to: {installed} (Lossless Scaling installed: {installed})");
            }

            // Rebuild Quick Settings tiles to show/hide Lossless Scaling tile
            if (quickSettingsInitialized)
            {
                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                Logger.Info($"Rebuilt Quick Settings tiles for Lossless Scaling visibility: {installed}");
            }
        }

        #endregion

        #region Legion Go Handlers

        /// <summary>
        /// Shows or hides the Legion tab based on device detection
        /// </summary>
        private void SetLegionTabVisibility(bool visible)
        {
            if (LegionNavItem != null)
            {
                LegionNavItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Legion tab visibility set to: {visible}");
            }

            // Show/hide TDP Mode card in Performance tab for Legion devices
            if (TDPModeCard != null)
            {
                TDPModeCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"TDP Mode card visibility set to: {visible}");

                // Update XY focus bindings based on Legion detection
                UpdatePerformanceTabXYFocus(visible);

                // Sync TDP Mode with Legion Performance Mode if visible
                // Skip during initial sync - ApplyProfileTDPToHelper will set the correct value
                if (visible && LegionPerformanceModeComboBox != null && TDPModeComboBox != null && !isInitialSync)
                {
                    TDPModeComboBox.SelectedIndex = LegionPerformanceModeComboBox.SelectedIndex;
                }
            }

            // Show/hide Manufacturer WMI option in TDP Method dropdown based on Legion detection
            if (TdpMethodWmiItem != null)
            {
                TdpMethodWmiItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"TDP Method WMI option visibility set to: {visible}");

                // If Legion detected and WMI option now visible, select it if not already selected
                if (visible && TdpMethodComboBox != null && TdpMethodComboBox.SelectedIndex < 0)
                {
                    TdpMethodComboBox.SelectedIndex = 0; // ManufacturerWMI
                }
                // If Legion not detected and WMI was selected, switch to PawnIO
                else if (!visible && TdpMethodComboBox != null)
                {
                    var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is string tag && tag == "ManufacturerWMI")
                    {
                        // Find and select PawnIO
                        for (int i = 0; i < TdpMethodComboBox.Items.Count; i++)
                        {
                            if (TdpMethodComboBox.Items[i] is ComboBoxItem item && item.Tag is string t && t == "PawnIO")
                            {
                                TdpMethodComboBox.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
            }

            // Update the TDP Method description based on Legion detection
            if (TdpMethodDescription != null)
            {
                if (visible)
                {
                    TdpMethodDescription.Text = "Select TDP control method. Manufacturer WMI (Legion) and PawnIO are anti-cheat safe.";
                }
                else
                {
                    TdpMethodDescription.Text = "Select TDP control method. PawnIO is anti-cheat safe. WinRing0 may trigger anti-cheat.";
                }
            }

            // Refresh Quick Settings tiles to show/hide Legion-specific tiles
            RefreshQuickSettingsForLegion();
        }

        /// <summary>
        /// Updates XY focus bindings in Performance tab based on Legion detection
        /// </summary>
        private void UpdatePerformanceTabXYFocus(bool isLegion)
        {
            if (PerformanceOverlayComboBox != null && TDPModeComboBox != null && TDPSlider != null)
            {
                if (isLegion)
                {
                    // Legion: PerformanceOverlay -> TDPMode -> TDPSlider
                    PerformanceOverlayComboBox.XYFocusDown = TDPModeComboBox;
                    TDPSlider.XYFocusUp = TDPModeComboBox;
                }
                else
                {
                    // Non-Legion: PerformanceOverlay -> TDPSlider
                    PerformanceOverlayComboBox.XYFocusDown = TDPSlider;
                    TDPSlider.XYFocusUp = PerformanceOverlayComboBox;
                }
            }
        }

        /// <summary>
        /// Shows or hides the Default Game Profile card based on profile availability.
        /// </summary>
        private void SetDefaultProfileCardVisibility(bool isVisible)
        {
            if (DefaultGameProfileCard != null)
            {
                DefaultGameProfileCard.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Default Game Profile card visibility set to: {isVisible}");

                // Update XY navigation when DGP visibility changes
                UpdatePerformanceTabXYNavigation();
            }
        }

        /// <summary>
        /// Updates XY focus navigation for the Performance tab based on current state.
        /// Flow: Nav -> DGP Toggle (if visible) -> PerGameProfile Toggle (if game detected) -> Performance Overlay -> ...
        /// When DGP is ON: Nav -> DGP Toggle -> TDP Extras (skip disabled TDP/FPS controls)
        /// </summary>
        private void UpdatePerformanceTabXYNavigation()
        {
            // Early exit if UI elements aren't ready
            if (PerformanceNavItem == null || PerformanceOverlayComboBox == null) return;

            bool dgpVisible = DefaultGameProfileCard?.Visibility == Visibility.Visible;
            bool dgpEnabled = defaultGameProfileEnabled?.Value == true;
            bool gameDetected = runningGame?.Value.IsValid() == true;

            Logger.Debug($"UpdatePerformanceTabXYNavigation: dgpVisible={dgpVisible}, dgpEnabled={dgpEnabled}, gameDetected={gameDetected}");

            // Determine the chain of focusable elements
            // Start from PerformanceNavItem going down

            if (dgpVisible && DefaultProfileToggle != null)
            {
                // DGP is visible: Nav -> DefaultProfileToggle
                PerformanceNavItem.XYFocusDown = DefaultProfileToggle;
                DefaultProfileToggle.XYFocusUp = PerformanceNavItem;

                if (dgpEnabled && TDPExtrasExpandToggle != null)
                {
                    // DGP is ON: skip disabled TDP/FPS controls, go to TDP Extras dropdown
                    // (OS Power Mode and CPU Extras are still available)
                    DefaultProfileToggle.XYFocusDown = TDPExtrasExpandToggle;
                    TDPExtrasExpandToggle.XYFocusUp = DefaultProfileToggle;

                    // Set navigation from TDP Extras down to OS Power Mode, and OS Power Mode up to TDP Extras
                    if (OSPowerModeComboBox != null)
                    {
                        TDPExtrasExpandToggle.XYFocusDown = OSPowerModeComboBox;
                        OSPowerModeComboBox.XYFocusUp = TDPExtrasExpandToggle;
                    }
                }
                else if (gameDetected && PerGameProfileToggle != null)
                {
                    // DGP visible but OFF, game detected: DGP Toggle -> PerGameProfile Toggle -> Overlay
                    DefaultProfileToggle.XYFocusDown = PerGameProfileToggle;
                    PerGameProfileToggle.XYFocusUp = DefaultProfileToggle;
                    PerGameProfileToggle.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = PerGameProfileToggle;
                }
                else
                {
                    // DGP visible but OFF, no game: DGP Toggle -> Overlay (skip disabled PerGameProfile)
                    DefaultProfileToggle.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = DefaultProfileToggle;
                }
            }
            else
            {
                // DGP not visible
                if (gameDetected && PerGameProfileToggle != null)
                {
                    // No DGP, game detected: Nav -> PerGameProfile Toggle -> Overlay
                    PerformanceNavItem.XYFocusDown = PerGameProfileToggle;
                    PerGameProfileToggle.XYFocusUp = PerformanceNavItem;
                    PerGameProfileToggle.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = PerGameProfileToggle;
                }
                else
                {
                    // No DGP, no game: Nav -> Overlay (skip disabled PerGameProfile)
                    PerformanceNavItem.XYFocusDown = PerformanceOverlayComboBox;
                    PerformanceOverlayComboBox.XYFocusUp = PerformanceNavItem;
                }
            }
        }

        /// <summary>
        /// Updates the Default Game Profile card display with profile settings.
        /// </summary>
        private void UpdateDefaultProfileDisplay(Shared.Data.DefaultGameProfile? profile)
        {
            if (profile.HasValue)
            {
                var p = profile.Value;

                // Store current profile first (needed by UpdateDefaultProfileGameIcon)
                currentDefaultGameProfile = p;

                // Update game name
                if (DefaultProfileGameName != null)
                {
                    DefaultProfileGameName.Text = p.GameName ?? "";
                }

                // Update game icon from Steam CDN if available
                UpdateDefaultProfileGameIcon();

                // Update settings text
                if (DefaultProfileSettingsText != null)
                {
                    var settings = new System.Collections.Generic.List<string>();

                    settings.Add($"{p.TDP}W");

                    if (p.FrameCap.HasValue && p.FrameCap.Value > 0)
                    {
                        settings.Add($"{p.FrameCap.Value}fps");
                    }

                    if (!string.IsNullOrEmpty(p.ResolutionCap))
                    {
                        settings.Add(p.ResolutionCap);
                    }

                    DefaultProfileSettingsText.Text = string.Join(" - ", settings);
                    Logger.Info($"Default Game Profile display updated: {DefaultProfileSettingsText.Text}");
                }

                // Update "Optimizing for Z2/Z1 Extreme" text based on hardware model
                if (DefaultProfileOptimizingText != null && DefaultProfileSeparator != null)
                {
                    string optimizingText = "Optimizing for your device";
                    if (!string.IsNullOrEmpty(p.HardwareModel))
                    {
                        if (p.HardwareModel == "HORSEM4N")
                        {
                            optimizingText = "Optimizing for Z2 Extreme";
                        }
                        else if (p.HardwareModel == "OMNI")
                        {
                            optimizingText = "Optimizing for Z1 Extreme";
                        }
                    }
                    DefaultProfileOptimizingText.Text = optimizingText;
                    DefaultProfileOptimizingText.Visibility = Visibility.Visible;
                    DefaultProfileSeparator.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // Hide elements when no profile
                if (DefaultProfileOptimizingText != null)
                {
                    DefaultProfileOptimizingText.Visibility = Visibility.Collapsed;
                }
                if (DefaultProfileSeparator != null)
                {
                    DefaultProfileSeparator.Visibility = Visibility.Collapsed;
                }
                if (DefaultProfileGameName != null)
                {
                    DefaultProfileGameName.Text = "";
                }
                currentDefaultGameProfile = null;
            }
        }

        /// <summary>
        /// Updates the game icon in the Default Game Profile card.
        /// Loads icon from Steam's local cache for Steam games.
        /// </summary>
        private async void UpdateDefaultProfileGameIcon()
        {
            if (DefaultProfileGameIcon == null)
                return;

            // Try to load Steam icon if we have a Steam App ID
            if (currentDefaultGameProfile.HasValue)
            {
                var iconPath = currentDefaultGameProfile.Value.GetSteamIconPath();
                if (!string.IsNullOrEmpty(iconPath))
                {
                    try
                    {
                        // Load from local file using StorageFile for UWP compatibility
                        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(iconPath);
                        using (var stream = await file.OpenReadAsync())
                        {
                            var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            await bitmap.SetSourceAsync(stream);
                            DefaultProfileGameIcon.Source = bitmap;
                            DefaultProfileGameIcon.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Failed to load Steam icon from {iconPath}: {ex.Message}");
                    }
                }
            }

            // Hide the icon if no Steam icon available
            DefaultProfileGameIcon.Visibility = Visibility.Collapsed;
        }

        // Cached default game profile for UI state management
        private Shared.Data.DefaultGameProfile? currentDefaultGameProfile;

        /// <summary>
        /// Called when the Default Game Profile enabled state changes.
        /// Greys out TDP controls and syncs FPS limit when enabled.
        /// </summary>
        private void OnDefaultProfileEnabledChanged(bool enabled)
        {
            Logger.Info($"Default Game Profile enabled changed to: {enabled}");

            // IMPORTANT: When DISABLING, load the appropriate profile for current power state!
            // Don't restore saved values - they may be from a different power state (e.g., DC when now on AC)
            // Set flag to suppress profile saves during restoration (toggle handlers would otherwise save wrong values)
            if (!enabled)
            {
                isRestoringFromDefaultProfile = true;
                try
                {
                    // Clear saved state - we'll load from profile instead of restoring
                    // FPS limit and TDP can differ between AC/DC profiles, so restoring pre-DGP state is wrong
                    originalFpsLimitToggleState = null;
                    originalFpsLimitSliderValue = null;
                    originalTdpSliderValue = null;

                    // Load the appropriate profile for current power state
                    // This ensures AC profile is loaded when on AC, DC profile when on DC
                    // Profile loading handles TDP, FPS limit, and all other settings
                    string targetProfile = GetTargetProfileName();
                    Logger.Info($"DGP disabled - loading profile for current power state: {targetProfile}");
                    LoadProfileSettings(targetProfile, isExplicitSwitch: false);
                }
                finally
                {
                    isRestoringFromDefaultProfile = false;
                }
            }

            // Update TDP controls enabled state (now with correct values if disabling)
            UpdateTDPControlsForDefaultProfile(enabled);

            // Update per-game profile toggle state
            UpdatePerGameProfileForDefaultProfile(enabled);

            if (enabled && currentDefaultGameProfile.HasValue)
            {
                var profile = currentDefaultGameProfile.Value;

                // Save original FPS limit state before changing
                if (FPSLimitToggle != null && !originalFpsLimitToggleState.HasValue)
                {
                    originalFpsLimitToggleState = FPSLimitToggle.IsOn;
                    originalFpsLimitSliderValue = FPSLimitSlider?.Value ?? 60;
                    Logger.Info($"Saved original FPS limit state: toggle={originalFpsLimitToggleState}, value={originalFpsLimitSliderValue}");
                }

                // Save original TDP slider value before changing
                if (TDPSlider != null && !originalTdpSliderValue.HasValue)
                {
                    originalTdpSliderValue = TDPSlider.Value;
                    Logger.Info($"Saved original TDP slider value: {originalTdpSliderValue}W");
                }

                // Sync FPS limit toggle and slider to match profile
                if (profile.FrameCap.HasValue && profile.FrameCap.Value > 0)
                {
                    if (FPSLimitToggle != null)
                    {
                        FPSLimitToggle.IsOn = true;
                    }
                    if (FPSLimitSlider != null)
                    {
                        FPSLimitSlider.Value = profile.FrameCap.Value;
                    }
                    Logger.Info($"FPS limit synced to default profile: {profile.FrameCap.Value}fps");
                }

                // Sync TDP slider to match profile
                if (profile.TDP > 0 && TDPSlider != null)
                {
                    TDPSlider.Value = profile.TDP;
                    Logger.Info($"TDP slider synced to default profile: {profile.TDP}W");
                }
            }

            // Update Quick tab tile styling
            UpdateQuickSettingsTileStates();

            // Update XY navigation for controller support
            UpdatePerformanceTabXYNavigation();
        }

        // Store original state for restoration when default profile is disabled
        private bool? originalFpsLimitToggleState;
        private double? originalFpsLimitSliderValue;
        private double? originalTdpSliderValue;
        private bool isRestoringFromDefaultProfile; // Flag to suppress profile saves during DGP restoration

        /// <summary>
        /// Updates per-game profile toggle state based on Default Game Profile.
        /// </summary>
        private void UpdatePerGameProfileForDefaultProfile(bool defaultProfileEnabled)
        {
            if (defaultProfileEnabled)
            {
                // Hide the Active Profile card when default game profile is enabled
                if (ActiveProfileCard != null)
                {
                    ActiveProfileCard.Visibility = Visibility.Collapsed;
                }

                Logger.Debug("Active Profile card hidden - Default Game Profile is active");
            }
            else
            {
                // Show the Active Profile card when default game profile is disabled
                if (ActiveProfileCard != null)
                {
                    ActiveProfileCard.Visibility = Visibility.Visible;
                }

                // Re-enable the per-game profile toggle
                if (PerGameProfileToggle != null)
                {
                    PerGameProfileToggle.IsEnabled = runningGame?.Value.IsValid() == true;
                }

                Logger.Debug("Active Profile card shown - Default Game Profile is inactive");
            }
        }

        /// <summary>
        /// Updates TDP control enabled states based on Default Game Profile.
        /// </summary>
        private void UpdateTDPControlsForDefaultProfile(bool defaultProfileEnabled)
        {
            if (defaultProfileEnabled)
            {
                // Disable TDP controls when default profile is active
                if (TDPModeComboBox != null)
                {
                    TDPModeComboBox.IsEnabled = false;
                }
                if (TDPSlider != null)
                {
                    TDPSlider.IsEnabled = false;
                }
                if (TDPBoostToggle != null)
                {
                    TDPBoostToggle.IsEnabled = false;
                }
                if (AutoTDPToggle != null)
                {
                    AutoTDPToggle.IsEnabled = false;
                }
                if (StickyTDPToggle != null)
                {
                    StickyTDPToggle.IsEnabled = false;
                }

                // Also disable FPS limit controls (controlled by Default Game Profile)
                if (FPSLimitToggle != null)
                {
                    FPSLimitToggle.IsEnabled = false;
                }
                if (FPSLimitSlider != null)
                {
                    FPSLimitSlider.IsEnabled = false;
                }

                Logger.Debug("TDP and FPS controls disabled - Default Game Profile is active");
            }
            else
            {
                // Re-enable TDP controls based on current mode
                if (TDPModeComboBox != null)
                {
                    TDPModeComboBox.IsEnabled = true;
                }

                // Re-enable FPS limit controls
                if (FPSLimitToggle != null)
                {
                    FPSLimitToggle.IsEnabled = true;
                }
                if (FPSLimitSlider != null)
                {
                    FPSLimitSlider.IsEnabled = true;
                }

                // Re-evaluate other controls based on current TDP mode
                UpdateTDPSliderEnabledState();

                Logger.Debug("TDP and FPS controls re-enabled - Default Game Profile is inactive");
            }
        }

        /// <summary>
        /// Updates WinRing0 option visibility in TDP Method dropdown based on file availability.
        /// </summary>
        private void UpdateWinRing0Visibility(bool available)
        {
            if (TdpMethodWinRing0Item != null)
            {
                TdpMethodWinRing0Item.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"WinRing0 TDP option visibility set to: {available}");

                // If WinRing0 was selected but is no longer available, switch to WMI or PawnIO
                if (!available && TdpMethodComboBox != null)
                {
                    var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is string tag && tag == "WinRing0")
                    {
                        // Try to select ManufacturerWMI first, then PawnIO
                        if (TdpMethodWmiItem?.Visibility == Visibility.Visible)
                        {
                            TdpMethodComboBox.SelectedItem = TdpMethodWmiItem;
                        }
                        else if (TdpMethodPawnIOItem?.Visibility == Visibility.Visible)
                        {
                            TdpMethodComboBox.SelectedItem = TdpMethodPawnIOItem;
                        }
                    }
                }
            }

            // Ensure a valid option is selected after visibility changes
            EnsureValidTdpMethodSelected();
        }

        /// <summary>
        /// Ensures a valid (visible and enabled) TDP method is selected in the dropdown.
        /// IMPORTANT: Never auto-select WinRing0 - user must explicitly choose it.
        /// </summary>
        private void EnsureValidTdpMethodSelected()
        {
            if (TdpMethodComboBox == null) return;

            var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
            var selectedIndex = TdpMethodComboBox.SelectedIndex;

            // If current selection is valid (visible and enabled), do nothing
            if (selectedItem != null && selectedItem.Visibility == Visibility.Visible && selectedItem.IsEnabled)
            {
                return;
            }

            // If ManufacturerWMI is selected but collapsed, wait for Legion detection
            // Don't auto-select PawnIO - Legion detection will make WMI visible if it's a Legion device
            if (selectedItem != null && selectedItem == TdpMethodWmiItem && selectedItem.Visibility == Visibility.Collapsed)
            {
                Logger.Debug("EnsureValidTdpMethodSelected: ManufacturerWMI selected but collapsed, waiting for Legion detection");
                return;
            }

            // If selectedIndex is 0 (ManufacturerWMI position) but selectedItem isn't matching,
            // it means WMI was intended but may be collapsed - wait for Legion detection
            if (selectedIndex == 0 && TdpMethodWmiItem?.Visibility == Visibility.Collapsed)
            {
                Logger.Debug("EnsureValidTdpMethodSelected: SelectedIndex=0 (WMI) but WMI collapsed, waiting for Legion detection");
                return;
            }

            // If nothing is selected yet and WMI is collapsed, wait for Legion detection
            // This handles the case where the ComboBox rejected the initial Collapsed selection
            if (selectedItem == null && TdpMethodWmiItem?.Visibility == Visibility.Collapsed)
            {
                Logger.Debug("EnsureValidTdpMethodSelected: No selection and WMI collapsed, waiting for Legion detection");
                return;
            }

            // Find the first visible and enabled option and select it
            // Priority: ManufacturerWMI > PawnIO (if installed)
            // NEVER auto-select WinRing0 - it's a legacy option that may trigger anti-cheat
            if (TdpMethodWmiItem?.Visibility == Visibility.Visible && TdpMethodWmiItem.IsEnabled)
            {
                TdpMethodComboBox.SelectedItem = TdpMethodWmiItem;
                Logger.Info("TDP Method auto-selected: ManufacturerWMI");
            }
            else if (TdpMethodPawnIOItem?.Visibility == Visibility.Visible && TdpMethodPawnIOItem.IsEnabled)
            {
                TdpMethodComboBox.SelectedItem = TdpMethodPawnIOItem;
                Logger.Info("TDP Method auto-selected: PawnIO");
            }
            else
            {
                // Don't auto-select WinRing0 - user must explicitly choose it
                Logger.Warn("No safe TDP method available - user must select WinRing0 manually if desired");
            }
        }

        /// <summary>
        /// Updates the PawnIO install button state and dropdown option based on driver installation status.
        /// PawnIO option is always visible but disabled if not installed.
        /// </summary>
        private void UpdatePawnIOInstalledUI(bool installed)
        {
            // PawnIO option is always visible, but enable/disable based on installation status
            // This prevents WinRing0 from being auto-selected when PawnIO detection is delayed
            if (TdpMethodPawnIOItem != null)
            {
                // Keep PawnIO visible always - just update text to show status
                TdpMethodPawnIOItem.Visibility = Visibility.Visible;
                TdpMethodPawnIOItem.IsEnabled = installed;
                TdpMethodPawnIOItem.Content = installed ? "PawnIO" : "PawnIO (Not Installed)";
                Logger.Info($"PawnIO TDP option enabled: {installed}");

                // If PawnIO was selected but is no longer installed, switch to WMI only
                // NEVER auto-switch to WinRing0 - user must explicitly choose it
                if (!installed && TdpMethodComboBox != null)
                {
                    var selectedItem = TdpMethodComboBox.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is string tag && tag == "PawnIO")
                    {
                        // Try to select ManufacturerWMI, don't fall back to WinRing0
                        if (TdpMethodWmiItem?.Visibility == Visibility.Visible)
                        {
                            TdpMethodComboBox.SelectedItem = TdpMethodWmiItem;
                        }
                        // If WMI not available, leave selection as-is or clear it
                        // User will need to reinstall PawnIO or manually select WinRing0
                    }
                }
            }

            if (InstallPawnIOButton != null)
            {
                InstallPawnIOButton.Content = installed ? "Installed" : "Install";
                InstallPawnIOButton.IsEnabled = !installed;
                Logger.Info($"PawnIO install button updated: installed={installed}");

                // Update XY navigation to skip disabled button
                if (TdpMethodComboBox != null && TDPLimitsExpandButton != null)
                {
                    if (installed)
                    {
                        TdpMethodComboBox.XYFocusDown = TDPLimitsExpandButton;
                        TDPLimitsExpandButton.XYFocusUp = TdpMethodComboBox;
                    }
                    else
                    {
                        TdpMethodComboBox.XYFocusDown = InstallPawnIOButton;
                        TDPLimitsExpandButton.XYFocusUp = InstallPawnIOButton;
                    }
                }
            }

            if (PawnIOStatusText != null)
            {
                if (installed)
                {
                    PawnIOStatusText.Text = "PawnIO driver is installed. Signed kernel driver for anti-cheat safe hardware access.";
                }
                else
                {
                    PawnIOStatusText.Text = "Signed kernel driver for hardware access. Replaces WinRing0.";
                }
            }

            // Ensure a valid option is selected after visibility changes
            EnsureValidTdpMethodSelected();
        }

        /// <summary>
        /// Handles the PawnIO install button click.
        /// After installation, the helper restarts to reinitialize with PawnIO support.
        /// </summary>
        private async void InstallPawnIOButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("InstallPawnIOButton clicked - triggering PawnIO installation");

                // Update button to show installing state
                if (InstallPawnIOButton != null)
                {
                    InstallPawnIOButton.Content = "Installing...";
                    InstallPawnIOButton.IsEnabled = false;
                }

                // Trigger the installation via the property
                installPawnIO?.TriggerInstall();

                // Wait for helper to complete installation and exit
                // The helper exits after successful PawnIO installation
                Logger.Info("Waiting for PawnIO installation to complete...");
                await Task.Delay(5000);

                // Check if helper is still connected, if not, relaunch it
                // The helper will have exited after successful installation
                Logger.Info("Relaunching helper after PawnIO installation...");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();

                // Wait for helper to start and reinitialize
                await Task.Delay(2000);
                Logger.Info("Helper relaunched after PawnIO installation");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during PawnIO installation: {ex.Message}");
                // Reset button state on error
                if (InstallPawnIOButton != null)
                {
                    InstallPawnIOButton.Content = "Install";
                    InstallPawnIOButton.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// Updates the ViGEmBus install button state based on driver installation status.
        /// </summary>
        private void UpdateViGEmBusInstalledUI(bool installed)
        {
            if (ViGEmBusStatusText != null)
            {
                ViGEmBusStatusText.Text = installed ? "Status: Installed" : "Status: Not Installed";
                ViGEmBusStatusText.Foreground = installed
                    ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LimeGreen)
                    : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
            }

            if (ViGEmBusInstallButton != null)
            {
                ViGEmBusInstallButton.Content = installed ? "Installed" : "Install ViGEmBus";
                ViGEmBusInstallButton.IsEnabled = !installed;
            }

            Logger.Info($"ViGEmBus install UI updated: installed={installed}");
        }

        /// <summary>
        /// Handles the ViGEmBus install button click.
        /// </summary>
        private async void ViGEmBusInstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("ViGEmBusInstallButton clicked - triggering ViGEmBus installation");

                // Update button to show installing state
                if (ViGEmBusInstallButton != null)
                {
                    ViGEmBusInstallButton.Content = "Installing...";
                    ViGEmBusInstallButton.IsEnabled = false;
                }

                if (ViGEmBusStatusText != null)
                {
                    ViGEmBusStatusText.Text = "Status: Installing...";
                }

                // Trigger the installation via the property
                installViGEmBus?.TriggerInstall();

                // The helper will send an updated status after installation completes
                Logger.Info("ViGEmBus installation triggered, waiting for helper response...");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during ViGEmBus installation: {ex.Message}");
                // Reset button state on error
                if (ViGEmBusInstallButton != null)
                {
                    ViGEmBusInstallButton.Content = "Install ViGEmBus";
                    ViGEmBusInstallButton.IsEnabled = true;
                }
                if (ViGEmBusStatusText != null)
                {
                    ViGEmBusStatusText.Text = "Status: Error";
                }
            }
        }

        /// <summary>
        /// Shows or hides the Custom TDP card based on performance mode
        /// </summary>
        private void SetCustomTDPVisibility(bool visible)
        {
            if (LegionCustomTDPCard != null)
            {
                LegionCustomTDPCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Custom TDP card visibility set to: {visible}");
            }

            // Update XY focus navigation for controller navigation
            // When Custom TDP card is hidden, focus should skip directly to fan controls
            if (LegionPerformanceModeComboBox != null)
            {
                if (visible && LegionCustomTDPSlowSlider != null)
                {
                    LegionPerformanceModeComboBox.XYFocusDown = LegionCustomTDPSlowSlider;
                }
                else if (LegionFanFullSpeedToggle != null)
                {
                    LegionPerformanceModeComboBox.XYFocusDown = LegionFanFullSpeedToggle;
                }
            }

            // Also update XYFocusUp on fan controls for navigation back up
            if (LegionFanFullSpeedToggle != null)
            {
                if (visible && LegionCustomTDPPeakSlider != null)
                {
                    // When Custom TDP visible, navigate up to the last slider (Peak/Sustained)
                    LegionFanFullSpeedToggle.XYFocusUp = LegionCustomTDPPeakSlider;
                }
                else if (LegionPerformanceModeComboBox != null)
                {
                    // When Custom TDP hidden, navigate up directly to performance mode dropdown
                    LegionFanFullSpeedToggle.XYFocusUp = LegionPerformanceModeComboBox;
                }
            }
        }

        /// <summary>
        /// Toggles the ColorPicker visibility
        /// </summary>
        private void LegionColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LegionColorPicker != null)
                {
                    bool isExpanded = LegionColorPicker.Visibility == Visibility.Visible;
                    LegionColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    // Update button icon (chevron down/up)
                    if (LegionColorExpandButton != null)
                    {
                        LegionColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionColorExpandButton_Click: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles ColorPicker color changes and updates the preview
        /// </summary>
        private void LegionColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            try
            {
                // Update color preview
                if (LegionColorPreview != null)
                {
                    LegionColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                legionLightColor?.OnColorChanged(args.NewColor);

                // Save to controller profile (handler is detached during profile loading)
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionColorPicker_ColorChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles brightness slider changes
        /// </summary>
        private void LegionBrightnessSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (LegionBrightnessSlider != null && LegionBrightnessValue != null)
                {
                    int brightness = (int)LegionBrightnessSlider.Value;
                    LegionBrightnessValue.Text = $"{brightness}%";
                }
                // Save to controller profile
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionBrightnessSlider_ValueChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles speed slider changes
        /// </summary>
        private void LegionSpeedSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (LegionSpeedSlider != null && LegionSpeedValue != null)
                {
                    int speed = (int)LegionSpeedSlider.Value;
                    LegionSpeedValue.Text = $"{speed}%";
                }

                // Save to controller profile (ControllerSettingChanged checks for loading state)
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionSpeedSlider_ValueChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles light mode ComboBox selection - shows/hides appropriate controls
        /// Mode options visibility:
        /// - Off (0): hide all
        /// - Solid (1): Color + Brightness
        /// - Pulse (2): Color + Speed
        /// - Dynamic (3): Brightness + Speed
        /// - Spiral (4): Brightness + Speed
        /// </summary>
        private void LegionLightModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                UpdateLegionLightControlsVisibility();

                // Save to controller profile (handler is detached during profile loading)
                ControllerSettingChanged(sender, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionLightModeComboBox_SelectionChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the visibility of Legion light controls based on the selected mode
        /// </summary>
        private void UpdateLegionLightControlsVisibility()
        {
            if (LegionLightModeComboBox == null || LegionColorCard == null ||
                LegionBrightnessCard == null || LegionSpeedCard == null)
                return;

            int mode = LegionLightModeComboBox.SelectedIndex;

            // Off (0): hide all
            // Solid (1): Color + Brightness
            // Pulse (2): Color + Brightness + Speed
            // Dynamic (3): Brightness + Speed
            // Spiral (4): Brightness + Speed

            bool showColor = mode == 1 || mode == 2; // Solid, Pulse
            bool showBrightness = mode >= 1; // All modes except Off have brightness
            bool showSpeed = mode == 2 || mode == 3 || mode == 4; // Pulse, Dynamic, Spiral

            LegionColorCard.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
            LegionBrightnessCard.Visibility = showBrightness ? Visibility.Visible : Visibility.Collapsed;
            LegionSpeedCard.Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed;

            Logger.Info($"Legion light mode {mode}: Color={showColor}, Brightness={showBrightness}, Speed={showSpeed}");
        }

        /// <summary>
        /// Handles performance mode ComboBox selection in Legion tab
        /// </summary>
        private void LegionPerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Property handles the update, just log here
            Logger.Info($"Legion Performance mode selection changed");

            // Sync TDP Mode dropdown in Performance tab
            // Skip during initial sync - ApplyProfileTDPToHelper will set the correct value
            if (TDPModeComboBox != null && LegionPerformanceModeComboBox != null && !isInitialSync)
            {
                if (TDPModeComboBox.SelectedIndex != LegionPerformanceModeComboBox.SelectedIndex)
                {
                    TDPModeComboBox.SelectedIndex = LegionPerformanceModeComboBox.SelectedIndex;
                }
            }

            // Update TDP slider enabled state based on mode
            UpdateTDPSliderEnabledState();
        }

        /// <summary>
        /// Handles TDP Mode ComboBox selection in Performance tab (Legion devices only)
        /// </summary>
        private int lastTDPModeIndex = 1; // Track last index to avoid redundant updates (init to XAML default: Balanced)
        private void TDPModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TDPModeComboBox == null) return;

            // Skip during initialization or profile loading to prevent cycling
            if (isInitialSync || isLoadingProfile) return;

            // Get the selected mode value from tag
            int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom
            int selectedIndex = TDPModeComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= modeValues.Length) return;

            // Skip if this is the same index as last time (avoid redundant processing)
            if (selectedIndex == lastTDPModeIndex) return;
            lastTDPModeIndex = selectedIndex;

            int modeValue = modeValues[selectedIndex];
            Logger.Info($"TDP Mode selection changed to index {selectedIndex} (value {modeValue})");

            // Sync with Legion Performance Mode ComboBox and property
            if (LegionPerformanceModeComboBox != null && LegionPerformanceModeComboBox.SelectedIndex != selectedIndex)
            {
                LegionPerformanceModeComboBox.SelectedIndex = selectedIndex;
            }

            // Send to helper via the Legion Performance Mode property (only if value changed)
            if (legionPerformanceMode != null && legionPerformanceMode.Value != modeValue)
            {
                legionPerformanceMode.SetValue(modeValue);
            }

            // Update TDP slider enabled state based on mode
            UpdateTDPSliderEnabledState();

            // Save profile when TDP Mode changes (if not during initialization or helper update)
            // Allow save if user-initiated from Quick Tab tile (bypasses isApplyingHelperUpdate)
            // Don't save when Default Game Profile is active (to avoid contaminating user's profile)
            if (!isInitialSync && !isLoadingProfile && SaveTDP && (!isApplyingHelperUpdate || isUserInitiatedTDPModeChange) && defaultGameProfileEnabled?.Value != true)
            {
                Logger.Info($"Saving TDP Mode change to profile: {currentProfileName}");
                SaveCurrentSettingsToProfile(currentProfileName);
            }
            else
            {
                Logger.Warn($"TDP Mode save skipped: isInitialSync={isInitialSync}, isApplyingHelperUpdate={isApplyingHelperUpdate}, isLoadingProfile={isLoadingProfile}, SaveTDP={SaveTDP}, isUserInitiatedTDPModeChange={isUserInitiatedTDPModeChange}, defaultGameProfile={defaultGameProfileEnabled?.Value}");
            }
        }

        /// <summary>
        /// Updates TDP slider enabled state based on TDP Mode (Legion only: disabled when not Custom)
        /// Also updates XY focus bindings to skip disabled TDP slider
        /// </summary>
        private void UpdateTDPSliderEnabledState()
        {
            if (TDPSlider == null) return;

            // Only apply this logic for Legion devices
            if (legionGoDetected?.Value != true) return;

            // If Default Game Profile is active, keep TDP controls disabled
            if (defaultGameProfileEnabled?.Value == true)
            {
                Logger.Debug("TDP slider state update skipped - Default Game Profile is active");
                return;
            }

            // Check if in Custom mode (index 3 = Custom = 255)
            bool isCustomMode = TDPModeComboBox?.SelectedIndex == 3;

            // TDP slider, TDP Boost, and AutoTDP should only be enabled in Custom mode for Legion devices
            // Note: TDP slider also requires tdp property to be ready (IsEnabled is set elsewhere too)
            if (!isCustomMode)
            {
                TDPSlider.IsEnabled = false;

                // Set flag to prevent toggle handlers from saving forced-off state to LocalSettings
                isUpdatingTDPMode = true;
                try
                {
                    // Also disable TDP Boost and AutoTDP controls in preset modes
                    if (TDPBoostToggle != null)
                    {
                        TDPBoostToggle.IsEnabled = false;
                        TDPBoostToggle.IsOn = false; // Turn off when switching to preset mode
                    }
                    if (TDPBoostContent != null)
                    {
                        TDPBoostContent.Visibility = Visibility.Collapsed;
                    }
                    if (AutoTDPToggle != null)
                    {
                        AutoTDPToggle.IsEnabled = false;
                        AutoTDPToggle.IsOn = false; // Turn off when switching to preset mode
                    }
                    if (AutoTDPTargetFPSSlider != null) AutoTDPTargetFPSSlider.IsEnabled = false;
                    if (AutoTDPMinSlider != null) AutoTDPMinSlider.IsEnabled = false;
                    if (AutoTDPMaxSlider != null) AutoTDPMaxSlider.IsEnabled = false;
                    if (StickyTDPToggle != null)
                    {
                        StickyTDPToggle.IsEnabled = false;
                        StickyTDPToggle.IsOn = false; // Turn off when switching to preset mode
                    }
                    if (StickyTDPIntervalSlider != null) StickyTDPIntervalSlider.IsEnabled = false;
                }
                finally
                {
                    isUpdatingTDPMode = false;
                }

                // Update XY focus to skip disabled controls
                // TDPModeComboBox -> OSPowerModeComboBox (skip all TDP controls)
                if (TDPModeComboBox != null && OSPowerModeComboBox != null)
                {
                    TDPModeComboBox.XYFocusDown = OSPowerModeComboBox;
                    OSPowerModeComboBox.XYFocusUp = TDPModeComboBox;
                }

                Logger.Debug("TDP slider, TDP Boost, AutoTDP, and Sticky TDP disabled - not in Custom mode");
            }
            else
            {
                // In Custom mode, enable if tdp property is ready
                TDPSlider.IsEnabled = tdp != null;

                // Re-enable TDP Boost, AutoTDP, and Sticky TDP controls in Custom mode
                if (TDPBoostToggle != null) TDPBoostToggle.IsEnabled = true;
                if (AutoTDPToggle != null) AutoTDPToggle.IsEnabled = true;
                if (AutoTDPTargetFPSSlider != null) AutoTDPTargetFPSSlider.IsEnabled = true;
                if (AutoTDPMinSlider != null) AutoTDPMinSlider.IsEnabled = true;
                if (AutoTDPMaxSlider != null) AutoTDPMaxSlider.IsEnabled = true;
                if (StickyTDPToggle != null) StickyTDPToggle.IsEnabled = true;
                if (StickyTDPIntervalSlider != null) StickyTDPIntervalSlider.IsEnabled = true;

                // Restore toggle states from LocalSettings (they were turned off when not in Custom mode)
                // Use flag to prevent toggle handlers from re-saving the restored values
                isUpdatingTDPMode = true;
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;

                    if (TDPBoostToggle != null && settings.Values.TryGetValue("TDPBoostEnabled", out object tdpBoostVal) && tdpBoostVal is bool tdpBoostEnabledVal)
                    {
                        TDPBoostToggle.IsOn = tdpBoostEnabledVal;
                        this.tdpBoostEnabled?.SetValue(tdpBoostEnabledVal); // Send to helper
                        if (tdpBoostEnabledVal && TDPBoostContent != null)
                        {
                            TDPBoostContent.Visibility = Visibility.Visible;
                        }
                        Logger.Debug($"Restored TDP Boost toggle state from LocalSettings: {tdpBoostEnabledVal}");
                    }

                    if (AutoTDPToggle != null && settings.Values.TryGetValue("AutoTDPEnabled", out object autoTdpVal) && autoTdpVal is bool autoTdpEnabled)
                    {
                        isLoadingAutoTDPSettings = true;
                        try
                        {
                            AutoTDPToggle.IsOn = autoTdpEnabled;
                            // NOTE: Do NOT send to helper - helper is source of truth for profile values
                            Logger.Debug($"Restored AutoTDP toggle state from LocalSettings: {autoTdpEnabled}");
                        }
                        finally
                        {
                            isLoadingAutoTDPSettings = false;
                        }
                    }

                    if (StickyTDPToggle != null && settings.Values.TryGetValue("StickyTDPEnabled", out object stickyVal) && stickyVal is bool stickyEnabled)
                    {
                        StickyTDPToggle.IsOn = stickyEnabled;
                        // Start/stop Sticky TDP timer based on restored state
                        if (stickyEnabled)
                        {
                            targetTDPLimit = TDPSlider.Value;
                            StartStickyTDPTimer();
                            Logger.Debug($"Restored Sticky TDP enabled - monitoring TDP: {targetTDPLimit}W");
                        }
                        else
                        {
                            StopStickyTDPTimer();
                        }
                        Logger.Debug($"Restored Sticky TDP toggle state from LocalSettings: {stickyEnabled}");
                    }
                }
                finally
                {
                    isUpdatingTDPMode = false;
                }

                Logger.Debug($"TDP slider, TDP Boost, AutoTDP, and Sticky TDP enabled in Custom mode: {TDPSlider.IsEnabled}");

                // CRITICAL FIX: Sync TDPProperty.Value with the slider's current visual value
                // When TDP sync is skipped (preset modes), TDPProperty.Value stays at initial value (4).
                // Profile loads set TDPSlider.Value but not TDPProperty.Value.
                // Without this sync, Slider_ValueChanged comparison (newValue != Value) fails
                // because the property's Value doesn't match what the user sees on screen.
                if (tdp != null)
                {
                    int currentSliderValue = (int)TDPSlider.Value;
                    tdp.StopDebounceTimer(); // Cancel any pending debounce
                    tdp.SetValueSilent(currentSliderValue); // Update internal Value without sending

                    // Also send current value to helper to ensure hardware matches UI
                    tdp.ForceSetValue(currentSliderValue);
                    Logger.Info($"Custom mode enabled - synced TDP property to slider value: {currentSliderValue}W");
                }

                // Restore normal XY focus chain in Custom mode
                // TDPModeComboBox -> TDPSlider -> TDPBoostToggle -> AutoTDPToggle -> StickyTDPToggle -> OSPowerModeComboBox
                if (TDPModeComboBox != null && OSPowerModeComboBox != null)
                {
                    TDPModeComboBox.XYFocusDown = TDPSlider;
                    TDPSlider.XYFocusUp = TDPModeComboBox;
                    TDPSlider.XYFocusDown = TDPBoostToggle;
                    TDPBoostToggle.XYFocusUp = TDPSlider;
                    TDPBoostToggle.XYFocusDown = AutoTDPToggle;
                    AutoTDPToggle.XYFocusUp = TDPBoostToggle;
                    AutoTDPToggle.XYFocusDown = StickyTDPToggle;
                    StickyTDPToggle.XYFocusUp = AutoTDPToggle;
                    StickyTDPToggle.XYFocusDown = OSPowerModeComboBox;
                    OSPowerModeComboBox.XYFocusUp = StickyTDPToggle;
                    Logger.Debug("XY focus restored for Custom mode");
                }
            }
        }

        /// <summary>
        /// Handles Custom TDP slider changes and updates the value labels
        /// Note: The actual value sync is handled by WidgetSliderProperty's built-in debounce
        /// </summary>
        private void LegionCustomTDPSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                // Update value labels immediately for visual feedback
                if (LegionCustomTDPSlowSlider != null && LegionCustomTDPSlowValue != null)
                {
                    LegionCustomTDPSlowValue.Text = $"{(int)LegionCustomTDPSlowSlider.Value}W";
                }
                if (LegionCustomTDPFastSlider != null && LegionCustomTDPFastValue != null)
                {
                    LegionCustomTDPFastValue.Text = $"{(int)LegionCustomTDPFastSlider.Value}W";
                }
                if (LegionCustomTDPPeakSlider != null && LegionCustomTDPPeakValue != null)
                {
                    LegionCustomTDPPeakValue.Text = $"{(int)LegionCustomTDPPeakSlider.Value}W";
                }
                // The WidgetSliderProperty handles debouncing and sending to helper
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in LegionCustomTDPSlider_ValueChanged: {ex.Message}");
            }
        }

        #endregion

        #region Quick Settings

        // Tile brushes
        private SolidColorBrush tileOffBrush;
        private SolidColorBrush tileOnBrush;
        private SolidColorBrush tileActiveBrush;
        private SolidColorBrush tileTriggerBrush;
        private LinearGradientBrush tileDefaultProfileBrush;
        private bool quickSettingsInitialized = false;

        // Tile definitions with visibility tracking
        private class TileDefinition
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Glyph { get; set; }
            public bool IsVisible { get; set; } = true;
            public bool IsTrigger { get; set; } = false;  // True for tiles that trigger actions (keyboard, custom shortcuts)
            public bool IsAction { get; set; } = false;   // True for action tiles (Task Manager, Explorer, etc.) - shown at bottom
            public string CustomShortcut { get; set; }    // For custom shortcut tiles
            public int Order { get; set; } = 0;           // Display order (lower = first)
            public Button TileButton { get; set; }
            public TextBlock StateText { get; set; }
            public CheckBox VisibilityCheckBox { get; set; }

            // For scrolling text animation (Profile tile)
            public Canvas StateTextCanvas { get; set; }
            public TranslateTransform StateTextTransform { get; set; }
            public Storyboard ScrollStoryboard { get; set; }
        }

        // List of custom shortcut tiles
        private List<TileDefinition> qsCustomShortcuts = new List<TileDefinition>();

        private List<TileDefinition> qsTileDefinitions = new List<TileDefinition>();
        private Dictionary<string, TileDefinition> qsTileMap = new Dictionary<string, TileDefinition>();

        // Edit mode state for tile customization
        private bool qsEditMode = false;
        private TileDefinition qsSelectedTileForMove = null;

        // Column count setting (3 or 4 columns)
        private int qsColumnCount = 4;

        // Timer for TDP reapply when switching to Custom mode
        private Windows.UI.Xaml.DispatcherTimer qsTdpReapplyTimer;

        /// <summary>
        /// Initialize Quick Settings resources and build tiles
        /// </summary>
        private void InitializeQuickSettings()
        {
            if (quickSettingsInitialized) return;

            try
            {
                // Clear any stale state from previous initialization attempts
                // This ensures fresh state when widget is reloaded
                qsTileDefinitions.Clear();
                qsTileMap.Clear();
                qsCustomShortcuts.Clear();
                qsEditMode = false;
                qsSelectedTileForMove = null;

                // Dark mode colors with sharp contrast for handheld devices
                // On state: dark green
                tileOnBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 46, 31));    // #1A2E1F

                // Other tile brushes - dark mode
                tileOffBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));   // #1A1C1E
                tileActiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 37, 48)); // #1A2530 - dark blue
                tileTriggerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 32, 48)); // #252030 - dark purple

                // Default Game Profile gradient brush (matches Performance tab card)
                tileDefaultProfileBrush = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0xC0, 0x40, 0x40), Offset = 0.0 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0x40, 0x80, 0x50), Offset = 0.35 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0x40, 0x50, 0x80), Offset = 0.65 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(0x40, 0x80, 0x40, 0x60), Offset = 1.0 }
                    }
                };

                // Define all tiles
                DefineQuickSettingsTiles();

                // Load visibility settings from storage
                LoadQuickSettingsConfig();

                // Build tile UI
                RebuildQuickSettingsTiles();

                // Build sortable grid (for customize panel, initially hidden)
                BuildSortableGrid();

                quickSettingsInitialized = true;
                Logger.Info("Quick Settings initialized with system accent color");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing Quick Settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh Quick Settings tiles when Legion status changes
        /// </summary>
        private void RefreshQuickSettingsForLegion()
        {
            if (!quickSettingsInitialized) return;

            try
            {
                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                UpdateQuickSettingsTileStates();
                Logger.Info("Quick Settings refreshed for Legion detection change");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing Quick Settings for Legion: {ex.Message}");
            }
        }

        /// <summary>
        /// Define all available Quick Settings tiles
        /// </summary>
        private void DefineQuickSettingsTiles()
        {
            qsTileDefinitions.Clear();
            qsTileMap.Clear();

            int order = 0;

            // Core tiles
            AddTileDefinition("TDPMode", "TDP Mode", "\uE945", order: order++);
            AddTileDefinition("Profile", "Profile", "\uE77B", order: order++);
            AddTileDefinition("Overlay", "Overlay", "\uE7B3", order: order++);
            AddTileDefinition("PowerMode", "Power Mode", "\uE945", order: order++);
            AddTileDefinition("FPSLimit", "FPS Limit", "\uE916", order: order++);
            AddTileDefinition("AutoTDP", "AutoTDP", "\uE9F5", order: order++);
            AddTileDefinition("Resolution", "Resolution", "\uE7F8", order: order++);
            AddTileDefinition("HDR", "HDR", "\uE706", order: order++);
            AddTileDefinition("LosslessScaling", "Lossless", "\uE740", order: order++);
            AddTileDefinition("RIS", "RIS", "\uE8B3", order: order++);
            AddTileDefinition("AFMF", "AFMF", "\uE916", order: order++);
            AddTileDefinition("RSR", "RSR", "\uE8B3", order: order++);
            AddTileDefinition("AntiLag", "Anti-Lag", "\uE916", order: order++);
            AddTileDefinition("RadeonChill", "Chill", "\uE9CA", order: order++);
            AddTileDefinition("CPUBoost", "CPU Boost", "\uE7F4", order: order++);
            AddTileDefinition("EPP", "EPP", "\uE83E", order: order++);

            // Keyboard trigger tile
            AddTileDefinition("Keyboard", "Keyboard", "\uE765", isTrigger: true, order: order++);

            // Legion-specific tiles (will be hidden if Legion not detected)
            AddTileDefinition("LegionTouchpad", "Touchpad", "\uE962", order: order++);
            AddTileDefinition("LegionLightMode", "Light Mode", "\uE781", order: order++);
            AddTileDefinition("LegionDesktopControls", "Desktop", "\uE7F4", order: order++);
            AddTileDefinition("LegionRemapControls", "Remap", "\uE7FC", order: order++);
            AddTileDefinition("LegionChargeLimit", "Charge Limit", "\uE83F", order: order++);
            AddTileDefinition("LegionPowerLight", "Power Light", "\uE7E8", order: order++);
            AddTileDefinition("Battery", "Battery", "\uE83F", order: order++);

            // Load custom shortcut tiles from storage
            LoadCustomShortcutTiles();

            // Action tiles at the bottom (high order numbers)
            int actionOrder = 1000;  // Start action tiles at high order to keep them at bottom
            AddTileDefinition("ActionTaskManager", "Task Mgr", "\uE7EF", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionExplorer", "Explorer", "\uEC50", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionEndTask", "End Task", "\uE711", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionFullscreen", "Fullscreen", "\uE740", isAction: true, order: actionOrder++);
            AddTileDefinition("ActionHibernate", "Hibernate", "\uE708", isAction: true, order: actionOrder++);
        }

        private void AddTileDefinition(string id, string name, string glyph, bool isTrigger = false, bool isAction = false, string customShortcut = null, int order = 0)
        {
            var def = new TileDefinition { Id = id, Name = name, Glyph = glyph, IsVisible = true, IsTrigger = isTrigger, IsAction = isAction, CustomShortcut = customShortcut, Order = order };
            qsTileDefinitions.Add(def);
            qsTileMap[id] = def;
        }

        /// <summary>
        /// Load custom shortcut tiles from storage using QuickSettingsConfig
        /// </summary>
        private void LoadCustomShortcutTiles()
        {
            try
            {
                // Load from QuickSettingsConfig (the new unified storage)
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var customTiles = config.Tiles.Where(t => t.Type == QuickSettings.TileType.CustomShortcut).ToList();

                // Calculate starting order (after built-in tiles)
                int startingOrder = qsTileDefinitions.Count > 0 ? qsTileDefinitions.Max(t => t.Order) + 1 : 100;

                int index = 0;
                foreach (var tile in customTiles)
                {
                    if (!string.IsNullOrEmpty(tile.CustomShortcut))
                    {
                        // Use the stable GUID from QuickSettingsConfig instead of index-based ID
                        // This prevents tile ID mismatch when widget is reloaded
                        string tileId = tile.Id;
                        var def = new TileDefinition
                        {
                            Id = tileId,
                            Name = tile.Name,
                            Glyph = tile.Icon ?? "\uE768",
                            IsVisible = tile.IsVisible,
                            IsTrigger = true,
                            CustomShortcut = tile.CustomShortcut,
                            Order = startingOrder + index  // Order will be overridden by LoadQuickSettingsConfig if saved
                        };
                        qsTileDefinitions.Add(def);
                        qsTileMap[tileId] = def;
                        qsCustomShortcuts.Add(def);
                        index++;
                    }
                }
                Logger.Info($"Loaded {index} custom shortcut tiles from QuickSettingsConfig (using stable GUIDs)");

                // Migration: If old storage has shortcuts that aren't in the new system, migrate them
                MigrateOldCustomShortcuts();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading custom shortcut tiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Migrate old custom shortcuts from the legacy storage format to QuickSettingsConfig
        /// </summary>
        private void MigrateOldCustomShortcuts()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("QS_CustomShortcuts", out object val) && val is string data && !string.IsNullOrEmpty(data))
                {
                    var config = QuickSettings.QuickSettingsConfig.Instance;
                    var existingShortcuts = config.Tiles
                        .Where(t => t.Type == QuickSettings.TileType.CustomShortcut)
                        .Select(t => t.CustomShortcut)
                        .ToHashSet();

                    var shortcuts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    int migratedCount = 0;

                    foreach (var shortcut in shortcuts)
                    {
                        var parts = shortcut.Split('|');
                        if (parts.Length == 2 && !existingShortcuts.Contains(parts[1]))
                        {
                            // Add to QuickSettingsConfig if not already present
                            config.AddCustomTile(parts[0], "\uE768", parts[1]);
                            migratedCount++;
                        }
                    }

                    if (migratedCount > 0)
                    {
                        Logger.Info($"Migrated {migratedCount} custom shortcuts from legacy storage");
                        // Clear old storage after migration
                        settings.Values.Remove("QS_CustomShortcuts");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error migrating old custom shortcuts: {ex.Message}");
            }
        }

        /// <summary>
        /// Save custom shortcut tiles to QuickSettingsConfig
        /// Note: This is now handled automatically by QuickSettingsConfig.AddCustomTile
        /// This method is kept for compatibility but delegates to QuickSettingsConfig
        /// </summary>
        private void SaveCustomShortcutTiles()
        {
            try
            {
                // QuickSettingsConfig.Save() is called automatically by AddCustomTile
                // This method now just triggers a save to ensure consistency
                QuickSettings.QuickSettingsConfig.Instance.Save();
                Logger.Info($"Custom shortcut tiles saved to QuickSettingsConfig");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving custom shortcut tiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a new custom shortcut tile using QuickSettingsConfig
        /// </summary>
        private void AddCustomShortcutTile(string name, string shortcut)
        {
            try
            {
                // Add to QuickSettingsConfig (saves automatically) - returns tile with GUID
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var configTile = config.AddCustomTile(name, "\uE768", shortcut);

                // Calculate new order (place at end)
                int maxOrder = qsTileDefinitions.Count > 0 ? qsTileDefinitions.Max(t => t.Order) : 0;

                // Use the GUID from QuickSettingsConfig for stable tile identification
                string tileId = configTile.Id;
                var def = new TileDefinition
                {
                    Id = tileId,
                    Name = name,
                    Glyph = "\uE768",
                    IsVisible = true,
                    IsTrigger = true,
                    CustomShortcut = shortcut,
                    Order = maxOrder + 1
                };
                qsTileDefinitions.Add(def);
                qsTileMap[tileId] = def;
                qsCustomShortcuts.Add(def);

                RebuildQuickSettingsTiles();
                BuildSortableGrid();

                Logger.Info($"Added custom shortcut tile: {name} -> {shortcut} (id: {tileId})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error adding custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Load Quick Settings configuration from storage
        /// </summary>
        private void LoadQuickSettingsConfig()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load column count setting
                if (settings.Values.TryGetValue("QS_ColumnCount", out object colVal) && colVal is int colCount)
                {
                    qsColumnCount = Math.Max(3, Math.Min(5, colCount));  // Clamp to 3-5
                }

                foreach (var tile in qsTileDefinitions)
                {
                    string visKey = $"QS_{tile.Id}_Visible";
                    string orderKey = $"QS_{tile.Id}_Order";

                    if (settings.Values.TryGetValue(visKey, out object val) && val is bool visible)
                    {
                        tile.IsVisible = visible;
                    }
                    if (settings.Values.TryGetValue(orderKey, out object orderVal) && orderVal is int order)
                    {
                        tile.Order = order;
                    }
                }

                Logger.Info($"Quick Settings config loaded (columns: {qsColumnCount})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading Quick Settings config: {ex.Message}");
            }
        }

        /// <summary>
        /// Save Quick Settings configuration to storage
        /// </summary>
        private void SaveQuickSettingsConfig()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Save column count setting
                settings.Values["QS_ColumnCount"] = qsColumnCount;

                foreach (var tile in qsTileDefinitions)
                {
                    settings.Values[$"QS_{tile.Id}_Visible"] = tile.IsVisible;
                    settings.Values[$"QS_{tile.Id}_Order"] = tile.Order;
                }

                Logger.Info($"Quick Settings config saved (columns: {qsColumnCount})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving Quick Settings config: {ex.Message}");
            }
        }

        /// <summary>
        /// Build sortable grid for tile customization
        /// </summary>
        private void BuildSortableGrid()
        {
            if (TileSortableGrid == null) return;

            TileSortableGrid.Children.Clear();

            // Get all tiles sorted by order (including hidden ones)
            var allTiles = qsTileDefinitions
                .Where(t => !ShouldSkipTile(t))
                .OrderBy(t => t.Order)
                .ToList();

            // Build rows of tiles (3 or 4 columns based on setting)
            Grid currentRow = null;
            int colIndex = 0;

            for (int i = 0; i < allTiles.Count; i++)
            {
                if (colIndex == 0)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    // Add column definitions dynamically based on qsColumnCount
                    for (int c = 0; c < qsColumnCount; c++)
                    {
                        if (c > 0) currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });  // Spacer
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    TileSortableGrid.Children.Add(currentRow);
                }

                var tile = allTiles[i];
                var miniTile = CreateMiniTileForSort(tile, i);
                Grid.SetColumn(miniTile, colIndex * 2);
                currentRow.Children.Add(miniTile);

                colIndex++;
                if (colIndex >= qsColumnCount)
                {
                    colIndex = 0;
                }
            }
        }

        /// <summary>
        /// Create a mini tile button for the sortable grid
        /// </summary>
        private Button CreateMiniTileForSort(TileDefinition tile, int index)
        {
            bool isSelected = qsSelectedTileForMove?.Id == tile.Id;

            var button = new Button
            {
                Tag = tile.Id,
                MinHeight = 60,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = isSelected
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180))  // Highlight selected
                    : (tile.IsVisible
                        ? tileOffBrush
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(128, 26, 28, 30))),  // Dimmed if hidden
                BorderBrush = isSelected
                    ? new SolidColorBrush(Windows.UI.Colors.White)
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                UseSystemFocusVisuals = true,
                FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent),
                TabIndex = index
            };

            var content = new Grid();

            // Icon and name stack
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new FontIcon
            {
                Glyph = tile.Glyph,
                FontSize = 18,
                Foreground = new SolidColorBrush(tile.IsVisible ? Windows.UI.Colors.White : Windows.UI.Colors.Gray)
            });
            stack.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(tile.IsVisible ? Windows.UI.Colors.White : Windows.UI.Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            content.Children.Add(stack);

            // Eye icon (top-right) - shows visibility status
            var eyeIcon = new FontIcon
            {
                Glyph = tile.IsVisible ? "\uE7B3" : "\uED1A",  // Eye / Eye crossed
                FontSize = 12,
                Foreground = new SolidColorBrush(tile.IsVisible
                    ? Windows.UI.Color.FromArgb(255, 100, 200, 100)   // Green for visible
                    : Windows.UI.Color.FromArgb(255, 200, 100, 100)), // Red for hidden
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0)
            };
            content.Children.Add(eyeIcon);

            // Order number badge (bottom-left)
            var orderText = new TextBlock
            {
                Text = (index + 1).ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 2)
            };
            content.Children.Add(orderText);

            // Custom shortcut indicator (bottom-right) - shows it can be deleted
            if (!string.IsNullOrEmpty(tile.CustomShortcut))
            {
                var customIcon = new FontIcon
                {
                    Glyph = "\uE932",  // Pin icon to indicate custom
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 200, 100)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 4, 2)
                };
                content.Children.Add(customIcon);
            }

            button.Content = content;
            button.Click += SortableTile_Click;

            return button;
        }

        /// <summary>
        /// Handle delete button click on sortable tile for custom shortcuts
        /// </summary>
        private void SortableTileDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tileId))
                    return;

                if (!qsTileMap.TryGetValue(tileId, out var tile))
                    return;

                DeleteCustomShortcutTile(tile);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling sortable tile delete: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle tap on sortable tile - select, swap, or toggle visibility
        /// </summary>
        private void SortableTile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button button) || !(button.Tag is string tileId))
                    return;

                if (!qsTileMap.TryGetValue(tileId, out var clickedTile))
                    return;

                if (qsSelectedTileForMove == null)
                {
                    // First tap: select tile - just update visuals, don't rebuild
                    qsSelectedTileForMove = clickedTile;
                    UpdateSelectedTileIndicator(clickedTile);
                    UpdateSortableGridVisuals(tileId);
                }
                else if (qsSelectedTileForMove.Id == clickedTile.Id)
                {
                    // Tap same tile: toggle visibility - need rebuild for eye icon change
                    clickedTile.IsVisible = !clickedTile.IsVisible;
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGridPreserveScroll(tileId);
                    Logger.Info($"Toggled visibility for {clickedTile.Name}: {clickedTile.IsVisible}");
                }
                else
                {
                    // Tap different tile: swap Order values - need rebuild for reorder
                    int tempOrder = qsSelectedTileForMove.Order;
                    qsSelectedTileForMove.Order = clickedTile.Order;
                    clickedTile.Order = tempOrder;

                    Logger.Info($"Swapped tile order: {qsSelectedTileForMove.Name} <-> {clickedTile.Name}");

                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGridPreserveScroll(tileId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling sortable tile click: {ex.Message}");
            }
        }

        /// <summary>
        /// Build sortable grid while preserving scroll position and focus
        /// </summary>
        private void BuildSortableGridPreserveScroll(string focusTileId = null)
        {
            // Save scroll position
            double scrollOffset = 0;
            if (QuickSettingsScrollViewer != null)
            {
                scrollOffset = QuickSettingsScrollViewer.VerticalOffset;
            }

            BuildSortableGrid();

            // Restore scroll position and focus after layout update
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                if (QuickSettingsScrollViewer != null && scrollOffset > 0)
                {
                    QuickSettingsScrollViewer.ChangeView(null, scrollOffset, null, true);
                }

                // Restore focus to the specified tile
                if (!string.IsNullOrEmpty(focusTileId) && TileSortableGrid != null)
                {
                    foreach (var child in TileSortableGrid.Children)
                    {
                        if (child is Grid row)
                        {
                            foreach (var cell in row.Children)
                            {
                                if (cell is Button btn && btn.Tag is string id && id == focusTileId)
                                {
                                    btn.Focus(FocusState.Programmatic);
                                    return;
                                }
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Update visual state of sortable tiles without rebuilding (for selection changes)
        /// </summary>
        private void UpdateSortableGridVisuals(string focusTileId = null)
        {
            if (TileSortableGrid == null) return;

            foreach (var child in TileSortableGrid.Children)
            {
                if (child is Grid row)
                {
                    foreach (var cell in row.Children)
                    {
                        if (cell is Button btn && btn.Tag is string id && qsTileMap.TryGetValue(id, out var tile))
                        {
                            bool isSelected = qsSelectedTileForMove?.Id == id;

                            // Update button background and border
                            btn.Background = isSelected
                                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180))
                                : (tile.IsVisible
                                    ? tileOffBrush
                                    : new SolidColorBrush(Windows.UI.Color.FromArgb(128, 26, 28, 30)));
                            btn.BorderBrush = isSelected
                                ? new SolidColorBrush(Windows.UI.Colors.White)
                                : new SolidColorBrush(Windows.UI.Color.FromArgb(80, 80, 85, 92));
                            btn.BorderThickness = new Thickness(isSelected ? 2 : 1);

                            // Focus the specified tile
                            if (!string.IsNullOrEmpty(focusTileId) && id == focusTileId)
                            {
                                btn.Focus(FocusState.Programmatic);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update the selected tile indicator text
        /// </summary>
        private void UpdateSelectedTileIndicator(TileDefinition tile)
        {
            if (SelectedTileIndicator == null || SelectedTileText == null)
                return;

            if (tile == null)
            {
                SelectedTileIndicator.Visibility = Visibility.Collapsed;
                if (DeleteSelectedTileButton != null)
                    DeleteSelectedTileButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                SelectedTileIndicator.Visibility = Visibility.Visible;
                SelectedTileText.Text = $"Selected: {tile.Name}\nTap another tile to swap, or tap again to toggle visibility";

                // Show delete button for custom shortcuts (identified by having a CustomShortcut value)
                if (DeleteSelectedTileButton != null)
                {
                    DeleteSelectedTileButton.Visibility = !string.IsNullOrEmpty(tile.CustomShortcut)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Handle delete button click in the selected tile indicator
        /// </summary>
        private void DeleteSelectedTile_Click(object sender, RoutedEventArgs e)
        {
            if (qsSelectedTileForMove != null && !string.IsNullOrEmpty(qsSelectedTileForMove.CustomShortcut))
            {
                DeleteCustomShortcutTile(qsSelectedTileForMove);
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile
        /// </summary>
        private void DeleteCustomShortcutTile(TileDefinition tile)
        {
            try
            {
                // Remove from QuickSettingsConfig persistent storage first
                // Need to find the matching config tile by custom shortcut path
                var config = QuickSettings.QuickSettingsConfig.Instance;
                var configTile = config.Tiles.FirstOrDefault(t =>
                    t.Type == QuickSettings.TileType.CustomShortcut &&
                    t.CustomShortcut == tile.CustomShortcut);
                if (configTile != null)
                {
                    config.RemoveTile(configTile.Id);
                }

                // Remove from local lists
                qsTileDefinitions.Remove(tile);
                qsTileMap.Remove(tile.Id);
                qsCustomShortcuts.Remove(tile);

                // Clear selection if we deleted the selected tile
                if (qsSelectedTileForMove?.Id == tile.Id)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                }

                BuildSortableGridPreserveScroll();
                // Don't rebuild main tiles here - they'll update when panel closes

                Logger.Info($"Deleted custom shortcut tile: {tile.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile (button click handler - legacy)
        /// </summary>
        private void DeleteCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string tileId)
                {
                    var tile = qsTileDefinitions.FirstOrDefault(t => t.Id == tileId);
                    if (tile != null)
                    {
                        DeleteCustomShortcutTile(tile);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a tile should be skipped based on hardware detection
        /// </summary>
        private bool ShouldSkipTile(TileDefinition tile)
        {
            // Skip Legion tiles if not detected
            if ((tile.Id == "LegionTouchpad" || tile.Id == "LegionLightMode" ||
                 tile.Id == "LegionDesktopControls" || tile.Id == "LegionRemapControls" ||
                 tile.Id == "LegionChargeLimit" || tile.Id == "LegionPowerLight") &&
                (legionGoDetected?.Value != true))
            {
                return true;
            }

            // Skip TDP Mode if Legion not detected
            if (tile.Id == "TDPMode" && (legionGoDetected?.Value != true))
            {
                return true;
            }

            // Skip Lossless Scaling tile if not installed
            if (tile.Id == "LosslessScaling" && (losslessScalingInstalled?.Value != true))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rebuild tile grid with only visible tiles, in 3-column layout
        /// </summary>
        private void RebuildQuickSettingsTiles()
        {
            if (QuickSettingsTilesContainer == null) return;

            QuickSettingsTilesContainer.Children.Clear();

            // Get tiles to display - in edit mode show all (including hidden), otherwise only visible
            var tilesToShow = qsTileDefinitions
                .Where(t => !ShouldSkipTile(t) && (qsEditMode || t.IsVisible))
                .OrderBy(t => t.Order)
                .ToList();

            // Build rows of tiles (3 or 4 columns based on setting)
            Grid currentRow = null;
            int colIndex = 0;

            for (int i = 0; i < tilesToShow.Count; i++)
            {
                if (colIndex == 0)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    // Add column definitions dynamically based on qsColumnCount
                    for (int c = 0; c < qsColumnCount; c++)
                    {
                        if (c > 0) currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });  // Spacer
                        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    QuickSettingsTilesContainer.Children.Add(currentRow);
                }

                var tile = tilesToShow[i];
                var tileButton = CreateTileButton(tile);
                Grid.SetColumn(tileButton, colIndex * 2);
                currentRow.Children.Add(tileButton);

                colIndex++;
                if (colIndex >= qsColumnCount)
                {
                    colIndex = 0;
                }
            }
        }

        /// <summary>
        /// Create a tile button for the given definition
        /// </summary>
        private Button CreateTileButton(TileDefinition tile)
        {
            // Action tiles get a distinct background color
            var bgBrush = tile.IsAction
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 32, 48))  // Dark purple for action tiles
                : tileOffBrush;

            var button = new Button
            {
                Tag = tile.Id,
                Style = Resources["QuickSettingsTileStyle"] as Style,
                Background = bgBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            content.Children.Add(new FontIcon
            {
                Glyph = tile.Glyph,
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            content.Children.Add(new TextBlock
            {
                Text = tile.Name,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });

            // Action tiles show "Action" instead of state
            var stateText = new TextBlock
            {
                Text = tile.IsAction ? "Action" : "Off",
                FontSize = 13,
                Foreground = tile.IsAction
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 150, 200))  // Light purple for action
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };

            // For Profile tile, wrap in Canvas for scrolling long names
            if (tile.Id == "Profile")
            {
                var transform = new TranslateTransform { X = 0 };
                stateText.RenderTransform = transform;
                stateText.Margin = new Thickness(0); // Remove margin, Canvas handles positioning

                var canvas = new Canvas
                {
                    Width = 90, // Tile width for text
                    Height = 18,
                    Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 90, 18) };
                canvas.Children.Add(stateText);

                content.Children.Add(canvas);

                tile.StateTextCanvas = canvas;
                tile.StateTextTransform = transform;
            }
            else
            {
                content.Children.Add(stateText);
            }

            button.Content = content;
            button.Click += QuickSettingsTile_Click;

            tile.TileButton = button;
            tile.StateText = stateText;

            return button;
        }

        /// <summary>
        /// Updates the scroll animation for the Profile tile's state text.
        /// If text is wider than the canvas, starts a scrolling animation.
        /// </summary>
        private void UpdateProfileTileScrollAnimation(TileDefinition profileTile)
        {
            if (profileTile?.StateText == null || profileTile.StateTextCanvas == null || profileTile.StateTextTransform == null)
                return;

            // Stop any existing animation
            if (profileTile.ScrollStoryboard != null)
            {
                profileTile.ScrollStoryboard.Stop();
                profileTile.ScrollStoryboard = null;
            }

            // Reset transform
            profileTile.StateTextTransform.X = 0;

            // Measure text width
            profileTile.StateText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = profileTile.StateText.DesiredSize.Width;
            double canvasWidth = profileTile.StateTextCanvas.Width;

            // If text fits, no animation needed
            if (textWidth <= canvasWidth)
            {
                // Center the text
                Canvas.SetLeft(profileTile.StateText, (canvasWidth - textWidth) / 2);
                return;
            }

            // Text is too wide - set up scrolling animation
            Canvas.SetLeft(profileTile.StateText, 0);

            // Calculate scroll distance and duration
            double scrollDistance = textWidth - canvasWidth + 10; // Extra padding
            double scrollSpeed = 30; // pixels per second
            double scrollDuration = scrollDistance / scrollSpeed;

            var storyboard = new Storyboard();
            var animation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Pause at start
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0
            });
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)),
                Value = 0
            });

            // Scroll left
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration)),
                Value = -scrollDistance
            });

            // Pause at end
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + scrollDuration)),
                Value = -scrollDistance
            });

            // Scroll back right
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + scrollDuration * 2)),
                Value = 0
            });

            // Pause before repeat
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5 + scrollDuration * 2)),
                Value = 0
            });

            Storyboard.SetTarget(animation, profileTile.StateTextTransform);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);

            profileTile.ScrollStoryboard = storyboard;
            storyboard.Begin();
        }

        /// <summary>
        /// Update all Quick Settings tile states based on current property values
        /// </summary>
        private void UpdateQuickSettingsTileStates()
        {
            if (!quickSettingsInitialized)
            {
                InitializeQuickSettings();
            }

            try
            {
                var accentForeground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 200, 255));
                var offForeground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));

                // TDP Mode tile - color-coded backgrounds: Quiet=Blue, Balanced=White/Grey, Performance=Red, Custom=Purple
                if (qsTileMap.TryGetValue("TDPMode", out var tdpTile) && tdpTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true && legionPerformanceMode != null)
                    {
                        int mode = legionPerformanceMode.Value;
                        string modeText;
                        SolidColorBrush tdpModeBrush;
                        switch (mode)
                        {
                            case 1: // Quiet - Desaturated Blue
                                modeText = "Quiet";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 45, 60));
                                break;
                            case 2: // Balanced - Grey
                                modeText = "Balanced";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                break;
                            case 3: // Performance - Desaturated Red
                                modeText = "Performance";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 40, 40));
                                break;
                            case 255: // Custom - Desaturated Purple
                                int currentTdp = (int)(tdp?.Value ?? 15);
                                modeText = $"Custom ({currentTdp}W)";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 42, 58));
                                break;
                            default:
                                modeText = "Balanced";
                                tdpModeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 55));
                                break;
                        }
                        tdpTile.StateText.Text = modeText;
                        tdpTile.StateText.Foreground = accentForeground;
                        tdpTile.TileButton.Background = tdpModeBrush;
                    }
                }

                // AutoTDP tile
                if (qsTileMap.TryGetValue("AutoTDP", out var autoTdpTile) && autoTdpTile.TileButton != null)
                {
                    bool enabled = AutoTDPToggle?.IsOn ?? false;
                    int targetFps = (int)(AutoTDPTargetFPSSlider?.Value ?? 60);
                    string stateText = enabled ? $"{targetFps} FPS" : "Off";
                    autoTdpTile.StateText.Text = stateText;
                    autoTdpTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    autoTdpTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Profile tile
                if (qsTileMap.TryGetValue("Profile", out var profileTile) && profileTile.TileButton != null)
                {
                    bool perGame = perGameProfile?.Value ?? false;
                    bool defaultProfileActive = defaultGameProfileEnabled?.Value ?? false;
                    string gameName = (runningGame != null && runningGame.Value.IsValid()) ? runningGame.Value.GameId.Name : "Per-Game";

                    // Show game name with gradient when default game profile is active
                    string profileName;
                    if (defaultProfileActive)
                    {
                        // Use game name from current profile or running game
                        profileName = currentDefaultGameProfile?.GameName ?? gameName;
                        profileTile.StateText.Text = profileName;
                        profileTile.StateText.Foreground = accentForeground;
                        profileTile.TileButton.Background = tileDefaultProfileBrush;
                    }
                    else
                    {
                        profileName = perGame ? gameName : "Global";
                        profileTile.StateText.Text = profileName;
                        profileTile.StateText.Foreground = perGame ? accentForeground : offForeground;
                        profileTile.TileButton.Background = perGame ? tileOnBrush : tileOffBrush;
                    }

                    // Update scroll animation for long profile names
                    UpdateProfileTileScrollAnimation(profileTile);
                }

                // Performance Overlay tile
                if (qsTileMap.TryGetValue("Overlay", out var overlayTile) && overlayTile.TileButton != null)
                {
                    if (osdProvider == 1) // AMD
                    {
                        string amdLevelText = amdOverlayLevel > 0 ? $"AMD {amdOverlayLevel}" : "Off";
                        overlayTile.StateText.Text = amdLevelText;
                        overlayTile.StateText.Foreground = amdOverlayLevel > 0 ? accentForeground : offForeground;
                        overlayTile.TileButton.Background = amdOverlayLevel > 0 ? tileOnBrush : tileOffBrush;
                    }
                    else // RTSS
                    {
                        int level = (int)(osd?.Value ?? 0);
                        string levelText;
                        switch (level)
                        {
                            case 0: levelText = "Off"; break;
                            case 1: levelText = "Basic"; break;
                            case 2: levelText = "Detailed"; break;
                            case 3: levelText = "Full"; break;
                            default: levelText = "Off"; break;
                        }
                        overlayTile.StateText.Text = levelText;
                        overlayTile.StateText.Foreground = level > 0 ? accentForeground : offForeground;
                        overlayTile.TileButton.Background = level > 0 ? tileOnBrush : tileOffBrush;
                    }
                }

                // Power Mode tile
                if (qsTileMap.TryGetValue("PowerMode", out var powerModeTile) && powerModeTile.TileButton != null)
                {
                    int mode = osPowerMode?.Value ?? 1;
                    string modeText;
                    switch (mode)
                    {
                        case 0: modeText = "Efficiency"; break;
                        case 1: modeText = "Balanced"; break;
                        case 2: modeText = "Performance"; break;
                        default: modeText = "Balanced"; break;
                    }
                    powerModeTile.StateText.Text = modeText;
                    powerModeTile.StateText.Foreground = mode != 1 ? accentForeground : offForeground;
                    powerModeTile.TileButton.Background = mode == 2 ? tileOnBrush : (mode == 0 ? tileActiveBrush : tileOffBrush);
                }

                // FPS Limit tile
                if (qsTileMap.TryGetValue("FPSLimit", out var fpsLimitTile) && fpsLimitTile.TileButton != null)
                {
                    int limit = fpsLimit?.Value ?? 0;
                    string limitText = limit == 0 ? "Off" : $"{limit}";
                    fpsLimitTile.StateText.Text = limitText;
                    fpsLimitTile.StateText.Foreground = limit > 0 ? accentForeground : offForeground;
                    fpsLimitTile.TileButton.Background = limit > 0 ? tileOnBrush : tileOffBrush;
                }

                // Resolution tile
                if (qsTileMap.TryGetValue("Resolution", out var resTile) && resTile.TileButton != null)
                {
                    string currentRes = resolution?.Value ?? "1920x1080";
                    resTile.StateText.Text = currentRes;
                    resTile.StateText.Foreground = accentForeground;
                    resTile.TileButton.Background = tileOffBrush;
                }

                // HDR tile
                if (qsTileMap.TryGetValue("HDR", out var hdrTile) && hdrTile.TileButton != null)
                {
                    bool supported = hdrSupported?.Value ?? false;
                    bool enabled = hdrEnabled?.Value ?? false;
                    hdrTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    hdrTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    hdrTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Lossless Scaling tile
                if (qsTileMap.TryGetValue("LosslessScaling", out var lsTile) && lsTile.TileButton != null)
                {
                    bool enabled = losslessScalingEnabled?.Value ?? false;
                    lsTile.StateText.Text = enabled ? "On" : "Off";
                    lsTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    lsTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // RIS (Radeon Image Sharpening) tile
                if (qsTileMap.TryGetValue("RIS", out var risTile) && risTile.TileButton != null)
                {
                    bool supported = amdImageSharpeningSupported?.Value ?? false;
                    bool enabled = amdImageSharpeningEnabled?.Value ?? false;
                    risTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    risTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    risTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // AFMF tile
                if (qsTileMap.TryGetValue("AFMF", out var afmfTile) && afmfTile.TileButton != null)
                {
                    bool supported = amdFluidMotionFrameSupported?.Value ?? false;
                    bool enabled = amdFluidMotionFrameEnabled?.Value ?? false;
                    afmfTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    afmfTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    afmfTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // RSR tile
                if (qsTileMap.TryGetValue("RSR", out var rsrTile) && rsrTile.TileButton != null)
                {
                    bool supported = amdRadeonSuperResolutionSupported?.Value ?? false;
                    bool enabled = amdRadeonSuperResolutionEnabled?.Value ?? false;
                    rsrTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    rsrTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    rsrTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Anti-Lag tile
                if (qsTileMap.TryGetValue("AntiLag", out var antiLagTile) && antiLagTile.TileButton != null)
                {
                    bool supported = amdRadeonAntiLagSupported?.Value ?? false;
                    bool enabled = amdRadeonAntiLagEnabled?.Value ?? false;
                    antiLagTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    antiLagTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    antiLagTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // Radeon Chill tile
                if (qsTileMap.TryGetValue("RadeonChill", out var chillTile) && chillTile.TileButton != null)
                {
                    bool supported = amdRadeonChillSupported?.Value ?? false;
                    bool enabled = amdRadeonChillEnabled?.Value ?? false;
                    chillTile.StateText.Text = !supported ? "N/A" : (enabled ? "On" : "Off");
                    chillTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    chillTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // CPU Boost tile
                if (qsTileMap.TryGetValue("CPUBoost", out var boostTile) && boostTile.TileButton != null)
                {
                    bool enabled = cpuBoost?.Value ?? false;
                    boostTile.StateText.Text = enabled ? "On" : "Off";
                    boostTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                    boostTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                }

                // EPP tile
                if (qsTileMap.TryGetValue("EPP", out var eppTile) && eppTile.TileButton != null)
                {
                    int eppValue = (int)(cpuEPP?.Value ?? 0);
                    eppTile.StateText.Text = $"{eppValue}%";
                    eppTile.StateText.Foreground = accentForeground;
                    eppTile.TileButton.Background = eppValue > 50 ? tileActiveBrush : tileOffBrush;
                }

                // Keyboard trigger tile
                if (qsTileMap.TryGetValue("Keyboard", out var keyboardTile) && keyboardTile.TileButton != null)
                {
                    keyboardTile.StateText.Text = "Open";
                    keyboardTile.StateText.Foreground = accentForeground;
                    keyboardTile.TileButton.Background = tileTriggerBrush;
                }

                // Custom shortcut tiles
                foreach (var shortcutTile in qsCustomShortcuts)
                {
                    if (shortcutTile.TileButton != null && shortcutTile.StateText != null)
                    {
                        shortcutTile.StateText.Text = shortcutTile.CustomShortcut ?? "Run";
                        shortcutTile.StateText.Foreground = accentForeground;
                        shortcutTile.TileButton.Background = tileTriggerBrush;
                    }
                }

                // Legion Touchpad tile
                if (qsTileMap.TryGetValue("LegionTouchpad", out var touchpadTile) && touchpadTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionTouchpadEnabled?.Value ?? false;
                        touchpadTile.StateText.Text = enabled ? "On" : "Off";
                        touchpadTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        touchpadTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Light Mode tile
                if (qsTileMap.TryGetValue("LegionLightMode", out var lightTile) && lightTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        int mode = legionLightMode?.Value ?? 0;
                        string modeText;
                        switch (mode)
                        {
                            case 0: modeText = "Off"; break;
                            case 1: modeText = "Static"; break;
                            case 2: modeText = "Breathing"; break;
                            case 3: modeText = "Rainbow"; break;
                            case 4: modeText = "Spiral"; break;
                            default: modeText = "Off"; break;
                        }
                        lightTile.StateText.Text = modeText;
                        lightTile.StateText.Foreground = mode > 0 ? accentForeground : offForeground;
                        lightTile.TileButton.Background = mode > 0 ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Desktop Controls tile
                if (qsTileMap.TryGetValue("LegionDesktopControls", out var desktopTile) && desktopTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = LegionDesktopControlsToggle?.IsOn ?? false;
                        desktopTile.StateText.Text = enabled ? "On" : "Off";
                        desktopTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        desktopTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Remap Controls tile
                if (qsTileMap.TryGetValue("LegionRemapControls", out var remapTile) && remapTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool isGameProfile = LegionControllerProfileToggle?.IsOn == true && HasValidGame(currentGameName);
                        string profileName = isGameProfile ? currentGameName : "Global";
                        // Truncate long names
                        if (profileName.Length > 10)
                            profileName = profileName.Substring(0, 9) + "…";
                        remapTile.StateText.Text = profileName;
                        remapTile.StateText.Foreground = isGameProfile ? accentForeground : offForeground;
                        remapTile.TileButton.Background = isGameProfile ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Charge Limit tile (80% battery limit)
                if (qsTileMap.TryGetValue("LegionChargeLimit", out var chargeLimitTile) && chargeLimitTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionChargeLimit?.Value ?? false;
                        chargeLimitTile.StateText.Text = enabled ? "80%" : "Off";
                        chargeLimitTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        chargeLimitTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Legion Power Light tile
                if (qsTileMap.TryGetValue("LegionPowerLight", out var powerLightTile) && powerLightTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true)
                    {
                        bool enabled = legionPowerLight?.Value ?? false;
                        powerLightTile.StateText.Text = enabled ? "On" : "Off";
                        powerLightTile.StateText.Foreground = enabled ? accentForeground : offForeground;
                        powerLightTile.TileButton.Background = enabled ? tileOnBrush : tileOffBrush;
                    }
                }

                // Battery tile - device battery in title, controllers in state text
                if (qsTileMap.TryGetValue("Battery", out var batteryTile) && batteryTile.TileButton != null)
                {
                    // Get device battery info (hide bolt at 100%)
                    int deviceBat = PowerManager.RemainingChargePercent;
                    bool deviceCharging = PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
                    string deviceIndicator = (deviceCharging && deviceBat < 100) ? "⚡" : "";

                    // Get the tile content elements
                    var content = batteryTile.TileButton.Content as StackPanel;
                    var iconElement = content?.Children.Count >= 1 ? content.Children[0] as FontIcon : null;
                    var labelText = content?.Children.Count >= 2 ? content.Children[1] as TextBlock : null;

                    // Update battery icon based on level and charging state
                    // Battery icons: \uE850-\uE859 (0-9), \uE83F (full)
                    // Charging icons: \uE85A-\uE863 (0-9), \uEBB5 (charging full)
                    if (iconElement != null)
                    {
                        string glyph;
                        if (deviceCharging)
                        {
                            // Charging icons
                            if (deviceBat >= 90) glyph = "\uEBB5";      // Full charging
                            else if (deviceBat >= 70) glyph = "\uE862"; // Charging 8
                            else if (deviceBat >= 50) glyph = "\uE85F"; // Charging 5
                            else if (deviceBat >= 30) glyph = "\uE85C"; // Charging 2
                            else glyph = "\uE85A";                       // Charging 0
                        }
                        else
                        {
                            // Normal battery icons
                            if (deviceBat >= 90) glyph = "\uE83F";      // Full
                            else if (deviceBat >= 70) glyph = "\uE858"; // Battery 8
                            else if (deviceBat >= 50) glyph = "\uE855"; // Battery 5
                            else if (deviceBat >= 30) glyph = "\uE852"; // Battery 2
                            else glyph = "\uE850";                       // Battery 0 (low)
                        }
                        iconElement.Glyph = glyph;
                    }

                    string stateText;
                    SolidColorBrush bgBrush;
                    int minBat = deviceBat; // Start with device battery

                    // Update title with device battery
                    if (labelText != null)
                    {
                        labelText.Text = $"{deviceBat}%{deviceIndicator}";
                    }

                    if (legionGoDetected?.Value == true)
                    {
                        int leftBat = controllerBatteryLeft?.Value ?? -1;
                        int rightBat = controllerBatteryRight?.Value ?? -1;
                        bool leftCharging = controllerChargingLeft?.Value ?? false;
                        bool rightCharging = controllerChargingRight?.Value ?? false;

                        if (leftBat > 0 && rightBat > 0)
                        {
                            // Controllers connected - show L/R with % (hide bolt at 100%)
                            string leftIndicator = (leftCharging && leftBat < 100) ? "⚡" : "";
                            string rightIndicator = (rightCharging && rightBat < 100) ? "⚡" : "";
                            stateText = $"L:{leftBat}%{leftIndicator} R:{rightBat}%{rightIndicator}";

                            // Color based on lowest of all batteries
                            minBat = Math.Min(deviceBat, Math.Min(leftBat, rightBat));
                        }
                        else
                        {
                            // Controllers not connected
                            stateText = "No Ctrl";
                        }
                    }
                    else
                    {
                        // Not Legion Go - just show "Device" in state
                        stateText = "Device";
                    }

                    // Color based on minimum battery level
                    if (minBat < 20)
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 35, 35)); // Red
                    else if (minBat < 50)
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 55, 35)); // Yellow
                    else
                        bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 55, 40)); // Green

                    batteryTile.StateText.Text = stateText;
                    batteryTile.StateText.Foreground = accentForeground;
                    batteryTile.TileButton.Background = bgBrush;
                }

                Logger.Debug("Quick Settings tile states updated");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error updating Quick Settings tile states: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle Quick Settings tile clicks
        /// </summary>
        private void QuickSettingsTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tileTag)
            {
                try
                {
                    // First check if it's in our local qsTileMap (includes custom shortcuts with GUID IDs)
                    if (qsTileMap.TryGetValue(tileTag, out var mappedTile) && !string.IsNullOrEmpty(mappedTile.CustomShortcut))
                    {
                        _ = SendCustomShortcutAsync(mappedTile.CustomShortcut, mappedTile.Name);
                    }
                    // Fallback: Check QuickSettingsConfig by ID (tile IDs are now GUIDs)
                    else if (QuickSettings.QuickSettingsConfig.Instance.GetTile(tileTag) is QuickSettings.QuickSettingsTile configTile
                             && configTile.Type == QuickSettings.TileType.CustomShortcut
                             && !string.IsNullOrEmpty(configTile.CustomShortcut))
                    {
                        _ = SendCustomShortcutAsync(configTile.CustomShortcut, configTile.Name);
                    }
                    else
                    {
                        switch (tileTag)
                        {
                            case "TDPMode":
                                CycleTDPMode();
                                break;
                            case "AutoTDP":
                                ToggleAutoTDPTile();
                                break;
                            case "Profile":
                                TogglePerGameProfile();
                                break;
                            case "Overlay":
                                CyclePerformanceOverlay();
                                break;
                            case "PowerMode":
                                CyclePowerMode();
                                break;
                            case "FPSLimit":
                                CycleFPSLimit();
                                break;
                            case "Resolution":
                                CycleResolution();
                                break;
                            case "HDR":
                                ToggleHDR();
                                break;
                            case "LosslessScaling":
                                ToggleLosslessScaling();
                                break;
                            case "RIS":
                                ToggleRIS();
                                break;
                            case "AFMF":
                                ToggleAFMF();
                                break;
                            case "RSR":
                                ToggleRSR();
                                break;
                            case "AntiLag":
                                ToggleAntiLag();
                                break;
                            case "RadeonChill":
                                ToggleRadeonChill();
                                break;
                            case "CPUBoost":
                                ToggleCPUBoost();
                                break;
                            case "EPP":
                                CycleEPP();
                                break;
                            case "Keyboard":
                                TriggerOnScreenKeyboard();
                                break;
                            case "LegionTouchpad":
                                ToggleLegionTouchpad();
                                break;
                            case "LegionLightMode":
                                CycleLegionLightMode();
                                break;
                            case "LegionDesktopControls":
                                ToggleLegionDesktopControls();
                                break;
                            case "LegionRemapControls":
                                ToggleRemapControlsProfile();
                                break;
                            case "LegionChargeLimit":
                                ToggleLegionChargeLimit();
                                break;
                            // Action tiles
                            case "ActionTaskManager":
                                LaunchTaskManager();
                                break;
                            case "ActionExplorer":
                                LaunchExplorer();
                                break;
                            case "ActionEndTask":
                                SendAltF4();
                                break;
                            case "ActionFullscreen":
                                ToggleFullscreen();
                                break;
                            case "ActionHibernate":
                                ExecuteHibernate();
                                break;
                            case "LegionPowerLight":
                                ToggleLegionPowerLight();
                                break;
                        }
                    }

                    // Update tile states after action
                    UpdateQuickSettingsTileStates();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling Quick Settings tile click: {ex.Message}");
                }
            }
        }

        private void CycleTDPMode()
        {
            // If default game profile is active, turn it off when user manually changes TDP mode
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("TDP Mode tile clicked - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
                // The toggle change will trigger OnDefaultProfileEnabledChanged which re-enables controls
            }

            if (legionGoDetected?.Value == true && legionPerformanceMode != null)
            {
                int currentMode = legionPerformanceMode.Value;
                int nextMode;
                switch (currentMode)
                {
                    case 1: nextMode = 2; break;     // Quiet -> Balanced
                    case 2: nextMode = 3; break;     // Balanced -> Performance
                    case 3: nextMode = 255; break;   // Performance -> Custom
                    case 255: nextMode = 1; break;   // Custom -> Quiet
                    default: nextMode = 2; break;
                }
                legionPerformanceMode.SetValue(nextMode);

                // Update TDPModeComboBox to match - set flag so SelectionChanged handler saves properly
                int nextIndex = Array.IndexOf(new int[] { 1, 2, 3, 255 }, nextMode);
                if (nextIndex >= 0 && TDPModeComboBox != null)
                {
                    isUserInitiatedTDPModeChange = true; // Flag this as user-initiated
                    TDPModeComboBox.SelectedIndex = nextIndex;
                    isUserInitiatedTDPModeChange = false;
                }

                // If switching to Custom mode, schedule TDP reapply after 5 seconds
                if (nextMode == 255)
                {
                    ScheduleQsTdpReapply();
                }

                Logger.Info($"TDP Mode cycled from {currentMode} to {nextMode}");
            }
        }

        private void ToggleAutoTDPTile()
        {
            if (AutoTDPToggle != null)
            {
                AutoTDPToggle.IsOn = !AutoTDPToggle.IsOn;
                Logger.Info($"AutoTDP tile toggled to: {AutoTDPToggle.IsOn}");
            }
        }

        private void ScheduleQsTdpReapply()
        {
            try
            {
                // Cancel existing timer
                if (qsTdpReapplyTimer != null)
                {
                    qsTdpReapplyTimer.Stop();
                }

                // Create new timer
                qsTdpReapplyTimer = new Windows.UI.Xaml.DispatcherTimer();
                qsTdpReapplyTimer.Interval = TimeSpan.FromSeconds(5);
                qsTdpReapplyTimer.Tick += async (s, e) =>
                {
                    qsTdpReapplyTimer.Stop();
                    // Reapply TDP - still in Custom mode?
                    if (legionPerformanceMode?.Value == 255)
                    {
                        // Read TDP value NOW (at timer fire time), not when scheduled
                        // This ensures we use the current profile's TDP if profile switched
                        int currentTdpValue = (int)(TDPSlider?.Value ?? 15);

                        // Reapply using current Performance tab TDP value
                        if (tdp != null)
                        {
                            // Force reapply by sending different value to helper first, then the real value
                            // This ensures the helper doesn't skip due to "equals current value"
                            tdp.SetValue(currentTdpValue - 1);
                            await System.Threading.Tasks.Task.Delay(100);
                            tdp.SetValue(currentTdpValue);
                            Logger.Info($"Quick Settings: Reapplied TDP {currentTdpValue}W after Custom mode switch");
                        }
                    }
                };
                qsTdpReapplyTimer.Start();
                Logger.Info($"Quick Settings: Scheduled TDP reapply in 5 seconds");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scheduling TDP reapply: {ex.Message}");
            }
        }

        private void TogglePerGameProfile()
        {
            // If Default Game Profile is active, toggle it off instead
            if (defaultGameProfileEnabled?.Value == true && DefaultProfileToggle != null)
            {
                Logger.Info("Profile tile clicked - turning off Default Game Profile");
                DefaultProfileToggle.IsOn = false;
                return;
            }

            // Only allow toggling when a game is detected
            if (perGameProfile != null && runningGame != null && runningGame.Value.IsValid())
            {
                bool newValue = !perGameProfile.Value;
                isUserInitiatedProfileToggle = true; // Flag this as user-initiated
                perGameProfile.SetValue(newValue);
                isUserInitiatedProfileToggle = false;
                Logger.Info($"Per-game profile toggled to {newValue}");
            }
            else
            {
                Logger.Info("Per-game profile toggle ignored - no game detected");
            }
        }

        private async void TriggerOnScreenKeyboard()
        {
            try
            {
                // Launch the accessibility on-screen keyboard via helper
                if (App.Connection != null)
                {
                    var message = new Windows.Foundation.Collections.ValueSet { { "LaunchProcess", "osk.exe" } };
                    await App.Connection.SendMessageAsync(message);
                    Logger.Info("On-screen keyboard launched via osk.exe");
                }
                else
                {
                    // Fallback to Win+Ctrl+O
                    QuickSettings.KeyboardShortcutHelper.SendShortcut("Win+Ctrl+O");
                    Logger.Info("On-screen keyboard triggered via Win+Ctrl+O (fallback)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error triggering on-screen keyboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch Task Manager via helper
        /// </summary>
        private void LaunchTaskManager()
        {
            try
            {
                _ = SendKeyboardShortcutViaHelper("Ctrl+Shift+Escape");
                Logger.Info("Task Manager launched via Ctrl+Shift+Escape");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Task Manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch File Explorer via helper
        /// </summary>
        private void LaunchExplorer()
        {
            try
            {
                _ = SendKeyboardShortcutViaHelper("Win+E");
                Logger.Info("Explorer launched via Win+E");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching Explorer: {ex.Message}");
            }
        }

        /// <summary>
        /// Close the foreground game window
        /// Uses Alt+Tab to switch to game, then Alt+F4 to close it
        /// </summary>
        private async void SendAltF4()
        {
            try
            {
                // Alt+Tab to switch focus to the game (away from Game Bar)
                _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                Logger.Info("Alt+Tab sent to focus game");

                // Wait for focus switch
                await Task.Delay(200);

                // Now send Alt+F4 to close the focused game
                _ = SendKeyboardShortcutViaHelper("Alt+F4");
                Logger.Info("Alt+F4 sent to close game");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error closing game: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle fullscreen via F11
        /// Uses Alt+Tab first to focus the game
        /// </summary>
        private async void ToggleFullscreen()
        {
            try
            {
                // Alt+Tab to switch focus to the game (away from Game Bar)
                _ = SendKeyboardShortcutViaHelper("Alt+Tab");
                Logger.Info("Alt+Tab sent to focus game");

                // Wait for focus switch
                await Task.Delay(200);

                // F11 is the most universal fullscreen toggle
                _ = SendKeyboardShortcutViaHelper("F11");
                Logger.Info("Fullscreen toggled via F11");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error toggling fullscreen: {ex.Message}");
            }
        }

        // Resolutions to exclude from quick cycling (odd resolutions that don't scale well)
        private static readonly HashSet<string> excludedQuickResolutions = new HashSet<string>
        {
            "1680x1050"  // Odd 16:10 resolution that doesn't scale cleanly
        };

        private void CycleResolution()
        {
            if (resolution != null && resolutions?.Value != null && resolutions.Value.Count > 0)
            {
                // Filter out excluded resolutions for quick cycling
                var quickResolutions = resolutions.Value
                    .Where(r => !excludedQuickResolutions.Contains(r))
                    .ToList();

                if (quickResolutions.Count == 0)
                {
                    quickResolutions = resolutions.Value; // Fallback to all if filter removes everything
                }

                string currentRes = resolution.Value;
                int currentIndex = quickResolutions.IndexOf(currentRes);

                // If current resolution is not in quick list, start from first
                if (currentIndex < 0) currentIndex = -1;

                int nextIndex = (currentIndex + 1) % quickResolutions.Count;
                string nextRes = quickResolutions[nextIndex];
                resolution.SetValue(nextRes);
                Logger.Info($"Resolution cycled from {currentRes} to {nextRes}");
            }
        }

        private void ToggleHDR()
        {
            if (hdrEnabled != null && (hdrSupported?.Value ?? false))
            {
                bool newValue = !hdrEnabled.Value;
                hdrEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (HDRToggle != null)
                    HDRToggle.IsOn = newValue;
                Logger.Info($"HDR toggled to {newValue}");
            }
        }

        private void ToggleLosslessScaling()
        {
            if (losslessScalingEnabled != null)
            {
                bool newValue = !losslessScalingEnabled.Value;
                losslessScalingEnabled.SetValue(newValue);
                Logger.Info($"Lossless Scaling toggled to {newValue}");
            }
        }

        private void ToggleAFMF()
        {
            if (amdFluidMotionFrameEnabled != null && (amdFluidMotionFrameSupported?.Value ?? false))
            {
                bool newValue = !amdFluidMotionFrameEnabled.Value;
                amdFluidMotionFrameEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDFluidMotionFrameToggle != null)
                    AMDFluidMotionFrameToggle.IsOn = newValue;
                Logger.Info($"AFMF toggled to {newValue}");
            }
        }

        private void ToggleRSR()
        {
            if (amdRadeonSuperResolutionEnabled != null && (amdRadeonSuperResolutionSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonSuperResolutionEnabled.Value;
                amdRadeonSuperResolutionEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonSuperResolutionToggle != null)
                    AMDRadeonSuperResolutionToggle.IsOn = newValue;
                Logger.Info($"RSR toggled to {newValue}");
            }
        }

        private void ToggleRIS()
        {
            if (amdImageSharpeningEnabled != null && (amdImageSharpeningSupported?.Value ?? false))
            {
                bool newValue = !amdImageSharpeningEnabled.Value;
                amdImageSharpeningEnabled.SetValue(newValue);
                AMDImageSharpeningToggle.IsOn = newValue;
                Logger.Info($"RIS toggled to {newValue}");
            }
        }

        private void ToggleAntiLag()
        {
            if (amdRadeonAntiLagEnabled != null && (amdRadeonAntiLagSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonAntiLagEnabled.Value;
                amdRadeonAntiLagEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonAntiLagToggle != null)
                    AMDRadeonAntiLagToggle.IsOn = newValue;
                Logger.Info($"Anti-Lag toggled to {newValue}");
            }
        }

        private void ToggleRadeonChill()
        {
            if (amdRadeonChillEnabled != null && (amdRadeonChillSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonChillEnabled.Value;
                amdRadeonChillEnabled.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (AMDRadeonChillToggle != null)
                    AMDRadeonChillToggle.IsOn = newValue;
                Logger.Info($"Radeon Chill toggled to {newValue}");
            }
        }

        private void ToggleCPUBoost()
        {
            if (cpuBoost != null)
            {
                bool newValue = !cpuBoost.Value;
                cpuBoost.SetValue(newValue);
                // Update the toggle so SettingChanged fires and saves to profile
                if (CPUBoostToggle != null)
                    CPUBoostToggle.IsOn = newValue;
                Logger.Info($"CPU Boost toggled to {newValue}");
            }
        }

        private void CyclePowerMode()
        {
            if (osPowerMode != null)
            {
                // Cycle: Efficiency (0) -> Balanced (1) -> Performance (2) -> Efficiency (0)
                int currentMode = osPowerMode.Value;
                int nextMode = (currentMode + 1) % 3;
                osPowerMode.SetValue(nextMode);

                // Update the combobox and value text in Performance tab
                isLoadingOSPowerMode = true;
                try
                {
                    OSPowerModeComboBox.SelectedIndex = nextMode;
                    OSPowerModeValue.Text = OSPowerModeNames[nextMode];
                }
                finally
                {
                    isLoadingOSPowerMode = false;
                }

                Logger.Info($"Power Mode cycled to {OSPowerModeNames[nextMode]}");

                // Save the change to profile
                if (!isInitialSync && !isApplyingHelperUpdate && !isLoadingProfile && SaveOSPowerMode)
                {
                    Logger.Info($"Saving OS Power Mode change to profile: {currentProfileName}");
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        private void CycleEPP()
        {
            if (cpuEPP != null)
            {
                int currentValue = (int)cpuEPP.Value;
                int nextValue;
                switch (currentValue)
                {
                    case 0: nextValue = 30; break;
                    case 30: nextValue = 80; break;
                    case 80: nextValue = 100; break;
                    case 100: nextValue = 0; break;
                    default: nextValue = 0; break;
                }
                cpuEPP.SetValue(nextValue);

                // Update slider to match (SaveCurrentSettingsToProfile reads from it)
                if (CPUEPPSlider != null)
                {
                    CPUEPPSlider.Value = nextValue;
                }

                Logger.Info($"EPP cycled from {currentValue} to {nextValue}");

                // Save the change to profile
                // Use direct save to bypass isApplyingHelperUpdate check - this is a user-initiated action
                if (!isInitialSync && !isLoadingProfile && SaveCPUEPP && !string.IsNullOrEmpty(currentProfileName))
                {
                    try
                    {
                        var profile = GetProfile(currentProfileName);
                        profile.CPUEPP = nextValue;
                        SaveProfileToStorage(currentProfileName, profile);
                        Logger.Info($"Saved EPP {nextValue} to profile: {currentProfileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to save EPP to profile: {ex.Message}");
                    }
                }
            }
        }

        private void CyclePerformanceOverlay()
        {
            if (osdProvider == 1) // AMD
            {
                // AMD has 4 overlay levels that cycle with Ctrl+Shift+X
                // Ctrl+Shift+O toggles the overlay on/off completely
                // Cycle: Off -> Level 1 -> Level 2 -> Level 3 -> Level 4 -> Off
                if (amdOverlayLevel == 0)
                {
                    // Currently off, turn on (starts at level 1)
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 1;
                    SaveAMDOverlayLevel();
                    Logger.Info("AMD Overlay toggled ON (Level 1)");
                }
                else if (amdOverlayLevel < 4)
                {
                    // Cycle to next level
                    CycleAMDOverlayLevel();
                    amdOverlayLevel++;
                    SaveAMDOverlayLevel();
                    Logger.Info($"AMD Overlay cycled to Level {amdOverlayLevel}");
                }
                else
                {
                    // At level 4, turn off
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 0;
                    SaveAMDOverlayLevel();
                    Logger.Info("AMD Overlay toggled OFF");
                }
                UpdateQuickSettingsTileStates();
            }
            else // RTSS
            {
                if (osd != null)
                {
                    int currentLevel = (int)osd.Value;
                    int nextLevel = (currentLevel + 1) % 4;
                    osd.SetValue(nextLevel);
                    Logger.Info($"RTSS Performance Overlay cycled from {currentLevel} to {nextLevel}");
                }
            }
        }

        /// <summary>
        /// Cycle FPS limit through: Off -> MaxRefresh -> MaxRefresh/2 -> MaxRefresh/3 -> Off
        /// </summary>
        private void CycleFPSLimit()
        {
            if (fpsLimit == null) return;

            // Get max refresh rate from current display
            int maxRefresh = 60; // Default
            if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
            {
                maxRefresh = refreshRates.Value.Max();
            }

            // Calculate FPS limit values: Max, Max/2, Max/3
            int[] fpsValues = new int[]
            {
                0,                          // Off (unlimited)
                maxRefresh,                 // e.g., 144
                maxRefresh / 2,             // e.g., 72
                maxRefresh / 3              // e.g., 48
            };

            // Find current index and cycle to next
            int currentLimit = fpsLimit.Value;
            int currentIndex = 0;
            for (int i = 0; i < fpsValues.Length; i++)
            {
                if (fpsValues[i] == currentLimit)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = (currentIndex + 1) % fpsValues.Length;
            int nextLimit = fpsValues[nextIndex];

            fpsLimit.SetValue(nextLimit);
            Logger.Info($"FPS Limit cycled from {currentLimit} to {nextLimit} (max refresh: {maxRefresh})");

            // Sync the Performance tab FPS Limit controls
            isApplyingHelperUpdate = true;
            try
            {
                // Update slider maximum to current refresh rate
                FPSLimitSlider.Maximum = maxRefresh;

                if (nextLimit > 0)
                {
                    FPSLimitToggle.IsOn = true;
                    FPSLimitSlider.Value = nextLimit;
                }
                else
                {
                    FPSLimitToggle.IsOn = false;
                }
            }
            finally
            {
                isApplyingHelperUpdate = false;
            }

            // Save to profile if FPS Limit saving is enabled
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// FPS Limit toggle changed - set FPS limit to slider value or 0 (off)
        /// </summary>
        private void FPSLimitToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Update display text when toggle is enabled
            if (FPSLimitToggle.IsOn && FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)FPSLimitSlider.Value} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                // Get max refresh rate and update slider
                int maxRefresh = 60;
                if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                {
                    maxRefresh = refreshRates.Value.Max();
                }
                FPSLimitSlider.Maximum = maxRefresh;

                // If slider is at minimum (15) or below, set to max refresh as default
                int limit = (int)FPSLimitSlider.Value;
                if (limit <= 15)
                {
                    limit = maxRefresh;
                    FPSLimitSlider.Value = limit;
                }

                // Update display text with the final value
                if (FPSLimitValue != null)
                {
                    FPSLimitValue.Text = $"{limit} FPS";
                }

                fpsLimit.SetValue(limit);
                Logger.Info($"FPS Limit enabled: {limit}");
            }
            else
            {
                // Disable FPS limit (0 = unlimited)
                fpsLimit.SetValue(0);
                Logger.Info("FPS Limit disabled");
            }

            // Save to profile if FPS Limit saving is enabled
            // Don't save during DGP restoration - values being restored to original state
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile && !isRestoringFromDefaultProfile)
            {
                SaveCurrentSettingsToProfile(currentProfileName);
            }
        }

        /// <summary>
        /// RSR toggle changed - disable RIS if RSR is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonSuperResolutionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDRadeonSuperResolutionToggle.IsOn && AMDImageSharpeningToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RSR enabled - disabling RIS (mutually exclusive)");
                AMDImageSharpeningToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// RIS toggle changed - disable RSR if RIS is enabled (mutually exclusive)
        /// </summary>
        private void AMDImageSharpeningToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // RSR and RIS are mutually exclusive - enabling one disables the other
            if (AMDImageSharpeningToggle.IsOn && AMDRadeonSuperResolutionToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("RIS enabled - disabling RSR (mutually exclusive)");
                AMDRadeonSuperResolutionToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Anti-Lag toggle changed - disable Chill if Anti-Lag is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonAntiLagToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Anti-Lag and Chill are mutually exclusive
            if (AMDRadeonAntiLagToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Anti-Lag enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Boost toggle changed - disable Chill if Boost is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonBoostToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Boost and Chill are mutually exclusive
            if (AMDRadeonBoostToggle.IsOn && AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                Logger.Info("Boost enabled - disabling Chill (mutually exclusive)");
                AMDRadeonChillToggle.IsOn = false;
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// Radeon Chill toggle changed - disable Anti-Lag and Boost if Chill is enabled (mutually exclusive)
        /// </summary>
        private void AMDRadeonChillToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Chill is mutually exclusive with Anti-Lag and Boost
            if (AMDRadeonChillToggle.IsOn && !isLoadingProfile && !isSwitchingProfile && !isApplyingHelperUpdate)
            {
                if (AMDRadeonAntiLagToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Anti-Lag (mutually exclusive)");
                    AMDRadeonAntiLagToggle.IsOn = false;
                }
                if (AMDRadeonBoostToggle.IsOn)
                {
                    Logger.Info("Chill enabled - disabling Boost (mutually exclusive)");
                    AMDRadeonBoostToggle.IsOn = false;
                }
            }

            // Call the generic setting changed handler
            SettingChanged(sender, e);
        }

        /// <summary>
        /// FPS Limit slider changed - update FPS limit if toggle is on (with debouncing)
        /// </summary>
        private void FPSLimitSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Always update the display text
            if (FPSLimitValue != null)
            {
                FPSLimitValue.Text = $"{(int)e.NewValue} FPS";
            }

            if (fpsLimit == null || isApplyingHelperUpdate) return;

            if (FPSLimitToggle.IsOn)
            {
                int limit = (int)e.NewValue;
                fpsLimitPendingValue = limit;

                // Initialize debounce timer if needed
                if (fpsLimitDebounceTimer == null)
                {
                    fpsLimitDebounceTimer = new DispatcherTimer();
                    fpsLimitDebounceTimer.Interval = TimeSpan.FromMilliseconds(FPS_LIMIT_DEBOUNCE_MS);
                    fpsLimitDebounceTimer.Tick += FPSLimitDebounceTimer_Tick;
                }

                // Restart the debounce timer
                fpsLimitDebounceTimer.Stop();
                fpsLimitDebounceTimer.Start();
            }
        }

        /// <summary>
        /// Debounce timer tick - apply the pending FPS limit value
        /// </summary>
        private void FPSLimitDebounceTimer_Tick(object sender, object e)
        {
            fpsLimitDebounceTimer?.Stop();

            if (fpsLimit != null && FPSLimitToggle.IsOn)
            {
                fpsLimit.SetValue(fpsLimitPendingValue);
                Logger.Info($"FPS Limit changed (debounced): {fpsLimitPendingValue}");

                // Save to profile if FPS Limit saving is enabled
                if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
                {
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status and current fpsLimit value
        /// </summary>
        private void UpdateFPSLimitControls()
        {
            UpdateFPSLimitControls(rtssInstalled?.Value == true);
        }

        /// <summary>
        /// Update FPS Limit controls based on RTSS installed status
        /// </summary>
        private void UpdateFPSLimitControls(bool rtssAvailable)
        {
            // Dispatch to UI thread since this may be called from property callback on non-UI thread
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    if (isUnloading) return;

                    // Guard against null controls during initialization or shutdown
                    if (FPSLimitToggle == null || FPSLimitSlider == null) return;

                    FPSLimitToggle.IsEnabled = rtssAvailable;

                    // Update slider maximum to current refresh rate
                    int maxRefresh = 60; // Default
                    if (refreshRates?.Value != null && refreshRates.Value.Count > 0)
                    {
                        maxRefresh = refreshRates.Value.Max();
                    }
                    FPSLimitSlider.Maximum = maxRefresh;

                    // Set tick frequency based on max refresh rate (show ~5-8 ticks)
                    int tickFreq;
                    if (maxRefresh >= 144)
                        tickFreq = 24;
                    else if (maxRefresh >= 120)
                        tickFreq = 20;
                    else if (maxRefresh >= 90)
                        tickFreq = 15;
                    else
                        tickFreq = 10;
                    FPSLimitSlider.TickFrequency = tickFreq;

                    // Sync toggle/slider with fpsLimit value
                    if (fpsLimit != null)
                    {
                        isApplyingHelperUpdate = true;
                        try
                        {
                            int limit = fpsLimit.Value;
                            if (limit > 0)
                            {
                                FPSLimitToggle.IsOn = true;
                                // Clamp value to slider range
                                FPSLimitSlider.Value = Math.Min(limit, maxRefresh);
                            }
                            else
                            {
                                FPSLimitToggle.IsOn = false;
                            }
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in UpdateFPSLimitControls: {ex.Message}");
                }
            });
        }

        #region OS Power Mode

        private static readonly string[] OSPowerModeNames = { "Best Power Efficiency", "Balanced", "Best Performance" };

        /// <summary>
        /// Called when the OS Power Mode property changes (synced from helper)
        /// </summary>
        private void OSPowerMode_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (isUnloading) return;

                isLoadingOSPowerMode = true;
                try
                {
                    int mode = osPowerMode?.Value ?? 1;
                    if (mode >= 0 && mode < OSPowerModeNames.Length)
                    {
                        OSPowerModeComboBox.SelectedIndex = mode;
                        OSPowerModeValue.Text = OSPowerModeNames[mode];
                    }

                    // Update Quick Settings tile
                    UpdateQuickSettingsTileStates();
                }
                finally
                {
                    isLoadingOSPowerMode = false;
                }
            });
        }

        /// <summary>
        /// Called when user changes the OS Power Mode combo box
        /// </summary>
        private void OSPowerModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSPowerMode || osPowerMode == null) return;

            int selectedIndex = OSPowerModeComboBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < OSPowerModeNames.Length)
            {
                osPowerMode.SetValue(selectedIndex);
                OSPowerModeValue.Text = OSPowerModeNames[selectedIndex];
                Logger.Info($"OS Power Mode changed to: {OSPowerModeNames[selectedIndex]}");

                // Save the change to profile
                if (!isInitialSync && !isApplyingHelperUpdate && !isLoadingProfile && SaveOSPowerMode)
                {
                    Logger.Info($"Saving OS Power Mode change to profile: {currentProfileName}");
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
            }
        }

        #endregion

        private void ToggleLegionTouchpad()
        {
            if (legionGoDetected?.Value == true && legionTouchpadEnabled != null)
            {
                bool newValue = !legionTouchpadEnabled.Value;
                legionTouchpadEnabled.SetValue(newValue);
                Logger.Info($"Legion Touchpad toggled to {newValue}");
            }
        }

        private void CycleLegionLightMode()
        {
            if (legionGoDetected?.Value == true && legionLightMode != null)
            {
                int currentMode = legionLightMode.Value;
                int nextMode = (currentMode + 1) % 5; // 0-4: Off, Static, Breathing, Rainbow, Spiral
                legionLightMode.SetValue(nextMode);
                Logger.Info($"Legion Light Mode cycled from {currentMode} to {nextMode}");
            }
        }

        private void ToggleLegionDesktopControls()
        {
            if (legionGoDetected?.Value == true && LegionDesktopControlsToggle != null)
            {
                bool newValue = !LegionDesktopControlsToggle.IsOn;
                LegionDesktopControlsToggle.IsOn = newValue;
                // The Toggled event handler will apply the mappings
                Logger.Info($"Legion Desktop Controls toggled to {newValue}");
            }
        }

        private void ToggleLegionChargeLimit()
        {
            if (legionGoDetected?.Value == true && legionChargeLimit != null)
            {
                bool newValue = !legionChargeLimit.Value;
                legionChargeLimit.SetValue(newValue);
                // Also update the toggle in Legion tab if it exists
                if (LegionChargeLimitToggle != null)
                {
                    LegionChargeLimitToggle.IsOn = newValue;
                }
                Logger.Info($"Legion Charge Limit toggled to {(newValue ? "80%" : "Off")}");
            }
        }

        private void ToggleRemapControlsProfile()
        {
            if (legionGoDetected?.Value != true)
                return;

            if (LegionControllerProfileToggle == null)
                return;

            // Toggle the per-game controller profile
            LegionControllerProfileToggle.IsOn = !LegionControllerProfileToggle.IsOn;
            Logger.Info($"Toggled per-game controller profile to: {LegionControllerProfileToggle.IsOn}");

            // Update Quick Settings tiles
            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Show/hide customization panel
        /// </summary>
        private void QuickSettingsCustomize_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Enter edit mode
                qsEditMode = true;
                qsSelectedTileForMove = null;

                QuickSettingsCustomizePanel.Visibility = Visibility.Visible;
                QuickSettingsCustomizeButton.Visibility = Visibility.Collapsed;

                // Register keyboard handler for B/Escape to deselect
                QuickSettingsCustomizePanel.KeyDown -= QuickSettingsCustomizePanel_KeyDown;
                QuickSettingsCustomizePanel.KeyDown += QuickSettingsCustomizePanel_KeyDown;

                // Update column button visuals
                UpdateColumnButtonVisuals();

                // Rebuild UIs with edit mode enabled
                BuildSortableGrid();
                RebuildQuickSettingsTiles();  // Shows hidden tiles with overlay in edit mode
            }
        }

        /// <summary>
        /// Handle keyboard input in customize panel (B/Escape to deselect)
        /// </summary>
        private void QuickSettingsCustomizePanel_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape ||
                e.Key == Windows.System.VirtualKey.GamepadB)
            {
                if (qsSelectedTileForMove != null)
                {
                    qsSelectedTileForMove = null;
                    UpdateSelectedTileIndicator(null);
                    BuildSortableGrid();
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Close customization panel
        /// </summary>
        private void QuickSettingsCustomizeDone_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                // Exit edit mode
                qsEditMode = false;
                qsSelectedTileForMove = null;
                UpdateSelectedTileIndicator(null);

                QuickSettingsCustomizePanel.Visibility = Visibility.Collapsed;
                QuickSettingsCustomizeButton.Visibility = Visibility.Visible;

                // Save config and rebuild tiles without edit overlays
                SaveQuickSettingsConfig();
                RebuildQuickSettingsTiles();
                UpdateQuickSettingsTileStates();
            }
        }

        /// <summary>
        /// Set column count to 3
        /// </summary>
        private void ColumnCount3_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 3)
            {
                qsColumnCount = 3;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 4
        /// </summary>
        private void ColumnCount4_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 4)
            {
                qsColumnCount = 4;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Set column count to 5
        /// </summary>
        private void ColumnCount5_Click(object sender, RoutedEventArgs e)
        {
            if (qsColumnCount != 5)
            {
                qsColumnCount = 5;
                UpdateColumnButtonVisuals();
                BuildSortableGrid();
                RebuildQuickSettingsTiles();
            }
        }

        /// <summary>
        /// Update column button visuals to show current selection
        /// </summary>
        private void UpdateColumnButtonVisuals()
        {
            if (Column3Button == null || Column4Button == null || Column5Button == null) return;

            var selectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 180));
            var normalBrush = tileOffBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));

            Column3Button.Background = qsColumnCount == 3 ? selectedBrush : normalBrush;
            Column4Button.Background = qsColumnCount == 4 ? selectedBrush : normalBrush;
            Column5Button.Background = qsColumnCount == 5 ? selectedBrush : normalBrush;
        }

        /// <summary>
        /// Add a custom shortcut tile
        /// </summary>
        private void AddCustomShortcut_Click(object sender, RoutedEventArgs e)
        {
            string name = CustomShortcutNameBox?.Text?.Trim();
            string shortcut = CustomShortcutKeyBox?.Text?.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(shortcut))
            {
                Logger.Warn("Custom shortcut name or shortcut is empty");
                return;
            }

            AddCustomShortcutTile(name, shortcut);

            // Clear input boxes
            if (CustomShortcutNameBox != null) CustomShortcutNameBox.Text = "";
            if (CustomShortcutKeyBox != null) CustomShortcutKeyBox.Text = "";

            UpdateQuickSettingsTileStates();
        }

        /// <summary>
        /// Handle tile visibility checkbox changes
        /// </summary>
        private void TileVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is string tileId)
            {
                bool isVisible = checkBox.IsChecked ?? true;

                if (qsTileMap.TryGetValue(tileId, out var tile))
                {
                    tile.IsVisible = isVisible;
                }
            }
        }

        #endregion

        #region Steam Game Icons

        // Steam library cache: maps install directory paths to Steam App IDs
        // Cached on first use for performance
        private static Dictionary<string, string> _steamInstallCache;
        private static bool _steamCacheInitialized = false;

        /// <summary>
        /// Builds a cache of Steam game installations by parsing appmanifest files.
        /// Maps install directory paths to Steam App IDs.
        /// </summary>
        private static Dictionary<string, string> BuildSteamInstallCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Get all Steam library folders
                var libraryFolders = GetSteamLibraryFolders();

                foreach (var libraryPath in libraryFolders)
                {
                    var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamAppsPath))
                        continue;

                    // Parse each appmanifest_*.acf file
                    var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                    foreach (var manifestFile in manifestFiles)
                    {
                        try
                        {
                            var content = File.ReadAllText(manifestFile);

                            // Parse AppID
                            var appIdMatch = Regex.Match(content, @"""appid""\s+""(\d+)""");
                            if (!appIdMatch.Success) continue;
                            var appId = appIdMatch.Groups[1].Value;

                            // Parse install directory name
                            var installDirMatch = Regex.Match(content, @"""installdir""\s+""([^""]+)""");
                            if (!installDirMatch.Success) continue;
                            var installDir = installDirMatch.Groups[1].Value;

                            // Build full path to game install
                            var fullPath = Path.Combine(steamAppsPath, "common", installDir);
                            if (Directory.Exists(fullPath))
                            {
                                cache[fullPath] = appId;
                            }
                        }
                        catch
                        {
                            // Skip manifest files we can't parse
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Failed to build Steam install cache: {ex.Message}");
            }

            return cache;
        }

        /// <summary>
        /// Gets all Steam library folder paths from libraryfolders.vdf.
        /// </summary>
        private static List<string> GetSteamLibraryFolders()
        {
            var folders = new List<string>();

            // Common Steam install locations
            var steamPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };

            string steamPath = null;
            foreach (var path in steamPaths)
            {
                if (Directory.Exists(path))
                {
                    steamPath = path;
                    break;
                }
            }

            if (steamPath == null)
            {
                return folders;
            }

            // Add the main Steam folder
            folders.Add(steamPath);

            // Parse libraryfolders.vdf for additional library locations
            var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersPath))
            {
                try
                {
                    var content = File.ReadAllText(libraryFoldersPath);

                    // Match "path" entries in the VDF file
                    var pathMatches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match match in pathMatches)
                    {
                        var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(libPath) && !folders.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                        {
                            folders.Add(libPath);
                        }
                    }
                }
                catch
                {
                    // Ignore errors parsing library folders
                }
            }

            return folders;
        }

        /// <summary>
        /// Gets the Steam App ID for a game executable by walking up the directory tree.
        /// </summary>
        private static string GetSteamAppIdFromPath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            // Initialize cache on first use
            if (!_steamCacheInitialized)
            {
                _steamInstallCache = BuildSteamInstallCache();
                _steamCacheInitialized = true;
            }

            if (_steamInstallCache == null || _steamInstallCache.Count == 0)
                return null;

            try
            {
                // Walk up the directory tree to find a cached Steam install path
                var searchDir = Path.GetDirectoryName(exePath);
                while (!string.IsNullOrEmpty(searchDir))
                {
                    if (_steamInstallCache.TryGetValue(searchDir, out var appId))
                    {
                        return appId;
                    }

                    var parent = Directory.GetParent(searchDir);
                    if (parent == null)
                        break;
                    searchDir = parent.FullName;
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Gets the local Steam icon path for a game by its Steam App ID.
        /// Looks in Steam's library cache folder for the game's icon.
        /// </summary>
        private static string GetSteamIconPath(string steamAppId)
        {
            if (string.IsNullOrEmpty(steamAppId))
                return null;

            // Steam caches icons locally - try common Steam install locations
            var steamPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };

            foreach (var steamPath in steamPaths)
            {
                // Steam stores assets in folders named by AppID
                var cacheFolder = Path.Combine(steamPath, "appcache", "librarycache", steamAppId);
                if (!Directory.Exists(cacheFolder))
                    continue;

                try
                {
                    // The icon is a small hash-named .jpg file (typically ~1KB)
                    // Look for the smallest jpg that isn't a known named file
                    var jpgFiles = Directory.GetFiles(cacheFolder, "*.jpg");
                    string iconPath = null;
                    long smallestSize = long.MaxValue;

                    foreach (var file in jpgFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        // Skip known large files
                        if (fileName == "header.jpg" || fileName == "library_600x900.jpg" ||
                            fileName == "library_hero.jpg" || fileName == "library_hero_blur.jpg")
                            continue;

                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Length < smallestSize && fileInfo.Length < 5000) // Icons are typically < 2KB
                        {
                            smallestSize = fileInfo.Length;
                            iconPath = file;
                        }
                    }

                    if (iconPath != null)
                        return iconPath;

                    // Fall back to logo.png (transparent game logo)
                    var logoPath = Path.Combine(cacheFolder, "logo.png");
                    if (File.Exists(logoPath))
                        return logoPath;
                }
                catch
                {
                    // Ignore errors and try next Steam path
                }
            }

            return null;
        }

        /// <summary>
        /// Loads the game icon for the current game and updates the UI.
        /// Uses helper-extracted icon if available, falls back to Steam lookup.
        /// Must be called from background thread - dispatches to UI thread.
        /// </summary>
        /// <param name="exePath">Path to the game executable</param>
        /// <param name="helperIconPath">Optional icon path from helper (extracted via Shell API)</param>
        private async void LoadCurrentGameIcon(string exePath, string helperIconPath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (CurrentGameIcon != null)
                    {
                        CurrentGameIcon.Source = null;
                        CurrentGameIcon.Visibility = Visibility.Collapsed;
                    }
                    if (LegionControllerProfileGameIcon != null)
                    {
                        LegionControllerProfileGameIcon.Source = null;
                        LegionControllerProfileGameIcon.Visibility = Visibility.Collapsed;
                    }
                });
                return;
            }

            try
            {
                Logger.Info($"LoadCurrentGameIcon: Starting for {exePath}");

                string iconPath = null;

                // Priority 1: Use helper-extracted icon if available
                if (!string.IsNullOrEmpty(helperIconPath) && File.Exists(helperIconPath))
                {
                    iconPath = helperIconPath;
                    Logger.Info($"LoadCurrentGameIcon: Using helper icon: {iconPath}");
                }
                else
                {
                    // Priority 2: Fall back to Steam icon lookup
                    var steamAppId = GetSteamAppIdFromPath(exePath);
                    if (!string.IsNullOrEmpty(steamAppId))
                    {
                        Logger.Info($"LoadCurrentGameIcon: Found Steam App ID {steamAppId}");
                        iconPath = GetSteamIconPath(steamAppId);
                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            Logger.Info($"LoadCurrentGameIcon: Using Steam icon: {iconPath}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(iconPath))
                {
                    Logger.Info($"LoadCurrentGameIcon: No icon found for {exePath}");
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        if (CurrentGameIcon != null)
                        {
                            CurrentGameIcon.Source = null;
                            CurrentGameIcon.Visibility = Visibility.Collapsed;
                        }
                        if (LegionControllerProfileGameIcon != null)
                        {
                            LegionControllerProfileGameIcon.Source = null;
                            LegionControllerProfileGameIcon.Visibility = Visibility.Collapsed;
                        }
                    });
                    return;
                }

                // Load icon and update UI - must be done on UI thread
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(iconPath);
                        using (var stream = await file.OpenAsync(FileAccessMode.Read))
                        {
                            var bitmapImage = new BitmapImage();
                            await bitmapImage.SetSourceAsync(stream);

                            if (CurrentGameIcon != null)
                            {
                                CurrentGameIcon.Source = bitmapImage;
                                CurrentGameIcon.Visibility = Visibility.Visible;
                            }
                            if (LegionControllerProfileGameIcon != null)
                            {
                                LegionControllerProfileGameIcon.Source = bitmapImage;
                                LegionControllerProfileGameIcon.Visibility = Visibility.Visible;
                            }
                            Logger.Info($"LoadCurrentGameIcon: Icon loaded successfully");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"LoadCurrentGameIcon: Failed to load bitmap - {ex.Message}");
                        if (CurrentGameIcon != null)
                        {
                            CurrentGameIcon.Source = null;
                            CurrentGameIcon.Visibility = Visibility.Collapsed;
                        }
                        if (LegionControllerProfileGameIcon != null)
                        {
                            LegionControllerProfileGameIcon.Source = null;
                            LegionControllerProfileGameIcon.Visibility = Visibility.Collapsed;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Info($"LoadCurrentGameIcon: Failed - {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the game icon for a saved profile.
        /// Checks helper-extracted cache first, falls back to Steam lookup.
        /// Returns a BitmapImage if found, null otherwise.
        /// </summary>
        private async Task<BitmapImage> LoadSavedProfileIconAsync(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            try
            {
                string iconPath = null;

                // Priority 1: Check helper-extracted icon cache
                var cachedIconPath = GetCachedIconPath(exePath);
                if (!string.IsNullOrEmpty(cachedIconPath) && File.Exists(cachedIconPath))
                {
                    iconPath = cachedIconPath;
                }
                else
                {
                    // Priority 2: Fall back to Steam icon lookup
                    var steamAppId = GetSteamAppIdFromPath(exePath);
                    if (!string.IsNullOrEmpty(steamAppId))
                    {
                        iconPath = GetSteamIconPath(steamAppId);
                    }
                }

                if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                    return null;

                // Load icon on UI thread using TaskCompletionSource
                var tcs = new TaskCompletionSource<BitmapImage>();

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(iconPath);
                        using (var stream = await file.OpenAsync(FileAccessMode.Read))
                        {
                            var bitmapImage = new BitmapImage();
                            await bitmapImage.SetSourceAsync(stream);
                            tcs.TrySetResult(bitmapImage);
                        }
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                });

                return await tcs.Task;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the cached icon path for an executable from the helper's icon cache.
        /// Uses the same MD5 hash-based naming scheme as the helper.
        /// </summary>
        private string GetCachedIconPath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            try
            {
                // Get the icon cache folder (same as helper uses)
                var cacheFolder = Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                    "icons");

                if (!Directory.Exists(cacheFolder))
                    return null;

                // Generate cache filename using same algorithm as helper
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(exePath.ToLowerInvariant()));
                    var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                    var exeName = Path.GetFileNameWithoutExtension(exePath);
                    foreach (var c in Path.GetInvalidFileNameChars())
                    {
                        exeName = exeName.Replace(c, '_');
                    }
                    if (exeName.Length > 32)
                        exeName = exeName.Substring(0, 32);

                    var cacheFileName = $"{exeName}_{hashString.Substring(0, 8)}.png";
                    var cachePath = Path.Combine(cacheFolder, cacheFileName);

                    return File.Exists(cachePath) ? cachePath : null;
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Labs Section

        private void InitializeLabsSection()
        {
            // Create DAService status polling timer (only runs when Legion tab is visible)
            daServiceStatusTimer = new DispatcherTimer();
            daServiceStatusTimer.Interval = TimeSpan.FromSeconds(30);
            daServiceStatusTimer.Tick += (s, e) => UpdateDAServiceStatus();
            // Don't start timer here - it will be started when Legion tab becomes visible

            // Wire up Legion button remap event handlers (done in code to avoid XAML init issues)
            if (LegionLActionComboBox != null)
                LegionLActionComboBox.SelectionChanged += LegionLActionComboBox_SelectionChanged;
            if (LegionRActionComboBox != null)
                LegionRActionComboBox.SelectionChanged += LegionRActionComboBox_SelectionChanged;

            // Wire up Scroll wheel remap event handlers
            if (ScrollActionComboBox != null)
                ScrollActionComboBox.SelectionChanged += ScrollActionComboBox_SelectionChanged;
            if (ScrollClickActionComboBox != null)
                ScrollClickActionComboBox.SelectionChanged += ScrollClickActionComboBox_SelectionChanged;

            // Load saved Legion remap settings
            LoadLegionRemapSettings();

            // Load saved Scroll wheel remap settings
            LoadScrollRemapSettings();

            // Mark Labs section as initialized (enables event handlers)
            labsSectionInitialized = true;

            // Apply saved settings to helper (after connection is established)
            _ = Task.Run(async () =>
            {
                // Wait for helper connection
                for (int i = 0; i < 30 && App.Connection == null; i++)
                    await Task.Delay(200);

                if (App.Connection != null)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ApplyLegionRemapSettingsToHelper();
                        ApplyScrollRemapSettingsToHelper();
                    });
                }
            });
        }

        private async void RequestViGEmBusStatus()
        {
            if (App.Connection == null)
                return;

            // Request ViGEmBus installed status from helper
            try
            {
                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Function", (int)Function.ViGEmBusInstalled);
                var response = await App.Connection.SendMessageAsync(request);

                // Handle response
                if (response.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (response.Message.TryGetValue("Value", out object installedObj))
                    {
                        bool installed = Convert.ToBoolean(installedObj);
                        UpdateViGEmBusInstalledUI(installed);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to request ViGEmBus status: {ex.Message}");
            }
        }

        private async void UpdateDAServiceStatus()
        {
            if (App.Connection == null)
                return;

            // Request DAService status from helper
            try
            {
                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Function", (int)Function.Labs_DAServiceStatus);
                request.Add("Value", 0); // Request status
                var response = await App.Connection.SendMessageAsync(request);

                // Handle response
                if (response.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (response.Message.TryGetValue("Value", out object statusObj))
                    {
                        int status = Convert.ToInt32(statusObj);
                        OnDAServiceStatusReceived(status);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to request DAService status: {ex.Message}");
            }
        }

        private void OnDAServiceStatusReceived(int status)
        {
            // Status: 0 = Stopped/Disabled, 1 = Running, 2 = Not Found
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (DAServiceStatusText == null || ToggleDAServiceButton == null)
                    return;

                switch (status)
                {
                    case 0: // Stopped/Disabled
                        daServiceIsRunning = false;
                        DAServiceStatusText.Text = "Service disabled - Legion L/R buttons disabled";
                        DAServiceStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 200, 83)); // Green
                        ToggleDAServiceButton.Content = "Enable";
                        break;
                    case 1: // Running
                        daServiceIsRunning = true;
                        DAServiceStatusText.Text = "Service running - Legion Space controls buttons";
                        DAServiceStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 170, 0)); // Orange
                        ToggleDAServiceButton.Content = "Disable";
                        break;
                    case 2: // Not Found
                        daServiceIsRunning = false;
                        DAServiceStatusText.Text = "DAService not found on this system";
                        DAServiceStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128)); // Gray
                        ToggleDAServiceButton.IsEnabled = false;
                        break;
                }
            });
        }

        private async void ToggleDAServiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.Connection == null)
                return;

            try
            {
                // Update button text immediately for responsiveness
                ToggleDAServiceButton.Content = "...";
                DAServiceStatusText.Text = daServiceIsRunning ? "Disabling service..." : "Enabling service...";

                // Send start/stop command to helper
                // Value: 0 = Stop, 1 = Start
                int action = daServiceIsRunning ? 0 : 1;
                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Function", (int)Function.Labs_DAServiceControl);
                request.Add("Value", action);
                var response = await App.Connection.SendMessageAsync(request);

                // Handle response - helper sends back updated status
                if (response.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (response.Message.TryGetValue("Value", out object statusObj))
                    {
                        int status = Convert.ToInt32(statusObj);
                        OnDAServiceStatusReceived(status);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to control DAService: {ex.Message}");
                // Reset UI on error
                UpdateDAServiceStatus();
            }
        }

        // Legion L event handlers
        private void LegionLActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!labsSectionInitialized) return;

            int selection = LegionLActionComboBox?.SelectedIndex ?? 0;
            // 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
            bool isShortcut = selection == 2;
            bool isCommand = selection == 3;
            if (LegionLShortcutGrid != null)
                LegionLShortcutGrid.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
            if (LegionLCommandGrid != null)
                LegionLCommandGrid.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;

            // Apply immediately for Disabled, Xbox Guide, or Focus GoTweaks
            if (selection != 2 && selection != 3)
                ApplyLegionButtonConfig(true);

            UpdateLegionRemapDescription();
        }

        private void LegionLShortcutApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyLegionButtonConfig(true);
            UpdateLegionRemapDescription();
        }

        private void LegionLCommandApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyLegionButtonConfig(true);
            UpdateLegionRemapDescription();
        }

        // Legion R event handlers
        private void LegionRActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!labsSectionInitialized) return;

            int selection = LegionRActionComboBox?.SelectedIndex ?? 0;
            // 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
            bool isShortcut = selection == 2;
            bool isCommand = selection == 3;
            if (LegionRShortcutGrid != null)
                LegionRShortcutGrid.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
            if (LegionRCommandGrid != null)
                LegionRCommandGrid.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;

            // Apply immediately for Disabled, Xbox Guide, or Focus GoTweaks
            if (selection != 2 && selection != 3)
                ApplyLegionButtonConfig(false);

            UpdateLegionRemapDescription();
        }

        private void LegionRShortcutApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyLegionButtonConfig(false);
            UpdateLegionRemapDescription();
        }

        private void LegionRCommandApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyLegionButtonConfig(false);
            UpdateLegionRemapDescription();
        }

        private void UpdateLegionRemapDescription()
        {
            if (LegionRemapDescription == null) return;

            int lSelection = LegionLActionComboBox?.SelectedIndex ?? 0;
            int rSelection = LegionRActionComboBox?.SelectedIndex ?? 0;

            // 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
            string lAction = lSelection == 0 ? null :
                             lSelection == 1 ? "Xbox Guide" :
                             lSelection == 2 ? LegionLShortcutTextBox?.Text?.Trim() :
                             lSelection == 3 ? GetCommandDisplayName(LegionLCommandTextBox?.Text?.Trim()) :
                             "GoTweaks";
            string rAction = rSelection == 0 ? null :
                             rSelection == 1 ? "Xbox Guide" :
                             rSelection == 2 ? LegionRShortcutTextBox?.Text?.Trim() :
                             rSelection == 3 ? GetCommandDisplayName(LegionRCommandTextBox?.Text?.Trim()) :
                             "GoTweaks";

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(lAction))
                parts.Add($"L → {lAction}");
            if (!string.IsNullOrEmpty(rAction))
                parts.Add($"R → {rAction}");

            if (parts.Count > 0)
                LegionRemapDescription.Text = string.Join(", ", parts);
            else
                LegionRemapDescription.Text = "Requires ViGEmBus for Xbox Guide";
        }

        private string GetCommandDisplayName(string commandPath)
        {
            if (string.IsNullOrEmpty(commandPath))
                return null;
            // Show just the exe name if it's a path
            try
            {
                var fileName = System.IO.Path.GetFileName(commandPath.Split(' ')[0]);
                return !string.IsNullOrEmpty(fileName) ? fileName : commandPath;
            }
            catch
            {
                return commandPath;
            }
        }

        private void SaveLegionRemapSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["LegionL_Action"] = LegionLActionComboBox?.SelectedIndex ?? 0;
                settings.Values["LegionL_Shortcut"] = LegionLShortcutTextBox?.Text ?? "";
                settings.Values["LegionL_Command"] = LegionLCommandTextBox?.Text ?? "";
                settings.Values["LegionR_Action"] = LegionRActionComboBox?.SelectedIndex ?? 0;
                settings.Values["LegionR_Shortcut"] = LegionRShortcutTextBox?.Text ?? "";
                settings.Values["LegionR_Command"] = LegionRCommandTextBox?.Text ?? "";
                Logger.Info("Legion remap settings saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save Legion remap settings: {ex.Message}");
            }
        }

        private void LoadLegionRemapSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load Legion L settings
                if (settings.Values.TryGetValue("LegionL_Action", out var lAction) && lAction is int lActionInt)
                {
                    if (LegionLActionComboBox != null && lActionInt >= 0 && lActionInt <= 4)
                        LegionLActionComboBox.SelectedIndex = lActionInt;
                }
                if (settings.Values.TryGetValue("LegionL_Shortcut", out var lShortcut) && lShortcut is string lShortcutStr)
                {
                    if (LegionLShortcutTextBox != null)
                        LegionLShortcutTextBox.Text = lShortcutStr;
                }
                if (settings.Values.TryGetValue("LegionL_Command", out var lCommand) && lCommand is string lCommandStr)
                {
                    if (LegionLCommandTextBox != null)
                        LegionLCommandTextBox.Text = lCommandStr;
                }

                // Load Legion R settings
                if (settings.Values.TryGetValue("LegionR_Action", out var rAction) && rAction is int rActionInt)
                {
                    if (LegionRActionComboBox != null && rActionInt >= 0 && rActionInt <= 4)
                        LegionRActionComboBox.SelectedIndex = rActionInt;
                }
                if (settings.Values.TryGetValue("LegionR_Shortcut", out var rShortcut) && rShortcut is string rShortcutStr)
                {
                    if (LegionRShortcutTextBox != null)
                        LegionRShortcutTextBox.Text = rShortcutStr;
                }
                if (settings.Values.TryGetValue("LegionR_Command", out var rCommand) && rCommand is string rCommandStr)
                {
                    if (LegionRCommandTextBox != null)
                        LegionRCommandTextBox.Text = rCommandStr;
                }

                // Update description and show/hide input grids based on loaded settings
                UpdateLegionRemapDescription();
                int lSelectionLoaded = LegionLActionComboBox?.SelectedIndex ?? 0;
                int rSelectionLoaded = LegionRActionComboBox?.SelectedIndex ?? 0;
                if (LegionLShortcutGrid != null)
                    LegionLShortcutGrid.Visibility = (lSelectionLoaded == 2) ? Visibility.Visible : Visibility.Collapsed;
                if (LegionLCommandGrid != null)
                    LegionLCommandGrid.Visibility = (lSelectionLoaded == 3) ? Visibility.Visible : Visibility.Collapsed;
                if (LegionRShortcutGrid != null)
                    LegionRShortcutGrid.Visibility = (rSelectionLoaded == 2) ? Visibility.Visible : Visibility.Collapsed;
                if (LegionRCommandGrid != null)
                    LegionRCommandGrid.Visibility = (rSelectionLoaded == 3) ? Visibility.Visible : Visibility.Collapsed;

                Logger.Info("Legion remap settings loaded");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load Legion remap settings: {ex.Message}");
            }
        }

        private async void ApplyLegionRemapSettingsToHelper()
        {
            // Apply Legion L if not disabled
            if (LegionLActionComboBox?.SelectedIndex > 0)
                ApplyLegionButtonConfig(true);

            // Small delay between requests
            await Task.Delay(100);

            // Apply Legion R if not disabled
            if (LegionRActionComboBox?.SelectedIndex > 0)
                ApplyLegionButtonConfig(false);
        }

        private async void ApplyLegionButtonConfig(bool isLegionL)
        {
            if (App.Connection == null) return;

            try
            {
                ComboBox actionComboBox = isLegionL ? LegionLActionComboBox : LegionRActionComboBox;
                TextBox shortcutTextBox = isLegionL ? LegionLShortcutTextBox : LegionRShortcutTextBox;
                TextBox commandTextBox = isLegionL ? LegionLCommandTextBox : LegionRCommandTextBox;
                string buttonName = isLegionL ? "Legion L" : "Legion R";

                if (actionComboBox == null) return;

                int selection = actionComboBox.SelectedIndex; // 0=Disabled, 1=Xbox Guide, 2=Shortcut, 3=Command, 4=Focus GoTweaks
                bool enabled = selection != 0;
                // Convert UI selection to helper action type: 0=Xbox Guide, 1=Shortcut, 2=Command, 3=Focus GoTweaks
                int actionType = selection == 1 ? 0 : selection == 2 ? 1 : selection == 3 ? 2 : selection == 4 ? 3 : 0;

                string shortcutOrCommand = "";
                if (selection == 2 && shortcutTextBox != null)
                {
                    shortcutOrCommand = shortcutTextBox.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(shortcutOrCommand))
                    {
                        if (LegionRemapStatusText != null)
                        {
                            LegionRemapStatusText.Text = $"{buttonName}: Please enter a shortcut";
                            LegionRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100));
                        }
                        return;
                    }
                }
                else if (selection == 3 && commandTextBox != null)
                {
                    shortcutOrCommand = commandTextBox.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(shortcutOrCommand))
                    {
                        if (LegionRemapStatusText != null)
                        {
                            LegionRemapStatusText.Text = $"{buttonName}: Please enter a command";
                            LegionRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100));
                        }
                        return;
                    }
                }

                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Function", (int)Function.Labs_LegionButtonRemap);
                request.Add("Button", isLegionL ? "L" : "R");
                request.Add("Enabled", enabled);
                request.Add("Action", actionType);
                request.Add("Shortcut", shortcutOrCommand); // Reuse "Shortcut" field for both shortcut and command

                var response = await App.Connection.SendMessageAsync(request);

                if (response.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (response.Message.TryGetValue("Success", out object successObj))
                    {
                        bool success = Convert.ToBoolean(successObj);
                        if (LegionRemapStatusText != null)
                        {
                            if (!enabled)
                            {
                                LegionRemapStatusText.Text = "";
                            }
                            else if (success)
                            {
                                LegionRemapStatusText.Text = "";
                            }
                            else
                            {
                                string errorMsg = actionType == 0 ? "ViGEmBus not installed or controller not found" : "Controller not found";
                                LegionRemapStatusText.Text = $"{buttonName}: Failed - {errorMsg}";
                                LegionRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
                                actionComboBox.SelectedIndex = 0; // Reset to Disabled
                            }
                        }

                        // Save settings on success
                        if (success || !enabled)
                            SaveLegionRemapSettings();

                        Logger.Info($"Legion Button Remap: {buttonName}, Enabled={enabled}, Action={actionType}, Value={shortcutOrCommand}, Success={success}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply Legion button config: {ex.Message}");
            }
        }

        #endregion

        #region Scroll Wheel Remap

        // Scroll (unified) event handlers - direction not available via Raw Input API
        private void ScrollActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!labsSectionInitialized) return;

            int selection = ScrollActionComboBox?.SelectedIndex ?? 0;
            // 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
            bool isShortcut = selection == 2;
            bool isCommand = selection == 3;
            if (ScrollShortcutGrid != null)
                ScrollShortcutGrid.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollCommandGrid != null)
                ScrollCommandGrid.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;

            // Apply immediately for Disabled, Xbox Guide, or Focus GoTweaks
            if (selection != 2 && selection != 3)
                ApplyScrollWheelConfig("Scroll");

            UpdateScrollRemapDescription();
        }

        private void ScrollShortcutApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyScrollWheelConfig("Scroll");
            UpdateScrollRemapDescription();
        }

        private void ScrollCommandApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyScrollWheelConfig("Scroll");
            UpdateScrollRemapDescription();
        }

        // Scroll Click event handlers
        private void ScrollClickActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!labsSectionInitialized) return;

            int selection = ScrollClickActionComboBox?.SelectedIndex ?? 0;
            bool isShortcut = selection == 2;
            bool isCommand = selection == 3;
            if (ScrollClickShortcutGrid != null)
                ScrollClickShortcutGrid.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollClickCommandGrid != null)
                ScrollClickCommandGrid.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;

            if (selection != 2 && selection != 3)
                ApplyScrollWheelConfig("Click");

            UpdateScrollRemapDescription();
        }

        private void ScrollClickShortcutApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyScrollWheelConfig("Click");
            UpdateScrollRemapDescription();
        }

        private void ScrollClickCommandApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyScrollWheelConfig("Click");
            UpdateScrollRemapDescription();
        }

        private void UpdateScrollRemapDescription()
        {
            if (ScrollRemapDescription == null) return;

            int scrollSelection = ScrollActionComboBox?.SelectedIndex ?? 0;
            int clickSelection = ScrollClickActionComboBox?.SelectedIndex ?? 0;

            string scrollAction = scrollSelection == 0 ? null :
                             scrollSelection == 1 ? "Guide" :
                             scrollSelection == 2 ? ScrollShortcutTextBox?.Text?.Trim() :
                             scrollSelection == 3 ? GetCommandDisplayName(ScrollCommandTextBox?.Text?.Trim()) :
                             "GoTweaks";
            string clickAction = clickSelection == 0 ? null :
                             clickSelection == 1 ? "Guide" :
                             clickSelection == 2 ? ScrollClickShortcutTextBox?.Text?.Trim() :
                             clickSelection == 3 ? GetCommandDisplayName(ScrollClickCommandTextBox?.Text?.Trim()) :
                             "GoTweaks";

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(scrollAction))
                parts.Add($"⟳{scrollAction}");
            if (!string.IsNullOrEmpty(clickAction))
                parts.Add($"●{clickAction}");

            if (parts.Count > 0)
                ScrollRemapDescription.Text = string.Join(" ", parts);
            else
                ScrollRemapDescription.Text = "Map back scroll wheel actions (direction not available via Raw Input)";
        }

        private void SaveScrollRemapSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["Scroll_Action"] = ScrollActionComboBox?.SelectedIndex ?? 0;
                settings.Values["Scroll_Shortcut"] = ScrollShortcutTextBox?.Text ?? "";
                settings.Values["Scroll_Command"] = ScrollCommandTextBox?.Text ?? "";
                settings.Values["ScrollClick_Action"] = ScrollClickActionComboBox?.SelectedIndex ?? 0;
                settings.Values["ScrollClick_Shortcut"] = ScrollClickShortcutTextBox?.Text ?? "";
                settings.Values["ScrollClick_Command"] = ScrollClickCommandTextBox?.Text ?? "";
                Logger.Info("Scroll wheel remap settings saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save scroll wheel remap settings: {ex.Message}");
            }
        }

        private void LoadScrollRemapSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load Scroll (unified) settings
                if (settings.Values.TryGetValue("Scroll_Action", out var scrollAction) && scrollAction is int scrollActionInt)
                {
                    if (ScrollActionComboBox != null && scrollActionInt >= 0 && scrollActionInt <= 4)
                        ScrollActionComboBox.SelectedIndex = scrollActionInt;
                }
                if (settings.Values.TryGetValue("Scroll_Shortcut", out var scrollShortcut) && scrollShortcut is string scrollShortcutStr)
                {
                    if (ScrollShortcutTextBox != null)
                        ScrollShortcutTextBox.Text = scrollShortcutStr;
                }
                if (settings.Values.TryGetValue("Scroll_Command", out var scrollCommand) && scrollCommand is string scrollCommandStr)
                {
                    if (ScrollCommandTextBox != null)
                        ScrollCommandTextBox.Text = scrollCommandStr;
                }

                // Load Scroll Click settings
                if (settings.Values.TryGetValue("ScrollClick_Action", out var clickAction) && clickAction is int clickActionInt)
                {
                    if (ScrollClickActionComboBox != null && clickActionInt >= 0 && clickActionInt <= 4)
                        ScrollClickActionComboBox.SelectedIndex = clickActionInt;
                }
                if (settings.Values.TryGetValue("ScrollClick_Shortcut", out var clickShortcut) && clickShortcut is string clickShortcutStr)
                {
                    if (ScrollClickShortcutTextBox != null)
                        ScrollClickShortcutTextBox.Text = clickShortcutStr;
                }
                if (settings.Values.TryGetValue("ScrollClick_Command", out var clickCommand) && clickCommand is string clickCommandStr)
                {
                    if (ScrollClickCommandTextBox != null)
                        ScrollClickCommandTextBox.Text = clickCommandStr;
                }

                // Update visibility of shortcut/command grids based on loaded settings
                UpdateScrollGridVisibility();

                Logger.Info("Scroll wheel remap settings loaded");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load scroll wheel remap settings: {ex.Message}");
            }
        }

        private void UpdateScrollGridVisibility()
        {
            int scrollSelection = ScrollActionComboBox?.SelectedIndex ?? 0;
            if (ScrollShortcutGrid != null)
                ScrollShortcutGrid.Visibility = scrollSelection == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollCommandGrid != null)
                ScrollCommandGrid.Visibility = scrollSelection == 3 ? Visibility.Visible : Visibility.Collapsed;

            int clickSelection = ScrollClickActionComboBox?.SelectedIndex ?? 0;
            if (ScrollClickShortcutGrid != null)
                ScrollClickShortcutGrid.Visibility = clickSelection == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollClickCommandGrid != null)
                ScrollClickCommandGrid.Visibility = clickSelection == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ApplyScrollRemapSettingsToHelper()
        {
            // Apply Scroll (unified) if not disabled
            if (ScrollActionComboBox?.SelectedIndex > 0)
                ApplyScrollWheelConfig("Scroll");

            await Task.Delay(100);

            // Apply Scroll Click if not disabled
            if (ScrollClickActionComboBox?.SelectedIndex > 0)
                ApplyScrollWheelConfig("Click");
        }

        private async void ApplyScrollWheelConfig(string direction)
        {
            if (App.Connection == null) return;

            try
            {
                ComboBox actionComboBox = direction == "Scroll" ? ScrollActionComboBox :
                                          direction == "Click" ? ScrollClickActionComboBox :
                                          ScrollClickActionComboBox;
                TextBox shortcutTextBox = direction == "Scroll" ? ScrollShortcutTextBox :
                                          direction == "Click" ? ScrollClickShortcutTextBox :
                                          ScrollClickShortcutTextBox;
                TextBox commandTextBox = direction == "Scroll" ? ScrollCommandTextBox :
                                         direction == "Click" ? ScrollClickCommandTextBox :
                                         ScrollClickCommandTextBox;
                string actionName = direction == "Scroll" ? "Scroll Wheel" : $"Scroll {direction}";

                if (actionComboBox == null) return;

                int selection = actionComboBox.SelectedIndex; // 0=Disabled, 1=Xbox Guide, 2=Shortcut, 3=Command, 4=Focus GoTweaks
                bool enabled = selection != 0;
                // Convert UI selection to helper action type: 0=Xbox Guide, 1=Shortcut, 2=Command, 3=Focus GoTweaks
                int actionType = selection == 1 ? 0 : selection == 2 ? 1 : selection == 3 ? 2 : selection == 4 ? 3 : 0;

                string shortcutOrCommand = "";
                if (selection == 2 && shortcutTextBox != null)
                {
                    shortcutOrCommand = shortcutTextBox.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(shortcutOrCommand))
                    {
                        if (ScrollRemapStatusText != null)
                        {
                            ScrollRemapStatusText.Text = $"{actionName}: Please enter a shortcut";
                            ScrollRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100));
                        }
                        return;
                    }
                }
                else if (selection == 3 && commandTextBox != null)
                {
                    shortcutOrCommand = commandTextBox.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(shortcutOrCommand))
                    {
                        if (ScrollRemapStatusText != null)
                        {
                            ScrollRemapStatusText.Text = $"{actionName}: Please enter a command";
                            ScrollRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100));
                        }
                        return;
                    }
                }

                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Function", (int)Function.Labs_LegionScrollRemap);
                request.Add("Direction", direction);
                request.Add("Enabled", enabled);
                request.Add("Action", actionType);
                request.Add("Shortcut", shortcutOrCommand);

                var response = await App.Connection.SendMessageAsync(request);

                if (response.Status == Windows.ApplicationModel.AppService.AppServiceResponseStatus.Success)
                {
                    if (response.Message.TryGetValue("Success", out object successObj))
                    {
                        bool success = Convert.ToBoolean(successObj);
                        if (ScrollRemapStatusText != null)
                        {
                            if (!enabled)
                            {
                                ScrollRemapStatusText.Text = "";
                            }
                            else if (success)
                            {
                                ScrollRemapStatusText.Text = "";
                            }
                            else
                            {
                                string errorMsg = actionType == 0 ? "ViGEmBus not installed or controller not found" : "Controller not found";
                                ScrollRemapStatusText.Text = $"{actionName}: Failed - {errorMsg}";
                                ScrollRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
                                actionComboBox.SelectedIndex = 0; // Reset to Disabled
                            }
                        }

                        // Save settings on success
                        if (success || !enabled)
                            SaveScrollRemapSettings();

                        Logger.Info($"Scroll Wheel Remap: {direction}, Enabled={enabled}, Action={actionType}, Value={shortcutOrCommand}, Success={success}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply scroll wheel config: {ex.Message}");
            }
        }

        #endregion
    }
}

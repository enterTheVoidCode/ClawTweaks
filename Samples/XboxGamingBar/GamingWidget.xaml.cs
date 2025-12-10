using Microsoft.Gaming.XboxGameBar;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.QuickSettings;
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
        public bool LimitCPUClock { get; set; } = false;
        public double CPUClockMax { get; set; } = 5500;
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
        // OS Power Mode (0=Best Power Efficiency, 1=Balanced, 2=Best Performance)
        public int OSPowerMode { get; set; } = 1;
        // Legion Performance Mode (1=Quiet, 2=Balanced, 3=Performance, 255=Custom)
        public int LegionPerformanceMode { get; set; } = 2;

        public PerformanceProfile Clone()
        {
            return new PerformanceProfile
            {
                TDP = this.TDP,
                CPUBoost = this.CPUBoost,
                CPUEPP = this.CPUEPP,
                LimitCPUClock = this.LimitCPUClock,
                CPUClockMax = this.CPUClockMax,
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
                OSPowerMode = this.OSPowerMode,
                LegionPerformanceMode = this.LegionPerformanceMode
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

        // Properties
        private readonly OSDProperty osd;
        private readonly TDPProperty tdp;
        private readonly CurrentTDPProperty currentTdp;
        private readonly RunningGameProperty runningGame;
        private readonly PerGameProfileProperty perGameProfile;
        private readonly CPUBoostProperty cpuBoost;
        private readonly CPUEPPProperty cpuEPP;
        private readonly LimitCPUClockProperty limitCPUClock;
        private readonly CPUClockMaxProperty cpuClockMax;
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
        private readonly LegionGyroEnabledProperty legionGyroEnabled;
        private readonly LegionVibrationProperty legionVibration;
        private readonly LegionPowerLightProperty legionPowerLight;
        private readonly LegionChargeLimitProperty legionChargeLimit;

        // Settings properties
        private readonly UseManufacturerWMIProperty useManufacturerWMI;

        // AutoTDP properties
        private readonly AutoTDPEnabledProperty autoTDPEnabled;
        private readonly AutoTDPTargetFPSProperty autoTDPTargetFPS;
        private readonly AutoTDPCurrentFPSProperty autoTDPCurrentFPS;
        private readonly TDPLimitsProperty tdpLimits;
        private readonly CPUCoreConfigProperty cpuCoreConfig;
        private readonly CPUCoreActiveConfigProperty cpuCoreActiveConfig;
        private readonly CoreParkingPercentProperty coreParkingPercent;
        private readonly ForceParkModeProperty forceParkMode;

        // OS Power Mode
        private readonly OSPowerModeProperty osPowerMode;
        private bool isLoadingOSPowerMode = false;

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
        private string currentProfileName = "Global";
        private string currentGameName = "";
        private string currentGameExePath = "";
        private bool isLoadingProfile = false;
        private bool isSwitchingProfile = false;
        private bool isApplyingHelperUpdate = false; // Prevents saves when helper echoes values back
        private bool isInitialSync = true; // Prevents saves during initial app startup sync
        private bool isInternalToggleDisable = false; // Indicates toggle is being disabled internally (game close)
        private int savedLegionPerformanceMode = -1; // Stores Legion mode before per-game profile (-1 = not saved)

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

        // Profile save settings
        private bool SaveTDP => ProfileSaveTDPCheckBox?.IsChecked ?? true;
        private bool SaveCPUBoost => ProfileSaveCPUBoostCheckBox?.IsChecked ?? true;
        private bool SaveCPUEPP => ProfileSaveCPUEPPCheckBox?.IsChecked ?? true;
        private bool SaveLimitCPUClock => ProfileSaveLimitCPUClockCheckBox?.IsChecked ?? true;
        private bool SaveAMDFeatures => ProfileSaveAMDFeaturesCheckBox?.IsChecked ?? false;
        private bool SaveFPSLimit => ProfileSaveFPSLimitCheckBox?.IsChecked ?? false;
        private bool SaveAutoTDP => ProfileSaveAutoTDPCheckBox?.IsChecked ?? false;
        private bool SaveOSPowerMode => ProfileSaveOSPowerModeCheckBox?.IsChecked ?? false;

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
            Logger.Info("GamingWidget constructor called - creating new instance.");

            // Prevent OSD checkbox events from saving during XAML initialization
            isLoadingOSDConfig = true;
            // Prevent profile settings checkbox events from saving during XAML initialization
            isLoadingProfileSettings = true;
            // Prevent TDP limits slider events from saving during XAML initialization
            isLoadingTDPLimits = true;

            InitializeComponent();

            // Register for lifecycle events
            this.Loaded += GamingWidget_Loaded;
            this.Unloaded += GamingWidget_Unloaded;
            Logger.Info("Registered Loaded and Unloaded event handlers.");

            // Register for LT/RT tab navigation
            this.KeyDown += GamingWidget_KeyDown;

            tdp = new TDPProperty(4, TDPSlider, this);
            currentTdp = new CurrentTDPProperty(CurrentTDPValueText, this);
            osd = new OSDProperty(0, PerformanceOverlaySlider, this);
            runningGame = new RunningGameProperty(RunningGameText, PerGameProfileToggle, DetectedGameText, this);
            runningGame.SetNavigationReferences(PerformanceNavItem, PerformanceOverlayComboBox);
            perGameProfile = new PerGameProfileProperty(PerGameProfileToggle, this);
            cpuBoost = new CPUBoostProperty(CPUBoostToggle, this);
            cpuEPP = new CPUEPPProperty(80, CPUEPPSlider, this);
            limitCPUClock = new LimitCPUClockProperty(LimitCPUClockToggle, this);
            cpuClockMax = new CPUClockMaxProperty(CPUClockMaxSlider, this);
            // GPU Clock - DISABLED: Not supported by RyzenAdj on this hardware (returns error -1)
            //limitGPUClock = new LimitGPUClockProperty(LimitGPUClockToggle, this);
            //gpuClockMin = new GPUClockMinProperty(GPUClockMinSlider, this);
            //gpuClockMax = new GPUClockMaxProperty(GPUClockMaxSlider, this);
            refreshRates = new RefreshRatesProperty(RefreshRatesComboBox, this);
            refreshRate = new RefreshRateProperty(RefreshRatesComboBox, this);
            resolutions = new ResolutionsProperty(ResolutionComboBox, this);
            resolution = new ResolutionProperty(ResolutionComboBox, this);
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
            losslessScalingInstalled = new LosslessScalingInstalledProperty();
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
            legionGyroEnabled = new LegionGyroEnabledProperty(null, this); // Gyro removed from UI, kept for backwards compatibility
            legionVibration = new LegionVibrationProperty(LegionVibrationComboBox, this);
            legionPowerLight = new LegionPowerLightProperty(LegionPowerLightToggle, this);
            legionChargeLimit = new LegionChargeLimitProperty(LegionChargeLimitToggle, this);

            // Settings properties
            useManufacturerWMI = new UseManufacturerWMIProperty(UseManufacturerWMIToggle, this);

            // AutoTDP properties
            autoTDPEnabled = new AutoTDPEnabledProperty(false);
            autoTDPTargetFPS = new AutoTDPTargetFPSProperty(60);
            autoTDPCurrentFPS = new AutoTDPCurrentFPSProperty(0);
            tdpLimits = new TDPLimitsProperty("4,35");
            cpuCoreConfig = new CPUCoreConfigProperty("");
            cpuCoreActiveConfig = new CPUCoreActiveConfigProperty("");
            coreParkingPercent = new CoreParkingPercentProperty(100); // 100% = all cores active
            forceParkMode = new ForceParkModeProperty(false);

            // OS Power Mode property
            osPowerMode = new OSPowerModeProperty();

            // FPS Limit property
            fpsLimit = new FPSLimitProperty();

            // Set up Legion tab visibility callback
            legionGoDetected.SetVisibilityCallback(SetLegionTabVisibility);

            // Set up custom TDP visibility callback
            legionPerformanceMode.SetCustomTDPVisibilityCallback(SetCustomTDPVisibility);

            // NOTE: Event handlers for Chill FPS will be registered AFTER first sync
            // to avoid crash when binding evaluates RadeonChillOnText before both values are ready
            // See RegisterChillFPSHandlers() called after sync completes

            properties = new WidgetProperties(
                osd,
                tdp,
                runningGame,
                perGameProfile,
                cpuBoost,
                cpuEPP,
                limitCPUClock,
                cpuClockMax,
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
                legionGyroEnabled,
                legionVibration,
                legionPowerLight,
                legionChargeLimit,
                useManufacturerWMI,
                autoTDPEnabled,
                autoTDPTargetFPS,
                autoTDPCurrentFPS,
                fpsLimit,
                osPowerMode,
                tdpLimits,
                cpuCoreConfig,
                cpuCoreActiveConfig,
                coreParkingPercent,
                forceParkMode
            );

            // Register card focus handlers for all interactive controls
            RegisterCardFocusHandlers();
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

            // Performance tab - Limit CPU Clock card
            LimitCPUClockToggle.GotFocus += Control_GotFocus;
            LimitCPUClockToggle.LostFocus += Control_LostFocus;
            CPUClockMaxSlider.GotFocus += Control_GotFocus;
            CPUClockMaxSlider.LostFocus += Control_LostFocus;

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
            ProfileSaveLimitCPUClockCheckBox.GotFocus += StandaloneControl_GotFocus;
            ProfileSaveAMDFeaturesCheckBox.GotFocus += StandaloneControl_GotFocus;

            // System tab - Sticky TDP card
            StickyTDPToggle.GotFocus += Control_GotFocus;
            StickyTDPToggle.LostFocus += Control_LostFocus;
            StickyTDPIntervalSlider.GotFocus += Control_GotFocus;
            StickyTDPIntervalSlider.LostFocus += Control_LostFocus;

            // System tab - Manufacturer WMI TDP card
            UseManufacturerWMIToggle.GotFocus += Control_GotFocus;
            UseManufacturerWMIToggle.LostFocus += Control_LostFocus;

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

            // Load profiles from storage
            LoadProfileFromStorage("Global", globalProfile);
            LoadProfileFromStorage("AC", acProfile);
            LoadProfileFromStorage("DC", dcProfile);

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

            // Subscribe to settings changes for auto-save
            SubscribeToSettingsChanges();

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

            // Subscribe to property changes that affect Quick Settings tiles
            SubscribeToQuickSettingsPropertyChanges();

            // Load OSD customization settings
            LoadOSDConfigFromStorage();
            LoadOSDOptionsForLevel(1); // Load Basic level options by default

            // Load Performance Overlay setting
            LoadPerformanceOverlaySetting();

            // Load Power Plan settings
            LoadPowerPlanSettings();

            // Send OSD config to helper on startup
            SendOSDConfigToHelper();
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
                resolution.PropertyChanged += QuickSettingsProperty_Changed;
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
                if (PerGameProfileToggle.IsOn)
                {
                    // Per-game profiles enabled - only proceed if we have a valid game
                    if (HasValidGame(currentGameName))
                    {
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
                    // But ALLOW internal disables (when game closes)
                    if (HasValidGame(currentGameName) && isApplyingHelperUpdate && !isInternalToggleDisable)
                    {
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

                        if (hasExistingProfile)
                        {
                            Logger.Info($"Ignoring helper request to disable toggle - game '{currentGameName}' has saved profile");
                            PerGameProfileToggle.IsOn = true; // Keep it on
                            return;
                        }
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

                    // Auto-enable per-game toggle if profile exists OR if it's already on
                    if (hasExistingProfile || (PerGameProfileToggle?.IsOn == true))
                    {
                        if (!PerGameProfileToggle.IsOn)
                        {
                            Logger.Info($"Auto-enabling per-game profile for {currentGameName}");
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
            LimitCPUClockToggle.Toggled += SettingChanged;
            CPUClockMaxSlider.ValueChanged += SettingChanged;
            FPSLimitToggle.Toggled += FPSLimitToggle_Toggled;
            FPSLimitSlider.ValueChanged += FPSLimitSlider_ValueChanged;

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

            // Update Limit CPU Clock display text
            if ((sender == CPUClockMaxSlider || sender == LimitCPUClockToggle) && LimitCPUClockValue != null)
            {
                LimitCPUClockValue.Text = $"{(int)CPUClockMaxSlider.Value} MHz";
            }

            // Don't save during profile loading, switching, initial sync, or when helper is updating values
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate || isInitialSync)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate}, initialSync={isInitialSync})");
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
                            Logger.Info("AMD Overlay toggled OFF via ComboBox");
                        }
                        else if (index > 0 && amdOverlayLevel == 0)
                        {
                            // Turn on AMD overlay (starts at level 1)
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 1;
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
            Logger.Info($"StickyTDPToggle toggled to: {StickyTDPToggle.IsOn}");

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
        }

        private void StickyTDPIntervalSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickyTDPIntervalSlider == null) return;

            stickyTDPCheckIntervalSeconds = (int)Math.Round(e.NewValue);
            Logger.Info($"Sticky TDP check interval changed to: {stickyTDPCheckIntervalSeconds}s");

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

            Logger.Info($"AutoTDP toggled to: {AutoTDPToggle.IsOn}");

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
            // When AutoTDP is off, focus down goes to UseManufacturerWMIToggle
            if (AutoTDPToggle.IsOn && AutoTDPTargetFPSSlider != null)
            {
                AutoTDPToggle.XYFocusDown = AutoTDPTargetFPSSlider;
            }
            else if (UseManufacturerWMIToggle != null)
            {
                AutoTDPToggle.XYFocusDown = UseManufacturerWMIToggle;
            }
        }

        private void AutoTDPTargetFPSSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (AutoTDPTargetFPSSlider == null) return;
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

        private void LoadAutoTDPSettings()
        {
            isLoadingAutoTDPSettings = true;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;

                // Load enabled state
                if (settings.Values.TryGetValue("AutoTDPEnabled", out object enabledObj) && enabledObj is bool enabled)
                {
                    AutoTDPToggle.IsOn = enabled;
                }

                // Load target FPS
                if (settings.Values.TryGetValue("AutoTDPTargetFPS", out object targetObj) && targetObj is int target)
                {
                    AutoTDPTargetFPSSlider.Value = target;
                    AutoTDPTargetFPSValue.Text = $"{target} FPS";
                }

                // Update focus navigation after loading settings
                UpdateAutoTDPFocusNavigation();
            }
            finally
            {
                isLoadingAutoTDPSettings = false;
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
            { 1, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "Memory", false }, { "VRAM", false }, { "CPU", false }, { "CPUClock", false }, { "GPU", false }, { "GPUClock", false }, { "Fan", false }, { "AutoTDP", false } } },
            { 2, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "Memory", false }, { "VRAM", false }, { "CPU", true }, { "CPUClock", false }, { "GPU", true }, { "GPUClock", false }, { "Fan", true }, { "AutoTDP", false } } },
            { 3, new Dictionary<string, bool> { { "AppName", true }, { "Time", true }, { "FPS", true }, { "Battery", true }, { "Memory", true }, { "VRAM", true }, { "CPU", true }, { "CPUClock", true }, { "GPU", true }, { "GPUClock", true }, { "Fan", true }, { "AutoTDP", true } } }
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

        // Global OSD layout settings
        private int osdTextSize = 100;    // Percentage: 50=Small, 100=Medium, 150=Large, 200=X-Large
        private string osdTextColor = "DYNAMIC";  // DYNAMIC = value-based colors, or hex color code
        private int osdProvider = 0;  // 0=RTSS, 1=AMD
        private int amdOverlayLevel = 0;  // Track AMD overlay level: 0=Off, 1-4=Level 1-4 (can't query from AMD)
        private bool isOSDCustomizeExpanded = false;
        private bool isProfileSettingsExpanded = false;
        private bool isTDPLimitsExpanded = false;
        private bool isPowerPlanExpanded = false;
        private bool isColorSettingsExpanded = false;
        private bool isLoadingTDPLimits = false;
        private bool isLoadingPowerPlans = false;
        private List<PowerPlanItem> availablePowerPlans = new List<PowerPlanItem>();
        private Guid acPowerPlanGuid = Guid.Empty;
        private Guid dcPowerPlanGuid = Guid.Empty;
        private bool powerPlanAutoSwitch = true;
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
                        // Enable AMD overlay (send Ctrl+Shift+O)
                        SendAMDOverlayToggle();
                        amdOverlayLevel = 1;  // Start at level 1
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

        private void SendAMDOverlayToggle()
        {
            // Send Ctrl+Shift+O to toggle AMD Adrenaline's metrics overlay on/off
            try
            {
                QuickSettings.KeyboardShortcutHelper.SendShortcut("Ctrl+Shift+O");
                Logger.Info("Sent AMD overlay toggle hotkey (Ctrl+Shift+O)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending AMD overlay toggle: {ex.Message}");
            }
        }

        private void CycleAMDOverlayLevel()
        {
            // Send Ctrl+Shift+X to cycle AMD Adrenaline's metrics overlay levels
            try
            {
                QuickSettings.KeyboardShortcutHelper.SendShortcut("Ctrl+Shift+X");
                Logger.Info("Sent AMD overlay cycle hotkey (Ctrl+Shift+X)");
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
                var config = osdLevelConfig[level];

                if (OSDShowAppNameCheckBox != null) OSDShowAppNameCheckBox.IsChecked = config.GetValueOrDefault("AppName", false);
                if (OSDShowTimeCheckBox != null) OSDShowTimeCheckBox.IsChecked = config.GetValueOrDefault("Time", false);
                if (OSDShowFPSCheckBox != null) OSDShowFPSCheckBox.IsChecked = config.GetValueOrDefault("FPS", true);
                if (OSDShowBatteryCheckBox != null) OSDShowBatteryCheckBox.IsChecked = config.GetValueOrDefault("Battery", true);
                if (OSDShowMemoryCheckBox != null) OSDShowMemoryCheckBox.IsChecked = config.GetValueOrDefault("Memory", false);
                if (OSDShowVRAMCheckBox != null) OSDShowVRAMCheckBox.IsChecked = config.GetValueOrDefault("VRAM", false);
                if (OSDShowCPUCheckBox != null) OSDShowCPUCheckBox.IsChecked = config.GetValueOrDefault("CPU", false);
                if (OSDShowCPUClockCheckBox != null) OSDShowCPUClockCheckBox.IsChecked = config.GetValueOrDefault("CPUClock", false);
                if (OSDShowGPUCheckBox != null) OSDShowGPUCheckBox.IsChecked = config.GetValueOrDefault("GPU", false);
                if (OSDShowGPUClockCheckBox != null) OSDShowGPUClockCheckBox.IsChecked = config.GetValueOrDefault("GPUClock", false);
                if (OSDShowFanCheckBox != null) OSDShowFanCheckBox.IsChecked = config.GetValueOrDefault("Fan", false);
                if (OSDShowAutoTDPCheckBox != null) OSDShowAutoTDPCheckBox.IsChecked = config.GetValueOrDefault("AutoTDP", false);

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

        private void OSDCustomTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            SaveCurrentOSDConfig();
        }

        private void SaveCurrentOSDConfig()
        {
            if (OSDCustomizeLevelComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int level))
                {
                    if (!osdLevelConfig.ContainsKey(level))
                    {
                        osdLevelConfig[level] = new Dictionary<string, bool>();
                    }

                    var config = osdLevelConfig[level];
                    config["AppName"] = OSDShowAppNameCheckBox?.IsChecked ?? false;
                    config["Time"] = OSDShowTimeCheckBox?.IsChecked ?? false;
                    config["FPS"] = OSDShowFPSCheckBox?.IsChecked ?? true;
                    config["Battery"] = OSDShowBatteryCheckBox?.IsChecked ?? true;
                    config["Memory"] = OSDShowMemoryCheckBox?.IsChecked ?? false;
                    config["VRAM"] = OSDShowVRAMCheckBox?.IsChecked ?? false;
                    config["CPU"] = OSDShowCPUCheckBox?.IsChecked ?? false;
                    config["CPUClock"] = OSDShowCPUClockCheckBox?.IsChecked ?? false;
                    config["GPU"] = OSDShowGPUCheckBox?.IsChecked ?? false;
                    config["GPUClock"] = OSDShowGPUClockCheckBox?.IsChecked ?? false;
                    config["Fan"] = OSDShowFanCheckBox?.IsChecked ?? false;
                    config["AutoTDP"] = OSDShowAutoTDPCheckBox?.IsChecked ?? false;

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
            }
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
                }

                // Save global layout settings
                settings.Values["OSD_TextSize"] = osdTextSize;
                settings.Values["OSD_TextColor"] = osdTextColor;

                Logger.Info("OSD configuration saved to storage");
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
                var itemKeys = new[] { "AppName", "Time", "FPS", "Battery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "Fan", "AutoTDP" };

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
                }

                // Load global layout settings
                if (settings.Values.TryGetValue("OSD_TextSize", out object sizeVal) && sizeVal is int size)
                {
                    osdTextSize = size;
                }
                if (settings.Values.TryGetValue("OSD_TextColor", out object textColorVal) && textColorVal is string textColor)
                {
                    osdTextColor = textColor;
                }
                if (settings.Values.TryGetValue("OSD_Provider", out object providerVal) && providerVal is int provider)
                {
                    osdProvider = provider;
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
                OSDCustomizeExpandIcon.Text = isOSDCustomizeExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ProfileSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isProfileSettingsExpanded = !isProfileSettingsExpanded;

            if (ProfileSettingsContent != null)
            {
                ProfileSettingsContent.Visibility = isProfileSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileSettingsExpandIcon.Text = isProfileSettingsExpanded ? "\uE70E" : "\uE70D";
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
                TDPLimitsExpandIcon.Text = isTDPLimitsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void PowerPlanExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isPowerPlanExpanded = !isPowerPlanExpanded;

            if (PowerPlanOptionsPanel != null)
            {
                PowerPlanOptionsPanel.Visibility = isPowerPlanExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PowerPlanExpandIcon != null)
            {
                PowerPlanExpandIcon.Glyph = isPowerPlanExpanded ? "\uE70E" : "\uE76C";
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

            powerPlanAutoSwitch = PowerPlanAutoSwitchToggle?.IsOn ?? true;
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

                if (settings.Values.TryGetValue("PowerPlan_AutoSwitch", out object autoVal) && autoVal is bool autoSwitch)
                {
                    powerPlanAutoSwitch = autoSwitch;
                }

                // Note: If GUIDs are empty, LoadPowerPlans() will use the current active plan as default

                Logger.Info($"Power plan settings loaded: AC={acPowerPlanGuid}, DC={dcPowerPlanGuid}, AutoSwitch={powerPlanAutoSwitch}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading power plan settings: {ex.Message}");
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
                AdvancedExpandIcon.Text = isAdvancedExpanded ? "\uE70E" : "\uE70D";
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

        private void OSDColorOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            // Get text color
            if (OSDTextColorComboBox?.SelectedItem is ComboBoxItem textItem && textItem.Tag is string textTag)
            {
                osdTextColor = textTag;

                // Update preview
                if (OSDTextColorPreview != null)
                {
                    try
                    {
                        if (textTag == "DYNAMIC")
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
                        else
                        {
                            var color = Windows.UI.Color.FromArgb(255,
                                Convert.ToByte(textTag.Substring(0, 2), 16),
                                Convert.ToByte(textTag.Substring(2, 2), 16),
                                Convert.ToByte(textTag.Substring(4, 2), 16));
                            OSDTextColorPreview.Background = new SolidColorBrush(color);
                        }
                    }
                    catch { }
                }
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
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

                // Set text color combobox and preview
                if (OSDTextColorComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDTextColorComboBox.Items)
                    {
                        if (item.Tag is string tag && tag == osdTextColor)
                        {
                            OSDTextColorComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                if (OSDTextColorPreview != null)
                {
                    try
                    {
                        if (osdTextColor == "DYNAMIC")
                        {
                            // Show gradient for dynamic color preview
                            var gradient = new LinearGradientBrush();
                            gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                            gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                            gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 128, 255), Offset = 0 });
                            gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 0.33 });
                            gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 255, 0), Offset = 0.66 });
                            gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 0, 0), Offset = 1 });
                            OSDTextColorPreview.Background = gradient;
                        }
                        else
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
                bool isLegionCustomMode = legionGoDetected?.Value == true && legionPerformanceMode?.Value == 255;
                bool powerSourceProfileEnabled = PowerSourceProfileToggle?.IsOn == true;

                if (isLegionCustomMode || powerSourceProfileEnabled)
                {
                    SchedulePowerSourceTdpReapply();
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
                // Store current TDP value from Performance tab slider
                int pendingTdpValue = (int)TDPSlider.Value;

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

                    // Skip TDP reapply if not in Custom mode - preset modes manage TDP automatically
                    if (legionGoDetected?.Value == true && legionPerformanceMode?.Value != 255)
                    {
                        Logger.Info($"Power source change: Skipping TDP reapply - using {GetLegionModeShortName(legionPerformanceMode?.Value ?? 0)} preset mode");
                        return;
                    }

                    // Reapply TDP - use the Performance tab TDP value
                    if (tdp != null)
                    {
                        // Set guard flag to prevent saving TDP-1 to profile
                        isApplyingHelperUpdate = true;
                        try
                        {
                            // Force reapply by sending different value to helper first, then the real value
                            // This ensures the helper doesn't skip due to "equals current value"
                            tdp.SetValue(pendingTdpValue - 1);
                            await System.Threading.Tasks.Task.Delay(100);
                            tdp.SetValue(pendingTdpValue);
                            Logger.Info($"Power source change: Reapplied TDP {pendingTdpValue}W after 5 seconds");
                        }
                        finally
                        {
                            isApplyingHelperUpdate = false;
                        }
                    }
                };
                powerSourceTdpReapplyTimer.Start();
                Logger.Info($"Power source change: Scheduled TDP reapply in 5 seconds (TDP={pendingTdpValue}W)");
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

            // Switch profile if needed
            SwitchProfile();
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

                    // Load settings from new profile
                    LoadProfileSettings(currentProfileName);
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
            // Don't save during helper updates - prevents race conditions
            if (isApplyingHelperUpdate)
            {
                Logger.Debug($"Skipping profile save for {profileName} - isApplyingHelperUpdate is true");
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
            if (SaveTDP)
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
            if (SaveCPUBoost)
            {
                profile.CPUBoost = CPUBoostToggle.IsOn;
            }
            if (SaveCPUEPP)
            {
                profile.CPUEPP = CPUEPPSlider.Value;
            }
            if (SaveLimitCPUClock)
            {
                profile.LimitCPUClock = LimitCPUClockToggle.IsOn;
                profile.CPUClockMax = CPUClockMaxSlider.Value;
            }
            if (SaveAMDFeatures)
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
            if (SaveFPSLimit)
            {
                profile.FPSLimitEnabled = FPSLimitToggle.IsOn;
                profile.FPSLimitValue = (int)FPSLimitSlider.Value;
            }
            if (SaveAutoTDP)
            {
                profile.AutoTDPEnabled = AutoTDPToggle.IsOn;
                profile.AutoTDPTargetFPS = (int)AutoTDPTargetFPSSlider.Value;
            }
            if (SaveOSPowerMode)
            {
                profile.OSPowerMode = OSPowerModeComboBox.SelectedIndex;
            }

            // Persist to storage
            Logger.Info($"Saving profile {profileName}: TDP={profile.TDP}W");
            SaveProfileToStorage(profileName, profile);

            // Update profile display
            UpdateProfileDisplay();
        }

        private void LoadProfileSettings(string profileName)
        {
            if (isLoadingProfile) return;
            isLoadingProfile = true;

            try
            {
                var profile = GetProfile(profileName);

                // Apply only enabled settings to UI controls
                if (SaveTDP)
                {
                    TDPSlider.Value = profile.TDP;
                    // Send to helper explicitly using ForceSetValue to ensure hardware TDP is updated
                    // even if cached value matches (important when switching TDP modes on Legion)
                    tdp?.ForceSetValue((int)profile.TDP);
                    // Update Sticky TDP target when loading profile
                    if (StickyTDPToggle?.IsOn == true)
                    {
                        targetTDPLimit = profile.TDP;
                        Logger.Info($"Sticky TDP target updated to: {targetTDPLimit}W (profile load)");
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
                    cpuEPP?.SetValue((int)profile.CPUEPP);
                }
                if (SaveLimitCPUClock)
                {
                    LimitCPUClockToggle.IsOn = profile.LimitCPUClock;
                    CPUClockMaxSlider.Value = profile.CPUClockMax;
                    // Send to helper explicitly (cast to int for property type)
                    limitCPUClock?.SetValue(profile.LimitCPUClock);
                    cpuClockMax?.SetValue((int)profile.CPUClockMax);
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
                    AutoTDPToggle.IsOn = profile.AutoTDPEnabled;
                    AutoTDPTargetFPSSlider.Value = profile.AutoTDPTargetFPS;
                    // Update text display explicitly
                    if (AutoTDPTargetFPSValue != null)
                    {
                        AutoTDPTargetFPSValue.Text = $"{profile.AutoTDPTargetFPS} FPS";
                    }
                    // Send to helper explicitly (toggle/slider handlers may be blocked by flags)
                    autoTDPEnabled?.SetValue(profile.AutoTDPEnabled);
                    autoTDPTargetFPS?.SetValue(profile.AutoTDPTargetFPS);
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
                Logger.Info($"LoadProfileSettings Legion check: legionGoDetected={legionGoDetected?.Value}, LegionPerformanceModeComboBox={LegionPerformanceModeComboBox != null}, TDPModeComboBox={TDPModeComboBox != null}");
                if (legionGoDetected?.Value == true && LegionPerformanceModeComboBox != null && TDPModeComboBox != null)
                {
                    int[] modeValues = { 1, 2, 3, 255 }; // Quiet, Balanced, Performance, Custom

                    if (profileName.StartsWith("Game_"))
                    {
                        // Loading a game profile: save the GLOBAL PROFILE's TDP mode (not the current UI state)
                        // This ensures we restore to the intended global profile mode, not whatever the helper
                        // may have changed it to
                        if (savedLegionPerformanceMode < 0)
                        {
                            // Get the global profile's saved TDP mode, not the current combobox state
                            savedLegionPerformanceMode = globalProfile.LegionPerformanceMode;
                            Logger.Info($"Saved Legion Performance Mode from global profile: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) before game profile");
                        }

                        // Apply game profile's TDP Mode if SaveTDP is enabled
                        if (SaveTDP)
                        {
                            int profileMode = profile.LegionPerformanceMode;
                            int modeIndex = Array.IndexOf(modeValues, profileMode);
                            if (modeIndex >= 0 && (legionPerformanceMode.Value != profileMode || TDPModeComboBox.SelectedIndex != modeIndex))
                            {
                                if (LegionPerformanceModeComboBox.SelectedIndex != modeIndex)
                                    LegionPerformanceModeComboBox.SelectedIndex = modeIndex;
                                if (TDPModeComboBox.SelectedIndex != modeIndex)
                                {
                                    lastTDPModeIndex = modeIndex;
                                    TDPModeComboBox.SelectedIndex = modeIndex;
                                }
                                legionPerformanceMode?.ForceSetValue(profileMode);
                                Logger.Info($"Applied game profile TDP Mode: {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName}");
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
                            Logger.Info($"Restored Legion Performance Mode: {GetLegionModeShortName(savedLegionPerformanceMode)} ({savedLegionPerformanceMode}) after game closed");
                        }
                        // Also restore the TDP slider to the profile's TDP value
                        // This is needed because the slider may still show the game profile's TDP
                        if (SaveTDP && TDPSlider.Value != profile.TDP)
                        {
                            TDPSlider.Value = profile.TDP;
                            Logger.Info($"Restored TDP slider to {profile.TDP}W after game closed");
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
                        if (modeIndex >= 0)
                        {
                            if (LegionPerformanceModeComboBox.SelectedIndex != modeIndex)
                                LegionPerformanceModeComboBox.SelectedIndex = modeIndex;
                            if (TDPModeComboBox.SelectedIndex != modeIndex)
                            {
                                lastTDPModeIndex = modeIndex;
                                TDPModeComboBox.SelectedIndex = modeIndex;
                            }
                            legionPerformanceMode?.ForceSetValue(profileMode);
                            Logger.Info($"Applied profile TDP Mode: {GetLegionModeShortName(profileMode)} ({profileMode}) for {profileName}");
                        }
                    }

                    // Update TDP slider enabled state based on mode
                    UpdateTDPSliderEnabledState();
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
            container.Values["LimitCPUClock"] = profile.LimitCPUClock;
            container.Values["CPUClockMax"] = profile.CPUClockMax;
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
            container.Values["OSPowerMode"] = profile.OSPowerMode;
            container.Values["LegionPerformanceMode"] = profile.LegionPerformanceMode;
        }

        private void LoadProfileFromStorage(string profileName, PerformanceProfile profile)
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey($"Profile_{profileName}"))
            {
                var container = settings.Containers[$"Profile_{profileName}"];

                profile.TDP = container.Values.ContainsKey("TDP") ? (double)container.Values["TDP"] : 15;
                profile.CPUBoost = container.Values.ContainsKey("CPUBoost") ? (bool)container.Values["CPUBoost"] : false;
                profile.CPUEPP = container.Values.ContainsKey("CPUEPP") ? (double)container.Values["CPUEPP"] : 0;
                profile.LimitCPUClock = container.Values.ContainsKey("LimitCPUClock") ? (bool)container.Values["LimitCPUClock"] : false;
                profile.CPUClockMax = container.Values.ContainsKey("CPUClockMax") ? (double)container.Values["CPUClockMax"] : 5500;
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
                profile.OSPowerMode = container.Values.ContainsKey("OSPowerMode") ? (int)container.Values["OSPowerMode"] : 1;
                profile.LegionPerformanceMode = container.Values.ContainsKey("LegionPerformanceMode") ? (int)container.Values["LegionPerformanceMode"] : 2;

                Logger.Info($"Loaded {profileName} profile from storage");
            }
        }

        private void UpdateProfileDisplay()
        {
            // Determine visibility based on save settings
            var tdpModeVisibility = (legionGoDetected?.Value == true && SaveTDP) ? Visibility.Visible : Visibility.Collapsed;
            var tdpVisibility = SaveTDP ? Visibility.Visible : Visibility.Collapsed;
            var cpuBoostVisibility = SaveCPUBoost ? Visibility.Visible : Visibility.Collapsed;
            var cpuEPPVisibility = SaveCPUEPP ? Visibility.Visible : Visibility.Collapsed;
            var cpuClockVisibility = SaveLimitCPUClock ? Visibility.Visible : Visibility.Collapsed;
            var fpsLimitVisibility = SaveFPSLimit ? Visibility.Visible : Visibility.Collapsed;
            var autoTDPVisibility = SaveAutoTDP ? Visibility.Visible : Visibility.Collapsed;
            var powerModeVisibility = SaveOSPowerMode ? Visibility.Visible : Visibility.Collapsed;
            var amdVisibility = SaveAMDFeatures ? Visibility.Visible : Visibility.Collapsed;

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

            GlobalProfileCPUClockLabel.Visibility = cpuClockVisibility;
            GlobalProfileCPUClockText.Visibility = cpuClockVisibility;
            GlobalProfileCPUClockText.Text = globalProfile.LimitCPUClock ? $"{globalProfile.CPUClockMax}MHz" : "Off";

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

            ACDCProfileCPUClockLabel.Visibility = cpuClockVisibility;
            ACProfileCPUClockText.Visibility = cpuClockVisibility;
            DCProfileCPUClockText.Visibility = cpuClockVisibility;
            ACProfileCPUClockText.Text = acProfile.LimitCPUClock ? $"{acProfile.CPUClockMax}MHz" : "Off";
            DCProfileCPUClockText.Text = dcProfile.LimitCPUClock ? $"{dcProfile.CPUClockMax}MHz" : "Off";

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

                    // CPU Clock
                    GameACDCProfileCPUClockLabel.Visibility = cpuClockVisibility;
                    GameACProfileCPUClockText.Visibility = cpuClockVisibility;
                    GameDCProfileCPUClockText.Visibility = cpuClockVisibility;
                    GameACProfileCPUClockText.Text = gameACProfile.LimitCPUClock ? $"{gameACProfile.CPUClockMax}MHz" : "Off";
                    GameDCProfileCPUClockText.Text = gameDCProfile.LimitCPUClock ? $"{gameDCProfile.CPUClockMax}MHz" : "Off";

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

                    // CPU Boost
                    GameProfileCPUBoostLabel.Visibility = cpuBoostVisibility;
                    GameProfileCPUBoostText.Visibility = cpuBoostVisibility;
                    GameProfileCPUBoostText.Text = gameProfile.CPUBoost ? "On" : "Off";

                    // CPU EPP
                    GameProfileCPUEPPLabel.Visibility = cpuEPPVisibility;
                    GameProfileCPUEPPText.Visibility = cpuEPPVisibility;
                    GameProfileCPUEPPText.Text = $"{gameProfile.CPUEPP}";

                    // CPU Clock
                    GameProfileCPUClockLabel.Visibility = cpuClockVisibility;
                    GameProfileCPUClockText.Visibility = cpuClockVisibility;
                    GameProfileCPUClockText.Text = gameProfile.LimitCPUClock ? $"{gameProfile.CPUClockMax}MHz" : "Off";

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

                    // CPU Clock Limit
                    if (SaveLimitCPUClock)
                    {
                        AddTextBlock(acDcGrid, rowIndex, 0, "CPU Clk", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                        AddTextBlock(acDcGrid, rowIndex, 1, gameAC.LimitCPUClock ? $"{gameAC.CPUClockMax}MHz" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                        AddTextBlock(acDcGrid, rowIndex, 2, gameDC.LimitCPUClock ? $"{gameDC.CPUClockMax}MHz" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
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
                        }
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

                    // CPU Clock Limit
                    if (SaveLimitCPUClock)
                    {
                        AddTextBlock(singleGrid, rowIndex, 0, "CPU Clock", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                        AddTextBlock(singleGrid, rowIndex, 1, game.LimitCPUClock ? $"{game.CPUClockMax}MHz" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));
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
                ProfileSaveTDPCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveTDP") ? (bool)settings.Values["ProfileSaveTDP"] : true;
                ProfileSaveCPUBoostCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveCPUBoost") ? (bool)settings.Values["ProfileSaveCPUBoost"] : true;
                ProfileSaveCPUEPPCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveCPUEPP") ? (bool)settings.Values["ProfileSaveCPUEPP"] : true;
                ProfileSaveLimitCPUClockCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveLimitCPUClock") ? (bool)settings.Values["ProfileSaveLimitCPUClock"] : true;
                ProfileSaveAMDFeaturesCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveAMDFeatures") ? (bool)settings.Values["ProfileSaveAMDFeatures"] : false;
                ProfileSaveFPSLimitCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveFPSLimit") ? (bool)settings.Values["ProfileSaveFPSLimit"] : false;
                ProfileSaveAutoTDPCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveAutoTDP") ? (bool)settings.Values["ProfileSaveAutoTDP"] : false;
                ProfileSaveOSPowerModeCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveOSPowerMode") ? (bool)settings.Values["ProfileSaveOSPowerMode"] : false;
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
            settings.Values["ProfileSaveTDP"] = ProfileSaveTDPCheckBox.IsChecked;
            settings.Values["ProfileSaveCPUBoost"] = ProfileSaveCPUBoostCheckBox.IsChecked;
            settings.Values["ProfileSaveCPUEPP"] = ProfileSaveCPUEPPCheckBox.IsChecked;
            settings.Values["ProfileSaveLimitCPUClock"] = ProfileSaveLimitCPUClockCheckBox.IsChecked;
            settings.Values["ProfileSaveAMDFeatures"] = ProfileSaveAMDFeaturesCheckBox.IsChecked;
            settings.Values["ProfileSaveFPSLimit"] = ProfileSaveFPSLimitCheckBox.IsChecked;
            settings.Values["ProfileSaveAutoTDP"] = ProfileSaveAutoTDPCheckBox.IsChecked;
            settings.Values["ProfileSaveOSPowerMode"] = ProfileSaveOSPowerModeCheckBox.IsChecked;
        }

        private void ProfileSettingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveProfileCustomizationSettings();
            Logger.Info($"Profile customization settings updated");
        }

        private void ProfileSettingsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveProfileCustomizationSettings();
            Logger.Info($"Profile customization settings updated");
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
                        break;
                    case "System":
                        SystemScrollViewer.Visibility = Visibility.Visible;
                        SystemScrollViewer.ChangeView(null, 0, null, true);
                        break;
                }
            }
        }

        private void GamingWidget_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Handle LT (Left Trigger) and RT (Right Trigger) for tab navigation
            if (e.Key == VirtualKey.GamepadLeftTrigger)
            {
                NavigateToPreviousTab();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.GamepadRightTrigger)
            {
                NavigateToNextTab();
                e.Handled = true;
            }
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

            // Clean up widget activity
            if (widgetActivity != null)
            {
                Logger.Info("Completing widget activity during page unload.");
                try
                {
                    widgetActivity.Complete();
                    widgetActivity = null;
                    Logger.Info("Widget activity completed and disposed.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing widget activity during unload: {ex.Message}");
                    widgetActivity = null;
                }
            }
            else
            {
                Logger.Info("No widget activity to clean up during unload.");
            }

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

                // Show connection status banner while waiting for helper to connect
                if (ConnectionStatusBanner != null)
                {
                    ConnectionStatusBanner.Visibility = Visibility.Visible;
                    Logger.Info("Connection status banner shown - waiting for helper connection.");
                }

                Logger.Info("Launching full trust process via FullTrustProcessLauncher.");
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                Logger.Info("FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync completed.");
            }
            else
            {
                Logger.Info($"Not launching full trust process. App.Connection is null: {App.Connection == null}, FullTrustAppContract present: {ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0)}");

                // If connection already exists, register event handlers and sync properties
                if (App.Connection != null)
                {
                    Logger.Info("AppService connection already exists. Ensuring event handlers are registered.");

                    // Hide connection status banner since we're connected
                    if (ConnectionStatusBanner != null)
                    {
                        ConnectionStatusBanner.Visibility = Visibility.Collapsed;
                        Logger.Info("Connection status banner hidden - already connected.");
                    }

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

                        await properties.Sync();
                        Logger.Info("Property sync completed.");

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
                await properties.Sync();

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
                        string expectedProfile = GetTargetProfileName();
                        if (expectedProfile != currentProfileName)
                        {
                            Logger.Info($"Profile changed while in background: '{currentProfileName}' -> '{expectedProfile}'");
                            currentProfileName = expectedProfile;
                            LoadProfileSettings(currentProfileName);
                        }
                        else
                        {
                            // Even if profile name is same, reload settings to ensure UI matches profile
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

                // Hide connection status banner and update profile display now that we're connected
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (ConnectionStatusBanner != null)
                    {
                        ConnectionStatusBanner.Visibility = Visibility.Collapsed;
                        Logger.Info("Connection status banner hidden - connected to helper.");
                    }

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
        /// Applies the current profile's TDP value and TDP Mode to the helper.
        /// Called after connection is established since profile loads before connection.
        /// </summary>
        private async Task ApplyProfileTDPToHelper()
        {
            try
            {
                var profile = GetProfile(currentProfileName);
                if (profile == null)
                {
                    Logger.Warn($"Cannot apply profile TDP - profile '{currentProfileName}' not found");
                    return;
                }

                // Run on UI thread since we're touching UI controls (including SaveTDP which accesses checkbox)
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

                            // Update UI combo boxes if needed
                            if (LegionPerformanceModeComboBox != null && LegionPerformanceModeComboBox.SelectedIndex != modeIndex)
                                LegionPerformanceModeComboBox.SelectedIndex = modeIndex;
                            if (TDPModeComboBox != null && TDPModeComboBox.SelectedIndex != modeIndex)
                            {
                                lastTDPModeIndex = modeIndex; // Update tracker to avoid redundant handler call
                                TDPModeComboBox.SelectedIndex = modeIndex;
                            }

                            // Always force send to helper during startup to ensure hardware matches profile
                            // This is necessary because TDP sync may have triggered Custom mode on hardware
                            legionPerformanceMode.ForceSetValue(profileMode);

                            // Update TDP slider enabled state
                            UpdateTDPSliderEnabledState();
                        }
                    }

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
                        if (ConnectionStatusBanner != null)
                        {
                            ConnectionStatusBanner.Visibility = Visibility.Visible;
                            Logger.Info("Connection status banner shown - disconnected from helper.");
                        }
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

            // Clean up widget activity
            if (widgetActivity != null)
            {
                Logger.Info("Completing and disposing widget activity.");
                try
                {
                    widgetActivity.Complete();
                    widgetActivity = null;
                    Logger.Info("Widget activity stopped successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing widget activity: {ex.Message}");
                    widgetActivity = null;
                }
            }
            else
            {
                Logger.Info("WidgetActivity was already null during disconnect.");
            }

            // Only relaunch if we're running as a widget (not standalone app)
            // and the disconnect was due to a crash/termination (not normal suspension)
            // AND we don't already have a connection (prevents duplicate launches)
            bool shouldRelaunch = widget != null &&
                                  eventArgs != null &&
                                  (eventArgs.Reason == BackgroundTaskCancellationReason.Abort ||
                                   eventArgs.Reason == BackgroundTaskCancellationReason.Terminating) &&
                                  App.Connection == null;  // Don't relaunch if connection still exists

            if (shouldRelaunch)
            {
                Logger.Info($"Widget disconnected due to {eventArgs.Reason} and no connection exists. Attempting to relaunch full trust process.");
                try
                {
                    await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error relaunching full trust process: {ex.Message}");
                }
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

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        currentGameExePath = exePath;
                        Logger.Info($"Updated currentGameExePath: {currentGameExePath}");
                    }
                    else
                    {
                        currentGameExePath = "";
                        Logger.Info("Cleared currentGameExePath (no path in RunningGame)");
                    }
                }
                else
                {
                    currentGameExePath = "";
                    Logger.Info("Cleared currentGameExePath (no running game)");
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
                    // Format: "GameName<||>WindowTitle" - use window title as filter for Lossless Scaling profile matching
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

            // Manufacturer WMI TDP card is always visible (device-agnostic)
            // Update the description based on Legion detection
            if (ManufacturerWMIDescription != null)
            {
                if (visible)
                {
                    ManufacturerWMIDescription.Text = "Use device manufacturer's WMI method for TDP control. Supported: Legion Go / Go 2. Disable to use RyzenAdj (may trigger anti-cheat).";
                }
                else
                {
                    ManufacturerWMIDescription.Text = "Use device manufacturer's WMI method for TDP control. No supported device detected. RyzenAdj will be used (may trigger anti-cheat).";
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
        /// Shows or hides the Custom TDP card based on performance mode
        /// </summary>
        private void SetCustomTDPVisibility(bool visible)
        {
            if (LegionCustomTDPCard != null)
            {
                LegionCustomTDPCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Custom TDP card visibility set to: {visible}");
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
        private int lastTDPModeIndex = -1; // Track last index to avoid redundant updates
        private void TDPModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TDPModeComboBox == null) return;

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
            if (!isInitialSync && !isApplyingHelperUpdate && !isLoadingProfile && SaveTDP)
            {
                Logger.Info($"Saving TDP Mode change to profile: {currentProfileName}");
                SaveCurrentSettingsToProfile(currentProfileName);
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

            // Check if in Custom mode (index 3 = Custom = 255)
            bool isCustomMode = TDPModeComboBox?.SelectedIndex == 3;

            // TDP slider should only be enabled in Custom mode for Legion devices
            // Note: TDP slider also requires tdp property to be ready (IsEnabled is set elsewhere too)
            if (!isCustomMode)
            {
                TDPSlider.IsEnabled = false;
                Logger.Debug("TDP slider disabled - not in Custom mode");

                // Update XY focus to skip disabled TDP slider
                // TDPModeComboBox -> AutoTDPToggle (skip TDPSlider)
                if (TDPModeComboBox != null && AutoTDPToggle != null)
                {
                    TDPModeComboBox.XYFocusDown = AutoTDPToggle;
                    AutoTDPToggle.XYFocusUp = TDPModeComboBox;
                    Logger.Debug("XY focus updated to skip disabled TDP slider");
                }
            }
            else
            {
                // In Custom mode, enable if tdp property is ready
                TDPSlider.IsEnabled = tdp != null;
                Logger.Debug($"TDP slider enabled in Custom mode: {TDPSlider.IsEnabled}");

                // Restore normal XY focus chain
                // TDPModeComboBox -> TDPSlider -> AutoTDPToggle
                if (TDPModeComboBox != null && AutoTDPToggle != null)
                {
                    TDPModeComboBox.XYFocusDown = TDPSlider;
                    TDPSlider.XYFocusUp = TDPModeComboBox;
                    TDPSlider.XYFocusDown = AutoTDPToggle;
                    AutoTDPToggle.XYFocusUp = TDPSlider;
                    Logger.Debug("XY focus restored to include TDP slider");
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
        private bool quickSettingsInitialized = false;

        // Tile definitions with visibility tracking
        private class TileDefinition
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Glyph { get; set; }
            public bool IsVisible { get; set; } = true;
            public bool IsTrigger { get; set; } = false;  // True for tiles that trigger actions (keyboard, custom shortcuts)
            public string CustomShortcut { get; set; }    // For custom shortcut tiles
            public Button TileButton { get; set; }
            public TextBlock StateText { get; set; }
            public CheckBox VisibilityCheckBox { get; set; }
        }

        // List of custom shortcut tiles
        private List<TileDefinition> qsCustomShortcuts = new List<TileDefinition>();

        private List<TileDefinition> qsTileDefinitions = new List<TileDefinition>();
        private Dictionary<string, TileDefinition> qsTileMap = new Dictionary<string, TileDefinition>();

        // Timer for TDP reapply when switching to Custom mode
        private Windows.UI.Xaml.DispatcherTimer qsTdpReapplyTimer;
        private int qsPendingTdpValue;

        /// <summary>
        /// Initialize Quick Settings resources and build tiles
        /// </summary>
        private void InitializeQuickSettings()
        {
            if (quickSettingsInitialized) return;

            try
            {
                // Dark mode colors with sharp contrast for handheld devices
                // On state: dark green
                tileOnBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 46, 31));    // #1A2E1F

                // Other tile brushes - dark mode
                tileOffBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 28, 30));   // #1A1C1E
                tileActiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 37, 48)); // #1A2530 - dark blue
                tileTriggerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 32, 48)); // #252030 - dark purple

                // Define all tiles
                DefineQuickSettingsTiles();

                // Load visibility settings from storage
                LoadQuickSettingsConfig();

                // Build tile UI
                RebuildQuickSettingsTiles();

                // Build visibility panel
                BuildVisibilityPanel();

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
                BuildVisibilityPanel();
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

            // Core tiles
            AddTileDefinition("TDPMode", "TDP Mode", "\uE945");
            AddTileDefinition("Profile", "Profile", "\uE77B");
            AddTileDefinition("Overlay", "Overlay", "\uE7B3");
            AddTileDefinition("PowerMode", "Power Mode", "\uE945");
            AddTileDefinition("FPSLimit", "FPS Limit", "\uE916");
            AddTileDefinition("AutoTDP", "AutoTDP", "\uE9F5");
            AddTileDefinition("Resolution", "Resolution", "\uE7F8");
            AddTileDefinition("HDR", "HDR", "\uE706");
            AddTileDefinition("LosslessScaling", "Lossless", "\uE740");
            AddTileDefinition("RIS", "RIS", "\uE8B3");
            AddTileDefinition("AFMF", "AFMF", "\uE916");
            AddTileDefinition("RSR", "RSR", "\uE8B3");
            AddTileDefinition("AntiLag", "Anti-Lag", "\uE916");
            AddTileDefinition("RadeonChill", "Chill", "\uE9CA");
            AddTileDefinition("CPUBoost", "CPU Boost", "\uE7F4");
            AddTileDefinition("EPP", "EPP", "\uE83E");

            // Keyboard trigger tile
            AddTileDefinition("Keyboard", "Keyboard", "\uE765", isTrigger: true);

            // Legion-specific tiles (will be hidden if Legion not detected)
            AddTileDefinition("LegionTouchpad", "Touchpad", "\uE962");
            AddTileDefinition("LegionLightMode", "Light Mode", "\uE781");

            // Load custom shortcut tiles from storage
            LoadCustomShortcutTiles();
        }

        private void AddTileDefinition(string id, string name, string glyph, bool isTrigger = false, string customShortcut = null)
        {
            var def = new TileDefinition { Id = id, Name = name, Glyph = glyph, IsVisible = true, IsTrigger = isTrigger, CustomShortcut = customShortcut };
            qsTileDefinitions.Add(def);
            qsTileMap[id] = def;
        }

        /// <summary>
        /// Load custom shortcut tiles from storage
        /// </summary>
        private void LoadCustomShortcutTiles()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("QS_CustomShortcuts", out object val) && val is string json && !string.IsNullOrEmpty(json))
                {
                    // Parse simple format: "Name1|Shortcut1;Name2|Shortcut2"
                    var shortcuts = json.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    int index = 0;
                    foreach (var shortcut in shortcuts)
                    {
                        var parts = shortcut.Split('|');
                        if (parts.Length == 2)
                        {
                            string tileId = $"CustomShortcut_{index}";
                            var def = new TileDefinition
                            {
                                Id = tileId,
                                Name = parts[0],
                                Glyph = "\uE768",
                                IsVisible = true,
                                IsTrigger = true,
                                CustomShortcut = parts[1]
                            };
                            qsTileDefinitions.Add(def);
                            qsTileMap[tileId] = def;
                            qsCustomShortcuts.Add(def);
                            index++;
                        }
                    }
                    Logger.Info($"Loaded {index} custom shortcut tiles");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading custom shortcut tiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Save custom shortcut tiles to storage
        /// </summary>
        private void SaveCustomShortcutTiles()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var parts = new List<string>();
                foreach (var tile in qsCustomShortcuts)
                {
                    if (!string.IsNullOrEmpty(tile.Name) && !string.IsNullOrEmpty(tile.CustomShortcut))
                    {
                        parts.Add($"{tile.Name}|{tile.CustomShortcut}");
                    }
                }
                settings.Values["QS_CustomShortcuts"] = string.Join(";", parts);
                Logger.Info($"Saved {parts.Count} custom shortcut tiles");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving custom shortcut tiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a new custom shortcut tile
        /// </summary>
        private void AddCustomShortcutTile(string name, string shortcut)
        {
            try
            {
                int index = qsCustomShortcuts.Count;
                string tileId = $"CustomShortcut_{index}";
                var def = new TileDefinition
                {
                    Id = tileId,
                    Name = name,
                    Glyph = "\uE768",
                    IsVisible = true,
                    IsTrigger = true,
                    CustomShortcut = shortcut
                };
                qsTileDefinitions.Add(def);
                qsTileMap[tileId] = def;
                qsCustomShortcuts.Add(def);

                SaveCustomShortcutTiles();
                RebuildQuickSettingsTiles();
                BuildVisibilityPanel();

                Logger.Info($"Added custom shortcut tile: {name} -> {shortcut}");
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

                foreach (var tile in qsTileDefinitions)
                {
                    string key = $"QS_{tile.Id}_Visible";
                    if (settings.Values.TryGetValue(key, out object val) && val is bool visible)
                    {
                        tile.IsVisible = visible;
                    }
                }

                Logger.Info("Quick Settings config loaded");
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

                foreach (var tile in qsTileDefinitions)
                {
                    string key = $"QS_{tile.Id}_Visible";
                    settings.Values[key] = tile.IsVisible;
                }

                Logger.Info("Quick Settings config saved");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving Quick Settings config: {ex.Message}");
            }
        }

        /// <summary>
        /// Build visibility checkbox panel
        /// </summary>
        private void BuildVisibilityPanel()
        {
            if (TileVisibilityPanel == null) return;

            TileVisibilityPanel.Children.Clear();

            foreach (var tile in qsTileDefinitions)
            {
                // Skip Legion tiles if Legion not detected
                if ((tile.Id == "LegionTouchpad" || tile.Id == "LegionLightMode") &&
                    (legionGoDetected?.Value != true))
                {
                    continue;
                }

                // For custom shortcuts, add a row with checkbox and delete button
                if (tile.Id.StartsWith("CustomShortcut_"))
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var checkBox = new CheckBox
                    {
                        Content = tile.Name,
                        IsChecked = tile.IsVisible,
                        Tag = tile.Id,
                        Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                        Padding = new Thickness(8, 6, 8, 6),
                        UseSystemFocusVisuals = true,
                        FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                        FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent)
                    };
                    checkBox.Checked += TileVisibility_Changed;
                    checkBox.Unchecked += TileVisibility_Changed;
                    tile.VisibilityCheckBox = checkBox;
                    Grid.SetColumn(checkBox, 0);
                    row.Children.Add(checkBox);

                    var deleteButton = new Button
                    {
                        Content = "\uE74D", // Delete icon
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Tag = tile.Id,
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                        Foreground = new SolidColorBrush(Windows.UI.Colors.Red),
                        VerticalAlignment = VerticalAlignment.Center,
                        UseSystemFocusVisuals = true,
                        FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                        FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent)
                    };
                    deleteButton.Click += DeleteCustomShortcut_Click;
                    Grid.SetColumn(deleteButton, 1);
                    row.Children.Add(deleteButton);

                    TileVisibilityPanel.Children.Add(row);
                }
                else
                {
                    var checkBox = new CheckBox
                    {
                        Content = tile.Name,
                        IsChecked = tile.IsVisible,
                        Tag = tile.Id,
                        Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                        Padding = new Thickness(8, 6, 8, 6),
                        UseSystemFocusVisuals = true,
                        FocusVisualPrimaryBrush = new SolidColorBrush(Windows.UI.Colors.White),
                        FocusVisualSecondaryBrush = new SolidColorBrush(Windows.UI.Colors.Transparent)
                    };
                    checkBox.Checked += TileVisibility_Changed;
                    checkBox.Unchecked += TileVisibility_Changed;
                    tile.VisibilityCheckBox = checkBox;
                    TileVisibilityPanel.Children.Add(checkBox);
                }
            }
        }

        /// <summary>
        /// Delete a custom shortcut tile
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
                        qsTileDefinitions.Remove(tile);
                        qsTileMap.Remove(tileId);
                        qsCustomShortcuts.Remove(tile);

                        SaveCustomShortcutTiles();
                        RebuildQuickSettingsTiles();
                        BuildVisibilityPanel();

                        Logger.Info($"Deleted custom shortcut tile: {tile.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting custom shortcut tile: {ex.Message}");
            }
        }

        /// <summary>
        /// Rebuild tile grid with only visible tiles, in 3-column layout
        /// </summary>
        private void RebuildQuickSettingsTiles()
        {
            if (QuickSettingsTilesContainer == null) return;

            QuickSettingsTilesContainer.Children.Clear();

            // Get visible tiles
            var visibleTiles = new List<TileDefinition>();
            foreach (var tile in qsTileDefinitions)
            {
                // Skip Legion tiles if not detected
                if ((tile.Id == "LegionTouchpad" || tile.Id == "LegionLightMode") &&
                    (legionGoDetected?.Value != true))
                {
                    continue;
                }

                // Skip TDP Mode if Legion not detected
                if (tile.Id == "TDPMode" && (legionGoDetected?.Value != true))
                {
                    continue;
                }

                if (tile.IsVisible)
                {
                    visibleTiles.Add(tile);
                }
            }

            // Build rows of 3 tiles
            Grid currentRow = null;
            int colIndex = 0;

            for (int i = 0; i < visibleTiles.Count; i++)
            {
                if (colIndex == 0)
                {
                    currentRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    QuickSettingsTilesContainer.Children.Add(currentRow);
                }

                var tile = visibleTiles[i];
                var tileButton = CreateTileButton(tile);
                Grid.SetColumn(tileButton, colIndex * 2);
                currentRow.Children.Add(tileButton);

                colIndex++;
                if (colIndex >= 3)
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
            var button = new Button
            {
                Tag = tile.Id,
                Style = Resources["QuickSettingsTileStyle"] as Style,
                Background = tileOffBrush,
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

            var stateText = new TextBlock
            {
                Text = "Off",
                FontSize = 13,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            content.Children.Add(stateText);

            button.Content = content;
            button.Click += QuickSettingsTile_Click;

            tile.TileButton = button;
            tile.StateText = stateText;

            return button;
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

                // TDP Mode tile
                if (qsTileMap.TryGetValue("TDPMode", out var tdpTile) && tdpTile.TileButton != null)
                {
                    if (legionGoDetected?.Value == true && legionPerformanceMode != null)
                    {
                        int mode = legionPerformanceMode.Value;
                        string modeText;
                        switch (mode)
                        {
                            case 1: modeText = "Quiet"; break;
                            case 2: modeText = "Balanced"; break;
                            case 3: modeText = "Performance"; break;
                            case 255:
                                int currentTdp = (int)(tdp?.Value ?? 15);
                                modeText = $"Custom ({currentTdp}W)";
                                break;
                            default: modeText = "Balanced"; break;
                        }
                        tdpTile.StateText.Text = modeText;
                        tdpTile.StateText.Foreground = accentForeground;
                        tdpTile.TileButton.Background = mode == 255 ? tileActiveBrush : (mode == 3 ? tileOnBrush : tileOffBrush);
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
                    string gameName = (runningGame != null && runningGame.Value.IsValid()) ? runningGame.Value.GameId.Name : "Per-Game";
                    string profileName = perGame ? gameName : "Global";
                    profileTile.StateText.Text = profileName;
                    profileTile.StateText.Foreground = perGame ? accentForeground : offForeground;
                    profileTile.TileButton.Background = perGame ? tileOnBrush : tileOffBrush;
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
                    // Check for custom shortcut tiles first
                    if (tileTag.StartsWith("CustomShortcut_") && qsTileMap.TryGetValue(tileTag, out var customTile))
                    {
                        if (!string.IsNullOrEmpty(customTile.CustomShortcut))
                        {
                            QuickSettings.KeyboardShortcutHelper.SendShortcut(customTile.CustomShortcut);
                            Logger.Info($"Custom shortcut tile clicked: {customTile.Name} -> {customTile.CustomShortcut}");
                        }
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

                // If switching to Custom mode, schedule TDP reapply after 5 seconds
                if (nextMode == 255)
                {
                    ScheduleQsTdpReapply();
                }

                Logger.Info($"TDP Mode cycled from {currentMode} to {nextMode}");

                // Save profile after TDP mode cycle (if not during initialization)
                if (!isInitialSync && !isLoadingProfile && SaveTDP)
                {
                    Logger.Info($"Saving TDP Mode cycle to profile: {currentProfileName}");
                    SaveCurrentSettingsToProfile(currentProfileName);
                }
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
                // Store current TDP value from Performance tab slider
                qsPendingTdpValue = (int)(tdp?.Value ?? 15);

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
                        // Reapply using Performance tab TDP value
                        if (tdp != null)
                        {
                            // Force reapply by sending different value to helper first, then the real value
                            // This ensures the helper doesn't skip due to "equals current value"
                            tdp.SetValue(qsPendingTdpValue - 1);
                            await System.Threading.Tasks.Task.Delay(100);
                            tdp.SetValue(qsPendingTdpValue);
                            Logger.Info($"Quick Settings: Reapplied TDP {qsPendingTdpValue}W after Custom mode switch");
                        }
                    }
                };
                qsTdpReapplyTimer.Start();
                Logger.Info($"Quick Settings: Scheduled TDP reapply in 5 seconds (TDP={qsPendingTdpValue}W)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scheduling TDP reapply: {ex.Message}");
            }
        }

        private void TogglePerGameProfile()
        {
            // Only allow toggling when a game is detected
            if (perGameProfile != null && runningGame != null && runningGame.Value.IsValid())
            {
                bool newValue = !perGameProfile.Value;
                perGameProfile.SetValue(newValue);
                Logger.Info($"Per-game profile toggled to {newValue}");
            }
            else
            {
                Logger.Info("Per-game profile toggle ignored - no game detected");
            }
        }

        private void TriggerOnScreenKeyboard()
        {
            try
            {
                // Send Win+Ctrl+O to toggle on-screen keyboard (works in UWP sandbox)
                QuickSettings.KeyboardShortcutHelper.SendShortcut("Win+Ctrl+O");
                Logger.Info("On-screen keyboard triggered via Win+Ctrl+O");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error triggering on-screen keyboard: {ex.Message}");
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
                Logger.Info($"AFMF toggled to {newValue}");
            }
        }

        private void ToggleRSR()
        {
            if (amdRadeonSuperResolutionEnabled != null && (amdRadeonSuperResolutionSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonSuperResolutionEnabled.Value;
                amdRadeonSuperResolutionEnabled.SetValue(newValue);
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
                Logger.Info($"Anti-Lag toggled to {newValue}");
            }
        }

        private void ToggleRadeonChill()
        {
            if (amdRadeonChillEnabled != null && (amdRadeonChillSupported?.Value ?? false))
            {
                bool newValue = !amdRadeonChillEnabled.Value;
                amdRadeonChillEnabled.SetValue(newValue);
                Logger.Info($"Radeon Chill toggled to {newValue}");
            }
        }

        private void ToggleCPUBoost()
        {
            if (cpuBoost != null)
            {
                bool newValue = !cpuBoost.Value;
                cpuBoost.SetValue(newValue);
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
                Logger.Info($"EPP cycled from {currentValue} to {nextValue}");
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
                    Logger.Info("AMD Overlay toggled ON (Level 1)");
                }
                else if (amdOverlayLevel < 4)
                {
                    // Cycle to next level
                    CycleAMDOverlayLevel();
                    amdOverlayLevel++;
                    Logger.Info($"AMD Overlay cycled to Level {amdOverlayLevel}");
                }
                else
                {
                    // At level 4, turn off
                    SendAMDOverlayToggle();
                    amdOverlayLevel = 0;
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
            if (SaveFPSLimit && !isLoadingProfile && !isSwitchingProfile)
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

        /// <summary>
        /// Show/hide customization panel
        /// </summary>
        private void QuickSettingsCustomize_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                QuickSettingsCustomizePanel.Visibility = Visibility.Visible;
                QuickSettingsCustomizeButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Close customization panel
        /// </summary>
        private void QuickSettingsCustomizeDone_Click(object sender, RoutedEventArgs e)
        {
            if (QuickSettingsCustomizePanel != null)
            {
                QuickSettingsCustomizePanel.Visibility = Visibility.Collapsed;
                QuickSettingsCustomizeButton.Visibility = Visibility.Visible;
                SaveQuickSettingsConfig();
                RebuildQuickSettingsTiles();
                UpdateQuickSettingsTileStates();
            }
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
    }
}

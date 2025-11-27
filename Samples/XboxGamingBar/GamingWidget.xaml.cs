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
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
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
        public bool RadeonAntiLag { get; set; } = false;
        public bool RadeonBoost { get; set; } = false;
        public double RadeonBoostResolution { get; set; } = 0;
        public bool RadeonChill { get; set; } = false;
        public double RadeonChillMinFPS { get; set; } = 30;
        public double RadeonChillMaxFPS { get; set; } = 60;

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
                RadeonAntiLag = this.RadeonAntiLag,
                RadeonBoost = this.RadeonBoost,
                RadeonBoostResolution = this.RadeonBoostResolution,
                RadeonChill = this.RadeonChill,
                RadeonChillMinFPS = this.RadeonChillMinFPS,
                RadeonChillMaxFPS = this.RadeonChillMaxFPS
            };
        }
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

        // Sticky TDP monitoring
        private DispatcherTimer stickyTDPTimer = null;
        private double targetTDPLimit = 15; // Stores the TDP limit we want to maintain
        private int stickyTDPCheckIntervalSeconds = 5;
        private bool isStickyTDPReapplying = false; // Prevents slider flicker during reapply

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
        private readonly LegionPerformanceModeProperty legionPerformanceMode;
        private readonly LegionCustomTDPSlowProperty legionCustomTDPSlow;
        private readonly LegionCustomTDPFastProperty legionCustomTDPFast;
        private readonly LegionCustomTDPPeakProperty legionCustomTDPPeak;
        private readonly LegionFanFullSpeedProperty legionFanFullSpeed;
        private readonly LegionGyroEnabledProperty legionGyroEnabled;

        // Settings properties
        private readonly UseManufacturerWMIProperty useManufacturerWMI;

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
        private bool isInternalToggleDisable = false; // Indicates toggle is being disabled internally (game close)

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
            InitializeComponent();

            // Register for lifecycle events
            this.Loaded += GamingWidget_Loaded;
            this.Unloaded += GamingWidget_Unloaded;
            Logger.Info("Registered Loaded and Unloaded event handlers.");

            tdp = new TDPProperty(4, TDPSlider, this);
            currentTdp = new CurrentTDPProperty(CurrentTDPValueText, this);
            osd = new OSDProperty(0, PerformanceOverlaySlider, this);
            runningGame = new RunningGameProperty(RunningGameText, PerGameProfileToggle, DetectedGameText, this);
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
            trackedGame = new TrackedGameProperty(new TrackedGame());
            rtssInstalled = new RTSSInstalledProperty(PerformanceOverlaySlider, this);
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
            legionPerformanceMode = new LegionPerformanceModeProperty(LegionPerformanceModeComboBox, this);
            legionCustomTDPSlow = new LegionCustomTDPSlowProperty(LegionCustomTDPSlowSlider, this);
            legionCustomTDPFast = new LegionCustomTDPFastProperty(LegionCustomTDPFastSlider, this);
            legionCustomTDPPeak = new LegionCustomTDPPeakProperty(LegionCustomTDPPeakSlider, this);
            legionFanFullSpeed = new LegionFanFullSpeedProperty(LegionFanFullSpeedToggle, this);
            legionGyroEnabled = new LegionGyroEnabledProperty(LegionGyroToggle, this);

            // Settings properties
            useManufacturerWMI = new UseManufacturerWMIProperty(UseManufacturerWMIToggle, this);

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
                useManufacturerWMI
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

            // Performance tab - TDP card
            TDPSlider.GotFocus += Control_GotFocus;
            TDPSlider.LostFocus += Control_LostFocus;

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

            // Profiles tab - Power Source Profile card
            PowerSourceProfileToggle.GotFocus += Control_GotFocus;
            PowerSourceProfileToggle.LostFocus += Control_LostFocus;

            // Graphics tab - Refresh Rate card
            RefreshRatesComboBox.GotFocus += Control_GotFocus;
            RefreshRatesComboBox.LostFocus += Control_LostFocus;

            // Graphics tab - AMD cards
            AMDRadeonSuperResolutionToggle.GotFocus += Control_GotFocus;
            AMDRadeonSuperResolutionToggle.LostFocus += Control_LostFocus;
            AMDRadeonSuperResolutionSharpnessSlider.GotFocus += Control_GotFocus;
            AMDRadeonSuperResolutionSharpnessSlider.LostFocus += Control_LostFocus;
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

            // Legion tab - Gyroscope card
            LegionGyroToggle.GotFocus += Control_GotFocus;
            LegionGyroToggle.LostFocus += Control_LostFocus;

            // Legion tab - Light Mode card
            LegionLightModeComboBox.GotFocus += Control_GotFocus;
            LegionLightModeComboBox.LostFocus += Control_LostFocus;

            // Legion tab - Light Color card (ColorPicker)
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
        }

        private void Control_GotFocus(object sender, RoutedEventArgs e)
        {
            var control = sender as FrameworkElement;
            if (control == null) return;

            // Find parent card (Border with CardStyle)
            var card = FindParentCard(control);
            if (card != null)
            {
                // Clear previous card highlight
                if (currentFocusedCard != null && currentFocusedCard != card)
                {
                    currentFocusedCard.BorderBrush = cardDefaultBorderBrush;
                }

                // Highlight current card
                card.BorderBrush = cardFocusBorderBrush;
                currentFocusedCard = card;
            }
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
            ProfileSaveTDPCheckBox.Checked += ProfileSettingCheckBox_Changed;
            ProfileSaveTDPCheckBox.Unchecked += ProfileSettingCheckBox_Changed;
            ProfileSaveCPUBoostCheckBox.Checked += ProfileSettingCheckBox_Changed;
            ProfileSaveCPUBoostCheckBox.Unchecked += ProfileSettingCheckBox_Changed;
            ProfileSaveCPUEPPCheckBox.Checked += ProfileSettingCheckBox_Changed;
            ProfileSaveCPUEPPCheckBox.Unchecked += ProfileSettingCheckBox_Changed;
            ProfileSaveLimitCPUClockCheckBox.Checked += ProfileSettingCheckBox_Changed;
            ProfileSaveLimitCPUClockCheckBox.Unchecked += ProfileSettingCheckBox_Changed;
            ProfileSaveAMDFeaturesCheckBox.Checked += ProfileSettingCheckBox_Changed;
            ProfileSaveAMDFeaturesCheckBox.Unchecked += ProfileSettingCheckBox_Changed;

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

            // AMD settings
            AMDFluidMotionFrameToggle.Toggled += SettingChanged;
            AMDRadeonAntiLagToggle.Toggled += SettingChanged;
            AMDRadeonBoostToggle.Toggled += SettingChanged;
            AMDRadeonBoostResolutionSlider.ValueChanged += SettingChanged;
            AMDRadeonChillToggle.Toggled += SettingChanged;
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

            // Don't save during profile loading, switching, or when helper is updating values
            if (isLoadingProfile || isSwitchingProfile || isApplyingHelperUpdate)
            {
                Logger.Debug($"Skipping auto-save during profile operation (loading={isLoadingProfile}, switching={isSwitchingProfile}, helperUpdate={isApplyingHelperUpdate})");
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
                    PerformanceOverlaySlider.Value = index;
                }
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

        private async void PowerManager_PowerSourceChanged(object sender, object e)
        {
            // Small delay to allow system to update power status
            await System.Threading.Tasks.Task.Delay(100);

            // Update the active profile indicator when power source changes
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateActiveProfileIndicator();
            });
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
                    var batteryStatus = PowerManager.BatteryStatus;
                    var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                    bool isOnAC = batteryStatus == BatteryStatus.Charging ||
                                  (batteryStatus == BatteryStatus.Idle && powerSupplyStatus == PowerSupplyStatus.Adequate);

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
                    var batteryStatus = PowerManager.BatteryStatus;
                    var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                    var remainingCharge = PowerManager.RemainingChargePercent;

                    Logger.Info($"Power status - Battery: {batteryStatus}, PowerSupply: {powerSupplyStatus}, Charge: {remainingCharge}%");

                    // Device is on AC power if:
                    // 1. Battery is charging, OR
                    // 2. Battery is idle AND power supply is adequate
                    // If power supply is NotPresent or Inadequate, we're on battery
                    bool isOnAC = batteryStatus == BatteryStatus.Charging ||
                                  (batteryStatus == BatteryStatus.Idle && powerSupplyStatus == PowerSupplyStatus.Adequate);

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

            // IMPORTANT: Never create profile names for invalid games
            // If per-game is enabled but no valid game, fall back to global profiles
            if (perGameEnabled && hasGame)
            {
                // Per-game profile - only if we have a VALID game name
                Logger.Info($"Using per-game profile for: {currentGameName}");

                if (PowerSourceProfileToggle.IsOn)
                {
                    var batteryStatus = PowerManager.BatteryStatus;
                    var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                    bool isOnAC = batteryStatus == BatteryStatus.Charging ||
                                  (batteryStatus == BatteryStatus.Idle && powerSupplyStatus == PowerSupplyStatus.Adequate);
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
                    var batteryStatus = PowerManager.BatteryStatus;
                    var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                    bool isOnAC = batteryStatus == BatteryStatus.Charging ||
                                  (batteryStatus == BatteryStatus.Idle && powerSupplyStatus == PowerSupplyStatus.Adequate);
                    return isOnAC ? "AC" : "DC";
                }
            }
        }

        private void SaveCurrentSettingsToProfile(string profileName)
        {
            // Don't save during helper updates - prevents race conditions
            if (isApplyingHelperUpdate)
            {
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
                profile.TDP = TDPSlider.Value;
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
                profile.RadeonAntiLag = AMDRadeonAntiLagToggle.IsOn;
                profile.RadeonBoost = AMDRadeonBoostToggle.IsOn;
                profile.RadeonBoostResolution = AMDRadeonBoostResolutionSlider.Value;
                profile.RadeonChill = AMDRadeonChillToggle.IsOn;
                profile.RadeonChillMinFPS = AMDRadeonChillMinFPSSlider.Value;
                profile.RadeonChillMaxFPS = AMDRadeonChillMaxFPSSlider.Value;
            }

            // Persist to storage
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
                }
                if (SaveCPUBoost)
                {
                    CPUBoostToggle.IsOn = profile.CPUBoost;
                }
                if (SaveCPUEPP)
                {
                    CPUEPPSlider.Value = profile.CPUEPP;
                }
                if (SaveLimitCPUClock)
                {
                    LimitCPUClockToggle.IsOn = profile.LimitCPUClock;
                    CPUClockMaxSlider.Value = profile.CPUClockMax;
                }
                if (SaveAMDFeatures)
                {
                    AMDFluidMotionFrameToggle.IsOn = profile.FluidMotionFrames;
                    AMDRadeonAntiLagToggle.IsOn = profile.RadeonAntiLag;
                    AMDRadeonBoostToggle.IsOn = profile.RadeonBoost;
                    AMDRadeonBoostResolutionSlider.Value = profile.RadeonBoostResolution;
                    AMDRadeonChillToggle.IsOn = profile.RadeonChill;
                    AMDRadeonChillMinFPSSlider.Value = profile.RadeonChillMinFPS;
                    AMDRadeonChillMaxFPSSlider.Value = profile.RadeonChillMaxFPS;
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
            container.Values["LimitCPUClock"] = profile.LimitCPUClock;
            container.Values["CPUClockMax"] = profile.CPUClockMax;
            container.Values["FluidMotionFrames"] = profile.FluidMotionFrames;
            container.Values["RadeonAntiLag"] = profile.RadeonAntiLag;
            container.Values["RadeonBoost"] = profile.RadeonBoost;
            container.Values["RadeonBoostResolution"] = profile.RadeonBoostResolution;
            container.Values["RadeonChill"] = profile.RadeonChill;
            container.Values["RadeonChillMinFPS"] = profile.RadeonChillMinFPS;
            container.Values["RadeonChillMaxFPS"] = profile.RadeonChillMaxFPS;
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
                profile.RadeonAntiLag = container.Values.ContainsKey("RadeonAntiLag") ? (bool)container.Values["RadeonAntiLag"] : false;
                profile.RadeonBoost = container.Values.ContainsKey("RadeonBoost") ? (bool)container.Values["RadeonBoost"] : false;
                profile.RadeonBoostResolution = container.Values.ContainsKey("RadeonBoostResolution") ? (double)container.Values["RadeonBoostResolution"] : 0;
                profile.RadeonChill = container.Values.ContainsKey("RadeonChill") ? (bool)container.Values["RadeonChill"] : false;
                profile.RadeonChillMinFPS = container.Values.ContainsKey("RadeonChillMinFPS") ? (double)container.Values["RadeonChillMinFPS"] : 30;
                profile.RadeonChillMaxFPS = container.Values.ContainsKey("RadeonChillMaxFPS") ? (double)container.Values["RadeonChillMaxFPS"] : 60;

                Logger.Info($"Loaded {profileName} profile from storage");
            }
        }

        private void UpdateProfileDisplay()
        {
            // Update Global profile display
            GlobalProfileTDPText.Text = $"{globalProfile.TDP}W";
            GlobalProfileCPUBoostText.Text = globalProfile.CPUBoost ? "On" : "Off";
            GlobalProfileCPUEPPText.Text = $"{globalProfile.CPUEPP}";

            // Update AC profile display
            ACProfileTDPText.Text = $"{acProfile.TDP}W";
            ACProfileCPUBoostText.Text = acProfile.CPUBoost ? "On" : "Off";
            ACProfileCPUEPPText.Text = $"{acProfile.CPUEPP}";

            // Update DC profile display
            DCProfileTDPText.Text = $"{dcProfile.TDP}W";
            DCProfileCPUBoostText.Text = dcProfile.CPUBoost ? "On" : "Off";
            DCProfileCPUEPPText.Text = $"{dcProfile.CPUEPP}";

            // Update game profile display (if game is running)
            if (HasValidGame(currentGameName))
            {
                if (PowerSourceProfileToggle?.IsOn == true)
                {
                    // Show AC/DC game profiles
                    GameACProfileTDPText.Text = $"{gameACProfile.TDP}W";
                    GameACProfileCPUBoostText.Text = gameACProfile.CPUBoost ? "On" : "Off";
                    GameACProfileCPUEPPText.Text = $"{gameACProfile.CPUEPP}";

                    GameDCProfileTDPText.Text = $"{gameDCProfile.TDP}W";
                    GameDCProfileCPUBoostText.Text = gameDCProfile.CPUBoost ? "On" : "Off";
                    GameDCProfileCPUEPPText.Text = $"{gameDCProfile.CPUEPP}";
                }
                else
                {
                    // Show single game profile
                    GameProfileTDPText.Text = $"{gameProfile.TDP}W";
                    GameProfileCPUBoostText.Text = gameProfile.CPUBoost ? "On" : "Off";
                    GameProfileCPUEPPText.Text = $"{gameProfile.CPUEPP}";
                }
            }

            // Update all saved game profiles display
            UpdateAllGameProfilesDisplay();
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
                    GameProfileTitleWithPower.Text = $"🎮 {currentGameName}";
                }
                else
                {
                    GameProfileWithPowerSource.Visibility = Visibility.Collapsed;
                    GameProfileWithoutPowerSource.Visibility = Visibility.Visible;
                    GameProfileTitleNoPower.Text = $"🎮 {currentGameName}";
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
                    Text = $"🎮 {gameName}",
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
                    Tag = gameName  // Store game name for delete handler
                };
                deleteButton.Click += DeleteProfileButton_Click;
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
                    acDcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    acDcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    acDcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    acDcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    acDcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Headers
                    AddTextBlock(acDcGrid, 0, 1, "AC", 10, "#FFD700", horizontalAlignment: HorizontalAlignment.Center);
                    AddTextBlock(acDcGrid, 0, 2, "DC", 10, "#FF6B6B", horizontalAlignment: HorizontalAlignment.Center);

                    // TDP
                    AddTextBlock(acDcGrid, 1, 0, "TDP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                    AddTextBlock(acDcGrid, 1, 1, $"{gameAC.TDP}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    AddTextBlock(acDcGrid, 1, 2, $"{gameDC.TDP}W", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);

                    // Boost
                    AddTextBlock(acDcGrid, 2, 0, "Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                    AddTextBlock(acDcGrid, 2, 1, gameAC.CPUBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    AddTextBlock(acDcGrid, 2, 2, gameDC.CPUBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);

                    // EPP
                    AddTextBlock(acDcGrid, 3, 0, "EPP", 10, "#AAAAAA", margin: new Thickness(0, 3, 8, 0));
                    AddTextBlock(acDcGrid, 3, 1, $"{gameAC.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);
                    AddTextBlock(acDcGrid, 3, 2, $"{gameDC.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0), horizontalAlignment: HorizontalAlignment.Center);

                    stackPanel.Children.Add(acDcGrid);
                }
                else if (hasSingle)
                {
                    // Load single profile
                    var game = new PerformanceProfile();
                    LoadProfileFromStorage($"Game_{gameName}", game);

                    // Create simple grid
                    var singleGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    singleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    singleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    singleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    AddTextBlock(singleGrid, 0, 0, "TDP", 10, "#AAAAAA");
                    AddTextBlock(singleGrid, 0, 1, $"{game.TDP}W", 10, "#FFFFFF");

                    AddTextBlock(singleGrid, 1, 0, "CPU Boost", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                    AddTextBlock(singleGrid, 1, 1, game.CPUBoost ? "On" : "Off", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));

                    AddTextBlock(singleGrid, 2, 0, "CPU EPP", 10, "#AAAAAA", margin: new Thickness(0, 3, 0, 0));
                    AddTextBlock(singleGrid, 2, 1, $"{game.CPUEPP}", 10, "#FFFFFF", margin: new Thickness(0, 3, 0, 0));

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
            var settings = ApplicationData.Current.LocalSettings;
            ProfileSaveTDPCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveTDP") ? (bool)settings.Values["ProfileSaveTDP"] : true;
            ProfileSaveCPUBoostCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveCPUBoost") ? (bool)settings.Values["ProfileSaveCPUBoost"] : true;
            ProfileSaveCPUEPPCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveCPUEPP") ? (bool)settings.Values["ProfileSaveCPUEPP"] : true;
            ProfileSaveLimitCPUClockCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveLimitCPUClock") ? (bool)settings.Values["ProfileSaveLimitCPUClock"] : true;
            ProfileSaveAMDFeaturesCheckBox.IsChecked = settings.Values.ContainsKey("ProfileSaveAMDFeatures") ? (bool)settings.Values["ProfileSaveAMDFeatures"] : false;
        }

        private void SaveProfileCustomizationSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ProfileSaveTDP"] = ProfileSaveTDPCheckBox.IsChecked;
            settings.Values["ProfileSaveCPUBoost"] = ProfileSaveCPUBoostCheckBox.IsChecked;
            settings.Values["ProfileSaveCPUEPP"] = ProfileSaveCPUEPPCheckBox.IsChecked;
            settings.Values["ProfileSaveLimitCPUClock"] = ProfileSaveLimitCPUClockCheckBox.IsChecked;
            settings.Values["ProfileSaveAMDFeatures"] = ProfileSaveAMDFeaturesCheckBox.IsChecked;
        }

        private void ProfileSettingCheckBox_Changed(object sender, RoutedEventArgs e)
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
                PerformanceScrollViewer.Visibility = Visibility.Collapsed;
                GameScrollViewer.Visibility = Visibility.Collapsed;
                AMDScrollViewer.Visibility = Visibility.Collapsed;
                ScalingScrollViewer.Visibility = Visibility.Collapsed;
                LegionScrollViewer.Visibility = Visibility.Collapsed;
                SystemScrollViewer.Visibility = Visibility.Collapsed;

                // Show selected section and scroll to top
                switch (tag)
                {
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

        private void GamingWidget_Unloaded(object sender, RoutedEventArgs e)
        {
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
            Logger.Info("GamingWidget being deactivated - stopping pending updates.");
            try
            {
                properties.StopPendingUpdates();
                Logger.Info("Pending updates stopped.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping pending updates: {ex.Message}");
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
                        await properties.Sync();
                        Logger.Info("Property sync completed.");

                        // Register Chill FPS handlers after first sync to prevent crash
                        RegisterChillFPSHandlers();
                    }
                    finally
                    {
                        isApplyingHelperUpdate = false;
                    }
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

                // Register Chill FPS handlers after sync to prevent crash
                RegisterChillFPSHandlers();
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

            Logger.Info("=== GamingWidget_AppServiceConnected END ===");
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
                        if (LosslessScalingSaveSettingsButton != null) LosslessScalingSaveSettingsButton.IsEnabled = enableSaveButton;
                        if (LosslessScalingCreateProfileButton != null) LosslessScalingCreateProfileButton.IsEnabled = enableControls && HasValidGame(currentGameName);

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

                // Show/hide LSFG3 settings card
                if (LSFG3SettingsCard != null)
                {
                    LSFG3SettingsCard.Visibility = selectedType == "LSFG3" ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide LSFG2 settings card
                if (LSFG2SettingsCard != null)
                {
                    LSFG2SettingsCard.Visibility = selectedType == "LSFG2" ? Visibility.Visible : Visibility.Collapsed;
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
                if (LosslessScalingSharpnessPanel != null)
                {
                    LosslessScalingSharpnessPanel.Visibility = showSharpness ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide FSR Optimize panel (FSR only)
                if (LosslessScalingFSROptimizePanel != null)
                {
                    LosslessScalingFSROptimizePanel.Visibility = selectedType == "FSR" ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Anime4K panel
                if (LosslessScalingAnime4KPanel != null)
                {
                    LosslessScalingAnime4KPanel.Visibility = selectedType == "Anime4K" ? Visibility.Visible : Visibility.Collapsed;
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

                // Show/hide Auto mode panel
                if (LosslessScalingAutoModePanel != null)
                {
                    LosslessScalingAutoModePanel.Visibility = selectedMode == "Auto" ? Visibility.Visible : Visibility.Collapsed;
                }

                // Show/hide Custom mode panel
                if (LosslessScalingCustomModePanel != null)
                {
                    LosslessScalingCustomModePanel.Visibility = selectedMode == "Custom" ? Visibility.Visible : Visibility.Collapsed;
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

                // Hide multiplier when Adaptive mode is selected
                if (LosslessScalingLSFG3MultiplierPanel != null)
                {
                    LosslessScalingLSFG3MultiplierPanel.Visibility = selectedMode == "ADAPTIVE" ? Visibility.Collapsed : Visibility.Visible;
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

            // Also show the Manufacturer WMI TDP option on System tab when Legion is detected
            if (ManufacturerWMICard != null)
            {
                ManufacturerWMICard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Manufacturer WMI TDP card visibility set to: {visible}");
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
        private void LegionColorPicker_ColorChanged(Windows.UI.Xaml.Controls.ColorPicker sender, Windows.UI.Xaml.Controls.ColorChangedEventArgs args)
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
        /// Handles performance mode ComboBox selection
        /// </summary>
        private void LegionPerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Property handles the update, just log here
            Logger.Info($"Performance mode selection changed");
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
    }
}

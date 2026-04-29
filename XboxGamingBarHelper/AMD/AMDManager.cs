using Microsoft.Win32;
using System;
using System.Diagnostics;
//using XboxGamingBarHelper.Windows;
//using System.Windows.Forms;
using XboxGamingBarHelper.AMD.Properties;
using XboxGamingBarHelper.AMD.Settings;
using XboxGamingBarHelper.OnScreenDisplay;
//using XboxGamingBarHelper.Settings;
using Windows.UI.Input.Preview.Injection;
using Windows.System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.AMD
{
    internal class AMDManager : OnScreenDisplayManager
    {
        // AMD Software stuff
        // Computer\HKEY_CURRENT_USER\Software\AMD\CN\Performance
        private static readonly RegistryKey AMD_PERFORMANCE_KEY_ROOT = Registry.CurrentUser;
        private const string AMD_PERFORMANCE_KEY_PATH = @"Software\AMD\CN\Performance";
        private const string AMD_PERFORMANCE_STATE_KEY_NAME = "MetricsOverlayState";
        private const string AMD_PERFORMANCE_PROFILE_KEY_NAME = "MetricsProfile";

        // ADLX stuff
        private readonly ADLX_RESULT adlxInitializeResult;
        private readonly ADLXHelper adlxHelper;
        private readonly IADLXSystem adlxSystemSevices;
        private readonly IADLXDisplayServices adlxDisplayServices;
        private readonly IADLXGPU adlxInternalGPU;
        private readonly IADLXGPU adlxDedicatedGPU;
        private readonly IADLXGPU adlxSecondDedicatedGPU;
        private readonly IADLX3DSettingsServices2 adlx3DSettingsServices;

        // AMD Settings.
        private readonly AMDRadeonSuperResolutionSetting amdRadeonSuperResolutionSetting;
        public AMDRadeonSuperResolutionSetting AMDRadeonSuperResolutionSetting
        {
            get { return amdRadeonSuperResolutionSetting; }
        }

        private readonly AMDFluidMotionFrameSetting amdFluidMotionFrameSetting;
        public AMDFluidMotionFrameSetting AMDFluidMotionFrameSetting
        {
            get { return amdFluidMotionFrameSetting; }
        }

        // ADLX 1.5+: AFMF 2.x extended controls (Algorithm / SearchMode / PerformanceMode
        // / FastMotionResponse). Null on drivers that don't implement
        // IADLX3DAMDFluidMotionFrames1 — callers must IsAvailable-check before reading.
        private readonly AMDFluidMotionFrameSettingV1 amdFluidMotionFrameSettingV1;
        public AMDFluidMotionFrameSettingV1 AMDFluidMotionFrameSettingV1
        {
            get { return amdFluidMotionFrameSettingV1; }
        }

        // ADLX 1.5+: VGM (Variable Graphics Memory / UMA carveout). Acquired via
        // IADLXSystem3.GetVariableGraphicsMemory(). Null on systems where the driver
        // doesn't expose ADLX 1.5; on supported APUs (Legion Go 2 Z2 Extreme, etc.)
        // returns the list of allocation options (Auto + fixed Custom sizes).
        private readonly AMDVariableGraphicsMemorySetting amdVariableGraphicsMemorySetting;
        public AMDVariableGraphicsMemorySetting AMDVariableGraphicsMemorySetting
        {
            get { return amdVariableGraphicsMemorySetting; }
        }

        private readonly AMDRadeonAntiLagSetting amdRadeonAntiLagSetting;
        public AMDRadeonAntiLagSetting AMDRadeonAntiLagSetting
        {
            get { return amdRadeonAntiLagSetting; }
        }

        private readonly AMDRadeonBoostSetting amdRadeonBoostSetting;
        public AMDRadeonBoostSetting AMDRadeonBoostSetting
        {
            get { return amdRadeonBoostSetting; }
        }

        private readonly AMDRadeonChillSetting amdRadeonChillSetting;
        public AMDRadeonChillSetting AMDRadeonChillSetting
        {
            get { return amdRadeonChillSetting; }
        }

        private readonly AMDImageSharpeningSetting amdImageSharpeningSetting;
        public AMDImageSharpeningSetting AMDImageSharpeningSetting
        {
            get { return amdImageSharpeningSetting; }
        }

        private readonly AMDDisplayCustomColorSetting amdDisplayCustomColorSetting;
        public AMDDisplayCustomColorSetting AMDDisplayCustomColorSetting
        {
            get { return amdDisplayCustomColorSetting; }
        }

        private readonly AMD3DSettingsChangedListener amd3DSettingsChangedListener;
        public AMD3DSettingsChangedListener AMD3DSettingsChangedListener
        {
            get { return amd3DSettingsChangedListener; }
        }

        // AMD Properties.
        private readonly AMDRadeonSuperResolutionSupportedProperty amdRadeonSuperResolutionSupported;
        public AMDRadeonSuperResolutionSupportedProperty AMDRadeonSuperResolutionSupported
        {
            get { return amdRadeonSuperResolutionSupported; }
        }

        private readonly AMDRadeonSuperResolutionEnabledProperty amdRadeonSuperResolutionEnabled;
        public AMDRadeonSuperResolutionEnabledProperty AMDRadeonSuperResolutionEnabled
        {
            get { return amdRadeonSuperResolutionEnabled; }
        }

        private readonly AMDRadeonSuperResolutionSharpnessProperty amdRadeonSuperResolutionSharpness;
        public AMDRadeonSuperResolutionSharpnessProperty AMDRadeonSuperResolutionSharpness
        {
            get { return amdRadeonSuperResolutionSharpness; }
        }

        private readonly AMDFluidMotionFrameSupportedProperty amdFluidMotionFrameSupported;
        public AMDFluidMotionFrameSupportedProperty AMDFluidMotionFrameSupported
        {
            get { return amdFluidMotionFrameSupported; }
        }

        private readonly AMDFluidMotionFrameEnabledProperty amdFluidMotionFrameEnabled;
        public AMDFluidMotionFrameEnabledProperty AMDFluidMotionFrameEnabled
        {
            get { return amdFluidMotionFrameEnabled; }
        }

        private readonly AMDFluidMotionFrameV1SupportedProperty amdFluidMotionFrameV1Supported;
        public AMDFluidMotionFrameV1SupportedProperty AMDFluidMotionFrameV1Supported
        {
            get { return amdFluidMotionFrameV1Supported; }
        }

        private readonly AMDFluidMotionFrameAlgorithmProperty amdFluidMotionFrameAlgorithm;
        public AMDFluidMotionFrameAlgorithmProperty AMDFluidMotionFrameAlgorithm
        {
            get { return amdFluidMotionFrameAlgorithm; }
        }

        private readonly AMDFluidMotionFrameSearchModeProperty amdFluidMotionFrameSearchMode;
        public AMDFluidMotionFrameSearchModeProperty AMDFluidMotionFrameSearchMode
        {
            get { return amdFluidMotionFrameSearchMode; }
        }

        private readonly AMDFluidMotionFramePerformanceModeProperty amdFluidMotionFramePerformanceMode;
        public AMDFluidMotionFramePerformanceModeProperty AMDFluidMotionFramePerformanceMode
        {
            get { return amdFluidMotionFramePerformanceMode; }
        }

        private readonly AMDFluidMotionFrameFastMotionResponseProperty amdFluidMotionFrameFastMotionResponse;
        public AMDFluidMotionFrameFastMotionResponseProperty AMDFluidMotionFrameFastMotionResponse
        {
            get { return amdFluidMotionFrameFastMotionResponse; }
        }

        private readonly AMDRadeonAntiLagSupportedProperty amdRadeonAntiLagSupported;
        public AMDRadeonAntiLagSupportedProperty AMDRadeonAntiLagSupported
        {
            get { return amdRadeonAntiLagSupported; }
        }

        private readonly AMDRadeonAntiLagEnabledProperty amdRadeonAntiLagEnabled;
        public AMDRadeonAntiLagEnabledProperty AMDRadeonAntiLagEnabled
        {
            get { return amdRadeonAntiLagEnabled; }
        }

        private readonly AMDRadeonBoostSupportedProperty amdRadeonBoostSupported;
        public AMDRadeonBoostSupportedProperty AMDRadeonBoostSupported
        {
            get { return amdRadeonBoostSupported; }
        }

        private readonly AMDRadeonBoostEnabledProperty amdRadeonBoostEnabled;
        public AMDRadeonBoostEnabledProperty AMDRadeonBoostEnabled
        {
            get { return amdRadeonBoostEnabled; }
        }

        private readonly AMDRadeonBoostResolutionProperty amdRadeonBoostResolution;
        public AMDRadeonBoostResolutionProperty AMDRadeonBoostResolution
        {
            get { return amdRadeonBoostResolution; }
        }

        private readonly AMDRadeonChillSupportedProperty amdRadeonChillSupported;
        public AMDRadeonChillSupportedProperty AMDRadeonChillSupported
        {
            get { return amdRadeonChillSupported; }
        }

        private readonly AMDRadeonChillEnabledProperty amdRadeonChillEnabled;
        public AMDRadeonChillEnabledProperty AMDRadeonChillEnabled
        {
            get { return amdRadeonChillEnabled; }
        }

        private readonly AMDRadeonChillMinFPSProperty amdRadeonChillMinFPS;
        public AMDRadeonChillMinFPSProperty AMDRadeonChillMinFPS
        {
            get { return amdRadeonChillMinFPS; }
        }

        private readonly AMDRadeonChillMaxFPSProperty amdRadeonChillMaxFPS;
        public AMDRadeonChillMaxFPSProperty AMDRadeonChillMaxFPS
        {
            get { return amdRadeonChillMaxFPS; }
        }

        private readonly AMDImageSharpeningSupportedProperty amdImageSharpeningSupported;
        public AMDImageSharpeningSupportedProperty AMDImageSharpeningSupported
        {
            get { return amdImageSharpeningSupported; }
        }

        private readonly AMDImageSharpeningEnabledProperty amdImageSharpeningEnabled;
        public AMDImageSharpeningEnabledProperty AMDImageSharpeningEnabled
        {
            get { return amdImageSharpeningEnabled; }
        }

        private readonly AMDImageSharpeningSharpnessProperty amdImageSharpeningSharpness;
        public AMDImageSharpeningSharpnessProperty AMDImageSharpeningSharpness
        {
            get { return amdImageSharpeningSharpness; }
        }

        // Display Color Properties
        private readonly AMDDisplayBrightnessSupportedProperty amdDisplayBrightnessSupported;
        public AMDDisplayBrightnessSupportedProperty AMDDisplayBrightnessSupported
        {
            get { return amdDisplayBrightnessSupported; }
        }

        private readonly AMDDisplayBrightnessProperty amdDisplayBrightness;
        public AMDDisplayBrightnessProperty AMDDisplayBrightness
        {
            get { return amdDisplayBrightness; }
        }

        private readonly AMDDisplayContrastSupportedProperty amdDisplayContrastSupported;
        public AMDDisplayContrastSupportedProperty AMDDisplayContrastSupported
        {
            get { return amdDisplayContrastSupported; }
        }

        private readonly AMDDisplayContrastProperty amdDisplayContrast;
        public AMDDisplayContrastProperty AMDDisplayContrast
        {
            get { return amdDisplayContrast; }
        }

        private readonly AMDDisplaySaturationSupportedProperty amdDisplaySaturationSupported;
        public AMDDisplaySaturationSupportedProperty AMDDisplaySaturationSupported
        {
            get { return amdDisplaySaturationSupported; }
        }

        private readonly AMDDisplaySaturationProperty amdDisplaySaturation;
        public AMDDisplaySaturationProperty AMDDisplaySaturation
        {
            get { return amdDisplaySaturation; }
        }

        private readonly AMDDisplayTemperatureSupportedProperty amdDisplayTemperatureSupported;
        public AMDDisplayTemperatureSupportedProperty AMDDisplayTemperatureSupported
        {
            get { return amdDisplayTemperatureSupported; }
        }

        private readonly AMDDisplayTemperatureProperty amdDisplayTemperature;
        public AMDDisplayTemperatureProperty AMDDisplayTemperature
        {
            get { return amdDisplayTemperature; }
        }

        private readonly InputInjector inputInjector;
        private readonly InjectedInputKeyboardInfo[] turnAMDOverlayOnOffKeyboardCombo;
        private readonly InjectedInputKeyboardInfo[] changeAMDOverlayLevelKeyboardCombo;
        private readonly List<Tuple<int, int>> amdOverlayLevelList;
        private readonly Dictionary<int, int> amdOverlayLevelMap;

        private long lastUpdate;

        // Set in Dispose so the deferred RSR cold-boot retry task knows to exit
        // rather than calling IsSupported on disposed ADLX interfaces (potential
        // access violation in unmanaged code, not a catchable exception).
        private volatile bool _disposed;

        public AMDManager() : base()
        {
            try
            {
                Logger.Info("Initializing ADLX...");

                // Log DLL search path info for debugging
                var currentDir = Environment.CurrentDirectory;
                var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                Logger.Info($"ADLX DLL search - CurrentDir: {currentDir}, ExeDir: {exeDir}");

                // Check if ADLXCSharpBind.dll exists
                var dllPath = System.IO.Path.Combine(exeDir ?? currentDir, "ADLXCSharpBind.dll");
                var dllExists = System.IO.File.Exists(dllPath);
                Logger.Info($"ADLX DLL path check - {dllPath} exists: {dllExists}");

                // Note: SetDllDirectory is called in Program.cs before manager initialization
                // to ensure native DLLs are found when running elevated from deployed location

                // Initialize ADLX with ADLXHelper
                Logger.Info("Creating ADLXHelper instance...");
                adlxHelper = new ADLXHelper();
                Logger.Info("ADLXHelper instance created, calling Initialize()...");
                adlxInitializeResult = adlxHelper.Initialize();

                if (adlxInitializeResult != ADLX_RESULT.ADLX_OK)
                {
                    Logger.Error("AMD Manager initialize failed.");
                    throw new Exception("ADLX initialization returned non-OK result");
                }

                adlxSystemSevices = adlxHelper.GetSystemServices();
                if (adlxSystemSevices == null)
                {
                    Logger.Error("Can't get AMD system service.");
                    throw new Exception("ADLX GetSystemServices returned null");
                }

            Logger.Info("Get AMD display services.");
            // Get display services
            var displayServicesPointer = ADLX.new_displaySerP_Ptr();
            adlxSystemSevices.GetDisplaysServices(displayServicesPointer);
            adlxDisplayServices = ADLX.displaySerP_Ptr_value(displayServicesPointer);

            // Get GPU
            var gpuListPointer = ADLX.new_gpuListP_Ptr();
            adlxSystemSevices.GetGPUs(gpuListPointer);
            var gpuList = ADLX.gpuListP_Ptr_value(gpuListPointer);

            Logger.Info($"Found {gpuList.Size()} GPU.");
            for (uint i = 0; i < gpuList.Size(); i++)
            {
                var gpuPointer = ADLX.new_gpuP_Ptr();
                gpuList.At(i, gpuPointer);
                var gpu = ADLX.gpuP_Ptr_value(gpuPointer);

                var gpuIsExternalPointer = ADLX.new_boolP();
                gpu.IsExternal(gpuIsExternalPointer);
                var gpuIsExternal = ADLX.boolP_value(gpuIsExternalPointer);

                Logger.Info($"GPU {i}: IsExternal={gpuIsExternal}");

                if (gpuIsExternal)
                {
                    if (adlxDedicatedGPU == null)
                    {
                        adlxDedicatedGPU = gpu;
                        Logger.Info($"Found a dGPU (external) at index {i}");
                    }
                    else if (adlxSecondDedicatedGPU == null)
                    {
                        adlxSecondDedicatedGPU = gpu;
                        Logger.Info($"Found second dGPU at index {i}");
                    }
                    else
                    {
                        Logger.Warn($"Found too many dGPUs at index {i}");
                    }
                }
                else
                {
                    if (adlxInternalGPU == null)
                    {
                        adlxInternalGPU = gpu;
                        Logger.Info($"Found an iGPU (internal) at index {i}");
                    }
                    else
                    {
                        // Store additional non-external GPUs as dedicated
                        if (adlxDedicatedGPU == null)
                        {
                            adlxDedicatedGPU = gpu;
                            Logger.Info($"Found additional GPU (storing as dGPU) at index {i}");
                        }
                        else
                        {
                            Logger.Warn($"Found too many GPUs at index {i}");
                        }
                    }
                }
            }

            // If no iGPU found but we have a dGPU, use dGPU for 3D settings
            if (adlxInternalGPU == null && adlxDedicatedGPU != null)
            {
                Logger.Info("No iGPU found, using dGPU for 3D settings");
                adlxInternalGPU = adlxDedicatedGPU;
            }

            if (adlxInternalGPU == null)
            {
                Logger.Error("No AMD GPU found! AMD features will not work.");
            }

            Logger.Info("Get AMD 3D Settings Services.");
            var threeDSettingsServicesPointer = ADLX.new_threeDSettingsSerP_Ptr();
            adlxSystemSevices.Get3DSettingsServices(threeDSettingsServicesPointer);
            adlx3DSettingsServices = new IADLX3DSettingsServices2(ADLXPINVOKE.threeDSettingsSerP_Ptr_value(SWIGTYPE_p_p_adlx__IADLX3DSettingsServices.getCPtr(threeDSettingsServicesPointer)), false);

            Logger.Info("Get Radeon Super Resolution.");
            var threeDRadeonSuperResolutionPointer = ADLX.new_threeDRadeonSuperResolutionP_Ptr();
            adlx3DSettingsServices.GetRadeonSuperResolution(threeDRadeonSuperResolutionPointer);
            var threeDRadeonSuperResolution = ADLX.threeDRadeonSuperResolutionP_Ptr_value(threeDRadeonSuperResolutionPointer);
            amdRadeonSuperResolutionSetting = new AMDRadeonSuperResolutionSetting(threeDRadeonSuperResolution);
            amdRadeonSuperResolutionSupported = new AMDRadeonSuperResolutionSupportedProperty(amdRadeonSuperResolutionSetting.IsSupported(), this);
            amdRadeonSuperResolutionEnabled = new AMDRadeonSuperResolutionEnabledProperty(amdRadeonSuperResolutionSetting.IsEnabled(), this);
            amdRadeonSuperResolutionSharpness = new AMDRadeonSuperResolutionSharpnessProperty(amdRadeonSuperResolutionSetting.GetSharpness(), this);

            Logger.Info("Get AMD Fluid Motion Frame.");
            var threeDFluidMotionFramePointer = ADLX.new_threeDAMDFluidMotionFramesP_Ptr();
            adlx3DSettingsServices.GetAMDFluidMotionFrames(threeDFluidMotionFramePointer);
            var threeDFluidMotionFrame = ADLX.threeDAMDFluidMotionFramesP_Ptr_value(threeDFluidMotionFramePointer);
            amdFluidMotionFrameSetting = new AMDFluidMotionFrameSetting(threeDFluidMotionFrame);
            amdFluidMotionFrameSupported = new AMDFluidMotionFrameSupportedProperty(amdFluidMotionFrameSetting.IsSupported(), this);
            amdFluidMotionFrameEnabled = new AMDFluidMotionFrameEnabledProperty(amdFluidMotionFrameSetting.IsEnabled(), this);

            // ADLX 1.5+ AFMF 2.x extended controls. Construct the v1 wrapper from the
            // same C++ pointer the v0 wrapper holds — IADLX3DAMDFluidMotionFrames1
            // inherits from IADLX3DAMDFluidMotionFrames in the SDK, so the cast is
            // structurally valid. On drivers without AFMF 2.x support, the underlying
            // vtable methods return ADLX_NOT_SUPPORTED, which AMDFluidMotionFrameSettingV1
            // surfaces as default values. Initial probe with IsAlgorithmSupported logs
            // whether v1 is actually live so we can correlate widget UI state with
            // driver capability.
            try
            {
                var afmfV1Raw = ADLXPINVOKE.threeDAMDFluidMotionFramesP_Ptr_value(
                    SWIGTYPE_p_p_adlx__IADLX3DAMDFluidMotionFrames.getCPtr(threeDFluidMotionFramePointer));
                if (afmfV1Raw != System.IntPtr.Zero)
                {
                    var afmfV1Iface = new IADLX3DAMDFluidMotionFrames1(afmfV1Raw, false);
                    amdFluidMotionFrameSettingV1 = new AMDFluidMotionFrameSettingV1(afmfV1Iface);
                    bool algoSupported = amdFluidMotionFrameSettingV1.IsAlgorithmSupported();
                    Logger.Info($"AFMF v1 (extended controls) wrapper constructed (algorithmSupported={algoSupported}, "
                        + $"algo={amdFluidMotionFrameSettingV1.GetAlgorithm()}, "
                        + $"searchMode={amdFluidMotionFrameSettingV1.GetSearchMode()}, "
                        + $"performanceMode={amdFluidMotionFrameSettingV1.GetPerformanceMode()}, "
                        + $"fastMotionResponse={amdFluidMotionFrameSettingV1.GetFastMotionResponse()})");
                }
                else
                {
                    Logger.Warn("AFMF v1: underlying pointer is null, leaving v1 wrapper unset");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warn($"AFMF v1 wrapper init threw {ex.GetType().Name}: {ex.Message} — extended controls unavailable");
            }

            // ADLX 1.5+: VGM (Variable Graphics Memory) probe — DISABLED in 2093 after
            // 2092's QueryInterface(IADLXSystem3, ppVoid) approach hung AMDManager init,
            // causing a helper crash-loop. The Marshal.AllocHGlobal + manually-constructed
            // SWIGTYPE_p_p_void path apparently doesn't survive ADLX's expectations on
            // this driver — the call never returns, AMDManager never DONE, bootstrapper
            // respawns the helper. Need a different approach (likely add proper
            // %pointer_functions(void*, ...) to SWIG and regenerate) before we can
            // re-attempt VGM. The raw-cast version from 2087/2091 returned
            // ADLX_INVALID_ARGS without hanging, so it's at least safe — but useless,
            // since the VGM call routes to the wrong vtable. Pure no-op for now to
            // unblock the helper.

            // GPU Tuning probe was REMOVED in 2090 — both 2088 (with DDR + tuning probes)
            // and 2089 (tuning only) crashed at AMDManager init with AccessViolation
            // inside ADLX. The cause is unclear: GetGPUTuningServices is on IADLXSystem
            // (no risky cast), but something in this path (regenerated SWIG bindings,
            // AMDXyz vtable layout on Legion Go 2's specific driver) blows up. Need to
            // rebuild from a cleaner known-good state before reintroducing tuning probes.
            //
            // DDR (Dynamic Refresh Rate Control) probe was also removed in 2089 — the
            // IADLXDisplayServices3 cast trick is unsafe (DisplayServices3 isn't
            // layout-compatible with DisplayServices on this driver). Both probes need
            // proper QueryInterface routing rather than raw pointer reuse.

            // AFMF 2.x extended-control properties. Always constructed so the propertyList
            // 2088 produced an AccessViolation at AMDManager init. The IADLXSystem3 cast
            // trick (reusing the IADLXSystem raw pointer) worked for the VGM probe
            // because AMD's runtime System object happens to be System3-layout
            // compatible, but IADLXDisplayServices is NOT layout-compatible with
            // IADLXDisplayServices3 — calling GetDynamicRefreshRateControl on the v0
            // pointer dereferences garbage at offset (3.h)0x… in the vtable. Proper
            // QueryInterface routing needed before re-introducing this probe.

            // AFMF 2.x extended-control properties. Always constructed so the propertyList
            // in Program.cs has them registered for pipe routing; values come from the v1
            // wrapper when available and defaults when not, with V1Supported gating the UI.
            bool v1Available = amdFluidMotionFrameSettingV1 != null && amdFluidMotionFrameSettingV1.IsAlgorithmSupported();
            amdFluidMotionFrameV1Supported = new AMDFluidMotionFrameV1SupportedProperty(v1Available, this);
            amdFluidMotionFrameAlgorithm = new AMDFluidMotionFrameAlgorithmProperty(
                v1Available ? (int)amdFluidMotionFrameSettingV1.GetAlgorithm() : 0, this);
            amdFluidMotionFrameSearchMode = new AMDFluidMotionFrameSearchModeProperty(
                v1Available ? (int)amdFluidMotionFrameSettingV1.GetSearchMode() : 0, this);
            amdFluidMotionFramePerformanceMode = new AMDFluidMotionFramePerformanceModeProperty(
                v1Available ? (int)amdFluidMotionFrameSettingV1.GetPerformanceMode() : 0, this);
            amdFluidMotionFrameFastMotionResponse = new AMDFluidMotionFrameFastMotionResponseProperty(
                v1Available ? (int)amdFluidMotionFrameSettingV1.GetFastMotionResponse() : 0, this);

            // GPU-specific 3D settings - only initialize if we have a GPU
            if (adlxInternalGPU != null)
            {
                Logger.Info("Get AMD Anti-Lag.");
                var threeDAntiLagPointer = ADLX.new_threeDAntiLagP_Ptr();
                adlx3DSettingsServices.GetAntiLag(adlxInternalGPU, threeDAntiLagPointer);
                var threeDAntiLag = ADLX.threeDAntiLagP_Ptr_value(threeDAntiLagPointer);
                amdRadeonAntiLagSetting = new AMDRadeonAntiLagSetting(threeDAntiLag);
                amdRadeonAntiLagSupported = new AMDRadeonAntiLagSupportedProperty(amdRadeonAntiLagSetting.IsSupported(), this);
                amdRadeonAntiLagEnabled = new AMDRadeonAntiLagEnabledProperty(amdRadeonAntiLagSetting.IsEnabled(), this);

                Logger.Info("Get AMD Radeon Boost.");
                var threeDRadeonBoostPointer = ADLX.new_threeDBoostP_Ptr();
                adlx3DSettingsServices.GetBoost(adlxInternalGPU, threeDRadeonBoostPointer);
                var threeDRadeonBoost = ADLX.threeDBoostP_Ptr_value(threeDRadeonBoostPointer);
                amdRadeonBoostSetting = new AMDRadeonBoostSetting(threeDRadeonBoost);
                amdRadeonBoostSupported = new AMDRadeonBoostSupportedProperty(amdRadeonBoostSetting.IsSupported(), this);
                amdRadeonBoostEnabled = new AMDRadeonBoostEnabledProperty(amdRadeonBoostSetting.IsEnabled(), this);
                var amdRadeonBoostResolutionRange = amdRadeonBoostSetting.GetResolutionRange();
                amdRadeonBoostResolution = new AMDRadeonBoostResolutionProperty(amdRadeonBoostSetting.GetResolution() == amdRadeonBoostResolutionRange.Item1 ? 0 : 1, this);

                Logger.Info("Get AMD Radeon Chill.");
                var threeDRadeonChillPointer = ADLX.new_threeDChillP_Ptr();
                adlx3DSettingsServices.GetChill(adlxInternalGPU, threeDRadeonChillPointer);
                var threeDRadeonChill = ADLX.threeDChillP_Ptr_value(threeDRadeonChillPointer);
                amdRadeonChillSetting = new AMDRadeonChillSetting(threeDRadeonChill);
                amdRadeonChillEnabled = new AMDRadeonChillEnabledProperty(amdRadeonChillSetting.IsEnabled(), this);
                amdRadeonChillSupported = new AMDRadeonChillSupportedProperty(amdRadeonChillSetting.IsSupported(), this);
                amdRadeonChillMinFPS = new AMDRadeonChillMinFPSProperty(amdRadeonChillSetting.GetMinFPS(), this);
                amdRadeonChillMaxFPS = new AMDRadeonChillMaxFPSProperty(amdRadeonChillSetting.GetMaxFPS(), this);

                Logger.Info("Get AMD Image Sharpening.");
                var threeDImageSharpeningPointer = ADLX.new_threeDImageSharpeningP_Ptr();
                adlx3DSettingsServices.GetImageSharpening(adlxInternalGPU, threeDImageSharpeningPointer);
                var threeDImageSharpening = ADLX.threeDImageSharpeningP_Ptr_value(threeDImageSharpeningPointer);
                amdImageSharpeningSetting = new AMDImageSharpeningSetting(threeDImageSharpening);
                amdImageSharpeningSupported = new AMDImageSharpeningSupportedProperty(amdImageSharpeningSetting.IsSupported(), this);
                amdImageSharpeningEnabled = new AMDImageSharpeningEnabledProperty(amdImageSharpeningSetting.IsEnabled(), this);
                amdImageSharpeningSharpness = new AMDImageSharpeningSharpnessProperty(amdImageSharpeningSetting.GetSharpness(), this);
            }
            else
            {
                Logger.Warn("No GPU available - GPU-specific 3D settings will not be initialized (Anti-Lag, Boost, Chill, Image Sharpening)");
                // Create null/default settings to avoid null reference exceptions
                amdRadeonAntiLagSetting = new AMDRadeonAntiLagSetting(null);
                amdRadeonAntiLagSupported = new AMDRadeonAntiLagSupportedProperty(false, this);
                amdRadeonAntiLagEnabled = new AMDRadeonAntiLagEnabledProperty(false, this);

                amdRadeonBoostSetting = new AMDRadeonBoostSetting(null);
                amdRadeonBoostSupported = new AMDRadeonBoostSupportedProperty(false, this);
                amdRadeonBoostEnabled = new AMDRadeonBoostEnabledProperty(false, this);
                amdRadeonBoostResolution = new AMDRadeonBoostResolutionProperty(0, this);

                amdRadeonChillSetting = new AMDRadeonChillSetting(null);
                amdRadeonChillEnabled = new AMDRadeonChillEnabledProperty(false, this);
                amdRadeonChillSupported = new AMDRadeonChillSupportedProperty(false, this);
                amdRadeonChillMinFPS = new AMDRadeonChillMinFPSProperty(0, this);
                amdRadeonChillMaxFPS = new AMDRadeonChillMaxFPSProperty(0, this);

                amdImageSharpeningSetting = new AMDImageSharpeningSetting(null);
                amdImageSharpeningSupported = new AMDImageSharpeningSupportedProperty(false, this);
                amdImageSharpeningEnabled = new AMDImageSharpeningEnabledProperty(false, this);
                amdImageSharpeningSharpness = new AMDImageSharpeningSharpnessProperty(0, this);
            }

            Logger.Info("Get AMD Display Custom Color.");
            // Get display list and find a display that supports custom color
            var displayListPointer = ADLX.new_displayListP_Ptr();
            adlxDisplayServices.GetDisplays(displayListPointer);
            var displayList = ADLX.displayListP_Ptr_value(displayListPointer);
            Logger.Info($"Display list: {displayList}, Size: {displayList?.Size() ?? 0}");

            bool foundSupportedDisplay = false;
            if (displayList != null && displayList.Size() > 0)
            {
                // Try each display to find one that supports custom color
                for (uint i = 0; i < displayList.Size(); i++)
                {
                    var displayPointer = ADLX.new_displayP_Ptr();
                    displayList.At(i, displayPointer);
                    var display = ADLX.displayP_Ptr_value(displayPointer);
                    Logger.Info($"Checking display {i}: {display}");

                    var displayCustomColorPointer = ADLX.new_displayCustomColorP_Ptr();
                    var customColorResult = adlxDisplayServices.GetCustomColor(display, displayCustomColorPointer);
                    Logger.Info($"Display {i} GetCustomColor result: {customColorResult}");

                    if (customColorResult == ADLX_RESULT.ADLX_OK)
                    {
                        var displayCustomColor = ADLX.displayCustomColorP_Ptr_value(displayCustomColorPointer);
                        Logger.Info($"Display {i} CustomColor: {displayCustomColor}");

                        if (displayCustomColor != null)
                        {
                            var tempSetting = new AMDDisplayCustomColorSetting(displayCustomColor);
                            bool brightnessSupported = tempSetting.IsBrightnessSupported();
                            bool contrastSupported = tempSetting.IsContrastSupported();
                            bool saturationSupported = tempSetting.IsSaturationSupported();
                            bool temperatureSupported = tempSetting.IsTemperatureSupported();

                            Logger.Info($"Display {i} supports: Brightness={brightnessSupported}, Contrast={contrastSupported}, Saturation={saturationSupported}, Temperature={temperatureSupported}");

                            // If this display supports any custom color feature, use it
                            if (brightnessSupported || contrastSupported || saturationSupported || temperatureSupported)
                            {
                                amdDisplayCustomColorSetting = tempSetting;
                                amdDisplayBrightnessSupported = new AMDDisplayBrightnessSupportedProperty(brightnessSupported, this);
                                amdDisplayBrightness = new AMDDisplayBrightnessProperty(amdDisplayCustomColorSetting.GetBrightness(), this);
                                amdDisplayContrastSupported = new AMDDisplayContrastSupportedProperty(contrastSupported, this);
                                amdDisplayContrast = new AMDDisplayContrastProperty(amdDisplayCustomColorSetting.GetContrast(), this);
                                amdDisplaySaturationSupported = new AMDDisplaySaturationSupportedProperty(saturationSupported, this);
                                amdDisplaySaturation = new AMDDisplaySaturationProperty(amdDisplayCustomColorSetting.GetSaturation(), this);
                                amdDisplayTemperatureSupported = new AMDDisplayTemperatureSupportedProperty(temperatureSupported, this);
                                amdDisplayTemperature = new AMDDisplayTemperatureProperty(amdDisplayCustomColorSetting.GetTemperature(), this);
                                Logger.Info($"Using display {i} for custom color settings");
                                foundSupportedDisplay = true;
                                break;
                            }
                            else
                            {
                                tempSetting.Dispose();
                            }
                        }
                    }
                }
            }

            if (!foundSupportedDisplay)
            {
                Logger.Warn("No displays with custom color support found, using defaults.");
                amdDisplayBrightnessSupported = new AMDDisplayBrightnessSupportedProperty(false, this);
                amdDisplayBrightness = new AMDDisplayBrightnessProperty(0, this);
                amdDisplayContrastSupported = new AMDDisplayContrastSupportedProperty(false, this);
                amdDisplayContrast = new AMDDisplayContrastProperty(100, this);
                amdDisplaySaturationSupported = new AMDDisplaySaturationSupportedProperty(false, this);
                amdDisplaySaturation = new AMDDisplaySaturationProperty(100, this);
                amdDisplayTemperatureSupported = new AMDDisplayTemperatureSupportedProperty(false, this);
                amdDisplayTemperature = new AMDDisplayTemperatureProperty(6500, this);
            }

            Logger.Info("AMD Manager initialized successfully.");

            // Cold-boot recovery for RSR (issue #79 / Rayekkk):
            // On Windows cold boot the AMD driver may not have fully initialized
            // its RSR feature when this constructor runs. GetRadeonSuperResolution
            // returns a usable interface but IsSupported() returns non-ADLX_OK,
            // which we map to false — and amdRadeonSuperResolutionSupported then
            // stays false for the helper's lifetime, showing "N/A" in the widget.
            // The 3DSettingsChangedListener only refreshes Enabled, never Supported,
            // so the user has to reinstall (which restarts the helper later, when
            // the driver is ready). RIS/Anti-Lag don't hit this because they're
            // queried inside the GPU-enumeration block above, which gives the
            // driver extra time to settle. Retry IsSupported a few times with
            // backoff and push the update to the widget if it flips true.
            if (amdRadeonSuperResolutionSetting != null && !amdRadeonSuperResolutionSupported.Value)
            {
                Task.Run(async () =>
                {
                    int[] backoffMs = new[] { 2000, 5000, 15000 };
                    foreach (var delay in backoffMs)
                    {
                        await Task.Delay(delay);
                        // Bail out if the manager was disposed while we slept.
                        // After Dispose the underlying ADLX interfaces are torn down,
                        // and IsSupported on a disposed wrapper can crash unmanaged code
                        // (not a catchable .NET exception).
                        if (_disposed)
                        {
                            Logger.Debug("RSR cold-boot retry aborted — AMDManager disposed.");
                            return;
                        }
                        try
                        {
                            if (!amdRadeonSuperResolutionSetting.IsSupported())
                            {
                                continue;
                            }

                            Logger.Info($"RSR Supported flipped to true on cold-boot retry after ~{delay}ms — refreshing widget state.");
                            amdRadeonSuperResolutionSupported.SetValue(true);

                            bool isEnabledNow = amdRadeonSuperResolutionSetting.IsEnabled();
                            if (isEnabledNow != amdRadeonSuperResolutionEnabled.Value)
                            {
                                amdRadeonSuperResolutionEnabled.SetValue(isEnabledNow);
                            }

                            int sharpnessNow = amdRadeonSuperResolutionSetting.GetSharpness();
                            if (sharpnessNow != amdRadeonSuperResolutionSharpness.Value)
                            {
                                amdRadeonSuperResolutionSharpness.SetValue(sharpnessNow);
                            }
                            return;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"RSR cold-boot retry failed (delay={delay}ms): {ex.Message}");
                        }
                    }
                    Logger.Info("RSR cold-boot retry sequence finished without flipping to supported.");
                });
            }

            amdFluidMotionFrameEnabled.PropertyChanged += AmdFluidMotionFrameEnabled;
            amdRadeonAntiLagEnabled.PropertyChanged += AmdRadeonAntiLagEnabled;
            amdRadeonBoostEnabled.PropertyChanged += AmdRadeonBoostEnabled;
            amdRadeonChillEnabled.PropertyChanged += AmdRadeonChillEnabled;

            var threeDSettingsChangedHandlingPointer = ADLX.new_threeDSettingsChangedHandlingP_Ptr();
            //ADLX.new_threeDSettingsChangedHandlingP_Ptr
            adlx3DSettingsServices.Get3DSettingsChangedHandling(threeDSettingsChangedHandlingPointer);
            var threeDSettingsChangedHandling = ADLX.threeDSettingsChangedHandlingP_Ptr_value(threeDSettingsChangedHandlingPointer);
            amd3DSettingsChangedListener = new AMD3DSettingsChangedListener(this);
            threeDSettingsChangedHandling.Add3DSettingsEventListener(amd3DSettingsChangedListener);

            inputInjector = InputInjector.TryCreate();
            turnAMDOverlayOnOffKeyboardCombo = new InjectedInputKeyboardInfo[]
            {
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftControl, KeyOptions = InjectedInputKeyOptions.None },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftShift, KeyOptions = InjectedInputKeyOptions.None },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.O, KeyOptions = InjectedInputKeyOptions.None },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftControl, KeyOptions = InjectedInputKeyOptions.KeyUp },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftShift, KeyOptions = InjectedInputKeyOptions.KeyUp },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.O, KeyOptions = InjectedInputKeyOptions.KeyUp },
            };
            changeAMDOverlayLevelKeyboardCombo = new InjectedInputKeyboardInfo[]
            {
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftControl, KeyOptions = InjectedInputKeyOptions.None },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftShift, KeyOptions = InjectedInputKeyOptions.None },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.X, KeyOptions = InjectedInputKeyOptions.None },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftControl, KeyOptions = InjectedInputKeyOptions.KeyUp },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.LeftShift, KeyOptions = InjectedInputKeyOptions.KeyUp },
                new InjectedInputKeyboardInfo { VirtualKey = (ushort)VirtualKey.X, KeyOptions = InjectedInputKeyOptions.KeyUp }
            };
            // In AMD Software: Adrenaline Edition:
            // Level 3 is basic (FPS only)                      => Our level 1 (FPS)
            // Level 1 is intermediate (FPS + Usage + Wattage)  => Our level 2 (BATTERY)
            // Level 2 is advanced (many elements)              => Our level 3 (DETAILED)
            // Level 0 is custom (user seletectable)            => Our level 4 (FULL)
            amdOverlayLevelList = new List<Tuple<int, int>>()
            {
                new Tuple<int, int>(1, 3),
                new Tuple<int, int>(2, 1),
                new Tuple<int, int>(3, 2),
                new Tuple<int, int>(4, 0),
            };
            amdOverlayLevelMap = new Dictionary<int, int>();
            foreach (var amdOverlayLevel in amdOverlayLevelList)
            {
                amdOverlayLevelMap.Add(amdOverlayLevel.Item1, amdOverlayLevel.Item2);
            }
            lastUpdate = 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"ADLX initialization failed with exception: {ex.Message}");
                Logger.Error($"Exception type: {ex.GetType().FullName}");

                // Log inner exceptions to reveal the actual cause (especially for TypeInitializationException)
                var innerEx = ex.InnerException;
                while (innerEx != null)
                {
                    Logger.Error($"Inner exception: {innerEx.GetType().FullName}: {innerEx.Message}");
                    innerEx = innerEx.InnerException;
                }

                Logger.Error($"Stack trace: {ex.StackTrace}");

                // Provide guidance on common causes
                if (ex.Message.Contains("ADLXPINVOKE") || ex.Message.Contains("type initializer"))
                {
                    Logger.Error("ADLX DLL failed to load. Possible causes:");
                    Logger.Error("  1. AMD Adrenalin drivers not installed (ADLX requires AMD driver)");
                    Logger.Error("  2. AMD driver version incompatible with ADLX SDK");
                    Logger.Error("  3. Visual C++ Runtime not installed");
                    Logger.Error("  4. System has no AMD GPU");
                }

                adlxInitializeResult = ADLX_RESULT.ADLX_FAIL;

                // Initialize properties with default/disabled values
                amdRadeonSuperResolutionSupported = amdRadeonSuperResolutionSupported ?? new AMDRadeonSuperResolutionSupportedProperty(false, this);
                amdRadeonSuperResolutionEnabled = amdRadeonSuperResolutionEnabled ?? new AMDRadeonSuperResolutionEnabledProperty(false, this);
                amdRadeonSuperResolutionSharpness = amdRadeonSuperResolutionSharpness ?? new AMDRadeonSuperResolutionSharpnessProperty(0, this);
                amdFluidMotionFrameSupported = amdFluidMotionFrameSupported ?? new AMDFluidMotionFrameSupportedProperty(false, this);
                amdFluidMotionFrameEnabled = amdFluidMotionFrameEnabled ?? new AMDFluidMotionFrameEnabledProperty(false, this);
                amdRadeonAntiLagSupported = amdRadeonAntiLagSupported ?? new AMDRadeonAntiLagSupportedProperty(false, this);
                amdRadeonAntiLagEnabled = amdRadeonAntiLagEnabled ?? new AMDRadeonAntiLagEnabledProperty(false, this);
                amdRadeonBoostSupported = amdRadeonBoostSupported ?? new AMDRadeonBoostSupportedProperty(false, this);
                amdRadeonBoostEnabled = amdRadeonBoostEnabled ?? new AMDRadeonBoostEnabledProperty(false, this);
                amdRadeonBoostResolution = amdRadeonBoostResolution ?? new AMDRadeonBoostResolutionProperty(0, this);
                amdRadeonChillSupported = amdRadeonChillSupported ?? new AMDRadeonChillSupportedProperty(false, this);
                amdRadeonChillEnabled = amdRadeonChillEnabled ?? new AMDRadeonChillEnabledProperty(false, this);
                amdRadeonChillMinFPS = amdRadeonChillMinFPS ?? new AMDRadeonChillMinFPSProperty(0, this);
                amdRadeonChillMaxFPS = amdRadeonChillMaxFPS ?? new AMDRadeonChillMaxFPSProperty(0, this);
                amdImageSharpeningSupported = amdImageSharpeningSupported ?? new AMDImageSharpeningSupportedProperty(false, this);
                amdImageSharpeningEnabled = amdImageSharpeningEnabled ?? new AMDImageSharpeningEnabledProperty(false, this);
                amdImageSharpeningSharpness = amdImageSharpeningSharpness ?? new AMDImageSharpeningSharpnessProperty(0, this);
                amdDisplayBrightnessSupported = amdDisplayBrightnessSupported ?? new AMDDisplayBrightnessSupportedProperty(false, this);
                amdDisplayBrightness = amdDisplayBrightness ?? new AMDDisplayBrightnessProperty(0, this);
                amdDisplayContrastSupported = amdDisplayContrastSupported ?? new AMDDisplayContrastSupportedProperty(false, this);
                amdDisplayContrast = amdDisplayContrast ?? new AMDDisplayContrastProperty(0, this);
                amdDisplaySaturationSupported = amdDisplaySaturationSupported ?? new AMDDisplaySaturationSupportedProperty(false, this);
                amdDisplaySaturation = amdDisplaySaturation ?? new AMDDisplaySaturationProperty(0, this);
                amdDisplayTemperatureSupported = amdDisplayTemperatureSupported ?? new AMDDisplayTemperatureSupportedProperty(false, this);
                amdDisplayTemperature = amdDisplayTemperature ?? new AMDDisplayTemperatureProperty(0, this);
            }
        }

        private void AmdRadeonChillEnabled(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (amdRadeonChillEnabled)
            {
                if (amdRadeonAntiLagSupported && amdRadeonAntiLagEnabled)
                {
                    Logger.Info($"AMD Radeon Chill enabled, Radeon Anti-Lag should be disabled too.");
                    amdRadeonAntiLagEnabled.SetValue(false);
                }
                else
                {
                    Logger.Info($"AMD Radeon Chill enabled but Radeon Anti-Lag is not supported or enabled.");
                }

                if (amdRadeonBoostSupported && amdRadeonBoostEnabled)
                {
                    Logger.Info($"AMD Radeon Chill enabled, Radeon Boost should be disabled too.");
                    amdRadeonBoostEnabled.SetValue(false);
                }
                else
                {
                    Logger.Info($"AMD Radeon Chill enabled but Radeon Boost is not supported or enabled.");
                }
            }
        }

        private void AmdRadeonBoostEnabled(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (amdRadeonBoostEnabled)
            {
                if (amdRadeonChillSupported && amdRadeonChillEnabled)
                {
                    Logger.Info($"Radeon Boost enabled, AMD Radeon Chill should be disabled too.");
                    amdRadeonChillEnabled.SetValue(false);
                }
                else
                {
                    Logger.Info($"Radeon Boost enabled but AMD Radeon Chill is not supported or enabled.");
                }
            }
        }

        private void AmdRadeonAntiLagEnabled(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (amdRadeonAntiLagEnabled)
            {
                if (amdRadeonChillSupported && amdRadeonChillEnabled)
                {
                    Logger.Info($"Radeon Anti-Lag enabled, AMD Radeon Chill should be disabled too.");
                    amdRadeonChillEnabled.SetValue(false);
                }
                else
                {
                    Logger.Info($"Radeon Anti-Lag enabled but AMD Radeon Chill is not supported or enabled.");
                }
            }
        }

        private void AmdFluidMotionFrameEnabled(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (amdFluidMotionFrameEnabled)
            {
                if (amdRadeonAntiLagSupported && !amdRadeonAntiLagEnabled)
                {
                    Logger.Info($"AMD Fluid Motion Frame enabled, Radeon Anti-Lag should be enabled too.");
                    amdRadeonAntiLagEnabled.SetValue(true);
                }
                else
                {
                    Logger.Info($"AMD Fluid Motion Frame enabled but Radeon Anti-Lag is not supported or already enabled.");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Signal the deferred RSR retry task to bail out before we tear down
                // the ADLX interfaces below. volatile so the retry task observes it
                // promptly when it wakes from Task.Delay.
                _disposed = true;
                Logger.Info("AMDManager: Disposing ADLX resources");
                adlxDisplayServices?.Dispose();
                adlxInternalGPU?.Dispose();
                adlxDedicatedGPU?.Dispose();
                adlxSecondDedicatedGPU?.Dispose();
                adlx3DSettingsServices?.Dispose();
                amdRadeonSuperResolutionSetting?.Dispose();
                amdFluidMotionFrameSetting?.Dispose();
                amdImageSharpeningSetting?.Dispose();
                amdDisplayCustomColorSetting?.Dispose();
                adlxHelper?.Dispose();
                Logger.Info("AMDManager: ADLX resources disposed");
            }
            base.Dispose(disposing);
        }

        public override void Update()
        {
            base.Update();

            var now = DateTime.Now.Ticks;
            Logger.Debug($"Time since last update: {now - lastUpdate}");
            if (now - lastUpdate < TimeSpan.TicksPerSecond * 2)
            {
                return;
            }
            lastUpdate = now;

            if (IsInUsed)
            {
                SetAMDValues();
            }
        }

        public override void SetLevel(int level)
        {
            base.SetLevel(level);

            // SetAMDValues();
        }

        private async void SetAMDValues()
        {
            try
            {
                var (currentlyOn, currentLevel) = ReadCurrentMetricsProfile();
                if (onScreenDisplayLevel == 0)
                {
                    if (currentlyOn == 1)
                    {
                        Logger.Info("Turning OFF AMD On-Screen Display.");
                        inputInjector.InjectKeyboardInput(turnAMDOverlayOnOffKeyboardCombo);
                    }
                    else
                    {
                        Logger.Info("AMD On-Screen Display is already turned OFF.");
                    }
                }
                else
                {
                    if (currentlyOn == 0)
                    {
                        Logger.Info("Turning ON AMD On-Screen Display.");
                        inputInjector.InjectKeyboardInput(turnAMDOverlayOnOffKeyboardCombo);
                        await Task.Delay(100);
                    }

                    var targetLevel = amdOverlayLevelMap[onScreenDisplayLevel];
                    if (currentLevel != targetLevel)
                    {
                        var currentLevelIndex = 0;
                        var targetLevelIndex = 0;
                        for (var i = 0; i < amdOverlayLevelList.Count; i++)
                        {
                            if (amdOverlayLevelList[i].Item2 == currentLevel)
                            {
                                currentLevelIndex = i;
                            }
                            if (amdOverlayLevelList[i].Item2 == targetLevel)
                            {
                                targetLevelIndex = i;
                            }
                        }

                        var numberOfKeyPresses = Math.Abs(targetLevelIndex - currentLevelIndex);
                        Logger.Info($"Current AMD On-Screen Display level is {currentLevel} at index {currentLevelIndex}, need to change to {targetLevel} at index {targetLevelIndex}, need to press {numberOfKeyPresses} times.");
                        for (var i = 0; i < numberOfKeyPresses; i++)
                        {
                            inputInjector.InjectKeyboardInput(changeAMDOverlayLevelKeyboardCombo);
                            await Task.Delay(100);
                        }
                    }
                    else
                    {
                        Logger.Info($"Current AMD On-Screen Display level is {currentLevel} already matches {targetLevel}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in SetAMDValues: {ex.Message}");
            }
        }

        private static Tuple<int, int> ReadCurrentMetricsProfile()
        {
            try
            {
                using (RegistryKey subKey = AMD_PERFORMANCE_KEY_ROOT.OpenSubKey(AMD_PERFORMANCE_KEY_PATH))
                {
                    if (subKey != null)
                    {
                        object stateObject = subKey.GetValue(AMD_PERFORMANCE_STATE_KEY_NAME);

                        if (stateObject != null)
                        {
                            Logger.Debug($"Value of '{AMD_PERFORMANCE_STATE_KEY_NAME}' at '{AMD_PERFORMANCE_KEY_PATH}': {stateObject} of type {stateObject.GetType().Name}");
                            var stateValue = (int)stateObject;
                            if (stateValue == 0)
                            {
                                return new Tuple<int, int>(0, 0);
                            }
                            else
                            {
                                var profileObject = subKey.GetValue(AMD_PERFORMANCE_PROFILE_KEY_NAME);
                                if (profileObject != null)
                                {
                                    Logger.Debug($"Value of {AMD_PERFORMANCE_PROFILE_KEY_NAME} is {profileObject} of type {profileObject.GetType().Name}");
                                    var profileValue = (int)profileObject;
                                    return new Tuple<int, int>(stateValue, profileValue);
                                }
                                else
                                {
                                    return new Tuple<int, int>(stateValue, 0);
                                }
                            }
                        }
                        else
                        {
                            Logger.Warn($"Value '{AMD_PERFORMANCE_STATE_KEY_NAME}' not found in '{AMD_PERFORMANCE_KEY_PATH}'.");
                            return new Tuple<int, int>(0, 0);
                        }
                    }
                    else
                    {
                        Logger.Warn($"Registry key '{AMD_PERFORMANCE_KEY_PATH}' not found.");
                        return new Tuple<int, int>(0, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred: {ex.Message}");
                return new Tuple<int, int>(0, 0);
            }
        }

        private static bool SetCurrentMetricsProfile(int metricOverlayState, int metricProfile)
        {
            try
            {
                using (RegistryKey subKey = AMD_PERFORMANCE_KEY_ROOT.OpenSubKey(AMD_PERFORMANCE_KEY_PATH))
                {
                    if (subKey != null)
                    {
                        subKey.SetValue(AMD_PERFORMANCE_STATE_KEY_NAME, metricOverlayState);
                        subKey.SetValue(AMD_PERFORMANCE_PROFILE_KEY_NAME, metricProfile);
                        Logger.Debug($"Set registry key '{AMD_PERFORMANCE_KEY_PATH}\\{AMD_PERFORMANCE_STATE_KEY_NAME}' to {metricOverlayState} and '{AMD_PERFORMANCE_KEY_PATH}\\{AMD_PERFORMANCE_PROFILE_KEY_NAME}' to {metricProfile}.");
                        return true;
                    }
                    else
                    {
                        Logger.Warn($"Registry key '{AMD_PERFORMANCE_KEY_PATH}' not found.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred: {ex.Message}");
                return false;
            }
        }
    }
}

// =============================================================================
// LegionGoLibrary.cs
//
// A comprehensive library for controlling Lenovo Legion Go hardware features
// including WMI system controls and USB HID controller customization.
//
// Supports: Legion Go, Legion Go 2
// Author: LegionGoWMI Project
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{

    /// <summary>
    /// TDP (Thermal Design Power) preset modes for the Legion Go.
    /// Controls the power profile affecting performance and fan behavior.
    /// </summary>
    public enum TdpMode
    {
        /// <summary>Quiet mode - Low power, minimal fan noise (0x01)</summary>
        Quiet = 0x01,
        /// <summary>Balanced mode - Standard performance and thermals (0x02)</summary>
        Balanced = 0x02,
        /// <summary>Performance mode - Maximum performance, higher thermals (0x03)</summary>
        Performance = 0x03,
        /// <summary>Custom mode - User-defined power limits (0xFF)</summary>
        Custom = 0xFF
    }

    /// <summary>
    /// WMI Capability IDs for accessing various hardware features through LENOVO_OTHER_METHOD.
    /// These IDs are used with GetFeatureValue/SetFeatureValue methods.
    /// </summary>
    public enum CapabilityID : uint
    {
        // GPU Controls
        /// <summary>Integrated GPU mode setting</summary>
        IGPUMode = 0x00010000,
        /// <summary>Flip to start feature</summary>
        FlipToStart = 0x00030000,
        /// <summary>NVIDIA GPU dynamic display switching</summary>
        NvidiaGPUDynamicDisplaySwitching = 0x00040000,
        /// <summary>AMD SmartShift mode for power distribution between CPU/GPU</summary>
        AMDSmartShiftMode = 0x00050001,
        /// <summary>AMD skin temperature tracking</summary>
        AMDSkinTemperatureTracking = 0x00050002,
        /// <summary>Supported power modes bitmask</summary>
        SupportedPowerModes = 0x00070000,
        /// <summary>Legion Zone support version</summary>
        LegionZoneSupportVersion = 0x00090000,
        /// <summary>IGPU mode change status</summary>
        IGPUModeChangeStatus = 0x000F0000,

        // CPU Power Limits
        /// <summary>CPU Short-term Power Limit (SPL / Fast TDP) in watts</summary>
        CPUShortTermPowerLimit = 0x0101FF00,
        /// <summary>CPU Long-term Power Limit (SPPL / Slow TDP) in watts</summary>
        CPULongTermPowerLimit = 0x0102FF00,
        /// <summary>CPU Peak Power Limit (FPPT / Peak TDP) in watts</summary>
        CPUPeakPowerLimit = 0x0103FF00,
        /// <summary>CPU temperature limit in Celsius</summary>
        CPUTemperatureLimit = 0x0104FF00,
        /// <summary>APU sPPT power limit in watts</summary>
        APUsPPTPowerLimit = 0x0105FF00,
        /// <summary>CPU cross-loading power limit in watts</summary>
        CPUCrossLoadingPowerLimit = 0x0106FF00,
        /// <summary>CPU PL1 Tau (time constant) in seconds</summary>
        CPUPL1Tau = 0x0107FF00,

        // GPU Power Limits
        /// <summary>GPU power boost value in watts</summary>
        GPUPowerBoost = 0x0201FF00,
        /// <summary>GPU configurable TGP (Total Graphics Power)</summary>
        GPUConfigurableTGP = 0x0202FF00,
        /// <summary>GPU temperature limit in Celsius</summary>
        GPUTemperatureLimit = 0x0203FF00,
        /// <summary>GPU TGP offset from baseline on AC power</summary>
        GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = 0x0204FF00,
        /// <summary>GPU status flags</summary>
        GPUStatus = 0x02070000,
        /// <summary>GPU Device ID and Vendor ID</summary>
        GPUDidVid = 0x02090000,

        // Battery
        /// <summary>Battery charge limit to 80% (InstantBoot AC setting)</summary>
        InstantBootAc = 0x03010001,
        /// <summary>USB Power Delivery InstantBoot setting</summary>
        InstantBootUsbPowerDelivery = 0x03010002,

        // Fan Controls
        /// <summary>Fan full speed mode (0=off, 1=on)</summary>
        FanFullSpeed = 0x04020000,
        /// <summary>Current CPU fan speed in RPM</summary>
        CpuCurrentFanSpeed = 0x04030001,
        /// <summary>Current GPU fan speed in RPM</summary>
        GpuCurrentFanSpeed = 0x04030002,

        // Temperature Sensors
        /// <summary>Fan control sensor temperature (0x01 sensor) - what EC uses for fan curve lookup</summary>
        FanControlSensorTemp = 0x05010000,
        /// <summary>Current CPU temperature in Celsius</summary>
        CpuCurrentTemperature = 0x05040000,
        /// <summary>Current GPU temperature in Celsius</summary>
        GpuCurrentTemperature = 0x05050000
    }

    /// <summary>
    /// RGB lighting modes for the controller stick lights.
    /// </summary>
    public enum RgbMode : byte
    {
        /// <summary>Solid static color</summary>
        Solid = 1,
        /// <summary>Pulsing/breathing effect</summary>
        Pulse = 2,
        /// <summary>Dynamic color cycling</summary>
        Dynamic = 3,
        /// <summary>Spiral animation effect</summary>
        Spiral = 4
    }

    // NOTE: Controller and TouchpadVibrationLevel enums are defined in LegionGoController.cs

    /// <summary>
    /// Controller vibration/haptic feedback intensity levels.
    /// </summary>
    public enum ControllerVibrationLevel : byte
    {
        /// <summary>Vibration disabled</summary>
        Off = 0x00,
        /// <summary>Light vibration</summary>
        Weak = 0x01,
        /// <summary>Medium vibration intensity</summary>
        Medium = 0x02,
        /// <summary>Strong vibration intensity</summary>
        Strong = 0x03
    }

    /// <summary>
    /// Legion Go device variant type.
    /// </summary>
    public enum DeviceType
    {
        /// <summary>Unknown or unsupported device</summary>
        Unknown,
        /// <summary>Original Legion Go or Legion Go 2 (same command format)</summary>
        LegionGo,
        /// <summary>Legion Go S (different command format) - Not yet active</summary>
        LegionGoSlim
    }

}

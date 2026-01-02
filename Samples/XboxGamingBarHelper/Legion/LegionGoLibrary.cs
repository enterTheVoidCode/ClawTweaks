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

namespace LegionGoLibrary
{
    #region Enums

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

    /// <summary>
    /// Controller identifier for left or right detachable controller.
    /// </summary>
    public enum Controller : byte
    {
        /// <summary>Left controller (0x03)</summary>
        Left = 0x03,
        /// <summary>Right controller (0x04)</summary>
        Right = 0x04
    }

    /// <summary>
    /// Touchpad haptic vibration intensity levels.
    /// </summary>
    public enum TouchpadVibrationLevel : byte
    {
        /// <summary>Vibration disabled</summary>
        Off = 0x00,
        /// <summary>Light vibration</summary>
        Weak = 0x01,
        /// <summary>Medium vibration</summary>
        Medium = 0x02,
        /// <summary>Strong vibration</summary>
        Strong = 0x03
    }

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

    #endregion

    #region LenovoWMIService

    /// <summary>
    /// Service class for interacting with Lenovo WMI interfaces to control
    /// system-level features like TDP, fan control, temperatures, and lighting.
    /// Requires administrator privileges for most operations.
    /// </summary>
    public class LenovoWMIService
    {
        private const string WMI_NAMESPACE = @"root\WMI";

        /// <summary>
        /// Minimum allowed fan curve values (percentage) for custom fan tables.
        /// Array of 10 values corresponding to temperature thresholds.
        /// </summary>
        public static readonly int[] MinCurve = { 44, 48, 55, 60, 71, 79, 87, 87, 100, 100 };

        #region WMI Discovery

        /// <summary>
        /// Lists all Lenovo WMI classes available in the system.
        /// Useful for discovering available WMI interfaces.
        /// </summary>
        /// <returns>Tuple containing success status, message, and array of class names</returns>
        public (bool Success, string Message, string[]? Classes) ListWMIClasses()
        {
            try
            {
                var classes = new List<string>();
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM meta_class");
                foreach (ManagementClass wmiClass in searcher.Get())
                {
                    var className = wmiClass["__CLASS"]?.ToString();
                    if (className != null && className.IndexOf("LENOVO", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        classes.Add(className);
                    }
                }
                return (true, $"Found {classes.Count} Lenovo WMI classes", classes.ToArray());
            }
            catch (Exception ex)
            {
                return (false, $"Error listing WMI classes: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Gets all methods available on a specific WMI class with their parameter signatures.
        /// </summary>
        /// <param name="className">The WMI class name to inspect</param>
        /// <returns>Tuple containing success status, message, and array of method signatures</returns>
        public (bool Success, string Message, string[]? Methods) GetClassMethods(string className)
        {
            try
            {
                var methods = new List<string>();
                using var classObj = new ManagementClass(WMI_NAMESPACE, className, null);
                foreach (MethodData method in classObj.Methods)
                {
                    var inParams = "";
                    var outParams = "";

                    if (method.InParameters != null)
                    {
                        var props = method.InParameters.Properties.Cast<PropertyData>()
                            .Select(p => $"{p.Type} {p.Name}");
                        inParams = string.Join(", ", props);
                    }

                    if (method.OutParameters != null)
                    {
                        var props = method.OutParameters.Properties.Cast<PropertyData>()
                            .Select(p => $"{p.Type} {p.Name}");
                        outParams = string.Join(", ", props);
                    }

                    methods.Add($"{method.Name}({inParams}) -> ({outParams})");
                }
                return (true, $"Found {methods.Count} methods in {className}", methods.ToArray());
            }
            catch (Exception ex)
            {
                return (false, $"Error getting methods for {className}: {ex.Message}", null);
            }
        }

        #endregion

        #region LENOVO_GAMEZONE_DATA Methods

        /// <summary>
        /// Gets the current Smart Fan (TDP) mode from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and TdpMode value</returns>
        public (bool Success, string Message, TdpMode? Result) GetSmartFanMode()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetSmartFanMode", null, null);
                    if (outParams != null)
                    {
                        var mode = Convert.ToInt32(outParams["Data"]);
                        return mode switch
                        {
                            0x01 => (true, "Smart Fan Mode: Quiet (1)", TdpMode.Quiet),
                            0x02 => (true, "Smart Fan Mode: Balanced (2)", TdpMode.Balanced),
                            0x03 => (true, "Smart Fan Mode: Performance (3)", TdpMode.Performance),
                            0xFF => (true, "Smart Fan Mode: Custom (255)", TdpMode.Custom),
                            _ => (true, $"Smart Fan Mode: Unknown ({mode})", null)
                        };
                    }
                }
                return (false, "GetSmartFanMode returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetSmartFanMode: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Sets the Smart Fan (TDP) mode via LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <param name="mode">The TDP mode to set</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetSmartFanMode(TdpMode mode)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("SetSmartFanMode");
                    inParams["Data"] = (int)mode;
                    obj.InvokeMethod("SetSmartFanMode", inParams, null);
                    return (true, $"SetSmartFanMode executed with Data={(int)mode} ({mode})");
                }
                return (false, "No LENOVO_GAMEZONE_DATA instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error in SetSmartFanMode: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current CPU temperature from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and temperature in Celsius</returns>
        public (bool Success, string Message, int? Result) GetCPUTemp()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetCPUTemp", null, null);
                    if (outParams != null)
                    {
                        var temp = Convert.ToInt32(outParams["Data"]);
                        return (true, $"CPU Temp: {temp}°C", temp);
                    }
                }
                return (false, "GetCPUTemp returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetCPUTemp: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Gets the current GPU temperature from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and temperature in Celsius</returns>
        public (bool Success, string Message, int? Result) GetGPUTemp()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetGPUTemp", null, null);
                    if (outParams != null)
                    {
                        var temp = Convert.ToInt32(outParams["Data"]);
                        return (true, $"GPU Temp: {temp}°C", temp);
                    }
                }
                return (false, "GetGPUTemp returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetGPUTemp: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Gets the current fan cooling status from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and status value</returns>
        public (bool Success, string Message, int? Result) GetFanCoolingStatus()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetFanCoolingStatus", null, null);
                    if (outParams != null)
                    {
                        var status = Convert.ToInt32(outParams["Data"]);
                        return (true, $"Fan Cooling Status: {status}", status);
                    }
                }
                return (false, "GetFanCoolingStatus returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetFanCoolingStatus: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Sets the fan cooling value via LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <param name="value">The cooling value to set</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFanCooling(int value)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("SetFanCooling");
                    inParams["Data"] = value;
                    obj.InvokeMethod("SetFanCooling", inParams, null);
                    return (true, $"SetFanCooling executed with Data={value}");
                }
                return (false, "No LENOVO_GAMEZONE_DATA instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error in SetFanCooling: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current thermal mode from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and thermal mode value</returns>
        public (bool Success, string Message, int? Result) GetThermalMode()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetThermalMode", null, null);
                    if (outParams != null)
                    {
                        var mode = Convert.ToInt32(outParams["Data"]);
                        return (true, $"Thermal Mode: {mode}", mode);
                    }
                }
                return (false, "GetThermalMode returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetThermalMode: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Gets the Smart Fan setting from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and setting value</returns>
        public (bool Success, string Message, int? Result) GetSmartFanSetting()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetSmartFanSetting", null, null);
                    if (outParams != null)
                    {
                        var setting = Convert.ToInt32(outParams["Data"]);
                        return (true, $"Smart Fan Setting: {setting}", setting);
                    }
                }
                return (false, "GetSmartFanSetting returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetSmartFanSetting: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Gets the power charge mode from LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and mode value</returns>
        public (bool Success, string Message, int? Result) GetPowerChargeMode()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_GAMEZONE_DATA");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetPowerChargeMode", null, null);
                    if (outParams != null)
                    {
                        var mode = Convert.ToInt32(outParams["Data"]);
                        return (true, $"Power Charge Mode: {mode}", mode);
                    }
                }
                return (false, "GetPowerChargeMode returned no data", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetPowerChargeMode: {ex.Message}", null);
            }
        }

        #endregion

        #region LENOVO_OTHER_METHOD - Feature Get/Set

        /// <summary>
        /// Gets a feature value using a CapabilityID from LENOVO_OTHER_METHOD.
        /// </summary>
        /// <param name="capabilityId">The capability ID to query</param>
        /// <returns>Tuple containing success status, message, and value</returns>
        public (bool Success, string Message, int? Result) GetFeatureValue(CapabilityID capabilityId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_OTHER_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("GetFeatureValue");
                    inParams["IDs"] = (int)capabilityId;
                    var outParams = obj.InvokeMethod("GetFeatureValue", inParams, null);
                    if (outParams != null)
                    {
                        var value = Convert.ToInt32(outParams["Value"]);
                        return (true, $"{capabilityId} (0x{(uint)capabilityId:X8}) = {value}", value);
                    }
                }
                return (false, $"GetFeatureValue returned no data for {capabilityId}", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetFeatureValue({capabilityId}): {ex.Message}", null);
            }
        }

        /// <summary>
        /// Sets a feature value using a CapabilityID via LENOVO_OTHER_METHOD.
        /// </summary>
        /// <param name="capabilityId">The capability ID to set</param>
        /// <param name="value">The value to set</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFeatureValue(CapabilityID capabilityId, int value)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_OTHER_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("SetFeatureValue");
                    inParams["IDs"] = (int)capabilityId;
                    inParams["value"] = value;
                    obj.InvokeMethod("SetFeatureValue", inParams, null);
                    return (true, $"SetFeatureValue: {capabilityId} = {value}");
                }
                return (false, "No LENOVO_OTHER_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error in SetFeatureValue({capabilityId}, {value}): {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a feature value using a raw feature ID from LENOVO_OTHER_METHOD.
        /// Use this for custom/undocumented feature IDs.
        /// </summary>
        /// <param name="featureId">The raw feature ID (hex) to query</param>
        /// <returns>Tuple containing success status, message, and value</returns>
        public (bool Success, string Message, int? Result) GetFeatureValue(uint featureId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_OTHER_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("GetFeatureValue");
                    inParams["IDs"] = (int)featureId;
                    var outParams = obj.InvokeMethod("GetFeatureValue", inParams, null);
                    if (outParams != null)
                    {
                        var value = Convert.ToInt32(outParams["Value"]);
                        return (true, $"Feature 0x{featureId:X8} = {value}", value);
                    }
                }
                return (false, $"GetFeatureValue returned no data for 0x{featureId:X8}", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetFeatureValue(0x{featureId:X8}): {ex.Message}", null);
            }
        }

        /// <summary>
        /// Sets a feature value using a raw feature ID via LENOVO_OTHER_METHOD.
        /// Use this for custom/undocumented feature IDs.
        /// </summary>
        /// <param name="featureId">The raw feature ID (hex) to set</param>
        /// <param name="value">The value to set</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFeatureValue(uint featureId, int value)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_OTHER_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("SetFeatureValue");
                    inParams["IDs"] = (int)featureId;
                    inParams["value"] = value;
                    obj.InvokeMethod("SetFeatureValue", inParams, null);
                    return (true, $"SetFeatureValue: 0x{featureId:X8} = {value}");
                }
                return (false, "No LENOVO_OTHER_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error in SetFeatureValue(0x{featureId:X8}, {value}): {ex.Message}");
            }
        }

        #endregion

        #region LENOVO_FAN_METHOD

        /// <summary>
        /// Sets a custom fan curve table via LENOVO_FAN_METHOD.
        /// The fan table consists of 10 values (percentage 0-100) corresponding to temperature thresholds.
        /// </summary>
        /// <param name="fanSpeeds">Array of exactly 10 fan speed percentages</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFanTable(ushort[] fanSpeeds)
        {
            if (fanSpeeds.Length != 10)
                return (false, "Fan table must have exactly 10 values");

            try
            {
                // Build fan table bytes - matching HandheldCompanion FanTable format
                var fanTableBytes = new byte[40]; // 10 * 4 bytes
                for (int i = 0; i < 10; i++)
                {
                    var bytes = BitConverter.GetBytes(fanSpeeds[i]);
                    fanTableBytes[i * 4] = bytes[0];
                    fanTableBytes[i * 4 + 1] = bytes[1];
                    fanTableBytes[i * 4 + 2] = 0;
                    fanTableBytes[i * 4 + 3] = 0;
                }

                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_FAN_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("Fan_Set_Table");
                    inParams["FanTable"] = fanTableBytes;
                    obj.InvokeMethod("Fan_Set_Table", inParams, null);
                    return (true, $"Fan table set: [{string.Join(", ", fanSpeeds)}]");
                }
                return (false, "No LENOVO_FAN_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error in SetFanTable: {ex.Message}");
            }
        }

        #endregion

        #region TDP Convenience Methods

        /// <summary>
        /// Gets the CPU Short-term Power Limit (SPL / Fast TDP) in watts.
        /// </summary>
        /// <returns>Tuple containing success status, message, and power limit in watts</returns>
        public (bool Success, string Message, int? Result) GetCPUShortTermPowerLimit() => GetFeatureValue(CapabilityID.CPUShortTermPowerLimit);

        /// <summary>
        /// Gets the CPU Long-term Power Limit (SPPL / Slow TDP) in watts.
        /// </summary>
        /// <returns>Tuple containing success status, message, and power limit in watts</returns>
        public (bool Success, string Message, int? Result) GetCPULongTermPowerLimit() => GetFeatureValue(CapabilityID.CPULongTermPowerLimit);

        /// <summary>
        /// Gets the CPU Peak Power Limit (FPPT / Peak TDP) in watts.
        /// </summary>
        /// <returns>Tuple containing success status, message, and power limit in watts</returns>
        public (bool Success, string Message, int? Result) GetCPUPeakPowerLimit() => GetFeatureValue(CapabilityID.CPUPeakPowerLimit);

        /// <summary>
        /// Sets the CPU Short-term Power Limit (SPL / Fast TDP).
        /// </summary>
        /// <param name="watts">Power limit in watts</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetCPUShortTermPowerLimit(int watts) => SetFeatureValue(CapabilityID.CPUShortTermPowerLimit, watts);

        /// <summary>
        /// Sets the CPU Long-term Power Limit (SPPL / Slow TDP).
        /// </summary>
        /// <param name="watts">Power limit in watts</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetCPULongTermPowerLimit(int watts) => SetFeatureValue(CapabilityID.CPULongTermPowerLimit, watts);

        /// <summary>
        /// Sets the CPU Peak Power Limit (FPPT / Peak TDP).
        /// </summary>
        /// <param name="watts">Power limit in watts</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetCPUPeakPowerLimit(int watts) => SetFeatureValue(CapabilityID.CPUPeakPowerLimit, watts);

        #endregion

        #region Fan Convenience Methods

        /// <summary>
        /// Gets whether fan full speed mode is enabled.
        /// </summary>
        /// <returns>Tuple containing success status, message, and status (1=enabled, 0=disabled)</returns>
        public (bool Success, string Message, int? Result) GetFanFullSpeed()
        {
            var result = GetFeatureValue(CapabilityID.FanFullSpeed);
            if (result.Success)
                return (true, $"Fan Full Speed: {(result.Result == 1 ? "Enabled" : "Disabled")}", result.Result);
            return result;
        }

        /// <summary>
        /// Enables or disables fan full speed mode.
        /// </summary>
        /// <param name="enabled">True to enable full speed, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFanFullSpeed(bool enabled) => SetFeatureValue(CapabilityID.FanFullSpeed, enabled ? 1 : 0);

        /// <summary>
        /// Gets the current CPU fan speed in RPM.
        /// </summary>
        /// <returns>Tuple containing success status, message, and RPM value</returns>
        public (bool Success, string Message, int? Result) GetCpuFanSpeed() => GetFeatureValue(CapabilityID.CpuCurrentFanSpeed);

        /// <summary>
        /// Gets the current GPU fan speed in RPM.
        /// </summary>
        /// <returns>Tuple containing success status, message, and RPM value</returns>
        public (bool Success, string Message, int? Result) GetGpuFanSpeed() => GetFeatureValue(CapabilityID.GpuCurrentFanSpeed);

        #endregion

        #region Battery Convenience Methods

        /// <summary>
        /// Gets whether the battery charge limit (80%) is enabled.
        /// </summary>
        /// <returns>Tuple containing success status, message, and status (1=enabled, 0=disabled)</returns>
        public (bool Success, string Message, int? Result) GetBatteryChargeLimit()
        {
            var result = GetFeatureValue(CapabilityID.InstantBootAc);
            if (result.Success)
                return (true, $"Battery Charge Limit (80%): {(result.Result == 1 ? "Enabled" : "Disabled")}", result.Result);
            return result;
        }

        /// <summary>
        /// Enables or disables the battery charge limit (80%).
        /// When enabled, the battery will only charge to 80% to extend battery lifespan.
        /// </summary>
        /// <param name="enabled">True to enable 80% limit, false to allow full charge</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetBatteryChargeLimit(bool enabled) => SetFeatureValue(CapabilityID.InstantBootAc, enabled ? 1 : 0);

        #endregion

        #region Temperature Convenience Methods

        /// <summary>
        /// Gets the current CPU temperature from LENOVO_OTHER_METHOD.
        /// Alternative to GetCPUTemp() which uses LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and temperature in Celsius</returns>
        public (bool Success, string Message, int? Result) GetCpuCurrentTemperature() => GetFeatureValue(CapabilityID.CpuCurrentTemperature);

        /// <summary>
        /// Gets the current GPU temperature from LENOVO_OTHER_METHOD.
        /// Alternative to GetGPUTemp() which uses LENOVO_GAMEZONE_DATA.
        /// </summary>
        /// <returns>Tuple containing success status, message, and temperature in Celsius</returns>
        public (bool Success, string Message, int? Result) GetGpuCurrentTemperature() => GetFeatureValue(CapabilityID.GpuCurrentTemperature);

        #endregion

        #region Lighting Control (LENOVO_LIGHTING_METHOD)

        /// <summary>
        /// Gets the panel logo light status.
        /// </summary>
        /// <returns>Tuple containing success status, message, and status (1=on, 0=off)</returns>
        public (bool Success, string Message, int? Result) GetPanelLogoLight()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_LIGHTING_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("Get_Lighting_Current_Status");
                    inParams["Lighting_ID"] = 0; // Panel logo = 0
                    var result = obj.InvokeMethod("Get_Lighting_Current_Status", inParams, null);
                    var status = Convert.ToInt32(result["Current_State_Type"]);
                    return (true, $"Panel Logo Light: {(status == 1 ? "On" : "Off")}", status);
                }
                return (false, "No LENOVO_LIGHTING_METHOD instance found", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error getting panel logo light: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Enables or disables the panel logo light.
        /// </summary>
        /// <param name="enabled">True to turn on, false to turn off</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetPanelLogoLight(bool enabled)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_LIGHTING_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("Set_Lighting_Current_Status");
                    inParams["Lighting_ID"] = 0; // Panel logo = 0
                    inParams["Current_State_Type"] = enabled ? 1 : 0;
                    inParams["Current_Brightness_Level"] = 1;
                    obj.InvokeMethod("Set_Lighting_Current_Status", inParams, null);
                    return (true, $"Panel Logo Light: {(enabled ? "On" : "Off")}");
                }
                return (false, "No LENOVO_LIGHTING_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error setting panel logo light: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the IO port light status.
        /// </summary>
        /// <returns>Tuple containing success status, message, and status (1=on, 0=off)</returns>
        public (bool Success, string Message, int? Result) GetIOPortLight()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_LIGHTING_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("Get_Lighting_Current_Status");
                    inParams["Lighting_ID"] = 1; // IO Port = 1
                    var result = obj.InvokeMethod("Get_Lighting_Current_Status", inParams, null);
                    var status = Convert.ToInt32(result["Current_State_Type"]);
                    return (true, $"IO Port Light: {(status == 1 ? "On" : "Off")}", status);
                }
                return (false, "No LENOVO_LIGHTING_METHOD instance found", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error getting IO port light: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Enables or disables the IO port light.
        /// </summary>
        /// <param name="enabled">True to turn on, false to turn off</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetIOPortLight(bool enabled)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_LIGHTING_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("Set_Lighting_Current_Status");
                    inParams["Lighting_ID"] = 1; // IO Port = 1
                    inParams["Current_State_Type"] = enabled ? 1 : 0;
                    inParams["Current_Brightness_Level"] = 1;
                    obj.InvokeMethod("Set_Lighting_Current_Status", inParams, null);
                    return (true, $"IO Port Light: {(enabled ? "On" : "Off")}");
                }
                return (false, "No LENOVO_LIGHTING_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error setting IO port light: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the power button LED status using v2 (new) format.
        /// Uses Lighting_ID = 0x04 for newer BIOS.
        /// Returns: 0x02 = on, 0x01 = off
        /// </summary>
        /// <returns>Tuple containing success status, message, and enabled state</returns>
        public (bool Success, string Message, bool? Result) GetPowerLight()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_LIGHTING_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var inParams = obj.GetMethodParameters("Get_Lighting_Current_Status");
                    inParams["Lighting_ID"] = (byte)0x04;
                    var result = obj.InvokeMethod("Get_Lighting_Current_Status", inParams, null);

                    // Get the brightness level which contains the state
                    var brightness = Convert.ToInt32(result["Current_Brightness_Level"]);
                    // 0x02 = on, 0x01 = off
                    bool isOn = brightness == 0x02;

                    return (true, $"Power Light: {(isOn ? "On" : "Off")} (raw=0x{brightness:X2})", isOn);
                }
                return (false, "No LENOVO_LIGHTING_METHOD instance found", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error getting power light: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Sets the power button LED using v2 (new) format.
        /// Uses Lighting_ID = 0x04 for newer BIOS.
        /// Values for Current_Brightness_Level: 0x01 = off, 0x02 = on
        /// </summary>
        /// <param name="enabled">True to turn on, false to turn off</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetPowerLight(bool enabled)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_LIGHTING_METHOD");
                foreach (ManagementObject obj in searcher.Get())
                {
                    byte brightness = enabled ? (byte)0x02 : (byte)0x01;

                    var inParams = obj.GetMethodParameters("Set_Lighting_Current_Status");
                    inParams["Lighting_ID"] = (byte)0x04;
                    inParams["Current_State_Type"] = (byte)0x00;
                    inParams["Current_Brightness_Level"] = brightness;
                    obj.InvokeMethod("Set_Lighting_Current_Status", inParams, null);

                    return (true, $"Power Light: {(enabled ? "On" : "Off")}");
                }
                return (false, "No LENOVO_LIGHTING_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error setting power light: {ex.Message}");
            }
        }

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Gets all available system information in a single call.
        /// Queries multiple WMI methods and returns aggregated results.
        /// </summary>
        /// <returns>Tuple containing success status, message, and dictionary of key-value pairs</returns>
        public (bool Success, string Message, Dictionary<string, object>? Result) GetAllInfo()
        {
            var info = new Dictionary<string, object>();

            // Smart Fan Mode
            var smartFanMode = GetSmartFanMode();
            if (smartFanMode.Success && smartFanMode.Result.HasValue)
                info["SmartFanMode"] = smartFanMode.Result.Value.ToString();

            // Temperatures from GAMEZONE_DATA
            var cpuTemp = GetCPUTemp();
            if (cpuTemp.Success && cpuTemp.Result.HasValue)
                info["CPU Temp (GameZone)"] = $"{cpuTemp.Result.Value}°C";

            var gpuTemp = GetGPUTemp();
            if (gpuTemp.Success && gpuTemp.Result.HasValue)
                info["GPU Temp (GameZone)"] = $"{gpuTemp.Result.Value}°C";

            // Thermal Mode
            var thermalMode = GetThermalMode();
            if (thermalMode.Success && thermalMode.Result.HasValue)
                info["ThermalMode"] = thermalMode.Result.Value;

            // Power Charge Mode
            var powerCharge = GetPowerChargeMode();
            if (powerCharge.Success && powerCharge.Result.HasValue)
                info["PowerChargeMode"] = powerCharge.Result.Value;

            // Fan Cooling Status
            var fanCooling = GetFanCoolingStatus();
            if (fanCooling.Success && fanCooling.Result.HasValue)
                info["FanCoolingStatus"] = fanCooling.Result.Value;

            // TDP Values
            var splTdp = GetCPUShortTermPowerLimit();
            if (splTdp.Success && splTdp.Result.HasValue)
                info["CPU SPL (Short)"] = $"{splTdp.Result.Value}W";

            var spplTdp = GetCPULongTermPowerLimit();
            if (spplTdp.Success && spplTdp.Result.HasValue)
                info["CPU SPPL (Long)"] = $"{spplTdp.Result.Value}W";

            var fpptTdp = GetCPUPeakPowerLimit();
            if (fpptTdp.Success && fpptTdp.Result.HasValue)
                info["CPU FPPT (Peak)"] = $"{fpptTdp.Result.Value}W";

            // Fan Full Speed
            var fanFull = GetFanFullSpeed();
            if (fanFull.Success && fanFull.Result.HasValue)
                info["FanFullSpeed"] = fanFull.Result.Value == 1 ? "Enabled" : "Disabled";

            // Battery Charge Limit
            var chargeLimit = GetBatteryChargeLimit();
            if (chargeLimit.Success && chargeLimit.Result.HasValue)
                info["BatteryChargeLimit"] = chargeLimit.Result.Value == 1 ? "Enabled" : "Disabled";

            // Fan Speeds
            var cpuFan = GetCpuFanSpeed();
            if (cpuFan.Success && cpuFan.Result.HasValue)
                info["CPU Fan Speed"] = $"{cpuFan.Result.Value} RPM";

            var gpuFan = GetGpuFanSpeed();
            if (gpuFan.Success && gpuFan.Result.HasValue)
                info["GPU Fan Speed"] = $"{gpuFan.Result.Value} RPM";

            return (info.Count > 0, info.Count > 0 ? "Info retrieved" : "No info available", info);
        }

        #endregion
    }

    #endregion

    #region LegionControllerService

    /// <summary>
    /// Service class for controlling Legion Go detachable controllers via USB HID.
    /// Provides RGB lighting, touchpad, gyro, and configuration controls.
    /// Supports Legion Go and Legion Go 2 (same command format).
    /// </summary>
    public class LegionControllerService : IDisposable
    {
        // Lenovo Legion Go USB identifiers
        private const int VENDOR_ID = 0x17EF;

        // Original Legion Go PIDs
        private static readonly int[] ORIGINAL_PIDS = { 0x6182, 0x6183, 0x6184, 0x6185 };

        // Legion Go 2 / 2025 Firmware PIDs (uses same commands as original)
        private static readonly int[] GO2_PIDS = { 0x61EB, 0x61EC, 0x61ED, 0x61EE };

        // All supported PIDs
        private static readonly int[] ALL_PRODUCT_IDS = ORIGINAL_PIDS.Concat(GO2_PIDS).ToArray();

        // HID Usage for Legion Go controller
        private const ushort USAGE_PAGE = 0xFFA0;
        private const ushort USAGE = 0x0001;

        private IntPtr _deviceHandle = IntPtr.Zero;
        private bool _isConnected = false;
        private DeviceType _deviceType = DeviceType.Unknown;
        private int _connectedPid = 0;
        private string _devicePath = null;  // Stored for opening separate read handle

        // Battery monitoring
        private IntPtr _batteryReadHandle = IntPtr.Zero;  // Separate handle for reading input reports
        private Thread _batteryMonitorThread;
        private volatile bool _monitoringBattery;
        private int _leftControllerBattery = -1;
        private int _rightControllerBattery = -1;
        private bool _leftControllerCharging = false;
        private bool _rightControllerCharging = false;

        /// <summary>
        /// Event raised when controller battery status is updated.
        /// </summary>
        public event EventHandler<ControllerServiceBatteryEventArgs> BatteryUpdated;

        /// <summary>
        /// Gets the left controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int LeftControllerBattery => _leftControllerBattery;

        /// <summary>
        /// Gets the right controller battery percentage (1-100), or -1 if unavailable.
        /// </summary>
        public int RightControllerBattery => _rightControllerBattery;

        /// <summary>
        /// Gets whether the left controller is charging.
        /// </summary>
        public bool LeftControllerCharging => _leftControllerCharging;

        /// <summary>
        /// Gets whether the right controller is charging.
        /// </summary>
        public bool RightControllerCharging => _rightControllerCharging;

        #region Native HID API

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] lpReportBuffer, uint reportBufferLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        #endregion

        /// <summary>
        /// Gets whether the controller is currently connected.
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Gets the detected device type (LegionGo, LegionGoSlim, or Unknown).
        /// </summary>
        public DeviceType DetectedDeviceType => _deviceType;

        /// <summary>
        /// Gets the connected device's Product ID.
        /// </summary>
        public int ConnectedProductId => _connectedPid;

        #region Connection Management

        /// <summary>
        /// Lists all Lenovo HID devices found on the system.
        /// Useful for debugging connection issues.
        /// </summary>
        /// <returns>Tuple containing success status, message, and list of device descriptions</returns>
        public (bool Success, string Message, List<string> Devices) ListLenovoHidDevices()
        {
            var devices = new List<string>();
            try
            {
                HidD_GetHidGuid(out Guid hidGuid);

                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (deviceInfoSet == INVALID_HANDLE_VALUE)
                    return (false, "Failed to get device info set", devices);

                try
                {
                    SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                    uint memberIndex = 0;
                    while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex++, ref deviceInterfaceData))
                    {
                        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                        IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4) ?? "";

                                // Check if path contains our VID
                                if (!devicePath.ToLower().Contains("vid_17ef"))
                                    continue;

                                string deviceInfo = "";

                                // Try multiple access modes
                                IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                string accessMode = "RW";

                                if (handle == INVALID_HANDLE_VALUE)
                                {
                                    handle = CreateFile(devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                    accessMode = "R";
                                }

                                if (handle == INVALID_HANDLE_VALUE)
                                {
                                    handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                    accessMode = "Q";
                                }

                                if (handle != INVALID_HANDLE_VALUE)
                                {
                                    HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                                    attributes.Size = Marshal.SizeOf(attributes);

                                    if (HidD_GetAttributes(handle, ref attributes))
                                    {
                                        deviceInfo = $"VID:0x{attributes.VendorID:X4} PID:0x{attributes.ProductID:X4} [{accessMode}]";

                                        if (HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                                        {
                                            try
                                            {
                                                int status = HidP_GetCaps(preparsedData, out HIDP_CAPS caps);
                                                if (status == 0) // HIDP_STATUS_SUCCESS
                                                {
                                                    deviceInfo += $" UP:0x{caps.UsagePage:X4} U:0x{caps.Usage:X4}";
                                                    deviceInfo += $" Out:{caps.OutputReportByteLength}";

                                                    if (caps.UsagePage == USAGE_PAGE && caps.Usage == USAGE)
                                                    {
                                                        deviceInfo += " [MATCH]";
                                                    }
                                                }
                                                else
                                                {
                                                    deviceInfo += $" (caps:0x{status:X8})";
                                                }
                                            }
                                            finally
                                            {
                                                HidD_FreePreparsedData(preparsedData);
                                            }
                                        }
                                        else
                                        {
                                            int err = Marshal.GetLastWin32Error();
                                            deviceInfo += $" (preparsed err:{err})";
                                        }
                                    }
                                    else
                                    {
                                        deviceInfo = "Path contains VID_17EF but GetAttributes failed";
                                    }

                                    CloseHandle(handle);
                                }
                                else
                                {
                                    int err = Marshal.GetLastWin32Error();
                                    deviceInfo = $"VID_17EF device (open err:{err})";
                                }

                                if (!string.IsNullOrEmpty(deviceInfo))
                                    devices.Add(deviceInfo);
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataBuffer);
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }

                return (true, $"Found {devices.Count} Lenovo HID device(s)", devices);
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}", devices);
            }
        }

        /// <summary>
        /// Connects to the Legion Go controller.
        /// Automatically finds the correct HID interface with write access.
        /// </summary>
        /// <returns>Tuple containing success status and connection message</returns>
        public (bool Success, string Message) Connect()
        {
            try
            {
                HidD_GetHidGuid(out Guid hidGuid);

                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (deviceInfoSet == INVALID_HANDLE_VALUE)
                    return (false, "Failed to get device info set");

                IntPtr bestHandle = IntPtr.Zero;
                string bestInfo = "";
                int bestScore = 0;
                string bestPath = null;

                try
                {
                    SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                    uint memberIndex = 0;
                    while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex++, ref deviceInterfaceData))
                    {
                        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                        IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4) ?? "";

                                // Quick filter by path
                                if (!devicePath.ToLower().Contains("vid_17ef"))
                                    continue;

                                // Try to open with write access
                                IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                                if (handle != INVALID_HANDLE_VALUE)
                                {
                                    HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                                    attributes.Size = Marshal.SizeOf(attributes);

                                    if (HidD_GetAttributes(handle, ref attributes))
                                    {
                                        if (attributes.VendorID == VENDOR_ID && ALL_PRODUCT_IDS.Contains(attributes.ProductID))
                                        {
                                            int score = 1; // Base score for matching VID/PID with RW access
                                            string info = $"PID: 0x{attributes.ProductID:X4}";

                                            if (HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                                            {
                                                try
                                                {
                                                    if (HidP_GetCaps(preparsedData, out HIDP_CAPS caps) == 0)
                                                    {
                                                        info += $", UP: 0x{caps.UsagePage:X4}, U: 0x{caps.Usage:X4}, Out: {caps.OutputReportByteLength}";

                                                        // Exact match - highest priority
                                                        if (caps.UsagePage == USAGE_PAGE && caps.Usage == USAGE)
                                                        {
                                                            score = 100;
                                                        }
                                                        // Good output report size
                                                        else if (caps.OutputReportByteLength >= 64)
                                                        {
                                                            score = 10;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // HidP_GetCaps failed - test if WriteFile works on this interface
                                                        // This is needed for Legion Go 2 which has multiple interfaces with same VID/PID
                                                        byte[] testBuffer = new byte[64];
                                                        testBuffer[0] = 0x05; // Report ID for Legion Go
                                                        if (WriteFile(handle, testBuffer, (uint)testBuffer.Length, out uint written, IntPtr.Zero) && written > 0)
                                                        {
                                                            score = 50; // WriteFile works - good candidate
                                                            info += " (WriteFile OK)";
                                                        }
                                                        else
                                                        {
                                                            score = 2; // WriteFile failed - skip this one
                                                            info += " (caps failed, WriteFile failed)";
                                                        }
                                                    }
                                                }
                                                finally
                                                {
                                                    HidD_FreePreparsedData(preparsedData);
                                                }
                                            }
                                            else
                                            {
                                                // Can't get preparsed data - test if WriteFile works
                                                byte[] testBuffer = new byte[64];
                                                testBuffer[0] = 0x05; // Report ID for Legion Go
                                                if (WriteFile(handle, testBuffer, (uint)testBuffer.Length, out uint written, IntPtr.Zero) && written > 0)
                                                {
                                                    score = 50; // WriteFile works - good candidate
                                                    info += " (no caps, WriteFile OK)";
                                                }
                                                else
                                                {
                                                    score = 2; // WriteFile failed - skip this one
                                                    info += " (no caps, WriteFile failed)";
                                                }
                                            }

                                            if (score > bestScore)
                                            {
                                                if (bestHandle != IntPtr.Zero)
                                                    CloseHandle(bestHandle);
                                                bestHandle = handle;
                                                bestPath = devicePath;
                                                bestInfo = info;
                                                bestScore = score;
                                                _connectedPid = attributes.ProductID;
                                                handle = IntPtr.Zero;
                                            }
                                        }
                                    }

                                    if (handle != IntPtr.Zero)
                                        CloseHandle(handle);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataBuffer);
                        }
                    }

                    if (bestHandle != IntPtr.Zero)
                    {
                        _deviceHandle = bestHandle;
                        _devicePath = bestPath;
                        _isConnected = true;

                        // Determine device type based on PID
                        // Legion Go 2 uses same commands as original Legion Go
                        if (ORIGINAL_PIDS.Contains(_connectedPid) || GO2_PIDS.Contains(_connectedPid))
                            _deviceType = DeviceType.LegionGo;
                        else
                            _deviceType = DeviceType.Unknown;

                        string mode = bestScore >= 100 ? "exact match" : bestScore >= 50 ? "WriteFile test" : bestScore >= 10 ? "by output size" : "basic";
                        string deviceName = GO2_PIDS.Contains(_connectedPid) ? "Legion Go 2" : "Legion Go";
                        return (true, $"Connected to {deviceName} ({mode}: {bestInfo})");
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }

                return (false, "Legion Go controller not found. Make sure controllers are attached and no other app is using them.");
            }
            catch (Exception ex)
            {
                return (false, $"Error connecting to controller: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnects from the controller and releases the HID handles.
        /// </summary>
        public void Disconnect()
        {
            // Stop battery monitoring first (this also closes the battery read handle)
            StopBatteryMonitoring();

            if (_deviceHandle != IntPtr.Zero && _deviceHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
            _devicePath = null;
            _isConnected = false;
        }

        private (bool Success, string Message) SendCommand(byte[] command)
        {
            if (!_isConnected || _deviceHandle == IntPtr.Zero)
                return (false, "Controller not connected");

            try
            {
                // Pad command to 64 bytes (HID report size)
                byte[] buffer = new byte[64];
                Array.Copy(command, buffer, Math.Min(command.Length, 64));

                // Try HidD_SetOutputReport first (standard method)
                if (HidD_SetOutputReport(_deviceHandle, buffer, (uint)buffer.Length))
                {
                    return (true, "Command sent successfully");
                }

                // Fallback to WriteFile if HidD_SetOutputReport fails
                // This is needed for Legion Go 2 where some interfaces don't support SetOutputReport
                if (WriteFile(_deviceHandle, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero) && written > 0)
                {
                    return (true, "Command sent successfully (WriteFile)");
                }

                int error = Marshal.GetLastWin32Error();
                return (false, $"Failed to send command (Error: {error})");
            }
            catch (Exception ex)
            {
                return (false, $"Error sending command: {ex.Message}");
            }
        }

        #endregion

        #region RGB/Stick Light Control

        /// <summary>
        /// Enables or disables RGB lighting on a controller.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="enabled">True to enable RGB, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetRgbEnabled(Controller controller, bool enabled)
        {
            // Legion Go / Legion Go 2: 05 06 70 02 [ctrl] [0/1] 01
            byte[] command = {
                0x05, 0x06, 0x70, 0x02,
                (byte)controller,
                (byte)(enabled ? 0x01 : 0x00),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"RGB {(enabled ? "enabled" : "disabled")} for {controller} controller"
                : result.Message);
        }

        /// <summary>
        /// Sets the RGB profile with color, mode, brightness, and speed settings.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="mode">RGB animation mode</param>
        /// <param name="red">Red color component (0-255)</param>
        /// <param name="green">Green color component (0-255)</param>
        /// <param name="blue">Blue color component (0-255)</param>
        /// <param name="brightness">Brightness level (0.0 to 1.0)</param>
        /// <param name="speed">Animation speed (0.0 to 1.0, where 1.0 is fastest)</param>
        /// <param name="profile">Profile slot to save to (default 0x03)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetRgbProfile(
            Controller controller,
            RgbMode mode,
            byte red, byte green, byte blue,
            float brightness = 1.0f,
            float speed = 0.5f,
            byte profile = 0x03)
        {
            // Legion Go / Legion Go 2
            byte r_brightness = (byte)Math.Max(0, Math.Min(63, (int)(64 * brightness)));
            byte r_speed = (byte)Math.Max(0, Math.Min(63, (int)(64 * (1 - speed))));

            byte[] command = {
                0x05, 0x0C, 0x72, 0x01,
                (byte)controller,
                (byte)mode,
                red, green, blue,
                r_brightness,
                r_speed,
                profile,
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"RGB profile set: {mode}, Color: RGB({red},{green},{blue})"
                : result.Message);
        }

        /// <summary>
        /// Loads (applies) a previously saved RGB profile.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="profile">Profile slot to load (default 0x03)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) LoadRgbProfile(Controller controller, byte profile = 0x03)
        {
            // Legion Go / Legion Go 2
            byte[] command = {
                0x05, 0x06, 0x73, 0x02,
                (byte)controller,
                profile,
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"RGB profile {profile} loaded for {controller} controller"
                : result.Message);
        }

        /// <summary>
        /// Sets the stick light mode for both controllers at once.
        /// Convenience method that applies settings to both left and right controllers.
        /// </summary>
        /// <param name="mode">RGB animation mode</param>
        /// <param name="red">Red color component (0-255)</param>
        /// <param name="green">Green color component (0-255)</param>
        /// <param name="blue">Blue color component (0-255)</param>
        /// <param name="brightness">Brightness level (0.0 to 1.0)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetStickLightMode(RgbMode mode, byte red, byte green, byte blue, float brightness = 1.0f, float speed = 0.5f)
        {
            // Legion Go / Legion Go 2 - set for both controllers
            var leftResult = SetRgbProfile(Controller.Left, mode, red, green, blue, brightness, speed);
            var rightResult = SetRgbProfile(Controller.Right, mode, red, green, blue, brightness, speed);

            LoadRgbProfile(Controller.Left);
            LoadRgbProfile(Controller.Right);

            if (leftResult.Success && rightResult.Success)
                return (true, $"Stick light mode set to {mode} with color RGB({red},{green},{blue})");

            return (false, $"Left: {leftResult.Message}, Right: {rightResult.Message}");
        }

        #endregion

        #region Touchpad Control

        /// <summary>
        /// Enables or disables the right controller touchpad.
        /// </summary>
        /// <param name="enabled">True to enable touchpad, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetTouchpadEnabled(bool enabled)
        {
            // Legion Go / Legion Go 2: 05 06 6B 02 04 [0/1] 01
            byte[] command = {
                0x05, 0x06, 0x6B, 0x02,
                0x04,  // Right controller
                (byte)(enabled ? 0x01 : 0x00),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"Touchpad {(enabled ? "enabled" : "disabled")}"
                : result.Message);
        }

        /// <summary>
        /// Sets the touchpad haptic vibration intensity.
        /// </summary>
        /// <param name="level">Vibration intensity level</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetTouchpadVibration(TouchpadVibrationLevel level)
        {
            // Legion Go / Legion Go 2: 05 06 6C 02 04 [level] 01
            byte[] command = {
                0x05, 0x06, 0x6C, 0x02,
                0x04,  // Right controller
                (byte)level,
                0x01
            };

            var result = SendCommand(command);
            string levelName = level switch
            {
                TouchpadVibrationLevel.Off => "Off",
                TouchpadVibrationLevel.Weak => "Weak",
                TouchpadVibrationLevel.Medium => "Medium",
                TouchpadVibrationLevel.Strong => "Strong",
                _ => "Unknown"
            };

            return (result.Success, result.Success
                ? $"Touchpad vibration set to {levelName}"
                : result.Message);
        }

        #endregion

        #region Controller Vibration

        /// <summary>
        /// Sets the vibration/haptic feedback intensity for a controller.
        /// This controls the overall motor vibration strength.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="level">Vibration intensity level</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetControllerVibration(Controller controller, ControllerVibrationLevel level)
        {
            // Command: 05 06 67 02 [controller] [level] 01
            byte[] command = {
                0x05, 0x06, 0x67, 0x02,
                (byte)controller,
                (byte)level,
                0x01
            };

            var result = SendCommand(command);
            string levelName = level switch
            {
                ControllerVibrationLevel.Off => "Off",
                ControllerVibrationLevel.Weak => "Weak",
                ControllerVibrationLevel.Medium => "Medium",
                ControllerVibrationLevel.Strong => "Strong",
                _ => "Unknown"
            };

            return (result.Success, result.Success
                ? $"Vibration set to {levelName} for {controller} controller"
                : result.Message);
        }

        /// <summary>
        /// Sets vibration intensity for both controllers at once.
        /// </summary>
        /// <param name="level">Vibration intensity level</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetBothControllersVibration(ControllerVibrationLevel level)
        {
            var leftResult = SetControllerVibration(Controller.Left, level);
            var rightResult = SetControllerVibration(Controller.Right, level);

            if (leftResult.Success && rightResult.Success)
            {
                string levelName = level switch
                {
                    ControllerVibrationLevel.Off => "Off",
                    ControllerVibrationLevel.Weak => "Weak",
                    ControllerVibrationLevel.Medium => "Medium",
                    ControllerVibrationLevel.Strong => "Strong",
                    _ => "Unknown"
                };
                return (true, $"Vibration set to {levelName} for both controllers");
            }

            return (false, $"Left: {leftResult.Message}, Right: {rightResult.Message}");
        }

        #endregion

        #region Gyro Control

        /// <summary>
        /// Enables or disables the gyroscope on a controller.
        /// When enabled, also sets high-quality gyro mode.
        /// </summary>
        /// <param name="controller">Which controller (Left or Right)</param>
        /// <param name="enabled">True to enable gyro, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetGyroEnabled(Controller controller, bool enabled)
        {
            // Legion Go / Legion Go 2
            if (enabled)
            {
                byte[] enableCmd = { 0x05, 0x06, 0x6A, 0x02, (byte)controller, 0x01, 0x01 };
                var result1 = SendCommand(enableCmd);

                byte[] hqCmd = { 0x05, 0x06, 0x6A, 0x07, (byte)controller, 0x02, 0x01 };
                var result2 = SendCommand(hqCmd);

                return (result1.Success && result2.Success,
                    $"Gyro enabled for {controller} controller");
            }
            else
            {
                byte[] disableCmd = { 0x05, 0x06, 0x6A, 0x07, (byte)controller, 0x01, 0x01 };
                var result = SendCommand(disableCmd);

                return (result.Success, $"Gyro disabled for {controller} controller");
            }
        }

        #endregion

        #region Controller Configuration

        /// <summary>
        /// Swaps the left and right controller button mappings.
        /// Useful for left-handed users.
        /// </summary>
        /// <param name="swapped">True to swap controllers, false for normal layout</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetControllerSwap(bool swapped)
        {
            // Same format for both device types
            byte[] command = {
                0x05, 0x06, 0x69, 0x04,
                0x01,
                (byte)(swapped ? 0x02 : 0x01),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"Controller swap {(swapped ? "enabled" : "disabled")}"
                : result.Message);
        }

        /// <summary>
        /// Enables or disables the FPS button remapper mode.
        /// When enabled, provides optimized button layout for FPS games.
        /// </summary>
        /// <param name="enabled">True to enable FPS remapper, false to disable</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFpsRemapper(bool enabled)
        {
            // FPS remapper: 05 06 69 05 01 [01=on, 02=off] 01
            byte[] command = {
                0x05, 0x06, 0x69, 0x05,
                0x01,
                (byte)(enabled ? 0x01 : 0x02),
                0x01
            };

            var result = SendCommand(command);
            return (result.Success, result.Success
                ? $"FPS remapper {(enabled ? "enabled" : "disabled")}"
                : result.Message);
        }

        #endregion

        #region Battery Monitoring

        /// <summary>
        /// Starts monitoring battery status from controller input reports.
        /// Battery reports are pushed by the controllers continuously.
        /// Report format: 04 00 a1 [leftBat] [leftStatus] [rightBat] [rightStatus]
        /// Uses a separate device handle to avoid blocking the main command handle.
        /// </summary>
        public void StartBatteryMonitoring()
        {
            if (_monitoringBattery)
                return;

            if (string.IsNullOrEmpty(_devicePath))
                return;

            // Open a separate handle for reading input reports
            // This avoids blocking the main handle used for sending commands
            _batteryReadHandle = CreateFile(_devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (_batteryReadHandle == INVALID_HANDLE_VALUE)
            {
                _batteryReadHandle = IntPtr.Zero;
                return;
            }

            _monitoringBattery = true;
            _batteryMonitorThread = new Thread(ReadBatteryReports)
            {
                IsBackground = true,
                Name = "LegionControllerService-BatteryMonitor"
            };
            _batteryMonitorThread.Start();
        }

        /// <summary>
        /// Stops the battery monitoring thread and closes the read handle.
        /// </summary>
        public void StopBatteryMonitoring()
        {
            _monitoringBattery = false;

            // Close the battery read handle to unblock any pending ReadFile call
            if (_batteryReadHandle != IntPtr.Zero && _batteryReadHandle != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_batteryReadHandle);
                _batteryReadHandle = IntPtr.Zero;
            }

            if (_batteryMonitorThread != null && _batteryMonitorThread.IsAlive)
            {
                _batteryMonitorThread.Join(500);
                _batteryMonitorThread = null;
            }
        }

        /// <summary>
        /// Background thread that reads battery status from input reports.
        /// Uses a separate handle (_batteryReadHandle) to avoid blocking main command handle.
        /// </summary>
        private void ReadBatteryReports()
        {
            byte[] buffer = new byte[64];
            int consecutiveFailures = 0;
            const int MAX_CONSECUTIVE_FAILURES = 5;

            while (_monitoringBattery && _isConnected && _batteryReadHandle != IntPtr.Zero)
            {
                try
                {
                    uint bytesRead;
                    if (ReadFile(_batteryReadHandle, buffer, (uint)buffer.Length, out bytesRead, IntPtr.Zero))
                    {
                        consecutiveFailures = 0; // Reset on successful read

                        // Check for battery report format: 04 00 a1 ...
                        // Bytes: [0-1]=ReportID, [2]=Status, [3]=LeftBat, [4]=LeftCharge, [5]=RightBat, [6]=RightCharge
                        //        [7-9]=UNK, [10]=LeftConnType, [11]=RightConnType
                        // Connection type: 0x01=Not connected, 0x02=Bluetooth, 0x03=USB
                        if (bytesRead >= 12 && buffer[0] == 0x04 && buffer[1] == 0x00 && buffer[2] == 0xa1)
                        {
                            // Check connection type: 0x01 = not connected, 0x02 = BT, 0x03 = USB
                            bool leftConnected = buffer[10] != 0x01;
                            bool rightConnected = buffer[11] != 0x01;

                            // Battery value (1-100), or -1 if not connected
                            int leftBattery = leftConnected ? buffer[3] : -1;
                            int rightBattery = rightConnected ? buffer[5] : -1;

                            // Charging status: 0x04 = charging, 0x01 = discharging
                            bool leftCharging = leftConnected && buffer[4] == 0x04;
                            bool rightCharging = rightConnected && buffer[6] == 0x04;

                            bool changed = _leftControllerBattery != leftBattery ||
                                           _rightControllerBattery != rightBattery ||
                                           _leftControllerCharging != leftCharging ||
                                           _rightControllerCharging != rightCharging;

                            _leftControllerBattery = leftBattery;
                            _leftControllerCharging = leftCharging;
                            _rightControllerBattery = rightBattery;
                            _rightControllerCharging = rightCharging;

                            if (changed)
                            {
                                BatteryUpdated?.Invoke(this, new ControllerServiceBatteryEventArgs(
                                    leftBattery, leftCharging, rightBattery, rightCharging));
                            }
                        }
                    }
                    else
                    {
                        // Read failed - check if device disconnected
                        int error = Marshal.GetLastWin32Error();
                        consecutiveFailures++;

                        // ERROR_DEVICE_NOT_CONNECTED (1167) or ERROR_INVALID_HANDLE (6) = device disconnected or handle closed
                        if (error == 1167 || error == 6 || consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                        {
                            // Device disconnected - mark as disconnected (main handle will be closed by Disconnect())
                            _isConnected = false;
                            break;
                        }

                        Thread.Sleep(100);
                    }
                }
                catch
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    {
                        _isConnected = false;
                        break;
                    }
                    Thread.Sleep(100);
                }
            }

            // Cleanup when monitoring stops (device disconnected or StopBatteryMonitoring called)
            _monitoringBattery = false;

            // Close handle if still valid (may already be closed by StopBatteryMonitoring)
            var handleToClose = _batteryReadHandle;
            _batteryReadHandle = IntPtr.Zero;
            if (handleToClose != IntPtr.Zero && handleToClose != INVALID_HANDLE_VALUE)
            {
                try { CloseHandle(handleToClose); } catch { }
            }

            // Reset values when monitoring stops
            _leftControllerBattery = -1;
            _rightControllerBattery = -1;
            _leftControllerCharging = false;
            _rightControllerCharging = false;
        }

        #endregion

        /// <summary>
        /// Disposes the service and disconnects from the controller.
        /// </summary>
        public void Dispose()
        {
            StopBatteryMonitoring();
            Disconnect();
        }
    }

    #endregion

    #region ControllerServiceBatteryEventArgs

    /// <summary>
    /// Event arguments for controller battery status updates from LegionControllerService.
    /// </summary>
    public class ControllerServiceBatteryEventArgs : EventArgs
    {
        public int LeftBattery { get; private set; }
        public bool LeftCharging { get; private set; }
        public int RightBattery { get; private set; }
        public bool RightCharging { get; private set; }

        public ControllerServiceBatteryEventArgs(int leftBattery, bool leftCharging, int rightBattery, bool rightCharging)
        {
            LeftBattery = leftBattery;
            LeftCharging = leftCharging;
            RightBattery = rightBattery;
            RightCharging = rightCharging;
        }
    }

    #endregion
}

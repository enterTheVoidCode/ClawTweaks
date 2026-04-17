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

        #region LENOVO_FAN_METHOD - Custom Fan Curve

        /// <summary>
        /// Stored fan curve values (percentages 0-100) for retrieval.
        /// </summary>
        private static ushort[] _lastSetFanCurve = null;

        /// <summary>
        /// Default fan curve values (percentages) matching Legion Go defaults.
        /// </summary>
        public static readonly ushort[] DefaultFanCurve = { 44, 48, 55, 60, 71, 79, 87, 87, 100, 100 };

        /// <summary>
        /// Sets a custom fan curve via LENOVO_FAN_METHOD.Fan_Set_Table.
        /// Uses 40-byte FanTable format: 10 fan speeds as 4-byte DWORDs (2 bytes value + 2 bytes padding).
        /// </summary>
        /// <param name="fanSpeeds">Array of exactly 10 fan speed percentages (0-100)</param>
        /// <returns>Tuple containing success status and message</returns>
        public (bool Success, string Message) SetFanCurve(ushort[] fanSpeeds)
        {
            if (fanSpeeds == null || fanSpeeds.Length != 10)
                return (false, "Fan curve must have exactly 10 values");

            // Validate values are 0-100
            for (int i = 0; i < 10; i++)
            {
                if (fanSpeeds[i] > 100)
                    return (false, $"Fan speed at index {i} ({fanSpeeds[i]}) exceeds 100%");
            }

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

                using (var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_FAN_METHOD"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var inParams = obj.GetMethodParameters("Fan_Set_Table");
                        inParams["FanTable"] = fanTableBytes;
                        obj.InvokeMethod("Fan_Set_Table", inParams, null);

                        // Store the curve for later retrieval
                        _lastSetFanCurve = (ushort[])fanSpeeds.Clone();

                        return (true, $"Fan curve set: [{string.Join(", ", fanSpeeds)}]%");
                    }
                }
                return (false, "No LENOVO_FAN_METHOD instance found");
            }
            catch (Exception ex)
            {
                return (false, $"Error in SetFanCurve: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current custom fan curve values via LENOVO_FAN_METHOD.Fan_Get_Table.
        /// </summary>
        /// <returns>Tuple containing success status, message, and array of 10 fan speeds (0-100%)</returns>
        public (bool Success, string Message, ushort[] FanSpeeds) GetFanCurve()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(WMI_NAMESPACE, "SELECT * FROM LENOVO_FAN_METHOD"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            var outParams = obj.InvokeMethod("Fan_Get_Table", null, null);
                            if (outParams != null)
                            {
                                var tableBytes = (byte[])outParams["FanTable"];
                                if (tableBytes != null && tableBytes.Length >= 40) // 10 * 4 bytes
                                {
                                    // Parse 40-byte format: 10 DWORDs (2 bytes value + 2 bytes padding each)
                                    var fanSpeeds = new ushort[10];
                                    for (int i = 0; i < 10; i++)
                                    {
                                        fanSpeeds[i] = BitConverter.ToUInt16(tableBytes, i * 4);
                                    }
                                    _lastSetFanCurve = fanSpeeds;
                                    return (true, $"Fan curve: [{string.Join(", ", fanSpeeds)}]%", fanSpeeds);
                                }
                            }
                        }
                        catch
                        {
                            // Fan_Get_Table may not exist on all devices
                        }
                    }
                }

                // Fall back to last set values or default
                if (_lastSetFanCurve != null)
                {
                    return (true, $"Fan curve (cached): [{string.Join(", ", _lastSetFanCurve)}]%", (ushort[])_lastSetFanCurve.Clone());
                }

                return (true, $"Fan curve (default): [{string.Join(", ", DefaultFanCurve)}]%", (ushort[])DefaultFanCurve.Clone());
            }
            catch (Exception ex)
            {
                return (false, $"Error in GetFanCurve: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Checks if a custom fan curve has been set during this session.
        /// </summary>
        /// <returns>True if a custom fan curve has been set</returns>
        public bool HasCustomFanCurve() => _lastSetFanCurve != null;

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
        /// Gets the fan control sensor temperature (0x01 sensor) that the EC uses for fan curve lookup.
        /// This is typically 10-17°C lower than CPU temperature.
        /// </summary>
        /// <returns>Tuple containing success status, message, and temperature in Celsius</returns>
        public (bool Success, string Message, int? Result) GetFanControlSensorTemp() => GetFeatureValue(CapabilityID.FanControlSensorTemp);

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

}

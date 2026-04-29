using NLog;
using System.Collections.Generic;

namespace XboxGamingBarHelper.AMD.Settings
{
    /// <summary>
    /// One UMA / VGM allocation option as exposed by ADLX 1.5
    /// (IADLXVariableGraphicsMemoryOption). Snapshotted at probe time —
    /// AMD's driver returns Auto plus a handful of fixed Custom sizes
    /// (e.g. 4 GB / 8 GB / 12 GB / 16 GB depending on installed system RAM).
    /// </summary>
    internal class VariableGraphicsMemoryOptionInfo
    {
        public string Name;
        public ADLX_VARIABLE_GRAPHICS_MEMORY_MODE Mode;
        public double MemoryCarvedGB;
        public double MemoryRemainingGB;
    }

    /// <summary>
    /// Wraps IADLXVariableGraphicsMemory (ADLX 1.5+) for UMA carveout control.
    /// Gives the helper the ability to read the available allocation options,
    /// query the currently active one, and apply a different one. Setting an
    /// option typically requires a reboot to take effect (driver-imposed,
    /// matches AMD Adrenalin's behavior).
    ///
    /// Null on drivers without ADLX 1.5 — callers must IsAvailable-check.
    /// </summary>
    internal class AMDVariableGraphicsMemorySetting
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IADLXVariableGraphicsMemory adlxSetting;

        // Cached parallel lists. Indices line up: optionRefs[i] is the live
        // ADLX option object whose snapshot is optionInfos[i]. Refs are
        // retained so SetOptionByIndex can hand the same pointer back to ADLX.
        private List<IADLXVariableGraphicsMemoryOption> optionRefs = new List<IADLXVariableGraphicsMemoryOption>();
        private List<VariableGraphicsMemoryOptionInfo> optionInfos = new List<VariableGraphicsMemoryOptionInfo>();

        public AMDVariableGraphicsMemorySetting(IADLXVariableGraphicsMemory setting)
        {
            adlxSetting = setting;
        }

        public bool IsAvailable => adlxSetting != null;

        public bool IsSupported()
        {
            if (adlxSetting == null) return false;
            var ptr = ADLX.new_boolP();
            try
            {
                if (adlxSetting.IsSupported(ptr) != ADLX_RESULT.ADLX_OK) return false;
                return ADLX.boolP_value(ptr);
            }
            finally
            {
                ADLX.delete_boolP(ptr);
            }
        }

        /// <summary>
        /// Enumerates available options and caches both the live IADLX refs
        /// and an immutable info snapshot for serialization. Returns the
        /// snapshot list; callers should treat the returned list as read-only.
        /// </summary>
        public List<VariableGraphicsMemoryOptionInfo> RefreshAvailableOptions()
        {
            optionRefs.Clear();
            optionInfos.Clear();
            if (adlxSetting == null) return optionInfos;

            var listPtr = ADLX.new_vgmOptionListP_Ptr();
            try
            {
                if (adlxSetting.GetAvailableOptions(listPtr) != ADLX_RESULT.ADLX_OK)
                {
                    Logger.Warn("VGM GetAvailableOptions failed");
                    return optionInfos;
                }
                var rawList = ADLXPINVOKE.vgmOptionListP_Ptr_value(
                    SWIGTYPE_p_p_adlx__IADLXVariableGraphicsMemoryOptionList.getCPtr(listPtr));
                if (rawList == System.IntPtr.Zero) return optionInfos;

                using (var list = new IADLXVariableGraphicsMemoryOptionList(rawList, false))
                {
                    uint count = list.Size();
                    for (uint i = 0; i < count; i++)
                    {
                        var optPtr = ADLX.new_vgmOptionP_Ptr();
                        try
                        {
                            if (list.At(i, optPtr) != ADLX_RESULT.ADLX_OK) continue;
                            var rawOpt = ADLXPINVOKE.vgmOptionP_Ptr_value(
                                SWIGTYPE_p_p_adlx__IADLXVariableGraphicsMemoryOption.getCPtr(optPtr));
                            if (rawOpt == System.IntPtr.Zero) continue;
                            var opt = new IADLXVariableGraphicsMemoryOption(rawOpt, false);
                            optionRefs.Add(opt);
                            optionInfos.Add(SnapshotOption(opt));
                        }
                        finally
                        {
                            ADLX.delete_vgmOptionP_Ptr(optPtr);
                        }
                    }
                }
            }
            finally
            {
                ADLX.delete_vgmOptionListP_Ptr(listPtr);
            }
            return optionInfos;
        }

        public List<VariableGraphicsMemoryOptionInfo> GetCachedOptions() => optionInfos;

        /// <summary>
        /// Index in the cached available-options list of whichever option the
        /// driver currently reports as active, or -1 if not found / unsupported.
        /// Match is by Mode + carved size since pointer identity isn't
        /// guaranteed across separate GetOption / GetAvailableOptions calls.
        /// </summary>
        public int GetCurrentOptionIndex()
        {
            if (adlxSetting == null) return -1;
            var optPtr = ADLX.new_vgmOptionP_Ptr();
            try
            {
                if (adlxSetting.GetOption(optPtr) != ADLX_RESULT.ADLX_OK) return -1;
                var rawOpt = ADLXPINVOKE.vgmOptionP_Ptr_value(
                    SWIGTYPE_p_p_adlx__IADLXVariableGraphicsMemoryOption.getCPtr(optPtr));
                if (rawOpt == System.IntPtr.Zero) return -1;
                using (var current = new IADLXVariableGraphicsMemoryOption(rawOpt, false))
                {
                    var snap = SnapshotOption(current);
                    for (int i = 0; i < optionInfos.Count; i++)
                    {
                        var c = optionInfos[i];
                        if (c.Mode == snap.Mode &&
                            System.Math.Abs(c.MemoryCarvedGB - snap.MemoryCarvedGB) < 0.01)
                        {
                            return i;
                        }
                    }
                }
            }
            finally
            {
                ADLX.delete_vgmOptionP_Ptr(optPtr);
            }
            return -1;
        }

        public bool SetOptionByIndex(int index)
        {
            if (adlxSetting == null)
            {
                Logger.Warn("VGM SetOptionByIndex: adlxSetting null (ADLX 1.5 not available)");
                return false;
            }
            if (index < 0 || index >= optionRefs.Count)
            {
                Logger.Warn($"VGM SetOptionByIndex: index {index} out of range (count={optionRefs.Count})");
                return false;
            }
            var ok = adlxSetting.SetOption(optionRefs[index]) == ADLX_RESULT.ADLX_OK;
            if (ok)
            {
                var info = optionInfos[index];
                Logger.Info($"VGM SetOption applied: '{info.Name}' Mode={info.Mode} carved={info.MemoryCarvedGB:F1}GB remaining={info.MemoryRemainingGB:F1}GB (reboot may be required)");
            }
            else
            {
                Logger.Warn($"VGM SetOption FAILED at index {index}");
            }
            return ok;
        }

        private static VariableGraphicsMemoryOptionInfo SnapshotOption(IADLXVariableGraphicsMemoryOption opt)
        {
            var info = new VariableGraphicsMemoryOptionInfo();
            var namePtr = ADLX.new_charP_Ptr();
            try
            {
                if (opt.Name(namePtr) == ADLX_RESULT.ADLX_OK)
                {
                    info.Name = ADLX.charP_Ptr_value(namePtr) ?? "";
                }
            }
            finally
            {
                ADLX.delete_charP_Ptr(namePtr);
            }
            var modePtr = ADLX.new_vgmModeP();
            try
            {
                if (opt.Mode(modePtr) == ADLX_RESULT.ADLX_OK)
                {
                    info.Mode = ADLX.vgmModeP_value(modePtr);
                }
            }
            finally
            {
                ADLX.delete_vgmModeP(modePtr);
            }
            var carvedPtr = ADLX.new_doubleP();
            try
            {
                if (opt.MemoryCarved(carvedPtr) == ADLX_RESULT.ADLX_OK)
                {
                    info.MemoryCarvedGB = ADLX.doubleP_value(carvedPtr);
                }
            }
            finally
            {
                ADLX.delete_doubleP(carvedPtr);
            }
            var remainPtr = ADLX.new_doubleP();
            try
            {
                if (opt.MemoryRemaining(remainPtr) == ADLX_RESULT.ADLX_OK)
                {
                    info.MemoryRemainingGB = ADLX.doubleP_value(remainPtr);
                }
            }
            finally
            {
                ADLX.delete_doubleP(remainPtr);
            }
            return info;
        }
    }
}

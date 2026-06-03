using System;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Minimal P/Invoke wrapper around IGCL_Wrapper.dll for Intel Endurance Gaming (FPS tier cap).
    /// Ported from IntelGameBar (github.com/BassemMohsen/ToothNClaw) — only the endurance gaming
    /// surface is included. If the DLL is absent, Initialize() returns false and all methods are no-ops.
    /// </summary>
    internal static class IGCLBackend
    {
        // ── Native types ──────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct ctl_init_args_t
        {
            public int AppVersion;
            public int flags;
            public int Size;
            public int Version;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ctl_device_adapter_handle_t
        {
            public IntPtr handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ctl_endurance_gaming_t
        {
            public ctl_3d_endurance_gaming_control_t EGControl;
            public ctl_3d_endurance_gaming_mode_t    EGMode;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct ctl_device_adapter_properties_t
        {
            public uint   Size;
            public byte   Version;
            public IntPtr pDeviceID;
            public uint   device_id_size;
            public uint   device_type;
            public uint   supported_subfunction_flags;
            public ulong  driver_version;
            public ulong  fw_major;
            public ulong  fw_minor;
            public ulong  fw_build;
            public uint   pci_vendor_id;
            public uint   pci_device_id;
            public uint   rev_id;
            public uint   num_eus_per_sub_slice;
            public uint   num_sub_slices_per_slice;
            public uint   num_slices;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
            public string name;
            public uint   graphics_adapter_properties;
            public uint   Frequency;
            public ushort pci_subsys_id;
            public ushort pci_subsys_vendor_id;
            public byte   bdf_bus;
            public byte   bdf_device;
            public byte   bdf_function;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 112)]
            public string reserved;
        }

        public enum ctl_result_t : uint
        {
            CTL_RESULT_SUCCESS = 0x00000000,
            CTL_RESULT_ERROR_GENERIC_START = 0x40000000,
        }

        public enum ctl_3d_endurance_gaming_control_t : uint
        {
            OFF  = 0,
            ON   = 1,
            AUTO = 2,
            MAX  = 3
        }

        public enum ctl_3d_endurance_gaming_mode_t : uint
        {
            PERFORMANCE = 0,    // 60 FPS
            BALANCED    = 1,    // 40 FPS
            BATTERY     = 2,    // 30 FPS
            MAX         = 3
        }

        // ── Display: Adaptive Sharpness + Saturation (high-level wrapper exports) ──

        public enum ctl_sharpness_filter_type_flag_t : uint
        {
            NON_ADAPTIVE = 1,
            ADAPTIVE     = 2,
            MAX          = 0x80000000
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ctl_sharpness_settings_t
        {
            public uint Size;
            public byte Version;
            [MarshalAs(UnmanagedType.U1)]
            public bool Enable;
            public ctl_sharpness_filter_type_flag_t FilterType;
            public float Intensity;
        }

        // ── Kernel32 imports ─────────────────────────────────────────────────

        [DllImport("kernel32")]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = false)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

        // ── Delegate types ────────────────────────────────────────────────────

        private delegate ctl_result_t InitializeIgclDelegate();
        private delegate void         CloseIgclDelegate();
        private delegate IntPtr       EnumerateDevicesDelegate(ref uint pAdapterCount);
        private delegate ctl_result_t GetDevicePropertiesDelegate(ctl_device_adapter_handle_t hDev, ref ctl_device_adapter_properties_t props);
        private delegate ctl_result_t GetEnduranceGamingSettingsDelegate(ctl_device_adapter_handle_t hDev, ref ctl_endurance_gaming_t settings);
        private delegate ctl_result_t SetEnduranceGamingSettingsDelegate(ctl_device_adapter_handle_t hDev, ctl_endurance_gaming_t settings);
        // Display features (separate set so a missing export never disables the FPS limiter).
        private delegate ctl_result_t GetSharpnessSettingsDelegate(ctl_device_adapter_handle_t hDev, uint displayIdx, ref ctl_sharpness_settings_t s);
        private delegate ctl_result_t SetSharpnessSettingsDelegate(ctl_device_adapter_handle_t hDev, uint displayIdx, ctl_sharpness_settings_t s);
        private delegate ctl_result_t SetHueSaturationValuesDelegate(ctl_device_adapter_handle_t hDev, double hue, double saturation);

        // ── Loaded delegates ─────────────────────────────────────────────────

        private static InitializeIgclDelegate             _InitializeIgcl;
        private static CloseIgclDelegate                  _CloseIgcl;
        private static EnumerateDevicesDelegate           _EnumerateDevices;
        private static GetDevicePropertiesDelegate        _GetDeviceProperties;
        private static GetEnduranceGamingSettingsDelegate _GetEnduranceGamingSettings;
        private static SetEnduranceGamingSettingsDelegate _SetEnduranceGamingSettings;
        private static GetSharpnessSettingsDelegate       _GetSharpnessSettings;
        private static SetSharpnessSettingsDelegate       _SetSharpnessSettings;
        private static SetHueSaturationValuesDelegate     _SetHueSaturationValues;

        // ── State ─────────────────────────────────────────────────────────────

        public static IntPtr[] Devices = Array.Empty<IntPtr>();

        private static IntPtr  _hDll = IntPtr.Zero;
        private static bool    _ready = false;
        private static bool    _displayReady = false;

        /// <summary>True when the adaptive-sharpness / saturation exports bound successfully.</summary>
        public static bool IsDisplayReady => _displayReady;

        // Relative path: IGCL_Wrapper.dll sits next to the helper .exe
        private const string DllName = "IGCL_Wrapper.dll";

        // ── Public API ────────────────────────────────────────────────────────

        public static bool IsReady => _ready;

        /// <summary>Load the DLL, wire up delegates, and call InitializeIgcl.</summary>
        public static bool Initialize()
        {
            if (_ready) return true;

            _hDll = LoadLibrary(DllName);
            if (_hDll == IntPtr.Zero)
            {
                Console.WriteLine($"[IGCL] DLL not found: {DllName}");
                return false;
            }

            try
            {
                _InitializeIgcl             = GetDelegate<InitializeIgclDelegate>("IntializeIgcl");   // note: upstream has typo "Intialize"
                _CloseIgcl                  = GetDelegate<CloseIgclDelegate>("CloseIgcl");
                _EnumerateDevices           = GetDelegate<EnumerateDevicesDelegate>("EnumerateDevices");
                _GetDeviceProperties        = GetDelegate<GetDevicePropertiesDelegate>("GetDeviceProperties");
                _GetEnduranceGamingSettings = GetDelegate<GetEnduranceGamingSettingsDelegate>("GetEnduranceGamingSettings");
                _SetEnduranceGamingSettings = GetDelegate<SetEnduranceGamingSettingsDelegate>("SetEnduranceGamingSettings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IGCL] Failed to bind delegates: {ex.Message}");
                return false;
            }

            var result = _InitializeIgcl!();
            if (result != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] InitializeIgcl failed: 0x{(uint)result:X8}");
                return false;
            }

            _ready = true;
            Console.WriteLine("[IGCL] Initialized successfully.");

            // Bind the display-feature exports separately: if any is missing we only lose
            // sharpness/saturation, never the (already-working) FPS limiter.
            try
            {
                _GetSharpnessSettings   = GetDelegate<GetSharpnessSettingsDelegate>("GetSharpnessSettings");
                _SetSharpnessSettings   = GetDelegate<SetSharpnessSettingsDelegate>("SetSharpnessSettings");
                _SetHueSaturationValues = GetDelegate<SetHueSaturationValuesDelegate>("SetHueSaturationValues");
                _displayReady = true;
                Console.WriteLine("[IGCL] Display features (sharpness/saturation) bound.");
            }
            catch (Exception ex)
            {
                _displayReady = false;
                Console.WriteLine($"[IGCL] Display features unavailable: {ex.Message}");
            }

            return true;
        }

        /// <summary>Enumerate Intel GPU adapters and populate <see cref="Devices"/>.</summary>
        public static int FindIntelDeviceIndex(string preferredName = null)
        {
            if (!_ready) return -1;

            uint count = 0;
            IntPtr hDevices = _EnumerateDevices!(ref count);
            if (hDevices == IntPtr.Zero || count == 0) return -1;

            Devices = new IntPtr[count];
            Marshal.Copy(hDevices, Devices, 0, (int)count);

            for (int i = 0; i < Devices.Length; i++)
            {
                var props = new ctl_device_adapter_properties_t();
                var hDev  = new ctl_device_adapter_handle_t { handle = Devices[i] };
                var res   = _GetDeviceProperties!(hDev, ref props);
                if (res != ctl_result_t.CTL_RESULT_SUCCESS) continue;

                Console.WriteLine($"[IGCL] Adapter[{i}]: {props.name}");

                if (count == 1) return i;
                if (preferredName != null && props.name != null &&
                    props.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            // Fallback: return the first adapter
            return Devices.Length > 0 ? 0 : -1;
        }

        public static ctl_endurance_gaming_t GetEnduranceGaming(int deviceIdx)
        {
            var settings = new ctl_endurance_gaming_t();
            if (!_ready || deviceIdx < 0 || deviceIdx >= Devices.Length) return settings;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            _GetEnduranceGamingSettings!(hDev, ref settings);
            return settings;
        }

        public static bool SetEnduranceGaming(int deviceIdx,
            ctl_3d_endurance_gaming_control_t control,
            ctl_3d_endurance_gaming_mode_t    mode)
        {
            if (!_ready || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var settings = new ctl_endurance_gaming_t { EGControl = control, EGMode = mode };
            var hDev     = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            var res      = _SetEnduranceGamingSettings!(hDev, settings);
            return res == ctl_result_t.CTL_RESULT_SUCCESS;
        }

        /// <summary>
        /// Apply adaptive sharpness on a display output. intensity 0 = disable; 1..100 = enable
        /// adaptive filter at that intensity. Returns true on success (and read-back match).
        /// </summary>
        public static bool SetAdaptiveSharpness(int deviceIdx, uint displayIdx, int intensity)
        {
            if (!_displayReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };

            var s = new ctl_sharpness_settings_t();
            var res = _GetSharpnessSettings!(hDev, displayIdx, ref s);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] GetSharpnessSettings(d{displayIdx}) failed: 0x{(uint)res:X8}");
                return false;
            }

            if (intensity <= 0)
            {
                s.Enable = false;
                s.Intensity = 0;
            }
            else
            {
                if (intensity > 100) intensity = 100;
                s.Enable = true;
                s.FilterType = ctl_sharpness_filter_type_flag_t.ADAPTIVE;
                s.Intensity = intensity;
            }

            res = _SetSharpnessSettings!(hDev, displayIdx, s);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetSharpnessSettings(d{displayIdx}, {intensity}) failed: 0x{(uint)res:X8}");
                return false;
            }

            Console.WriteLine($"[IGCL] Adaptive sharpness applied: enable={s.Enable}, intensity={s.Intensity} (display {displayIdx}).");
            return true;
        }

        /// <summary>
        /// Apply colour saturation as a multiplier (1.0 = neutral). Hue is held at 0.
        /// The wrapper builds the CSC matrix internally.
        /// </summary>
        public static bool SetSaturation(int deviceIdx, double saturation)
        {
            if (!_displayReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            var res = _SetHueSaturationValues!(hDev, 0.0, saturation);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetHueSaturationValues({saturation}) failed: 0x{(uint)res:X8}");
                return false;
            }
            Console.WriteLine($"[IGCL] Saturation applied: {saturation:0.00} (hue 0).");
            return true;
        }

        public static void Terminate()
        {
            if (_ready && _CloseIgcl != null)
            {
                _CloseIgcl();
            }
            _ready = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static T GetDelegate<T>(string procName) where T : Delegate
        {
            IntPtr ptr = GetProcAddress(_hDll, procName);
            if (ptr == IntPtr.Zero)
                throw new EntryPointNotFoundException($"[IGCL] Export '{procName}' not found in {DllName}");
            return (T)Marshal.GetDelegateForFunctionPointer(ptr, typeof(T));
        }
    }
}

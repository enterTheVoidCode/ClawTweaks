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

        // ── Gaming 3D features: arbitrary FPS limit, low latency, frame sync ──────
        // These map to IGCL CTL_3D_FEATURE_FRAME_LIMIT / _LOW_LATENCY / _GAMING_FLIP_MODES
        // via the wrapper's high-level exports. Signatures verified against the ToothNClaw
        // C# backend that binds the same IGCL_Wrapper.dll (see reverse_engineered/RE_Intel_IGCL_Features.md).

        /// <summary>FPS-limit read-back: matches the wrapper's ctl_fps_limiter_t (Sequential, plain bool).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ctl_fps_limiter_t
        {
            public bool isLimiterEnabled;
            public int  fpsLimitValue;
        }

        public enum ctl_3d_low_latency_types_t : uint
        {
            TURN_OFF            = 0,
            TURN_ON             = 1,
            TURN_ON_BOOST_ON    = 2,
            MAX
        }

        [System.Flags]
        public enum ctl_gaming_flip_mode_flag_t : uint
        {
            APPLICATION_DEFAULT = 1u << 0,
            VSYNC_OFF           = 1u << 1,
            VSYNC_ON            = 1u << 2,
            SMOOTH_SYNC         = 1u << 3,
            SPEED_FRAME         = 1u << 4,
            CAPPED_FPS          = 1u << 5,
            MAX                 = 0x80000000
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
        private delegate ctl_result_t SetBrightnessContrastGammaValuesDelegate(ctl_device_adapter_handle_t hDev, double contrast, double panelGamma, double brightness);
        // Gaming features (separate set so a missing export never disables the FPS-tier / display paths).
        private delegate ctl_result_t SetFramesPerSecondLimitDelegate(ctl_device_adapter_handle_t hDev, bool isEnabled, int fpsLimit);
        private delegate ctl_result_t GetFramesPerSecondLimitDelegate(ctl_device_adapter_handle_t hDev, ref ctl_fps_limiter_t fpsLimiter);
        private delegate ctl_result_t SetLowLatencySettingDelegate(ctl_device_adapter_handle_t hDev, ctl_3d_low_latency_types_t setting);
        private delegate ctl_result_t GetLowLatencySettingDelegate(ctl_device_adapter_handle_t hDev, ref ctl_3d_low_latency_types_t setting);
        private delegate ctl_result_t SetFrameSyncSettingDelegate(ctl_device_adapter_handle_t hDev, ctl_gaming_flip_mode_flag_t setting);
        private delegate ctl_result_t GetFrameSyncSettingDelegate(ctl_device_adapter_handle_t hDev, ref ctl_gaming_flip_mode_flag_t setting);

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
        private static SetBrightnessContrastGammaValuesDelegate _SetBrightnessContrastGammaValues;
        private static SetFramesPerSecondLimitDelegate     _SetFramesPerSecondLimit;
        private static GetFramesPerSecondLimitDelegate     _GetFramesPerSecondLimit;
        private static SetLowLatencySettingDelegate        _SetLowLatencySetting;
        private static GetLowLatencySettingDelegate        _GetLowLatencySetting;
        private static SetFrameSyncSettingDelegate         _SetFrameSyncSetting;
        private static GetFrameSyncSettingDelegate         _GetFrameSyncSetting;

        // ── State ─────────────────────────────────────────────────────────────

        public static IntPtr[] Devices = Array.Empty<IntPtr>();

        private static IntPtr  _hDll = IntPtr.Zero;
        private static bool    _ready = false;
        private static bool    _displayReady = false;
        private static bool    _gamingReady = false;

        /// <summary>True when the adaptive-sharpness / saturation exports bound successfully.</summary>
        public static bool IsDisplayReady => _displayReady;

        /// <summary>True when the gaming exports (FPS limit / low latency / frame sync) bound successfully.</summary>
        public static bool IsGamingReady => _gamingReady;

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
                _SetBrightnessContrastGammaValues = GetDelegate<SetBrightnessContrastGammaValuesDelegate>("SetBrightnessContrastGammaValues");
                _displayReady = true;
                Console.WriteLine("[IGCL] Display features (sharpness/saturation) bound.");
            }
            catch (Exception ex)
            {
                _displayReady = false;
                Console.WriteLine($"[IGCL] Display features unavailable: {ex.Message}");
            }

            // Bind the gaming-feature exports separately (FPS limit / low latency / frame sync):
            // if any is missing we only lose those, never the (already-working) FPS tier / display.
            try
            {
                _SetFramesPerSecondLimit = GetDelegate<SetFramesPerSecondLimitDelegate>("SetFramesPerSecondLimit");
                _GetFramesPerSecondLimit = GetDelegate<GetFramesPerSecondLimitDelegate>("GetFramesPerSecondLimit");
                _SetLowLatencySetting    = GetDelegate<SetLowLatencySettingDelegate>("SetLowLatencySetting");
                _GetLowLatencySetting    = GetDelegate<GetLowLatencySettingDelegate>("GetLowLatencySetting");
                _SetFrameSyncSetting     = GetDelegate<SetFrameSyncSettingDelegate>("SetFrameSyncSetting");
                _GetFrameSyncSetting     = GetDelegate<GetFrameSyncSettingDelegate>("GetFrameSyncSetting");
                _gamingReady = true;
                Console.WriteLine("[IGCL] Gaming features (fps limit / low latency / frame sync) bound.");
            }
            catch (Exception ex)
            {
                _gamingReady = false;
                Console.WriteLine($"[IGCL] Gaming features unavailable: {ex.Message}");
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
                // DISABLE — ported 1:1 from ToothNClaw (SetImageSharpening(false)). The IGCL driver
                // does NOT clear an active sharpness filter if you flip Enable=false in the SAME call
                // that still carries a non-zero intensity. It must be a two-step commit: set
                // Intensity=0 FIRST while Enable is still true, commit, THEN set Enable=false and
                // commit. Doing it in one call left the filter stuck in the driver, which also poisoned
                // subsequent enables — so sharpness appeared to "not work at all" and never cleared on
                // game end. (Our old single-call path was the deviation from the reference.)
                s.Intensity = 0;
                res = _SetSharpnessSettings!(hDev, displayIdx, s);
                if (res != ctl_result_t.CTL_RESULT_SUCCESS)
                {
                    Console.WriteLine($"[IGCL] SetSharpnessSettings(intensity=0) failed: 0x{(uint)res:X8}");
                    return false;
                }
                s.Enable = false;
                res = _SetSharpnessSettings!(hDev, displayIdx, s);
                if (res != ctl_result_t.CTL_RESULT_SUCCESS)
                {
                    Console.WriteLine($"[IGCL] SetSharpnessSettings(disable) failed: 0x{(uint)res:X8}");
                    return false;
                }
                Console.WriteLine($"[IGCL] Adaptive sharpness disabled (display {displayIdx}).");
                return true;
            }

            // ENABLE. The driver does not reliably engage a filter that is flipped disabled→enabled
            // in the SAME commit that also carries the new intensity — it reports SUCCESS but nothing
            // changes on screen (exactly the "ok=True but no visual effect" we logged). ToothNClaw only
            // ever changes intensity on an ALREADY-enabled filter (it enables via a separate
            // SetImageSharpening(true) call), so mirror that: when the filter is currently off, commit
            // Enable=true FIRST, then commit the intensity in a second Set.
            if (intensity > 100) intensity = 100;

            if (!s.Enable)
            {
                s.Enable = true;
                s.FilterType = ctl_sharpness_filter_type_flag_t.ADAPTIVE;
                res = _SetSharpnessSettings!(hDev, displayIdx, s);
                if (res != ctl_result_t.CTL_RESULT_SUCCESS)
                {
                    Console.WriteLine($"[IGCL] SetSharpnessSettings(enable) failed: 0x{(uint)res:X8}");
                    return false;
                }
                _GetSharpnessSettings!(hDev, displayIdx, ref s); // refresh so the intensity commit builds on enabled state
            }

            s.Enable = true;
            s.FilterType = ctl_sharpness_filter_type_flag_t.ADAPTIVE;
            s.Intensity = intensity;
            res = _SetSharpnessSettings!(hDev, displayIdx, s);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetSharpnessSettings(d{displayIdx}, {intensity}) failed: 0x{(uint)res:X8}");
                return false;
            }

            // Read back and verify it actually stuck (ToothNClaw does this). A Set can return SUCCESS
            // while the driver silently ignores the value — the read-back is how we tell "applied" from
            // "no-op", and returning the match surfaces it as ok=False in the IntelGpuManager log.
            var verify = new ctl_sharpness_settings_t();
            if (_GetSharpnessSettings!(hDev, displayIdx, ref verify) == ctl_result_t.CTL_RESULT_SUCCESS)
            {
                bool stuck = verify.Enable && (int)verify.Intensity == intensity;
                Console.WriteLine($"[IGCL] Adaptive sharpness set intensity={intensity}; read-back enable={verify.Enable}, intensity={verify.Intensity}, filter={verify.FilterType} → stuck={stuck}");
                return stuck;
            }
            Console.WriteLine($"[IGCL] Adaptive sharpness applied: enable={s.Enable}, intensity={s.Intensity} (display {displayIdx}).");
            return true;
        }

        /// <summary>
        /// Apply hue (-180..180, 0 = neutral) + saturation (0..100, 50 = neutral). Values are
        /// passed through in TnC/IGCL units; the wrapper builds the CSC matrix internally.
        /// </summary>
        public static bool SetHueSaturation(int deviceIdx, double hue, double saturation)
        {
            if (!_displayReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            var res = _SetHueSaturationValues!(hDev, hue, saturation);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetHueSaturationValues(hue={hue}, sat={saturation}) failed: 0x{(uint)res:X8}");
                return false;
            }
            Console.WriteLine($"[IGCL] Hue/Saturation applied: hue={hue}, sat={saturation}.");
            return true;
        }

        /// <summary>
        /// Apply contrast (0..100, 50 = neutral), gamma (0.3..2.8, 1.0 = neutral) and
        /// brightness (0..100, 50 = neutral) together — single wrapper call.
        /// </summary>
        public static bool SetBrightnessContrastGamma(int deviceIdx, double contrast, double gamma, double brightness)
        {
            if (!_displayReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            if (_SetBrightnessContrastGammaValues == null) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            var res = _SetBrightnessContrastGammaValues(hDev, contrast, gamma, brightness);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetBrightnessContrastGammaValues(c={contrast}, g={gamma}, b={brightness}) failed: 0x{(uint)res:X8}");
                return false;
            }
            Console.WriteLine($"[IGCL] Brightness/Contrast/Gamma applied: contrast={contrast}, gamma={gamma:0.00}, brightness={brightness}.");
            return true;
        }

        // ── Gaming features: arbitrary FPS limit / low latency / frame sync ──────

        /// <summary>
        /// Arbitrary FPS cap via IGCL FRAME_LIMIT (independent of AC/DC power state, unlike
        /// Endurance Gaming which is DC-only). fps &lt;= 0 disables. Returns true on success.
        /// </summary>
        public static bool SetFramesPerSecondLimit(int deviceIdx, int fps)
        {
            if (!_gamingReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            bool enable = fps > 0;
            var res = _SetFramesPerSecondLimit!(hDev, enable, enable ? fps : 0);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetFramesPerSecondLimit(enable={enable}, fps={fps}) failed: 0x{(uint)res:X8}");
                return false;
            }
            Console.WriteLine($"[IGCL] Frame limit set: enable={enable}, fps={fps}.");
            return true;
        }

        /// <summary>Read the current IGCL frame limit; returns 0 when disabled/unavailable.</summary>
        public static int GetFramesPerSecondLimit(int deviceIdx)
        {
            if (!_gamingReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return 0;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            var f = new ctl_fps_limiter_t();
            var res = _GetFramesPerSecondLimit!(hDev, ref f);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS) return 0;
            return f.isLimiterEnabled ? f.fpsLimitValue : 0;
        }

        /// <summary>Low latency / anti-lag: 0 = off, 1 = on, 2 = on + boost. Returns true on success.</summary>
        public static bool SetLowLatency(int deviceIdx, int mode)
        {
            if (!_gamingReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            ctl_3d_low_latency_types_t setting = mode <= 0
                ? ctl_3d_low_latency_types_t.TURN_OFF
                : (mode == 1 ? ctl_3d_low_latency_types_t.TURN_ON
                             : ctl_3d_low_latency_types_t.TURN_ON_BOOST_ON);
            var res = _SetLowLatencySetting!(hDev, setting);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetLowLatencySetting({setting}) failed: 0x{(uint)res:X8}");
                return false;
            }
            Console.WriteLine($"[IGCL] Low latency set: {setting}.");
            return true;
        }

        /// <summary>
        /// Frame sync / gaming flip mode: 0 = App default, 1 = VSync off, 2 = VSync on,
        /// 3 = Smooth Sync, 4 = Speed Sync. Returns true on success.
        /// </summary>
        public static bool SetFrameSync(int deviceIdx, int mode)
        {
            if (!_gamingReady || deviceIdx < 0 || deviceIdx >= Devices.Length) return false;
            var hDev = new ctl_device_adapter_handle_t { handle = Devices[deviceIdx] };
            ctl_gaming_flip_mode_flag_t setting;
            switch (mode)
            {
                case 1:  setting = ctl_gaming_flip_mode_flag_t.VSYNC_OFF;    break;
                case 2:  setting = ctl_gaming_flip_mode_flag_t.VSYNC_ON;     break;
                case 3:  setting = ctl_gaming_flip_mode_flag_t.SMOOTH_SYNC;  break;
                case 4:  setting = ctl_gaming_flip_mode_flag_t.SPEED_FRAME;  break;
                default: setting = ctl_gaming_flip_mode_flag_t.APPLICATION_DEFAULT; break;
            }
            var res = _SetFrameSyncSetting!(hDev, setting);
            if (res != ctl_result_t.CTL_RESULT_SUCCESS)
            {
                Console.WriteLine($"[IGCL] SetFrameSyncSetting({setting}) failed: 0x{(uint)res:X8}");
                return false;
            }
            Console.WriteLine($"[IGCL] Frame sync set: {setting}.");
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

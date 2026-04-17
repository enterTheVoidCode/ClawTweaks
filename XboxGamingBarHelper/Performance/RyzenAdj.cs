using System;
using System.IO;
using System.Runtime.InteropServices;

namespace XboxGamingBarHelper.Performance
{
    /// <summary>
    /// RyzenAdj wrapper that loads libryzenadj.dll and WinRing0 from an external folder.
    /// Uses LoadLibrary + GetProcAddress to avoid DllImport path issues.
    /// </summary>
    internal class RyzenAdj
    {
        // Kernel32 functions
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetCurrentDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCurrentDirectory(uint nBufferLength, System.Text.StringBuilder lpBuffer);

        // DLL handles
        private static IntPtr winRing0Handle = IntPtr.Zero;
        private static IntPtr libRyzenAdjHandle = IntPtr.Zero;

        // WinRing0 status codes
        private const uint OLS_DLL_NO_ERROR = 0;
        private const uint OLS_DLL_UNSUPPORTED_PLATFORM = 1;
        private const uint OLS_DLL_DRIVER_NOT_LOADED = 2;
        private const uint OLS_DLL_DRIVER_NOT_FOUND = 3;
        private const uint OLS_DLL_DRIVER_UNLOADED = 4;
        private const uint OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK = 5;
        private const uint OLS_DLL_UNKNOWN_ERROR = 9;

        // WinRing0 function delegates
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool InitializeOlsDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint GetDllStatusDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DeinitializeOlsDelegate();

        private static InitializeOlsDelegate _initializeOls;
        private static GetDllStatusDelegate _getDllStatus;
        private static DeinitializeOlsDelegate _deinitializeOls;

        // Delegate types for RyzenAdj functions
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr InitRyzenAdjDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CleanupRyzenAdjDelegate(IntPtr ry);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RefreshTableDelegate(IntPtr ry);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetLimitDelegate(IntPtr ry, uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float GetLimitDelegate(IntPtr ry);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float GetValueWithIndexDelegate(IntPtr ry, uint index);

        // Function pointers
        private static InitRyzenAdjDelegate _init_ryzenadj;
        private static CleanupRyzenAdjDelegate _cleanup_ryzenadj;
        private static RefreshTableDelegate _refresh_table;
        private static SetLimitDelegate _set_stapm_limit;
        private static SetLimitDelegate _set_fast_limit;
        private static SetLimitDelegate _set_slow_limit;
        private static GetLimitDelegate _get_stapm_limit;
        private static GetLimitDelegate _get_fast_limit;
        private static GetLimitDelegate _get_slow_limit;
        private static GetValueWithIndexDelegate _get_core_clk;
        private static GetValueWithIndexDelegate _get_core_power;
        private static GetLimitDelegate _get_gfx_clk;
        private static GetLimitDelegate _get_gfx_temp;
        private static GetLimitDelegate _get_gfx_volt;
        private static GetLimitDelegate _get_mem_clk;
        private static GetLimitDelegate _get_fclk;
        private static GetLimitDelegate _get_soc_power;
        private static GetLimitDelegate _get_soc_volt;
        private static GetLimitDelegate _get_socket_power;
        private static SetLimitDelegate _set_max_gfxclk_freq;
        private static SetLimitDelegate _set_min_gfxclk_freq;
        private static SetLimitDelegate _set_gfx_clk;

        private static bool _isLoaded = false;
        private static string _loadedFolder = null;

        /// <summary>
        /// Gets a human-readable description of WinRing0 status code.
        /// </summary>
        public static string GetWinRing0StatusDescription(uint status)
        {
            switch (status)
            {
                case OLS_DLL_NO_ERROR: return "No error";
                case OLS_DLL_UNSUPPORTED_PLATFORM: return "Unsupported platform";
                case OLS_DLL_DRIVER_NOT_LOADED: return "Driver not loaded (needs admin rights)";
                case OLS_DLL_DRIVER_NOT_FOUND: return "Driver file not found";
                case OLS_DLL_DRIVER_UNLOADED: return "Driver was unloaded";
                case OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK: return "Cannot load driver on network path";
                case OLS_DLL_UNKNOWN_ERROR: return "Unknown error";
                default: return $"Unknown status code: {status}";
            }
        }

        /// <summary>
        /// Gets the last WinRing0 status. Call after init_ryzenadj() fails.
        /// </summary>
        public static uint GetLastWinRing0Status()
        {
            if (_getDllStatus != null)
                return _getDllStatus();
            return OLS_DLL_UNKNOWN_ERROR;
        }


        /// <summary>
        /// Loads RyzenAdj from the specified folder containing all required files.
        /// The folder must contain: libryzenadj.dll, WinRing0x64.dll, WinRing0x64.sys
        /// </summary>
        /// <param name="folder">Path to folder containing all RyzenAdj/WinRing0 files</param>
        /// <returns>True if loaded successfully</returns>
        public static bool LoadFromFolder(string folder)
        {
            if (_isLoaded)
                return true;

            // Save current directory
            var originalDir = new System.Text.StringBuilder(260);
            GetCurrentDirectory(260, originalDir);

            try
            {
                // Set current directory to folder - WinRing0 looks for .sys in current directory
                if (!SetCurrentDirectory(folder))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new System.ComponentModel.Win32Exception(error,
                        $"Failed to set current directory to {folder}");
                }

                // Load WinRing0 first - libryzenadj depends on it
                string winRing0Path = Path.Combine(folder, "WinRing0x64.dll");
                winRing0Handle = LoadLibrary(winRing0Path);
                if (winRing0Handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new System.ComponentModel.Win32Exception(error,
                        $"Failed to load WinRing0x64.dll from {winRing0Path}");
                }

                // Get WinRing0 diagnostic functions
                IntPtr initOlsPtr = GetProcAddress(winRing0Handle, "InitializeOls");
                IntPtr getDllStatusPtr = GetProcAddress(winRing0Handle, "GetDllStatus");
                IntPtr deinitOlsPtr = GetProcAddress(winRing0Handle, "DeinitializeOls");

                if (initOlsPtr != IntPtr.Zero)
                    _initializeOls = Marshal.GetDelegateForFunctionPointer<InitializeOlsDelegate>(initOlsPtr);
                if (getDllStatusPtr != IntPtr.Zero)
                    _getDllStatus = Marshal.GetDelegateForFunctionPointer<GetDllStatusDelegate>(getDllStatusPtr);
                if (deinitOlsPtr != IntPtr.Zero)
                    _deinitializeOls = Marshal.GetDelegateForFunctionPointer<DeinitializeOlsDelegate>(deinitOlsPtr);

                // Load libryzenadj
                string libRyzenAdjPath = Path.Combine(folder, "libryzenadj.dll");
                libRyzenAdjHandle = LoadLibrary(libRyzenAdjPath);
                if (libRyzenAdjHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new System.ComponentModel.Win32Exception(error,
                        $"Failed to load libryzenadj.dll from {libRyzenAdjPath}");
                }

                // Get function pointers
                _init_ryzenadj = GetDelegate<InitRyzenAdjDelegate>("init_ryzenadj");
                _cleanup_ryzenadj = GetDelegate<CleanupRyzenAdjDelegate>("cleanup_ryzenadj");
                _refresh_table = GetDelegate<RefreshTableDelegate>("refresh_table");
                _set_stapm_limit = GetDelegate<SetLimitDelegate>("set_stapm_limit");
                _set_fast_limit = GetDelegate<SetLimitDelegate>("set_fast_limit");
                _set_slow_limit = GetDelegate<SetLimitDelegate>("set_slow_limit");
                _get_stapm_limit = GetDelegate<GetLimitDelegate>("get_stapm_limit");
                _get_fast_limit = GetDelegate<GetLimitDelegate>("get_fast_limit");
                _get_slow_limit = GetDelegate<GetLimitDelegate>("get_slow_limit");
                _get_core_clk = GetDelegate<GetValueWithIndexDelegate>("get_core_clk");
                _get_core_power = GetDelegate<GetValueWithIndexDelegate>("get_core_power");
                _get_gfx_clk = GetDelegate<GetLimitDelegate>("get_gfx_clk");
                _get_gfx_temp = GetDelegate<GetLimitDelegate>("get_gfx_temp");
                _get_gfx_volt = GetDelegate<GetLimitDelegate>("get_gfx_volt");
                _get_mem_clk = GetDelegate<GetLimitDelegate>("get_mem_clk");
                _get_fclk = GetDelegate<GetLimitDelegate>("get_fclk");
                _get_soc_power = GetDelegate<GetLimitDelegate>("get_soc_power");
                _get_soc_volt = GetDelegate<GetLimitDelegate>("get_soc_volt");
                _get_socket_power = GetDelegate<GetLimitDelegate>("get_socket_power");
                _set_max_gfxclk_freq = GetDelegate<SetLimitDelegate>("set_max_gfxclk_freq");
                _set_min_gfxclk_freq = GetDelegate<SetLimitDelegate>("set_min_gfxclk_freq");
                _set_gfx_clk = GetDelegate<SetLimitDelegate>("set_gfx_clk");

                _isLoaded = true;
                _loadedFolder = folder;
                return true;
            }
            catch
            {
                // Cleanup on failure
                if (libRyzenAdjHandle != IntPtr.Zero)
                {
                    FreeLibrary(libRyzenAdjHandle);
                    libRyzenAdjHandle = IntPtr.Zero;
                }
                if (winRing0Handle != IntPtr.Zero)
                {
                    FreeLibrary(winRing0Handle);
                    winRing0Handle = IntPtr.Zero;
                }
                throw;
            }
            finally
            {
                // Restore original directory
                if (originalDir.Length > 0)
                {
                    SetCurrentDirectory(originalDir.ToString());
                }
            }
        }

        private static T GetDelegate<T>(string functionName) where T : Delegate
        {
            IntPtr procAddr = GetProcAddress(libRyzenAdjHandle, functionName);
            if (procAddr == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(error,
                    $"Failed to get function pointer for {functionName}");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(procAddr);
        }

        // Public API - calls through loaded function pointers
        public static IntPtr init_ryzenadj()
        {
            if (!_isLoaded) throw new InvalidOperationException("RyzenAdj not loaded. Call LoadFromFolder first.");

            // WinRing0 loads its .sys driver during init_ryzenadj() and looks in current directory
            var originalDir = new System.Text.StringBuilder(260);
            GetCurrentDirectory(260, originalDir);

            try
            {
                if (!string.IsNullOrEmpty(_loadedFolder))
                {
                    SetCurrentDirectory(_loadedFolder);
                }
                return _init_ryzenadj();
            }
            finally
            {
                // Restore original directory
                if (originalDir.Length > 0)
                {
                    SetCurrentDirectory(originalDir.ToString());
                }
            }
        }

        public static void cleanup_ryzenadj(IntPtr ry)
        {
            if (!_isLoaded) return;
            _cleanup_ryzenadj(ry);
        }

        public static int refresh_table(IntPtr ry)
        {
            if (!_isLoaded) return -1;
            return _refresh_table(ry);
        }

        public static int set_stapm_limit(IntPtr ry, uint value)
        {
            if (!_isLoaded) return -1;
            return _set_stapm_limit(ry, value);
        }

        public static int set_fast_limit(IntPtr ry, uint value)
        {
            if (!_isLoaded) return -1;
            return _set_fast_limit(ry, value);
        }

        public static int set_slow_limit(IntPtr ry, uint value)
        {
            if (!_isLoaded) return -1;
            return _set_slow_limit(ry, value);
        }

        public static float get_stapm_limit(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_stapm_limit(ry);
        }

        public static float get_fast_limit(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_fast_limit(ry);
        }

        public static float get_slow_limit(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_slow_limit(ry);
        }

        public static float get_core_clk(IntPtr ry, uint value)
        {
            if (!_isLoaded) return float.NaN;
            return _get_core_clk(ry, value);
        }

        public static float get_core_power(IntPtr ry, uint value)
        {
            if (!_isLoaded) return float.NaN;
            return _get_core_power(ry, value);
        }

        public static float get_gfx_clk(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_gfx_clk(ry);
        }

        public static float get_gfx_temp(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_gfx_temp(ry);
        }

        public static float get_gfx_volt(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_gfx_volt(ry);
        }

        public static float get_mem_clk(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_mem_clk(ry);
        }

        public static float get_fclk(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_fclk(ry);
        }

        public static float get_soc_power(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_soc_power(ry);
        }

        public static float get_soc_volt(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_soc_volt(ry);
        }

        public static float get_socket_power(IntPtr ry)
        {
            if (!_isLoaded) return float.NaN;
            return _get_socket_power(ry);
        }

        public static int set_max_gfxclk_freq(IntPtr ry, uint value)
        {
            if (!_isLoaded) return -1;
            return _set_max_gfxclk_freq(ry, value);
        }

        public static int set_min_gfxclk_freq(IntPtr ry, uint value)
        {
            if (!_isLoaded) return -1;
            return _set_min_gfxclk_freq(ry, value);
        }

        public static int set_gfx_clk(IntPtr ry, uint value)
        {
            if (!_isLoaded) return -1;
            return _set_gfx_clk(ry, value);
        }
    }
}

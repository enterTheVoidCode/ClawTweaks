using System;
using System.Collections.Generic;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class SettingsManager : Manager
    {
        private static SettingsManager instance;
        public static SettingsManager CreateInstance()
        {
            if (instance == null)
            {
                instance = new SettingsManager();
            }
            return instance;
        }

        public static SettingsManager GetInstance()
        {
            return instance;
        }

        // Step 1b: EXE-path → stable game name for per-game CONTROLLER profiles. These live widget-side
        // (LocalSettings containers) and are NOT in the performance GameProfiles dict, so the widget
        // pushes a flat (path, name) list here. SystemManager.TryMatchProfileByPath consults it so a
        // controller-only game is also detected by path with a stable identity (not the window title).
        private volatile IReadOnlyDictionary<string, string> controllerProfileGames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> ControllerProfileGames => controllerProfileGames;

        /// <summary>
        /// Replace the controller-profile path-to-name map from the widget's serialized payload.
        /// One entry per line; fields tab-separated: "&lt;exePath&gt;\t&lt;gameName&gt;". Tab and
        /// newline never occur in an exe path or game name, so no escaping is needed. Must match
        /// GamingWidget.BuildControllerProfileGamesPayload on the widget side.
        /// </summary>
        public void SetControllerProfileGames(string serialized)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(serialized))
            {
                foreach (var entry in serialized.Split('\n'))
                {
                    var trimmed = entry.Trim('\r');
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    var parts = trimmed.Split('\t');
                    if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1]))
                        map[parts[0]] = parts[1];
                }
            }
            controllerProfileGames = map;
            Logger.Info($"ControllerProfileGames updated: {map.Count} per-game controller profile path(s)");
            foreach (var kv in map)
                Logger.Info($"  ctrlProfile: \"{kv.Value}\" -> {kv.Key}");
        }

        private readonly AutoStartRTSSProperty autoStartRTSS;
        public AutoStartRTSSProperty AutoStartRTSS
        {
            get { return autoStartRTSS; }
        }

        private readonly AutoHibernateEnabledProperty autoHibernateEnabled;
        public AutoHibernateEnabledProperty AutoHibernateEnabled
        {
            get { return autoHibernateEnabled; }
        }

        private readonly AutoHibernateIdleMinutesProperty autoHibernateIdleMinutes;
        public AutoHibernateIdleMinutesProperty AutoHibernateIdleMinutes
        {
            get { return autoHibernateIdleMinutes; }
        }

        private readonly OnScreenDisplayProviderProperty onScreenDisplayProvider;
        public OnScreenDisplayProviderProperty OnScreenDisplayProvider
        {
            get { return onScreenDisplayProvider; }
        }

        private readonly IsForegroundProperty isForeground;
        public IsForegroundProperty IsForeground
        {
            get { return isForeground; }
        }

        private readonly UseManufacturerWMIProperty useManufacturerWMI;
        /// <summary>
        /// DEPRECATED: Use TdpMethod instead
        /// </summary>
        public UseManufacturerWMIProperty UseManufacturerWMI
        {
            get { return useManufacturerWMI; }
        }

        private readonly TdpMethodProperty tdpMethod;
        public TdpMethodProperty TdpMethod
        {
            get { return tdpMethod; }
        }

        // Controller emulation backend selection (Legacy ViGEm vs VIIPER)
        private readonly EmulationBackendProperty emulationBackend;
        public EmulationBackendProperty EmulationBackend
        {
            get { return emulationBackend; }
        }

        private readonly UsbipInstalledProperty usbipInstalled;
        public UsbipInstalledProperty UsbipInstalled
        {
            get { return usbipInstalled; }
        }

        // VIIPER emulation configuration (global, persisted)
        private readonly ViiperDeviceTypeProperty viiperDeviceType;
        public ViiperDeviceTypeProperty ViiperDeviceType
        {
            get { return viiperDeviceType; }
        }

        private readonly ViiperInputSourceProperty viiperInputSource;
        public ViiperInputSourceProperty ViiperInputSource
        {
            get { return viiperInputSource; }
        }

        private readonly ViiperGyroSourceProperty viiperGyroSource;
        public ViiperGyroSourceProperty ViiperGyroSource
        {
            get { return viiperGyroSource; }
        }

        private readonly ViiperSteamSubDeviceProperty viiperSteamSubDevice;
        public ViiperSteamSubDeviceProperty ViiperSteamSubDevice
        {
            get { return viiperSteamSubDevice; }
        }

        private readonly ViiperGuideButtonModeProperty viiperGuideButtonMode;
        public ViiperGuideButtonModeProperty ViiperGuideButtonMode
        {
            get { return viiperGuideButtonMode; }
        }

        private readonly ViiperSwapRumbleMotorsProperty viiperSwapRumbleMotors;
        public ViiperSwapRumbleMotorsProperty ViiperSwapRumbleMotors
        {
            get { return viiperSwapRumbleMotors; }
        }

        private readonly ViiperGameBarAutoXboxSwapProperty viiperGameBarAutoXboxSwap;
        public ViiperGameBarAutoXboxSwapProperty ViiperGameBarAutoXboxSwap
        {
            get { return viiperGameBarAutoXboxSwap; }
        }

        private readonly ViiperRumbleIntensityProperty viiperRumbleIntensity;
        public ViiperRumbleIntensityProperty ViiperRumbleIntensity
        {
            get { return viiperRumbleIntensity; }
        }

        private readonly ViiperMirrorLightbarToStickProperty viiperMirrorLightbarToStick;
        public ViiperMirrorLightbarToStickProperty ViiperMirrorLightbarToStick
        {
            get { return viiperMirrorLightbarToStick; }
        }

        private readonly ViiperStickGyroEnabledProperty viiperStickGyroEnabled;
        public ViiperStickGyroEnabledProperty ViiperStickGyroEnabled
        {
            get { return viiperStickGyroEnabled; }
        }

        private readonly ViiperGyroAxisMapProperty viiperGyroAxisMapX;
        public ViiperGyroAxisMapProperty ViiperGyroAxisMapX
        {
            get { return viiperGyroAxisMapX; }
        }

        private readonly ViiperGyroAxisMapProperty viiperGyroAxisMapY;
        public ViiperGyroAxisMapProperty ViiperGyroAxisMapY
        {
            get { return viiperGyroAxisMapY; }
        }

        private readonly ViiperGyroAxisMapProperty viiperGyroAxisMapZ;
        public ViiperGyroAxisMapProperty ViiperGyroAxisMapZ
        {
            get { return viiperGyroAxisMapZ; }
        }

        // Profile Detection Settings
        private readonly ProfileMatchByExeProperty profileMatchByExe;
        public ProfileMatchByExeProperty ProfileMatchByExe
        {
            get { return profileMatchByExe; }
        }

        private readonly ProfileCustomGamePathProperty profileCustomGamePath;
        public ProfileCustomGamePathProperty ProfileCustomGamePath
        {
            get { return profileCustomGamePath; }
        }

        private readonly ProfileGamesOnlyProperty profileGamesOnly;
        public ProfileGamesOnlyProperty ProfileGamesOnly
        {
            get { return profileGamesOnly; }
        }

        private readonly ProfileBlacklistPathsProperty profileBlacklistPaths;
        public ProfileBlacklistPathsProperty ProfileBlacklistPaths
        {
            get { return profileBlacklistPaths; }
        }

        protected SettingsManager() : base()
        {
            autoStartRTSS = new AutoStartRTSSProperty(this);
            autoHibernateEnabled = new AutoHibernateEnabledProperty(this);
            autoHibernateIdleMinutes = new AutoHibernateIdleMinutesProperty(this);
            onScreenDisplayProvider = new OnScreenDisplayProviderProperty(this);
            isForeground = new IsForegroundProperty(this);
            useManufacturerWMI = new UseManufacturerWMIProperty(this);
            tdpMethod = new TdpMethodProperty(this);
            emulationBackend = new EmulationBackendProperty(this);
            usbipInstalled = new UsbipInstalledProperty(this);
            viiperDeviceType = new ViiperDeviceTypeProperty(this);
            viiperInputSource = new ViiperInputSourceProperty(this);
            viiperGyroSource = new ViiperGyroSourceProperty(this);
            viiperSteamSubDevice = new ViiperSteamSubDeviceProperty(this);
            viiperGuideButtonMode = new ViiperGuideButtonModeProperty(this);
            viiperSwapRumbleMotors = new ViiperSwapRumbleMotorsProperty(this);
            viiperGameBarAutoXboxSwap = new ViiperGameBarAutoXboxSwapProperty(this);
            viiperRumbleIntensity = new ViiperRumbleIntensityProperty(this);
            viiperMirrorLightbarToStick = new ViiperMirrorLightbarToStickProperty(this);
            viiperStickGyroEnabled = new ViiperStickGyroEnabledProperty(this);
            viiperGyroAxisMapX = new ViiperGyroAxisMapProperty(this, Shared.Enums.Function.Viiper_GyroAxisMapX, "ViiperGyroAxisMapX", "X");
            viiperGyroAxisMapY = new ViiperGyroAxisMapProperty(this, Shared.Enums.Function.Viiper_GyroAxisMapY, "ViiperGyroAxisMapY", "Y");
            viiperGyroAxisMapZ = new ViiperGyroAxisMapProperty(this, Shared.Enums.Function.Viiper_GyroAxisMapZ, "ViiperGyroAxisMapZ", "Z");
            // Profile Detection Settings
            profileMatchByExe = new ProfileMatchByExeProperty(this);
            profileCustomGamePath = new ProfileCustomGamePathProperty(this);
            profileGamesOnly = new ProfileGamesOnlyProperty(this);
            profileBlacklistPaths = new ProfileBlacklistPathsProperty(this);
        }
    }
}

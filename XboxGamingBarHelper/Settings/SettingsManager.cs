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

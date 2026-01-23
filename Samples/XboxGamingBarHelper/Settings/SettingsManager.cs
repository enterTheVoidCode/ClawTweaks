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
            onScreenDisplayProvider = new OnScreenDisplayProviderProperty(this);
            isForeground = new IsForegroundProperty(this);
            useManufacturerWMI = new UseManufacturerWMIProperty(this);
            tdpMethod = new TdpMethodProperty(this);
            // Profile Detection Settings
            profileMatchByExe = new ProfileMatchByExeProperty(this);
            profileCustomGamePath = new ProfileCustomGamePathProperty(this);
            profileGamesOnly = new ProfileGamesOnlyProperty(this);
            profileBlacklistPaths = new ProfileBlacklistPathsProperty(this);
        }
    }
}

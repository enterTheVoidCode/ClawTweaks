using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    internal class SettingsManager : Manager
    {
        private static SettingsManager instance;
        public static SettingsManager CreateInstance(AppServiceConnection connection)
        {
            if (instance == null)
            {
                instance = new SettingsManager(connection);
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

        private readonly OnScreenDisplayProviderInstalledProperty onScreenDisplayProviderInstalled;
        public OnScreenDisplayProviderInstalledProperty OnScreenDisplayProviderInstalled
        {
            get { return onScreenDisplayProviderInstalled; }
        }

        private readonly IsForegroundProperty isForeground;
        public IsForegroundProperty IsForeground
        {
            get { return isForeground; }
        }

        protected SettingsManager(AppServiceConnection connection) : base(connection)
        {
            autoStartRTSS = new AutoStartRTSSProperty(this);
            onScreenDisplayProvider = new OnScreenDisplayProviderProperty(this);
            onScreenDisplayProviderInstalled = new OnScreenDisplayProviderInstalledProperty(this);
            isForeground = new IsForegroundProperty(this);
        }

        public override void Update()
        {
            base.Update();

            Logger.Debug($"On-Screen Display provider {Program.onScreenDisplay.Manager.GetType().Name} is {(Program.onScreenDisplay.Manager.IsInstalled ? "installed" : "not installed")}.");
            onScreenDisplayProviderInstalled.SetValue(Program.onScreenDisplay.Manager.IsInstalled);
        }
    }
}

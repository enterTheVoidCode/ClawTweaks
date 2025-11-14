using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.OnScreenDisplay
{
    internal abstract class OnScreenDisplayManager : Manager, IOnScreenDisplayProvider
    {
        protected int onScreenDisplayLevel;

        protected OnScreenDisplayManager(AppServiceConnection connection) : base(connection)
        {
            onScreenDisplayLevel = 0;
        }

        public bool IsInUsed { get ; set ; }

        public virtual void SetLevel(int level)
        {
            onScreenDisplayLevel = level;
        }
    }
}

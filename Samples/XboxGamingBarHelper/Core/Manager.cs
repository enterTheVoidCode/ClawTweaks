using NLog;
using Windows.ApplicationModel.AppService;

namespace XboxGamingBarHelper.Core
{
    internal abstract class Manager : IManager
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected Manager(AppServiceConnection connection)
        {
            Connection = connection;
        }

        public AppServiceConnection Connection { get; }

        public virtual void Update()
        {
            // Reserved.
        }
    }
}

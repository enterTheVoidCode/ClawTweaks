using System;
using Windows.ApplicationModel.AppService;

namespace XboxGamingBarHelper.Core
{
    internal interface IManager : IDisposable
    {
        AppServiceConnection Connection { get; }

        void Update();
    }
}

using System;
using Windows.ApplicationModel.AppService;

namespace XboxGamingBarHelper.Core
{
    internal interface IManager : IDisposable
    {
        AppServiceConnection Connection { get; set; }

        void Update();
    }
}

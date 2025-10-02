using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBar.Internal
{
    public enum FullTrustLaunchState
    {
        NotLaunched = 0,
        Reconnecting = 1,
        Launching = 2,
        Launched = 3,
    }
}

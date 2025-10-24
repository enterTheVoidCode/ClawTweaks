using System.Diagnostics;

namespace Shared.Utilities
{
    public static class RTSSHelper
    {
        public static bool IsRunning()
        {
            return Process.GetProcessesByName("RTSS").Length > 0;
        }
    }
}

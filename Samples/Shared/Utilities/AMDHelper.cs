using Microsoft.Win32;

namespace Shared.Utilities
{
    public class AMDHelper
    {
        public static bool IsInstalled()
        {
            // Computer\HKEY_CURRENT_USER\Software\AMD\CN
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\AMD\CN"))
            {
                return key != null;
            }
        }
    }
}

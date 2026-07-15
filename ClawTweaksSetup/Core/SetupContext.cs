using System;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Where the wizard looks for the package/cert to install. Defaults to the exe's own directory
    /// (release-folder execution, unchanged from before). <see cref="CenterMenuWindow"/> repoints this
    /// at a downloaded/staged folder before opening <see cref="MainWindow"/> for a standalone run.
    /// </summary>
    public static class SetupContext
    {
        public static string AssetRoot = AppContext.BaseDirectory;
    }
}

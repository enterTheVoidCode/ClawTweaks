using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        /// <summary>
        /// Shows or hides the Scale tab and Lossless Scaling tile based on Lossless Scaling installation
        /// </summary>
        private void SetScaleTabVisibility(bool installed)
        {
            if (ScalingNavItem != null)
            {
                ScalingNavItem.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"Scale tab visibility set to: {installed} (Lossless Scaling installed: {installed})");
            }

            // Rebuild Quick Settings tiles to show/hide Lossless Scaling tile
            if (quickSettingsInitialized)
            {
                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                Logger.Info($"Rebuilt Quick Settings tiles for Lossless Scaling visibility: {installed}");
            }
        }
    }
}

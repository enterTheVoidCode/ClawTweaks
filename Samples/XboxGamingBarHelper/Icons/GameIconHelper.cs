using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NLog;
using Windows.Storage;

namespace XboxGamingBarHelper.Icons
{
    /// <summary>
    /// Extracts game icons from executables using managed .NET APIs.
    /// Works with any executable - Steam, GOG, Epic, direct installs, etc.
    /// </summary>
    public static class GameIconHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string ICON_CACHE_FOLDER = "icons";
        private const int DEFAULT_ICON_SIZE = 64;
        private const int DEFAULT_CORNER_RADIUS = 6;

        /// <summary>
        /// Gets the path to the icon cache folder.
        /// Uses ApplicationData when available, falls back to LocalAppData when running outside package context.
        /// </summary>
        public static string GetIconCacheFolder()
        {
            try
            {
                // Try to use ApplicationData (works when running in package context)
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, ICON_CACHE_FOLDER);
            }
            catch
            {
                // Fallback for running outside package context (e.g., via scheduled task)
                // Use the same path as the deployed helper location
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Packages", "PlayandBuildCustom.10365195AA1EC_8edemd50ez3gg", "LocalCache", ICON_CACHE_FOLDER);
            }
        }

        /// <summary>
        /// Ensures the icon cache folder exists.
        /// </summary>
        private static void EnsureIconCacheFolderExists()
        {
            var folder = GetIconCacheFolder();
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Logger.Info($"Created icon cache folder: {folder}");
            }
        }

        /// <summary>
        /// Generates a unique cache filename for an executable path.
        /// Uses MD5 hash of the path to handle special characters and long paths.
        /// </summary>
        private static string GetCacheFileName(string exePath)
        {
            // Use hash to create a unique, filesystem-safe filename
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant()));
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                // Also include exe name for easier debugging
                var exeName = Path.GetFileNameWithoutExtension(exePath);
                // Remove invalid filename characters
                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    exeName = exeName.Replace(c, '_');
                }

                // Truncate if too long
                if (exeName.Length > 32)
                    exeName = exeName.Substring(0, 32);

                return $"{exeName}_{hashString.Substring(0, 8)}.png";
            }
        }

        /// <summary>
        /// Gets the cached icon path for an executable, or null if not cached.
        /// </summary>
        public static string GetCachedIconPath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            var cacheFolder = GetIconCacheFolder();
            var cacheFileName = GetCacheFileName(exePath);
            var cachePath = Path.Combine(cacheFolder, cacheFileName);

            return File.Exists(cachePath) ? cachePath : null;
        }

        /// <summary>
        /// Extracts and caches the icon for an executable.
        /// Returns the path to the cached icon, or null if extraction failed.
        /// </summary>
        /// <param name="exePath">Full path to the executable</param>
        /// <param name="size">Icon size in pixels (default 64)</param>
        /// <param name="cornerRadius">Corner radius for rounded corners (default 6, 0 for square)</param>
        /// <param name="forceRefresh">If true, re-extract even if cached</param>
        public static string ExtractAndCacheIcon(string exePath, int size = DEFAULT_ICON_SIZE, int cornerRadius = DEFAULT_CORNER_RADIUS, bool forceRefresh = false)
        {
            if (string.IsNullOrEmpty(exePath))
                return null;

            if (!File.Exists(exePath))
            {
                Logger.Debug($"ExtractAndCacheIcon: Exe not found: {exePath}");
                return null;
            }

            try
            {
                EnsureIconCacheFolderExists();

                var cacheFolder = GetIconCacheFolder();
                var cacheFileName = GetCacheFileName(exePath);
                var cachePath = Path.Combine(cacheFolder, cacheFileName);

                // Check if already cached
                if (!forceRefresh && File.Exists(cachePath))
                {
                    Logger.Debug($"ExtractAndCacheIcon: Using cached icon: {cachePath}");
                    return cachePath;
                }

                // Extract icon using managed API
                using (var bitmap = ExtractIconBitmap(exePath, size, cornerRadius))
                {
                    if (bitmap == null)
                    {
                        Logger.Debug($"ExtractAndCacheIcon: Failed to extract icon from {exePath}");
                        return null;
                    }

                    // Save to cache as PNG
                    bitmap.Save(cachePath, ImageFormat.Png);
                    Logger.Info($"ExtractAndCacheIcon: Cached icon for {Path.GetFileName(exePath)} at {cachePath}");
                    return cachePath;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"ExtractAndCacheIcon: Error extracting icon from {exePath}");
                return null;
            }
        }

        /// <summary>
        /// Extracts an icon from an executable using the managed .NET Icon API.
        /// For UWP apps, looks for logo assets in the app folder instead.
        /// </summary>
        private static Bitmap ExtractIconBitmap(string exePath, int size, int cornerRadius)
        {
            try
            {
                // Check if this is a UWP app (in WindowsApps folder)
                // UWP apps don't have embedded icons - they use separate PNG assets
                if (exePath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var uwpIcon = ExtractUwpAppIcon(exePath, size, cornerRadius);
                    if (uwpIcon != null)
                    {
                        Logger.Info($"ExtractIconBitmap: Using UWP app icon for {Path.GetFileName(exePath)}");
                        return uwpIcon;
                    }
                    Logger.Debug($"ExtractIconBitmap: No UWP icon found, falling back to ExtractAssociatedIcon");
                }

                // Use managed API - Icon.ExtractAssociatedIcon
                // This is a trusted .NET Framework API that doesn't trigger security warnings
                using (var icon = Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon == null)
                    {
                        Logger.Debug($"ExtractIconBitmap: ExtractAssociatedIcon returned null for {exePath}");
                        return null;
                    }

                    // Convert icon to bitmap at desired size
                    using (var iconBitmap = icon.ToBitmap())
                    {
                        // Resize to target size
                        var resizedBitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(resizedBitmap))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            g.DrawImage(iconBitmap, 0, 0, size, size);
                        }

                        // Apply rounded corners if needed
                        if (cornerRadius > 0)
                        {
                            var rounded = ApplyRoundedCorners(resizedBitmap, size, cornerRadius);
                            resizedBitmap.Dispose();
                            return rounded;
                        }

                        return resizedBitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ExtractIconBitmap: Exception for {exePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts icon from a UWP app by looking for logo assets in the app folder.
        /// UWP apps store icons as separate PNG files (Square150x150Logo.png, StoreLogo.png, etc.)
        /// </summary>
        private static Bitmap ExtractUwpAppIcon(string exePath, int size, int cornerRadius)
        {
            try
            {
                var appFolder = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(appFolder) || !Directory.Exists(appFolder))
                    return null;

                // Look for logo files in order of preference
                // 44x44 is ideal for small UI icons (36x36 display) - minimal scaling = sharper details
                var logoFiles = new[]
                {
                    "Square44x44Logo.png",    // Best match for 36x36 display
                    "Square150x150Logo.png",  // Good fallback
                    "StoreLogo.png",
                    "Square480x480Logo.png",  // Large - loses detail when scaled down
                    "Logo.png",
                    "AppIcon.png"
                };

                // Also check for scaled versions (e.g., Square150x150Logo.scale-200.png)
                var scaleSuffixes = new[] { "", ".scale-200", ".scale-150", ".scale-125", ".scale-100" };

                string foundLogoPath = null;

                foreach (var logo in logoFiles)
                {
                    foreach (var scale in scaleSuffixes)
                    {
                        var logoName = Path.GetFileNameWithoutExtension(logo) + scale + Path.GetExtension(logo);
                        var logoPath = Path.Combine(appFolder, logoName);

                        if (File.Exists(logoPath))
                        {
                            foundLogoPath = logoPath;
                            break;
                        }
                    }

                    if (foundLogoPath != null)
                        break;

                    // Also check in Assets subfolder
                    var assetsPath = Path.Combine(appFolder, "Assets", logo);
                    if (File.Exists(assetsPath))
                    {
                        foundLogoPath = assetsPath;
                        break;
                    }
                }

                if (foundLogoPath == null)
                {
                    Logger.Debug($"ExtractUwpAppIcon: No logo found in {appFolder}");
                    return null;
                }

                Logger.Debug($"ExtractUwpAppIcon: Found logo at {foundLogoPath}");

                // Load the PNG and resize to target size
                using (var originalBitmap = new Bitmap(foundLogoPath))
                {
                    var resizedBitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(resizedBitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.DrawImage(originalBitmap, 0, 0, size, size);
                    }

                    // Apply rounded corners if needed
                    if (cornerRadius > 0)
                    {
                        var rounded = ApplyRoundedCorners(resizedBitmap, size, cornerRadius);
                        resizedBitmap.Dispose();
                        return rounded;
                    }

                    return resizedBitmap;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ExtractUwpAppIcon: Exception for {exePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies rounded corners to a bitmap.
        /// </summary>
        private static Bitmap ApplyRoundedCorners(Image source, int size, int cornerRadius)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int diameter = cornerRadius * 2;
                    path.AddArc(0, 0, diameter, diameter, 180, 90);
                    path.AddArc(bmp.Width - diameter, 0, diameter, diameter, 270, 90);
                    path.AddArc(bmp.Width - diameter, bmp.Height - diameter, diameter, diameter, 0, 90);
                    path.AddArc(0, bmp.Height - diameter, diameter, diameter, 90, 90);
                    path.CloseFigure();

                    g.SetClip(path);
                    g.DrawImage(source, 0, 0, bmp.Width, bmp.Height);
                }
            }

            return bmp;
        }

        /// <summary>
        /// Converts a color Bitmap to grayscale (useful for suspended games).
        /// </summary>
        public static Bitmap MakeGrayscale(Bitmap original)
        {
            if (original == null)
                return null;

            Bitmap grayBmp = new Bitmap(original.Width, original.Height);

            using (Graphics g = Graphics.FromImage(grayBmp))
            {
                // Create grayscale color matrix
                var colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                        new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                        new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }

            return grayBmp;
        }

        /// <summary>
        /// Clears the entire icon cache.
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                var folder = GetIconCacheFolder();
                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.GetFiles(folder, "*.png"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                    Logger.Info("Icon cache cleared");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error clearing icon cache");
            }
        }

        /// <summary>
        /// Removes a specific icon from the cache.
        /// </summary>
        public static void RemoveFromCache(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return;

            try
            {
                var cachePath = GetCachedIconPath(exePath);
                if (cachePath != null && File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                    Logger.Debug($"Removed icon from cache: {cachePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error removing icon from cache for {exePath}");
            }
        }
    }
}

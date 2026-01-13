using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NLog;
using Windows.Storage;

namespace XboxGamingBarHelper.Icons
{
    /// <summary>
    /// Extracts game icons from executables using Windows Shell API.
    /// Works with any executable - Steam, GOG, Epic, direct installs, etc.
    /// </summary>
    public static class GameIconHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string ICON_CACHE_FOLDER = "icons";
        private const int DEFAULT_ICON_SIZE = 64;
        private const int DEFAULT_CORNER_RADIUS = 6;

        #region COM Interfaces

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(
                [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
                [In] SIIGBF flags,
                out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10,
            SIIGBF_ICONBACKGROUND = 0x80
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
        }

        #endregion

        #region P/Invoke

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        #endregion

        /// <summary>
        /// Gets the path to the icon cache folder.
        /// </summary>
        public static string GetIconCacheFolder()
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, ICON_CACHE_FOLDER);
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

                // Extract icon using Shell API
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
        /// Extracts an icon from an executable using Windows Shell API.
        /// </summary>
        private static Bitmap ExtractIconBitmap(string exePath, int size, int cornerRadius)
        {
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                // Create the shell item from the file path
                Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                SHCreateItemFromParsingName(exePath, IntPtr.Zero, shellItemGuid, out IShellItem shellItem);

                if (shellItem == null)
                {
                    Logger.Debug($"ExtractIconBitmap: SHCreateItemFromParsingName returned null for {exePath}");
                    return null;
                }

                var factory = (IShellItemImageFactory)shellItem;
                if (factory == null)
                {
                    Logger.Debug($"ExtractIconBitmap: Failed to get IShellItemImageFactory for {exePath}");
                    return null;
                }

                SIZE sz = new SIZE { cx = size, cy = size };

                // Request the icon with the specified size
                int hr = factory.GetImage(sz, SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                {
                    Logger.Debug($"ExtractIconBitmap: GetImage failed with HRESULT {hr:X8} for {exePath}");
                    return null;
                }

                // Convert HBITMAP to managed Bitmap
                using (var temp = Image.FromHbitmap(hBitmap))
                {
                    // Apply rounded corners if needed
                    if (cornerRadius > 0)
                    {
                        return ApplyRoundedCorners(temp, size, cornerRadius);
                    }
                    else
                    {
                        // Return a copy (temp will be disposed)
                        return new Bitmap(temp);
                    }
                }
            }
            catch (COMException ex)
            {
                Logger.Debug($"ExtractIconBitmap: COM exception for {exePath}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Debug($"ExtractIconBitmap: Exception for {exePath}: {ex.Message}");
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
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

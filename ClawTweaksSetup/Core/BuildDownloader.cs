using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using ClawTweaksSetup.Core.Sources;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Downloads a <see cref="BuildSource"/> picked in the Center menu and stages it into a folder
    /// that <see cref="PackageInstaller"/>/<see cref="CertInstaller"/> can treat as
    /// <see cref="SetupContext.AssetRoot"/> — either just the .msix (cert already trusted) or the
    /// full installer ZIP, extracted flat (matches how Build-Setup.ps1 zips the release folder).
    /// </summary>
    public static class BuildDownloader
    {
        public static async Task<string> DownloadAndStageAsync(
            BuildSource source, bool certAlreadyTrusted, Action<string> log = null, IProgress<int> progress = null)
        {
            string safeVersion = string.Join("_", source.Version.Split(Path.GetInvalidFileNameChars()));
            string dir = Path.Combine(Path.GetTempPath(), "ClawTweaksCenter", safeVersion);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            bool msixOnly = certAlreadyTrusted && source.MsixUrl != null;

            if (msixOnly)
            {
                log?.Invoke($"Cert already trusted — downloading just the .msix ({source.Version})…");
                string msixPath = Path.Combine(dir, "package.msix");
                await DownloadFileAsync(source.MsixUrl, msixPath, progress);
                log?.Invoke("Download complete.");
                return dir;
            }

            log?.Invoke($"Downloading installer ({source.Version})…");
            string zipPath = Path.Combine(dir, "installer.zip");
            await DownloadFileAsync(source.ZipUrl, zipPath, progress);
            log?.Invoke("Extracting…");
            ZipFile.ExtractToDirectory(zipPath, dir, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { }
            log?.Invoke("Ready.");
            return dir;
        }

        private static async Task DownloadFileAsync(string url, string destPath, IProgress<int> progress)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ClawTweaks");

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long? total = resp.Content.Headers.ContentLength;
            using var httpStream = await resp.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(destPath);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, n);
                read += n;
                if (total.HasValue && total.Value > 0)
                    progress?.Report((int)(read * 100 / total.Value));
            }
        }
    }
}

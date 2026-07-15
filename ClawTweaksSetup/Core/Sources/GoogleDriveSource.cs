using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ClawTweaksSetup.Core.Sources
{
    /// <summary>
    /// Lists the last few nightlies from the shared "Nightlys" Google Drive folder (public,
    /// "anyone with the link") via the Drive API v3, using an API key — no OAuth/login needed since
    /// the folder is publicly readable. Only full installer ZIPs are uploaded there (no separate
    /// msix), so every nightly always goes through <see cref="BuildDownloader"/>'s full-zip path.
    /// </summary>
    public static class GoogleDriveSource
    {
        private const string FolderId = "1yLUyaYy20eZHFWy0ygyP6LbJsApAHBXF";

        private static readonly Regex VersionRegex = new Regex(@"ClawTweaks_([\d.]+)_Installer\.zip", RegexOptions.IgnoreCase);

        /// <summary>
        /// The Drive API key lives outside the repo entirely — same trust model as ClawTweaks.pfx
        /// (never committed). Drop it in %LOCALAPPDATA%\ClawTweaksCenter\drive-api-key.txt on any
        /// machine that should be able to list nightlies; without it, this section just shows a
        /// clean "not configured" error instead of crashing anything else.
        /// </summary>
        private static string LoadApiKey()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClawTweaksCenter", "drive-api-key.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch { return null; }
        }

        public static async Task<List<BuildSource>> FetchAsync()
        {
            string apiKey = LoadApiKey();
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException(
                    "Google Drive API key not configured — add one to %LOCALAPPDATA%\\ClawTweaksCenter\\drive-api-key.txt");

            string url = "https://www.googleapis.com/drive/v3/files"
                + "?q=" + Uri.EscapeDataString($"'{FolderId}' in parents and trashed=false")
                + "&orderBy=" + Uri.EscapeDataString("modifiedTime desc")
                + "&pageSize=5"
                + "&fields=" + Uri.EscapeDataString("files(id,name,modifiedTime,size)")
                + "&key=" + apiKey;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ClawTweaks");

            string json = await http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var result = new List<BuildSource>();
            if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var f in files.EnumerateArray())
            {
                string id = f.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                string name = f.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (id == null || name == null) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                DateTime when = f.TryGetProperty("modifiedTime", out var mt) && DateTime.TryParse(mt.GetString(), out var d)
                    ? d : DateTime.MinValue;
                long? size = f.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.String &&
                             long.TryParse(sz.GetString(), out var sizeVal) ? sizeVal : (long?)null;

                var m = VersionRegex.Match(name);
                string version = m.Success ? m.Groups[1].Value : name;

                result.Add(new BuildSource
                {
                    Origin = "Nightly",
                    Version = version,
                    Title = name,
                    When = when,
                    SizeBytes = size,
                    ZipUrl = $"https://www.googleapis.com/drive/v3/files/{id}?alt=media&key={apiKey}",
                    MsixUrl = null,
                });
            }

            return result.OrderByDescending(b => b.When).Take(3).ToList();
        }
    }
}

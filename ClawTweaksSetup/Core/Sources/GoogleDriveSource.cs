using System;
using System.Collections.Generic;
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

        // Read-only, restricted-to-Drive-API key so end users get a working nightly list out of the
        // box — no per-user setup step. Shipping in the exe means it's extractable via decompilation
        // like any client-embedded key; mitigate by keeping it restricted to the Drive API only and
        // capped with a quota in Google Cloud Console, and rotate it there (+ rebuild) if it's ever
        // abused.
        private const string ApiKey = "AIzaSyCnOwAdpy8Z3CFkCp0nNz2SovHyuBPFD2o";

        private static readonly Regex VersionRegex = new Regex(@"ClawTweaks_([\d.]+)_Installer\.zip", RegexOptions.IgnoreCase);

        public static async Task<List<BuildSource>> FetchAsync()
        {
            string apiKey = ApiKey;

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClawTweaksSetup.Core.Sources
{
    /// <summary>
    /// Lists ClawTweaks GitHub releases, split into stable releases and test builds (pre-releases).
    /// Unauthenticated — a handful of manual refreshes per session stays well under GitHub's 60/hour
    /// anonymous rate limit.
    /// </summary>
    public static class GitHubReleaseSource
    {
        private const string ApiUrl = "https://api.github.com/repos/enterTheVoidCode/ClawTweaks/releases?per_page=30";

        public static async Task<(List<BuildSource> Releases, List<BuildSource> TestBuilds)> FetchAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ClawTweaks");
            http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            string json = await http.GetStringAsync(ApiUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var releases = new List<BuildSource>();
            var testBuilds = new List<BuildSource>();

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (GetBool(el, "draft")) continue;

                bool prerelease = GetBool(el, "prerelease");
                string tag = GetString(el, "tag_name") ?? "?";
                string name = GetString(el, "name") ?? tag;
                DateTime when = DateTime.TryParse(GetString(el, "published_at"), out var d) ? d : DateTime.MinValue;

                string zipUrl = null, msixUrl = null; long? size = null;
                if (el.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string assetName = GetString(asset, "name") ?? "";
                        string url = GetString(asset, "browser_download_url");
                        if (assetName.EndsWith("_Installer.zip", StringComparison.OrdinalIgnoreCase))
                        {
                            zipUrl = url;
                            size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : (long?)null;
                        }
                        else if (assetName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                                 assetName.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
                        {
                            msixUrl = url;
                        }
                    }
                }

                if (zipUrl == null) continue; // nothing installable here (e.g. a source-only release)

                var build = new BuildSource
                {
                    Origin = prerelease ? "Test build" : "Release",
                    Version = tag,
                    Title = name,
                    When = when,
                    SizeBytes = size,
                    ZipUrl = zipUrl,
                    MsixUrl = msixUrl,
                    Body = GetString(el, "body"),
                };
                (prerelease ? testBuilds : releases).Add(build);
            }

            releases = releases.OrderByDescending(b => b.When).Take(2).ToList();
            testBuilds = testBuilds.OrderByDescending(b => b.When).Take(3).ToList();
            return (releases, testBuilds);
        }

        private static string GetString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static bool GetBool(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.True;
    }
}

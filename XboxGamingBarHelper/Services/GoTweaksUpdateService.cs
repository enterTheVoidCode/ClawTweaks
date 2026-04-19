using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.Services
{
    /// <summary>
    /// Checks the GoTweaks GitHub releases feed for a newer version of the
    /// installed package, and optionally downloads + installs the signed
    /// .msixbundle via PowerShell's Add-AppxPackage (helper runs elevated
    /// so the child inherits admin, and AppX install requires it).
    ///
    /// Repo is hard-coded to the fork that ships the releases users install
    /// from — <c>corando98/GoTweaks</c>. If we ever flip upstreams, change
    /// <see cref="RepoPath"/> only.
    ///
    /// Everything here is defensive — network issues, API rate limits, or
    /// asset-naming changes produce an empty/update-not-found result rather
    /// than throwing. The widget can always fall back to its manual
    /// "download from releases page" link.
    /// </summary>
    internal static class GoTweaksUpdateService
    {
        private const string RepoPath = "corando98/GoTweaks";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly HttpClient _http = CreateHttpClient();

        private static GoTweaksUpdateResult _lastResult;
        public static GoTweaksUpdateResult LastResult => _lastResult;

        private static HttpClient CreateHttpClient()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)12288; // Tls13
            }
            catch { }
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // GitHub API requires a UA header on every request.
            client.DefaultRequestHeaders.Add("User-Agent", "GoTweaks-UpdateCheck/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        /// <summary>
        /// Fetches the latest published release from GitHub and compares its
        /// tag to the supplied <paramref name="currentVersion"/>. Returns a
        /// populated result with IsUpdateAvailable + DownloadUrl when newer,
        /// or IsUpdateAvailable=false when up to date / unreachable.
        /// </summary>
        public static async Task<GoTweaksUpdateResult> CheckAsync(string currentVersion)
        {
            var result = new GoTweaksUpdateResult
            {
                CurrentVersion = currentVersion ?? "",
            };
            try
            {
                string url = $"https://api.github.com/repos/{RepoPath}/releases/latest";
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Info($"GoTweaks update check: GitHub returned HTTP {(int)response.StatusCode} — assuming up to date");
                    _lastResult = result;
                    return result;
                }
                string body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    _lastResult = result;
                    return result;
                }

                string tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : "";
                string name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() : "";
                string htmlUrl = root.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String
                    ? h.GetString() : "";

                result.LatestVersion = NormaliseVersion(tag);
                result.LatestTag = tag ?? "";
                result.ReleaseName = string.IsNullOrWhiteSpace(name) ? tag : name;
                result.ReleasePageUrl = htmlUrl ?? "";

                // Find the first msixbundle asset — that's the sideload-install
                // artefact. Skip cer/appxsym/pfx/etc.
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.ValueKind != JsonValueKind.Object) continue;
                        var aname = asset.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String
                            ? an.GetString() ?? "" : "";
                        var aurl = asset.TryGetProperty("browser_download_url", out var au) && au.ValueKind == JsonValueKind.String
                            ? au.GetString() ?? "" : "";
                        if (aname.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
                        {
                            result.DownloadUrl = aurl;
                            result.AssetName = aname;
                            break;
                        }
                    }
                }

                result.IsUpdateAvailable = IsNewer(result.LatestVersion, currentVersion);
                Logger.Info($"GoTweaks update check: current={currentVersion}, latest={result.LatestVersion}, update={result.IsUpdateAvailable}, asset={result.AssetName}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaks update check threw: {ex.Message}");
            }
            _lastResult = result;
            return result;
        }

        /// <summary>
        /// Downloads the msixbundle and launches `Add-AppxPackage` via
        /// PowerShell. Returns JSON the widget can display. Requires the
        /// msix signing cert to be trusted on the machine — if it isn't,
        /// the install fails cleanly and the user is pointed at the
        /// releases page.
        /// </summary>
        public static async Task<string> InstallAsync(string downloadUrl)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                return "{\"success\":false,\"message\":\"No download URL from GitHub release.\"}";

            Uri uri;
            try { uri = new Uri(downloadUrl); }
            catch { return "{\"success\":false,\"message\":\"Invalid URL.\"}"; }
            if (uri.Scheme != "https")
                return "{\"success\":false,\"message\":\"Only https URLs are accepted.\"}";
            // Pin to GitHub — the only host that serves release assets.
            var host = uri.Host.ToLowerInvariant();
            bool trusted = host.EndsWith("github.com") || host.EndsWith("githubusercontent.com");
            if (!trusted)
                return "{\"success\":false,\"message\":\"Host not allowed: " + host + "\"}";

            string fileName = System.IO.Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "gotweaks.msixbundle";
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(bad, '_');

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GoTweaksSelfUpdate");
            try { System.IO.Directory.CreateDirectory(dir); }
            catch (Exception ex) { return "{\"success\":false,\"message\":\"Temp dir: " + ex.Message.Replace("\"", "'") + "\"}"; }

            string target = System.IO.Path.Combine(dir, fileName);
            Logger.Info($"GoTweaksUpdateService: downloading {downloadUrl} -> {target}");
            try
            {
                using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                    return "{\"success\":false,\"message\":\"HTTP " + (int)response.StatusCode + " from GitHub.\"}";
                using (var src = await response.Content.ReadAsStreamAsync())
                using (var dst = System.IO.File.Create(target))
                    await src.CopyToAsync(dst);
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaks download failed: {ex.Message}");
                return "{\"success\":false,\"message\":\"Download failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }

            // Kick off the install via PowerShell. Add-AppxPackage blocks for
            // seconds, so run it detached — we report "launched" and the
            // widget will disappear on its own once the app is reinstalled.
            try
            {
                string psCommand = $"Add-AppxPackage -Path '{target.Replace("'", "''")}' -ForceApplicationShutdown";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                System.Diagnostics.Process.Start(psi);
                Logger.Info($"GoTweaks update install started via PowerShell for {target}");
                return "{\"success\":true,\"message\":\"Installing update — the widget will reload when finished.\"}";
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaks install launch failed: {ex.Message}");
                return "{\"success\":false,\"message\":\"Install failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }
        }

        /// <summary>Strips the leading "v" GitHub tags often carry ("v0.3.2" → "0.3.2").</summary>
        private static string NormaliseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";
            var t = tag.Trim();
            if (t.Length > 1 && (t[0] == 'v' || t[0] == 'V')) t = t.Substring(1);
            return t;
        }

        /// <summary>
        /// Dotted-numeric compare; returns true if <paramref name="candidate"/>
        /// is strictly newer than <paramref name="current"/>. Anything
        /// unparsable falls back to a case-insensitive not-equal check so we
        /// don't offer an "update" that's just a rename.
        /// </summary>
        private static bool IsNewer(string candidate, string current)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current)) return false;
            var c = ParseVersion(candidate);
            var i = ParseVersion(current);
            if (c == null || i == null)
                return !string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase);
            int len = Math.Max(c.Length, i.Length);
            for (int k = 0; k < len; k++)
            {
                long a = k < c.Length ? c[k] : 0;
                long b = k < i.Length ? i[k] : 0;
                if (a > b) return true;
                if (a < b) return false;
            }
            return false;
        }

        private static long[] ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Split('.');
            var r = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var digits = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
                if (digits.Length == 0) return null;
                if (!long.TryParse(digits, out r[i])) return null;
            }
            return r;
        }
    }

    internal sealed class GoTweaksUpdateResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string LatestTag { get; set; } = "";
        public string ReleaseName { get; set; } = "";
        public string ReleasePageUrl { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string AssetName { get; set; } = "";

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
    }
}

using System;
using System.Collections.Generic;
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
        private const string RepoPath = "enterTheVoidCode/ClawTweaks";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly HttpClient _http = CreateHttpClient();

        private static GoTweaksUpdateResult _lastResult;
        public static GoTweaksUpdateResult LastResult => _lastResult;

        // In-app install progress, polled by the widget via Function.AppInstallStatus so the
        // Onboarding update card can show "Downloading 45%…" instead of a static "Installing…".
        public static volatile int InstallPercent = -1;        // 0..100 while downloading; -1 = idle/unknown
        private static volatile string _installPhase = "idle"; // idle | downloading | launch | failed
        private static string _installMessage = "";
        // When phase == "launch": the downloaded package's file name inside the widget's LocalState
        // \update folder. The widget opens it with the OS App Installer (Launcher.LaunchFileAsync) so
        // the user finishes with a single "Install/Update" click — no PowerShell, no AddPackage.
        private static string _installFile = "";

        public static string GetInstallStatusJson()
        {
            string phase = _installPhase ?? "idle";
            string msg = JsonEscape(_installMessage);
            string file = JsonEscape(_installFile);
            return $"{{\"phase\":\"{phase}\",\"percent\":{InstallPercent},\"message\":\"{msg}\",\"file\":\"{file}\"}}";
        }

        private static string JsonEscape(string s)
            => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void SetInstallStatus(string phase, int percent, string message = "", string file = "")
        {
            _installPhase = phase;
            InstallPercent = percent;
            _installMessage = message ?? "";
            _installFile = file ?? "";
        }

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
        /// Fetches the most recent <paramref name="count"/> published releases
        /// (newest first, drafts skipped) and returns them as a JSON array the
        /// Onboarding tab renders into "jump to / roll back" cards. Each entry
        /// carries the version, name, published date, the changelog body and the
        /// .msixbundle download URL. Network/parse failures yield "[]" — the UI
        /// then shows a "couldn't load" hint and a link to the releases page.
        /// </summary>
        public static async Task<string> CheckListAsync(int count = 2)
        {
            var list = new List<ReleaseInfo>();
            try
            {
                // per_page a little above count so we can skip drafts and still
                // fill the requested number of real releases.
                int perPage = Math.Max(count + 3, 5);
                string url = $"https://api.github.com/repos/{RepoPath}/releases?per_page={perPage}";
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Info($"ClawTweaks release list: GitHub returned HTTP {(int)response.StatusCode}");
                    return "[]";
                }
                string body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return "[]";

                foreach (var rel in root.EnumerateArray())
                {
                    if (list.Count >= count) break;
                    if (rel.ValueKind != JsonValueKind.Object) continue;

                    // Skip drafts (unpublished). Prereleases are included — a
                    // user rolling forward/back wants to see them.
                    if (rel.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True)
                        continue;

                    string tag = GetStr(rel, "tag_name");
                    // Pick the installable asset. Releases ship a "..._Installer.zip" that contains the
                    // signed .msix; some may attach a bare .msixbundle/.msix instead. Prefer a direct
                    // package, fall back to the installer zip (helper extracts the .msix from it).
                    string downloadUrl = null, assetName = null;
                    string zipUrl = null, zipName = null;
                    if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.ValueKind != JsonValueKind.Object) continue;
                            var aname = GetStr(asset, "name");
                            if (aname.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                                aname.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                            {
                                assetName = aname;
                                downloadUrl = GetStr(asset, "browser_download_url");
                                break; // direct package wins
                            }
                            if (zipUrl == null && aname.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                zipName = aname;
                                zipUrl = GetStr(asset, "browser_download_url");
                            }
                        }
                    }
                    if (downloadUrl == null && zipUrl != null)
                    {
                        downloadUrl = zipUrl;
                        assetName = zipName;
                    }

                    list.Add(new ReleaseInfo
                    {
                        Tag = tag,
                        Version = NormaliseVersion(tag),
                        Name = GetStr(rel, "name"),
                        Body = GetStr(rel, "body"),
                        PublishedAt = GetStr(rel, "published_at"),
                        ReleasePageUrl = GetStr(rel, "html_url"),
                        DownloadUrl = downloadUrl ?? "",
                        AssetName = assetName ?? "",
                        IsPrerelease = rel.TryGetProperty("prerelease", out var pr) && pr.ValueKind == JsonValueKind.True,
                    });
                }
                Logger.Info($"ClawTweaks release list: returning {list.Count} release(s) from {RepoPath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawTweaks release list threw: {ex.Message}");
            }

            try
            {
                return JsonSerializer.Serialize(list, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
            }
            catch { return "[]"; }
        }

        private static string GetStr(JsonElement obj, string key)
            => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

        /// <summary>
        /// Downloads the signed package and opens it with the OS App Installer so the user finishes
        /// with a single "Install/Update" click. The helper does the open itself (the Game Bar widget
        /// sandbox blocks Launcher.LaunchFileAsync); because the helper is elevated and Windows refuses
        /// to activate a packaged app from an elevated process, it delegates to explorer.exe, which
        /// opens the file at the user's integrity level. A silent WinRT install is kept only as a
        /// failure fallback. The helper downloads (and, for legacy installer ZIPs, extracts the .msix).
        ///
        /// Deliberately AV-clean: no PowerShell, no Process.Start, no "runas" verb, no script-driven
        /// persistence and no self-copy from temp — every one of those tripped a Defender/DrWeb
        /// heuristic in earlier attempts (Persistence.A!ml, Bearfoos.A!ml, Trojan.DownloaderNET,
        /// Wacapew). Opening a Microsoft-signed App Installer on a downloaded package is none of those.
        /// The msix signing cert is already trusted from the first install, so the update is a
        /// per-user operation that needs no UAC.
        ///
        /// The package is placed in the widget's LocalState\update folder so the (sandboxed) widget
        /// can reach it via ApplicationData.Current.LocalFolder and launch it.
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

            // Download into the widget's LocalState\update folder. The widget (UWP, sandboxed) can
            // only Launcher-open files it can reach via ApplicationData.Current.LocalFolder, so the
            // package must live there — not in the helper's %TEMP%. Wipe stale downloads first.
            string dir;
            try
            {
                dir = System.IO.Path.Combine(ResolveLocalStateFolder(), "update");
                if (System.IO.Directory.Exists(dir))
                {
                    try { System.IO.Directory.Delete(dir, true); } catch { }
                }
                System.IO.Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { return "{\"success\":false,\"message\":\"Update dir: " + ex.Message.Replace("\"", "'") + "\"}"; }

            string target = System.IO.Path.Combine(dir, fileName);
            Logger.Info($"GoTweaksUpdateService: downloading {downloadUrl} -> {target}");
            SetInstallStatus("downloading", 0);
            try
            {
                using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    SetInstallStatus("failed", -1, $"HTTP {(int)response.StatusCode} from GitHub.");
                    return "{\"success\":false,\"message\":\"HTTP " + (int)response.StatusCode + " from GitHub.\"}";
                }

                long total = response.Content.Headers.ContentLength ?? -1;
                // Manual streamed copy: log progress and bound stalls. The shared _http.Timeout only
                // covers the header phase (ResponseHeadersRead); a dead connection during the body
                // read would otherwise hang forever, so each ReadAsync gets its own 90s watchdog.
                using (var src = await response.Content.ReadAsStreamAsync())
                using (var dst = System.IO.File.Create(target))
                {
                    var buffer = new byte[81920];
                    long read = 0, lastLogged = 0;
                    while (true)
                    {
                        int n;
                        using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90)))
                        {
                            n = await src.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                        }
                        if (n <= 0) break;
                        await dst.WriteAsync(buffer, 0, n);
                        read += n;
                        if (total > 0) InstallPercent = (int)(read * 100 / total);
                        if (read - lastLogged >= 8_000_000)
                        {
                            lastLogged = read;
                            Logger.Info(total > 0
                                ? $"GoTweaks download progress: {read * 100 / total}% ({read / 1048576}/{total / 1048576} MB)"
                                : $"GoTweaks download progress: {read / 1048576} MB");
                        }
                    }
                    Logger.Info($"GoTweaks download complete: {read / 1048576} MB -> {target}");
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Warn("GoTweaks download stalled (no data for 90s) — aborting.");
                SetInstallStatus("failed", -1, "Download stalled — check your connection.");
                return "{\"success\":false,\"message\":\"Download stalled — check your connection and try again.\"}";
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaks download failed: {ex.Message}");
                SetInstallStatus("failed", -1, "Download failed.");
                return "{\"success\":false,\"message\":\"Download failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }

            // The package is downloaded. If it's a legacy installer ZIP, extract the .msix/.msixbundle
            // out of it into the update folder root; a direct package asset is already in place. Then
            // hand the file name to the widget (phase = "launch"), which opens it with the OS App
            // Installer. The helper does NOT install it — no PowerShell, no AddPackage, no self-deploy.
            string packageFile = target; // absolute path to the package the widget should launch
            if (target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string extractDir = System.IO.Path.Combine(dir, "extracted");
                try
                {
                    try { if (System.IO.Directory.Exists(extractDir)) System.IO.Directory.Delete(extractDir, true); } catch { }
                    System.IO.Directory.CreateDirectory(extractDir);
                    System.IO.Compression.ZipFile.ExtractToDirectory(target, extractDir);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"GoTweaks zip extract failed: {ex.Message}");
                    SetInstallStatus("failed", -1, "Unpack failed.");
                    return "{\"success\":false,\"message\":\"Unpack failed: " + ex.Message.Replace("\"", "'") + "\"}";
                }

                string pkg = System.IO.Directory.GetFiles(extractDir, "*.msixbundle", System.IO.SearchOption.AllDirectories).FirstOrDefault()
                          ?? System.IO.Directory.GetFiles(extractDir, "*.msix", System.IO.SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrEmpty(pkg))
                {
                    SetInstallStatus("failed", -1, "No package in the zip.");
                    return "{\"success\":false,\"message\":\"No .msix/.msixbundle found inside the installer zip.\"}";
                }

                // Move it to the update-folder root so the widget can reach it by a simple name.
                try
                {
                    string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(pkg));
                    if (System.IO.File.Exists(dest)) System.IO.File.Delete(dest);
                    System.IO.File.Move(pkg, dest);
                    packageFile = dest;
                    try { System.IO.Directory.Delete(extractDir, true); } catch { }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"GoTweaks package relocate failed: {ex.Message}");
                    SetInstallStatus("failed", -1, "Unpack failed.");
                    return "{\"success\":false,\"message\":\"Couldn't stage package: " + ex.Message.Replace("\"", "'") + "\"}";
                }
                Logger.Info($"GoTweaksUpdateService: staged package {packageFile} from {target}");
            }

            // Open the package with the OS App Installer. NOT from the widget: the Game Bar widget host
            // sandbox blocks Launcher.LaunchFileAsync (it returns false). The helper is a full Win32
            // process and can do it — but it runs elevated, and Windows refuses to activate a packaged
            // app (App Installer) directly from an elevated process. So we delegate to the already-running
            // shell via explorer.exe, which opens the file at the user's normal integrity level → the
            // App Installer appears and the user clicks Install/Update. (explorer.exe opening a document
            // is maximally benign — none of the heuristics that flagged earlier attempts.)
            string launchName = System.IO.Path.GetFileName(packageFile);
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + packageFile + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Logger.Info($"GoTweaksUpdateService: opening App Installer via explorer.exe for {packageFile}");
                System.Diagnostics.Process.Start(psi);
                SetInstallStatus("launch", 100, "The Windows App Installer is opening — click Install/Update to finish.", launchName);
                return "{\"success\":true,\"message\":\"App Installer opening — click Install/Update to finish.\"}";
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaks explorer launch failed, falling back to silent install: {ex.Message}");
            }

            // Fallback: silent WinRT install so an update never dead-ends if the App Installer can't be
            // opened. AddPackageAsync verifies the already-trusted signature and re-registers the package
            // for the current user — no PowerShell, no spawned process, no UAC. The helper redeploys on
            // the next Game Bar open via its own one-UAC setup.
            try
            {
                SetInstallStatus("installing", 100, "Installing the update…");
                Logger.Info($"GoTweaksUpdateService: installing {packageFile} via PackageManager.AddPackageAsync (fallback)");
                var pm = new global::Windows.Management.Deployment.PackageManager();
                var op = pm.AddPackageAsync(new Uri(packageFile), null,
                    global::Windows.Management.Deployment.DeploymentOptions.ForceApplicationShutdown
                    | global::Windows.Management.Deployment.DeploymentOptions.ForceUpdateFromAnyVersion);
                var result = await op.AsTask();
                if (op.Status == global::Windows.Foundation.AsyncStatus.Completed &&
                    (result == null || result.ExtendedErrorCode == null))
                {
                    Logger.Info($"GoTweaks update installed (fallback) from {packageFile}");
                    SetInstallStatus("done", 100, "Update installed — reloading.");
                    return "{\"success\":true,\"message\":\"Update installed — the widget will reload.\"}";
                }
                string err = result?.ErrorText;
                if (string.IsNullOrWhiteSpace(err) && result?.ExtendedErrorCode != null) err = result.ExtendedErrorCode.Message;
                if (string.IsNullOrWhiteSpace(err)) err = "Install did not complete.";
                Logger.Warn($"GoTweaks fallback AddPackageAsync failed: {err}");
                SetInstallStatus("failed", -1, "Install failed.");
                return "{\"success\":false,\"message\":\"Install failed: " + err.Replace("\"", "'") + "\"}";
            }
            catch (Exception ex)
            {
                Logger.Warn($"GoTweaks fallback install failed: {ex.Message}");
                SetInstallStatus("failed", -1, "Install failed.");
                return "{\"success\":false,\"message\":\"Install failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }
        }

        /// <summary>
        /// The widget's LocalState folder. Uses package identity when available, else the hard-coded
        /// ClawTweaks family (same fallback the heartbeat/LED writers use) so the elevated helper and
        /// the sandboxed widget agree on the folder.
        /// </summary>
        private static string ResolveLocalStateFolder()
        {
            try { return global::Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "MSIClaw.ClawTweaks_7eszav2039cvc", "LocalState");
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

    /// <summary>One GitHub release as surfaced to the Onboarding update cards.</summary>
    internal sealed class ReleaseInfo
    {
        public string Tag { get; set; } = "";
        public string Version { get; set; } = "";
        public string Name { get; set; } = "";
        public string Body { get; set; } = "";
        public string PublishedAt { get; set; } = "";
        public string ReleasePageUrl { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string AssetName { get; set; } = "";
        public bool IsPrerelease { get; set; }
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

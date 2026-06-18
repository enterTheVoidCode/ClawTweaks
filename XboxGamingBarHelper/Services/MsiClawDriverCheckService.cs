using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.Services
{
    /// <summary>
    /// Driver/firmware update check for the MSI Claw (Intel Lunar Lake).
    ///
    /// Mirrors <see cref="LenovoDriverCheckService"/> in shape (machine info +
    /// installed-vs-latest comparison + whitelist download/launch) but uses a
    /// HYBRID data source because MSI/Intel have no clean public catalog:
    ///
    ///   - Installed versions come from WMI / Win32_PnPSignedDriver (shared
    ///     matching logic in <see cref="DriverMatchUtil"/>).
    ///   - MSI firmware/BIOS "latest" comes from a curated JSON manifest hosted
    ///     in our own GitHub repo (claw-drivers.json). Downloads come straight
    ///     from MSI's direct CDN (download.msi.com), not the Cloudflare-gated
    ///     support site.
    ///   - Intel drivers (GPU/Wi-Fi/BT/chipset) are detected (installed version)
    ///     but NOT downloaded in-app — Intel updates frequently and has no clean
    ///     per-device API, so we delegate to the Intel Driver & Support Assistant
    ///     via a deep-link. Precedence rule: Intel always wins on overlapping
    ///     categories; MSI only fills the gaps (firmware, BIOS, MSI-specific).
    ///
    /// Never throws — failure paths return a result with IsMsiClaw=false or an
    /// empty Drivers list so the widget always has something to render plus the
    /// layer-1 deep-link buttons.
    /// </summary>
    internal static class MsiClawDriverCheckService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Curated manifest. Hosted on the long-lived 'master' branch so the URL
        // does not churn with per-release branches. If the repo layout changes,
        // this single constant is the only thing to update.
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/master/manifest/claw-drivers.json";

        // Intel delegates to the Driver & Support Assistant (keeps itself current).
        private const string IntelDsaUrl =
            "https://www.intel.com/content/www/us/en/support/detect.html";

        // MSI Claw 8 AI+ support landing page (layer-1 fallback).
        private const string MsiSupportUrlDefault =
            "https://www.msi.com/Handheld/Claw-8-AI-Plus-A2VMX/support";

        private static MsiDriverUpdateResult _lastResult;
        public static MsiDriverUpdateResult LastResult => _lastResult;

        // Cached hardware-family check used by the pipe dispatcher to route
        // driver-update requests to this service (vs. the Lenovo one). One WMI
        // read, memoised — the manufacturer doesn't change at runtime.
        private static bool? _isClawCached;
        public static bool IsClawHardware()
        {
            if (_isClawCached.HasValue) return _isClawCached.Value;
            bool claw = false;
            try
            {
                using var csp = new ManagementObjectSearcher("SELECT Vendor FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject obj in csp.Get())
                {
                    var vendor = obj["Vendor"]?.ToString() ?? "";
                    claw = vendor.IndexOf("Micro-Star", StringComparison.OrdinalIgnoreCase) >= 0
                        || vendor.IndexOf("MSI", StringComparison.OrdinalIgnoreCase) >= 0;
                    break;
                }
            }
            catch (Exception ex) { Logger.Debug($"IsClawHardware WMI read failed: {ex.Message}"); }
            _isClawCached = claw;
            return claw;
        }

        // Vendor tokens that are not discriminating for the Claw — drop them so
        // fuzzy matching keys on the actual component name. "intel" is kept on
        // purpose (Intel rows are matched by DriverProviderName, and several MSI
        // driver titles legitimately contain "Intel").
        private static readonly HashSet<string> _stopWords =
            DriverMatchUtil.WithStopWords("msi", "claw", "micro", "star", "microstar", "international");

        private static readonly HttpClient _http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; // Tls13
            }
            catch { /* best-effort */ }

            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "ClawTweaks/1.0 (Windows NT 11.0; MSI Claw driver check)");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            return client;
        }

        // ------------------------------------------------------------------

        public static async Task<MsiDriverUpdateResult> CheckAsync()
        {
            var result = new MsiDriverUpdateResult();
            try
            {
                PopulateMachineInfo(result);
                result.MsiSupportUrl = MsiSupportUrlDefault;
                result.IntelDsaUrl = IntelDsaUrl;

                if (!result.IsMsiClaw)
                {
                    result.ErrorMessage = "Not running on an MSI Claw (WMI manufacturer mismatch).";
                    _lastResult = result;
                    return result;
                }

                // Installed snapshot (PnP + uninstall registry), shared logic.
                var index = DriverMatchUtil.BuildInstalledIndex(result.BiosVersion, _stopWords);

                // MSI firmware/BIOS "latest" from the curated manifest (best-effort).
                List<MsiDriverEntry> manifestEntries;
                try
                {
                    manifestEntries = await TryFetchManifestAsync(result.ModelCode);
                    result.LiveFetchSucceeded = manifestEntries.Count > 0;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Claw manifest fetch failed: {ex.Message}");
                    manifestEntries = new List<MsiDriverEntry>();
                    result.LiveFetchSucceeded = false;
                }

                // Controller firmware version from the HID descriptor (bcdDevice) —
                // the same read the LED code uses (ported from HC). Lets the
                // firmware row show the actually-installed version.
                string controllerFw = null;
                try { controllerFw = XboxGamingBarHelper.Devices.MSIClaw.MsiClawLedController.TryGetControllerFirmwareVersion(); }
                catch (Exception ex) { Logger.Debug($"Controller FW read failed: {ex.Message}"); }

                // Intel's official driver catalog (same data the DSA uses) for automatic
                // "latest WHQL" detection per Intel component. Best-effort: null on failure.
                List<IntelDsaCatalogService.CatalogEntry> intelCatalog = null;
                try { intelCatalog = await IntelDsaCatalogService.LoadAsync(); }
                catch (Exception ex) { Logger.Debug($"Intel catalog load failed: {ex.Message}"); }

                result.Drivers = MergeAndApplyPrecedence(manifestEntries, index, controllerFw, intelCatalog);
                Logger.Info($"MsiClawDriverCheck: model={result.ModelCode}, BIOS={result.BiosVersion}, " +
                            $"manifest={manifestEntries.Count}, total rows={result.Drivers.Count}, live={result.LiveFetchSucceeded}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"MsiClawDriverCheckService.CheckAsync threw: {ex.Message}");
                result.ErrorMessage = ex.Message;
            }
            _lastResult = result;
            return result;
        }

        /// <summary>Reads MSI-specific WMI fields: vendor, model code, BIOS version.</summary>
        private static void PopulateMachineInfo(MsiDriverUpdateResult result)
        {
            try
            {
                using var csp = new ManagementObjectSearcher("SELECT Vendor, Name, Version FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject obj in csp.Get())
                {
                    result.Manufacturer = obj["Vendor"]?.ToString()?.Trim() ?? "";
                    result.Model = obj["Name"]?.ToString()?.Trim() ?? "";
                    result.ModelVersion = obj["Version"]?.ToString()?.Trim() ?? "";
                    break;
                }
            }
            catch (Exception ex) { Logger.Debug($"Win32_ComputerSystemProduct read failed: {ex.Message}"); }

            try
            {
                using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, Version FROM Win32_BIOS");
                foreach (ManagementObject obj in bios.Get())
                {
                    result.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim()
                                     ?? obj["Version"]?.ToString()?.Trim()
                                     ?? "";
                    break;
                }
            }
            catch (Exception ex) { Logger.Debug($"Win32_BIOS read failed: {ex.Message}"); }

            // MSI = "Micro-Star International" (Vendor). Model code is "MS-1T52" etc.
            var mfr = result.Manufacturer;
            result.IsMsiClaw = mfr.IndexOf("Micro-Star", StringComparison.OrdinalIgnoreCase) >= 0
                            || mfr.IndexOf("MSI", StringComparison.OrdinalIgnoreCase) >= 0;
            result.ModelCode = ExtractModelCode(result.Model) ?? ExtractModelCode(result.ModelVersion) ?? "";
        }

        private static string ExtractModelCode(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            // MSI board/model codes look like "MS-1T52".
            var match = System.Text.RegularExpressions.Regex.Match(source, @"\b(MS-?\w{3,5})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Fetches the curated manifest and returns the driver entries that apply
        /// to this device (deviceType 40, model matches or unscoped). Returns an
        /// empty list on any failure so the caller falls back to deep-links.
        /// </summary>
        private static async Task<List<MsiDriverEntry>> TryFetchManifestAsync(string modelCode)
        {
            var entries = new List<MsiDriverEntry>();
            string body;
            using (var response = await _http.GetAsync(ManifestUrl))
            {
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Info($"Claw manifest: {ManifestUrl} -> HTTP {(int)response.StatusCode} (using deep-links)");
                    return entries;
                }
                body = await response.Content.ReadAsStringAsync();
            }
            if (string.IsNullOrWhiteSpace(body)) return entries;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return entries;
            if (!root.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
                return entries;

            foreach (var dev in devices.EnumerateArray())
            {
                // deviceType 40 = MSIClaw. Accept matching model, or an unscoped block.
                int deviceType = GetJsonInt(dev, "deviceType");
                if (deviceType != 0 && deviceType != 40) continue;

                if (dev.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    var modelList = models.EnumerateArray()
                        .Where(m => m.ValueKind == JsonValueKind.String)
                        .Select(m => m.GetString())
                        .ToList();
                    if (modelList.Count > 0 && !string.IsNullOrEmpty(modelCode)
                        && !modelList.Any(m => string.Equals(m, modelCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // scoped to other models
                    }
                }

                if (!dev.TryGetProperty("drivers", out var drivers) || drivers.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var d in drivers.EnumerateArray())
                {
                    var e = new MsiDriverEntry
                    {
                        Name = GetJsonString(d, "name") ?? "",
                        Category = GetJsonString(d, "category") ?? "",
                        ProviderScope = (GetJsonString(d, "providerScope") ?? "msi").ToLowerInvariant(),
                        Version = GetJsonString(d, "version") ?? "",
                        ReleaseDate = GetJsonString(d, "releaseDate") ?? "",
                        DownloadUrl = GetJsonString(d, "downloadUrl") ?? "",
                        Severity = GetJsonString(d, "severity") ?? "",
                        Action = (GetJsonString(d, "action") ?? "install").ToLowerInvariant(),
                        DeviceIdMatch = GetJsonString(d, "deviceIdMatch") ?? "",
                        PnpMatchHints = GetJsonStringArray(d, "pnpMatchHints"),
                    };
                    if (!string.IsNullOrWhiteSpace(e.Name)) entries.Add(e);
                }
            }
            Logger.Info($"Claw manifest fetched: {entries.Count} entries for model {modelCode}");
            return entries;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the final list: Intel rows synthesised from the PnP index
        /// (Intel always wins overlapping categories), plus the manifest's MSI
        /// rows for the gaps Intel doesn't cover. MSI manifest entries that fall
        /// into an Intel domain are dropped (code-side guard for the precedence
        /// rule; the manifest should not contain them in the first place).
        /// </summary>
        private static List<MsiDriverEntry> MergeAndApplyPrecedence(List<MsiDriverEntry> manifestEntries, DriverMatchUtil.InstalledIndex index, string controllerFw, List<IntelDsaCatalogService.CatalogEntry> intelCatalog)
        {
            var output = new List<MsiDriverEntry>();

            // 1) Intel domain rows from installed PnP devices, with "latest" from the
            //    Intel DSA catalog (precise hardware-ID match) and the curated manifest
            //    as fallback.
            var intelManifest = manifestEntries.Where(e => e.ProviderScope == "intel").ToList();
            output.AddRange(BuildIntelRows(intelManifest, intelCatalog));

            // 2) MSI manifest rows for gaps Intel doesn't own.
            foreach (var e in manifestEntries)
            {
                if (e.ProviderScope == "intel") continue; // Intel handled dynamically above
                if (ClassifyDomain(e.Category, e.Name) == DriverDomain.Intel)
                {
                    Logger.Info($"Claw merge: dropping manifest entry '{e.Name}' (Intel domain - Intel wins)");
                    continue;
                }

                // Controller/EC firmware: the installed version isn't a PnP driver
                // version — read it from the HID descriptor (bcdDevice) like the LED
                // code does. A HID deviceIdMatch (or a Firmware category) marks the row.
                bool isControllerFw = (e.DeviceIdMatch ?? "").StartsWith("HID", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(e.Category, "Firmware", StringComparison.OrdinalIgnoreCase);
                if (isControllerFw && !string.IsNullOrWhiteSpace(controllerFw))
                {
                    e.InstalledVersion = controllerFw;
                    e.UpdateStatus = DriverMatchUtil.CompareVersions(controllerFw, e.Version);
                    output.Add(e);
                    continue;
                }

                // Everything else: fill installed version + status from the local
                // snapshot. For download entries this drives the up-to-date/update
                // badge; for deep-link entries (BIOS) it still shows what's installed.
                var m = DriverMatchUtil.MatchEntry(e.Name, e.Category, e.Version, index);
                e.InstalledVersion = m.InstalledVersion;
                e.MatchedDeviceName = m.MatchedDeviceName ?? "";
                e.MatchedProvider = m.MatchedProvider ?? "";
                e.MatchScore = m.MatchScore;
                e.UpdateStatus = m.Status;
                output.Add(e);
            }

            return output
                .OrderBy(e => e.Category ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Intel-specific download pages. The page IDs are stable across driver
        // releases (only the file changes), so deep-linking to them always lands
        // on the newest version — no manifest churn. Graphics gets the dedicated
        // Arc/Iris Xe page; the rest fall back to the DSA auto-detect page.
        private const string IntelArcGraphicsUrl =
            "https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html";

        // Intel domains we surface as one row each. RowCategory = display category;
        // CatalogCategory = Intel DSA catalog Components.Category; DeviceClasses = the
        // Win32_PnPSignedDriver DeviceClass values that belong to this domain;
        // Keywords = manifest-fallback matching; DefaultUrl = deep-link when no catalog
        // page is known.
        private static readonly (string Label, string RowCategory, string CatalogCategory, string[] DeviceClasses, string[] Keywords, string DefaultUrl)[] _intelDomains =
        {
            ("Intel Arc Grafik", "Graphics",  "Graphics",  new[] { "DISPLAY" },   new[] { "graphics", "display", "arc", "iris", "gpu" }, IntelArcGraphicsUrl),
            ("Intel Wi-Fi",      "Network",   "Wireless",  new[] { "NET" },       new[] { "wifi", "wireless", "wlan", "killer" },        IntelDsaUrl),
            ("Intel Bluetooth",  "Bluetooth", "Bluetooth", new[] { "BLUETOOTH" }, new[] { "bluetooth" },                                 IntelDsaUrl),
        };

        private sealed class IntelDev
        {
            public string Name;
            public string Version;
            public string HwId;
            public string DeviceClass;
        }

        /// <summary>Installed Intel devices (display/net/bluetooth) with their hardware
        /// ID + driver version, for catalog matching.</summary>
        private static List<IntelDev> QueryIntelDevices()
        {
            var list = new List<IntelDev>();
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT DeviceName, DriverVersion, DriverProviderName, HardWareID, DeviceID, DeviceClass FROM Win32_PnPSignedDriver WHERE DriverVersion IS NOT NULL");
                foreach (ManagementObject o in s.Get())
                {
                    var provider = (o["DriverProviderName"]?.ToString() ?? "");
                    if (provider.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var cls = (o["DeviceClass"]?.ToString() ?? "").Trim();
                    string hw = o["HardWareID"] as string;
                    if (string.IsNullOrWhiteSpace(hw)) hw = o["DeviceID"]?.ToString();
                    list.Add(new IntelDev
                    {
                        Name = (o["DeviceName"]?.ToString() ?? "").Trim(),
                        Version = (o["DriverVersion"]?.ToString() ?? "").Trim(),
                        HwId = (hw ?? "").Trim(),
                        DeviceClass = cls,
                    });
                }
            }
            catch (Exception ex) { Logger.Warn($"QueryIntelDevices failed: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// Builds one row per Intel domain (Graphics/Wi-Fi/Bluetooth). "Latest" comes
        /// from the Intel DSA catalog matched by hardware ID (precise, automatic); if
        /// the catalog is unavailable, falls back to a curated manifest intel entry;
        /// otherwise the version is unknown and the button just opens the Intel page.
        /// </summary>
        private static List<MsiDriverEntry> BuildIntelRows(List<MsiDriverEntry> intelManifest, List<IntelDsaCatalogService.CatalogEntry> catalog)
        {
            var rows = new List<MsiDriverEntry>();
            var devices = QueryIntelDevices();
            if (devices.Count == 0) return rows;

            foreach (var dom in _intelDomains)
            {
                var devs = devices.Where(d => dom.DeviceClasses.Any(c => string.Equals(c, d.DeviceClass, StringComparison.OrdinalIgnoreCase))).ToList();
                if (devs.Count == 0) continue;

                MsiDriverEntry row = null;

                // Preferred: catalog match by hardware ID → exact latest WHQL version.
                foreach (var d in devs)
                {
                    var hit = IntelDsaCatalogService.FindLatest(catalog, dom.CatalogCategory, new[] { d.HwId });
                    if (hit == null) continue;
                    row = new MsiDriverEntry
                    {
                        Name = dom.Label,
                        Category = dom.RowCategory,
                        ProviderScope = "intel",
                        Version = hit.Version,
                        InstalledVersion = d.Version,
                        UpdateStatus = DriverMatchUtil.CompareVersions(d.Version, hit.Version),
                        Action = "deeplink",
                        DownloadUrl = !string.IsNullOrWhiteSpace(hit.PageUrl) ? hit.PageUrl : dom.DefaultUrl,
                        MatchedDeviceName = d.Name,
                        MatchedProvider = "Intel (DSA catalog)",
                    };
                    break;
                }

                // Fallback: curated manifest entry, else unknown.
                if (row == null)
                {
                    var d = devs[0];
                    var man = intelManifest?.FirstOrDefault(e => IntelManifestMatchesDomain(dom.Keywords, e));
                    string latest = man?.Version ?? "";
                    row = new MsiDriverEntry
                    {
                        Name = dom.Label,
                        Category = dom.RowCategory,
                        ProviderScope = "intel",
                        Version = latest,
                        InstalledVersion = d.Version,
                        UpdateStatus = string.IsNullOrWhiteSpace(latest)
                            ? DriverUpdateStatus.Unknown
                            : DriverMatchUtil.CompareVersions(d.Version, latest),
                        Action = "deeplink",
                        DownloadUrl = !string.IsNullOrWhiteSpace(man?.DownloadUrl) ? man.DownloadUrl : dom.DefaultUrl,
                        Severity = man?.Severity ?? "",
                        ReleaseDate = man?.ReleaseDate ?? "",
                        MatchedDeviceName = d.Name,
                        MatchedProvider = "Intel",
                    };
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>True when a manifest intel entry belongs to the given Intel domain
        /// (its category/name/hints share a keyword).</summary>
        private static bool IntelManifestMatchesDomain(string[] keywords, MsiDriverEntry e)
        {
            string hay = ((e.Category ?? "") + " " + (e.Name ?? "") + " " + string.Join(" ", e.PnpMatchHints ?? new List<string>())).ToLowerInvariant();
            return keywords.Any(t => hay.Contains(t));
        }

        private enum DriverDomain { Intel, Msi }

        private static DriverDomain ClassifyDomain(string category, string name)
        {
            string s = ((category ?? "") + " " + (name ?? "")).ToLowerInvariant();
            string[] intelMarkers =
            {
                "graphic", "display", "video", "gpu", "arc", "iris",
                "wifi", "wi-fi", "wlan", "wireless", "network", "ethernet", "lan",
                "bluetooth",
                "chipset", "smbus", "thunderbolt",
            };
            return intelMarkers.Any(m => s.Contains(m)) ? DriverDomain.Intel : DriverDomain.Msi;
        }

        // ------------------------------------------------------------------
        // Download + launch (own whitelist, separate from Lenovo).

        private static bool IsAllowedHost(string host)
        {
            host = (host ?? "").ToLowerInvariant();
            return host == "download.msi.com" || host == "download-2.msi.com";
        }

        public static async Task<string> InstallDriverAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "{\"success\":false,\"message\":\"Missing download URL.\"}";

            Uri uri;
            try { uri = new Uri(url); }
            catch { return "{\"success\":false,\"message\":\"Invalid URL.\"}"; }
            if (uri.Scheme != "https")
                return "{\"success\":false,\"message\":\"Only https URLs are accepted.\"}";
            if (!IsAllowedHost(uri.Host))
                return "{\"success\":false,\"message\":\"Host not allowed: " + uri.Host + "\"}";

            var (path, err) = await DownloadToTempAsync(url);
            if (path == null)
                return "{\"success\":false,\"message\":\"Download failed: " + (err ?? "").Replace("\"", "'") + "\"}";

            string ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext != ".exe" && ext != ".msi")
            {
                // .zip/BIOS payloads are downloaded but never auto-launched.
                string safe = path.Replace("\\", "\\\\").Replace("\"", "'");
                return "{\"success\":true,\"launched\":false,\"path\":\"" + safe + "\",\"message\":\"Downloaded. Open the file manually to install.\"}";
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                return "{\"success\":false,\"message\":\"Launch failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }
            Logger.Info($"MsiClaw InstallDriverAsync: launched {System.IO.Path.GetFileName(path)}");
            return "{\"success\":true,\"launched\":true,\"message\":\"Installer launched.\"}";
        }

        public static async Task<string> BatchInstallAsync(IList<string> urls)
        {
            if (urls == null || urls.Count == 0)
                return "{\"success\":false,\"total\":0,\"launched\":0,\"message\":\"No URLs.\"}";

            int launched = 0, downloaded = 0;
            foreach (var url in urls)
            {
                var (path, _) = await DownloadToTempAsync(url);
                if (string.IsNullOrEmpty(path)) continue;
                downloaded++;
                string ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
                if (ext != ".exe" && ext != ".msi") continue;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
                    launched++;
                    await Task.Delay(800);
                }
                catch (Exception ex) { Logger.Warn($"MsiClaw batch launch failed for {path}: {ex.Message}"); }
            }
            return "{\"success\":" + (launched > 0 ? "true" : "false") +
                   ",\"total\":" + urls.Count +
                   ",\"downloaded\":" + downloaded +
                   ",\"launched\":" + launched +
                   ",\"message\":\"Launched " + launched + " of " + urls.Count + " installers.\"}";
        }

        private static async Task<(string path, string error)> DownloadToTempAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return (null, "Missing URL");
            Uri uri;
            try { uri = new Uri(url); }
            catch { return (null, "Invalid URL"); }
            if (uri.Scheme != "https") return (null, "Not https");
            if (!IsAllowedHost(uri.Host)) return (null, "Host not allowed: " + uri.Host);

            string fileName = System.IO.Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "msi_installer";
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(bad, '_');

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GoTweaksClawInstallers");
            try { System.IO.Directory.CreateDirectory(dir); }
            catch (Exception ex) { return (null, "Temp dir: " + ex.Message); }

            string target = System.IO.Path.Combine(dir, fileName);
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) return (null, "HTTP " + (int)response.StatusCode);
                using (var src = await response.Content.ReadAsStreamAsync())
                using (var dst = System.IO.File.Create(target))
                    await src.CopyToAsync(dst);
                return (target, null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        // ------------------------------------------------------------------

        private static string GetJsonString(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            if (!obj.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            return null;
        }

        private static int GetJsonInt(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object) return 0;
            if (!obj.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
            return 0;
        }

        private static List<string> GetJsonStringArray(JsonElement obj, string name)
        {
            var list = new List<string>();
            if (obj.ValueKind != JsonValueKind.Object) return list;
            if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in v.EnumerateArray())
                if (el.ValueKind == JsonValueKind.String) list.Add(el.GetString());
            return list;
        }
    }

    // ----------------------------------------------------------------------
    // Result/entry types. Field names are chosen so the camelCase JSON keys
    // overlap the Lenovo result the widget already renders (model, biosVersion,
    // machineTypeCode, driverPageUrl, liveFetchSucceeded, drivers[], ...), with
    // a few Claw-specific extras (providerScope, action, intelDsaUrl).

    internal sealed class MsiDriverUpdateResult
    {
        public bool IsMsiClaw { get; set; }
        public string Manufacturer { get; set; } = "";
        public string Model { get; set; } = "";
        public string ModelVersion { get; set; } = "";
        /// <summary>Serialised as machineTypeCode for widget compatibility.</summary>
        [JsonPropertyName("machineTypeCode")]
        public string ModelCode { get; set; } = "";
        public string BiosVersion { get; set; } = "";
        /// <summary>Serialised as driverPageUrl so the existing "Open page" button works.</summary>
        [JsonPropertyName("driverPageUrl")]
        public string MsiSupportUrl { get; set; } = "";
        public string IntelDsaUrl { get; set; } = "";
        public bool LiveFetchSucceeded { get; set; }
        public List<MsiDriverEntry> Drivers { get; set; } = new List<MsiDriverEntry>();
        public string ErrorMessage { get; set; } = "";

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
        }
    }

    internal sealed class MsiDriverEntry
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Version { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Severity { get; set; } = "";
        public string InstalledVersion { get; set; } = "";
        public DriverUpdateStatus UpdateStatus { get; set; } = DriverUpdateStatus.Unknown;
        /// <summary>"msi" | "intel" — drives the per-row action label.</summary>
        public string ProviderScope { get; set; } = "msi";
        /// <summary>"install" (download+launch) | "deeplink" (open URL in browser).</summary>
        public string Action { get; set; } = "install";
        public string MatchedDeviceName { get; set; } = "";
        public string MatchedProvider { get; set; } = "";
        public int MatchScore { get; set; }

        // Manifest-only hints (not serialised to the widget).
        [JsonIgnore] public string DeviceIdMatch { get; set; } = "";
        [JsonIgnore] public List<string> PnpMatchHints { get; set; } = new List<string>();
    }
}

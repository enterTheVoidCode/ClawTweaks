using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        //
        // v2 exists because this constant is compiled into every binary we ever shipped, so the file
        // it points at is live config for ALL versions at once. Until the BaseBoard fix below,
        // ModelCode was always empty, and the manifest's models[] filter only applies to a non-empty
        // ModelCode — old clients therefore render every device block. Adding the Claw 8 EX to
        // claw-drivers.json would have offered Claw 8 AI+ users the EX's BIOS as a recommended
        // update. Old builds cannot resolve the v2 name, so v1 must stay as-is (and keep being
        // maintained) for as long as old installs exist.
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/master/manifest/claw-drivers-v2.json";

        // Intel delegates to the Driver & Support Assistant (keeps itself current).
        private const string IntelDsaUrl =
            "https://www.intel.com/content/www/us/en/support/detect.html";

        // MSI Claw 8 AI+ support landing page (layer-1 fallback).
        private const string MsiSupportUrlDefault =
            "https://www.msi.com/Handheld/Claw-8-AI-Plus-A2VMX/support?sub_product=Claw-8-AI-Plus-A2VM";

        private static MsiDriverUpdateResult _lastResult;
        public static MsiDriverUpdateResult LastResult => _lastResult;

        /// <summary>Drops the cached driver snapshot so the next (even non-forced) check
        /// rebuilds the PnP/registry index. Called after a driver install so a just-updated
        /// driver's new version is picked up instead of the pre-install cache.</summary>
        public static void InvalidateCache() => _lastResult = null;

        // Optional modded Wi-Fi driver (theboss619), parsed from the manifest.
        private static ModdedWifiInfo _moddedWifi;
        internal sealed class ModdedWifiInfo { public string Name = ""; public string Version = ""; public string Url = ""; public string ThreadUrl = ""; }

        /// <summary>User opt-in (persisted) for the third-party modded Wi-Fi driver.</summary>
        public static bool IsModdedWifiEnabled()
        {
            try { if (XboxGamingBarHelper.Settings.LocalSettingsHelper.TryGetValue<bool>("UseModdedWifiDriver", out var v)) return v; }
            catch { }
            return false;
        }

        /// <summary>Time since the last successful driver check (auto or manual), or
        /// null if never checked. Used by the startup probe to throttle itself.</summary>
        public static TimeSpan? TimeSinceLastCheck()
        {
            try
            {
                if (XboxGamingBarHelper.Settings.LocalSettingsHelper.TryGetValue<string>("LastDriverCheckUtc", out var s)
                    && DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    return DateTimeOffset.UtcNow - dt;
            }
            catch { }
            return null;
        }

        // -------- Per-driver mute (scoped to the current latest version) --------

        /// <summary>Stable mute key for a (name, latest-version) pair. MUST match the
        /// widget's composition so a mute set in the UI is recognised here.</summary>
        public static string IgnoreKey(string name, string version)
            => ((name ?? "").Trim() + "|" + (version ?? "").Trim()).ToLowerInvariant();

        public static HashSet<string> ReadIgnoreSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (XboxGamingBarHelper.Settings.LocalSettingsHelper.TryGetValue<string>("DriverIgnoreSet", out var s) && !string.IsNullOrWhiteSpace(s))
                    foreach (var line in s.Split('\n')) { var t = line.Trim(); if (t.Length > 0) set.Add(t); }
            }
            catch { }
            return set;
        }

        /// <summary>Re-applies the current mute set to a result's rows. Cheap — called
        /// on every cached serve so a freshly-toggled mute shows without a live re-check.</summary>
        public static void ApplyMutes(MsiDriverUpdateResult r)
        {
            if (r?.Drivers == null) return;
            var muted = ReadIgnoreSet();
            foreach (var d in r.Drivers)
                d.Ignored = muted.Count > 0 && muted.Contains(IgnoreKey(d.Name, d.Version));
        }

        /// <summary>Adds/removes a mute key (the widget passes the composed key).</summary>
        public static void SetIgnore(string key, bool ignored)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            try
            {
                var set = ReadIgnoreSet();
                if (ignored) set.Add(key.Trim().ToLowerInvariant());
                else set.Remove(key.Trim().ToLowerInvariant());
                XboxGamingBarHelper.Settings.LocalSettingsHelper.SetValue("DriverIgnoreSet", string.Join("\n", set));
                Logger.Info($"Driver mute set updated ({(ignored ? "mute" : "unmute")} '{key}') -> {set.Count} muted");
            }
            catch (Exception ex) { Logger.Warn($"SetIgnore failed: {ex.Message}"); }
        }

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

                // Apply the user's per-driver mutes (scoped to the current latest
                // version): a muted row stays visible but is flagged Ignored so it
                // drops out of the update count + "Update all". A newer version has a
                // different key, so the mute auto-expires when an update ships.
                ApplyMutes(result);

                // Attach any installer files still on disk from earlier downloads so the widget
                // can offer re-run/downgrade/delete per file.
                PopulateCachedInstallers(result);

                Logger.Info($"MsiClawDriverCheck: model={result.ModelCode}, BIOS={result.BiosVersion}, " +
                            $"manifest={manifestEntries.Count}, total rows={result.Drivers.Count}, live={result.LiveFetchSucceeded}");

                // Stamp the last successful check so the startup probe can throttle
                // itself (manual checks count too, resetting the timer).
                try { XboxGamingBarHelper.Settings.LocalSettingsHelper.SetValue("LastDriverCheckUtc", DateTime.UtcNow.ToString("o")); } catch { }
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

            // The board code lives in Win32_BaseBoard.Product and nowhere else. Win32_ComputerSystemProduct
            // reports marketing names — "Claw 8 AI+ A2VM" / "Claw 8 EX AI+ CG3EM Launch Pack" — and Version
            // is "REV:1.0", so extracting from those never matched and ModelCode came out empty on every
            // Claw. That silently disabled the manifest's models[] filter (it only applies when ModelCode
            // is non-empty), so a single device block happened to apply everywhere. With two blocks that
            // would have offered each Claw the other's BIOS.
            try
            {
                using var bb = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
                foreach (ManagementObject obj in bb.Get())
                {
                    result.BoardProduct = obj["Product"]?.ToString()?.Trim() ?? "";
                    break;
                }
            }
            catch (Exception ex) { Logger.Debug($"Win32_BaseBoard read failed: {ex.Message}"); }

            // MSI = "Micro-Star International" (Vendor). Model code is "MS-1T52" (A2VM) / "MS-1T91" (EX).
            var mfr = result.Manufacturer;
            result.IsMsiClaw = mfr.IndexOf("Micro-Star", StringComparison.OrdinalIgnoreCase) >= 0
                            || mfr.IndexOf("MSI", StringComparison.OrdinalIgnoreCase) >= 0;
            result.ModelCode = ExtractModelCode(result.BoardProduct)
                            ?? ExtractModelCode(result.Model)
                            ?? ExtractModelCode(result.ModelVersion)
                            ?? "";
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

            // Optional top-level modded Wi-Fi driver (theboss619). The attachment URL
            // changes on each release and the forum thread can't be auto-scraped, so it
            // lives in the manifest for easy updates without an app rebuild.
            if (root.TryGetProperty("moddedWifi", out var mw) && mw.ValueKind == JsonValueKind.Object)
            {
                _moddedWifi = new ModdedWifiInfo
                {
                    Name = GetJsonString(mw, "name") ?? "Modded Wi-Fi driver",
                    Version = GetJsonString(mw, "version") ?? "",
                    Url = GetJsonString(mw, "url") ?? "",
                    ThreadUrl = GetJsonString(mw, "threadUrl") ?? "",
                };
            }

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
                    if (d.TryGetProperty("installedRegistry", out var reg) && reg.ValueKind == JsonValueKind.Object)
                    {
                        e.RegistryPath = GetJsonString(reg, "path") ?? "";
                        e.RegistryValue = GetJsonString(reg, "value") ?? "";
                    }
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

                // Registry-sourced installed version (e.g. MSI Center M stores it in
                // HKLM\SOFTWARE\WOW6432Node\MSI\MSI Center M : Package_Version). Far more
                // reliable than fuzzy-matching the uninstall registry for MSI software.
                if (!string.IsNullOrWhiteSpace(e.RegistryPath) && !string.IsNullOrWhiteSpace(e.RegistryValue))
                {
                    string regVer = ReadRegistryValue(e.RegistryPath, e.RegistryValue);
                    e.InstalledVersion = regVer ?? "";
                    e.UpdateStatus = string.IsNullOrWhiteSpace(regVer)
                        ? DriverUpdateStatus.NotInstalled
                        : DriverMatchUtil.CompareVersions(regVer, e.Version);
                    output.Add(e);
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
            ("Intel Arc Graphics", "Graphics",  "Graphics",  new[] { "DISPLAY" },   new[] { "graphics", "display", "arc", "iris", "gpu" }, IntelArcGraphicsUrl),
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

                // Modded Wi-Fi (opt-in): replace the stock Wi-Fi row with the modded one,
                // skipping the stock catalog comparison (no false "update"/"unknown").
                if (string.Equals(dom.RowCategory, "Network", StringComparison.OrdinalIgnoreCase)
                    && IsModdedWifiEnabled() && _moddedWifi != null && !string.IsNullOrWhiteSpace(_moddedWifi.Url))
                {
                    rows.Add(new MsiDriverEntry
                    {
                        Name = string.IsNullOrWhiteSpace(_moddedWifi.Name) ? "Wi-Fi (modded driver)" : _moddedWifi.Name,
                        Category = "Network",
                        ProviderScope = "modded",
                        InstalledVersion = devs[0].Version,
                        Version = _moddedWifi.Version,
                        UpdateStatus = string.IsNullOrWhiteSpace(_moddedWifi.Version)
                            ? DriverUpdateStatus.Unknown
                            : DriverMatchUtil.CompareVersions(devs[0].Version, _moddedWifi.Version),
                        Action = "moddedwifi",
                        DownloadUrl = _moddedWifi.Url,
                        MatchedDeviceName = devs[0].Name,
                        MatchedProvider = "theboss619 (modded)",
                    });
                    continue;
                }

                MsiDriverEntry row = null;

                // Preferred: catalog match by hardware ID → exact latest WHQL version.
                foreach (var d in devs)
                {
                    var hit = IntelDsaCatalogService.FindLatest(catalog, dom.CatalogCategory, new[] { d.HwId });
                    if (hit == null) continue;
                    // Use the direct installer URL when the catalog provides one and the
                    // host is whitelisted — the user gets an Install button and the helper
                    // downloads + launches the .exe. Falls back to the Intel download page
                    // (browser "Download" button) otherwise.
                    // Intel's catalog API sometimes puts the direct CDN URL in the Url/PageUrl
                    // field rather than Files[0].url, so we check both.
                    string directUrl = null;
                    if (!string.IsNullOrWhiteSpace(hit.FileUrl)
                        && Uri.TryCreate(hit.FileUrl, UriKind.Absolute, out var fileUri)
                        && IsAllowedHost(fileUri.Host))
                    {
                        directUrl = hit.FileUrl;
                    }
                    else if (!string.IsNullOrWhiteSpace(hit.PageUrl)
                        && Uri.TryCreate(hit.PageUrl, UriKind.Absolute, out var pageUri)
                        && IsAllowedHost(pageUri.Host))
                    {
                        directUrl = hit.PageUrl;
                    }
                    bool hasDirectUrl = directUrl != null;
                    row = new MsiDriverEntry
                    {
                        Name = dom.Label,
                        Category = dom.RowCategory,
                        ProviderScope = "intel",
                        Version = hit.Version,
                        InstalledVersion = d.Version,
                        UpdateStatus = DriverMatchUtil.CompareVersions(d.Version, hit.Version),
                        Action = hasDirectUrl ? "install" : "deeplink",
                        DownloadUrl = hasDirectUrl ? directUrl
                                    : !string.IsNullOrWhiteSpace(hit.PageUrl) ? hit.PageUrl
                                    : dom.DefaultUrl,
                        Highlights = hit.Highlights ?? "",
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
                    string manifestUrl = !string.IsNullOrWhiteSpace(man?.DownloadUrl) ? man.DownloadUrl : dom.DefaultUrl;
                    // If the manifest URL itself is a direct CDN link (downloadmirror.intel.com /
                    // downloads.intel.com), treat it as an auto-install rather than a deep-link.
                    bool manifestIsDirect = !string.IsNullOrWhiteSpace(man?.DownloadUrl)
                        && Uri.TryCreate(man.DownloadUrl, UriKind.Absolute, out var mUri)
                        && IsAllowedHost(mUri.Host);
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
                        Action = manifestIsDirect ? "install" : "deeplink",
                        DownloadUrl = manifestUrl,
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
            return host == "download.msi.com"
                || host == "download-2.msi.com"
                || host == "downloadmirror.intel.com"
                || host == "downloads.intel.com";
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
            {
                // Headless download blocked (Intel AWS WAF) — let the browser do it, then poll
                // the Downloads folder and launch the finished installer.
                if (err == WafBlockedMarker)
                {
                    Logger.Info($"InstallDriverAsync: direct download blocked, falling back to browser for {url}");
                    return await BrowserDownloadAndLaunchAsync(url);
                }
                return "{\"success\":false,\"message\":\"Download failed: " + (err ?? "").Replace("\"", "'") + "\"}";
            }

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

        /// <summary>
        /// Assisted install of the third-party modded Wi-Fi driver (theboss619):
        /// downloads the archive from the manifest URL, detects its format, renames it
        /// with the correct extension (ZIP or 7z), opens the containing folder in Explorer
        /// and tells the user to extract + run Setup.bat. We deliberately do NOT auto-run
        /// any script. Supports both ZIP and 7-Zip archives (theboss619 ships as .7z).
        /// </summary>
        public static async Task<string> InstallModdedWifiAsync()
        {
            var mw = _moddedWifi;
            if (mw == null || string.IsNullOrWhiteSpace(mw.Url))
                return "{\"success\":false,\"message\":\"No modded Wi-Fi driver URL configured in the manifest.\"}";

            Uri uri;
            try { uri = new Uri(mw.Url); }
            catch { return "{\"success\":false,\"message\":\"Invalid modded Wi-Fi URL.\"}"; }
            string host = uri.Host.ToLowerInvariant();
            if (uri.Scheme != "https" || !(host == "www.techpowerup.com" || host == "techpowerup.com"))
                return "{\"success\":false,\"message\":\"Host not allowed: " + uri.Host + "\"}";

            Logger.Info($"Modded Wi-Fi: downloading from {mw.Url}");
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClawModdedWifi");
            try
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
                System.IO.Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { return "{\"success\":false,\"message\":\"Temp dir: " + ex.Message.Replace("\"", "'") + "\"}"; }

            // Download to a temp name first; we'll rename once we know the format.
            string rawPath = System.IO.Path.Combine(dir, "modded-wifi.tmp");
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, mw.Url))
                {
                    // TechPowerUp serves the attachment to a browser-like request.
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    req.Headers.TryAddWithoutValidation("Referer", "https://www.techpowerup.com/forums/");
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (!resp.IsSuccessStatusCode)
                        return "{\"success\":false,\"message\":\"Download HTTP " + (int)resp.StatusCode + "\"}";
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = System.IO.File.Create(rawPath))
                        await src.CopyToAsync(dst);
                }
            }
            catch (Exception ex) { return "{\"success\":false,\"message\":\"Download failed: " + ex.Message.Replace("\"", "'") + "\"}"; }

            // Detect archive format by magic bytes: PK = ZIP (50 4B), 7z = 7-Zip (37 7A BC AF).
            string ext = ".zip";
            try
            {
                byte[] magic = new byte[4];
                using (var fs = System.IO.File.OpenRead(rawPath))
                    fs.Read(magic, 0, 4);
                if (magic[0] == 0x37 && magic[1] == 0x7A && magic[2] == 0xBC && magic[3] == 0xAF)
                    ext = ".7z";
            }
            catch { /* keep .zip as fallback */ }

            string archivePath = System.IO.Path.Combine(dir, "modded-wifi" + ext);
            try { System.IO.File.Move(rawPath, archivePath); }
            catch (Exception ex) { return "{\"success\":false,\"message\":\"Rename failed: " + ex.Message.Replace("\"", "'") + "\"}"; }

            Logger.Info($"Modded Wi-Fi: downloaded {ext} archive ({new System.IO.FileInfo(archivePath).Length / 1024} KB) to {archivePath}");

            // Extract using Shell.Application (Windows built-in — handles both ZIP and 7z
            // natively on Windows 11 without any third-party library).
            string openDir = dir;
            string extractDir = System.IO.Path.Combine(dir, "extracted");
            try
            {
                System.IO.Directory.CreateDirectory(extractDir);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic archive = shell.NameSpace(archivePath);
                dynamic dest = shell.NameSpace(extractDir);
                // 4 = no progress dialog, 16 = respond "Yes to all" for overwrites
                dest.CopyHere(archive.Items(), 4 | 16);
                // CopyHere is fire-and-forget; wait for at least one file to appear (up to 30 s).
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline && System.IO.Directory.GetFiles(extractDir, "*", System.IO.SearchOption.AllDirectories).Length == 0)
                    System.Threading.Thread.Sleep(200);
                var setup = System.IO.Directory.GetFiles(extractDir, "Setup.bat", System.IO.SearchOption.AllDirectories).FirstOrDefault();
                openDir = !string.IsNullOrEmpty(setup) ? System.IO.Path.GetDirectoryName(setup) : extractDir;
                Logger.Info($"Modded Wi-Fi: extracted {ext} archive to {openDir}");

                // Auto-launch Setup.bat via explorer.exe so it runs at the user's medium
                // integrity (not our elevated token). The bat's own UAC request then shows
                // to the user as a normal confirmation prompt.
                if (!string.IsNullOrEmpty(setup))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "\"" + setup + "\"",
                            UseShellExecute = false,
                        });
                        Logger.Info($"Modded Wi-Fi: launched Setup.bat via explorer (de-elevated)");
                        string safeBat = openDir.Replace("\\", "\\\\").Replace("\"", "'");
                        return "{\"success\":true,\"message\":\"Setup.bat started — confirm the UAC prompt to install the certificate and driver.\",\"path\":\"" + safeBat + "\"}";
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Modded Wi-Fi: Setup.bat launch failed ({ex.Message}), opening folder instead");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Modded Wi-Fi: shell extraction failed ({ex.Message}), opening archive folder instead");
                openDir = dir;
            }

            // Fallback: open the folder so the user can run Setup.bat manually.
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + openDir + "\"",
                    UseShellExecute = false,
                });
            }
            catch (Exception ex) { Logger.Warn($"Modded Wi-Fi: open folder failed: {ex.Message}"); }

            string msg = "Downloaded and extracted. Run Setup.bat in the opened folder (installs the theboss619 certificate + driver).";
            Logger.Info($"Modded Wi-Fi: done — opened {openDir}");
            string safe = openDir.Replace("\\", "\\\\").Replace("\"", "'");
            return "{\"success\":true,\"message\":\"" + msg + "\",\"path\":\"" + safe + "\"}";
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
                Logger.Info($"DownloadToTempAsync: starting GET {url}");
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                // AWS WAF / CloudFront bot challenge: Intel's CDN (downloadmirror.intel.com)
                // answers a headless client with HTTP 202 + 'x-amzn-waf-action: challenge' and
                // an empty text/html body instead of the binary. 202 IS a 2xx, so the old
                // IsSuccessStatusCode check passed and we streamed a 0-byte "exe" that then
                // failed to launch. A bare HttpClient cannot solve the JS challenge — signal
                // the caller to fall back to a real browser download.
                if (response.Headers.TryGetValues("x-amzn-waf-action", out var wafVals))
                {
                    Logger.Warn($"DownloadToTempAsync: AWS WAF challenge ({string.Join(",", wafVals)}) for {url} → browser fallback");
                    return (null, WafBlockedMarker);
                }
                if ((int)response.StatusCode != 200)
                {
                    Logger.Warn($"DownloadToTempAsync: HTTP {(int)response.StatusCode} (non-200) for {url}");
                    // 202/204/3xx from a CDN means "not the file" — let the caller try the browser.
                    return (null, ((int)response.StatusCode == 202) ? WafBlockedMarker : "HTTP " + (int)response.StatusCode);
                }
                // A real installer is never text/html. Reject WAF/landing pages up front.
                string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                    mediaType.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.Warn($"DownloadToTempAsync: non-binary Content-Type '{mediaType}' for {url} → browser fallback");
                    return (null, WafBlockedMarker);
                }

                Logger.Info($"DownloadToTempAsync: headers OK (CT='{mediaType}'), streaming to {target}");
                using (var src = await response.Content.ReadAsStreamAsync())
                using (var dst = System.IO.File.Create(target))
                    await src.CopyToAsync(dst);

                // Never hand a bogus payload to the launcher: validate EXE/MSI magic bytes.
                // This is what bit us — a 0-byte challenge page saved as gfx_*.exe.
                string ext = (System.IO.Path.GetExtension(target) ?? "").ToLowerInvariant();
                if ((ext == ".exe" || ext == ".msi") && !LooksLikeWindowsBinary(target))
                {
                    long len = 0; try { len = new System.IO.FileInfo(target).Length; } catch { }
                    Logger.Warn($"DownloadToTempAsync: payload is not a valid PE/MSI ({len} bytes) for {url} → browser fallback");
                    try { System.IO.File.Delete(target); } catch { }
                    return (null, WafBlockedMarker);
                }

                long size = 0; try { size = new System.IO.FileInfo(target).Length; } catch { }
                Logger.Info($"DownloadToTempAsync: download complete → {target} ({size} bytes)");
                return (target, null);
            }
            catch (Exception ex)
            {
                Logger.Warn($"DownloadToTempAsync: exception for {url}: {ex.GetType().Name}: {ex.Message}");
                return (null, ex.Message);
            }
        }

        /// <summary>Sentinel returned by DownloadToTempAsync when a headless download is blocked
        /// (AWS WAF challenge / non-binary payload). Tells InstallDriverAsync to open the URL in
        /// the user's browser instead, where the challenge auto-solves.</summary>
        internal const string WafBlockedMarker = "__WAF_BLOCKED__";

        // ------------------------------------------------------------------
        // Cached-installer management: list / delete / re-launch installer files
        // left on disk from earlier downloads so the widget can free space or
        // re-run (downgrade to) an older version. Files live in exactly two
        // folders — the helper's temp download dir and the user's Downloads.

        private static string TempInstallerDir =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GoTweaksClawInstallers");

        private static string DownloadsDir =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        private static readonly string[] ManagedInstallerExts = { ".exe", ".msi", ".zip" };

        /// <summary>Derives a conservative match stem from a driver's download URL filename by
        /// stripping trailing version/build tokens. e.g. "gfx_win_101.8860.exe" → "gfx_win".
        /// Returns (stem, ext); stem is null when nothing usable remains (→ list nothing).</summary>
        private static (string stem, string ext) DeriveInstallerStem(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return (null, null);
            string fileName;
            try { fileName = System.IO.Path.GetFileName(new Uri(url).LocalPath); }
            catch { return (null, null); }
            if (string.IsNullOrWhiteSpace(fileName)) return (null, null);

            string ext = (System.IO.Path.GetExtension(fileName) ?? "").ToLowerInvariant();
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);

            // Peel version/build groups off the end (e.g. "_101.8860", "-1.2.3", " (2)").
            string prev;
            do
            {
                prev = stem;
                stem = Regex.Replace(stem, @"[ _\-.\(]*\d[\d._\-\) ]*$", "");
            } while (stem != prev && stem.Length > 0);

            stem = stem.Trim(' ', '_', '-', '.');
            if (stem.Length < 3) return (null, null); // too generic → don't match anything
            return (stem, ext);
        }

        /// <summary>Strips a browser "name (1)" dedup suffix so re-downloads match the same stem.</summary>
        private static string StripBrowserDedupSuffix(string nameNoExt) =>
            Regex.Replace(nameNoExt ?? "", @"\s*\(\d+\)$", "");

        /// <summary>Lists installer files on disk that belong to the given driver (by download URL).
        /// Scans the temp download dir + Downloads for same-extension files whose (dedup-stripped)
        /// name begins with the derived stem. Never throws.</summary>
        internal static List<CachedInstallerInfo> ListCachedInstallers(string url)
        {
            var list = new List<CachedInstallerInfo>();
            var (stem, ext) = DeriveInstallerStem(url);
            if (stem == null || string.IsNullOrEmpty(ext)) return list;
            if (Array.IndexOf(ManagedInstallerExts, ext) < 0) return list;

            foreach (var (dir, label) in new[] { (TempInstallerDir, "Temp"), (DownloadsDir, "Downloads") })
            {
                try
                {
                    if (!System.IO.Directory.Exists(dir)) continue;
                    foreach (var path in System.IO.Directory.EnumerateFiles(dir, "*" + ext))
                    {
                        string nameNoExt = StripBrowserDedupSuffix(System.IO.Path.GetFileNameWithoutExtension(path));
                        if (!nameNoExt.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;

                        System.IO.FileInfo fi;
                        try { fi = new System.IO.FileInfo(path); } catch { continue; }
                        list.Add(new CachedInstallerInfo
                        {
                            FullPath = path,
                            FileName = System.IO.Path.GetFileName(path),
                            Version = ExtractVersionFromName(System.IO.Path.GetFileNameWithoutExtension(path)),
                            SizeBytes = fi.Exists ? fi.Length : 0,
                            LastWriteUtc = fi.Exists ? fi.LastWriteTimeUtc.ToString("o") : "",
                            Folder = label,
                        });
                    }
                }
                catch (Exception ex) { Logger.Debug($"ListCachedInstallers scan '{dir}' failed: {ex.Message}"); }
            }

            // Newest first.
            list.Sort((a, b) => string.CompareOrdinal(b.LastWriteUtc, a.LastWriteUtc));
            return list;
        }

        /// <summary>Best-effort version token from an installer filename (first dotted digit group).</summary>
        private static string ExtractVersionFromName(string nameNoExt)
        {
            if (string.IsNullOrWhiteSpace(nameNoExt)) return "";
            var m = Regex.Match(nameNoExt, @"\d+(?:[._]\d+){1,3}");
            return m.Success ? m.Value.Replace('_', '.') : "";
        }

        /// <summary>True only when the path is a real file directly inside one of the two managed
        /// folders and has a managed installer extension. Blocks deleting/launching anything else.</summary>
        internal static bool IsManagedInstallerPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string full;
            try { full = System.IO.Path.GetFullPath(path); }
            catch { return false; }

            string ext = (System.IO.Path.GetExtension(full) ?? "").ToLowerInvariant();
            if (Array.IndexOf(ManagedInstallerExts, ext) < 0) return false;

            string parent;
            try { parent = System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(full) ?? ""); }
            catch { return false; }

            bool inManagedDir =
                string.Equals(parent, System.IO.Path.GetFullPath(TempInstallerDir), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parent, System.IO.Path.GetFullPath(DownloadsDir), StringComparison.OrdinalIgnoreCase);
            if (!inManagedDir) return false;

            return System.IO.File.Exists(full);
        }

        /// <summary>Deletes a cached installer after the safety check. Returns a result JSON.</summary>
        internal static string DeleteCachedInstaller(string path)
        {
            if (!IsManagedInstallerPath(path))
            {
                Logger.Warn($"DeleteCachedInstaller: refused path outside managed folders: {path}");
                return "{\"success\":false,\"message\":\"Path is not a managed installer.\"}";
            }
            try
            {
                System.IO.File.Delete(path);
                Logger.Info($"DeleteCachedInstaller: deleted {System.IO.Path.GetFileName(path)}");
                return "{\"success\":true,\"message\":\"Deleted.\"}";
            }
            catch (Exception ex)
            {
                return "{\"success\":false,\"message\":\"Delete failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }
        }

        /// <summary>Re-launches a cached .exe/.msi installer after the safety check (reuses the
        /// same ShellExecute launch as InstallDriverAsync). .zip is not auto-launched.</summary>
        internal static string LaunchCachedInstaller(string path)
        {
            if (!IsManagedInstallerPath(path))
            {
                Logger.Warn($"LaunchCachedInstaller: refused path outside managed folders: {path}");
                return "{\"success\":false,\"message\":\"Path is not a managed installer.\"}";
            }
            string ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext != ".exe" && ext != ".msi")
                return "{\"success\":true,\"launched\":false,\"message\":\"Open the file manually to install.\"}";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                Logger.Info($"LaunchCachedInstaller: launched {System.IO.Path.GetFileName(path)}");
                return "{\"success\":true,\"launched\":true,\"message\":\"Installer launched.\"}";
            }
            catch (Exception ex)
            {
                return "{\"success\":false,\"message\":\"Launch failed: " + ex.Message.Replace("\"", "'") + "\"}";
            }
        }

        /// <summary>JSON array of cached installers for a driver — used by the ManageDriverInstaller
        /// pipe handler (list + refresh-after-delete).</summary>
        internal static string CachedInstallersJson(string url)
        {
            return JsonSerializer.Serialize(ListCachedInstallers(url), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }

        /// <summary>Fills each driver row's CachedInstallers from disk. Best-effort; never throws.</summary>
        private static void PopulateCachedInstallers(MsiDriverUpdateResult result)
        {
            if (result?.Drivers == null) return;
            foreach (var d in result.Drivers)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(d.DownloadUrl))
                        d.CachedInstallers = ListCachedInstallers(d.DownloadUrl);
                }
                catch (Exception ex) { Logger.Debug($"PopulateCachedInstallers('{d.Name}') failed: {ex.Message}"); }
            }
        }

        /// <summary>Cheap sanity check that a downloaded file is a real Windows installer:
        /// 'MZ' for PE/EXE, or the OLE compound-doc magic (D0 CF 11 E0) for MSI. Anything
        /// smaller than 4 KB (e.g. a challenge/landing page) is rejected outright.</summary>
        private static bool LooksLikeWindowsBinary(string path)
        {
            try
            {
                using var fs = System.IO.File.OpenRead(path);
                if (fs.Length < 4096) return false;
                var b = new byte[8];
                if (fs.Read(b, 0, 8) < 4) return false;
                if (b[0] == 0x4D && b[1] == 0x5A) return true;                                  // MZ
                if (b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0) return true;   // MSI/OLE
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Browser fallback for WAF-protected CDN downloads (Intel). Opens the file URL in the
        /// user's default browser (de-elevated via explorer, so the browser's own session solves
        /// the AWS WAF challenge), then polls the Downloads folder until the installer finishes
        /// downloading and launches it. Returns a result JSON in the same shape as InstallDriverAsync.
        /// </summary>
        private static async Task<string> BrowserDownloadAndLaunchAsync(string url)
        {
            string fileName = null;
            try { fileName = System.IO.Path.GetFileName(new Uri(url).LocalPath); } catch { }
            string baseName = string.IsNullOrEmpty(fileName)
                ? null : System.IO.Path.GetFileNameWithoutExtension(fileName);

            string downloads = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // Snapshot existing files so we only pick up the NEW download (ignores stale copies
            // from a previous attempt — the browser saves those as "name (1).exe").
            var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (System.IO.Directory.Exists(downloads))
                    foreach (var f in System.IO.Directory.GetFiles(downloads)) before.Add(f);
            }
            catch { }

            // Open the URL in the default browser at the user's integrity level (explorer.exe
            // de-elevates). The browser runs the WAF JS challenge and downloads to Downloads.
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + url + "\"",
                    UseShellExecute = false,
                });
                Logger.Info($"BrowserDownload: opened {url} in default browser; polling {downloads}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"BrowserDownload: failed to open browser: {ex.Message}");
                return "{\"success\":false,\"message\":\"Could not open the browser: " + ex.Message.Replace("\"", "'") + "\"}";
            }

            // Poll for the completed installer. Driver EXEs are large (200 MB–1 GB) → allow time.
            string found = await PollForDownloadAsync(downloads, before, baseName, TimeSpan.FromMinutes(5));
            if (found == null)
            {
                Logger.Info("BrowserDownload: timed out waiting for the download; asking the user to run it manually");
                return "{\"success\":true,\"launched\":false,\"message\":\"Opened the download in your browser. Run the installer from your Downloads folder once it finishes.\"}";
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = found, UseShellExecute = true });
                Logger.Info($"BrowserDownload: launched {System.IO.Path.GetFileName(found)}");
            }
            catch (Exception ex)
            {
                string safe = found.Replace("\\", "\\\\").Replace("\"", "'");
                Logger.Warn($"BrowserDownload: launch failed for {found}: {ex.Message}");
                return "{\"success\":true,\"launched\":false,\"path\":\"" + safe + "\",\"message\":\"Downloaded — open it from your Downloads folder to install.\"}";
            }
            return "{\"success\":true,\"launched\":true,\"message\":\"Downloaded via browser and launched the installer.\"}";
        }

        /// <summary>Watches the Downloads folder for a NEW completed .exe/.msi (one not present in
        /// <paramref name="before"/>), preferring a name matching <paramref name="baseName"/>. Waits
        /// out in-progress partials (.crdownload/.tmp/.part) and confirms the file size is stable
        /// before returning it. Returns null on timeout.</summary>
        private static async Task<string> PollForDownloadAsync(
            string dir, HashSet<string> before, string baseName, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000);

                string[] files;
                try { files = System.IO.Directory.GetFiles(dir); }
                catch { continue; }

                var candidates = files.Where(f => !before.Contains(f)).ToList();
                if (candidates.Count == 0) continue;

                // A partial sibling (.crdownload/.part/.tmp) means the browser is still writing.
                bool partialInProgress = candidates.Any(f =>
                {
                    var e = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    return e == ".crdownload" || e == ".tmp" || e == ".part" || e == ".partial" || e == ".download";
                });
                if (partialInProgress) continue;

                var done = candidates.Where(f =>
                {
                    var e = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    return e == ".exe" || e == ".msi";
                }).ToList();
                if (done.Count == 0) continue;

                string pick = null;
                if (!string.IsNullOrEmpty(baseName))
                    pick = done.FirstOrDefault(f =>
                        System.IO.Path.GetFileName(f).IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (pick == null)
                {
                    try { pick = done.OrderByDescending(f => new System.IO.FileInfo(f).LastWriteTimeUtc).First(); }
                    catch { pick = done[0]; }
                }

                if (await IsFileStableAsync(pick))
                {
                    Logger.Info($"BrowserDownload: detected completed download {System.IO.Path.GetFileName(pick)}");
                    return pick;
                }
            }
            return null;
        }

        /// <summary>True once the file's size stops changing and it is no longer write-locked.</summary>
        private static async Task<bool> IsFileStableAsync(string path)
        {
            try
            {
                long s1 = new System.IO.FileInfo(path).Length;
                if (s1 <= 0) return false;
                await Task.Delay(1200);
                long s2 = new System.IO.FileInfo(path).Length;
                if (s1 != s2) return false;
                using (System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read)) { }
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------

        private static string ReadRegistryValue(string path, string valueName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                int slash = path.IndexOf('\\');
                if (slash <= 0) return null;
                string hive = path.Substring(0, slash).ToUpperInvariant();
                string sub = path.Substring(slash + 1);
                Microsoft.Win32.RegistryKey root;
                switch (hive)
                {
                    case "HKLM": case "HKEY_LOCAL_MACHINE": root = Microsoft.Win32.Registry.LocalMachine; break;
                    case "HKCU": case "HKEY_CURRENT_USER": root = Microsoft.Win32.Registry.CurrentUser; break;
                    default: return null;
                }
                using (var k = root.OpenSubKey(sub))
                    return k?.GetValue(valueName)?.ToString();
            }
            catch (Exception ex) { Logger.Debug($"ReadRegistryValue failed ({path}: {valueName}): {ex.Message}"); return null; }
        }

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
        /// <summary>Win32_BaseBoard.Product — the only place the MS-xxxx board code appears.</summary>
        public string BoardProduct { get; set; } = "";
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
        /// <summary>Game On Driver highlights from the Intel DSA catalog (Graphics rows only).</summary>
        public string Highlights { get; set; } = "";
        /// <summary>"msi" | "intel" — drives the per-row action label.</summary>
        public string ProviderScope { get; set; } = "msi";
        /// <summary>"install" (download+launch) | "deeplink" (open URL in browser).</summary>
        public string Action { get; set; } = "install";
        public string MatchedDeviceName { get; set; } = "";
        public string MatchedProvider { get; set; } = "";
        public int MatchScore { get; set; }
        /// <summary>True when the user muted this update for its current latest version
        /// (excluded from the update count + "Update all"; reappears when a newer version ships).</summary>
        public bool Ignored { get; set; }
        /// <summary>Installer files already downloaded for this driver that are still on disk
        /// (in %TEMP%\GoTweaksClawInstallers or the user's Downloads folder). Lets the widget
        /// offer re-run/downgrade/delete per file. Serialised as "cachedInstallers".</summary>
        public List<CachedInstallerInfo> CachedInstallers { get; set; } = new List<CachedInstallerInfo>();

        // Manifest-only hints (not serialised to the widget).
        [JsonIgnore] public string DeviceIdMatch { get; set; } = "";
        [JsonIgnore] public List<string> PnpMatchHints { get; set; } = new List<string>();
        // Optional: read the installed version from a registry value instead of the
        // uninstall-registry fuzzy match (e.g. MSI Center M stores it under
        // HKLM\SOFTWARE\WOW6432Node\MSI\MSI Center M : Package_Version).
        [JsonIgnore] public string RegistryPath { get; set; } = "";
        [JsonIgnore] public string RegistryValue { get; set; } = "";
    }

    /// <summary>One installer file left on disk from a previous download of a driver.
    /// Serialised camelCase (fullPath, fileName, version, sizeBytes, lastWriteUtc, folder).</summary>
    internal sealed class CachedInstallerInfo
    {
        public string FullPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Version { get; set; } = "";
        public long SizeBytes { get; set; }
        public string LastWriteUtc { get; set; } = "";
        /// <summary>"Temp" or "Downloads" — where the file lives.</summary>
        public string Folder { get; set; } = "";
    }
}

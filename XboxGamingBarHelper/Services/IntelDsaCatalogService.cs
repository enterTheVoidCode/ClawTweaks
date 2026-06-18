using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.Services
{
    /// <summary>
    /// Reads Intel's official driver catalog — the same data the Intel Driver &amp;
    /// Support Assistant (DSA) uses — to determine the LATEST available version per
    /// component, matched precisely by hardware ID. This is what powers automatic
    /// "new WHQL driver available" notifications for the Intel rows.
    ///
    /// Source (best-effort, in priority order):
    ///   1. The DSA's local cache zip if installed: %ProgramData%\Intel\DSA\Data\data-*.zip
    ///      (offline, instant — present whenever the user has the DSA).
    ///   2. Live fetch of https://dsadata.intel.com/data/&lt;locale&gt; (a ~150 KB zip).
    ///      This is Intel's data CDN for DSA clients — NOT the Cloudflare-gated
    ///      www.intel.com site — so a plain GET works (verified HTTP 200, application/zip).
    ///
    /// The zip contains software-configurations.json: an array of driver packages,
    /// each with Components[]{ Category, Version, DetectionValues[] (PCI/USB hardware
    /// IDs) }, a top-level Url (download page) and Files[]{ url } (direct installer).
    /// We match a Component's DetectionValues against the installed device's hardware
    /// ID and pick the highest version in the requested category.
    ///
    /// Everything is best-effort: any failure returns no result and the caller falls
    /// back to the curated manifest / "open Intel page" deep-link.
    /// </summary>
    internal static class IntelDsaCatalogService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string DataCdnUrl = "https://dsadata.intel.com/data/en";
        private static readonly string[] LocalCacheZips =
        {
            @"C:\ProgramData\Intel\DSA\Data\data-en.zip",
            @"C:\ProgramData\Intel\DSA\Data\data-de.zip",
        };

        private static readonly HttpClient _http = CreateHttpClient();
        private static HttpClient CreateHttpClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288;
            }
            catch { }
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.Add("User-Agent", "ClawTweaks/1.0 (Intel DSA catalog)");
            return c;
        }

        internal sealed class CatalogEntry
        {
            public string Category;          // "Graphics" | "Wireless" | "Bluetooth" | "LAN" | "BIOS" | ...
            public string Version;           // e.g. "32.0.101.8826"
            public string PageUrl;           // Intel download page (placeholder resolved)
            public string FileUrl;           // direct downloadmirror.intel.com installer (may be empty)
            public List<string> DetectionValues = new List<string>(); // VEN_xxxx&DEV_xxxx[&SUBSYS_...]
        }

        // Parsed catalog cached for the helper's lifetime (the data changes ~weekly;
        // one load per session is plenty and keeps the widget snappy).
        private static List<CatalogEntry> _cache;
        private static bool _loaded;

        public static async Task<List<CatalogEntry>> LoadAsync()
        {
            if (_loaded) return _cache;
            _loaded = true;
            try
            {
                byte[] zipBytes = TryReadLocalCache();
                if (zipBytes == null)
                {
                    Logger.Info("Intel catalog: no local DSA cache, fetching from dsadata.intel.com");
                    using var resp = await _http.GetAsync(DataCdnUrl);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Logger.Info($"Intel catalog: CDN returned HTTP {(int)resp.StatusCode}");
                        return _cache;
                    }
                    zipBytes = await resp.Content.ReadAsByteArrayAsync();
                }
                _cache = ParseCatalog(zipBytes);
                Logger.Info($"Intel catalog loaded: {_cache?.Count ?? 0} component entries");
            }
            catch (Exception ex)
            {
                Logger.Warn($"IntelDsaCatalogService.LoadAsync failed: {ex.Message}");
            }
            return _cache;
        }

        private static byte[] TryReadLocalCache()
        {
            foreach (var path in LocalCacheZips)
            {
                try { if (File.Exists(path)) return File.ReadAllBytes(path); }
                catch (Exception ex) { Logger.Debug($"Intel catalog local cache read failed ({path}): {ex.Message}"); }
            }
            return null;
        }

        private static List<CatalogEntry> ParseCatalog(byte[] zipBytes)
        {
            var entries = new List<CatalogEntry>();
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var swEntry = zip.GetEntry("software-configurations.json");
            if (swEntry == null) return entries;

            string json;
            using (var sr = new StreamReader(swEntry.Open()))
                json = sr.ReadToEnd();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return entries;

            foreach (var pkg in doc.RootElement.EnumerateArray())
            {
                string pageUrl = GetStr(pkg, "Url");
                if (!string.IsNullOrEmpty(pageUrl))
                    pageUrl = pageUrl.Replace("/{packageId}", "").Replace("{packageId}/", "").Replace("{packageId}", "");

                string fileUrl = "";
                if (pkg.TryGetProperty("Files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var u = GetStr(f, "url");
                        if (!string.IsNullOrEmpty(u)) { fileUrl = u; break; }
                    }
                }

                if (!pkg.TryGetProperty("Components", out var comps) || comps.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var comp in comps.EnumerateArray())
                {
                    var e = new CatalogEntry
                    {
                        Category = GetStr(comp, "Category"),
                        Version = GetStr(comp, "Version"),
                        PageUrl = pageUrl,
                        FileUrl = fileUrl,
                    };
                    if (comp.TryGetProperty("DetectionValues", out var dv) && dv.ValueKind == JsonValueKind.Array)
                        foreach (var v in dv.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String) e.DetectionValues.Add(v.GetString());
                    if (!string.IsNullOrWhiteSpace(e.Category) && !string.IsNullOrWhiteSpace(e.Version))
                        entries.Add(e);
                }
            }
            return entries;
        }

        /// <summary>
        /// Finds the latest catalog entry in <paramref name="category"/> whose
        /// detection IDs match any of the installed device hardware IDs. Returns null
        /// when the catalog is unavailable or nothing matches.
        /// </summary>
        public static CatalogEntry FindLatest(List<CatalogEntry> catalog, string category, IEnumerable<string> hardwareIds)
        {
            if (catalog == null || catalog.Count == 0) return null;
            var hwids = (hardwareIds ?? Enumerable.Empty<string>())
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h.ToUpperInvariant())
                .ToList();
            if (hwids.Count == 0) return null;

            CatalogEntry best = null;
            foreach (var e in catalog)
            {
                if (!string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                // A DetectionValue like "VEN_8086&DEV_64A0&SUBSYS_146C1462" is a
                // substring of the installed hardware ID "PCI\VEN_8086&DEV_64A0&SUBSYS_146C1462&REV_04".
                bool matches = e.DetectionValues.Any(dv =>
                {
                    var d = dv.ToUpperInvariant();
                    return hwids.Any(h => h.Contains(d));
                });
                if (!matches) continue;
                if (best == null ||
                    DriverMatchUtil.CompareVersions(best.Version, e.Version) == DriverUpdateStatus.UpdateAvailable)
                {
                    best = e;
                }
            }
            return best;
        }

        private static string GetStr(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object) return "";
            if (!obj.TryGetProperty(name, out var v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            return "";
        }
    }
}

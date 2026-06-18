using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using NLog;

namespace XboxGamingBarHelper.Services
{
    /// <summary>
    /// Vendor-neutral driver/version matching helpers.
    ///
    /// This is a clean re-home of the reusable matching logic originally written
    /// for <see cref="LenovoDriverCheckService"/> (PnP/registry snapshot, fuzzy
    /// token-overlap matching, dotted-numeric version comparison). It is kept as
    /// a SEPARATE file so the proven, AV-clean Lenovo path stays byte-for-byte
    /// untouched; <see cref="MsiClawDriverCheckService"/> consumes these helpers.
    ///
    /// Reuses the namespace-level <see cref="DriverUpdateStatus"/> enum declared
    /// alongside the Lenovo service.
    ///
    /// Stop-words and token-alias maps are injectable so each caller can tune
    /// what counts as a discriminating token (Lenovo strips "lenovo"/"legion";
    /// MSI strips "msi"/"intel"/"claw"/"micro"/"star").
    /// </summary>
    internal static class DriverMatchUtil
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // -------- Tokenisation -------------------------------------------------

        /// <summary>
        /// Generic stop-word set — words present in nearly every driver/app name
        /// and therefore useless as a discriminator. Callers typically union this
        /// with their own vendor tokens via <see cref="WithStopWords"/>.
        /// </summary>
        public static readonly HashSet<string> DefaultStopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "driver", "drivers", "utility", "software", "and", "for", "the",
            "windows", "win", "x64", "x86", "64", "bit", "download",
            "universal", "application", "edition", "inc", "ltd", "corp",
            "corporation", "technology", "technologies",
        };

        /// <summary>Returns a new stop-word set = <see cref="DefaultStopWords"/> plus the extras.</summary>
        public static HashSet<string> WithStopWords(params string[] extra)
        {
            var set = new HashSet<string>(DefaultStopWords, StringComparer.Ordinal);
            if (extra != null)
                foreach (var e in extra)
                    if (!string.IsNullOrWhiteSpace(e)) set.Add(e.ToLowerInvariant());
            return set;
        }

        /// <summary>
        /// Symmetric token synonym map — catalog titles use one name ("WLAN",
        /// "Bluetooth") while Windows PnP entries use another ("Wi-Fi", "BT").
        /// Each token maps to every other token in its group.
        /// </summary>
        public static readonly Dictionary<string, string[]> DefaultTokenAliases = BuildTokenAliases();

        private static Dictionary<string, string[]> BuildTokenAliases()
        {
            var groups = new[]
            {
                new[] { "wlan", "wifi", "wireless", "wlancard" },
                new[] { "bluetooth", "bthusb", "btdriver" },
                new[] { "vga", "graphics", "display", "video", "gpu" },
                new[] { "audio", "sound", "hdaudio" },
                new[] { "cardreader", "reader" },
                new[] { "fingerprint", "fingerprinter", "biometric" },
                new[] { "chipset" },
            };
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var g in groups)
                foreach (var token in g)
                    map[token] = g.Where(t => t != token).ToArray();
            return map;
        }

        public static HashSet<string> TokeniseLower(string s, HashSet<string> stopWords, Dictionary<string, string[]> aliases)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(s)) return set;
            stopWords = stopWords ?? DefaultStopWords;
            aliases = aliases ?? DefaultTokenAliases;
            // Normalise hyphenated compound terms ("Wi-Fi" -> "wifi") so they
            // tokenise to the same token the catalog uses.
            var normalised = s.ToLowerInvariant()
                .Replace("wi-fi", "wifi")
                .Replace("wi fi", "wifi");
            foreach (var raw in System.Text.RegularExpressions.Regex.Split(normalised, @"[^a-z0-9]+"))
            {
                if (raw.Length < 3) continue;
                if (stopWords.Contains(raw)) continue;
                set.Add(raw);
                if (aliases.TryGetValue(raw, out var al))
                    foreach (var a in al) set.Add(a);
            }
            return set;
        }

        // -------- Installed snapshot ------------------------------------------

        public sealed class InstalledDriver
        {
            public string DeviceName;
            public string DriverProviderName;
            public string DriverVersion;
            public string DriverDate;
            public HashSet<string> NameTokens;
        }

        public sealed class InstalledApp
        {
            public string DisplayName;
            public string DisplayVersion;
            public string Publisher;
            public HashSet<string> NameTokens;
        }

        public sealed class InstalledIndex
        {
            public readonly List<InstalledDriver> Drivers;
            public readonly List<InstalledApp> Apps;
            public readonly string BiosVersion;
            public readonly HashSet<string> StopWords;
            public readonly Dictionary<string, string[]> Aliases;
            public InstalledIndex(List<InstalledDriver> drivers, List<InstalledApp> apps, string biosVersion,
                HashSet<string> stopWords, Dictionary<string, string[]> aliases)
            {
                Drivers = drivers ?? new List<InstalledDriver>();
                Apps = apps ?? new List<InstalledApp>();
                BiosVersion = biosVersion ?? "";
                StopWords = stopWords ?? DefaultStopWords;
                Aliases = aliases ?? DefaultTokenAliases;
            }
        }

        /// <summary>
        /// Query Win32_PnPSignedDriver once + the Add/Remove-Programs registry,
        /// pre-tokenise names, and bundle into an index for fuzzy matching.
        /// </summary>
        public static InstalledIndex BuildInstalledIndex(string biosVersion,
            HashSet<string> stopWords = null, Dictionary<string, string[]> aliases = null)
        {
            stopWords = stopWords ?? DefaultStopWords;
            aliases = aliases ?? DefaultTokenAliases;

            var drivers = new List<InstalledDriver>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceName, DriverProviderName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DriverVersion IS NOT NULL");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string deviceName = (obj["DeviceName"]?.ToString() ?? "").Trim();
                    string version = (obj["DriverVersion"]?.ToString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(version)) continue;
                    string provider = (obj["DriverProviderName"]?.ToString() ?? "").Trim();
                    drivers.Add(new InstalledDriver
                    {
                        DeviceName = deviceName,
                        DriverProviderName = provider,
                        DriverVersion = version,
                        DriverDate = (obj["DriverDate"]?.ToString() ?? "").Trim(),
                        NameTokens = TokeniseLower(deviceName + " " + provider, stopWords, aliases),
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Win32_PnPSignedDriver query failed: {ex.Message}");
            }

            var apps = BuildInstalledAppIndex(stopWords, aliases);
            Logger.Info($"Installed driver index: {drivers.Count} signed PnP drivers, {apps.Count} apps from uninstall registry");
            return new InstalledIndex(drivers, apps, biosVersion, stopWords, aliases);
        }

        private static List<InstalledApp> BuildInstalledAppIndex(HashSet<string> stopWords, Dictionary<string, string[]> aliases)
        {
            var apps = new List<InstalledApp>();
            string[] registryPaths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };
            foreach (var path in registryPaths)
            {
                try
                {
                    using var hive = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
                    if (hive == null) continue;
                    foreach (var subName in hive.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = hive.OpenSubKey(subName);
                            if (sub == null) continue;
                            string displayName = (sub.GetValue("DisplayName") as string)?.Trim() ?? "";
                            string displayVersion = (sub.GetValue("DisplayVersion") as string)?.Trim() ?? "";
                            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(displayVersion)) continue;
                            if (displayName.StartsWith("Update for Microsoft", StringComparison.OrdinalIgnoreCase)
                                || displayName.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase)
                                || subName.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
                                continue;
                            string publisher = (sub.GetValue("Publisher") as string)?.Trim() ?? "";
                            apps.Add(new InstalledApp
                            {
                                DisplayName = displayName,
                                DisplayVersion = displayVersion,
                                Publisher = publisher,
                                NameTokens = TokeniseLower(displayName + " " + publisher, stopWords, aliases),
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"Uninstall registry sub-key '{subName}' read failed: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Uninstall registry hive open failed for '{path}': {ex.Message}");
                }
            }
            return apps;
        }

        // -------- Matching -----------------------------------------------------

        public struct MatchResult
        {
            public string InstalledVersion;
            public string MatchedDeviceName;
            public string MatchedProvider;
            public int MatchScore;
            public DriverUpdateStatus Status;
        }

        public static bool LooksLikeBios(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var lower = s.ToLowerInvariant();
            return lower.Contains("bios") || lower.Contains("uefi");
        }

        /// <summary>
        /// Finds the best-matching installed driver/app for a catalog entry by
        /// token overlap and returns the installed version + update status.
        /// BIOS entries compare directly against the index's BIOS version.
        /// </summary>
        public static MatchResult MatchEntry(string entryName, string entryCategory, string latestVersion, InstalledIndex index)
        {
            var r = new MatchResult { Status = DriverUpdateStatus.Unknown, InstalledVersion = "" };
            if (index == null) return r;

            bool isBios = LooksLikeBios(entryCategory) || LooksLikeBios(entryName);
            if (isBios)
            {
                r.InstalledVersion = index.BiosVersion;
                r.Status = CompareVersions(index.BiosVersion, latestVersion);
                return r;
            }

            var entryTokens = TokeniseLower(entryName, index.StopWords, index.Aliases);
            if (entryTokens.Count == 0)
            {
                r.Status = DriverUpdateStatus.Unknown;
                return r;
            }

            var catalogVersionPrefix = ParseVersion(ExtractLatestVersion(latestVersion));

            InstalledDriver bestDriver = null;
            int bestDriverScore = 0;
            int bestDriverVerAffinity = -1;
            foreach (var d in index.Drivers)
            {
                int overlap = 0;
                foreach (var tok in entryTokens)
                    if (d.NameTokens.Contains(tok)) overlap++;
                if (overlap == 0) continue;
                int affinity = VersionPrefixMatch(d.DriverVersion, catalogVersionPrefix);
                if (overlap > bestDriverScore || (overlap == bestDriverScore && affinity > bestDriverVerAffinity))
                {
                    bestDriverScore = overlap;
                    bestDriverVerAffinity = affinity;
                    bestDriver = d;
                }
            }

            InstalledApp bestApp = null;
            int bestAppScore = 0;
            int bestAppVerAffinity = -1;
            foreach (var a in index.Apps)
            {
                int overlap = 0;
                foreach (var tok in entryTokens)
                    if (a.NameTokens.Contains(tok)) overlap++;
                if (overlap == 0) continue;
                int affinity = VersionPrefixMatch(a.DisplayVersion, catalogVersionPrefix);
                if (overlap > bestAppScore || (overlap == bestAppScore && affinity > bestAppVerAffinity))
                {
                    bestAppScore = overlap;
                    bestAppVerAffinity = affinity;
                    bestApp = a;
                }
            }

            const int MinTokensForMatch = 2;
            bool driverEligible = bestDriver != null && bestDriverScore >= MinTokensForMatch;
            bool appEligible = bestApp != null && bestAppScore >= MinTokensForMatch;

            if (!driverEligible && !appEligible)
            {
                r.InstalledVersion = "";
                r.Status = DriverUpdateStatus.NotInstalled;
                return r;
            }

            bool useApp = appEligible && (!driverEligible || bestAppScore >= bestDriverScore);
            if (useApp)
            {
                r.InstalledVersion = bestApp.DisplayVersion;
                r.MatchedDeviceName = bestApp.DisplayName ?? "";
                r.MatchedProvider = bestApp.Publisher ?? "";
                r.MatchScore = bestAppScore;
            }
            else
            {
                r.InstalledVersion = bestDriver.DriverVersion;
                r.MatchedDeviceName = bestDriver.DeviceName ?? "";
                r.MatchedProvider = bestDriver.DriverProviderName ?? "";
                r.MatchScore = bestDriverScore;
            }
            r.Status = CompareVersions(r.InstalledVersion, latestVersion);
            return r;
        }

        // -------- Version comparison (pure) -----------------------------------

        public static List<string> ExtractAllNumericVersions(string raw)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return results;
            foreach (var piece in raw.Split('/', ';', ',', ' ', '\t', '_'))
            {
                var p = piece.Trim();
                if (p.Length == 0) continue;
                int i = 0;
                while (i < p.Length && !char.IsDigit(p[i])) i++;
                if (i >= p.Length) continue;
                p = p.Substring(i);
                if (CountDots(p) == 0 && !char.IsDigit(p[0])) continue;
                if (ParseVersion(p) != null) results.Add(p);
            }
            if (results.Count == 0) results.Add(raw.Trim());
            return results;
        }

        public static string ExtractLatestVersion(string raw)
        {
            var all = ExtractAllNumericVersions(raw);
            if (all.Count == 0) return raw?.Trim() ?? "";
            string best = all[0];
            int bestDots = CountDots(best);
            for (int i = 1; i < all.Count; i++)
            {
                int dots = CountDots(all[i]);
                if (dots > bestDots) { best = all[i]; bestDots = dots; }
            }
            return best;
        }

        public static int VersionPrefixMatch(string candidateVersion, long[] catalogParts)
        {
            if (catalogParts == null || catalogParts.Length == 0) return 0;
            var c = ParseVersion(candidateVersion);
            if (c == null) return 0;
            int len = Math.Min(c.Length, catalogParts.Length);
            int i = 0;
            while (i < len && c[i] == catalogParts[i]) i++;
            return i;
        }

        public static int CountDots(string s)
        {
            int n = 0;
            foreach (var c in s) if (c == '.') n++;
            return n;
        }

        /// <summary>
        /// Compares an installed version string against a catalog version string.
        /// 1) dotted-numeric tuple match (exact, then major.minor-anchored),
        /// 2) letter-code pattern ([LETTERS][DIGITS][LETTERS], shared bookends —
        ///    covers BIOS codes like "RRCN14WW" / MSI EC codes), 3) string equality.
        /// </summary>
        public static DriverUpdateStatus CompareVersions(string installed, string latest)
        {
            if (string.IsNullOrWhiteSpace(installed)) return DriverUpdateStatus.NotInstalled;
            if (string.IsNullOrWhiteSpace(latest)) return DriverUpdateStatus.Unknown;

            var inst = ParseVersion(installed);
            if (inst != null)
            {
                var candidates = ExtractAllNumericVersions(latest)
                    .Select(c => new { Raw = c, Parts = ParseVersion(c) })
                    .Where(c => c.Parts != null)
                    .ToList();

                var exactMatch = candidates.FirstOrDefault(c =>
                    c.Parts.Length == inst.Length && c.Parts.SequenceEqual(inst));
                if (exactMatch != null) return DriverUpdateStatus.UpToDate;

                var majorMinorMatches = candidates.Where(c =>
                    c.Parts.Length >= 2 && inst.Length >= 2 &&
                    c.Parts[0] == inst[0] && c.Parts[1] == inst[1]).ToList();

                long[] chosen;
                if (majorMinorMatches.Count == 1)
                {
                    chosen = majorMinorMatches[0].Parts;
                }
                else if (majorMinorMatches.Count > 1)
                {
                    chosen = majorMinorMatches
                        .OrderBy(c => (c.Parts.Length >= 3 && inst.Length >= 3)
                            ? Math.Abs(c.Parts[2] - inst[2])
                            : long.MaxValue)
                        .First().Parts;
                }
                else
                {
                    chosen = candidates.Count > 0
                        ? candidates.OrderByDescending(c => c.Parts.Length).First().Parts
                        : null;
                }

                if (chosen != null)
                {
                    int len = Math.Max(inst.Length, chosen.Length);
                    for (int i = 0; i < len; i++)
                    {
                        long a = i < inst.Length ? inst[i] : 0;
                        long b = i < chosen.Length ? chosen[i] : 0;
                        if (a < b) return DriverUpdateStatus.UpdateAvailable;
                        if (a > b) return DriverUpdateStatus.UpToDate;
                    }
                    return DriverUpdateStatus.UpToDate;
                }
            }

            var codeCompare = CompareLetterCodes(installed, latest);
            if (codeCompare.HasValue) return codeCompare.Value;

            return string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase)
                ? DriverUpdateStatus.UpToDate
                : DriverUpdateStatus.Unknown;
        }

        public static long[] ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Split('.');
            var result = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var digits = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
                if (digits.Length == 0) return null;
                if (!long.TryParse(digits, out result[i])) return null;
            }
            return result;
        }

        private static readonly System.Text.RegularExpressions.Regex _letterCodeRegex =
            new System.Text.RegularExpressions.Regex(@"^([A-Z]+)(\d+)([A-Z]+)$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static DriverUpdateStatus? CompareLetterCodes(string installed, string latest)
        {
            var mi = _letterCodeRegex.Match(installed?.Trim().ToUpperInvariant() ?? "");
            var ml = _letterCodeRegex.Match(latest?.Trim().ToUpperInvariant() ?? "");
            if (!mi.Success || !ml.Success) return null;
            if (!string.Equals(mi.Groups[1].Value, ml.Groups[1].Value, StringComparison.Ordinal)) return null;
            if (!string.Equals(mi.Groups[3].Value, ml.Groups[3].Value, StringComparison.Ordinal)) return null;
            if (!long.TryParse(mi.Groups[2].Value, out long a)) return null;
            if (!long.TryParse(ml.Groups[2].Value, out long b)) return null;
            if (a < b) return DriverUpdateStatus.UpdateAvailable;
            if (a > b) return DriverUpdateStatus.UpToDate;
            return DriverUpdateStatus.UpToDate;
        }
    }
}

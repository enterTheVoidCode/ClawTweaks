using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Checks the running ClawTweaksSetup.exe's own version (ClawTweaksSetup.csproj &lt;Version&gt;,
    /// a separate number from the main ClawTweaks app version) against a minimum declared in a small
    /// curated manifest — same hosting pattern as manifest/claw-drivers.json
    /// (MsiClawDriverCheckService): a JSON file on the 'master' branch, fetched via
    /// raw.githubusercontent.com so the URL never churns with per-release branches.
    ///
    /// This exists because the standalone Center picker downloads and runs old, already-built exes
    /// (GitHub releases/test builds/Drive nightlies) — a Setup-side bug fix landing in source doesn't
    /// retroactively fix binaries a user already downloaded. Bumping minimumSetupVersion in the
    /// manifest after cutting a fixed Setup build flags those stale downloads.
    /// </summary>
    public static class SetupVersionCheck
    {
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/master/manifest/setup-manifest.json";

        public sealed class Result
        {
            public bool Outdated;
            public Version MinimumVersion;
            public Version RunningVersion;
            public string Message;
        }

        /// <summary>Null on any failure (offline, manifest missing/malformed) — this check must never
        /// block or nag the user when it simply couldn't run.</summary>
        public static async Task<Result> CheckAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.Add("User-Agent", "ClawTweaks");
                string json = await http.GetStringAsync(ManifestUrl).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string minStr = root.TryGetProperty("minimumSetupVersion", out var minEl) ? minEl.GetString() : null;
                if (string.IsNullOrEmpty(minStr) || !Version.TryParse(minStr, out var minVersion))
                    return null;

                string message = root.TryGetProperty("message", out var msgEl)
                    ? msgEl.GetString()
                    : "This ClawTweaks Center build is outdated. Please download the latest build.";

                var running = Assembly.GetExecutingAssembly().GetName().Version;
                return new Result
                {
                    Outdated = running < minVersion,
                    MinimumVersion = minVersion,
                    RunningVersion = running,
                    Message = message,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

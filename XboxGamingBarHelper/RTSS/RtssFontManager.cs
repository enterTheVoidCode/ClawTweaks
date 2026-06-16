using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using Shared.Utilities;

namespace XboxGamingBarHelper.RTSS
{
    /// <summary>
    /// Sets the RTSS OSD font by editing RTSS's Global profile ([Font] Face/Weight) and restarting RTSS
    /// so it picks the change up. ClawTweaks defaults the overlay to "Bahnschrift" (ships with Windows
    /// 10/11) and falls back to RTSS's stock "Unispace" if Bahnschrift isn't installed.
    ///
    /// RTSS owns the profile at runtime (reads on start, writes on exit), so we only write + restart when
    /// the requested font actually differs from what's already on disk — no needless restart/blink on
    /// every boot. RTSS is restarted via explorer.exe so it comes back at the user's integrity level.
    /// </summary>
    internal static class RtssFontManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public const string DefaultFace = "Bahnschrift";
        public const string FallbackFace = "Unispace";
        public const int DefaultWeight = 400;

        private static string GlobalProfilePath()
        {
            string exe = RTSSHelper.ExecutablePath();
            if (string.IsNullOrEmpty(exe)) return null;
            string dir = Path.GetDirectoryName(exe);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "Profiles", "Global");
        }

        public static bool IsFontInstalled(string face)
        {
            if (string.IsNullOrWhiteSpace(face)) return false;
            try
            {
                using var col = new System.Drawing.Text.InstalledFontCollection();
                return col.Families.Any(f => string.Equals(f.Name, face, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>
        /// Ensures the RTSS Global profile uses the given face/weight (falling back to Unispace if the
        /// requested face isn't installed). Only writes + restarts RTSS when something actually changes.
        /// </summary>
        public static void EnsureFont(string face, int weight)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(face)) face = DefaultFace;
                if (weight <= 0) weight = DefaultWeight;

                if (!string.Equals(face, FallbackFace, StringComparison.OrdinalIgnoreCase) && !IsFontInstalled(face))
                {
                    Logger.Info($"[RtssFont] '{face}' not installed — falling back to '{FallbackFace}'");
                    face = FallbackFace;
                    weight = DefaultWeight;
                }

                string path = GlobalProfilePath();
                if (path == null || !File.Exists(path))
                {
                    Logger.Warn("[RtssFont] RTSS Global profile not found — cannot set font");
                    return;
                }

                var lines = File.ReadAllLines(path).ToList();
                int sec = lines.FindIndex(l => l.Trim().Equals("[Font]", StringComparison.OrdinalIgnoreCase));
                if (sec < 0)
                {
                    Logger.Warn("[RtssFont] [Font] section not found in Global profile");
                    return;
                }

                int faceIdx = -1, weightIdx = -1, sectionEnd = lines.Count;
                for (int i = sec + 1; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("[")) { sectionEnd = i; break; }
                    if (t.StartsWith("Face=", StringComparison.OrdinalIgnoreCase)) faceIdx = i;
                    else if (t.StartsWith("Weight=", StringComparison.OrdinalIgnoreCase)) weightIdx = i;
                }

                string curFace = faceIdx >= 0 ? lines[faceIdx].Substring(lines[faceIdx].IndexOf('=') + 1).Trim() : "";
                string curWeight = weightIdx >= 0 ? lines[weightIdx].Substring(lines[weightIdx].IndexOf('=') + 1).Trim() : "";
                if (string.Equals(curFace, face, StringComparison.OrdinalIgnoreCase) && curWeight == weight.ToString())
                {
                    Logger.Debug($"[RtssFont] already {face} ({weight}) — no change");
                    return;
                }

                if (faceIdx >= 0) lines[faceIdx] = "Face=" + face;
                else lines.Insert(sectionEnd, "Face=" + face);
                if (weightIdx >= 0) lines[weightIdx] = "Weight=" + weight;
                else lines.Insert(sec + 1, "Weight=" + weight);

                File.WriteAllLines(path, lines);
                Logger.Info($"[RtssFont] Global font set to '{face}' weight {weight}");

                // Apply WITHOUT restarting RTSS when possible. A kill+restart makes RTSS reload its OSD
                // at the default (centered) position and drop the runtime layout — very visible when the
                // font is changed mid-game (the Full preset jumps to screen centre and stays there).
                // Instead ask the running RTSS to re-read the Global profile from disk live (the same
                // RTSSHooks API the FPS limiter uses). Only fall back to a restart if that's unavailable.
                if (TryLiveReload())
                    Logger.Info("[RtssFont] applied via live profile reload (no RTSS restart, OSD position kept)");
                else
                {
                    Logger.Info("[RtssFont] live reload unavailable — restarting RTSS");
                    RestartRtss();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RtssFont] EnsureFont failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the just-written profile font to the running RTSS without a restart. If RTSS isn't
        /// running there's nothing to do (the profile is read on its next start), so this also returns
        /// true to skip the restart. Returns false only when RTSS is running but the live-reload API
        /// (RTSSHooks64.dll) isn't usable — the caller then restarts RTSS as a fallback.
        /// </summary>
        private static bool TryLiveReload()
        {
            try
            {
                if (!RTSSHelper.IsRunning())
                {
                    Logger.Debug("[RtssFont] RTSS not running — font will be read on next start, no restart needed");
                    return true;
                }
                if (!RTSSFPSLimiter.Initialize())
                    return false;
                return RTSSFPSLimiter.ReloadGlobalProfile();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RtssFont] TryLiveReload failed: {ex.Message}");
                return false;
            }
        }

        private static void RestartRtss()
        {
            try
            {
                string exe = RTSSHelper.ExecutablePath();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    Logger.Warn("[RtssFont] RTSS.exe not found — cannot restart");
                    return;
                }

                var proc = RTSSHelper.GetProcess();
                if (proc != null)
                {
                    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    proc.Dispose();
                }

                // Launch RTSS DIRECTLY from the (already-elevated) helper so it inherits the admin token.
                // RTSS.exe is requireAdministrator; relaunching it de-elevated would pop a UAC prompt, but
                // as a child of our elevated helper it starts elevated with no prompt — and elevated RTSS
                // is the recommended config for reliable game hooking anyway.
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                Logger.Info("[RtssFont] RTSS restarted (elevated, no UAC)");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RtssFont] RestartRtss failed: {ex.Message}");
            }
        }
    }
}

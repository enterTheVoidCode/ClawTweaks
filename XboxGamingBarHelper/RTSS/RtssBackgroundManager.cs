using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using Shared.Utilities;

namespace XboxGamingBarHelper.RTSS
{
    /// <summary>
    /// Toggles RTSS's native On-Screen Display background fill ([OSD] EnableFill/FillColor in the
    /// RTSS Global profile) — a real, RTSS-rendered backdrop box behind the OSD text, sized and
    /// positioned by RTSS itself. Because RTSS owns the rendering, this automatically fits whatever
    /// overlay level/layout is currently active (Basic/Horizontal/Horizontal Detailed/Full) with no
    /// per-line tag hacks — unlike the old dead OSDBackground text-tag approach (a "<B=w,h>" bar needs
    /// a fixed width and breaks with dynamic content, per RTSS's own docs).
    ///
    /// Mirrors RtssFontManager's read/patch/live-reload pattern: RTSS owns the profile at runtime
    /// (reads on start, writes on exit), so we only write + reload when the requested state actually
    /// differs from what's already on disk.
    /// </summary>
    internal static class RtssBackgroundManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static string GlobalProfilePath()
        {
            string exe = RTSSHelper.ExecutablePath();
            if (string.IsNullOrEmpty(exe)) return null;
            string dir = Path.GetDirectoryName(exe);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "Profiles", "Global");
        }

        /// <summary>
        /// Ensures RTSS's [OSD] EnableFill/FillColor match the requested state (black fill, alpha
        /// derived from opacityPercent 0-100). Only writes + reloads when something actually changed.
        /// </summary>
        public static void EnsureBackground(bool enabled, int opacityPercent)
        {
            try
            {
                opacityPercent = Math.Max(0, Math.Min(100, opacityPercent));
                byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255.0);
                string fillColor = $"{alpha:X2}000000";

                string path = GlobalProfilePath();
                if (path == null || !File.Exists(path))
                {
                    Logger.Warn("[RtssBackground] RTSS Global profile not found — cannot set background");
                    return;
                }

                var lines = File.ReadAllLines(path).ToList();
                int sec = lines.FindIndex(l => l.Trim().Equals("[OSD]", StringComparison.OrdinalIgnoreCase));
                if (sec < 0)
                {
                    Logger.Warn("[RtssBackground] [OSD] section not found in Global profile");
                    return;
                }

                int enableIdx = -1, colorIdx = -1, sectionEnd = lines.Count;
                for (int i = sec + 1; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("[")) { sectionEnd = i; break; }
                    if (t.StartsWith("EnableFill=", StringComparison.OrdinalIgnoreCase)) enableIdx = i;
                    else if (t.StartsWith("FillColor=", StringComparison.OrdinalIgnoreCase)) colorIdx = i;
                }

                string curEnable = enableIdx >= 0 ? lines[enableIdx].Substring(lines[enableIdx].IndexOf('=') + 1).Trim() : "";
                string curColor = colorIdx >= 0 ? lines[colorIdx].Substring(lines[colorIdx].IndexOf('=') + 1).Trim() : "";
                string wantEnable = enabled ? "1" : "0";

                if (curEnable == wantEnable && string.Equals(curColor, fillColor, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug($"[RtssBackground] already enabled={enabled}, fill={fillColor} — no change");
                    return;
                }

                if (enableIdx >= 0) lines[enableIdx] = "EnableFill=" + wantEnable;
                else lines.Insert(sectionEnd, "EnableFill=" + wantEnable);
                if (colorIdx >= 0) lines[colorIdx] = "FillColor=" + fillColor;
                else lines.Insert(sec + 1, "FillColor=" + fillColor);

                File.WriteAllLines(path, lines);
                Logger.Info($"[RtssBackground] Global EnableFill={wantEnable}, FillColor={fillColor}");

                if (TryLiveReload())
                    Logger.Info("[RtssBackground] applied via live profile reload (no RTSS restart)");
                else
                {
                    Logger.Info("[RtssBackground] live reload unavailable — restarting RTSS");
                    RestartRtss();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RtssBackground] EnsureBackground failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the just-written profile to the running RTSS without a restart. If RTSS isn't
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
                    Logger.Debug("[RtssBackground] RTSS not running — background will be read on next start, no restart needed");
                    return true;
                }
                if (!RTSSFPSLimiter.Initialize())
                    return false;
                return RTSSFPSLimiter.ReloadGlobalProfile();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RtssBackground] TryLiveReload failed: {ex.Message}");
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
                    Logger.Warn("[RtssBackground] RTSS.exe not found — cannot restart");
                    return;
                }

                var proc = RTSSHelper.GetProcess();
                if (proc != null)
                {
                    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                    proc.Dispose();
                }

                // Launch RTSS DIRECTLY from the (already-elevated) helper so it inherits the admin token
                // (see RtssFontManager.RestartRtss for the same rationale).
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                Logger.Info("[RtssBackground] RTSS restarted (elevated, no UAC)");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RtssBackground] RestartRtss failed: {ex.Message}");
            }
        }
    }
}

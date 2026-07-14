using System;
using System.IO;
using System.Text.Json.Nodes;
using NLog;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Coexistence mirror for MSI Center M's controller store. Center M's UI + its per-apply re-push
    /// read from profile.rec (JSON), NOT the live controller EEPROM, so any firmware write we make must
    /// also be written here or Center M shows a stale state and clobbers us on its next apply.
    ///
    /// We do a targeted JSON read-modify-write that touches ONLY our field and preserves everything else
    /// (button remaps, vibration, the DS profile, the unknown LSS/RSS/MT booleans, ...). The helper runs
    /// elevated, which is required — the file's ACL gives normal users read-only. If the file is absent
    /// (Center M not installed) we silently no-op and rely on the EEPROM write alone.
    ///
    /// Field map (see reverse_engineered/RE_MSI_ButtonRemap.md):
    ///   sticks  → GS.{LSDZ,LSEDZ,RSDZ,RSEDZ}, swap → GS.{LSS,RSS}
    ///   triggers→ TP.{LTDZ,LTEDZ,RTDZ,RTEDZ}
    ///   gyro    → MTP.outputType (0 = off)
    /// </summary>
    internal static class MsiCenterMProfile
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string ProfilePath = @"C:\ProgramData\MSI\MSI_Center_M\Control Mode\profile.rec";
        private static readonly object FileLock = new object();

        public static bool Available => File.Exists(ProfilePath);

        /// <summary>Mirror a single deadzone/limit field. Widget field names map to GS.* / TP.*.</summary>
        public static void MirrorDeadzone(string field, int value)
        {
            switch (field)
            {
                case "LSDZ": case "LSEDZ": case "RSDZ": case "RSEDZ":
                    Edit(root => SetNum(root, "GS", field, value)); break;
                case "LTDZ": case "LTEDZ": case "RTDZ": case "RTEDZ":
                    Edit(root => SetNum(root, "TP", field, value)); break;
            }
        }

        public static void MirrorSticksFactory() => Edit(root =>
        {
            SetNum(root, "GS", "LSDZ", 5);  SetNum(root, "GS", "LSEDZ", 100);
            SetNum(root, "GS", "RSDZ", 5);  SetNum(root, "GS", "RSEDZ", 100);
        });

        public static void MirrorTriggersFactory() => Edit(root =>
        {
            SetNum(root, "TP", "LTDZ", 0);  SetNum(root, "TP", "LTEDZ", 100);
            SetNum(root, "TP", "RTDZ", 0);  SetNum(root, "TP", "RTEDZ", 100);
        });

        public static void MirrorGyroOff() => Edit(root =>
        {
            var mtp = root["MTP"]?.AsObject();
            if (mtp != null) mtp["outputType"] = 0;   // 0 = off/none
        });

        public static void MirrorStickSwapOff() => Edit(root =>
        {
            SetBool(root, "GS", "LSS", false);
            SetBool(root, "GS", "RSS", false);
        });

        // ── internals ───────────────────────────────────────────────────────────
        private static void SetNum(JsonNode root, string obj, string key, int value)
        {
            var o = root[obj]?.AsObject();
            if (o != null) o[key] = value;
        }

        private static void SetBool(JsonNode root, string obj, string key, bool value)
        {
            var o = root[obj]?.AsObject();
            if (o != null) o[key] = value;
        }

        private static void Edit(Action<JsonNode> mutate)
        {
            lock (FileLock)
            {
                try
                {
                    if (!File.Exists(ProfilePath))
                    {
                        Logger.Debug("[MsiCenterMProfile] profile.rec not present — EEPROM-only (Center M not installed)");
                        return;
                    }
                    var root = JsonNode.Parse(File.ReadAllText(ProfilePath));
                    if (root == null) { Logger.Warn("[MsiCenterMProfile] profile.rec parsed to null"); return; }
                    mutate(root);
                    // Minified single-line JSON, like MSI writes it.
                    File.WriteAllText(ProfilePath, root.ToJsonString());
                    Logger.Info("[MsiCenterMProfile] profile.rec mirrored");
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Warn("[MsiCenterMProfile] profile.rec write denied (need elevation) — EEPROM write stands, Center M may clobber it");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[MsiCenterMProfile] profile.rec mirror failed: {ex.Message}");
                }
            }
        }
    }
}

using System;
using Shared.Led;
using XboxGamingBarHelper.Devices.MSIClaw;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Per-zone LED composite driver: takes a <see cref="LedCompositeSpec"/> (3 zones + global speed/
    /// brightness), renders it with <see cref="LedCompositor"/> and writes the 4 frames. Owns the
    /// authoritative composite so the SoC timer (LedSoc) can re-render battery zones on a band change,
    /// and the boot LED can restore the exact look.
    /// </summary>
    internal partial class Program
    {
        private static LedCompositeSpec _currentComposite;
        private static readonly object _compositeLock = new object();

        /// <summary>Loads the persisted composite at startup so the SoC timer / boot have it.</summary>
        internal static void InitLedComposite()
        {
            try
            {
                if (MsiLedCompositeStore.TryLoad(out var spec))
                {
                    var migrated = MigrateComposite(spec);
                    if (!ReferenceEquals(migrated, spec))
                    {
                        MsiLedCompositeStore.Save(migrated);   // heal the persisted file
                        Logger.Info("[LedComposite] migrated legacy/white composite → current default");
                    }
                    lock (_compositeLock) _currentComposite = migrated;
                    Logger.Info("[LedComposite] Init: loaded persisted composite");
                }
            }
            catch (Exception ex) { Logger.Debug($"[LedComposite] Init failed: {ex.Message}"); }
        }

        /// <summary>The old factory default (static white) was persisted onto many devices by the
        /// resume-clobber bug. Treat it (and the current default) as "unconfigured" and adopt the current
        /// factory default, so those devices don't stay stuck on white.</summary>
        private static LedCompositeSpec MigrateComposite(LedCompositeSpec spec)
            => (spec != null && spec.IsPristineOrLegacyDefault) ? new LedCompositeSpec() : spec;

        /// <summary>Pipe entry point: persist + drive a new composite.</summary>
        internal static void ApplyComposite(LedCompositeSpec spec)
        {
            if (spec == null) return;
            lock (_compositeLock) _currentComposite = spec;
            MsiLedCompositeStore.Save(spec);
            _lastSocBand = -1;   // force the SoC timer to re-evaluate its band for any battery zone
            bool ok = DriveComposite(spec);
            Logger.Info($"[LedComposite] applied '{spec.Serialize()}' → ok={ok}");
        }

        /// <summary>Renders + writes the composite. Battery zones use the current SoC band colour.</summary>
        internal static bool DriveComposite(LedCompositeSpec spec)
        {
            if (spec == null) return false;
            LedRgb? soc = null;
            if (spec.HasBatteryZone && TryGetSocColor(out var c)) soc = c;
            byte[][] frames = LedCompositor.Compose(spec, soc);
            return MsiClawLedController.TrySetFrames(frames, LedCompositor.SpeedByte(spec.SpeedIdx));
        }

        /// <summary>Boot path: apply the persisted composite (retryable). Legacy fallback to the old
        /// solid colour store when no composite exists yet (upgrade case).</summary>
        internal static bool TryApplyPersistedComposite()
        {
            LedCompositeSpec spec;
            lock (_compositeLock) spec = _currentComposite;
            if (spec == null && MsiLedCompositeStore.TryLoad(out var s))
            {
                spec = MigrateComposite(s);
                if (!ReferenceEquals(spec, s)) MsiLedCompositeStore.Save(spec);
                lock (_compositeLock) _currentComposite = spec;
            }
            if (spec != null) return DriveComposite(spec);

            // Legacy: no composite yet → apply the old saved solid colour, if any.
            if (MsiLedColorStore.TryLoad(out byte r, out byte g, out byte b, out byte br))
                return MsiClawLedController.TrySetLedColor(r, g, b, br);
            return false;
        }

        /// <summary>True if a composite is stored and any of its zones is Battery.</summary>
        internal static bool CompositeHasBatteryZone()
        {
            LedCompositeSpec spec;
            lock (_compositeLock) spec = _currentComposite;
            return spec != null && spec.HasBatteryZone;
        }

        /// <summary>Re-renders the current composite (used by the SoC timer when the battery band changes).</summary>
        internal static void ReapplyCompositeForBattery()
        {
            LedCompositeSpec spec;
            lock (_compositeLock) spec = _currentComposite;
            if (spec != null && spec.HasBatteryZone) DriveComposite(spec);
        }

        /// <summary>Current SoC band colour (raw; global brightness is applied by the compositor). False when battery unreadable.</summary>
        internal static bool TryGetSocColor(out LedRgb color)
        {
            color = default(LedRgb);
            try
            {
                int soc = (int)(performanceManager?.BatteryLevel?.Value ?? -1f);
                if (soc <= 0 || soc > 100) return false;
                var c = LedSocBandColors[ComputeSocBand(soc)];
                color = new LedRgb(c.R, c.G, c.B);
                return true;
            }
            catch { return false; }
        }
    }
}

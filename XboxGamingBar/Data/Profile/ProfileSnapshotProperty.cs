using NLog;
using Shared.Data;
using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// The helper's performance-profile truth, received read-only (plan §5.3). Replaces the widget's
    /// own copy as the source for DISPLAYING profile values.
    ///
    /// Read-only is the point: the widget sends user-initiated sets and shows what comes back. Writing
    /// a profile value here would recreate the second store this channel exists to remove.
    ///
    /// The payload is the compact XML of a <c>List&lt;GameProfile&gt;</c> — the same type the helper
    /// persists, so a new profile field needs no change in this class. Parsed ONCE per received value,
    /// not per read: the cards query it many times per refresh, and <c>XmlHelper.FromXMLString</c> is
    /// far too expensive for that.
    /// </summary>
    internal class ProfileSnapshotProperty : WidgetProperty<string>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly object parseLock = new object();

        /// <summary>The XML the current parse result belongs to — guards against re-parsing.</summary>
        private string parsedFrom;
        private Dictionary<string, GameProfile> byName;

        /// <summary>Names already reported as missing, reset whenever a new snapshot is parsed.</summary>
        private readonly HashSet<string> loggedMisses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string loggedMissesFor;

        public ProfileSnapshotProperty() : base("", null, Function.ProfileSnapshot)
        {
        }

        /// <summary>
        /// Raised on the PIPE thread whenever a new snapshot arrives — the subscriber has to marshal to
        /// the UI thread itself.
        ///
        /// Without this the cards never redrew when the store changed. They only refreshed when some
        /// other interaction happened to call UpdateProfileDisplay, so every card rendered from the
        /// PREVIOUS snapshot. On a boolean that alternates, being one change behind is indistinguishable
        /// from being negated: flipping "Separate values per power state" ON drew the single-value card
        /// and the text "one value for both power states", because at that instant the widget still held
        /// the snapshot from before the write (reported 2026-08-02). The same lag made any other card
        /// value look wrong for as long as nothing else forced a refresh.
        /// </summary>
        public event Action SnapshotChanged;

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            try { SnapshotChanged?.Invoke(); }
            catch (Exception ex) { Logger.Warn($"ProfileSnapshot SnapshotChanged subscriber threw: {ex.Message}"); }
        }

        /// <summary>
        /// True once a snapshot has arrived. Callers MUST check this before displaying profile values:
        /// until then the fields have to stay hidden rather than showing 0 (plan §6 — a visible "0 W"
        /// during the startup gap is the most likely new bug report of this rebuild).
        /// </summary>
        public bool HasSnapshot => !string.IsNullOrEmpty(Value);

        /// <summary>
        /// The global profile, or null while no snapshot has arrived.
        /// </summary>
        public GameProfile GetGlobal() => GetByName(GameProfile.GLOBAL_PROFILE_NAME);

        /// <summary>
        /// The profile stored under this game name, or null if there is none (or no snapshot yet).
        /// A null result means "no per-game profile" — callers fall back to the global one, they must
        /// not substitute zeros.
        ///
        /// The name is matched case-insensitively and trimmed. The two sides do NOT derive this string
        /// from one source: the helper stores <c>GameId.Name</c> (the title it recorded for the exe),
        /// the widget asks with its own profile-container name. Memory `game-detection-key-vs-label`
        /// describes exactly this split, and a saved profile whose casing or padding drifted once would
        /// otherwise render as a card with no rows at all — silently, because "no per-game profile" is
        /// a legitimate answer here.
        /// </summary>
        public GameProfile GetByName(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return null;

            var map = EnsureParsed();
            if (map == null)
            {
                // No snapshot at all is a different failure from "this game is not in it", and it looks
                // identical on screen (a card with no rows). Say which one it is.
                LogMiss(gameName, null);
                return null;
            }

            if (map.TryGetValue(gameName.Trim(), out var profile)) return profile;

            LogMiss(gameName, map);
            return null;
        }

        /// <summary>
        /// Reports a lookup that found no profile, ONCE per (name, snapshot) pair. At Info: a miss here
        /// empties a whole profile card, and diagnosing that from a user's log is impossible if the only
        /// trace is a Debug line the widget never writes.
        ///
        /// A miss is not always a defect — a game the helper has no profile for is normal — so the
        /// available keys are printed alongside: they say immediately whether the profile is absent or
        /// merely stored under a different string.
        /// </summary>
        private void LogMiss(string gameName, Dictionary<string, GameProfile> map)
        {
            lock (parseLock)
            {
                if (loggedMissesFor != parsedFrom)
                {
                    loggedMissesFor = parsedFrom;
                    loggedMisses.Clear();
                }
                if (!loggedMisses.Add(gameName)) return;
            }

            if (map == null)
            {
                Logger.Info($"ProfileSnapshot lookup for '{gameName}' ran before any snapshot arrived "
                    + "- every profile card stays empty until the helper pushes one");
                return;
            }

            Logger.Info($"ProfileSnapshot has no profile named '{gameName}' — the snapshot holds "
                + $"{map.Count}: {string.Join(", ", map.Keys)}");
        }

        /// <summary>
        /// All profiles in the snapshot, global first. Empty while no snapshot has arrived.
        /// </summary>
        public IReadOnlyCollection<GameProfile> GetAll()
        {
            var map = EnsureParsed();
            return map == null ? new List<GameProfile>() : new List<GameProfile>(map.Values);
        }

        /// <summary>
        /// True while the device runs on battery, i.e. which side of the AC/DC override applies.
        /// Lives here so every card resolves the power source the same way — the widget determined it
        /// with `PowerSupplyStatus != NotPresent` in several places, and "isOnAC" inverted at the call
        /// site is exactly where an off-by-one-negation hides.
        /// </summary>
        public static bool IsOnBattery =>
            Windows.System.Power.PowerManager.PowerSupplyStatus == Windows.System.Power.PowerSupplyStatus.NotPresent;

        private Dictionary<string, GameProfile> EnsureParsed()
        {
            var xml = Value;
            if (string.IsNullOrEmpty(xml)) return null;

            lock (parseLock)
            {
                if (ReferenceEquals(parsedFrom, xml) || parsedFrom == xml)
                    return byName;

                Dictionary<string, GameProfile> map = null;
                try
                {
                    var list = XmlHelper.FromXMLString<List<GameProfile>>(xml);
                    if (list != null)
                    {
                        // Case-insensitive and trimmed, for the reason spelled out on GetByName: the key
                        // travels from the helper's store to a widget profile-container name, and those
                        // two are not produced by the same code.
                        map = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                        foreach (var profile in list)
                        {
                            var name = profile?.GameId.Name?.Trim();
                            if (string.IsNullOrEmpty(name)) continue;
                            map[name] = profile;
                        }
                        Logger.Info($"ProfileSnapshot received: {map.Count} profiles ({xml.Length} chars)");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"ProfileSnapshot could not be parsed, keeping the previous one: {ex.Message}");
                    return byName; // stale-but-valid beats blank cards
                }

                if (map == null) return byName;

                parsedFrom = xml;
                byName = map;
                return byName;
            }
        }
    }
}

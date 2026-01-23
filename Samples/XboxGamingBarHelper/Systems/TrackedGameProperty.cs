using NLog;
using Shared.Data;
using Shared.Enums;
using Shared.Utilities;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    internal class TrackedGameProperty : HelperProperty<TrackedGame, SystemManager>
    {
        private static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Timeout in seconds after which a TrackedGame with no matching window will be cleared.
        /// This prevents the TrackedGame from persisting indefinitely after the game closes.
        /// </summary>
        private const int NoWindowMatchTimeoutSeconds = 15;

        /// <summary>
        /// Timestamp when we first detected no window match for the current TrackedGame.
        /// Null when there is a window match or no tracked game.
        /// </summary>
        private DateTime? noWindowMatchSince = null;

        public string AumId
        {
            get { return Value.AumId; }
        }

        public string DisplayName
        {
            get { return Value.DisplayName; }
        }

        public string TitleId
        {
            get { return Value.TitleId; }
        }

        public bool IsFullscreen
        {
            get { return Value.IsFullscreen; }
        }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Value.DisplayName) || !string.IsNullOrEmpty(Value.TitleId);
        }

        /// <summary>
        /// Called by game detection when the TrackedGame has a matching window.
        /// Resets the no-window-match timeout.
        /// </summary>
        public void OnWindowMatched()
        {
            if (noWindowMatchSince.HasValue)
            {
                Logger.Debug($"TrackedGame \"{DisplayName}\" window match restored");
                noWindowMatchSince = null;
            }
        }

        /// <summary>
        /// Called by game detection when the TrackedGame has no matching window.
        /// Starts or continues the no-window-match timeout tracking.
        /// Returns true if the timeout has been exceeded and the TrackedGame should be cleared.
        /// </summary>
        public bool OnNoWindowMatch()
        {
            if (!IsValid())
            {
                return false;
            }

            if (!noWindowMatchSince.HasValue)
            {
                noWindowMatchSince = DateTime.UtcNow;
                Logger.Debug($"TrackedGame \"{DisplayName}\" no window match - starting timeout");
                return false;
            }

            var elapsed = DateTime.UtcNow - noWindowMatchSince.Value;
            if (elapsed.TotalSeconds >= NoWindowMatchTimeoutSeconds)
            {
                Logger.Info($"TrackedGame \"{DisplayName}\" has had no window match for {elapsed.TotalSeconds:F0}s - clearing");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Forcibly clears the TrackedGame. Called when the no-window-match timeout expires.
        /// </summary>
        public void Clear()
        {
            if (IsValid())
            {
                Logger.Info($"Clearing stale TrackedGame: {DisplayName}");
                noWindowMatchSince = null;
                base.SetValue(new TrackedGame(), 0);
            }
        }

        /// <summary>
        /// Check if a TrackedGame instance is valid (has DisplayName or TitleId).
        /// </summary>
        private bool IsTrackedGameValid(TrackedGame trackedGame)
        {
            return !string.IsNullOrEmpty(trackedGame.DisplayName) || !string.IsNullOrEmpty(trackedGame.TitleId);
        }

        /// <summary>
        /// Override SetValue to reject empty/invalid TrackedGame updates from the widget.
        /// When the widget loses focus, Game Bar sends empty TrackedGame which should not
        /// clear our currently detected game.
        /// </summary>
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            TrackedGame? trackedGame = null;

            // Handle both cases: direct TrackedGame object or XML string that needs deserialization
            if (newValue is TrackedGame directTrackedGame)
            {
                trackedGame = directTrackedGame;
            }
            else if (newValue is string xmlString && !string.IsNullOrEmpty(xmlString))
            {
                // Deserialize XML string to TrackedGame for validation
                try
                {
                    trackedGame = XmlHelper.FromXMLString<TrackedGame>(xmlString);
                }
                catch
                {
                    // If deserialization fails, let base class handle it (will also fail there)
                    Logger.Warn($"Failed to deserialize TrackedGame XML for validation, passing to base");
                }
            }

            // Check if the incoming TrackedGame is valid before accepting it
            if (trackedGame.HasValue)
            {
                bool newValueIsValid = IsTrackedGameValid(trackedGame.Value);

                // If we have a valid tracked game and the new one is invalid, reject the update
                // UNLESS the tracked game has timed out (no window match for extended period)
                if (IsValid() && !newValueIsValid)
                {
                    // Check if we should allow clearing due to timeout
                    if (noWindowMatchSince.HasValue)
                    {
                        var elapsed = DateTime.UtcNow - noWindowMatchSince.Value;
                        if (elapsed.TotalSeconds >= NoWindowMatchTimeoutSeconds)
                        {
                            Logger.Info($"Accepting empty TrackedGame update - current \"{Value.DisplayName}\" has timed out after {elapsed.TotalSeconds:F0}s with no window match");
                            noWindowMatchSince = null;
                            return base.SetValue(newValue, updatedTime);
                        }
                    }

                    Logger.Info($"Rejecting empty TrackedGame update - preserving current: {Value.DisplayName}");
                    return false;
                }
            }

            // Reset timeout when accepting a new valid TrackedGame
            if (trackedGame.HasValue && IsTrackedGameValid(trackedGame.Value))
            {
                noWindowMatchSince = null;
            }

            return base.SetValue(newValue, updatedTime);
        }

        public TrackedGameProperty(SystemManager inManager) : base(new TrackedGame(), null, Function.TrackedGame, inManager)
        {
        }
    }
}

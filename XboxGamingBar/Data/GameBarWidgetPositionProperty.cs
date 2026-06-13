using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side setting: the 1-based Game Bar widget-bar slot ClawTweaks occupies (Microsoft
    /// occupies the first two slots, so this is usually 3). Sent to the helper, which on Game Bar
    /// open taps RB (position − 1) times on the virtual controller to hop onto ClawTweaks.
    ///
    /// Default 1 (= RB hops 0 = auto-jump off; the user opts in by raising it). Persisted separately
    /// in LocalSettings by the widget (WidgetProperty itself does not persist) so it survives across
    /// sessions and is re-synced to the helper on connect.
    /// </summary>
    internal class GameBarWidgetPositionProperty : WidgetProperty<int>
    {
        public GameBarWidgetPositionProperty() : base(1, null, Function.GameBarWidgetPosition) { }
    }
}

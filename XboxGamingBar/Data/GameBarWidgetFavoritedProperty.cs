using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side signal: whether the ClawTweaks widget is currently favorited into the Game Bar home
    /// bar. Read live from <c>XboxGameBarWidget.Favorited</c> and re-sent on <c>FavoritedChanged</c>.
    /// This is the ONLY reliable "is CTW in the bar" signal — the Game Bar profile files do not persist
    /// it (see reverse_engineered/RE_GameBar_WidgetBar_Order.md). Pushed to the helper, which mirrors it
    /// into the Center status snapshot so onboarding's "add CTW to the Game Bar" step auto-completes the
    /// moment the user favorites it. Default false (assume not in the bar until the widget reports).
    /// </summary>
    internal class GameBarWidgetFavoritedProperty : WidgetProperty<bool>
    {
        public GameBarWidgetFavoritedProperty() : base(false, null, Function.GameBarWidgetFavorited) { }
    }
}

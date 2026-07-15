using System.Windows;
using System.Windows.Controls;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// Purely informational scaffold page for phases whose real logic isn't wired yet. Shows a title
    /// and description only — no action button (so there is never a visible Ⓐ that does nothing).
    /// </summary>
    public sealed class PlaceholderPhase : PhaseBase
    {
        private readonly string _title;
        public override string Title => _title;

        public PlaceholderPhase(string title, string description, PhaseState initialState = PhaseState.Ok)
        {
            _title = title;
            State = initialState;

            var stack = new StackPanel { Margin = new Thickness(4) };
            stack.Children.Add(UiHelpers.Title(title));
            stack.Children.Add(UiHelpers.Body(description));
            Content = stack;
        }
    }
}

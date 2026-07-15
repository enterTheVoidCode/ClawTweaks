using System;
using ClawTweaksSetup.Navigation;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// A single button-bound action a phase exposes (e.g. A = Install, Y = Re-check). Rendered as a
    /// big glyph + label in the footer action bar and invoked when the mapped controller button is
    /// pressed (or the chip is clicked with the mouse).
    /// </summary>
    public sealed class PhaseAction
    {
        public PadButton Button { get; }
        public string Label { get; }
        public Action Invoke { get; }

        /// <summary>When false the chip is dimmed and the button press is ignored.</summary>
        public Func<bool> IsEnabled { get; }

        public PhaseAction(PadButton button, string label, Action invoke, Func<bool> isEnabled = null)
        {
            Button = button;
            Label = label;
            Invoke = invoke;
            IsEnabled = isEnabled ?? (() => true);
        }
    }
}

namespace ClawTweaksSetup.Navigation
{
    /// <summary>
    /// The controller buttons the wizard reacts to. Navigation is deliberately NOT a roaming focus
    /// model — instead every on-screen action is bound to a fixed button and shown with its glyph,
    /// so the user always knows exactly which button does what (handheld-friendly).
    /// </summary>
    public enum PadButton
    {
        A,     // primary / confirm action of the current phase
        B,     // back
        X,     // (reserved for a secondary action if a phase needs it)
        Y,     // re-check / refresh
        Menu,  // continue to next phase (blocked until the phase allows it)

        // Discrete D-Pad presses (edge-triggered, like the face buttons above) — NOT rendered as
        // footer chips (no fixed-button glyph makes sense for "move"). Used by screens with a real
        // list/grid selection, e.g. CenterMenuWindow's build picker. Windows that don't bind these
        // simply never see them dispatched (MainWindow's phases never register them).
        Up, Down, Left, Right,
    }
}

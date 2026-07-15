using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace ClawTweaksSetup.Phases
{
    /// <summary>
    /// Result of a phase's live self-check, driving the header stepper colour and whether the user
    /// may continue.
    /// </summary>
    public enum PhaseState
    {
        Pending,   // not evaluated yet
        Working,   // an action is running
        Ok,        // green — requirement satisfied, may continue
        Action,    // yellow — user action recommended, may continue with a warning
        Blocked,   // red — must be resolved before continuing
    }

    /// <summary>
    /// Base class for every wizard page. A phase is a self-contained, idempotent step that measures
    /// live system state in <see cref="OnEnter"/>, exposes its button-bound actions, and reports
    /// whether the user may continue.
    /// </summary>
    public abstract class PhaseBase : UserControl
    {
        /// <summary>Short title shown in the header stepper.</summary>
        public abstract string Title { get; }

        private PhaseState _state = PhaseState.Pending;
        public PhaseState State
        {
            get => _state;
            protected set
            {
                if (_state == value) return;
                _state = value;
                StateChanged?.Invoke();
            }
        }

        /// <summary>Ok/Action allow continue; Pending/Working/Blocked do not.</summary>
        public bool CanContinue => State == PhaseState.Ok || State == PhaseState.Action;

        /// <summary>Raised whenever state OR the available actions change, so the shell refreshes chrome.</summary>
        public event Action StateChanged;

        /// <summary>Phases call this when only their action set changed (e.g. a button became enabled).</summary>
        protected void RaiseActionsChanged() => StateChanged?.Invoke();

        /// <summary>
        /// The phase-specific actions (typically A = primary, Y = re-check). The shell adds the
        /// global Back (B) and Continue (Menu) actions automatically.
        /// </summary>
        public virtual IReadOnlyList<PhaseAction> Actions => Array.Empty<PhaseAction>();

        /// <summary>Called every time the phase becomes the active page. Re-measures system state.</summary>
        public virtual void OnEnter() { }
    }
}

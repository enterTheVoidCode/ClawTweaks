using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ClawTweaksSetup.Navigation;
using ClawTweaksSetup.Phases;
using ClawTweaksSetup.Ui;

namespace ClawTweaksSetup
{
    public partial class MainWindow : Window
    {
        private readonly List<PhaseBase> _phases = new List<PhaseBase>();
        private int _index = -1;
        private XInputNavigator _nav;

        // The currently rendered actions, keyed by their controller button, so a pad press maps
        // straight to the action (no roaming focus).
        private readonly Dictionary<PadButton, PhaseAction> _liveActions = new Dictionary<PadButton, PhaseAction>();

        public MainWindow()
        {
            InitializeComponent();

            _phases.Add(new DetectPhase());
            _phases.Add(new ControllerPhase());
            _phases.Add(new ToolsPhase());
            _phases.Add(new InstallPhase());
            _phases.Add(new FinalizePhase());

            foreach (var p in _phases)
                p.StateChanged += OnPhaseChanged;

            BuildStepper();

            Loaded += (_, __) =>
            {
                _nav = new XInputNavigator(this);
                _nav.ButtonPressed += OnPadButton;
                _nav.ScrollRequested += d => Dispatcher.Invoke(() =>
                    ContentScroller.ScrollToVerticalOffset(ContentScroller.VerticalOffset + d));
                _nav.Start();
                GoTo(0);
            };
            Closed += (_, __) => _nav?.Dispose();

            // Keyboard fallbacks for desk testing: Esc/Backspace = Back, Enter = Continue.
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Back) { Invoke(PadButton.B); e.Handled = true; }
                else if (e.Key == Key.Enter) { Invoke(PadButton.Menu); e.Handled = true; }
            };
        }

        private void OnPadButton(PadButton b) => Dispatcher.Invoke(() => Invoke(b));

        /// <summary>Runs the action bound to a button (if enabled). Back/Continue are handled globally.</summary>
        private void Invoke(PadButton b)
        {
            if (!_liveActions.TryGetValue(b, out var action)) return;
            if (!action.IsEnabled()) return;
            action.Invoke();
        }

        #region Stepper header
        // Phases the user has completed and moved past (rendered with a checkmark).
        private readonly HashSet<int> _completed = new HashSet<int>();

        private void BuildStepper() => RefreshStepper();

        private void RefreshStepper()
        {
            var subtle = (Brush)Application.Current.Resources["SubtleTextBrush"];
            var text = (Brush)Application.Current.Resources["TextBrush"];
            var accent = (Brush)Application.Current.Resources["AccentBrush"];

            Stepper.Items.Clear();
            for (int i = 0; i < _phases.Count; i++)
            {
                bool current = i == _index;
                bool done = _completed.Contains(i);

                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 26, 0) };

                FrameworkElement marker;
                if (done)
                {
                    // Completed → green checkmark badge.
                    marker = UiHelpers.Badge(StatusKind.Ok, 22);
                }
                else if (current)
                {
                    // Active phase → filled accent dot inside a ring, so it clearly reads as "in progress".
                    var ring = new System.Windows.Shapes.Ellipse
                    {
                        Width = 22, Height = 22,
                        Stroke = accent, StrokeThickness = 2,
                        Fill = System.Windows.Media.Brushes.Transparent,
                    };
                    var core = new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Fill = accent };
                    marker = new Grid { Width = 22, Height = 22 };
                    ((Grid)marker).Children.Add(ring);
                    ((Grid)marker).Children.Add(core);
                }
                else
                {
                    // Future → hollow grey dot.
                    marker = new System.Windows.Shapes.Ellipse
                    {
                        Width = 16, Height = 16,
                        Stroke = subtle, StrokeThickness = 2,
                        Fill = System.Windows.Media.Brushes.Transparent,
                    };
                }
                marker.VerticalAlignment = VerticalAlignment.Center;
                marker.Margin = new Thickness(0, 0, 10, 0);

                var label = new TextBlock
                {
                    Text = _phases[i].Title,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 17,
                    Foreground = current ? text : (done ? UiHelpers.Ok : subtle),
                    FontWeight = current ? FontWeights.Bold : FontWeights.Normal,
                };

                item.Children.Add(marker);
                item.Children.Add(label);
                Stepper.Items.Add(item);
            }
        }
        #endregion

        #region Footer action bar
        private void RefreshActionBar()
        {
            _liveActions.Clear();
            ActionBar.Children.Clear();

            var phase = _phases[_index];
            var actions = new List<PhaseAction>();

            // B = Back (except on the first phase).
            if (_index > 0)
                actions.Add(new PhaseAction(PadButton.B, "Zurück", GoBack));

            // Phase-specific actions (A = primary, Y = re-check, ...).
            actions.AddRange(phase.Actions);

            // Menu (☰) = Continue / Finish — only enabled when the phase allows it.
            bool last = _index == _phases.Count - 1;
            actions.Add(new PhaseAction(
                PadButton.Menu,
                last ? "Fertig" : "Weiter",
                GoForward,
                () => phase.CanContinue));

            foreach (var a in actions)
            {
                _liveActions[a.Button] = a;
                ActionBar.Children.Add(BuildChip(a));
            }
        }

        private UIElement BuildChip(PhaseAction a) =>
            ActionBarBuilder.BuildChip(a.Button, a.Label, a.IsEnabled(), () => Invoke(a.Button));
        #endregion

        #region Navigation
        private void GoTo(int index)
        {
            if (index < 0 || index >= _phases.Count) return;
            _index = index;
            var phase = _phases[index];
            PhaseHost.Content = phase;
            ContentScroller.ScrollToTop();
            phase.OnEnter();

            RefreshStepper();
            RefreshActionBar();
        }

        private void GoBack()
        {
            if (_index > 0) GoTo(_index - 1);
        }

        private void GoForward()
        {
            if (!_phases[_index].CanContinue) return;      // hard gate: cannot skip an unfinished step
            _completed.Add(_index);                        // mark this phase done (checkmark in stepper)
            if (_index < _phases.Count - 1) GoTo(_index + 1);
            else Close();
        }

        private void OnPhaseChanged()
        {
            RefreshStepper();
            RefreshActionBar();
        }
        #endregion
    }
}

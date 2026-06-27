using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    /// <summary>
    /// "Tab Settings" — lets the user reorder the nav tabs, hide non-mandatory ones, and optionally
    /// always open a specific tab when the Game Bar opens. Three independent knobs:
    ///   (a) order   → reorders <see cref="MainNavPanel"/> children
    ///   (b) visible → hides a tab's nav RadioButton (mandatory tabs have no checkbox)
    ///   (c) default → on Game Bar open, jump to a chosen tab (default off = keep last)
    /// Mirrors the OSD stats reorder UI. Persisted in LocalSettings; applied after device gating +
    /// onboarding layout so it is the final authority on order.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private sealed class TabSettingVm : INotifyPropertyChanged
        {
            public string Tag { get; set; }
            public string DisplayName { get; set; }
            /// <summary>Mandatory tabs (System, Setup) render the name only — no hide checkbox.</summary>
            public bool CanHide { get; set; }

            private bool _isVisible = true;
            public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChanged(); } }

            private bool _canMoveUp;
            public bool CanMoveUp { get => _canMoveUp; set { _canMoveUp = value; OnPropertyChanged(); } }

            private bool _canMoveDown;
            public bool CanMoveDown { get => _canMoveDown; set { _canMoveDown = value; OnPropertyChanged(); } }

            public Visibility CheckBoxVisibility => CanHide ? Visibility.Visible : Visibility.Collapsed;
            public Visibility LabelVisibility => CanHide ? Visibility.Collapsed : Visibility.Visible;

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private sealed class TabDef
        {
            public string Tag;
            public string Name;
            public TabDef(string tag, string name) { Tag = tag; Name = name; }
        }

        // Manageable tabs: tag → display label. This array's order is the factory-default tab order.
        private static readonly TabDef[] _manageableTabs =
        {
            new TabDef("Quick",       "Main"),
            new TabDef("Performance", "Performance"),
            new TabDef("Legion",      "Controls"),
            new TabDef("Display",     "Display"),
            new TabDef("Fan",         "Fan"),
            new TabDef("Scaling",     "Lossless Scaling"),
            new TabDef("Game",        "Profiles"),
            new TabDef("Drivers",     "Drivers"),
            new TabDef("GPD",         "GPD"),
            new TabDef("AMD",         "Graphics"),
            new TabDef("System",      "System"),
            new TabDef("Onboarding",  "Setup"),
        };

        // Non-hideable (no checkbox) — the Tab Settings host + the Setup tab.
        private static readonly HashSet<string> _mandatoryTabs =
            new HashSet<string> { "System", "Onboarding" };

        // Device-gated tabs: their base visibility is owned by device logic. We only ever HIDE these
        // on user request — never force-show, so we don't surface a tab that doesn't apply.
        private static readonly HashSet<string> _deviceGatedTabs =
            new HashSet<string> { "Display", "Fan", "Scaling", "Legion", "GPD", "AMD", "Trigger" };

        private const string TabOrderKey = "TabOrder";
        private const string TabHiddenKey = "TabHidden";
        private const string DefaultTabEnabledKey = "DefaultTabEnabled";
        private const string DefaultTabKey = "DefaultTab";

        private readonly ObservableCollection<TabSettingVm> _tabSettingVms = new ObservableCollection<TabSettingVm>();
        private bool _tabSettingsLoading;

        // Collapsible section state (default collapsed, like other cards). Two copies: System + Setup tab.
        private bool _tabSettingsExpandedSystem;
        private bool _tabSettingsExpandedOnboarding;

        // Device-gated tabs become available when device logic first shows them. We remember that for
        // the session so un-hiding a device-gated tab (e.g. Lossless Scaling, Fan) actually re-shows it.
        // Without this, a hidden device-gated tab is Collapsed and would be indistinguishable from a
        // device-unavailable one → it could never be re-shown and would vanish from the list.
        private readonly HashSet<string> _everAvailableTabs = new HashSet<string>();

        /// <summary>A tab is manageable when it's not device-gated (always available) or device logic
        /// has shown it at least once this session.</summary>
        private bool IsTabAvailable(string tag)
            => !_deviceGatedTabs.Contains(tag) || _everAvailableTabs.Contains(tag);

        /// <summary>Remember any device-gated tab that is currently visible (i.e. device logic enabled
        /// it) so it stays manageable even after the user hides it.</summary>
        private void CaptureAvailableTabs()
        {
            if (MainNavPanel == null) return;
            foreach (var g in _deviceGatedTabs)
            {
                var rb = NavItemForTag(g);
                if (rb != null && MainNavPanel.Children.Contains(rb) && rb.Visibility == Visibility.Visible)
                    _everAvailableTabs.Add(g);
            }
        }

        private RadioButton NavItemForTag(string tag)
        {
            switch (tag)
            {
                case "Quick": return QuickNavItem;
                case "Onboarding": return OnboardingNavItem;
                case "Performance": return PerformanceNavItem;
                case "Display": return DisplayNavItem;
                case "Legion": return LegionNavItem;
                case "Fan": return FanNavItem;
                case "Scaling": return ScalingNavItem;
                case "Game": return ProfilesNavItem;
                case "System": return SystemNavItem;
                case "Drivers": return DriverNavItem;
                case "AMD": return GraphicsNavItem;
                case "GPD": return GPDNavItem;
                case "Trigger": return TriggerNavItem;
                default: return null;
            }
        }

        // ── persistence helpers ─────────────────────────────────────────────────────────────────
        private List<string> LoadSavedTabOrder()
        {
            var s = ApplicationData.Current.LocalSettings.Values[TabOrderKey] as string;
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        private HashSet<string> LoadHiddenTabs()
        {
            var s = ApplicationData.Current.LocalSettings.Values[TabHiddenKey] as string ?? "";
            return new HashSet<string>(s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
        }

        private void SaveHiddenTabs(IEnumerable<string> tags)
            => ApplicationData.Current.LocalSettings.Values[TabHiddenKey] = string.Join(",", tags);

        /// <summary>Saved order if present, else factory order; any manageable tag missing from the
        /// saved order (e.g. a tab added by an update) is appended in factory order.</summary>
        private List<string> EffectiveTabOrder()
        {
            var def = _manageableTabs.Select(t => t.Tag).ToList();
            var saved = LoadSavedTabOrder();
            if (saved == null) return def;
            var result = saved.Where(def.Contains).ToList();
            foreach (var t in def) if (!result.Contains(t)) result.Add(t);
            return result;
        }

        private void SaveOrderFromShown(List<string> shownOrder)
        {
            // Persist the user-arranged (shown) order, then append any manageable tag not currently
            // shown (hidden-by-device), so their relative order survives until they become available.
            var full = new List<string>(shownOrder);
            foreach (var t in _manageableTabs.Select(t => t.Tag))
                if (!full.Contains(t)) full.Add(t);
            ApplicationData.Current.LocalSettings.Values[TabOrderKey] = string.Join(",", full);
        }

        // ── apply to the real nav bar ───────────────────────────────────────────────────────────
        /// <summary>Applies saved visibility + order to the nav bar. Idempotent and defensive; safe to
        /// call on every Game Bar open and after device gating / onboarding layout.</summary>
        internal void ApplyTabPrefs()
        {
            try
            {
                if (MainNavPanel == null) return;
                CaptureAvailableTabs();
                var hidden = LoadHiddenTabs();

                // (b) Visibility — for every AVAILABLE tab: hide it if the user hid it, otherwise show it.
                // Mandatory tabs are never hidden. Device-unavailable tabs are left to device logic
                // (untouched), so we never surface a tab that doesn't apply to this device.
                foreach (var def in _manageableTabs)
                {
                    var tag = def.Tag;
                    var rb = NavItemForTag(tag);
                    if (rb == null) continue;
                    if (!IsTabAvailable(tag)) continue;
                    bool userHidden = hidden.Contains(tag) && !_mandatoryTabs.Contains(tag);
                    rb.Visibility = userHidden ? Visibility.Collapsed : Visibility.Visible;
                }

                // (a) Order. While onboarding is incomplete, force the Setup tab to the very FIRST
                // position — a live-only override on top of the saved/factory order so the user is
                // steered straight to setup. It is never persisted (the reorder-list UI and
                // SaveOrderFromShown keep using the pure EffectiveTabOrder), so the user's real tab
                // order is restored automatically once onboarding completes.
                var order = EffectiveTabOrder();
                if (OnboardingIncompleteForOrder())
                {
                    order = new List<string>(order);
                    order.Remove("Onboarding");
                    order.Insert(0, "Onboarding");
                }
                ReorderNavChildren(order);

                // Never leave a hidden tab selected.
                EnsureValidActiveTab();
            }
            catch (Exception ex) { Logger.Warn($"ApplyTabPrefs failed: {ex.Message}"); }
        }

        private void ReorderNavChildren(List<string> order)
        {
            var desired = order
                .Select(NavItemForTag)
                .Where(rb => rb != null && MainNavPanel.Children.Contains(rb))
                .Cast<UIElement>()
                .ToList();
            if (desired.Count == 0) return;

            var managedSet = new HashSet<UIElement>(desired);

            // Current relative order of the managed items in the panel.
            var current = MainNavPanel.Children.Where(managedSet.Contains).ToList();
            if (current.SequenceEqual(desired)) return; // already arranged → don't disturb focus/selection

            int firstManagedIdx = -1;
            for (int i = 0; i < MainNavPanel.Children.Count; i++)
                if (managedSet.Contains(MainNavPanel.Children[i])) { firstManagedIdx = i; break; }
            if (firstManagedIdx < 0) return;

            foreach (var rb in desired) MainNavPanel.Children.Remove(rb);
            int insertAt = Math.Min(firstManagedIdx, MainNavPanel.Children.Count);
            for (int i = 0; i < desired.Count; i++)
                MainNavPanel.Children.Insert(insertAt + i, desired[i]);
        }

        /// <summary>If the currently-checked tab is now hidden/absent, select the first visible tab.</summary>
        private void EnsureValidActiveTab()
        {
            try
            {
                var visible = MainNavPanel.Children.OfType<RadioButton>()
                    .Where(rb => rb.Visibility == Visibility.Visible).ToList();
                if (visible.Count == 0) return;
                var active = visible.FirstOrDefault(rb => rb.IsChecked == true);
                if (active == null)
                    visible[0].IsChecked = true;
            }
            catch { /* best effort */ }
        }

        // ── the reorder list UI (shared collection bound to the System + Onboarding copies) ──────
        private void RefreshTabSettingsList()
        {
            _tabSettingsLoading = true;
            try
            {
                CaptureAvailableTabs();
                var hidden = LoadHiddenTabs();
                var shown = EffectiveTabOrder().Where(IsTabAvailable).ToList();
                var nameMap = _manageableTabs.ToDictionary(t => t.Tag, t => t.Name);

                _tabSettingVms.Clear();
                for (int i = 0; i < shown.Count; i++)
                {
                    var tag = shown[i];
                    _tabSettingVms.Add(new TabSettingVm
                    {
                        Tag = tag,
                        DisplayName = nameMap.TryGetValue(tag, out var n) ? n : tag,
                        CanHide = !_mandatoryTabs.Contains(tag),
                        IsVisible = !hidden.Contains(tag),
                        CanMoveUp = i > 0,
                        CanMoveDown = i < shown.Count - 1,
                    });
                }

                if (TabSettingsItemsControlSystem != null)
                    TabSettingsItemsControlSystem.ItemsSource = _tabSettingVms;
                if (TabSettingsItemsControlOnboarding != null)
                    TabSettingsItemsControlOnboarding.ItemsSource = _tabSettingVms;

                RefreshDefaultTabUi();
            }
            finally { _tabSettingsLoading = false; }
        }

        private void UpdateTabMoveFlags()
        {
            for (int i = 0; i < _tabSettingVms.Count; i++)
            {
                _tabSettingVms[i].CanMoveUp = i > 0;
                _tabSettingVms[i].CanMoveDown = i < _tabSettingVms.Count - 1;
            }
        }

        private int IndexOfTabVm(string tag)
        {
            for (int i = 0; i < _tabSettingVms.Count; i++)
                if (_tabSettingVms[i].Tag == tag) return i;
            return -1;
        }

        private void TabSettingMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string tag)) return;
            int idx = IndexOfTabVm(tag);
            if (idx <= 0) return;
            _tabSettingVms.Move(idx, idx - 1);
            CommitTabOrder();
        }

        private void TabSettingMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string tag)) return;
            int idx = IndexOfTabVm(tag);
            if (idx < 0 || idx >= _tabSettingVms.Count - 1) return;
            _tabSettingVms.Move(idx, idx + 1);
            CommitTabOrder();
        }

        private void CommitTabOrder()
        {
            UpdateTabMoveFlags();
            SaveOrderFromShown(_tabSettingVms.Select(v => v.Tag).ToList());
            ApplyTabPrefs();
        }

        private void TabSettingVisible_Changed(object sender, RoutedEventArgs e)
        {
            if (_tabSettingsLoading) return;
            if (!((sender as CheckBox)?.Tag is string tag)) return;
            if (_mandatoryTabs.Contains(tag)) return; // mandatory: ignore (shouldn't have a checkbox anyway)

            var hidden = LoadHiddenTabs();
            bool isChecked = (sender as CheckBox).IsChecked == true;
            if (isChecked) hidden.Remove(tag); else hidden.Add(tag);
            SaveHiddenTabs(hidden);

            ApplyTabPrefs();
            RefreshDefaultTabUi(); // a hidden tab must drop out of the default-tab picker
        }

        // ── default-tab-on-open (option independent of order/visibility) ─────────────────────────
        private void RefreshDefaultTabUi()
        {
            _tabSettingsLoading = true;
            try
            {
                bool enabled = ApplicationData.Current.LocalSettings.Values[DefaultTabEnabledKey] is bool b && b;
                string savedTag = ApplicationData.Current.LocalSettings.Values[DefaultTabKey] as string;

                // Candidate tabs = currently visible nav items, in their on-screen order.
                var nameMap = _manageableTabs.ToDictionary(t => t.Tag, t => t.Name);
                var candidates = MainNavPanel.Children.OfType<RadioButton>()
                    .Where(rb => rb.Visibility == Visibility.Visible)
                    .Select(rb => rb.Tag?.ToString())
                    .Where(t => !string.IsNullOrEmpty(t) && nameMap.ContainsKey(t))
                    .ToList();

                // If the saved default tab is no longer visible, the jump silently falls away.
                if (!string.IsNullOrEmpty(savedTag) && !candidates.Contains(savedTag))
                    savedTag = null;

                FillDefaultTabCombo(DefaultTabComboSystem, candidates, nameMap, savedTag);
                FillDefaultTabCombo(DefaultTabComboOnboarding, candidates, nameMap, savedTag);

                if (DefaultTabToggleSystem != null) DefaultTabToggleSystem.IsOn = enabled;
                if (DefaultTabToggleOnboarding != null) DefaultTabToggleOnboarding.IsOn = enabled;
                if (DefaultTabComboSystem != null) DefaultTabComboSystem.IsEnabled = enabled;
                if (DefaultTabComboOnboarding != null) DefaultTabComboOnboarding.IsEnabled = enabled;
            }
            finally { _tabSettingsLoading = false; }
        }

        private void FillDefaultTabCombo(ComboBox combo, List<string> candidates,
            Dictionary<string, string> nameMap, string selectedTag)
        {
            if (combo == null) return;
            combo.Items.Clear();
            foreach (var tag in candidates)
                combo.Items.Add(new ComboBoxItem { Content = nameMap[tag], Tag = tag });
            if (!string.IsNullOrEmpty(selectedTag))
            {
                for (int i = 0; i < combo.Items.Count; i++)
                    if (combo.Items[i] is ComboBoxItem ci && (ci.Tag as string) == selectedTag)
                    { combo.SelectedIndex = i; break; }
            }
        }

        private void DefaultTabToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_tabSettingsLoading) return;
            bool on = (sender as ToggleSwitch)?.IsOn ?? false;
            ApplicationData.Current.LocalSettings.Values[DefaultTabEnabledKey] = on;
            RefreshDefaultTabUi();
        }

        private void DefaultTabCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_tabSettingsLoading) return;
            if ((sender as ComboBox)?.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
            {
                ApplicationData.Current.LocalSettings.Values[DefaultTabKey] = tag;
                RefreshDefaultTabUi();
            }
        }

        /// <summary>On Game Bar open: if enabled and the chosen tab is currently visible, jump to it.
        /// Default (option off) keeps the last position. A hidden/removed target is ignored.</summary>
        internal bool ApplyDefaultTabOnOpen()
        {
            try
            {
                if (!(ApplicationData.Current.LocalSettings.Values[DefaultTabEnabledKey] is bool en) || !en) return false;
                var tag = ApplicationData.Current.LocalSettings.Values[DefaultTabKey] as string;
                if (string.IsNullOrEmpty(tag)) return false;
                var rb = NavItemForTag(tag);
                if (rb != null && rb.Visibility == Visibility.Visible)
                {
                    // Check the nav item (themes the pill / moves focus) AND force the actual content
                    // switch. Relying on the Checked event alone is not enough: after a tab reorder the
                    // item can already be IsChecked=true (so Checked never fires) or a competing setter
                    // (Loaded's Quick default) overrides it — leaving the pill on the target tab but the
                    // content on the previous one. Calling NavRadioButton_Checked directly is idempotent
                    // and guarantees the matching ScrollViewer is shown.
                    if (rb.IsChecked != true) rb.IsChecked = true;
                    NavRadioButton_Checked(rb, null);
                    Logger.Info($"Default-tab on open → '{tag}'");
                    return true;
                }
            }
            catch (Exception ex) { Logger.Debug($"ApplyDefaultTabOnOpen: {ex.Message}"); }
            return false;
        }

        // ── collapsible section (default collapsed) + controller navigation ─────────────────────
        private void TabSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            bool system = ReferenceEquals(sender, TabSettingsExpandButtonSystem);
            bool expanded = system
                ? (_tabSettingsExpandedSystem = !_tabSettingsExpandedSystem)
                : (_tabSettingsExpandedOnboarding = !_tabSettingsExpandedOnboarding);

            var content = system ? TabSettingsContentSystem : TabSettingsContentOnboarding;
            var icon = system ? TabSettingsExpandIconSystem : TabSettingsExpandIconOnboarding;
            if (content != null) content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (icon != null) icon.Glyph = expanded ? "" : ""; // ChevronUp / ChevronDown
            if (expanded) RefreshTabSettingsList(); // build the rows now that the panel is visible
        }

        // Expand toggle: D-pad Down enters the panel when expanded; when collapsed, leave it unhandled
        // so normal Down navigation moves to the next card. Controller (XYFocus) navigation inside the
        // reorder list only works once focus sits on a row's hide-checkbox — from there Down walks
        // checkbox→checkbox and across to the sort arrows. So we explicitly land on the FIRST row's
        // checkbox. The order is fully user-reorderable, so we key off row index 0 (the topmost item),
        // never a fixed tab.
        private void TabSettingsExpandButton_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Down && e.Key != Windows.System.VirtualKey.GamepadDPadDown) return;
            bool system = ReferenceEquals(sender, TabSettingsExpandButtonSystem);
            bool expanded = system ? _tabSettingsExpandedSystem : _tabSettingsExpandedOnboarding;
            var content = system ? TabSettingsContentSystem : TabSettingsContentOnboarding;
            var items = system ? TabSettingsItemsControlSystem : TabSettingsItemsControlOnboarding;
            if (!expanded || content == null) return;

            if (TryFocusFirstTabRowControl(items))
            {
                e.Handled = true;
                return;
            }

            // Fallback: first focusable anywhere in the panel (e.g. list not yet realized).
            var first = Windows.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(content) as Control;
            if (first != null)
            {
                first.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        }

        /// <summary>Focus the first reorder row's hide-checkbox (the controller-navigation entry point).
        /// Order is user-reorderable, so this targets row index 0 — whatever tab currently sits on top.
        /// If that row is a mandatory tab (no checkbox, e.g. System/Setup moved to the top), falls back
        /// to the row's first focusable control (its sort arrows) so focus still enters at the top.</summary>
        private bool TryFocusFirstTabRowControl(ItemsControl items)
        {
            if (items == null) return false;
            int count = items.Items?.Count ?? 0;
            if (count == 0) return false;

            // Ensure the item containers exist before we ask for them (plain ItemsControl, no
            // virtualization — UpdateLayout realizes all rows).
            items.UpdateLayout();

            for (int i = 0; i < count; i++)
            {
                if (!(items.ContainerFromIndex(i) is DependencyObject container)) continue;

                var cb = FindVisibleCheckBox(container);
                if (cb != null) { cb.Focus(FocusState.Keyboard); return true; }

                if (Windows.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(container) is Control ctrl)
                { ctrl.Focus(FocusState.Keyboard); return true; }
            }
            return false;
        }

        private static CheckBox FindVisibleCheckBox(DependencyObject root)
        {
            if (root is CheckBox cb && cb.Visibility == Visibility.Visible) return cb;
            int n = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindVisibleCheckBox(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XboxGamingBarHelper.Devices.Libraries.Legion;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Generic gamepad button SWAP / remap applied to the virtual ViGEm controller.
    ///
    /// This is the Controller tab "Re-Map Specific Buttons to Another Button" feature
    /// (the three combined dropdowns: source button -> mode -> target button), distinct
    /// from the M1/M2/back-button remaps handled by <see cref="ApplyLegionUserspaceRemaps"/>.
    ///
    /// The M1/M2 remaps only INJECT a virtual button when an extra/aux hardware button is
    /// pressed. Here the source is a STANDARD XInput button (A/B/X/Y, D-pad, LB/RB, LT/RT,
    /// Start/Select, stick clicks), so applying a remap means: CLEAR the source button on
    /// the forwarded state and SET the target action. That is what makes a true A&lt;-&gt;B swap
    /// possible (the user configures A-&gt;B and B-&gt;A as two entries).
    ///
    /// Why this was missing: the 24-button mapping JSON was only routed to
    /// <c>LegionManager.ApplyGamepadButtonMappings()</c>, which pushes HID commands to a
    /// physical Legion Go controller. On the MSI Claw that device never connects, so the
    /// swaps never reached the ViGEm output. This file reads the very same JSON
    /// (<c>legionManager.LegionGamepadMapping.Value</c>, which is already persisted per
    /// global/per-game profile and pushed live + on profile switch) and applies it inside
    /// the forwarding loop, exactly like the M1/M2 path.
    ///
    /// Scope: Gamepad-mode targets only. Keyboard/Mouse remap modes are intentionally
    /// ignored here. Stick-direction SOURCES (LSUp/RSLeft/...) are out of scope too; only
    /// digital buttons and the analog triggers are accepted as a source.
    /// </summary>
    internal partial class ControllerEmulationManager
    {
        private const byte GamepadSwapTriggerThreshold = 30;

        private sealed class GamepadButtonSwapEntry
        {
            public ushort SourceButtonMask;   // 0 when the source is an analog trigger
            public bool SourceIsLeftTrigger;
            public bool SourceIsRightTrigger;
            public RemapAction[] Actions;
            public bool Pressed;              // transient per-frame flag (single forwarding thread)
        }

        private GamepadButtonSwapEntry[] gamepadButtonSwaps = Array.Empty<GamepadButtonSwapEntry>();
        private long gamepadButtonSwapCacheTicksUtc;
        private string gamepadButtonSwapCachedJson;

        /// <summary>
        /// Applies the generic source-&gt;target button remaps onto the forwarded gamepad
        /// state. Called once per forwarding iteration after the physical state has been
        /// read (and after M1/M2 injection), before it is submitted to ViGEm.
        /// </summary>
        private void ApplyGamepadButtonSwaps(ref XINPUT_GAMEPAD gamepad)
        {
            RefreshGamepadButtonSwapsIfNeeded();

            GamepadButtonSwapEntry[] swaps = gamepadButtonSwaps;
            if (swaps == null || swaps.Length == 0)
            {
                return;
            }

            // Snapshot the ORIGINAL state so a two-entry swap (A->B and B->A) resolves
            // atomically instead of cascading (clear A, set B, then re-read B and clear it).
            ushort origButtons = gamepad.wButtons;
            byte origLeftTrigger = gamepad.bLeftTrigger;
            byte origRightTrigger = gamepad.bRightTrigger;

            ushort clearButtonMask = 0;
            bool clearLeftTrigger = false;
            bool clearRightTrigger = false;
            bool anyPressed = false;

            // Phase 1: decide which sources are pressed (from the snapshot) and accumulate
            // the bits/triggers to clear.
            for (int i = 0; i < swaps.Length; i++)
            {
                GamepadButtonSwapEntry swap = swaps[i];
                bool pressed = IsGamepadSwapSourcePressed(swap, origButtons, origLeftTrigger, origRightTrigger);
                swap.Pressed = pressed;
                if (!pressed)
                {
                    continue;
                }

                anyPressed = true;
                clearButtonMask |= swap.SourceButtonMask;
                if (swap.SourceIsLeftTrigger) clearLeftTrigger = true;
                if (swap.SourceIsRightTrigger) clearRightTrigger = true;
            }

            if (!anyPressed)
            {
                return;
            }

            // Phase 2: clear the pressed sources, then inject the targets.
            gamepad.wButtons = (ushort)(origButtons & ~clearButtonMask);
            if (clearLeftTrigger) gamepad.bLeftTrigger = 0;
            if (clearRightTrigger) gamepad.bRightTrigger = 0;

            for (int i = 0; i < swaps.Length; i++)
            {
                GamepadButtonSwapEntry swap = swaps[i];
                if (!swap.Pressed)
                {
                    continue;
                }

                RemapAction[] actions = swap.Actions;
                for (int a = 0; a < actions.Length; a++)
                {
                    ApplyGamepadRemapAction(ref gamepad, actions[a]);
                }
            }
        }

        private static bool IsGamepadSwapSourcePressed(GamepadButtonSwapEntry swap, ushort buttons, byte leftTrigger, byte rightTrigger)
        {
            if (swap.SourceIsLeftTrigger)
            {
                return leftTrigger >= GamepadSwapTriggerThreshold;
            }
            if (swap.SourceIsRightTrigger)
            {
                return rightTrigger >= GamepadSwapTriggerThreshold;
            }
            return swap.SourceButtonMask != 0 && (buttons & swap.SourceButtonMask) != 0;
        }

        private void RefreshGamepadButtonSwapsIfNeeded()
        {
            if (legionManager == null)
            {
                gamepadButtonSwaps = Array.Empty<GamepadButtonSwapEntry>();
                return;
            }

            long nowTicksUtc = DateTime.UtcNow.Ticks;
            if (gamepadButtonSwapCacheTicksUtc != 0 &&
                (nowTicksUtc - gamepadButtonSwapCacheTicksUtc) < LegionUserspaceRemapRefreshTicks)
            {
                return;
            }
            gamepadButtonSwapCacheTicksUtc = nowTicksUtc;

            string json = legionManager.LegionGamepadMapping?.Value;
            if (string.Equals(json, gamepadButtonSwapCachedJson, StringComparison.Ordinal))
            {
                return; // unchanged since last rebuild
            }
            gamepadButtonSwapCachedJson = json;
            gamepadButtonSwaps = BuildGamepadButtonSwaps(json);
        }

        private static GamepadButtonSwapEntry[] BuildGamepadButtonSwaps(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
            {
                return Array.Empty<GamepadButtonSwapEntry>();
            }

            var entries = new List<GamepadButtonSwapEntry>();

            // Outer dict shape: "ButtonName":{...},"ButtonName":{...}
            MatchCollection matches = Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*(\\{[^}]+\\})");
            foreach (Match match in matches)
            {
                string buttonName = match.Groups[1].Value;
                string mappingJson = match.Groups[2].Value;

                if (!TryGetGamepadSwapSource(buttonName, out ushort sourceMask, out bool isLeftTrigger, out bool isRightTrigger))
                {
                    continue; // stick-direction sources are out of scope for now
                }

                ButtonMappingParser.ParsedButtonMapping parsed = ButtonMappingParser.ParseExtended(mappingJson);
                // Gamepad-type only; Keyboard/Mouse remap modes are handled elsewhere.
                if (parsed.Type != 0)
                {
                    continue;
                }

                var actions = new List<RemapAction>();
                if (parsed.GamepadActions != null && parsed.GamepadActions.Length > 0)
                {
                    for (int i = 0; i < parsed.GamepadActions.Length; i++)
                    {
                        RemapAction mapped = RemapActionHelper.GetByIndex(parsed.GamepadActions[i]);
                        if (IsXinputRemapAction(mapped) && !actions.Contains(mapped))
                        {
                            actions.Add(mapped);
                        }
                    }
                }

                if (actions.Count == 0 && parsed.GamepadAction > 0)
                {
                    RemapAction single = RemapActionHelper.GetByIndex(parsed.GamepadAction);
                    if (IsXinputRemapAction(single))
                    {
                        actions.Add(single);
                    }
                }

                // No real target (Disabled / reset state) -> let the source pass through unchanged.
                if (actions.Count == 0)
                {
                    continue;
                }

                entries.Add(new GamepadButtonSwapEntry
                {
                    SourceButtonMask = sourceMask,
                    SourceIsLeftTrigger = isLeftTrigger,
                    SourceIsRightTrigger = isRightTrigger,
                    Actions = actions.ToArray(),
                });
            }

            return entries.ToArray();
        }

        /// <summary>
        /// Maps a widget source-button name (see GamepadButtonNames in the widget) to the
        /// XInput button mask / trigger it corresponds to. Returns false for stick-direction
        /// sources, which are intentionally not supported as a swap source yet.
        /// </summary>
        private static bool TryGetGamepadSwapSource(string buttonName, out ushort sourceMask, out bool isLeftTrigger, out bool isRightTrigger)
        {
            sourceMask = 0;
            isLeftTrigger = false;
            isRightTrigger = false;

            switch (buttonName)
            {
                case "A": sourceMask = XINPUT_GAMEPAD_A; return true;
                case "B": sourceMask = XINPUT_GAMEPAD_B; return true;
                case "X": sourceMask = XINPUT_GAMEPAD_X; return true;
                case "Y": sourceMask = XINPUT_GAMEPAD_Y; return true;
                case "LB": sourceMask = XINPUT_GAMEPAD_LEFT_SHOULDER; return true;
                case "RB": sourceMask = XINPUT_GAMEPAD_RIGHT_SHOULDER; return true;
                case "DPadUp": sourceMask = XINPUT_GAMEPAD_DPAD_UP; return true;
                case "DPadDown": sourceMask = XINPUT_GAMEPAD_DPAD_DOWN; return true;
                case "DPadLeft": sourceMask = XINPUT_GAMEPAD_DPAD_LEFT; return true;
                case "DPadRight": sourceMask = XINPUT_GAMEPAD_DPAD_RIGHT; return true;
                case "Start": sourceMask = XINPUT_GAMEPAD_START; return true;
                case "Select": sourceMask = XINPUT_GAMEPAD_BACK; return true;
                case "LSClick": sourceMask = XINPUT_GAMEPAD_LEFT_THUMB; return true;
                case "RSClick": sourceMask = XINPUT_GAMEPAD_RIGHT_THUMB; return true;
                case "LT": isLeftTrigger = true; return true;
                case "RT": isRightTrigger = true; return true;
                default: return false; // LSUp/LSDown/.../RSRight -> stick directions, out of scope
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XboxGamingBarHelper.Devices.Libraries.Legion;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Generic gamepad button SWAP / remap for the MSI Claw, applied to the outgoing virtual
    /// ViGEm controller inside <see cref="ProcessDirectInputState"/>.
    ///
    /// This is the Controller tab "Re-Map Specific Buttons to Another Button" feature
    /// (the three combined dropdowns: source button → mode → target). It supports two modes:
    ///   • Gamepad target  → the source XInput button is cleared and the target XInput button
    ///                        is injected (true A↔B swap when configured both ways).
    ///   • Keyboard target  → the source XInput button is cleared and, on the press edge, the
    ///                        configured key chord is fired through the helper's standard keyboard
    ///                        injector (same path tiles / actions / M1-M2 use). No Legion HID
    ///                        dependency — keys are converted from HID usage codes to injector
    ///                        tokens here, then handed to <see cref="KeyboardChordCallback"/>.
    /// Mouse target mode is intentionally NOT handled here (the Claw virtual mouse is wired up
    /// very specifically elsewhere).
    ///
    /// Why it lives here: on the MSI Claw the legacy <c>ControllerEmulationManager</c> forwarding
    /// loop (which has its own gamepad-only swap pass) is suppressed — ClawButtonMonitor is the
    /// active emulation path that drives ViGEm. <c>LegionManager.ApplyGamepadButtonMappings()</c>
    /// only pushes HID commands to a physical Legion Go controller, which never connects on the
    /// Claw, so without this the swaps never reached the virtual controller.
    ///
    /// Wiring: <c>Program.MSIClaw</c> routes <c>LegionManager.OnGamepadMappingChanged</c> (fired by
    /// the LegionGamepadMapping property on every live change / profile switch / restore) to
    /// <see cref="ConfigureGamepadSwaps"/>, and sets <see cref="KeyboardChordCallback"/>.
    ///
    /// Source scope: digital buttons + the analog triggers only. Stick-direction SOURCES
    /// (LSUp/RSLeft/…) are out of scope (stick directions ARE valid gamepad targets).
    /// </summary>
    internal partial class ClawButtonMonitor
    {
        // Analog trigger source counts as "pressed" once it crosses this 0–255 threshold.
        private const byte GamepadSwapTriggerThreshold = 30;

        /// <summary>
        /// Fires a "+"-joined key chord (e.g. "LCtrl+LShift+A") through the helper's standard
        /// keyboard injector. Wired by Program.MSIClaw to SendKeyboardShortcutViaInputInjector.
        /// </summary>
        public Action<string> KeyboardChordCallback;

        private sealed class GamepadSwapEntry
        {
            public ushort SourceButtonMask;   // 0 when the source is an analog trigger
            public bool SourceIsLeftTrigger;
            public bool SourceIsRightTrigger;
            public string SourceButtonName;   // canonical name (A/B/DPadUp/LT…), for FW-remap suppression

            // Gamepad-target mode: the XInput actions to inject while the source is held.
            public RemapAction[] Actions;

            // Keyboard-target mode: fire KeyboardToken once on the press edge.
            public bool IsKeyboard;
            public string KeyboardToken;
            public int[] KeyboardHidCodes;    // raw HID usage codes (for the firmware keyboard backend)

            // Guide-target mode ("Xbox Button"): fire a momentary Guide tap once on the press edge.
            public bool IsGuide;

            // Transient state (poll thread only).
            public bool Pressed;
            public bool PrevPressed;   // keyboard edge detection
        }

        // Built off the poll thread (wiring callback), read on the poll thread. Reference
        // assignment is atomic and the array is treated as immutable once published, so a
        // plain volatile reference is sufficient — no lock needed. The transient Pressed/
        // PrevPressed fields are only ever touched by the poll thread.
        private volatile GamepadSwapEntry[] _gamepadSwaps = Array.Empty<GamepadSwapEntry>();

        /// <summary>
        /// Rebuilds the active swap table from the 24-button mapping JSON (same payload the widget
        /// persists per global/per-game profile). Empty/blank JSON clears all swaps.
        /// </summary>
        public void ConfigureGamepadSwaps(string json)
        {
            try
            {
                GamepadSwapEntry[] built = BuildGamepadSwaps(json);
                _gamepadSwaps = built;
                Logger.Info($"ClawButtonMonitor: Gamepad button swaps configured — {built.Length} active entr{(built.Length == 1 ? "y" : "ies")}");
                // Firmware keyboard backend follows the same profile changes (A2VM only; no-op otherwise).
                RecomputeFirmwareKeyboardDesired();
            }
            catch (Exception ex)
            {
                Logger.Warn($"ClawButtonMonitor: ConfigureGamepadSwaps threw: {ex.Message}");
                _gamepadSwaps = Array.Empty<GamepadSwapEntry>();
            }
        }

        /// <summary>
        /// Applies the source→target swaps onto the outgoing ViGEm state. The original (pre-swap)
        /// state is snapshotted so a two-entry swap (A→B and B→A) resolves atomically instead of
        /// cascading (clear A, set B, then re-read B and clear it). Keyboard-target entries fire
        /// their chord once per press edge and otherwise just suppress the source button.
        /// </summary>
        private void ApplyGamepadSwaps(ref ushort xiBtns, ref byte ltrig, ref byte rtrig,
            ref short leftX, ref short leftY, ref short rightX, ref short rightY)
        {
            GamepadSwapEntry[] swaps = _gamepadSwaps;
            if (swaps == null || swaps.Length == 0)
            {
                return;
            }

            // Snapshot the ORIGINAL state for source detection.
            ushort origBtns = xiBtns;
            byte origLeftTrigger = ltrig;
            byte origRightTrigger = rtrig;

            ushort clearButtonMask = 0;
            bool clearLeftTrigger = false;
            bool clearRightTrigger = false;
            bool anyPressed = false;

            // Phase 1: detect pressed sources (from the snapshot), accumulate the sources to
            // clear, and handle keyboard edge-fire + prev-state. Done in this loop (not after a
            // possible early return) so keyboard edge state stays correct on release.
            for (int i = 0; i < swaps.Length; i++)
            {
                GamepadSwapEntry swap = swaps[i];
                bool pressed = IsSwapSourcePressed(swap, origBtns, origLeftTrigger, origRightTrigger);
                swap.Pressed = pressed;

                if (pressed)
                {
                    anyPressed = true;
                    clearButtonMask |= swap.SourceButtonMask;
                    if (swap.SourceIsLeftTrigger) clearLeftTrigger = true;
                    if (swap.SourceIsRightTrigger) clearRightTrigger = true;
                }

                if (swap.IsKeyboard)
                {
                    // When the firmware keyboard backend owns this button, the firmware emits the real
                    // HID key itself — don't ALSO fire the software chord (double-fire). The source is
                    // still cleared above so Viiper never emits the raw button either.
                    if (pressed && !swap.PrevPressed && !string.IsNullOrEmpty(swap.KeyboardToken)
                        && !IsButtonFirmwareKeyboardMapped(swap.SourceButtonName))
                    {
                        try { KeyboardChordCallback?.Invoke(swap.KeyboardToken); }
                        catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: gamepad keyboard swap fire threw: {ex.Message}"); }
                    }
                    swap.PrevPressed = pressed;
                }
                else if (swap.IsGuide)
                {
                    if (pressed && !swap.PrevPressed)
                    {
                        try { TriggerGuideTap(); }
                        catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: gamepad guide swap fire threw: {ex.Message}"); }
                    }
                    swap.PrevPressed = pressed;
                }
            }

            if (!anyPressed)
            {
                return;
            }

            // Phase 2: clear the pressed sources, then inject the gamepad targets.
            xiBtns = (ushort)(origBtns & ~clearButtonMask);
            if (clearLeftTrigger) ltrig = 0;
            if (clearRightTrigger) rtrig = 0;

            for (int i = 0; i < swaps.Length; i++)
            {
                GamepadSwapEntry swap = swaps[i];
                if (!swap.Pressed || swap.IsKeyboard || swap.IsGuide)
                {
                    continue; // keyboard/guide entries only suppress the source (handled in phase 1)
                }

                RemapAction[] actions = swap.Actions;
                for (int a = 0; a < actions.Length; a++)
                {
                    ApplyXInputRemapAction(actions[a],
                        ref xiBtns, ref ltrig, ref rtrig,
                        ref leftX, ref leftY, ref rightX, ref rightY);
                }
            }
        }

        private static bool IsSwapSourcePressed(GamepadSwapEntry swap, ushort buttons, byte leftTrigger, byte rightTrigger)
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

        private static GamepadSwapEntry[] BuildGamepadSwaps(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
            {
                return Array.Empty<GamepadSwapEntry>();
            }

            var entries = new List<GamepadSwapEntry>();

            // Outer dict shape: "ButtonName":{...},"ButtonName":{...}
            MatchCollection matches = Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*(\\{[^}]+\\})");
            foreach (Match match in matches)
            {
                string buttonName = match.Groups[1].Value;
                string mappingJson = match.Groups[2].Value;

                if (!TryGetSwapSource(buttonName, out ushort sourceMask, out bool isLeftTrigger, out bool isRightTrigger))
                {
                    continue; // stick-direction sources are out of scope
                }

                ButtonMappingParser.ParsedButtonMapping parsed = ButtonMappingParser.ParseExtended(mappingJson);

                // ── Keyboard target (Type==1) ──────────────────────────────────────────────
                if (parsed.Type == 1)
                {
                    string token = BuildKeyboardToken(parsed.KeyboardKeys);
                    if (string.IsNullOrEmpty(token))
                    {
                        continue; // no keys configured → pass the source through unchanged
                    }

                    entries.Add(new GamepadSwapEntry
                    {
                        SourceButtonMask = sourceMask,
                        SourceIsLeftTrigger = isLeftTrigger,
                        SourceIsRightTrigger = isRightTrigger,
                        SourceButtonName = buttonName,
                        IsKeyboard = true,
                        KeyboardToken = token,
                        KeyboardHidCodes = parsed.KeyboardKeys,
                    });
                    continue;
                }

                // ── Gamepad target (Type==0) ───────────────────────────────────────────────
                // Mouse (Type==2) and anything else is intentionally ignored.
                if (parsed.Type != 0)
                {
                    continue;
                }

                // "Xbox Button" target → edge-fire a momentary Guide tap (Guide can't ride the
                // XInput button mask, so it's not a normal injected action). Detected before the
                // regular action build because IsXinputSwapAction() excludes it from that list.
                bool wantsGuide = false;
                if (parsed.GamepadActions != null)
                {
                    for (int i = 0; i < parsed.GamepadActions.Length; i++)
                    {
                        if (RemapActionHelper.GetByIndex(parsed.GamepadActions[i]) == RemapAction.XboxGuide)
                        {
                            wantsGuide = true;
                        }
                    }
                }
                if (!wantsGuide && parsed.GamepadAction > 0 &&
                    RemapActionHelper.GetByIndex(parsed.GamepadAction) == RemapAction.XboxGuide)
                {
                    wantsGuide = true;
                }
                if (wantsGuide)
                {
                    entries.Add(new GamepadSwapEntry
                    {
                        SourceButtonMask = sourceMask,
                        SourceIsLeftTrigger = isLeftTrigger,
                        SourceIsRightTrigger = isRightTrigger,
                        IsGuide = true,
                    });
                    continue;
                }

                var actions = new List<RemapAction>();
                if (parsed.GamepadActions != null)
                {
                    for (int i = 0; i < parsed.GamepadActions.Length; i++)
                    {
                        RemapAction mapped = RemapActionHelper.GetByIndex(parsed.GamepadActions[i]);
                        if (IsXinputSwapAction(mapped) && !actions.Contains(mapped))
                        {
                            actions.Add(mapped);
                        }
                    }
                }

                if (actions.Count == 0 && parsed.GamepadAction > 0)
                {
                    RemapAction single = RemapActionHelper.GetByIndex(parsed.GamepadAction);
                    if (IsXinputSwapAction(single))
                    {
                        actions.Add(single);
                    }
                }

                // No real target (Disabled / reset) → let the source pass through unchanged.
                if (actions.Count == 0)
                {
                    continue;
                }

                entries.Add(new GamepadSwapEntry
                {
                    SourceButtonMask = sourceMask,
                    SourceIsLeftTrigger = isLeftTrigger,
                    SourceIsRightTrigger = isRightTrigger,
                    SourceButtonName = buttonName,   // needed for the firmware gamepad→gamepad slot lookup
                    Actions = actions.ToArray(),
                });
            }

            return entries.ToArray();
        }

        private static bool IsXinputSwapAction(RemapAction action)
        {
            switch (action)
            {
                case RemapAction.Disabled:
                case RemapAction.DesktopButton:
                case RemapAction.PageButton:
                case RemapAction.XboxGuide:   // handled as an edge-fire Guide tap, not an injected XInput action
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Maps a widget source-button name (GamepadButtonNames in the widget) to the XInput button
        /// mask / trigger it corresponds to. Returns false for stick-direction sources.
        /// </summary>
        private static bool TryGetSwapSource(string buttonName, out ushort sourceMask, out bool isLeftTrigger, out bool isRightTrigger)
        {
            sourceMask = 0;
            isLeftTrigger = false;
            isRightTrigger = false;

            switch (buttonName)
            {
                case "A": sourceMask = XI_A; return true;
                case "B": sourceMask = XI_B; return true;
                case "X": sourceMask = XI_X; return true;
                case "Y": sourceMask = XI_Y; return true;
                case "LB": sourceMask = XI_LB; return true;
                case "RB": sourceMask = XI_RB; return true;
                case "DPadUp": sourceMask = XI_DPAD_UP; return true;
                case "DPadDown": sourceMask = XI_DPAD_DOWN; return true;
                case "DPadLeft": sourceMask = XI_DPAD_LEFT; return true;
                case "DPadRight": sourceMask = XI_DPAD_RIGHT; return true;
                case "Start": sourceMask = XI_START; return true;
                case "Select": sourceMask = XI_BACK; return true;
                case "LSClick": sourceMask = XI_LS; return true;
                case "RSClick": sourceMask = XI_RS; return true;
                case "LT": isLeftTrigger = true; return true;
                case "RT": isRightTrigger = true; return true;
                default: return false; // LSUp/LSDown/.../RSRight → stick directions, out of scope
            }
        }

        /// <summary>
        /// Converts the widget's stored HID keyboard usage codes (KeyboardKeys) into a "+"-joined
        /// token string the helper's input injector understands (e.g. "LCtrl+LShift+A"). Mirrors
        /// the widget GetKeyDisplayName / HidToInjectorToken tables (which map 1:1 to injector
        /// tokens). Unknown codes are skipped. No Legion HID dependency.
        /// </summary>
        private static string BuildKeyboardToken(int[] hidKeys)
        {
            if (hidKeys == null || hidKeys.Length == 0)
            {
                return null;
            }

            var tokens = new List<string>(hidKeys.Length);
            foreach (int hid in hidKeys)
            {
                string token = HidToInjectorToken(hid);
                if (!string.IsNullOrEmpty(token))
                {
                    tokens.Add(token);
                }
            }
            return tokens.Count > 0 ? string.Join("+", tokens) : null;
        }

        private static string HidToInjectorToken(int hid)
        {
            // Letters A-Z (HID 0x04-0x1D)
            if (hid >= 0x04 && hid <= 0x1D) return ((char)('A' + (hid - 0x04))).ToString();
            // Digits 1-9 (0x1E-0x26) then 0 (0x27)
            if (hid >= 0x1E && hid <= 0x26) return ((char)('1' + (hid - 0x1E))).ToString();
            if (hid == 0x27) return "0";
            // F1-F12 (0x3A-0x45)
            if (hid >= 0x3A && hid <= 0x45) return "F" + (hid - 0x3A + 1);

            switch (hid)
            {
                case 0x28: return "Enter";
                case 0x29: return "Esc";
                case 0x2C: return "Space";
                case 0x2B: return "Tab";
                case 0x2A: return "Backspace";
                case 0x52: return "Up";
                case 0x51: return "Down";
                case 0x50: return "Left";
                case 0x4F: return "Right";
                case 0xE0: return "LCtrl";
                case 0xE1: return "LShift";
                case 0xE2: return "LAlt";
                case 0xE3: return "LMeta";
                case 0xE4: return "RCtrl";
                case 0xE5: return "RShift";
                case 0xE6: return "RAlt";
                case 0xE7: return "RMeta";
                case 0x4A: return "Home";
                case 0x4D: return "End";
                case 0x4B: return "PgUp";
                case 0x4E: return "PgDn";
                case 0x49: return "Ins";
                case 0x4C: return "Del";
                case 0x46: return "PrtSc";
                case 0x48: return "Pause";
                case 0x80: return "VOLUME_UP";
                case 0x81: return "VOLUME_DOWN";
                case 0x7F: return "VOLUME_MUTE";
                case 0x2F: return "[";
                case 0x30: return "]";
                default: return null;
            }
        }
    }
}

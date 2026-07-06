# Claw 8 AI+ EX — Phase 5 Validation Record

Pass/fail record for the on-device feature-parity checklist (port plan Phase 5).
Rows marked UNATTENDED-PASS were verified 2026-07-05 from helper log evidence with no
human present (log `helper_2026-07-05_13.log`, helper launched elevated via the
`\ClawTweaks\ClawTweaksHelper` scheduled task). Everything else needs a human at the
device — do not mark those from log output alone.

| # | Feature | Status | Evidence / notes |
|---|---------|--------|------------------|
| 1 | Detection | ✅ UNATTENDED-PASS (2026-07-05) | `Device matched: MSI Claw 8 EX (MSIClaw)`; features Controller/Gyro=True, RGB=False (intentional), no Generic fallback |
| 2 | Widget shows Claw sections | ⬜ pending human | Needs Win+G interaction |
| 3 | Controller emu in a game | ⬜ pending human | **Blocker: ViGEm and HidHide are NOT installed on this unit** (checked 2026-07-05) — install via the app's driver flow first |
| 4 | M1/M2 remap | ⬜ pending human | fw 0x0411 ≥ 0x166 → code will use the `M1_NEW`/`M2_NEW` GetM12 params; needs physical button presses to confirm |
| 5 | OEM button single/double | ⬜ pending human | WMI listener starts (code path verified); needs physical press |
| 6 | Gyro→stick | 🟡 partial | Gyro *pipeline* verified end-to-end unattended: CustomSensor source starts (10 ms interval), 64 samples/s through ClawGyroSourceAdapter, remapped gravity vector numerically correct. **Axis directions/signs vs. physical motion NOT verified** — the A1M remap is a hypothesis on the EX (see ClawGyroSourceAdapter.cs header). Verify: roll/pitch/yaw each move aim the right way |
| 7 | Gyro→mouse | 🟡 partial | Same pipeline; same axis caveat |
| 8 | TDP slider | ⬜ pending human | P4 write probe skipped unattended by design. Note: `Get_Data 80/81` read 0 — read-back may need another source |
| 9 | FPS limit (IGCL + RTSS) | ⬜ pending human | RTSS not confirmed installed |
| 10 | Fan custom curve | ⬜ pending human | P5 write probe skipped unattended by design (EC latch risk). Firmware baseline table measured: `3A,46,4A,4C,4E,50,54,5E` (58–94), fan-control bit OFF |
| 11 | Fan stress (no EC latch) | ⬜ pending human | |
| 12 | Charge limit 80 % | ⬜ pending human | **Open question first**: EX `Get_Data 215` reads `0x80` = A2VM-decoding "enabled, 0 %", which is suspicious — verify the encoding (set a limit in MSI Center M / BIOS and re-read) before writing |
| 13 | Per-game profiles | ⬜ pending human | |
| 14 | AC/DC switch | ⬜ pending human | |
| 15 | Suspend/resume | ⬜ pending human | |
| 16 | Center M coexistence | ⬜ pending human | MSI Center M app not installed on this unit (only the SDK v3.0.2605.2101) |

## Unattended session cannot do (needs a human at the device)

1. Install ViGEm + HidHide (row 3 blocker) and optionally MSI Center M (row 16).
2. Physical-motion gyro axis verification (rows 6/7) — if aim moves the wrong way, fix
   the remap table in `ClawGyroSourceAdapter.cs` (single place), variant-gate it if the
   EX needs different signs than A2VM.
3. All hardware WRITE probes, in the port-plan order and with its STOP-gate wording:
   P4 TDP (write −2 W, read back, restore), P5 fan no-op write (EC latch risk — read
   `MsiClawFanController.cs:28` comments first), P6 charge limit (after the 0x80
   encoding question is answered), P7 LED (fw 0x0411 is not in the address table —
   verify `[0x02,0x4A]` nearest-match addresses are safe before enabling RGB).
4. ~~Re-test DirectInput enumeration from an outside-container process~~ — **done
   2026-07-05, not a container artifact**: still zero devices system-wide from a genuine
   outside-container process. Not a blocker (gyro/controller emu don't use DirectInput);
   see port log 2026-07-05 entry.

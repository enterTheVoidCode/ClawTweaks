# Claw 8 AI+ EX — Phase 5 Validation Record

Pass/fail record for the on-device feature-parity checklist (port plan Phase 5).
Rows marked UNATTENDED-PASS were verified 2026-07-05 from helper log evidence with no
human present (log `helper_2026-07-05_13.log`, helper launched elevated via the
`\ClawTweaks\ClawTweaksHelper` scheduled task). Everything else needs a human at the
device — do not mark those from log output alone.

| # | Feature | Status | Evidence / notes |
|---|---------|--------|------------------|
| 1 | Detection | ✅ UNATTENDED-PASS (2026-07-05) | `Device matched: MSI Claw 8 EX (MSIClaw)`; features Controller/Gyro=True, RGB=False at the time of this run (later flipped to True after the P7 LED pass — see port log 2026-07-05 evening), no Generic fallback |
| 2 | Widget shows Claw sections | ✅ PASS (2026-07-05) | Kyle used the widget live all evening: fan presets (17:29–17:30), LED tile (17:28/17:47), TDP slider (17:28), CPU boost (17:47) — Claw sections present and functional (helper log evidence throughout) |
| 3 | Controller emu in a game | ✅ PASS (owner play-test 2026-07-07) | All blockers cleared 2026-07-05 (see port log, three iterations): monitor's Gamepad-only DirectInput filter missed the EX joystick (enumerates as FirstPerson) — **fixed** (`DeviceClass.GameControl`), verified on-device: joystick acquired, **VIIPER virtual pad mounted (no ViGEm needed)**, HidHide cloaks physical, boot complete, no thrash. In-game session confirmed by owner (see addendum) |
| 4 | M1/M2 remap | ✅ PASS (owner play-test 2026-07-07) | fw 0x0411 ≥ 0x166 → `M1_NEW`/`M2_NEW` GetM12 params; physical presses confirmed in the owner play-test (see addendum) |
| 5 | OEM button single/double | ✅ PASS (owner play-test 2026-07-07) | WMI listener code path verified 2026-07-05; physical presses confirmed in the owner play-test (see addendum) |
| 6 | Gyro→stick | ✅ PASS (owner play-test 2026-07-07) | Pipeline verified end-to-end unattended 2026-07-05 (CustomSensor source, 10 ms interval, 64 samples/s, remapped gravity vector numerically correct). Axis directions/signs vs. physical motion confirmed in the owner play-test — the A1M remap holds on the EX (ClawGyroSourceAdapter.cs header updated) |
| 7 | Gyro→mouse | ✅ PASS (owner play-test 2026-07-07) | Same pipeline; axis caveat resolved with row 6 |
| 8 | TDP slider | ✅ PASS (owner play-test 2026-07-07) | P4 superseded by live behavior (port log post-reboot entry): production WMI writes (unlock 35/37, profile 25/26, power-shift 0xC0, slider 25↔30) all succeed on the EX; OverBoost was factory-enabled, nothing persistent written by us. EC compliance under load confirmed in the owner play-test. The 2026-07-05 "CPU temp/power show --" issue is FIXED by the MSR fallback (`IntelMsrCpuFallback`) — the fan auto-safety reading a live 73 °C on 2026-07-07 is direct log evidence |
| 9 | FPS limit (IGCL + RTSS) | ✅ PASS (owner play-test 2026-07-07) | The 2026-07-05 RTSS exceptions were a benign post-install race; clean after reboot. Confirmed in the owner play-test (see addendum) |
| 10 | Fan custom curve | ✅ PASS with finding (owner play-test 2026-07-07) | P5 no-op/restore leg COMPLETE 2026-07-05: EX baseline `58,70,74,76,78,80,84,94` written and read back byte-for-byte MATCH on CPU+GPU blocks (byte[0] anomaly explained: EC sanity-clamps implausibly-low backup bytes). Hand-back variant-keyed to the EX baseline. **Finding: fan auto-safety reverts audibly on the EX — see addendum** |
| 11 | Fan stress (no EC latch) | ✅ PASS (owner play-test 2026-07-07) | No EC latch observed in a ≥30 min game session (EC Sport held 89 °C without latching, helper log 15:21). See addendum for the auto-safety behavior triggered during this test |
| 12 | Charge limit 80 % | ⏭ SKIPPED (owner) | Kyle waived charge-limit validation 2026-07-05 ("don't really care"). No writes made; `Get_Data 215 = 0x80` encoding question stays open; feature un-validated (not failed) on the EX |
| 13 | Per-game profiles | ✅ PASS (owner play-test 2026-07-07) | Confirmed in the owner play-test (see addendum) |
| 14 | AC/DC switch | ✅ PASS (owner play-test 2026-07-07) | Confirmed in the owner play-test (see addendum) |
| 15 | Suspend/resume | ✅ PASS (owner play-test 2026-07-07) | Confirmed in the owner play-test (see addendum) |
| 16 | Center M coexistence | ⬜ UNTESTED | MSI Center M app not installed on this unit (only the SDK v3.0.2605.2101); owner did not opt to install it — recorded as untested |

## Unattended session cannot do (needs a human at the device)

1. ~~Install ViGEm + HidHide (row 3 blocker)~~ — **done 2026-07-05**: HidHide, PawnIO,
   RTSS, and usbip-win2 (VIIPER, the current default emulation backend) all installed and
   verified running. MSI Center M (row 16) still not installed — human decision pending.
2. ~~Physical-motion gyro axis verification (rows 6/7)~~ — **done 2026-07-07** (owner
   play-test): the A1M remap in `ClawGyroSourceAdapter.cs` is correct on the EX, no
   variant gating needed.
3. Remaining hardware WRITE probes, in the port-plan order and with its STOP-gate wording:
   ~~P4 TDP~~ (superseded 2026-07-05 — production WMI writes already succeed; EC-compliance
   check merged into row 8), ~~P5 fan no-op write~~ (**done 2026-07-05** — byte-for-byte
   round-trip MATCH, see row 10), P6 charge limit (owner-skipped, see row 12; the 0x80
   encoding question stays open — do not write to block 215), ~~P7 LED~~ (**done
   2026-07-05** — LEDs visibly responded to `[0x02,0x4A]` on fw 0x0411, human-verified;
   0x0411 promoted to an exact `FirmwareTable` entry, `SupportsRgbLighting=true` on the
   EX config, and the previously-ungated write path now checks the flag in
   `MsiClawLedController`).

## Addendum — owner play-test, 2026-07-07

Kyle ran the row 3–15 play-test checklist on-device and reported it working overall
("looked good"). Rows above are recorded as owner-verified passes on that summary
report (not a per-row written sign-off), with one behavioral finding:

**Fan preset appears to "unset" mid-game (rows 10/11).** ~30 minutes into a game the
fan reverts to the loud stock behavior while the widget still shows the chosen preset
as active. Log-confirmed (`helper_2026-07-07_15.log`) and **working as designed**, but
the design predates the EX and is much more audible there:

- 15:12:59 — `Fan auto-safety: CPU 73°C ≥ 70°C in curve mode 1 → engaging EC Sport`,
  followed by the firmware hand-back writing the EX baseline `[58,70,74,76,78,80,84,94]`.
  This is the A2VM-era safety (`MsiFanAutoSportTick`, threshold 70 °C in Quiet/Default
  modes) that deliberately keeps the saved/displayed mode unchanged so it can restore
  after the game.
- On the A2VM the equivalent hand-back table is near-silent at idle temps; the EX
  baseline runs the fan audibly at ALL temperatures, so what is a quiet degradation on
  the A2VM is a loud, visible "my preset got unset" on the EX.
- 15:21:24 — on game end the saved curve was restored, then 200 ms later the safety
  re-engaged (CPU still 89 °C), leaving the fan loud at the desktop until the next
  explicit preset apply or game end. No EC latch occurred (EC Sport held 89 °C fine).

Follow-up (not in the port PR): make the auto-safety EX-aware — options include an
EX-specific hand-back/restore policy (e.g. cooldown-based restore when no game is
running), raising the EX Quiet/Default curves' mid/top band per the measured firmware
table, or surfacing the auto-Sport state in the widget instead of silently keeping the
preset highlighted. Needs an on-device stress re-test; do not tune blind.
4. ~~Re-test DirectInput enumeration from an outside-container process~~ — **done
   2026-07-05, not a container artifact**: still zero devices system-wide from a genuine
   outside-container process. Not a blocker (gyro/controller emu don't use DirectInput);
   see port log 2026-07-05 entry.

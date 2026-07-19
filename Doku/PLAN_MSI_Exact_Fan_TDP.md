# PLAN — MSI-exact TDP + fan rebuild (decoupled), A2VM + Claw 8 EX

**Goal (user directive 2026-07-17):** drive TDP **and** fan exactly like MSI Center M, with the fan
**decoupled from TDP**. Keep our two differentiators (editable temperature axis; a "boost to 100%" fan
toggle) and the existing UI; rebuild only the underbau. **Order: fix A2VM TDP → MSI FIRST, then the fan.**

Rationale: the RE (decompile + on-device, `RE_MSI_FanCurve.md` §Update 2026-07-17) shows MSI keeps the
power-shift **fixed per mode** and applies the fan **independently** (never per-watt, never per-tick). Our
current code re-writes the shift (d210) on every TDP apply and re-asserts the fan (`ReassertMsiFanAfterShift`)
+ runs a reactive auto-Sport latch guard — that coupling is the suspected source of the fan instability/latch.

## Current state (verified in code, 2026-07-17)
- `PerformanceManager.ApplyMsiClawTdp` → `SetMsiAcpiTDP` (EX already low(8)→PL2→PL1; A2VM still direct 80,81)
  → `SetMsiPowerShiftForTdp` (**writes d210 EVERY apply**: EX 0xC6; A2VM 0xC0/0xC4 via `_msiSportCooling`;
  then `ReassertMsiFanAfterShift`) → `WriteMsiUserScenarioPL` (registry mirror — MSI-conform, keep).
- `MsiClawFanController` is ALREADY MSI-shaped: `ApplyMsiCurve` = Set_Fan(duty)+Set_Thermal(temp axis)+212 bit7;
  `ApplyFirmwareAutoBaseline` = MSI Auto (212=0); `SetFanFullSpeed` = full-speed 152 bit7 (our 100% toggle).
  Temp axis is EDITABLE via Set_Thermal (MSI keeps it fixed — this is our differentiator, KEEP).
- `Program.cs` `MsiFanAutoSportTick()` / `EngageAutoSport` = the reactive latch guard (Sport at ≥70 °C).

## MSI recipe to match (from the decompile)
- **TDP (both):** PL write ordering **PL1→floor(8) → PL2(81) → PL1(80)**; PL2 ceiling per model (A2VM 37, EX 45).
- **Shift (d210), set ONCE per mode (Manual TDP):** **A2VM(1T52) = 0xC0 (Comfort)**, **EX(1T91) = 0xC6 (Manual)**.
  MSI does NOT touch d210 on a watt change (only PL blocks) and never uses Sport in Manual.
- **Fan, independent of TDP:** Auto = `Set_Data(212,0)` (firmware); Custom = Set_Fan(1&2 duty) + `Set_Data(212,
  GetAP[1]|0x80)`. Full-speed/CoolerBoost = `Set_Data(152, |0x80)`. Applied on **mode/profile/AC-DC change**,
  NOT per TDP tick. No temperature/watt auto-override of a custom curve (EC just follows the curve).

---

## Stage 1 — A2VM TDP fully MSI-conform (do FIRST, contained, verifiable)
`PerformanceManager.cs`:
1. **`SetMsiAcpiTDP`**: extend the low(8)→PL2→PL1 ordering to **A2VM as well** (currently EX-only). Same blocks
   80/81, raw watts; just the MSI ordering for both.
2. **Decouple the shift from the per-apply path**: remove the `SetMsiPowerShiftForTdp(pl1)` call from
   `ApplyMsiClawTdp`. Assert the shift **once** when the performance mode is established/changed (helper start,
   profile/game apply, AC↔DC) via a new `AssertMsiPowerShiftForMode()`: A2VM→0xC0, EX→0xC6, guarded by
   `lastMsiShiftValue` so a repeat is a no-op. Watt changes then touch ONLY PL 80/81.
3. Drop the `_msiSportCooling` 0xC4 path from the TDP shift (MSI manual never uses Sport). `ReassertMsiFanAfterShift`
   is no longer called from the TDP path.
4. Keep `EnsureMsiClawTdpUnlock` (OverBoost/ceiling) and `WriteMsiUserScenarioPL` (registry mirror).
- **Verify (gate):** A2VM + HWiNFO — set a manual TDP, run a load; `CPU Package Power`/`PL1 (Dynamic)` must
  still HOLD past Tau. If it holds → proceed to Stage 2. (This changes a currently-working path; verify before fan.)

## Stage 2 — Fan underbau MSI-exact + decoupled
1. **Remove the coupling/guard:** delete `MsiFanAutoSportTick`/`EngageAutoSport` (reactive auto-Sport) and
   `ReassertMsiFanAfterShift`. Fan no longer rides on the shift/TDP.
2. **Keep** `MsiClawFanController.ApplyMsiCurve` (Set_Fan + Set_Thermal + 212) — it already IS MSI's custom-fan
   mechanism plus our editable temp axis. **Keep** `SetFanFullSpeed` (100% toggle) and `ApplyFirmwareAutoBaseline`
   (= MSI Auto, 212=0).
3. **"MSI Default" fan preset — model-agnostic, read at runtime (NO tester read, NO hardcoded per-model values).**
   The decompile gives the exact MECHANISM (identical A2VM/EX); the per-device default curve VALUES are NOT in
   the code — MSI reads them live from the EC (`Check_Fan`). Our helper runs on the device and does the same:
   - `MsiClawFanController.GetFirmwareTempAxis()` reads the device's own axis live from `Get_Thermal[1]`
     (MSI never writes it → firmware-stable per model); fallback = `MsiTemps_Default` if the read is implausible.
   - `ApplyFirmwareAutoBaseline()` is now MSI-exact "Auto" (just `212=0`, no table write → EC runs its own
     per-model curve).
   - `ApplyMsiFan` builds presets 0/1/2 on the live `baseAxis` (Cooling = base−10). Correct on A2VM AND EX
     with zero external input. (DONE 2026-07-17, built 0.1.7.100.)
4. **Event-based apply:** (re)apply the fan on mode/profile/AC-DC change only — not per TDP tick.
5. **UI unchanged:** the fan card keeps its curve editor (editable temp axis) + the 100% toggle; only the values
   feeding it change (per-model MSI default preset). Optionally drop the bespoke "Cooling (−10 °C)" preset in favour
   of the editable axis.
- **Verify (gate):** A2VM + EX — custom curve is followed; 100% toggle works; no per-tick fan re-grab; run a
  sustained load and watch for the platform latch (see risk below).

## Kept / Removed / Changed
- **Kept:** editable temperature axis (Set_Thermal), 100% boost toggle (152), the whole fan UI, the registry
  PL mirror, `EnsureMsiClawTdpUnlock`.
- **Removed:** per-TDP-tick d210 re-write, `ReassertMsiFanAfterShift`, `_msiSportCooling` coupling,
  `MsiFanAutoSportTick`/`EngageAutoSport` reactive latch guard.
- **Changed:** A2VM PL ordering → MSI; shift asserted once per mode (A2VM 0xC0 / EX 0xC6); fan is event-based.

## Risks / open decisions
1. **Latch guard removal — DECIDED 2026-07-17: remove it COMPLETELY.** The auto-Sport guard mitigates a
   PLATFORM latch (Intel IPF/TFN1) that even MSI Center M suffers and that only a reboot/sleep clears (see
   `clawtweaks-fan-panic`). User chose full MSI-exact with NO guard and no sleep/wake net. Accepted residual
   risk: if the platform latch does hit, only a reboot clears it (same as MSI). (A manual sleep/wake "reset
   fan" — Task #22 — remains available to add later if the latch proves to still occur in practice.)
2. **Changing a working A2VM TDP path** (Stage 1) — must be HWiNFO-verified before shipping.
3. ~~EX default fan preset needs a fresh-EX capture (tester).~~ RESOLVED — the "MSI Default" preset reads
   the device's own firmware axis live from the EC at runtime (`GetFirmwareTempAxis`), so it is correct on
   A2VM and EX with no tester read and no hardcoded EX values.

## Verification
Stage 1: A2VM HWiNFO TDP-hold. Stage 2: A2VM + EX fan-follows-curve, 100% toggle, sustained-load latch watch.
Build via `.\Build-Package.ps1 -Mode Test`. Ship-gate = on-device (tester) for the EX.

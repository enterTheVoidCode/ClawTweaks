---
name: clawtweaks-doubleinput-mechanism
description: "MSI Claw double-input root cause = physical read path / FW mode, not virtual device type or VIIPER version"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1b480e0e-3bbe-4299-a3c4-78e8d5102f90
---

On-device measured 2026-07-12 (CTW vs. HandheldCompanion, Claw 8 AI+ A2VM, via Diagnostics/Check-ControllerState.ps1).

**Double-input on the MSI Claw is caused by the physical read path / Claw firmware mode — NOT the virtual device type and NOT the VIIPER version.**

- **DInput read path** (Claw FW = DInput, PID_1902; its gamepad collection hidden via HidHide): a game sees **1** functional joystick (only the virtual VIIPER Xbox360 pad `045e:028e`) → **no double**. This is CTW's default and equals HC's "Controller Input = DirectInput".
- **XInput read path** (Claw FW = XInput, PID_1901 active): the physical Claw `1901` is ALSO enumerated as a **fully functional DirectInput joystick** (winmm 10 buttons/5 axes) that HidHide does NOT remove → a game sees **2** functional joysticks (virtual + physical) → **double input**. This equals HC's "Controller Input = XInput".

**IMPORTANT refinement (do not overclaim):** XInput always enumerated exactly **1** device in ALL configs (CTW-Virtual, CTW-HW, HC-DInput, HC-XInput). The "second controller" only appears in the **winmm/DirectInput** enumeration, never as a 2nd XInput slot. So the earlier "winmm functional-joystick count > 1 = double input" heuristic is DISPROVEN as sufficient: **Brotato in HC-XInput mode (2 functional winmm joysticks) did NOT double.** Double-input only manifests in games that read **DirectInput in addition to XInput and don't dedupe** (Forza-class) AND the physical DInput mirror is functional; XInput-centric titles (Brotato) never double regardless of mode. The device snapshot does NOT predict double-input — it depends on the specific game's input-API usage. Repro needs the actual dual-API title, not Brotato.

**Fix direction:** when a virtual pad is active, avoid leaving the physical Claw as a second functional DInput device — keep it in DInput FW mode + hide 1902 (CTW already does this by default = clean); if XInput FW ever becomes active (resume/boot/mode-switch), also hide the functional DInput mirror of 1901. winmm joystick count is a *latent-risk* signal only, not a double-input guarantee.

Secondary: CTW's in-process libviiper.dll is an older **dirty** build (`v0.4.2-…+dirty`, commit 2026-02-26) vs HC's clean `v0.0.0-…` commit 2026-04-10 — both load libVIIPER in-process. Worth updating but NOT the double-input cause. Full log: Doku/DIAG_DoubleInput_Measurements.md, plan Doku/PLAN_Viiper_Upgrade_Implementation.md. Related: [[project_clawtweaks]].

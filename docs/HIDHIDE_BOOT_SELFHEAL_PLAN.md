# Fix: HidHide boot-time hide verification + self-heal (MSI Claw double input)

## Problem (confirmed on-device 2026-06-28)
On some cold boots the physical MSI Claw controller (`VID_0DB0&PID_1902`, DInput gamepad)
is **not effectively hidden** by HidHide, even though `Enable()` returns `true` and HidHide
reports `blocked=1`. Result: the physical pad stays visible to apps **alongside** the virtual
VIIPER pad → every input registers twice, **system-wide** (Steam *and* Game Bar).

Toggling "Virtual Controller & Mouse" off→on fixes it (re-applies the hide against the now-stable
device instance). So the hide just needs a **verify + retry** at boot.

### Ruled out (with evidence)
- Not a second virtual pad: after removing a stale usbip devnode, XInput showed 1 slot, double persisted.
- Not `steamxbox` driver: not installed; XnaComposite UpperFilter is only `HidHide`.
- Not the HidHide app-allowlist: Steam is **not** in it.
- It is system-wide (Game Bar too) → physical leak, i.e. HidHide hide didn't take.

### Mechanism
At boot, `PID_1902` is still mid DInput-enumeration when `Enable()` runs. HidHide blocks an
instance-id that then changes after re-enumeration → the **current** gamepad instance is not in the
block-set → visible. A simple joy.cpl/HID count is **not** a valid check because the helper is on
HidHide's allowlist and always sees the physical. Reliable check: **are all current `PID_1902`
gamepad instances actually in HidHide's blocked set?**

## Fix
After `Enable()` at boot, verify and (if leaking) re-apply, capped retries.

### Files
1. `XboxGamingBarHelper/ControllerEmulation/ControllerSuppressionManager.cs`
   - `bool VerifyAllTargetsHidden(DeviceType deviceType, int hideTargetMode, out string diag)`
     - enumerate current target instances (reuse `QueryPnpDeviceIds(0x0DB0, 0x1902)`)
     - get HidHide blocked-instance list (reuse the list source used by `ForceUnhideAll`)
     - return true iff every current target instance is in the blocked set (+ cloaking active)
   - `bool EnsureHidden(DeviceType, int hideTargetMode, IReadOnlyCollection<string> excluded = null)`
     - call `Enable(...)`; settle ~700ms; `VerifyAllTargetsHidden`; if not hidden →
       `Disable()` + re-enumerate + `Enable()` again; up to 2 retries; log each attempt.
2. `XboxGamingBarHelper/Startup/Program.MSIClaw.cs` (~line 1969, the boot mount)
   - replace `suppression.Enable(...)` with `suppression.EnsureHidden(...)`
   - gate: only when `mountVigem && !startInMouseMode && !_externalGamepadModeActive`

### Constraints / risk
- Additive; happy path only adds a fast verify. Off→on re-cycle runs **only** on detected leak (rare).
- Retry hard-capped (no loop). Gated to avoid false positives in mouse/external modes.
- Boot time: +few ms happy path; +~1-2s on the rare leak boot.

### Verify
- Build helper (Release|x64). On-device: several cold boots; look for log
  `EnsureHidden: leak detected → reapplied` and confirm no double input.

## Status
- [x] VerifyAllTargetsHidden + EnsureHidden in ControllerSuppressionManager
- [x] Wire EnsureHidden into Program.MSIClaw boot (gated on `mountVigem`)
- [x] Build helper (Release|x64) — clean
- [ ] On-device verification (user): several cold boots, watch for
      `HidHide EnsureHidden: leak detected … → re-applying`, confirm no double input

Related: TODO #10 "Emulation vor Helper-Kill sauber abschalten" (teardown, separate).
Memory: clawtweaks-steam-input-injection / clawtweaks-helper-update-lifecycle.

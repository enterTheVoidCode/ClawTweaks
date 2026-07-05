# Claw 8 AI+ EX Port Log

Running log of measured facts, probe outputs, and decisions made while porting ClawTweaks
to the MSI Claw 8 AI+ EX (Panther Lake). Append-only; datestamp every entry. Do not "correct"
a measurement to match code assumptions — if a probe disagrees with what the A2VM code
assumes, the probe wins and the disagreement gets written down verbatim.

Reference device (currently supported): MSI Claw 8 AI+ A2VM (Lunar Lake, MS-1T52).
Target device (this log): MSI Claw 8 AI+ EX (Panther Lake).

---

## 2026-07-03 — Phase 0: Baseline

**Environment confirmed:** this Claude Code session is running directly on the target
device (not remote/simulated).

**Identity probe** (`Win32_ComputerSystemProduct`):
```
Vendor            : Micro-Star International Co., Ltd.
Name              : Claw 8 EX AI+ CG3EM
Version           : REV:1.0
IdentifyingNumber : K2605N0098484
```

This is the exact string `DeviceRegistry`/`MSIClawConfig.Matches()` will see as
`deviceInfo.Model`. Note it is **`Claw 8 EX AI+ CG3EM`**, not `A2VM` — confirms
`MSIClawConfig.Matches()` (which requires Name contains `"A2VM"`) will NOT match this
device. Full Win32_ComputerSystem / Win32_BaseBoard / Win32_BIOS / CPU / GPU probes are
deferred to Phase 1a (this Phase 0 check only needed the product Name string to confirm
the detection-failure hypothesis; Phase 1 will capture the complete manifest).

**Repo state before branching:**
- Repo: `C:\Users\kyle\Documents\git\ClawTweaks`, remote `origin` =
  `https://github.com/enterTheVoidCode/ClawTweaks.git`.
- Origin's default branch (`origin/HEAD`) is `release/v0.3.98.0`, NOT `master`.
  `master` has no common ancestor with `release/v0.3.98.0` (diverged/stale history,
  last commit on master ~2 weeks older). Branched from `release/v0.3.98.0` per human
  decision (asked via AskUserQuestion since the plan's "current default branch" was
  ambiguous).
- Working tree had pre-existing unrelated dirty state at session start:
  - ~190 modified files under `XboxGamingBarHelper/ADLXDepends/` — auto-regenerated SWIG
    C# bindings, header showed local regen from SWIG 4.3.1 → 4.4.1 (type renames like
    `SWIGTYPE_p_adlx__ADLX_DISPLAY_TYPE` → `SWIGTYPE_p_ADLX_DISPLAY_TYPE`). Unrelated to
    this port.
  - Untracked `DisplayEvents/`, `DisplayInfo/`, `Samples/` (top-level) — new ADLX SDK
    sample projects that came with the newer local ADLX SDK/SWIG install.
  - Untracked new files under `ADLXDepends/` (`SWIGTYPE_p_ADLX_*.cs`) from the same regen.
  - Dirty `ADLX` submodule (modified `ADLXCSharpBind.vcxproj`, untracked `Debug/` output).
  - Human decision: discard all of it (`git checkout --` on tracked files/submodule,
    `git clean -fd` on the untracked SWIG regen files, `rm -rf` on the sample folders).
    Done. Tree was clean before `git checkout -b feature/claw8-ex-support`.
- Branch `feature/claw8-ex-support` created from `release/v0.3.98.0` at commit `f8a31cf`
  ("Quick Settings: wide brightness/volume slider tile + onboarding tab-jump fix").

**Build:**
- `vswhere.exe` is present but returns no installations (broken VS instance registration).
  Located MSBuild directly at
  `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe`.
- Full-solution build (`XboxGamingBar.sln`, Debug|x64) fails with 5 pre-existing errors,
  **unrelated to the Claw port**:
  1. `ADLX\Samples\csharp\ADLXCSharpBind\ADLXCSharpBind.vcxproj` — C1010 "unexpected end of
     file while looking for precompiled header" in `ADLXHelper.cpp`, `WinAPIs.cpp`,
     `ADLXCSharpBind_wrap.cxx`. `pch.h`/`pch.cpp` exist on disk but the compiled `.pch`
     binary doesn't (fresh-checkout / first-build issue on this machine, submodule itself
     is clean per `git status` inside `ADLX/`). This is an AMD ADLX SDK sample project, not
     Claw device code.
  2. `XboxGamingBarPackage.wapproj` — APPX0104/APPX0107, missing/invalid
     `XboxGamingBarPackage_TemporaryKey.pfx` local dev signing cert (not checked into repo,
     expected on a fresh machine).
  - Neither failure touches any file this port will modify.
- Scoped build of the actually-relevant project succeeds clean:
  `XboxGamingBarHelper\XboxGamingBarHelper.csproj` (Debug|x64) → **0 errors, 222 warnings**
  (pre-existing warnings, not investigated — out of scope for this port).
  `Shared.csproj` is a dependency of Helper and built transitively as part of that same
  invocation with no errors.

**Decision:** Phase 0's "build completes with 0 errors" is satisfied for the project this
port actually touches (`XboxGamingBarHelper` + `Shared`). The two full-solution failures
(ADLX C++ sample PCH, packaging cert) are pre-existing local-environment gaps unrelated to
Claw hardware support and are left alone — not in scope for this plan.

**Detection-failure confirmation:**

NLog config: `XboxGamingBarHelper\NLog.config`, target pattern
`${gdc:item=LogDirectory}/helper_${date:format=yyyy-MM-dd_HH}.log`, `minlevel="Info"`,
`maxArchiveDays="3"`. Resolves to `%LOCALAPPDATA%\helper_<date>.log` for an unpackaged
(non-MSIX) run. Logging is on by default, no flag needed.

Launching the exe is not a one-shot affair — `ElevationBootstrapper` does a
deploy-then-relaunch-elevated dance (copies itself to
`%LOCALAPPDATA%\ClawTweaks\Helper`, registers a scheduled task `\ClawTweaks\ClawTweaksHelper`
with RunLevel Highest, then runs itself via that task to get elevation without a UAC prompt
each time). Found and worked around two issues along the way, both **pre-existing bugs,
unrelated to Panther Lake and worth a separate ticket, not part of this port**:
- The scheduled task's `<Command>` XML element is registered WITH literal surrounding
  double-quote characters baked into the path string (confirmed via
  `schtasks /query /xml`: `<Command>"C:\...\XboxGamingBarHelper.exe"</Command>`). Task
  Scheduler does not expect shell-style quoting in that field, so it looks for a file
  literally named with quote characters and fails with `ERROR_FILE_NOT_FOUND`
  (`LastTaskResult=2147942402`). Every run through the scheduled-task path silently no-ops.
  Likely cause: whatever builds the `Action.Path`/`Execute` field (probably in a
  `ScheduledTaskService`) is wrapping the path in `"..."` the way you would for a shell
  command line, which is wrong for the structured Task Scheduler API.
- `Start-Process -Verb RunAs` against the *deployed* copy
  (`%LOCALAPPDATA%\ClawTweaks\Helper\XboxGamingBarHelper.exe`) fails immediately with
  "The system cannot find the path specified," even though `Test-Path` on that exact path
  returns `True` and a plain non-elevated `Start-Process` on the same path works fine.
  RunAs against the original build-output copy (`bin\x64\Debug\XboxGamingBarHelper.exe`)
  works normally. Not root-caused (didn't chase it — copy has no unusual ACLs observed at a
  glance); worked around by elevating the build-output copy directly instead of the
  deployed one.

Worked around both by running
`XboxGamingBarHelper\bin\x64\Debug\XboxGamingBarHelper.exe` directly via
`Start-Process -Verb RunAs` (elevation itself works fine in this session — verified UAC
consent goes through non-interactively here). Captured from
`%LOCALAPPDATA%\helper_2026-07-03_19.log`:

```
2026-07-03 19:09:12.8618 INFO  DeviceRegistry       No matching device config found for: Micro-Star International Co., Ltd. Claw 8 EX AI+ CG3EM
2026-07-03 19:09:12.8618 INFO  DeviceDetector       Device type: Generic (no specific device matched)
2026-07-03 19:09:12.8618 INFO  DeviceDetector       Detected device: Micro-Star International Co., Ltd. Claw 8 EX AI+ CG3EM (REV:1.0) - Type: Generic
2026-07-03 19:09:12.8618 INFO  DeviceDetector         Manufacturer: Micro-Star International Co., Ltd.
2026-07-03 19:09:12.8618 INFO  DeviceDetector         Model: Claw 8 EX AI+ CG3EM
2026-07-03 19:09:12.8618 INFO  DeviceDetector         Version: REV:1.0
2026-07-03 19:09:12.8618 INFO  DeviceDetector         SystemFamily: Claw
2026-07-03 19:09:12.8618 INFO  DeviceDetector         DeviceType: Generic
2026-07-03 19:09:12.8618 INFO  DeviceDetector         Features - WMI TDP: False, Controller: False, RGB: False, Gyro: False, FanControl: False
2026-07-03 19:09:12.8618 INFO  DeviceDetector         Hardware - Touchpad: False, ScrollWheel: False, DetachableControllers: False
```

Exactly the predicted failure: falls through to `DeviceType.Generic`, every Claw-specific
feature flag (`WMI TDP`, `Controller`, `RGB`, `Gyro`, `FanControl`) reads `False`. This is
the Phase 0 target confirmed.

**Cleanup after the probe:** force-killed the elevated helper process (`taskkill /F /IM
XboxGamingBarHelper.exe /T`, elevated) and removed the scheduled task
(`Unregister-ScheduledTask ClawTweaksHelper`) created during setup, so the machine is back
to a clean state with nothing running in the background.

**Note for future phases:** every `msbuild ... /restore` on the full solution regenerates
the same ~190 `ADLXDepends` SWIG files + `Samples/DisplayInfo/DisplayEvents` sample
projects from a locally-installed newer SWIG (this happened again after the Phase 0 build,
identical to the state found at session start). This is a side effect of building
`XboxGamingBar.sln` itself (or `/restore`), not something a person did — evidently the ADLX
submodule's build/restore step invokes local SWIG codegen. Before every commit in this
port, run `git status --short` and if `ADLXDepends`/`Samples`/`DisplayInfo`/`DisplayEvents`
reappear as modified/untracked, discard them the same way:
`git checkout -- XboxGamingBarHelper/ADLXDepends/ XboxGamingBarPackage/Package.appxmanifest ADLX`
then `git clean -fd -- XboxGamingBarHelper/ADLXDepends DisplayEvents DisplayInfo Samples`.
To avoid the churn entirely, prefer building just the in-scope project
(`XboxGamingBarHelper.csproj`) rather than the full `.sln` in later phases.

**Phase 0 acceptance criteria: met.**
- Build: 0 errors on the in-scope projects (`XboxGamingBarHelper`, `Shared`).
- Branch `feature/claw8-ex-support` exists and is checked out, based on
  `release/v0.3.98.0` @ `f8a31cf`.
- Port log contains the exact "No matching device config found" line, plus the full
  Generic-fallback feature-flag dump.

---

## 2026-07-03 — Phase 1: Hardware enumeration and diff

Full raw probe output and the diff table live in
[CLAW8_EX_HARDWARE.md](CLAW8_EX_HARDWARE.md). Summary of what matched A2VM vs what didn't:

**Matches A2VM assumptions (VERIFIED-IDENTICAL):**
- WMI Vendor string ("Micro-Star International Co., Ltd.").
- Controller VID/PID in XInput mode: `0x0DB0`/`0x1901`.
- Command-interface UsagePage/Usage in XInput mode: `0xFFA0`/`0x0001`, 64-byte in/out
  reports — measured live via a new HidSharp-based probe
  (`Diagnostics/Claw8EXProbes/HidInventory`), not just PnP enumeration.
- `MSI_ACPI` WMI class exists at instance `ACPI\PNP0C14\0_0`, exactly matching the
  hardcoded path in `MsiClawWmi.cs`. Note: querying this instance **requires
  elevation** — a non-elevated `Get-CimInstance` silently returns 0 instances (no error
  surfaced unless you drop `-ErrorAction SilentlyContinue`), which looks identical to
  "class not present." Anyone re-probing this later should elevate first or they'll get a
  false negative.
- `MSI_ACPI`'s method set is a superset of what the plan expected (includes all the
  Get_AP/Set_AP-style methods, plus newer ones like `Get_EC2`, `*_64` variants) — the newer
  MSI Center M SDK (v3.0.2605.2101, confirmed installed) exposes more surface than A2VM's
  presumably older SDK, but nothing A2VM uses looks removed.

**Diverges from A2VM assumptions (needs real investigation, not just detection-string
changes):**
- **Board ID is `MS-1T91`**, not `MS-1T52` (expected — different hardware generation, this
  is just a fact to bake into the new device config, not a problem).
- **`Windows.Devices.Sensors.Gyrometer.GetDefault()` returns null on this device.** Built
  a dedicated probe (`Diagnostics/Claw8EXProbes/GyroProbe`, calls the same WinRT API
  `WindowsSensorGyroSourceAdapter` uses) to confirm this wasn't a fluke — confirmed null for
  both Gyrometer and Accelerometer. Checked the obvious mundane cause first (Windows motion
  sensor privacy/consent) — `sensors.custom` consent is `Allow` at both HKLM and HKCU, so
  that's not it. An Intel Sensor Hub (`HID\VID_8087&PID_0AC2`, PnP class `Sensor`) IS present,
  but its HardwareID (`HID\VID_8087&UP:0020_U:0001`) only proves a generic HID Sensor
  collection exists, not that a Gyroscope (usage 0x76) sub-collection is declared under it —
  didn't parse the raw HID report descriptor to check in Phase 1, that's Phase 3 P1 work.
  **This is the single biggest open risk in the whole port**: if there is genuinely no
  gyroscope exposed to Windows on this device, `ClawGyroSourceAdapter`/
  `WindowsSensorGyroSourceAdapter` cannot be reused as-is for the EX, and gyro-to-stick /
  gyro-to-mouse features may need a different data source (raw HID sensor reports) or may
  simply be unsupported on this device. Do not write any gyro remap code in Phase 4 until
  Phase 3 P1 resolves this one way or the other.
- DInput-mode PID/usage-page (`0x1902`/`0xFFF0`/`0x0040`) and firmware-version/M1-M2-param
  reads were NOT captured — device was in XInput mode throughout Phase 1 and switching
  modes is explicitly Phase 3 (P2) work, not Phase 1 (read-only enumeration only).

**Probes committed:** `Diagnostics/Claw8EXProbes/HidInventory` (net472 console app,
references the repo's existing `packages/HidSharp.2.1.0`) and
`Diagnostics/Claw8EXProbes/GyroProbe` (net8.0-windows10.0.19041.0 console app, calls the
WinRT Sensor API directly). Both build clean and were run on-device to produce the results
above.

**Phase 1 acceptance criteria: met**, with the gyro divergence flagged as the standout risk
to carry forward rather than silently resolved.

---

## 2026-07-03 — Phase 2: Reference research

Ran two subagents in parallel: (1) web research on HandheldCompanion / msi-ec / Linux WMI
driver prior art and any known "Gyrometer null on Panther Lake" issue, (2) exhaustive
in-repo inventory of every MSIClaw-specific touchpoint. Full inventory output is long and
was captured verbatim in the task transcript; key decisions below.

### (1) Web research findings

- **HandheldCompanion has no Panther Lake Claw class.** `HandheldCompanion/Devices/MSI/`
  has exactly three classes: `ClawA1M.cs` (base, MS-1T41), `ClawA2VM.cs` (MS-1T42/MS-1T52,
  Lunar Lake — extends A1M, only overrides TDP tables + `GyrometerAxis`), `ClawBZ2EM.cs`
  (MS-1T8K, AMD Z2 Extreme "Claw A8"). No `MS-1T91` entry anywhere. **We are ahead of
  upstream on this hardware** — nobody has documented `MS-1T91` / "Claw 8 EX AI+ CG3EM"
  publicly before this port log, as far as the research could find.
- **Gyro sourcing in HandheldCompanion is a dead end for our null-Gyrometer problem.**
  `ClawA1M` sends an HID control command (`SetMotionStatus`, opcode `0x2F`) to tell the
  controller firmware to turn its IMU stream on/off, but the actual *readout* path
  (`IMUGyrometer.cs`, `SensorsManager.cs`) is `SensorFamily.Windows` → still just
  `Gyrometer.GetDefault()` / `ReadingChanged` — the identical WinRT API we already found
  returns null on our unit. There's a `SensorFamily.Controller` enum case that logs
  "initialised" but **has no actual read implementation**. The only other working path is
  `SensorFamily.SerialUSBIMU`, for external USB IMU dongles — not applicable to an internal
  handheld sensor. **Conclusion: even upstream doesn't have a working internal-HID raw-gyro
  fallback for the Claw family.** If Gyrometer stays null after the dock-mode retest (see
  below), there is no existing prior-art path to copy — we'd be first to solve it, which is
  a real scope risk for this port, not a quick fix.
- Confirmed fan-table format matches what we already knew from the A2VM code: 8-byte
  duty-cycle array, 0–150 scale, via the same `MSI_ACPI`/`ACPI\PNP0C14\0_0` WMI path.
  `ClawA2VM` changes only TDP ceilings and clock ranges vs the base class — no EC/WMI
  protocol differences A2VM vs A1M. Useful precedent: whatever the EX needs will likely
  also be "same protocol, different numeric ceilings," not a new protocol.
- Linux side: `msi-ec` (raw EC register driver) has no Claw board IDs at all. The actual
  Linux Claw support effort is a *different*, newer driver — `msi-wmi-platform`
  (in-tree docs at `docs.kernel.org/wmi/devices/msi-wmi-platform.html`), a 2025 patch
  series aiming for fan/TDP/battery parity with MSI Center M, WMI-method-based (not raw EC
  pokes) — consistent with what we're already doing. Predates the EX's release; doesn't
  name it.
- No confirmed root cause or known fix found anywhere for "Gyrometer.GetDefault() null on
  Panther Lake" as a named issue. Intel's ISH docs describe accel/gyro/e-compass support as
  aimed at detachables/convertibles, suggesting OEMs must explicitly wire up ISH exposure
  per-SKU — plausible explanation for why a handheld might ship without it, but this is
  inference, not confirmation. **Human (Kyle) has raised an alternative, much better
  hypothesis: docked mode.** The device may currently be docked, and Panther Lake
  ISH/Gyrometer exposure may depend on the device being in handheld/undocked mode. This
  wasn't on our probe checklist and is a strong lead — testing now (see below), before
  concluding "no gyro hardware" or writing any gyro-workaround code.

### (2) In-repo touchpoint inventory (summary — this becomes the Phase 4 checklist)

19 `DeviceType.MSIClaw` branch points across 8 files (mostly guard clauses in
`Program.MSIClaw.cs`, `Program.LedSoc.cs`, plus routing in `PerformanceManager.cs`,
`ControllerSuppressionManager.cs`, `ControllerEmulationManager.GyroSource.cs`,
`ControllerEmulationManager.Normalize.cs`, `LegionManager.cs`, `ViiperEmulationManager.cs`).
~50 hardcoded-constant references (`0x0DB0`/`0x1901`/`0x1902`/`0xFFA0`/`0xFFF0`) across 17
files. 10 `MsiClawWmi.Get/Set` call sites (8 fan-related in `MsiClawFanController.cs`, 2
battery-related in `MsiClawBatteryManager.cs`). 7 firmware-version branch points in
`MsiClawLedController.cs`'s RGB-address table (`0x163`/`0x166`/`0x167`/`0x211`/`0x217`/
`0x219`/`0x308`) plus the M1/M2 `GetM12` byte pairs in `ClawButtonMonitor.cs` gated on
firmware `>= 0x166` vs `0x163`.

Notable items the plan's placeholder list didn't call out, now added to the Phase 4
checklist:
- `MsiClawFanController.cs` also uses `Get_AP`/`Set_Data` (not just `Get_Fan`/`Set_Fan`) for
  the fan-control-enable bit and a separate "full-speed override" bit at data block 152 —
  more surface than just the 8-byte table.
- `ControllerSuppressionManager.cs` has MSIClaw-specific HidHide filtering logic (hides only
  PID 0x1902, preserves 0x1901) and a USB-port-cycling helper keyed on both PIDs — easy to
  miss if only searching for `DeviceType.MSIClaw`.
- `Core/ControllerHotkeyMonitor.cs` has a third PID, `0x1903`, in its product-ID array,
  commented as "testing" — not mentioned anywhere else in the plan or code; flagged for
  Phase 4, worth asking whether this is relevant to the EX or truly dead test code.
- `MsiClawDriverCheckService.cs`'s `IsClawHardware()` check is looser than
  `MSIClawConfig.Matches()` (matches "Micro-Star" OR "MSI" in vendor, no model-string
  requirement) — this check will already treat the EX as Claw hardware today, even before
  any Phase 4 changes, which matters for driver-download UX consistency.

### Per-subsystem classification (carried into Phase 3)

| Subsystem | Classification |
|---|---|
| Detection (WMI Vendor/Model match) | Known-different (Phase 1 measured: needs new config, EX name string known) |
| Controller HID (VID/PID/usage pages, XInput mode) | Verified-identical (Phase 1) |
| Controller HID (DInput mode, firmware/M1-M2 reads, mode switch) | Unknown — Phase 3 P2 |
| Gyro | **Unresolved / actively being retested** (dock-mode hypothesis, see below) — no prior-art fallback exists if it stays null |
| Fan control | Expected same protocol as A2VM, different tuning/ceilings — Phase 3 P3/P5 |
| TDP | Expected same protocol (MSI ACPI WMI path, not kx.exe, per `PerformanceManager.cs:1139`) — ceiling values need Phase 3 P4 + real spec numbers, not guesses |
| Battery charge limit | Expected identical protocol (`Get_Data`/`Set_Data` block 215) — Phase 3 P3/P6 |
| LED | Expected new firmware-version table entry needed (EX firmware version unknown yet) — Phase 3 P2/P7 |

### Live retest: dock-mode hypothesis for the null Gyrometer

Kyle (human) proposed the null `Gyrometer.GetDefault()` result might simply be because the
device is currently docked, and offered to switch to handheld/undocked mode for a retest.
Re-ran `Diagnostics/Claw8EXProbes/GyroProbe.exe` once already while still docked — still
null, confirming the dock state hadn't changed yet. Waiting on Kyle to physically undock
before re-testing. **Do not conclude "no gyro hardware" until this retest is done** — see
next log entry for the result.

### Dock-mode retest result: hypothesis ruled out

Kyle physically unplugged the device from its dock/hub accessory. Confirmed the disconnect
took effect at the OS level: `Win32_Battery.BatteryStatus = 1` (Discharging — i.e. running
on battery, dock was supplying AC power). `Get-PnpDevice -Class Sensor` list is byte-for-
byte unchanged before/after (still just "Simple Device Orientation Sensor" and "HID Sensor
Collection V2", both `Status=OK`). Re-ran `GyroProbe.exe` twice after undocking (once
immediately, once after attempting a `pnputil /scan-devices` forced rescan — the rescan
itself failed on a UAC cancellation and wasn't retried, but Windows enumerates sensors
dynamically regardless so this doesn't invalidate the result): **`Gyrometer.GetDefault()`
and `Accelerometer.GetDefault()` are still both null.**

**Conclusion: dock/undock state is not the cause.** This was a good hypothesis to test
(cheap, physically plausible, would have been a trivial non-issue if true) but is now
ruled out with direct evidence. The null Gyrometer stands as a real finding, not a
transient mode artifact. Per Phase 2's research, there is no known prior-art fix for this
— HandheldCompanion's own gyro path depends on the same WinRT API and has no working
internal-HID fallback for the Claw family. Next step (Phase 3 P1) is to parse the raw HID
report descriptor of the Intel ISH "HID Sensor Collection V2" node
(`HID\VID_8087&PID_0AC2\7&38CDE06&0&0000`) directly — via HidSharp, similar to the
`HidInventory` probe already built — to determine whether a Gyroscope (HID Sensor usage
page 0x0020, usage 0x0076) sub-collection is declared at all under that node. If yes, the
EX gyro would need a new raw-HID sensor adapter (real new work, no shortcut). If no
Gyroscope usage is declared anywhere in that descriptor, the honest conclusion is this
device may simply not expose a gyroscope to the OS, and gyro-to-stick/gyro-to-mouse would
need to be marked unsupported on the EX rather than forced.

### Four gyro-sourcing avenues tried, all dead ends

Kyle asserted there is definitely a physical gyroscope on this device (confident, not
speculative). That raised the bar from "confirm whether gyro exists" to "find where it
actually lives," since the WinRT Sensor API clearly isn't the answer. Tried, in order:

1. **Intel ISH driver health check.** `pnputil /enum-drivers` confirms the FULL genuine
   Intel ISH stack is installed: `ishheci.inf` (HECI transport), `ishipcm.inf` (IPC),
   `ishhidbus.inf` (HID bus), `ishhidmini.inf` (HID mini-driver), `ishoed.inf`. Microsoft's
   `sensorshidclassdriver.inf` (the standard HID-Sensor-Collection-to-WinRT bridge) is bound
   on top, device `Status=OK`. This is not a missing/broken driver situation.
2. **Raw HID descriptor parsing of the ISH collection** (`Diagnostics/Claw8EXProbes/
   SensorDescriptorProbe`, hand-rolled HID report-descriptor walker, not HidSharp's
   higher-level parser, specifically to see nested Collection/Usage announcements a
   Gyroscope sub-collection would use). Dead end before it could even try: the ISH's "HID
   Sensor Collection V2" node is claimed under PnP Class `Sensor`, not `HIDClass` — its raw
   HID device interface isn't published for user-mode enumeration at all (confirmed via a
   full unfiltered `HidSharp.DeviceList.GetHidDevices()` dump: PID 0x0AC2 does not appear
   anywhere, even though Device Manager also shows a *separate* "Intel(R) ISS HID Device"
   HIDClass node at the same VID/PID — that one didn't show up in HidSharp's enumeration
   either). This is the Sensor Class Extension's exclusive-ownership-by-design behavior, not
   something specific to the EX or fixable from a probe.
3. **Controller-side motion streaming** (`Diagnostics/Claw8EXProbes/ControllerMotionProbe`).
   Hypothesis: on many handhelds the gyro chip lives in the *controller* hardware, not the
   chassis ISH — HandheldCompanion's `ClawA1M.cs` sends a `SetMotionStatus` vendor HID
   command (report id `0x0F`, commandType `0x2F`, matching the exact wire format
   `MSIClawHidController.cs` already uses for `SwitchMode`) to turn on IMU report
   streaming, though HC's own readout path for it (`SensorFamily.Controller`) turned out to
   have no implementation per Phase 2. Sent `{0x0F,0,0,0x3C,0x2F,1}` to the vendor command
   interface, then listened on every openable `VID_0DB0` HID interface (the command channel
   itself, the consumer-control collection, and the raw gamepad `IG_05` collection — the
   keyboard and mouse collections couldn't be opened, exclusively claimed by Windows' own
   input drivers) for 12 seconds while physically tilting the device. **Zero input reports
   arrived on any interface.** Restored with `{0x0F,0,0,0x3C,0x2F,0}` after.
4. **DirectInput axis/slider check** (`Diagnostics/Claw8EXProbes/DInputMotionProbe`).
   Hypothesis: DirectInput sometimes surfaces vendor-extended axes/sliders a raw HID report
   read wouldn't distinguish from noise. Switched the controller to DInput mode via the
   same `SwitchMode` command `ClawButtonMonitor.cs`/`MSIClawHidController.cs` already use,
   confirmed via `Get-PnpDevice` that `PID_1902` nodes did appear, then tried
   `DirectInput().GetDevices(...)` across `Gamepad`, `Joystick`, and `Driving` device-type
   filters, retrying for 8 seconds. **DirectInput enumerated zero devices of any of those
   types, system-wide** — not a VID/PID mismatch, a complete empty result. Restored XInput
   mode afterward; confirmed via `Get-PnpDevice` the command interface is back to `PID_1901`,
   `Status=OK`.

**All four avenues came back empty.** This is not "we tried once and gave up" — each probe
targeted a structurally different place gyro data could plausibly live (chassis sensor via
WinRT, chassis sensor via raw HID, controller via HID reports, controller via DirectInput
axes), used the exact wire formats/patterns already proven correct elsewhere in this
codebase (`MSIClawHidController`'s command format, `ClawButtonMonitor`'s DirectInput
acquire pattern), and each was verified to at least partially work up to the point of
failure (mode switches happened, HID opens succeeded, commands sent without error) — the
absence of data is the finding, not a probe bug. Given Kyle's confidence that a gyroscope
physically exists, the leading hypotheses now are: (a) an ISH/EC firmware or BIOS
configuration issue gating exposure regardless of software probing (nothing found via
Windows Update, MSI's site, or public reviews/specs — none confirm or deny a gyro for this
SKU either), or (b) the gyro requires a vendor driver/config package (MSI Center M, not
just the SDK that's currently installed) that provisions something none of the in-box
Microsoft/Intel drivers do on their own. Installing the full MSI Center M app is the next
step, pending on Kyle. **No gyro-related code should be written in Phase 4 until this
resolves one way or the other** — there is nothing to port yet, because nothing has been
found to read from.

---

## 2026-07-05 — Phase 3 P1 RESOLVED: gyro found, streaming, via Windows.Devices.Sensors.Custom

Kyle re-asserted the gyro definitely exists and asked for the investigation to continue.
Before accepting the "install MSI Center M" hypothesis, ran the cheap OS-level diagnostics
the previous session had not tried. The second one cracked it.

**Diagnostics run first (all read-only):**
- Sensor services: `SensorService` = Running (Manual). `SensorDataService`/`SensrSvc`
  stopped — normal (demand-start). Not the cause.
- HidHide / ViGEm: **neither driver is installed on this machine at all**
  (`Get-PnpDevice` no match, no `HKLM\...\Services\HidHide`). Two consequences:
  (a) the Phase 3 "DirectInput enumerated zero devices" result was NOT HidHide cloaking —
  still unexplained, but moot for gyro; (b) flagged for Phase 4/5: the helper's controller
  emulation path REQUIRES ViGEm + HidHide, so they must be installed before controller
  validation (the app's driver-check service normally handles this).
- ISH "HID Sensor Collection V2" PnP node has **no child devnodes** (`DEVPKEY_Device_Children`
  empty) — modern Sensor Class Extension exposes sensors as *device interfaces*, not child
  PDOs, so the earlier child-node reasoning couldn't have found anything.

**The break: device-interface enumeration of the ISH node.** Searching
`HKLM\SYSTEM\CurrentControlSet\Control\DeviceClasses` for interfaces published by
`HID\VID_8087&PID_0AC2` found TEN interface classes, four of which follow the
HID-sensor-usage-derived custom-sensor-type pattern `{000000XX-766d-4333-8262-27e82dd158b1}`:

| Interface class GUID | HID usage | Reference-string FriendlyName | CM API state |
|---|---|---|---|
| `{00000073-766d-...}` | 0x73 Accelerometer 3D | "Physical Accelerometer" | **LIVE** |
| `{00000076-766d-...}` | 0x76 Gyrometer 3D | **"Physical Gyrometer"** | **LIVE** |
| `{00000233-766d-...}` | 0x233 (vendor) | "Shake Gesture" | LIVE |
| `{00000302-766d-...}` | 0x302 (vendor) | "Simple DMD" | LIVE |

Verified live (not stale registry) via `CM_Get_Device_Interface_List` with
`PRESENT` flag. Meanwhile **`GUID_DEVINTERFACE_SENSOR` ({BA1BB692-9B7A-4833-9A1E-525ED134E7E2})
has ZERO interfaces system-wide** — and `Gyrometer.GetDeviceSelector()` turns out to be
`System.Devices.InterfaceClassGuid:="{09485F5A-759E-42C2-BD4B-A349B75C8643}" AND
System.Devices.InterfaceEnabled:=true`, which matches nothing on this machine either.

**Root cause of the null Gyrometer (now proven, no longer hypothesis):** the EX's sensor
stack publishes the IMU only under HID-usage-derived *custom sensor* interface classes —
never under the standard Gyrometer/Accelerometer interface classes or
GUID_DEVINTERFACE_SENSOR. Every standard API (WinRT `Gyrometer.GetDefault()`, Win32 Sensor
COM API, DirectInput sensors) enumerates the standard classes, so they all correctly saw
nothing. The data was always there, one GUID away. This also explains why the privacy
consent key that mattered was `sensors.custom`.

**Live data confirmed** via new probe `Diagnostics/Claw8EXProbes/CustomSensorProbe`
(net8.0-windows, `CustomSensor.GetDeviceSelector(guid)` → `DeviceInformation.FindAllAsync`
→ `CustomSensor.FromIdAsync` → `ReadingChanged`):
- **Gyrometer: 491 readings in 5 s (~100 Hz)**, `MinimumReportInterval=10 ms`. At-rest
  values ≈ (1.19, −0.28, 0.14) — plausible uncalibrated bias, units expected deg/s per HID
  sensor spec (scale unverified until someone physically rotates the device).
- **Accelerometer: 2495 readings in 5 s (~500 Hz)**, `MinimumReportInterval=2 ms`. At-rest
  ≈ (0.007, −0.097, 1.003) g — Z ≈ +1 g with the device lying flat: correct gravity vector,
  so units g and magnitude are verified for the accel.
- Shake Gesture: 0 events in 5 s at rest (event-driven, expected). Simple DMD: 1 event
  (motion-state 0 = stationary).

**Reading property-bag decode** (keys are `"{propertyset-GUID} <PID>"` strings):
- `{C458F8A7-4AE8-4777-9607-2E9BDD65110A}` PID 2 = timestamp (DateTimeOffset),
  PID 161/162/163 = X/Y/Z data values, PID 188 = monotonic µs counter, PID 187 = constant
  152 (unidentified, maybe report id/status).
- `{B14C764F-07CF-41E8-9D82-EBE3D0776A6F}` PID 5 = the sensor's HID usage as int
  (115=0x73 accel, 118=0x76 gyro, 770=0x302 DMD) — handy runtime sanity check.

**Decision for Phase 4 gyro implementation:** add a `CustomSensor`-based
`IGyroSourceAdapter` beside `WindowsSensorGyroSourceAdapter`, and have
`ClawGyroSourceAdapter` fall back to it when `Gyrometer.GetDefault()` is null. Runtime
fallback (not variant-keyed) keeps A2VM on its proven standard path and gives the EX the
custom path, with no detection-config coupling. Keep the ClawA1M axis remap initially —
same-vendor chassis sensor — but **flag axis mapping/signs as UNVERIFIED on the EX** until
a human physically rolls/pitches/yaws the device (Phase 5 row 6/7). Rationale recorded:
the custom path bypasses whatever orientation normalization the standard stack would apply,
so the A1M remap is a starting hypothesis, not a measurement.

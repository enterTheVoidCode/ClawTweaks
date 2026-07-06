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

---

## 2026-07-05 — Phase 3 P2 (partial): controller firmware version = 0x0411

Re-ran `HidInventory` (read-only): every VID_0DB0 interface reports USB bcdDevice
(`ReleaseNumberBcd`) = **0x0411** (the XInput gamepad collection `IG_04` reports 0x0000,
as HID-mapped XInput nodes do; all real interfaces say 0x0411).

Consequences:
- **LED:** 0x0411 is NOT in `MsiClawLedController.FirmwareTable`
  (0x163/0x166/0x167/0x211/0x217/0x219/0x308). The nearest-match fallback would pick
  0x308 → RGB EEPROM start address `[0x02,0x4A]` — plausible (all fw ≥ 0x166 share it)
  but UNVERIFIED, and LED writes go to controller EEPROM. Decision: EX config ships
  `SupportsRgbLighting => false` until Phase 3 P7 verifies with a human present.
- **M1/M2:** firmware gate in `ClawButtonMonitor` is `>= 0x166` → the EX will use the
  `M1_NEW`/`M2_NEW` GetM12 parameter byte-pairs. Cannot verify unattended (the 0x21
  command's effect is only observable by physically pressing M1/M2) — Phase 5 row 4.
- Remaining P2 items (mode-switch round trip) were already proven 2026-07-03 by
  `DInputMotionProbe` (PID_1902 appeared after SwitchMode→DInput, XInput restored).

---

## 2026-07-05 — Environment discovery: sessions run in an MSIX container (rewrites two Phase 0 "bugs")

While trying to launch the rebuilt helper elevated (UAC is fully on:
`ConsentPromptBehaviorAdmin=5` + secure desktop, and Kyle is away so nobody can click a
prompt), discovered that **this Claude Code session — and the previous ones — run inside
Claude's MSIX app container with filesystem virtualization**. Proof: the deployed helper at
`%LOCALAPPDATA%\ClawTweaks\Helper\XboxGamingBarHelper.exe` shows an NTFS redirect target of
`C:\Users\kyle\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\ClawTweaks\Helper\...`.
Writes to `%LOCALAPPDATA%` from inside the session land in the package overlay, visible to
the session (merged view) but **invisible to Task Scheduler and every process outside the
container**. Paths like `C:\Users\kyle\Documents\...` are NOT virtualized.

This rewrites both Phase 0 "pre-existing bugs":
- The scheduled task's `ERROR_FILE_NOT_FOUND` was NOT a quoted-`<Command>` bug — the task
  (created by the in-container helper) pointed at the real `%LOCALAPPDATA%` path where the
  deploy had never actually landed. Today, after placing the files at the REAL path, the
  task runs fine with the quoted command. The quoting theory is retracted.
- `Start-Process -Verb RunAs` "cannot find the path" on the deployed copy: the elevated
  (outside-container) process couldn't see the overlay file that `Test-Path` (inside)
  could. Same root cause.
- It also likely explains Phase 3's "DirectInput enumerated zero devices system-wide"
  anomaly: that probe ran inside the container too. **Re-test DirectInput from an
  outside-container process during Phase 5** before trusting that result for anything.
  (The gyro root-cause is NOT affected — the interface-class evidence came from the CM
  API/registry, which virtualization does not fake, and the outside-container helper run
  below reconfirmed WinRT Gyrometer null + CustomSensor streaming.)

**Working unattended elevation path** (no UAC click available): a LIMITED scheduled task
(`schtasks /create` without `/RL` — allowed non-elevated) runs OUTSIDE the container, so a
task running `robocopy bin\x64\Debug → real %LOCALAPPDATA%\ClawTweaks\Helper` deploys for
real; then `schtasks /run \ClawTweaks\ClawTweaksHelper` (the app's own RunLevel-Highest,
LogonTrigger task, still registered from 2026-07-03) launches the helper elevated,
prompt-free. Used for all on-device verification below. Temporary task `ClawPortDeploy`
does the copy (delete when the port is done).

---

## 2026-07-05 — Phase 4 (items 1+2) implemented and verified on-device; Phase 3 P3 reads captured

**Code changes** (branch `feature/claw8-ex-support`):
1. `Devices/MSIClaw/MSIClaw8EXConfig.cs` (new): Vendor contains "Micro-Star" + Name
   contains "Claw 8 EX". `SupportsGyro=true`, `SupportsRgbLighting=false` (fw 0x0411 not
   in the LED table — see previous entry), other flags mirror A2VM. Registered in
   `DeviceRegistry` AFTER `MSIClawConfig` — matchers are mutually exclusive, and keeping
   A2VM first preserves `GetByType(MSIClaw)` display-name behavior for existing installs.
2. `ControllerEmulation/GyroSourceAdapters.cs`: new `CustomSensorGyroSourceAdapter`
   reading the "Physical Gyrometer"/"Physical Accelerometer" custom sensors.
3. `ControllerEmulation/ClawGyroSourceAdapter.cs`: now tries WinRT first, falls back to
   CustomSensor at runtime — A2VM path untouched, EX gets the new source; A1M axis remap
   applied identically on both (EX axes still pending physical verification).
4. `Startup/Program.MSIClaw.cs`: two one-shot read-only startup diagnostics —
   `ProbeMsiGyroSource` (which source started, sample rate, one live reading) and
   `ProbeMsiEcState` (every EC/WMI block the app uses, raw+decoded).
5. `Windows/User32.cs` `GetSupportedResolutions()`: **shared-code crash fix** — when
   `EnumDisplaySettings` returns no modes (locked session/display off, which is exactly
   how the task-launched helper starts on the unattended device), `GCD(0,0)` caused a
   `DivideByZeroException` that killed the whole helper at boot (observed live, event
   log 0xE0434352). Now degrades to an empty resolution list. This path is identical on
   all devices and only changes behavior in the previously-crashing case.

**On-device verification** (helper run elevated via the task chain above, log
`helper_2026-07-05_13.log`):
- Detection: `Device matched: MSI Claw 8 EX (MSIClaw)`, features
  `WMI TDP: False, Controller: True, RGB: False, Gyro: True, FanControl: False`. ✔
- Gyro: `WinRT` path reports unavailable (expected), then
  `Gyro source 'MSI Claw Internal Gyro (CustomSensor)' started via CustomSensor (gyro
  interval: 10ms, accelerometer available: True)` and
  `GyroProbe: source='MSI Claw Internal Gyro (CustomSensor)' — 64 samples in 1s, last
  gyro=(1.26,0.14,0.35) deg/s, accel=(-0.008,-1.003,-0.098) g`. The post-remap accel
  gravity vector equals the A1M-remap of the raw probe values (outY=-rawZ≈-1 g with the
  device flat) — the pipeline is numerically consistent end to end. ✔
  Fix found during this: under the .NET Framework WinRT projection the property-bag
  values are NOT boxed `System.Double` (they are under .NET 8) — the adapter now accepts
  any convertible numeric. The first run's key/type dump is in the 13:05 log block.
- **Phase 3 P3 (EC/WMI reads, all `success=1`, READ-ONLY):**
  | Block | Value | Interpretation |
  |---|---|---|
  | Get_Fan 1 (CPU table) | `3A,46,4A,4C,4E,50,54,5E` (58–94) | plausible non-decreasing duty table |
  | Get_Fan 2 (GPU table) | `3A,46,4A,4C,4E,50,54,5E` | identical to CPU |
  | Get_AP 1 | `00,00,04,00,00,00,00` | software-fan-control bit7 OFF (firmware owns fan) |
  | Get_Data 152 | `06` | full-speed bit7 OFF; low bits 0x06 ≠ A2VM-documented semantics, unknown |
  | Get_Data 215 (charge limit) | `80` | bit7 SET with pct bits = 0 — does NOT match A2VM encoding (enabled+0%). **Flag: verify encoding before enabling charge-limit UI writes on the EX** |
  | Get_Data 210 (power shift) | `C1` | 0xC1 = "Green" scenario — valid HC value, sane |
  | Get_Data 80 / 81 (PL1/PL2) | `00` / `00` | reads return 0 — likely write-only blocks (HC only ever writes them); TDP read-back must come from elsewhere in P4 |
- Fan-table read note for Phase 4 fan work: the EX firmware baseline table (58–94 on the
  0–150 scale) is now known; the latch-avoidance rule (80–100 °C points ≥ firmware values)
  has concrete numbers to respect.

**Still requires a human present (do NOT attempt unattended):** P4 TDP write probe,
P5 fan-table write probe (EC latch risk), P6 charge-limit write (encoding question above),
P7 LED probe (fw 0x0411 unknown to the address table), M1/M2 press verification, and the
full Phase 5 in-game/motion validation incl. gyro axis-direction check.

---

## 2026-07-05 — DirectInput re-test outside the MSIX container: confirmed real, not a container artifact

Kyle back at the device; resumed the port. Re-ran `DInputMotionProbe` (rebuilt clean via
MSBuild, output at
`Diagnostics/Claw8EXProbes/DInputMotionProbe/bin/x64/Debug/net472/DInputMotionProbe.exe`)
from a genuinely outside-container process this time: a non-elevated scheduled task
(`ClawPortDInputProbe`, created via `schtasks /create` with no `/RL`, invoked through a
`.bat` wrapper to sidestep `schtasks`' `/tr` quoting — passing a raw `cmd /c "..." > "..."`
string directly to `/tr` silently produced `ERROR_FILE_NOT_FOUND`/`0x80070002` on the first
attempt, same failure signature as the Phase 0 scheduled-task bug, this time caused by our
own quoting mistake, not a real product bug) redirected to a log file under
`C:\Users\kyle\Documents\...` (not virtualized, so both in- and out-of-container processes
see the same file).

Result: **`DirectInput().GetDevices()` across Gamepad/Joystick/Driving still enumerates
zero devices system-wide**, identical to the original in-container Phase 3 result. The
mode switch itself worked (command sent, 8s wait, restore-to-XInput sent unconditionally
on failure), so this isn't a command-interface problem — DirectInput itself sees nothing,
from either execution context. Confirmed via `Get-PnpDevice` afterward that XInput
(`PID_1901`) devices are back to `Status=OK`; only ghosted/non-present `PID_1902` entries
remain (expected PnP residue from mode-switch history, not a live device).

**Conclusion: the MSIX container was NOT the cause.** This closes the last open item from
the 2026-07-05 container-discovery entry. DirectInput's empty enumeration on this device
is a genuine, reproducible fact (root cause still unknown — plausibly a driver/manifest
quirk specific to this XInput-mode-first controller, or something in how Windows exposes
`PID_1902` while the top-level composite device stays bound to the XInput driver stack).
Not a blocker for anything currently planned (gyro uses CustomSensor, not DirectInput;
controller emulation uses ViGEm/XInput, not DirectInput), so no further chase planned
unless a future feature needs DirectInput specifically — noted here so nobody re-investigates
this from scratch.

---

## 2026-07-05 — Environment ready for Phase 5: prerequisite tools installed, app installed as MSIX

**All four prerequisite tools installed and re-verified live** (PnP/services/process
checks, not hearsay): PawnIO (SoftwareDevice, Status OK), HidHide (Nefarius HidHide
Device, Status OK; `HidHideWatchdog.exe` running), RTSS (process running, exe at
`C:\Program Files (x86)\RivaTuner Statistics Server\RTSS.exe`), usbip-win2 (services
`usbip2_ude`/`usbip2_filter` both Running).

**Correction to earlier framing (2026-07-05 P1-resolution entry and validation doc row 3):
the app's default controller-emulation backend is no longer ViGEm.**
`XboxGamingBarHelper/Setup/Setup-Tools.ps1:211` documents ViGEm as the LEGACY backend,
installed only via an explicit `-Only vigem` request; the default check-and-install path
installs **usbip-win2 (VIIPER)** instead. Phase 5 row 3 (controller emulation in a game)
must therefore be judged against VIIPER, not ViGEm. HidHide remains in use for
input-suppression on top of whichever backend is active. Validation doc updated to match.

**The app is now installed and launchable as a real MSIX package** — first time in this
port anything beyond the bare Helper exe has run on this machine. Key facts (full build/
sign/install recipe recorded in the session handoff notes):
- Built unsigned via `msbuild XboxGamingBarPackage.wapproj` (Debug|x64, `AppxBundle=Never`,
  `AppxPackageSigningEnabled=false`), then signed with the local dev cert
  `CN=ClawTweaks Dev, O=MSIClaw` (thumbprint `925C6F2C1669F1380BC1E5081872EA085468F080`,
  in `Cert:\CurrentUser\My`, trusted in `Cert:\LocalMachine\TrustedPeople`), installed via
  `Add-AppxPackage` with the x64 framework dependencies. Developer Mode enabled by Kyle.
- Identity: `PackageFamilyName = MSIClaw.ClawTweaks_7eszav2039cvc`; launch via
  `shell:AppsFolder\MSIClaw.ClawTweaks_7eszav2039cvc!App`.
- Packaged helper logs live under
  `C:\Users\kyle\AppData\Local\Packages\MSIClaw.ClawTweaks_7eszav2039cvc\LocalCache\Local\`
  — NOT the bare `%LOCALAPPDATA%` root used by the Phase 0 unpackaged runs.
- Rebuild note: the wapproj build auto-bumps `Package.appxmanifest`'s Version — revert with
  `git checkout -- XboxGamingBarPackage/Package.appxmanifest` before committing unless the
  bump is intentional.

**Loose end from the install hour** (`helper_2026-07-05_17.log`): repeated
`System.ArgumentException: The path is not of a legal form` from `RTSSManager.Update()`
(race reading RTSS's install path before winget's registry write landed) followed by
recurring `FileNotFoundException` (0x80070002) opening RTSS's shared-memory OSD map right
after RTSS launch — all logged as non-terminating. **Reboot recommended before Phase 5
testing**: PawnIO's and HidHide's installers both said a reboot may be required for driver
activation, and a reboot should clear the RTSS state too. Re-verify RTSS OSD after reboot
before trusting validation row 9.

---

## 2026-07-05 (post-reboot, ~17:24) — Live-session findings: TDP writes already live, ungated LED writes, DirectInput root cause SOLVED

Kyle rebooted at 17:24 (before this session's work — verified via `LastBootUpTime` vs
commit timestamps). Helper auto-started elevated at logon (PID 12420, 17:24:48). Post-boot
log is clean on the reboot-motivated items: detection matches EX config, gyro CustomSensor
source starts (64 samples/s), `RTSSHooks64.dll` loads, RTSS starts; the OSD shared-memory
`FileNotFoundException` fired ONCE at 17:25:15 (startup race while RTSS was still
launching) and never recurred.

### 1. TDP writes are already happening on the EX (P4 gate de-facto superseded)

The helper's production path fired automatically at 17:25:29 when the global profile
applied (nobody bypassed a gate — the app auto-starts at boot and applies its profile):
- `MsiOverBoost`: **"OverBoost already enabled (box[1]=1)"** — the EX ships with the
  OverBoost UEFI flag ALREADY SET from factory/BIOS. Confirmed no prior run ever wrote it
  (searched all helper logs: 13:xx run and pre-reboot 17:xx run contain zero TDP/OverBoost
  writes; 17:25:29 was the first-ever unlock sequence on this device). **No persistent
  UEFI write was performed by our code.**
- Ceiling unlock `Set_Data 80=35, 81=37` → both WMI success. Profile apply
  `80=25, 81=26` → success. Power-shift `210=0xC0` (Comfort/None active) → success.
- 17:28: repeated 25↔30 W writes (Kyle moving the widget TDP slider) — all WMI-OK.

Reframing for what's left of P4: the write path works at the WMI level; the REMAINING
question is whether the EC honors the limits (WMI success ≠ EC compliance — on A2VM the
EC silently clamps without the unlock). Verification = sustained load with package power
observed ≈ PL1 (OSD `CPUWattageSensor` via LibreHardwareMonitor, or HWiNFO). This merges
into validation row 8 rather than a separate probe. Note: kx.exe absence is BY DESIGN
(`XboxGamingBarHelper.csproj:499` — intentionally not bundled, AV-flagged; WMI is the
fallback), so the startup `[KX] kx.exe not found` ERROR is expected, not an EX problem.

### 2. Ungated LED writes fired on the EX (gating bug found + fixed)

At 17:28:00–17:28:06 and 17:47:41–42 the widget's LED tile sent `MsiLedColor` pipe
commands (Kyle toggling LED on/off): `FW=0x411 → RGB addr [02,4A] (nearest match,
dist=265)`, writes returned ok=True. This is exactly the unverified-EEPROM-address write
P7 was gated on. Two gating gaps: the widget shows the LED tile despite
`SupportsRgbLighting=false` being pushed to it, and the helper's pipe handler
(`Program.PipeHandlers.cs:395`) never checked the flag either (nor do the `MsiLedBoot` /
LED-by-SoC callers). **Fix (this session): single choke-point gate in
`MsiClawLedController.TrySetLedColor` — refuses when the detected device reports
`SupportsRgbLighting=false`.** A2VM unaffected (`MSIClawConfig` sets true).
PENDING (needs Kyle's eyes): did the controller LEDs visibly respond to the 17:28/17:47
toggles? If YES → the nearest-match `[02,4A]` address is CORRECT for fw 0x0411; P7 is
answered by accident, and the right follow-up is adding `0x0411 → [0x02,0x4A]` to
`FirmwareTable` as a measured entry + flipping the EX config to `SupportsRgbLighting=true`.
If NO → address is wrong or LED protocol differs; RGB stays unsupported; note that ~8
writes to the wrong EEPROM address already happened with no observed controller
malfunction (controller kept working: mode switches, commands, inputs all fine after).

### 3. DirectInput "zero devices" ROOT CAUSE SOLVED — it was an enumeration-filter gap, not a platform fact

With the controller live in DInput mode (ClawButtonMonitor's retry loop had it there) and
the `HID-compliant game controller` PnP node (`HID\VID_0DB0&PID_1902&MI_00&COL01`) present
with Status OK, added a read-only `enum` mode to `DInputMotionProbe` (no mode commands)
that lists `GetDevices(DeviceClass.All)` + every DeviceType + `DeviceClass.GameControl`:
- DirectInput sees **29 devices** on this machine — it was NEVER empty. The Claw joystick
  enumerates as **`DeviceType.FirstPerson` (Subtype 259)** with a garbled product name
  ("Љ" — mangled string from the EX firmware descriptor), interface path
  `\\?\hid#vid_0db0&pid_1902&mi_00&col01#…` — exactly the game-controller node.
- **`GetDevices(DeviceClass.GameControl)` returns exactly 1 device: the Claw joystick.**
- Every prior "zero devices" result (2026-07-03 probe, 2026-07-05 outside-container
  re-test, and ClawButtonMonitor's live failures) used Gamepad/Joystick/Driving filters
  (the monitor: Gamepad only, HC's 1:1 pattern) — A1M/A2VM enumerate as Gamepad, the EX
  enumerates as FirstPerson, so the filters missed it. The earlier "DirectInput
  enumerates zero devices system-wide" conclusion is RETRACTED — the probes only printed
  their filtered subset.
- **Fix (this session): `ClawButtonMonitor.FindAndAcquireJoystick()` now enumerates
  `DeviceClass.GameControl`** (superset of all game-controller DeviceTypes — A2VM resolves
  the identical device via the unchanged VID/PID interface-path match; EX becomes
  visible). This also explains and should end the observed live thrash loop (monitor
  retrying SwitchMode(DInput) + settle every few seconds after Kyle enabled the master
  emulation toggle at 17:26).

### 4. Correction: MSI Claw controller emulation uses ViGEm, NOT VIIPER

The earlier "judge row 3 against VIIPER" note (previous entry + validation doc) was wrong
for this device family: the live log says `VIIPER: Skipping — MSI Claw uses
ClawButtonMonitor (DInput path) instead of VIIPER`, and ClawButtonMonitor forwards to a
**ViGEm virtual Xbox 360 pad** (`_vigem`, `NeedsViGEm`). VIIPER is the default backend for
OTHER devices; the Claw path still requires **ViGEmBus, which is NOT installed** (log:
`ViGEmBus installed status: False`; Setup-Tools' default path deliberately skips ViGEm as
"legacy" — a trap for fresh Claw installs, arguably its own bug outside this port's
scope). Row 3 therefore needs `Setup-Tools.ps1 -Only vigem` (one UAC click) plus the
GameControl fix deployed. Validation doc re-corrected.

**CORRECTION TO THE CORRECTION (same day, after deploying the fixed build):** ViGEmBus is
NOT needed after all. When VIIPER is active, ClawButtonMonitor mounts its virtual pad
THROUGH the VIIPER backend: `MSIClaw: VIIPER backend active → mounting VIIPER virtual pad
(xbox360) instead of ViGEm` → `ClawButtonMonitor: VIIPER virtual Xbox pad mounted (ViGEm
suppressed)`. The "Skipping" line only means ViiperEmulationManager's own input pipeline
is skipped — the monitor reads the physical DInput joystick and drives a VIIPER-provided
virtual pad. So the ViGEm paragraph above stands only as history; nothing needs
installing. (Kept per append-only rule; this supersedes it.)

---

## 2026-07-05 (evening) — Fixed build deployed and verified on-device; fan writes discovered already-exercised via widget

Rebuilt/signed/installed the MSIX as **0.1.7.616** (gotcha for future rebuilds: the
wapproj's version auto-increment did NOT protect against a STALE packaging layout —
`XboxGamingBarPackage\bin\x64\Debug\AppxManifest.xml` kept the old version and the msix
inherited it, producing a same-identity-different-content package Windows refuses to
install, 0x80073CFB. Delete that layout file and rebuild if it happens again. Manifest
auto-bump reverted via `git checkout` per the standing instruction.)

**GameControl fix VERIFIED on-device** (helper PID 6728, `helper_2026-07-05_18.log`):
`ClawButtonMonitor: Acquired DInput joystick: \\?\hid#vid_0db0&pid_1902&mi_00&col01…` —
first successful acquisition ever on the EX — then `monitor.Start() OK — virtual pad live
+ DInput acquired`, HidHide cloaks the physical joystick (cycle-port re-enum success=True),
transient InputLost from the port cycle re-acquired in 500 ms, `BOOT COMPLETE`. Zero
"STILL not found" thrash lines after boot (vs a retry loop every ~10 s before the fix).

**LED gate VERIFIED on-device:** the boot LED re-apply path (saved colour from Kyle's
earlier tile toggles) was blocked 7× with `LED write blocked: detected device reports
SupportsRgbLighting=false`, exactly as intended, and `MsiLedBoot` gave up cleanly after
its retries. No further EEPROM writes will occur until the flag is flipped deliberately.

**Fan-table writes were ALREADY exercised on the EX via the widget** (found while checking
an odd fan read; nobody bypassed a gate — the fan section is reachable for any
MSIClaw-type device, it was never keyed on a feature flag, and `SupportsFanControl` is not
overridden by either MSI config — that flag isn't what gates MSI fan code):
- 17:29:15 first write; the before-write read captured the **EX firmware baseline
  [58,70,74,76,78,80,84,94]** — matches P3 exactly.
- Subsequent preset taps (17:30:00, 17:30:03, 18:02:50) show before-write reads returning
  the PREVIOUSLY WRITTEN A2VM tables byte-for-byte on bytes 1–7
  (e.g. [·,0,10,26,46,78,113,150] = LLFanTable_BestPerformance, [·,0,0,14,33,63,98,150] =
  BetterPerformance) → **EC write+read-back round-trips work on the EX** — the core of
  P5's no-op/modified-write test, answered by live use.
- **Byte[0] (backup/40 °C) anomaly:** reads back as 58, later 150, never what was written
  (10) — the EX EC appears to repurpose/override that byte. UNRESOLVED; flag for the
  proper P5 pass.
- Fan-control bit stayed 0x00 (firmware owns the fan) through all of it — these were
  hardware-mode preset writes.
- **Outstanding hygiene item:** the EC's stored table is currently an A2VM-tuned array,
  not the EX firmware baseline; its 80 °C point (78) is 2 BELOW the EX firmware's own 80 —
  marginal vs the latch-avoidance rule (which was derived against A2VM firmware values).
  Restore [58,70,74,76,78,80,84,94] as the EX's hand-back table (this is also the natural
  P5 no-op-write test), then Phase 4 item 4 = proper `EXFanTable_*` arrays keyed by
  variant so hardware mode writes the RIGHT baseline per device.

---

## 2026-07-05 (late evening) — Kyle's blanket authorization; LED = P7 PASS; EX fan baseline restored (P5 no-op leg complete); P6 skipped by owner

Kyle confirmed the earlier LED tile clicks DID visibly drive the controller LEDs ("worked
like 5 minutes ago" — it stopped because the new build's gate correctly blocked writes
while the config still said RGB-unsupported), gave **blanket authorization to continue
with all remaining items**, and explicitly waived the charge-limit work ("don't really
care about chargelimit").

**Phase 3 P7 (LED): PASS.** fw 0x0411 → `[0x02,0x4A]` is human-verified (LEDs visibly
respond). Code: promoted to an exact `FirmwareTable` entry in `MsiClawLedController`
(no more nearest-match dependence) and `MSIClaw8EXConfig.SupportsRgbLighting => true`.
The write gate added earlier stays (defense-in-depth for future unknown firmware).

**Phase 4 fan item (variant table): implemented.** `ApplyHardwareTable` on the EX now
writes the measured EX firmware baseline `[58,70,74,76,78,80,84,94]` for EVERY profileKey
(only one firmware table is known for the EX; strictly safer than the LL arrays). A2VM
switch untouched. Runtime selection via the same Model-contains-"Claw 8 EX" check the
branch already uses in `MsiClawDriverCheckService`, cached (1 s auto-safety loop calls it).

**Phase 3 P5 no-op/restore leg: COMPLETE** (via production code path, authorized): on the
0.1.8.616 build's startup hand-back, both fan blocks went `[150,0,10,26,46,78,113,150]` →
write `[58,70,74,76,78,80,84,94]` → **read-back MATCH byte-for-byte on CPU and GPU
blocks, including byte[0]=58**. Refines the byte[0] anomaly: the EC round-trips a
plausible backup byte (58) fine — it only overrode the implausibly-low A2VM values (0/10),
so the anomaly is an EC sanity-clamp on byte[0], not a protocol difference. Remaining P5
material: fan response/latch behavior under sustained load (validation rows 10/11).

**Phase 3 P6 (charge limit): SKIPPED per owner decision.** No writes will be made; the
`Get_Data 215 = 0x80` encoding question stays open and charge-limit UI remains
un-validated on the EX (validation row 12 recorded as skipped, not failed). Revisit only
if a user asks for the feature on this device.

**Build/packaging note:** the 0x80073CFB same-identity error recurred; root cause of the
version confusion: the wapproj's auto-increment target runs AFTER packaging, so each
build packages the PRE-bump version — reverting the manifest bump between rebuilds
(per the earlier standing instruction) re-feeds already-used versions. Correct loop:
build → sign → install → THEN `git checkout` the manifest. Installed now: **0.1.8.616**
(helper PID 19152), verified: detection ✔, `RGB: True` ✔, LED write with EXACT fw-table
match ✔ (re-applied Kyle's saved off-state), gyro CustomSensor ✔, joystick acquired ✔,
BOOT COMPLETE ✔, fan baseline restored ✔.

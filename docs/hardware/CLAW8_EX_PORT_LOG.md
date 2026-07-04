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

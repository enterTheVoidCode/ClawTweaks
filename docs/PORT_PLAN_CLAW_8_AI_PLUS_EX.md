# Port Plan: MSI Claw 8 AI+ EX (Panther Lake) Support

**Status:** Not started
**Target device:** MSI Claw 8 AI+ EX (Intel Panther Lake) — the physical device this plan is executed ON.
**Reference device:** MSI Claw 8 AI+ A2VM (Lunar Lake, MS-1T52) — the currently supported hardware.
**End goal:** ClawTweaks runs on the Claw 8 AI+ EX with full feature parity: device detection, controller emulation/remap, gyro, TDP control, fan control, battery charge limit, LED control — without regressing A2VM support.

---

## How to execute this plan (read this first)

You are executing this plan step by step. Rules that apply to EVERY phase:

1. **Track goals.** At the start, create one task per phase with `TaskCreate` (Phase 0 … Phase 6). Mark a phase `in_progress` when you start it and `completed` only when ALL of its acceptance criteria pass. Never work on two phases at once. Never skip a phase.
2. **Never guess hardware facts.** Every VID/PID, WMI name, EC value, firmware byte in this plan for the EX device is UNKNOWN until you measure it on the device. If a probe returns something different from what the A2VM code assumes, write down what you measured — do not "correct" the measurement to match the code.
3. **Read before write.** For every hardware interface (WMI fan blocks, charge limit, HID commands): run the READ probe and record output BEFORE ever running a WRITE probe.
4. **STOP gates.** Steps marked `⛔ STOP` require you to stop, print a summary of what you are about to do and what the recovery path is, and wait for the human to say "go". Do not proceed past a STOP gate autonomously.
5. **Keep a port log.** Append every finding (measured values, probe outputs, decisions) to `docs/hardware/CLAW8_EX_PORT_LOG.md` as you go. Datestamp each entry. This file is your memory — re-read it whenever you resume work.
6. **Subagents:** Use subagents ONLY for the tasks explicitly marked `🤖 subagent` below (read-only code exploration and web research). Never let a subagent touch the hardware, run probes, or edit code — hardware access must be serialized through your main session, and code edits must go through you so the diff stays reviewable.
7. **Branch discipline.** All work happens on branch `feature/claw8-ex-support`. Commit at the end of each phase with message `Claw 8 EX port: Phase N — <summary>`. Never commit to the default branch.
8. **If a step fails 3 times, stop and report.** Write what you tried and the exact error into the port log, then ask the human. Do not improvise around hardware failures.

### Key facts already established (verified in the codebase — do not re-derive)

| Subsystem | File(s) | Current A2VM behavior |
|---|---|---|
| Device detection | `XboxGamingBarHelper/Devices/DeviceDetector.cs`, `Devices/DeviceRegistry.cs`, `Devices/MSIClaw/MSIClawConfig.cs`, `Shared/Data/DeviceInfo.cs`, `Shared/Enums/DeviceType.cs` | Reads `Win32_ComputerSystemProduct` (Vendor/Name/Version). `MSIClawConfig.Matches()` requires Vendor contains `"Micro-Star"` AND Name contains `"A2VM"`. **The EX will fail this check and fall back to Generic — this alone disables gyro, fan, charge limit, remap, TDP unlock.** |
| Controller HID | `Devices/MSIClaw/MSIClawHidController.cs`, `Labs/ClawButtonMonitor.cs`, `Devices/MSIClaw/MsiClawDesktopModeForwarder.cs`, `Startup/Program.MSIClaw.cs` | VID `0x0DB0`; PID `0x1901` = XInput mode (cmd iface UsagePage `0xFFA0`/Usage `0x0001`); PID `0x1902` = DInput mode (UsagePage `0xFFF0`/Usage `0x0040`). Commands: report ID `0x0F`, `0x22` = SyncToROM, `0x24` = SwitchMode (1=XInput, 2=DInput, 4=Desktop). Mode switch re-enumerates USB (~2.5 s). M1/M2 read parameters differ per firmware version (`ClawButtonMonitor.cs:56-60`). |
| Gyro | `ControllerEmulation/ClawGyroSourceAdapter.cs`, `ControllerEmulation/GyroSourceAdapters.cs` (`WindowsSensorGyroSourceAdapter`), used from `Labs/ClawButtonMonitor.cs:259` | Gyro comes from the **Windows Sensor stack** (`Windows.Devices.Sensors.Gyrometer`), with an axis remap ported from HandheldCompanion ClawA1M (Y↔Z swap, sign flips). Note: `ControllerEmulationManager.GyroSource.cs:97-100` returns `null` for MSIClaw (that legacy path is intentionally disabled); the LIVE path is ClawButtonMonitor's `_gyroAdapter`. |
| Fan control | `MSI/MsiClawFanController.cs`, `MSI/MsiClawWmi.cs`, widget: `XboxGamingBar/Features/Devices/GamingWidget.MsiFanControl.cs` | 8-byte EC fan table (0–150 scale, temp-indexed) written via ACPI-WMI class `MSI_ACPI`, instance `ACPI\PNP0C14\0_0`, namespace `root\WMI`, 32-byte packages. Fan tables/curves are **Lunar-Lake-tuned**. There is a dangerous EC "full-speed latch" behavior documented in `MsiClawFanController.cs:28-52` — read those comments before touching fan code. |
| TDP | `Shared/Enums/TdpMethod.cs` (`IntelKxExe`), `Performance/PerformanceManager.cs:~1522` (TDP unlock, PL1=35/PL2=37), `MSI/IntelThermalControl.cs`, Intel IGCL for FPS limiting | PL1/PL2 written via `kx.exe` MCHBAR writes, values tuned for Lunar Lake. A once-per-boot WMI "TDP unlock" replicates HC A2VM behavior. |
| Battery charge limit | `Devices/MSIClaw/MsiClawBatteryManager.cs` | Written to EC via the same MSI ACPI/WMI interface. |
| LED | `Devices/MSIClaw/MsiClawLedController.cs` | HID vendor commands, firmware-version-aware (has per-generation tables, see `:41`). |
| Driver check | `Services/MsiClawDriverCheckService.cs` | Hardcodes the A2VM MSI support URL (`:54`). |
| EC port I/O | `Devices/EC/EcController.cs` | GPD-only (IT5570 via inpoutx64). MSI Claw does NOT use this path — do not touch it. |

---

## Phase 0 — Baseline: build, branch, confirm the failure

**Goal:** A reproducible starting point: the repo builds, and we have logged proof of exactly what fails on the EX device today.

Steps:

1. Create branch: `git checkout -b feature/claw8-ex-support` (from the repo's current default branch, in `C:\Users\kyle\Documents\git\ClawTweaks`).
2. Build the solution: open a Developer PowerShell and run
   `msbuild XboxGamingBar.sln /p:Configuration=Debug /p:Platform=x64 /restore`
   (If `msbuild` is not on PATH, locate it via `& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`.)
3. Create `docs/hardware/` directory and the port log file `docs/hardware/CLAW8_EX_PORT_LOG.md` with a header and today's date.
4. Run the currently-installed ClawTweaks helper on the EX device (or launch the freshly built `XboxGamingBarHelper` exe as admin), then find the NLog output and record the device-detection lines. 🤖 `subagent (Explore)`: "Find where NLog is configured in ClawTweaks (search for NLog.config / LogManager configuration / FileTarget) and report the log file path on disk." Then YOU read that log and copy the detection lines into the port log. Expected on the EX: `No matching device config found for: Micro-Star International Co., Ltd. <EX product name>` (from `DeviceRegistry.cs:65`).

**Acceptance criteria (all must pass):**
- [ ] Build completes with 0 errors (warnings OK).
- [ ] Branch `feature/claw8-ex-support` exists and is checked out.
- [ ] Port log exists and contains the exact "No matching device config found" line (or, if the device unexpectedly DID match, that line instead — record whichever happened).

---

## Phase 1 — Enumerate the EX hardware and diff it against what the code supports

**Goal:** A complete, measured hardware manifest for the Claw 8 AI+ EX, and a filled-in diff table against the A2VM assumptions baked into the code. **No code changes in this phase. No writes to any hardware interface.**

### 1a. Identity probes (PowerShell, run each, paste full output into the manifest)

```powershell
Get-CimInstance Win32_ComputerSystemProduct | Format-List Vendor, Name, Version, IdentifyingNumber
Get-CimInstance Win32_ComputerSystem        | Format-List Manufacturer, Model, SystemFamily, SystemSKUNumber
Get-CimInstance Win32_BaseBoard             | Format-List Manufacturer, Product, Version
Get-CimInstance Win32_BIOS                  | Format-List SMBIOSBIOSVersion, ReleaseDate
Get-CimInstance Win32_Processor             | Format-List Name, NumberOfCores, MaxClockSpeed
Get-CimInstance Win32_VideoController       | Format-List Name, DriverVersion
```

The single most important value is `Win32_ComputerSystemProduct.Name` — this is what `MSIClawConfig.Matches()` sees as `deviceInfo.Model`. Record it character-for-character. Also record the board ID (`Win32_BaseBoard.Product`, expected form `MS-1Txx`).

### 1b. Controller HID probes (read-only)

```powershell
# All MSI-vendor devices, any class:
Get-PnpDevice | Where-Object { $_.InstanceId -match 'VID_0DB0' } | Format-Table Status, Class, FriendlyName, InstanceId -AutoSize
# Full HID inventory (to catch a changed VID):
Get-PnpDevice -Class HIDClass -Status OK | Format-Table FriendlyName, InstanceId -AutoSize
```

Then write a small read-only console probe `Diagnostics/Claw8EXProbes/HidInventory/` (new .NET Framework console project or LINQPad-style script) that uses **HidSharp** (already in `packages/HidSharp.2.1.0`) to list, for every device with VendorID `0x0DB0` (and, if none found, ALL HID devices): ProductID, DevicePath, UsagePage/Usage of each interface, Max input/output/feature report lengths. Model it on the enumeration code in `MSIClawHidController.cs:135-160`. Record output in both controller modes if MSI Center M offers a mode toggle — otherwise just the boot-default mode.

### 1c. Sensor (gyro) probes (read-only)

```powershell
Get-PnpDevice -Class Sensor | Format-Table Status, FriendlyName, InstanceId -AutoSize
```

Then a probe `Diagnostics/Claw8EXProbes/GyroProbe/` that calls `Windows.Devices.Sensors.Gyrometer.GetDefault()` and `Accelerometer.GetDefault()` (mirror how `WindowsSensorGyroSourceAdapter` in `ControllerEmulation/GyroSourceAdapters.cs:49` acquires them), prints whether each is null, the ReportInterval, and 5 seconds of samples at rest and 5 seconds while you tilt the device. Record:
- Is a Gyrometer present at all? (If null → gyro must come from HID raw data instead; note for Phase 2 research.)
- Which physical rotation maps to which reported axis (roll/pitch/yaw → X/Y/Z, and signs). Compare against the A1M remap table in `ClawGyroSourceAdapter.cs:11-24` and record whether the EX matches it or needs a different remap.

### 1d. ACPI-WMI (fan / charge limit interface) probes (READ-ONLY — no Set calls)

```powershell
# Does the MSI_ACPI class exist on the EX?
Get-CimClass -Namespace root\WMI | Where-Object CimClassName -match 'MSI' | Select-Object CimClassName
Get-CimInstance -Namespace root\WMI -ClassName MSI_ACPI -ErrorAction SilentlyContinue | Format-List InstanceName
# ACPI PNP0C14 (WMI mapper) devices present:
Get-PnpDevice | Where-Object InstanceId -match 'PNP0C14' | Format-Table FriendlyName, InstanceId
```

Record whether `MSI_ACPI.InstanceName='ACPI\PNP0C14\0_0'` (the exact path hardcoded in `MsiClawWmi.cs:21`) exists. List ALL method names on the class: `(Get-CimClass -Namespace root\WMI -ClassName MSI_ACPI).CimClassMethods | Select Name`. Compare against the methods the code calls (grep `MsiClawWmi.Set(` and `MsiClawWmi.Get(` call sites in `MsiClawFanController.cs` and `MsiClawBatteryManager.cs` and list every methodName/data-block index used).

### 1e. Software environment

Record: Windows build (`winver`), MSI Center M version installed (Settings → Apps), Intel graphics driver version, whether ViGEmBus and HidHide are installed (`Get-PnpDevice | Where FriendlyName -match 'ViGEm|HidHide'`), whether RTSS/PawnIO are installed, and whether `kx.exe` works on Panther Lake — but do NOT run kx.exe writes yet, just note its presence in the helper directory.

### 1f. Produce the manifest + diff table

Create `docs/hardware/CLAW8_EX_HARDWARE.md` containing all raw probe outputs, then this diff table filled with MEASURED values:

| Item | A2VM (code assumption) | EX (measured) | Same? |
|---|---|---|---|
| WMI Vendor | contains "Micro-Star" | | |
| WMI Product Name | contains "A2VM" | | |
| Board ID | MS-1T52 | | |
| CPU | Lunar Lake (Core Ultra 200V) | | |
| Controller VID/PID (XInput mode) | 0DB0/1901 | | |
| Controller VID/PID (DInput mode) | 0DB0/1902 | | |
| Cmd iface UsagePage/Usage (XInput) | FFA0/0001 | | |
| Cmd iface UsagePage/Usage (DInput) | FFF0/0040 | | |
| Windows Gyrometer present | yes | | |
| Gyro axis orientation vs A1M remap | matches ClawA1M table | | |
| MSI_ACPI at ACPI\PNP0C14\0_0 | yes | | |
| MSI_ACPI method set | Get_AP/Set_AP/Get_WMI/… (list from code) | | |
| MSI Center M present | yes | | |

**Acceptance criteria:**
- [ ] `docs/hardware/CLAW8_EX_HARDWARE.md` exists with raw output from ALL of 1a–1e pasted in.
- [ ] Every row of the diff table has a measured value or an explicit "NOT PRESENT" — no blanks, no "assumed".
- [ ] Probe projects/scripts are committed under `Diagnostics/Claw8EXProbes/`.
- [ ] Port log updated with a summary: which subsystems look identical, which differ.

---

## Phase 2 — Reference research (how do others support this hardware?)

**Goal:** Know what prior art exists before writing any device code. This phase is pure research; run its two subagents in parallel and merge results into the port log.

1. 🤖 `subagent (general-purpose, WebSearch/WebFetch)`: "Research software support for the MSI Claw 8 AI+ EX (Panther Lake MSI Claw generation). Specifically: (a) Does HandheldCompanion (github.com/Valkirie/HandheldCompanion) have a device class for it — look for files like `ClawA1M.cs`, `Claw8.cs`, or Panther Lake variants in `HandheldCompanion/Devices/MSI/`, and report its WMI/product-name detection strings, HID VID/PID, gyro source, fan table format, and any EC/WMI differences vs the A2VM class. (b) Check the Linux kernel `msi-ec` driver and any GitHub issues/PRs mentioning the EX or Panther Lake Claw for EC register info. (c) Find the exact `Win32_ComputerSystemProduct.Name` string and board ID (MS-1Txx) other projects use to detect this model. Report findings as a table with source links."
2. 🤖 `subagent (Explore, this repo)`: "In C:\Users\kyle\Documents\git\ClawTweaks, produce a complete inventory of every code location that branches on MSI Claw device identity or hardware constants: (a) all uses of `DeviceType.MSIClaw` / `SharedDeviceType.MSIClaw`, (b) all uses of `MSIClawConfig`, (c) all hardcoded `0x0DB0`, `0x1901`, `0x1902`, `0xFFA0`, `0xFFF0`, (d) all `MsiClawWmi.Get/Set` call sites with their methodName + data-block index arguments, (e) all firmware-version branches (search `fw`, `firmware`, `0x166`, `0x163`). For each: file, line, and one sentence on what it does. Be very thorough — this list is the checklist for the port."
3. Merge both reports into the port log. Where HandheldCompanion already supports the EX, note file/class names — the repo's existing pattern is to port HC behavior 1:1 and cite it in comments (see `ClawGyroSourceAdapter.cs`, `MsiClawFanController.cs` headers). Follow that pattern.

**Acceptance criteria:**
- [ ] Port log contains the HC research summary WITH source links (or an explicit "HC has no EX support as of <date>").
- [ ] Port log contains the full in-repo touchpoint inventory (this becomes the Phase 4 checklist).
- [ ] A decision note: for each subsystem (detection, HID, gyro, fan, TDP, battery, LED) — "expect identical to A2VM", "known-different (source: X)", or "unknown, must probe in Phase 3".

---

## Phase 3 — Probe the unknowns on the device (isolated, before touching main code)

**Goal:** For every subsystem marked "unknown" or "known-different" in Phase 2, get a working proof-of-concept in an isolated probe under `Diagnostics/Claw8EXProbes/` — so Phase 4 code changes are transcription of verified facts, not experiments.

Run probes IN THIS ORDER (safest first). Skip a probe only if Phase 1/2 already proved the subsystem identical.

### P1. Gyro (harmless, read-only)
Extend the Phase 1c GyroProbe: apply the ClawA1M remap from `ClawGyroSourceAdapter.cs` in the probe and verify physically: rolling the device left/right, pitching forward/back, yawing flat on a table each move the EXPECTED output axis with the EXPECTED sign. If not, derive the correct EX remap (swap/sign table) by experiment and record it. **Test:** a written table in the port log mapping each physical motion → output axis, verified twice.

### P2. HID command interface (read-only reads, then reversible mode switch)
1. Using the Phase 1b inventory, open the command interface and send the READ-style commands the code already uses: firmware version read (see how `MsiClawLedController.cs` / `ClawButtonMonitor` read firmware version — replicate) and M1/M2 GetM12 reads (`ClawButtonMonitor.cs:56-60`). Record firmware version bytes and which M1/M2 parameter set answers correctly.
2. ⛔ STOP, then (with human present): send SwitchMode→DInput (`0x24`, mode 2), wait 3 s, re-enumerate, confirm the DInput PID appears; then SwitchMode→XInput to restore. **Recovery if stuck:** reboot restores XInput mode; MSI Center M can also reset controller mode.
**Test:** probe log shows (a) firmware version read OK, (b) mode switch to DInput observed via new PID, (c) restored to XInput.

### P3. MSI_ACPI reads (fan table, fan control bit, charge limit — READ ONLY)
Write probe `WmiRead` that uses the same 32-byte package protocol as `MsiClawWmi.cs` to READ every data block the app uses (the call-site list from Phase 2 step 2d tells you which). Print raw bytes. Sanity checks: the fan table read should return 8 plausible bytes (0–150, non-decreasing-ish); charge limit should match whatever is set in MSI Center M. **Test:** every block the app reads on A2VM returns `success flag = 1` and plausible payloads on the EX; each recorded in the port log. If a block fails or looks wrong → mark that subsystem BLOCKED, do not write to it in Phase 4, and report at the phase gate.

### P4. TDP read/write (small, reversible)
1. Read current PL1/PL2 (via the code's existing read path — `IntelThermalControl.cs` / kx.exe read, whichever `PerformanceManager` uses).
2. ⛔ STOP, then: write PL1 down by 2 W, read back, confirm, restore original. Watch HWiNFO or the read-back value — do NOT raise limits above the firmware defaults recorded in step 1 during this probe.
**Test:** read-back equals written value, then restored value equals original. If kx.exe fails on Panther Lake (MCHBAR change), record the error — Phase 4 will need an alternative (research via the Phase 2 subagent's findings) — and mark TDP-write BLOCKED rather than experimenting.

### P5. Fan table write (MOST DANGEROUS — EC latch risk)
Read `MsiClawFanController.cs:28-63` comments about the EC thermal-protection latch BEFORE this probe.
⛔ STOP gate with this exact summary to the human: "About to write a fan table to the EC. I will write back the EXACT 8 bytes read in P3 (a no-op write), verify read-back, then hand control back to hardware mode. Recovery: `Tools/Fan-Panic-*.ps1`, reboot, or full power-off restores firmware fan control. Device must be idle and cool. OK?"
1. No-op write: write the exact bytes read in P3, read back, verify byte-identical.
2. Hand back: replicate `ApplyHardwareTable` behavior (clear full-speed bit, write firmware table, hardware mode).
3. Only if 1–2 pass: write a mildly different table (one byte +10 on the 60 °C point), verify read-back, listen for fan response, restore.
**Test:** all three read-backs byte-identical to what was written; fan behaves; firmware control restored (fan responds normally to load afterwards).

### P6. Charge limit write (low risk, reversible)
Read current limit (P3), ⛔ STOP, write limit 80 %, read back, verify Windows shows charging stops at ~80 % (or the EC reports the value), restore original. **Test:** read-back matches both times.

### P7. LED (optional, cosmetic)
Only if Phase 2 found the EX LED protocol. Send one color command, verify visually, restore. Failure here does NOT block the port — record and move on.

**Acceptance criteria:**
- [ ] Every subsystem is now classified in the port log as VERIFIED-IDENTICAL, VERIFIED-DIFFERENT (with the measured EX values), or BLOCKED (with the exact failure).
- [ ] No BLOCKED subsystem will get write-code in Phase 4 — list them in the phase-gate report to the human.
- [ ] All probes committed under `Diagnostics/Claw8EXProbes/` with a README saying what each does and what it found.

---

## Phase 4 — Implement support in the main codebase (idiomatic changes)

**Goal:** Wire the verified facts into the app the same way existing devices are wired in. One commit per numbered item below, each building cleanly.

**Design decision (follow this unless Phase 1–3 findings contradict it, in which case STOP and ask):**
Keep `DeviceType.MSIClaw` as the shared type (so every existing `switch (deviceType)` keeps working — see the Phase 2 inventory), and introduce a **variant** distinction inside the MSI Claw family, mirroring how the code already distinguishes A1M vs A2VM by firmware/model checks:

1. **Detection.**
   - Add `MSIClaw8EXConfig : DeviceConfig` in `XboxGamingBarHelper/Devices/MSIClaw/`, modeled line-for-line on `MSIClawConfig.cs` (same XML-doc style documenting confirmed vs assumed WMI names). `Matches()` = Vendor contains "Micro-Star" + Name contains the EXACT substring measured in Phase 1a. `DeviceType => DeviceType.MSIClaw`. Feature flags set from Phase 3 results (e.g. `SupportsGyro` only if P1 passed).
   - Register it in `DeviceRegistry.cs` BEFORE `MSIClawConfig` (more specific first — same rule the registry already documents at `:26-37`).
   - Add a `ClawVariant` enum (e.g. `A2VM`, `EX`) exposed on both MSI configs, and plumb it to wherever variant-specific constants are chosen (fan tables, TDP unlock, M1/M2 bytes). Grep the Phase 2 inventory for every `DeviceRegistry.GetByType(DeviceType.MSIClaw)` call — verify each still resolves correctly with two configs sharing the type; if any is ambiguous, route it through the detected config instance instead of GetByType.
   - Update `Shared/Enums/DeviceType.cs` doc comment to mention the EX.
   - **Test:** run helper on the EX → log shows `Device matched: MSI Claw 8 AI+ EX`. Unit-style check: temporarily feed a fake `DeviceInfo` with the A2VM name and confirm it still matches `MSIClawConfig` (not the EX config), and vice versa.

2. **Gyro.** If P1 showed the ClawA1M remap is correct → nothing to change beyond detection (gyro flows through `ClawButtonMonitor._gyroAdapter` once the device matches). If the remap differs → parameterize `ClawGyroSourceAdapter` with the remap table per `ClawVariant` (keep the existing header-comment style documenting the mapping and its source). **Test:** in-game or desktop gyro-to-mouse: roll/pitch/yaw each move the pointer/stick the correct direction; note results in port log.

3. **Controller HID.** If PIDs/usage pages/commands identical (P2) → no change. If different → add the EX values beside the A2VM constants in `MSIClawHidController.cs` / `ClawButtonMonitor.cs`, selected by `ClawVariant`, following the existing firmware-version-branch pattern (`ClawButtonMonitor.cs:56-60`). Add the EX firmware's M1/M2 parameter bytes measured in P2. **Test:** on the EX — controller works in a game via ViGEm; M1/M2 remap works; mode switch + HidHide cloak works (no double input in Steam); OEM button single/double click actions fire.

4. **Fan control.** Gate on P5 results. Start with the EX firmware tables read in P3 as the `LLFanTable_*` equivalents (new `EXFanTable_*` arrays); re-derive the software curves only if Panther Lake thermals demand it — keep A2VM arrays untouched. Respect the latch-avoidance constraint documented in `MsiClawFanController.cs:28-52` (80–100 °C points ≥ firmware values). Select tables by `ClawVariant`. **Test:** apply Quiet curve → "Check applied values" in the widget verifies EC bytes match; hand back to hardware mode → firmware control resumes; run a 10-min game load → fan ramps, no EC full-speed latch.

5. **TDP.** Per P4: if kx.exe works, add Panther-Lake-appropriate unlock ceilings in `PerformanceManager.cs` (~1522) keyed by variant — get the correct PL1/PL2 ceilings from Phase 2 research (HC or MSI spec: use the EX's official sustained/boost TDP, do NOT invent numbers). If kx.exe is BLOCKED, leave A2VM path untouched and surface "TDP control unavailable on this model" in the UI the same way other unsupported features degrade. **Test:** set TDP slider to a value, verify package power follows under load (OSD or HWiNFO); reboot → unlock re-applies (check log line).

6. **Battery charge limit.** Per P6; likely no change beyond detection. **Test:** set 80 % in widget, confirm charging stops ~80 %, survives helper restart (re-apply log line present).

7. **LED.** Per P7; add EX command variant to `MsiClawLedController.cs`'s existing per-generation table (`:41`) or record as unsupported.

8. **Peripheral updates.** `Services/MsiClawDriverCheckService.cs`: add the EX support URL (keyed by variant). README.md: move the EX row to "Supported". Any installer/first-run device gate found in Phase 2 inventory: add the EX detection string there too.

**Acceptance criteria:**
- [ ] Every item in the Phase 2 in-repo touchpoint inventory has been visited and either changed or explicitly marked "no change needed" in the port log.
- [ ] Solution builds with 0 errors after every commit.
- [ ] Every changed file that encodes hardware facts cites its source in comments (probe result date or HC file), matching the repo's existing comment style.
- [ ] **A2VM regression review:** `git diff <base>` shows every behavioral change is gated behind the EX variant/config; no A2VM constant was edited in place. If any shared code path changed, list it in the port log with justification.

---

## Phase 5 — On-device validation

**Goal:** Prove feature parity on the EX with a written pass/fail record.

Run through this checklist ON THE DEVICE, recording pass/fail + notes for each row in `docs/hardware/CLAW8_EX_VALIDATION.md`:

| # | Feature | Test | Pass criteria |
|---|---|---|---|
| 1 | Detection | Start helper, read log | "Device matched" with EX config; no Generic fallback |
| 2 | Widget | Win+G → Gaming widget | All Claw sections visible (fan, charge limit, controller) |
| 3 | Controller emu | Launch a Steam game | Virtual X360 pad works; no double input |
| 4 | M1/M2 remap | Map M1→A in a per-game profile | M1 produces A in-game; reverts outside the game |
| 5 | OEM button | Single + double click actions | Both fire configured actions |
| 6 | Gyro→stick | Enable per-game gyro, hold-activation | Aim follows motion, correct axes/signs, no drift at rest |
| 7 | Gyro→mouse | Desktop mouse mode | Cursor follows motion correctly |
| 8 | TDP | Slider 15 W under load | Package power ≈15 W in OSD; M1+DPad up/down steps by 1 W |
| 9 | FPS limit | IGCL tier + RTSS mode | Cap engages in a game for both paths |
| 10 | Fan | Custom curve + "Check applied values" | EC bytes match; hand-back restores firmware control |
| 11 | Fan stress | 15 min sustained load | No EC full-speed latch; temps stable |
| 12 | Charge limit | 80 % cap while plugged in | Charging stops ~80 %; persists across reboot |
| 13 | Profiles | Per-game profile with TDP+gyro+remap | Auto-applies on game launch, reverts on exit |
| 14 | AC/DC | Different AC vs DC values | Switch on unplug applies DC values |
| 15 | Suspend/resume | Sleep 2 min, resume, repeat #3/#6/#8 | All still work without helper restart |
| 16 | Center M coexistence | Start MSI Center M | Emulation suspends (per README behavior), resumes after |

Any FAIL → fix in Phase 4 style (variant-gated), re-run that row plus rows 1–3, and log the fix. Do not mark Phase 5 complete with open FAILs unless the human explicitly accepts them as known limitations.

**Acceptance criteria:**
- [ ] Validation doc committed with all 16 rows marked and dated.
- [ ] Zero unexplained FAILs.

---

## Phase 6 — Documentation and hand-off

**Goal:** The port is documented, releasable, and safe for A2VM users.

1. README.md: update the Supported Devices table (EX → ✅ Supported, with the measured board ID), and the Known Limitations section.
2. Update `docs/hardware/CLAW8_EX_HARDWARE.md` with any values corrected during Phases 3–5 so it is the canonical EX reference.
3. Write release notes from `docs/RELEASE_NOTES_TEMPLATE.md`: new device support, any features degraded/unsupported on EX (BLOCKED list), explicit note that A2VM behavior is unchanged.
4. Final review: 🤖 `subagent (Explore)`: "Diff branch feature/claw8-ex-support against its merge base. List any change that could alter behavior on an A2VM device (i.e., not gated behind the EX variant/config or new files). Also list any hardcoded value lacking a source comment." Fix anything it finds.
5. ⛔ STOP: present the human with a summary (features working, features blocked, A2VM-risk assessment) and wait for approval before opening a PR to the default branch. PR body follows repo conventions; do not merge yourself.

**Acceptance criteria:**
- [ ] README + release notes committed.
- [ ] Final-review subagent found no ungated A2VM-affecting changes (or all were fixed).
- [ ] PR opened after human approval, linking to the hardware manifest, port log, and validation doc.

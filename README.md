# ClawTweaks

> **⚠️ Beta Software** — ClawTweaks is actively developed and not yet feature-complete. Tested primarily on **MSI Claw 7 AI** and **MSI Claw 8 AI** (A1M / A2VM). Other MSI Claw models may work but are untested and could have issues. Use at your own risk.

A Game Bar widget for MSI Claw handheld gaming PCs — ported and significantly extended from [GoTweaks](https://github.com/corando98/GoTweaks), originally built for Lenovo Legion Go.

ClawTweaks adapts GoTweaks to the Intel Lunar Lake architecture of the MSI Claw, replaces AMD-specific features with Intel equivalents, and adds MSI Claw-specific functionality that was not present in the original.

---

## What's different from GoTweaks

GoTweaks was designed around the Lenovo Legion Go (AMD + dedicated controller hardware). ClawTweaks ports that foundation to MSI Claw:

- **Intel TDP control** — replaces RyzenAdj with Intel-compatible power limit management
- **Intel IGCL FPS limiter** — alternative to RTSS using Intel's GPU driver API
- **MSI Claw controller emulation** — DInput path via ViGEm (no Legion controller hardware)
- **Gyroscope support** — ported from Handheld Companion, tuned for Claw sensor layout
- **MSI Center gating** — detects MSI Center M and disables conflicting features automatically
- **Claw-specific Quick Settings tiles** — Mode tile (Controller ↔ Mouse), MSI Center toggle
- Legion Go-specific tabs and features are hidden automatically when not on a Legion device

---

## Features

### Quick Settings
Customizable dashboard with quick-access tiles for your most-used settings.

- One-tap toggles for TDP, FPS Limit, Overlay, Profile, and more
- **Controller ↔ Mouse mode tile** — switch input mode without opening the full settings
- Custom keyboard shortcut tiles and predefined action tiles (Brightness, Volume, Desktop, etc.)
- Tile hotkeys — bind controller button combos to trigger any tile from in-game
- Device-specific tiles appear only when the relevant hardware or software is detected

---

### Performance Control

**TDP Management:**
- Adjust power limits with real-time monitoring
- Sticky TDP — restores TDP if changed by another app
- TDP Boost (PL2 / Overboost) with separate slider

**AutoTDP (Beta):**
- Automatically adjusts TDP to hold a target FPS
- Real-time status shown in OSD overlay

**FPS Limiter:**
- **Intel IGCL** — driver-level limiter built into the Intel GPU. Unlike RTSS it does not render and then discard a frame — the limit is applied before rendering, so it costs no extra GPU work and does not consume an FPS from your headroom. Tiers: Performance (60), Balanced (40), Efficiency (30).
- **RTSS** — RivaTuner Statistics Server for finer-grained limits (30 / 40 / 60 / 90 / 120 FPS). Requires RTSS to be installed. Adds ~1 FPS of overhead compared to Intel IGCL.
- Quick toggle between Intel and RTSS mode from the Quick Settings tile or the Performance tab

**CPU Controls:**
- CPU Boost enable/disable
- CPU EPP (Energy Performance Preference, 0–100)
- OS Power Mode (Efficiency / Balanced / Performance)

---

### Controller (MSI Claw)

The MSI Claw does not have dedicated controller hardware like the Lenovo Legion Go. ClawTweaks implements software controller emulation: it hides the physical controller via HidHide and presents a clean virtual Xbox 360 controller to games and Steam. All features run through this virtual device.

**Controller emulation:**
- Virtual Xbox 360 controller via ViGEm Bus (DInput path, PID 0x1902)
- HidHide suppression hides only the gamepad interface — keyboard and Win+G remain fully functional
- MSI Center M detection — emulation is automatically suspended when MSI Center M is active to avoid conflicts
- Quick toggle via the **Controller ↔ Mouse** tile in Quick Settings

**Controller mode** (default) — full gamepad emulation:
- All buttons, sticks, triggers, and D-pad forwarded to the virtual controller
- M1 / M2 buttons remappable to any gamepad action
- Per-game button profiles — different mappings per game, auto-applied on launch

**Mouse mode** — desktop-friendly input without a mouse:
- Right stick → mouse cursor
- Left stick → scroll wheel
- LB → right click, RB → left click

**Gyroscope:**
- Ported from Handheld Companion, tuned for the Claw's sensor orientation
- Gyro-to-right-stick (for games with native gyro aim support via Steam or in-game settings)
- Gyro-to-mouse (direct mouse movement)
- Sensitivity X/Y, deadzone, invert axes
- Activation modes: Always On, Hold button, Toggle button
- Activation button configurable (LT, RT, LB, RB, or any face button)

> Controller emulation requires [ViGEmBus](https://github.com/nefarius/ViGEmBus) and [HidHide](https://github.com/nefarius/HidHide) to be installed.

---

### Per-Game Profiles
Automatically apply your preferred settings the moment a game launches — no manual switching needed.

- Automatic profile switching on game detection via Xbox Game Bar's focus API
- Each profile saves independently: TDP, TDP Boost, FPS Limit, CPU Boost, EPP, OS Power Mode, controller mappings, gyro settings, and more
- **Default game profile** — applies a single preset to any unknown game (useful for "always cap FPS + set TDP to X for every game I haven't configured yet")
- Profile card shown in the widget header while a game is active, with the active profile name
- Per-game controller button remapping — M1/M2 behave differently in each game without touching global settings

---

### Performance Overlay (OSD)
Real-time on-screen display powered by RivaTuner Statistics Server.

- FPS and frametime graph
- CPU / GPU usage and temperatures
- Power consumption, memory, VRAM
- Battery level and charge status
- Fan speed (supported devices)
- TDP limits and AutoTDP status

---

### AMD Radeon Features
For devices with AMD GPUs (not MSI Claw, but supported when an AMD GPU is present).

- Radeon Super Resolution (RSR), AFMF frame generation
- Radeon Anti-Lag, Radeon Boost, Radeon Chill
- Image sharpening, display color controls

---

### Lossless Scaling Integration
- Launch and manage Lossless Scaling from the widget
- Configure scaling type, factor, and frame generation mode (LSFG2 / LSFG3)
- Per-profile configurations

---

### Graphics & Display
- Resolution and refresh rate control
- HDR toggle
- Display rotation

---

## Installation

### Before you install — bring the Claw to a clean state

ClawTweaks takes exclusive ownership of the MSI Claw controller. It hides the physical hardware controller via HidHide and replaces it with a virtual Xbox 360 controller. This is required to enable gyroscope, per-game button remapping, and all other controller features — everything runs through that virtual device.

**If you use Handheld Companion, Winhanced, or any other tool that also manages the controller or HidHide, you must fully exit and ideally uninstall that tool before using ClawTweaks.** Running two tools that both try to own the controller will cause conflicts — double input, lost gyro, or the controller not being detected at all.

Before installing, make sure the Claw is in its default state:

1. **Exit and disable** Handheld Companion, Winhanced, or any similar app (check the system tray)
2. **Open MSI Center M** (the OEM software) and verify the controller is recognized normally there — this confirms the hardware is back to factory state
3. If HidHide was previously configured by another tool, open **HidHide Configuration Client** and clear all blocked devices
4. Optionally reboot to make sure no leftover processes or driver state remain

Once the Claw is back to its stock state ClawTweaks will handle everything from there — it enables and configures HidHide automatically when you turn on controller emulation in the widget.

---

### Option A: Install Script (Recommended)

The install script must be run as Administrator. Right-clicking and choosing "Run with PowerShell" will request elevation automatically.

1. Download the latest release from [Releases](../../releases)
2. Extract the package
3. Right-click `Install.ps1` → **Run with PowerShell**
4. Click **Yes** when prompted for Administrator access

The script handles closing blocking processes, installing certificates, dependencies, and the widget.

**Silent install:** `powershell.exe -ExecutionPolicy Bypass -File .\Install.ps1 -Force`

**Troubleshooting:** If the script doesn't run, open PowerShell as Administrator and run:
```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
.\Install.ps1
```

### Option B: Manual Install

1. Install the `.cer` certificate → Local Machine → Trusted People
2. Install dependencies from `Dependencies\x64` folder
3. Double-click the `.msixbundle` to install

### Enable the Widget

1. Open Xbox Game Bar (`Win + G`)
2. Click the **Widgets** menu
3. Find and enable **"Gaming"**

### Enable Game Detection

Required for per-game profiles and AutoTDP:

1. Open Xbox Game Bar → **Settings** → **More Settings**
2. Find **Gaming** widget
3. Enable **"Know which game or app is in focus"**

---

## Requirements

- Windows 10/11
- Xbox Game Bar
- **For controller emulation:** [ViGEmBus](https://github.com/nefarius/ViGEmBus) + [HidHide](https://github.com/nefarius/HidHide)
- **Optional:**
  - [RivaTuner Statistics Server](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) — required for OSD overlay and RTSS FPS limiter
  - [PawnIO](https://github.com/SuporteTI/PawnIO) — required for extended sensors (fan speed, GPU power draw on some devices)
  - AMD GPU — for Radeon features
  - Lossless Scaling — for scaling integration

### Smart App Control

Windows Smart App Control may prevent the app from running correctly. If you experience issues, disable Smart App Control in Windows Security settings.

---

## Known Limitations

- Tested only on **MSI Claw 7 AI (A1M)** and **MSI Claw 8 AI (A2VM)**. Other Claw models are untested.
- VIIPER controller backend is not yet available (Coming Soon).
- This is beta software — expect rough edges and report issues.

---

## Technology

100% free and open source. Built with C#.

**Libraries used:**
- **LibreHardwareMonitor** — hardware sensors
- **RyzenAdj** — AMD TDP control (Legion Go path)
- **RTSSSharedMemoryNET** — OSD overlay with frametime graph support
- **ADLX** — AMD Display Library for Radeon features
- **ViGEmBus / HidHide** — virtual controller and HID suppression

---

## Credits

Based on [GoTweaks](https://github.com/corando98/GoTweaks) by [namquang93](https://github.com/namquang93) / [corando98](https://github.com/corando98).

Controller emulation and gyro implementation adapted from [Handheld Companion](https://github.com/Valkirie/HandheldCompanion).

**Special Thanks:**
- **Mute** (Legion Go Discord) — testing and feedback on the original GoTweaks
- **[GameTechPlanet](https://www.youtube.com/@GameTechPlanet)** — covering GoTweaks
- **The Community** — bug reports and feedback

## License

This project is open source. See LICENSE file for details.

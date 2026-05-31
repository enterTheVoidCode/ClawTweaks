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
- RTSS-based limiter (requires RivaTuner Statistics Server)
- Intel IGCL Endurance Gaming tiers (Performance 60, Balanced 40, Efficiency 30)
- Quick toggle between RTSS and Intel mode

**CPU Controls:**
- CPU Boost enable/disable
- CPU EPP (Energy Performance Preference, 0–100)
- OS Power Mode (Efficiency / Balanced / Performance)

---

### Controller (MSI Claw)

ClawTweaks provides software controller emulation for MSI Claw via ViGEm, since the Claw does not have dedicated controller hardware like the Legion Go.

- **Controller emulation** — virtual Xbox 360 controller over DInput (HidHide-based suppression)
- **Mouse mode** — right stick → cursor, left stick → scroll, LB/RB → mouse buttons
- **Gyroscope** — gyro-to-stick or gyro-to-mouse, with sensitivity, deadzone, invert, and activation button
- **Button remapping** — M1 / M2 mapped to gamepad actions
- **Per-game controller profiles**

> Controller emulation requires ViGEmBus and HidHide to be installed.

---

### Per-Game Profiles
Automatically apply settings when a game launches.

- Automatic profile switching on game detection
- Saves TDP, TDP Boost, FPS Limit, CPU settings, controller settings per game
- Default game profile for unknown titles
- Profile card shown in the widget while a game is active

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

### Option A: Install Script (Recommended)

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

# ClawTweaks

> ### ✅ Supported Devices
> | Device | Status |
> |--------|--------|
> | **MSI Claw 8 AI+ A2VM** (Lunar Lake, MS-1T52) | ✅ Supported |
> | **MSI Claw 7 AI+ A2VM / A2VMX** (Lunar Lake, MS-1T42) | ✅ Supported |
> | MSI Claw A1M (Meteor Lake) | ❌ Not supported — different processor, EC and HW controller |
> | MSI Claw 8 EX (Panther Lake) | 🔮 Possible future support — shares Intel architecture |
> | Other MSI Claw models | ❌ Not tested / not supported |
>
> **The installer will abort with an error message if run on an unsupported device.**

> **⚠️ Beta Software** — ClawTweaks is actively developed. Use at your own risk.

A Game Bar widget for MSI Claw handheld gaming PCs. Built on the foundation of [GoTweaks](https://github.com/corando98/GoTweaks) (a Lenovo Legion Go widget), ClawTweaks replaces AMD-specific features with Intel equivalents, adds MSI Claw-specific hardware support, and introduces a range of new features alongside a streamlined UI.

---

## Features

### Quick Settings
Customizable dashboard with quick-access tiles for your most-used settings.

- Gamebar widget settings for TDP, FPS Limit, Overlay, Profile, and more
- **Controller ↔ Mouse mode tile** — switch input mode without opening the full settings
- Custom keyboard shortcut tiles and predefined action tiles (Brightness, Volume, Desktop, etc.)
- Device-specific tiles appear only when the relevant hardware or software is detected

**In-game controller shortcuts:**
Every tile can be assigned a controller button combo that triggers it directly while in a game — without opening the Game Bar. Examples:
- `M1 + D-Pad Up/Down` — raise or lower TDP by 1W on the fly
- `M1 + D-Pad Left/Right` — cycle FPS limit up or down
- `Select + A/B` — toggle brightness up/down
- Any combination of 2+ buttons can be assigned to any tile

This means you can adjust TDP, brightness, FPS cap, overlay level, or trigger any custom action mid-game using only the controller — no interruption to gameplay.

---

### Performance Control

**TDP Management:**
- Adjust power limits with real-time monitoring
- TDP Boost (PL2 / Overboost) with separate slider

**FPS Limiter:**
- **Intel IGCL** — driver-level limiter built into the Intel GPU. Unlike RTSS it does not render and then discard a frame — the limit is applied before rendering, so it costs no extra GPU work and does not consume an FPS from your headroom. Tiers: Performance (60), Balanced (40), Efficiency (30).
- **RTSS** — RivaTuner Statistics Server for finer-grained limits (30 / 40 / 60 / 90 / 120 FPS). Requires RTSS to be installed. Adds ~1 FPS of overhead compared to Intel IGCL.
- Quick toggle between Intel and RTSS mode from the Quick Settings tile or the Performance tab

**CPU Controls:**
- CPU Boost enable/disable
- OS Power Mode (Efficiency / Balanced / Performance)

---

### Controller (MSI Claw)

ClawTweaks implements software controller emulation: it hides the physical controller via HidHide and presents a clean virtual Xbox 360 controller to games and Steam. All features run through this virtual device.

**Button remapping — per game:**
Every hardware button on the Claw is remappable independently for each game. Profiles switch automatically when a game launches.
- **M1, M2** (right side back buttons) — remap to any gamepad button, D-pad direction, or stick click
- **Left front OEM button** — remap to any gamepad action
- All remaps are per-game: M1 can be "Jump" in one game and "Dodge" in another, applied automatically

**Gyroscope — per game:**
- Gyro-to-right-stick — works with any game that supports stick-based aim (gyro aim in Steam, or natively)
- Gyro-to-mouse — direct cursor movement, useful for desktop or mouse-driven games
- Sensitivity X/Y, deadzone, and invert per axis
- Activation: Always On, Hold a button, or Toggle — configurable button (LT, RT, LB, RB, face buttons)
- Gyro settings saved per-game profile

**Controller ↔ Mouse mode:**
- **Controller mode** (default) — full virtual gamepad, all inputs forwarded
- **Mouse mode** — right stick → cursor, left stick → scroll, LB/RB → mouse buttons
- Switch instantly via the Quick Settings tile without opening the full widget

**Technical:**
- Virtual Xbox 360 controller via ViGEm (DInput path)
- HidHide hides only the gamepad interface — keyboard and Win+G stay fully functional
- MSI Center M detection — emulation suspends automatically when MSI Center M is active

> Requires [ViGEmBus](https://github.com/nefarius/ViGEmBus) and [HidHide](https://github.com/nefarius/HidHide).

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
- TDP limits

---

### Lossless Scaling Integration
- Launch and manage Lossless Scaling from the widget
- Configure scaling type, factor, and frame generation mode (LSFG2 / LSFG3)
- Per-profile configurations

---

## Installation

Download the latest release, extract the ZIP, and run `Install.ps1` with PowerShell (right-click → **Run with PowerShell**). The script handles everything automatically.

See the release page for step-by-step instructions.

### Enable the Widget

1. Open Xbox Game Bar (`Win + G`)
2. Click the **Widgets** menu
3. Find and enable **"Gaming"**

### Enable Game Detection

Required for per-game profiles:

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
  - Lossless Scaling — for scaling integration

### Smart App Control

Windows Smart App Control may prevent the app from running correctly. If you experience issues, disable Smart App Control in Windows Security settings.

---

## Known Limitations

- **Only supports MSI Claw 7/8 AI+ A2VM (Lunar Lake)**. The A1M (Meteor Lake) and other variants are not supported — installation will be blocked on unsupported hardware.
- VIIPER controller backend is not yet available (Coming Soon).
- This is beta software — expect rough edges and report issues.

---

## Technology

100% free and open source. Built with C#.

**Libraries used:**
- **LibreHardwareMonitor** — hardware sensors
- **RTSSSharedMemoryNET** — OSD overlay with frametime graph support
- **ViGEmBus / HidHide** — virtual controller and HID suppression

---

## Credits

Based on [GoTweaks](https://github.com/corando98/GoTweaks) by [namquang93](https://github.com/namquang93) / [corando98](https://github.com/corando98).

Controller emulation and gyro implementation adapted from [Handheld Companion](https://github.com/Valkirie/HandheldCompanion).

## License

ClawTweaks is licensed under the **GNU Affero General Public License v3 (AGPLv3)** —
see [`LICENSE`](LICENSE). In short: you are free to use, study, modify and share it,
but any distributed or network-hosted derivative must also publish its complete
source under AGPLv3. It cannot be turned into a closed-source product.

Original portions derived from [GoTweaks](https://github.com/corando98/GoTweaks) /
the Microsoft Game Bar widget sample remain under the MIT License
([`LICENSE.MIT`](LICENSE.MIT)). See [`LICENSING.md`](LICENSING.md) for the full
explanation, the trademark note, and contribution terms.

> **No prebuilt binaries.** This repo does not ship ready-to-run signed packages
> or the full packaging pipeline — building and signing is left to the user.

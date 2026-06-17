# ClawTweaks 0.1.6 (Preview)

> [!WARNING]
> **Experimental preview build.** Pre-release for testing the items below — expect rough edges. Feedback welcome, especially on the Viiper controller backend and game detection.

---

## What's new

### Overlay
- **New default font: Bahnschrift.** The RTSS overlay now ships with **Bahnschrift** (replacing Cascadia Mono) as the default — cleaner, included with Windows 10/11, and it **live-reloads** when you change it (a brief overlay blink, only on an actual font change). Applies to all presets (see the Full and Horizontal preset screenshots).
- Part of the larger overlay rework: a dedicated **TDP block** (live watts + PL1/PL2) and **per-core P/E clock** readouts in the Full preset.

### Lighting
- **LED on/off Quick Settings tile (MSI Claw).** Toggle the controller LED **off and back on** straight from Quick Settings — off dims the LED to zero brightness, on restores your last saved colour. No need to open the System tab.

### Controller
- **Viiper (USB/IP) controller backend for the MSI Claw — experimental.** An alternative virtual-controller path with **live device-type hot-swap** (e.g. Xbox 360 / DS4), plus boot-time optimisation so it comes up faster.
- **Auto-switch to Xbox while the Game Bar is open — experimental, opt-in.** When a non-Xbox virtual pad is mounted, ClawTweaks can temporarily swap it to **Xbox 360** while the Game Bar is open (and back on close) to avoid Game Bar input quirks. Off by default.
- **Viiper boot rumble fix** — vibration now arms reliably after a reboot without needing an emulation re-toggle.

### Game detection
- **Game-Bar-based detection.** Game detection now relies on the **Xbox Game Bar** (plus RTSS), instead of window-name heuristics. More reliable, far less log churn. **RetroArch** keeps a per-core exception so per-core profiles still work.

### Fixes & polish
- **Reliable Left-MSI focus jump** on the **first** Game Bar open after a reboot (previously missed the first open).
- **CPU section redesign / UI cleanup**, plus a fix for **CPU frequency-cap persistence** and dropdown sync.
- **Quieter logs** — rumble, AutoTDP and controller-forwarding stats moved to Debug; log export widened for diagnostics.

---

## Installation
See the standard ClawTweaks install steps (certificate once for new devices, then the `.msix`; existing users just take the `.msix`). Full instructions are in the release description template.

> [!NOTE]
> **Assets:** before publishing, attach **both** the bare `XboxGamingBarPackage_…_x64.msix` (in-app / manual update) **and** the installer **ZIP** (new-user offline install).

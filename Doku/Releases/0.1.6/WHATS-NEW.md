# ClawTweaks 0.1.6 (Preview)

> **📍 Center M needed as a base** — ClawTweaks is designed to completely disable/hide Center M (with one click). But Center M is needed — mostly for OEM button and controller virtualization support. If you are coming from 3rd-party Center M replacements, make sure to uninstall everything and re-install Center M properly.

> [!WARNING]
> **Experimental preview build.** Pre-release for testing the items below — expect rough edges. Feedback welcome, especially on the Viiper controller backend and game detection.

---

## Installation

### 🟢 Already running ClawTweaks? → just update (no certificate step)
The certificate is **already trusted** on your device, so you only need the **`XboxGamingBarPackage_…_x64.msix`** asset below — **not** the full ZIP.

Download just that `.msix`, double-click it → **Install**.

---

### 🟠 First time on this device? → full install
New users install the signing certificate **once**, then the app. No PowerShell, no typing — just a couple of double-clicks.

1. **Extract the whole ZIP** to a folder (right-click the ZIP → **Extract All…**). Keep all the files together.
2. **Trust the certificate — first install only, once per device:** double-click **`XboxGamingBarPackage_…_x64.cer`** → **Install Certificate…** → choose **Local Machine** → **Next** (approve the admin prompt) → **Place all certificates in the following store** → **Browse…** → **Trusted People** → **OK** → **Next** → **Finish**. You'll see *"The import was successful."*
4. **Install the app:** double-click **`XboxGamingBarPackage_…_x64.msix`** → the Windows **App Installer** window opens → click **Install**. *(If Windows offers to fetch required framework components, let it.)*
5. **Wait until the Game Bar opens** on its own, and approve the background **UAC** prompt if it appears.

Then finish the first-time setup:

6. Locate and position the ClawTweaks widget on the left side.
7. On first launch an **Onboarding** tab appears as the second tab — download all the tools ClawTweaks needs, one by one. Once all are installed, the Onboarding tab moves to the far right.
8. Disable MSI Center M in the Main tab to unlock all features.
9. Enable Virtual Controller & Mouse in the Controls tab.
10. By default you switch between **Mouse** and **Controller** mode with the **Left MSI front button**.

<details>
<summary><b>Alternative: one-shot installer script</b> — use this only if the double-click install above complains about missing framework packages</summary>

<br>

`Install.ps1` installs the certificate **and** every framework dependency in one go:

1. In the extracted folder, open a terminal **in that folder**: right-click an empty spot → **Open in Terminal** (or open **Windows PowerShell** from the Start menu).
2. Paste this line and press **Enter**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```
   *The `-ExecutionPolicy Bypass` part only applies to this one run and changes nothing permanently.*

</details>

> [!NOTE]
> **No controller input?** If nothing on the controller works and the left stick only moves the mouse cursor, the MSI Claw's hardware mouse/controller switch is in mouse mode. Hold the **Start** button for at least **3 seconds** to switch it back to controller mode.
>
> **Doubled inputs with controller emulation active?** In **Steam**, check whether the **Xbox controller driver with extended feature support** is installed (Steam → Settings → Controller) and **remove / disable it**. That driver fights with ClawTweaks' virtual controller and causes doubled inputs.
>
> **Reordering widgets:** remove and re-add them in the desired sequence to change their order.

---

## What's new

### Controller
- **VIIPER (USB/IP) is now the default controller backend (MSI Claw).** Fresh installs from **0.1.6 onward get VIIPER automatically** (with an automatic fallback to ViGEm if `usbip-win2` isn't present) — nothing to do. **Upgrading from an older version (e.g. 0.1.5 / 0.1.4)?** VIIPER is *not* switched on for you — enable it manually, see the FAQ entry **"Transition to VIIPER UsbIP"**. The backend offers **live device-type hot-swap** (Xbox 360 / DS4 / DualSense Edge / Switch Pro …) plus boot-time optimisation so it comes up faster.
- **Auto-switch to Xbox while the Game Bar is open — experimental, opt-in.** When a non-Xbox virtual pad is mounted, ClawTweaks can temporarily swap it to **Xbox 360** while the Game Bar is open (and back on close) to avoid Game Bar input quirks. Off by default.
- **Viiper boot rumble fix** — vibration now arms reliably after a reboot without needing an emulation re-toggle.
- Switching to a non-Xbox virtual pad now shows a quick warning (Game Bar compatibility) with **Stay on Xbox 360 / Switch / Switch + Auto-Xbox** — all controller-navigable.

<img alt="VIIPER controller backend and device hot-swap" src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/b33ad1b43a6ee55c45dc39d4c5ca45fc158f9499/Doku/Releases/0.1.6/NewViiper%20Controller.png" width="360" />

### Quick Settings
- **New Power tile.** A red **Power** tile in Quick Settings opens a controller-navigable menu with **Power Off, Reboot, Sleep, Hibernate** and **Reboot to BIOS**. Every action runs forced and immediate — no graceful "please wait" shutdown delay.

<img alt="New Power tile – power options menu" src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/b33ad1b43a6ee55c45dc39d4c5ca45fc158f9499/Doku/Releases/0.1.6/New%20Tile%20Power%20Options.png" width="360" />

### Overlay
- **New default font: Bahnschrift.** The RTSS overlay now ships with **Bahnschrift** (replacing Cascadia Mono) as the default — cleaner, included with Windows 10/11, and it **live-reloads** when you change it (a brief overlay blink, only on an actual font change). Applies to all presets.
- Part of the larger overlay rework: a dedicated **TDP block** (live watts + PL1/PL2) and **per-core P/E clock** readouts in the Full preset.

<img alt="New default overlay font – Bahnschrift" src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/19f6d00c95f9932580ebeedcd5a95191ff661b29/Doku/Releases/0.1.6/Overlay_New_Default_Font__Bahnschrift.png" width="360" />

| Full preset | Horizontal preset |
|---|---|
| <img alt="Bahnschrift – Full preset" src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/19f6d00c95f9932580ebeedcd5a95191ff661b29/Doku/Releases/0.1.6/Overlay_New_Default_Font__Bahnschrift_Full_Preset.png" width="320" /> | <img alt="Bahnschrift – Horizontal preset" src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/19f6d00c95f9932580ebeedcd5a95191ff661b29/Doku/Releases/0.1.6/Overlay_New_Default_Font__Bahnschrift_Horizontal_Preset.png" width="320" /> |

### Lighting
- **LED on/off Quick Settings tile (MSI Claw).** Toggle the controller LED **off and back on** straight from Quick Settings — off dims the LED to zero brightness, on restores your last saved colour. No need to open the System tab.

<img alt="New Quick Settings tile – toggle LED on/off" src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/19f6d00c95f9932580ebeedcd5a95191ff661b29/Doku/Releases/0.1.6/New%20Tile%20Toggle%20LED%20on%20or%20off.png" width="360" />

### Game detection
- **Game-Bar-based detection.** Game detection now relies on the **Xbox Game Bar** (plus RTSS), instead of window-name heuristics. More reliable, far less log churn. **RetroArch** keeps a per-core exception so per-core profiles still work.

### Fixes & polish
- **CPU section redesign / UI cleanup**, plus a fix for **CPU frequency-cap persistence** and dropdown sync.
- **Quieter logs** — rumble, AutoTDP and controller-forwarding stats moved to Debug; log export widened for diagnostics.

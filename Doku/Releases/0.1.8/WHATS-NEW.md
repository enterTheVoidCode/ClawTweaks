<!--
  DRAFT pre-release notes for ClawTweaks 0.1.8 (Preview).
  Screenshot raw-URLs already point at the committed SHA 50006b4 (Doku/Releases/0.1.8/).
  BEFORE PUBLISHING:
   1. Set the real 4-part build version in the title/tag (e.g. 0.1.7.xx) — see TESTRELEASE_FORMAT.md.
   2. Attach ClawTweaks_<version>_Installer.zip and publish with --prerelease.
-->

# ClawTweaks 0.1.8 (Preview)

> **📍 Center M needed as a base** — ClawTweaks is designed to completely disable/hide Center M (with one click). But Center M is needed — mostly for OEM button and controller virtualization support. If you are coming from 3rd-party Center M replacements, make sure to uninstall everything and re-install Center M properly.

> [!WARNING]
> **Experimental preview build.** Pre-release for testing the items below — expect rough edges. Feedback especially welcome on the firmware button→keyboard remaps (A2VM only) and the new grouped key/button pickers.

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

### Firmware button→keyboard remaps — work even inside games (MSI Claw 8/7 AI+, A2VM)
Button-to-keyboard remaps can now run **entirely on the controller firmware** instead of being injected as software keystrokes. Software keystrokes are ignored by many games (anti-cheat, exclusive fullscreen, raw-input) — a firmware remap is seen as a real keypress, so **it works in-game**. Turn on **Keyboard remaps via firmware** to route your remaps through the hardware; a **Show current firmware keyboard mappings** panel with a **Re-read firmware** button reads the live mapping straight off the device (e.g. `M2 → Ctrl+V`) so you can verify what's actually programmed. A2VM (MSI Claw 8/7 AI+) only.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/50006b4923f52b759a66c32eaec54a78795ad896/Doku/Releases/0.1.8/new%20fw%20based%20button%20to%20keyboard%20mappings.png" width="420">

### New grouped keyboard-key picker with icons — built for the D-Pad
Picking a key no longer means scrolling one long flat dropdown. Keys are now grouped into **Letters, Numbers, Function, Arrows, Modifiers, Navigation, Control, Media** and **Symbols**, each group showing preview key-icons. Move from group to group and the matching keys appear immediately as **icon tiles** on the right — fast and fully D-Pad-navigable. Select a key and the picker closes; reopen it to add the next key of a combo (up to 5).

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/50006b4923f52b759a66c32eaec54a78795ad896/Doku/Releases/0.1.8/new%20grouped%20keyboard%20key%20selections%20with%20icons.png" width="420">

### New grouped controller-button picker with icons
The controller-button selector got the same treatment: choose the source by category — **Left Stick, Right Stick, D-Pad, Face Buttons, Bumpers & Triggers, System** (or **Off**) — each with its own preview icons, and pick the exact input (e.g. the individual stick-direction variants) from an icon grid. Much quicker to assign on the handheld than the old text dropdown.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/50006b4923f52b759a66c32eaec54a78795ad896/Doku/Releases/0.1.8/new%20grouped%20controller%20button%20selections%20with%20icons.png" width="420">

### Fan RPM in the overlay — via firmware reverse engineering
We reverse-engineered MSI's fan tachometer, so ClawTweaks can now read the **actual fan RPM** off the firmware — something MSI Center M itself never surfaces in an OSD. Fan speed is now available as an overlay metric (**FAN 3025RPM**), shown by default in the **Horizontal Detailed** overlay (far right of the second row) and toggleable in the **Full** overlay via *Items to Display*. On by default.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/50006b4923f52b759a66c32eaec54a78795ad896/Doku/Releases/0.1.8/exposed%20fan%20rpm%20via%20fw%20reverse%20engineering.png" width="420">

### Fan curve: extended duty range
Building on the reverse-engineered fan curve, the custom curve now uses MSI's real **0–150** duty scale end-to-end (the same range MSI Center M's own Custom slider exposes) instead of clamping at 100, so the top of the curve reaches true full-speed and the ramp can be set more aggressively.

### Fixes & polish
- **Keyboard-shortcut injection fixed** for M-buttons, Quick-Settings tiles and overlay toggles — shortcuts that could silently drop now fire reliably.
- **Media sliders** (brightness / volume) refresh live again while open.
- **OptiScaler / ReShade tile** label corrected.
- **Intel sharpness** now uses the correct IGCL sequence, and the **Intel display setting** persists globally as expected.

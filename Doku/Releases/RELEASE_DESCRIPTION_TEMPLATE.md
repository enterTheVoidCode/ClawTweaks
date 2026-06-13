> **📍 Center M needed as a base** — ClawTweaks is designed to completely disable/hide Center M (with one click). But Center M is needed — mostly for OEM button and controller virtualization support. If you are coming from 3rd-party Center M replacements, make sure to uninstall everything and re-install Center M properly.

> [!WARNING]
> **Experimental test build (v0.1.5 Preview).** This is a pre-release for testing the new in-app updater and the controller features below. Expect rough edges.

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

### In-app updates (experimental)
A new **App version & updates** section at the top of the Onboarding tab lists the available builds and installs them straight from the widget — the download is verified and installed by the signed helper, with no extra prompts. You only ever see versions **newer** than the one you have (no downgrades), and test builds are clearly flagged as **EXPERIMENTAL BUILD**.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/d7c8424da53876258ce5baf055ad5d71838bdb83/Doku/Releases/0.1.5/Auto%20In-App%20Updates.png" width="420">

### Auto-jump to the ClawTweaks widget
Open the Game Bar with the **Right MSI (front) button** and — when controller emulation is active — ClawTweaks now **auto-jumps to its own widget** instead of leaving you on the first Microsoft widget. Set your widget position (Microsoft occupies the first two slots, so ClawTweaks is usually #3) and it lands there on open. The jump is **off by default** (position 1); raise it to your actual slot to enable it.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/d7c8424da53876258ce5baf055ad5d71838bdb83/Doku/Releases/0.1.5/Auto%20Jump%20To%20ClawTweaks%20Widget.png" width="420">

### Xbox (Guide) button is now mappable
You can assign the **Xbox Button** action to a front button (e.g. **Left MSI Button → Xbox Button**). It **always opens the Game Bar**, no matter what Windows is set to do with the Guide button.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/d7c8424da53876258ce5baf055ad5d71838bdb83/Doku/Releases/0.1.5/New%20Mappable%20Xbox%20Button.png" width="420">

### External Pad mode
A new Quick-Settings tile **"Ext. Pad"** hides **both** the virtual ViGEm controller **and** the Claw's built-in controller, so an external / physical controller works without doubled input.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/d7c8424da53876258ce5baf055ad5d71838bdb83/Doku/Releases/0.1.5/External%20Contr%20Mode%20Hide%20Virtual%20and%20Claw%20Controller.png" width="280">

### Controller Status card
A live **Controller Status** card shows the current mode at a glance — e.g. *Virtual controller mode: ViGEm active, physical MSI controllers hidden via HidHide* — so you can immediately tell what's active.

<img src="https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/d7c8424da53876258ce5baf055ad5d71838bdb83/Doku/Releases/0.1.5/Controller%20Status.png" width="420">

### Smoother, more responsive gyro
The gyro-to-stick mapping was rebuilt against HandheldCompanion's behaviour: the old "sticky at low speed" feeling is gone, slow movements register reliably thanks to an anti-deadzone, and a speed-adaptive filter keeps fast motion crisp without lag. You can pick a **Gyro Engine** — *Adaptive (ClawTweaks)* for extra smoothing or *Direct (HandheldCompanion)* for a raw 1:1 feel — and sensitivity now starts from a sensible, centred default.

### More reliable controller emulation & updates
Enabling and disabling the virtual controller is more robust, startup is faster, and the controller state now **self-heals** after a ClawTweaks update. A previous issue where several background helper tasks could linger after an update — blocking new fixes or features until a full reboot — has been resolved, so updates apply cleanly.

### Controller health check
ClawTweaks now checks your controller setup and flags the Steam **"Xbox controller with extended feature support"** driver — a common cause of **doubled inputs** when controller emulation is active — so you can disable it before it causes trouble.

### Fan fix: no more stuck-loud fan
In software fan mode the fan could, under sustained full load, latch at high speed and never spin back down. The fan curves now hand control back cleanly, so it ramps down as expected.

### TDP above 25 W no longer resets on startup
TDP values above 25 W are now correctly restored on launch instead of snapping back to 25 W.

### RTSS overlay survives sleep & hibernate
The RivaTuner (RTSS) on-screen overlay no longer freezes after waking from sleep or hibernation — ClawTweaks now detects the stale connection and **reconnects the overlay automatically**.

### UI polish
The Controls tab's **Virtual Controller & Mouse** card and several tab icons got a refresh, the customisation panel is easier to close (a "Close Customizations" button at the top too), and the LED-colour setting moved up next to Charge Limit.





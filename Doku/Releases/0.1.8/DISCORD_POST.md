**ClawTweaks 0.1.8 (Preview) - Experimental Test Build**

New test build for the MSI Claw 8/7 AI+ (A2VM). Rough edges expected - feedback very welcome.

**What's new**

**New default controller mode (Hardware or Virtual)**
The forced "Virtual Controller" toggle is gone. You now pick your default controller mode. You can run the hardware controller as the default and still use button and keyboard-shortcut remaps thanks to firmware remapping. Per game you can set the opposite as an exception.

**Firmware button-to-keyboard remaps (work inside games)**
Remaps can now run entirely on the controller firmware instead of software keystrokes, so they work even in games that ignore injected input (anti-cheat, exclusive fullscreen, raw input). Turn on "Keyboard remaps via firmware" and use "Re-read firmware" to verify the live mapping. A2VM only.

**New grouped key and button pickers with icons**
Picking a keyboard key or a controller input is now grouped by category (Letters, Numbers, Function, Arrows, Modifiers, and more) with preview icons - much faster to navigate with the D-Pad than the old long dropdowns.

**Fan RPM in the overlay**
We reverse-engineered MSI's firmware fan state, so ClawTweaks can now show the actual fan RPM in the overlay (previously only HWiNFO could read it on the Intel Claws). On by default in the Horizontal Detailed overlay, toggleable in the Full overlay.

**Extended fan curve range**
The custom fan curve now uses MSI's real 0-150 duty scale end to end instead of clamping at 100, so the top of the curve reaches true full speed and the ramp can be set more aggressively.

**Better diagnostics and detailed controller logging**
Export Logs now bundles a detailed controller/HID diagnostic snapshot, so controller issues are much faster to pin down.

**Fixes**
- Fixed a game crash caused by a leftover forced CPU core-affinity feature - ClawTweaks no longer changes the affinity of game processes (verified with Ori and the Will of the Wisps).
- Fixed D-Pad navigation getting stuck on "Button Remapping and Macros" in Hardware Controller mode.

**Install**
Already running ClawTweaks: download just the .msix from the release and double-click to install. First time on the device: extract the full ZIP and run Install.ps1. Note: MSI Center M must be installed as a base.

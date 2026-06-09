# Controller Feature Pipeline — Global + Per-Game

This is the canonical recipe for adding a **new controller setting** to ClawTweaks that is

- **persisted globally** (applies whenever no per-game profile is active), and
- **overridable per game** (loaded on game start, reverted to the global value on game end),

mirroring exactly how the M1/M2 paddles and the gyro settings already behave.

The worked example throughout is the **stepless Vibration Intensity** slider (0–100 %) that
scales the rumble forwarded to the physical MSI Claw. The **Stick Deadzone** sliders follow the
same shape. Use this document as a checklist when wiring the next feature.

> TL;DR of the data flow:
> **Widget UI → widget WidgetSliderProperty (IPC) → helper Legion\* property →
> `LegionControllerSetting_PropertyChanged` (save + live-apply to `ClawButtonMonitor`)**,
> with per-game persistence in `GameProfile` and game-start / game-end handled by
> `ApplyControllerProfile` / the widget re-pushing the active profile.

---

## The 9 stations

Each station lists the file and the exact anchor you edit. `Xxx` = your feature
(`VibrationIntensity`).

### 1. Widget profile field — `XboxGamingBar/GamingWidget.xaml.cs`
`class ControllerProfile`: add `public int VibrationIntensity { get; set; } = 100;` and copy it in
the `Clone()` initializer. This struct is the in-memory model for one controller profile.

### 2. Widget save/load — `XboxGamingBar/Features/Controller/GamingWidget.ControllerProfileStorage.cs`
- **Save** (`SaveControllerProfileToStorage`): `container.Values["VibrationIntensity"] = profile.VibrationIntensity;`
- **Load** (`LoadControllerProfileFromStorage`):
  `profile.VibrationIntensity = container.Values.ContainsKey("VibrationIntensity") ? (int)container.Values["VibrationIntensity"] : 100;`
- Containers are `ControllerProfile_Global` and `ControllerProfile_Game_<name>` in the widget's
  `ApplicationData.LocalSettings`.

### 3. Widget UI — `XboxGamingBar/GamingWidget.xaml`
Add the `Slider` (+ value `TextBlock`) inside a card in the Controller tab. For MSI-Claw-only
features place it in `ControllerFeedbackCard` (gated visible on the Claw — see station 3a).
Do **not** add `ValueChanged=` in XAML — handlers are wired in code (station 5a) to control
double-firing.

### 3a. Device gating — `XboxGamingBar/Features/Devices/GamingWidget.LegionGo.cs`
`SetGyroSectionVisibility(...)` sets `ControllerFeedbackCard.Visibility` based on
`isMsiClaw = (legionGoDetected?.Value != true) && (controllerEmulationAvailable?.Value == true)`.
Mirror this for any device-specific card.

### 4. Widget IPC property — `XboxGamingBar/Data/Legion/LegionVibrationIntensityProperty.cs`
A `WidgetSliderProperty` (default 100, `Function.LegionVibrationIntensity`, the slider, `this`).
Its base class subscribes to the slider's `ValueChanged` and, on change, sends the value to the
helper over the named pipe.
- Declare + instantiate + register it in `GamingWidget.xaml.cs`
  (`private readonly … legionVibrationIntensity;`, `new …(VibrationIntensitySlider, this)`, and add to
  the properties collection).
- Add `<Compile Include="Data\Legion\LegionVibrationIntensityProperty.cs" />` to
  `XboxGamingBar/XboxGamingBar.csproj` (old-style csproj — a missing include is a build error,
  which is the canary).

### 4a. Shared enum + per-game model — `Shared/`
- `Shared/Enums/Function.cs`: **append** `LegionVibrationIntensity` at the END of the enum (never
  insert in the middle — the value is used as an IPC key and reordering shifts ordinals).
- `Shared/Data/GameProfile.cs`: add a **nullable** backing field + property
  `[XmlElement("LegionVibrationIntensity")] private int? legionVibrationIntensity; public int? LegionVibrationIntensity {…Save();…}`
  and null it in the field-reset constructor block. `null` ⇒ "use global / built-in default".

### 5. Helper IPC property — `XboxGamingBarHelper/Devices/Libraries/Legion/`
- `LegionProperties.cs`: add `LegionVibrationIntensityProperty : HelperProperty<int, LegionManager>`
  with `Function.LegionVibrationIntensity`. `NotifyPropertyChanged` calls `base` (sends the pipe
  echo + raises `PropertyChanged`). Only call a `Manager?.SetXxx(...)` here if the **Legion Go**
  hardware needs it; the MSI-Claw routing is done in station 6.
- `LegionManager.cs`: declare `public readonly LegionVibrationIntensityProperty LegionVibrationIntensity;`
  and instantiate it (`new LegionVibrationIntensityProperty(100, this)`).

### 5a. Register the property — `XboxGamingBarHelper/Program.cs`
Add `legionManager.LegionVibrationIntensity` to the properties collection **and**
`legionManager.LegionVibrationIntensity.PropertyChanged += LegionControllerSetting_PropertyChanged;`.

> Widget-side `ControllerSettingChanged` subscriptions live in
> `XboxGamingBar/Features/Profile/GamingWidget.ProfileSettingsSubscription.cs` — add the slider
> there (this saves the widget profile + updates the value text). Do this in code, **not** XAML.

### 6. Helper dispatch (save + live apply) — `XboxGamingBarHelper/Startup/Program.LegionControllerHandlers.cs`
In `LegionControllerSetting_PropertyChanged` add:
```csharp
else if (sender == legionManager?.LegionVibrationIntensity)
{
    RouteProfileSave(ProfileSaveFlagsState.Vibration, "LegionVibrationIntensity",
        cur => cur.LegionVibrationIntensity = legionManager.LegionVibrationIntensity.Value,
        glo => glo.LegionVibrationIntensity = legionManager.LegionVibrationIntensity.Value);
    clawButtonMonitor?.SetVibrationIntensity(legionManager.LegionVibrationIntensity.Value);
}
```
- `RouteProfileSave(flags, name, curSetter, gloSetter)` persists to the **CurrentProfile** (which
  is the per-game profile while a game with a profile is active) or the **GlobalProfile**, based on
  the widget's Profiles-tab save flags.
- `clawButtonMonitor?.SetXxx(...)` applies the value live to the active emulation path.
- The handler early-returns while `isApplyingProfile` is true; the widget re-pushes after applying.

### 6a. Helper per-game accessor — `XboxGamingBarHelper/Profile/GameProfileProperty.cs`
Add the `int? LegionVibrationIntensity` pass-through property (get/set into the wrapped
`GameProfile`).

### 7. Game-start apply
The widget owns the lifecycle for live-applied settings: on game start it loads the game's
controller profile and calls `SendControllerSettingsToHelper(profile)`
(`GamingWidget.ControllerProfileStorage.cs`), which does
`legionVibrationIntensity?.SetValue(profile.VibrationIntensity)` → pipe → helper property →
station 6 → `ClawButtonMonitor`.
- Helper-applied settings (e.g. stick deadzone, the old discrete vibration) can additionally be
  applied from `ApplyControllerProfile` (`Program.ProfileHandlers.cs`) via
  `if (profile.LegionXxx.HasValue) legionManager.LegionXxx.SetValue(profile.LegionXxx.Value);`.
- Because `ClawButtonMonitor` may not exist yet when the profile loads, also re-apply the current
  values right before `monitor.Start()` in `Program.MSIClaw.cs`
  (`StartClawButtonMonitorBackground`, the gyro block):
  `monitor.SetVibrationIntensity(legionManager.LegionVibrationIntensity.Value);`.

### 8. Game-end restore
The widget pushes the **global** profile again (same `SendControllerSettingsToHelper` path) so the
value reverts. On the helper, `RestoreGlobalProfileSettings` (`Program.ProfileHandlers.cs`) +
`ApplyLegionControllerSettingsFromProfile` re-apply helper-applied settings from `GlobalProfile`.

### 9. Factory reset — `Program.LegionControllerHandlers.cs` `FactoryResetGlobalControllerProfile`
- Clear the persisted per-game/global override: `g.LegionVibrationIntensity = null;`
- Apply the live default so it takes effect without reboot:
  `legionManager?.LegionVibrationIntensity?.SetValue(100); clawButtonMonitor?.SetVibrationIntensity(100);`

---

## Two valid lifecycle models

| Model | Who applies on game start/end | Examples | Echo risk |
|-------|-------------------------------|----------|-----------|
| **Widget-driven** | Widget re-pushes the active profile (`SendControllerSettingsToHelper`); helper does **not** re-apply in `ApplyControllerProfile` | Gyro, **VibrationIntensity** | None — widget is the single source of truth |
| **Helper-applied** | `ApplyControllerProfile` reads `profile.LegionXxx` and calls `legionManager.LegionXxx.SetValue(...)` | Stick deadzone, triggers, old discrete vibration | `SetValue` echoes to the widget — only use for settings that tolerate it |

When in doubt, prefer **widget-driven** (no echo, matches M1/M2 and gyro). Both still route the
live value to `ClawButtonMonitor` through station 6.

---

## HID reports to the physical MSI Claw

All vendor writes to the Claw go to **one** vendor command interface. Its HID usage page depends
on the firmware mode:

| Mode | PID | UsagePage / Usage |
|------|-----|-------------------|
| XInput | 0x1901 | `0xFFA0` / `0x0001` |
| DirectInput (ClawTweaks normal operation) | 0x1902 | `0xFFF0` / `0x0040` |

`ClawButtonMonitor.FindCommandDevice` (and, after the LED fix,
`MSIClawHidController.FindClawHidDevice`) search **both** pages — searching only `0xFFA0` finds
nothing in DInput mode, which is why LED writes silently no-op'd before the fix.

Report IDs on that interface:

| Report ID | Purpose | Layout |
|-----------|---------|--------|
| `0x0F` | Mode switch / M1-M2 / **LED** | `0F 00 00 3C …` (64 bytes) |
| `0x05` | **Rumble** | `05 01 00 00 <small*intensity> <large*intensity> 00…` (small @4, large @5) |

`ClawButtonMonitor` already owns this interface as `_cmdDevice`. The rumble write
(`WriteRumble`) reuses it: it subscribes to the ViGEm controller's `RumbleReceived(large, small)`
event, scales by `_vibrationIntensity` (0.0–1.0), dedupes unchanged values, and writes under
`_rumbleLock` (the callback runs on a ViGEm thread while the poll thread also touches `_cmdDevice`).

Stick deadzone is applied in software in `ClawButtonMonitor.ProcessDirectInputState`
(`ApplyStickDeadzone`, a radial inner deadzone) on the outgoing ViGEm stick values — it does not
involve a HID write.

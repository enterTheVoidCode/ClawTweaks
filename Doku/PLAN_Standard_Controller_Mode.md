# ClawTweaks — Standard-Controller-Modus (HW vs. Virtuell) mit HW-Modus-Remapping

## Ziel
Der virtuelle Controller ist heute de-facto Default; der HW-Controller ist nur eine **Pro-Spiel-Ausnahme**
(„HW Controller Exception"). Wir drehen das um: Der User bekommt einen **Standard-Controller-Modus**
(HW **oder** Virtuell), und die Pro-Spiel-Ausnahme wird **dynamisch das Gegenteil** des gewählten Standards.

- **Fresh install / erster Start:** immer **Hardware Controller** (entspricht heutigem Toggle = AUS).
- Optional stellt der User den Standard auf **Virtueller Controller** um (heutiges Verhalten).
- Im **HW-Controller-Modus** werden die reverse-engineerten **Firmware-Remappings** unterstützt (auch M-Buttons),
  aber weniger mächtig als im virtuellen Modus. Kein Gyro, keine virtuelle Maus im HW-Modus.

---

## Controller-Backend: VIIPER-primär (ViGEm nur mitgezogen)
- Der **virtuelle** Controller läuft primär über **VIIPER (usbip / `usbip-win2`)**. **ViGEm wird nicht mehr
  aktiv supportet**; es hängt nur noch über einen **Zwischenlayer** mit und wird dadurch mitgezogen.
- Für diesen Umbau gilt: der „virtuelle Modus" = **VIIPER + usbip** betrachten. ViGEm-Pfade bleiben lauffähig
  (Fallback via Zwischenlayer), sind aber **nicht** das Zielbild — keine neuen ViGEm-spezifischen Sonderwege bauen.
- Praktisch: wo unten `StartClawButtonMonitorBackground(mountVigem:true)` steht, ist das der bestehende
  Emulations-Start, der über den Zwischenlayer VIIPER (bzw. ViGEm-Fallback) mountet — Semantik unverändert,
  aber gedanklich als **VIIPER-Mount** lesen.

---

## Verifizierte Ist-Architektur (Stand HEAD, mit Fundstellen)

### 1. Master-Toggle „Enable Virtual Controller & Mouse" (Screenshot 1)
- XAML: Karte `VirtualControllerMasterCard` (`GamingWidget.xaml:6858`, `Visibility="Collapsed"`),
  Header „Enable Virtual Controller & Mouse" (`:6889`), Subtitle `ControllerEmulationStatusText` (`:6890`),
  ToggleSwitch **`ControllerEmulationEnabledToggle`** (`:6894`, Handler `ControllerEmulationEnabledToggle_Toggled`).
- Handler: `Features/Devices/GamingWidget.GpdTabCallbacks.cs:521` (nur UI-Refresh; Wert schreibt die Property).
- Property (Widget): `Data/Controller/ControllerEmulationProperties.cs:41` → `Function.ControllerEmulationEnabled`.
- Persistenz (**Helper-seitig**, überlebt Reboot): LocalSettings-Key **`"ControllerEmulationEnabled"`**
  (`ControllerEmulation/ControllerEmulationManager.Normalize.cs:284/288/713`, Default **false**).
- MSI-Claw-Start liest zusätzlich `MsiClawControllerMode` und startet `StartClawButtonMonitorBackground(...)`
  (`Startup/Program.MSIClaw.cs:1278`).

> ⚠️ **Namens-Falle:** Es gibt bereits `MsiClawControllerMode` (`Function.MsiClawControllerMode`,
> Default true) — das ist **NICHT** unser neues Setting, sondern der **Firmware-Modus Maus vs. Controller
> innerhalb der HW** (HW-Mouse-Kachel). Orthogonal zu „HW vs. Virtuell". Nicht vermischen.

### 2. Pro-Spiel „HW Controller Exception" (Screenshot 3)
- XAML: Karte `HwControllerExceptionCard` (`GamingWidget.xaml:10546`, Collapsed), Header/Subtitle (`:10554/10555`),
  ToggleSwitch **`HwControllerExceptionToggle`** (`:10559`, Handler `HwControllerExceptionToggle_Toggled`),
  Hinweis `HwControllerExceptionHint` (`:10567`, „Close and restart the game to apply").
- Handler: `GamingWidget.xaml.cs:3543`; Sichtbarkeit `UpdateHwControllerExceptionVisibility()` (`:3518`).
- Property: `Data/HwControllerExceptionProperty.cs` → `Function.HwControllerException` (`Shared/Enums/Function.cs:566`).
- **Persistenz: KEIN Profilfeld**, sondern Helper-seitige Menge von Spielnamen unter Key
  **`"HwControllerExceptionGames"`** (`Startup/Program.MSIClaw.cs:1712`; Set `_hwExceptionGames`,
  `IsHwControllerException`/`SetHwControllerException` `:1754/1762`).
- Runtime-Anwendung: `ApplyHwControllerExceptionOnGameStart(gameKey)` (`Program.MSIClaw.cs:1796`) →
  bei aktivem Flag + laufender Emulation: `StopMSIClawButtonMonitor()` (baut virtuellen Pad ab → nativer
  XInput-Claw). Zurück: `RestoreVirtualControllerAfterHwException()` (`:1843`). Hooks in
  `Program.ProfileHandlers.cs:1139` (Start) / `:1167` (Stop).

### 3. Zentrale Gating-Logik (Herzstück des Umbaus)
- **`IsVirtualControllerActive()`** (`GpdTabCallbacks.cs:252-258`):
  ```csharp
  bool hwExceptionActive = HasValidGame(currentGameName) && HwControllerExceptionToggle?.IsOn == true;
  return controllerEmulationSupported && ControllerEmulationEnabledToggle?.IsOn == true && !hwExceptionActive;
  ```
- **`UpdateControllerEmulationControlState()`** (`GpdTabCallbacks.cs:281-466`): `enabled = IsVirtualControllerActive()`,
  danach `GateControllerSection(...)` (`:265-279`, sperrt Expander + klappt zu) für: Virtual-Controller-Body,
  **Gyro** (`GyroSettingsContent`), **Button-Remapping** (`ButtonRemappingContent`), Touchpad-Vibration,
  Vibration & Deadzone, **Mouse Settings** (`MouseSettingsContent`). D.h. **heute ist im HW-Modus ALLES gesperrt.**
- Karten-Sichtbarkeit: `UpdateControllerEmulationCardVisibility()` (`:212-229`) blendet Karten im Spiel/bei
  fehlendem Support komplett aus.

### 4. Remapping-Bereich „Button Remapping and Macros"
- XAML-Karte ab `GamingWidget.xaml:9005`; Content `ButtonRemappingContent` (`:9034`).
- Buttons: `LegionRemapButtonNames = { Y1,Y2,Y3,M1,M2,M3,Desktop,Page }` (`ControllerProfileStorage.cs:1833`).
- Pro Button ein Type-Combo mit **Gamepad / Keyboard / Mouse / Macro**; **Macro nur M1/M2**.
  Panel-Gating: `OnButtonTypeChanged` (`ControllerProfileStorage.cs:455-496`): `0=Gamepad,1=Keyboard,2=Mouse,3=Macro`.
- **Screenshot 2 (Mouse + Macro):** genau die beiden Typen, die im **HW-Modus NICHT unterstützt** werden.
- Generischer Swap („Re-Map Specific Buttons to Another Button"): `GamepadButtonRemapping.cs:160-164` —
  **läuft in Software auf dem ViGEm-Output**, nicht in Firmware.

### 5. Firmware-Remapping (bereits RE'd, A2VM)
- **Firmware-Keyboard-Remap:** `Labs/ClawButtonMonitor.FwKeyboard.cs`. Toggle `MsiClawFwKeyboardModeToggle`
  in Panel `FwKeyboardRemapPanel` (`GamingWidget.xaml:10745/10759`), „Re-read" `FwButtonMapRefresh_Click`
  (`Features/Devices/GamingWidget.LegionGo.cs:213`). Slots `FwSlots` (16 Buttons: DPad U/D/L/R, LB, RB,
  LSClick, RSClick, A, B, X, Y, M1@0x00BA, M2@0x0163, LT, RT; Start/Select ausgelassen).
  **Sichtbarkeit rein Capability-gated (A2VM), NICHT modus-gated** (`LegionGo.cs:200`).
- **Firmware-Command-Kanal:** Vendor-HID `_cmdDevice`, Opcodes `0x21` write / `0x04` read / `0x22` SyncROM /
  `0x24` SwitchMode.
  🔴 **Kritische Kopplung:** Firmware-Writes landen **nur, solange `ClawButtonMonitor` läuft**
  (`FwKeyboard.cs:308` Boot-Race-Retry). Im HW-Modus ist der Monitor heute **gestoppt** → Kanal zu.
- **Firmware Button→Button: Protokoll IST RE'd & on-device verifiziert, aber im Code NICHT verdrahtet.**
  `reverse_engineered/RE_MSI_ButtonRemap.md:25-79` dokumentiert es byte-exakt: Write-Frame
  `0F 00 00 3C 21 01 <addrHi> <addrLo> 03 04 0C <targetCode>` (Daten `04 0C <targetCode>`), komplette
  EEPROM-Adressmap `address = 0x003B + (code−1)×8` (Codes 1–12), M1@0x00BA / M2@0x0163 als `01 04 0C <code>`,
  Reset `00 0C <ownCode>`. Beispiele gecaptured: A→Y, B→A, Left→X („all mappable buttons remapped 2026-07-04").
  🟢 **Kein RE nötig** — reine Implementierungsaufgabe: `BuildSlotPayload` um den Gamepad-Target-Typ erweitern
  und im HW-Modus verdrahten. Heute läuft Button→Button dagegen als **Software** auf dem VIIPER/ViGEm-Output
  (`ClawButtonMonitor.GamepadSwap.cs`); in Firmware landet aktuell nur Keyboard (`mappingType==1`).
- **HW-Mouse-Kachel:** Tile `MsiClawHwMouse` (`QuickSettings.cs:364`), `ToggleMsiClawHwMouse()`
  (`QuickSettings.Actions.cs:2739`) → `SendSwitchMode`: `MODE_DESKTOP 0x04` (Maus) / `MODE_DINPUT 0x02`
  (Controller). Zeigt heute „Emulation off" (disabled), **wenn der Monitor nicht läuft** → gleiche Kopplung.

### 6. Gyro
- Device-Support: `SetGyroSectionVisibility` (`LegionGo.cs:236`), `GyroSection` (`xaml:9465`).
- Virtual-Gate: `GateControllerSection(..., GyroSettingsContent, ...)` (`GpdTabCallbacks.cs:290`).
- Per-Game-Gate: `UpdateGyroSectionForProfileMode` (`OSDCustomization.cs:1154`), Hint `GyroPerGameOnlyHint`.
- **Kein HW-Gyro implementiert** (Claw-Gyro im HW-Modus wird von MSI Center M anders gelesen → RE offen).

---

## Zielverhalten (UX)

### Standard-Controller-Modus (Umbau Screenshot 1)
- Karte umbenennen: **„Standard Controller Mode"**. Toggle → **Dropdown/ComboBox** mit
  `Hardware Controller` (Default) und `Virtual Controller`.
- Neuer Erklärtext: sinngemäß „Wählt den Standard-Controllermodus für alle Spiele. Pro Spiel lässt sich
  ein Controller-Profil anlegen und darin eine Ausnahme vom Standard setzen."
- Default bei Neuinstallation/Erststart: **Hardware Controller**.

### Pro-Spiel-Ausnahme (Umbau Screenshot 3) — dynamisch
- Standard = **HW** → Ausnahme-Karte heißt **„Use Virtual Controller for this Game"** (Toggle an = virtueller Pad
  für dieses Spiel).
- Standard = **Virtuell** → Ausnahme-Karte heißt **„Use Hardware Controller for this Game"** (heutiges Verhalten).
- Toggle-Semantik bleibt „weiche Ausnahme pro Spielname"; nur Label/Erklärung + Wirkrichtung drehen.

### HW-Controller-Modus — Fähigkeiten
| Feature | HW-Modus | Virtueller Modus |
|---|---|---|
| Button-Remapping Gamepad→Keyboard (Firmware) | ✅ (16 FwSlots) | ✅ |
| Button-Remapping Gamepad→Gamepad | ✅ Firmware (Protokoll RE'd, nur zu verdrahten) | ✅ (Software) |
| Button-Type **Mouse** | ❌ gesperrt (Screenshot 2) | ✅ |
| Button-Type **Macro** (M1/M2) | ❌ gesperrt (Screenshot 2) | ✅ |
| Gyro | ❌ gesperrt (RE offen, s.u.) | ✅ |
| Virtuelle Maus (Mouse-Emulationsmodus) | ❌ | ✅ |
| HW-Maus ↔ HW-Controller umschalten (Kachel) | ✅ | ✅ |

---

## Umbau im Detail

### A. Neues Setting `DefaultControllerMode`
- **Neuer Function-Enum-Wert** (`Shared/Enums/Function.cs`), z.B. `DefaultControllerMode` (int: 0=Hardware, 1=Virtual).
- **Widget-Property** `Data/Controller/DefaultControllerModeProperty.cs` (`WidgetProperty<int>`, Default 0=Hardware),
  gebunden an eine neue **ComboBox** `DefaultControllerModeComboBox` in `VirtualControllerMasterCard`.
- **Helper-Persistenz:** neuer LocalSettings-Key **`"DefaultControllerMode"`** in
  `ControllerEmulationManager.Normalize.cs` (Default 0). Beim Start entscheidet dieser Wert (statt des reinen
  `ControllerEmulationEnabled`-Bool) über HW vs. Virtuell.
- **Migration:** bestehendes `ControllerEmulationEnabled==true` → `DefaultControllerMode=Virtual` einmalig
  übernehmen (sonst würden Bestands-User in den HW-Default fallen). `ControllerEmulationEnabled` bleibt als
  abgeleiteter interner Zustand erhalten (= „virtueller Pad soll laufen"), damit Monitor/ViGEm-Pfade unverändert
  bleiben; nur die **Quelle** dieses Bools ändert sich (Standardmodus + Pro-Spiel-Ausnahme).

### B. Zentrale Gating-Logik modus-bewusst machen
- `IsVirtualControllerActive()` bleibt die Wahrheit für „läuft ein virtueller Pad?" — künftig aus
  `DefaultControllerMode` **kombiniert mit** der (jetzt richtungsabhängigen) Pro-Spiel-Ausnahme berechnet.
- **Neu:** `IsHardwareRemapActive()` = A2VM-Support **und** aktueller Effektivmodus == Hardware. 
- In `UpdateControllerEmulationControlState()`:
  - Button-Remapping-Karte **nicht mehr hart an `IsVirtualControllerActive()`** hängen, sondern an
    `IsVirtualControllerActive() || IsHardwareRemapActive()` (im HW-Modus **entsperren**).
  - Innerhalb der Remapping-Karte im HW-Modus die Type-Optionen **Mouse (Index 2)** und **Macro (Index 3)**
    ausblenden/deaktivieren (nur **Gamepad + Keyboard**). Umsetzen in `OnButtonTypeChanged` +
    beim Befüllen der Type-Combos (die `<ComboBoxItem>` in `GamingWidget.xaml` je Button, bzw. dynamisch filtern).
  - Gyro-, Mouse-Settings-, Vibration/Deadzone-Karten im HW-Modus **weiter gesperrt** lassen (Gyro-RE offen;
    virtuelle Maus/Rumble-Profile gibt es im HW-Modus nicht).
- Statustext (`UpdateControllerEmulationStatusText`, `:483`) modusabhängig anpassen
  (heute: „Enable controller emulation to use button remapping, gyro, Mouse Mode and more").

### C. Firmware-Command-Kanal vom virtuellen Monitor entkoppeln (Helper — größter Brocken)
Damit Firmware-Keyboard-Remap **und** die HW-Mouse-Kachel im HW-Modus funktionieren, muss der Vendor-HID-
Command-Kanal (`_cmdDevice`) unabhängig vom virtuellen Pad offen sein.
- Neuer schlanker Monitor-Zustand in `ClawButtonMonitor`: **„firmware-only"** — öffnet `_cmdDevice`, wendet
  Firmware-Remaps an (`FlushFirmwareKeyboardMap`), erlaubt `SendSwitchMode` (HW-Mouse), **mountet KEIN ViGEm**
  und **versteckt den physischen Controller NICHT** (kein HidHide).
  - Abgrenzung zu bestehenden Pfaden: `StartClawButtonMonitorBackground(mountVigem:false)` (Ext.Pad) **versteckt**
    heute den Pad via HidHide — das ist NICHT das Gewünschte. Neuer Parameter/Modus nötig
    (z.B. `firmwareOnly:true` → kein HidHide, kein ViGEm, nur Command-Kanal + FW-Flush).
- Startlogik `Program.MSIClaw.cs` (~`:1174/1278/1387`): bei `DefaultControllerMode==Hardware` den
  firmware-only-Monitor starten statt des virtuellen; bei `Virtual` wie bisher.
- HW-Mouse-Kachel-Zustand (`QuickSettings.TileStates.cs:1420`) so anpassen, dass sie im firmware-only-Modus
  **aktiv** ist (nicht „Emulation off").

### D. Pro-Spiel-Ausnahme dynamisch (Widget + Helper)
- Widget: Label/Erklärung/Wirkrichtung von `HwControllerExceptionCard` anhand `DefaultControllerMode` umschalten
  (`UpdateHwControllerExceptionVisibility` erweitern). Der Toggle bedeutet weiterhin „für dieses Spiel vom
  Standard abweichen".
- Helper: `ApplyHwControllerExceptionOnGameStart` verallgemeinern zu „Effektivmodus für dieses Spiel bestimmen"
  = `DefaultControllerMode` XOR `ExceptionForGame`. Ist der Effektivmodus Virtuell → Monitor mit virtuellem Pad
  (VIIPER via Zwischenlayer, ViGEm-Fallback); ist er Hardware → firmware-only-Monitor. Die vorhandene
  Spielnamen-Menge (`HwControllerExceptionGames`) bleibt der
  Speicher der Abweichung (ggf. Key generischer benennen, z.B. `ControllerModeExceptionGames`).

### E. Gyro bleibt im HW-Modus gesperrt
- Keine Änderung an der Sperre; nur sicherstellen, dass die Entsperrung aus (B) **nicht** versehentlich Gyro
  freischaltet (Gyro-Gate unabhängig von `IsHardwareRemapActive()` lassen). Siehe RE-TODO unten.

### F. Erststart / Default
- `DefaultControllerMode`-Default = **Hardware**. Onboarding-Text ergänzen: Standardmodus ist HW-Controller,
  virtueller Modus optional aktivierbar.

---

## RE-TODO (separat, „nichts angehen" bis reverse-engineered)

### RE-TODO: HW-Gyro (aus MSI Center M auslesen)
- Ziel: Gyro-Daten des Claw im **HW-Controller-Modus** (nativer XInput, kein virtueller Pad) verfügbar machen —
  so wie MSI Center M den Gyro dort ausliest. **Read-only RE zuerst**, kein Schreiben, keine Änderung am
  Gyro-Pfad, bis das Protokoll verstanden ist. Ergebnis in `reverse_engineered/RE_MSI_Gyro_HwMode.md`.

> Firmware **Button→Button** ist **KEIN RE-TODO** — Protokoll ist RE'd & on-device verifiziert
> (`RE_MSI_ButtonRemap.md:25-79`, „all mappable buttons remapped 2026-07-04"). Nur zu **verdrahten** (siehe unten).

### Implementierungs-Schritt (kein RE): Firmware Button→Button verdrahten
- `BuildSlotPayload` (`Labs/ClawButtonMonitor.FwKeyboard.cs`) um den **Gamepad-Target-Typ** erweitern:
  Face/DPad/Shoulder/Stick-Click `04 0C <targetCode>` @ `0x003B + (code−1)×8` (Codes 1–12), M1 `01 04 0C <code>`
  @0x00BA, M2 @0x0163, Reset `00 0C <ownCode>`. Alles bereits in `RE_MSI_ButtonRemap.md` dokumentiert.
- Im HW-Modus die Button-Type-Option **Gamepad** darüber in die Firmware schreiben (statt Software-`GamepadSwap`).

---

## Empfohlene Phasen
1. **Phase 1:** Standardmodus-Dropdown + Persistenz/Migration (A), dynamische Pro-Spiel-Ausnahme (D),
   firmware-only-Command-Kanal (C), Remapping im HW-Modus mit **Gamepad + Keyboard** entsperren (beide Firmware,
   beide RE'd) + Mouse/Macro sperren (B), Firmware-Button→Button verdrahten (Implementierungs-Schritt oben),
   HW-Mouse-Kachel im HW-Modus aktiv. Gyro bleibt gesperrt.
2. **Phase 2 (nach RE-TODO):** HW-Gyro.

> **Fallback:** Rutscht die Button→Button-Verdrahtung doch aus Phase 1 heraus, „Gamepad" solange **ausgrauen mit
> Hinweis** statt ausblenden — damit die (bereits RE'd) Fähigkeit sichtbar bleibt. Keyboard-Remap steht in
> jedem Fall sofort zur Verfügung.

---

## Kritische Dateien
- `XboxGamingBar/GamingWidget.xaml` — Karte 6858 (Toggle→Dropdown), Remapping-Karte 9005 (Type-Filter),
  Ausnahme-Karte 10546 (dynamisches Label), FwKeyboard-Panel 10745.
- `XboxGamingBar/Features/Devices/GamingWidget.GpdTabCallbacks.cs` — `IsVirtualControllerActive` (252),
  `UpdateControllerEmulationControlState` (281), neu `IsHardwareRemapActive`.
- `XboxGamingBar/Features/Controller/GamingWidget.ControllerProfileStorage.cs` — `OnButtonTypeChanged` (455),
  Type-Combo-Befüllung, `LegionRemapButtonNames` (1833).
- `XboxGamingBar/Data/Controller/*` — neue `DefaultControllerModeProperty`; Anpassung
  `ControllerEmulationProperties`, `HwControllerExceptionProperty`.
- `Shared/Enums/Function.cs` — neuer `DefaultControllerMode`.
- `XboxGamingBarHelper/ControllerEmulation/ControllerEmulationManager.Normalize.cs` — Persistenz + Migration.
- `XboxGamingBarHelper/Startup/Program.MSIClaw.cs` — Startlogik (1174/1278/1387), Exception-Verallgemeinerung
  (1712-1863), firmware-only-Monitor.
- `XboxGamingBarHelper/Labs/ClawButtonMonitor.cs` + `ClawButtonMonitor.FwKeyboard.cs` — firmware-only-Zustand,
  `SendSwitchMode` ohne laufenden virtuellen Pad.
- `XboxGamingBar/Features/QuickSettings/GamingWidget.QuickSettings.TileStates.cs` — HW-Mouse-Kachelzustand.

## Risiken / offene Punkte
- **HidHide-Baseline:** Sicherstellen, dass der firmware-only-Modus den physischen Controller **nicht** versteckt
  (sonst sieht das Spiel keinen Controller). Abgrenzung zum Ext.Pad-Pfad (der versteckt bewusst).
- **Boot-Race:** Firmware-Flush braucht offenen `_cmdDevice`; der firmware-only-Start muss denselben
  Retry-Mechanismus (`RescheduleFwKeyboardFlush`) nutzen.
- **Migration:** Bestands-User dürfen nach dem Update nicht ungewollt im HW-Default landen — Migration aus
  `ControllerEmulationEnabled` zwingend.
- **Naming-Kollision** `MsiClawControllerMode` (Maus/Controller-FW) vs. neuer `DefaultControllerMode` (HW/Virtuell)
  — klar getrennt halten.
- **Nicht-Claw-Geräte (Legion Go):** neuer HW-Modus ist A2VM-spezifisch; auf anderen Geräten Default-Verhalten
  unverändert lassen (Gating an `controllerEmulationSupported` + Claw-Check).

## Verifikation
- Build: `Build-Package.ps1 -Mode Test`.
- On-Device (Claw 8 AI+):
  - Fresh install → Standardmodus = Hardware; nativer XInput-Controller funktioniert; Firmware-Keyboard-Remap
    (z.B. M2→Ctrl+V) wirkt **in einem Spiel**; HW-Mouse-Kachel schaltet um.
  - Umschalten auf „Virtual Controller" → heutiges Verhalten (VIIPER/usbip primär, ViGEm-Fallback via
    Zwischenlayer; Gyro, Mouse-Modus verfügbar).
  - Pro-Spiel-Ausnahme dreht Label/Wirkung je nach Standard; Neustart-Hinweis erscheint; Effektivmodus stimmt.
  - Mouse/Macro-Typen im HW-Modus gesperrt; Gyro im HW-Modus gesperrt (mit Hinweis).
  - Regression: Legion Go unverändert.

## Out of scope (dieser Umbau)
- Tatsächliche Implementierung von **HW-Gyro** (nur RE-TODO anlegen). Firmware-Button→Button ist **in Scope**
  (Protokoll RE'd, nur zu verdrahten — Phase 1).
- Neue **ViGEm-spezifische** Sonderwege — Backend ist VIIPER/usbip; ViGEm nur via Zwischenlayer mitgezogen.
- Änderungen an MSI Center M / dessen Gyro-Pfad („gehe da nichts an").

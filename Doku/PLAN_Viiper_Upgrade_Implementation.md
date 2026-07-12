# Implementierungsplan — VIIPER-Upgrade & Double-Input-Fix (MSI Claw)

> **Nur Plan — keine Code-Änderungen.** Grundlage: Analyse in `Doku/PLAN_Viiper_vs_HC_DoubleInput.md`.
> **Bestätigt:** VIIPER ist bereits der Default-Backend, **ViGEm ist deinstalliert** (seit Wochen) — der
> aktive Pfad auf dem Claw ist somit **VIIPER + usbip-win2**. ViGEm-Fallback-Code ist toter Pfad (out of scope).

## Ziel
1. Die eingebettete VIIPER-DLL von **`v0.4.2-dirty` (Feb 2026)** auf ein **sauberes Release v0.7.0** heben.
2. Den **virtuellen Controller-Typ (XInput ↔ DirectInput)** als Nutzer-Setting exponieren (wie HCs `HIDmode`),
   um Double-Input (Forza Horizon, Brotato) zu adressieren.
3. Suppression-/Attach-Sequencing härten.

---

## Arbeitspaket 1 — libviiper.dll: `v0.4.2-dirty` → sauberes `v0.7.0`

**Warum zuerst:** stärkster, einfachster Hebel; deckt sich mit „HC nutzt neuere VIIPER-Version". Unser Build ist
zudem `+dirty` (aus uncommittetem Tree) — nicht reproduzierbar.

### 1.1 Artefakt beschaffen
- Bevorzugt: **fertige `libviiper.dll` (x64)** aus dem VIIPER-Release **v0.7.0** (Alia5/VIIPER). Falls kein
  DLL-Artefakt im Release: aus dem `v0.7.0`-Tag bauen — Go + CGo, `go build -buildmode=c-shared -o libviiper.dll
  ./clib` (Modulpfad `github.com/Alia5/VIIPER/clib`, bestätigt aus DLL-Strings). Als **clean** (kein `+dirty`)
  bauen und Version im Repo notieren.
- SHA256 des DLL festhalten (Reproduzierbarkeit).

### 1.2 C-API-Kompatibilität prüfen (`ControllerEmulation/Viiper/LibViiper.cs`)
- Abgleich unserer 14 P/Invokes gegen die 0.7.0-Exporte (`viiper_init`, `viiper_shutdown`,
  `viiper_bus_create/remove`, `viiper_device_add`, `viiper_device_add_ex`, `viiper_device_remove`,
  `viiper_device_attach`, `viiper_list_device_types`, `viiper_device_set_input`,
  `viiper_device_set_feedback_callback`, `viiper_last_error`, `viiper_free_string`).
- **Beibehalten:** `viiper_device_attach` **nicht** aufrufen (add attacht intern — Doppel-Attach = zwei Pads,
  belegt in `ViiperService.cs:127-134`). Falls 0.7.0 das geändert hat: neu bewerten.
- Feedback-Callback-Signatur (`FeedbackCallback`, `LibViiper.cs:65-66`) gegen 0.7.0 verifizieren.

### 1.3 Wire-Format je Gerätetyp verifizieren/anpassen (`ViiperWireFormat.cs`) — **kritisch**
- **v0.5.0 war „breaking: Support XInput subtypes"** → unsere Byte-Layouts (Xbox360=20B, DS4=31B,
  DualSenseEdge=33B, XboxElite2/Steam=33B, SwitchPro=24B) können von 0.7.0 abweichen.
- Pro tatsächlich genutztem Typ (Claw: primär `xbox360`, künftig `dualshock4` — AP2) den 0.7.0-Report-Aufbau
  gegenprüfen und ggf. `WriteXbox360`/`BuildDualShock4`/… anpassen. Verifikation: reale Inputs/Achsen/Trigger
  in einem Test-Titel + `viiper_last_error`-Logging.
- `viiper_device_set_input`-Längen (`InputLength`) müssen mit dem 0.7.0-Devicepaket übereinstimmen.

### 1.4 Gerätetyp-Strings abgleichen
- `viiper_list_device_types()` (0.7.0) gegen unsere hartkodierten Typ-Strings in
  `ViiperWireFormat.BuildForDeviceType` / `ViiperInputForwarder.BuildDeviceInput`
  (`xbox360, dualshock4, dualsenseedge, xboxelite2, steamdeck-generic, switchpro, joycon-*`).
  0.7.0 ergänzt DualSense/Edge + **Switch-2-Pro** — Namensschema prüfen (Bruchgefahr bei Umbenennung).

### 1.5 usbip-win2-Pin prüfen (`Setup/UsbipInstaller.cs`)
- Aktuell gepinnt **`v.0.9.7.7`** (vadimgrn). Prüfen, welche usbip-win2-Version 0.7.0 voraussetzt/verifiziert;
  Pin (URL + Publisher-Signatur `Scheibling`/`Cloudyne`) nur bei Bedarf anheben. `UsbipInstalledProperty.cs`
  (Service `usbip2_ude` „Running") bleibt unverändert gültig.

### 1.6 DLL an allen Kopierorten ersetzen + Pipeline
- Ersetzen: `XboxGamingBarHelper/libviiper.dll` (Quelle) **und** die Build-Ausgaben
  (`XboxGamingBarHelper/bin/x64/Release/…`, `XboxGamingBarPackage/bin/x64/Release/XboxGamingBarHelper/…`).
  Prüfen, wie die DLL ins Paket kommt (csproj `Content`/`CopyToOutputDirectory` bzw. Build-Package.ps1) — nur
  die Quell-DLL pflegen, Rest generiert.

### Test-Gate AP1
- Emulation an/aus, Reboot, Sleep/Resume: virtueller Pad kommt sauber, kein doppelter Pad, keine
  `viiper_last_error`-Fehler; Rumble round-trips; Achsen korrekt in einem Referenztitel.

---

## Arbeitspaket 2 — Virtueller Controller-Typ (XInput/DirectInput) als Claw-Setting

**Warum:** HC lässt den Pad-Typ wählen (`HIDmode`); User meldet mit **DirectInput** weniger Double-Input. Die
Plumbing existiert bereits (`_viiperDeviceType`, per-Typ Wire-Format, per-Typ Feedback) — es fehlt die
Nutzer-Steuerung.

### 2.1 Helper steuerbar machen
- `ClawButtonMonitor.Viiper.cs`: `_viiperDeviceType` (Default `"xbox360"`, Zeile ~37) aus einer Einstellung
  speisen statt hart. Beim Wechsel: **Remount** des VIIPER-Device (RemoveDevice → AddDevice mit neuem `typeName`)
  über den bestehenden Teardown/Start-Pfad (`TeardownViiper`/`EnsureViiper`), damit der Typwechsel ohne
  Neustart greift.

### 2.2 Function-Enum + Property (Widget ↔ Helper)
- Neuer `Function`-Wert (`Shared/Enums/Function.cs`), z.B. `MsiClawVirtualControllerType` (int: 0=XInput/xbox360,
  1=DirectInput/dualshock4; optional 2=DualSense).
- Widget-Property `XboxGamingBar/Data/…VirtualControllerTypeProperty.cs` (Default 0=XInput).
- Helper-Property + Manager, der `ClawButtonMonitor` den neuen `typeName` setzt und Remount auslöst.
- Persistenz: LocalSettings-Key (analog `EmulationBackendProperty`).

### 2.3 UI (Claw-only)
- Dropdown „Virtual Controller Type: **XInput (Xbox 360)** / **DirectInput (DualShock 4)**" in der
  Virtual-Controller-Karte (`GamingWidget.xaml`, Nähe `VirtualControllerMasterCard`), nur sichtbar auf dem Claw.
  Hinweistext: „DirectInput kann Double-Input in manchen Titeln vermeiden."
- Gating an `controllerEmulationSupported` + Claw-Check (nicht auf Legion zeigen).

### 2.4 Live-Reapply
- Bei Änderung im Betrieb: Emulation aktiv → Device remounten; Emulation aus → nur Setting speichern.
- Mit HidHide-Zustand koordinieren (kein Fenster ohne Suppression, siehe AP3).

### 2.5 Optional (später): per-Game-Override
- Typ pro Spielprofil speicherbar (analog vorhandener Per-Game-Controller-Profile), damit z.B. Forza fix auf
  DirectInput läuft. Nicht Teil des ersten Wurfs.

### Test-Gate AP2
- **Forza Horizon + Brotato**: mit XInput reproduziertes Double-Input; auf DirectInput umschalten → prüfen ob weg.
- `joy.cpl` / Windows Game Controllers + XInput-Slots vor/nach dem Umschalten dokumentieren.

---

## Arbeitspaket 3 — Suppression/Attach-Sequencing härten

**Warum:** Restliche Double-Input-Quellen unabhängig vom Typ.

### 3.1 Physischen XInput-Zustand klären (Claw-Spezifikum)
- Auf dem Claw wird via HidHide **nur PID 0x1902 (DInput)** versteckt; **PID 0x1901 (XInput+Keyboard) bleibt
  sichtbar** (für Win+G). **Messen (Phase 0):** Ist der Claw im Emulationsbetrieb im DInput-FW-Modus (0x1902
  präsent, 0x1901 evtl. gar nicht enumeriert) — oder sind beide gleichzeitig da? Davon hängt ab, ob überhaupt
  eine zweite XInput-Quelle existiert. `MsiClawControllerModeManager`/`SendSwitchMode` (MODE_DINPUT 0x02 /
  MODE_XINPUT 0x01) als Referenz.
- Falls 0x1901-XInput real mitläuft: Weg suchen, es während Emulation zu unterdrücken, **ohne Win+G zu brechen**
  (nur die Gamepad-Collection hiden, Keyboard-Collection sichtbar lassen) — sonst bleibt AP2/DirectInput der
  primäre Workaround.

### 3.2 Duplicate-/Stale-Pad-Absicherung
- Bestehende Guards bestätigen/konsolidieren: kein `viiper_device_attach` nach `add`; verwaiste Pads nach
  fehlgeschlagenem `RemoveDevice` (`ClawButtonMonitor.Viiper.cs:240-259`, Retry 4×350ms); `VerifyAllTargetsHidden`.

### 3.3 HidHide-Reihenfolge
- Enable **vor** AddDevice beibehalten; nach dem 0.7.0-Update erneut auf Re-Enumeration-Races prüfen
  (`ControllerSuppressionManager` cycle-port).

### Test-Gate AP3
- Reboot ×3, Sleep/Resume ×3: nie zwei Pads, physischer Pad in Spielen nicht doppelt sichtbar.

---

## Reihenfolge & Abhängigkeiten
1. **Phase 0 (messen, kein Code):** Double-Input reproduzieren; XInput-1901-Präsenz klären (AP3.1); HCs
   „Controller Input"-Setting exakt einordnen.
2. **AP1** (DLL v0.7.0) — Basis; danach neu messen (evtl. ist Double-Input schon reduziert).
3. **AP2** (Typ-Wahl) — Haupt-Fix für Double-Input.
4. **AP3** (Sequencing/1901) — Feinschliff.

## Verifikation (gesamt)
- `Build-Package.ps1 -Mode Test`; On-Device Claw 8 AI+.
- Matrix: {XInput, DirectInput} × {Forza Horizon, Brotato, ein XInput-Titel, ein DInput-Titel} — Double-Input,
  Rumble, Achsen/Trigger, Gyro-abhängige Fälle.
- Regression: Sleep/Resume, Reboot, HW-Mouse-Umschaltung, Win+G weiterhin ok.

## Risiken
- **Wire-Format-Bruch durch v0.5.0-Breaking** → AP1.3 zwingend je Typ gegentesten, sonst falsche Inputs.
- **DLL-Bau (Go/CGo)** benötigt Toolchain; clean-Build statt `+dirty` sicherstellen.
- **DirectInput-Kompatibilität** pro Spiel unterschiedlich (manche Titel nur XInput) → Default bleibt XInput,
  DirectInput als Opt-in.
- **1901-Unterdrückung** darf Win+G nicht brechen (nur Gamepad-Collection).

## Kritische Dateien (ClawTweaks)
- DLL: `XboxGamingBarHelper/libviiper.dll` (+ Build-Ausgaben).
- Binding/Format: `ControllerEmulation/Viiper/{LibViiper,ViiperService,ViiperWireFormat,ViiperInputForwarder}.cs`.
- Claw-Pfad: `Labs/ClawButtonMonitor.Viiper.cs` (`_viiperDeviceType`, `EnsureViiper`/`TeardownViiper`),
  `Labs/ClawButtonMonitor.cs`.
- Backend/Settings: `Settings/EmulationBackendProperty.cs`, `Startup/Program.MSIClaw.cs`
  (`useViiper`-Auswahl, Backend-Seed).
- usbip: `Setup/UsbipInstaller.cs`, `Settings/UsbipInstalledProperty.cs`.
- Suppression: `ControllerEmulation/ControllerSuppressionManager.cs`.
- Neu (AP2): `Shared/Enums/Function.cs`, `XboxGamingBar/Data/…VirtualControllerTypeProperty.cs`, Helper-Manager,
  `XboxGamingBar/GamingWidget.xaml` (Dropdown).

## Out of scope
- **ViGEm** (deinstalliert; Fallback-Code toter Pfad — separates Cleanup, nicht hier).
- Legion-Pfad (`ViiperEmulationManager`) außer als Referenz.
- Tatsächliche Code-Änderungen (dieser Task = nur Plan).

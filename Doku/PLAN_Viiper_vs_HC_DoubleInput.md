# ClawTweaks vs. HandheldCompanion — VIIPER/usbip-Vergleich & Double-Input-Analyse

> **Scope:** Nur VIIPER + usbip-Modus, auf beiden Seiten (ClawTweaks & HandheldCompanion). **ViGEm bleibt außen
> vor** (veraltet, nur noch Fallback via Zwischenlayer). **Nur Plan — keine Code-Anpassungen.**
> Ziel: verstehen, warum HC-User weniger Double-Input-Probleme melden (z.B. Forza Horizon, Brotato), und welche
> Unterschiede in den VIIPER-Implementierungen dafür in Frage kommen.

---

## 1. Verifizierte Fakten (Stand HEAD / Upstream Juli 2026)

### ClawTweaks (lokal)
- **libviiper.dll = Alia5/VIIPER, eingebettete Go-Pseudo-Version `v0.4.2-0.20260226004220-642231cd86e8+dirty`**
  — Basis-Tag v0.4.2, Commit **26.02.2026**, gebaut **19.05.2026**, **`+dirty` (aus uncommittetem Working-Tree)**.
  Keine Win32-Versionsressource (normal für CGo `c-shared`). Datei: `XboxGamingBarHelper/libviiper.dll` (~7,7 MB).
- **C-API (P/Invoke, `ControllerEmulation/Viiper/LibViiper.cs`):** `viiper_init(listenAddr)`, `viiper_shutdown`,
  `viiper_bus_create/remove`, `viiper_device_add`, `viiper_device_add_ex(bus,type,vid,pid,out id)`,
  `viiper_device_remove`, `viiper_device_attach` (deklariert, **absichtlich NICHT aufgerufen** — `add` attacht
  schon intern; doppeltes Attach = zwei Pads, „caught during local test build 2067"), `viiper_list_device_types`,
  `viiper_device_set_input`, `viiper_device_set_feedback_callback`, `viiper_last_error`, `viiper_free_string`.
  **Kein Protokoll-/Wire-Version-Handshake C#-seitig.**
- **Transport:** libviiper IST der USB/IP-Server (`viiper_init("127.0.0.1:3241")`, nur Loopback). Windows-Attach
  passiert in libviiper gegen **usbip-win2 (vadimgrn)**, **gepinnt auf `v.0.9.7.7`** (`Setup/UsbipInstaller.cs`,
  Authenticode-verifiziert). Attach-Retry `4×350ms` (`Labs/ClawButtonMonitor.Viiper.cs`).
- **Claw-Pfad (wichtig):** `ClawButtonMonitor` ist der **primäre** Emulationspfad des Claw. Er liest den
  physischen Controller via **DirectInput (PID 0x1902)** und mountet einen virtuellen Pad. Backend =
  globale Einstellung `EmulationBackend` (`Settings/EmulationBackendProperty.cs`) → **Default VIIPER**;
  Auswahl in `Program.MSIClaw.cs:2231` `useViiper = _viiperBackendActive && mountVigem && usbipReady`, mit
  **automatischem ViGEm-Fallback** wenn usbip-win2 fehlt. **Default-Gerätetyp `xbox360` (XInput)**
  (`ClawButtonMonitor.Viiper.cs:37`).
  - Der separate `ViiperEmulationManager` (Legion) **überspringt den Claw** explizit — deshalb hat der Claw
    seinen **eigenen** VIIPER-Pfad in `ClawButtonMonitor.Viiper.cs`. (Nicht verwechseln: 2 VIIPER-Integrationen.)
  - Physisch: auf dem Claw wird **nur PID 0x1902 (DInput) via HidHide versteckt; PID 0x1901 (XInput+Keyboard)
    bleibt sichtbar**, damit Win+G funktioniert (`Program.MSIClaw.cs:16-25`). → **Der physische Claw ist als
    XInput-Gerät weiter präsent.**
- **Wire-Format (`ViiperWireFormat.cs`):** headerless Byte-Array pro Frame, **kein Version-/Magic-/Flag-Byte**.
  Xbox360=20B, DS4=31B, DualSenseEdge=33B, XboxElite2/Steam/Deck=33B, SwitchPro/JoyCon=24B. Gerätetypen embedded:
  `xbox360, xboxgip, switchpro, dualshock4, xboxelite2, dualsenseedge, keyboard, mouse`.
- **HidHide (`ControllerEmulation/ControllerSuppressionManager.cs`):** Nefarius.Drivers.HidHide (+ CLI-Fallback).
  Enable **vor** AddDevice; PnP cycle-port Re-Enumeration; `EnsureHidden`/`VerifyAllTargetsHidden` gegen Leaks.
  **Dokumentierte Double-Input-Quellen im Code:** Startup-Race zwischen Backends (Legion), Doppel-Attach,
  Claw-VIIPER neben ViGEm, verwaiste Pads nach fehlgeschlagenem RemoveDevice.
- ⚠️ **Interner Widerspruch zu verifizieren:** Es gibt scheinbar **zwei Mount-Routinen** im Claw-Pfad —
  `ClawButtonMonitor.cs:1183-1204` bevorzugt **ViGEm** und mountet VIIPER nur bei `_suppressVigem && _viiperEnabled`;
  `Program.MSIClaw.cs:2231` nutzt dagegen `useViiper = _viiperBackendActive && mountVigem && usbipReady`. Welche
  greift im Normalstart? Muss geklärt werden (evtl. mountet der Claw real doch ViGEm statt VIIPER).

### HandheldCompanion (GitHub `Valkirie/HandheldCompanion`, main, pushed 02.07.2026)
- Ersetzte ViGEm durch **VIIPER**; laut Release-Notes: „eliminates the need to suspend and resume the virtual
  controller on system sleep", „prevents the redundant power-cycling that was causing **XInput slot 0 recovery**
  issues", „should put an end to the **input loss** problems".
- **Targets (`HandheldCompanion/Targets/`):** `VIIPERTarget.cs` (abstrakte Basis) + `Xbox360Target`,
  `DualShock4Target`, `DualSenseTarget`, `SteamControllerTarget`, `SteamDeckTarget`, `SwitchProTarget`.
- **Binding (`Targets/Viiper/`):** `LibViiper.cs`, `ViiperModels.cs`, `ViiperService.cs`, **`ViiperXInput.cs`**
  (XInput-Rumble-Ausgabe via `xinput1_4.dll XInputSetState`). **Gleiche C-API wie ClawTweaks** (bus/device-Modell).
- **Manager (`HandheldCompanion/Managers/`):** `VirtualManager.cs`, `ViiperServerManager.cs`, `SettingsManager.cs`.
  - Gerätetyp-Wahl über Setting **`HIDmode`** (Enum: `Xbox360Controller`, `DualShock4Controller`,
    `DualSenseController`, `SteamDeckController`, `SwitchProController`), **pro Profil überschreibbar**
    (`SetControllerModeCore`). Das ist HCs „Controller Input"-Dropdown (User beschrieb DirectInput/XInput).
  - **VIIPER-Server:** `ViiperServerManager` startet libviiper (`viiper_init`) auf `VIIPERHost:VIIPERPort`
    (Default `127.0.0.1:3241`), **ein Bus** (`CreateBus(1)`), Start/Stop/Restart-Lifecycle.
  - **Suspend/Resume:** bei OS-Sleep `SetControllerMode(NoController)` + VIIPER stoppen; bei Resume Server +
    Mode wiederherstellen (sauberer Teardown statt Pad-Power-Cycling).
- **VIIPER upstream Releases:** neueste **v0.7.0** (~Mitte 2026); relevante Meilensteine: **v0.5.0 „Support XInput
  subtypes" (breaking)**, **v0.6.0 libVIIPER (pure C API)**, **v0.7.0 DualSense/Edge + Switch-2-Pro**.
  ClawTweaks' DLL (Basis v0.4.2, Feb-Commit, dirty) liegt damit **vor 0.5.0/0.6.0/0.7.0**.

Quellen: [HandheldCompanion](https://github.com/Valkirie/HandheldCompanion) ·
[VIIPER](https://github.com/Alia5/VIIPER)

---

## 2. Vergleich auf einen Blick

| Aspekt | ClawTweaks | HandheldCompanion |
|---|---|---|
| VIIPER-Version | **v0.4.2-dirty** (Feb 2026 Commit) | bündelt aktuellere VIIPER (Ziel: **v0.7.0**) |
| C-API-Modell | bus/device (identisch) | bus/device (identisch) |
| Physisch lesen (Claw-analog) | **DInput** (PID 0x1902) | XInput/HID je Gerät |
| Virtueller Default-Typ | **xbox360 (XInput), hart** | `HIDmode`-Auswahl, **pro Profil** |
| DInput/XInput-Wahl im UI | **nein** (nur Debug-Backend-Toggle) | **ja** (HIDmode-Dropdown) |
| Physischen Pad verstecken | HidHide, **nur DInput 1902; XInput 1901 bleibt sichtbar** | HidHide/Cloaking der jeweiligen HW |
| Suspend/Resume | Monitor-abhängig | sauberer Teardown, kein Pad-Power-Cycling |
| usbip-win2 | gepinnt v.0.9.7.7 | zu prüfen (Version) |

---

## 3. Warum HC weniger Double-Input hat — Hypothesen (mit Belegen, zu verifizieren)

- **H1 — Veraltete/dirty VIIPER-DLL.** Unsere `v0.4.2-dirty` (Feb 2026) liegt vor v0.5.0 (XInput-Subtypes),
  v0.6.0 (C-API-Stabilisierung) und v0.7.0. Fixes an Enumeration/Attach/Feedback aus 0.5–0.7 fehlen uns.
  **Stärkster, einfachster Hebel.**
- **H2 — Kein DInput/XInput-Wahlschalter, harter XInput-Default.** HC lässt den User den virtuellen Pad-Typ
  (HIDmode) wählen; ein **DInput-Output (DS4)** kollidiert nicht mit einem XInput-Slot. Auf dem Claw ist der
  **physische Pad als XInput (1901) weiter sichtbar** (wegen Win+G) — ein **virtueller XInput-Pad (xbox360)**
  ergibt so potenziell **zwei XInput-Quellen** → klassischer Double-Input in Forza/Brotato. Ein DInput-Virtual-Pad
  (oder ein Weg, 1901-XInput sauberer zu unterdrücken) könnte das umgehen — deckt sich mit dem User-Report
  „bei DirectInput weniger Probleme".
- **H3 — Attach/HidHide-Sequencing & Races.** Unsere dokumentierten Race-/Doppel-Attach-Fälle vs. HCs
  Server+Target-Trennung mit definiertem Lifecycle. Ggf. Timing-Unterschiede beim Verstecken vor dem Mounten.
- **H4 — usbip-win2-Version.** Prüfen, ob HC eine neuere/andere usbip-win2-Version nutzt als unser Pin v.0.9.7.7.
- **H5 — Zwei-Mount-Routinen-Widerspruch (siehe §1).** Falls der Claw real ViGEm statt VIIPER mountet, wäre die
  ganze „VIIPER"-Annahme für den Claw hinfällig — zuerst zu klären.

---

## 4. Untersuchungs- & Umsetzungsplan (nur Plan)

### Phase 0 — Messen & Grundannahmen verifizieren (kein Code)
1. **Double-Input reproduzieren** (Forza Horizon, Brotato) auf dem Claw mit aktuellem ClawTweaks; parallel mit
   aktuellem HC. Je: welcher virtuelle Typ ist aktiv, ist der physische Pad sichtbar (HidHide-Status,
   `joy.cpl`/Game Controllers, XInput-Slots)?
2. **Welches Backend/welche Mount-Routine** greift auf dem Claw real? Logs prüfen (`useViiper`-Log,
   „VIIPER virtual Xbox pad mounted" vs. ViGEm), Widerspruch §1 auflösen.
3. **HCs „Controller Input"-Setting exakt bestimmen** (Device → Device Settings → Controller Input): Ist es
   HIDmode (Output-Typ) oder ein Read-Backend? Welche Option meldet der User als „DirectInput"?
4. **usbip-win2-Version** bei HC vs. unser Pin vergleichen.

### Phase 1 — VIIPER-DLL aktualisieren (größter, einfachster Hebel)
- libviiper.dll auf **sauberes Release v0.7.0** (kein dirty-Build) heben; C-API-Kompatibilität prüfen
  (unsere P/Invokes vs. 0.7.0-Header) und Wire-Format je Gerätetyp gegen 0.7.0 gegentesten (Byte-Layouts können
  sich seit v0.4.2 geändert haben — v0.5.0 war „breaking" für XInput-Subtypes).
- usbip-win2-Pin ggf. auf die von 0.7.0 verifizierte Version anheben.

### Phase 2 — Gerätetyp/Protokoll-Wahl exponieren (adressiert H2)
- Auf dem Claw eine **UI-Wahl des virtuellen Pad-Typs** (mind. XInput=xbox360 vs. DInput=dualshock4) anbieten,
  analog HCs HIDmode; Default weiterhin xbox360, aber DInput als Ausweg bei Double-Input testen.
- Alternativ/zusätzlich prüfen, ob der **physische XInput (1901)** während Emulation sauberer unterdrückt werden
  kann, ohne Win+G zu brechen (der Grund, warum 1901 heute sichtbar bleibt).

### Phase 3 — Attach/HidHide-Sequencing an HC angleichen (adressiert H3)
- Server+Target-Lifecycle und HidHide-Reihenfolge mit HC vergleichen; Doppel-Attach-/Race-Absicherungen
  konsolidieren; sauberer Teardown bei Suspend/Resume.

---

## 5. Offene Fragen / Risiken
- **Wire-Format-Bruch:** v0.5.0 war breaking (XInput-Subtypes). Ein DLL-Update ohne Anpassung der Byte-Layouts
  in `ViiperWireFormat.cs` könnte falsche Inputs erzeugen. → Gegentesten je Gerätetyp.
- **Claw-Besonderheit:** DInput-Read + XInput-1901-sichtbar ist Claw-spezifisch (Win+G). Ein DInput-Virtual-Pad
  ändert, was Spiele sehen (kein XInput) — Kompatibilität pro Spiel prüfen.
- **Zwei-Mount-Routinen** (§1/H5) zuerst klären, sonst ist die Backend-Annahme falsch.
- HCs Vorteil könnte **primär die neuere VIIPER-Version** sein (H1) — Phase 1 zuerst, dann neu messen, bevor
  H2/H3 umgesetzt werden.

## 6. Kritische Dateien
**ClawTweaks:** `XboxGamingBarHelper/libviiper.dll`; `ControllerEmulation/Viiper/{LibViiper,ViiperService,
ViiperWireFormat,ViiperInputForwarder,ViiperEmulationManager}.cs`; `Labs/ClawButtonMonitor.Viiper.cs` +
`ClawButtonMonitor.cs` (Mount-Routinen); `Settings/EmulationBackendProperty.cs`; `Setup/UsbipInstaller.cs`;
`ControllerEmulation/ControllerSuppressionManager.cs`; `Startup/Program.MSIClaw.cs` (Backend-Seed + `useViiper`).
**HandheldCompanion (Referenz, GitHub):** `Targets/VIIPERTarget.cs` + `Targets/Viiper/{LibViiper,ViiperService,
ViiperXInput}.cs`; `Managers/{VirtualManager,ViiperServerManager,SettingsManager}.cs`; Targets je Gerätetyp.

## 7. Out of scope
- **ViGEm** (nur Fallback via Zwischenlayer; nicht weiterentwickeln).
- Nicht-Claw-Geräte (Legion) außer als Referenz.
- Tatsächliche Code-Änderungen (dieser Task = nur Plan).

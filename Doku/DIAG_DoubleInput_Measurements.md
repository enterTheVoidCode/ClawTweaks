# On-Device-Messungen — Double-Input / VIIPER (laufendes Protokoll)

Begleitet die Analyse in `PLAN_Viiper_vs_HC_DoubleInput.md`. Snapshots via
`Diagnostics/Check-ControllerState.ps1` (read-only). Gerät: Claw 8 AI+ A2VM (MSICLAW8AI).

## 2026-07-12

### CTW-Virtual (elevated, Leerlauf/Desktop)
- usbip: **Port 01 = Xbox360 Controller (045e:028e)** via usbip://localhost:3241 -> VIIPER aktiv.
- XInput: **1** (virtueller Pad 045e:028e, capsFlags=12).
- winmm/DInput: joy1 = 045e:028e **voll funktional (10 Buttons/5 Achsen)**; joy2 = physisch 1902 (0/0 Stub).
- Claw-FW: **DInput-Modus** (PID_1902 OK, PID_1901 nicht OK).
- HidHide: CLI/Registry **"Zugriff verweigert" (0x0005)** — Helper haelt den Treiber exklusiv.

### CTW-HW (elevated, Leerlauf/Desktop)
- usbip: **leer** (kein virtueller Pad).
- XInput: **1** = **physischer Claw** (PID_1901, capsFlags=0, packet=1).
- winmm/DInput: joy2 + joy4 = 1901 (beide 0/0 Stubs).
- Claw-FW: **XInput-Modus** (PID_1901 OK).

### WICHTIG — empirischer Gegenbeweis (User, on-device, vor CTW-Deinstallation)
- **CTW virtueller Controller in Brotato getestet -> KEIN Double-Input.**
- Folge: **Die "XInput+DInput Dual-Enumeration = Double-Input"-Hypothese reicht als alleinige Erklaerung
  NICHT.** Obwohl der virtuelle Xbox360-Pad in beiden APIs auftaucht, verdoppelt Brotato nicht.
- Das Problem ist also komplexer / titel- bzw. konfigurationsspezifisch / evtl. transient (Boot/Resume,
  externer BT-Pad, bestimmte Titel wie Forza) — NICHT allein die Gerätetyp-Wahl.
- Konsequenz fuer den Plan: AP2 (DInput-Gerätetyp) bleibt plausibler Hebel, ist aber **nicht bewiesen**;
  die eigentliche Ursache muss per gezieltem Repro (der konkrete Problemtitel, ggf. mit/ohne BT-Pad,
  frisch vs. nach Resume) eingegrenzt werden.

### HC-DInput (elevated) — Controller Mode = DirectInput
- usbip: **Port 01 = Xbox360 Controller (045e:028e)** via usbip://localhost:**3242** (CTW nutzte 3241).
- XInput: **1** (045e:028e, capsFlags=12) — **identisch zu CTW-Virtual**.
- winmm/DInput: joy1 = 045e:028e (10/5), joy2 = 1902 (0/0) — **identisch zu CTW-Virtual**.
- **HidHide lesbar** (CTW-Helper weg): cloak **on**; hidden = **NUR** `HID\VID_0DB0&PID_1902&MI_00&COL01`
  (die physische Claw-DInput-Gamepad-Collection); Allowlist = HC.exe, GameInput-Proxies, GameBar-Familie,
  ApplicationFrameHost, RuntimeBroker, svchost, explorer, Shell-Hosts. -> **gleicher Ansatz wie CTW**
  (Claw via DInput lesen, 1902 hiden, XInput-1901+Keyboard sichtbar lassen fuer Win+G).

### HC user.config (relevante Settings)
- **`HIDmode = 0` = Xbox360Controller** -> der emulierte Pad ist **Xbox360**, AUCH im "DirectInput"-Modus.
  D.h. HCs "Controller Input: DirectInput" schaltet **NICHT** den Ausgabe-Gerätetyp auf DS4 um; es betrifft
  den **physischen Lesepfad** (HC liest den Claw via DirectInput/1902) — genau das, was CTW ohnehin tut.
- `VIIPEREnabled=True`, `VIIPERPort=3242`, `VIIPERHost=127.0.0.1`. `VIIPERExecutablePath=Tools\...exe`
  ist **ungenutzt** (kein viiper-Prozess; `viiper_go_debug.log` im HC-Ordner) -> HC lädt libVIIPER
  **in-process**, genau wie CTW.
- `HIDstatus=1`, `HIDcloakonconnect=True`, `MSIClawControllerIndex=2`.

### VIIPER-Versionsvergleich (in-process libVIIPER)
- **CTW:** `libviiper.dll`, Go-Version `v0.4.2-0.20260226004220-642231cd86e8+dirty` — Commit **26.02.2026**,
  **+dirty** (uncommitteter Tree), 8.110.538 B, 19.05.2026.
- **HC:** `libVIIPER.dll`, Go-Version `v0.0.0-20260410095643-746e56fc9e2f` — Commit **10.04.2026**,
  **clean**, 8.594.967 B, 30.05.2026.
- => HCs libVIIPER ist **~6 Wochen neuer und ein sauberer Build**; CTWs ist älter und dirty.

## Revidierte Schlussfolgerung (2026-07-12)
1. **In vergleichbarem Zustand sind CTW-Virtual und HC-DInput praktisch IDENTISCH** (gleicher Xbox360-045e:028e
   VIIPER-Pad, gleiche Dual-Enumeration, gleiche joy.cpl, gleicher Hide-Ansatz).
2. Die frühere These "DInput-Gerätetyp (DS4) = Fix" ist **entkräftet**: HC mountet im DirectInput-Modus
   weiterhin **Xbox360**, nicht DS4. Die DInput/XInput-Wahl in HC betrifft den **Lesepfad**, den CTW schon nutzt.
3. **Dual-Enumeration ist NICHT die Double-Input-Ursache** (Brotato doppelte bei CTW nicht).
4. **Einziger messbarer Unterschied bisher:** HCs libVIIPER ist **neuer + clean**, CTWs **älter + dirty**.
   -> **AP1 (DLL auf sauberes, neueres/aktuelles VIIPER heben) ist der plausibelste reale Hebel**; AP2
   (Gerätetyp-Wahl) ist **nicht bewiesen** und rutscht in der Priorität nach hinten.
5. Das Double-Input ist offenbar **titel-/situationsspezifisch** (Forza? nach Resume/Boot? externer BT-Pad?)
   — muss per **In-Game-Repro des konkreten Problemtitels** eingegrenzt werden, nicht im Desktop-Leerlauf.

### HC-XInput (elevated) — Controller Mode = XInput  ***DER SCHLÜSSEL***
- usbip: Port 01 = Xbox360 045e:028e (unverändert, HIDmode bleibt Xbox360).
- XInput: 1 (virtueller 045e:028e).
- winmm/DInput: **DREI** Joysticks:
  - joy1 = 045e:028e **(10/5, funktional)** = virtueller Pad
  - joy2 = 0DB0:1901 (0/0 Stub)
  - **joy4 = 0DB0:1901 (10/5, FUNKTIONAL)** = **der physische Claw als zweiter voll nutzbarer Joystick!**
- Claw-FW: **XInput-Modus** (1901 aktiv). HidHide-hidden = `1901&IG_03`, `1902&MI_00&COL01`, `USB\1901&MI_00`
  — aber der **funktionale DInput-Spiegel des 1901 (joy4) bleibt sichtbar/nicht unterdrückt**.

## ***GEFUNDEN: der Double-Input-Mechanismus*** (2026-07-12)
Der Unterschied liegt am **physischen Lesepfad / Claw-FW-Modus**, NICHT am virtuellen Gerätetyp oder der
VIIPER-Version:

| Zustand | funktionale Joysticks für ein Spiel | Double? |
|---|---|---|
| **CTW-Virtual** (Claw FW=DInput, 1902 versteckt) | **1** (nur virtueller 045e:028e) | **nein** |
| **HC-DInput** (Claw FW=DInput, 1902 versteckt) | **1** (nur virtueller 045e:028e) | **nein** |
| **HC-XInput** (Claw FW=XInput, 1901 aktiv) | **2** (virtueller 045e:028e **+ physischer 1901**) | **JA** |

- Im **DInput-Lesepfad** setzt die FW den Claw auf 1902; dessen Gamepad-Collection wird versteckt → nur der
  virtuelle Pad ist funktional → **kein Double**.
- Im **XInput-Lesepfad** ist der physische Claw 1901 aktiv und wird **zusätzlich als voll funktionaler
  DirectInput-Joystick (joy4, 10/5)** enumeriert, den HidHide hier NICHT wegnimmt → **zweiter Pad neben dem
  virtuellen → Double-Input**. Deckt sich mit "DirectInput = weniger Probleme".
- **CTW default = DInput-Lesepfad** = der saubere Zustand (= HC-DInput). Deshalb doppelte Brotato bei CTW nicht.
- Folge: CTW-User mit Double-Input sind vermutlich in einem **XInput-artigen Zustand** (Claw in XInput-FW +
  virtueller Pad, physischer DInput-Spiegel nicht versteckt) — transient (Resume/Boot/Mode-Switch) oder
  titelgetrieben.

## Konsequenz für den Plan (revidiert)
- **Nicht** primär AP2 (Gerätetyp) und **nicht** primär die VIIPER-Version.
- **Kernfix:** sicherstellen, dass bei aktivem virtuellem Pad der **physische Claw NIE als zweiter
  funktionaler Controller sichtbar** ist — d.h. Claw konsequent im **DInput-FW-Modus** halten und die
  1902-Collection verstecken (CTW tut das bereits), bzw. falls je XInput-FW aktiv ist, auch den **funktionalen
  DInput-Spiegel des 1901** via HidHide entfernen. Race/Transient beim Mode-Switch/Resume absichern.
- Diagnose-Signal für Task #23 / Export-Logs: **Anzahl funktionaler winmm-Joysticks (>1 = Double)** — genau das
  loggen.

## KORREKTUR (2026-07-12, nach Brotato-Test im HC-XInput-Modus)
- **Brotato im HC-XInput-Betrieb getestet -> KEIN Double-Input**, obwohl der Snapshot 2 funktionale
  winmm-Joysticks zeigte. => Die These **"winmm-Count > 1 = Double" ist widerlegt** (nur latentes Risiko-Signal,
  keine Garantie).
- Entscheidend: **XInput zeigte in ALLEN Captures immer genau 1** (CTW-Virtual, CTW-HW, HC-DInput, HC-XInput).
  Der zweite Controller existiert nur in der **winmm/DirectInput**-Enumeration, nie als zweiter XInput-Slot.
- Verfeinertes Modell: Double-Input entsteht **nur in Spielen, die DirectInput ZUSÄTZLICH zu XInput lesen und
  NICHT deduplizieren** (Forza-Klasse) UND der physische DInput-Spiegel funktional ist. **XInput-zentrierte
  Titel (Brotato) doppeln nie**, unabhängig vom Modus. Der Geräte-Snapshot **sagt Double NICHT voraus** — es
  hängt am Input-API-Verhalten des konkreten Spiels.
- Konsequenz: Repro braucht den **konkret betroffenen Titel** (dual-API, kein Dedupe). Brotato ist als
  Repro-Titel ungeeignet.

## Symptom-Korrektur: es ist KEIN Double-Input
User-Report der betroffenen Forza-User: das Spiel **switcht staendig zwischen Tastatur- und Controller-Modus**
(Icon-Flip), NICHT zwei Controller. => Input-Device-Arbitration-Flapping, nicht Doppel-Pad.
Zwei Hypothesen: (A) virtueller Pad flappt (VIIPER-Attach-Instabilitaet unter Last) -> Fallback auf Tastatur je
Drop; (B) ein Keyboard/Maus-HID feuert nebenher (Stick-als-Maus-Drift / Gyro-Maus / FW-Keyboard-Remap).

## In-Game-Messungen (Forza Horizon 6, User, 2026-07-12)
- **HC-DirectInput + FH6 = SAUBER** (kein Icon-Flip).
- **HC-XInput + FH6 = SAUBER** (kein Icon-Flip).
- **CTW-HW-Modus + FH6 = OK** (nativer Controller, KEIN virtueller Pad -> erwartungsgemaess sauber).
- **CTW-VIRTUELL + FH6 = SAUBER (nach Reboot!)**: virtueller VIIPER-Controller funktioniert einwandfrei in
  FH6, KEIN Icon-Flip, keine Doppelung.
=> **In ALLEN getesteten Konfigurationen (HC-DInput, HC-XInput, CTW-HW, CTW-Virtuell) ist FH6 sauber.**

## Zwischenfall (wichtig): usbip ohne Reboot = "mounted, aber tot"
- usbip-win2 frisch installiert, KEIN Reboot -> CTW-Status OK/VIIPER, aber Game Bar/Gamepad-Tester bekamen
  KEINE Eingaben (Kernel-Treiber erst nach Reboot live). Sah aus wie ein Controller-Bug.
- **Nach Reboot: alles ok, virtueller Controller inkl. FH6 funktioniert.**
- -> Realer UX-Bug, Task #24 (Reboot-Hinweis nach usbip-Install erzwingen). Sehr plausibel, dass ein Teil der
  User-"Controller geht nicht"-Reports genau daher kommt.

## GESAMTFAZIT (2026-07-12)
1. **Der gemeldete Forza-Flip / "Double-Input" liess sich auf diesem Geraet in KEINER Konfiguration
   reproduzieren** — weder CTW noch HC, weder DInput noch XInput, HW noch virtuell. Kein Double-Input, kein
   Keyboard/Controller-Flip.
2. Alle Zwischen-Hypothesen (Dual-Enumeration; winmm-Count>1; Lese-Pfad/FW-Modus) wurden durch Messungen
   **widerlegt**.
3. **Konkret gefundener realer Bug:** usbip-ohne-Reboot = toter virtueller Controller -> Task #24.
4. **Bester verbleibender (unbewiesener) Hebel:** CTWs libVIIPER = aelterer **dirty Feb-Build**, HCs = **clean
   Apr-Build**. Update (Plan AP1) als Stabilitaets-/Hygiene-Massnahme sinnvoll, aber **nicht als Fix fuer einen
   nicht-reproduzierbaren Bug belegt**.
5. **Fuer echten Fortschritt am Flip:** exakte Repro-Config der betroffenen User noetig (Titel-Version, externe
   Pads, Config wie Stick-Maus/Gyro/FW-Remap, frisch vs. nach Resume). Ohne Repro nicht weiter jagbar. Der Lese-Pfad/FW-Modus und die Device-Enumeration sind damit als Ursache
**ausgeschlossen**. **Der Flip ist CTW-spezifisch** — etwas, das CTW tut und HC nicht.

## Verbleibende CTW-spezifische Verdächtige (zu messen)
- **A) VIIPER-Pad flappt** unter FH6-Last: CTWs libVIIPER ist der aeltere **dirty Feb-Build** (HCs clean Apr) —
  instabilere Attach-Schicht koennte unter Last kurz droppen -> Fallback auf Tastatur.
- **A2) CTW re-mountet den Pad** bei Ereignissen waehrend des Spiels (Profil-Reapply bei game-start/stop,
  RTSS-Reconnect, Auto-Xbox-Swap bei offener Game Bar, HW-Mouse-Watcher, ExternalGamepad/Mode-Wechsel).
- **B) CTW emittiert Keyboard/Maus** nebenher: Stick-als-Maus-Drift, Gyro-als-Maus, FW-Keyboard-Remaps.
  (Reine Claw-Keyboard/Maus-Collections scheiden aus — HC laesst sie ebenso sichtbar und flippt NICHT.)

## Naechster (entscheidender) Schritt
- **CTW neu installieren -> FH6 + Watch-ControllerFlap.ps1 (-Label CTW-Forza)** mitlaufen lassen.
  - `XINPUT_LOST/GAINED`-Paare beim Flip -> Pad flappt (A/A2) -> danach CTW-Helper-Log (helper_*.log) an den
    Flap-Zeitstempeln lesen, um den Re-Mount-Ausloeser zu finden.
  - Kein XInput-Verlust trotz Flip -> B (CTW emittiert Keyboard/Maus) -> RawInput-Logger bauen.
- Vorab-Screening B: waren bei den betroffenen Usern Stick-als-Maus / Gyro-Maus / FW-Keyboard-Remaps aktiv?

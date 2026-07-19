# PLAN: ClawTweaks Center schrittweise mit dem Helper verbinden & als Haupt-Tool integrieren

> Status: Entwurf zur Abstimmung. Baut auf `Doku/PLAN_Setup_App.md` auf (dort als „später"
> vermerkt: „In-App-Update-Integration"/Phase 4 „Wichtige Einstellungen"). Dieser Plan macht daraus
> einen konkreten, schrittweisen Weg: **Center bekommt eine echte IPC-Verbindung zum Helper** und
> wird danach schrittweise zum größeren Einstellungsfenster neben dem Widget.

---

## 0a. Phase 0 — Center als reguläre Windows-App (✅ umgesetzt & getestet)

Bevor irgendetwas anderes passiert — auch vor dem Umsetzen von IPC/Onboarding unten —, installiert
sich Center jetzt selbst als reguläre App, statt portabel von irgendwo zu laufen:

- **Neu:** `Core/SelfInstaller.cs`, `InstallCenterWindow.xaml(.cs)`.
- Läuft die gestartete exe **nicht** aus `%ProgramFiles%\ClawTweaks Center\`, zeigt `App.xaml.cs` als
  allererstes `InstallCenterWindow` (vor `CenterMenuWindow`/`MainWindow`). Ⓐ „Install" kopiert die
  exe dorthin (+ msix/cer/Dependencies/Setup-Tools.ps1, falls im Release-Ordner danebenliegend —
  damit bleibt der bestehende Release-Ordner-Ablauf unverändert funktionsfähig), legt eine
  Startmenü-Verknüpfung an (WScript.Shell COM, keine neue Abhängigkeit), registriert einen
  Add/Remove-Programs-Eintrag (`HKLM\...\Uninstall\ClawTweaksCenter`, `UninstallString` ruft die exe
  mit `--uninstall`) und startet die installierte Kopie neu. Erst danach läuft der bisherige
  Standalone/Release-Ordner-Ablauf weiter — **das Widget-MSIX kann also nie installiert werden, bevor
  Center selbst installiert ist**, exakt wie gewünscht.
- **Auf dem Gerät verifiziert:** Erststart zeigt den Install-Prompt → Ⓐ → Programmordner + Verknüpfung
  + Registry-Eintrag korrekt angelegt → Neustart aus Program Files → zweiter Start erkennt
  `IsRunningFromInstallDir()` und zeigt den Prompt **nicht** erneut, geht direkt weiter.
- **Technik-Entscheidung (mit dir abgestimmt):** selbst-kopierend statt Inno/WiX — keine neue
  Build-Abhängigkeit, alles in C#/WPF im bestehenden Projekt.
- **`--uninstall` getestet und funktionsfähig:** Registry-Eintrag + Startmenü-Verknüpfung sofort weg,
  Programmordner (inkl. exe) nach kurzer Verzögerung vollständig gelöscht. Der ursprüngliche
  Lösch-Mechanismus (Polling-Schleife mit `goto`-Sprung zu einem Label **innerhalb** einer
  geklammerten `for`-Schleife) ließ den Ordner beim ersten Test liegen — bekannter `cmd.exe`-Fallstrick
  (`goto` auf ein Label in einer Klammerung ist unzuverlässig). Fix: simple feste 2-Sekunden-Verzögerung
  (`timeout /t 2 & rmdir`) statt Polling — robuster, weil der aufrufende Prozess ohnehin fast sofort
  nach dem Spawnen beendet wird.
- **Rest von Phase 0** (Auto-Start von Center statt/neben der Game Bar nach Erstinstall, echtes
  MSIX-Bundling in `Build-Package.ps1`) bleibt unverändert **§5 Hand-off** — nicht von mir umsetzbar.

---

## 0. Ziel

- **Helper bleibt die alleinige Quelle der Wahrheit** ("waltet und schaltet") — das ändert sich
  nicht. Center greift genau wie das Widget nur über IPC auf den Helper zu, implementiert **keine**
  eigene Geräte-/TDP-/Controller-Logik.
- **Phase A (kurzfristig):** Onboarding wird an **zwei Stellen** angeboten:
  1. **Automatisch** direkt nach Erstinstallation **und** nach jedem Update — sobald der Helper
     elevated läuft und frisch installiert/aktualisiert wurde. Der User bleibt **in ClawTweaks
     Center**, statt sofort zur Game Bar zu springen.
  2. **Manuell jederzeit** über einen Eintrag im Center-Hauptmenü (für „ich will das nochmal
     durchlaufen" / „ich hab was verstellt").
  Beide Wege führen in dieselbe Onboarding-Sequenz aus §3 Phase 3.
- **Später:** Center wird ein vollwertiges, größeres Einstellungsfenster (mehr Platz als die
  Game-Bar-Kacheln erlauben), das man auch nach der Ersteinrichtung jederzeit öffnen kann — parallel
  zum Widget, nicht als Ersatz.

---

## 1. Der Kernblocker: der Helper spricht heute nur mit EINEM Client

Verifiziert im Code (`XboxGamingBarHelper/IPC/NamedPipeServer.cs`):

```csharp
public const string PipeName = "GoTweaksHelper";
...
_pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
    1 /* maxNumberOfServerInstances */, ...);
```

- **Nur eine** Pipe-Instanz, **ein** Reader/Writer-Paar (`_reader`/`_writer` als einzelne Felder,
  kein Dictionary pro Verbindung). Der Helper kann aktuell **nicht gleichzeitig** mit Widget UND
  Center sprechen.
- Zusätzlich gated `GamingWidget.PipeClient.cs` (`App.GetActiveGamingWidget()`) Nachrichten auf
  „genau eine aktive Widget-Instanz" — noch ein Grund, das nicht anzufassen.

**Konsequenz für den Plan:** Center bekommt eine **zweite, eigene Named Pipe**
(z. B. `PipeName = "ClawTweaksCenter"`), nicht die vom Widget benutzte `GoTweaksHelper`-Pipe.

- Helper-seitig: eine **zweite Instanz derselben `NamedPipeServer`-Klasse** unter neuem Pipe-Namen
  starten (reine Ergänzung, keine Änderung an der bestehenden Widget-Pipe/-Logik — die bleibt
  unangetastet, damit nichts an der ohnehin fragilen Widget-Reconnect-Historie riskiert wird, siehe
  CLAUDE.md „Widget bleibt nach Hibernate leer").
- Center-seitig: ein `NamedPipeClientStream` gegen `\\.\pipe\ClawTweaksCenter`, **identisches
  Zeilen-JSON-Protokoll** wie Widget↔Helper (`{"Key":"Value", ...}` pro Zeile), damit der Helper
  keine zweite Nachrichten-Grammatik lernen muss.
- Beide Pipes greifen auf **dieselben** `HelperProperty<T,TManager>`-Instanzen zu (z. B.
  `ControllerEmulationEnabledProperty`) — eine Änderung über Center broadcastet sich also “gratis”
  auch zum Widget, falls das parallel offen ist (die Property-Klasse kennt schon
  Mehrfach-Subscriber-Broadcast intern, das ist nicht neu zu bauen).

---

## 2. Was wiederverwendet wird (kein Neubau von Geräte-Logik)

| Onboarding-Schritt | Bereits vorhandener Baustein (Helper) | Aufwand für Center |
|---|---|---|
| **Virtuellen Controller aktivieren** | `ControllerEmulationEnabledProperty`, `ControllerEmulationDefaultModeProperty` (`XboxGamingBarHelper/ControllerEmulation/ControllerEmulationProperties.cs`) — exakt das, was der Widget-Toggle setzt | Center setzt dieselbe Property über die neue Pipe. Kein neuer Code im Helper nötig. |
| **MSI Center M deaktivieren** | `MsiCenterManager.Disable()` (`XboxGamingBarHelper/MSI/MsiCenterManager.cs`) — vollständig: Tasks stoppen, Dienste deaktivieren, Prozesse killen, Game-Bar-Extension-Paket entfernen (1:1 aus HC `ISpaceWatcher.Disable()` portiert). Verdrahtet als `Function.MsiCenterActive` (`Shared/Enums/Function.cs:455`, bool, „write to toggle") — **das ist bereits die Kachel-Logik aus dem Widget.** | Center schreibt einfach `MsiCenterActive=false` über die neue Pipe — **kein neuer Helper-Code**, exakt dieselbe Funktion wie die Widget-Kachel. |
| **LED-Grundeinstellung** | `Program.LedComposite.cs` (Pipe-Handler) + `MsiLedCompositeStore.cs` (Persistenz) + `LedCompositor.cs` | Center schickt dieselbe Composite-Message wie das Widget-LED-Tab. Payload-Format 1:1 vom Widget übernehmen (`GamingWidget.MsiLedEffect.cs` als Referenz für den JSON-Aufbau). |
| **Prüfen: virtueller Controller korrekt verbunden** | `ClawTweaksSetup/Core/ControllerHealth.cs` — **existiert in Center bereits** (`ControllerPhase.cs` nutzt es schon): `Probe()` liefert `VirtualPadCount`, `VirtualPadName`, `XInputConnected`, `ClawPresent` über eine schnelle, read-only PS-Diagnose. | 1:1 wiederverwenden — nach dem Aktivieren des virtuellen Controllers erneut `Probe()` aufrufen und auf `VirtualPadCount >= 1` prüfen, statt etwas Neues zu bauen. |
| **Auto-Jump zur ClawTweaks-Kachel** | `GameBarWidgetPositionProperty` (`XboxGamingBar/Data/GameBarWidgetPositionProperty.cs`, `Function.GameBarWidgetPosition`, int) — 1-basierter Slot; Helper tippt beim Game-Bar-Öffnen (Position−1)× RB, um automatisch zu ClawTweaks zu springen (`Program.MSIClaw.cs`, `GameBarAutoNavRbCount`). Default 1 = aus. | Center schreibt denselben Function-Wert (üblich: 3, da Microsoft die ersten zwei Slots belegt). **Wichtiger Vorbehalt:** laut Code-Kommentar ist das eine **vom Widget** persistierte `WidgetProperty` (`ApplicationData.Current.LocalSettings` der Widget-Paketidentität), der Helper selbst speichert sie nicht dauerhaft. Setzt Center sie über die neue Pipe, wirkt sie zwar sofort im Helper, könnte aber beim nächsten Verbinden des Widgets von dessen (alten) gespeichertem Wert wieder überschrieben werden. **Vor Umsetzung klären/prüfen:** entweder persistiert der Helper diesen Wert künftig selbst (kleine, gezielte Helper-Änderung), oder Center schreibt zusätzlich in denselben Speicherort wie das Widget. Siehe auch §4. |
| **„Ist der Helper überhaupt bereit?"** | Bereits im alten Setup-Plan skizziert (`HelperWaiter.cs`, in `ClawTweaksSetup` als `InstallPhase`-Warteschritte umgesetzt) | Direkt weiterverwenden — Center verbindet sich erst, wenn der Helper-Prozess läuft **und** die neue Pipe angenommen hat. |

**Nichts davon ist erfunden** — jede Zeile oben verweist auf existierenden Code. Der einzig neue
Baustein ist die zweite Pipe plus ein schlanker Center-seitiger Property-Client (die Client-Hälfte
von `HelperProperty<T>`, im Prinzip eine Kopie des Musters aus `GamingWidget.PipeClient.cs`, ohne
die WinUI/Game-Bar-spezifischen Teile).

---

## 3. Phasen

### Phase 1+2 — Zweite Pipe + schreibender Zugriff (✅ Code umgesetzt, ⏳ On-Device-Test steht aus)

Statt eines separaten reinen Lesezugriffs-Meilensteins wurden Pipe + Schreibzugriff in einem Zug
gebaut, weil beides zusammen dieselbe kleine, überschaubare Änderung ist:

- **Helper (`XboxGamingBarHelper`):**
  - `IPC/NamedPipeServer.cs` — Pipe-Name jetzt Instanz-Property (`_pipeName`, Konstruktor-Parameter,
    Default = bisheriger `PipeName`-Konstante) statt hart codiert, damit eine zweite Instanz mit
    eigenem Namen laufen kann, ohne die bestehende Klasse zu duplizieren.
  - `Program.cs` — zweites Feld `centerPipeServer = new IPC.NamedPipeServer("ClawTweaksCenter")` in
    `InitializeConnection()`, **bewusst mit minimalen `Connected`/`Disconnected`-Handlern** (kein
    Per-Game-Profil-Suppress-Fenster, keine TDP/Fan/LED-Re-Push-Logik wie bei der Widget-Pipe — die
    bleibt komplett unangetastet).
  - `Startup/Program.PipeHandlers.cs` — `centerPipeServer.MessageReceived` läuft über **denselben**
    `PipeServer_MessageReceived`-Handler wie die Widget-Pipe (verifiziert: Property-SETs laufen über
    den generischen `properties.HandlePipeMessage(valueSet)`-Pfad, nicht über die ~50 Stellen im Code,
    die Antworten hart an `pipeServer` adressieren — jene sind nur für RequestId-korrelierte
    Anfragen relevant, und Center sendet nie eine RequestId > 0, siehe unten). `IsPipeConnected` und
    `SendPipeMessage` sind jetzt Dual-Pipe: `IsPipeConnected` = Widget ODER Center verbunden;
    `SendPipeMessage` broadcastet an **beide**, die gerade verbunden sind.
  - **Bewusst NICHT angefasst:** die ~50 Stellen, die Request/Response-Antworten hart an `pipeServer`
    schreiben (z. B. `SendPipeAck`). Property-Broadcasts (das, was Center für alle vier
    Onboarding-Schritte braucht) laufen über `SendPipeMessage`, nicht über diese Stellen — die
    Center-Anbindung braucht sie nicht und lässt sie unverändert riskant nur für Widget-Anfragen.
- **Center (`ClawTweaksSetup`):**
  - Neu: `Core/HelperPipeClient.cs` — verbindet als `NamedPipeClientStream` gegen
    `\\.\pipe\ClawTweaksCenter`, **identisches Zeilen-JSON** wie die Widget-Pipe
    (`{"RequestId":0,"Command":1,"Function":N,"Content":"..."}`), Retry-Connect mit Timeout,
    `SetProperty(Function, value)` (Fire-and-forget, RequestId bleibt 0 — kein Ack nötig/erwartet),
    `SetAndWaitForConfirmationAsync(...)` (wartet auf den **Push-Broadcast** der geänderten Property
    zurück, kein Polling).
  - **Bewusst NICHT** `Shared.IPC.PipeMessage` wiederverwendet: dessen `ToValueSet()`/`FromValueSet()`
    hängen an `Windows.Foundation.Collections.ValueSet` (WinRT) — laut Kommentar in `Shared.csproj`
    darf das nie in einen modernen SDK-Style-Consumer wie `ClawTweaksSetup` durchschlagen
    (`CoreClrInitFailure` beim Start). `HelperPipeClient` baut das JSON stattdessen von Hand
    (genau wie das Widget selbst beim **Parsen** eingehender Nachrichten macht,
    `ParsePipeMessageToValueSet` in `GamingWidget.PipeClient.cs`) und nutzt nur die reinen
    `Shared.Enums.Function`/`Command`-Enums (unproblematisch, wie `DeviceInfo`/`DeviceType` schon
    vorher aus `Shared` genutzt werden).
- **Build-Check:** `ClawTweaksSetup` baut clean (`dotnet build`). Der Helper lässt sich nicht
  vollständig über `dotnet build` prüfen (fehlende WinRT-Async-Erweiterungsmethoden sind ein
  Tooling-Limit von `dotnet build` gegen dieses alte .NET-Framework-4.8-Projekt, **nicht** von meinen
  Änderungen — die drei geänderten Dateien selbst wurden vom Compiler ohne Fehler durchlaufen, bevor
  er an unrelated Stellen abbrach). Ein echter, vollständiger Build läuft nur über `Build-Package.ps1`
  — **das darf ich nicht ausführen**, s. „Wie testen" unten.

### Phase 3 — Onboarding-Sequenz (✅ Code umgesetzt, ⏳ On-Device-Test steht aus)

**Umgesetzt:**
- `Core/OnboardingRunner.cs` — die feste Sequenz als eigene Klasse: Center M aus + Bestätigung
  abwarten → virtuellen Controller an → `ControllerHealth.Probe()` (bis zu 5 Versuche, 1 s Abstand)
  → Auto-Jump **nur** wenn der Health-Check `VirtualPadCount >= 1` bestätigt. Jeder Schritt hat einen
  Status (Pending/Working/Ok/Error/Skipped) + Detailtext, per Event an die UI gemeldet.
- **Eigener Bereich auf der Home-Seite** (`CenterMenuWindow.BuildOnboardingSection()`, direkt unter
  dem „Currently installed"-Block, oberhalb von „Update & Release") — Leerlauf zeigt Beschreibung +
  „Start onboarding"-Button, ein Lauf zeigt alle vier Schritte live mit Status-Icon, danach
  „Run again". Kein separates Fenster/keine eigene Seite — bewusst eine feste Sektion, wie gewünscht.
- **Automatischer Trigger, beide Installationswege:**
  - `CenterMenuWindow.InstallSelectedAsync` (der „Update & Release"-Baupfad direkt in Center) —
    startet die Sequenz, sobald `ok` (Zertifikat + Paket + Helper-Wait allesamt erfolgreich) true ist.
  - `MainWindow` (Release-Ordner-Assistent) — `InstallPhase.ReadyForOnboarding` wird nach einem
    erfolgreichen `InstallAsync()`-Lauf gesetzt; `MainWindow.GoForward()` öffnet beim Abschluss der
    Phasenkette `new CenterMenuWindow(startOnboarding: true)` statt einfach zu schließen, wenn das
    Flag gesetzt ist.
  - Beide Wege decken „nach jeder Installation oder jedem Update" ab, weil `InstallAsync`/
    `InstallSelectedAsync` in beiden Fenstern **immer** der eigentliche Install/Update-Lauf sind
    (idempotent, kein separater „schon aktuell"-Kurzschluss, der die Sequenz umgeht).
- **UI-Test (ohne echten Helper) erfolgreich:** Abschnitt rendert korrekt, alle vier Schritte zeigen
  sauber „✕ Could not connect to the helper" statt zu crashen, wenn kein Helper läuft — „Run again"
  funktioniert. Screenshot vom User bestätigt.
- **LED bewusst NICHT Teil der Sequenz** — siehe §4, dort war die Frage offen; die konkrete
  Reihenfolge, die du danach vorgegeben hast, enthielt nur die vier oben genannten Schritte.

### (alte Entwurfs-Notizen, durch das oben Umgesetzte ersetzt)

**Wo angeboten (aus deiner Vorgabe):**
- **Automatisch:** direkt im Anschluss an `InstallPhase`, wenn die Installation eine **Erstinstallation
  oder ein Update** war (nicht bei „schon aktuell, nichts zu tun") **und** der Helper danach bestätigt
  elevated läuft (`HelperWaiter`-Erfolg). Läuft der Helper schon vorher unverändert weiter (z. B. Setup
  neu gestartet, aber Paket war bereits aktuell), wird die Sequenz **nicht** automatisch angestoßen.
- **Manuell:** ein Eintrag im Center-Hauptmenü (`CenterMenuWindow`/Home-Ansicht), der dieselbe Sequenz
  jederzeit erneut startet — z. B. „Onboarding erneut durchlaufen".
- Beide Wege landen in derselben neuen Phase/View, unten als „Onboarding" bezeichnet.

**Ablauf — feste Reihenfolge, kein freies Formular:**
1. **MSI Center M deaktivieren.** Center schreibt `MsiCenterActive=false`. Da `Disable()`
   (Tasks/Dienste/Prozesse/Appx-Paket) einen Moment dauert und die Pipe keinen expliziten
   Fertig-Ack kennt, **pollt** Center danach `MsiCenterActive` (bzw. den ControllerHealth-
   `CenterMRunning`-Wert) im selben Muster wie die bestehenden Tool-Installs in `ToolsPhase`
   (kurzes Intervall, Timeout mit Fehlermeldung statt endlosem Warten) — **kein neues Ack-Protokoll
   erfinden**, sondern das etablierte „schreiben → mit Timeout re-verifizieren"-Muster übernehmen.
2. **Warten, bis der Helper durch ist** (= Schritt 1 bestätigt abgeschlossen, s. o.), erst dann weiter.
3. **Virtuellen Controller aktivieren.** `ControllerEmulationEnabledProperty=true` (+ Default-Modus)
   schreiben.
4. **Prüfen, ob der virtuelle Controller korrekt verbunden ist.** `ControllerHealth.Probe()` erneut
   aufrufen (bestehender Code, s. Tabelle oben) — kurz warten/retryen, da USBIP/ViGEm-Enumeration
   nicht instantan ist. Ergebnis `VirtualPadCount >= 1` (und `ClawPresent` weiterhin true) = ok.
5. **Nur wenn Schritt 4 erfolgreich war:** Auto-Jump setzen (`GameBarWidgetPosition`, s. Tabelle oben
   inkl. Persistenz-Vorbehalt). Schlägt Schritt 4 fehl, wird Auto-Jump **nicht** gesetzt (macht keinen
   Sinn, auf eine Kachel zu springen, die nicht sauber läuft) — stattdessen Fehleranzeige + Re-Try.
- **LED** ist Teil des ursprünglichen Wunsches („led beleuchtung, center m deaktivieren, virtuellen
  controller aktivieren"), aber in der jetzt vorgegebenen Reihenfolge nicht wieder erwähnt — offen,
  wo genau LED in diese Sequenz gehört (vor Schritt 1, parallel, oder als eigener, unabhängiger Punkt
  im Menü). Vor Umsetzung kurz klären, siehe §4.
- **Der User bleibt im Center-Fenster**, bis die Sequenz durchlaufen ist — erst danach der bestehende
  „Game Bar öffnen"-Schritt, nicht mehr automatisch sofort nach dem Paket-Install.
- **Test:** kompletter Erstinstall- und Update-Durchlauf; Reihenfolge exakt wie oben beobachtbar
  (Center M geht zuerst aus, erst danach reagiert der virtuelle Controller); Auto-Jump wird nur bei
  bestätigt laufendem virtuellem Controller gesetzt, nie blind.

---

## 3a. Wie der Helper den Trigger von Center überhaupt bekommt (On-Device-Test)

Der Code (Phase 1+2+3 oben) ist fertig und baut clean, aber **läuft noch nirgendwo** — der aktuell
installierte Helper auf deinem Gerät kennt die zweite Pipe nicht, weil er aus dem **alten**,
unveränderten MSIX-Paket stammt. Damit Center den Helper überhaupt erreichen kann, muss der Helper
**neu gebaut und neu deployed** werden. Das kann ich nicht selbst anstoßen (`Build-Package.ps1`/
`Install.ps1` sind tabu) — das sind deine Schritte:

1. **`Build-Package.ps1` laufen lassen** (wie gewohnt). Das baut `XboxGamingBarHelper` mit den
   Änderungen aus diesem Plan (`Program.cs`, `Program.PipeHandlers.cs`, `IPC/NamedPipeServer.cs`) neu
   und packt das Ergebnis ins MSIX unter `Build\Installer`.
2. **Installieren/aktualisieren** — entweder klassisch über `Install.ps1`, oder über `CTW_Center.exe`
   selbst (Home → „Update & Release" → die frisch gebaute Version wählen, falls sie dort auftaucht;
   sonst den lokalen Installer-Ordner direkt nutzen, je nachdem wie du normalerweise testest).
3. **Prüfen, dass der Helper die zweite Pipe wirklich startet** — im Helper-Log
   (`%LOCALAPPDATA%\Packages\MSIClaw.ClawTweaks_7eszav2039cvc\LocalCache\ClawTweaks\Helper\...` bzw.
   die `helper_*.log`-Dateien) nach dieser Zeile suchen, die kurz nach dem Widget-Pipe-Start kommen
   sollte:
   ```
   Named Pipe server started: \\.\pipe\ClawTweaksCenter
   ```
   Fehlt sie, lief noch der alte Helper (Deployment nicht durchgelaufen, oder falscher Build).
4. **Center starten und Onboarding auslösen** — entweder automatisch (frisch installiert/aktualisiert,
   s. o.) oder manuell über den „Start onboarding"-Button auf der Home-Seite. Jetzt sollte die Sequenz
   nicht mehr sofort mit „Could not connect to the helper" abbrechen, sondern:
   - Schritt 1 (Center M aus) grün werden, sobald der `MsiCenterActive=false`-Broadcast zurückkommt,
   - Schritt 3 (Health-Check) den echten `ControllerHealth.Probe()`-Stand zeigen,
   - Schritt 4 (Auto-Jump) nur grün werden, wenn Schritt 3 grün war.
5. **Gegenprobe im Helper-Log:** nach `ClawTweaks Center connected via Named Pipe` suchen (bestätigt,
   dass die Verbindung wirklich über die neue, zweite Pipe lief und nicht zufällig über die
   Widget-Pipe) sowie nach den üblichen `SetTDP`/Property-Log-Zeilen für `MsiCenterActive` und
   `ControllerEmulationEnabled`, um zu sehen, dass die Werte tatsächlich ankamen.

Das ist der eigentliche Lackmustest für Phase 1+2 — bis dahin ist alles hier nur code-review-geprüft
und per UI-Smoke-Test (ohne echten Helper) verifiziert, s. Phase 3 oben.

---

## 3b. Wo Center installiert liegt (für eine künftige Widget-Kachel „Open ClawTweaks Center")

Damit das **Widget** (anderes Projekt, `XboxGamingBar`) später eine Kachel/Einstellung bekommen kann,
die Center aus der Game Bar heraus startet, hier die konkreten, von `Core/SelfInstaller.cs` (Phase 0)
festgelegten Fundstellen — nichts davon ist im Widget-Projekt umgesetzt, das ist reine Doku für den
nächsten Schritt:

- **Fester Pfad:** `%ProgramFiles%\ClawTweaks Center\CTW_Center.exe`
  (`SelfInstaller.InstallDir` = `Path.Combine(Environment.SpecialFolder.ProgramFiles, "ClawTweaks Center")`,
  `AppDisplayName = "ClawTweaks Center"`, `ExeName = "CTW_Center.exe"` — beides Konstanten in
  `SelfInstaller.cs`, falls sich der Name mal ändert, hier nachschauen statt zu raten).
- **Robuster als der feste Pfad — Registry:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\
  Uninstall\ClawTweaksCenter`, Wert `InstallLocation` (REG_SZ) = Installationsordner,
  `DisplayIcon` (REG_SZ) = voller Pfad zur exe. **Fehlt der Key komplett → Center ist nicht
  installiert** (z. B. altes Widget/Helper vor Phase 0, oder User hat Center nie ausgeführt) — das ist
  der zuverlässigste „ist Center überhaupt da"-Check für eine Kachel, robuster als nur auf den festen
  Pfad zu prüfen, weil er derselbe Mechanismus ist, den Windows selbst für Add/Remove Programs nutzt.
- **Startmenü-Verknüpfung** (Alternative, falls eine Kachel lieber „wie ein Nutzer" starten soll statt
  den Pfad direkt zu kennen): `%ProgramData%\Microsoft\Windows\Start Menu\Programs\
  ClawTweaks Center.lnk` (maschinenweit, nicht pro User — Center läuft elevated).
- **Elevation:** `CTW_Center.exe` verlangt `requireAdministrator` (App-Manifest) — ein Kachel-Klick aus
  dem (nicht zwingend elevated laufenden) Widget heraus löst beim Start also einen UAC-Prompt aus,
  genau wie andere „externes Tool starten"-Kacheln das schon tun. Kein neues Muster nötig, nur denselben
  Ansatz wiederverwenden, den bestehende Tile-Actions in `XboxGamingBar/QuickSettings/TileAction.cs`
  für externe Tools bereits nutzen.
- **Noch offen (nicht Teil dieses Plans):** die eigentliche Kachel/Setting im Widget-Projekt anlegen,
  inkl. „installiert/nicht installiert"-Status-Anzeige über den Registry-Check oben. Gehört in eine
  eigene Abstimmung, sobald Phase 1–3 auf dem Gerät bestätigt sind.

---

### Phase 4 — Center jederzeit öffenbar (nicht nur beim Setup)
- ~~Bisher ist `CTW_Center.exe` ein einmaliges Installer-Tool...~~ **Erledigt durch Phase 0:** die
  Startmenü-Verknüpfung aus `SelfInstaller` deckt „erneut startbar" bereits ab. Bleibt offen: beim
  erneuten Start **direkt** in eine Konfig-/Übersichtsansicht springen, wenn Package/Cert schon
  vorhanden sind (aktuell landet man einfach wieder auf der normalen Home-Seite — die zeigt die
  Onboarding-Sektion zwar prominent an, aber startet sie nicht automatisch bei jedem Öffnen, nur nach
  einem echten Install/Update-Lauf, s. Phase 3).
- Inhaltlich wächst die Konfig-Ansicht hier optional über die drei Onboarding-Punkte hinaus (mehr
  Properties/Tabs) — bewusst **nicht** in diesem Plan vorwegnehmen, sondern nach Phase 3 einzeln
  abstimmen, welche Einstellungen als nächstes sinnvoll sind (großes Fenster = Platz für z. B.
  Profilverwaltung, die auf den Game-Bar-Kacheln immer beengt war).

### Phase 5 — Bundling ins Hauptpaket (Hand-off, NICHT von mir umsetzbar)
- Damit `CTW_Center.exe` wirklich „mit ClawTweaks gebundelt" ist (im MSIX mitinstalliert, aus dem
  Installationsordner startbar, ggf. vom Helper/Install.ps1 nach Erstinstallation automatisch
  gestartet statt der Game Bar), müssen **`Build-Package.ps1`** (Packaging-Layout/Content-Liste) und
  **`Install.ps1`** (Post-Install-Startverhalten) geändert werden.
- **Das sind die beiden Dateien, die laut CLAUDE.md „niemals angefasst" werden dürfen — keine
  Ausnahmen.** Dieser Schritt ist deshalb bewusst als eigener, letzter Punkt ausgegliedert: alles in
  Phase 1–4 lässt sich vollständig bauen und auf dem Gerät testen, **ohne** diese beiden Dateien zu
  berühren (Center bleibt bis dahin ein separat verteiltes Tool, das man manuell danebenlegt/startet,
  genau wie heute). Das eigentliche „ins Paket einbacken" musst du selbst in `Build-Package.ps1`/
  `Install.ps1` vornehmen, wenn Phase 1–4 sich bewährt haben.

---

## 4. Offene Fragen (vor Umsetzung kurz klären)

- ~~Kann `MsiCenterManager` MSI Center M heute schon abschalten, oder nur erkennen?~~ **Geklärt:**
  ja, vollständig (`Disable()`), bereits als `Function.MsiCenterActive` verdrahtet — kein neuer
  Helper-Code nötig, Center ruft nur die bestehende Kachel-Funktion über die neue Pipe auf.
- **LED-Default beim Onboarding:** Soll Center bei „frisch installiert, noch nie konfiguriert"
  überhaupt aktiv einen LED-Wert *setzen*, oder nur den vom Helper gemeldeten Ist-Zustand anzeigen
  und dem User die Wahl lassen (sicherer angesichts der bekannten Clobber-Historie)?
- **Wo steht LED in der Reihenfolge?** Die in dieser Iteration vorgegebene feste Sequenz (§3 Phase 3)
  nennt nur Center-M-aus → warten → virtuellen Controller an → Health-Check → Auto-Jump. LED war im
  ursprünglichen Wunsch dabei, aber nicht Teil dieser Reihenfolge — als eigener, unabhängiger Schritt
  davor/danach, oder ganz aus der Auto-Sequenz raus und nur manuell im Menü?
- **Auto-Jump-Persistenz:** schreibt der Helper `GameBarWidgetPosition` künftig selbst dauerhaft, oder
  schreibt Center zusätzlich in den vom Widget genutzten Speicherort? (Details in der Tabelle oben.)
  Ohne Klärung besteht das Risiko, dass ein späteres Öffnen des Widgets den von Center gesetzten Wert
  wieder auf den alten Stand zurückzieht.
- **Reihenfolge Config-Phase vs. Game-Bar-Öffnen:** Game Bar danach automatisch öffnen (wie heute),
  oder dem User überlassen?
- **Zweite Pipe vs. Multi-Client-Umbau der bestehenden Pipe:** Dieser Plan setzt bewusst auf eine
  zweite, komplett getrennte Pipe (Risiko-minimal). Ein Multi-Client-Umbau von
  `NamedPipeServer`/`App.GetActiveGamingWidget()` wäre die „sauberere" Langfrist-Lösung, ist aber ein
  deutlich größerer, riskanterer Eingriff in stabilen, historisch fragilen Code (siehe Blank-Widget-
  Bug). Für „nach und nach" wie gewünscht ist die zweite Pipe der richtige erste Schritt; ein
  Zusammenführen kann später erwogen werden, wenn sich das bewährt hat.

---

## 5. Testreihenfolge

1. Phase 1 auf dem Gerät: zweite Pipe verbindet, Live-Werte lesbar, kein Einfluss auf Widget/Game Bar.
2. Phase 2: alle drei Properties schreibend, Wirkung an der Hardware/im Widget verifiziert.
3. Phase 3: kompletter Erstinstall-Durchlauf mit neuer Konfig-Phase.
4. Phase 4: Center nach Abschluss erneut startbar, springt direkt in die Konfig-Ansicht.
5. Phase 5: eigenständig von dir in `Build-Package.ps1`/`Install.ps1` verdrahtet, sobald 1–4 sich
   bewährt haben.

# PLAN: Geführtes ClawTweaks-Setup (`ClawTweaksSetup.exe`)

> Status: Entwurf zur Umsetzung. UI-Framework entschieden: **WPF / .NET (standalone `setup.exe`)**,
> elevated, controller-navigierbar. Verteilung **vorerst parallel** zu den bestehenden Methoden
> (PS-Skript-ZIP + reine MSIX), **später** in den In-App-Update integriert.

---

## 0. Ziel & Leitplanken

Ein einziges, geführtes Setup, das ein Nutzer **bei Erstinstallation UND bei jedem Update**
ausführen kann und das in **beiden Fällen dieselben idempotenten Checks + Reparaturen** fährt.
Es soll die Fehlerquellen abfangen, die sich im Support als „geht nicht / Doppelinput / kein
virtueller Controller" zeigen — und die ein Nutzer heute nur über verstreute Setup-Tab-Buttons
und manuelle Tool-Installs löst.

**Kernaussagen (vom Nutzer festgelegt):**

- **Viiper ist der alleinige Standard** für virtuelle Emulation. **ViGEm ist irrelevant** und wird
  vom Setup **nicht** angefasst/installiert (bleibt nur als Legacy-Per-Tool-Button im Setup-Tab).
- **usbip (usbip-win2) ist Pflicht**, solange der virtuelle Modus der Default ist. **Nach der
  usbip-Erstinstallation ist ein Reboot nötig**, damit der UDE-Treiber aktiv wird.
  (Ein späterer optionaler reiner HW-Controller-Betrieb bräuchte usbip nicht — jetzt noch nicht
  Thema, aber im Ablauf als „für virtuellen Modus erforderlich" formulieren, nicht als hart-blockend
  für die reine App-Installation.)
- **HW-Controller-Gesundheit zuerst.** Ist der native Claw-Controller nicht sauber (typisch: MSI
  Center M / MysticLight kämpft um Controller/LED, doppelte HID-Geräte, Center-M-Reste), **kann
  CTW gar nicht sauber laufen**. Dann: Anleitung MSI Center M **per Uninstall-Tool** entfernen +
  **neueste Version** installieren (neueste Center-M-Version liegt bereits im Treiberpfad übers
  Manifest), **Center M Cleaner + Firmware verlinken**.
- **Königsdisziplin (optional):** wichtigste Einstellungen direkt anbieten — Game Bar selbst auf
  **Position 3** schieben, **Auto-Jump auf Pos 3** nach Virtual-Controller-Aktivierung, finaler
  **Health-Check**.

**Absolute Verbote (bestehende Projektregeln, gelten weiter):**

- `Build-Package.ps1`, `Install.ps1` (das MSIX-Install-Skript) und `ClawTweaks.pfx` **niemals
  anfassen/committen**. Das Setup ist ein **neues, danebenliegendes** Projekt.
- Commit-Messages + Code-Kommentare **Englisch** (UI-Texte deutsch/lokalisierbar erlaubt).
- HID-Commands via **HidSharp**, nicht `HidD_SetOutputReport`.
- **Portieren, nicht neu erfinden:** so viel wie möglich aus vorhandener Logik wiederverwenden
  (siehe §2), nur die geführte Orchestrierung + UI ist neu.

---

## 1. Was wiederverwendet wird (kein Neuschreiben)

| Zweck | Vorhandene Quelle | Wiederverwendung |
|-------|-------------------|------------------|
| Zertifikat trusten + MSIX (+Deps) installieren, alten Helper sauber stoppen, `-ForceUpdateFromAnyVersion` | `XboxGamingBarPackage/InstallTemplate/Install.ps1` | **Als Baustein aufrufen** (nicht editieren) — das Setup ruft dieses Skript bzw. dieselben Schritte auf. |
| Tool-Detection (HidHide/RTSS/PawnIO/usbip/ViGEm) + winget-Install | `XboxGamingBarHelper/Setup/Setup-Tools.ps1`, `Labs/HidHideProperties.cs`, `Labs/RtssInstallHelper.cs` | Detection-Logik + winget-IDs 1:1 übernehmen (in setup-eigene Detektor-Klasse portieren, oder das PS-Skript direkt mit `-Only` aufrufen). |
| usbip-Download (pinned, signiert) + Authenticode-Verify + interaktiver Inno-Install (`/NORESTART`) | `XboxGamingBarHelper/Setup/UsbipInstaller.cs` | **Kernlogik 1:1 portieren** (pinned URL `v.0.9.7.7`, Signer-Pins „Scheibling"/„Cloudyne", WinVerifyTrust). Exit `3010` = Reboot nötig. |
| Tool-Uninstall (ARP / Service / NSIS `/S` / Inno unins000) | `XboxGamingBarHelper/Labs/ToolUninstaller.cs` | Für „Center M sauber entfernen" (ARP-Pattern) und Reparatur-Pfade wiederverwenden. |
| Controller-Diagnose (Health-Check-Datenbasis) | `XboxGamingBarHelper/Diagnostics/ControllerDiagnostics.cs` (embedded PS `Script`) | **Als Health-Check-Engine** nutzen: dieselbe Abfrage (Game Controller, XInput-Slots, MSI-Mode, HidHideCLI, ViGEm, usbip, steamxbox, Prozesse) parsen → sauber/unsauber ableiten. |
| „Ist installiert?"-Registry/Service-Checks | `Setup-Tools.ps1` `Test-*Installed`, `HidHideHelper.IsInstalled` | Für die Setup-Statusliste. |

> **Konsequenz:** Das Setup ist zu ~70 % Orchestrierung + UI über vorhandene Bausteine.
> Neu ist v. a.: WPF-Fenster, XInput-Navigation, Zustands-/Phasen-Maschine, Health-Interpretation,
> Update-vs-Install-Erkennung, Mehrfach-Helper-Erkennung, optionale Settings-Anwendung.

---

## 2. Projektstruktur (neu)

Neues Projekt **`ClawTweaksSetup/`** (WPF, .NET Framework 4.8 — gleiche Runtime wie Helper, damit
der portierte `UsbipInstaller`/WinVerifyTrust-Code unverändert läuft; SDK-Style csproj, x64).

```
ClawTweaksSetup/
  ClawTweaksSetup.csproj        # WPF, net48, x64, <ApplicationManifest> requireAdministrator
  app.manifest                  # requestedExecutionLevel level="requireAdministrator"
  App.xaml / App.xaml.cs
  MainWindow.xaml / .cs         # Wizard-Shell (ein Fenster, Phasen als Content)
  Navigation/
    XInputNavigator.cs          # Poll XInput (D-Pad/Stick=Fokus, A=aktivieren, B=zurück)
    FocusVisuals.cs             # großer, controller-tauglicher Fokusrahmen
  Phases/
    PhaseBase.cs                # gemeinsames Interface: Title, Run(), Status, CanContinue
    Phase0_Detect.cs            # Install-vs-Update, Mehrfach-Helper, Vorbedingungen
    Phase1_HwHealth.cs          # HW-Controller-Health zuerst (Center-M-Guidance)
    Phase2_Tools.cs            # HidHide / usbip / RTSS check+install (+Reboot-Prompt)
    Phase3_Cert.cs              # Cert-Check + Klick-Install
    Phase3b_Package.cs          # MSIX-Install (+Deps), Helper-Wait + Progress
    Phase4_Settings.cs          # optional: Game Bar Pos 3, Auto-Jump, Final-Health
  Core/
    ToolDetect.cs               # Test-*Installed portiert aus Setup-Tools.ps1
    UsbipSetup.cs               # portiert aus UsbipInstaller.cs
    CertInstaller.cs            # Import-Certificate TrustedPeople (aus Install.ps1)
    PackageInstaller.cs         # Add-AppxPackage-Aufruf / Install.ps1-Aufruf
    HelperWaiter.cs             # wartet auf Helper-Prozess + Pipe „bereit"
    ControllerHealth.cs         # ruft ControllerDiagnostics-PS, parst, bewertet
    GameBarLauncher.cs          # Win+G / Game-Bar-Aktivierung + Progress-Text
  Resources/  (Icons, Center-M-Cleaner-Link, Firmware-Link als Konstanten)
```

**Build:** eigener MSBuild/`dotnet build`-Aufruf **außerhalb** von `Build-Package.ps1`. Distribution
als signierte `ClawTweaksSetup.exe` neben ZIP/MSIX. (Signierung mit derselben Cert wie Helper —
aber die `.pfx` bleibt ungetrackt; Signierung übernimmt der bestehende Build-Mechanismus/Manuell,
nicht dieser Plan.)

---

## 3. Ablauf (Phasen-Maschine)

Ein Fenster, oben eine Fortschrittsleiste mit den Phasen; jede Phase hat: **Status-Icon**
(grün ok / gelb Aktion nötig / rot blockiert), **Erklärtext**, **Aktions-Button** (A-Taste),
**Weiter** (nur aktiv wenn Phase ok oder bewusst übersprungen). Alles mit großem Fokusrahmen und
XInput bedienbar. Jede Phase ist **idempotent** und **re-run-fähig** (Re-Check-Button).

### Phase 0 — Erkennung & Vorbedingungen
- **Install vs. Update erkennen:** `Get-AppxPackage *ClawTweaks*` vorhanden? → Update-Pfad
  (Kopfzeile „Update wird vorbereitet"), sonst Erstinstallation. **Beide fahren dieselben
  Folgephasen** — nur Texte/Default-Häkchen unterscheiden sich.
- **Mehrfach-Helper erkennen:** mehr als ein `XboxGamingBarHelper`-Prozess und/oder Alt-Task
  `GoTweaks\GoTweaksHelper` neben `ClawTweaks\ClawTweaksHelper` → Warnung + „Bereinigen"
  (Tasks beenden + alle Helper killen, Muster aus `Install.ps1` §3.5 wiederverwenden).
- **Elevation** ist per Manifest schon sichergestellt (Setup startet elevated). Kein Nachladen.
- Ausgang: Weiter → Phase 1.

### Phase 1 — HW-Controller-Gesundheit (ZUERST)
- **Datenbasis:** `ControllerHealth.Collect()` ruft die embedded PS aus `ControllerDiagnostics`
  (oder eine schlanke Teilmenge) und wertet aus:
  - genau **ein** MSI-Claw-Gamepad sichtbar (kein Doppel-HID/Doppel-XInput),
  - MSI-Mode plausibel (XInput/DInput wie erwartet),
  - **kein MSI Center M / MysticLight-Konflikt** (Prozess/Dienst vorhanden? Center-M-Version?),
  - keine „steamxbox"/fremden virtuellen Pads, die Doppelinput erzeugen.
- **Ergebnis „sauber" (grün):** Weiter freigeschaltet.
- **Ergebnis „unsauber":**
  - **Claw gar nicht erkannt** → **rot/blockiert** (echtes Problem; ohne Controller geht nichts).
  - **MSI Center M läuft** → **nur gelbe Warnung, NICHT blockierend.** Die eigentliche geführte
    Center-M-Deaktivierung/-Entfernung passiert **erst NACH erfolgreicher Installation** (siehe
    Phase 4b). Grund: Center M am Anfang anzufassen ist riskant, und der Health-Check hier ist rein
    diagnostisch. Der User sieht den Hinweis „wird nach der Installation behandelt".
- **Wichtig:** Diese Phase blockiert nur bei echten Problemen (fehlender Controller), nicht bei
  behebbaren Warnungen (Center M, Steam-Filter). Der User darf mit Warnung fortfahren.

### Phase 4b — MSI Center M bereinigen (NACH der Installation)
Erst wenn das Paket installiert und der Helper gestartet ist, die geführten Center-M-Aktionen:
  1. **Center M deaktivieren** (Prozess beenden) für die aktuelle Sitzung, ODER
  2. **Center M deinstallieren** — Button (via `ToolUninstaller` ARP-Pattern „MSI Center M"/
     „MSI Center"), danach Hinweis auf **Center M Cleaner** (Link) für Reste.
  3. **Neueste Center-M-Version installieren** — liegt bereits im **Treiberpfad übers Manifest**;
     Button startet die lokale Version (Pfad aus Manifest). **Firmware-Link** dazu.
  4. **Re-Check** + Reboot-Hinweis, falls Reste erst nach Neustart weg.

### Phase 2 — Erforderliche Tools prüfen & installieren
Reihenfolge **HidHide → usbip → RTSS** (ViGEm bewusst NICHT). Pro Tool: Status + „Installieren".
- **HidHide:** Detection aus `Test-HidHideInstalled`/`HidHideHelper.IsInstalled`; Install via winget
  `Nefarius.HidHide` (`Setup-Tools.ps1 -Only hidhide`). Reboot-Hinweis „kann nötig sein".
- **usbip (Pflicht für virtuellen Modus):** Detection = ARP/Service. Fehlt → `UsbipSetup.Run()`
  (portiert): pinned signierten Installer laden, **Authenticode verifizieren**, **interaktiven**
  Inno-Installer `/NORESTART` starten (der sichtbare Wizard ist nötig, damit die Treiber-Bestätigung
  kommt — siehe Kommentar in `UsbipInstaller.cs`). **Exit 3010 oder Erstinstallation → Phase setzt
  `RebootRequired=true`** und zeigt am Ende einen **Reboot-jetzt / Später**-Prompt; nach Reboot soll
  der Nutzer das Setup einfach erneut starten (idempotent → springt direkt weiter).
- **RTSS:** Detection `Test-RTSSInstalled`; Install via winget `Guru3D.RTSS`
  (`RtssInstallHelper.Install()` / `-Only rtss`). Clock-Skew-Hinweis (0x8A15005E) aus vorhandenem
  Text übernehmen.
- **Alle-installieren**-Button (Batch), plus Einzel-Buttons. Nach jedem Install **Re-Check**.
- Ausgang: alle Pflicht-Tools grün (usbip darf „installiert, Reboot ausstehend" sein) → Weiter.

### Phase 3 — Zertifikat prüfen + installieren, dann MSIX
- **Cert-Check:** ist die ClawTweaks-Signing-Cert in `Cert:\LocalMachine\TrustedPeople`? (Thumbprint
  der neben dem Setup liegenden `*.cer` vergleichen). Nicht vertraut → **rot**, Button „Zertifikat
  installieren" → `Import-Certificate ... -CertStoreLocation Cert:\LocalMachine\TrustedPeople`
  (exakt wie `Install.ps1`, **kein** Root-CA-Store — AV-Flag vermeiden). Danach grün.
- **MSIX-Install:** erst wenn Cert grün. `PackageInstaller` ruft die **bestehende** Install-Logik auf
  (Helper stoppen → `Add-AppxPackage -DependencyPath ... -ForceApplicationShutdown
  -ForceUpdateFromAnyVersion` → alten Helper-Deploy-Ordner löschen). Fortschrittsanzeige
  („Paket wird installiert…").
- **Helper-Wait + Progress:** nach Install **Game Bar automatisch öffnen** (`GameBarLauncher`,
  Progressbar „**Game Bar wird automatisch geöffnet…**"), dann `HelperWaiter` pollt auf einen
  laufenden `XboxGamingBarHelper` + Pipe-„bereit" (Timeout mit freundlicher Meldung „öffne Win+G
  manuell"). Bei Update: erneut auf **genau einen** Helper prüfen (Mehrfach-Helper aus Phase 0
  nicht wieder aufgetaucht).

### Phase 4 — Wichtige Einstellungen anwenden (optional, „Königsdisziplin")
Nur anbieten, mit Häkchen (default an), alles über Helper-IPC/vorhandene Properties:
- **Game Bar Widget auf Position 3** schieben (soweit über vorhandene Order-/Favoriten-Mechanik
  möglich — siehe Memo `todo-gamebar-widget-order-fav`: OEM-Slot gated, ggf. nur Anleitung statt
  Automatik; realistisch als „so weit möglich + Hinweis").
- **Auto-Jump auf Pos 3** nach Virtual-Controller-Aktivierung aktivieren (Setting setzen).
- **Finaler Health-Check** (Phase-1-Messung erneut) → grüne Zusammenfassung „Alles bereit".
- Abschluss-Screen: Status aller Phasen, „Fertig", ggf. **Reboot-jetzt** falls usbip frisch.

---

## 4. Idempotenz & Wiederanlauf

- Jede Phase misst **live** und stellt nur das Fehlende her; erneuter Start nach Reboot springt
  automatisch zu erster nicht-grüner Phase.
- Kein Persistieren von „done"-Flags nötig — der **Systemzustand ist die Wahrheit** (Tools da? Cert
  vertraut? Paket installiert? Helper läuft?). Genau das macht Install und Update über denselben Weg.

## 5. Sicherheit / AV-Rücksicht

- usbip-Download nur auf **expliziten Klick**, **pinned URL**, **Authenticode + Publisher-Pin**
  (aus `UsbipInstaller` übernommen). Keine generische Download-&-Launch-Schleife im Setup-Body für
  die anderen Tools — die laufen via **winget** (wie `Setup-Tools.ps1`), das AV-neutral ist.
- Cert nur in **TrustedPeople**, nie Root.
- Keine skript-getriebene Persistenz (kein PS-kopiert-exe+Task) — Helper-Deploy macht wie gehabt der
  Helper selbst via `--setup` (siehe `Install.ps1`-Kommentar zu Defender-Persistence-Flag).

## 6. Offene Punkte / später

- **Position-3-Automatik** ist laut Memo teils nicht zuverlässig automatisierbar → als „best effort +
  Anleitung" umsetzen, nicht als harte Zusage.
- **Center-M-Version im Manifest**: exakten lokalen Pfad beim Umsetzen aus dem Treiber-Manifest lesen
  (Konstante in `Resources`), Center-M-Cleaner- + Firmware-URLs dort pflegen.
- **Reiner HW-Modus ohne usbip**: künftig usbip in Phase 2 von „Pflicht" auf „nur für virtuellen
  Modus" degradieren (Flag), sobald das Feature existiert.
- **In-App-Update-Integration**: später den Phasen-Kern als Bibliothek auch aus dem Helper-Update
  aufrufbar machen; jetzt bewusst standalone.

---

## 7. Umsetzungsreihenfolge (testbare Schritte)

1. **Gerüst:** WPF-Projekt `ClawTweaksSetup`, elevated-Manifest, Wizard-Shell + eine Dummy-Phase,
   **XInput-Navigation** (Fokus/A/B) lauffähig. → Test: Fenster startet elevated, mit Controller
   navigierbar.
2. **Core-Detektoren:** `ToolDetect` (HidHide/usbip/RTSS) + `ControllerHealth` (Diagnose-PS parsen).
   → Test: Statusliste zeigt real korrekt an.
3. **Phase 2 (Tools):** HidHide/RTSS via winget, **usbip via portiertem `UsbipSetup`** inkl.
   Reboot-Prompt. → Test auf Gerät: fehlendes Tool wird installiert, usbip verlangt Reboot.
4. **Phase 3 (Cert + MSIX):** Cert-Check/Install + Paket-Install + Helper-Wait + Game-Bar-Progress.
   → Test: frische Installation und Update laufen durch, Helper meldet sich.
5. **Phase 1 (HW-Health):** Health-Interpretation + Center-M-Guidance/Uninstall/Links.
   → Test: mit installiertem Center M „unsauber", nach Entfernen „sauber".
6. **Phase 0 (Erkennung):** Install/Update + Mehrfach-Helper-Bereinigung.
7. **Phase 4 (Settings):** optionale Anwendung + Final-Health + Abschluss.
8. **Distribution:** signierte `ClawTweaksSetup.exe` neben ZIP/MSIX bereitstellen (parallel).

> Nach jedem Schritt: Nutzer testet auf dem Gerät, Feedback, dann weiter. Build des Setups über
> eigenen `dotnet build`/MSBuild-Aufruf — **nicht** über `Build-Package.ps1`.

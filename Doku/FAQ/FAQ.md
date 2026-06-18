# ClawTweaks – FAQ

Häufige Fragen rund um ClawTweaks. Jeder Punkt lässt sich aufklappen.

<details>
<summary><strong>Was sind die Features von ClawTweaks?</strong></summary>

<br/>

ClawTweaks ist ein Xbox-Game-Bar-Widget (plus Hintergrund-Helper) für den MSI Claw. Hier ein Überblick der Features nach Art:

### Kacheln (Quick-Settings Tiles)

Schnellzugriff-Kacheln direkt im Widget – das Herzstück der Bedienung.

<img src="Features/Feature%20Tiles.png" width="420" />

### Kacheln anpassen & Hotkeys

Kacheln frei anordnen, ein-/ausblenden und mit Hotkeys belegen.

<img src="Features/Feature%20Tiles%20Customization%20and%20Hotkeys.png" width="420" />

### Themes / Design

Mehrere Themes zur Auswahl – inklusive des neuen Default-Designs „Next Gen Claw".

<img src="Features/Feature%20Theme%20Support.png" width="420" />

### Eingebaute Actions für Tiles & Shortcuts

Eine große Auswahl an eingebauten Aktionen für Kacheln und Shortcuts (OS-, Performance-, Launcher-, Programm-, Website- und Media-Actions).

<img src="Features/Feature%20Builtin%20Custom%20Tile%20and%20Shortcut%20Actions%201.png" width="420" />
<img src="Features/Feature%20Builtin%20Custom%20Tile%20and%20Shortcut%20Actions%202.png" width="420" />
<img src="Features/Feature%20Builtin%20Custom%20Tile%20and%20Shortcut%20Actions%203.png" width="420" />

### Eigene EXE-Dateien, Skripte & URLs

Eigene Programme (`.exe`), PowerShell-Skripte (`.ps1`) und Website-URLs hinzufügen und auf Kacheln, Controller-Kombos oder den MSI-Button legen.

<img src="Features/Feature%20Add%20Own%20EXE%20Files%20and%20URLs.png" width="420" />

### Front-OEM-Button: Einzel- & Doppelklick-Actions

Dem linken MSI-/Front-OEM-Button getrennte Aktionen für Einzel- und Doppelklick zuweisen.

<img src="Features/Feature%20Front%20OEM%20Button%20Single%20Doubleclick%20Actions.png" width="420" />

### Controller-Tasten neu belegen

Tasten des virtuellen Controllers frei remappen (Tastatur, Maus, Gamepad-Action).

<img src="Features/Feature%20Controls%20Button%20Remapping.png" width="420" />

### Gyro – global oder pro Spiel

Gyro-Steuerung global oder individuell pro Spiel konfigurieren.

<img src="Features/Feature%20Controls%20Gyro%20Global%20or%20per%20Game.png" width="420" />

### Performance-Profile

TDP/Performance-Profile setzen und schnell umschalten.

<img src="Features/Feature%20Performance%20Profiles.png" width="420" />

### Profile pro Spiel

Eigene Profile pro Spiel, die automatisch beim Spielstart greifen.

<img src="Features/Feature%20Per%20Game%20Profiles.png" width="420" />

### Lüftersteuerung

Lüfterkurven und -presets direkt im Widget steuern.

<img src="Features/Feature%20Fan%20Control.png" width="420" />

### Intel Farb- & Display-Einstellungen

Farb- und Display-Einstellungen über die Intel-GPU anpassen.

<img src="Features/Feature%20Intel%20Color%20and%20Display%20Settings.png" width="420" />
<img src="Features/Feature%20Intel%20Color%20and%20Display%20Settings%202.png" width="420" />

### Lossless Scaling Integration

Integration von Lossless Scaling für mehr Bildrate.

<img src="Features/Feature%20Lossless%20Scaling%20Integration.png" width="420" />

### Overlay (horizontal)

Ein konfigurierbares Performance-Overlay – kompakt oder detailliert.

<img src="Features/Feature%20Overley%20Horizontal.png" width="420" />
<img src="Features/Feature%20Overley%20Horizontal%20Detailed.png" width="420" />

</details>

<details>
<summary><strong>Transition to VIIPER UsbIP</strong></summary>

<br/>

> **Nur für Bestands-User von vor 0.1.6:** Diese Transition betrifft ausschließlich User, die ClawTweaks **vor Version 0.1.6** erstmals installiert haben. **Ab 0.1.6 ist VIIPER standardmäßig aktiv** — wer neu installiert, muss nichts tun. Nur wer von einer älteren Version (z. B. 0.1.5 oder 0.1.4) kommt, muss den Umstieg **aktiv** wie unten beschrieben durchführen.

VIIPER ist das neue virtuelle Controller-Backend von ClawTweaks. Statt wie bisher über **ViGEm** läuft der virtuelle Controller über **USB/IP** (`usbip-win2`). Vorteile: der Gerätetyp (Xbox 360, DualShock 4, DualSense Edge, Switch Pro …) lässt sich **im laufenden Betrieb umschalten** (Hot-Swap), und in vielen Fällen ist VIIPER stabiler als ViGEm. Diese Anleitung zeigt den Umstieg Schritt für Schritt.

> **Hinweis:** VIIPER ist noch experimentell und benötigt `usbip-win2`. Ist es nicht installiert, fällt ClawTweaks automatisch auf ViGEm zurück, sodass nie ein toter Controller entsteht. Für die **Xbox Game Bar** ist der **Xbox-360-Typ** am verträglichsten — andere Typen können dort Probleme machen (siehe Schritt 4 & 5).

### 1 · Enable VIIPER via settings tab debug

Den VIIPER-Backend-Schalter im **Settings-Tab** unter **Debug** einschalten. Damit wird das nächste Mal, wenn die Controller-Emulation startet, ein VIIPER-Gerät statt eines ViGEm-Controllers gemountet.

<img src="Transition%20to%20VIIPER%20UsbIP/1_Enable%20VIIPER%20via%20settings%20tab%20debug.png" width="420" />

### 2 · ReEnable Virtual Controller Emulation after auto disabling

Beim Backend-Wechsel wird die Controller-Emulation **einmal automatisch deaktiviert** — so wird der physische Claw-Controller sauber wiederhergestellt, bevor das neue Backend übernimmt. Anschließend die **virtuelle Controller-Emulation wieder einschalten**, damit das VIIPER-Gerät gemountet wird.

<img src="Transition%20to%20VIIPER%20UsbIP/2_ReEnable%20Virtual%20Controller%20Emaulation%20after%20auto%20disabling.png" width="420" />

### 3 · Check Viiper Status

In der **Controller-Status-Karte** prüfen, ob alles läuft: Es sollte „Virtual VIIPER controller active" stehen, der physische MSI-Controller ist via HidHide versteckt, und unten siehst du den aktiven virtuellen Gerätetyp. Ein Wechsel des „Virtual device"-Dropdowns greift sofort (Hot-Swap).

<img src="Transition%20to%20VIIPER%20UsbIP/3_Check%20Viiper%20Status.png" width="420" />

### 4 · Highly Experimental – Don't use other controller types than Xbox 360 / Try Auto Switch

Andere Gerätetypen als **Xbox 360** sind hochexperimentell. In Spielen funktionieren DS4 und Switch Pro meist gut, aber die **Xbox Game Bar** verträgt sie wegen Kompatibilitäts-Eigenheiten nicht zuverlässig (z. B. DS4-RT-Spamming, siehe Schritt 5). Bleibst du bei einem Nicht-Xbox-Typ, hilft der opt-in-Schalter **„Auto-switch to Xbox in Game Bar"**: Er stellt das Gerät nur **solange die Game Bar offen ist** temporär auf Xbox 360 und danach wieder zurück. Bitte testen und Feedback geben.

<img src="Transition%20to%20VIIPER%20UsbIP/4_Highly%20Experimental_Dont%20Use%20other%20Cntroller%20types%20than%20Xbox%20360_TryAuto%20Switc.png" width="420" />

### 5 · Resolve Game Bar RT spamming by detaching the DS4 controller via USBip

Ein DS4-Gerät kann in der Game Bar den **rechten Trigger spammen** (in Spielen tritt das nicht auf). Da VIIPER über USB/IP läuft, lässt sich das Gerät **abkoppeln (detach)**, um das Spamming zu stoppen — alternativ den Auto-Switch aus Schritt 4 nutzen oder direkt bei Xbox 360 bleiben.

<img src="Transition%20to%20VIIPER%20UsbIP/5_Resolve%20Gamebar%20RT%20Spamming%20by%20detaching%20DS4%20Controller%20via%20USBip.png" width="420" />

</details>

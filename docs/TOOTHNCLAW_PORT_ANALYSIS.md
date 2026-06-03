# ToothNClaw (TnC) Feature-Port — Analyse & Vorarbeit

Quelle: https://github.com/BassemMohsen/ToothNClaw (Tooth N Claw), Intel-/Claw-8-optimierter Game-Bar-Tuner.
Dieses Dokument hält fest, **wie** TnC die portierten/geplanten Features umsetzt, damit wir sie schrittweise
übernehmen können. Phase 1 (CPU-Sektion + Tab-Icons) ist umgesetzt; Display/IGCL und das zweite Widget sind
hier nur **analysiert** und werden später bei Bedarf realisiert.

---

## 1. CPU-Sektion (Phase 1 — UMGESETZT)

TnC-Datei: `Tooth.Backend/CpuBoostController.cs` (managed C#, `powrprof.dll`, keine nativen Abhängigkeiten).

### 1.1 CPU Boost Modi
GUID `GUID_PROCESSOR_PERFBOOST_MODE = be337238-0d82-4146-a960-4f3749d470c7` (Subgroup
`54533251-82be-4824-96c1-47b60b740d00`), geschrieben via `PowerWriteAC/DCValueIndex` + `PowerSetActiveScheme`.

Werte (`CpuBoostController.BoostMode`):
| Wert | Modus | Anmerkung |
|------|-------|-----------|
| -1 | UnsupportedAndHidden | nicht verfügbar |
| 0 | Disabled | Boost aus |
| 1 | Enabled | TnC-Default ("Enabled") |
| 2 | Aggressive | unser bisheriges "On" entsprach 2 |
| 3 | Efficient Enabled | |
| 4 | Efficient Aggressive | |
| 5 | Aggressive At Guaranteed | |
| 6 | Efficient Aggressive At Guaranteed | |

→ In ClawTweaks: Toggle bleibt On/Off; bei **On** wählt eine ComboBox den Modus (1–6). Off schreibt 0.

### 1.2 Processor Scheduling Policy (Hetero-Policy)
Drei GUIDs werden gemeinsam gesetzt:
- Heterogeneous policy `7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5`
- Long-thread policy `93b8b6dc-0698-4d1c-9ee4-0644e900c85d`
- Short-thread policy `bae08b81-2d5e-4688-ad6a-13243356654b`

Modi (`SchedulingPolicyMode` → (Policy, ThreadPolicy, ShortThreadPolicy)):
| Modus | Policy | Thread | Short |
|-------|--------|--------|-------|
| AllCoresAuto | 0 | 5 | 5 |
| AllCoresPrefPCore | 1 | 2 | 2 |
| AllCoresPrefECore | 1 | 4 | 4 |
| OnlyPCore | 3 | 1 | 1 |
| OnlyECore | 2 | 3 | 3 |

### 1.3 P-/E-Core Max Frequency (MHz)
- E-Core/All (PROCFREQMAX) `75b0ae3f-bce0-45a7-8c89-c9611c25e100`
- P-Core (PROCFREQMAX1, Efficiency Class 1) `75b0ae3f-bce0-45a7-8c89-c9611c25e101`
- 0 MHz = unbegrenzt.

> Hinweis TnC: nutzt einen 3-s-`Timer`, der MaxFreq + SchedulingPolicy periodisch re-applied, weil Windows
> die Werte bei Power-/Scheme-Events teils zurücksetzt. In ClawTweaks wenden wir beim Profilwechsel + bei
> User-Änderung an; ein Enforce-Timer kann später ergänzt werden, falls Werte „weglaufen".

### 1.4 ClawTweaks-Integration (Phase 1)
- `Shared/Enums/Function.cs`: neue Functions `CpuBoostMode`, `ProcessorSchedulingPolicy`, `MaxPCoreFreqMHz`, `MaxECoreFreqMHz`.
- `Shared/Data/GameProfile.cs`: neue Felder (global **und** per-game), in allen Apply-Pfaden gesetzt.
- Helper: `PowerManager` erweitert (Boost-Mode-Wert, Scheduling-Policy, Freq via vorhandenem `SetCpuFreqLimit`).
- Widget: ausklappbarer **CPU**-Bereich im Performance-Tab oberhalb der Fan-Kurve; Perf-Kärtchen mehrspaltig erweitert.

---

## 2. Display / Intel-Farbe (Phase 2 — NUR ANALYSE, später)

TnC-Dateien: `Tooth.Backend/IGCLBackend.cs` (~2000 Z. P/Invoke), `DisplayController.cs`,
`Tooth/ColorRemaster*.cs`, native `Tooth.Backend/Libraries/IGCL_Wrapper.dll`.

### 2.1 Technik
Farb-Features (Sättigung, Hue, adaptive Schärfung, Kontrast, Gamma) laufen **nicht** über Standard-Windows-APIs,
sondern über die **Intel Graphics Control Library (IGCL)** via einer vorkompilierten nativen C++-Wrapper-DLL
(`IGCL_Wrapper.dll`, abgeleitet aus Intels IGCL-Sample, MIT). `IGCLBackend.cs` ist das P/Invoke-Mapping
(ctl_* Strukturen, `ctl_result_t`, Pixel-Transformation-Blöcke, Sharpness-Filter, etc.).

Relevante IGCL-Bausteine:
- **Pixel Transformation (Pixtx)**: 3x3-Color-Matrix / CSC für Sättigung & Hue, LUT für Gamma/Kontrast
  (`ctl_pixtx_*`, Block-IDs; Fehlercodes `..._INVALID_PIXTX_*`).
- **Sharpness**: `ctl_sharpness_settings_t` (Enable, FilterType `NON_ADAPTIVE`/`ADAPTIVE`, Intensity),
  Caps via `ctl_sharpness_caps_t` → adaptive Schärfung = `CTL_SHARPNESS_FILTER_TYPE_FLAG_ADAPTIVE`.
- Init über `ctlInit` (ctl_init_args_t), Adapter-Enumeration, Display-Output-Handles.

### 2.2 Aufwand / Risiko
- **Native Abhängigkeit**: `IGCL_Wrapper.dll` muss mitgeliefert + in unseren Helper geladen werden
  (Helper läuft elevated, hat GPU-Zugriff). Lizenz MIT — Übernahme von DLL + `IGCLBackend.cs` möglich.
- Treiberabhängig (nur Intel Arc/iGPU mit IGCL-fähigem Treiber, Lunar Lake Xe2 = ok).
- Werte sind **kein** Power-Tweak, sondern echte GPU-Pixel-Transformation → eigener Manager im Helper nötig.

### 2.3 Geplante ClawTweaks-Umsetzung
- Neuer **Display-Tab** rechts neben „Controls". Vorarbeit erledigt: `DisplayNavItem` (Tag `Display`,
  aktuell `Visibility=Collapsed`) liegt bereits in `GamingWidget.xaml`. Zum Aktivieren: Visibility entfernen,
  ScrollViewer/Content mit Tag `Display` ergänzen, in `NavRadioButton_Checked` Routing prüfen.
- Helper: `IntelColorManager` (Wrapper um `IGCLBackend`) + Functions
  `IntelColorSaturation`, `IntelColorHue`, `IntelAdaptiveSharpness`, `IntelDisplayContrast`, `IntelDisplayGamma`.
- Werte per-game/global im `GameProfile` (Pattern wie CPU-Sektion).
- `IGCL_Wrapper.dll` ins Helper-Output + ins Package aufnehmen (csproj `<Content>` + Build-Script).

---

## 3. Zwei-Widget-Architektur (NUR ANALYSE, später)

TnC liefert **zwei** auswählbare Game-Bar-Widgets aus einem Package:
„Tooth N Claw: Performance" und „Tooth N Claw: Color Remaster".

### 3.1 Manifest (`Tooth.Package/Package.appxmanifest`)
Ein `<Application>` mit **zwei** `uap3:AppExtension Name="microsoft.gameBarUIExtension"`-Einträgen:
- Performance: `Id="Tooth.XboxGameBarUI"`, `PublicFolder="GameBar"`,
  `ActivationUri=ms-gamebarwidget://Tooth.XboxGameBarUI`, eigenes `<Icon>`/`<Logo>`.
- Color Remaster: `Id="ColorRemaster.XboxGameBarUI"`, `PublicFolder="ColorRemaster"`,
  `ActivationUri=ms-gamebarwidget://ColorRemaster.XboxGameBarUI`, eigenes Icon.

Jeder `GameBarWidget`-Block hat eigenes `<Window>` (Size/Resize), `HomeMenuVisible`, `FavoriteAfterInstall`,
`SupportsPinning`. Beide teilen denselben `desktop:Extension windows.fullTrustProcess` (gemeinsames Backend),
und denselben Proxy-Stub-Block (`00000355-…` MBM für die Game-Bar-Private-Interfaces).

### 3.2 App-Routing
`App.OnActivated` unterscheidet anhand der Activation-`AppExtensionId`/`ActivationUri`, welche XAML-Page
(`MainPage` vs `ColorRemasterMainPage`) im Game-Bar-Frame angezeigt wird. Beide Pages reden über denselben
Named-Pipe/Backend-Kanal mit dem fullTrust-Helper.

### 3.3 ClawTweaks-Umsetzung (für späteres eigenes Widget)
1. Im Package-Manifest (`XboxGamingBarPackage`/`Package.appxmanifest`) einen **zweiten**
   `microsoft.gameBarUIExtension`-Block ergänzen (eigene Id, eigener `PublicFolder`, eigene `ActivationUri`,
   eigenes Icon). Den bestehenden fullTrustProcess + ProxyStub **wiederverwenden**.
2. Im `App.OnActivated` (bzw. unserem Game-Bar-Activation-Handler) anhand der Id auf eine neue Page routen
   (z. B. `ColorRemasterPage`) statt auf `GamingWidget`.
3. Eigene Public-Folder-Assets (Icon/Logo) anlegen.
4. Pipe/Helper-Kanal wird geteilt — kein zweiter Helper nötig.

> Empfehlung: Erst Display-Features als **Tab** im bestehenden Widget (geringeres Risiko), danach optional in
> ein eigenes „Color Remaster"-Widget auslagern, wenn der Mehrwert (separater Game-Bar-Eintrag) gewünscht ist.

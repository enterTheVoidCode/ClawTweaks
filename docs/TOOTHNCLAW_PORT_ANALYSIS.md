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

## 2. Display / Intel-Farbe (Phase 2)

> **KORREKTUR (nach Code-Prüfung):** Die ursprüngliche Einschätzung „aufwändige native Abhängigkeit" war
> falsch. **Wir liefern `IGCL_Wrapper.dll` bereits aus und laden sie schon** — für den Intel-FPS-Limiter
> (Endurance Gaming), der einwandfrei läuft. Sättigung und adaptive Schärfung sind damit **trivial**
> nachrüstbar: kein neues DLL, kein Rebuild, keine Zusatz-Installation.

### 2.1 Was schon da ist
- `XboxGamingBarHelper/Intel/IGCLBackend.cs` lädt `IGCL_Wrapper.dll` per `LoadLibrary` + `GetProcAddress`
  und bindet aktuell **nur 6** Exporte (Endurance Gaming / FPS-Tier).
- `dumpbin /exports IGCL_Wrapper.dll` zeigt: die DLL exportiert den **kompletten** IGCL-Umfang
  **plus fertige High-Level-Helfer**, u. a.:
  - **Adaptive Sharpness:** `GetSharpnessCaps`, `GetSharpnessSettings`, `SetSharpnessSettings`
    (+ roh `ctlGetSharpnessCaps`, `ctlSetCurrentSharpness`).
  - **Sättigung / Hue:** `SetHueSaturationValues` (Wrapper rechnet die CSC-Matrix intern!),
    `ApplyHueSaturation`, `GenerateHueSaturationMatrix`, `MapSaturation`, `SetCsc`.
  - Bonus: `SetBrightnessContrastGammaValues` / `GetSetGamma` (Kontrast/Gamma, falls später gewünscht).
- IGCL-Runtime = der **installierte Intel-Grafiktreiber** (Lunar Lake Xe2). Kein separater Installer —
  bewiesen dadurch, dass der FPS-Limiter über genau diese DLL bereits funktioniert.

### 2.2 Exakte Signaturen (aus TnC `Tooth.Backend/IGCLBackend.cs` verifiziert)
```csharp
// Adaptive Sharpness
struct ctl_sharpness_settings_t { uint Size; byte Version; bool Enable;
                                  ctl_sharpness_filter_type_flag_t FilterType; float Intensity; }
// FilterType: NON_ADAPTIVE=1, ADAPTIVE=2
ctl_result_t GetSharpnessSettings(ctl_device_adapter_handle_t hDevice, uint displayIdx, ref ctl_sharpness_settings_t s);
ctl_result_t SetSharpnessSettings(ctl_device_adapter_handle_t hDevice, uint displayIdx, ctl_sharpness_settings_t s);
// Apply adaptive: s.Enable=true; s.FilterType=ADAPTIVE; s.Intensity=<0..100>; Set; dann Get zum Verifizieren.

// Sättigung / Hue (DLL macht die Matrix intern)
ctl_result_t SetHueSaturationValues(ctl_device_adapter_handle_t hDevice, double Hue, double Saturation);
// Default/neutral: Hue=0, Saturation=1.0. Sättigung als Multiplikator (TnC-Slider-Range prüfen, grob 0..4).
```

### 2.3 Aufwand
- **Adaptive Sharpness:** ~30–40 Z. C# (3 Delegates + 1 Struct + 1 Enum in `IGCLBackend.cs`, eine
  `SetAdaptiveSharpness(intensity)`-Methode) + Toggle/Slider. **Klein.** Hinweis: pro Display-Output
  (`displayIdx`), i. d. R. das interne eDP-Panel = 0; ggf. Display-Outputs enumerieren.
- **Sättigung:** ~15 Z. C# (1 Delegate `SetHueSaturationValues` + `SetSaturation(value)`) + Slider.
  **Sehr klein.** Hue lassen wir auf 0 (nicht zwingend nötig).
- **Persistenz:** wie CPU-Sektion — Felder im `GameProfile` (global + per-game), Apply-Pfade, Function-Enum.
- **Reset/Restore:** CSC/Sharpness sind global (Desktop-weit) und persistent bis zurückgesetzt → „Reset"
  (Saturation=1.0, Sharpness off) anbieten und beim Helper-Start den gespeicherten Wert re-applien.

### 2.4 Geplante ClawTweaks-Umsetzung
- Neuer **Display-Tab** rechts neben „Controls". Vorarbeit erledigt: `DisplayNavItem` (Tag `Display`,
  `Visibility=Collapsed`) liegt bereits in `GamingWidget.xaml`. Aktivieren: Visibility entfernen,
  ScrollViewer/Content mit Tag `Display` ergänzen, Routing in `NavRadioButton_Checked` prüfen.
- Helper: `IGCLBackend` um Sharpness-/Saturation-Exporte erweitern; ein kleiner `IntelDisplayManager`
  oder direkt in `IntelGpuManager` integrieren. Functions z. B. `IntelAdaptiveSharpness` (int 0..100,
  0=off) und `IntelColorSaturation` (int, 100=neutral).
- DLL bereits im Package (kein csproj/Build-Script-Change nötig).

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

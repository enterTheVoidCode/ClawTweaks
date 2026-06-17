# Konzept: Spielerkennung — Game-Bar-only (umgesetzt)

Status: **Umgesetzt & verifiziert** (Build 0.1.5.537) · Stand: 2026-06-17
Betroffen: `XboxGamingBarHelper\Systems\SystemManager.cs` (Kern), `XboxGamingBar\GamingWidget.xaml`
(UI). ViGEm / HidHide / Viiper **unberührt**.

> Hinweis: Ein früherer Entwurf dieses Dokuments beschrieb einen **profil-/pfadbasierten** Ansatz
> (Step 1–5). Der wurde verworfen — siehe „Historie" am Ende. Maßgeblich ist der hier beschriebene
> Game-Bar-only-Stand.

---

## 1. Leitidee

Die Spielerkennung ruht **ausschließlich** auf zwei Signalen:

1. **Xbox Game Bar** (`XboxGameBarAppTargetTracker` → `target.IsGame`) — der autoritative
   Spiel-Klassifizierer von Windows. Liefert `DisplayName`, `AumId`, `TitleId`.
2. **RTSS** (FPS > 0) — „rendert Frames" = Spiel, als Sekundärsignal.

Alle früheren Heuristiken (Fenstertitel als Erkennung, „jedes Vordergrundfenster", Emulator-
Whitelist, Custom-Pfade, profilbasierte Pfad-Erkennung) sind **deaktiviert**. Fensternamen
interessieren nicht — ein Spiel ist nur, was Game Bar oder RTSS als solches melden. Wird nichts
erkannt, bleibt das **globale** Profil aktiv (kein Sonderfall nötig).

Begründung: In der Praxis trackt die Game Bar praktisch alles — Steam-Spiele, Emulatoren, alte
Spiele und sogar RetroArch. RTSS fängt den Rest (rendernde Apps, die die Game Bar nicht klassifiziert)
über die FPS ab.

---

## 2. Erkennung (`SystemManager.GetRunningGame`)

Ein Fenster gilt als Spiel, wenn **(a) ODER (b)**:

- **(a) Game Bar `TrackedGame`** ist gültig und matcht ein Fenster (früher Early-Return-Block bzw.
  der Fallback-Match in der Hauptschleife). Kein FPS nötig.
- **(b) RTSS FPS > 0** für das Fenster.

**Deaktiviert (auskommentiert, reversibel):**
- Profil-Pfad-Match (`TryMatchProfileByPath`, ehem. Step 1/1b)
- Titel-Normalisierung (`NormalizeGameTitle` / `TrailingPercentRegex`, ehem. Step 2)
- Custom-Game-Pfade (`ProfileCustomGamePath`-Block)
- „GamesOnly = AUS → jedes Vordergrundfenster"
- Emulator-`GameProcesses`-Whitelist

`preferExe`/`gamesOnly` sind hartkodiert (`preferExe=false`, `gamesOnly=true`); die Properties
`ProfileMatchByExe`/`ProfileGamesOnly` bleiben (synchronisiert) bestehen, werden aber ignoriert.

---

## 3. Identität (welcher Name keyt die Profile) — `ResolveGameBarName`

| Quelle | Identität |
|--------|-----------|
| Game-Bar-getracktes Spiel (Standard) | **`DisplayName`** (stabil), Fallback Fenstertitel → EXE |
| **RetroArch** (`retroarch.exe`, Ausnahme) | **Fenstertitel** (enthält den Core → Per-Core-Profile, z. B. „RetroArch Gambatte …"); Fallback `DisplayName` |
| RTSS-only (kein TrackedGame) | **EXE-Name** (kein Fenstertitel) |

**RetroArch-Sonderweg:** Die Game Bar meldet nur das Programm („RetroArch"); der Core steht nur im
Fenstertitel. Da der User pro Core eigene (Farb-)Profile will, nutzt `retroarch.exe` bewusst den
Titel. Übergänge „RetroArch ⇄ RetroArch <Core>" sind echte Zustandswechsel (Menü ⇄ Core), kein Churn.

**Gap-Überbrückung (RTSS-Pfad):** Verliert die Game Bar das Ziel für 1–2 Ticks, würde RTSS auf den
nackten EXE-Namen („retroarch") fallen. Stattdessen wird die **zuletzt veröffentlichte Identität
gehalten**, solange es derselbe Prozess(-Pfad) ist → kein Flackern.

---

## 4. UI

Die „Game Detection"-Karte (Toggles *Prefer executable* / *Games only*) ist im Setup-Tab auf
`Visibility="Collapsed"` gesetzt — verschwunden, aber die Property-Bindings bleiben intakt.

---

## 5. Bewusst NICHT umgesetzt

- **„Nicht erkannt"-Info-Notification** (RTSS sieht ein Spiel, Game Bar trackt es nicht): verworfen,
  weil die Game Bar in der Praxis alles erkennt. Bei Bedarf nachrüstbar — Helper müsste die
  `RunningGame` mit `RecognizedByGameBar` markieren, Widget zeigt die Info über
  `ShowProfileNotificationAsync`. Testbar nur erzwungen (Game-Bar-App-Target-Permission ausschalten).

---

## 6. Verifikation (auf Gerät, 0.1.5.537)

- Drei Steam-Spiele (RE2, Blasphemous 2, Hollow Knight Silksong): über Game Bar erkannt, Identität =
  `DisplayName` (== Titel dort), **Alt-Profile matchen weiter**, auch nach Neustart.
- RetroArch: Menü „RetroArch", mit geladenem Core „RetroArch Gambatte v0.5.0-netlink …" → eigenes
  Core-Profil greift, auch nach Neustart. Kein „retroarch"-EXE-Flackern mehr (Gap-Bridge wirkt).
- Kein Re-Apply-Sturm; Logs ruhig.

Log: `%LOCALAPPDATA%\Packages\MSIClaw.ClawTweaks_7eszav2039cvc\LocalCache\Local\helper_*.log`

---

## Historie (verworfener Ansatz)

Ursprünglich war ein **profil-/pfadbasierter** Umbau geplant (Step 1: Perf-Profil per EXE-Pfad;
Step 1b: Controller-Profile per Pfad via Widget→Helper-Map; Step 2: Titel-`%`-Strip; Step 3: Gate
„unbekannt ⇒ nichts"). Steps 1/1b/2 wurden gebaut (0.1.5.532/533/535), dann aber verworfen, als die
Logs zeigten, dass die Game Bar ohnehin **alle** Testspiele trackt (inkl. RetroArch) und der
profil-/titelbasierte Pfad gar nicht zum Tragen kam. Der Code dieser Steps ist in `SystemManager.cs`
(und das Step-1b-Plumbing in `SettingsManager`/`Program.PipeHandlers`/`GamingWidget.ControllerProfileStorage`)
**auskommentiert** erhalten — falls man einen pfadbasierten Fallback je wieder reaktivieren will.

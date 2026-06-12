# Test-Release Format (ClawTweaks)

Standard für **experimentelle Test-Releases** (Prereleases zum Verifizieren neuer Features,
z. B. des In-App-Updaters). Immer so anwenden.

## Eckdaten
- **Tag:** die volle 4-teilige Build-Version, z. B. `0.1.4.420` (NICHT die 3-teilige Marketing-Version).
- **Titel:** `Experimental Test Build (<version>)` — z. B. `Experimental Test Build (0.1.4.420)`.
  (Kein `vX.Y.Z` im Titel.)
- **Prerelease:** **ja** (`--prerelease`). Dann zeigt das Widget den Eintrag als **EXPERIMENTAL BUILD**.
- **Asset:** die `ClawTweaks_<version>_Installer.zip` aus `Build\`.
- **Target:** der Commit-SHA, aus dem gebaut wurde (für die raw-URLs der Screenshots).

## Body-Reihenfolge (genau so)
1. **📍 Center M needed as a base** (ganz oben, als Blockquote).
2. **`> [!WARNING]` Experimental test build** — kurzer Hinweis, dass es ein Prerelease ist.
3. `---`
4. **## Installation** — Boilerplate (aus dem letzten Release übernehmen, OHNE dessen „What's new"):
   - `> [!IMPORTANT]` Changed installation (first-time users only).
   - **Setup process** Schritte. WICHTIG: Install-Befehl IMMER als Einzeiler, NICHT „Run with PowerShell":
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\Install.ps1
     ```
     (Hinweis: gilt nur für diesen Lauf, kein `Set-ExecutionPolicy` nötig; umgeht Mark-of-the-Web.)
   - `> [!NOTE]` Hinweise:
     - „No controller input?" → **Start** 3 s halten (HW-Mouse/Controller-Switch).
     - „Doubled inputs with controller emulation active?" → in **Steam** den **Xbox controller driver
       with extended feature support** prüfen (Steam → Settings → Controller) und **entfernen/deaktivieren**.
     - „Reordering widgets:" → entfernen und in gewünschter Reihenfolge neu hinzufügen.
5. `---`
6. **## What's new** — GANZ UNTEN. Pro Feature ein `###`-Abschnitt + Screenshot via raw-URL.

## Screenshots / raw-URLs
- Screenshots unter `Doku/Releases/<release>/` ablegen und **committen + pushen**.
- Im Body referenzieren über:
  `https://raw.githubusercontent.com/enterTheVoidCode/ClawTweaks/<COMMIT-SHA>/Doku/Releases/<release>/<Datei>.png`
  - Leerzeichen im Dateinamen als `%20` kodieren.
  - **Commit-SHA** verwenden (stabil), nicht den Branchnamen.
- Breiten: Screenshots vom ganzen Tab `width="420"`; schmale/Tile-Ausschnitte `width="280"`.

## gh-Befehl (Vorlage)
```powershell
gh release create "<version>" `
  --repo enterTheVoidCode/ClawTweaks `
  --target <COMMIT-SHA> `
  --title "Experimental Test Build (<version>)" `
  --notes-file "<pfad-zu-notes.md>" `
  --prerelease `
  "C:\...\Build\ClawTweaks_<version>_Installer.zip"
```
Text später ändern: `gh release edit "<version>" --repo enterTheVoidCode/ClawTweaks --notes-file <...>`
Löschen nach dem Test: `gh release delete "<version>"`.

## Hinweise
- Prereleases sind öffentlich sichtbar (für alle als „experimental"). Nach dem Test löschen, wenn nicht
  als regulärer Build gedacht.
- Für ein **reguläres** Release stattdessen die 3-teilige Version (`-ReleaseVersion 0.1.5`) bauen, NICHT
  prerelease, und Titel/Tag entsprechend (`v0.1.5`).

> Referenzbeispiel, das exakt diesem Format folgt: Release `0.1.4.420`.

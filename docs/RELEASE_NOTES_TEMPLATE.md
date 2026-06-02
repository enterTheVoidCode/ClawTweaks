# Release Notes — Style Guide & Template

Keep every GitHub release consistent by following this template. Title format:
`ClawTweaks vMAJOR.MINOR.BUILD.REVISION` (the version baked into the installer ZIP).

Attach the installer ZIP (`Build/ClawTweaks_<version>_Installer.zip`) as the release asset.

---

## Structure

Two top-level sections, always in this order:

### 1. Installation
- One-line pre-requisite warning (exit competing controller tools).
- **Setup process:** a numbered list, kept stable release-to-release. Only change a
  step when the actual flow changes. Current canonical steps are 1–9 (see template
  below). Step 9 documents the default **Start + Select** Mouse/Controller switch.
- An **important note** about the MSI Claw hardware mouse/controller switch
  (hold **Start** ≥ 3 s) when the controller appears dead and only the mouse moves.
- **Recommended tips:** short, optional. Move detailed how-tos (e.g. per-game
  profiles) into the FAQ instead of the release notes.
- Closing line about the self-signed certificate.

### 2. What's New
Only list things the **user** actually notices or benefits from. Skip internal
refactors, build-system changes, and code cleanups.

- **New features** — 1–2 sentences each, a little more detail. Explain what it does
  and how to use it (e.g. the Launcher mapping on the left OEM button).
- **Improvements** — one line each, user-facing behavioural improvements.
- **Fixes** — brief, grouped, one line each. Summarize; don't enumerate every commit.

---

## Template

```
## Installation

Before installing, exit any competing controller management tools like Handheld
Companion or Winhanced to avoid conflicts.

**Setup process:**
1. Extract the ZIP file to your chosen directory
2. Run Install.bat with administrator privileges
3. Confirm the UAC prompt
4. Enable Gaming in Xbox Game Bar (Win + G)
5. Locate and position the ClawTweaks widget on the left side
6. Confirm the background UAC prompt on first launch
7. Disable MSI Center M in the Main tab to unlock all features
8. Enable Virtual Controller & Mouse in the Controls tab
9. By default you can switch between Mouse and Controller mode with Start + Select

> No controller input? If nothing on the controller works and the left stick only
> moves the mouse cursor, the MSI Claw's hardware mouse/controller switch is in mouse
> mode. Hold the Start button for at least 3 seconds to switch it back.

**Recommended tips:**
- Reorder widgets by removing and re-adding them in desired sequence

The installer uses a self-signed certificate (CN=ClawTweaks Dev) with automatic trust handling.

## What's New

**New features**
- ...

**Improvements**
- ...

**Fixes**
- ...
```

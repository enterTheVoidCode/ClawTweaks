# Ori and the Will of the Wisps — crash with ClawTweaks (open)

Rough notes from on-device diagnosis 2026-07-12. Enough for a dev to pick it up.

## Symptom
- Ori (Steam, `oriwotw.exe`) crashes while ClawTweaks is running. Reproduced by two users incl. on our device.
- With HandheldCompanion + RTSS the game always ran fine → something CTW-specific.

## What the logs show (session 21:11–21:12)
- `helper_2026-07-12_21.log`: game detected 21:11:28 (PID 11464), ran clean at FPS 64 for ~43 s.
- `widget_2026-07-12_21.log`: **21:12:11.2 window goes "(Keine Rückmeldung)" / Not Responding**, process gone 21:12:12.
- Crash correlates with an **Xbox Game Bar open (21:12:00) + close (21:12:07)** over the running game, NOT with game start.
- **No helper/widget exception** — the game itself hangs.

## Ruled out
- **Viiper virtual controller**: user reproduced the crash in **HW Controller mode** too → emulation backend is not the cause.

## Prime suspect: forced CPU affinity ("CPU Core Config")
- Inherited GoTweaks/Legion feature. Saved setting = **3 active P-cores** (of 4). Log on every start: `Core configuration set: 3/4 P-Cores, 4/4 E-Cores`.
- On each detected game CTW calls `SetProcessAffinityMask` on the game process → `mask=0xF7` on the 258V (4P+4E, no HT), i.e. **cuts the 4th P-core**. Ori got this at 21:11:28.
- Code: `XboxGamingBarHelper/Systems/SystemManager.cs` — `ApplyCoreConfiguration` (~1463), `ApplyAffinityToRunningGame` (~1524), `ApplyAffinityToProcess` (`SetProcessAffinityMask`, log at ~1581). When all cores are enabled it logs `All cores enabled, no affinity change needed (anticheat safe)` and skips the game entirely.
- Caveat: cutting one P-core hard-crashing a game is untypical (usually just slower), but a forced external affinity write on a running engine threadpool is a known hang candidate. It's the only CTW-specific intervention on the game process that is independent of controller mode → fits "also crashes in HW mode".

## Next step (config-only test, no code)
1. Set **Performance → Advanced → CPU Core Config → all cores (4P+4E)**, retry Ori.
   - No crash → affinity was it → fix = disable/soften this feature on the Claw.
   - Still crashes → it's the Game-Bar-overlay / focus-emulation-transition path (see below); investigate there.
2. Secondary path if affinity is cleared: opening Game Bar over the fullscreen game triggered
   `[GameBarAutoNav]` RB×2+DpadDown injection + widget "Clearing stuck LT/RT tab-nav state (focus/emulation transition)" right before the hang.

Tracked as task #102.

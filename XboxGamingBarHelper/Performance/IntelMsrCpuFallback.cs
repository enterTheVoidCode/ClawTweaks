using System;
using System.Diagnostics;
using LibreHardwareMonitor.PawnIo;
using Microsoft.Win32;
using NLog;

namespace XboxGamingBarHelper.Performance
{
    /// <summary>
    /// Direct-MSR fallback for CPU package temperature and power on Intel CPUs that
    /// LibreHardwareMonitor does not recognise.
    ///
    /// Why: LHM's IntelCpu configures TjMax / RAPL energy units from a switch on the CPU
    /// model ID. On unknown models it creates NO Temperature/Power sensors at all —
    /// observed on the MSI Claw 8 AI+ EX (Panther Lake, family 6 model 0xCC "Intel Arc G3
    /// Extreme", LHM 0.9.6): the CPU exposed only perf-counter Load sensors and a dead
    /// Voltage sensor, so the OSD/widget showed "--" for CPU temp and power
    /// (docs/hardware/CLAW8_EX_PORT_LOG.md, 2026-07-05).
    ///
    /// The registers themselves are model-independent across modern Intel Core parts
    /// (Intel SDM vol. 4):
    ///   MSR_TEMPERATURE_TARGET   (0x1A2) bits 23:16 = TjMax (°C)
    ///   IA32_PACKAGE_THERM_STATUS(0x1B1) bits 22:16 = digital readout (°C below TjMax)
    ///   MSR_RAPL_POWER_UNIT      (0x606) bits 12:8  = energy status unit (J = 1/2^ESU)
    ///   MSR_PKG_ENERGY_STATUS    (0x611) bits 31:0  = cumulative package energy counter
    ///
    /// MSR access rides LHM's own public PawnIO binding (<see cref="IntelMsr"/>), i.e. the
    /// same signed driver the rest of the sensor stack uses — no extra kernel component.
    /// If PawnIO is unavailable or any probe read fails, the fallback disables itself
    /// permanently for the process.
    ///
    /// A2VM-safety: callers only invoke <see cref="TryFill"/> for sensor slots LHM left at
    /// -1, and initialisation only ever happens in that case, so on devices where LHM
    /// recognises the CPU (A2VM/Lunar Lake) this class never activates at all.
    /// </summary>
    internal sealed class IntelMsrCpuFallback
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const uint MSR_TEMPERATURE_TARGET    = 0x1A2;
        private const uint IA32_PACKAGE_THERM_STATUS = 0x1B1;
        private const uint MSR_RAPL_POWER_UNIT       = 0x606;
        private const uint MSR_PKG_ENERGY_STATUS     = 0x611;

        private IntelMsr msr;
        private bool initAttempted;
        private bool available;

        private float tjMax;                 // °C from MSR 0x1A2
        private float energyUnitJoules;      // J per energy-counter tick from MSR 0x606
        private uint lastEnergyRaw;          // last 32-bit energy counter sample
        private long lastEnergyTimestamp;    // Stopwatch ticks of that sample
        private float lastPowerWatts = -1f;  // last computed package power

        /// <summary>
        /// Fills <paramref name="temperatureC"/> / <paramref name="powerW"/> with MSR-derived
        /// values. Either out value is -1 when unavailable. Returns false when the fallback
        /// is (or became) unavailable on this machine — callers should stop asking.
        /// </summary>
        public bool TryFill(out float temperatureC, out float powerW)
        {
            temperatureC = -1f;
            powerW = -1f;

            if (!initAttempted)
                Initialize();
            if (!available)
                return false;

            try
            {
                // Package temperature: TjMax − digital readout.
                if (msr.ReadMsr(IA32_PACKAGE_THERM_STATUS, out ulong therm))
                {
                    float readout = (therm >> 16) & 0x7F;
                    float t = tjMax - readout;
                    if (t > 0 && t < 120)
                        temperatureC = t;
                }

                // Package power: delta of the cumulative energy counter over wall time.
                // First sample only records the baseline; the OSD gets a value one
                // update-tick (~1 s) later. The 32-bit counter wraps (~minutes at handheld
                // wattage) — unchecked uint subtraction handles a single wrap correctly.
                if (msr.ReadMsr(MSR_PKG_ENERGY_STATUS, out ulong energy))
                {
                    uint raw = (uint)(energy & 0xFFFFFFFF);
                    long now = Stopwatch.GetTimestamp();
                    if (lastEnergyTimestamp != 0)
                    {
                        float seconds = (now - lastEnergyTimestamp) / (float)Stopwatch.Frequency;
                        if (seconds > 0.1f)
                        {
                            uint deltaTicks = unchecked(raw - lastEnergyRaw);
                            float watts = deltaTicks * energyUnitJoules / seconds;
                            // Plausibility: handhelds live well under 100 W; a huge value
                            // means the counter wrapped more than once or the read glitched.
                            if (watts >= 0 && watts < 200)
                                lastPowerWatts = watts;
                            lastEnergyRaw = raw;
                            lastEnergyTimestamp = now;
                        }
                        // else: called again too soon — keep the previous sample and value.
                    }
                    else
                    {
                        lastEnergyRaw = raw;
                        lastEnergyTimestamp = now;
                    }
                    powerW = lastPowerWatts;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[MsrFallback] read failed, disabling: {ex.Message}");
                available = false;
                return false;
            }

            return true;
        }

        private void Initialize()
        {
            initAttempted = true;
            try
            {
                // Intel-only registers — never activate on AMD.
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (!string.Equals(key?.GetValue("VendorIdentifier") as string, "GenuineIntel", StringComparison.Ordinal))
                    {
                        Logger.Info("[MsrFallback] non-Intel CPU — fallback not applicable");
                        return;
                    }
                }

                msr = new IntelMsr();   // loads the PawnIO MSR module; throws if PawnIO is absent

                if (!msr.ReadMsr(MSR_TEMPERATURE_TARGET, out ulong target))
                {
                    Logger.Info("[MsrFallback] MSR_TEMPERATURE_TARGET read failed — fallback unavailable");
                    return;
                }
                tjMax = (target >> 16) & 0xFF;
                if (tjMax < 50 || tjMax > 120)
                {
                    Logger.Info($"[MsrFallback] implausible TjMax {tjMax} — fallback unavailable");
                    return;
                }

                if (!msr.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unit))
                {
                    Logger.Info("[MsrFallback] MSR_RAPL_POWER_UNIT read failed — fallback unavailable");
                    return;
                }
                int esu = (int)((unit >> 8) & 0x1F);
                energyUnitJoules = 1.0f / (1 << esu);

                available = true;
                Logger.Info($"[MsrFallback] active: TjMax={tjMax} °C, energy unit={energyUnitJoules:G4} J (ESU={esu}) — filling CPU temp/power for LHM-unrecognised Intel CPU");
            }
            catch (Exception ex)
            {
                Logger.Info($"[MsrFallback] unavailable ({ex.Message}) — CPU temp/power stay at N/A");
            }
        }
    }
}

using Microsoft.Win32;
using NLog;
using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// Best-effort physical controller suppression using HidHide CLI.
    /// This avoids double-input by hiding handheld physical devices while forwarding to a virtual controller.
    /// </summary>
    internal sealed class ControllerSuppressionManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object syncRoot = new object();
        private readonly HashSet<string> hiddenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string cliPath;
        private bool appRegistered;
        private bool cloakingEnabled;

        public bool IsAvailable => !string.IsNullOrEmpty(cliPath);

        public ControllerSuppressionManager()
        {
            cliPath = ResolveCliPath();
            if (!string.IsNullOrEmpty(cliPath))
            {
                Logger.Info($"HidHide CLI detected at '{cliPath}'");
            }
            else
            {
                Logger.Warn("HidHide CLI not found. Physical controller suppression disabled.");
            }
        }

        public bool Enable(DeviceType deviceType)
        {
            lock (syncRoot)
            {
                if (string.IsNullOrEmpty(cliPath))
                {
                    return false;
                }

                EnsureApplicationRegistered();

                List<string> ids = EnumerateDeviceInstanceIds(deviceType).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (ids.Count == 0)
                {
                    Logger.Warn($"HidHide suppression: no matching physical device IDs found for {deviceType}");
                    return false;
                }

                bool changed = false;
                foreach (string id in ids)
                {
                    if (hiddenDeviceIds.Contains(id))
                    {
                        continue;
                    }

                    if (RunCli($"--dev-hide \"{id}\""))
                    {
                        hiddenDeviceIds.Add(id);
                        changed = true;
                    }
                }

                if (!cloakingEnabled)
                {
                    RunCli("--cloak-on");
                    cloakingEnabled = true;
                }

                if (changed)
                {
                    Logger.Info($"HidHide suppression enabled for {hiddenDeviceIds.Count} device path(s)");
                }

                return hiddenDeviceIds.Count > 0;
            }
        }

        public void Disable()
        {
            lock (syncRoot)
            {
                if (string.IsNullOrEmpty(cliPath) || hiddenDeviceIds.Count == 0)
                {
                    return;
                }

                foreach (string id in hiddenDeviceIds.ToArray())
                {
                    RunCli($"--dev-unhide \"{id}\"");
                }

                hiddenDeviceIds.Clear();
                Logger.Info("HidHide suppression disabled for previously hidden device path(s)");
            }
        }

        private static string ResolveCliPath()
        {
            string fromRegistry = ReadCliPathFromRegistry(RegistryView.Registry64)
                                  ?? ReadCliPathFromRegistry(RegistryView.Registry32);
            if (!string.IsNullOrEmpty(fromRegistry))
            {
                return fromRegistry;
            }

            List<string> fallbackCandidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
            };

            return fallbackCandidates.FirstOrDefault(File.Exists);
        }

        private static string ReadCliPathFromRegistry(RegistryView view)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide");
                string installPath = key?.GetValue("Path") as string;
                if (string.IsNullOrWhiteSpace(installPath))
                {
                    return null;
                }

                string candidate = installPath;
                if (Directory.Exists(candidate))
                {
                    candidate = Path.Combine(candidate, "x64", "HidHideCLI.exe");
                }

                return File.Exists(candidate) ? candidate : null;
            }
            catch
            {
                return null;
            }
        }

        private void EnsureApplicationRegistered()
        {
            if (appRegistered || string.IsNullOrEmpty(cliPath))
            {
                return;
            }

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return;
                }

                if (RunCli($"--app-reg \"{exePath}\""))
                {
                    appRegistered = true;
                    Logger.Info("HidHide application registration completed for helper executable");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"HidHide app registration failed: {ex.Message}");
            }
        }

        private static IEnumerable<string> EnumerateDeviceInstanceIds(DeviceType deviceType)
        {
            switch (deviceType)
            {
                case DeviceType.LegionGo:
                case DeviceType.LegionGo2:
                    return QueryPnpDeviceIds(0x17EF, 0x6182, 0x6183, 0x6184, 0x6185, 0x61EB, 0x61EC, 0x61ED, 0x61EE);

                case DeviceType.GPDWin5:
                    return QueryPnpDeviceIds(0x2F24, 0x0137, 0x0135);

                default:
                    return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> QueryPnpDeviceIds(int vendorId, params int[] productIds)
        {
            HashSet<string> results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int pid in productIds)
            {
                string query = $"SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'HID\\\\VID_{vendorId:X4}&PID_{pid:X4}%'";
                try
                {
                    using ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                    foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
                    {
                        string id = obj["PNPDeviceID"] as string;
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            results.Add(id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"HidHide query failed for VID_{vendorId:X4}&PID_{pid:X4}: {ex.Message}");
                }
            }

            return results;
        }

        private bool RunCli(string arguments)
        {
            if (string.IsNullOrEmpty(cliPath))
            {
                return false;
            }

            try
            {
                using Process process = new Process();
                process.StartInfo.FileName = cliPath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                if (!process.Start())
                {
                    Logger.Warn($"HidHide command failed to start: {arguments}");
                    return false;
                }

                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(); } catch { }
                    Logger.Warn($"HidHide command timeout: {arguments}");
                    return false;
                }

                string stderr = process.StandardError.ReadToEnd();
                if (process.ExitCode != 0)
                {
                    Logger.Warn($"HidHide command failed (exit {process.ExitCode}): {arguments} {stderr}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"HidHide command exception ({arguments}): {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            Disable();
        }
    }
}

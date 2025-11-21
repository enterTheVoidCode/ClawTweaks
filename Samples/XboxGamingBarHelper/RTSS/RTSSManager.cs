using System;
using System.IO;
using System.Diagnostics;
using RTSSSharedMemoryNET;
using Shared.Enums;
using Shared.Utilities;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS.OSDItems;

namespace XboxGamingBarHelper.RTSS
{
    internal class RTSSManager : OnScreenDisplayManager
    {
        // START IOnScreenDisplayProvider implementation
        public override bool IsInstalled => RTSSHelper.IsInstalled(out _);
        // END IOnScreenDisplayProvider implementation

        private const string OSDSeparator = " <C=6E006A>|<C> ";
        private const string OSDBackground = "<P=0,0><L0><C=80000000><B=0,0>\b<C>";
        private const string OSDAppName = "Xbox Gaming Bar OSD";

        private OSD rtssOSD;
        private readonly OSDItem[] osdItems;

        public RTSSManager(PerformanceManager performanceManager, AppServiceConnection connection) : base(connection)
        {
            
            osdItems = new OSDItem[]
            {
                new OSDItemFPS(),
                new OSDItemBattery(performanceManager.BatteryLevel, performanceManager.BatteryDischargeRate, performanceManager.BatteryChargeRate, performanceManager.BatteryRemainingTime),
                new OSDItemMemory(performanceManager.MemoryUsage, performanceManager.MemoryUsed),
                new OSDItemCPU(performanceManager.CPUUsage, performanceManager.CPUClock, performanceManager.CPUWattage, performanceManager.CPUTemperature),
                new OSDItemGPU(performanceManager.GPUUsage, performanceManager.GPUClock, performanceManager.GPUWattage, performanceManager.GPUTemperature),
            };
        }

        public override void Update()
        {
            base.Update();
            
            if (!RTSSHelper.IsInstalled(out string installDir))
            {
                Logger.Debug("Rivatuner Statistics Server is not installed.");
                return;
            }

            var isRunning = RTSSHelper.IsRunning();
            string executablePath = Path.Combine(installDir, $"{RTSSHelper.RTSS_FILE_NAME}.exe");
            if (!isRunning && !File.Exists(executablePath))
            {
                Logger.Debug("Rivatuner Statistics Server is installed but the exe file is not found.");
                applicationState = ApplicationState.NotInstalled;
                return;
            }

            if (onScreenDisplayLevel == 0)
            {
                if (rtssOSD != null)
                {
                    rtssOSD.Update(string.Empty);
                    rtssOSD.Dispose();
                    rtssOSD = null;
                }

                /*var rtssProcess = RTSSHelper.GetProcess();
                if (rtssProcess != null && SettingsManager.GetInstance().AutoStartRTSS)
                {
                    try
                    {
                        Logger.Info("Stopping Rivatuner Statistics Server..");
                        rtssProcess.Kill();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to stop Rivatuner Statistics Server.");
                    }
                }
                rtssState = RivatunerStatisticsServerState.NotRunning;*/

                return;
            }

            if (!isRunning)
            {
                if (applicationState == ApplicationState.Starting)
                {
                    Logger.Info("Starting Rivatuner Statistics Server..");
                }
                else
                {
                    applicationState = ApplicationState.Starting;
                    try
                    {
                        Logger.Info("Start Rivatuner Statistics Server.");
                        Process.Start(executablePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to start Rivatuner Statistics Server.");
                        applicationState = ApplicationState.NotRunning;
                    }
                }
                return;
            }

            applicationState = ApplicationState.Running;

            if (rtssOSD == null)
            {
                rtssOSD = new OSD(OSDAppName);
            }

            string osdString = OSDBackground;
            for (int i = 0; i < osdItems.Length; i++)
            {
                var osdItemString = osdItems[i].GetOSDString(onScreenDisplayLevel);
                if (string.IsNullOrEmpty(osdItemString))
                    continue;

                if (i == 0)
                {
                    osdString += osdItemString;
                }
                else
                {
                    osdString += OSDSeparator + osdItemString;
                }
            }

            rtssOSD.Update(osdString);
        }
    }
}

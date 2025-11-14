using RTSSSharedMemoryNET;
using Shared.Enums;
using Shared.Utilities;
using System;
using System.Diagnostics;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS.OSDItems;
using XboxGamingBarHelper.Settings;

namespace XboxGamingBarHelper.RTSS
{
    internal class RTSSManager : OnScreenDisplayManager
    {
        private const string OSDSeparator = " <C=6E006A>|<C> ";
        private const string OSDBackground = "<P=0,0><L0><C=80000000><B=0,0>\b<C>";
        private const string OSDAppName = "Xbox Gaming Bar OSD";

        private OSD rtssOSD;
        private readonly OSDItem[] osdItems;

        private readonly RTSSInstalledProperty rtssInstalled;
        public RTSSInstalledProperty RTSSInstalled
        {
            get { return rtssInstalled; }
        }

        private RivatunerStatisticsServerState rtssState;

        public RTSSManager(PerformanceManager performanceManager, AppServiceConnection connection) : base(connection)
        {
            
            rtssInstalled = new RTSSInstalledProperty(this);
            osdItems = new OSDItem[]
            {
                new OSDItemFPS(),
                new OSDItemBattery(performanceManager.BatteryLevel, performanceManager.BatteryDischargeRate, performanceManager.BatteryChargeRate, performanceManager.BatteryRemainingTime),
                new OSDItemMemory(performanceManager.MemoryUsage, performanceManager.MemoryUsed),
                new OSDItemCPU(performanceManager.CPUUsage, performanceManager.CPUClock, performanceManager.CPUWattage, performanceManager.CPUTemperature),
                new OSDItemGPU(performanceManager.GPUUsage, performanceManager.GPUClock, performanceManager.GPUWattage, performanceManager.GPUTemperature),
            };

            rtssState = RivatunerStatisticsServerState.NotInstalled;
        }

        public override void Update()
        {
            base.Update();

            var isRTSSInstalled = RTSSHelper.IsInstalled();
            if (rtssInstalled.Value != isRTSSInstalled)
                rtssInstalled.SetValue(isRTSSInstalled);

            if (!isRTSSInstalled)
            {
                Logger.Debug("Rivatuner Statistics Server is not installed.");
                rtssState = RivatunerStatisticsServerState.NotInstalled;
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

            if (!RTSSHelper.IsRunning())
            {
                if (SettingsManager.GetInstance().AutoStartRTSS)
                {
                    if (rtssState == RivatunerStatisticsServerState.Starting)
                    {
                        Logger.Info("Starting Rivatuner Statistics Server..");
                    }
                    else
                    {
                        rtssState = RivatunerStatisticsServerState.Starting;
                        try
                        {
                            Logger.Info("Start Rivatuner Statistics Server.");
                            Process.Start(RTSSHelper.ExecutablePath());
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Failed to start Rivatuner Statistics Server.");
                            rtssState = RivatunerStatisticsServerState.NotRunning;
                        }
                    }
                }
                return;
            }

            rtssState = RivatunerStatisticsServerState.Running;

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

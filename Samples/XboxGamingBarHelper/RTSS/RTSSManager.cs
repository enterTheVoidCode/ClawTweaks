using RTSSSharedMemoryNET;
using Shared.Utilities;
using Windows.ApplicationModel.AppService;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.RTSS.OSDItems;

namespace XboxGamingBarHelper.RTSS
{
    internal class RTSSManager : Manager
    {
        private const string OSDSeparator = " <C=6E006A>|<C> ";
        private const string OSDBackground = "<P=0,0><L0><C=80000000><B=0,0>\b<C>";
        private const string OSDAppName = "Xbox Gaming Bar OSD";

        private OSD rtssOSD;
        private readonly OSDItem[] osdItems;

        private readonly OSDProperty osd;
        public OSDProperty OSD
        {
            get { return osd; }
        }

        public RTSSManager(PerformanceManager performanceManager, AppServiceConnection connection) : base(connection)
        {
            osd = new OSDProperty(0, null, this);
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
            // Console.WriteLine($"OSD level {OSDLevel}");
            
            if (!RTSSHelper.IsRunning())
            {
                Logger.Debug("Rivatuner Statistics Server is not running.");
                return;
            }

            if (osd == 0)
            {
                if (rtssOSD != null)
                {
                    rtssOSD.Update(string.Empty);
                    rtssOSD.Dispose();
                    rtssOSD = null;
                }

                var osdEntries = RTSSSharedMemoryNET.OSD.GetOSDEntries();
                for (int i = 0; i < osdEntries.Length; i++)
                {
                    OSDEntry osdEntry = osdEntries[i];
                    if (osdEntry.Owner == OSDAppName)
                    {
                        osdEntry.Text = string.Empty;
                    }
                }

                return;
            }

            if (rtssOSD == null)
            {
                rtssOSD = new OSD(OSDAppName);
            }

            string osdString = OSDBackground;
            for (int i = 0; i < osdItems.Length; i++)
            {
                var osdItemString = osdItems[i].GetOSDString(osd);
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

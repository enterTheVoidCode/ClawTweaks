using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RTSSSharedMemoryNET;

namespace XboxGamingBarHelper
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var osdEntries = OSD.GetOSDEntries();
            // osdEntries[0].Text = "Hello from XboxGamingBarHelper!";
            Debug.WriteLine($"OSD {osdEntries.Length} APP {OSD.GetAppEntries().Length}");
        }
    }
}

using System;
using RTSSSharedMemoryNET;

namespace XboxGamingBarHelper
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Xbox Gaming Bar Helper";
            Console.WriteLine($"OSD {OSD.GetOSDEntries()} APP {OSD.GetAppEntries().Length}");
            Console.WriteLine("\r\nPress any key to exit ...");
            Console.ReadLine();
        }
    }
}

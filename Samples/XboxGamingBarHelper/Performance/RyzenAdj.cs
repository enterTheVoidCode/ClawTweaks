using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.Performance
{
    internal class RyzenAdj
    {
        private const string RYZEN_ADJ_PATH = "libryzenadj.dll";

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern IntPtr init_ryzenadj();

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int refresh_table(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int set_stapm_limit(IntPtr ry, [In] uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int set_fast_limit(IntPtr ry, [In] uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int set_slow_limit(IntPtr ry, [In] uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_stapm_limit(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_fast_limit(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_slow_limit(IntPtr ry);

    }
}

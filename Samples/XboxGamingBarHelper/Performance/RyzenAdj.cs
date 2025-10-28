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

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_core_clk(IntPtr ry, uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_core_power(IntPtr ry, uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_gfx_clk(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_gfx_temp(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_gfx_volt(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_mem_clk(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_fclk(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_soc_power(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_soc_volt(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern float get_socket_power(IntPtr ry);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int set_max_gfxclk_freq(IntPtr ry, [In] uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int set_min_gfxclk_freq(IntPtr ry, [In] uint value);

        [DllImport(RYZEN_ADJ_PATH)]
        public static extern int set_gfx_clk(IntPtr ry, [In] uint value);
    }
}

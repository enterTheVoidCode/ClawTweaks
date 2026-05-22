using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemFPS : OSDItem
    {
        public OSDItemFPS() : base("FPS", "FPS", Color.Red)
        {
        }

        public override string GetOSDString(int osdLevel)
        {
            // FPS and frametime - uses text color, yellow for frametime then back to text color
            // Apply opacity to all colors for OLED protection
            var tc = GetTextColorWithOpacity();

            // Horizontal modes (2 = Horizontal, 3 = Horizontal Detailed): compact frametime graph instead of ms text
            if (osdLevel == 2 || osdLevel == 3)
            {
                string graphColor = ApplyOpacity("00FFFF");
                return $"<C={tc}><FR> FPS <S=35><C={graphColor}><G=<FT>><C={tc}><S>";
            }

            // All other levels: standard FPS + frametime in ms
            return $"<C={tc}><FR> FPS <C={ApplyOpacity("FFFF00")}><FT> ms<C={tc}>";
        }
    }
}

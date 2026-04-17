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
            return $"<C={tc}><FR> FPS <C={ApplyOpacity("FFFF00")}><FT> ms<C={tc}>";
        }
    }
}

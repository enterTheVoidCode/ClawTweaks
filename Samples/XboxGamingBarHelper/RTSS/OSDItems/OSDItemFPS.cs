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
            return $"<C={textColor}><FR> FPS <C=FFFF00><FT> ms<C={textColor}>";
        }
    }
}

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemFPS : OSDItem
    {
        public override string GetOSDString(int osdLevel)
        {
            // Basic (level 2+): FPS and frametime
            var osdString = "<C=FF0000><APP><C> <C=FFFFFF><FR><S=50> FPS<S><C> <C=FFFF00><FT><S=50> ms<S><C>";

            return osdString;
        }
    }
}

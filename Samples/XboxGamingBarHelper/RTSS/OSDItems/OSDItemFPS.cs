namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemFPS : OSDItem
    {
        public override string GetOSDString(int osdLevel)
        {
            // Basic: FPS only
            var osdString = "<C=FF0000><APP><C> <C=FFFFFF><FR><S=50> FPS<S><C>";

            // Detailed (level 3+): Add frametime graph
            if (osdLevel >= 3)
            {
                // RTSS graph: width=60, height=20, min=0, max=50ms, data=frametime, flags
                osdString += " <C=FFFF00><FT,1><S=50> ms<S><C> <G60,20,0,50,FT,0,0,30>";
            }

            return osdString;
        }
    }
}

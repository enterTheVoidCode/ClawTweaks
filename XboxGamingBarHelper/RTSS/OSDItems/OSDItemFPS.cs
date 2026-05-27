using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemFPS : OSDItem
    {
        // Active FPS cap info: 0 = no cap; set by RTSSManager.SetFpsCapDisplay()
        private int _fpsCapValue = 0;
        private bool _fpsCapIsIntel = false;

        public OSDItemFPS() : base("FPS", "FPS", Color.Red)
        {
        }

        /// <summary>
        /// Called by RTSSManager each tick with the currently active FPS limit.
        /// fps=0 means no cap active.
        /// </summary>
        public void SetFpsCapDisplay(int fps, bool isIntel)
        {
            _fpsCapValue = fps;
            _fpsCapIsIntel = isIntel;
        }

        public override string GetOSDString(int osdLevel)
        {
            // FPS and frametime - uses text color, yellow for frametime then back to text color
            // Apply opacity to all colors for OLED protection
            var tc = GetTextColorWithOpacity();

            // Build subtle cap hint suffix (small font, muted grey) when a limiter is active
            string capHint = BuildCapHint();

            // Horizontal modes (2 = Horizontal, 3 = Horizontal Detailed): compact frametime graph instead of ms text
            if (osdLevel == 2 || osdLevel == 3)
            {
                string graphColor = ApplyOpacity("00FFFF");
                return $"<C={tc}><FR> FPS <S=35><C={graphColor}><G=<FT>><C={tc}><S>{capHint}";
            }

            // All other levels: standard FPS + frametime in ms
            return $"<C={tc}><FR> FPS <C={ApplyOpacity("FFFF00")}><FT> ms<C={tc}>{capHint}";
        }

        /// <summary>
        /// Builds a small, muted cap-limit indicator appended after the FPS value.
        /// e.g. " [60]" for RTSS at 60 fps, " [I]" for Intel.
        /// Returns empty string when no cap is active.
        /// </summary>
        private string BuildCapHint()
        {
            if (_fpsCapValue <= 0) return string.Empty;

            string mutedColor = ApplyOpacity("686868");
            string label = _fpsCapIsIntel ? "I" : _fpsCapValue.ToString();
            // <S=55> = 55% text size (subtly smaller); reset with <S> after
            return $"<S=55><C={mutedColor}>[{label}]<C><S>";
        }
    }
}

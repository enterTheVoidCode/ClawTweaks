using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for display orientation.
    /// Values: 0=Landscape, 1=Portrait (90°), 2=Landscape flipped (180°), 3=Portrait flipped (270°)
    /// </summary>
    internal class DisplayOrientationProperty : WidgetProperty<int>
    {
        public DisplayOrientationProperty() : base(0, null, Function.DisplayOrientation)
        {
        }

        public string GetOrientationText()
        {
            switch (Value)
            {
                case 0: return "Landscape";
                case 1: return "Portrait";
                case 2: return "Landscape (F)"; // Flipped
                case 3: return "Portrait (F)"; // Flipped
                default: return "Landscape";
            }
        }
    }
}

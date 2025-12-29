using System;
using System.Collections.Generic;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS
{
    internal abstract class OSDItem
    {
        protected string name;
        protected string id;
        protected string colorCode;
        protected string textColor = "FFFFFF";
        protected bool useDynamicColor = false;

        public string Id => id;

        public void SetLabelColor(string color)
        {
            if (!string.IsNullOrEmpty(color) && color != "DEFAULT")
            {
                colorCode = color;
            }
        }

        public void SetTextColor(string color)
        {
            if (color == "DYNAMIC")
            {
                useDynamicColor = true;
                textColor = "FFFFFF"; // Default fallback
            }
            else
            {
                useDynamicColor = false;
                textColor = color;
            }
        }

        protected OSDItem()
        {
            name = "OSD Item";
            id = "Unknown";
            colorCode = "FFFFFF";
        }

        protected OSDItem(string name, Color color) : this(name, name, color)
        {
        }

        protected OSDItem(string name, string id, Color color)
        {
            this.name = name;
            this.id = id;
            this.colorCode = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public virtual string GetOSDString(int osdLevel)
        {
            var osdValues = GetValues(osdLevel);

            if (osdValues == null || osdValues.Count == 0)
            {
                return string.Empty;
            }

            var osdString = $"{GetNameString()} ";

            if (osdValues == null || osdValues.Count == 0)
            {
                return osdString + " N/A";
            }

            for (int i = 0; i < osdValues.Count; i++)
            {
                var osdValue = osdValues[i];
                if (osdValue.Value < 0)
                {
                    osdString += $"<C={textColor}>N/A";
                }
                else
                {
                    var valueColor = GetValueColor(osdValue);
                    osdString += $"<C={valueColor}>{osdValue.Prefix}{osdValue.FormattedValue}{osdValue.Unit}";
                }
                if (i < osdValues.Count - 1)
                {
                    osdString += " ";
                }
            }

            // Reset to text color at end
            osdString += $"<C={textColor}>";

            return osdString;
        }

        protected virtual string GetNameString()
        {
            return $"<C={colorCode}>{name}<C={textColor}>";
        }

        protected virtual List<OSDItemValue> GetValues(int osdLevel)
        {
            return new List<OSDItemValue>();
        }

        /// <summary>
        /// Gets the color for a value based on its type and the dynamic color setting.
        /// </summary>
        protected string GetValueColor(OSDItemValue value)
        {
            if (!useDynamicColor || value.ValueType == OSDValueType.None)
            {
                return textColor;
            }

            return value.ValueType switch
            {
                OSDValueType.Temperature => GetTemperatureColor(value.Value),
                OSDValueType.Percentage => GetPercentageColor(value.Value),
                OSDValueType.PercentageInv => GetPercentageInvColor(value.Value),
                OSDValueType.Wattage => GetWattageColor(value.Value),
                OSDValueType.Speed => textColor, // Speed doesn't change color
                _ => textColor
            };
        }

        /// <summary>
        /// Temperature color: Blue (cold) -> Green (normal) -> Yellow (warm) -> Red (hot)
        /// </summary>
        private string GetTemperatureColor(float temp)
        {
            if (temp < 45) return "0080FF";      // Blue - cold
            if (temp < 55) return "00FF80";      // Cyan-green - cool
            if (temp < 65) return "00FF00";      // Green - normal
            if (temp < 75) return "80FF00";      // Yellow-green - getting warm
            if (temp < 80) return "FFFF00";      // Yellow - warm
            if (temp < 85) return "FF8000";      // Orange - hot
            return "FF0000";                      // Red - very hot
        }

        /// <summary>
        /// Percentage color (for usage): Green (low) -> Yellow (mid) -> Red (high)
        /// </summary>
        private string GetPercentageColor(float percent)
        {
            if (percent < 30) return "00FF00";   // Green - low usage
            if (percent < 50) return "80FF00";   // Yellow-green
            if (percent < 70) return "FFFF00";   // Yellow - moderate
            if (percent < 85) return "FF8000";   // Orange - high
            return "FF0000";                      // Red - very high
        }

        /// <summary>
        /// Inverted percentage color (for battery): Red (low) -> Yellow (mid) -> Green (high)
        /// </summary>
        private string GetPercentageInvColor(float percent)
        {
            if (percent < 15) return "FF0000";   // Red - critical
            if (percent < 30) return "FF8000";   // Orange - low
            if (percent < 50) return "FFFF00";   // Yellow - mid
            if (percent < 70) return "80FF00";   // Yellow-green
            return "00FF00";                      // Green - good
        }

        /// <summary>
        /// Wattage color: Green (low) -> Yellow (mid) -> Red (high)
        /// Based on typical handheld TDP ranges (5-30W)
        /// </summary>
        private string GetWattageColor(float watts)
        {
            if (watts < 8) return "00FF00";      // Green - low power
            if (watts < 15) return "80FF00";     // Yellow-green
            if (watts < 20) return "FFFF00";     // Yellow - moderate
            if (watts < 25) return "FF8000";     // Orange - high
            return "FF0000";                      // Red - very high
        }
    }
}

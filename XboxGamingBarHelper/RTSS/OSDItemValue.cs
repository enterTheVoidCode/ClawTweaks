using System;

namespace XboxGamingBarHelper.RTSS
{
    /// <summary>
    /// Type of value for dynamic color calculation
    /// </summary>
    internal enum OSDValueType
    {
        None,           // No dynamic color (uses default text color)
        Percentage,     // 0-100%: green at low, yellow at mid, red at high (for usage)
        PercentageInv,  // 0-100%: red at low, yellow at mid, green at high (for battery)
        Temperature,    // Temperature: blue cold (<50), green normal (50-70), yellow warm (70-80), red hot (>80)
        Wattage,        // Wattage: green low, yellow mid, red high (relative to TDP)
        Speed           // Speed (MHz, MB/s): neutral/white, no color scaling
    }

    internal readonly struct OSDItemValue
    {
        public float Value { get; }
        public string Unit { get; }
        public string Prefix { get; }
        public OSDValueType ValueType { get; }
        public int DecimalPlaces { get; }

        public OSDItemValue(float value, string unit)
        {
            Value = value;
            Unit = unit;
            Prefix = string.Empty;
            ValueType = OSDValueType.None;
            DecimalPlaces = 0;
        }

        public OSDItemValue(float value, string unit, string prefix)
        {
            Value = value;
            Unit = unit;
            Prefix = prefix;
            ValueType = OSDValueType.None;
            DecimalPlaces = 0;
        }

        public OSDItemValue(float value, string unit, OSDValueType valueType)
        {
            Value = value;
            Unit = unit;
            Prefix = string.Empty;
            ValueType = valueType;
            DecimalPlaces = 0;
        }

        public OSDItemValue(float value, string unit, string prefix, OSDValueType valueType)
        {
            Value = value;
            Unit = unit;
            Prefix = prefix;
            ValueType = valueType;
            DecimalPlaces = 0;
        }

        public OSDItemValue(float value, string unit, OSDValueType valueType, int decimalPlaces)
        {
            Value = value;
            Unit = unit;
            Prefix = string.Empty;
            ValueType = valueType;
            DecimalPlaces = decimalPlaces;
        }

        public string FormattedValue => DecimalPlaces > 0
            ? Value.ToString($"F{DecimalPlaces}")
            : Math.Floor(Value).ToString();
    }
}

namespace XboxGamingBarHelper.RTSS
{
    internal readonly struct OSDItemValue
    {
        public float Value { get; }
        public string Unit { get; }
        public string Prefix { get; }

        public OSDItemValue(float value, string unit)
        {
            Value = value;
            Unit = unit;
            Prefix = string.Empty;
        }

        public OSDItemValue(float value, string unit, string prefix)
        {
            Value = value;
            Unit = unit;
            Prefix = prefix;
        }
    }
}

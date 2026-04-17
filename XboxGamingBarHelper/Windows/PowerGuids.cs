using System;

namespace XboxGamingBarHelper.Windows
{
    internal static class PowerGuids
    {
        // Processor settings subgroup
        public static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new Guid("54533251-82be-4824-96c1-47b60b740d00");

        // CPU Boost
        public static readonly Guid GUID_PROCESSOR_PERFBOOST_MODE = new Guid("be337238-0d82-4146-a960-4f3749d470c7");

        // CPU EPP
        public static readonly Guid GUID_PROCESSOR_EPP = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");

        // Processor frequency limits
        public static readonly Guid GUID_PROCESSOR_FREQUENCY_LIMIT = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e100"); // PROCFREQMAX
        public static readonly Guid GUID_PROCESSOR_FREQUENCY_LIMIT1 = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e101"); // PROCFREQMAX1

        // Processor state limits (percentage)
        public static readonly Guid GUID_PROCESSOR_THROTTLE_MAX = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec"); // Maximum processor state %
        public static readonly Guid GUID_PROCESSOR_THROTTLE_MIN = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c"); // Minimum processor state %
        public static readonly Guid GUID_PROCESSOR_THROTTLE_MAX1 = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ed"); // Maximum processor state % for Processor Power Efficiency Class 1
        public static readonly Guid GUID_PROCESSOR_THROTTLE_MIN1 = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964d"); // Minimum processor state % for Processor Power Efficiency Class 1

        // Energy Saver settings
        public static readonly Guid GUID_ENERGY_SAVER_SUBGROUP = new Guid("de830923-a562-41af-a086-e3a2c6bad2da");
        public static readonly Guid GUID_ENERGY_SAVER_BATTERY_THRESHOLD = new Guid("e69653ca-cf7f-4f05-aa73-cb833fa90ad4"); // 0=never, 100=always
    }
}

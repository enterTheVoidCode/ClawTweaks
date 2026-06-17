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

        // Heterogeneous (P/E core) scheduling policy — set as a trio (ToothNClaw port)
        public static readonly Guid GUID_PROCESSOR_HETEROGENEOUS_POLICY = new Guid("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5");
        public static readonly Guid GUID_PROCESSOR_LONG_THREAD_POLICY = new Guid("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
        public static readonly Guid GUID_PROCESSOR_SHORT_THREAD_POLICY = new Guid("bae08b81-2d5e-4688-ad6a-13243356654b");

        // Core parking max/min unparked cores (percentage). Per-efficiency-class variants (suffix "1"
        // = Efficiency Class 1). On Lunar Lake: Class 0 = LP-E (Skymont) cores, Class 1 = P (Lion Cove)
        // cores. Used to actually park the opposite class for Only-P/Only-E, since the legacy
        // heterogeneous scheduling policy alone is overridden by Thread Director/HGS on Lunar Lake.
        public static readonly Guid GUID_PROCESSOR_CORE_PARKING_MAX_CORES = new Guid("ea062031-0e34-4ff1-9b6d-eb1059334028"); // class 0
        public static readonly Guid GUID_PROCESSOR_CORE_PARKING_MAX_CORES_1 = new Guid("ea062031-0e34-4ff1-9b6d-eb1059334029"); // class 1 (P)
        public static readonly Guid GUID_PROCESSOR_CORE_PARKING_MIN_CORES = new Guid("0cc5b647-c1df-4637-891a-dec35c318583"); // class 0
        public static readonly Guid GUID_PROCESSOR_CORE_PARKING_MIN_CORES_1 = new Guid("0cc5b647-c1df-4637-891a-dec35c318584"); // class 1 (P)

        // Energy Saver settings
        public static readonly Guid GUID_ENERGY_SAVER_SUBGROUP = new Guid("de830923-a562-41af-a086-e3a2c6bad2da");
        public static readonly Guid GUID_ENERGY_SAVER_BATTERY_THRESHOLD = new Guid("e69653ca-cf7f-4f05-aa73-cb833fa90ad4"); // 0=never, 100=always
    }
}

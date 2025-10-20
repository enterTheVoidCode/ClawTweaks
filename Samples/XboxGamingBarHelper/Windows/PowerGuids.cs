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
    }
}

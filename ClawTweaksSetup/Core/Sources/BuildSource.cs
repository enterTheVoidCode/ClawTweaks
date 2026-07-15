using System;

namespace ClawTweaksSetup.Core.Sources
{
    /// <summary>An installable build offered in the Center menu, from any of the three sources.</summary>
    public sealed class BuildSource
    {
        public string Origin;        // "Release" | "Test build" | "Nightly"
        public string Version;       // tag / filename-derived version, for display + sort
        public string Title;         // release name, or file name for nightlies
        public DateTime When;        // published_at / modifiedTime
        public long? SizeBytes;
        public string ZipUrl;        // full installer zip (always present)
        public string MsixUrl;       // msix-only asset; null if not offered (nightlies)
        public string Body;          // GitHub release body (markdown) for the "What's new" panel; null for nightlies

        public string SizeLabel => SizeBytes.HasValue ? $"{SizeBytes.Value / 1024.0 / 1024.0:0.#} MB" : null;
    }
}

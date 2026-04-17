using Shared.Utilities;
using System.Xml.Serialization;

namespace Shared.Data
{
    [XmlRoot("TrackedGame")]
    public struct TrackedGame
    {
        [XmlElement("AumId")]
        public string AumId;

        [XmlElement("DisplayName")]
        public string DisplayName;

        [XmlElement("TitleId")]
        public string TitleId;

        [XmlElement("IsFullscreen")]
        public bool IsFullscreen;

        public TrackedGame(string aumId, string displayName, string titleId, bool isFullscreen)
        {
            AumId = aumId;
            DisplayName = displayName;
            TitleId = titleId;
            IsFullscreen = isFullscreen;
        }

        public static bool operator ==(TrackedGame g1, TrackedGame g2)
        {
            if (ReferenceEquals(g1, g2))
                return true;

            if (ReferenceEquals(g1, null) || ReferenceEquals(g2, null))
                return false;

            return g1.DisplayName == g2.DisplayName && g1.TitleId == g2.TitleId;
        }

        public static bool operator !=(TrackedGame g1, TrackedGame g2)
        {
            return !(g1 == g2);
        }

        public override bool Equals(object obj)
        {
            if (obj is TrackedGame other)
            {
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return DisplayName.GetHashCode();
        }

        // Export to xml string.
        public override string ToString()
        {
            return XmlHelper.ToXMLString(this, true);
        }
    }
}

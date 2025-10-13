using System.Xml.Serialization;

namespace Shared.Data
{
    [XmlRoot("GameId")]
    public struct GameId
    {
        [XmlElement("Name")]
        public string Name;

        [XmlElement("Path")]
        public string Path;

        public GameId(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Name);
        }

        public static bool operator ==(GameId g1, GameId g2)
        {
            if (ReferenceEquals(g1, g2))
                return true;

            if (ReferenceEquals(g1, null) || ReferenceEquals(g2, null))
                return false;

            return g1.Name == g2.Name && g1.Path == g2.Path;
        }

        public static bool operator !=(GameId p1, GameId p2)
        {
            return !(p1 == p2);
        }

        public override bool Equals(object obj)
        {
            if (obj is GameId other)
            {
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            if (string.IsNullOrEmpty(Name) ||string.IsNullOrEmpty(Path))
            {
                return -1;
            }

            return Name.GetHashCode() ^ Path.GetHashCode();
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Name))
                return "Nothing";
            return $"{Name} at {Path}";
        }
    }
}

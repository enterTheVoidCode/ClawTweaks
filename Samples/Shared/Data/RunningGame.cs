namespace Shared.Data
{
    using Shared.Utilities;
    using System.Xml.Serialization;

    /// <summary>
    /// Contain information related to a running game.
    /// </summary>
    [XmlRoot("RunningGame")]
    public struct RunningGame
    {
        [XmlElement("ProcessId")]
        public int ProcessId;

        [XmlElement("Name")]
        public GameId GameId;

        [XmlElement("FPS")]
        public uint FPS;

        [XmlElement("IsForeground")]
        public bool IsForeground;

        public RunningGame(int processId, string name, string path, uint fps, bool isForeground)
        {
            ProcessId = processId;
            GameId = new GameId(name, path);
            FPS = fps;
            IsForeground = isForeground;
        }

        public bool IsValid()
        {
            return ProcessId > 0;
        }

        public static bool operator ==(RunningGame g1, RunningGame g2)
        {
            if (ReferenceEquals(g1, g2))
                return true;

            if (ReferenceEquals(g1, null) || ReferenceEquals(g2, null))
                return false;

            return g1.GameId == g2.GameId;
        }

        public static bool operator !=(RunningGame g1, RunningGame g2)
        {
            return !(g1 == g2);
        }

        public override bool Equals(object obj)
        {
            if (obj is RunningGame other)
            {
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return GameId.GetHashCode();
        }

        // Export to xml string.
        public override string ToString()
        {
            return XmlHelper.ToXMLString(this, true);
        }
    }
}

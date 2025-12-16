using NLog;
using Shared.Utilities;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Shared.Data
{
    [XmlRoot("GameProfile")]
    public struct GameProfile
    {
        public const string GLOBAL_PROFILE_NAME = "global";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Lock object for thread-safe cache and file operations.
        /// Prevents race conditions during profile switching.
        /// </summary>
        private static readonly object ProfileLock = new object();

        [XmlElement("GameId")]
        public GameId GameId;

        [XmlElement("Use")]
        private bool use;
        public bool Use
        {
            get
            {
                if (IsGlobalProfile)
                {
                    // Logger.Warn("Per-game profile is preferred over global profile.");
                    return false;
                }

                return use;
            }
            set
            {
                if (IsGlobalProfile)
                {
                    Logger.Warn("Can't change \"Use\" property of global profile.");
                    return;
                }

                if (use != value)
                {
                    use = value;
                    Save();
                }
            }
        }

        [XmlElement("TDP")]
        private int tdp;
        public int TDP
        {
            get { return tdp; }
            set
            {
                if (tdp != value)
                {
                    tdp = value;
                    Save();
                }
            }
        }

        [XmlElement("CPUBoost")]
        private bool cpuBoost;
        public bool CPUBoost
        {
            get { return cpuBoost; }
            set
            {
                if (cpuBoost != value)
                {
                    cpuBoost = value;
                    Save();
                }
            }
        }

        [XmlElement("CPUEPP")]
        private int cpuEPP;
        public int CPUEPP
        {
            get { return cpuEPP; }
            set
            {
                if (cpuEPP != value)
                {
                    cpuEPP = value;
                    Save();
                }
            }
        }

        [XmlElement("MaxCPUState")]
        private int maxCPUState;
        public int MaxCPUState
        {
            get { return maxCPUState; }
            set
            {
                if (maxCPUState != value)
                {
                    maxCPUState = value;
                    Save();
                }
            }
        }

        [XmlElement("MinCPUState")]
        private int minCPUState;
        public int MinCPUState
        {
            get { return minCPUState; }
            set
            {
                if (minCPUState != value)
                {
                    minCPUState = value;
                    Save();
                }
            }
        }

        [XmlElement("TDPBoostEnabled")]
        private bool tdpBoostEnabled;
        public bool TDPBoostEnabled
        {
            get { return tdpBoostEnabled; }
            set
            {
                if (tdpBoostEnabled != value)
                {
                    tdpBoostEnabled = value;
                    Save();
                }
            }
        }

        [XmlIgnore]
        public string Path;

        public bool IsGlobalProfile { get { return string.Compare(GameId.Name, GLOBAL_PROFILE_NAME) == 0; } }

        [XmlIgnore]
        private IDictionary<GameId, GameProfile> cache;
        [XmlIgnore]
        public IDictionary<GameId, GameProfile> Cache
        {
            get { return cache; }
            set { cache = value; }
        }

        public GameProfile(string gameName, string gamePath, bool inUse, int inTDP, bool inCPUBoost, int inCPUEPP, int inMaxCPUState, int inMinCPUState, bool inTDPBoostEnabled, string inPath, IDictionary<GameId, GameProfile> inCache)
        {
            GameId = new GameId(gameName, gamePath);
            use = inUse;
            tdp = inTDP;
            cpuBoost = inCPUBoost;
            cpuEPP = inCPUEPP;
            maxCPUState = inMaxCPUState;
            minCPUState = inMinCPUState;
            tdpBoostEnabled = inTDPBoostEnabled;
            Path = inPath;
            cache = inCache;
        }

        public bool IsValid()
        {
            return GameId.IsValid();
        }

        public static bool operator ==(GameProfile g1, GameProfile g2)
        {
            if (ReferenceEquals(g1, g2))
                return true;

            if (ReferenceEquals(g1, null) || ReferenceEquals(g2, null))
                return false;

            return g1.GameId == g2.GameId;
        }

        public static bool operator !=(GameProfile p1, GameProfile p2)
        {
            return !(p1 == p2);
        }

        public override bool Equals(object obj)
        {
            if (obj is GameProfile other)
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

        public void Save()
        {
            lock (ProfileLock)
            {
                if (cache != null)
                {
                    cache[GameId] = this;
                }

                if (string.IsNullOrEmpty(Path))
                {
                    // Logger.Warn($"Can't save profile {GameId.Name} due to empty path.");
                    return;
                }

                XmlHelper.ToXMLFile(this, Path);
            }
        }
    }
}

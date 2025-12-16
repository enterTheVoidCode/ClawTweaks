using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Profile
{
    internal class GameProfileProperty : HelperProperty<GameProfile, ProfileManager>
    {
        public GameProfileProperty(GameProfile inValue, ProfileManager inManager) : base(inValue, null, Function.None, inManager)
        {
        }

        public int TDP
        {
            get { return value.TDP; }
            set
            {
                if (this.value.TDP != value)
                {
                    this.value.TDP = value;
                }
            }
        }

        public bool CPUBoost
        {
            get { return value.CPUBoost; }
            set
            {
                if (this.value.CPUBoost != value)
                {
                    this.value.CPUBoost = value;
                }
            }
        }

        public int CPUEPP
        {
            get { return value.CPUEPP; }
            set
            {
                if (this.value.CPUEPP != value)
                {
                    this.value.CPUEPP = value;
                }
            }
        }

        public int MaxCPUState
        {
            get { return value.MaxCPUState; }
            set
            {
                if (this.value.MaxCPUState != value)
                {
                    this.value.MaxCPUState = value;
                }
            }
        }

        public int MinCPUState
        {
            get { return value.MinCPUState; }
            set
            {
                if (this.value.MinCPUState != value)
                {
                    this.value.MinCPUState = value;
                }
            }
        }

        public bool TDPBoostEnabled
        {
            get { return value.TDPBoostEnabled; }
            set
            {
                if (this.value.TDPBoostEnabled != value)
                {
                    this.value.TDPBoostEnabled = value;
                }
            }
        }

        public GameId GameId
        {
            get { return value.GameId; }
        }

        public bool Use
        {
            get { return value.Use; }
            set
            {
                if (this.value.Use != value)
                {
                    this.value.Use = value;
                }
            }
        }

        public bool IsGlobalProfile
        {
            get { return value.IsGlobalProfile; }
        }
    }
}

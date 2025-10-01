using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.RTSS
{
    internal struct OSDItemValue
    {
        float value;
        public float Value => value;

        string unit;
        public string Unit => unit;

        public OSDItemValue(float value, string unit)
        {
            this.value = value;
            this.unit = unit;
        }
    }
}

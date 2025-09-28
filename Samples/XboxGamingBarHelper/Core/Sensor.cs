using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XboxGamingBarHelper.Core
{
    internal abstract class Sensor
    {
        protected string name;
        public string Name => name;

        protected Sensor()
        {
            name = "Unknown Sensor";
        }

        protected Sensor(string name)
        {
            this.name = name;
        }
    }
}

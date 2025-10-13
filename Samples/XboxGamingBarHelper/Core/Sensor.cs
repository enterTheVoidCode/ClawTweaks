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

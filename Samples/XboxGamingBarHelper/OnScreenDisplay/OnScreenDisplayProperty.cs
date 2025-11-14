using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.OnScreenDisplay
{
    internal class OnScreenDisplayProperty : HelperProperty<int, OnScreenDisplayManager>
    {
        public OnScreenDisplayProperty(int inValue, IProperty inParentProperty, OnScreenDisplayManager inManager) : base(inValue, inParentProperty, Function.OSD, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            manager?.SetLevel(Value);
        }

        public virtual void ChangeManager(OnScreenDisplayManager inManager)
        {
            if (inManager == null)
            {
                Logger.Warn($"Can't change On-Screen Display's manager to null.");
                return;
            }

            if (inManager == manager)
            {
                Logger.Warn($"On-Screen Display's manager is already the same instance.");
                return;
            }

            // Before changing manager, set the previous manager's level to 0 to clean the old OSD.
            manager.SetLevel(0);
            manager.IsInUsed = false;
            manager = inManager;
            manager.SetLevel(Value);
            manager.IsInUsed = true;
        }
    }
}

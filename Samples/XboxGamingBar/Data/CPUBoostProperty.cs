using System;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CPUBoostProperty : WidgetToggleProperty
    {
        public CPUBoostProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.CPUBoost, inUI, inOwner)
        {
        }
    }
}

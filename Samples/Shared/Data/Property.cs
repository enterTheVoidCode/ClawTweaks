using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Shared.Data
{
    public abstract class Property : IProperty
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IProperty parentProperty;
        public IProperty ParentProperty
        {
            get { return parentProperty; }
        }

        private readonly List<IProperty> childProperties;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual async void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public List<IProperty> ChildProperties
        {
            get { return childProperties; }
        }

        protected Property()
        {
            parentProperty = null;
            childProperties = new List<IProperty>();
        }

        protected Property(IProperty inParentProperty)
        {
            parentProperty = inParentProperty;
            childProperties = new List<IProperty>();
            if (parentProperty != null && !parentProperty.ChildProperties.Contains(this))
            {
                parentProperty.ChildProperties.Add(this);
            }
        }

        public abstract bool TryGetValue<OutValueType>(out OutValueType value);

        public abstract bool SetValue(object value);

        public abstract object GetValue();

        public abstract Task Sync();

        public abstract bool TrySetValue<InValueType>(InValueType newValue);
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Shared.Data
{
    public interface IProperty : INotifyPropertyChanged
    {
        IProperty ParentProperty { get; }

        List<IProperty> ChildProperties { get; }

        bool TrySetValue<InValueType>(InValueType newValue);

        bool TryGetValue<ValueType>(out ValueType value);

        bool SetValue(object value);

        object GetValue();

        Task Sync();
    }
}

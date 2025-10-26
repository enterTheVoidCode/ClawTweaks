using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Shared.Data
{
    public interface IProperty : INotifyPropertyChanged
    {
        IProperty ParentProperty { get; }

        List<IProperty> ChildProperties { get; }

        //bool TrySetValue<InValueType>(InValueType newValue, long updatedTime);

        //bool TryGetValue<ValueType>(out ValueType value);

        bool SetValue(object newValue, long updatedTime);

        object GetValue();

        Task Sync();
    }
}

namespace Shared.Data
{
    public class PropertyValueChangedEventArgs<T>
    {
        public T NewValue { get; }

        public PropertyValueChangedEventArgs(T newValue)
        {
            NewValue = newValue;
        }
    }
}

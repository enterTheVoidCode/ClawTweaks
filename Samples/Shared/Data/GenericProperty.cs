using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using Windows.Foundation.Collections;

namespace Shared.Data
{
    /// <summary>
    /// Contains value for something, like the TDP, or OSD level.
    /// </summary>
    /// <typeparam name="ValueType">Type of that value. Int or bool or what so ever.</typeparam>
    public abstract class GenericProperty<ValueType> : FunctionalProperty
    {
        protected ValueType value;
        public ValueType Value
        {
            get { return  value; }
            //set
            //{
            //    if (!EqualityComparer<ValueType>.Default.Equals(this.value, value))
            //    {
            //        this.value = value;
            //        NotifyPropertyChanged();
            //    }
            //    else
            //    {
            //        Logger.Debug($"Property {GetType().Name} has same value, nothing changed.");
            //    }
            //}
        }

        private long lastUpdatedTime;

        public static implicit operator ValueType(GenericProperty<ValueType> property)
        {
            return property.Value;
        }

        public static bool operator ==(GenericProperty<ValueType> left, ValueType right)
        {
            // Handle null cases
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            // If left is not null, then call its Equals method
            return left.Equals(right);
        }

        public static bool operator ==(ValueType left, GenericProperty<ValueType> right)
        {
            // Handle null cases
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            // If left is not null, then call its Equals method
            return right.Equals(left);
        }

        public static bool operator ==(GenericProperty<ValueType> left, GenericProperty<ValueType> right)
        {
            // Handle null cases
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            // If left is not null, then call its Equals method
            return left.Equals(right);
        }

        public static bool operator !=(GenericProperty<ValueType> left, ValueType right)
        {
            return !(left == right);
        }

        public static bool operator !=(ValueType left, GenericProperty<ValueType> right)
        {
            return !(right == left);
        }

        public static bool operator !=(GenericProperty<ValueType> left, GenericProperty<ValueType> right)
        {
            return !(left == right);
        }

        // Override the Equals method
        public override bool Equals(object obj)
        {
            if (obj is GenericProperty<ValueType> other)
            {
                return EqualityComparer<ValueType>.Default.Equals(value, other.value);
            }

            if (obj is ValueType otherValue)
            {
                return EqualityComparer<ValueType>.Default.Equals(value, otherValue);
            }

            return false;
        }

        // Override GetHashCode (required when overriding Equals)
        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public GenericProperty(ValueType inValue) : base()
        {
            value = inValue;
            lastUpdatedTime = 0L;
        }

        public GenericProperty(ValueType inValue, IProperty inParentProperty) : base(inParentProperty)
        {
            value = inValue;
            lastUpdatedTime = 0L;
        }

        public GenericProperty(ValueType inValue, IProperty inParentProperty, Function inFunction) : base(inParentProperty, inFunction)
        {
            value = inValue;
            lastUpdatedTime = 0L;
        }

        public override ValueSet AddValueSetContent(in ValueSet inValueSet)
        {
            if (TypeHelper.IsStruct<ValueType>())
            {
                inValueSet.Add(nameof(Content), XmlHelper.ToXMLString(Value, true));
            }
            else
            {
                inValueSet.Add(nameof(Content), Value);
            }
            inValueSet.Add(nameof(UpdatedTime), lastUpdatedTime);
            return inValueSet;
        }

        protected bool SetValue(ValueType newValue, long updatedTime)
        {
            if (updatedTime < lastUpdatedTime)
            {
                Logger.Warn($"Skip value {newValue} of {Function} because it is older than current value {updatedTime} vs {lastUpdatedTime}.");
                return false;
            }

            if (EqualityComparer<ValueType>.Default.Equals(value, newValue))
            {
                Logger.Warn($"Skip value {newValue} of {Function} because it equals to current value.");
                lastUpdatedTime = updatedTime;
                return true;
            }

            lastUpdatedTime = updatedTime;
            value = newValue;
            NotifyPropertyChanged(nameof(value));
            return true;
        }

        //public override bool TrySetValue<InValueType>(InValueType newValue, long updatedTime)
        //{
        //    if (updatedTime < lastUpdatedTime)
        //    {
        //        Logger.Warn($"Skip value {value} because it is older than current value {updatedTime} vs {lastUpdatedTime}.");
        //        return false;
        //    }

        //    if (typeof(ValueType).IsAssignableFrom(typeof(InValueType)))
        //    {
        //        var newValue = (ValueType)(object)value;

        //        if (EqualityComparer<ValueType>.Default.Equals(value, newValue))
        //        {
        //            Logger.Warn($"Skip value {newValue} because it equals to current value.");
        //            lastUpdatedTime = updatedTime;
        //            return false;
        //        }

        //        return true;
        //    }

        //    Logger.Error($"Can't try set value {value} of type {typeof(InValueType).Name} to property {Function}");
        //    return false;
        //}

        //public override bool TryGetValue<OutValueType>(out OutValueType value)
        //{
        //    if (typeof(OutValueType) == typeof(string))
        //    {
        //        value = (OutValueType)(object)Value.ToString();
        //        return true;
        //    }

        //    if (typeof(OutValueType).IsAssignableFrom(typeof(ValueType)))
        //    {
        //        value = (OutValueType)(object)Value;
        //        return true;
        //    }

        //    Logger.Error($"Can't try get value of type {typeof(OutValueType).Name} from property {Function}");
        //    value = default;
        //    return false;
        //}

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            if (updatedTime == 0)
            {
                updatedTime = DateTime.Now.Ticks;
            }

            ValueType myTypeValue;
            if (TypeHelper.IsStruct<ValueType>() && newValue is string stringValue)
            {
                myTypeValue = XmlHelper.FromXMLString<ValueType>(stringValue);
            }
            else if (newValue is ValueType correctValueType)
            {
                myTypeValue = correctValueType;
            }
            else
            {
                Logger.Error($"Can't set value {newValue} of type {newValue.GetType().Name} to property type {typeof(ValueType).Name}");
                return false;
            }

            return SetValue(myTypeValue, updatedTime);
        }

        public override object GetValue()
        {
            try
            {
                return Value;
            }
            catch (Exception e)
            {
                Logger.Error($"Exception {e} while trying to get {Function} value.");
                return null;
            }
        }
    }
}

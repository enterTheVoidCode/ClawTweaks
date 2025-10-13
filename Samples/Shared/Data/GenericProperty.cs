using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
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
            set
            {
                if (!EqualityComparer<ValueType>.Default.Equals(this.value, value))
                {
                    this.value = value;
                    NotifyPropertyChanged();
                }
                else
                {
                    Logger.Info($"Property {GetType().Name} has same value, nothing changed.");
                }
            }
        }

        //public static explicit operator ValueType(GenericProperty<ValueType> property)
        //{
        //    return property.Value;
        //}

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
        }

        public GenericProperty(ValueType inValue, IProperty inParentProperty) : base(inParentProperty)
        {
            value = inValue;
        }

        public GenericProperty(ValueType inValue, IProperty inParentProperty, Function inFunction) : base(inParentProperty, inFunction)
        {
            value = inValue;
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
            return inValueSet;
        }

        public override bool TrySetValue<InValueType>(InValueType value)
        {
            if (typeof(ValueType).IsAssignableFrom(typeof(InValueType)))
            {
                Value = (ValueType)(object)value;
                return true;
            }

            Logger.Error($"Can't try set value {value} of type {typeof(InValueType).Name} to property {Function}");
            return false;
        }

        public override bool TryGetValue<OutValueType>(out OutValueType value)
        {
            if (typeof(OutValueType) == typeof(string))
            {
                value = (OutValueType)(object)Value.ToString();
                return true;
            }

            if (typeof(OutValueType).IsAssignableFrom(typeof(ValueType)))
            {
                value = (OutValueType)(object)Value;
                return true;
            }

            Logger.Error($"Can't try get value of type {typeof(OutValueType).Name} from property {Function}");
            value = default;
            return false;
        }

        public override bool SetValue(object value)
        {
            if (TypeHelper.IsStruct<ValueType>() && value is string stringValue)
            {
                Value = XmlHelper.FromXMLString<ValueType>(stringValue);
                return true;
            }

            try
            {
                Value = (ValueType)value;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error($"Exception {e} while trying to set {Function} value.");
                return false;
            }
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

using Shared.Constants;
using Shared.Enums;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// The last time this property was updated.
        /// </summary>
        public override long UpdatedTime => lastUpdatedTime;

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
                Logger.Debug($"Add ValueSet struct content {inValueSet[nameof(Content)]}");
            }
            else if (typeof(ValueType) == typeof(List<int>))
            {
                var list = (List<int>)(object)Value;
                inValueSet.Add(nameof(Content), string.Join(StringConstants.COMMA, list));
                Logger.Debug($"Add ValueSet list content {inValueSet[nameof(Content)]}");
            }
            else if (typeof(ValueType) == typeof(List<string>))
            {
                var list = (List<string>)(object)Value;
                inValueSet.Add(nameof(Content), string.Join(StringConstants.COMMA, list));
                Logger.Debug($"Add ValueSet string list content {inValueSet[nameof(Content)]}");
            }
            else
            {
                inValueSet.Add(nameof(Content), Value);
                Logger.Debug($"Add ValueSet content {inValueSet[nameof(Content)]}");
            }
            inValueSet.Add(nameof(UpdatedTime), lastUpdatedTime);
            return inValueSet;
        }

        protected bool SetValue(ValueType newValue, long updatedTime)
        {
            if (updatedTime < lastUpdatedTime)
            {
                Logger.Debug($"Skip value {newValue} of {Function} because it is older than current value {updatedTime} vs {lastUpdatedTime}.");
                return false;
            }

            if (EqualityComparer<ValueType>.Default.Equals(value, newValue))
            {
                Logger.Debug($"Skip value {newValue} of {Function} because it equals to current value.");
                lastUpdatedTime = updatedTime;
                return true;
            }

            if (typeof(ValueType) == typeof(List<int>))
            {
                var currentListValue = (List<int>)(object)Value;
                var newListValue = (List<int>)(object)newValue;
                // Now compare 2 lists.
                var identical = true;
                if (currentListValue.Count != newListValue.Count)
                {
                    identical = false;
                }

                if (identical)
                {
                    for (var i = 0; i < currentListValue.Count; i++)
                    {
                        if (currentListValue[i] != newListValue[i])
                        {
                            identical = false;
                            break;
                        }
                    }
                }

                if (identical)
                {
                    Logger.Debug($"Skip value list of {Function} because it equals to current value.");
                    lastUpdatedTime = updatedTime;
                    return true;
                }
            }

            if (typeof(ValueType) == typeof(List<string>))
            {
                var currentListValue = (List<string>)(object)Value;
                var newListValue = (List<string>)(object)newValue;
                // Now compare 2 lists.
                var identical = true;
                if (currentListValue.Count != newListValue.Count)
                {
                    identical = false;
                }

                if (identical)
                {
                    for (var i = 0; i < currentListValue.Count; i++)
                    {
                        if (currentListValue[i] != newListValue[i])
                        {
                            identical = false;
                            break;
                        }
                    }
                }

                if (identical)
                {
                    Logger.Debug($"Skip value string list of {Function} because it equals to current value.");
                    lastUpdatedTime = updatedTime;
                    return true;
                }
            }

            lastUpdatedTime = updatedTime;
            value = newValue;
            NotifyPropertyChanged(nameof(value));
            return true;
        }

        /// <summary>
        /// Sets the value without triggering NotifyPropertyChanged.
        /// Use this for batch sync to avoid echoing values back to the sender.
        /// </summary>
        protected bool SetValueSilent(ValueType newValue, long updatedTime)
        {
            if (updatedTime < lastUpdatedTime)
            {
                return false;
            }

            lastUpdatedTime = updatedTime;
            value = newValue;
            // No NotifyPropertyChanged - silent update
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
            if (TypeHelper.IsStruct<ValueType>() && newValue is string structStringValue)
            {
                myTypeValue = XmlHelper.FromXMLString<ValueType>(structStringValue);
            }
            else if (typeof(ValueType) == typeof(List<int>) && newValue is string listIntStringValue)
            {
                myTypeValue = (ValueType)(object)listIntStringValue.Split(StringConstants.COMMA.ToCharArray()).Select(int.Parse).ToList();
                Logger.Debug($"SetValue string {listIntStringValue} to list {myTypeValue}");
            }
            else if (typeof(ValueType) == typeof(List<string>) && newValue is string listStringValue)
            {
                myTypeValue = (ValueType)(object)listStringValue.Split(StringConstants.COMMA.ToCharArray()).ToList();
                Logger.Debug($"SetValue string {listStringValue} to string list {myTypeValue}");
            }
            // Handle string-to-bool conversion (for pipe JSON deserialization)
            else if (typeof(ValueType) == typeof(bool) && newValue is string boolStringValue)
            {
                if (bool.TryParse(boolStringValue, out bool parsedBool))
                {
                    myTypeValue = (ValueType)(object)parsedBool;
                }
                else
                {
                    Logger.Error($"Can't parse '{boolStringValue}' as Boolean for property {Function}");
                    return false;
                }
            }
            // Handle string-to-int conversion (for pipe JSON deserialization)
            else if (typeof(ValueType) == typeof(int) && newValue is string intStringValue)
            {
                if (int.TryParse(intStringValue, out int parsedInt))
                {
                    myTypeValue = (ValueType)(object)parsedInt;
                }
                else
                {
                    Logger.Error($"Can't parse '{intStringValue}' as Int32 for property {Function}");
                    return false;
                }
            }
            // Handle string-to-double conversion (for pipe JSON deserialization)
            else if (typeof(ValueType) == typeof(double) && newValue is string doubleStringValue)
            {
                if (double.TryParse(doubleStringValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedDouble))
                {
                    myTypeValue = (ValueType)(object)parsedDouble;
                }
                else
                {
                    Logger.Error($"Can't parse '{doubleStringValue}' as Double for property {Function}");
                    return false;
                }
            }
            // Handle string-to-float conversion (for pipe JSON deserialization)
            else if (typeof(ValueType) == typeof(float) && newValue is string floatStringValue)
            {
                if (float.TryParse(floatStringValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float parsedFloat))
                {
                    myTypeValue = (ValueType)(object)parsedFloat;
                }
                else
                {
                    Logger.Error($"Can't parse '{floatStringValue}' as Single for property {Function}");
                    return false;
                }
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

        /// <summary>
        /// Sets the value without triggering NotifyPropertyChanged.
        /// Use this when syncing values from hardware to avoid re-applying them.
        /// </summary>
        public virtual void SetValueSilent(ValueType newValue)
        {
            value = newValue;
            lastUpdatedTime = DateTime.Now.Ticks;
            Logger.Debug($"Silent set {Function} to {newValue}");
        }

        /// <summary>
        /// Sets the value without triggering NotifyPropertyChanged (object overload with type conversion).
        /// Use this when syncing values during Sync() to avoid sending the value back.
        /// </summary>
        public override bool SetValueSilent(object newValue, long updatedTime)
        {
            if (updatedTime == 0)
            {
                updatedTime = DateTime.Now.Ticks;
            }

            if (updatedTime < lastUpdatedTime)
            {
                Logger.Debug($"Skip silent set value {newValue} of {Function} because it is older than current value {updatedTime} vs {lastUpdatedTime}.");
                return false;
            }

            ValueType myTypeValue;
            if (TypeHelper.IsStruct<ValueType>() && newValue is string structStringValue)
            {
                myTypeValue = XmlHelper.FromXMLString<ValueType>(structStringValue);
            }
            else if (typeof(ValueType) == typeof(List<int>) && newValue is string listIntStringValue)
            {
                myTypeValue = (ValueType)(object)listIntStringValue.Split(StringConstants.COMMA.ToCharArray()).Select(int.Parse).ToList();
            }
            else if (typeof(ValueType) == typeof(List<string>) && newValue is string listStringValue)
            {
                myTypeValue = (ValueType)(object)listStringValue.Split(StringConstants.COMMA.ToCharArray()).ToList();
            }
            else if (newValue is ValueType correctValueType)
            {
                myTypeValue = correctValueType;
            }
            else
            {
                Logger.Error($"Can't silent set value {newValue} of type {newValue.GetType().Name} to property type {typeof(ValueType).Name}");
                return false;
            }

            value = myTypeValue;
            lastUpdatedTime = updatedTime;
            Logger.Debug($"Silent set {Function} to {myTypeValue}");
            return true;
        }

        /// <summary>
        /// Forces the value to be set and sent to the other side, even if it equals the current value.
        /// Use this when loading profile values to ensure the hardware state is synchronized.
        /// </summary>
        public bool ForceSetValue(ValueType newValue)
        {
            var updatedTime = DateTime.Now.Ticks;
            lastUpdatedTime = updatedTime;

            // Always update the value - ForceSetValue should force the full value update
            // This is important for structs where equality may not include all fields (e.g., RunningGame.IconPath)
            bool wasEqual = EqualityComparer<ValueType>.Default.Equals(value, newValue);
            value = newValue;

            if (wasEqual)
            {
                Logger.Info($"Force sending {Function} value to remote (equality unchanged but value updated)");
            }

            NotifyPropertyChanged(nameof(value));
            return true;
        }
    }
}

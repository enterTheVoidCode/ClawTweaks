using System;
using System.Collections.Generic;

namespace Shared.Utilities
{
    public static class TypeHelper
    {
        public static bool IsStruct(this Type type)
        {
            return type.IsValueType && !type.IsPrimitive && !type.IsEnum && type != typeof(decimal);
        }

        public static bool IsStruct<T>()
        {
            return IsStruct(typeof(T));
        }

        public static bool IsList(this Type type)
        {
            // Check if the type is a generic type and its generic type definition is List<>
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        public static bool IsList<T>()
        {
            return IsList(typeof(T));
        }
    }
}

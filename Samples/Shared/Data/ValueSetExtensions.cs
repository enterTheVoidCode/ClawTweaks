using System.Text;
using Windows.Foundation.Collections;

namespace Shared.Data
{
    public static class ValueSetExtensions
    {
        private const string QUOTE = "\"";
        public static string ToDebugString(this ValueSet valueSet)
        {
            if (valueSet == null || valueSet.Count == 0)
            {
                return string.Empty;
            }

            var debugString = new StringBuilder();
            debugString.Append(QUOTE);
            foreach (var value in valueSet)
            {
                debugString.Append(value.Key);
                debugString.Append(":");
                var isStringValue = value.Value is string;
                if (isStringValue)
                {
                    debugString.Append(QUOTE);
                }
                debugString.Append(value.Value);
                if (isStringValue)
                {
                    debugString.Append(QUOTE);
                }
                debugString.Append(",");
            }
            debugString.Remove(debugString.Length - 1, 1);
            debugString.Append(QUOTE);
            return debugString.ToString();
        }
    }
}

using Shared.Enums;
using System.Text;
using Windows.Foundation.Collections;

namespace Shared.Data
{
    public static class ValueSetExtensions
    {
        private const string QUOTE = "\"";
        private const string SPACE = " ";
        public static string ToDebugString(this ValueSet valueSet)
        {
            if (valueSet == null || valueSet.Count == 0)
            {
                return string.Empty;
            }

            var debugString = new StringBuilder();
            debugString.Append(QUOTE);

            if (valueSet.ContainsKey(nameof(Command)))
            {
                debugString.Append((Command)valueSet[nameof(Command)]);
            }

            if (valueSet.ContainsKey(nameof(Function)))
            {
                debugString.Append(SPACE);
                debugString.Append((Function)valueSet[nameof(Function)]);
            }

            if (valueSet.ContainsKey(nameof(Content)))
            {
                debugString.Append(" to ");
                debugString.Append(valueSet[nameof(Content)]);
            }

            if (valueSet.ContainsKey(nameof(UpdatedTime)))
            {
                debugString.Append(" at ");
                debugString.Append(valueSet[nameof(UpdatedTime)]);
            }

            debugString.Append(QUOTE);
            return debugString.ToString();
        }
    }
}

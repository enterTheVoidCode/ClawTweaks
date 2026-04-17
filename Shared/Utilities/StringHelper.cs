using System.Linq;

namespace Shared.Utilities
{
    public static class StringHelper
    {
        public static string CleanStringForSerialization(string input)
        {
            if (input == null)
                return null;

            // Remove XML-invalid control characters:
            // anything < 0x20 except TAB (0x09), LF (0x0A), CR (0x0D)
            return new string(input.Where(c =>
                c == '\t' || c == '\n' || c == '\r' || c >= ' '
            ).ToArray());
        }
    }
}

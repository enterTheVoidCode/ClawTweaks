using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// Cleans a game display name / name-key. Some games inject invisible Unicode into their
        /// window title: ARC Raiders, for example, puts zero-width characters (U+200B ZERO WIDTH
        /// SPACE, U+FEFF) between every letter and uses an exotic space (U+2005), and the exact
        /// pattern differs per launch — so the same game yields a different string each time. That
        /// breaks name-based profile matching and makes a saved profile look "missing" in the UI.
        /// Strip all format/control code points (ZWSP/ZWNJ/ZWJ/WORD JOINER/BOM …), normalize any
        /// Unicode space separator (NBSP, U+2000–U+200A, U+202F, U+3000 …) to a plain space,
        /// collapse runs of spaces, and trim. Identity/keys still resolve on the stable path/TitleId;
        /// this only stabilizes the human-facing, name-keyed label.
        /// </summary>
        public static string CleanGameName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name ?? "";

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.Format || cat == UnicodeCategory.Control)
                    continue; // drop zero-width / formatting / control code points
                if (cat == UnicodeCategory.SpaceSeparator)
                {
                    sb.Append(' '); // normalize exotic spaces to a plain ASCII space
                    continue;
                }
                sb.Append(ch);
            }

            return Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        }
    }
}

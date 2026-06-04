using System.Collections.Generic;

namespace Shared.Data
{
    /// <summary>
    /// A user-defined "Program Action": an .exe or .ps1 the user picked to launch from a tile,
    /// controller combo, or the Left MSI button. The full path is the stable identifier — it is
    /// baked into the binding, so bindings keep working even if this list is later edited.
    /// </summary>
    public class ProgramAction
    {
        /// <summary>Full path to the .exe or .ps1.</summary>
        public string Path { get; set; }

        public ProgramAction() { }

        public ProgramAction(string path) { Path = path; }

        /// <summary>File name without extension, used as the dropdown/list label.</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Path)) return "";
                try { return System.IO.Path.GetFileNameWithoutExtension(Path); }
                catch { return Path; }
            }
        }

        public static string ToJson(List<ProgramAction> items)
        {
            if (items == null || items.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append("{");
                sb.AppendFormat("\"Path\":\"{0}\"", EscapeJsonString(items[i].Path ?? ""));
                sb.Append("}");
                if (i < items.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        public static List<ProgramAction> FromJson(string json)
        {
            var list = new List<ProgramAction>();
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return list;
            try
            {
                int pos = 0;
                while (pos < json.Length)
                {
                    int objStart = json.IndexOf('{', pos);
                    if (objStart < 0) break;
                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;
                    string objStr = json.Substring(objStart, objEnd - objStart + 1);
                    var m = System.Text.RegularExpressions.Regex.Match(objStr, @"""Path""\s*:\s*""([^""]*)""");
                    if (m.Success)
                    {
                        string path = UnescapeJsonString(m.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(path))
                            list.Add(new ProgramAction(path));
                    }
                    pos = objEnd + 1;
                }
            }
            catch { }
            return list;
        }

        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string UnescapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}

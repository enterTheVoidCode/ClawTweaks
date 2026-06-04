using System.Collections.Generic;

namespace Shared.Data
{
    /// <summary>
    /// A user-defined "Launch Website" action: a URL the user entered to open in the default
    /// browser from a tile, controller combo, or the Left MSI button. The URL is the stable
    /// identifier and is baked into the binding.
    /// </summary>
    public class UrlAction
    {
        /// <summary>The full URL (http/https).</summary>
        public string Url { get; set; }

        public UrlAction() { }

        public UrlAction(string url) { Url = url; }

        /// <summary>Label for the dropdown/list — the URL without the scheme for compactness.</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Url)) return "";
                string s = Url.Trim();
                int idx = s.IndexOf("://");
                if (idx >= 0) s = s.Substring(idx + 3);
                return s.TrimEnd('/');
            }
        }

        public static string ToJson(List<UrlAction> items)
        {
            if (items == null || items.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append("{");
                sb.AppendFormat("\"Url\":\"{0}\"", EscapeJsonString(items[i].Url ?? ""));
                sb.Append("}");
                if (i < items.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        public static List<UrlAction> FromJson(string json)
        {
            var list = new List<UrlAction>();
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
                    var m = System.Text.RegularExpressions.Regex.Match(objStr, @"""Url""\s*:\s*""([^""]*)""");
                    if (m.Success)
                    {
                        string url = UnescapeJsonString(m.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(url))
                            list.Add(new UrlAction(url));
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

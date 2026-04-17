using System.Collections.Generic;

namespace Shared.Data
{
    /// <summary>
    /// Represents a TDP mode preset with customizable power limit.
    /// </summary>
    public class TdpPreset
    {
        /// <summary>
        /// Display name of the preset (e.g., "Quiet", "Balanced", "Turbo")
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// TDP power limit in watts
        /// </summary>
        public int TdpWatts { get; set; }

        /// <summary>
        /// Legion hardware mode value (1=Quiet, 2=Balanced, 3=Performance, null for custom presets).
        /// When set, Legion devices will use hardware mode instead of software TDP control.
        /// </summary>
        public int? LegionModeValue { get; set; }

        /// <summary>
        /// Whether this is a built-in preset (cannot be deleted, only edited)
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Whether TDP Boost is enabled for this preset
        /// </summary>
        public bool TdpBoostEnabled { get; set; }

        public TdpPreset() { }

        public TdpPreset(string name, int tdpWatts, int? legionModeValue = null, bool isBuiltIn = false, bool tdpBoostEnabled = false)
        {
            Name = name;
            TdpWatts = tdpWatts;
            LegionModeValue = legionModeValue;
            IsBuiltIn = isBuiltIn;
            TdpBoostEnabled = tdpBoostEnabled;
        }

        /// <summary>
        /// Creates the default set of TDP presets
        /// </summary>
        public static List<TdpPreset> GetDefaultPresets()
        {
            return new List<TdpPreset>
            {
                new TdpPreset("Quiet", 8, 1, true),
                new TdpPreset("Balanced", 15, 2, true),
                new TdpPreset("Performance", 25, 3, true)
            };
        }

        /// <summary>
        /// Serializes a list of presets to JSON string
        /// </summary>
        public static string ToJson(List<TdpPreset> presets)
        {
            if (presets == null || presets.Count == 0)
                return "[]";

            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < presets.Count; i++)
            {
                var p = presets[i];
                sb.Append("{");
                sb.AppendFormat("\"Name\":\"{0}\",", EscapeJsonString(p.Name ?? ""));
                sb.AppendFormat("\"TdpWatts\":{0},", p.TdpWatts);
                sb.AppendFormat("\"LegionModeValue\":{0},", p.LegionModeValue.HasValue ? p.LegionModeValue.Value.ToString() : "null");
                sb.AppendFormat("\"IsBuiltIn\":{0},", p.IsBuiltIn ? "true" : "false");
                sb.AppendFormat("\"TdpBoostEnabled\":{0}", p.TdpBoostEnabled ? "true" : "false");
                sb.Append("}");
                if (i < presets.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Deserializes a list of presets from JSON string
        /// </summary>
        public static List<TdpPreset> FromJson(string json)
        {
            var presets = new List<TdpPreset>();
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return presets;

            try
            {
                // Simple JSON array parsing without external dependencies
                // Format: [{"Name":"...","TdpWatts":...,"LegionModeValue":...,"IsBuiltIn":...},...]
                int pos = 0;
                while (pos < json.Length)
                {
                    int objStart = json.IndexOf('{', pos);
                    if (objStart < 0) break;

                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    string objStr = json.Substring(objStart, objEnd - objStart + 1);
                    var preset = ParsePresetObject(objStr);
                    if (preset != null)
                        presets.Add(preset);

                    pos = objEnd + 1;
                }
            }
            catch
            {
                // Return empty list on parse failure
            }

            return presets;
        }

        private static TdpPreset ParsePresetObject(string objStr)
        {
            var preset = new TdpPreset();

            // Parse Name
            var nameMatch = System.Text.RegularExpressions.Regex.Match(objStr, @"""Name""\s*:\s*""([^""]*)""");
            if (nameMatch.Success)
                preset.Name = UnescapeJsonString(nameMatch.Groups[1].Value);

            // Parse TdpWatts
            var tdpMatch = System.Text.RegularExpressions.Regex.Match(objStr, @"""TdpWatts""\s*:\s*(\d+)");
            if (tdpMatch.Success)
                preset.TdpWatts = int.Parse(tdpMatch.Groups[1].Value);

            // Parse LegionModeValue (can be null or a number)
            var legionMatch = System.Text.RegularExpressions.Regex.Match(objStr, @"""LegionModeValue""\s*:\s*(\d+|null)");
            if (legionMatch.Success)
            {
                string val = legionMatch.Groups[1].Value;
                if (val != "null" && int.TryParse(val, out int legionMode))
                    preset.LegionModeValue = legionMode;
                else
                    preset.LegionModeValue = null;
            }

            // Parse IsBuiltIn
            var builtInMatch = System.Text.RegularExpressions.Regex.Match(objStr, @"""IsBuiltIn""\s*:\s*(true|false)");
            if (builtInMatch.Success)
                preset.IsBuiltIn = builtInMatch.Groups[1].Value == "true";

            // Parse TdpBoostEnabled
            var boostMatch = System.Text.RegularExpressions.Regex.Match(objStr, @"""TdpBoostEnabled""\s*:\s*(true|false)");
            if (boostMatch.Success)
                preset.TdpBoostEnabled = boostMatch.Groups[1].Value == "true";

            return preset;
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

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Shared.Data
{
    /// <summary>
    /// Global widget settings that are not per-game.
    /// Includes button remapping, device limits, OSD customization.
    /// </summary>
    public class GlobalWidgetSettings
    {
        // Legion Button Remapping
        [XmlElement("LegionL_Action")]
        public int? LegionL_Action { get; set; }
        [XmlElement("LegionL_Shortcut")]
        public string LegionL_Shortcut { get; set; }
        [XmlElement("LegionL_Command")]
        public string LegionL_Command { get; set; }

        [XmlElement("LegionR_Action")]
        public int? LegionR_Action { get; set; }
        [XmlElement("LegionR_Shortcut")]
        public string LegionR_Shortcut { get; set; }
        [XmlElement("LegionR_Command")]
        public string LegionR_Command { get; set; }

        // Scroll Wheel Remapping
        [XmlElement("Scroll_Action")]
        public int? Scroll_Action { get; set; }
        [XmlElement("Scroll_Shortcut")]
        public string Scroll_Shortcut { get; set; }
        [XmlElement("Scroll_Command")]
        public string Scroll_Command { get; set; }

        [XmlElement("ScrollClick_Action")]
        public int? ScrollClick_Action { get; set; }
        [XmlElement("ScrollClick_Shortcut")]
        public string ScrollClick_Shortcut { get; set; }
        [XmlElement("ScrollClick_Command")]
        public string ScrollClick_Command { get; set; }

        // Device TDP Limits
        [XmlElement("DeviceTDPMin")]
        public int? DeviceTDPMin { get; set; }
        [XmlElement("DeviceTDPMax")]
        public int? DeviceTDPMax { get; set; }

        // OSD Customization
        [XmlElement("OSD_TextSize")]
        public int? OSD_TextSize { get; set; }
        [XmlElement("OSD_TextColor")]
        public string OSD_TextColor { get; set; }
        [XmlElement("OSD_LabelColor")]
        public string OSD_LabelColor { get; set; }
        [XmlElement("OSD_Opacity")]
        public int? OSD_Opacity { get; set; }

        // OSD Level Configuration - Item Order (comma-separated item IDs)
        [XmlElement("OSD_L1_Order")]
        public string OSD_L1_Order { get; set; }
        [XmlElement("OSD_L2_Order")]
        public string OSD_L2_Order { get; set; }
        [XmlElement("OSD_L3_Order")]
        public string OSD_L3_Order { get; set; }

        // OSD Level Configuration - Enabled Items (comma-separated item IDs that are enabled)
        [XmlElement("OSD_L1_Enabled")]
        public string OSD_L1_Enabled { get; set; }
        [XmlElement("OSD_L2_Enabled")]
        public string OSD_L2_Enabled { get; set; }
        [XmlElement("OSD_L3_Enabled")]
        public string OSD_L3_Enabled { get; set; }

        // OSD Level Configuration - Per-item Colors (format: "itemId:color,itemId:color")
        [XmlElement("OSD_L1_ItemColors")]
        public string OSD_L1_ItemColors { get; set; }
        [XmlElement("OSD_L2_ItemColors")]
        public string OSD_L2_ItemColors { get; set; }
        [XmlElement("OSD_L3_ItemColors")]
        public string OSD_L3_ItemColors { get; set; }

        // OSD Level Columns
        [XmlElement("OSD_L1_Columns")]
        public int? OSD_L1_Columns { get; set; }
        [XmlElement("OSD_L2_Columns")]
        public int? OSD_L2_Columns { get; set; }
        [XmlElement("OSD_L3_Columns")]
        public int? OSD_L3_Columns { get; set; }

        public GlobalWidgetSettings()
        {
        }
    }

    /// <summary>
    /// Container for exporting/importing all profiles.
    /// Used for backup and restore functionality.
    /// </summary>
    [XmlRoot("GoTweaksProfiles")]
    public class ProfileExport
    {
        /// <summary>
        /// Export format version for compatibility checking
        /// </summary>
        [XmlAttribute("Version")]
        public int Version { get; set; } = 2;

        /// <summary>
        /// Timestamp when the export was created
        /// </summary>
        [XmlElement("ExportDate")]
        public DateTime ExportDate { get; set; }

        /// <summary>
        /// Application version that created this export
        /// </summary>
        [XmlElement("AppVersion")]
        public string AppVersion { get; set; }

        /// <summary>
        /// Global widget settings (button remapping, TDP limits, OSD customization)
        /// </summary>
        [XmlElement("GlobalSettings")]
        public GlobalWidgetSettings GlobalSettings { get; set; }

        /// <summary>
        /// The global (default) profile
        /// </summary>
        [XmlElement("GlobalProfile")]
        public GameProfile GlobalProfile { get; set; }

        /// <summary>
        /// All per-game profiles
        /// </summary>
        [XmlArray("GameProfiles")]
        [XmlArrayItem("GameProfile")]
        public List<GameProfile> GameProfiles { get; set; } = new List<GameProfile>();

        /// <summary>
        /// Default constructor for serialization
        /// </summary>
        public ProfileExport()
        {
        }

        /// <summary>
        /// Creates an export with the given profiles
        /// </summary>
        public ProfileExport(GameProfile globalProfile, IEnumerable<GameProfile> gameProfiles, string appVersion, GlobalWidgetSettings globalSettings = null)
        {
            Version = 2;
            ExportDate = DateTime.Now;
            AppVersion = appVersion;
            GlobalProfile = globalProfile;
            GameProfiles = new List<GameProfile>(gameProfiles);
            GlobalSettings = globalSettings;
        }
    }
}

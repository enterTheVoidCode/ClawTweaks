using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// A blank spacer row for the Full overlay so users can visually group related stats. Renders a
    /// single (near-empty) row; it's toggleable and reorderable like any other item. Several instances
    /// exist (Blank1..Blank4) so multiple spacers can be placed independently.
    ///
    /// Returns a single space rather than "" because the OSD builder skips empty item strings — a
    /// space produces an actual empty row at full-overlay (1-column) layout.
    /// </summary>
    internal class OSDItemBlankLine : OSDItem
    {
        public OSDItemBlankLine(string id) : base(string.Empty, id, Color.Gray)
        {
        }

        public override string GetOSDString(int osdLevel)
        {
            return " ";
        }
    }
}

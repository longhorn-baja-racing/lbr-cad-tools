using System.Collections.Generic;
using System.Drawing;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// Sections of the command tab. Each section is its own box on the tab
    /// (SolidWorks draws a divider between boxes), has its own icon color, and
    /// has a heading in the Tool Guide.
    /// </summary>
    internal enum ToolGroup
    {
        VehicleDesign,
        SketchPoints,
        Assembly,
        Files,
        Help,
    }

    internal static class ToolGroups
    {
        /// <summary>Left-to-right order on the tab.</summary>
        public static IReadOnlyList<ToolGroup> Order { get; } = new[]
        {
            ToolGroup.VehicleDesign,
            ToolGroup.SketchPoints,
            ToolGroup.Assembly,
            ToolGroup.Files,
            ToolGroup.Help,
        };

        public static string Name(ToolGroup group)
        {
            switch (group)
            {
                case ToolGroup.VehicleDesign: return "Vehicle Design";
                case ToolGroup.SketchPoints: return "Sketch Points";
                case ToolGroup.Assembly: return "Assembly";
                case ToolGroup.Files: return "Files";
                default: return "Help and Setup";
            }
        }

        public static string Subtitle(ToolGroup group)
        {
            switch (group)
            {
                case ToolGroup.VehicleDesign: return "Rule-checked parts and geometry checks for the car.";
                case ToolGroup.SketchPoints: return "Move point data between sketches and CSV files (hardpoints, test data).";
                case ToolGroup.Assembly: return "Fix or float every component at once, and undo it.";
                case ToolGroup.Files: return "Export and save files without the busywork.";
                default: return "This guide, settings, and version information.";
            }
        }

        /// <summary>Icon tile color. All pass white-on-color contrast at icon sizes.</summary>
        public static Color Color(ToolGroup group)
        {
            switch (group)
            {
                case ToolGroup.VehicleDesign: return System.Drawing.Color.FromArgb(0xBF, 0x57, 0x00); // burnt orange
                case ToolGroup.SketchPoints: return System.Drawing.Color.FromArgb(0x00, 0x5F, 0x86); // UT blue
                case ToolGroup.Assembly: return System.Drawing.Color.FromArgb(0x33, 0x3F, 0x48);     // charcoal
                case ToolGroup.Files: return System.Drawing.Color.FromArgb(0x3D, 0x7A, 0x34);        // green
                default: return System.Drawing.Color.FromArgb(0x6B, 0x75, 0x7D);                     // slate
            }
        }

        /// <summary>Help buttons are small and stacked on the tab; the rest are large with text below.</summary>
        public static bool SmallButtons(ToolGroup group) => group == ToolGroup.Help;
    }
}

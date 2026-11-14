using System.Collections.Generic;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// Help text for one command, split into the labeled blocks the Tool
    /// Guide and the task pane show. Write for someone new to SolidWorks:
    /// short sentences, SolidWorks names for things, no jargon without a gloss.
    /// </summary>
    internal sealed class ToolHelp
    {
        /// <summary>What it does and why you would use it. One to three sentences.</summary>
        public string Purpose { get; set; } = "";

        /// <summary>
        /// What must be open or prepared first, as a short phrase that also
        /// reads as the reason the button is grayed out ("Open an assembly.").
        /// Empty when it always works.
        /// </summary>
        public string Needs { get; set; } = "";

        /// <summary>Numbered steps, one action each.</summary>
        public IReadOnlyList<string> Steps { get; set; } = new string[0];

        /// <summary>Tips, limits, and side effects worth knowing.</summary>
        public IReadOnlyList<string> Notes { get; set; } = new string[0];
    }
}

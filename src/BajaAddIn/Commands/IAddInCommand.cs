using System.Collections.Generic;
using Lbr.CadTools.AddIn.Util;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// One button on the LBR CAD Tools command tab. Add new commands to
    /// <see cref="AddIn.AllCommands"/>.
    /// </summary>
    internal interface IAddInCommand
    {
        /// <summary>Button and menu text.</summary>
        string Title { get; }

        /// <summary>One short sentence, no final period: what the button does. Tooltip, status bar, task pane.</summary>
        string Hint { get; }

        /// <summary>Full help for the Tool Guide.</summary>
        ToolHelp Help { get; }

        /// <summary>Tab section; also sets the icon color.</summary>
        ToolGroup Group { get; }

        /// <summary>Pictogram drawn on the button.</summary>
        ToolIcon Icon { get; }

        /// <summary>Document types whose command tab shows this button.</summary>
        IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; }

        /// <summary>Called often by SolidWorks; keep it cheap.</summary>
        bool IsEnabled(ISldWorks app);

        void Execute(ISldWorks app);
    }
}

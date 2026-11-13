using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>
    /// One button on the LBR CAD Tools command tab. Add new commands to
    /// <see cref="AddIn.CreateCommands"/>.
    /// </summary>
    internal interface IAddInCommand
    {
        /// <summary>Button and menu text.</summary>
        string Title { get; }

        /// <summary>Tooltip and status bar hint.</summary>
        string Hint { get; }

        /// <summary>One to three characters drawn on the button icon.</summary>
        string IconText { get; }

        /// <summary>Document types whose command tab shows this button.</summary>
        IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; }

        /// <summary>Called often by SolidWorks; keep it cheap.</summary>
        bool IsEnabled(ISldWorks app);

        void Execute(ISldWorks app);
    }
}

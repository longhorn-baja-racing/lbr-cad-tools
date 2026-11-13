using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Commands
{
    /// <summary>Shows the add-in version. Confirms the add-in loaded.</summary>
    internal sealed class AboutCommand : IAddInCommand
    {
        public string Title => "About";

        public string Hint => "Show the LBR CAD Tools version";

        public string IconText => "i";

        public IReadOnlyCollection<swDocumentTypes_e> DocumentTypes { get; } = new[]
        {
            swDocumentTypes_e.swDocPART,
            swDocumentTypes_e.swDocASSEMBLY,
            swDocumentTypes_e.swDocDRAWING,
        };

        public bool IsEnabled(ISldWorks app) => true;

        public void Execute(ISldWorks app)
        {
            string version = typeof(AboutCommand).Assembly.GetName().Version.ToString();
            app.SendMsgToUser2(
                $"{AddIn.Title} {version}\nLonghorn Baja Racing\ngithub.com/longhorn-baja-racing/lbr-cad-tools",
                (int)swMessageBoxIcon_e.swMbInformation,
                (int)swMessageBoxBtn_e.swMbOk);
        }
    }
}

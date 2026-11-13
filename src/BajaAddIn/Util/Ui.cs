using System;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>Small helpers shared by commands that talk to the user.</summary>
    internal static class Ui
    {
        /// <summary>SolidWorks message box with an OK button.</summary>
        public static void Show(ISldWorks app, string message, swMessageBoxIcon_e icon) =>
            app.SendMsgToUser2(message, (int)icon, (int)swMessageBoxBtn_e.swMbOk);

        /// <summary>The SolidWorks main window, to own WinForms dialogs; null when not found.</summary>
        public static IWin32Window MainWindow(ISldWorks app) =>
            app.Frame() is IFrame frame ? new WindowHandle(new IntPtr(frame.GetHWndx64())) : null;

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }
        }
    }
}

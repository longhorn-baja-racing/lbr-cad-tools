using System;
using System.Linq;
using System.Windows.Forms;

namespace Lbr.CadTools.Setup
{
    internal static class Program
    {
        /// <summary>
        /// Opens the installer window. "/uninstall" (used by Windows Apps)
        /// opens it ready to remove the add-in.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));
            Application.Run(new SetupForm(uninstall));
        }
    }
}

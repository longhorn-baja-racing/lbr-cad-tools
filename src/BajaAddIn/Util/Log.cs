using System;
using System.IO;

namespace Lbr.CadTools.AddIn.Util
{
    /// <summary>
    /// Plain text log at %LOCALAPPDATA%\LBR CAD Tools\BajaAddIn.log. Restarted
    /// each time SolidWorks loads the add-in. Logging never throws.
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string DataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LBR CAD Tools");

        public static string FilePath { get; } = Path.Combine(DataDirectory, "BajaAddIn.log");

        public static void Restart()
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.WriteAllText(FilePath, string.Empty);
                }
            }
            catch (Exception)
            {
                // Logging must never break the add-in.
            }
        }

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception ex = null) =>
            Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(FilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
                }
            }
            catch (Exception)
            {
                // Logging must never break the add-in.
            }
        }
    }
}

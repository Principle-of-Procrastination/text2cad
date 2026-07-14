using System;
using System.IO;

namespace Text2Cad.Addin.Infrastructure
{
    internal static class AddinLog
    {
        private static readonly object Sync = new object();

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception? exception)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Text2CAD",
                    "logs");
                Directory.CreateDirectory(directory);

                string line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (exception != null)
                {
                    line += Environment.NewLine + exception;
                }

                lock (Sync)
                {
                    File.AppendAllText(
                        Path.Combine(directory, "addin.log"),
                        line + Environment.NewLine,
                        System.Text.Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never destabilize SLDWORKS.exe.
            }
        }
    }
}

using System;
using System.IO;

namespace CryptoBot
{
    /// <summary>
    /// Common helper methods.
    /// </summary>
    public static class Helpers
    {
        private static object _fileLocker = new object();

        public static bool SaveToFile(string content, string path)
        {
            lock (_fileLocker)
            {
                try
                {
                    string directoryPath = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    if (File.Exists(path))
                    {
                        content += File.ReadAllText(path);
                    }

                    string tempPath = Path.Combine(Path.GetTempPath(), "TEMP_FILE");
                    File.WriteAllText(tempPath, content);

                    File.Delete(path);
                    File.Move(tempPath, path);
                }
                catch (Exception)
                {
                    return false;
                }

                return true;
            }
        }
    }
}

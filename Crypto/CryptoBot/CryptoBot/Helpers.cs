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

        public static bool SaveData(string data, string path)
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

                    File.AppendAllText(path, data);
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

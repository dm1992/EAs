using System.IO;

namespace Common
{
    public static class Helpers
    {
        private static object _fileWriteLocker = new object();
        private static object _fileReadLocker = new object();

        public static void WriteToFile(string data, string filePath)
        {
            lock (_fileWriteLocker)
            {
                string directory = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(filePath, data);
            }
        }

        public static string[] ReadFromFile(string filePath)
        {
            lock (_fileReadLocker)
            {
                return File.ReadAllLines(filePath);
            }
        }
    }
}

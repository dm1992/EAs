using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoBot.Data;
using CryptoBot.EventArgs;
using CryptoBot.Interfaces;
using CryptoBot.Managers;

namespace CryptoBot
{
    public class Program
    {
        public const string BASE_APPLICATION_DIRECTORY = "CryptoBotData";

        private static ManualResetEvent _terminateApplication = new ManualResetEvent(initialState: false);

        private static void WaitApplicationTermination()
        {
            _terminateApplication.WaitOne();
        }

        public static void TerminateApplication()
        {
            _terminateApplication.Set();
        }

        public static void OutputData(string data, string dataScope = null)
        {
            if (String.IsNullOrEmpty(data)) return;

            if (String.IsNullOrEmpty(dataScope))
            {
                // only general message is output to console
                Console.Write(data);
            }

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BASE_APPLICATION_DIRECTORY,
                          $"{dataScope ?? "Application"}_data_{DateTime.Now:ddMMyyyy}.txt");

            if (!Helpers.SaveToFile(data, path))
            {
                Console.WriteLine($"!!!Failed to save application message '{data}'!!!");
            }
        }

        public static void Main(string[] args)
        {
            try
            {
                if (!ApplicationHandler.Initialize())
                    return;

                WaitApplicationTermination();
            }
            catch (Exception e)
            {
                // all exceptions should be handled before, just in case
                OutputData($"!!!FATAL ERROR!!! Main program exception occurred: {e}.");
            }
            finally
            {
                Console.ReadLine();
                Environment.Exit(0);
             
            }
        }
    }
}

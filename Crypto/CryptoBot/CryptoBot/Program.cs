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
using CryptoBot.Managers.Davor;

namespace CryptoBot
{
    public class Program
    {
        private static ManualResetEvent _terminateApplication = new ManualResetEvent(initialState: false);

        private static void WaitApplicationTermination()
        {
            _terminateApplication.WaitOne();
        }

        public static void TerminateApplication()
        {
            _terminateApplication.Set();
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
                Console.WriteLine($"FATAL ERROR. Main program exception occurred: {e}.");
            }
            finally
            {
                Console.ReadLine();
                Environment.Exit(0);
             
            }
        }
    }
}

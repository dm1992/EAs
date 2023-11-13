using Common;
using MarketAnalyzer.Managers;
using MarketAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataAnalyzer
{
    public class Program
    {
        static void Main(string[] args)
        {
            try
            {
                LotteryManager lotteryManager = new LotteryManager();
                string sourceFilePath = "C:\\Users\\Davor\\Delo\\Projekti\\Lokalno\\EAs\\Crypto\\CryptoBot\\DataAnalyzer\\Data\\results_ej.csv";
                string destinationFilePath = "C:\\Users\\Davor\\Delo\\Projekti\\Lokalno\\EAs\\Crypto\\CryptoBot\\DataAnalyzer\\Data\\frequencies.txt";

                if (!lotteryManager.ParseDrawingResults(sourceFilePath))
                {
                    Console.WriteLine($"Failed to parse eurojackpot results from source '{sourceFilePath}'.");
                    return;
                }

                List<DrawingNumberFrequency> drawingNumberFrequencies = lotteryManager.GetDrawingNumbersFrequencies(LotteryResultFilter.Year);

                Helpers.WriteToFile(String.Join("\n\n-------------\n\n", drawingNumberFrequencies.Select(x => x.Dump())), destinationFilePath);

                Console.WriteLine("Eurojackpot analysis results written.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Fatal error ocurred: {e.Message}");
            }
            finally
            {
                Console.ReadLine();
            }
        }
    }
}

using Common;
using MarketAnalyzer.Managers;
using MarketAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataAnalyzer
{
    public class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                List<DrawingLookupType> drawingLookupTypes = ParseDrawingLookupTypes(args);
                if (drawingLookupTypes.IsNullOrEmpty())
                    return;

                LotteryManager lotteryManager = new LotteryManager();
                string sourceFilePath = "C:\\Users\\Davor\\Delo\\Projekti\\Lokalno\\EAs\\Crypto\\CryptoBot\\DataAnalyzer\\Data\\results_ej.csv";
                string destinationFilePath = "C:\\Users\\Davor\\Delo\\Projekti\\Lokalno\\EAs\\Crypto\\CryptoBot\\DataAnalyzer\\Data\\";

                if (!lotteryManager.ParseDrawings(sourceFilePath))
                {
                    Console.WriteLine($"Failed to parse eurojackpot drawings from source '{sourceFilePath}'.");
                    return;
                }

                foreach (DrawingLookupType dlt in drawingLookupTypes)
                {
                    Console.WriteLine($"Drawing number frequency on {dlt}.");

                    foreach (var drawingNumberFrequency in lotteryManager.GetDrawingNumberFrequencies(dlt).OrderBy(x => x.Value ?? 0))
                    {
                        Helpers.WriteToFile(drawingNumberFrequency.Dump(), Path.Combine(destinationFilePath, $"{dlt}_frequency.txt"));
                    }
                }

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

        private static List<DrawingLookupType> ParseDrawingLookupTypes(string [] args)
        {
            List<DrawingLookupType> drawingLookupTypes = new List<DrawingLookupType>();

            if (args.Length < 1)
            {
                Console.WriteLine("Pass arguments!");
                return drawingLookupTypes;
            }

            foreach (var arg in args)
            {
                drawingLookupTypes.Add((DrawingLookupType)Enum.Parse(typeof(DrawingLookupType),arg));
            }

            return drawingLookupTypes;
        }
    }
}

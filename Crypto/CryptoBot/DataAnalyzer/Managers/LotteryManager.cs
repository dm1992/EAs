using Common;
using MarketAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketAnalyzer.Managers
{
    public class LotteryManager
    {
        private const int DRAWING_NUMBERS_1 = 50;
        private const int DRAWING_NUMBERS_2 = 12;

        private List<DrawingResult> _drawingResults;

        // explicit for eurojackpot
        private Dictionary<int, int> _drawingNumbers1;
        private Dictionary<int, int> _drawingNumbers2;

        public LotteryManager()
        {
            _drawingResults = new List<DrawingResult>();

            SetDrawingNumbers();
        }

        private void SetDrawingNumbers()
        {
            _drawingNumbers1 = new Dictionary<int, int>();

            for (int i = 1; i <= DRAWING_NUMBERS_1; i++)
            {
                _drawingNumbers1.Add(i, 0);
            }

            _drawingNumbers2 = new Dictionary<int, int>();

            for (int i = 1; i <= DRAWING_NUMBERS_2; i++)
            {
                _drawingNumbers2.Add(i, 0);
            }
        }

        public bool ParseDrawingResults(string sourceFilePath)
        {
            try
            {
                string [] data = Helpers.ReadFromFile(sourceFilePath);
                
                foreach (string d in data)
                {
                    string[] dataValues = d.Split(';');

                    DrawingResult drawing = new DrawingResult();
                    drawing.Date = Convert.ToDateTime(dataValues[0]);
                    drawing.Numbers1 = new List<int>();
                    drawing.Numbers2 = new List<int>();

                    for (int i = 1; i <= 5; i++)
                    {
                        drawing.Numbers1.Add(Convert.ToInt32(dataValues[i]));
                    }

                    for (int i = 6; i <= 7; i++)
                    {
                        drawing.Numbers2.Add(Convert.ToInt32(dataValues[i]));
                    }

                    _drawingResults.Add(drawing);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public List<DrawingNumberFrequency> GetDrawingNumbersFrequencies(LotteryResultFilter filter)
        {
            List<DrawingNumberFrequency> drawingNumberFrequencies = new List<DrawingNumberFrequency>();

            if (_drawingResults.IsNullOrEmpty())
                return drawingNumberFrequencies;

            foreach (var drawingYearGroupResults in _drawingResults.GroupBy(x => x.Date.Year).OrderBy(x => x.Key))
            {
                foreach (var drawingYearGroupResult in drawingYearGroupResults)
                {
                    EvaluateDrawingResult(drawingYearGroupResult);
                }

                DrawingNumberFrequency drawingNumberFrequency = new DrawingNumberFrequency();
                drawingNumberFrequency.LotteryResultFilter = filter;
                drawingNumberFrequency.Value = drawingYearGroupResults.Key;
                drawingNumberFrequency.NumberFrequency1 = _drawingNumbers1.OrderByDescending(x => x.Value).ToList();
                drawingNumberFrequency.NumberFrequency2 = _drawingNumbers2.OrderByDescending(x => x.Value).ToList();

                drawingNumberFrequencies.Add(drawingNumberFrequency);

                SetDrawingNumbers();
            }

            return drawingNumberFrequencies;
        }

        private void EvaluateDrawingResult(DrawingResult drawingResult)
        {
            if (drawingResult == null)
                return;

            for (int i = 0; i < drawingResult.Numbers1.Count; i++)
            {
                _drawingNumbers1[drawingResult.Numbers1.ElementAt(i)]++;
            }

            for (int i = 0; i < drawingResult.Numbers2.Count; i++)
            {
                _drawingNumbers2[drawingResult.Numbers2.ElementAt(i)]++;
            }
        }
    }
}

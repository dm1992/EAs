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
        private const int MAX_NUMBER_FIELD1 = 50;
        private const int MAX_NUMBER_FIELD2 = 12;

        private List<Drawing> _drawings;

        // explicit for eurojackpot
        private Dictionary<int, int> _drawingNumberFrequencyField1;
        private Dictionary<int, int> _drawingNumberFrequencyField2;

        public LotteryManager()
        {
           _drawings = new List<Drawing>();

            ResetDrawingNumberFrequencyFields();
        }

        public bool ParseDrawings(string sourceFilePath)
        {
            try
            {
                string [] data = Helpers.ReadFromFile(sourceFilePath);
                
                foreach (string d in data)
                {
                    string[] dataValues = d.Split(';');

                    Drawing drawing = new Drawing();
                    drawing.Date = Convert.ToDateTime(dataValues[0]);
                    drawing.Field1 = new List<int>();
                    drawing.Field2 = new List<int>();

                    for (int i = 1; i <= 5; i++)
                    {
                        drawing.Field1.Add(Convert.ToInt32(dataValues[i]));
                    }

                    for (int i = 6; i <= 7; i++)
                    {
                        drawing.Field2.Add(Convert.ToInt32(dataValues[i]));
                    }

                    _drawings.Add(drawing);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public List<DrawingNumberFrequency> GetDrawingNumberFrequencies(DrawingLookupType drawingLookupType)
        {
            if (_drawings.IsNullOrEmpty())
                return null;

            switch (drawingLookupType)
            {
                case DrawingLookupType.Month:
                    return GetDrawingNumberFrequencies_Month();

                case DrawingLookupType.CurrentYearMonth:
                    return GetDrawingNumberFrequencies_CurrentYearMonth();

                case DrawingLookupType.Total:
                default:
                    return GetDrawingNumberFrequencies_Total();

            }
        }

        private List<DrawingNumberFrequency> GetDrawingNumberFrequencies_Month()
        {
            try
            {
                List<DrawingNumberFrequency> drawingNumberFrequencies = new List<DrawingNumberFrequency>();

                foreach (var drawingGroup in _drawings.GroupBy(x => x.Date.Month))
                {
                    foreach (var drawingGroupItem in drawingGroup)
                    {
                        EvaluateDrawing(drawingGroupItem);
                    }

                    DrawingNumberFrequency drawingNumberFrequency = new DrawingNumberFrequency(DrawingLookupType.Month);
                    drawingNumberFrequency.Value = drawingGroup.Key;
                    drawingNumberFrequency.TotalDrawings = drawingGroup.Count();
                    drawingNumberFrequency.DrawingNumberFrequencyField1 = _drawingNumberFrequencyField1.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                    drawingNumberFrequency.DrawingNumberFrequencyField2 = _drawingNumberFrequencyField2.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);

                    drawingNumberFrequencies.Add(drawingNumberFrequency);

                    ResetDrawingNumberFrequencyFields();
                }

                return drawingNumberFrequencies;
            }
            finally
            {
                ResetDrawingNumberFrequencyFields();
            }
        }

        private List<DrawingNumberFrequency> GetDrawingNumberFrequencies_CurrentYearMonth()
        {
            try
            {
                List<DrawingNumberFrequency> drawingNumberFrequencies = new List<DrawingNumberFrequency>();

                foreach (var drawingGroup in _drawings.Where(x => x.Date.Year == DateTime.Now.Year).GroupBy(x => x.Date.Month))
                {
                    foreach (var drawingGroupItem in drawingGroup)
                    {
                        EvaluateDrawing(drawingGroupItem);
                    }

                    DrawingNumberFrequency drawingNumberFrequency = new DrawingNumberFrequency(DrawingLookupType.CurrentYearMonth);
                    drawingNumberFrequency.Value = drawingGroup.Key;
                    drawingNumberFrequency.TotalDrawings = drawingGroup.Count();
                    drawingNumberFrequency.DrawingNumberFrequencyField1 = _drawingNumberFrequencyField1.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                    drawingNumberFrequency.DrawingNumberFrequencyField2 = _drawingNumberFrequencyField2.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);

                    drawingNumberFrequencies.Add(drawingNumberFrequency);

                    ResetDrawingNumberFrequencyFields();
                }

                return drawingNumberFrequencies;
            }
            finally
            {
                ResetDrawingNumberFrequencyFields();
            }
        }

        private List<DrawingNumberFrequency> GetDrawingNumberFrequencies_Total()
        {
            try
            {
                foreach (Drawing drawing in _drawings)
                {
                    EvaluateDrawing(drawing);
                }

                DrawingNumberFrequency drawingNumberFrequency = new DrawingNumberFrequency(DrawingLookupType.Total);
                drawingNumberFrequency.TotalDrawings = _drawings.Count;
                drawingNumberFrequency.DrawingNumberFrequencyField1 = _drawingNumberFrequencyField1.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                drawingNumberFrequency.DrawingNumberFrequencyField2 = _drawingNumberFrequencyField2.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);

                return new List<DrawingNumberFrequency>() { drawingNumberFrequency };
            }
            finally
            {
                ResetDrawingNumberFrequencyFields();
            }
        }

        private void EvaluateDrawing(Drawing drawing)
        {
            if (drawing == null) return;

            for (int i = 0; i < drawing.Field1.Count; i++)
            {
                _drawingNumberFrequencyField1[drawing.Field1.ElementAt(i)]++;
            }

            for (int i = 0; i < drawing.Field2.Count; i++)
            {
                _drawingNumberFrequencyField2[drawing.Field2.ElementAt(i)]++;
            }
        }

        private void ResetDrawingNumberFrequencyFields()
        {
            _drawingNumberFrequencyField1 = new Dictionary<int, int>();

            for (int i = 1; i <= MAX_NUMBER_FIELD1; i++)
            {
                _drawingNumberFrequencyField1.Add(i, 0);
            }

            _drawingNumberFrequencyField2 = new Dictionary<int, int>();

            for (int i = 1; i <= MAX_NUMBER_FIELD2; i++)
            {
                _drawingNumberFrequencyField2.Add(i, 0);
            }
        }
    }
}

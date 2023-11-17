
using Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MarketAnalyzer.Models
{
    public class DrawingNumberFrequency
    {
        public DrawingNumberFrequency(DrawingLookupType drawingLookupType)
        {
            this.DrawingLookupType = drawingLookupType;
            this.DrawingNumberFrequencyField1 = new Dictionary<int, int>();
            this.DrawingNumberFrequencyField2 = new Dictionary<int, int>();
        }

        public DrawingLookupType DrawingLookupType { get; set; }
        public int? Value { get; set; }
        public int TotalDrawings { get; set; }
        public Dictionary<int, int> DrawingNumberFrequencyField1 { get; set; }
        public Dictionary<int, int> DrawingNumberFrequencyField2 { get; set; }

        public string Dump()
        {
            return $"\n\n--------------{this.DrawingLookupType}/{this.Value}/FIELD1/{this.TotalDrawings}----------------\n\n" +
                   String.Join("\n", this.DrawingNumberFrequencyField1.Select(x => $"{x.Key}:{x.Value} ({Math.Round((x.Value /(decimal)(this.TotalDrawings)) * 100.0m, 2)}%)")) +
                   $"\n\n--------------{this.DrawingLookupType}/{this.Value}/FIELD2/{this.TotalDrawings}----------------\n\n" +
                   String.Join("\n", this.DrawingNumberFrequencyField2.Select(x => $"{x.Key}:{x.Value} ({Math.Round((x.Value / (decimal)(this.TotalDrawings)) * 100.0m, 2)}%)"));
        }
    }
}

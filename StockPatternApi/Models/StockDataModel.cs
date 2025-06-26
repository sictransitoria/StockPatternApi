namespace StockPatternApi.Models
{
    public class StockDataModel
    {
        public DateTime Date { get; set; }
        public double Close { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public long Volume { get; set; }
        public double SMA50 { get; set; }
        public double Highs { get; set; }
        public double Lows { get; set; }
        public bool LowerHighs { get; set; }
        public bool HigherLows { get; set; }
        public bool Wedge { get; set; }
        public double VolMA { get; set; }
        public bool DecVol { get; set; }
        public bool Setup { get; set; }
        public bool Trend { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace StockPatternApi.Models.Reports
{
    public class AggregatedSummaryReport
    {
        [Key]
        public int TotalTrades { get; set; }
        public int GreenCount { get; set; }
        public int RedCount { get; set; }
        public double SuccessRate { get; set; }
        public double AvgReturnPct { get; set; }
        public double BestTradePct { get; set; }
        public double WorstTradePct { get; set; }
    }
}
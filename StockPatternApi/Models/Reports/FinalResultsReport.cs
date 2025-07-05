using System.ComponentModel.DataAnnotations;

namespace StockPatternApi.Models
{
    public class FinalResultsReport
    {
        [Key]
        public required string Ticker { get; set; }
        public double PercentageDifference { get; set; }
        public required string GreenOrRedDay { get; set; }
        public required string DateUpdated { get; set; }
    }
}
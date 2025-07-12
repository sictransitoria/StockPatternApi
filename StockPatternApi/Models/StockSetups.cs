using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockPatternApi.Models
{
    [Table("SPA_StockSetups")]
    public class StockSetups
    {
        [Key]
        public int Id { get; set; }
        public required string Ticker { get; set; }
        public DateTime Date { get; set; }
        public double Close { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public long Volume { get; set; }
        public double VolMA { get; set; }
        public bool Trend { get; set; }
        public bool Setup { get; set; }
        public required string Signal { get; set; }
        public bool IsFinalized { get; set; }
        public double ResistanceLevel { get; set; }
    }
}

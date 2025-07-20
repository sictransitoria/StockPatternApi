using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockPatternApi.Models
{
    [Table("SPA_FinalResults")]
    public class FinalResults
    {
        [Key]
        public int Id { get; set; }
        public int? StockSetupId { get; set; }
        public DateTime DateUpdated { get; set; }
        public double PriceSoldAt { get; set; }
        public bool IsActive { get; set; }
    }
}

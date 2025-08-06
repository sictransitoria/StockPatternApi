using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockPatternApi.Models
{
    [Table("SPA_JournalEntries")]
    public class JournalEntries
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public required string EntrySubject { get; set; }
        public required string EntryBody { get; set; }
        public bool IsActive { get; set; }
    }
}

using Microsoft.EntityFrameworkCore;
using StockPatternApi.Models;

namespace StockPatternApi.Helpers
{
    public class StockPatternDbContext : DbContext
    {
        public StockPatternDbContext(DbContextOptions<StockPatternDbContext> options)
            : base(options) { }

        // Tables
        public DbSet<StockSetups> SPA_StockSetups { get; set; }
        public DbSet<FinalResults> SPA_FinalResults { get; set; }

        // Stored Procedures
        public DbSet<FinalResultsReport> FinalResultsReport { get; set; }
    }
}

using Microsoft.EntityFrameworkCore;
using StockPatternApi.Models;

namespace StockPatternApi.Data
{
    public class StockPatternDbContext : DbContext
    {
        public StockPatternDbContext(DbContextOptions<StockPatternDbContext> options)
            : base(options) { }

        public DbSet<StockSetup> SPA_StockSetups { get; set; }
        public DbSet<StockDataModel> SPA_FinalResults { get; set; }
    }
}

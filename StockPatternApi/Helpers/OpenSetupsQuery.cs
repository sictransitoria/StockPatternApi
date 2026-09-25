using Microsoft.EntityFrameworkCore;
using StockPatternApi.Models;

namespace StockPatternApi.Helpers;

/// <summary>
/// Shared watchlist filters for Setups UI and Bot digest: last 24 hours, not finalized,
/// and not linked to an inactive FinalResults row (IsActive = 0).
/// </summary>
public static class OpenSetupsQuery
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static DateTime Cutoff(DateTime? now = null) => (now ?? DateTime.Now) - MaxAge;

    public static bool IsWithinAgeWindow(DateTime setupDate, DateTime? now = null) =>
        setupDate >= Cutoff(now);

    /// <summary>
    /// Open setups for UI / email-open: unfinalized, within 24h, and without an inactive FinalResults row.
    /// IsActive lives on SPA_FinalResults (not SPA_StockSetups).
    /// </summary>
    public static IQueryable<StockSetups> Watchlist(StockPatternDbContext db, DateTime? now = null)
    {
        var cutoff = Cutoff(now);
        return db.SPA_StockSetups
            .Where(s => !s.IsFinalized)
            .Where(s => s.Date >= cutoff)
            .Where(s => !db.SPA_FinalResults.Any(f => f.StockSetupId == s.Id && !f.IsActive));
    }
}

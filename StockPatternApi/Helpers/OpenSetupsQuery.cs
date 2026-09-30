using Microsoft.EntityFrameworkCore;
using StockPatternApi.Models;

namespace StockPatternApi.Helpers;

/// <summary>
/// Shared watchlist filters for Setups UI and Bot digest: last 24 hours, not finalized,
/// and not linked to an inactive FinalResults row (IsActive = 0).
/// Soft-RR breakouts (R:R below 2.0 / "(RR soft)" label) are excluded - not actionable.
/// Trend/SMA50 is not required so PDF-valid downtrend reversals still appear.
/// </summary>
public static class OpenSetupsQuery
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Same floor as StockPatternScanService.MinRewardToRisk / Algorithm.MinRewardToRisk.</summary>
    public const double MinRewardToRisk = 2.0;

    public static DateTime Cutoff(DateTime? now = null) => (now ?? DateTime.Now) - MaxAge;

    public static bool IsWithinAgeWindow(DateTime setupDate, DateTime? now = null) =>
        setupDate >= Cutoff(now);

    /// <summary>
    /// In-memory twin of the row predicates in <see cref="Watchlist"/> (except inactive FinalResults,
    /// which requires the DB). Keep these checks identical so scan email and UI cannot drift.
    /// </summary>
    public static bool MatchesOpenCriteria(StockSetups s, DateTime? now = null)
    {
        if (s.IsFinalized)
            return false;
        if (!IsWithinAgeWindow(s.Date, now))
            return false;

        var signal = s.Signal ?? string.Empty;
        if (signal.Contains("RR soft", StringComparison.Ordinal))
            return false;
        if (signal.Contains("Breakout", StringComparison.Ordinal) && s.RewardToRisk < MinRewardToRisk)
            return false;

        return true;
    }

    /// <summary>Apply <see cref="MatchesOpenCriteria"/> to an in-memory sequence.</summary>
    public static IEnumerable<StockSetups> FilterOpen(IEnumerable<StockSetups> setups, DateTime? now = null) =>
        setups.Where(s => MatchesOpenCriteria(s, now));

    /// <summary>
    /// Open setups for UI / email digest: unfinalized, within 24h, actionable R:R for breakouts,
    /// and without an inactive FinalResults row.
    /// IsActive lives on SPA_FinalResults (not SPA_StockSetups).
    /// </summary>
    public static IQueryable<StockSetups> Watchlist(StockPatternDbContext db, DateTime? now = null)
    {
        var cutoff = Cutoff(now);
        return db.SPA_StockSetups
            .Where(s => !s.IsFinalized)
            .Where(s => s.Date >= cutoff)
            // Soft-RR / sub-floor breakouts must never feed Get Stock Setups / UI watchlist
            // (covers legacy DB rows still labeled "A+ ... (RR soft)").
            .Where(s => !s.Signal.Contains("RR soft"))
            .Where(s => !s.Signal.Contains("Breakout") || s.RewardToRisk >= MinRewardToRisk)
            .Where(s => !db.SPA_FinalResults.Any(f => f.StockSetupId == s.Id && !f.IsActive));
    }
}

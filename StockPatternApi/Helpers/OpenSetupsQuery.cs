using Microsoft.EntityFrameworkCore;
using StockPatternApi.Models;

namespace StockPatternApi.Helpers;

/// <summary>
/// Shared watchlist filters for Setups UI: last 24 hours, not finalized,
/// and not linked to an inactive FinalResults row (IsActive = 0).
/// WeekdayScan / digest email uses <see cref="IsSameCalendarDayEt"/> separately
/// so the UI can still show overnight (prior-day) opens.
/// </summary>
public static class OpenSetupsQuery
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

    public static DateTime Cutoff(DateTime? now = null) => (now ?? DateTime.Now) - MaxAge;

    public static bool IsWithinAgeWindow(DateTime setupDate, DateTime? now = null) =>
        setupDate >= Cutoff(now);

    /// <summary>
    /// True when the setup forming bar falls on the same Eastern calendar day as <paramref name="now"/>.
    /// Used by email digests only — not by the UI watchlist.
    /// </summary>
    public static bool IsSameCalendarDayEt(DateTime setupDate, DateTime? now = null)
    {
        var (start, end) = SameDayEtWindow(now);
        return setupDate >= start && setupDate < end;
    }

    /// <summary>
    /// Eastern midnight..midnight window for "today" (inclusive start, exclusive end).
    /// </summary>
    public static (DateTime Start, DateTime EndExclusive) SameDayEtWindow(DateTime? now = null)
    {
        var instant = now ?? DateTime.Now;
        DateTime etNow;
        if (instant.Kind == DateTimeKind.Utc)
            etNow = TimeZoneInfo.ConvertTimeFromUtc(instant, Eastern);
        else if (instant.Kind == DateTimeKind.Local)
            etNow = TimeZoneInfo.ConvertTime(instant, Eastern);
        else
            // Unspecified: treat as Eastern wall clock (host/SQL Express local is ET).
            etNow = instant;

        var today = etNow.Date;
        return (today, today.AddDays(1));
    }

    /// <summary>
    /// Open setups for UI: unfinalized, within 24h, and without an inactive FinalResults row.
    /// Does NOT clamp to same calendar day — overnight opens stay visible.
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

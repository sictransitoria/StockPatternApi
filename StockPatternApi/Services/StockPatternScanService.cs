using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StockPatternApi.Helpers;
using StockPatternApi.Models;

namespace StockPatternApi.Services;

public sealed class ScanOptions
{
    /// <summary>If null or empty, uses <see cref="StockSymbols.DistinctOrdered"/>.</summary>
    public IReadOnlyList<string>? Tickers { get; set; }

    /// <summary>Sessions lookback used as DateTime.UtcNow.Date.AddDays(-(Lookback + 50)) for Yahoo fetch start.</summary>
    public int Lookback { get; set; } = 12;

    /// <summary>When true, write StockPatternAPIBotResults.json and clear prior result artifacts.</summary>
    public bool WriteJson { get; set; }
}

public sealed class ScanResult
{
    public List<StockSetups> Setups { get; init; } = [];
    public int InsertedCount { get; init; }
    public int DuplicateCount { get; init; }
    public List<ScanError> Errors { get; init; } = [];
    public string? OutPath { get; init; }
    /// <summary>Raw detector hits before VRTX-quality publish filters (deduped by ticker+day).</summary>
    public int RawSetupCount { get; init; }
}

public sealed class ScanError
{
    public string Ticker { get; init; } = "";
    public string Error { get; init; } = "";
}

/// <summary>
/// Shared StockPatternAPIBot scan pipeline: Yahoo 30m bars, blue-chip universe,
/// 5-session as-of detect, VRTX-quality publish filters, SPA_StockSetups persist, email.
/// </summary>
public sealed class StockPatternScanService
{
    private const int AsOfSessionCount = 5;
    private const int FormingMaxAgeSessions = 2;
    private const int PublishCap = 15;
    private const double FormingMinRewardToRisk = 2.0;
    private const double BreakoutMinVolumeVsMa = 1.5;
    private const int WashoutLookbackMinSessions = 8;
    private const int WashoutLookbackMaxSessions = 12;
    private const double WashoutMinAtrMult = 1.5;
    private const double WashoutMinPct = 0.015;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly EmailService _email;

    public StockPatternScanService(
        IConfiguration configuration,
        HttpClient httpClient,
        EmailService? emailService = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _email = emailService ?? new EmailService();

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StockPatternAPIBotScan", "1.0"));
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout == TimeSpan.Zero)
            _http.Timeout = TimeSpan.FromSeconds(30);
    }


    /// <summary>
    /// Standalone factory for CLI (--stockpatternapibot-scan) without a web host.
    /// Caller owns and should dispose the returned HttpClient if desired; service does not dispose it.
    /// </summary>
    public static (StockPatternScanService Service, HttpClient Http) CreateStandalone()
    {
        var configuration = BuildConfiguration();
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StockPatternAPIBotScan", "1.0"));
        var service = new StockPatternScanService(configuration, http, new EmailService());
        return (service, http);
    }

    public static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile(
                $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                optional: true,
                reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

    public static string GetResultsDirectory() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "StockPatternAPIBot"));

    public async Task<ScanResult> ScanAsync(ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ScanOptions();
        var distinct = (options.Tickers != null && options.Tickers.Count > 0)
            ? options.Tickers.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToArray()
            : StockSymbols.DistinctOrdered();

        string? outPath = null;
        if (options.WriteJson)
        {
            var outDir = GetResultsDirectory();
            outPath = Path.Combine(outDir, "StockPatternAPIBotResults.json");
            Directory.CreateDirectory(outDir);
            foreach (var file in Directory.EnumerateFiles(outDir, "StockPatternAPIBotResults*"))
                File.Delete(file);
        }

        StockPatternDbContext? dbContext = null;
        var unfinalizedTickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            dbContext = CreateDbContext();
            var unfinalizedTickerList = await dbContext.SPA_StockSetups
                .AsNoTracking()
                .Where(s => !s.IsFinalized)
                .Select(s => s.Ticker)
                .ToListAsync(cancellationToken);
            unfinalizedTickers = unfinalizedTickerList.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"Database connected. Unfinalized tickers skipped={unfinalizedTickers.Count}.");
        }
        catch (Exception ex)
        {
            dbContext?.Dispose();
            dbContext = null;
            Console.WriteLine($"Database setup failed; continuing scan without DB persistence: {ex.Message}");
        }

        var allRawSetups = new List<StockSetups>();
        var allSetups = new List<StockSetups>();
        var errors = new List<ScanError>();
        var lookbackStart = DateTime.UtcNow.Date.AddDays(-(options.Lookback + 50));

        Console.WriteLine(
            $"StockPatternAPIBot VRTX-quality falling-wedge scan starting. Tickers={distinct.Length}. Interval=30m. As-of last {AsOfSessionCount} sessions. Publish cap={PublishCap}. Source=Yahoo chart API.");

        foreach (var ticker in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (unfinalizedTickers.Contains(ticker))
            {
                Console.WriteLine($"  {ticker}: skipped (unfinalized setup exists in database)");
                continue;
            }

            try
            {
                var history = await FetchYahoo30mAsync(_http, ticker, lookbackStart, cancellationToken);
                if (history.Count == 0)
                {
                    errors.Add(new ScanError { Ticker = ticker, Error = "no bars" });
                    Console.WriteLine($"  {ticker}: no bars");
                    continue;
                }

                var sessionDaysDesc = history.Select(h => h.Date.Date).Distinct().OrderByDescending(d => d).ToList();
                var asOfDays = sessionDaysDesc.Take(AsOfSessionCount).ToList();
                var latestTwoSessions = sessionDaysDesc.Take(FormingMaxAgeSessions).ToHashSet();
                var foundForTicker = new List<StockSetups>();

                foreach (var day in asOfDays)
                {
                    var asOf = history.Where(h => h.Date.Date <= day).ToList();
                    var setups = Algorithm.WedgePatternDetector.Detect(ticker, asOf, new HashSet<DateTime>());
                    if (setups.Count > 0)
                        foundForTicker.AddRange(setups);
                }

                var lastClose = history[^1].Close;

                // Failed-break drop for breakouts (keep forming setups for later gates).
                foundForTicker = foundForTicker
                    .Where(s => !IsBreakoutSignal(s.Signal) || lastClose >= s.BreakoutPrice)
                    .ToList();

                var dedupedRaw = DeduplicateByTickerDay(foundForTicker);
                allRawSetups.AddRange(dedupedRaw);

                var quality = dedupedRaw
                    .Where(s => PassesActionablePublishGate(s, lastClose, latestTwoSessions))
                    .Where(PassesBreakoutVolumeGate)
                    .Where(s => PassesWashoutReclaimGate(s, history, lastClose))
                    .ToList();

                if (quality.Count > 0)
                {
                    var latest = quality.OrderByDescending(s => s.Date).First();
                    allSetups.AddRange(quality);
                    Console.WriteLine(
                        $"  {ticker}: raw={dedupedRaw.Count} published={quality.Count}, latest {latest.Signal} @ {latest.Date:u} close={latest.Close:F2} R:R={latest.RewardToRisk}");
                }
                else if (dedupedRaw.Count > 0)
                {
                    Console.WriteLine($"  {ticker}: raw={dedupedRaw.Count} published=0 (filtered by VRTX-quality gates)");
                }
                else
                {
                    Console.WriteLine($"  {ticker}: no setup");
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ScanError { Ticker = ticker, Error = ex.Message });
                Console.WriteLine($"  {ticker}: ERROR {ex.Message}");
            }

            await Task.Delay(60, cancellationToken);
        }

        var rawLatestSetups = DeduplicateByTickerDay(allRawSetups);
        var latestSetups = RankAndCapPublished(DeduplicateByTickerDay(allSetups), PublishCap);

        Console.WriteLine(
            $"VRTX-quality filter: raw(deduped)={rawLatestSetups.Count} -> published={latestSetups.Count} (cap {PublishCap}).");

        if (options.WriteJson && outPath != null)
        {
            var payload = new
            {
                generatedAt = DateTime.Now,
                source = "StockPatternAPIBot via Yahoo Finance chart API (not Financial Modeling Prep)",
                pattern = "Falling Wedge VRTX-quality (washout-reclaim + breakout vol>=1.5xMA + actionable window + failed-break; freefall rejected)",
                interval = "30m",
                mode = $"as-of last {AsOfSessionCount} sessions; publish held Breakouts (incl RR soft) + A+/Good forming (R:R>={FormingMinRewardToRisk}, last {FormingMaxAgeSessions} sessions); rank Breakouts then Date then R:R; top {PublishCap}",
                tickerCount = distinct.Length,
                rawSetupCount = rawLatestSetups.Count,
                setupCount = latestSetups.Count,
                setups = latestSetups.Select(s => new
                {
                    s.Ticker,
                    s.Date,
                    s.Close,
                    s.High,
                    s.Low,
                    s.Volume,
                    s.VolMA,
                    s.Trend,
                    s.Setup,
                    s.Signal,
                    s.ResistanceLevel,
                    s.BreakoutPrice,
                    s.IsFinalized,
                    s.Compression,
                    s.HighSlope,
                    s.LowSlope,
                    s.SmoothedATR,
                    s.StopLoss,
                    s.TakeProfit,
                    s.RiskPerShare,
                    s.RewardPerShare,
                    s.RewardToRisk
                }),
                errors = errors.Select(e => new { ticker = e.Ticker, error = e.Error })
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outPath, json, cancellationToken);
            Console.WriteLine($"Done. Raw={rawLatestSetups.Count}. Published={latestSetups.Count}. Wrote {outPath}");
        }
        else
        {
            Console.WriteLine($"Done. Raw={rawLatestSetups.Count}. Published={latestSetups.Count}.");
        }

        var inserted = 0;
        var duplicates = 0;
        if (dbContext != null)
        {
            try
            {
                var persistResult = await PersistSetupsAsync(dbContext, latestSetups, cancellationToken);
                inserted = persistResult.Inserted;
                duplicates = persistResult.Duplicates;
                Console.WriteLine(
                    $"Database persistence complete. Inserted={persistResult.Inserted}; skipped duplicates={persistResult.Duplicates}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database persistence failed: {ex.Message}");
            }
            finally
            {
                await dbContext.DisposeAsync();
            }
        }

        try
        {
            SendSummaryEmail(latestSetups, DateTime.Now);
            Console.WriteLine("Email sent via EmailService to configured EMAIL_TO.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Email failed; scan results were still written: {ex.Message}");
        }

        return new ScanResult
        {
            Setups = latestSetups,
            InsertedCount = inserted,
            DuplicateCount = duplicates,
            Errors = errors,
            OutPath = outPath,
            RawSetupCount = rawLatestSetups.Count
        };
    }

    public async Task EmailLatestResultsAsync(CancellationToken cancellationToken = default)
    {
        var outPath = Path.Combine(GetResultsDirectory(), "StockPatternAPIBotResults.json");
        if (!File.Exists(outPath))
            throw new FileNotFoundException($"StockPatternAPIBot results file was not found: {outPath}");

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outPath, cancellationToken));
        var root = document.RootElement;
        var setups = root.TryGetProperty("setups", out var setupsElement) && setupsElement.ValueKind == JsonValueKind.Array
            ? setupsElement.EnumerateArray()
                .Select(element => JsonSerializer.Deserialize<StockSetups>(element.GetRawText(), JsonOptions))
                .Where(setup => setup != null)
                .Cast<StockSetups>()
                .ToList()
            : [];
        var generatedAt = root.TryGetProperty("generatedAt", out var generatedAtElement)
                          && generatedAtElement.TryGetDateTime(out var parsedGeneratedAt)
            ? parsedGeneratedAt
            : DateTime.Now;

        try
        {
            await using var dbContext = CreateDbContext();
            var persistResult = await PersistSetupsAsync(dbContext, setups, cancellationToken);
            Console.WriteLine(
                $"Database persistence complete. Inserted={persistResult.Inserted}; skipped duplicates={persistResult.Duplicates}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database persistence failed; continuing with email: {ex.Message}");
        }

        try
        {
            SendSummaryEmail(setups, generatedAt);
            Console.WriteLine("Email sent via EmailService to configured EMAIL_TO.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Email failed: {ex.Message}");
            throw;
        }
    }

    public async Task EmailOpenUnfinalizedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var setups = await db.SPA_StockSetups
            .AsNoTracking()
            .Where(s => !s.IsFinalized)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.RewardToRisk)
            .ToListAsync(cancellationToken);
        Console.WriteLine($"Open unfinalized setups: {setups.Count}");
        foreach (var s in setups)
            Console.WriteLine($"  {s.Ticker} {s.Date:u} {s.Signal} R:R={s.RewardToRisk}");
        _email.SendSetupsDigest(setups, DateTime.Now, "Open unfinalized setups (watchlist)");
        Console.WriteLine("Email sent via EmailService to configured EMAIL_TO.");
    }
    private StockPatternDbContext CreateDbContext()
    {
        var connectionString = _configuration.GetConnectionString("StockPatternApi");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string 'StockPatternApi' was not found in appsettings.json.");

        var options = new DbContextOptionsBuilder<StockPatternDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new StockPatternDbContext(options);
    }

    private static async Task<(int Inserted, int Duplicates)> PersistSetupsAsync(
        StockPatternDbContext dbContext,
        IEnumerable<StockSetups> setups,
        CancellationToken cancellationToken = default)
    {
        var candidates = setups
            .GroupBy(s => s.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(s => s.Date).First())
            .ToList();
        if (candidates.Count == 0)
            return (0, 0);

        var tickers = candidates.Select(s => s.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = await dbContext.SPA_StockSetups
            .AsNoTracking()
            .Where(s => tickers.Contains(s.Ticker))
            .Select(s => new { s.Ticker, s.Date })
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(s => (s.Ticker, s.Date))
            .ToHashSet();

        var newRows = candidates
            .Where(s => !existingKeys.Contains((s.Ticker, s.Date)))
            .Select(CloneSetup)
            .ToList();
        if (newRows.Count > 0)
        {
            dbContext.SPA_StockSetups.AddRange(newRows);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (newRows.Count, candidates.Count - newRows.Count);
    }

    private static StockSetups CloneSetup(StockSetups source) => new()
    {
        Ticker = source.Ticker,
        Date = source.Date,
        Close = source.Close,
        High = source.High,
        Low = source.Low,
        Volume = source.Volume,
        VolMA = source.VolMA,
        Trend = source.Trend,
        Setup = source.Setup,
        Signal = source.Signal,
        ResistanceLevel = source.ResistanceLevel,
        BreakoutPrice = source.BreakoutPrice,
        IsFinalized = source.IsFinalized,
        Compression = source.Compression,
        HighSlope = source.HighSlope,
        LowSlope = source.LowSlope,
        SmoothedATR = source.SmoothedATR,
        StopLoss = source.StopLoss,
        TakeProfit = source.TakeProfit,
        RiskPerShare = source.RiskPerShare,
        RewardPerShare = source.RewardPerShare,
        RewardToRisk = source.RewardToRisk
    };

    private void SendSummaryEmail(IEnumerable<StockSetups> setups, DateTime generatedAt)
    {
        var orderedSetups = RankAndCapPublished(setups, PublishCap);
        _email.SendSetupsDigest(orderedSetups, generatedAt);
    }

    internal static bool IsBreakoutSignal(string signal) =>
        signal.Contains("Breakout", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSetupSignal(string signal) =>
        signal.Contains("Setup", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAPlusOrGood(string signal) =>
        signal.Contains("A+", StringComparison.OrdinalIgnoreCase)
        || signal.Contains("Good", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Publish only actionable setups: held Breakouts (incl RR soft), or fresh A+/Good forming with R:R &gt;= 2.
    /// OK forming setups are dropped.
    /// </summary>
    internal static bool PassesActionablePublishGate(
        StockSetups setup,
        double latestFullSeriesClose,
        IReadOnlySet<DateTime> latestTwoSessionDays)
    {
        if (IsBreakoutSignal(setup.Signal))
            return latestFullSeriesClose >= setup.BreakoutPrice;

        if (IsSetupSignal(setup.Signal))
        {
            if (setup.RewardToRisk < FormingMinRewardToRisk)
                return false;
            if (!IsAPlusOrGood(setup.Signal))
                return false; // drop OK
            return latestTwoSessionDays.Contains(setup.Date.Date);
        }

        return false;
    }

    internal static bool PassesBreakoutVolumeGate(StockSetups setup)
    {
        if (!IsBreakoutSignal(setup.Signal))
            return true;
        if (setup.VolMA <= 0)
            return false;
        return setup.Volume >= BreakoutMinVolumeVsMa * setup.VolMA;
    }

    /// <summary>
    /// Approximate Enhanced EM cloud washout-reclaim: tumble from recent swing high is OK;
    /// freefall with no reclaim (CVS-like) is rejected.
    /// </summary>
    internal static bool PassesWashoutReclaimGate(
        StockSetups setup,
        IReadOnlyList<GetHistoricalData> history,
        double latestFullSeriesClose)
    {
        var barsUpTo = history.Where(h => h.Date <= setup.Date).ToList();
        if (barsUpTo.Count == 0)
            return false;

        var sessions = barsUpTo
            .GroupBy(h => h.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var last = g.OrderByDescending(x => x.Date).First();
                return (
                    Date: g.Key,
                    High: g.Max(x => x.High),
                    Low: g.Min(x => x.Low),
                    Close: last.Close
                );
            })
            .ToList();

        var setupIdx = sessions.FindLastIndex(s => s.Date == setup.Date.Date);
        if (setupIdx < 0)
            setupIdx = sessions.Count - 1;
        if (setupIdx < 3)
            return false;

        var lookback = Math.Clamp(setupIdx, WashoutLookbackMinSessions, WashoutLookbackMaxSessions);
        var windowStart = Math.Max(0, setupIdx - lookback);
        var prior = sessions.Skip(windowStart).Take(setupIdx - windowStart).ToList();
        if (prior.Count < 3)
            return false;

        var swingHigh = prior[0].High;
        var swingHighDate = prior[0].Date;
        for (var i = 1; i < prior.Count; i++)
        {
            if (prior[i].High >= swingHigh)
            {
                swingHigh = prior[i].High;
                swingHighDate = prior[i].Date;
            }
        }

        var afterSwing = sessions
            .Where(s => s.Date > swingHighDate && s.Date <= setup.Date.Date)
            .ToList();
        if (afterSwing.Count == 0)
            return false;

        var washoutLow = afterSwing.Min(s => s.Low);
        var atr = setup.SmoothedATR > 0 ? setup.SmoothedATR : Math.Max(swingHigh * 0.01, 1e-6);
        var minDepth = Math.Max(WashoutMinAtrMult * atr, WashoutMinPct * swingHigh);
        var depth = swingHigh - washoutLow;
        if (depth < minDepth)
            return false;

        var midpoint = (washoutLow + swingHigh) / 2.0;
        var isBreakout = IsBreakoutSignal(setup.Signal);
        var breakoutHold = isBreakout && latestFullSeriesClose >= setup.BreakoutPrice;
        var reclaim = setup.Close > midpoint || (isBreakout && setup.Close >= setup.BreakoutPrice);

        // Freefall: last 3 full-series session closes all lower, still below midpoint, no breakout hold.
        var recentCloses = history
            .GroupBy(h => h.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderByDescending(x => x.Date).First().Close)
            .TakeLast(3)
            .ToList();
        var freefall = recentCloses.Count == 3
                       && recentCloses[1] < recentCloses[0]
                       && recentCloses[2] < recentCloses[1]
                       && setup.Close < midpoint
                       && !breakoutHold;

        if (freefall)
            return false;

        return reclaim;
    }

    private static List<StockSetups> DeduplicateByTickerDay(IEnumerable<StockSetups> setups) =>
        setups
            .GroupBy(s => new { s.Ticker, Day = s.Date.Date })
            .Select(g => g.OrderByDescending(x => x.Date).First())
            .ToList();

    /// <summary>Breakouts first, then Date desc, then RewardToRisk desc; optional cap.</summary>
    private static List<StockSetups> RankAndCapPublished(IEnumerable<StockSetups> setups, int cap) =>
        setups
            .OrderByDescending(s => IsBreakoutSignal(s.Signal))
            .ThenByDescending(s => s.Date)
            .ThenByDescending(s => s.RewardToRisk)
            .ThenBy(s => s.Ticker, StringComparer.OrdinalIgnoreCase)
            .Take(cap)
            .ToList();

    private static async Task<List<GetHistoricalData>> FetchYahoo30mAsync(
        HttpClient http,
        string ticker,
        DateTime startDate,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(ticker)}?interval=30m&range=1mo&includePrePost=false";
        using var resp = await http.GetAsync(url, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}: {body[..Math.Min(180, body.Length)]}");

        using var doc = JsonDocument.Parse(body);
        var result = doc.RootElement.GetProperty("chart").GetProperty("result");
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return [];

        var r0 = result[0];
        if (!r0.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.Array)
            return [];

        var quote = r0.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var list = new List<GetHistoricalData>();
        for (int i = 0; i < tsEl.GetArrayLength(); i++)
        {
            if (closes[i].ValueKind == JsonValueKind.Null || highs[i].ValueKind == JsonValueKind.Null ||
                lows[i].ValueKind == JsonValueKind.Null)
                continue;
            if (opens[i].ValueKind == JsonValueKind.Null)
                continue;

            var dt = DateTimeOffset.FromUnixTimeSeconds(tsEl[i].GetInt64()).LocalDateTime;
            if (dt < startDate) continue;

            list.Add(new GetHistoricalData
            {
                Date = dt,
                Open = opens[i].GetDouble(),
                Close = closes[i].GetDouble(),
                High = highs[i].GetDouble(),
                Low = lows[i].GetDouble(),
                Volume = volumes[i].ValueKind == JsonValueKind.Null ? 0 : volumes[i].GetInt64()
            });
        }

        return list.OrderBy(x => x.Date).ToList();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPatternApi.Helpers;
using StockPatternApi.Models;
using StockPatternApi.Services;
using System.Text.Json;

namespace StockPatternApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StockController(StockPatternDbContext context) : ControllerBase
    {
        #region Stock Controller
        private readonly string API_KEY = Keys.API_KEY;
        private readonly HttpClient httpClient = new();
        private readonly StockPatternDbContext dbContext = context;

        #region GET Stock Setups
        [HttpGet("getStockSetups")]
        public async Task<IActionResult> GetStockSetups([FromQuery] string[] tickers, [FromQuery] int lookback = 12)
        {
            try
            {
                var allSetups = new List<StockSetups>();
                var sendMailNotifcation = new EmailService();
                var symbols = (tickers != null && tickers.Length != 0) ? tickers : StockSymbols.Tickers;

                if (symbols == null || symbols.Length == 0)
                    return BadRequest("At least one ticker is required.");

                DateTime startDate = DateTime.UtcNow.AddDays(-(lookback + 50));

                // Check for existing unfinalized setups
                var unfinalizedSetups = dbContext.SPA_StockSetups
                    .Where(s => !s.IsFinalized)
                    .Select(s => s.Ticker)
                    .ToHashSet();

                foreach (var ticker in symbols)
                {
                    // Skip if there’s an unfinalized setup for this ticker
                    if (unfinalizedSetups.Contains(ticker)) continue;

                    var stockHistory = await GetHistoricalData(ticker.ToUpper(), startDate);
                    if (stockHistory == null || stockHistory.Count == 0) continue;

                    var existingSetups = dbContext.SPA_StockSetups
                        .Where(s => s.Ticker == ticker)
                        .Select(s => s.Date)
                        .ToHashSet();

                    var setups = Algorithm.WedgePatternDetector.Detect(ticker.ToUpper(), stockHistory, existingSetups);
                    if (setups != null && setups.Count > 0)
                    {
                        allSetups.AddRange(setups);
                    }
                }

                // Group by Ticker and select the latest setup per ticker
                var latestSetups = allSetups
                    .GroupBy(s => s.Ticker)
                    .Select(g => g.OrderByDescending(x => x.Date).First())
                    .ToList();

                var existing = dbContext.SPA_StockSetups
                    .Select(s => new { s.Ticker, s.Date })
                    .ToHashSet();

                var newSetups = latestSetups
                    .Where(s => !existing.Contains(new { s.Ticker, s.Date }))
                    .ToList();

                if (newSetups.Count > 0)
                {
                    dbContext.SPA_StockSetups.AddRange(newSetups);
                    await dbContext.SaveChangesAsync();
                    sendMailNotifcation.SendEmail(latestSetups);
                }

                return latestSetups.Count > 0
                    ? Ok(latestSetups)
                    : NotFound("No wedge setups found for any ticker.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"There was an error returning results. Error Message: {ex.Message}");
            }
        }
        #endregion

        #region GET Historical Data
        private async Task<List<GetHistoricalData>> GetHistoricalData(string ticker, DateTime startDate)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    DateTime endDate = DateTime.Now;
                    var intradaySetupUrl = $"https://financialmodelingprep.com/api/v3/historical-chart/30min/{ticker}?from={startDate:yyyy-MM-dd}&to={endDate:yyyy-MM-dd}&apikey={API_KEY}";
                    var response = await httpClient.GetStringAsync(intradaySetupUrl);
                    var json = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(response);

                    if (json == null || json.Count == 0)
                    {
                        throw new Exception("Invalid response from Financial Modeling Prep.");
                    }

                    var timeSeries = json
                        .Where(x => x.ContainsKey("date") &&
                                    x.ContainsKey("close") &&
                                    x.ContainsKey("high") &&
                                    x.ContainsKey("low") &&
                                    x.ContainsKey("volume") &&
                                    DateTime.TryParse(x["date"].GetString(), out var date) && date >= startDate)

                        .Select(x => new GetHistoricalData
                        {
                            Date = DateTime.Parse(x["date"].GetString()!),
                            Close = x["close"].GetDouble(),
                            High = x["high"].GetDouble(),
                            Low = x["low"].GetDouble(),
                            Volume = x["volume"].GetInt64()
                        })
                        .OrderBy(x => x.Date)
                        .ToList();
                    return timeSeries;
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
            throw new Exception("Failed to fetch historical data after multiple attempts.");
        }
        #endregion

        #region Other GET calls
        [HttpGet("getAllExistingSetups")]
        public IActionResult GetAllExistingSetups()
        {
            try
            {
                var setups = dbContext.SPA_StockSetups
                    .Where(s => !s.IsFinalized)
                    .OrderByDescending(s => s.Date)
                    .ToList();

                return setups.Count > 0 ? Ok(setups) : NotFound("No setups found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching setups: {ex.Message}");
            }
        }

        [HttpGet("getAllJournalEntries")]
        public IActionResult GetAllJournalEntries()
        {
            try
            {
                var journalEntries = dbContext.SPA_JournalEntries
                    .Where(s => s.IsActive)
                    .OrderByDescending(s => s.Date)
                    .ToList();

                return journalEntries.Count > 0 ? Ok(journalEntries) : NotFound("No journal entries found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching setups: {ex.Message}");
            }
        }
        #endregion

        #region POST calls
        [HttpPost("saveToFinalResults")]
        public async Task<IActionResult> SaveToFinalResults([FromBody] FinalResults data)
        {
            if (data == null || data.StockSetupId <= 0)
                return BadRequest("Invalid data.");

            try
            {
                var finalResult = new FinalResults
                {
                    StockSetupId = data.StockSetupId,
                    DateUpdated = DateTime.Now,
                    PriceSoldAt = data.PriceSoldAt,
                    IsActive = data.IsActive,
                    IsFalsePositive = data.IsFalsePositive
                };

                dbContext.SPA_FinalResults.Add(finalResult);

                var stockSetup = await dbContext.SPA_StockSetups
                    .FirstOrDefaultAsync(s => s.Id == data.StockSetupId);

                if (stockSetup == null)
                    return NotFound("Stock setup not found.");

                stockSetup.IsFinalized = true;

                await dbContext.SaveChangesAsync();
                return Ok("Data saved successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error saving closing price. Error Message: " + ex.Message);
            }
        }

        [HttpPost("saveToJournalEntries")]
        public async Task<IActionResult> SaveToJournalEntries([FromBody] JournalEntries journalEntry)
        {
            if (string.IsNullOrEmpty(journalEntry.EntryBody) || string.IsNullOrEmpty(journalEntry.EntrySubject))
                return BadRequest("Please review your entry. You haven't filled out what's required.");

            dbContext.SPA_JournalEntries.Add(journalEntry);
            await dbContext.SaveChangesAsync();
            return CreatedAtAction(nameof(SaveToJournalEntries), new { id = journalEntry.Id }, journalEntry);
        }
        #endregion

        #region Reports
        [HttpGet("getFinalResultsReport")]
        public async Task<IActionResult> GetFinalResultsReport()
        {
            try
            {
                var finalResults = await dbContext.FinalResultsReport
                    .FromSqlRaw("EXEC usp_SPA_getFinalResults")
                    .ToListAsync();

                return Ok(finalResults);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error executing stored procedure! Error Message: {ex.Message}");
            }
        }

        [HttpGet("getAggregatedSummaryReport")]
        public async Task<IActionResult> GetAggregatedSummaryReport()
        {
            try
            {
                var aggregatedSummaryResults = await dbContext.AggregatedSummaryReport
                    .FromSqlRaw("EXEC usp_SPA_getAggregatedSummary")
                    .ToListAsync();

                return Ok(aggregatedSummaryResults);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error executing stored procedure! Error Message: {ex.Message}");
            }
        }
        #endregion
    }
    #endregion 
}

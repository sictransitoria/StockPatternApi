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
        private readonly HttpClient httpClient = new HttpClient();
        private readonly StockPatternDbContext dbContext = context;

        #region GET Stock Setups
        [HttpGet("getStockSetups")]
        public async Task<IActionResult> GetBatchSetups([FromQuery] string[] tickers, [FromQuery] int lookback = 10)
        {
            try
            {
                var allSetups = new List<StockSetups>();
                var emailService = new EmailService();
                var symbols = (tickers != null && tickers.Length != 0) ? tickers : StockSymbols.Tickers;

                if (symbols == null || symbols.Length == 0)
                    return BadRequest("At least one ticker is required.");

                DateTime startDate = DateTime.UtcNow.AddDays(-(lookback + 50));

                foreach (var ticker in symbols)
                {
                    var stockHistory = await GetHistoricalData(ticker.ToUpper(), startDate);
                    if (stockHistory == null || stockHistory.Count == 0)
                        continue;

                    var existingSetups = dbContext.SPA_StockSetups
                        .Where(s => s.Ticker == ticker)
                        .Select(s => s.Date)
                        .ToHashSet();

                    var setups = Functions.WedgePatternDetector.Detect(ticker.ToUpper(), stockHistory, existingSetups);
                    if (setups != null && setups.Count > 0)
                        allSetups.AddRange(setups);
                }

                var latestSetups = allSetups
                    .GroupBy(s => s.Ticker)
                    .Select(g => g.OrderByDescending(x => x.Date).First())
                    .ToList();

                if (latestSetups.Count > 0)
                {
                    dbContext.SPA_StockSetups.AddRange(latestSetups);
                    await dbContext.SaveChangesAsync();
                    emailService.SendEmail(latestSetups);
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
                    var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={ticker}&outputsize=full&apikey={API_KEY}";
                    var response = await httpClient.GetStringAsync(url);
                    var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(response);

                    if (json == null || !json.ContainsKey("Time Series (Daily)"))
                        throw new Exception("Invalid response from Alpha Vantage.");

                    var timeSeries = json["Time Series (Daily)"].EnumerateObject()
                        .Select(x => new
                        {
                            Date = DateTime.Parse(x.Name),
                            Data = x.Value
                        })
                        .Where(x => x.Date >= startDate)
                        .Select(x => new GetHistoricalData
                        {
                            Date = x.Date,
                            Close = double.Parse(x.Data.GetProperty("4. close").GetString()),
                            High = double.Parse(x.Data.GetProperty("2. high").GetString()),
                            Low = double.Parse(x.Data.GetProperty("3. low").GetString()),
                            Volume = long.Parse(x.Data.GetProperty("5. volume").GetString())
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
                    ClosingPrice = data.ClosingPrice
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

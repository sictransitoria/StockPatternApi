using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPatternApi.Data;
using StockPatternApi.Models;
using StockPatternApi.Services;
using System.Text.Json;

namespace StockPatternApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StockController : ControllerBase
    {
        private readonly string API_KEY = Keys.API_KEY;
        private readonly HttpClient httpClient = new HttpClient();
        private readonly StockPatternDbContext dbContext;

        public StockController(StockPatternDbContext context)
        {
            dbContext = context;
        }

        [HttpGet("getStockSetups")]
        public async Task<IActionResult> GetBatchSetups([FromQuery] string[] tickers, [FromQuery] int lookback = 10)
        {
            try
            {
                var results = new List<object>();
                var emailService = new EmailService();

                var symbols = (tickers != null && tickers.Length != 0) ? tickers : NasdaqTickers.Tickers;

                if (symbols == null || symbols.Length == 0)
                {
                    return BadRequest("At least one ticker is required.");
                }

                foreach (var ticker in symbols)
                {
                    var setups = await DetectWedgePatterns(ticker.ToUpper(), lookback);
                    if (setups != null && setups.Count != 0)
                    {
                        results.AddRange(setups.Select(s => new
                        {
                            Ticker = ticker,
                            Setup = s
                        }));
                    }
                }
                if (results.Count > 0)
                {
                    emailService.SendEmail(results);
                }
                return results.Count != 0 ? Ok(results) : NotFound("As it turns out, there was no setups found for any ticker.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"There was an error returning results. Error Message: {ex.Message}");
            }
        }

        private async Task<IReadOnlyList<GetHistoricalData>> GetHistoricalData(string ticker, DateTime startDate)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={ticker}&outputsize=full&apikey={API_KEY}";
                    var response = await httpClient.GetStringAsync(url);
                    var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(response);

                    if (json == null || !json.ContainsKey("Time Series (Daily)"))
                    {
                        throw new Exception("Invalid response from Alpha Vantage.");
                    }

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

        private async Task<List<object>> DetectWedgePatterns(string ticker, int lookback)
        {
            const int buffer = 50;
            const int volMaWindow = 10;
            DateTime startDate = DateTime.UtcNow.AddDays(-(lookback + buffer));
            DateTime cutoffDate = GetMostRecentTradingDay(2);

            var stockHistory = await GetHistoricalData(ticker, startDate);
            if (stockHistory == null || !stockHistory.Any())
            {
                throw new Exception($"No data returned for {ticker}. Possibly invalid symbol.");
            }

            var data = stockHistory.ToList();
            if (data.Count < lookback) return new List<object>(); // fix early return

            var existingSetups = dbContext.SPA_StockSetups
                .Where(s => s.Ticker == ticker && s.IsFinalized)
                .Select(s => s.Date)
                .ToHashSet();

            double sma50 = data.Count >= 50 ? data.TakeLast(50).Average(d => d.Close) : data.Average(d => d.Close);

            var result = new List<object>();
            double[] volumes = new double[volMaWindow];
            int volIndex = 0;
            double volSum = 0;

            for (int i = lookback; i < data.Count; i++)
            {
                if (i >= volMaWindow)
                {
                    volSum -= volumes[volIndex];
                    volSum += data[i].Volume;
                    volumes[volIndex] = data[i].Volume;
                    volIndex = (volIndex + 1) % volMaWindow;
                }
                else
                {
                    volumes[volIndex] = data[i].Volume;
                    volSum += data[i].Volume;
                    volIndex++;
                }

                double highs = double.MinValue;
                double lows = double.MaxValue;

                for (int j = i - lookback; j < i; j++)
                {
                    highs = Math.Max(highs, data[j].High);
                    lows = Math.Min(lows, data[j].Low);
                }

                bool lowerHighs = i > 0 && data[i].High < data[i - 1].High;
                bool higherLows = i > 0 && data[i].Low > data[i - 1].Low;
                bool wedge = lowerHighs && higherLows;  // Wedge condition: lower highs & higher lows

                double highSlope = (data[i - 1].High - data[i - lookback].High) / lookback;
                double lowSlope = (data[i - 1].Low - data[i - lookback].Low) / lookback;
                bool isWedgePattern = highSlope < 0 && lowSlope > 0;  // Sloping wedge pattern

                double volMA = i >= volMaWindow ? volSum / volMaWindow : volSum / (i + 1);
                bool decVol = data[i].Volume < volMA;
                bool trend = data[i].Close > sma50;
                bool setup = trend && wedge && decVol;  // Valid setup: trend + wedge + low volume

                double compression = (highs - lows) / highs;  // Price range compression

                if (setup && data[i].Date >= cutoffDate)
                {
                    if (!existingSetups.Contains(data[i].Date))
                    {
                        var wedgeData = data.Skip(i - lookback).Take(lookback).ToList();

                        var resistancePoints = wedgeData
                            .Select((d, idx) => new { X = idx, Y = d.High })
                            .ToList();

                        double avgX = resistancePoints.Average(p => p.X);
                        double avgY = resistancePoints.Average(p => p.Y);

                        double numerator = resistancePoints.Sum(p => (p.X - avgX) * (p.Y - avgY));
                        double denominator = resistancePoints.Sum(p => Math.Pow(p.X - avgX, 2));
                        double slope = denominator == 0 ? 0 : numerator / denominator;
                        double intercept = avgY - slope * avgX;

                        int nextIndex = lookback;
                        double resistanceLevel = slope * nextIndex + intercept;
                        double breakoutPrice = resistanceLevel * 1.002;

                        resistanceLevel = Math.Round(resistanceLevel, 2);
                        breakoutPrice = Math.Round(breakoutPrice, 2);

                        string signal = compression < 0.05 ? "A+ Wedge Setup"
                                      : compression < 0.1 ? "Good Wedge Setup"
                                      : "Wedge Pattern Detected";

                        result.Add(new
                        {
                            data[i].Date,
                            Trend = trend,
                            data[i].Close,
                            data[i].High,
                            data[i].Low,
                            data[i].Volume,
                            Setup = setup,
                            VolMA = volMA,
                            Signal = signal,
                            IsWedgePattern = isWedgePattern,
                            ResistanceLevel = resistanceLevel,
                            BreakoutPrice = breakoutPrice,
                        });

                        dbContext.SPA_StockSetups.Add(new StockSetups
                        {
                            Ticker = ticker,
                            Date = data[i].Date,
                            Close = data[i].Close,
                            High = data[i].High,
                            Low = data[i].Low,
                            Volume = data[i].Volume,
                            Trend = trend,
                            Setup = setup,
                            VolMA = volMA,
                            IsFinalized = false,
                            Signal = signal,
                            IsWedgePattern = isWedgePattern,
                            ResistanceLevel = resistanceLevel,
                            BreakoutPrice = breakoutPrice
                        });
                    }
                }
            }
            await dbContext.SaveChangesAsync();
            return result;
        }

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

        [HttpPost("saveToFinalResults")]
        public async Task<IActionResult> SaveToFinalResults([FromBody] FinalResults data)
        {
            if (data == null || data.StockSetupId <= 0)
            {
                return BadRequest("Invalid data.");
            }

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
                {
                    return NotFound("Stock setup not found.");
                }

                stockSetup.IsFinalized = true;

                await dbContext.SaveChangesAsync();
                return Ok("Data saved successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error saving closing price. Error Message: " + ex.Message);
            }
        }

        public static DateTime GetMostRecentTradingDay(int tradingDaysAgo)
        {
            DateTime date = DateTime.Today;
            int count = 0;

            while (count < tradingDaysAgo)
            {
                date = date.AddDays(-1);
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                {
                    count++;
                }
            }
            return date;
        }

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
    }
}
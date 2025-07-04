using Microsoft.AspNetCore.Mvc;
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
                    var setups = await DetectPattern(ticker.ToUpper(), lookback);
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

        private async Task<List<object>> DetectPattern(string ticker, int lookback)
        {
            const int buffer = 50;
            const int volMaWindow = 10;
            DateTime startDate = DateTime.UtcNow.AddDays(-(lookback + buffer));

            var stockHistory = await GetHistoricalData(ticker, startDate);
            if (stockHistory == null || !stockHistory.Any())
            {
                throw new Exception($"No data returned for {ticker}. Possibly invalid symbol.");
            }

            var data = stockHistory.ToList();
            if (data.Count < lookback) return []; // Early exit if insufficient data

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

                data[i].SMA50 = sma50;
                data[i].Trend = data[i].Close > sma50;
                data[i].Highs = 0;
                data[i].Lows = double.MaxValue;
                for (int j = i - lookback; j < i; j++) // Direct index access
                {
                    data[i].Highs = Math.Max(data[i].Highs, data[j].High);
                    data[i].Lows = Math.Min(data[i].Lows, data[j].Low);
                }
                data[i].LowerHighs = i > 0 && data[i].High < data[i - 1].High;
                data[i].HigherLows = i > 0 && data[i].Low > data[i - 1].Low;
                data[i].Wedge = data[i].LowerHighs && data[i].HigherLows;
                data[i].VolMA = i >= volMaWindow ? volSum / volMaWindow : volSum / (i + 1);
                data[i].DecVol = data[i].Volume < data[i].VolMA;
                data[i].Setup = data[i].Trend && data[i].Wedge && data[i].DecVol;

                if (data[i].Setup && data[i].Date >= DateTime.Now.AddDays(-2))
                {
                    bool checkIfRecordExists = dbContext.SPA_StockSetups.Any(s => s.Ticker == ticker && s.Date == data[i].Date);

                    if (!checkIfRecordExists)
                    {
                        result.Add(new
                        {
                            data[i].Date,
                            data[i].Trend,
                            data[i].Close,
                            data[i].High,
                            data[i].Low,
                            data[i].Volume,
                            data[i].Setup,
                            data[i].VolMA,
                            Signal = "Wedge Pattern Detected"
                        });

                        var setupRecord = new StockSetups
                        {
                            Ticker = ticker,
                            Date = data[i].Date,
                            Close = data[i].Close,
                            High = data[i].High,
                            Low = data[i].Low,
                            Volume = data[i].Volume,
                            Trend = data[i].Trend,
                            Setup = data[i].Setup,
                            VolMA = data[i].VolMA,
                            Signal = "Wedge Pattern Detected"
                        };
                        dbContext.SPA_StockSetups.Add(setupRecord);
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
                    .OrderByDescending(s => s.Date)
                    .ToList();

                return Ok(setups);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching setups: {ex.Message}");
            }
        }
    }
}
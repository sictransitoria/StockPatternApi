using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using StockPatternApi.Models;
using StockPatternApi.Services;
using StockPatternApi.Data;

namespace StockPatternApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StockController : ControllerBase
    {
        private readonly string API_KEY = Keys.API_KEY;
        private readonly HttpClient _httpClient = new HttpClient();

        [HttpGet("getSetups")]
        public async Task<IActionResult> GetBatchSetups([FromQuery] string[] tickers, [FromQuery] string period = "6mo", [FromQuery] int lookback = 10)
        {
            try
            {
                var results = new List<object>();
                var emailService = new EmailService();
                foreach (var ticker in NasdaqTickers.Tickers)
                {
                    var setups = await DetectPattern(ticker.ToUpper(), period, lookback);
                    if (setups != null && setups.Any())
                    {
                        results.AddRange(setups.Select(s => new { Ticker = ticker, Setup = s }));
                        emailService.SendEmail(results);
                    }
                }

                if (NasdaqTickers.Tickers == null || !NasdaqTickers.Tickers.Any())
                    return BadRequest("At least one ticker is required.");

                return results.Any() ? Ok(results) : NotFound("No setups found for any ticker.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        private async Task<IReadOnlyList<StockDataModel>> GetHistoricalData(string ticker, DateTime startDate)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={ticker}&outputsize=full&apikey={API_KEY}";
                    var response = await _httpClient.GetStringAsync(url);
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
                        .Select(x => new StockDataModel
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

        private async Task<List<object>> DetectPattern(string ticker, string period, int lookback)
        {
            DateTime startDate = period switch
            {
                "1mo" => DateTime.Now.AddMonths(-1),
                "3mo" => DateTime.Now.AddMonths(-3),
                "6mo" => DateTime.Now.AddMonths(-6),
                _ => DateTime.Now.AddMonths(-6)
            };

            var stockHistory = await GetHistoricalData(ticker, startDate);

            if (stockHistory == null || !stockHistory.Any())
                throw new Exception($"No data returned for {ticker}. Possibly invalid symbol.");

            var data = stockHistory.ToList();

            for (int i = 0; i < data.Count; i++)
            {
                data[i].SMA50 = i >= 50 ? data.Skip(i - 50).Take(50).Average(d => d.Close) : 0;
                data[i].Trend = data[i].Close > data[i].SMA50;
                data[i].Highs = i >= lookback ? data.Skip(i - lookback).Take(lookback).Max(d => d.High) : 0;
                data[i].Lows = i >= lookback ? data.Skip(i - lookback).Take(lookback).Min(d => d.Low) : 0;
                data[i].LowerHighs = i > 0 && data[i].High < data[i - 1].High;
                data[i].HigherLows = i > 0 && data[i].Low > data[i - 1].Low;
                data[i].Wedge = data[i].LowerHighs && data[i].HigherLows;
                data[i].VolMA = i >= 10 ? data.Skip(i - 10).Take(10).Average(d => d.Volume) : 0;
                data[i].DecVol = data[i].Volume < data[i].VolMA;
                data[i].Setup = i >= 50 && data[i].Trend && data[i].Wedge && data[i].DecVol;
            }

            return data.Where(d => d.Setup).Select(d => new
            {
                d.Date,
                d.Trend,
                d.Close,
                d.High,
                d.Low,
                d.Volume,
                d.Setup,
                d.VolMA,
                Signal = "Wedge Pattern Detected"
            }).ToList<object>();
        }
    }
}
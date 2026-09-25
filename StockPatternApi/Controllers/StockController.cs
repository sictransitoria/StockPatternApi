using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPatternApi.Helpers;
using StockPatternApi.Models;
using StockPatternApi.Services;

namespace StockPatternApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StockController(StockPatternDbContext context, StockPatternScanService scanService) : ControllerBase
    {
        #region Stock Controller
        private readonly StockPatternDbContext dbContext = context;
        private readonly StockPatternScanService _scanService = scanService;

        #region GET Stock Setups
        [HttpGet("getStockSetups")]
        public async Task<IActionResult> GetStockSetups([FromQuery] string[] tickers, [FromQuery] int lookback = 12)
        {
            try
            {
                var symbols = (tickers != null && tickers.Length != 0) ? tickers : null;
                if (symbols == null && (StockSymbols.Tickers == null || StockSymbols.Tickers.Length == 0))
                    return BadRequest("At least one ticker is required.");

                var result = await _scanService.ScanAsync(new ScanOptions
                {
                    Tickers = symbols,
                    Lookback = lookback,
                    WriteJson = false
                });

                return result.Setups.Count > 0
                    ? Ok(result.Setups)
                    : NotFound("No wedge setups found for any ticker.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"There was an error returning results. Error Message: {ex.Message}");
            }
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
                    .ThenByDescending(s => s.RewardToRisk)
                    .ThenByDescending(s => s.RiskPerShare)
                    .ThenByDescending(s => s.RewardPerShare)
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

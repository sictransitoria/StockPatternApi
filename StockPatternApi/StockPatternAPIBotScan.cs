namespace StockPatternApi;

/// <summary>
/// Thin CLI entry for --stockpatternapibot-scan / --stockpatternapibot-email.
/// Scan pipeline lives in <see cref="Services.StockPatternScanService"/>.
/// </summary>
public static class StockPatternAPIBotScan
{
    public static async Task RunAsync()
    {
        var (service, http) = Services.StockPatternScanService.CreateStandalone();
        using (http)
        {
            await service.ScanAsync(new Services.ScanOptions { WriteJson = true });
        }
    }

    public static async Task EmailLatestResultsAsync()
    {
        var (service, http) = Services.StockPatternScanService.CreateStandalone();
        using (http)
        {
            await service.EmailLatestResultsAsync();
        }
    }

    public static async Task EmailOpenUnfinalizedAsync()
    {
        var (service, http) = Services.StockPatternScanService.CreateStandalone();
        using (http)
        {
            await service.EmailOpenUnfinalizedAsync();
        }
    }
}

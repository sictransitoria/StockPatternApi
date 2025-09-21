--

SELECT
    Ticker,
	CONVERT(varchar, [Date], 22) 'ESTDate',
	RiskPerShare,
	RewardPerShare,
	RewardToRisk,
	ResistanceLevel,
    BreakoutPrice,
	StopLoss,
	TakeProfit,
	CASE 
        WHEN SmoothedATR <= 2 THEN 'LOW'
        WHEN SmoothedATR > 2 AND SmoothedATR <= 5 THEN 'MEDIUM'
        WHEN SmoothedATR > 5 THEN 'HIGH'
    END AS Volatility,
    Volume,
    VolMA,
    Signal,
    [Compression],
    HighSlope,
    LowSlope,
    SmoothedATR,
	IsFinalized
FROM  [StockPatternApi].[dbo].[SPA_StockSetups]
WHERE IsFinalized = 0
ORDER BY [Date] DESC, RewardToRisk DESC, RewardPerShare DESC, RiskPerShare DESC;

/*

TRUNCATE TABLE [StockPatternApi].[dbo].[SPA_FinalResults];
TRUNCATE TABLE [StockPatternApi].[dbo].[SPA_StockSetups];

DELETE FROM [StockPatternApi].[dbo].[SPA_StockSetups]
 WHERE IsFinalized = 0;

*/
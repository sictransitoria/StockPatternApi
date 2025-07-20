
/*

EXEC usp_SPA_getAggregatedSummary;

*/

CREATE OR ALTER PROCEDURE usp_SPA_getAggregatedSummary
AS
BEGIN
    SELECT
        COUNT(*) AS TotalTrades,
        SUM(CASE WHEN f.PriceSoldAt > s.[Close] THEN 1 ELSE 0 END) AS GreenCount,
        SUM(CASE WHEN f.PriceSoldAt <= s.[Close] THEN 1 ELSE 0 END) AS RedCount,
        ROUND(CAST(SUM(CASE WHEN f.PriceSoldAt > s.[Close] THEN 1 ELSE 0 END) AS FLOAT) * 100.0 / NULLIF(COUNT(*), 0), 2) AS SuccessRate,
        ROUND(AVG(CASE WHEN f.PriceSoldAt IS NOT NULL THEN ((f.PriceSoldAt - s.[Close]) / s.[Close]) * 100 ELSE 0 END), 2) AS AvgReturnPct,
        ROUND(MAX(CASE WHEN f.PriceSoldAt IS NOT NULL THEN ((f.PriceSoldAt - s.[Close]) / s.[Close]) * 100 ELSE 0 END), 2) AS BestTradePct,
        ROUND(MIN(CASE WHEN f.PriceSoldAt IS NOT NULL THEN ((f.PriceSoldAt - s.[Close]) / s.[Close]) * 100 ELSE 0 END), 2) AS WorstTradePct
    FROM SPA_StockSetups s
    LEFT JOIN SPA_FinalResults f ON s.Id = f.StockSetupId
    WHERE s.IsFinalized = 1 AND 
		  f.IsActive = 1
END;
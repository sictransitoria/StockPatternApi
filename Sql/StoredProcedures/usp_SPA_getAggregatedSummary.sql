/*

EXEC usp_SPA_getAggregatedSummary;

*/

CREATE OR ALTER PROCEDURE usp_SPA_getAggregatedSummary
AS
BEGIN
	SELECT
		  COUNT(*) AS TotalTrades,
		  SUM(CASE WHEN f.ClosingPrice > s.[Close] THEN 1 ELSE 0 END) AS GreenCount,
		  SUM(CASE WHEN f.ClosingPrice <= s.[Close] THEN 1 ELSE 0 END) AS RedCount,
		  ROUND(AVG(((f.ClosingPrice - s.[Close]) / s.[Close]) * 100), 2) AS AvgReturnPct,
		  ROUND(MAX(((f.ClosingPrice - s.[Close]) / s.[Close]) * 100), 2) AS BestTradePct,
		  ROUND(MIN(((f.ClosingPrice - s.[Close]) / s.[Close]) * 100), 2) AS WorstTradePct
	FROM  SPA_StockSetups s
	JOIN  SPA_FinalResults f ON s.Id = f.StockSetupId
	WHERE s.IsFinalized = 1
END;
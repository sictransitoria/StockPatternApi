CREATE OR ALTER PROCEDURE usp_SPA_getFinalResults

AS
BEGIN
    SELECT
		  s.Ticker AS Ticker,
		  ROUND(((f.ClosingPrice - s.[Close]) / s.[Close] * 100), 2) AS PercentageDifference, -- Return as numeric
		  CASE WHEN f.ClosingPrice > s.[Close] THEN 'GREEN' ELSE 'RED' END AS GreenOrRedDay,
		  CONVERT(VARCHAR(17), f.DateUpdated, 13) AS DateUpdated
	FROM  SPA_StockSetups s
	JOIN  SPA_FinalResults f ON s.Id = f.StockSetupId
	WHERE s.IsFinalized = 1
END;
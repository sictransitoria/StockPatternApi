/*

EXEC usp_SPA_getFinalResults;

*/

CREATE OR ALTER PROCEDURE usp_SPA_getFinalResults
AS
BEGIN
    SELECT
	      s.Ticker AS Ticker,
	      ROUND(((f.PriceSoldAt - s.[Close]) / s.[Close] * 100), 2) AS PercentageDifference,
	      CASE WHEN f.PriceSoldAt > s.[Close] THEN 'GREEN' ELSE 'RED' END AS GreenOrRedDay,
	      CONVERT(VARCHAR(17), f.DateUpdated, 13) AS DateUpdated
	FROM  SPA_StockSetups s
	JOIN  SPA_FinalResults f ON s.Id = f.StockSetupId
	WHERE s.IsFinalized = 1 AND 
	      f.IsActive = 1
END;

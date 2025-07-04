CREATE OR ALTER PROCEDURE usp_getStockResults
AS
BEGIN
    SELECT 
        s.Ticker 'Symbol'
    ,   CONCAT(ROUND(((f.ClosingPrice - s.[Close]) / s.[Close] * 100), 2), '%') 'PercentageDifference'
    ,   CASE WHEN f.IsSuccessful = 1 THEN 'GREEN' ELSE 'RED' END 'Result'
    ,   FORMAT(f.DateUpdated, 'MM-dd-yy hh:mm:ss') 'DateTime'
    FROM SPA_StockSetups s
    JOIN SPA_FinalResults f ON s.Id = f.StockSetupId
END;

namespace StockPatternApi.Helpers
{
    public class Functions
    {
        public static DateTime GetMostRecentTradingDay(int tradingDaysAgo)
        {
            DateTime date = DateTime.Today;
            int count = 0;

            while (count < tradingDaysAgo)
            {
                date = date.AddDays(-1);
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                {
                    count++;
                }
            }
            return date;
        }
    }
}

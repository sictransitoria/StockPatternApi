using StockPatternApi.Models;

namespace StockPatternApi.Helpers
{
    public static class Algorithm
    {
        public struct SlopeVariables
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        public static DateTime GetMostRecentTradingDay(int daysBack)
        {
            DateTime today = DateTime.UtcNow;
            int days = 0;
            while (days < daysBack)
            {
                today = today.AddDays(-1);
                if (today.DayOfWeek != DayOfWeek.Saturday && today.DayOfWeek != DayOfWeek.Sunday)
                    days++;
            }
            return today.Date;
        }

        public static double CalculateSlope(List<SlopeVariables> points)
        {
            if (points.Count < 2) return 0;
            double avgX = points.Average(p => p.X);
            double avgY = points.Average(p => p.Y);
            double numerator = points.Sum(p => (p.X - avgX) * (p.Y - avgY));
            double denominator = points.Sum(p => Math.Pow(p.X - avgX, 2));
            return denominator == 0 ? 0 : numerator / denominator;
        }

        public static double CalculateATR(List<GetHistoricalData> data, int index, int period)
        {
            if (index < period) return 0;
            return data.Skip(index - period).Take(period)
                .Average(d => Math.Max(d.High - d.Low, Math.Max(Math.Abs(d.High - d.Close), Math.Abs(d.Low - d.Close))));
        }

        public static double CalculateEMA(double current, double previousEMA, int period)
        {
            return (current * (2.0 / (1 + period))) + (previousEMA * (1 - 2.0 / (1 + period)));
        }

        public class WedgePatternDetector
        {
            private const int VolumeWindow = 20;
            private const int Lookback = 7;
            private const int UptrendLookback = 20;
            private const double HighSlopeThreshold = -0.1;
            private const double LowSlopeThreshold = 0.3;
            private const double ParallelSlopeThreshold = 0.05; // For flag detection
            private const int ATRPeriod = 7;
            private const int EMAPeriod = 3;

            public static List<StockSetups> Detect(string ticker, List<GetHistoricalData> data, HashSet<DateTime> existingSetups)
            {
                var results = new List<StockSetups>();
                DateTime scanCutoff = GetMostRecentTradingDay(0);

                if (data.Count < Lookback + VolumeWindow + ATRPeriod + UptrendLookback)
                    return results;

                double sma50 = data.TakeLast(50).Average(d => d.Close);

                // ATR + Smoothed ATR
                double[] atr = new double[data.Count];
                double[] emaATR = new double[data.Count];
                for (int i = ATRPeriod; i < data.Count; i++)
                {
                    atr[i] = CalculateATR(data, i, ATRPeriod);
                    emaATR[i] = i == ATRPeriod ? atr[i] : CalculateEMA(atr[i], emaATR[i - 1], EMAPeriod);
                }

                for (int i = Lookback; i < data.Count; i++)
                {
                    var currentDate = data[i].Date;
                    if (existingSetups.Contains(currentDate) || currentDate < scanCutoff) continue;

                    // 1. Strong Uptrend Check
                    var uptrendSlice = data.Skip(i - UptrendLookback).Take(UptrendLookback)
                                           .Select((d, idx) => new SlopeVariables { X = idx, Y = d.Close })
                                           .ToList();
                    double closeSlope = CalculateSlope(uptrendSlice);
                    if (closeSlope <= 0 || data[i].Close < sma50) continue;

                    // 2. Volume Decreasing Check
                    var volSlice = data.Skip(i - Lookback).Take(Lookback)
                                       .Select((d, idx) => new SlopeVariables { X = idx, Y = d.Volume })
                                       .ToList();
                    double volSlope = CalculateSlope(volSlice);
                    if (volSlope >= 0) continue;

                    // 3. Pattern Shape
                    var slice = data.Skip(i - Lookback).Take(Lookback).ToList();
                    var highs = slice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.High }).ToList();
                    var lows = slice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.Low }).ToList();
                    double highSlope = CalculateSlope(highs);
                    double lowSlope = CalculateSlope(lows);

                    bool isWedge = (highSlope < HighSlopeThreshold && lowSlope > LowSlopeThreshold);
                    bool isFlag = (Math.Abs(highSlope) < ParallelSlopeThreshold && Math.Abs(lowSlope) < ParallelSlopeThreshold);
                    if (!isWedge && !isFlag) continue;

                    // Resistance (for info only)
                    double avgX = highs.Average(p => p.X);
                    double avgY = highs.Average(p => p.Y);
                    double slope = CalculateSlope(highs);
                    double intercept = avgY - slope * avgX;
                    double resistance = slope * Lookback + intercept;

                    // Avg Volume
                    double avgVolRecent = data.Skip(i - VolumeWindow).Take(VolumeWindow).Average(d => d.Volume);

                    // Compression
                    double high = slice.Max(d => d.High);
                    double low = slice.Min(d => d.Low);
                    double compression = high > 0 ? (high - low) / high : 0;

                    // Stop-loss info
                    double swingLow = slice.Min(d => d.Low);
                    double stopLoss = swingLow - emaATR[i];
                    stopLoss = Math.Round(stopLoss, 2);

                    string patternType = isWedge ? "Wedge" : "Flag";
                    string signal = compression < 0.03 ? $"A+ {patternType} Setup" :
                                    compression < 0.06 ? $"Good {patternType} Setup" : $"{patternType} Pattern Detected";

                    results.Add(new StockSetups
                    {
                        Ticker = ticker,
                        Date = currentDate,
                        Close = data[i].Close,
                        High = data[i].High,
                        Low = data[i].Low,
                        Volume = data[i].Volume,
                        VolMA = avgVolRecent,
                        Trend = true,
                        Setup = true,
                        Signal = signal,
                        ResistanceLevel = Math.Round(resistance, 2),
                        BreakoutPrice = Math.Round(resistance * 1.01, 2),
                        IsFinalized = false,
                        Compression = Math.Round(compression, 4),
                        HighSlope = Math.Round(highSlope, 4),
                        LowSlope = Math.Round(lowSlope, 4),
                        SmoothedATR = Math.Round(emaATR[i], 4),
                        StopLoss = stopLoss
                    });
                }
                return results;
            }
        }
    }
}

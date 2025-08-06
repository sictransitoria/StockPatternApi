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
            Console.WriteLine($"Most recent trading day for {daysBack} days back: {today.Date}");
            return today.Date;
        }

        public static double CalculateSlope(List<SlopeVariables> points)
        {
            if (points.Count < 2)
            {
                Console.WriteLine("Insufficient points for slope calculation");
                return 0;
            }
            double avgX = points.Average(p => p.X);
            double avgY = points.Average(p => p.Y);
            double numerator = points.Sum(p => (p.X - avgX) * (p.Y - avgY));
            double denominator = points.Sum(p => Math.Pow(p.X - avgX, 2));
            double slope = denominator == 0 ? 0 : numerator / denominator;
            Console.WriteLine($"Slope calculated: {slope}");
            return slope;
        }

        public static double CalculateATR(List<GetHistoricalData> data, int index, int period)
        {
            if (index < period)
            {
                Console.WriteLine($"Insufficient data for ATR at index {index}");
                return 0;
            }
            double atr = data.Skip(index - period).Take(period)
                .Average(d => Math.Max(d.High - d.Low, Math.Max(Math.Abs(d.High - d.Close), Math.Abs(d.Low - d.Close))));
            Console.WriteLine($"ATR at index {index}: {atr}");
            return atr;
        }

        public static double CalculateEMA(double current, double previousEMA, int period)
        {
            double ema = (current * (2.0 / (1 + period))) + (previousEMA * (1 - 2.0 / (1 + period)));
            Console.WriteLine($"EMA calculated: {ema}");
            return ema;
        }

        public class WedgePatternDetector
        {
            private const int VolumeWindow = 15;
            private const int Lookback = 7;
            private const double HighSlopeThreshold = -0.1;
            private const double LowSlopeThreshold = 0.3;
            private const int ATRPeriod = 7;
            private const int EMAPeriod = 3;
            private const int KeltnerEMAPeriod = 10;
            private const double KeltnerMultiplier = 2.5;

            public static List<StockSetups> Detect(string ticker, List<GetHistoricalData> data, HashSet<DateTime> existingSetups)
            {
                var results = new List<StockSetups>();
                DateTime scanCutoff = GetMostRecentTradingDay(0);
                Console.WriteLine($"Scan cutoff date: {scanCutoff}, Data count: {data.Count}");

                if (data.Count < Lookback + VolumeWindow + ATRPeriod)
                {
                    Console.WriteLine("Insufficient data for pattern detection");
                    return results;
                }

                double sma50 = data.TakeLast(50).Average(d => d.Close);
                Console.WriteLine($"SMA50: {sma50}");

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
                    Console.WriteLine($"Processing date: {currentDate}");

                    if (existingSetups.Contains(currentDate) || currentDate < scanCutoff ||
                        !(i >= 3 && data.Skip(i - 3).Take(3).Count(d => d.Close > sma50 * 0.95) >= 2) ||
                        !(i >= Lookback && i >= VolumeWindow && data[i].Volume < data.Skip(i - VolumeWindow).Take(VolumeWindow).Average(d => d.Volume)))
                    {
                        Console.WriteLine($"Skipping date {currentDate}: Conditions not met");
                        continue;
                    }

                    var wedgeSlice = data.Skip(i - Lookback).Take(Lookback).ToList();
                    var highs = wedgeSlice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.High }).ToList();
                    var lows = wedgeSlice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.Low }).ToList();
                    double highSlope = CalculateSlope(highs);
                    double lowSlope = CalculateSlope(lows);
                    Console.WriteLine($"High slope: {highSlope}, Low slope: {lowSlope}");

                    if (wedgeSlice.Count < Lookback || !(highSlope < HighSlopeThreshold && lowSlope > LowSlopeThreshold))
                    {
                        Console.WriteLine("Wedge pattern conditions not met");
                        continue;
                    }

                    double high = wedgeSlice.Max(d => d.High);
                    double low = wedgeSlice.Min(d => d.Low);
                    double compression = high > 0 ? (high - low) / high : 0;
                    Console.WriteLine($"Compression: {compression}");

                    double avgX = highs.Average(p => p.X);
                    double avgY = highs.Average(p => p.Y);
                    double slope = CalculateSlope(highs);
                    double intercept = avgY - slope * avgX;
                    double resistance = slope * Lookback + intercept;

                    double middleLine = data.Skip(i - KeltnerEMAPeriod).Take(KeltnerEMAPeriod).Average(d => d.Close);
                    double upperBand = middleLine + (KeltnerMultiplier * emaATR[i]);

                    bool breakout = data[i].Close > upperBand * 1.01;
                    double breakoutPrice = resistance * 1.01;

                    string signal = compression < 0.03 ? "A+ Wedge Setup" : compression < 0.06 ? "Good Wedge Setup" : "Wedge Pattern Detected";
                    if (breakout) signal += " with Keltner Breakout";
                    Console.WriteLine($"Signal: {signal}, Breakout: {breakout}");

                    results.Add(new StockSetups
                    {
                        Ticker = ticker,
                        Date = currentDate,
                        Close = data[i].Close,
                        High = data[i].High,
                        Low = data[i].Low,
                        Volume = data[i].Volume,
                        VolMA = data.Skip(i - VolumeWindow).Take(VolumeWindow).Average(d => d.Volume),
                        Trend = true,
                        Setup = true,
                        Signal = signal,
                        ResistanceLevel = Math.Round(resistance, 2),
                        BreakoutPrice = Math.Round(breakoutPrice, 2),
                        IsFinalized = false,
                        Compression = Math.Round(compression, 4),
                        HighSlope = Math.Round(highSlope, 4),
                        LowSlope = Math.Round(lowSlope, 4),
                        KeltnerBreakout = breakout,
                        SmoothedATR = Math.Round(emaATR[i], 4)
                    });
                    Console.WriteLine($"Added setup for {ticker} on {currentDate}");
                }
                Console.WriteLine($"Total setups detected for {ticker}: {results.Count}");
                return results;
            }
        }
    }
}
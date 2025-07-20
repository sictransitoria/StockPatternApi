using StockPatternApi.Models;

namespace StockPatternApi.Helpers
{
    public static class Functions
    {
        public struct ChartPoint
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

        public static double CalculateSlope(List<ChartPoint> points)
        {
            if (points.Count < 2)
                return 0;

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

        public static double CalculateEMA(double current, double previousEMA, int period) =>
            (current * (2.0 / (1 + period))) + (previousEMA * (1 - 2.0 / (1 + period)));

        public class WedgePatternDetector
        {
            private const int VolumeWindow = 10;
            private const int Lookback = 5;
            private const double HighSlopeThreshold = -0.05;
            private const double LowSlopeThreshold = 0.05;
            private const int ATRPeriod = 10;
            private const int EMAPeriod = 3;
            private const int KeltnerEMAPeriod = 20;
            private const double KeltnerMultiplier = 2.0;

            public static List<StockSetups> Detect(string ticker, List<GetHistoricalData> data, HashSet<DateTime> existingSetups)
            {
                var results = new List<StockSetups>();
                DateTime scanCutoff = GetMostRecentTradingDay(1);

                Console.WriteLine($"[INFO] Starting detection for {ticker}. Data count: {data.Count}, Scan cutoff date: {scanCutoff:yyyy-MM-dd}");

                if (data.Count < Lookback + VolumeWindow + ATRPeriod)
                {
                    Console.WriteLine("[WARN] Not enough data to run detection.");
                    return results;
                }

                double sma50 = data.TakeLast(50).Average(d => d.Close);
                double[] atr = new double[data.Count];
                double[] emaATR = new double[data.Count];

                for (int i = ATRPeriod; i < data.Count; i++)
                {
                    atr[i] = CalculateATR(data, i, ATRPeriod);
                    emaATR[i] = i == ATRPeriod ? atr[i] : CalculateEMA(atr[i], emaATR[i - 1], EMAPeriod);
                }

                Console.WriteLine($"[INFO] Calculated SMA50: {sma50:F2}");

                for (int i = Lookback; i < data.Count; i++)
                {
                    var currentDate = data[i].Date;

                    if (existingSetups.Contains(currentDate))
                    {
                        Console.WriteLine($"[SKIP] Date {currentDate:yyyy-MM-dd} already has an existing setup.");
                        continue;
                    }

                    if (currentDate < scanCutoff)
                    {
                        Console.WriteLine($"[SKIP] Date {currentDate:yyyy-MM-dd} is older than cutoff date {scanCutoff:yyyy-MM-dd}.");
                        continue;
                    }

                    bool strongUptrend = i >= 3 && data.Skip(i - 3).Take(3).Count(d => d.Close > sma50 * 0.98) >= 2;
                    if (!strongUptrend)
                    {
                        Console.WriteLine($"[SKIP] Date {currentDate:yyyy-MM-dd} failed strong uptrend check.");
                        continue;
                    }

                    bool volContraction = i >= Lookback &&
                        Enumerable.Range(i - Lookback + 1, Lookback - 1)
                        .Select(j =>
                        {
                            int windowStart = Math.Max(j - VolumeWindow, 0);
                            double avgVol = data.Skip(windowStart).Take(VolumeWindow).Average(d => d.Volume);
                            return data[j].Volume < avgVol;
                        })
                        .Count(result => result) >= (int)(0.6 * (Lookback - 1));

                    if (!volContraction)
                    {
                        Console.WriteLine($"[SKIP] Date {currentDate:yyyy-MM-dd} failed volume contraction check.");
                        continue;
                    }

                    var wedgeSlice = data.Skip(i - Lookback).Take(Lookback).ToList();
                    var highs = wedgeSlice.Select((d, idx) => new ChartPoint { X = idx, Y = d.High }).ToList();
                    var lows = wedgeSlice.Select((d, idx) => new ChartPoint { X = idx, Y = d.Low }).ToList();

                    double highSlope = CalculateSlope(highs);
                    double lowSlope = CalculateSlope(lows);

                    if (wedgeSlice.Count < Lookback)
                    {
                        Console.WriteLine($"[SKIP] Date {currentDate:yyyy-MM-dd} has insufficient data for wedgeSlice.");
                        continue;
                    }

                    bool lowerHighs = Enumerable.Range(1, wedgeSlice.Count - 1).All(j => wedgeSlice[j].High <= wedgeSlice[j - 1].High);
                    bool higherLows = Enumerable.Range(1, wedgeSlice.Count - 1).All(j => wedgeSlice[j].Low >= wedgeSlice[j - 1].Low);

                    bool wedge = highSlope < HighSlopeThreshold && lowSlope > LowSlopeThreshold;
                    if (!wedge)
                    {
                        Console.WriteLine($"[SKIP] Date {currentDate:yyyy-MM-dd} wedge pattern not confirmed. HighSlope={highSlope:F4}, LowSlope={lowSlope:F4}");
                        continue;
                    }

                    double high = wedgeSlice.Max(d => d.High);
                    double low = wedgeSlice.Min(d => d.Low);
                    double compression = (high - low) / high;

                    double avgX = highs.Average(p => p.X);
                    double avgY = highs.Average(p => p.Y);
                    double slope = CalculateSlope(highs);
                    double intercept = avgY - slope * avgX;
                    double resistance = slope * Lookback + intercept;

                    double middleLine = data.Skip(i - KeltnerEMAPeriod).Take(KeltnerEMAPeriod).Average(d => d.Close);
                    double upperBand = middleLine + (KeltnerMultiplier * emaATR[i]);
                    bool breakout = data[i].Close > upperBand;

                    double breakoutPrice = resistance * 1.002;

                    string signal = compression < 0.05 ? "A+ Wedge Setup"
                                    : compression < 0.1 ? "Good Wedge Setup"
                                    : "Wedge Pattern Detected";
                    if (breakout) signal += " with Keltner Breakout";

                    Console.WriteLine($"[DETECTED] Date {currentDate:yyyy-MM-dd} Signal: {signal}, Compression: {compression:P2}, Resistance: {resistance:F2}, BreakoutPrice: {breakoutPrice:F2}");

                    results.Add(new StockSetups
                    {
                        Ticker = ticker,
                        Date = currentDate,
                        Close = data[i].Close,
                        High = data[i].High,
                        Low = data[i].Low,
                        Volume = data[i].Volume,
                        VolMA = data.Skip(i - VolumeWindow + 1).Take(VolumeWindow).Average(d => d.Volume),
                        Trend = strongUptrend,
                        Setup = wedge,
                        Signal = signal,
                        ResistanceLevel = Math.Round(resistance, 2),
                        BreakoutPrice = Math.Round(breakoutPrice, 2),
                        IsFinalized = false,
                        Compression = Math.Round(compression, 4),
                        HighSlope = Math.Round(highSlope, 4),
                        LowSlope = Math.Round(lowSlope, 4),
                        LowerHighs = lowerHighs,
                        HigherLows = higherLows,
                        KeltnerBreakout = breakout,
                        SmoothedATR = Math.Round(emaATR[i], 4)
                    });
                }

                Console.WriteLine($"[INFO] Detection complete for {ticker}. Total setups found: {results.Count}");
                return results;
            }
        }
    }
}
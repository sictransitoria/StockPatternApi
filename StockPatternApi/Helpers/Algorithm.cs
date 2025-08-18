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

        public static double CalculateSlope(List<SlopeVariables> points)
        {
            if (points == null || points.Count < 2) return 0;
            double avgX = points.Average(p => p.X);
            double avgY = points.Average(p => p.Y);
            double numerator = points.Sum(p => (p.X - avgX) * (p.Y - avgY));
            double denominator = points.Sum(p => Math.Pow(p.X - avgX, 2));
            return denominator == 0 ? 0 : numerator / denominator;
        }

        // Keep CalculateEMA utility for other usage
        public static double CalculateEMA(double current, double previousEMA, int period)
        {
            double k = 2.0 / (1 + period);
            return (current * k) + (previousEMA * (1 - k));
        }

        // Compute True Range based ATR and EMA-smoothed ATR correctly using prior close
        public static double[] ComputeEmaATR(List<GetHistoricalData> data, int atrPeriod = 7, int emaPeriod = 3)
        {
            var tr = new double[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                if (i == 0)
                {
                    tr[i] = data[i].High - data[i].Low;
                }
                else
                {
                    double prevClose = data[i - 1].Close;
                    double h_l = data[i].High - data[i].Low;
                    double h_pc = Math.Abs(data[i].High - prevClose);
                    double l_pc = Math.Abs(data[i].Low - prevClose);
                    tr[i] = Math.Max(h_l, Math.Max(h_pc, l_pc));
                }
            }

            var ema = new double[data.Count];
            double k = 2.0 / (1 + emaPeriod);
            for (int i = 0; i < data.Count; i++)
            {
                if (i < atrPeriod)
                {
                    ema[i] = 0;
                    continue;
                }

                // Average TR across the latest 'atrPeriod' bars ending at i
                double atrWindowAvg = tr.Skip(i - atrPeriod + 1).Take(atrPeriod).Average();

                // Seed at position i == atrPeriod
                ema[i] = (i == atrPeriod) ? atrWindowAvg : (atrWindowAvg * k + ema[i - 1] * (1 - k));
            }

            return ema;
        }

        // For intraday datasets assume last bar date is the "most recent bar date"
        public static DateTime MostRecentBarDateUtc(List<GetHistoricalData> data)
        {
            if (data == null || data.Count == 0) return DateTime.UtcNow.Date;
            return data[^1].Date.Date;
        }

        private static bool ContainsDateTime(HashSet<DateTime> set, DateTime dt)
        {
            if (set == null) return false;
            return set.Contains(dt);
        }

        public class WedgePatternDetector
        {
            // Tunable - tightened for 30-min context
            private const int VolumeWindow = 30;       // for VolMA reference
            private const int Lookback = 12;           // window length for consolidation (~6 hours)
            private const int UptrendLookback = 24;    // lookback for trend slope (~12 hours)

            private const int ATRPeriod = 7;
            private const int EMAPeriod = 3;

            // Shape thresholds tightened
            private const double HighSlopeThreshold = -0.10;   // wedge: highs falling enough
            private const double LowSlopeThreshold = 0.28;     // wedge: lows rising enough
            private const double ParallelSlopeThreshold = 0.03;// flag: near-parallel stricter
            private const double MinCloseSlope = 0.02;         // strong uptrend requirement
            private const double MinCompressionPct = 0.12;     // at least 12% narrower range

            // Volume taper thresholds: half-over-half and variability
            private const double RequiredDropFactor = 0.80;    // second half avg <= 80% of first half
            private const double MaxLastBarSpikeFactor = 1.10; // reject if last bar spikes >110% of first half avg
            private const double MaxSecondHalfCV = 0.55;       // coefficient of variation cap

            // Breakout confirmation
            private const double BreakoutVolVsRecent = 1.30;   // current volume vs VolMA
            private const double BreakoutVolVsBase = 1.20;     // current volume vs first half avg of pattern

            // Minimal tick fallback, adjust per instrument if you can pass tick size
            private const double FallbackTick = 0.01;

            public static List<StockSetups> Detect(string ticker, List<GetHistoricalData> data, HashSet<DateTime> existingSetups)
            {
                var results = new List<StockSetups>();
                if (data == null || data.Count == 0) return results;

                DateTime scanCutoff = MostRecentBarDateUtc(data);

                // Safety minimum data length
                int requiredMinimum = Math.Max(UptrendLookback, 50) + Lookback + VolumeWindow + ATRPeriod + 2;
                if (data.Count < requiredMinimum) return results;

                // 50-period SMA for trend filter
                double sma50 = data.Skip(Math.Max(0, data.Count - 50)).Take(50).Average(d => d.Close);

                // Precompute EMA-smoothed ATR correctly
                double[] emaATR = ComputeEmaATR(data, ATRPeriod, EMAPeriod);

                for (int i = Lookback; i < data.Count; i++)
                {
                    var currentBar = data[i];
                    var currentDay = currentBar.Date;

                    // Skip if we've already flagged this date (normalize to date only)
                    if (ContainsDateTime(existingSetups, currentDay) || currentDay.Date < scanCutoff.Date) continue;

                    // 1) Strong Uptrend - slope of closes over UptrendLookback and above sma50
                    if (i - UptrendLookback + 1 < 0) continue; // not enough lookback for trend
                    var uptrendSlice = data.Skip(i - UptrendLookback + 1).Take(UptrendLookback)
                                           .Select((d, idx) => new SlopeVariables { X = idx, Y = d.Close })
                                           .ToList();
                    double closeSlope = CalculateSlope(uptrendSlice);
                    if (closeSlope < MinCloseSlope || currentBar.Close < sma50) continue;

                    // 2) Volume decreasing inside the consolidation window
                    var volSlice = data.Skip(i - Lookback + 1).Take(Lookback).Select(d => (double)d.Volume).ToList();
                    if (volSlice.Count < Lookback) continue;

                    int half = Lookback / 2;
                    double firstHalfAvg = volSlice.Take(half).Average();
                    double secondHalfAvg = volSlice.Skip(half).Take(Lookback - half).Average();
                    double secondHalfStd = Math.Sqrt(volSlice.Skip(half).Select(v => Math.Pow(v - secondHalfAvg, 2)).Average());
                    double secondHalfCV = secondHalfAvg > 0 ? secondHalfStd / secondHalfAvg : 1.0;
                    double lastBarVol = volSlice[^1];

                    if (firstHalfAvg <= 0) continue;
                    if (secondHalfAvg > firstHalfAvg * RequiredDropFactor) continue;
                    if (secondHalfCV > MaxSecondHalfCV) continue;
                    if (lastBarVol > firstHalfAvg * MaxLastBarSpikeFactor) continue;

                    // 3) Pattern shape
                    var slice = data.Skip(i - Lookback + 1).Take(Lookback).ToList();
                    var highs = slice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.High }).ToList();
                    var lows = slice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.Low }).ToList();

                    double highSlope = CalculateSlope(highs);
                    double lowSlope = CalculateSlope(lows);

                    // Reject grind-up channels where both slopes are positive
                    if (highSlope > 0 && lowSlope > 0) continue;

                    double highStart = highs.First().Y;
                    double highEnd = highs.Last().Y;
                    double lowStart = lows.First().Y;
                    double lowEnd = lows.Last().Y;
                    double rangeStart = highStart - lowStart;
                    double rangeEnd = highEnd - lowEnd;
                    if (rangeStart <= 0) continue;

                    double compressionPct = 1.0 - (rangeEnd / rangeStart);

                    // LH + HL check inside window
                    bool hasLowerHighs = highEnd < highStart;
                    bool hasHigherLows = lowEnd > lowStart;

                    // Classify patterns
                    bool isWedge = hasLowerHighs && hasHigherLows &&
                                   highSlope < HighSlopeThreshold && lowSlope > LowSlopeThreshold &&
                                   compressionPct >= MinCompressionPct;

                    bool isPennant = hasLowerHighs && hasHigherLows &&
                                     highSlope < 0 && lowSlope > 0 &&
                                     compressionPct >= MinCompressionPct + 0.05;

                    bool isFlag = Math.Abs(highSlope) < ParallelSlopeThreshold &&
                                  Math.Abs(lowSlope) < ParallelSlopeThreshold &&
                                  compressionPct >= 0.03 &&
                                  // avoid grind-up flagged as flag: require that closes inside window are not monotonically higher
                                  !(slice.Take(Lookback - 1).Max(d => d.Close) < slice[^1].Close);

                    if (!isWedge && !isFlag && !isPennant) continue;

                    // 4) Compute resistance from highs regression and project to last index inside window
                    double avgX = highs.Average(p => p.X);
                    double avgY = highs.Average(p => p.Y);
                    double slope = CalculateSlope(highs);
                    double intercept = avgY - slope * avgX;

                    int lastIdx = Lookback - 1; // last bar inside the window
                    double resistance = slope * lastIdx + intercept;

                    double highMax = slice.Max(d => d.High);
                    double lowMin = slice.Min(d => d.Low);
                    double compression = highMax > 0 ? (highMax - lowMin) / highMax : 0;

                    // Use a conservative swing low from second half to avoid oversized stops
                    double swingLow = slice.Skip(half).Min(d => d.Low);

                    // Use emaATR at index i, fallback to a small number
                    double atrAtI = (emaATR != null && emaATR.Length > i) ? emaATR[i] : 0;
                    if (atrAtI <= 0) atrAtI = Math.Max((highMax - lowMin) * 0.01, 0.5);

                    double stopLoss = Math.Round(swingLow - Math.Max(atrAtI, 0.5), 2);

                    double volMA = data.Skip(Math.Max(0, i - VolumeWindow + 1)).Take(VolumeWindow).Average(d => d.Volume);

                    // 5) Breakout confirmation
                    double breakoutBufferPts = Math.Max(FallbackTick, 0.25 * atrAtI);
                    bool brokeOut = currentBar.Close >= resistance + breakoutBufferPts &&
                                    (currentBar.Volume >= volMA * BreakoutVolVsRecent ||
                                     currentBar.Volume >= firstHalfAvg * BreakoutVolVsBase);

                    string patternType = isWedge ? "Wedge" : isFlag ? "Flag" : "Pennant";
                    string quality = compression < 0.028 ? "A+" :
                                     compression < 0.055 ? "Good" : "OK";

                    string signal = brokeOut
                        ? $"{quality} {patternType} Breakout"
                        : $"{quality} {patternType} Setup";

                    results.Add(new StockSetups
                    {
                        Ticker = ticker,
                        Date = currentBar.Date,
                        Close = currentBar.Close,
                        High = currentBar.High,
                        Low = currentBar.Low,
                        Volume = currentBar.Volume,
                        VolMA = Math.Round(volMA, 2),
                        Trend = true,
                        Setup = !brokeOut,
                        Signal = signal,
                        ResistanceLevel = Math.Round(resistance, 4),
                        BreakoutPrice = Math.Round(resistance + breakoutBufferPts, 4),
                        IsFinalized = brokeOut,
                        Compression = Math.Round(compression, 4),
                        HighSlope = Math.Round(highSlope, 6),
                        LowSlope = Math.Round(lowSlope, 6),
                        SmoothedATR = Math.Round(atrAtI, 4),
                        StopLoss = stopLoss
                    });
                }

                return results;
            }
        }
    }
}

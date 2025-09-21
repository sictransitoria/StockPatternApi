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
            if (points == null || points.Count < 2)
                return 0;

            double avgX = points.Average(p => p.X);
            double avgY = points.Average(p => p.Y);
            double numerator = points.Sum(p => (p.X - avgX) * (p.Y - avgY));
            double denominator = points.Sum(p => Math.Pow(p.X - avgX, 2));

            return denominator == 0 ? 0 : numerator / denominator;
        }

        // Compute Wilder ATR, then EMA-smoothed ATR
        public static double[] ComputeEmaATR(List<GetHistoricalData> data, int atrPeriod = 7, int emaPeriod = 3)
        {
            int n = data.Count;
            var tr = new double[n];

            for (int i = 0; i < n; i++)
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

            var atr = new double[n];
            if (n < atrPeriod)
                return atr;

            double seed = tr.Take(atrPeriod).Average();
            atr[atrPeriod - 1] = seed;

            for (int i = atrPeriod; i < n; i++)
            {
                atr[i] = ((atr[i - 1] * (atrPeriod - 1)) + tr[i]) / atrPeriod;
            }

            var ema = new double[n];
            double k = 2.0 / (emaPeriod + 1.0);

            int start = atrPeriod - 1;
            for (int i = 0; i < n; i++)
                ema[i] = 0.0;

            if (start < n)
            {
                ema[start] = atr[start];
                for (int i = start + 1; i < n; i++)
                {
                    ema[i] = (atr[i] * k) + (ema[i - 1] * (1.0 - k));
                }
            }

            return ema;
        }

        public static DateTime MostRecentBarDateUtc(List<GetHistoricalData> data)
        {
            if (data == null || data.Count == 0)
                return DateTime.UtcNow.Date;

            return data[^1].Date.Date;
        }

        private static bool ContainsTradingDate(HashSet<DateTime> set, DateTime dt)
        {
            if (set == null)
                return false;

            var d = dt.Date;
            return set.Contains(d);
        }

        private static double FindNextResistanceLeft(List<GetHistoricalData> data, int idx, double level, int lookback = 80, int pivot = 3)
        {
            int start = Math.Max(0, idx - lookback);
            double res = level;

            for (int i = idx; i >= start + pivot; i--)
            {
                bool isPivotHigh = true;
                for (int k = 1; k <= pivot; k++)
                {
                    if (i + k >= data.Count)
                    {
                        isPivotHigh = false;
                        break;
                    }
                    if (!(data[i].High > data[i - k].High && data[i].High > data[i + k].High))
                    {
                        isPivotHigh = false;
                        break;
                    }
                }

                if (isPivotHigh && data[i].High > level)
                    res = Math.Max(res, data[i].High);
            }

            return res;
        }

        public class WedgePatternDetector
        {
            private const int VolumeWindow = 20;
            private const int Lookback = 12;
            private const int UptrendLookback = 24;
            private const int ATRPeriod = 7;
            private const int EMAPeriod = 3;

            private const double HighSlopeThreshold = -0.015;
            private const double LowSlopeThreshold = 0.02;
            private const double ParallelSlopeThreshold = 0.08;

            private const double MinCloseSlope = 0.004;

            private const double MinCompressionPct = 0.04;
            private const double RequiredDropFactor = 1.05;
            private const double MaxLastBarSpikeFactor = 2.0;
            private const double MaxSecondHalfCV = 1.20;

            private const double BreakoutVolVsRecent = 1.10;
            private const double BreakoutVolVsBase = 1.00;

            private const double FallbackTick = 0.01;

            public static List<StockSetups> Detect(string ticker, List<GetHistoricalData> data, HashSet<DateTime> existingSetups)
            {
                var results = new List<StockSetups>();
                if (data == null || data.Count == 0)
                    return results;

                DateTime scanCutoff = MostRecentBarDateUtc(data);
                int requiredMinimum = Math.Max(UptrendLookback, 50) + Lookback + VolumeWindow + ATRPeriod + 2;
                if (data.Count < requiredMinimum)
                    return results;

                double sma50 = data.Skip(Math.Max(0, data.Count - 50)).Take(50).Average(d => d.Close);
                double[] emaATR = ComputeEmaATR(data, ATRPeriod, EMAPeriod);

                if (emaATR == null || emaATR.Length != data.Count)
                    emaATR = ComputeEmaATR(data, ATRPeriod, EMAPeriod);

                for (int i = Lookback; i < data.Count; i++)
                {
                    var currentBar = data[i];
                    var currentDay = currentBar.Date.Date;

                    if (ContainsTradingDate(existingSetups, currentDay) || currentDay < scanCutoff.Date)
                        continue;

                    if (i - UptrendLookback + 1 < 0)
                        continue;

                    // Step 1: Check for valid uptrend
                    var uptrendSlice = data.Skip(i - UptrendLookback + 1)
                        .Take(UptrendLookback)
                        .Select((d, idx) => new SlopeVariables { X = idx, Y = d.Close })
                        .ToList();
                    double closeSlope = CalculateSlope(uptrendSlice);

                    if (closeSlope < MinCloseSlope || currentBar.Close < sma50)
                        continue;

                    // Step 2: Check volume decrease
                    var volSlice = data.Skip(i - Lookback + 1).Take(Lookback).Select(d => (double)d.Volume).ToList();
                    if (volSlice.Count < Lookback)
                        continue;

                    int half = Lookback / 2;
                    double firstHalfAvg = volSlice.Take(half).Average();
                    double secondHalfAvg = volSlice.Skip(half).Take(Lookback - half).Average();
                    double secondHalfStd = Math.Sqrt(volSlice.Skip(half).Select(v => Math.Pow(v - secondHalfAvg, 2)).Average());
                    double secondHalfCV = secondHalfAvg > 0 ? secondHalfStd / secondHalfAvg : 1.0;
                    double lastBarVol = volSlice[^1];

                    if (firstHalfAvg <= 0 ||
                        secondHalfAvg > firstHalfAvg * RequiredDropFactor ||
                        secondHalfCV > MaxSecondHalfCV ||
                        lastBarVol > firstHalfAvg * MaxLastBarSpikeFactor)
                        continue;

                    // Step 3: Identify pattern
                    var slice = data.Skip(i - Lookback + 1).Take(Lookback).ToList();
                    var highs = slice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.High }).ToList();
                    var lows = slice.Select((d, idx) => new SlopeVariables { X = idx, Y = d.Low }).ToList();

                    double highSlope = CalculateSlope(highs);
                    double lowSlope = CalculateSlope(lows);

                    if (highSlope > 0 && lowSlope > 0)
                        continue;

                    double highStart = highs.First().Y;
                    double highEnd = highs.Last().Y;
                    double lowStart = lows.First().Y;
                    double lowEnd = lows.Last().Y;
                    double rangeStart = highStart - lowStart;
                    double rangeEnd = highEnd - lowEnd;

                    if (rangeStart <= 0)
                        continue;

                    double compressionPct = 1.0 - (rangeEnd / rangeStart);
                    bool hasLowerHighs = highEnd < highStart;
                    bool hasHigherLows = lowEnd > lowStart;

                    bool isWedge = hasLowerHighs && hasHigherLows &&
                                   highSlope < HighSlopeThreshold &&
                                   lowSlope > LowSlopeThreshold &&
                                   compressionPct >= MinCompressionPct;

                    bool isPennant = hasLowerHighs && hasHigherLows &&
                                     highSlope < 0 &&
                                     lowSlope > 0 &&
                                     compressionPct >= MinCompressionPct + 0.02;

                    bool lastIsNewHighClose = slice.Take(Lookback - 1).Max(d => d.Close) < slice[^1].Close;

                    bool isFlag = Math.Abs(highSlope - lowSlope) < ParallelSlopeThreshold &&
                                  compressionPct >= 0.03 &&
                                  !lastIsNewHighClose;

                    if (!isWedge && !isFlag && !isPennant)
                        continue;

                    // Step 4: Setup confirmed, now evaluate RR
                    double avgX = highs.Average(p => p.X);
                    double avgY = highs.Average(p => p.Y);
                    double slope = CalculateSlope(highs);
                    double intercept = avgY - slope * avgX;
                    int lastIdx = Lookback - 1;
                    double resistance = slope * lastIdx + intercept;

                    double highMax = slice.Max(d => d.High);
                    double lowMin = slice.Min(d => d.Low);
                    double swingLow = slice.Skip(half).Min(d => d.Low);
                    double atrAtI = (emaATR != null && emaATR.Length > i) ? emaATR[i] : 0;
                    double priceRange = Math.Max(1e-6, highMax - lowMin);
                    double tick = currentBar.Close < 5 ? 0.001 : FallbackTick;

                    if (atrAtI <= 0)
                        atrAtI = Math.Max(priceRange * 0.03, 3 * tick);

                    double volMA = data.Skip(Math.Max(0, i - VolumeWindow + 1)).Take(VolumeWindow).Average(d => d.Volume);
                    double breakoutBufferPts = Math.Max(tick, 0.20 * atrAtI);
                    bool strongVolume = currentBar.Volume >= Math.Min(Math.Max(volMA * BreakoutVolVsRecent, firstHalfAvg * BreakoutVolVsBase), Math.Max(volMA, firstHalfAvg) * 2.5);
                    bool priceBreak = currentBar.Close >= resistance + breakoutBufferPts;
                    bool brokeOut = priceBreak && strongVolume;

                    double entry = Math.Round(resistance + breakoutBufferPts, 4);

                    double stopLoss = Math.Round(swingLow - Math.Max(0.75 * atrAtI, 2 * tick), 4);
                    double nextResistance = FindNextResistanceLeft(data, i, resistance);

                    if (nextResistance <= resistance)
                        nextResistance = highMax + 1.5 * atrAtI;

                    double takeProfit = Math.Round(nextResistance, 4);
                    double riskPerShare = Math.Max(tick, entry - stopLoss);
                    double rewardPerShare = Math.Max(tick, takeProfit - entry);
                    double rr = rewardPerShare / riskPerShare;
                    bool passesRR = rr >= 1.5;

                    if (currentBar.Close < (currentBar.High + currentBar.Low) / 2)
                        continue;

                    if (currentBar.Close <= data[i - 1].Close)
                        continue;

                    double intradayPos = (currentBar.Close - currentBar.Low) / Math.Max(1e-6, currentBar.High - currentBar.Low);
                    if (intradayPos < 0.6)
                        continue;

                    // Step 5: Log setup
                    string patternType = isWedge ? "Wedge" : isFlag ? "Flag" : "Pennant";

                    string quality = compressionPct >= 0.20 ? "A+" :
                                     compressionPct >= 0.08 ? "Good" : "OK";

                    string signal;
                    if (brokeOut && passesRR)
                        signal = $"{quality} {patternType} Breakout";
                    else if (!passesRR)
                        signal = $"Low RR {patternType} Setup";
                    else
                        signal = $"{patternType} Setup";

                    results.Add(new StockSetups
                    {
                        Ticker = ticker,
                        Date = currentBar.Date,
                        Close = currentBar.Close,
                        High = currentBar.High,
                        Low = currentBar.Low,
                        Volume = currentBar.Volume,
                        VolMA = Math.Round(volMA, 2),
                        Trend = closeSlope >= MinCloseSlope && currentBar.Close >= sma50,
                        Setup = !brokeOut,
                        Signal = signal,
                        ResistanceLevel = Math.Round(resistance, 4),
                        BreakoutPrice = entry,
                        IsFinalized = brokeOut && passesRR,
                        Compression = Math.Round(compressionPct, 4),
                        HighSlope = Math.Round(highSlope, 6),
                        LowSlope = Math.Round(lowSlope, 6),
                        SmoothedATR = Math.Round(atrAtI, 4),
                        StopLoss = stopLoss,
                        TakeProfit = takeProfit,
                        RiskPerShare = Math.Round(riskPerShare, 4),
                        RewardPerShare = Math.Round(rewardPerShare, 4),
                        RewardToRisk = Math.Round(rr, 2)
                    });
                }

                return results;
            }
        }
    }
}

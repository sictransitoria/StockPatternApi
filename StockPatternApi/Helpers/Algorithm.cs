using StockPatternApi.Models;

namespace StockPatternApi.Helpers
{
    /// <summary>
    /// Pattern helpers aligned to EnhancedMarket / Trade Pro Elite falling-wedge rules.
    /// </summary>
    public static class Algorithm
    {
        public struct SlopePoint
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        public static double CalculateSlope(List<SlopePoint> points)
        {
            if (points == null || points.Count < 2)
                return 0;

            double avgX = points.Average(p => p.X);
            double avgY = points.Average(p => p.Y);
            double numerator = points.Sum(p => (p.X - avgX) * (p.Y - avgY));
            double denominator = points.Sum(p => Math.Pow(p.X - avgX, 2));

            return denominator == 0 ? 0 : numerator / denominator;
        }

        public static double[] ComputeEmaAtr(List<GetHistoricalData> data, int atrPeriod = 7, int emaPeriod = 3)
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
                    double hL = data[i].High - data[i].Low;
                    double hPc = Math.Abs(data[i].High - prevClose);
                    double lPc = Math.Abs(data[i].Low - prevClose);
                    tr[i] = Math.Max(hL, Math.Max(hPc, lPc));
                }
            }

            var atr = new double[n];
            if (n < atrPeriod)
                return atr;

            atr[atrPeriod - 1] = tr.Take(atrPeriod).Average();
            for (int i = atrPeriod; i < n; i++)
                atr[i] = ((atr[i - 1] * (atrPeriod - 1)) + tr[i]) / atrPeriod;

            var ema = new double[n];
            double k = 2.0 / (emaPeriod + 1.0);
            int start = atrPeriod - 1;
            if (start < n)
            {
                ema[start] = atr[start];
                for (int i = start + 1; i < n; i++)
                    ema[i] = (atr[i] * k) + (ema[i - 1] * (1.0 - k));
            }

            return ema;
        }

        public static DateTime MostRecentBarDate(List<GetHistoricalData> data)
        {
            if (data == null || data.Count == 0)
                return DateTime.UtcNow.Date;

            return data[^1].Date.Date;
        }

        private static bool ContainsTradingDate(HashSet<DateTime> set, DateTime dt)
        {
            return set != null && set.Contains(dt.Date);
        }

        /// <summary>
        /// Falling wedge detector (PDF Chapter 3 pattern section).
        /// Geometry: lower highs + lower lows, both slopes down, upper line steeper, converging range.
        /// Volume: drying up during formation; spike preferred on upside breakout.
        /// Trade plan: enter on break of upper trendline, stop under wedge low, target = measured move (widest height).
        /// </summary>
        public class WedgePatternDetector
        {
            private const int Lookback = 16;
            private const int VolumeWindow = 20;
            private const int AtrPeriod = 7;
            private const int EmaPeriod = 3;

            // Both lines must slope down; upper (highs) must be steeper (more negative) than lower (lows).
            private const double MaxHighSlope = -0.01;
            private const double MaxLowSlope = -0.002;
            private const double MinSlopeSeparation = 0.004; // upper steeper than lower
            private const double MinCompressionPct = 0.08;

            // Volume dry-up during wedge
            private const double MaxSecondHalfVolRatio = 0.95; // second half avg <= 95% of first half
            private const double MaxSecondHalfCv = 1.35;

            // Breakout confirmation
            private const double BreakoutVolVsRecent = 1.50; // PDF: real volume expansion on break
            private const double BreakoutVolVsBase = 1.20;
            private const double MinRewardToRisk = 2.0; // PDF prefers ~1:3; 2.0 is a practical floor

            // Filter pass: kill micro / junk patterns (CVS-style)
            private const double MinWedgeHeightAtr = 1.0;   // widest height >= 1x ATR
            private const double MinRiskPct = 0.005;        // stop distance >= 0.5% of entry
            private const double MaxRiskPct = 0.025;        // stop distance <= 2.5% of entry (PDF ~1-2%)

            private const double FallbackTick = 0.01;

            public static List<StockSetups> Detect(string ticker, List<GetHistoricalData> data, HashSet<DateTime> existingSetups)
            {
                var results = new List<StockSetups>();
                if (data == null || data.Count == 0)
                    return results;

                DateTime scanCutoff = MostRecentBarDate(data);
                int requiredMinimum = Math.Max(50, Lookback + VolumeWindow + AtrPeriod + 5);
                if (data.Count < requiredMinimum)
                    return results;

                double[] emaAtr = ComputeEmaAtr(data, AtrPeriod, EmaPeriod);

                for (int i = Lookback; i < data.Count; i++)
                {
                    var bar = data[i];
                    var day = bar.Date.Date;

                    // Same scan window as before: only emit for the most recent session in the series.
                    if (ContainsTradingDate(existingSetups, day) || day < scanCutoff)
                        continue;

                    var slice = data.Skip(i - Lookback + 1).Take(Lookback).ToList();
                    if (slice.Count < Lookback)
                        continue;

                    // --- PDF geometry: LH + LL, both slopes down, converging, upper steeper ---
                    var highPts = slice.Select((d, idx) => new SlopePoint { X = idx, Y = d.High }).ToList();
                    var lowPts = slice.Select((d, idx) => new SlopePoint { X = idx, Y = d.Low }).ToList();

                    double highSlope = CalculateSlope(highPts);
                    double lowSlope = CalculateSlope(lowPts);

                    double highStart = highPts.First().Y;
                    double highEnd = highPts.Last().Y;
                    double lowStart = lowPts.First().Y;
                    double lowEnd = lowPts.Last().Y;

                    bool hasLowerHighs = highEnd < highStart;
                    bool hasLowerLows = lowEnd < lowStart;

                    double rangeStart = highStart - lowStart;
                    double rangeEnd = highEnd - lowEnd;
                    if (rangeStart <= 0)
                        continue;

                    double compressionPct = 1.0 - (rangeEnd / rangeStart);
                    bool converging = compressionPct >= MinCompressionPct && rangeEnd < rangeStart;

                    // Upper trendline steeper downward => highSlope more negative than lowSlope
                    bool bothDown = highSlope <= MaxHighSlope && lowSlope <= MaxLowSlope && lowSlope < 0;
                    bool upperSteeper = highSlope < (lowSlope - MinSlopeSeparation);

                    if (!(hasLowerHighs && hasLowerLows && bothDown && upperSteeper && converging))
                        continue;

                    // --- PDF: decreasing volume as wedge forms ---
                    var vols = slice.Select(d => (double)d.Volume).ToList();
                    int half = Lookback / 2;
                    double firstHalfAvg = vols.Take(half).Average();
                    double secondHalfAvg = vols.Skip(half).Average();
                    if (firstHalfAvg <= 0)
                        continue;

                    double secondHalfStd = Math.Sqrt(vols.Skip(half).Select(v => Math.Pow(v - secondHalfAvg, 2)).Average());
                    double secondHalfCv = secondHalfAvg > 0 ? secondHalfStd / secondHalfAvg : 1.0;

                    if (secondHalfAvg > firstHalfAvg * MaxSecondHalfVolRatio || secondHalfCv > MaxSecondHalfCv)
                        continue;

                    // Upper trendline value at last bar (resistance / breakout line)
                    double avgX = highPts.Average(p => p.X);
                    double avgY = highPts.Average(p => p.Y);
                    double intercept = avgY - highSlope * avgX;
                    int lastIdx = Lookback - 1;
                    double resistance = highSlope * lastIdx + intercept;

                    double wedgeHigh = slice.Max(d => d.High);
                    double wedgeLow = slice.Min(d => d.Low);
                    double widestHeight = rangeStart; // PDF: height at widest point (start of pattern)
                    if (widestHeight <= 0)
                        continue;

                    double atr = (emaAtr != null && emaAtr.Length > i) ? emaAtr[i] : 0;
                    double tick = bar.Close < 5 ? 0.001 : FallbackTick;
                    if (atr <= 0)
                        atr = Math.Max((wedgeHigh - wedgeLow) * 0.03, 3 * tick);

                    // (1) Minimum wedge height vs ATR - drops micro measured moves / fake R:R
                    if (widestHeight < atr * MinWedgeHeightAtr)
                        continue;

                    double volMa = data.Skip(Math.Max(0, i - VolumeWindow + 1)).Take(VolumeWindow).Average(d => d.Volume);
                    double breakoutBuffer = Math.Max(tick, 0.15 * atr);

                    bool priceBreak = bar.Close >= resistance + breakoutBuffer;
                    bool volumeSpike =
                        bar.Volume >= volMa * BreakoutVolVsRecent &&
                        bar.Volume >= firstHalfAvg * BreakoutVolVsBase;

                    bool brokeOut = priceBreak && volumeSpike;

                    // (3) Failed-break filter: later bars closed back under the breakout line
                    if (brokeOut)
                    {
                        for (int j = i + 1; j < data.Count; j++)
                        {
                            if (data[j].Close < resistance)
                            {
                                brokeOut = false;
                                break;
                            }
                        }
                    }

                    // PDF entry: breakout above upper trendline (use buffer for noise)
                    double entry = Math.Round(resistance + breakoutBuffer, 4);

                    // PDF stop: just below the lowest point of the wedge
                    double stopLoss = Math.Round(wedgeLow - Math.Max(tick, 0.25 * atr), 4);

                    // PDF target: measured move = widest height projected up from breakout
                    double takeProfit = Math.Round(entry + widestHeight, 4);

                    double risk = Math.Max(tick, entry - stopLoss);
                    double reward = Math.Max(tick, takeProfit - entry);
                    double rr = reward / risk;
                    bool passesRr = rr >= MinRewardToRisk;

                    // (5) Risk band as % of entry - rejects penny stops and oversized stops
                    double riskPct = risk / Math.Max(entry, tick);
                    if (riskPct < MinRiskPct || riskPct > MaxRiskPct)
                        continue;

                    // Prefer decisive bullish breakout bar (close in upper half, green vs prior)
                    if (bar.Close < (bar.High + bar.Low) / 2.0)
                        continue;
                    if (i > 0 && bar.Close <= data[i - 1].Close)
                        continue;

                    double intradayPos = (bar.Close - bar.Low) / Math.Max(1e-6, bar.High - bar.Low);
                    if (intradayPos < 0.55)
                        continue;

                    string quality = compressionPct >= 0.25 && upperSteeper ? "A+" : compressionPct >= 0.12 ? "Good" : "OK";
                    string signal;
                    if (brokeOut && passesRr)
                        signal = $"{quality} Falling Wedge Breakout";
                    else if (brokeOut)
                        signal = $"{quality} Falling Wedge Breakout (RR soft)";
                    else
                        signal = $"{quality} Falling Wedge Setup";

                    // Trend flag: PDF allows reversal OR continuation; mark true if still above SMA50
                    double sma50 = data.Skip(Math.Max(0, i - 49)).Take(Math.Min(50, i + 1)).Average(d => d.Close);
                    bool inUptrendContext = bar.Close >= sma50;

                    results.Add(new StockSetups
                    {
                        Ticker = ticker,
                        Date = bar.Date,
                        Close = bar.Close,
                        High = bar.High,
                        Low = bar.Low,
                        Volume = bar.Volume,
                        VolMA = Math.Round(volMa, 2),
                        Trend = inUptrendContext,
                        Setup = !brokeOut,
                        Signal = signal,
                        ResistanceLevel = Math.Round(resistance, 4),
                        BreakoutPrice = entry,
                        IsFinalized = brokeOut && passesRr,
                        Compression = Math.Round(compressionPct, 4),
                        HighSlope = Math.Round(highSlope, 6),
                        LowSlope = Math.Round(lowSlope, 6),
                        SmoothedATR = Math.Round(atr, 4),
                        StopLoss = stopLoss,
                        TakeProfit = takeProfit,
                        RiskPerShare = Math.Round(risk, 4),
                        RewardPerShare = Math.Round(reward, 4),
                        RewardToRisk = Math.Round(rr, 2)
                    });
                }

                return results;
            }
        }
    }
}

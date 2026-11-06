using IdiotProof.Scripting;

namespace IdiotProof.Core.Monitoring;

/// <summary>The per-ticker facts a gapper plan is sized from.</summary>
public sealed record GapperPlanSignals(
    double Price, double GapPercent, double? PreviousClose,
    long? PremarketVolume, double? AvgDailyVolume, double? VolumeRatio, double? AtrPercent);

/// <summary>A synthesized plan: the behavior class, the tuned profile, the composed builder and the conviction score.</summary>
public sealed record GapperPlan(string Symbol, string Class, GapperProfile Profile, StrategyBuilder Builder, double Score)
{
    public string Title => $"{Symbol} Auto-Gapper +{Profile.MinGapPercent:0}% ({Class})";
}

/// <summary>
/// The one adaptive gapper synthesis (IP-LAW-10): price-tier behavior class, a stop sized to the
/// ticker's own volatility, a giveback that tightens with the gap, and a notional that grows with
/// premarket liquidity and shrinks with volatility. <see cref="AutoGapperScanner"/> and the
/// formation trigger both compose their <see cref="GapperProfile"/> here.
/// </summary>
public static class GapperPlanSynthesizer
{
    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

    public static GapperPlan Synthesize(string symbol, GapperPlanSignals s)
    {
        var (cls, stopMin, stopMax, trailMult, gvCap, arm, sellBy, minVol, baseNotional, typAtr) =
            s.Price < 5   ? ("penny",  6.0, 12.0, 1.25, 22.0, "09:10", "09:27", 3.0,  400.0, 12.0)
          : s.Price < 20  ? ("mid",    4.0,  8.0, 1.50, 28.0, "09:12", "09:28", 2.0,  800.0,  7.0)
          :                 ("large",  2.5,  5.0, 0.00, 35.0, "09:15", "09:29", 1.5, 1500.0,  4.0);

        var atr = s.AtrPercent is > 0 ? s.AtrPercent.Value : typAtr;
        var stop = Clamp(atr, stopMin, stopMax);
        double? trailing = cls == "large" ? null : Math.Round(stop * trailMult, 1);
        var gapGiveback = Clamp(30.0 - (s.GapPercent - 15.0) * 0.4, 15.0, 30.0);
        var giveback = Math.Min(gvCap, gapGiveback);

        var priceLow = Math.Round(Math.Max(0.1, s.Price * 0.6), 2);
        var priceHigh = Math.Round(s.Price * 1.8, 2);

        var convFactor = Clamp(1.0 + (s.VolumeRatio ?? 0.0), 0.75, 1.6);
        var volFactor = Clamp(atr / typAtr, 0.75, 1.6);
        var notional = Clamp(Math.Round(baseNotional * convFactor / volFactor / 50.0) * 50.0, 200.0, 3000.0);

        var profile = new GapperProfile
        {
            Id = "auto", Name = $"Auto {cls}",
            MinGapPercent = 15, MaxGapPercent = null,
            MinVolumeRatio = minVol, MinPrice = priceLow, MaxPrice = priceHigh,
            EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
            StopLossPercent = stop, TrailingStopPercent = trailing,
            PeakGivebackPercent = giveback, ArmExitAtEt = arm, SellByEt = sellBy,
            DefaultNotional = (decimal)notional,
        };
        var builder = GapperScriptFactory.Compose(symbol, profile);
        var score = s.GapPercent * (1.0 + (s.VolumeRatio ?? 0.0));
        return new GapperPlan(symbol, cls, profile, builder, score);
    }
}

using IdiotProof.Core.Evidence;
using IdiotProof.Models;
using IdiotProof.Operations.Reference;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Evidence;

public enum EvidenceTimeframe { Minute, Day }

public enum EvidenceSession { Rth, All }

/// <summary>How an edge between symbols is measured: lag correlation, or transfer entropy (catches nonlinear leads).</summary>
public enum EvidenceEdge { Correlation, Te }

/// <summary>Whether correlation edges are filtered by random-matrix theory before use.</summary>
public enum EvidenceClean { None, Rmt }

/// <summary>One symbol the penny universe may draw from; inactive ones are kept to fight survivorship bias.</summary>
public sealed record CandidateAsset(string Symbol, bool Active);

/// <summary>Where `evidence walkforward --universe` gets its candidate symbols (Alpaca /v2/assets in production).</summary>
public interface IEvidenceCandidateSource
{
    Task<IReadOnlyList<CandidateAsset>> GetCandidatesAsync(CancellationToken ct);
}

/// <summary>Every US equity Alpaca knows, active and inactive (no status filter), so since-delisted names stay candidates.</summary>
public sealed class AlpacaAssetCandidateSource(OperationContext ctx) : IEvidenceCandidateSource
{
    public async Task<IReadOnlyList<CandidateAsset>> GetCandidatesAsync(CancellationToken ct)
    {
        await using var api = await ReferenceSupport.TradingAsync(ctx, AlpacaAccount.Paper);
        var root = await api.GetAsync("/v2/assets", new Dictionary<string, string?> { ["asset_class"] = "us_equity" }, ct);
        return root.Items()
            .Select(a => new CandidateAsset((a.Str("symbol") ?? "").Trim().ToUpperInvariant(), a.Str("status") == "active"))
            .Where(c => c.Symbol.Length > 0 && !c.Symbol.Contains('.') && !c.Symbol.Contains('/'))
            .DistinctBy(c => c.Symbol)
            .ToList();
    }
}

internal static class EvidenceSupport
{
    public const string CleanWithTeMessage =
        "--clean rmt filters correlation edges; --edge te already keeps only edges that beat shuffled surrogates (BH-corrected). Use one or the other.";

    public static readonly TimeSpan RthOpen = new(9, 30, 0);
    public static readonly TimeSpan RthClose = new(16, 0, 0);

    public static DateOnly TodayEt(OperationContext ctx) => DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));

    public static DateOnly DateEt(Candle c) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Utc(c.StartUtc), MarketTime.Eastern));

    public static bool InRth(Candle c)
    {
        var tod = TimeZoneInfo.ConvertTimeFromUtc(Utc(c.StartUtc), MarketTime.Eastern).TimeOfDay;
        return tod >= RthOpen && tod < RthClose;
    }

    public static BarTimeframe Store(EvidenceTimeframe tf) => tf == EvidenceTimeframe.Day ? BarTimeframe.Day1 : BarTimeframe.Minute1;

    public static IReadOnlyList<Candle> Filter(IEnumerable<Candle> bars, EvidenceTimeframe tf, EvidenceSession session) =>
        bars.Where(c => c.IsValid && c.Close > 0 && (tf == EvidenceTimeframe.Day || session == EvidenceSession.All || InRth(c)))
            .OrderBy(c => c.StartUtc).ToList();

    public static IReadOnlyList<string> Symbols(IEnumerable<string> raw) =>
        raw.SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
           .Select(s => s.ToUpperInvariant()).Distinct().ToList();

    public static string Pct(double fraction, int decimals = 2) =>
        double.IsNaN(fraction) ? "—" : (fraction * 100).ToString("+0." + new string('0', decimals) + ";-0." + new string('0', decimals)) + "%";

    public static string Rate(double fraction) => double.IsNaN(fraction) ? "—" : (fraction * 100).ToString("0.00") + "%";

    public static double Round(double v, int digits = 6) => double.IsNaN(v) || double.IsInfinity(v) ? double.NaN : Math.Round(v, digits);

    private static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t, DateTimeKind.Utc);
}

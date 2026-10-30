using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// One round-trip, normalized for ML — entry-bar features → realized-P&amp;L label.
/// This is the SQL feature store the CSV export (replay-export) mirrors: query
/// <c>ReplayTrades</c> directly for per-trade supervised learning
/// (features → <see cref="Won"/> / <see cref="PnlPct"/>). Populated from a
/// <see cref="ReplayRun"/>; FK-linked so a run's trades cascade with it.
/// </summary>
public sealed class ReplayTrade
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReplayRunId { get; set; }

    [MaxLength(16)] public string Symbol { get; set; } = "";
    [MaxLength(10)] public string DateEt { get; set; } = "";
    [MaxLength(200)] public string Strategy { get; set; } = "";
    [MaxLength(8)] public string Feed { get; set; } = "";

    [MaxLength(8)] public string EntryEt { get; set; } = "";
    public int EntryMin { get; set; }
    public double EntryPx { get; set; }
    [MaxLength(8)] public string ExitEt { get; set; } = "";
    public int ExitMin { get; set; }
    public int HoldMin { get; set; }
    public double ExitPx { get; set; }

    public double PnlPct { get; set; }
    [MaxLength(24)] public string Reason { get; set; } = "";
    public bool Won { get; set; }

    // Entry-bar feature vector.
    public double EntryVwap { get; set; }
    public double EntryWindowHigh { get; set; }
    public double EntryVolx { get; set; }
    public double DistVwapPct { get; set; }
    public double DistWinHighPct { get; set; }

    public DateTime GeneratedUtc { get; set; }
}

/// <summary>
/// One minute bar, normalized for ML — the time-series feature row (price/VWAP/
/// window-high/volume ratio + condition progress + entry/exit flags). Mirrors
/// bars.csv; FK-linked to its <see cref="ReplayRun"/>.
/// </summary>
public sealed class ReplayBar
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReplayRunId { get; set; }

    [MaxLength(16)] public string Symbol { get; set; } = "";
    [MaxLength(10)] public string DateEt { get; set; } = "";
    [MaxLength(200)] public string Strategy { get; set; } = "";

    [MaxLength(8)] public string Et { get; set; } = "";
    public int Min { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public double Vwap { get; set; }
    public double WindowHigh { get; set; }
    public double Volx { get; set; }
    public bool InSession { get; set; }
    public int CondPassed { get; set; }
    public int CondTotal { get; set; }
    public bool Fire { get; set; }
    public bool Exit { get; set; }
}

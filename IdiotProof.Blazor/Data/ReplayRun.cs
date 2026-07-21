using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// One published strategy-replay run — the SQL system of record (IP-LAW-7) for
/// everything the replay harness renders. The static pages under
/// <c>/idiotproof/replays/&lt;ticker&gt;/&lt;stamp&gt;/</c> are a VIEW generated from
/// these rows: StrategyReplay writes the row, then renders the page from
/// <see cref="DataJson"/> + <see cref="StrategyHtml"/>, and the per-ticker and
/// root indexes are built by querying this table. The whole archive can be
/// regenerated from the DB alone (`replay-regen`), so SQL — not the file tree —
/// is authoritative.
/// </summary>
public sealed class ReplayRun
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string Symbol { get; set; } = "";

    [MaxLength(200)]
    public string Strategy { get; set; } = "";

    /// <summary>ET calendar day replayed, "yyyy-MM-dd".</summary>
    [MaxLength(10)]
    public string DateEt { get; set; } = "";

    /// <summary>Data tier actually used ("SIP" / "IEX").</summary>
    [MaxLength(8)]
    public string Feed { get; set; } = "";

    /// <summary>
    /// What this run represents: <c>"sim"</c> (default) — a hypothetical
    /// re-simulation of the strategy against the day's bars (the `replay`
    /// command); or <c>"live"</c> — the ACTUAL orders the Monitor executed,
    /// read from the trade diary and drawn on the same day's bars (the
    /// `replay-live` command). Both live in one archive, distinguished by badge.
    /// </summary>
    [MaxLength(8)]
    public string Kind { get; set; } = "sim";

    /// <summary>Folder id — the ET generation stamp "yyyy-MM-ddTHH.mm.ss[-a]".</summary>
    [MaxLength(40)]
    public string Stamp { get; set; } = "";

    /// <summary>UTC generation instant — the canonical ordering key (newest first).</summary>
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>ET generation stamp for display, "yyyy-MM-dd HH:mm:ss ET".</summary>
    [MaxLength(32)]
    public string GeneratedEt { get; set; } = "";

    public bool Fired { get; set; }
    public int PayoffCount { get; set; }
    public double TotalPnl { get; set; }

    /// <summary>First entry time ET "HH:mm" (null when no fire).</summary>
    [MaxLength(8)]
    public string? FirstFireEt { get; set; }

    public double? EntryPrice { get; set; }

    /// <summary>The full DATA payload (strict JSON) the run page renders from. nvarchar(max).</summary>
    public string DataJson { get; set; } = "";

    /// <summary>The strategy phase-card HTML fragment (StrategyDefinition.ToHtml()). nvarchar(max).</summary>
    public string StrategyHtml { get; set; } = "";
}

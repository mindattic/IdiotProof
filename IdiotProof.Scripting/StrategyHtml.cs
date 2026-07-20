using System.Net;
using System.Text;
using IdiotProof.Models;

namespace IdiotProof.Scripting;

/// <summary>
/// Renders a <see cref="StrategyDefinition"/> for humans: a clean six-phase
/// card fragment (<see cref="ToHtml"/>) and a Mermaid flowchart of the
/// evaluate→gates→order→exit lifecycle including any If/ElseIf/Else branching
/// (<see cref="ToMermaid"/>). Pure and dependency-free so the replay harness,
/// the Blazor UI, or any tool can render the same strategy identically.
///
/// The phase buckets mirror the authoring lifecycle in CLAUDE.md: Setup,
/// Filters, Entry, Order, Risk, Exit. Conditions are placed by their own
/// <see cref="ICondition.Phase"/> so the view can never disagree with how the
/// parser classified them.
/// </summary>
public static class StrategyHtml
{
    /// <summary>A self-contained HTML fragment: one card per non-empty phase.</summary>
    public static string ToHtml(this StrategyDefinition def)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"phases\">");

        // Setup
        var setup = new List<string>
        {
            Kv("Ticker", def.Symbol),
            Kv("Session", def.Session.ToString()),
            Kv("Direction", def.Direction.ToString()),
            Kv("Size", def.NotionalAmount is { } n ? $"${n:0.##} notional" : $"{def.Quantity} shares"),
        };
        Card(sb, "Setup", "ticker · session · size", setup);

        // Filters (regime gates) vs Entry (the trigger) — split by each
        // condition's own declared phase so the view matches the parser.
        var filters = def.EntryConditions.Where(c => c.Phase == StrategyPhase.Filters).Select(c => c.ToScript()).ToList();
        var entries = def.EntryConditions.Where(c => c.Phase != StrategyPhase.Filters).Select(c => c.ToScript()).ToList();
        if (filters.Count > 0) Card(sb, "Filters", "always-on regime gates", filters.Select(Li).ToList());
        if (entries.Count > 0) Card(sb, "Entry", "all must be true to fire (AND)", entries.Select(Li).ToList());

        // Branching (conditional order blocks), if any.
        foreach (var block in def.ConditionalBlocks)
        {
            var rows = new List<string>();
            for (int i = 0; i < block.Branches.Count; i++)
            {
                var b = block.Branches[i];
                var kind = i == 0 ? "If" : b.Condition is null ? "Else" : "ElseIf";
                var cond = b.Condition?.ToScript() ?? "(default)";
                rows.Add(Li($"<b>{kind}</b> {Esc(cond)} → {Esc(b.Overrides.ToScript())}"));
            }
            if (rows.Count > 0) Card(sb, "Branch", "conditional order overrides", rows);
        }

        // Order
        Card(sb, "Order", "what gets placed", new List<string>
        {
            Kv("Side", def.Direction == TradeDirection.Short ? "Sell short" : "Buy long"),
            Kv("Quantity", def.NotionalAmount is { } qn ? $"${qn:0.##}" : $"{def.Quantity} shares"),
        });

        // Risk
        var risk = new List<string>();
        if (def.StopLossPrice is { } sp) risk.Add(Kv("Hard stop", $"${sp:0.##}"));
        if (def.StopLossPercent is { } sc) risk.Add(Kv("Hard stop", $"{sc:0.#}%"));
        if (def.TrailingStopPercent is { } ts) risk.Add(Kv("Trailing", $"{ts:0.#}% off peak"));
        if (risk.Count == 0) risk.Add(Li("<span class=\"warn\">⚠ no stop — RiskGuardian would veto</span>"));
        Card(sb, "Risk", "the Guardian's rails", risk);

        // Exit
        var exit = new List<string>();
        if (def.TakeProfitPrice is { } tp) exit.Add(Kv("Take-profit", $"${tp:0.##}"));
        if (def.TakeProfitPercent is { } tpp) exit.Add(Kv("Take-profit", $"{tpp:0.#}%"));
        foreach (var t in def.TakeProfitTargets) exit.Add(Kv($"Target {t.Label}", $"${t.Price:0.##} · sell {t.PercentToSell}%"));
        if (def.TrailingStopPercent is { } ts2) exit.Add(Kv("Trailing stop", $"{ts2:0.#}% off peak"));
        if (def.PeakGivebackPercent is { } pg)
            exit.Add(Kv("Peak giveback", $"{pg:0.#}%{(def.PeakGivebackArmTime is { } arm ? $" armed {arm:hh\\:mm} ET" : "")}"));
        if (def.ExitTime is { } et) exit.Add(Kv("Sell-by", $"{et:hh\\:mm} ET"));
        if (exit.Count == 0) exit.Add(Li("(no target/time exit — stop only)"));
        Card(sb, "Exit", "whichever hits first", exit);

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// A Mermaid <c>flowchart TD</c> of the lifecycle: setup → each entry
    /// condition (AND) → the all-true gate → the three approval gates → order →
    /// exit, with any conditional block rendered as branch edges.
    /// </summary>
    public static string ToMermaid(this StrategyDefinition def)
    {
        // Conservative subset: `id["text"]` boxes, one `{text}` decision, plain
        // `-->` / `-->|label|` edges. Labels are sanitized to a safe ASCII set —
        // Mermaid's flow parser rejects parentheses, pipes, brackets and the
        // like even inside quotes (v11 "Syntax error in text").
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine($"  setup[\"Setup - {M(def.Symbol)} - {M(def.Session.ToString())}\"]");

        var conds = def.EntryConditions.ToList();
        string prev = "setup";
        if (conds.Count == 0)
        {
            sb.AppendLine("  gate{\"no conditions - fires in-session\"}");
            sb.AppendLine($"  {prev} --> gate");
        }
        else
        {
            for (int i = 0; i < conds.Count; i++)
            {
                sb.AppendLine($"  c{i}[\"{M(conds[i].ToScript())}\"]");
                sb.AppendLine($"  {prev} --> c{i}");
                prev = $"c{i}";
            }
            sb.AppendLine("  gate{\"ALL conditions true?\"}");
            sb.AppendLine($"  {prev} --> gate");
        }

        sb.AppendLine("  wait[\"keep waiting - next tick\"]");
        sb.AppendLine("  llm[\"Gate 2 - LLM voter quorum\"]");
        sb.AppendLine("  risk[\"Gate 3 - Risk Guardian\"]");
        var side = def.Direction == TradeDirection.Short ? "SELL short" : "BUY long";
        var size = def.NotionalAmount is { } n ? $"{n:0.##} notional" : $"{def.Quantity} shares";
        sb.AppendLine($"  order[\"Order - {M(side)} - {M(size)}\"]");
        sb.AppendLine("  gate -->|no| wait");
        sb.AppendLine("  gate -->|yes| llm");
        sb.AppendLine("  llm --> risk");
        sb.AppendLine("  risk --> order");

        var exits = new List<string>();
        if (def.StopLossPercent is { } sc) exits.Add($"stop {sc:0.#}pct");
        if (def.StopLossPrice is { } sp) exits.Add($"stop {sp:0.##}");
        if (def.TrailingStopPercent is { } ts) exits.Add($"trail {ts:0.#}pct");
        if (def.TakeProfitPrice is { } tp) exits.Add($"target {tp:0.##}");
        if (def.PeakGivebackPercent is { } pg) exits.Add($"giveback {pg:0.#}pct");
        if (def.ExitTime is { } et2) exits.Add($"sell-by {et2:hh\\:mm}");
        var exitLabel = exits.Count > 0 ? string.Join(" / ", exits) : "manual only";
        sb.AppendLine($"  exit[\"Exit - {M(exitLabel)}\"]");
        sb.AppendLine("  order --> exit");
        return sb.ToString();
    }

    // ── helpers ──
    private static void Card(StringBuilder sb, string phase, string sub, List<string> items)
    {
        sb.Append($"<div class=\"phase\" data-phase=\"{phase.ToLowerInvariant()}\">");
        sb.Append($"<div class=\"phase-h\"><span class=\"phase-n\">{Esc(phase)}</span><span class=\"phase-s\">{Esc(sub)}</span></div>");
        sb.Append("<ul>");
        foreach (var it in items) sb.Append(it);
        sb.Append("</ul></div>");
    }

    private static string Kv(string k, string v) => $"<li><span class=\"k\">{Esc(k)}</span><span class=\"v\">{Esc(v)}</span></li>";
    private static string Li(string html) => $"<li class=\"mono\">{html}</li>";
    private static string Esc(string s) => WebUtility.HtmlEncode(s ?? "");

    // Mermaid label text: keep a safe ASCII set only. The flow parser trips on
    // (), [], {}, |, ", ,, ; and non-ASCII even inside quotes, so map anything
    // outside [A-Za-z0-9 .%$/:-] to a space and collapse runs.
    private static string M(string s)
    {
        var sb = new StringBuilder((s ?? "").Length);
        foreach (var ch in s ?? "")
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 || " .%$/:-".IndexOf(ch) >= 0 ? ch : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

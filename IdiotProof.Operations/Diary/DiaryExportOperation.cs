using System.Globalization;
using System.Text;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Diary;

public sealed record DiaryExportArgs : DiaryFilterArgs
{
    [Option("csv", Description = "Absolute path of the CSV file to write on the Hub's machine, or - to return the CSV text.", Required = true)]
    public string Csv { get; init; } = "";

    [Option("overwrite", Description = "Replace the file if it already exists.")]
    public bool Overwrite { get; init; }
}

/// <summary>
/// Writes the diary (with the usual filters) to a CSV file: one row per trade, every column the
/// diary keeps plus mode, hold time and R, times in ET (with the UTC instant alongside). Pass
/// <c>--csv -</c> to get the CSV back instead of writing a file.
/// </summary>
[Operation("diary export", Summary = "Export your trade diary to a CSV file (ET times, P&L, risk plan, R), or print it with --csv -.", Risk = OperationRisk.Write)]
[Example("idiotproof diary export --csv C:/Users/me/Documents/diary.csv")]
[Example("idiotproof diary export --csv - --mode live --since 2026-01-01")]
public sealed class DiaryExportOperation : IOperation<DiaryExportArgs>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The CSV header, in column order.</summary>
    public static readonly string[] Columns =
    [
        "id", "strategy_id", "strategy", "symbol", "direction", "mode", "broker", "status",
        "entry_et", "entry_utc", "entry_price", "quantity", "notional", "entry_order_id",
        "exit_et", "exit_utc", "exit_price", "exit_reason", "exit_order_id",
        "realized_pnl", "return_pct", "stop_price", "stop_pct", "trail_pct", "target_price",
        "initial_risk", "r_multiple", "hold_minutes",
    ];

    public async Task<OperationResult> ExecuteAsync(DiaryExportArgs args, OperationContext ctx)
    {
        var target = (args.Csv ?? "").Trim();
        if (target.Length == 0) return OperationResult.Usage("--csv needs a file path (or - to print the CSV).");
        var toText = target == "-";
        if (!toText)
        {
            if (!Path.IsPathFullyQualified(target))
                return OperationResult.Usage("--csv needs an absolute path (the Hub writes it on its own machine), e.g. C:/Users/me/Documents/diary.csv.");
            if (File.Exists(target) && !args.Overwrite)
                return OperationResult.Usage($"{target} already exists; pass --overwrite to replace it.");
            if (Path.GetDirectoryName(target) is { } dir && !Directory.Exists(dir))
                return OperationResult.Usage($"Folder {dir} does not exist.");
        }

        var rows = (await DiarySupport.LoadAsync(ctx, args)).OrderBy(t => t.EntryUtc).Select(DiarySupport.ToRow).ToList();
        var csv = ToCsv(rows);

        if (toText)
            return OperationResult.Ok(new { Rows = rows.Count, Csv = csv }, csv.TrimEnd());

        await File.WriteAllTextAsync(target, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ctx.CancellationToken);
        var net = rows.Where(r => r.RealizedPnL is not null).Sum(r => r.RealizedPnL!.Value);
        return OperationResult.Ok(new { Path = target, Rows = rows.Count, Bytes = Encoding.UTF8.GetByteCount(csv) },
            $"Wrote {rows.Count} trades ({DiarySupport.Describe(args)}, realized {Fmt.SignedMoney(net)}) to {target}.");
    }

    /// <summary>RFC 4180 CSV with a header row; invariant numbers, ET and UTC timestamps.</summary>
    public static string ToCsv(IEnumerable<DiaryRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns));
        foreach (var r in rows)
        {
            string?[] cells =
            [
                r.Id.ToString(), r.StrategyId.ToString(), r.Strategy, r.Symbol, r.Direction, r.Mode, r.Broker, r.Status,
                Fmt.Et(r.EntryUtc), Iso(r.EntryUtc), Num(r.EntryPrice), r.Quantity.ToString(Inv), Num(r.Notional), r.EntryOrderId,
                r.ExitUtc is { } x ? Fmt.Et(x) : null, Iso(r.ExitUtc), Num(r.ExitPrice), r.ExitReason, r.ExitOrderId,
                Num(r.RealizedPnL), Num(r.ReturnPercent), Num(r.StopLossPrice), Num(r.StopLossPercent), Num(r.TrailingStopPercent),
                Num(r.TakeProfitPrice), Num(r.InitialRisk), Num(r.RMultiple), r.HoldMinutes?.ToString("0.#", Inv),
            ];
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        return sb.ToString();
    }

    private static string? Num(decimal? v) => v?.ToString(Inv);

    private static string? Iso(DateTime? utc) => utc is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", Inv) : null;

    private static string Escape(string? cell)
    {
        if (string.IsNullOrEmpty(cell)) return "";
        return cell.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
    }
}

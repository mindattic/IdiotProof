using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Streaming;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyProgressArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("watch", Description = "Keep sampling the Monitor's progress row and report each change: a terminal streams until Ctrl+C or all conditions pass; MCP/--json are bounded by --samples.", Short = 'w')]
    public bool Watch { get; init; }

    [Option("every", Description = "Seconds between samples while watching (0-60, default 5).")]
    public int Every { get; init; } = 5;

    [Option("samples", Description = "How many samples to take while watching (1-120; default 12 for MCP/--json, unlimited in a terminal).")]
    public int? Samples { get; init; }
}

[Operation("strategy progress", Summary = "How close a strategy is to firing: conditions passing N/M, the first failing verb and the last price, optionally watched.", Risk = OperationRisk.Read,
    Description = "Reads the row the Monitor writes every tick. --watch samples it every --every seconds and reports each change, stopping when every condition passes: in a terminal each change streams live until Ctrl+C; for MCP and --json it takes --samples samples (at most two minutes in all).")]
[Streaming]
[Example("idiotproof strategy progress NVDA")]
[Example("idiotproof strategy progress NVDA --watch --every 10 --samples 6")]
public sealed class StrategyProgressOperation : IOperation<StrategyProgressArgs>
{
    /// <summary>The longest a watch may run, whatever --every and --samples say.</summary>
    public static readonly TimeSpan MaxWatch = TimeSpan.FromMinutes(2);

    public async Task<OperationResult> ExecuteAsync(StrategyProgressArgs args, OperationContext ctx)
    {
        if (args.Every is < 0 or > 60) return OperationResult.Usage("--every is 0 to 60 seconds.");
        if (args.Samples is < 1 or > 120) return OperationResult.Usage("--samples is 1 to 120.");
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var repo = ctx.Get<ConditionProgressRepository>();
        var conditions = StrategyRefs.Load(s).Definition?.EntryConditions.Select(c => c.ToScript()).ToList() ?? [];

        var progress = await repo.GetAsync(s.Id, ctx.CancellationToken);
        var samples = new List<Sample> { Sample.Of(progress, ctx.UtcNow) };

        if (args.Watch && ctx.Sink is not null) return await WatchLiveAsync(args, ctx, s, repo, samples[0], progress?.IsFullPass ?? false);

        if (args.Watch)
        {
            var every = TimeSpan.FromSeconds(args.Every);
            var deadline = ctx.UtcNow + MaxWatch;
            for (var i = 1; i < (args.Samples ?? 12) && !(progress?.IsFullPass ?? false); i++)
            {
                if (ctx.UtcNow + every > deadline) break;
                if (every > TimeSpan.Zero) await Task.Delay(every, ctx.Clock, ctx.CancellationToken);
                progress = await repo.GetAsync(s.Id, ctx.CancellationToken);
                var next = Sample.Of(progress, ctx.UtcNow);
                if (!next.SameAs(samples[^1])) samples.Add(next);
            }
        }

        var header = $"{s.Symbol} '{s.Title}' — {(s.IsActive ? "active" : "INACTIVE (the Monitor isn't evaluating it)")}" +
                     (s.PositionQty > 0 ? $", holding {s.PositionQty} (exits are managed; entry progress is paused)" : "");
        var body = new List<string> { header, "" };
        if (progress is null) body.Add("No progress yet — the Monitor hasn't evaluated it (it only evaluates active strategies, in session).");
        else
        {
            body.Add($"{progress.PassedCount}/{progress.TotalCount} conditions passing{(progress.IsFullPass ? " — ALL PASS (fire gates next)" : "")}");
            body.Add($"waiting on: {progress.FirstFailingVerb ?? "nothing"}");
            if (progress.LastPrice is { } px) body.Add($"last price: {Fmt.Money(px)} ({Fmt.Et(progress.LastPriceUtc)})");
            body.Add($"evaluated: {Fmt.Et(progress.EvaluatedUtc)}");
        }
        if (conditions.Count > 0)
        {
            body.Add("");
            body.Add("conditions:");
            foreach (var c in conditions)
                body.Add((progress?.FirstFailingVerb is { } f && c.StartsWith(f, StringComparison.OrdinalIgnoreCase) ? "  ✗ " : "  · ") + c);
        }
        if (args.Watch)
        {
            body.Add("");
            var table = new TextTable { MaxWidth = ctx.Width }.Column("sampled").Column("cond", Align.Right).Column("waiting on").Column("price", Align.Right).Column("evaluated");
            foreach (var x in samples)
                table.Row(Fmt.EtTime(x.SampledUtc), x.Total is null ? "—" : $"{x.Passed}/{x.Total}", x.Failing ?? "", x.Price is { } p ? Fmt.Money(p) : "", Fmt.Et(x.EvaluatedUtc));
            body.Add(table.Render());
            body.Add($"{samples.Count} distinct reading(s).");
        }

        return OperationResult.Ok(new
        {
            s.Id, s.Symbol, s.IsActive, s.PositionQty,
            Progress = progress is null ? null : new { progress.PassedCount, progress.TotalCount, progress.FirstFailingVerb, progress.LastPrice, progress.EvaluatedUtc, progress.IsFullPass },
            Conditions = conditions,
            Samples = args.Watch ? samples : null,
        }, string.Join("\n", body));
    }

    /// <summary>
    /// <c>--watch</c> on the Hub's streaming endpoint: the current reading, then one line per change
    /// as the Monitor writes it, until every condition passes, --samples readings, or Ctrl+C.
    /// </summary>
    private static async Task<OperationResult> WatchLiveAsync(StrategyProgressArgs args, OperationContext ctx, Strategy s,
        ConditionProgressRepository repo, Sample first, bool fullPass)
    {
        using var stream = OperationStream.Open(ctx);
        await stream.EmitAsync($"{s.Symbol} '{s.Title}' — watching every {Math.Max(1, args.Every)}s (Ctrl+C stops)");
        await stream.EmitAsync(Line(first), first);
        var last = first;
        var taken = 1;
        await stream.RunAsync(async ct =>
        {
            while (!fullPass && (args.Samples is not { } cap || taken < cap))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, args.Every)), ct);
                var p = await repo.GetAsync(s.Id, ct);
                taken++;
                var next = Sample.Of(p, ctx.UtcNow);
                fullPass = p?.IsFullPass ?? false;
                if (next.SameAs(last)) continue;
                last = next;
                await stream.EmitAsync(Line(next) + (fullPass ? "  ALL PASS (fire gates next)" : ""), next);
            }
        });
        return stream.Finish($"{s.Symbol} '{s.Title}'", new { s.Id, s.Symbol, FullPass = fullPass, Samples = taken });
    }

    private static string Line(Sample x) =>
        $"  {Fmt.EtTime(x.SampledUtc),-11} {(x.Total is null ? "—" : $"{x.Passed}/{x.Total}"),6}  waiting on {x.Failing ?? "nothing"}" +
        (x.Price is { } p ? $"  @ {Fmt.Money(p)}" : "");

    /// <summary>One reading of the progress row.</summary>
    public sealed record Sample(DateTime SampledUtc, int? Passed, int? Total, string? Failing, decimal? Price, DateTime? EvaluatedUtc)
    {
        public static Sample Of(ConditionProgress? p, DateTime now) =>
            new(now, p?.PassedCount, p?.TotalCount, p?.FirstFailingVerb, p?.LastPrice, p?.EvaluatedUtc);

        public bool SameAs(Sample o) => Passed == o.Passed && Total == o.Total && Failing == o.Failing && Price == o.Price;
    }
}

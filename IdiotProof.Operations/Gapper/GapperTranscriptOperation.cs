using IdiotProof.Core.Identity;
using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Gapper;

public sealed record GapperFromTranscriptArgs
{
    [Argument(0, Name = "transcript", Description = "A transcript file path, the transcript text itself, or `-` to read it from stdin.")]
    public string Transcript { get; init; } = "";
}

/// <summary>
/// Turns trader talk (a video transcript, chat, notes) into gapper candidate cards through the
/// same <see cref="GapperInterpreter"/> the Gapper tab uses: Legion with the owner's Claude key,
/// strict JSON, every candidate re-validated (symbol shape, <c>GapperProfile.Validate</c>, the
/// five-candidate cap). Review-first: nothing is queued — each card ends with the exact
/// <c>gapper arm</c> command that would queue it.
/// </summary>
[Operation("gapper from-transcript", Summary = "Read a transcript and propose re-validated gapper candidate cards; nothing is queued until you `gapper arm` one.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper from-transcript notes.txt")]
[Example("idiotproof gapper from-transcript -", Description = "Pipe a transcript in: `type video.txt | idiotproof gapper from-transcript -`.")]
public sealed class GapperFromTranscriptOperation : IOperation<GapperFromTranscriptArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperFromTranscriptArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        var text = ReadSource(args.Transcript);
        if (string.IsNullOrWhiteSpace(text)) return OperationResult.Usage("The transcript is empty — pass a file, the text, or `-` and pipe it in.");

        var creds = await ctx.Get<IClaudeKeyContext>().ResolveAsync(ctx.CancellationToken);
        var result = await ctx.Get<GapperInterpreter>().InterpretAsync(text, creds, ctx.CancellationToken);

        var cards = result.Candidates.Select(c => new
        {
            c.Symbol,
            c.Rationale,
            c.Profile,
            Script = GapperRules.Compose(c.Symbol, c.Profile).Script,
            ArmCommand = ArmCommand(c),
        }).ToList();
        var data = new { result.Success, Candidates = cards, result.Warnings, result.Error };
        if (!result.Success)
            return OperationResult.Fail(result.Error ?? "No gapper candidates.", data);

        var parts = new List<string>();
        foreach (var c in cards)
        {
            var kv = GapperProfileShowOperation.Render(c.Profile);
            parts.Add($"── {c.Symbol} ──\n{c.Rationale}\n{kv.Render()}\nqueue: {c.ArmCommand}");
        }
        if (result.Warnings.Count > 0) parts.Add("Warnings:\n" + string.Join("\n", result.Warnings.Select(w => "  - " + w)));
        parts.Add($"{cards.Count} candidate(s), none queued. Review each card, then run its `gapper arm` line.");
        return OperationResult.Ok(data, string.Join("\n\n", parts));
    }

    /// <summary>A file path when one exists, else the argument is the transcript itself.</summary>
    internal static string ReadSource(string source)
    {
        var s = source ?? "";
        var trimmed = s.Trim();
        if (trimmed.Length is > 0 and < 260 && !trimmed.Contains('\n') && File.Exists(trimmed)) return File.ReadAllText(trimmed);
        return s;
    }

    /// <summary>The exact command that queues this candidate with its interpreted dials.</summary>
    internal static string ArmCommand(GapperCandidate c)
    {
        var p = c.Profile;
        var parts = new List<string>
        {
            $"idiotproof gapper arm {c.Symbol}",
            $"--gap {GapperRules.Num(p.MinGapPercent)}",
            $"--max-gap {(p.MaxGapPercent is { } mg ? GapperRules.Num(mg) : "0")}",
            $"--volume {GapperRules.Num(p.MinVolumeRatio)}",
            $"--min-price {GapperRules.Num(p.MinPrice)}",
            $"--max-price {GapperRules.Num(p.MaxPrice)}",
            $"--entry-start {p.EntryWindowStartEt}",
            $"--entry-end {p.EntryWindowEndEt}",
            $"--stop {GapperRules.Num(p.StopLossPercent)}",
            $"--trail {(p.TrailingStopPercent is { } t ? GapperRules.Num(t) : "0")}",
            $"--giveback {GapperRules.Num(p.PeakGivebackPercent)}",
            $"--arm-exit {p.ArmExitAtEt}",
            $"--sell-by {p.SellByEt}",
            $"--notional {p.DefaultNotional.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}",
        };
        return string.Join(' ', parts);
    }
}

using System.Text;

namespace IdiotProof.Core.Services;

/// <summary>
/// How many times a generator may send a model its own rejected output back (IP-T524). One
/// SettingsKv key, <see cref="RoundsKey"/>: the number of EXTRA model calls after the first, so 0
/// means "one call, no second chance". Deterministic fixes cost no model call and are not counted.
/// </summary>
public sealed record GenerationRepairPolicy(int MaxRounds)
{
    public const string RoundsKey = "llm.repair.rounds";
    public const int DefaultRounds = 2;
    public const int MaxAllowedRounds = 5;

    /// <summary>What applies when the key was never set.</summary>
    public static readonly GenerationRepairPolicy Default = new(DefaultRounds);

    public static async Task<GenerationRepairPolicy> ReadAsync(SettingsRepository settings, CancellationToken ct = default) =>
        new(Clamp(await settings.GetIntAsync(RoundsKey, ct) ?? DefaultRounds));

    public static int Clamp(int rounds) => Math.Clamp(rounds, 0, MaxAllowedRounds);
}

/// <summary>
/// What goes back to the model in a repair round: the round number (1 = the first extra call), its
/// own previous output word for word, the exact errors, and the fixes the validator can already name.
/// </summary>
public sealed record GenerationFeedback(int Round, string PreviousOutput, IReadOnlyList<string> Errors, IReadOnlyList<string> ProposedFixes)
{
    /// <summary>The line every repair request carries; the fake LLM reads the round number from it.</summary>
    public const string RoundMarker = "Repair round ";

    /// <summary>
    /// The block appended to the original request. It asks for the whole output again, in the same
    /// format, with only the named problems changed: a repair round fixes form, it is not a second
    /// chance to redesign the answer.
    /// </summary>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append(RoundMarker).Append(Round).AppendLine(": your previous output was rejected by the validator and nothing was saved.");
        sb.AppendLine();
        sb.AppendLine("Your previous output, exactly as received:");
        sb.AppendLine("<<<");
        sb.AppendLine(PreviousOutput);
        sb.AppendLine(">>>");
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (var e in Errors) sb.Append("- ").AppendLine(e);
        if (ProposedFixes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Proposed fixes (apply them unless they change what was asked for):");
            foreach (var f in ProposedFixes) sb.Append("- ").AppendLine(f);
        }
        sb.AppendLine();
        sb.AppendLine("Reply with the whole corrected output and nothing else, in the same format as before.");
        sb.AppendLine("Change only what the errors name. Keep every symbol, direction, size, price level, percent, count, time and condition that was correct.");
        sb.Append("Where an error asks for something the request never gave, follow the original instructions for it; never make up a value that contradicts the request.");
        return sb.ToString();
    }
}

/// <summary>
/// A validator's verdict on one model output. Exactly one of three things:
/// <para>valid as written: <see cref="Value"/> set, no errors, no fixes;</para>
/// <para>corrected without the model: <see cref="Value"/> set from the output with
/// <see cref="Fixes"/> applied, and <see cref="Errors"/> says what was wrong as written. The
/// validator must already have re-validated the corrected output in full;</para>
/// <para>rejected: no <see cref="Value"/>; <see cref="Errors"/> and <see cref="Fixes"/> (proposals
/// only) go back to the model, unless <see cref="Final"/> says asking again is pointless.</para>
/// </summary>
public sealed record GenerationCheck<T>(T? Value, IReadOnlyList<string> Errors, IReadOnlyList<string> Fixes, bool Final = false)
    where T : class
{
    public static GenerationCheck<T> Valid(T value) => new(value, [], []);

    public static GenerationCheck<T> Corrected(T value, IReadOnlyList<string> errorsAsWritten, IReadOnlyList<string> fixesApplied) =>
        new(value, errorsAsWritten, fixesApplied);

    public static GenerationCheck<T> Rejected(IReadOnlyList<string> errors, IReadOnlyList<string>? proposedFixes = null) =>
        new(null, errors, proposedFixes ?? []);

    /// <summary>The model itself said it cannot do this; no repair round is spent on it.</summary>
    public static GenerationCheck<T> Declined(string reason) => new(null, [reason], [], Final: true);
}

/// <summary>
/// One repair that was made or attempted. <see cref="Kind"/> is <c>deterministic</c> (fixes applied
/// to the output of model call <see cref="Round"/> with no further call; <see cref="Fixes"/> are
/// the edits made) or <c>model</c> (extra call number <see cref="Round"/>; <see cref="Problems"/>
/// and <see cref="Fixes"/> are what was sent back). <see cref="Resolved"/>: the result was accepted.
/// </summary>
public sealed record RepairStep(int Round, string Kind, IReadOnlyList<string> Problems, IReadOnlyList<string> Fixes, bool Resolved)
{
    public const string Deterministic = "deterministic";
    public const string Model = "model";
}

/// <summary>
/// What the repair loop did, for the operation result: model calls made, extra rounds used out of
/// the allowed maximum, deterministic fixes applied, and each step.
/// </summary>
public sealed record GenerationRepairReport(int ModelCalls, int Rounds, int MaxRounds, int DeterministicFixes, IReadOnlyList<RepairStep> Steps)
{
    public static readonly GenerationRepairReport None = new(0, 0, 0, 0, []);

    /// <summary>True when the first output was accepted as written.</summary>
    public bool Clean => Rounds == 0 && DeterministicFixes == 0;

    /// <summary>One or more lines for a terminal; empty when nothing was repaired.</summary>
    public string Render()
    {
        if (Steps.Count == 0) return "";
        var lines = new List<string> { $"Repair: {ModelCalls} model call(s), {Rounds} of {MaxRounds} repair round(s) used, {DeterministicFixes} deterministic fix(es)." };
        foreach (var s in Steps)
        {
            if (s.Kind == RepairStep.Deterministic)
                lines.Add($"  fixed without the model: {string.Join("; ", s.Fixes)}");
            else
                lines.Add($"  round {s.Round} ({(s.Resolved ? "accepted" : "still rejected")}), sent back: {string.Join("; ", s.Problems)}");
        }
        return string.Join("\n", lines);
    }
}

/// <summary>
/// How a generation ended. <see cref="Value"/> is set only when an output validated (as written, after
/// deterministic fixes, or after a model round); otherwise <see cref="Errors"/> holds the last
/// output's diagnostics. <see cref="Output"/> is the last text the model returned.
/// </summary>
public sealed record GenerationOutcome<T>(T? Value, string Output, IReadOnlyList<string> Errors, GenerationRepairReport Repair)
    where T : class
{
    public bool Success => Value is not null;
}

/// <summary>
/// The one repair loop behind every place a model writes something the system must be able to read
/// (IP-T524): ask, validate, and when the output is rejected send the model its own output with
/// the exact errors and the proposed fixes, at most <c>maxRounds</c> more times. It fails closed:
/// the only value it ever returns is one the validator accepted.
/// <para>
/// The loop knows nothing about strategies. <c>ask</c> makes the model call (null feedback = the
/// first call); <c>inspect</c> is the validator, and it alone decides whether a deterministic fix
/// is complete enough to accept without the model.
/// </para>
/// </summary>
public static class GenerationRepairLoop
{
    public static async Task<GenerationOutcome<T>> RunAsync<T>(
        Func<GenerationFeedback?, CancellationToken, Task<string>> ask,
        Func<string, GenerationCheck<T>> inspect,
        int maxRounds,
        CancellationToken ct = default)
        where T : class
    {
        maxRounds = GenerationRepairPolicy.Clamp(maxRounds);
        var steps = new List<RepairStep>();
        var deterministic = 0;
        GenerationFeedback? feedback = null;

        for (var round = 0; ; round++)
        {
            ct.ThrowIfCancellationRequested();
            var output = await ask(feedback, ct).ConfigureAwait(false) ?? "";
            var check = inspect(output);
            var accepted = check.Value is not null;
            if (feedback is not null)
                steps.Add(new RepairStep(round, RepairStep.Model, feedback.Errors, feedback.ProposedFixes, accepted));

            GenerationRepairReport Report() => new(round + 1, round, maxRounds, deterministic, steps);

            if (accepted)
            {
                if (check.Fixes.Count > 0)
                {
                    deterministic = check.Fixes.Count;
                    steps.Add(new RepairStep(round, RepairStep.Deterministic, check.Errors, check.Fixes, true));
                }
                return new GenerationOutcome<T>(check.Value, output, [], Report());
            }

            if (check.Final || round >= maxRounds)
                return new GenerationOutcome<T>(null, output, check.Errors, Report());

            feedback = new GenerationFeedback(round + 1, output, check.Errors, check.Fixes);
        }
    }
}

using System.Text.Json.Nodes;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Dsl;

public sealed record DslDiagnosticsArgs
{
    [Argument(0, Name = "code", Description = "One diagnostic code (IPS001, IPS013, ...); omit to list every code.", Required = false, Example = "IPS006")]
    public string? Code { get; init; }
}

/// <summary>One IdiotScript diagnostic code as the operations and the agent catalog publish it.</summary>
public sealed record DslDiagnosticCode(string Code, string Severity, string Title, string Meaning, bool CanFix, string? Fix, string? NoFix)
{
    public static DslDiagnosticCode From(ScriptDiagnosticDescriptor d) =>
        new(d.Code, d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Title, d.Meaning, d.CanFix, d.Fix, d.NoFix);

    public JsonObject ToJson() => new()
    {
        ["code"] = Code,
        ["severity"] = Severity,
        ["title"] = Title,
        ["meaning"] = Meaning,
        ["can_fix"] = CanFix,
        ["fix"] = Fix,
        ["no_fix"] = NoFix,
    };

    public string Render() => new KeyValueBlock()
        .Add("Code", Code)
        .Add("Severity", Severity)
        .Add("Title", Title)
        .Add("Meaning", Meaning)
        .Add(CanFix ? "Fix" : "No fix", CanFix ? Fix : NoFix)
        .Render();
}

/// <summary>
/// Every diagnostic code the IdiotScript analyzer can report (IP-T526): its severity, what it
/// means, and whether a diagnostic with that code can carry a fix, with what the fix does or why
/// there is none. Read from <see cref="ScriptDiagnosticCodes"/>, the one table
/// <see cref="ScriptDiagnostics"/> reports through, so this list cannot differ from what
/// <c>dsl validate</c> and <c>dsl parse</c> return (IP-LAW-4).
/// </summary>
[Operation("dsl diagnostics", Summary = "Every IdiotScript diagnostic code (IPS001...): severity, meaning, and what its fix does or why it has none.", Risk = OperationRisk.Read)]
[Example("idiotproof dsl diagnostics")]
[Example("idiotproof dsl diagnostics IPS006", Description = "One code.")]
[Example("idiotproof dsl diagnostics --json", Description = "The table as data (also in `agent catalog --json` under dsl.diagnostics).")]
public sealed class DslDiagnosticsOperation : IOperation<DslDiagnosticsArgs>
{
    public Task<OperationResult> ExecuteAsync(DslDiagnosticsArgs args, OperationContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(args.Code))
        {
            if (ScriptDiagnosticCodes.Find(args.Code) is not { } found)
                return Task.FromResult(OperationResult.Usage(
                    $"No diagnostic code '{args.Code.Trim()}'. Codes: {string.Join(", ", ScriptDiagnosticCodes.All.Select(d => d.Code))}."));
            var one = DslDiagnosticCode.From(found);
            return Task.FromResult(OperationResult.Ok(one, one.Render()));
        }

        var all = ScriptDiagnosticCodes.All.Select(DslDiagnosticCode.From).ToList();
        var table = new TextTable { MaxWidth = ctx.Width }.Column("Code").Column("Severity").Column("Title").Column("Fix").Column("Meaning");
        foreach (var d in all) table.Row(d.Code, d.Severity, d.Title, d.CanFix ? "yes" : "no", d.Meaning);
        return Task.FromResult(OperationResult.Ok(new { diagnostics = all },
            table.Render() + $"\n{all.Count} codes, {all.Count(d => d.CanFix)} can carry a fix. `idiotproof dsl diagnostics <code>` says what the fix does, or why there is none."));
    }
}

using System.Text.Json.Nodes;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyDiffArgs
{
    [Argument(0, Name = "a", Description = "First strategy: id, id prefix, stamp id or symbol.")]
    public string A { get; init; } = "";

    [Argument(1, Name = "b", Description = "Second strategy: id, id prefix, stamp id or symbol.")]
    public string B { get; init; } = "";
}

[Operation("strategy diff", Summary = "Compare two strategies field by field at the canon level (what the Monitor actually runs), plus title, mode and state.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy diff NVDA AMD")]
[Example("idiotproof strategy diff IP-P4AY-UXZU IP-Q7RB-K2MD")]
public sealed class StrategyDiffOperation : IOperation<StrategyDiffArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyDiffArgs args, OperationContext ctx)
    {
        var a = await StrategyRefs.ResolveAsync(ctx, args.A);
        var b = await StrategyRefs.ResolveAsync(ctx, args.B);
        var da = StrategyRefs.RequireDefinition(a);
        var db = StrategyRefs.RequireDefinition(b);

        var rows = Diff(Row(a), Row(b), "");
        rows.AddRange(Diff(JsonNode.Parse(StrategyJson.Serialize(da)), JsonNode.Parse(StrategyJson.Serialize(db)), ""));

        var head = $"a: {StrategyRefs.ShortGuid(a.Id)} {a.Symbol} '{a.Title}' {da.ShortId()}\nb: {StrategyRefs.ShortGuid(b.Id)} {b.Symbol} '{b.Title}' {db.ShortId()}";
        if (rows.Count == 0)
            return OperationResult.Ok(new { Same = true, Differences = rows }, head + "\n\nIdentical (same canon, title, mode and state).");

        var sameCanon = da.ShortId() == db.ShortId();
        var table = new TextTable { MaxWidth = ctx.Width }.Column("field", maxWidth: 40).Column("a", maxWidth: 40).Column("b", maxWidth: 40);
        foreach (var r in rows) table.Row(r.Path, r.A ?? "—", r.B ?? "—");
        return OperationResult.Ok(new { Same = false, SameCanon = sameCanon, StampA = da.ShortId(), StampB = db.ShortId(), Differences = rows },
            head + "\n\n" + table.Render() + $"\n{rows.Count} difference(s){(sameCanon ? "; the canons are identical — only the row settings differ" : "")}.");
    }

    /// <summary>One differing leaf: its JSON path and the value on each side (null when absent).</summary>
    public sealed record Difference(string Path, string? A, string? B);

    private static JsonNode Row(Strategy s) => new JsonObject
    {
        ["title"] = s.Title,
        ["mode"] = OrderIntent.ParseMode(s.BrokerMode).ToString(),
        ["active"] = s.IsActive,
    };

    /// <summary>Walks both JSON trees and reports every leaf that differs; arrays compare by index.</summary>
    internal static List<Difference> Diff(JsonNode? a, JsonNode? b, string path)
    {
        var list = new List<Difference>();
        switch (a, b)
        {
            case (JsonObject oa, JsonObject ob):
                foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
                {
                    if (key == "schemaVersion") continue;
                    list.AddRange(Diff(oa[key], ob[key], path.Length == 0 ? key : $"{path}.{key}"));
                }
                break;
            case (JsonArray xa, JsonArray xb):
                for (var i = 0; i < Math.Max(xa.Count, xb.Count); i++)
                    list.AddRange(Diff(i < xa.Count ? xa[i] : null, i < xb.Count ? xb[i] : null, $"{path}[{i}]"));
                break;
            default:
                var sa = Leaf(a);
                var sb = Leaf(b);
                if (sa != sb) list.Add(new Difference(path, sa, sb));
                break;
        }
        return list;
    }

    private static string? Leaf(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => n.ToJsonString(),
    };
}

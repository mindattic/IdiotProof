using System.Globalization;
using System.Reflection;
using System.Text;
using IdiotProof.Operations.Dsl;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Docs;

public sealed record DocsDslArgs
{
    [Argument(0, Name = "verb", Description = "An IdiotScript verb or alias: StopLoss, IsAboveVwap, OnReclaim, RequireAdxAbove, ...")]
    public string Verb { get; init; } = "";
}

/// <summary>
/// <c>docs dsl</c> (IP-T270): everything about one IdiotScript verb in one place: its phase and
/// every overload as reflected from <see cref="StrategyBuilder"/> / <see cref="Conditions"/>
/// (IP-LAW-4), the glossary meaning, the method's XML doc comment and source line, a worked
/// example built from the real signature, and where the checkout already uses it.
/// </summary>
[Operation("docs dsl", Summary = "Explain one IdiotScript verb: phase, signatures, meaning, doc comment, an example from its real signature, and uses.", Risk = OperationRisk.Read)]
[Example("idiotproof docs dsl StopLoss")]
[Example("idiotproof docs dsl IsAboveVwap --json")]
public sealed class DocsDslOperation : IOperation<DocsDslArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsDslArgs args, OperationContext ctx)
    {
        var wanted = DslVerbsOperation.NameOf(args.Verb);
        if (wanted.Length == 0) return OperationResult.Usage("Name a verb, e.g. `idiotproof docs dsl StopLoss` (`dsl verbs` lists them).");

        var glossary = DslGlossary.Find(wanted) ?? DslGlossary.All.FirstOrDefault(e => e.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        var catalog = Catalog();
        var name = catalog.Select(c => c.Name).FirstOrDefault(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                   ?? (glossary is not null ? catalog.Select(c => c.Name).FirstOrDefault(n => n == glossary.Key) : null)
                   ?? glossary?.Key;
        var forms = catalog.Where(c => c.Name == name).ToList();
        if (name is null || (forms.Count == 0 && glossary is null))
        {
            var near = catalog.Select(c => c.Name).Distinct()
                .Select(n => (Name: n, Distance: OperationNames.Distance(wanted.ToLowerInvariant(), n.ToLowerInvariant())))
                .Where(x => x.Distance <= 3 || x.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Distance).Take(5).Select(x => x.Name).ToList();
            throw OperationException.Failed($"No IdiotScript verb '{wanted}'." + (near.Count > 0 ? $" Did you mean: {string.Join(", ", near)}?" : " `dsl verbs` lists them."));
        }

        var index = await KnowledgeService.From(ctx).GetAsync(ct: ctx.CancellationToken);
        var docs = forms
            .Select(f => index.Get(XmlDocId(f.Method)))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
        var declared = docs.FirstOrDefault(d => d.Line > 0);
        var uses = index.Sources is { } sources
            ? sources.References(name, 40, declared is null ? null : new SourceLocation(declared.Source, declared.Line))
                .Where(r => r.Text.Contains(name + "(", StringComparison.Ordinal) && !r.Path.StartsWith("IdiotProof.Scripting/IdiotScript", StringComparison.Ordinal))
                .Take(5)
                .ToList()
            : [];

        var sb = new StringBuilder();
        sb.Append(name);
        if (glossary is not null) sb.Append(" — ").Append(glossary.Title);
        sb.Append('\n');
        sb.Append("Phase: ").Append(forms.Count > 0 ? string.Join(", ", forms.Select(f => f.Phase).Distinct()) : "(glossary only)");
        if (declared is not null) sb.Append("   Declared: ").Append(declared.Location);
        sb.Append('\n');
        if (glossary is not null) sb.Append('\n').Append(glossary.Description).Append('\n');
        if (forms.Count > 0)
        {
            sb.Append("\nSignatures:\n");
            foreach (var f in forms) sb.Append("  ").Append(f.Signature).Append('\n');
        }
        foreach (var d in docs.Where(d => d.Text.Length > 0).DistinctBy(d => d.Text))
            sb.Append('\n').Append(d.Text.Trim()).Append('\n');
        var example = forms.Count > 0 ? Example(forms[0]) : null;
        if (example is not null) sb.Append("\nExample:\n").Append(example).Append('\n');
        if (uses.Count > 0)
        {
            sb.Append("\nUsed in:\n");
            foreach (var u in uses) sb.Append("  ").Append(u).Append("  ").Append(u.Text).Append('\n');
        }

        var data = new
        {
            verb = name,
            phases = forms.Select(f => f.Phase).Distinct(),
            title = glossary?.Title,
            meaning = glossary?.Description,
            signatures = forms.Select(f => f.Signature),
            docs = docs.Select(d => new { id = d.Id, text = d.Text, location = d.Location }),
            example,
            uses = uses.Select(u => new { location = u.ToString(), text = u.Text }),
        };
        return OperationResult.Ok(data, sb.ToString().TrimEnd());
    }

    /// <summary>One reflected verb overload with the phase <c>dsl verbs</c> puts it in.</summary>
    internal sealed record VerbForm(string Phase, string Name, string Signature, MethodInfo? Method, bool IsCondition);

    /// <summary>Every verb overload: the phase grouping from <c>StrategyScriptGenerator</c>, the method from reflection.</summary>
    internal static IReadOnlyList<VerbForm> Catalog()
    {
        var forms = new List<VerbForm>();
        var builderMethods = typeof(StrategyBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(StrategyBuilder)).ToList();
        foreach (var group in StrategyScriptGenerator.GetVerbsByPhase())
        {
            foreach (var signature in group.Verbs)
            {
                var name = DslVerbsOperation.NameOf(signature);
                forms.Add(new VerbForm(group.Phase, name, signature, Match(builderMethods, name, signature), false));
            }
        }
        var conditionMethods = typeof(Conditions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(Conditions)).ToList();
        foreach (var signature in StrategyScriptGenerator.GetConditionCatalog())
        {
            var name = DslVerbsOperation.NameOf(signature);
            forms.Add(new VerbForm(DslVerbsOperation.ConditionsPhase, name, signature, Match(conditionMethods, name, signature), true));
        }
        return forms;
    }

    private static MethodInfo? Match(List<MethodInfo> methods, string name, string signature)
    {
        var candidates = methods.Where(m => m.Name == name).ToList();
        var paramCount = signature.Contains("()", StringComparison.Ordinal) ? 0 : signature.Count(c => c == ',') + 1;
        return candidates.FirstOrDefault(m => m.GetParameters().Length == paramCount) ?? candidates.FirstOrDefault();
    }

    /// <summary>A short chain using the verb with sample values for its real parameter types.</summary>
    internal static string? Example(VerbForm form)
    {
        if (form.Method is null) return form.IsCondition ? $"    .If({form.Name})" : null;
        var call = $"{form.Name}({string.Join(", ", form.Method.GetParameters().Where(p => !p.HasDefaultValue || p.Position == 0).Select(Sample))})";
        var sb = new StringBuilder("Stock.Ticker(\"SPY\")\n");
        if (form.IsCondition) sb.Append("    .If(").Append(call).Append(")\n");
        else if (form.Phase is "Setup" or "Filters" or "Entry") sb.Append("    .").Append(call).Append('\n');
        if (form.IsCondition || form.Phase is "Setup" or "Filters" or "Entry") sb.Append("    .Long()\n    .StopLoss(9.50)\n    .TakeProfit(12.00)");
        else if (form.Phase == "Order") sb.Append("    .IsAboveVwap()\n    .").Append(call).Append("\n    .StopLoss(9.50)\n    .TakeProfit(12.00)");
        else sb.Append("    .IsAboveVwap()\n    .Long()\n    .").Append(call);
        return sb.Append("\n    .Build();").ToString();
    }

    private static string Sample(ParameterInfo p)
    {
        if (p.HasDefaultValue && p.DefaultValue is not null)
            return p.DefaultValue is string s ? $"\"{s}\"" : Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) is { } v
                ? (p.ParameterType.IsEnum ? $"{p.ParameterType.Name}.{v}" : v.ToLowerInvariant() is "true" or "false" ? v.ToLowerInvariant() : v)
                : "default";
        var t = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        var n = (p.Name ?? "").ToLowerInvariant();
        if (t == typeof(string)) return n.Contains("time") || n.Contains("at") ? "\"09:45\"" : $"\"{p.Name}\"";
        if (t == typeof(bool)) return "true";
        if (t == typeof(int) || t == typeof(long)) return n.Contains("slow") ? "21" : n.Contains("period") || n.Contains("fast") || n.Contains("ema") ? "9" : "1";
        if (t == typeof(double) || t == typeof(decimal) || t == typeof(float))
            return n.Contains("percent") || n.Contains("pct") ? "2" : n.Contains("ratio") || n.Contains("mult") ? "1.5" : n.Contains("adx") || n.Contains("threshold") ? "20" : "10.00";
        if (t == typeof(TimeOnly)) return "new TimeOnly(9, 45)";
        if (t == typeof(TimeSpan)) return "TimeSpan.FromMinutes(15)";
        if (t.IsEnum) return $"{t.Name}.{Enum.GetNames(t).FirstOrDefault() ?? "?"}";
        return $"/* {t.Name} */";
    }

    /// <summary>The compiler's XML doc id of a method: <c>M:IdiotProof.Scripting.StrategyBuilder.StopLoss(System.Double)</c>.</summary>
    internal static string XmlDocId(MethodInfo? m)
    {
        if (m is null) return "";
        var ps = m.GetParameters();
        var prefix = $"M:{m.DeclaringType!.FullName}.{m.Name}";
        return ps.Length == 0 ? prefix : $"{prefix}({string.Join(",", ps.Select(p => DocTypeName(p.ParameterType)))})";
    }

    private static string DocTypeName(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner) return $"System.Nullable{{{DocTypeName(inner)}}}";
        if (t.IsArray) return DocTypeName(t.GetElementType()!) + "[]";
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition().FullName!;
            return $"{def[..def.IndexOf('`')]}{{{string.Join(",", t.GetGenericArguments().Select(DocTypeName))}}}";
        }
        return (t.FullName ?? t.Name).Replace('+', '.');
    }
}

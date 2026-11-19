using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Operations.Dsl;
using IdiotProof.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests;

/// <summary>
/// IP-T526: the agent catalog is self-describing, and every part of it is held to the code it
/// describes (IP-LAW-4). Every operation class is in the catalog with every argument described
/// (type, value kind, format, required, default, enum values, an example the binder accepts);
/// the value formats are the binder's own tables and behave as published; the error payload's
/// schema is the shape of the records the binder returns; every IdiotScript diagnostic code and
/// the canonical strategy JSON schema are published as the Scripting assembly defines them.
/// </summary>
public class SelfDescribingCatalogTests
{
    private static readonly Assembly[] Assemblies = [typeof(OperationCatalog).Assembly];

    private static readonly OperationCatalog Catalog = OperationCatalog.FromAssemblies(Assemblies);

    private static readonly JsonObject Json = Catalog.ToJsonNode();

    private static IEnumerable<string> AllOperationNames() => OperationCatalog.FromAssemblies(Assemblies).Operations.Select(o => o.Name);

    private static JsonObject Entry(string name) =>
        Json["operations"]!.AsArray().Select(o => o!.AsObject()).Single(o => o["name"]!.GetValue<string>() == name);

    private static string Snake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    private static IEnumerable<string> PropertiesOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").Select(p => Snake(p.Name));

    private static IEnumerable<string> SchemaProperties(JsonNode schema) => schema["properties"]!.AsObject().Select(p => p.Key);

    // ── operations and arguments ──

    [Test]
    public void EveryOperationClass_IsInTheCatalogJson()
    {
        var published = Json["operations"]!.AsArray().Select(o => o!["name"]!.GetValue<string>()).ToList();
        var declared = OperationCatalog.DiscoverTypes(Assemblies)
            .Select(t => t.GetCustomAttribute<OperationAttribute>(false)?.Name ?? t.FullName!)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(declared, Is.Not.Empty);
            Assert.That(published, Is.EquivalentTo(declared), "every [Operation] class is in agent catalog --json, and nothing else is");
        });
    }

    [TestCaseSource(nameof(AllOperationNames))]
    public void EveryArgument_IsDescribed(string name)
    {
        var d = Catalog.Find(name)!;
        var parameters = Entry(name)["parameters"]!.AsArray().Select(p => p!.AsObject()).ToDictionary(p => p["name"]!.GetValue<string>());
        var formats = Json["value_formats"]!.AsObject();

        // The args record's own attributed properties, read here rather than taken from the descriptor.
        var declared = d.ArgsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(OptionAttribute)) || p.IsDefined(typeof(ArgumentAttribute)))
            .Select(p => p.GetCustomAttribute<OptionAttribute>()?.Name ?? p.GetCustomAttribute<ArgumentAttribute>()!.Name ?? OperationNames.ToKebab(p.Name))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(parameters.Keys, Is.EquivalentTo(declared), name);
            foreach (var (parameter, p) in parameters)
            {
                var where = $"{name} --{parameter}";
                Assert.That(p["type"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty, where + " type");
                Assert.That(p["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty, where + " description");
                Assert.That(p["required"]!.GetValueKind(), Is.AnyOf(JsonValueKind.True, JsonValueKind.False), where + " required");
                Assert.That(p.ContainsKey("default"), Is.True, where + " default (null when there is none)");
                Assert.That(p["format"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty, where + " format");
                var kind = p["value_kind"]?.GetValue<string>();
                Assert.That(kind is not null && formats.ContainsKey(kind), Is.True, where + $" value_kind '{kind}' is not in value_formats");
                Assert.That(p["example"], Is.Not.Null, where + " example");
                Assert.That(p["example_source"]?.GetValue<string>(),
                    Is.AnyOf(ParameterExample.Declared, ParameterExample.FromExample, ParameterExample.FromDefault, ParameterExample.FromType), where);
                if (kind == "enum")
                {
                    var values = p["enum_values"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();
                    Assert.That(values, Is.Not.Empty, where + " enum_values");
                    Assert.That(values, Does.Contain(p["example"]!.GetValue<string>()), where + " example is one of its enum_values");
                }
                else
                {
                    Assert.That(p["enum_values"], Is.Null, where);
                }
            }
        });
    }

    [TestCaseSource(nameof(AllOperationNames))]
    public void EveryExample_IsAcceptedByTheBinder(string name)
    {
        var d = Catalog.Find(name)!;
        Assert.Multiple(() =>
        {
            foreach (var p in d.Parameters)
            {
                // The parameter's example, with the examples of whatever else the operation requires.
                var args = new JsonObject { [p.JsonName] = d.ExampleOf(p).Value?.DeepClone() };
                foreach (var other in d.Parameters.Where(o => o.Required && o != p)) args[other.JsonName] = d.ExampleOf(other).Value?.DeepClone();
                var bound = ArgsBinder.Bind(d, JsonSerializer.SerializeToElement(args));
                Assert.That(bound.Errors, Is.Empty, $"{name} --{p.Name}: {args.ToJsonString()}");
            }
        });
    }

    [Test]
    public void TextArguments_HaveARealExample_NotTheTypesPlaceholder()
    {
        var placeholders = Catalog.Operations
            .SelectMany(o => o.Parameters
                .Where(p => p.ValueKind is ParameterValueKind.String or ParameterValueKind.StringArray)
                .Where(p => o.ExampleOf(p).Source == ParameterExample.FromType)
                .Select(p => $"{o.Name} --{p.Name}"))
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(placeholders, Is.Empty,
                "give these an Example on their [Option]/[Argument], or pass them in one of the operation's [Example] lines");
        });
    }

    [Test]
    public void ADeclaredExample_TheBinderRefuses_IsACatalogProblem()
    {
        var problems = OperationCatalog.Validate([typeof(BadExampleOperation)]);
        Assert.That(problems.Single(), Does.Contain("'on'").And.Contain("\"tomorrow\"").And.Contain("a date"));
    }

    public sealed record BadExampleArgs
    {
        [Option("on", Description = "A date.", Example = "tomorrow")]
        public DateOnly? On { get; init; }
    }

    // Handed to the catalog by type only; the test assembly is never discovered.
    [Operation("test bad-example", Summary = "An example the binder would refuse.")]
    public sealed class BadExampleOperation : IOperation<BadExampleArgs>
    {
        public Task<OperationResult> ExecuteAsync(BadExampleArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
    }

    // ── value formats: the binder's tables, behaving as published ──

    private static OperationParameter Parameter(ParameterValueKind kind) => Fixtures.Everything.Parameters.First(p => p.ValueKind == kind);

    private static BindResult Bind(OperationParameter p, string text) =>
        ArgsBinder.Bind(Fixtures.Everything, p.Kind == ParameterKind.Argument ? [text] : ["A", $"--{p.Name}={text}"]);

    [Test]
    public void ValueFormats_CoverEveryKind_AndEachExampleBinds()
    {
        var published = Json["value_formats"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(published.Select(f => f.Key), Is.EquivalentTo(ValueFormats.All.Select(f => f.Type)));
            Assert.That(ValueFormats.All.Select(f => f.Kind), Is.EquivalentTo(Enum.GetValues<ParameterValueKind>()));
            foreach (var format in ValueFormats.All)
            {
                var p = Parameter(format.Kind);
                Assert.That(p.Format, Is.SameAs(format));
                Assert.That(format.Expects, Is.Not.Empty, format.Type);
                Assert.That(format.Accepts, Is.Not.Empty, format.Type);
                if (format.Kind == ParameterValueKind.Enum)
                {
                    Assert.That(p.TypeLabel, Is.EqualTo(string.Join('|', p.EnumValues)));
                    continue;
                }
                Assert.That(p.TypeLabel, Is.EqualTo(format.Type));
                var args = new JsonObject { ["ticker"] = "A", [p.JsonName] = format.Example!.DeepClone() };
                Assert.That(ArgsBinder.Bind(Fixtures.Everything, JsonSerializer.SerializeToElement(args)).Errors, Is.Empty, format.Type);
            }
        });
    }

    [Test]
    public void TheBindersDateTimeAndFlagForms_AreAccepted_AndThePublishedOnesAreThoseTables()
    {
        var inv = CultureInfo.InvariantCulture;
        var date = new DateOnly(2026, 10, 30);
        var time = new TimeOnly(9, 30, 0);
        Assert.Multiple(() =>
        {
            Assert.That(ValueFormats.For(ParameterValueKind.Date).Accepts, Is.EqualTo(ArgsBinder.DateFormats));
            Assert.That(ValueFormats.For(ParameterValueKind.Time).Accepts, Is.EqualTo(ArgsBinder.TimeFormats));
            foreach (var format in ArgsBinder.DateFormats)
                Assert.That(Bind(Parameter(ParameterValueKind.Date), date.ToString(format, inv)).Errors, Is.Empty, format);
            foreach (var format in ArgsBinder.TimeFormats)
                Assert.That(Bind(Parameter(ParameterValueKind.Time), time.ToString(format, inv)).Errors, Is.Empty, format);
            foreach (var word in ArgsBinder.TrueWords.Concat(ArgsBinder.FalseWords))
                Assert.That(Bind(Parameter(ParameterValueKind.Boolean), word.ToUpperInvariant()).Errors, Is.Empty, word);
        });
    }

    [Test]
    public void TheCorrectedForms_AreRefused_WithTheAcceptedSpellingAsTheFix()
    {
        var inv = CultureInfo.InvariantCulture;
        var date = new DateOnly(2026, 10, 30);
        var cases = new List<(ParameterValueKind Kind, string Sent, string Corrected)>();
        cases.AddRange(ArgsBinder.DateRepairFormats.Select(f => (ParameterValueKind.Date, date.ToString(f, inv), "2026-10-30")));
        cases.Add((ParameterValueKind.Time, new TimeOnly(9, 30).ToString(ArgsBinder.TimeDigitsFormat, inv), "09:30"));
        cases.AddRange(ArgsBinder.TimeMeridiemFormats.Select(f => (ParameterValueKind.Time, new TimeOnly(9, 0).ToString(f, inv), "09:00")));
        cases.AddRange(ArgsBinder.BoolShorthand.Select(kv => (ParameterValueKind.Boolean, kv.Key, kv.Value)));

        Assert.Multiple(() =>
        {
            foreach (var (kind, sent, corrected) in cases)
            {
                var p = Parameter(kind);
                var r = Bind(p, sent);
                Assert.That(r.Success, Is.False, $"'{sent}' is published as corrected, so it must not bind as sent");
                Assert.That(r.Problems.Single().Fix, Is.EqualTo(new CallChange(CallChangeKinds.Value, p.JsonName, sent, corrected)), sent);
                Assert.That(r.Fix, Is.Not.Null, sent);
            }
        });
    }

    // ── the error payload ──

    [Test]
    public void TheUsageErrorSchema_IsTheShapeOfTheRecordsTheBinderReturns()
    {
        var schema = Json["errors"]!["usage_error"]!["data_schema"]!;
        var error = schema["properties"]!["errors"]!["items"]!;
        var fix = schema["properties"]!["fix"]!;
        Assert.Multiple(() =>
        {
            Assert.That(SchemaProperties(schema), Is.EquivalentTo(PropertiesOf(typeof(UsageErrorData))));
            Assert.That(SchemaProperties(error), Is.EquivalentTo(PropertiesOf(typeof(BindError))));
            Assert.That(SchemaProperties(error["properties"]!["fix"]!), Is.EquivalentTo(PropertiesOf(typeof(CallChange))));
            Assert.That(SchemaProperties(fix), Is.EquivalentTo(PropertiesOf(typeof(CallFix))));
            Assert.That(SchemaProperties(fix["properties"]!["changes"]!["items"]!), Is.EquivalentTo(PropertiesOf(typeof(CallChange))));
            foreach (var node in new[] { schema, error, fix })
                foreach (var (key, property) in node["properties"]!.AsObject())
                    Assert.That(property!["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty, key + " has no description");
        });
    }

    [Test]
    public void ARealUsageError_SerializesToKeysTheSchemaNames()
    {
        var result = ArgsBinder.Bind(Fixtures.Everything, ["A", "--activ", "--limit", "5.0"]).ToUsageResult(Fixtures.Everything);
        var data = JsonSerializer.SerializeToNode(result.Data, OperationJson.Options)!.AsObject();
        var schema = Json["errors"]!["usage_error"]!["data_schema"]!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(data["fix"], Is.Not.Null, "both problems have one correction, so the corrected call rides along");
            Assert.That(data.Select(p => p.Key), Is.SubsetOf(SchemaProperties(schema)));
            Assert.That(data["fix"]!.AsObject().Select(p => p.Key), Is.SubsetOf(SchemaProperties(schema["properties"]!["fix"]!)));
            foreach (var error in data["errors"]!.AsArray())
                Assert.That(error!.AsObject().Select(p => p.Key), Is.SubsetOf(SchemaProperties(schema["properties"]!["errors"]!["items"]!)));
        });
    }

    [Test]
    public void TheChangeKinds_AreTheOnesTheBinderWrites()
    {
        var published = Json["errors"]!["usage_error"]!["change_kinds"]!.AsArray().Select(k => k!["kind"]!.GetValue<string>()).ToList();
        var written = new[]
        {
            ArgsBinder.Bind(Fixtures.Everything, ["A", "--activ"]),          // a misspelled option
            ArgsBinder.Bind(Fixtures.Everything, ["--ticker", "A"]),         // a positional passed by name
            ArgsBinder.Bind(Fixtures.Everything, ["A", "--limit", "5.0"]),   // a value with one reading
            ArgsBinder.BindJson(Fixtures.Everything, """{"ticker":"A","activ":true}"""),
        }.SelectMany(r => r.Problems).Select(p => p.Fix!.Kind).Distinct().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.EquivalentTo(CallChangeKinds.All.Select(k => k.Kind)));
            Assert.That(published, Is.Unique);
            Assert.That(written, Is.EquivalentTo(new[] { CallChangeKinds.Option, CallChangeKinds.Argument, CallChangeKinds.Value }));
            Assert.That(published, Is.SupersetOf(written));
            Assert.That(published, Does.Contain(CallChangeKinds.Operation));
        });
    }

    [Test]
    public void EveryStatus_IsPublished_WithItsExitCodeAndMeaning()
    {
        var statuses = Json["errors"]!["statuses"]!.AsArray().Select(s => s!.AsObject()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(statuses.Select(s => s["exit_code"]!.GetValue<int>()), Is.EqualTo(Enum.GetValues<OperationStatus>().Select(s => (int)s)));
            foreach (var s in statuses)
                Assert.That(s["meaning"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty, s["status"]!.GetValue<string>());
            // The name is the one the result envelope itself writes.
            var envelope = JsonNode.Parse(OperationResult.Usage("x").ToJson())!;
            Assert.That(Json["errors"]!["usage_error"]!["status"]!.GetValue<string>(), Is.EqualTo(envelope["status"]!.GetValue<string>()));
        });
    }

    // ── the DSL section ──

    [Test]
    public void EveryDiagnosticCode_IsPublished_AsTheAnalyzersTableDefinesIt()
    {
        var published = Json["dsl"]!["diagnostics"]!.AsArray().Select(d => d!.AsObject()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(published.Select(d => d["code"]!.GetValue<string>()), Is.EqualTo(ScriptDiagnosticCodes.All.Select(d => d.Code)));
            foreach (var d in published)
            {
                var code = d["code"]!.GetValue<string>();
                var descriptor = ScriptDiagnosticCodes.Find(code)!;
                Assert.That(d["severity"]!.GetValue<string>(), Is.EqualTo(descriptor.Severity == DiagnosticSeverity.Error ? "error" : "warning"), code);
                Assert.That(d["meaning"]!.GetValue<string>(), Is.EqualTo(descriptor.Meaning), code);
                Assert.That(d["can_fix"]!.GetValue<bool>(), Is.EqualTo(descriptor.CanFix), code);
                Assert.That(d["fix"]?.GetValue<string>(), Is.EqualTo(descriptor.Fix), code);
                Assert.That(d["no_fix"]?.GetValue<string>(), Is.EqualTo(descriptor.NoFix), code);
            }
        });
    }

    [Test]
    public void ADiagnosticTheAnalyzerReturns_HasACodeTheCatalogExplains()
    {
        var published = Json["dsl"]!["diagnostics"]!.AsArray().Select(d => d!["code"]!.GetValue<string>()).ToHashSet();
        var repair = ScriptDiagnostics.Repair("Ticker(\"NVDA\").IsAbveVwap().StopLoss(\"2\").IsAboveEma(nine).If(IsAboveVwap()).Then(StopLoss(1))");
        var data = JsonSerializer.SerializeToNode(DslRepair.Data(repair), OperationJson.Options)!;
        var schema = Json["dsl"]!["repair_schema"]!;
        Assert.Multiple(() =>
        {
            Assert.That(repair.Diagnostics, Is.Not.Empty);
            Assert.That(repair.Fixes, Is.Not.Empty);
            // Distinct: two diagnostics may share a code (two quoted numbers are two IPS006), and NUnit's SubsetOf counts duplicates.
            Assert.That(repair.Diagnostics.Select(d => d.Code).Distinct(), Is.SubsetOf(published));
            Assert.That(SchemaProperties(schema), Is.EquivalentTo(PropertiesOf(typeof(DslRepairData))));
            Assert.That(SchemaProperties(schema["properties"]!["diagnostics"]!["items"]!), Is.EquivalentTo(PropertiesOf(typeof(DslDiagnostic))));
            Assert.That(SchemaProperties(schema["properties"]!["fixes"]!["items"]!), Is.EquivalentTo(PropertiesOf(typeof(ScriptFix))));
            Assert.That(data.AsObject().Select(p => p.Key), Is.SubsetOf(SchemaProperties(schema)));
            foreach (var fix in data["fixes"]!.AsArray())
                Assert.That(fix!.AsObject().Select(p => p.Key), Is.EquivalentTo(SchemaProperties(schema["properties"]!["fixes"]!["items"]!)));
        });
    }

    [Test]
    public void TheStrategySchema_IsTheOneTheStrictReadersTablesGenerate()
    {
        var published = Json["dsl"]!["strategy_schema"]!;
        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(published, StrategyJson.Schema()), Is.True);
            foreach (var (name, keys) in StrategyJson.AcceptedKeys())
            {
                var schema = StrategyJson.SchemaDefinitionName(name) is { } definition ? published["$defs"]![definition]! : published;
                Assert.That(SchemaProperties(schema), Is.EquivalentTo(keys), name);
            }
        });
    }

    // ── the operations that serve the same tables ──

    private static OperationContext Context() => new() { Services = new ServiceCollection().BuildServiceProvider() };

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    [Test]
    public async Task DslDiagnostics_ListsEveryCode_AndExplainsOne()
    {
        var all = await new DslDiagnosticsOperation().ExecuteAsync(new DslDiagnosticsArgs(), Context());
        var one = await new DslDiagnosticsOperation().ExecuteAsync(new DslDiagnosticsArgs { Code = "ips013" }, Context());
        var none = await new DslDiagnosticsOperation().ExecuteAsync(new DslDiagnosticsArgs { Code = "IPS999" }, Context());

        Assert.Multiple(() =>
        {
            Assert.That(Data(all).GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()),
                Is.EqualTo(ScriptDiagnosticCodes.All.Select(d => d.Code)));
            Assert.That(all.Human, Does.Contain("IPS001").And.Contain("IPS025"));
            Assert.That(Data(one).GetProperty("code").GetString(), Is.EqualTo("IPS013"));
            Assert.That(Data(one).GetProperty("can_fix").GetBoolean(), Is.True);
            Assert.That(none.Status, Is.EqualTo(OperationStatus.UsageError));
            Assert.That(none.Message, Does.Contain("IPS001"));
        });
    }

    [Test]
    public async Task DslSchema_ReturnsTheSchema_OrOneObjectOfIt()
    {
        var whole = await new DslSchemaOperation().ExecuteAsync(new DslSchemaArgs(), Context());
        var one = await new DslSchemaOperation().ExecuteAsync(new DslSchemaArgs { Object = "indicatorCondition" }, Context());
        var none = await new DslSchemaOperation().ExecuteAsync(new DslSchemaArgs { Object = "nope" }, Context());

        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(Data(whole).GetRawText()), StrategyJson.Schema()), Is.True);
            Assert.That(whole.Human, Does.Contain("schemaVersion").And.Contain("indicatorCondition").And.Contain("trailingStopPercent"));
            Assert.That(Data(one).GetProperty("$ref").GetString(), Is.EqualTo("#/$defs/indicatorCondition"));
            Assert.That(Data(one).GetProperty("$defs").TryGetProperty("indicatorCondition", out _), Is.True);
            Assert.That(one.Human, Does.Contain("p1").And.Not.Contain("trailingStopPercent"));
            Assert.That(none.Status, Is.EqualTo(OperationStatus.UsageError));
        });
    }

    [Test]
    public async Task DslVerbs_CarriesTheDiagnosticCodes()
    {
        var r = await new DslVerbsOperation().ExecuteAsync(new DslVerbsArgs(), Context());
        Assert.That(Data(r).GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()),
            Is.EqualTo(ScriptDiagnosticCodes.All.Select(d => d.Code)));
    }
}

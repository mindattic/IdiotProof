using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Operations;

namespace IdiotProof.Operations.Tests;

/// <summary>
/// IP-T65: the shipped operation set is complete and self-consistent. Reflects every operation in
/// the IdiotProof.Operations assembly (add a host assembly to <see cref="Assemblies"/> as the Hub's
/// operations land) and proves each one has docs, a unique name, a schema, examples that bind,
/// and args that survive a JSON and an argv round trip.
/// </summary>
public class CatalogCompletenessTests
{
    private static readonly System.Reflection.Assembly[] Assemblies = [typeof(OperationCatalog).Assembly];

    private static OperationCatalog catalog = null!;

    [OneTimeSetUp]
    public void Build()
    {
        var problems = OperationCatalog.Validate(OperationCatalog.DiscoverTypes(Assemblies));
        Assert.That(problems, Is.Empty, "catalog problems:\n" + string.Join("\n", problems));
        catalog = OperationCatalog.FromAssemblies(Assemblies);
    }

    private static IEnumerable<string> OperationNames() =>
        OperationCatalog.FromAssemblies(Assemblies).Operations.Select(o => o.Name);

    [Test]
    public void Catalog_HasTheBuiltIns_AndNoCollisions()
    {
        Assert.That(catalog.Operations.Select(o => o.Name), Is.SupersetOf(new[] { "agent catalog", "hub ping" }));
        Assert.That(catalog.Operations.Select(o => o.Name), Is.Unique);
        Assert.That(catalog.Operations.Select(o => o.ToolName), Is.Unique);
    }

    [TestCaseSource(nameof(OperationNames))]
    public void Operation_IsDocumented(string name)
    {
        var d = catalog.Find(name)!;
        Assert.Multiple(() =>
        {
            Assert.That(d.Summary, Is.Not.Empty);
            Assert.That(d.Summary, Does.Not.EndWith(" "));
            Assert.That(d.Examples, Is.Not.Empty, "every operation ships at least one example");
            foreach (var p in d.Parameters) Assert.That(p.Description, Is.Not.Empty, $"{name} --{p.Name}");
            Assert.That(d.Help, Does.Contain(d.Usage));
        });
    }

    [TestCaseSource(nameof(OperationNames))]
    public void Operation_SchemaGeneratesAndMatchesParameters(string name)
    {
        var d = catalog.Find(name)!;
        var schema = d.JsonSchema;
        Assert.That(schema["type"]!.GetValue<string>(), Is.EqualTo("object"));
        var keys = schema["properties"]!.AsObject().Select(kv => kv.Key);
        Assert.That(keys, Is.EquivalentTo(d.Parameters.Select(p => p.JsonName)));
        Assert.That(JsonNode.Parse(schema.ToJsonString()), Is.Not.Null);
        Assert.That(catalog.ToJson(), Does.Contain($"\"tool_name\": \"{d.ToolName}\""));
    }

    [TestCaseSource(nameof(OperationNames))]
    public void Operation_ExamplesBind(string name)
    {
        var d = catalog.Find(name)!;
        foreach (var example in d.Examples)
        {
            var tokens = ArgvParser.Split(example.CommandLine).Skip(1).ToList(); // drop "idiotproof"
            var globals = ArgvParser.ExtractGlobals(tokens);
            Assert.That(globals.Error, Is.Null, example.CommandLine);
            Assert.That(catalog.TryResolve(globals.Remaining, out var resolved, out var rest), Is.True, example.CommandLine);
            Assert.That(resolved, Is.SameAs(d), example.CommandLine);
            var bound = ArgsBinder.Bind(d, rest);
            Assert.That(bound.Errors, Is.Empty, example.CommandLine);
        }
    }

    [TestCaseSource(nameof(OperationNames))]
    public void Operation_SampleArgsRoundTripJsonAndArgv(string name) => AssertRoundTrip(catalog.Find(name)!);

    [Test]
    public void RoundTrip_CoversEveryParameterType() =>
        AssertRoundTrip(Fixtures.Everything);

    /// <summary>
    /// Builds a sample value for every parameter, binds it once from JSON and once from argv, and
    /// requires both paths to produce the same args record.
    /// </summary>
    private static void AssertRoundTrip(OperationDescriptor d)
    {
        var json = new JsonObject();
        var argv = new List<string>();
        foreach (var p in d.Parameters)
        {
            var (jsonValue, text) = Sample(p);
            json[p.JsonName] = jsonValue;
            if (p.Kind == ParameterKind.Argument)
            {
                argv.Add(text);
                continue;
            }
            if (p.IsFlag) argv.Add("--" + p.Name);
            else argv.AddRange(["--" + p.Name, text]);
        }

        var fromJson = ArgsBinder.BindJson(d, json.ToJsonString());
        var fromArgv = ArgsBinder.Bind(d, argv);
        Assert.That(fromJson.Errors, Is.Empty, $"{d.Name} json: {json.ToJsonString()}");
        Assert.That(fromArgv.Errors, Is.Empty, $"{d.Name} argv: {string.Join(' ', argv)}");

        // Records compare arrays by reference, so compare the serialized forms.
        Assert.That(JsonSerializer.Serialize(fromArgv.Args, d.ArgsType, OperationJson.Options),
            Is.EqualTo(JsonSerializer.Serialize(fromJson.Args, d.ArgsType, OperationJson.Options)), d.Name);
    }

    private static (JsonNode Json, string Text) Sample(OperationParameter p) => p.ValueKind switch
    {
        ParameterValueKind.String => ("sample", "sample"),
        ParameterValueKind.Boolean => (true, "true"),
        ParameterValueKind.Int32 => (7, "7"),
        ParameterValueKind.Int64 => (9_000_000_000L, "9000000000"),
        ParameterValueKind.Decimal => (12.34m, "12.34"),
        ParameterValueKind.Double => (0.25, "0.25"),
        ParameterValueKind.Guid => ("0f8fad5b-d9cb-469f-a165-70867728950e", "0f8fad5b-d9cb-469f-a165-70867728950e"),
        ParameterValueKind.Date => ("2026-10-30", "2026-10-30"),
        ParameterValueKind.Time => ("09:30", "09:30"),
        ParameterValueKind.Enum => (p.EnumValues[^1], p.EnumValues[^1]),
        ParameterValueKind.StringArray => (new JsonArray("A", "B"), "A,B"),
        _ => throw new NotSupportedException(p.ValueKind.ToString()),
    };
}

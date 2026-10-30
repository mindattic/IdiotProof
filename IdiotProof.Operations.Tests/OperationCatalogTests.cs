using System.Text.Json.Nodes;
using IdiotProof.Operations;

namespace IdiotProof.Operations.Tests;

/// <summary>Reflection, validation and schema generation (IP-T51, IP-T54).</summary>
public class OperationCatalogTests
{
    [Test]
    public void Descriptor_ReflectsNameRiskExamplesAndParameters()
    {
        var d = Fixtures.Everything;
        Assert.Multiple(() =>
        {
            Assert.That(d.Noun, Is.EqualTo("test"));
            Assert.That(d.Verb, Is.EqualTo("everything"));
            Assert.That(d.ToolName, Is.EqualTo("test_everything"));
            Assert.That(d.Risk, Is.EqualTo(OperationRisk.Read));
            Assert.That(d.Examples, Has.Count.EqualTo(2));
            Assert.That(d.Parameters.Select(p => p.Name).Take(2), Is.EqualTo(new[] { "ticker", "tags" }), "arguments first, by position");
            Assert.That(d.Parameters.Any(p => p.Property.Name == "Display"), Is.False, "read-only computed properties are ignored");
        });

        var limit = d.Parameters.Single(p => p.Name == "limit");
        var side = d.Parameters.Single(p => p.Name == "side");
        Assert.Multiple(() =>
        {
            Assert.That(limit.DefaultValue, Is.EqualTo(50));
            Assert.That(limit.Short, Is.EqualTo('n'));
            Assert.That(side.EnumValues, Is.EqualTo(new[] { "buy", "short-sale" }));
            Assert.That(d.Parameters.Single(p => p.Name == "max-price").JsonName, Is.EqualTo("max_price"));
        });
    }

    [Test]
    public void PositionalRecord_DefaultsComeFromConstructor()
    {
        var d = Fixtures.Order;
        Assert.Multiple(() =>
        {
            Assert.That(d.Parameters.Single(p => p.Name == "qty").Required, Is.True);
            Assert.That(d.Parameters.Single(p => p.Name == "side").DefaultValue, Is.EqualTo(TradeSide.ShortSale));
            Assert.That(d.Parameters.Single(p => p.Name == "limit-price").DefaultValue, Is.Null);
            Assert.That(d.Risk, Is.EqualTo(OperationRisk.Money));
        });
    }

    [Test]
    public void Schema_IsDraft2020ObjectWithSnakeCaseProperties()
    {
        var s = Fixtures.Everything.JsonSchema;
        var props = s["properties"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(s["$schema"]!.GetValue<string>(), Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
            Assert.That(s["type"]!.GetValue<string>(), Is.EqualTo("object"));
            Assert.That(s["additionalProperties"]!.GetValue<bool>(), Is.False);
            Assert.That(s["required"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "ticker" }));
            Assert.That(props["max_price"]!["type"]!.GetValue<string>(), Is.EqualTo("number"));
            Assert.That(props["limit"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
            Assert.That(props["limit"]!["default"]!.GetValue<int>(), Is.EqualTo(50));
            Assert.That(props["active"]!["type"]!.GetValue<string>(), Is.EqualTo("boolean"));
            Assert.That(props["id"]!["format"]!.GetValue<string>(), Is.EqualTo("uuid"));
            Assert.That(props["on"]!["format"]!.GetValue<string>(), Is.EqualTo("date"));
            Assert.That(props["at"]!["pattern"], Is.Not.Null);
            Assert.That(props["side"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "buy", "short-sale" }));
            Assert.That(props["side"]!["default"]!.GetValue<string>(), Is.EqualTo("buy"));
            Assert.That(props["tags"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
            Assert.That(props["tags"]!["items"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
            Assert.That(props["tags"]!.AsObject().ContainsKey("default"), Is.False, "empty array defaults are not advertised");
            Assert.That(props["note"]!["description"]!.GetValue<string>(), Is.EqualTo("Free text."));
        });
    }

    [Test]
    public void Schema_IsACopy()
    {
        var d = Fixtures.Everything;
        d.JsonSchema["type"] = "mutated";
        Assert.That(d.JsonSchema["type"]!.GetValue<string>(), Is.EqualTo("object"));
    }

    [Test]
    public void Usage_And_Help_AreGenerated()
    {
        var d = Fixtures.Everything;
        Assert.That(d.Usage, Does.StartWith("idiotproof test everything <ticker> [<tags>...] [--active] [--limit <int>]"));
        Assert.That(d.Usage, Does.Contain("[--side <buy|short-sale>]"));
        Assert.That(Fixtures.Order.Usage, Does.Contain("--qty <int>").And.Not.Contain("[--qty"));
        Assert.That(d.Help, Does.Contain("-n, --limit <int>").And.Contain("(default: 50)").And.Contain("Examples:"));
    }

    [Test]
    public void Find_ByNameOrToolName_AndResolveFromArgv()
    {
        var catalog = Fixtures.Catalog();
        Assert.That(catalog.Find("test order"), Is.SameAs(catalog.Find("test_order")));
        Assert.That(catalog.Find("TEST   ORDER"), Is.Not.Null);
        Assert.That(catalog.TryResolve(["test", "order", "AAPL", "--qty", "1"], out var d, out var rest), Is.True);
        Assert.That(d!.Name, Is.EqualTo("test order"));
        Assert.That(rest, Is.EqualTo(new[] { "AAPL", "--qty", "1" }));
        Assert.That(catalog.TryResolve(["nope", "x"], out _, out _), Is.False);
        Assert.That(catalog.Suggest("test ordr"), Is.EqualTo("test order"));
    }

    [Test]
    public void ToJson_ListsEveryOperationWithSchema()
    {
        var node = JsonNode.Parse(Fixtures.Catalog().ToJson())!;
        var ops = node["operations"]!.AsArray();
        Assert.That(ops, Has.Count.EqualTo(Fixtures.Valid.Length));
        var order = ops.Single(o => o!["name"]!.GetValue<string>() == "test order")!;
        Assert.Multiple(() =>
        {
            Assert.That(order["tool_name"]!.GetValue<string>(), Is.EqualTo("test_order"));
            Assert.That(order["risk"]!.GetValue<string>(), Is.EqualTo("money"));
            Assert.That(order["input_schema"]!["type"]!.GetValue<string>(), Is.EqualTo("object"));
            Assert.That(order["parameters"]!.AsArray().Select(p => p!["json_name"]!.GetValue<string>()),
                Is.EqualTo(new[] { "ticker", "qty", "limit_price", "side" }));
        });
    }

    [TestCase(typeof(OneWordOperation), "two or more lower kebab-case words")]
    [TestCase(typeof(NoSummaryOperation), "has no Summary")]
    [TestCase(typeof(NoAttributeOperation), "has no [Operation] attribute")]
    [TestCase(typeof(GapOperation), "no gaps")]
    [TestCase(typeof(BadExampleOperation), "must start with 'idiotproof bad example'")]
    public void Validate_ReportsRuleViolations(Type type, string expected) =>
        Assert.That(OperationCatalog.Validate([type]), Has.Some.Contains(expected));

    [Test]
    public void Validate_ReportsEveryArgsProblemAtOnce()
    {
        var problems = OperationCatalog.Validate([typeof(BadArgsOperation)]);
        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("'thing' has no Description"));
            Assert.That(problems, Has.Some.Contains("'MaxPrice' must be lower kebab-case"));
            Assert.That(problems, Has.Some.Contains("'--json' is reserved"));
            Assert.That(problems, Has.Some.Contains("Forgotten has neither [Option] nor [Argument]"));
            Assert.That(problems, Has.Some.Contains("unsupported type"));
        });
    }

    [Test]
    public void FromTypes_RejectsDuplicateNames()
    {
        var ex = Assert.Throws<OperationCatalogException>(() =>
            OperationCatalog.FromTypes([typeof(EverythingOperation), typeof(DuplicateOperation)]));
        Assert.That(ex!.Problems, Has.Some.Contains("'test everything' is used by"));
    }

    [TestCase("MaxPrice", "max-price")]
    [TestCase("ETFOnly", "etf-only")]
    [TestCase("Top10", "top10")]
    [TestCase("Id", "id")]
    [TestCase("ShortSale", "short-sale")]
    public void ToKebab(string input, string expected) => Assert.That(OperationNames.ToKebab(input), Is.EqualTo(expected));

    [Test]
    public void ToolName_FromMultiWordVerb() =>
        Assert.That(OperationNames.ToToolName("order cancel-all"), Is.EqualTo("order_cancel_all"));
}

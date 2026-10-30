using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Mcp;
using IdiotProof.Operations;
using ModelContextProtocol.Protocol;

namespace IdiotProof.Mcp.Tests;

/// <summary>
/// The MCP surface is generated from the operation catalog, so these pin the generation rules:
/// every operation becomes exactly one tool with the catalog's schema, money/destructive tools
/// accept a confirmation code and say how to use it, risk drives the MCP hints, and Hub transport
/// failures come back as explainable tool errors instead of exceptions.
/// </summary>
[TestFixture]
public sealed class McpToolTests
{
    private static JsonNode Catalog() =>
        OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly).ToJsonNode();

    private static JsonNode Op(string name, string risk) => new JsonObject
    {
        ["name"] = name,
        ["tool_name"] = name.Replace(' ', '_'),
        ["summary"] = "Does " + name,
        ["risk"] = risk,
        ["usage"] = "idiotproof " + name,
        ["examples"] = new JsonArray(new JsonObject { ["command_line"] = "idiotproof " + name }),
        ["input_schema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["symbol"] = new JsonObject { ["type"] = "string" } },
        },
    };

    [Test]
    public void FromCatalog_EveryOperationBecomesOneUniquelyNamedTool()
    {
        var catalog = Catalog();
        var tools = ToolSpec.FromCatalog(catalog);

        Assert.That(tools.Select(t => t.Operation),
            Is.EquivalentTo(catalog["operations"]!.AsArray().Select(o => (string)o!["name"]!)));
        Assert.That(tools.Select(t => t.ToolName), Is.Unique);
        Assert.That(tools.All(t => t.InputSchema.GetProperty("type").GetString() == "object"), Is.True);
    }

    [Test]
    public void MoneyTools_AcceptAConfirmationCode_AndExplainTheLiveFlow()
    {
        var spec = ToolSpec.FromOperation(Op("trade buy", "money"));

        Assert.That(spec.InputSchema.GetProperty("properties").TryGetProperty(ToolSpec.ConfirmationProperty, out _), Is.True);
        Assert.That(spec.Description, Does.Contain("MOVES MONEY").And.Contain("show the user the preview"));
        Assert.That(spec.Destructive, Is.True);
    }

    [Test]
    public void ReadTools_DoNotAdvertiseAConfirmationCode()
    {
        var spec = ToolSpec.FromOperation(Op("strategy list", "read"));

        Assert.That(spec.InputSchema.GetProperty("properties").TryGetProperty(ToolSpec.ConfirmationProperty, out _), Is.False);
        Assert.That(spec.ReadOnly, Is.True);
    }

    [Test]
    public void HubTool_AnnotationsFollowRisk()
    {
        var hub = new HubInvoker(HubInvoker.CreateClient(new Uri("http://127.0.0.1:1/")));
        var read = new HubTool(ToolSpec.FromOperation(Op("strategy list", "read")), hub).ProtocolTool.Annotations!;
        var money = new HubTool(ToolSpec.FromOperation(Op("trade buy", "money")), hub).ProtocolTool.Annotations!;

        Assert.Multiple(() =>
        {
            Assert.That(read.ReadOnlyHint, Is.True);
            Assert.That(read.DestructiveHint, Is.False);
            Assert.That(money.ReadOnlyHint, Is.False);
            Assert.That(money.DestructiveHint, Is.True);
            Assert.That(money.OpenWorldHint, Is.True);
        });
    }

    private static HubOperationResponse Response(int exit, string text) => new()
    {
        Operation = "trade buy", Status = "x", ExitCode = exit, Text = text,
    };

    [Test]
    public void ToResult_GateVetoesAndUsageErrorsAreErrors_ConfirmationIsAQuestion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HubTool.ToResult(new(Response(0, "ok"), null, null)).IsError, Is.False);
            Assert.That(HubTool.ToResult(new(Response(2, "usage"), null, null)).IsError, Is.True);
            Assert.That(HubTool.ToResult(new(Response(3, "risk veto"), null, null)).IsError, Is.True);

            var confirm = HubTool.ToResult(new(Response(4, "BUY 10 NVDA · Live — code K7Q2ZP"), null, null));
            Assert.That(confirm.IsError, Is.False);
            Assert.That(((TextContentBlock)confirm.Content[0]).Text, Does.Contain("ONLY if they explicitly approve"));
        });
    }

    [Test]
    public async Task Invoker_HubDown_IsAStructuredError_NotAnException()
    {
        Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, "test-key");
        try
        {
            var invoker = new HubInvoker(HubInvoker.CreateClient(new Uri("http://127.0.0.1:9/"), new Refusing()));
            var outcome = await invoker.InvokeAsync(new HubOperationRequest { Operation = "hub ping" }, CancellationToken.None);

            Assert.That(outcome.ErrorCode, Is.EqualTo("hub_unreachable"));
            Assert.That(HubTool.ToResult(outcome).IsError, Is.True);
        }
        finally { Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, null); }
    }

    [Test]
    public async Task Invoker_SendsTheKeyAndCaller_AndReadsTheResponse()
    {
        Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, "test-key");
        try
        {
            var handler = new Recording();
            var invoker = new HubInvoker(HubInvoker.CreateClient(new Uri("http://127.0.0.1:5910/"), handler));
            var outcome = await invoker.InvokeAsync(new HubOperationRequest { Operation = "hub ping", Caller = OperationCaller.Mcp }, CancellationToken.None);

            Assert.That(handler.Key, Is.EqualTo("test-key"));
            Assert.That(handler.Body, Does.Contain("\"caller\":\"mcp\""));
            Assert.That(outcome.Response!.Text, Is.EqualTo("pong"));
        }
        finally { Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, null); }
    }

    [Test]
    public void ToolDocs_ListEveryTool()
    {
        var tools = ToolSpec.FromCatalog(Catalog());
        var md = ToolDocs.Render(tools);

        foreach (var t in tools) Assert.That(md, Does.Contain($"## `{t.ToolName}`"));
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class Recording : HttpMessageHandler
    {
        public string? Key { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Key = request.Headers.GetValues(HubWire.KeyHeader).Single();
            Body = await request.Content!.ReadAsStringAsync(ct);
            var json = JsonSerializer.Serialize(new HubOperationResponse { Operation = "hub ping", Status = "Ok", ExitCode = 0, Text = "pong" }, OperationJson.Options);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

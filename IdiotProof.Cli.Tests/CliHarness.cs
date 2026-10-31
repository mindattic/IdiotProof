using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

/// <summary>A Hub that lives in memory: health, catalog, version and /api/op with scripted answers.</summary>
internal sealed class FakeHub : HttpMessageHandler
{
    public bool Up { get; set; } = true;
    public string ExpectedKey { get; set; } = "test-key";
    public string? CatalogJson { get; set; }
    public TimeSpan OpDelay { get; set; } = TimeSpan.Zero;
    public List<HubOperationRequest> Requests { get; } = [];
    public List<string?> KeysSeen { get; } = [];
    public Func<HubOperationRequest, HubOperationResponse> OnOp { get; set; } = r => Ok("pong");

    /// <summary>The raw server-sent-events body /api/stream answers with (IP-T324); null = 404, as an older Hub.</summary>
    public Func<HubOperationRequest, string>? OnStream { get; set; }

    public List<HubOperationRequest> StreamRequests { get; } = [];

    public static HubOperationResponse Ok(string text) => Respond(OperationResult.Ok(human: text), "hub ping", OutputMode.Human);

    public static HubOperationResponse Respond(OperationResult result, string name, OutputMode output) => new()
    {
        Operation = name,
        Status = result.Status.ToString(),
        ExitCode = result.ExitCode,
        Text = result.Render(output),
        Envelope = JsonSerializer.SerializeToElement(result.ToEnvelope(), OperationJson.Options),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!Up) throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
        var path = request.RequestUri!.AbsolutePath;
        switch (path)
        {
            case HubWire.HealthPath:
                return Json(new HubHealth
                {
                    Status = "ok",
                    Version = "9.9.9",
                    StartedEt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    MonitorHosted = false,
                    Database = "(localdb) / Test",
                    Operations = 2,
                    ProcessId = 4242,
                });
            case HubWire.CatalogPath:
                return CatalogJson is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CatalogJson, Encoding.UTF8, "application/json") };
            case "/api/version":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("9.9.9") };
            case HubWire.OperationPath:
                var key = request.Headers.TryGetValues(HubWire.KeyHeader, out var values) ? values.FirstOrDefault() : null;
                KeysSeen.Add(key);
                if (key != ExpectedKey) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"hub_key_required\"}") };
                var body = await request.Content!.ReadAsStringAsync(ct);
                var op = JsonSerializer.Deserialize<HubOperationRequest>(body, OperationJson.Options)!;
                Requests.Add(op);
                if (OpDelay > TimeSpan.Zero) await Task.Delay(OpDelay, ct);
                return Json(OnOp(op));
            case HubWire.StreamPath:
                if (OnStream is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
                var streamOp = JsonSerializer.Deserialize<HubOperationRequest>(await request.Content!.ReadAsStringAsync(ct), OperationJson.Options)!;
                StreamRequests.Add(streamOp);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OnStream(streamOp), Encoding.UTF8, "text/event-stream") };
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, OperationJson.Options), Encoding.UTF8, "application/json"),
    };
}

/// <summary>Builds a <see cref="CliApp"/> over string writers, a temp folder and a <see cref="FakeHub"/>.</summary>
internal sealed class CliHarness : IDisposable
{
    public CliHarness(string stdin = "", string? key = "test-key", bool outputRedirected = true, Dictionary<string, string?>? envVars = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "idiotproof-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Paths = CliPaths.UnderRoot(Root);
        Env = new CliEnvironment
        {
            Out = Out,
            Err = Err,
            ReadStdin = () => { StdinReads++; return stdin; },
            IsInputRedirected = true,
            IsOutputRedirected = outputRedirected,
            Width = outputRedirected ? null : 120,
            GetEnv = name => envVars is not null && envVars.TryGetValue(name, out var v) ? v : null,
            Paths = Paths,
            HubHandler = Hub,
            HubKeySource = () => key,
            SlowNotice = TimeSpan.FromMilliseconds(100),
            BaseDirectory = Root,
            HubProcesses = Processes,
        };
    }

    public string Root { get; }
    public CliPaths Paths { get; }
    public FakeHub Hub { get; } = new();
    public FakeProcesses Processes { get; } = new();
    public StringWriter Out { get; } = new();
    public StringWriter Err { get; } = new();
    public CliEnvironment Env { get; }
    public int StdinReads { get; private set; }

    public Task<int> RunAsync(params string[] args) => new CliApp(Env).RunAsync(args);

    public void Dispose()
    {
        Hub.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

internal sealed class FakeProcesses : IHubProcessControl
{
    public List<int> Running { get; } = [];
    public List<int> Stopped { get; } = [];
    public List<HubLaunch> Started { get; } = [];

    public StartedHub Start(HubLaunch launch, string logFile)
    {
        Started.Add(launch);
        return new StartedHub(1, () => true);
    }

    public IReadOnlyList<int> FindHubProcessIds() => Running;

    public string Stop(int processId, TimeSpan grace)
    {
        Stopped.Add(processId);
        return $"process {processId} stopped";
    }
}

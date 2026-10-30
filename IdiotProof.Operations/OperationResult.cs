using System.Text.Json;

namespace IdiotProof.Operations;

/// <summary>
/// How an operation ended. The numeric value IS the process exit code the CLI returns, so the
/// mapping is fixed: 0 ok, 1 failed, 2 usage error, 3 blocked by a gate, 4 needs confirmation.
/// </summary>
public enum OperationStatus
{
    Ok = 0,
    Failed = 1,
    UsageError = 2,

    /// <summary>A safety gate (risk guardian, market hours, live-mode elevation) vetoed the action.</summary>
    Blocked = 3,

    /// <summary>Nothing happened yet: re-run with the returned confirmation code to proceed.</summary>
    ConfirmationRequired = 4,
}

/// <summary>
/// The second step of a two-step action: the operation previews what it would do and hands back
/// a code; the caller re-runs with <c>--confirm &lt;code&gt;</c> before <see cref="ExpiresAtUtc"/>.
/// </summary>
public sealed record OperationConfirmation(string Code, string Preview, DateTime ExpiresAtUtc);

/// <summary>
/// The one thing every operation returns. <see cref="Data"/> is the machine payload (serialized
/// for <c>--json</c>, MCP and the Hub API); <see cref="Human"/> is the rendered text a terminal
/// prints. Either may be null — <see cref="Render"/> falls back sensibly.
/// </summary>
public sealed record OperationResult
{
    public OperationStatus Status { get; init; }

    /// <summary>Process exit code for the CLI: the numeric value of <see cref="Status"/>.</summary>
    public int ExitCode => (int)Status;

    public bool IsOk => Status == OperationStatus.Ok;

    /// <summary>Structured payload; serialized with <see cref="OperationJson.Options"/>.</summary>
    public object? Data { get; init; }

    /// <summary>Text for a person at a terminal (may contain ANSI only when the context said Color).</summary>
    public string? Human { get; init; }

    /// <summary>Short status line: the error for failures, the reason for a block, the prompt for a confirmation.</summary>
    public string? Message { get; init; }

    public OperationConfirmation? Confirmation { get; init; }

    public static OperationResult Ok(object? data = null, string? human = null) =>
        new() { Status = OperationStatus.Ok, Data = data, Human = human };

    public static OperationResult Fail(string message, object? data = null) =>
        new() { Status = OperationStatus.Failed, Message = message, Data = data };

    public static OperationResult Usage(string message) =>
        new() { Status = OperationStatus.UsageError, Message = message };

    public static OperationResult Blocked(string reason, object? data = null) =>
        new() { Status = OperationStatus.Blocked, Message = reason, Data = data };

    public static OperationResult NeedsConfirmation(
        string code, string preview, DateTime expiresAtUtc, object? data = null) =>
        new()
        {
            Status = OperationStatus.ConfirmationRequired,
            Message = $"Confirmation required — re-run with --confirm {code} before {EasternClock.Format(expiresAtUtc)}.",
            Data = data,
            Confirmation = new OperationConfirmation(code, preview, EasternClock.AsUtc(expiresAtUtc)),
        };

    /// <summary>The envelope the Hub API, <c>--json</c> and MCP all return.</summary>
    public string ToJson(bool indented = false) =>
        JsonSerializer.Serialize(ToEnvelope(), indented ? OperationJson.Indented : OperationJson.Options);

    /// <summary>The serializable shape of this result (status as kebab text plus the exit code).</summary>
    public object ToEnvelope() => new ResultEnvelope(
        Status, ExitCode, Message, Data,
        Confirmation is null ? null : new ConfirmationEnvelope(Confirmation.Code, Confirmation.Preview, Confirmation.ExpiresAtUtc));

    /// <summary>
    /// What to print: JSON mode → the envelope; human mode → <see cref="Human"/>, then the message
    /// (plus the confirmation preview), then the data as indented JSON as a last resort.
    /// </summary>
    public string Render(OutputMode mode)
    {
        if (mode == OutputMode.Json) return ToJson(indented: true);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Human)) parts.Add(Human.TrimEnd());
        if (Confirmation is not null) parts.Add(Confirmation.Preview.TrimEnd());
        if (!string.IsNullOrEmpty(Message)) parts.Add(Status == OperationStatus.Ok ? Message : $"{StatusLabel}: {Message}");
        if (parts.Count == 0 && Data is not null) parts.Add(JsonSerializer.Serialize(Data, OperationJson.Indented));
        return string.Join(Environment.NewLine, parts);
    }

    private string StatusLabel => Status switch
    {
        OperationStatus.Failed => "Error",
        OperationStatus.UsageError => "Usage error",
        OperationStatus.Blocked => "Blocked",
        OperationStatus.ConfirmationRequired => "Confirm",
        _ => "OK",
    };

    private sealed record ResultEnvelope(
        OperationStatus Status, int ExitCode, string? Message, object? Data, ConfirmationEnvelope? Confirmation);

    private sealed record ConfirmationEnvelope(string Code, string Preview, DateTime ExpiresAt);
}

/// <summary>
/// Thrown from inside an operation (or its helpers) to end the call with a specific status; the
/// dispatcher turns it into the matching <see cref="OperationResult"/>. Anything else thrown is an
/// unexpected failure.
/// </summary>
public sealed class OperationException(OperationStatus status, string message, object? data = null)
    : Exception(message)
{
    public OperationStatus Status { get; } = status;

    /// <summary>Optional structured payload carried into <see cref="OperationResult.Data"/>.</summary>
    public object? Payload { get; } = data;

    public static OperationException Usage(string message) => new(OperationStatus.UsageError, message);

    public static OperationException Failed(string message, object? data = null) => new(OperationStatus.Failed, message, data);

    public static OperationException Blocked(string reason, object? data = null) => new(OperationStatus.Blocked, reason, data);

    public OperationResult ToResult() => new() { Status = Status, Message = Message, Data = Payload };
}

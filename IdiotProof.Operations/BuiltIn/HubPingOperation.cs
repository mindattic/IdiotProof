using System.Reflection;

namespace IdiotProof.Operations.BuiltIn;

/// <summary>
/// Liveness check for the Hub: proves the dispatcher, the DI scope and the clock all work and
/// shows the Hub's notion of "now" in ET (a skewed clock is worth knowing before a 9:30 entry).
/// </summary>
[Operation("hub ping", Summary = "Check the Hub is up and show its clock in ET.", Risk = OperationRisk.Read)]
[Example("idiotproof hub ping")]
public sealed class HubPingOperation : IOperation<NoArgs>
{
    private static readonly string Version =
        typeof(HubPingOperation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HubPingOperation).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var now = ctx.UtcNow;
        return Task.FromResult(OperationResult.Ok(
            new { Pong = true, Now = now, Version, Caller = ctx.Caller },
            $"pong — {ctx.Et(now)}"));
    }
}

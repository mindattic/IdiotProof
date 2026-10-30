using System.Text.Json;
using IdiotProof.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Operations;

/// <summary>
/// Who is calling and how the result will be shown — the transport-level facts the Hub (or a
/// test) knows before an operation runs. Everything here becomes part of <see cref="OperationContext"/>.
/// </summary>
public sealed record OperationInvocation
{
    public OperationCaller Caller { get; init; } = OperationCaller.Cli;

    public OutputMode Output { get; init; } = OutputMode.Human;

    /// <summary>An explicit owner (<c>--user</c>, the MCP session's user, the Blazor user). Wins over <see cref="IOwnerContext"/>.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>Why no owner could be resolved (an <see cref="OwnerResolver"/> message) — surfaced by <see cref="OperationContext.RequireOwner"/>.</summary>
    public string? OwnerError { get; init; }

    /// <summary><c>--confirm &lt;code&gt;</c>, echoed from an earlier ConfirmationRequired result.</summary>
    public string? ConfirmationCode { get; init; }

    /// <summary>The CLI is on a TTY and color is allowed.</summary>
    public bool Color { get; init; }

    /// <summary>Terminal width, when the CLI knows it.</summary>
    public int? Width { get; init; }
}

/// <summary>
/// The owner of the operation currently running in this DI scope. The dispatcher fills it before
/// the operation is resolved; <see cref="OperationOwnerContext"/> exposes it as an
/// <see cref="IOwnerContext"/> so scoped services (e.g. <c>OwnerClaudeKeyContext</c>) act for the
/// same user the operation does.
/// </summary>
public sealed class OperationOwner
{
    public Guid? OwnerId { get; set; }
}

/// <summary>
/// <see cref="IOwnerContext"/> backed by <see cref="OperationOwner"/>. A Hub that serves only
/// operations registers it: <c>services.AddScoped&lt;IOwnerContext, OperationOwnerContext&gt;()</c>.
/// </summary>
public sealed class OperationOwnerContext(OperationOwner owner) : IOwnerContext
{
    public Task<Guid?> GetOwnerIdAsync(CancellationToken ct = default) => Task.FromResult(owner.OwnerId);
}

/// <summary>
/// Runs operations by name for every front door: resolve the descriptor, open a DI scope, bind
/// args, establish the owner, execute, and turn every outcome — including exceptions — into an
/// <see cref="OperationResult"/>. Nothing escapes as an exception except caller cancellation
/// being reported as a Failed result; a crash becomes Failed with a one-line message (the stack
/// goes to the log, never into <see cref="OperationResult.Human"/>).
/// </summary>
public sealed class OperationDispatcher(
    OperationCatalog catalog,
    IServiceScopeFactory scopes,
    ILogger<OperationDispatcher>? logger = null)
{
    public OperationCatalog Catalog => catalog;

    /// <summary>MCP / Hub path: a named operation with a JSON object of args.</summary>
    public Task<OperationResult> DispatchAsync(
        string name, JsonElement args, OperationInvocation info, CancellationToken ct = default) =>
        RunAsync(name, d => ArgsBinder.Bind(d, args), info, ct);

    /// <summary>CLI path: a named operation with the argv tokens that followed its name.</summary>
    public Task<OperationResult> DispatchAsync(
        string name, IReadOnlyList<string> argv, OperationInvocation info, CancellationToken ct = default) =>
        RunAsync(name, d => ArgsBinder.Bind(d, argv), info, ct);

    /// <summary>
    /// CLI path when the operation name is still inside argv (<c>["strategy", "list", "--active"]</c>);
    /// global flags must already be removed (<see cref="ArgvParser.ExtractGlobals"/>).
    /// </summary>
    public Task<OperationResult> DispatchArgvAsync(
        IReadOnlyList<string> argv, OperationInvocation info, CancellationToken ct = default)
    {
        if (!catalog.TryResolve(argv, out var descriptor, out var rest))
        {
            var typed = string.Join(' ', argv.TakeWhile(t => !t.StartsWith('-')).Take(2));
            return Task.FromResult(UnknownOperation(typed));
        }
        return RunAsync(descriptor!.Name, d => ArgsBinder.Bind(d, rest), info, ct);
    }

    private async Task<OperationResult> RunAsync(
        string name, Func<OperationDescriptor, BindResult> bind, OperationInvocation info, CancellationToken ct)
    {
        var descriptor = catalog.Find(name);
        if (descriptor is null) return UnknownOperation(name);

        var bound = bind(descriptor);
        if (!bound.Success) return bound.ToUsageResult(descriptor);

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        try
        {
            var (ownerId, ownerError) = await ResolveOwnerAsync(services, info, ct);
            if (services.GetService<OperationOwner>() is { } holder) holder.OwnerId = ownerId;

            var ctx = new OperationContext
            {
                OwnerId = ownerId,
                OwnerError = ownerError,
                Services = services,
                Caller = info.Caller,
                Output = info.Output,
                Color = info.Color && info.Output == OutputMode.Human,
                Width = info.Width,
                ConfirmationCode = info.ConfirmationCode,
                CancellationToken = ct,
                Clock = services.GetService<TimeProvider>() ?? TimeProvider.System,
                Descriptor = descriptor,
            };

            var operation = services.GetRequiredService(descriptor.OperationType);
            var result = await descriptor.InvokeAsync(operation, bound.Args!, ctx);
            return result ?? OperationResult.Fail($"{descriptor.Name} returned no result.");
        }
        catch (OperationException ex)
        {
            return ex.ToResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Fail($"{descriptor.Name} was cancelled.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Operation {Operation} failed", descriptor.Name);
            return OperationResult.Fail($"{descriptor.Name} failed: {FirstLine(ex.Message)}");
        }
    }

    /// <summary>
    /// Explicit owner first; otherwise the host's <see cref="IOwnerContext"/> (when it is not just
    /// our own holder); otherwise none, with the invocation's error for RequireOwner to report.
    /// </summary>
    private static async Task<(Guid? OwnerId, string? Error)> ResolveOwnerAsync(
        IServiceProvider services, OperationInvocation info, CancellationToken ct)
    {
        if (info.OwnerId is { } explicitId) return (explicitId, null);
        var owner = services.GetService<IOwnerContext>() ?? new FixedOwnerContext(null);
        if (owner is OperationOwnerContext) return (null, info.OwnerError);
        return (await owner.GetOwnerIdAsync(ct), info.OwnerError);
    }

    private OperationResult UnknownOperation(string name)
    {
        var suggestion = catalog.Suggest(name);
        return OperationResult.Usage(suggestion is not null
            ? $"Unknown operation '{name}'. Did you mean '{suggestion}'?"
            : $"Unknown operation '{name}'. Run '{OperationDescriptor.ProgramName} agent catalog' to list operations.");
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].TrimEnd('\r');
        return line.Length <= 300 ? line : line[..299] + "…";
    }
}

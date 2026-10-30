using IdiotProof.Core.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations;

/// <summary>Which front door asked. Operations rarely care; audit and confirmation policy do.</summary>
public enum OperationCaller
{
    Cli,
    Mcp,
    Blazor,
    Test,
}

/// <summary>What the caller will do with the result: print <see cref="OperationResult.Human"/>, or serialize it.</summary>
public enum OutputMode
{
    Human,
    Json,
}

/// <summary>
/// Everything an operation knows about the call it is serving: who it acts for, the per-call DI
/// scope, which front door asked, how the output will be shown, cancellation and the clock.
/// Built by <see cref="OperationDispatcher"/> once per call; tests may construct one directly.
/// </summary>
public sealed class OperationContext
{
    /// <summary>The user the operation acts for, or null when nobody could be identified.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>
    /// Why there is no owner (an <see cref="OwnerResolver"/> message such as "Multiple users exist —
    /// pass --user &lt;guid&gt;"), reported by <see cref="RequireOwner"/>.
    /// </summary>
    public string? OwnerError { get; init; }

    /// <summary>The scope created for this call; disposed when the call ends.</summary>
    public required IServiceProvider Services { get; init; }

    public OperationCaller Caller { get; init; } = OperationCaller.Test;

    public OutputMode Output { get; init; } = OutputMode.Human;

    /// <summary>True only when the CLI is writing to a terminal that accepts ANSI color.</summary>
    public bool Color { get; init; }

    /// <summary>Terminal width when known, so tables can truncate to fit; null = unbounded.</summary>
    public int? Width { get; init; }

    /// <summary>
    /// The code the caller echoed back from an earlier <see cref="OperationStatus.ConfirmationRequired"/>
    /// result (<c>--confirm &lt;code&gt;</c>); null on a first call.
    /// </summary>
    public string? ConfirmationCode { get; init; }

    public CancellationToken CancellationToken { get; init; }

    /// <summary>The clock; tests substitute a fixed one. Defaults to the system clock.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The operation being run (null only for a hand-built test context).</summary>
    public OperationDescriptor? Descriptor { get; init; }

    public DateTime UtcNow => Clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// The owner, or a usage error (exit 2) telling the caller how to name one. Call it first in
    /// any operation that reads or writes per-user data; never fall back to "some user".
    /// </summary>
    public Guid RequireOwner() =>
        OwnerId ?? throw OperationException.Usage(OwnerError ?? OwnerResolver.AmbiguousMessage);

    /// <summary>A UTC instant as <c>2026-10-30 9:31 AM ET</c>.</summary>
    public string Et(DateTime utc) => EasternClock.Format(utc);

    /// <summary>Shorthand for <c>Services.GetRequiredService&lt;T&gt;()</c>.</summary>
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
}

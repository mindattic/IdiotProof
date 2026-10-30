namespace IdiotProof.Operations;

/// <summary>
/// One IdiotProof action, defined once. The dispatcher binds <typeparamref name="TArgs"/> from CLI
/// argv or MCP/Hub JSON, opens a DI scope, and calls <see cref="ExecuteAsync"/>; the class is
/// resolved from that scope, so it takes its services by constructor injection. Return an
/// <see cref="OperationResult"/> (or throw <see cref="OperationException"/>) — never write to the
/// console: the same result is rendered for a terminal, serialized for an agent, or shown in Blazor.
/// </summary>
/// <typeparam name="TArgs">A record whose properties carry <see cref="OptionAttribute"/> or
/// <see cref="ArgumentAttribute"/>; use <see cref="NoArgs"/> when there are none.</typeparam>
public interface IOperation<in TArgs> where TArgs : class
{
    Task<OperationResult> ExecuteAsync(TArgs args, OperationContext ctx);
}

/// <summary>The args record for an operation that takes no parameters.</summary>
public sealed record NoArgs;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdiotProof.Operations;

/// <summary>
/// Registers the operation framework next to <c>AddIdiotProofCore</c>: the catalog (built and
/// validated here, so a bad operation fails host startup, not a user's call), the dispatcher,
/// the per-call owner holder, and every operation type as scoped (one instance per call).
/// </summary>
public static class OperationServiceRegistration
{
    /// <summary>
    /// Adds every operation in <paramref name="assemblies"/> plus the built-ins in this assembly
    /// (<c>agent catalog</c>, <c>hub ping</c>).
    /// </summary>
    public static IServiceCollection AddIdiotProofOperations(this IServiceCollection services, params Assembly[] assemblies) =>
        services.AddIdiotProofOperations(OperationCatalog.DiscoverTypes([typeof(OperationCatalog).Assembly, .. assemblies]));

    /// <summary>Adds exactly these operation types (tests; hosts that expose a subset).</summary>
    public static IServiceCollection AddIdiotProofOperations(this IServiceCollection services, IEnumerable<Type> operationTypes)
    {
        var catalog = OperationCatalog.FromTypes(operationTypes);
        services.AddSingleton(catalog);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OperationDispatcher>();
        services.TryAddScoped<OperationOwner>();
        foreach (var descriptor in catalog.Operations) services.TryAddScoped(descriptor.OperationType);

        // The knowledge index behind `docs *` (IP-T266): one per host, warmed in the background at start.
        services.TryAddSingleton(_ => Docs.KnowledgeService.Shared);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, Docs.KnowledgeWarmup>());
        return services;
    }
}

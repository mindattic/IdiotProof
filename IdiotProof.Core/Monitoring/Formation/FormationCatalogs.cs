using IdiotProof.Core.Catalogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// Loads the two formation catalogs (IP-LAW-7) for DI. A missing or unreadable file is logged and
/// replaced by the safe value: the study's schedule for the times, and a rule that never passes.
/// </summary>
public static class FormationCatalogs
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDecisionTimesEt =
    [
        new(4, 0, 0), new(6, 0, 0), new(7, 0, 0), new(8, 0, 0), new(8, 30, 0), new(9, 0, 0),
        new(9, 15, 0), new(9, 25, 0), new(9, 30, 0), new(9, 31, 0), new(9, 35, 0), new(9, 45, 0),
    ];

    public static FormationSchedule Schedule(IServiceProvider sp)
    {
        var source = sp.GetService<IStaticCatalogSource>() ?? StaticCatalogSource.Default;
        try { return FormationSchedule.Load(source); }
        catch (Exception ex)
        {
            sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(FormationCatalogs))
                .LogWarning(ex, "{File} could not be loaded from {Path}; using the built-in study schedule.", FormationSchedule.FileName, source.Resolve(FormationSchedule.FileName));
            return new FormationSchedule(DefaultDecisionTimesEt);
        }
    }

    public static FormationTriggerRule TriggerRule(IServiceProvider sp)
    {
        var source = sp.GetService<IStaticCatalogSource>() ?? StaticCatalogSource.Default;
        try { return FormationTriggerRule.Load(source); }
        catch (Exception ex)
        {
            sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(FormationCatalogs))
                .LogWarning(ex, "{File} could not be loaded from {Path}; the trigger never passes.", FormationTriggerRule.FileName, source.Resolve(FormationTriggerRule.FileName));
            return FormationTriggerRule.Never;
        }
    }
}

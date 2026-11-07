using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Hosting;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Services;
using IdiotProof.Engine.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>
/// A throwaway study: a temp copy of the frozen params and hypotheses, a temp bar store, an in-memory
/// research database and every stage registered, so a test can seed rows and run <c>test</c>,
/// <c>report</c>, <c>calibrate-spread</c> or the Form 4 pull against them.
/// </summary>
internal sealed class GapperStudyHost : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);

    public string StudyDir { get; }
    public string BarRoot { get; }
    public GapperStudyPaths Paths { get; }
    public ServiceProvider Services { get; }
    public AppSettings Settings { get; } = new();

    public GapperStudyHost(bool withAlpacaKeys = false)
    {
        StudyDir = Path.Combine(Path.GetTempPath(), "gapper-host-" + Guid.NewGuid().ToString("N"));
        BarRoot = Path.Combine(StudyDir, "bars");
        Directory.CreateDirectory(StudyDir);
        var real = GapperStudyTestKit.Paths;
        File.Copy(real.ParamsFile, Path.Combine(StudyDir, "params.json"));
        File.Copy(real.HypothesesFile, Path.Combine(StudyDir, "hypotheses.json"));
        Environment.SetEnvironmentVariable(GapperStudyPaths.EnvVar, StudyDir);
        Environment.SetEnvironmentVariable("GAPPER_STUDY_BAR_ROOT", BarRoot);
        Paths = new GapperStudyPaths(StudyDir);
        if (withAlpacaKeys) { Settings.AlpacaApiKeyId = "key"; Settings.AlpacaApiSecretKey = "secret"; }

        Services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Now))
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"gapper-host-{Guid.NewGuid():N}"))
            .AddSingleton(new IdiotProofDatabase(@"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof_Research_Test;"))
            .AddSingleton(Settings)
            .AddSingleton<SettingsRepository>()
            .AddSingleton<GapperStudyServices>(sp => new GapperStudyServices(
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<IdiotProofDatabase>(), sp.GetRequiredService<AppSettings>(),
                null!, sp.GetRequiredService<SettingsRepository>(), sp.GetRequiredService<TimeProvider>(), NullLogger<GapperStudyServices>.Instance))
            .AddSingleton<GapperSnapshotStage>()
            .AddSingleton<GapperLabelStage>()
            .AddSingleton<GapperStatusStage>()
            .AddSingleton<GapperStudyPanel>()
            .AddSingleton<GapperTestStage>()
            .AddSingleton<GapperSpreadCalibrationStage>()
            .AddSingleton<GapperReportStage>()
            .AddSingleton<IdiotProof.Core.Research.Gappers.Explore.GapperExploreStage>()
            .BuildServiceProvider();
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public Task<AppDbContext> DbAsync() => Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    public FileBackedBarStore Store => new(BarRoot);

    public GapperStudyParams Params => Get<GapperStudyServices>().Params;

    public void Dispose()
    {
        Services.Dispose();
        Environment.SetEnvironmentVariable(GapperStudyPaths.EnvVar, null);
        Environment.SetEnvironmentVariable("GAPPER_STUDY_BAR_ROOT", null);
        try { if (Directory.Exists(StudyDir)) Directory.Delete(StudyDir, true); } catch (IOException) { }
    }
}

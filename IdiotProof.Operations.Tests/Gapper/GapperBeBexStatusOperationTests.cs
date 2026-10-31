using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Monitor;
using IdiotProof.Operations.Gapper;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper be-bex status (IP-T173): the stored decay reading and the gated BE strategies' arm state.</summary>
public class GapperBeBexStatusOperationTests
{
    [Test]
    public async Task ShowsTheLatestReading_TheVerdict_AndWhichGatedStrategiesTheGateDisarmed()
    {
        using var kit = new GapperKit();
        var settings = kit.Services.GetRequiredService<SettingsRepository>();
        await settings.SetAsync("decay.be-bex.latest",
            """{"computedUtc":"2026-10-29T20:45:00Z","lookbackDays":20,"beReturnPercent":12.5,"expectedBexReturnPercent":25,"actualBexReturnPercent":14.2,"decayPercent":10.8}""");

        Strategy gated;
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            gated = new Strategy { OwnerUserId = kit.Owner, Symbol = "BE", Title = "BE pullback", ScriptText = "x", Author = BeBexDecayScanner.DecayGatedAuthorTag, IsActive = false };
            db.Strategies.Add(gated);
            db.Strategies.Add(new Strategy { OwnerUserId = kit.Owner, Symbol = "BE", Title = "my own BE idea", ScriptText = "x", IsActive = true });
            await db.SaveChangesAsync();
        }
        await settings.SetAsync($"decay.be-bex.disarmed.{gated.Id}", "true");

        var result = await new GapperBeBexStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Human, Does.Contain("+10.80%").And.Contain("DISARM").And.Contain("decay ≥ 8%").And.Contain("20 trading days"));
            Assert.That(result.Human, Does.Contain("4:45 PM"), "the reading's time is shown in ET");
            Assert.That(result.Human, Does.Contain("BE pullback").And.Not.Contain("my own BE idea"));
        });
    }

    [Test]
    public async Task NoReadingYet_SaysSo()
    {
        using var kit = new GapperKit();
        var result = await new GapperBeBexStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());
        Assert.That(result.Human, Does.Contain("no reading yet").And.Contain("No gated BE strategies"));
    }
}

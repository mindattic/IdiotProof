using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Risk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Risk;

/// <summary>risk show|set|status|kill-switch|check (IP-T216..IP-T220) against EF InMemory and the real Risk Guardian.</summary>
public class RiskOperationsTests
{
    private ServiceProvider services = null!;
    private readonly Guid owner = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        var sc = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"risk-{Guid.NewGuid():N}"));
        sc.AddSingleton(TimeProvider.System);
        sc.AddSingleton<RiskGuardianService>();
        sc.AddSingleton<UserPreferencesService>();
        sc.AddSingleton<ConfirmationStore>();
        services = sc.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => services.Dispose();

    private OperationContext Context(string? confirm = null) => new()
    {
        Services = services, OwnerId = owner, ConfirmationCode = confirm, Caller = OperationCaller.Cli,
    };

    private static JsonElement Data(OperationResult r) => JsonSerializer.SerializeToElement(r.Data, OperationJson.Options);

    private Task<OperationResult> Check(decimal entry, decimal stop, int qty) =>
        new RiskCheckOperation().ExecuteAsync(new RiskCheckArgs { Symbol = "nvda", Entry = entry, Stop = stop, Qty = qty }, Context());

    [Test]
    public async Task Show_ListsTheSixLimits_DefaultsWhenNeverSet()
    {
        var result = await new RiskShowOperation().ExecuteAsync(new NoArgs(), Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var limits = (RiskLimits)result.Data!;
        Assert.That(limits, Is.EqualTo(new RiskLimits(100m, 500m, 0.5m, 10m, 10_000m, 1m, false)));
        Assert.That(result.Human, Does.Contain("Max loss per trade").And.Contain("$100.00").And.Contain("Kill switch"));
    }

    [Test]
    public async Task Set_ChangesOnlyTheNamedLimits_KeepsTheDailyCounter_AndRefusesNonsense()
    {
        var guardian = await services.GetRequiredService<RiskGuardianService>().GetForUserAsync(owner);
        guardian.RecordTradePnL(-30m);

        var result = await new RiskSetOperation().ExecuteAsync(new RiskSetArgs { MaxLossPerTrade = 50m, MaxLossPerDay = 200m }, Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("max loss/trade 100 -> 50").And.Contain("max loss/day 500 -> 200"));
        var after = await services.GetRequiredService<RiskGuardianService>().GetForUserAsync(owner);
        Assert.Multiple(() =>
        {
            Assert.That(after, Is.SameAs(guardian), "the Guardian (and its daily counter) survives a limit change");
            Assert.That(after.GetStatus().MaxLossPerTrade, Is.EqualTo(50m), "the new limits apply on the next entry, not in 2 minutes");
            Assert.That(after.GetStatus().DailyLossSoFar, Is.EqualTo(30m));
            Assert.That(((RiskLimits)Data(result).GetProperty("after").Deserialize<RiskLimits>(OperationJson.Options)!).MinStopLossPercent, Is.EqualTo(0.5m));
        });

        var bad = await new RiskSetOperation().ExecuteAsync(new RiskSetArgs { MaxLossPerDay = 10m }, Context());
        Assert.That(bad.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(bad.Message, Does.Contain("below the per-trade limit"));
        Assert.That((await new RiskSetOperation().ExecuteAsync(new RiskSetArgs(), Context())).Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task Status_ShowsRealizedPnl_CountedLosses_AndRoomLeft()
    {
        var guardian = await services.GetRequiredService<RiskGuardianService>().GetForUserAsync(owner);
        guardian.RecordTradePnL(-120m);
        await using (var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            db.TradeDiary.AddRange(
                new TradeDiaryEntry { OwnerUserId = owner, Symbol = "NVDA", EntryUtc = DateTime.UtcNow.AddMinutes(-30), ExitUtc = DateTime.UtcNow.AddMinutes(-5), RealizedPnL = -120m, Status = TradeDiaryStatus.Closed },
                new TradeDiaryEntry { OwnerUserId = owner, Symbol = "INTC", EntryUtc = DateTime.UtcNow.AddMinutes(-20), ExitUtc = DateTime.UtcNow.AddMinutes(-2), RealizedPnL = 45m, Status = TradeDiaryStatus.Closed },
                new TradeDiaryEntry { OwnerUserId = Guid.NewGuid(), Symbol = "AMD", EntryUtc = DateTime.UtcNow.AddMinutes(-20), ExitUtc = DateTime.UtcNow, RealizedPnL = -999m, Status = TradeDiaryStatus.Closed });
            await db.SaveChangesAsync();
        }

        var result = await new RiskStatusOperation().ExecuteAsync(new NoArgs(), Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var view = (RiskStatusView)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(view.RealizedPnlToday, Is.EqualTo(-75m), "only this owner's trades");
            Assert.That(view.TradesClosedToday, Is.EqualTo(2));
            Assert.That(view.DailyLossCounted, Is.EqualTo(120m));
            Assert.That(view.RemainingDailyRisk, Is.EqualTo(380m));
            Assert.That(view.EntriesAllowed, Is.True);
            Assert.That(result.Human, Does.Contain("Room left today").And.Contain("$380.00").And.Contain("-$75.00"));
        });
    }

    [Test]
    public async Task KillSwitch_OnVetoesEntriesAtOnce_OffNeedsAConfirmationCode()
    {
        Assert.That((await Check(100m, 98m, 10)).Human, Does.Contain("APPROVED"));

        var on = await new RiskKillSwitchOperation().ExecuteAsync(new RiskKillSwitchArgs { State = "on" }, Context());
        Assert.That(on.IsOk, Is.True, on.Message);
        var vetoed = await Check(100m, 98m, 10);
        Assert.That(Data(vetoed).GetProperty("approved").GetBoolean(), Is.False);
        Assert.That(vetoed.Human, Does.Contain("KILL SWITCH ON"));
        Assert.That(((RiskStatusView)(await new RiskStatusOperation().ExecuteAsync(new NoArgs(), Context())).Data!).EntriesAllowed, Is.False);

        var ask = await new RiskKillSwitchOperation().ExecuteAsync(new RiskKillSwitchArgs { State = "off" }, Context());
        Assert.That(ask.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That(await services.GetRequiredService<RiskGuardianService>().IsKillSwitchOnAsync(owner), Is.True, "no code, no change");

        var wrong = await new RiskKillSwitchOperation().ExecuteAsync(new RiskKillSwitchArgs { State = "off" }, Context("ZZZZZZ"));
        Assert.That(wrong.Status, Is.EqualTo(OperationStatus.Blocked));

        var off = await new RiskKillSwitchOperation().ExecuteAsync(new RiskKillSwitchArgs { State = "off" }, Context(ask.Confirmation!.Code));
        Assert.That(off.IsOk, Is.True, off.Message);
        Assert.That(Data(await Check(100m, 98m, 10)).GetProperty("approved").GetBoolean(), Is.True);
    }

    [Test]
    public async Task Check_ExplainsABlock_AndTheLargestSizeThatPasses()
    {
        // $2 stop x 40 shares = $80 expected, $120 worst case > $100 per trade.
        var result = await Check(100m, 98m, 40);

        Assert.That(result.IsOk, Is.True, "a dry run that blocks is still a successful answer");
        var data = Data(result);
        Assert.Multiple(() =>
        {
            Assert.That(data.GetProperty("approved").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("worst_case_loss").GetDecimal(), Is.EqualTo(120m));
            Assert.That(data.GetProperty("adjusted_quantity").GetInt32(), Is.EqualTo(33));
            Assert.That(data.GetProperty("max_quantity").GetInt32(), Is.EqualTo(33));
            Assert.That(result.Human, Does.Contain("BLOCK: Worst-case risk $120.00 exceeds max $100.00"));
        });
        Assert.That((await services.GetRequiredService<RiskGuardianService>().GetForUserAsync(owner)).GetStatus().DailyLossSoFar, Is.EqualTo(0m));
    }
}

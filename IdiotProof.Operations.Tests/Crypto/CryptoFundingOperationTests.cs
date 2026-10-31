using System.Net;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Crypto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Operations.Tests.Crypto;

/// <summary>
/// Crypto funding (IP-T317): reads fail closed per account with Alpaca's message; every withdrawal
/// and whitelist change previews and needs a confirmation code, on paper too, before any POST/DELETE.
/// </summary>
public class CryptoFundingOperationTests
{
    private const string Paper = "paper-api.alpaca.markets";
    private const string Live = "api.alpaca.markets";

    private IDbContextFactory<AppDbContext> db = null!;
    private ConfirmationStore confirmations = null!;

    [SetUp]
    public void SetUp()
    {
        db = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"crypto-funding-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        confirmations = new ConfirmationStore(db, TimeProvider.System);
    }

    private OperationContext Context(CryptoAlpacaHandler handler, string? code = null) =>
        CryptoAlpacaFactory.Context(handler, s => s
            .AddSingleton(confirmations)
            .AddSingleton(new AuditLogRepository(db, NullLogger<AuditLogRepository>.Instance)), code);

    private static int Mutations(CryptoAlpacaHandler h) => h.Requests.Count(r => r.Method != HttpMethod.Get);

    [Test]
    public async Task Wallets_ShowLive_AndPaperFailsClosedWithAlpacasMessage()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v2/wallets", """{"code":40310000,"message":"crypto transfers are not enabled for this account"}""", HttpStatusCode.Forbidden, host: Paper)
            .On("/v2/wallets", """{"asset_id":"a1","address":"bc1qlivedepositaddress","created_at":"2026-10-30T13:00:00Z"}""", host: Live);

        var result = await new CryptoWalletsOperation().ExecuteAsync(new CryptoWalletsArgs { Asset = "btc" }, Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Paper account: not available on this account (403: crypto transfers are not enabled for this account)"));
        Assert.That(result.Human, Does.Contain("bc1qlivedepositaddress").And.Contain("BTC").And.Contain("9:00 AM ET"));
        Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("asset=BTC"));
    }

    [Test]
    public async Task Transfers_And_Whitelist_ListPerAccount()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v2/wallets/transfers", """[{"id":"t-123456789","direction":"OUTGOING","status":"COMPLETE","asset":"BTC","amount":"0.01","usd_value":"700.5","network_fee":"0.0001","fees":"0","to_address":"bc1qdestinationaddress0001","created_at":"2026-10-29T15:00:00Z"}]""")
            .On("/v2/wallets/whitelists", """[{"id":"w1","asset":"BTC","chain":"BTC","address":"bc1qdest","status":"APPROVED","created_at":"2026-10-28T15:00:00Z"}]""");

        var transfers = await new CryptoTransfersOperation().ExecuteAsync(new CryptoTransfersArgs { Mode = AccountScope.Live }, Context(handler));
        var whitelist = await new CryptoWhitelistOperation().ExecuteAsync(new CryptoWhitelistArgs { Mode = AccountScope.Live }, Context(handler));

        Assert.That(transfers.Human, Does.Contain("OUTGOING").And.Contain("COMPLETE").And.Contain("$700.50").And.Contain("0.0001"));
        Assert.That(whitelist.Human, Does.Contain("APPROVED").And.Contain("bc1qdest").And.Contain("w1"));
    }

    [Test]
    public async Task FeeEstimate_ReadsTheFee()
    {
        var handler = new CryptoAlpacaHandler().On("/v2/wallets/fees/estimate", """{"fee":"0.0002"}""");

        var result = await new CryptoFeeEstimateOperation().ExecuteAsync(
            new CryptoFeeEstimateArgs { Asset = "BTC", Amount = 0.01m, To = "bc1qdest" }, Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("estimated network fee 0.0002 BTC").And.Contain("2.000%"));
        Assert.That(handler.Requests.Single().RequestUri!.Query, Does.Contain("to_address=bc1qdest").And.Contain("amount=0.01"));
    }

    [Test]
    public async Task Withdraw_OnPaperStillNeedsACode_ThenPostsExactlyOnce()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v2/wallets/transfers", """{"id":"t-9","status":"PROCESSING"}""", method: "POST");
        var args = new CryptoWithdrawArgs { Asset = "btc", Amount = 0.01m, Address = "bc1qdest" };

        var first = await new CryptoWithdrawOperation().ExecuteAsync(args, Context(handler));
        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That(first.Human, Does.Contain("WITHDRAW 0.01 BTC · Paper"));
        Assert.That(Mutations(handler), Is.Zero, "a preview never moves coins");

        var code = first.Confirmation!.Code;
        var swapped = await new CryptoWithdrawOperation().ExecuteAsync(args with { Address = "bc1qattacker" }, Context(handler, code));
        Assert.That(swapped.Status, Is.EqualTo(OperationStatus.Blocked));
        Assert.That(Mutations(handler), Is.Zero);

        var again = await new CryptoWithdrawOperation().ExecuteAsync(args, Context(handler));
        var done = await new CryptoWithdrawOperation().ExecuteAsync(args, Context(handler, again.Confirmation!.Code));
        Assert.That(done.IsOk, Is.True, done.Message);
        Assert.That(done.Human, Does.Contain("transfer t-9 is PROCESSING"));
        Assert.That(Mutations(handler), Is.EqualTo(1));
        Assert.That(handler.Requests.Single(r => r.Method == HttpMethod.Post).RequestUri!.Host, Is.EqualTo(Paper));
        Assert.That(handler.Bodies.Single(b => b is not null), Is.EqualTo("""{"amount":"0.01","address":"bc1qdest","asset":"BTC"}"""));

        var replay = await new CryptoWithdrawOperation().ExecuteAsync(args, Context(handler, again.Confirmation!.Code));
        Assert.That(replay.Status, Is.EqualTo(OperationStatus.Blocked), "a code confirms once");
    }

    [Test]
    public async Task Withdraw_RefusedByAlpaca_FailsWithItsMessage_AndIsAudited()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v2/wallets/transfers", """{"code":42210000,"message":"address is not whitelisted"}""", HttpStatusCode.UnprocessableEntity, method: "POST");
        var args = new CryptoWithdrawArgs { Asset = "BTC", Amount = 1m, Address = "bc1qnope", Mode = BrokerMode.Live };

        var first = await new CryptoWithdrawOperation().ExecuteAsync(args, Context(handler));
        var done = await new CryptoWithdrawOperation().ExecuteAsync(args, Context(handler, first.Confirmation!.Code));

        Assert.That(done.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(done.Message, Does.Contain("address is not whitelisted").And.Contain("Nothing was moved"));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.That(await ctx.AuditLogs.Select(a => a.Message).ToListAsync(), Has.Some.StartsWith("Rejected: WITHDRAW 1 BTC · Live"));
    }

    [Test]
    public async Task WhitelistAddAndRemove_NeedCodes()
    {
        var handler = new CryptoAlpacaHandler()
            .On("/v2/wallets/whitelists", """{"id":"w9","status":"PENDING"}""", method: "POST")
            .On("/v2/wallets/whitelists/w9", "", HttpStatusCode.NoContent, method: "DELETE");

        var add = await new CryptoWhitelistAddOperation().ExecuteAsync(new CryptoWhitelistAddArgs { Asset = "ETH", Address = "0xabc" }, Context(handler));
        Assert.That(add.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        var added = await new CryptoWhitelistAddOperation().ExecuteAsync(new CryptoWhitelistAddArgs { Asset = "ETH", Address = "0xabc" }, Context(handler, add.Confirmation!.Code));
        Assert.That(added.Human, Does.Contain("Whitelist entry w9 created on Paper: PENDING"));

        var remove = await new CryptoWhitelistRemoveOperation().ExecuteAsync(new CryptoWhitelistRemoveArgs { Id = "w9" }, Context(handler));
        Assert.That(remove.Status, Is.EqualTo(OperationStatus.ConfirmationRequired));
        Assert.That(Mutations(handler), Is.EqualTo(1));
        var removed = await new CryptoWhitelistRemoveOperation().ExecuteAsync(new CryptoWhitelistRemoveArgs { Id = "w9" }, Context(handler, remove.Confirmation!.Code));
        Assert.That(removed.IsOk, Is.True, removed.Message);
        Assert.That(handler.Requests.Last().Method, Is.EqualTo(HttpMethod.Delete));
    }
}

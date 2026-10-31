using System.Text.Json;
using IdiotProof.Core.Data;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Streaming;

/// <summary>orders stream (IP-T325): the Hub's trade_updates listener over a fake websocket; every fill lands in the audit log and the diary.</summary>
public class OrdersStreamOperationTests
{
    private const string Paper = AlpacaStreamSession.PaperTradingStream;

    private const string Authorized = """{"stream":"authorization","data":{"status":"authorized","action":"authenticate"}}""";
    private const string Listening = """{"stream":"listening","data":{"streams":["trade_updates"]}}""";

    private static string Update(string evt, string orderId, string side, string qty, string filledQty, string? price, string? avg, string? execution = null) =>
        JsonSerializer.Serialize(new
        {
            stream = "trade_updates",
            data = new
            {
                @event = evt, execution_id = execution, timestamp = "2026-10-30T13:31:07.123Z", price, qty = evt.Contains("fill") ? qty : null,
                position_qty = "10",
                order = new { id = orderId, client_order_id = "c-" + orderId, symbol = "AAPL", side, qty = "10", filled_qty = filledQty, filled_avg_price = avg, type = "market", status = evt },
            },
        });

    [Test]
    public void Parse_ReadsAlpacasStringNumbers_AndRendersOneLineInEt()
    {
        var u = TradeUpdate.Parse(JsonDocument.Parse(Update("partial_fill", "abcdef1234", "buy", "4", "4", "187.2", "187.2", "x1")).RootElement, "paper")!;

        Assert.That(u.IsFill, Is.True);
        Assert.That((u.Qty, u.Price, u.FilledQty, u.OrderQty), Is.EqualTo(((decimal?)4m, (decimal?)187.2m, (decimal?)4m, (decimal?)10m)));
        Assert.That(u.Line(), Is.EqualTo("9:31:07 AM ET  paper  PARTIAL FILL     BUY 4 AAPL @ $187.20  4/10 filled, avg $187.20  order abcdef12"));
        Assert.That(TradeUpdate.Parse(JsonDocument.Parse(Listening).RootElement, "paper"), Is.Null);
    }

    [Test]
    public async Task Stream_AuthenticatesListens_ShowsEvents_AndRecordsTheFill()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Paper] =
        [
            Authorized, Listening,
            Update("new", "o-1", "buy", "10", "0", null, null),
            Update("fill", "o-1", "buy", "10", "10", "187.25", "187.25", "e-1"),
            Update("fill", "o-1", "buy", "10", "10", "187.25", "187.25", "e-1"), // a repeat of the same execution
        ];

        var result = await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper, Seconds = 1 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("NEW").And.Contain("FILL").And.Contain("BUY 10 AAPL @ $187.25"));
        var sent = kit.Sockets.SentText(Paper).ToList();
        Assert.That(sent[0], Is.EqualTo("""{"action":"auth","key":"key-Paper","secret":"secret"}"""));
        Assert.That(sent[1], Is.EqualTo("""{"action":"listen","data":{"streams":["trade_updates"]}}"""));

        var listener = kit.Services.GetRequiredService<StreamRegistry>().TradeUpdateListeners.Single();
        await listener.DrainAsync();
        await using var db = await kit.Db.CreateDbContextAsync();
        var fills = await db.AuditLogs.Where(a => a.Category == "fill").ToListAsync();
        Assert.That(fills, Has.Count.EqualTo(1), "the repeated execution is written once");
        Assert.That(fills[0].Message, Does.StartWith("Fill: BUY 10 AAPL @ $187.25 (10/10 filled) · paper · order o-1"));
        Assert.That(fills[0].UserId, Is.EqualTo(StreamKit.Owner));
        var row = await db.TradeDiary.SingleAsync();
        Assert.That((row.Symbol, row.EntryPrice, row.Quantity, row.EntryOrderId, row.IsPaper), Is.EqualTo(("AAPL", 187.25m, 10, "o-1", true)));
        Assert.That(row.StrategyId, Is.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task TheListener_IsSharedAndKeptRunning_AcrossCalls()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Paper] = [Authorized, Listening];
        var op = new OrdersStreamOperation();

        await op.ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper, Seconds = 1 }, kit.Context());
        await op.ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper, Seconds = 1 }, kit.Context());

        Assert.That(kit.Sockets.Connections, Has.Count.EqualTo(1), "one websocket per account, however many watchers");
        Assert.That(kit.Services.GetRequiredService<StreamRegistry>().TradeUpdateListeners.Single().Running, Is.True);
        await kit.Services.DisposeAsync();
    }

    [Test]
    public async Task Live_PushesEachEventAsItArrives()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Paper] = [Authorized, Listening, Update("canceled", "o-9", "sell", "5", "0", null, null)];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var sink = new ListSink(cts, stopAt: 2);

        var result = await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper }, kit.Context(sink, cts.Token));

        Assert.That(sink.Lines[0].Text, Does.Contain("Alpaca trade updates for paper"));
        Assert.That(sink.Lines[1].Text, Does.Contain("CANCELED").And.Contain("SELL 10 AAPL"));
        Assert.That(result.Human, Does.EndWith("stopped."));
    }

    [Test]
    public async Task All_SkipsAnAccountWithoutKeys_AndBlocksWhenNoneHasThem()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Paper] = [Authorized, Listening];
        kit.Credentials.Refused.Add(BrokerMode.Live);

        var some = await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Seconds = 1 }, kit.Context());
        Assert.That(some.IsOk, Is.True, some.Message);
        Assert.That(some.Human, Does.Contain("for paper").And.Contain("skipped live: No live Alpaca keys"));

        kit.Credentials.Refused.Add(BrokerMode.Paper);
        var none = await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Live }, kit.Context());
        Assert.That(none.Status, Is.EqualTo(OperationStatus.Blocked));
    }

    [Test]
    public async Task ARefusedStream_EndsTheWatch_WithAlpacasReason()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[Paper] = ["""{"stream":"authorization","data":{"status":"unauthorized","action":"authenticate"}}"""];

        var result = await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper, Seconds = 5 }, kit.Context());

        Assert.That(result.Human, Does.Contain("paper stream ended").And.Contain("unauthorized"));
        Assert.That(kit.Services.GetRequiredService<StreamRegistry>().Health().Single().State, Is.EqualTo("refused"));
    }
}

/// <summary>The fill recorder against the diary rows the gateway and Monitor write (IP-T325 + IP-T93).</summary>
public class FillRecorderTests
{
    private static TradeUpdate Fill(string orderId, string side, decimal filled, decimal avg, string evt = "fill", string? exec = null) =>
        new("paper", evt, new DateTime(2026, 10, 30, 13, 31, 0, DateTimeKind.Utc), orderId, null, "AAPL", side,
            filled, avg, filled, 10m, avg, null, "market", evt, exec ?? Guid.NewGuid().ToString());

    [Test]
    public async Task AFillOfARecordedEntry_PutsTheRealPriceAndSizeOnIt()
    {
        var kit = new StreamKit();
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            db.TradeDiary.Add(new TradeDiaryEntry { OwnerUserId = StreamKit.Owner, Symbol = "AAPL", IsPaper = true, EntryPrice = 187m, Quantity = 10, EntryOrderId = "o-1", EntryUtc = DateTime.UtcNow, Status = TradeDiaryStatus.Open });
            await db.SaveChangesAsync();
        }
        var recorder = kit.Services.GetRequiredService<FillRecorder>();

        Assert.That(await recorder.RecordAsync(StreamKit.Owner, true, Fill("o-1", "buy", 4, 187.10m, "partial_fill")), Is.EqualTo("diary entry updated"));
        Assert.That(await recorder.RecordAsync(StreamKit.Owner, true, Fill("o-1", "buy", 10, 187.20m)), Is.EqualTo("diary entry updated"));

        await using var check = await kit.Db.CreateDbContextAsync();
        var row = await check.TradeDiary.SingleAsync();
        Assert.That((row.EntryPrice, row.Quantity), Is.EqualTo((187.20m, 10)));
        Assert.That(await check.AuditLogs.CountAsync(a => a.Category == "fill"), Is.EqualTo(2));
    }

    [Test]
    public async Task AFillOfARecordedExit_SetsTheExitPrice_AndRecomputesPnl()
    {
        var kit = new StreamKit();
        await using (var db = await kit.Db.CreateDbContextAsync())
        {
            db.TradeDiary.Add(new TradeDiaryEntry
            {
                OwnerUserId = StreamKit.Owner, Symbol = "AAPL", IsPaper = true, EntryPrice = 100m, Quantity = 10, EntryOrderId = "o-1",
                ExitOrderId = "o-2", ExitPrice = 0m, EntryUtc = DateTime.UtcNow, Status = TradeDiaryStatus.Closed,
            });
            await db.SaveChangesAsync();
        }

        var done = await kit.Services.GetRequiredService<FillRecorder>().RecordAsync(StreamKit.Owner, true, Fill("o-2", "sell", 10, 105m));

        Assert.That(done, Is.EqualTo("diary exit updated"));
        await using var check = await kit.Db.CreateDbContextAsync();
        var row = await check.TradeDiary.SingleAsync();
        Assert.That((row.ExitPrice, row.RealizedPnL, row.ReturnPercent), Is.EqualTo(((decimal?)105m, (decimal?)50m, (decimal?)5m)));
    }

    [Test]
    public async Task UnmatchedFills_OpenOnABuy_CloseTheManualRowOnASell_AndNeverInventAShort()
    {
        var kit = new StreamKit();
        var recorder = kit.Services.GetRequiredService<FillRecorder>();

        Assert.That(await recorder.RecordAsync(StreamKit.Owner, true, Fill("x-1", "sell", 5, 50m)), Is.EqualTo("audited (no diary row to close)"));
        Assert.That(await recorder.RecordAsync(StreamKit.Owner, true, Fill("b-1", "buy", 5, 50m)), Is.EqualTo("diary entry opened"));
        Assert.That(await recorder.RecordAsync(StreamKit.Owner, true, Fill("s-1", "sell", 5, 52m)), Is.EqualTo("diary row closed"));
        Assert.That(await recorder.RecordAsync(StreamKit.Owner, true, Fill("p-1", "buy", 2, 50m, "partial_fill")), Is.EqualTo("audited"),
            "a partial fill of an unrecorded order waits for the final fill");

        await using var check = await kit.Db.CreateDbContextAsync();
        var row = await check.TradeDiary.SingleAsync();
        Assert.That((row.Status, row.EntryPrice, row.ExitPrice, row.RealizedPnL), Is.EqualTo((TradeDiaryStatus.Closed, 50m, (decimal?)52m, (decimal?)10m)));
        Assert.That(await check.AuditLogs.CountAsync(a => a.Category == "fill"), Is.EqualTo(4));
    }

    [Test]
    public async Task NonFills_AreIgnored()
    {
        var kit = new StreamKit();
        var u = Fill("o-1", "buy", 0, 0m, "canceled");
        Assert.That(await kit.Services.GetRequiredService<FillRecorder>().RecordAsync(StreamKit.Owner, true, u), Is.Null);
    }
}

using IdiotProof.Operations.Account;
using IdiotProof.Operations.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Tests.Streaming;

/// <summary>market stream status (IP-T206): every websocket the Hub holds, with its health.</summary>
public class MarketStreamStatusOperationTests
{
    [Test]
    public async Task NothingOpen_SaysWhatStartsEachStream()
    {
        var ctx = new OperationContext { Services = new ServiceCollection().BuildServiceProvider() };

        var result = await new MarketStreamStatusOperation().ExecuteAsync(new NoArgs(), ctx);

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Human, Does.Contain("No Alpaca websockets are open").And.Contain("orders stream").And.Contain("market watch"));
    }

    [Test]
    public async Task ARunningTradeUpdatesListener_ShowsLiveWithItsCounts()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[AlpacaStreamSession.PaperTradingStream] =
        [
            """{"stream":"authorization","data":{"status":"authorized","action":"authenticate"}}""",
            """{"stream":"listening","data":{"streams":["trade_updates"]}}""",
        ];
        await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper, Seconds = 1 }, kit.Context());

        var result = await new MarketStreamStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var line = result.Human!.Split('\n').Single(l => l.StartsWith("trade updates · paper"));
        Assert.That(line, Does.Contain("live").And.Contain("0 fill(s) recorded").And.Contain(" ET"));
        Assert.That(result.Human, Does.Contain("1 stream(s), all connected"));
        var row = ((System.Collections.IEnumerable)result.Data!.GetType().GetProperty("Streams")!.GetValue(result.Data)!).Cast<StreamStatusRow>().Single();
        Assert.That((row.Messages, row.Reconnects, row.Healthy), Is.EqualTo((1L, 0, true)), "the listening answer (the auth answer is consumed by the handshake)");
        await kit.Services.DisposeAsync();
    }

    [Test]
    public async Task ARefusedListener_IsFlagged()
    {
        var kit = new StreamKit();
        kit.Sockets.Scripts[AlpacaStreamSession.PaperTradingStream] = ["""{"stream":"authorization","data":{"status":"unauthorized"}}"""];
        await new OrdersStreamOperation().ExecuteAsync(new OrdersStreamArgs { Mode = AccountScope.Paper, Seconds = 1 }, kit.Context());

        var result = await new MarketStreamStatusOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.Human, Does.Contain("! refused").And.Contain("1 of 1 stream(s) need attention"));
    }
}

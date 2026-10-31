using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Positions;

namespace IdiotProof.Operations.Tests.Positions;

/// <summary>
/// positions close (IP-T305): exits sized from the scripted broker
/// position, through the real TransactionGateway, placed on a recording fake broker.
/// </summary>
public class PositionsCloseTests
{
    [Test]
    public async Task Close_SellsTheWholeLongAsAnExit()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("HIHO", 351));

        var result = await new PositionsCloseOperation().ExecuteAsync(new PositionsCloseArgs { Symbol = "hiho" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var placed = kit.Brokers[BrokerMode.Paper].Placed.Single();
        Assert.Multiple(() =>
        {
            Assert.That(placed.Symbol, Is.EqualTo("HIHO"));
            Assert.That(placed.Quantity, Is.EqualTo(351));
            Assert.That(placed.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(placed.Type, Is.EqualTo(OrderType.Market));
            Assert.That(placed.PositionIntent, Is.EqualTo("sell_to_close"));
        });
        Assert.That(kit.Requests.Select(r => r.Method), Is.All.EqualTo(HttpMethod.Get), "the position is read; the exit goes through the gateway");
    }

    [Test]
    public async Task Close_Percentage_CoversHalfAShort_InWholeShares()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("TSLA", -9));

        var result = await new PositionsCloseOperation().ExecuteAsync(new PositionsCloseArgs { Symbol = "TSLA", Percentage = 50 }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var placed = kit.Brokers[BrokerMode.Paper].Placed.Single();
        Assert.That((placed.Side, placed.Quantity, placed.PositionIntent), Is.EqualTo((OrderSide.Buy, 4, "buy_to_close")));
    }

    [Test]
    public async Task Close_RefusesMoreThanHeld_AndIsOkWhenFlat()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Paper] = PositionsKit.Array(PositionsKit.Position("AAPL", 3));

        var tooMany = new PositionsCloseOperation().ExecuteAsync(new PositionsCloseArgs { Symbol = "AAPL", Quantity = 5 }, kit.Context());
        var flat = await new PositionsCloseOperation().ExecuteAsync(new PositionsCloseArgs { Symbol = "MSFT" }, kit.Context());

        Assert.That((await Catch(tooMany)).Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(flat.IsOk && flat.Human!.Contains("holds no MSFT"), Is.True);
        Assert.That(kit.Brokers.Values.SelectMany(b => b.Placed), Is.Empty);
    }

    [Test]
    public async Task Close_Live_IssuesACode_ThenPlacesOnlyOnConfirm()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Live] = PositionsKit.Array(PositionsKit.Position("NVDA", 10));
        var args = new PositionsCloseArgs { Symbol = "NVDA", Mode = BrokerMode.Live };

        var first = await new PositionsCloseOperation().ExecuteAsync(args, kit.Context());

        Assert.That(first.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), first.Message);
        Assert.That(kit.Brokers[BrokerMode.Live].Placed, Is.Empty);

        var second = await new PositionsCloseOperation().ExecuteAsync(args, kit.Context(first.Confirmation!.Code));

        Assert.That(second.IsOk, Is.True, second.Message);
        Assert.That(kit.Brokers[BrokerMode.Live].Placed.Single().Quantity, Is.EqualTo(10));
    }

    [Test]
    public async Task Close_Preview_SendsNothing()
    {
        var kit = new PositionsKit();
        kit.Held[BrokerMode.Live] = PositionsKit.Array(PositionsKit.Position("NVDA", 10));

        var result = await new PositionsCloseOperation().ExecuteAsync(new PositionsCloseArgs { Symbol = "NVDA", Mode = BrokerMode.Live, Preview = true }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("SELL 10 NVDA MKT").And.Contain("Preview only"));
        Assert.That(kit.Brokers[BrokerMode.Live].Placed, Is.Empty);
    }

    private static async Task<OperationResult> Catch(Task<OperationResult> call)
    {
        try { return await call; }
        catch (OperationException ex) { return ex.ToResult(); }
    }
}

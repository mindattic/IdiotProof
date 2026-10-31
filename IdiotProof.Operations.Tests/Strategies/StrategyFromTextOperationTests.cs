using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Strategies;

/// <summary>strategy from-text (IP-T144): LLM (the house fake) → stamp preview → save only on confirm.</summary>
public class StrategyFromTextOperationTests
{
    [Test]
    public async Task FromText_ShowsTheStampFirst_ThenSavesExactlyThatOnConfirm_WithoutCallingTheLlmAgain()
    {
        var kit = new StrategyKit();
        var op = new StrategyFromTextOperation();
        var args = new StrategyFromTextArgs { Description = "Buy AMD when it reclaims the 9 EMA with volume", Mode = BrokerMode.Paper };

        var preview = await op.ExecuteAsync(args, kit.Context());

        Assert.That(preview.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), preview.Message);
        Assert.That(preview.Confirmation!.Preview, Does.Contain("AMD").And.Contain("Nothing saved yet"));
        Assert.That(await kit.AllAsync(), Is.Empty, "nothing is saved before the confirmation");
        Assert.That(kit.LlmCalls, Is.EqualTo(1));

        var saved = await op.ExecuteAsync(args, kit.Context(confirm: preview.Confirmation.Code));

        Assert.That(saved.IsOk, Is.True, saved.Message);
        Assert.That(kit.LlmCalls, Is.EqualTo(1), "confirming replays the stored canon; the LLM isn't asked again");
        var row = (await kit.AllAsync()).Single();
        var def = StrategyJson.Deserialize(row.ScriptJson!);
        Assert.Multiple(() =>
        {
            Assert.That(row.Symbol, Is.EqualTo("AMD"));
            Assert.That(row.BrokerMode, Is.EqualTo("Paper"));
            Assert.That(row.IsActive, Is.False);
            Assert.That(def.Quantity, Is.EqualTo(1));
            Assert.That(preview.Confirmation.Preview, Does.Contain(def.ShortId()), "what was shown is what was saved");
        });

        var again = await op.ExecuteAsync(args, kit.Context(confirm: preview.Confirmation.Code));
        Assert.That(again.Status, Is.EqualTo(OperationStatus.Blocked), "a code works once");
    }

    [Test]
    public async Task FromText_UsesTheTickerOption_AndRefusesLive()
    {
        var kit = new StrategyKit();
        var op = new StrategyFromTextOperation();

        var preview = await op.ExecuteAsync(new StrategyFromTextArgs { Description = "reclaim of the 9 ema", Ticker = "tsla" }, kit.Context());
        Assert.That(preview.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), preview.Message);
        Assert.That(preview.Confirmation!.Preview, Does.Contain("TSLA"));

        Assert.That(async () => await op.ExecuteAsync(new StrategyFromTextArgs { Description = "x", Ticker = "TSLA", Mode = BrokerMode.Live }, kit.Context()),
            Throws.TypeOf<OperationException>());
        var noTicker = await op.ExecuteAsync(new StrategyFromTextArgs { Description = "buy the dip on a hammer" }, kit.Context());
        Assert.That(noTicker.Status, Is.EqualTo(OperationStatus.UsageError));
    }

    [Test]
    public async Task FromText_AnLlmScriptWithoutAStop_IsRefused()
    {
        var kit = new StrategyKit();
        // The fake LLM returns whatever a [[script: ...]] marker in the prose says.
        var result = await new StrategyFromTextOperation().ExecuteAsync(new StrategyFromTextArgs
        {
            Description = "[[script: Stock.Ticker(\"NVDA\").IsAboveVwap().Long().TakeProfit(200).Build()]]", Ticker = "NVDA",
        }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(result.Message, Does.Contain("No stop loss"));
        Assert.That(await kit.AllAsync(), Is.Empty);
    }

    [Test]
    public void GuessTicker_SkipsJargon()
    {
        Assert.That(StrategyFromTextOperation.GuessTicker("Buy when VWAP holds and RSI < 30 on NVDA"), Is.EqualTo("NVDA"));
        Assert.That(StrategyFromTextOperation.GuessTicker("long $amd? no: $AMD on a reclaim"), Is.EqualTo("AMD"));
        Assert.That(StrategyFromTextOperation.GuessTicker("buy the dip"), Is.Null);
    }
}

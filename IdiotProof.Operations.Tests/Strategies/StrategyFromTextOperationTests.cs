using IdiotProof.Core.Services;
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

    // ── The repair loop (IP-T524). The fake LLM plays its [[script: ...]] markers in order, one per call. ──

    private const string Good = "Stock.Ticker(\"NVDA\").IsAboveVwap().Long().StopLoss(170).TakeProfit(190).Build()";

    [Test]
    public async Task FromText_AMisspelledVerb_IsFixedWithoutAskingTheLlmAgain()
    {
        var kit = new StrategyKit();
        var result = await new StrategyFromTextOperation().ExecuteAsync(new StrategyFromTextArgs
        {
            Description = "[[script: Stock.Ticker(\"NVDA\").IsAbveVwap().Long().StopLoss(170).TakeProfit(190).Build()]]", Ticker = "NVDA",
        }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(kit.LlmCalls, Is.EqualTo(1), "a complete deterministic fix costs no model call");
            Assert.That(result.Confirmation!.Preview, Does.Contain("IsAboveVwap").And.Not.Contain("IsAbveVwap()"));
            Assert.That(result.Confirmation.Preview, Does.Contain("fixed without the model").And.Contain("Replace 'IsAbveVwap' with 'IsAboveVwap'"));
            Assert.That(result.ToJson(), Does.Contain("IPS001"));
        });
    }

    [Test]
    public async Task FromText_BadThenGood_TakesOneRepairRound_AndSavesTheGoodOne()
    {
        var kit = new StrategyKit();
        var op = new StrategyFromTextOperation();
        var args = new StrategyFromTextArgs
        {
            // No stop, then the same strategy with one: a guard finding is never fixed without the model.
            Description = "[[script: Stock.Ticker(\"NVDA\").IsAboveVwap().Long().TakeProfit(190).Build()]] [[script: " + Good + "]]", Ticker = "NVDA",
        };

        var preview = await op.ExecuteAsync(args, kit.Context());

        Assert.That(preview.Status, Is.EqualTo(OperationStatus.ConfirmationRequired), preview.Message);
        Assert.Multiple(() =>
        {
            Assert.That(kit.LlmCalls, Is.EqualTo(2));
            Assert.That(preview.Confirmation!.Preview, Does.Contain("1 of 2 repair round(s) used").And.Contain("No stop loss"));
        });

        var saved = await op.ExecuteAsync(args, kit.Context(confirm: preview.Confirmation!.Code));
        Assert.That(saved.IsOk, Is.True, saved.Message);
        Assert.That(StrategyJson.Deserialize((await kit.AllAsync()).Single().ScriptJson!).StopLossPrice, Is.EqualTo(170));
    }

    [Test]
    public async Task FromText_NeverValid_StopsAfterTheAllowedRounds_AndSavesNothing()
    {
        var kit = new StrategyKit();
        var result = await new StrategyFromTextOperation().ExecuteAsync(new StrategyFromTextArgs
        {
            Description = "[[script: Stock.Ticker(\"NVDA\").IsAboveVwap().Long().TakeProfit(200).Build()]]", Ticker = "NVDA",
        }, kit.Context());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(kit.LlmCalls, Is.EqualTo(3), "one call plus the default two repair rounds");
            Assert.That(result.Message, Does.Contain("after 2 repair round(s)").And.Contain("No stop loss"));
            Assert.That(result.Confirmation, Is.Null, "no confirmation code exists for a strategy that never validated");
        });
        Assert.That(await kit.AllAsync(), Is.Empty);
    }

    [Test]
    public async Task FromText_TheRoundsSetting_BoundsTheLoop()
    {
        var kit = new StrategyKit();
        await kit.Settings.SetAsync(GenerationRepairPolicy.RoundsKey, "0");
        var result = await new StrategyFromTextOperation().ExecuteAsync(new StrategyFromTextArgs
        {
            Description = "[[script: Stock.Ticker(\"NVDA\").IsAboveVwap().Long().TakeProfit(190).Build()]] [[script: " + Good + "]]", Ticker = "NVDA",
        }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(kit.LlmCalls, Is.EqualTo(1));
    }

    [Test]
    public void TheValidator_FixesForm_NeverSubstance()
    {
        var typo = GeneratedStrategyCheck.Inspect("Stock.Ticker(\"NVDA\").IsAbveVwap().Long().StopLoss(\"170\").Build()", "NVDA");
        var noStop = GeneratedStrategyCheck.Inspect("Stock.Ticker(\"NVDA\").IsAbveVwap().Long().TakeProfit(190).Build()", "NVDA");
        var noSymbol = GeneratedStrategyCheck.Inspect("IsAboveVwap().Long().StopLoss(170).Build()", "NVDA");

        Assert.Multiple(() =>
        {
            Assert.That(typo.Value, Is.Not.Null, "spelling and a quoted number are form");
            Assert.That(typo.Value!.Definition.StopLossPrice, Is.EqualTo(170));
            Assert.That(typo.Fixes, Has.Count.EqualTo(2));

            Assert.That(noStop.Value, Is.Null, "the typo is fixable, the missing stop is not: no stop is invented");
            Assert.That(noStop.Errors, Has.Some.Contain("IPS001").And.Some.Contain("No stop loss"));
            Assert.That(noStop.Fixes, Has.Some.Contain("With those fixes the script reads"));

            Assert.That(noSymbol.Value, Is.Null, "the --ticker value is never written into a script that names no symbol");
            Assert.That(noSymbol.Errors, Has.Some.Contain("IPS005"));
        });
    }

    [Test]
    public void GuessTicker_SkipsJargon()
    {
        Assert.That(StrategyFromTextOperation.GuessTicker("Buy when VWAP holds and RSI < 30 on NVDA"), Is.EqualTo("NVDA"));
        Assert.That(StrategyFromTextOperation.GuessTicker("long $amd? no: $AMD on a reclaim"), Is.EqualTo("AMD"));
        Assert.That(StrategyFromTextOperation.GuessTicker("buy the dip"), Is.Null);
    }
}

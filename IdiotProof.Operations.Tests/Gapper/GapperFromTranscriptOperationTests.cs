using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper from-transcript (IP-T167): a fake model's reply becomes re-validated candidate cards; nothing is queued.</summary>
public class GapperFromTranscriptOperationTests
{
    [Test]
    public async Task CandidatesAreRevalidated_CardsCarryTheArmCommand_AndNothingIsQueued()
    {
        using var kit = new GapperKit();
        kit.Model.Reply = """
            [
              {"symbol":"abcd","rationale":"Low float runner up 14% premarket.","profile":{"minGapPercent":"12","stopLossPercent":7,"peakGivebackPercent":18}},
              {"symbol":"not a ticker","rationale":"junk"},
              {"symbol":"EFGH","rationale":"bad dials","profile":{"armExitAtEt":"09:29","sellByEt":"09:20"}}
            ]
            """;
        var file = Path.Combine(kit.RunDir, "t.txt");
        Directory.CreateDirectory(kit.RunDir);
        await File.WriteAllTextAsync(file, "ABCD is gapping 14% on news, I like it under 5 bucks with a tight-ish stop.");

        var result = await new GapperFromTranscriptOperation().ExecuteAsync(new GapperFromTranscriptArgs { Transcript = file }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(kit.Model.Requests.Single(), Does.Contain("gapping 14%"), "the file's text went to the model");
            Assert.That(result.Human, Does.Contain("── ABCD ──").And.Contain("idiotproof gapper arm ABCD --gap 12").And.Contain("--stop 7").And.Contain("--giveback 18"));
            Assert.That(result.Human, Does.Not.Contain("── EFGH ──"), "a candidate whose dials fail validation is dropped");
            Assert.That(result.Human, Does.Contain("invalid symbol").And.Contain("Skipped EFGH"));
            Assert.That(result.Human, Does.Contain("none queued"));
        });
        Assert.That(await kit.StrategiesAsync(), Is.Empty);
    }

    [Test]
    public async Task NoUsableCandidates_IsAFailure()
    {
        using var kit = new GapperKit();
        kit.Model.Reply = "[]";
        var result = await new GapperFromTranscriptOperation().ExecuteAsync(new GapperFromTranscriptArgs { Transcript = "nothing about stocks here" }, kit.Context());

        Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
    }
}

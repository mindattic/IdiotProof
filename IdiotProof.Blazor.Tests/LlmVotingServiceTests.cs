using IdiotProof.Blazor.Services;
using IdiotProof.Models;

namespace IdiotProof.Blazor.Tests;

[TestFixture]
public sealed class LlmVotingServiceTests
{
    // Weight totals for the three hard-coded personas: Risk Manager(2) + Momentum Trader(2) + Technical Analyst(3) = 7

    // ── Weighted consensus logic ──

    [Test]
    public void CalculateWeightedConsensus_AllApprove_ConsensusApprove()
    {
        var service = new LlmVotingService(null!, null!);
        var result = ResultWith(
            ("Risk Manager",       VoteDecision.Approve, 80m),
            ("Momentum Trader",    VoteDecision.Approve, 85m),
            ("Technical Analyst",  VoteDecision.Approve, 90m));

        service.CalculateWeightedConsensus(result, 0.66m);

        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Approve));
    }

    [Test]
    public void CalculateWeightedConsensus_AllReject_ConsensusReject()
    {
        var service = new LlmVotingService(null!, null!);
        var result = ResultWith(
            ("Risk Manager",       VoteDecision.Reject, 60m),
            ("Momentum Trader",    VoteDecision.Reject, 55m),
            ("Technical Analyst",  VoteDecision.Reject, 70m));

        service.CalculateWeightedConsensus(result, 0.66m);

        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Reject));
    }

    [Test]
    public void CalculateWeightedConsensus_BelowThreshold_ConsensusAbstain()
    {
        // Risk Manager(2) Approve, Momentum Trader(2) Abstain, Technical Analyst(3) Reject
        // approve=2/7≈0.29, reject=3/7≈0.43 — both below 0.66
        var service = new LlmVotingService(null!, null!);
        var result = ResultWith(
            ("Risk Manager",       VoteDecision.Approve,  80m),
            ("Momentum Trader",    VoteDecision.Abstain,  50m),
            ("Technical Analyst",  VoteDecision.Reject,   65m));

        service.CalculateWeightedConsensus(result, 0.66m);

        Assert.That(result.Consensus, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public void CalculateWeightedConsensus_ComputesWeightedConfidence()
    {
        // Risk Manager(2)×80 + Momentum Trader(2)×60 + Technical Analyst(3)×70 = 160+120+210 = 490 / 7 = 70
        var service = new LlmVotingService(null!, null!);
        var result = ResultWith(
            ("Risk Manager",       VoteDecision.Approve, 80m),
            ("Momentum Trader",    VoteDecision.Approve, 60m),
            ("Technical Analyst",  VoteDecision.Approve, 70m));

        service.CalculateWeightedConsensus(result, 0.66m);

        Assert.That(result.ConsensusConfidence, Is.EqualTo(70m).Within(0.01m));
    }

    [Test]
    public void CalculateWeightedConsensus_BuildsReasoningFromVotes()
    {
        var service = new LlmVotingService(null!, null!);
        var result = ResultWith(
            ("Risk Manager",       VoteDecision.Approve, 80m, "Clean stop."),
            ("Technical Analyst",  VoteDecision.Approve, 75m, "Strong trend."));

        service.CalculateWeightedConsensus(result, 0.66m);

        Assert.That(result.ConsensusReasoning, Does.Contain("Risk Manager: Clean stop."));
        Assert.That(result.ConsensusReasoning, Does.Contain("Technical Analyst: Strong trend."));
    }

    // ── JSON vote parsing ──

    [Test]
    public void ParseVoteJson_ValidApproveJson_ParsesDecision()
    {
        var vote = LlmVotingService.ParseVoteJson(
            """{"decision":"Approve","confidence":85,"reasoning":"Strong setup"}""");

        Assert.That(vote, Is.Not.Null);
        Assert.That(vote!.Decision, Is.EqualTo(VoteDecision.Approve));
        Assert.That(vote.Confidence, Is.EqualTo(85m));
        Assert.That(vote.Reasoning, Is.EqualTo("Strong setup"));
    }

    [Test]
    public void ParseVoteJson_LowercaseReject_ParsesDecision()
    {
        var vote = LlmVotingService.ParseVoteJson(
            """{"decision":"reject","confidence":30,"reasoning":"Weak signal"}""");

        Assert.That(vote, Is.Not.Null);
        Assert.That(vote!.Decision, Is.EqualTo(VoteDecision.Reject));
    }

    [Test]
    public void ParseVoteJson_ProseWrapped_ExtractsJsonBlock()
    {
        const string response =
            """Here is my analysis: {"decision":"Abstain","confidence":50,"reasoning":"Mixed"} That's all.""";

        var vote = LlmVotingService.ParseVoteJson(response);

        Assert.That(vote, Is.Not.Null);
        Assert.That(vote!.Decision, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public void ParseVoteJson_ParsesDirection_Long()
    {
        var vote = LlmVotingService.ParseVoteJson(
            """{"decision":"Approve","confidence":90,"reasoning":"Trend up","direction":"Long"}""");

        Assert.That(vote, Is.Not.Null);
        Assert.That(vote!.SuggestedDirection, Is.EqualTo(TradeDirection.Long));
    }

    [Test]
    public void ParseVoteJson_InvalidInput_ReturnsNull()
    {
        var vote = LlmVotingService.ParseVoteJson("not json at all");
        Assert.That(vote, Is.Null);
    }

    [Test]
    public void ParseVoteJson_MissingDecisionKey_FailsClosedToAbstain()
    {
        // IP-A11: a response without a "decision" key used to leave the enum
        // at its zero value — Approve — silently counting a malformed vote as
        // an approval on a money-movement path. Must fail closed to Abstain.
        var vote = LlmVotingService.ParseVoteJson(
            """{"confidence":85,"reasoning":"forgot the decision key"}""");

        Assert.That(vote, Is.Not.Null);
        Assert.That(vote!.Decision, Is.EqualTo(VoteDecision.Abstain));
    }

    [Test]
    public void ParseVoteJson_CapitalizedPropertyNames_StillParse()
    {
        // JsonElement.TryGetProperty is case-sensitive; an LLM emitting
        // "Decision" used to be treated as if the key were absent.
        var vote = LlmVotingService.ParseVoteJson(
            """{"Decision":"Reject","Confidence":40,"Reasoning":"caps"}""");

        Assert.That(vote, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(vote!.Decision, Is.EqualTo(VoteDecision.Reject));
            Assert.That(vote.Confidence, Is.EqualTo(40m));
        });
    }

    // ── Helpers ──

    static LlmVotingResult ResultWith(params (string name, VoteDecision decision, decimal confidence)[] votes)
        => new() { Votes = votes.Select(v => new LlmVote { PersonaName = v.name, Decision = v.decision, Confidence = v.confidence }).ToList() };

    static LlmVotingResult ResultWith(params (string name, VoteDecision decision, decimal confidence, string reasoning)[] votes)
        => new() { Votes = votes.Select(v => new LlmVote { PersonaName = v.name, Decision = v.decision, Confidence = v.confidence, Reasoning = v.reasoning }).ToList() };
}

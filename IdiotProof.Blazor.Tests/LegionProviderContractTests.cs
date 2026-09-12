using IdiotProof.Engine.Settings;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Canary against Legion provider-id / model-catalog drift. Legion 20 renamed the Anthropic
/// provider "claude" → "claude-api"; Legion 25 renamed it back to "claude" and removed the
/// broken "claude-team" OAuth path entirely. IdiotProof call sites (StrategyScriptGenerator,
/// GapperInterpreter, LlmVotingService) and legion.json all pin "claude" and default models by
/// string, so these asserts fail loudly on the next rename instead of the Describe tab dying
/// silently at runtime.
/// </summary>
[TestFixture]
public sealed class LegionProviderContractTests
{
    [Test]
    public void ClaudeProviderId_IsRegisteredInLegionsCatalog()
        => Assert.That(LlmProviderCatalog.IsSupported("claude"), Is.True,
            "every IdiotProof CallAsync site pins providerId: \"claude\"");

    [Test]
    public void DefaultLlmVoterModel_IsKnownToTheClaudeProvider()
        => Assert.That(LlmProviderCatalog.IsKnownModel("claude", new AppSettings().LlmVoterModel), Is.True,
            "AppSettings.LlmVoterModel default must exist in Legion's claude model catalog");
}

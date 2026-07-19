using IdiotProof.Engine.Settings;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Canary against Legion provider-id / model-catalog drift. Legion 20 renamed
/// the Anthropic provider "claude" → "claude-api"; IdiotProof call sites
/// (StrategyScriptGenerator, GapperInterpreter, LlmVotingService) and
/// legion.json all pin "claude-api" and default models by string, so these
/// asserts fail loudly on the next rename instead of the Describe tab dying
/// silently at runtime.
/// </summary>
[TestFixture]
public sealed class LegionProviderContractTests
{
    [Test]
    public void ClaudeApiProviderId_IsRegisteredInLegionsCatalog()
        => Assert.That(LlmProviderCatalog.IsSupported("claude-api"), Is.True,
            "every IdiotProof CallAsync site pins providerId: \"claude-api\"");

    [Test]
    public void DefaultLlmVoterModel_IsKnownToTheClaudeApiProvider()
        => Assert.That(LlmProviderCatalog.IsKnownModel("claude-api", new AppSettings().LlmVoterModel), Is.True,
            "AppSettings.LlmVoterModel default must exist in Legion's claude-api model catalog");
}

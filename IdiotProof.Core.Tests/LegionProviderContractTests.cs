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
    public void EveryDeclaredCredential_IsAProviderIdiotProofActuallyResolves()
    {
        var declared = typeof(IdiotProofSettings)
            .GetCustomAttributes(typeof(MindAttic.Vault.Settings.Manifest.UsesCredentialAttribute), false)
            .Cast<MindAttic.Vault.Settings.Manifest.UsesCredentialAttribute>()
            .Select(a => a.ProviderId)
            .ToList();
        Assert.That(declared, Is.Not.Empty);
        Assert.That(declared, Has.None.EqualTo("alpaca"),
            "Alpaca pairs come from the Brokers group (alpaca-paper/alpaca-live), not a single 'alpaca' key");
        foreach (var id in declared)
            Assert.That(LlmProviderCatalog.IsSupported(id), Is.True,
                $"[UsesCredential(\"{id}\")] must be an LLM provider IdiotProof resolves through Legion");
    }

    [Test]
    public void DefaultLlmVoterModel_IsKnownToTheClaudeProvider()
        => Assert.That(LlmProviderCatalog.IsKnownModel("claude", new AppSettings().LlmVoterModel), Is.True,
            "AppSettings.LlmVoterModel default must exist in Legion's claude model catalog");
}

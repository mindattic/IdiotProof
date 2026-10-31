using IdiotProof.Core.Data;
using IdiotProof.Core.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The command ledger records every operation's arguments; SecretRedactor makes sure keys,
/// secrets, passwords and confirm codes never land there in the clear, while ordinary trading
/// arguments (symbols, quantities, prices) stay readable.
/// </summary>
[TestFixture]
public sealed class SecretRedactorTests
{
    [Test]
    public void RedactJson_MasksSecretNamedValues_AtAnyDepth()
    {
        var json = """{"symbol":"NVDA","qty":5,"api_key":"PKABCDEFGHIJKLMNOP","nested":{"alpacaSecret":"abc","password":"hunter2"},"confirm_code":"K7Q2ZP"}""";

        var redacted = SecretRedactor.RedactJson(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(redacted, Does.Contain("\"symbol\":\"NVDA\""));
            Assert.That(redacted, Does.Contain("\"qty\":5"));
            Assert.That(redacted, Does.Not.Contain("PKABCDEFGHIJKLMNOP"));
            Assert.That(redacted, Does.Not.Contain("hunter2"));
            Assert.That(redacted, Does.Not.Contain("\"abc\""));
            Assert.That(redacted, Does.Not.Contain("K7Q2ZP"));
        });
    }

    [Test]
    public void RedactJson_MasksSecretLookingValues_UnderInnocentNames()
    {
        var json = """{"text":"sk-ant-api03-abcdefghijklmnopqrstuvwxyz","note":"buy the dip"}""";

        var redacted = SecretRedactor.RedactJson(json)!;

        Assert.That(redacted, Does.Not.Contain("abcdefghijklmnop"));
        Assert.That(redacted, Does.Contain("sk-a...***"), "a short prefix survives so keys can be told apart");
        Assert.That(redacted, Does.Contain("buy the dip"));
    }

    [Test]
    public void RedactArgv_MasksFlagValues_BothForms()
    {
        var argv = new[] { "keys", "set", "alpaca-paper", "--api-key", "PKZZZZZZZZZZZZZZZZ", "--secret=s3cr3t", "--symbol", "AAPL" };

        var redacted = SecretRedactor.RedactArgv(argv);

        Assert.That(redacted, Is.EqualTo(new[] { "keys", "set", "alpaca-paper", "--api-key", SecretRedactor.Mask, "--secret=" + SecretRedactor.Mask, "--symbol", "AAPL" }));
    }

    [Test]
    public void IsSecretName_KeepsTheKeysNounReadable()
    {
        Assert.That(SecretRedactor.IsSecretName("keys"), Is.False);
        Assert.That(SecretRedactor.IsSecretName("ticker"), Is.False);
        Assert.That(SecretRedactor.IsSecretName("--api-key"), Is.True);
    }

    [Test]
    public void RedactJson_AccountNumbers_KeepOnlyTheLastFour()
    {
        var json = """{"account_number":"PA3PG9SP46WU","nested":{"accountNumber":252673877,"bank_account_number":"000123456789"},"symbol":"AAPL"}""";

        var redacted = SecretRedactor.RedactJson(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(redacted, Does.Not.Contain("PA3PG9SP46WU"));
            Assert.That(redacted, Does.Contain("***46WU"));
            Assert.That(redacted, Does.Not.Contain("252673877"));
            Assert.That(redacted, Does.Contain("***3877"));
            Assert.That(redacted, Does.Not.Contain("000123456789"));
            Assert.That(redacted, Does.Contain("\"symbol\":\"AAPL\""));
        });
    }

    [Test]
    public void RedactArgv_AccountNumberFlags_KeepOnlyTheLastFour()
    {
        var redacted = SecretRedactor.RedactArgv(["ach", "add", "--account-number", "000123456789", "--account_number=PA3PG9SP46WU"]);

        Assert.That(redacted, Is.EqualTo(new[] { "ach", "add", "--account-number", "***6789", "--account_number=***46WU" }));
    }

    [Test]
    public void RedactJson_InvalidJson_IsTreatedAsOneValue()
    {
        Assert.That(SecretRedactor.RedactJson("not json at all"), Is.EqualTo("not json at all"));
        Assert.That(SecretRedactor.RedactJson("sk-proj-abcdefghijklmnopqrstuvwxyz"), Does.StartWith("sk-p..."));
    }

    [Test]
    public async Task CommandLedgerRepository_RedactsOnTheWayIn_AndListsNewestFirst()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase($"ledger-{Guid.NewGuid():N}"))
            .BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
        var repo = new CommandLedgerRepository(factory, NullLogger<CommandLedgerRepository>.Instance);
        var owner = Guid.NewGuid();

        await repo.AppendAsync(new CommandLedgerEntry
        {
            OwnerUserId = owner, Operation = "keys set", Caller = "cli", Status = "Ok",
            ArgsJson = """{"secret":"abcdefghijklmnopqrstuvwxyz0123456789"}""",
            TimestampUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        await repo.AppendAsync(new CommandLedgerEntry { OwnerUserId = owner, Operation = "strategy list", Caller = "mcp", Status = "Ok" });

        var rows = await repo.ListAsync(owner);

        Assert.That(rows.Select(r => r.Operation), Is.EqualTo(new[] { "strategy list", "keys set" }));
        Assert.That(rows[1].ArgsJson, Does.Not.Contain("abcdefghijklmnop"));
    }
}

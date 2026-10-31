using System.Text.RegularExpressions;

namespace IdiotProof.Cli.Tests;

/// <summary>
/// The repository and its GitHub Pages site are public (IP-T329). No key-shaped string or account
/// identifier may ever land in README.md or docs/: examples use placeholders, real values live in
/// MindAttic.Vault (IP-T330).
/// </summary>
public class PublicDocsPrivacyTests
{
    private static readonly (string Name, Regex Pattern)[] Forbidden =
    [
        ("Alpaca key id", new Regex(@"\b(PK|AK|CK)[A-Z0-9]{18}\b")),
        ("Alpaca secret", new Regex(@"(secret|SECRET_KEY)[""':=\s]+[A-Za-z0-9/+]{40}\b")),
        ("Anthropic key", new Regex(@"sk-ant-[A-Za-z0-9_-]{20,}")),
        ("OpenAI key", new Regex(@"\bsk-(proj-)?[A-Za-z0-9]{32,}")),
        ("Alpaca account number", new Regex(@"account_number[""':=\s]+[""']?[A-Z0-9]{8,}")),
        ("Alpaca paper account id", new Regex(@"\bPA[0-9A-Z]{10}\b")),
        ("Hub key header value", new Regex(@"X-IdiotProof-Key[""':\s]+[A-Za-z0-9_-]{24,}")),
    ];

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) return dir.FullName;
        throw new InvalidOperationException("IdiotProof.slnx not found above the test directory.");
    }

    private static IEnumerable<string> PublicDocs()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "README.md");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.*", SearchOption.AllDirectories))
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                yield return file;
    }

    [Test]
    public void ReadmeAndDocsCarryNoKeysOrAccountIdentifiers()
    {
        var root = RepoRoot();
        var hits = new List<string>();
        foreach (var file in PublicDocs())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                foreach (var (name, pattern) in Forbidden)
                    if (pattern.IsMatch(lines[i]))
                        hits.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {name}");
        }

        Assert.That(hits, Is.Empty, "Public docs must not contain secrets or account identifiers:\n" + string.Join("\n", hits));
    }

    [TestCase("APCA key PKABCDEFGHIJKLMNOPQR in a doc", "Alpaca key id")]
    [TestCase("\"account_number\": \"PA3XYZ12345\"", "Alpaca account number")]
    [TestCase("claude key sk-ant-api03-abcdefghijklmnopqrstuv", "Anthropic key")]
    public void TheScannerCatchesWhatItShould(string line, string expected) =>
        Assert.That(Forbidden.Where(f => f.Pattern.IsMatch(line)).Select(f => f.Name), Does.Contain(expected));
}

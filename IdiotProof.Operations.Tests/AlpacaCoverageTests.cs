using System.Text.RegularExpressions;
using IdiotProof.Operations.Docs;

namespace IdiotProof.Operations.Tests;

/// <summary>
/// IP-T328: docs/ALPACA_COVERAGE.md inventories every Alpaca Trading and Market Data endpoint.
/// Every inventoried endpoint must name an operation that exists, and every Alpaca path the
/// operations call must be inventoried, so the doc and the code cannot drift apart.
/// </summary>
public partial class AlpacaCoverageTests
{
    private sealed record Row(string Method, string Endpoint, IReadOnlyList<string> Operations, int Line);

    private static string Repo() => Docs.KnowledgeIndexBuilderTests.RealRepo();

    private static IReadOnlyList<Row> Inventory()
    {
        var lines = File.ReadAllLines(Path.Combine(Repo(), "docs", "ALPACA_COVERAGE.md"));
        var start = Array.FindIndex(lines, l => l.Contains("<!-- inventory:start -->"));
        var end = Array.FindIndex(lines, l => l.Contains("<!-- inventory:end -->"));
        Assert.That(start, Is.GreaterThanOrEqualTo(0).And.LessThan(end), "inventory markers");
        var rows = new List<Row>();
        for (var i = start; i < end; i++)
        {
            var m = RowPattern().Match(lines[i]);
            if (!m.Success) continue;
            var ops = OpPattern().Matches(m.Groups[3].Value).Select(o => o.Groups[1].Value).ToList();
            rows.Add(new Row(m.Groups[1].Value, m.Groups[2].Value.Trim(), ops, i + 1));
        }
        return rows;
    }

    [Test]
    public void Inventory_IsSubstantial_AndHasNoDuplicates()
    {
        var rows = Inventory();
        Assert.That(rows, Has.Count.GreaterThan(80));
        Assert.That(rows.Select(r => $"{r.Method} {r.Endpoint}"), Is.Unique);
    }

    [Test]
    public void EveryInventoriedEndpoint_HasAnOperationThatExists()
    {
        var catalog = OperationCatalog.FromAssemblies(typeof(OperationCatalog).Assembly);
        var problems = new List<string>();
        foreach (var row in Inventory())
        {
            if (row.Operations.Count == 0) problems.Add($"line {row.Line}: {row.Method} {row.Endpoint} has no operation");
            foreach (var op in row.Operations.Where(op => catalog.Find(op) is null))
                problems.Add($"line {row.Line}: {row.Method} {row.Endpoint} names '{op}', which is not an operation");
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [Test]
    public void EveryAlpacaPathTheOperationsCall_IsInventoried()
    {
        var endpoints = Inventory().Select(r => Normalize(r.Endpoint)).ToList();
        var missing = new List<string>();
        var sourceRoot = Path.Combine(Repo(), "IdiotProof.Operations");
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in PathLiteral().Matches(File.ReadAllText(file)))
            {
                var prefix = m.Groups[1].Value;
                if (!endpoints.Any(e => e.StartsWith(prefix, StringComparison.Ordinal)))
                    missing.Add($"{Path.GetRelativePath(Repo(), file)}: {prefix}");
            }
        }
        Assert.That(missing.Distinct(), Is.Empty, "Alpaca paths called but not in docs/ALPACA_COVERAGE.md:\n" + string.Join("\n", missing.Distinct()));
    }

    /// <summary>The inventory's placeholders that the code always fills the same way.</summary>
    private static string Normalize(string endpoint) =>
        endpoint.Replace("{loc}", "us", StringComparison.Ordinal).Replace("{market_type}", "stocks", StringComparison.Ordinal);

    [Test]
    public void CoverageDoc_IsIndexedForDocsSearch() =>
        Assert.That(KnowledgeIndexBuilder.DocFiles, Does.Contain("docs/ALPACA_COVERAGE.md"));

    [GeneratedRegex(@"^\|\s*(GET|POST|PUT|PATCH|DELETE)\s*\|\s*([^|]+)\|([^|]*)\|")]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"`([a-z][a-z -]*)`")]
    private static partial Regex OpPattern();

    /// <summary>A string literal starting with an Alpaca API version; group 1 stops at an interpolation, query or quote.</summary>
    [GeneratedRegex(@"""(/v(?:1beta\d|1|2)/[^""{?]*)")]
    private static partial Regex PathLiteral();
}

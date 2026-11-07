using System.Text.Json;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>One configuration tried in the exploration: what was tried and how it scored per fold.</summary>
public sealed record ExploreTrial
{
    public int Id { get; init; }
    public DateTime Utc { get; init; }
    public string Track { get; init; } = "";
    public string Config { get; init; } = "";
    public string Metric { get; init; } = "";
    public IReadOnlyList<double?> FoldValues { get; init; } = [];
    public double? Pooled { get; init; }
    public int Rows { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// The exploration ledger (<c>runs/explore/ledger.jsonl</c>): one line per configuration tried,
/// append-only, so the final deflated Sharpe and multiple-testing corrections know how many tries
/// the search made. Trial ids are sequential across every run.
/// </summary>
public sealed class ExploreLedger(string file)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly object gate = new();

    public string File => file;

    public IReadOnlyList<ExploreTrial> ReadAll() =>
        System.IO.File.Exists(file)
            ? System.IO.File.ReadAllLines(file).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<ExploreTrial>(l, Json)!).ToList()
            : [];

    public int Count => ReadAll().Count;

    /// <summary>Append trials, numbering them after the last id on file; returns them with their ids.</summary>
    public IReadOnlyList<ExploreTrial> Append(IEnumerable<ExploreTrial> trials, DateTime utc)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            int next = ReadAll().Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
            var numbered = trials.Select(t => t with { Id = next++, Utc = utc }).ToList();
            System.IO.File.AppendAllLines(file, numbered.Select(t => JsonSerializer.Serialize(t, Json)));
            return numbered;
        }
    }
}

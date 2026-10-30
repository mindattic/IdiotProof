using System.Text;

namespace IdiotProof.Operations.Rendering;

/// <summary>
/// An aligned label/value block for one record (<c>strategy show</c>, <c>account summary</c>):
/// <code>
/// Ticker:  AAPL
/// Mode:    paper
/// Armed:   2026-10-30 9:31 AM ET
/// </code>
/// Multi-line values continue under the value column. Null values are skipped unless a
/// placeholder is given.
/// </summary>
public sealed class KeyValueBlock
{
    private readonly List<(string Key, string Value)> pairs = [];

    /// <summary>Dim labels when color is on.</summary>
    public Ansi Ansi { get; set; }

    public KeyValueBlock Add(string key, string? value, string? whenNull = null)
    {
        if (value is null && whenNull is null) return this;
        pairs.Add((key, value ?? whenNull!));
        return this;
    }

    public int Count => pairs.Count;

    public override string ToString() => Render();

    public string Render()
    {
        if (pairs.Count == 0) return "";
        var width = pairs.Max(p => p.Key.Length) + 1;
        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            var lines = value.Replace("\r\n", "\n").Split('\n');
            sb.Append(Ansi.Dim((key + ":").PadRight(width))).Append("  ").AppendLine(lines[0].TrimEnd());
            foreach (var more in lines.Skip(1)) sb.Append(new string(' ', width + 2)).AppendLine(more.TrimEnd());
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }
}

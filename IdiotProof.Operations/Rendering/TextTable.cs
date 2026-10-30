using System.Text;

namespace IdiotProof.Operations.Rendering;

public enum Align
{
    Left,
    Right,
}

/// <summary>
/// A plain-text table: columns size to their widest cell, numbers right-align, and when a
/// <see cref="MaxWidth"/> is set the widest columns give up width first, truncating with "…".
/// Color is applied after layout (so escape codes never skew widths) and only when enabled.
/// </summary>
public sealed class TextTable
{
    private const string Gap = "  ";
    private readonly List<ColumnSpec> columns = [];
    private readonly List<string[]> rows = [];
    private readonly List<Func<string, string>?[]> styles = [];

    /// <summary>Total width budget (usually the terminal width); null = unbounded.</summary>
    public int? MaxWidth { get; set; }

    /// <summary>Bold header row when color is on.</summary>
    public Ansi Ansi { get; set; }

    public TextTable Column(string header, Align align = Align.Left, int? maxWidth = null)
    {
        if (rows.Count > 0) throw new InvalidOperationException("Add columns before rows.");
        columns.Add(new ColumnSpec(header, align, maxWidth));
        return this;
    }

    public TextTable Row(params string?[] cells)
    {
        if (cells.Length != columns.Count)
            throw new ArgumentException($"Row has {cells.Length} cells; table has {columns.Count} columns.", nameof(cells));
        rows.Add([.. cells.Select(c => Clean(c ?? ""))]);
        styles.Add(new Func<string, string>?[columns.Count]);
        return this;
    }

    /// <summary>Styles one cell of the last added row (e.g. <c>ansi.Signed(pnl, ...)</c>), applied after padding.</summary>
    public TextTable Style(int column, Func<string, string> style)
    {
        styles[^1][column] = style;
        return this;
    }

    public int RowCount => rows.Count;

    public override string ToString() => Render();

    public string Render()
    {
        if (columns.Count == 0) return "";
        var widths = Widths();
        var sb = new StringBuilder();
        AppendLine(sb, [.. columns.Select(c => c.Header)], widths, null, header: true);
        sb.AppendLine(string.Join(Gap, widths.Select(w => new string('-', w))).TrimEnd());
        for (var r = 0; r < rows.Count; r++) AppendLine(sb, rows[r], widths, styles[r], header: false);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private int[] Widths()
    {
        var widths = new int[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            var natural = Math.Max(columns[c].Header.Length, rows.Count == 0 ? 0 : rows.Max(r => r[c].Length));
            widths[c] = columns[c].MaxWidth is { } cap ? Math.Min(natural, Math.Max(1, cap)) : natural;
        }

        if (MaxWidth is { } budget)
        {
            // Shave one character at a time off the currently widest column until the table fits
            // (never below 3, so a truncated cell still shows a letter or two and the ellipsis).
            var total = widths.Sum() + Gap.Length * (columns.Count - 1);
            while (total > budget)
            {
                var widest = Array.IndexOf(widths, widths.Max());
                if (widths[widest] <= 3) break;
                widths[widest]--;
                total--;
            }
        }
        return widths;
    }

    private void AppendLine(StringBuilder sb, string[] cells, int[] widths, Func<string, string>?[]? cellStyles, bool header)
    {
        var parts = new string[cells.Length];
        for (var c = 0; c < cells.Length; c++)
        {
            var text = Truncate(cells[c], widths[c]);
            var padded = columns[c].Align == Align.Right ? text.PadLeft(widths[c]) : text.PadRight(widths[c]);
            parts[c] = header ? Ansi.Bold(padded) : cellStyles?[c] is { } style ? style(padded) : padded;
        }
        sb.AppendLine(string.Join(Gap, parts).TrimEnd());
    }

    /// <summary>Cuts to <paramref name="width"/> with a trailing "…" when the text is longer.</summary>
    public static string Truncate(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…"[..width] : text[..(width - 1)] + "…";

    /// <summary>Cells are single-line: newlines and tabs become spaces.</summary>
    private static string Clean(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    private sealed record ColumnSpec(string Header, Align Align, int? MaxWidth);
}

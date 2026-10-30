using System.Text;

namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// Line-oriented canvas for a stamp: every box is exactly
/// <see cref="Width"/> columns, every other line is right-trimmed, so the
/// whole card is a fixed 72-column block with no trailing spaces.
/// </summary>
internal sealed class StampWriter(StampGlyphs g)
{
    public const int Width = 72;

    /// <summary>Usable text columns inside a box: "│  " + text + "  │".</summary>
    public const int Inner = Width - 6;

    private readonly List<string> lines = [];

    public StampGlyphs G { get; } = g;

    public void Raw(string line) => lines.Add(line.TrimEnd());

    public void Blank() => lines.Add("");

    /// <summary>Box top edge with a title tab on the left and a dim subtitle on the right.</summary>
    public void Top(string title, string? subtitle = null, bool heavy = false)
    {
        char h = heavy ? G.HH : G.H, l = heavy ? G.TTL : G.TL, r = heavy ? G.TTR : G.TR;
        var left = $"{l}{h}{G.TitleOpen}{title}{G.TitleClose}";
        var right = string.IsNullOrEmpty(subtitle) ? $"{h}{r}" : $" {subtitle} {h}{h}{r}";
        lines.Add(left + new string(h, Math.Max(1, Width - left.Length - right.Length)) + right);
    }

    /// <summary>A plain banner edge (no title) — top, divider or bottom.</summary>
    public void Edge(char left, char fill, char right) =>
        lines.Add(left + new string(fill, Width - 2) + right);

    public void Bottom(bool heavy = false) =>
        Edge(heavy ? G.TBL : G.BL, heavy ? G.HH : G.H, heavy ? G.TBR : G.BR);

    /// <summary>A divider inside a box: ├──────┤.</summary>
    public void Divider() => Edge(G.TeeL, G.H, G.TeeR);

    /// <summary>One boxed row; text longer than the box wraps with a hanging indent.</summary>
    public void Row(string text, int hang = 2, bool heavy = false)
    {
        var v = heavy ? G.VV : G.V;
        foreach (var piece in Wrap(text, Inner, hang))
            lines.Add($"{v}  {piece.PadRight(Inner)}  {v}");
    }

    /// <summary>Two-column row: left text, right text flush to the right edge.</summary>
    public void RowLR(string left, string right, bool heavy = false)
    {
        var gap = Inner - left.Length - right.Length;
        if (gap < 2) { Row(left, heavy: heavy); Row(right, heavy: heavy); return; }
        Row(left + new string(' ', gap) + right, heavy: heavy);
    }

    /// <summary>The flow arrow between two phase boxes.</summary>
    public void Connector(string? label = null)
    {
        var pad = new string(' ', Width / 2 - 1);
        lines.Add(label is null ? pad + G.Down : $"{pad}{G.Down}   {label}");
        lines.Add(pad + G.Arrow);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l.TrimEnd()).Append('\n');
        return sb.ToString();
    }

    /// <summary>Word-wraps to <paramref name="width"/>; continuation lines are indented by <paramref name="hang"/>.</summary>
    public static IEnumerable<string> Wrap(string text, int width, int hang = 2)
    {
        // A line that fits is kept verbatim — column alignment inside it matters.
        if (text.Length <= width) { yield return text; yield break; }

        var indent = new string(' ', hang);
        var lead = new string(' ', text.Length - text.TrimStart().Length);
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder(lead);
        bool first = true, empty = true;

        foreach (var raw in words)
        {
            var word = raw;
            while (true)
            {
                var need = line.Length + (empty ? 0 : 1) + word.Length;
                if (need <= width)
                {
                    if (!empty) line.Append(' ');
                    line.Append(word);
                    empty = false;
                    break;
                }
                if (empty)
                {
                    // A single word wider than the box: hard-break it.
                    var room = width - line.Length;
                    line.Append(word[..room]);
                    word = word[room..];
                }
                yield return line.ToString();
                line.Clear().Append(lead).Append(indent);
                first = false;
                empty = true;
            }
        }
        if (!empty || first) yield return line.ToString();
    }
}

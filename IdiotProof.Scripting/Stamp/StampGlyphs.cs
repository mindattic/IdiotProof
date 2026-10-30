using System.Text;

namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// The character set a stamp is drawn with. <see cref="Ascii"/> (the default)
/// is plain <c>+-|</c> so the stamp survives any terminal, email or ticket
/// system; <see cref="Unicode"/> uses box-drawing characters for a crisper
/// card on modern terminals. Every glyph is exactly one column wide.
/// </summary>
internal sealed record StampGlyphs(
    char H, char V, char TL, char TR, char BL, char BR,     // light box
    char HH, char VV, char TTL, char TTR, char TBL, char TBR, // heavy (banner) box
    char TeeL, char TeeR,                                   // ├ ┤ on a box edge
    string TitleOpen, string TitleClose,                    // around a box title
    char Down, char Arrow,                                  // flow connector stem + head
    char Bullet, string Sep,                                // list bullet, inline separator
    char BranchMid, char BranchLast, char BranchPipe, char BranchArm,
    char Dot,                                               // leader dots
    char Up, char Dn, char Entry,                           // ladder axis
    bool IsUnicode)
{
    public static readonly StampGlyphs Ascii = new(
        '-', '|', '+', '+', '+', '+',
        '=', '|', '+', '+', '+', '+',
        '+', '+',
        "[ ", " ]",
        '|', 'v',
        '*', " . ",
        '+', '\'', '|', '-',
        '.',
        '^', 'v', '=',
        IsUnicode: false);

    public static readonly StampGlyphs Unicode = new(
        '─', '│', '┌', '┐', '└', '┘',
        '═', '║', '╔', '╗', '╚', '╝',
        '├', '┤',
        "┤ ", " ├",
        '│', '▼',
        '•', " · ",
        '├', '└', '│', '─',
        '·',
        '▲', '▼', '═',
        IsUnicode: true);

    /// <summary>
    /// Free text (names) is printed as-is in Unicode mode; in ASCII mode common
    /// typography folds to ASCII and anything else becomes '?', so the ASCII
    /// stamp is guaranteed 7-bit clean. Control characters are always dropped.
    /// </summary>
    public string Clean(string? s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s ?? "")
        {
            if (char.IsControl(ch)) continue;
            if (IsUnicode || ch < 128) { sb.Append(ch); continue; }
            sb.Append(ch switch
            {
                '—' or '–' or '−' => '-',
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '·' or '•' => '.',
                _ => '?',
            });
        }
        return sb.ToString();
    }
}

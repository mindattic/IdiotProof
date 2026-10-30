namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// OpenSSH-style "drunken bishop" randomart (the picture `ssh-keygen -lv`
/// prints for a host key) over the strategy's SHA-256. A bishop starts in the
/// centre of a 17x9 board and makes four diagonal moves per hash byte, two
/// bits each, least-significant pair first; every square counts its visits
/// and the count picks a glyph. Humans compare pictures far better than hex,
/// so two stamps of the same strategy look identical at a glance and any
/// change to the canon produces a visibly different picture.
/// </summary>
internal static class StampFingerprint
{
    public const int FieldWidth = 17;
    public const int FieldHeight = 9;

    // Same ramp as OpenSSH's sshkey.c: more visits → denser glyph.
    private const string Ramp = " .o+=*BOX@%&#/^";

    /// <summary>The framed art: 11 lines of 19 columns.</summary>
    public static List<string> Render(ReadOnlySpan<byte> hash, string title, StampGlyphs g)
    {
        var field = new int[FieldWidth, FieldHeight];
        int x = FieldWidth / 2, y = FieldHeight / 2;
        int startX = x, startY = y;

        foreach (var b in hash)
        {
            int input = b;
            for (int step = 0; step < 4; step++)
            {
                x += (input & 1) != 0 ? 1 : -1;
                y += (input & 2) != 0 ? 1 : -1;
                x = Math.Clamp(x, 0, FieldWidth - 1);
                y = Math.Clamp(y, 0, FieldHeight - 1);
                if (field[x, y] < Ramp.Length - 1) field[x, y]++;
                input >>= 2;
            }
        }

        var lines = new List<string> { Frame(g.TL, g.TR, g.H, $"[{title}]") };
        for (int row = 0; row < FieldHeight; row++)
        {
            var chars = new char[FieldWidth];
            for (int col = 0; col < FieldWidth; col++)
                chars[col] = col == startX && row == startY ? 'S'
                           : col == x && row == y ? 'E'
                           : Ramp[field[col, row]];
            lines.Add($"{g.V}{new string(chars)}{g.V}");
        }
        lines.Add(Frame(g.BL, g.BR, g.H, "[SHA256]"));
        return lines;
    }

    // "+---[title]----+": the label is centred on the 17-column edge, extra
    // fill going to the right exactly like OpenSSH.
    private static string Frame(char left, char right, char fill, string label)
    {
        var room = Math.Max(0, FieldWidth - label.Length);
        var l = room / 2;
        return left + new string(fill, l) + label + new string(fill, room - l) + right;
    }
}

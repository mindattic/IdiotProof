using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// The machine half of a stamp: the canonical JSON, raw-deflated, base64url
/// encoded and wrapped at 64 columns between PGP-style armor lines, followed
/// by a CRC-32 of the compressed bytes:
/// <code>
/// -----BEGIN IDIOTPROOF STRATEGY-----
/// 7VZNb9swDP0rgs9JYDufzW3Y1mHAhh3W2zAEik0nwmTJkOS0QdD_PlJxEtfp...
/// CRC: 1a2b3c4d
/// -----END IDIOTPROOF STRATEGY-----
/// </code>
/// Decoding is fail closed (IP-LAW-1): any doubt → <see cref="StrategyStampException"/>.
/// The QR code carries a denser sibling, <c>IPS1:</c> + base45(crc32 ‖ deflate),
/// because base45 rides QR's alphanumeric mode (5.5 bits/char vs 8).
/// </summary>
internal static class StampArmor
{
    public const string Begin = "-----BEGIN IDIOTPROOF STRATEGY-----";
    public const string End = "-----END IDIOTPROOF STRATEGY-----";
    public const string CrcPrefix = "CRC:";
    public const string CompactPrefix = "IPS1:";
    public const int LineWidth = 64;

    public static List<string> Encode(byte[] canonical)
    {
        var packed = StampCodec.Deflate(canonical);
        var body = Base64Url.EncodeToString(packed);
        var lines = new List<string> { Begin };
        for (int i = 0; i < body.Length; i += LineWidth)
            lines.Add(body.Substring(i, Math.Min(LineWidth, body.Length - i)));
        lines.Add($"{CrcPrefix} {StampCodec.Crc32(packed):x8}");
        lines.Add(End);
        return lines;
    }

    /// <summary>The QR-sized form: <c>IPS1:</c> + base45(big-endian CRC-32 of the deflate ‖ deflate of the pruned canon).</summary>
    public static string EncodeCompact(byte[] canonical)
    {
        // Prefer the pruned spelling (much smaller after deflate), but only when
        // it provably reads back to the identical canon.
        var canon = Encoding.UTF8.GetString(canonical);
        var pruned = StampCodec.PrunedJson(canon);
        var source = StampCodec.CanonicalJson(StrategyJson.Deserialize(pruned)) == canon
            ? Encoding.UTF8.GetBytes(pruned)
            : canonical;

        var packed = StampCodec.Deflate(source);
        var framed = new byte[4 + packed.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed, StampCodec.Crc32(packed));
        packed.CopyTo(framed, 4);
        return CompactPrefix + StampCodec.Base45(framed);
    }

    /// <summary>
    /// Finds the armor anywhere in <paramref name="text"/> (chat quoting,
    /// indentation, CRLF, surrounding prose are all fine) and returns the
    /// canonical JSON it carries, CRC-verified. Falls back to a bare
    /// <c>IPS1:</c> line (a scanned QR) when no armor is present.
    /// </summary>
    public static string DecodeToJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new StrategyStampException("Nothing to decode: the text is empty.");

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int begin = Array.FindIndex(lines, l => l.Contains(Begin, StringComparison.Ordinal));
        if (begin < 0)
        {
            var compact = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(CompactPrefix, StringComparison.Ordinal));
            if (compact is not null) return DecodeCompact(compact);
            throw new StrategyStampException($"No strategy stamp found: missing the '{Begin}' line.");
        }

        // Whatever precedes BEGIN on its line ("> " e-mail quoting, "// " in a
        // code comment) is treated as a per-line prefix and stripped below.
        var quote = lines[begin][..lines[begin].IndexOf(Begin, StringComparison.Ordinal)].Trim();

        var body = new StringBuilder();
        string? crcText = null;
        bool ended = false;
        for (int i = begin + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (quote.Length > 0 && line.StartsWith(quote, StringComparison.Ordinal))
                line = line[quote.Length..].Trim();
            if (line.Contains(End, StringComparison.Ordinal)) { ended = true; break; }
            if (line.StartsWith(CrcPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (crcText is not null) throw new StrategyStampException("Strategy stamp has more than one CRC line.");
                crcText = line[CrcPrefix.Length..].Trim();
                continue;
            }
            if (crcText is not null && line.Length > 0)
                throw new StrategyStampException("Strategy stamp has payload text after its CRC line.");
            // Whitespace anywhere inside the body is tolerated (re-wrapped pastes).
            foreach (var ch in line)
                if (!char.IsWhiteSpace(ch)) body.Append(ch);
        }

        if (!ended) throw new StrategyStampException($"Strategy stamp is truncated: missing the '{End}' line.");
        if (crcText is null) throw new StrategyStampException("Strategy stamp has no CRC line.");
        if (body.Length == 0) throw new StrategyStampException("Strategy stamp payload is empty.");
        if (!uint.TryParse(crcText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expected))
            throw new StrategyStampException($"Strategy stamp CRC '{crcText}' is not 8 hex digits.");

        byte[] packed;
        try { packed = Base64Url.DecodeFromChars(body.ToString()); }
        catch (FormatException) { throw new StrategyStampException("Strategy stamp payload is not valid base64url."); }

        return Unpack(packed, expected);
    }

    private static string DecodeCompact(string line)
    {
        byte[] framed;
        try { framed = StampCodec.FromBase45(line[CompactPrefix.Length..]); }
        catch (FormatException ex) { throw new StrategyStampException($"QR payload is not valid base45: {ex.Message}"); }
        if (framed.Length < 5) throw new StrategyStampException("QR payload is too short.");
        return Unpack(framed[4..], BinaryPrimitives.ReadUInt32BigEndian(framed));
    }

    private static string Unpack(byte[] packed, uint expectedCrc)
    {
        var actual = StampCodec.Crc32(packed);
        if (actual != expectedCrc)
            throw new StrategyStampException(
                $"Strategy stamp CRC mismatch (stamp says {expectedCrc:x8}, payload is {actual:x8}): the text was altered or damaged.");

        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(StampCodec.Inflate(packed)); }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
        {
            throw new StrategyStampException($"Strategy stamp payload does not inflate to UTF-8 JSON: {ex.Message}");
        }
    }
}

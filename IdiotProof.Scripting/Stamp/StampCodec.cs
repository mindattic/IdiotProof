using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace IdiotProof.Scripting.Stamp;

/// <summary>
/// The byte-level plumbing behind <see cref="StrategyStamp"/>: the canonical
/// byte form of a strategy, its SHA-256 identity, and the small encoders the
/// stamp needs (RFC 4648 base32, base45, CRC-32, raw deflate). Pure and
/// dependency-free — every function is deterministic for a given input.
/// </summary>
internal static class StampCodec
{
    // ── Canon ───────────────────────────────────────────────────────────

    /// <summary>
    /// The stamp's canonical bytes: <see cref="StrategyJson.Serialize"/>
    /// (IP-LAW-8) re-emitted as COMPACT JSON. Two reasons not to hash the
    /// Serialize string as-is:
    ///   • it is indented with <see cref="Environment.NewLine"/>, so the same
    ///     strategy would hash differently on Windows (CRLF) and Linux (LF);
    ///   • compact JSON deflates smaller, which matters for the QR form.
    /// Re-parsing preserves property order and number text exactly, so the
    /// compact form is still a lossless StrategyJson document.
    ///
    /// Attribution is already out of canon: Serialize never writes the
    /// definition's random <see cref="StrategyDefinition.Id"/> (nor owner,
    /// row id, timestamps or BrokerMode — those live on the database row, not
    /// the model), so the same strategy always stamps identically and no Id
    /// zeroing is needed. A test pins this.
    /// </summary>
    public static string CanonicalJson(StrategyDefinition def) =>
        JsonNode.Parse(StrategyJson.Serialize(def))!.ToJsonString();

    /// <summary>
    /// A smaller, equivalent spelling of the canon for the QR code: every
    /// property whose value is null, false or an empty array is dropped.
    /// StrategyJson reads an absent property exactly like those defaults, so
    /// this deserializes to the same definition — and <see cref="StampArmor"/>
    /// proves that per strategy before using it, falling back to the full
    /// canon if it ever does not.
    /// </summary>
    public static string PrunedJson(string canonicalJson)
    {
        var root = JsonNode.Parse(canonicalJson)!;
        Prune(root);
        return root.ToJsonString();
    }

    private static void Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    Prune(value);
                    var drop = value is null
                        || value is JsonArray { Count: 0 }
                        || value is JsonValue v && v.TryGetValue<bool>(out var b) && !b;
                    if (drop) obj.Remove(key);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Prune(item);
                break;
        }
    }

    public static byte[] CanonicalBytes(StrategyDefinition def) =>
        Encoding.UTF8.GetBytes(CanonicalJson(def));

    public static byte[] Hash(StrategyDefinition def) => SHA256.HashData(CanonicalBytes(def));

    // ── Base32 (RFC 4648, upper-case, no padding) ───────────────────────

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    // ── Base45 (RFC 9285) — the QR alphanumeric-mode friendly encoding ──

    private const string Base45Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    public static string Base45(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length + 1) / 2 * 3);
        int i = 0;
        for (; i + 1 < data.Length; i += 2)
        {
            int n = data[i] * 256 + data[i + 1];
            sb.Append(Base45Alphabet[n % 45]).Append(Base45Alphabet[n / 45 % 45]).Append(Base45Alphabet[n / 2025]);
        }
        if (i < data.Length)
            sb.Append(Base45Alphabet[data[i] % 45]).Append(Base45Alphabet[data[i] / 45]);
        return sb.ToString();
    }

    public static byte[] FromBase45(string text)
    {
        var output = new List<byte>(text.Length / 3 * 2 + 1);
        int Digit(char c)
        {
            var d = Base45Alphabet.IndexOf(c);
            return d >= 0 ? d : throw new FormatException($"'{c}' is not a base45 character.");
        }
        int i = 0;
        for (; i + 2 < text.Length; i += 3)
        {
            int n = Digit(text[i]) + Digit(text[i + 1]) * 45 + Digit(text[i + 2]) * 2025;
            if (n > 0xFFFF) throw new FormatException("base45 triplet out of range.");
            output.Add((byte)(n >> 8));
            output.Add((byte)(n & 0xFF));
        }
        int rest = text.Length - i;
        if (rest == 2)
        {
            int n = Digit(text[i]) + Digit(text[i + 1]) * 45;
            if (n > 0xFF) throw new FormatException("base45 pair out of range.");
            output.Add((byte)n);
        }
        else if (rest == 1)
            throw new FormatException("base45 text has a dangling character.");
        return output.ToArray();
    }

    // ── CRC-32 (IEEE 802.3, the zip/PNG polynomial) ─────────────────────
    // System.IO.Hashing is a separate NuGet package, not part of the net10
    // shared framework; the table version is a dozen lines, so it lives here.

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    // ── Raw deflate ─────────────────────────────────────────────────────

    public static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(data);
        return ms.ToArray();
    }

    /// <summary>Inflates at most <paramref name="maxBytes"/> — a pasted stamp is untrusted input (no zip bombs).</summary>
    public static byte[] Inflate(byte[] data, int maxBytes = 1 << 20)
    {
        using var input = new MemoryStream(data);
        using var z = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = z.Read(buffer)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException($"Payload inflates past {maxBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

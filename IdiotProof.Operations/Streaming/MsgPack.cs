using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IdiotProof.Operations.Streaming;

/// <summary>
/// The little MessagePack the Alpaca options stream needs (it speaks msgpack only): encode a
/// control message (maps, strings, string arrays, numbers, bools) and decode a frame to JSON so the
/// rest of the streaming code reads every feed the same way. Timestamps (ext -1) become ISO-8601
/// UTC strings, binary becomes base64.
/// </summary>
public static class MsgPack
{
    /// <summary>A frame as JSON text.</summary>
    public static string ToJson(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            var pos = 0;
            Read(data, ref pos, w);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Encodes a control message: dictionaries, anonymous objects, strings, sequences, numbers, bools and null.</summary>
    public static byte[] Encode(object? value)
    {
        using var ms = new MemoryStream();
        Write(ms, value);
        return ms.ToArray();
    }

    private static void Read(ReadOnlySpan<byte> d, ref int p, Utf8JsonWriter w)
    {
        var b = d[p++];
        switch (b)
        {
            case <= 0x7f: w.WriteNumberValue(b); return;
            case >= 0xe0: w.WriteNumberValue((sbyte)b); return;
            case >= 0x80 and <= 0x8f: ReadMap(d, ref p, w, b & 0x0f); return;
            case >= 0x90 and <= 0x9f: ReadArray(d, ref p, w, b & 0x0f); return;
            case >= 0xa0 and <= 0xbf: w.WriteStringValue(Str(d, ref p, b & 0x1f)); return;
            case 0xc0: w.WriteNullValue(); return;
            case 0xc2: w.WriteBooleanValue(false); return;
            case 0xc3: w.WriteBooleanValue(true); return;
            case 0xc4: w.WriteBase64StringValue(Bytes(d, ref p, d[p++])); return;
            case 0xc5: { var n = BinaryPrimitives.ReadUInt16BigEndian(d[p..]); p += 2; w.WriteBase64StringValue(Bytes(d, ref p, n)); return; }
            case 0xc6: { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; w.WriteBase64StringValue(Bytes(d, ref p, n)); return; }
            case 0xc7: { int n = d[p++]; Ext(d, ref p, w, n); return; }
            case 0xc8: { var n = BinaryPrimitives.ReadUInt16BigEndian(d[p..]); p += 2; Ext(d, ref p, w, n); return; }
            case 0xc9: { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; Ext(d, ref p, w, n); return; }
            case 0xca: w.WriteNumberValue(BinaryPrimitives.ReadSingleBigEndian(d[p..])); p += 4; return;
            case 0xcb: w.WriteNumberValue(BinaryPrimitives.ReadDoubleBigEndian(d[p..])); p += 8; return;
            case 0xcc: w.WriteNumberValue(d[p]); p += 1; return;
            case 0xcd: w.WriteNumberValue(BinaryPrimitives.ReadUInt16BigEndian(d[p..])); p += 2; return;
            case 0xce: w.WriteNumberValue(BinaryPrimitives.ReadUInt32BigEndian(d[p..])); p += 4; return;
            case 0xcf: w.WriteNumberValue(BinaryPrimitives.ReadUInt64BigEndian(d[p..])); p += 8; return;
            case 0xd0: w.WriteNumberValue((sbyte)d[p]); p += 1; return;
            case 0xd1: w.WriteNumberValue(BinaryPrimitives.ReadInt16BigEndian(d[p..])); p += 2; return;
            case 0xd2: w.WriteNumberValue(BinaryPrimitives.ReadInt32BigEndian(d[p..])); p += 4; return;
            case 0xd3: w.WriteNumberValue(BinaryPrimitives.ReadInt64BigEndian(d[p..])); p += 8; return;
            case 0xd4: Ext(d, ref p, w, 1); return;
            case 0xd5: Ext(d, ref p, w, 2); return;
            case 0xd6: Ext(d, ref p, w, 4); return;
            case 0xd7: Ext(d, ref p, w, 8); return;
            case 0xd8: Ext(d, ref p, w, 16); return;
            case 0xd9: { int n = d[p++]; w.WriteStringValue(Str(d, ref p, n)); return; }
            case 0xda: { var n = BinaryPrimitives.ReadUInt16BigEndian(d[p..]); p += 2; w.WriteStringValue(Str(d, ref p, n)); return; }
            case 0xdb: { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; w.WriteStringValue(Str(d, ref p, n)); return; }
            case 0xdc: { var n = BinaryPrimitives.ReadUInt16BigEndian(d[p..]); p += 2; ReadArray(d, ref p, w, n); return; }
            case 0xdd: { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; ReadArray(d, ref p, w, n); return; }
            case 0xde: { var n = BinaryPrimitives.ReadUInt16BigEndian(d[p..]); p += 2; ReadMap(d, ref p, w, n); return; }
            case 0xdf: { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(d[p..]); p += 4; ReadMap(d, ref p, w, n); return; }
            default: throw new FormatException($"Unsupported MessagePack byte 0x{b:x2}.");
        }
    }

    private static void ReadArray(ReadOnlySpan<byte> d, ref int p, Utf8JsonWriter w, int n)
    {
        w.WriteStartArray();
        for (var i = 0; i < n; i++) Read(d, ref p, w);
        w.WriteEndArray();
    }

    private static void ReadMap(ReadOnlySpan<byte> d, ref int p, Utf8JsonWriter w, int n)
    {
        w.WriteStartObject();
        for (var i = 0; i < n; i++)
        {
            w.WritePropertyName(Key(d, ref p));
            Read(d, ref p, w);
        }
        w.WriteEndObject();
    }

    /// <summary>A map key as a property name (Alpaca's are strings; anything else is rendered as JSON text).</summary>
    private static string Key(ReadOnlySpan<byte> d, ref int p)
    {
        var b = d[p];
        if (b is >= 0xa0 and <= 0xbf) { p++; return Str(d, ref p, b & 0x1f); }
        if (b == 0xd9) { p++; int n = d[p++]; return Str(d, ref p, n); }
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) Read(d, ref p, w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Str(ReadOnlySpan<byte> d, ref int p, int n)
    {
        var s = Encoding.UTF8.GetString(d.Slice(p, n));
        p += n;
        return s;
    }

    private static ReadOnlySpan<byte> Bytes(ReadOnlySpan<byte> d, ref int p, int n)
    {
        var s = d.Slice(p, n);
        p += n;
        return s;
    }

    private static void Ext(ReadOnlySpan<byte> d, ref int p, Utf8JsonWriter w, int n)
    {
        var type = (sbyte)d[p++];
        var body = Bytes(d, ref p, n);
        if (type != -1) { w.WriteBase64StringValue(body); return; }

        // The timestamp extension: 32-bit seconds, 64-bit (30-bit nanos + 34-bit seconds), or 96-bit.
        long seconds; long nanos;
        switch (n)
        {
            case 4: seconds = BinaryPrimitives.ReadUInt32BigEndian(body); nanos = 0; break;
            case 8:
                var v = BinaryPrimitives.ReadUInt64BigEndian(body);
                nanos = (long)(v >> 34);
                seconds = (long)(v & 0x3_FFFF_FFFFUL);
                break;
            case 12: nanos = BinaryPrimitives.ReadUInt32BigEndian(body); seconds = BinaryPrimitives.ReadInt64BigEndian(body[4..]); break;
            default: w.WriteBase64StringValue(body); return;
        }
        var at = DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(nanos / 100);
        w.WriteStringValue(at.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
    }

    private static void Write(Stream s, object? value)
    {
        switch (value)
        {
            case null: s.WriteByte(0xc0); return;
            case bool b: s.WriteByte(b ? (byte)0xc3 : (byte)0xc2); return;
            case string str: WriteString(s, str); return;
            case int or long or short or byte:
                WriteInt(s, Convert.ToInt64(value, CultureInfo.InvariantCulture)); return;
            case double or float or decimal:
                Span<byte> f = stackalloc byte[9];
                f[0] = 0xcb;
                BinaryPrimitives.WriteDoubleBigEndian(f[1..], Convert.ToDouble(value, CultureInfo.InvariantCulture));
                s.Write(f); return;
            case IDictionary dict:
                WriteHeader(s, dict.Count, 0x80, 0xde, 0xdf);
                foreach (DictionaryEntry e in dict) { WriteString(s, Convert.ToString(e.Key, CultureInfo.InvariantCulture)!); Write(s, e.Value); }
                return;
            case IEnumerable seq:
                var items = seq.Cast<object?>().ToList();
                WriteHeader(s, items.Count, 0x90, 0xdc, 0xdd);
                foreach (var item in items) Write(s, item);
                return;
            default:
                // An anonymous object: its public properties, by name.
                var props = value.GetType().GetProperties().Where(pi => pi.CanRead && pi.GetIndexParameters().Length == 0).ToList();
                WriteHeader(s, props.Count, 0x80, 0xde, 0xdf);
                foreach (var pi in props) { WriteString(s, pi.Name); Write(s, pi.GetValue(value)); }
                return;
        }
    }

    private static void WriteHeader(Stream s, int count, byte fix, byte len16, byte len32)
    {
        if (count < 16) { s.WriteByte((byte)(fix | count)); return; }
        Span<byte> h = stackalloc byte[5];
        if (count <= ushort.MaxValue) { h[0] = len16; BinaryPrimitives.WriteUInt16BigEndian(h[1..], (ushort)count); s.Write(h[..3]); }
        else { h[0] = len32; BinaryPrimitives.WriteUInt32BigEndian(h[1..], (uint)count); s.Write(h); }
    }

    private static void WriteString(Stream s, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        if (bytes.Length < 32) s.WriteByte((byte)(0xa0 | bytes.Length));
        else if (bytes.Length <= byte.MaxValue) { s.WriteByte(0xd9); s.WriteByte((byte)bytes.Length); }
        else
        {
            Span<byte> h = stackalloc byte[3];
            h[0] = 0xda;
            BinaryPrimitives.WriteUInt16BigEndian(h[1..], (ushort)bytes.Length);
            s.Write(h);
        }
        s.Write(bytes);
    }

    private static void WriteInt(Stream s, long v)
    {
        if (v is >= 0 and <= 0x7f) { s.WriteByte((byte)v); return; }
        if (v is < 0 and >= -32) { s.WriteByte((byte)(sbyte)v); return; }
        Span<byte> b = stackalloc byte[9];
        b[0] = 0xd3;
        BinaryPrimitives.WriteInt64BigEndian(b[1..], v);
        s.Write(b);
    }
}

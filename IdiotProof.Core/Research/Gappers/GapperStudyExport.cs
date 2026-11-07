using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One exported CSV: its row count and the SHA-256 of its rows (header excluded), as <c>manifest.json</c> lists them.</summary>
public sealed record ExportedFile(string Name, long Rows, string Sha256, long Bytes);

/// <summary>
/// Streams rows to a CSV while hashing them, so a multi-hundred-megabyte snapshot export never sits
/// in memory and the manifest's hash covers exactly the rows a reader will see.
/// </summary>
public static class GapperStudyExport
{
    public const string ManifestFile = "manifest.json";

    public static async Task<ExportedFile> WriteCsvAsync(string path, IReadOnlyList<string> header, IAsyncEnumerable<IReadOnlyList<object?>> rows, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(",", header.Select(Quote)));
        long count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            var line = string.Join(",", row.Select(Cell));
            await writer.WriteLineAsync(line);
            hash.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
            count++;
        }
        await writer.FlushAsync(ct);
        return new ExportedFile(Path.GetFileName(path), count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), stream.Length);
    }

    public static string Cell(object? value) => value switch
    {
        null => "",
        bool b => b ? "1" : "0",
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeOnly t => t.ToString("HH:mm"),
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        double x => double.IsFinite(x) ? x.ToString("R", CultureInfo.InvariantCulture) : "",
        float f => float.IsFinite(f) ? f.ToString("R", CultureInfo.InvariantCulture) : "",
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        string s => Quote(s),
        _ => Quote(value.ToString() ?? ""),
    };

    public static string Quote(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static void WriteManifest(string dir, IReadOnlyList<ExportedFile> files, IDictionary<string, object?> extra)
    {
        var doc = new JsonObject();
        foreach (var (k, v) in extra) doc[k] = v is null ? null : JsonSerializer.SerializeToNode(v);
        doc["files"] = new JsonArray(files.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["rows"] = f.Rows, ["sha256"] = f.Sha256, ["bytes"] = f.Bytes }).ToArray());
        File.WriteAllText(Path.Combine(dir, ManifestFile), doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

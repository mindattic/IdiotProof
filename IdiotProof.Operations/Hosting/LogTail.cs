using System.Text;

namespace IdiotProof.Operations.Hosting;

/// <summary>
/// Reads the last lines of a log that another process (the Hub's own window mirror) is still
/// appending to, without loading the whole file: it reads backwards from the end in blocks until
/// it has enough lines. Shared-read, so the writer is never blocked. Backs <c>GET /api/logs</c> (IP-T78).
/// </summary>
public static class LogTail
{
    /// <summary>The most a single request may ask for.</summary>
    public const int MaxLines = 5000;

    /// <summary>The last <paramref name="lines"/> lines (oldest first); empty when the file does not exist.</summary>
    public static IReadOnlyList<string> ReadLastLines(string path, int lines)
    {
        lines = Math.Clamp(lines, 1, MaxLines);
        if (!File.Exists(path)) return [];

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const int block = 64 * 1024;
        var position = stream.Length;
        var collected = new List<byte[]>();
        var newlines = 0;
        var buffer = new byte[block];

        // Walk back until we've seen one more newline than lines requested (the trailing one).
        while (position > 0 && newlines <= lines)
        {
            var size = (int)Math.Min(block, position);
            position -= size;
            stream.Position = position;
            var read = 0;
            while (read < size)
            {
                var n = stream.Read(buffer, read, size - read);
                if (n == 0) break;
                read += n;
            }
            var chunk = buffer[..read];
            collected.Insert(0, chunk);
            newlines += chunk.Count(b => b == (byte)'\n');
        }

        var text = Encoding.UTF8.GetString(collected.SelectMany(c => c).ToArray());
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var all = text.Replace("\r\n", "\n").Split('\n');
        var trimmed = all.Length > 0 && all[^1].Length == 0 ? all[..^1] : all;
        return trimmed.Length <= lines ? trimmed : trimmed[^lines..];
    }
}

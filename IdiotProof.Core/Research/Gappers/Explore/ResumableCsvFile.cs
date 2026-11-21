using System.Text;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// A CSV that a long pull appends to and later runs resume from (IP-T618). One writer at a time holds
/// <c>{file}.lock</c> (FileShare.None, deleted on close), so a second pull over the same file refuses instead of
/// appending the same rows; appends and reads share the file both ways, so a report reading it mid-pull neither
/// blocks the writer nor is blocked; a sharing violation from an outside process is retried briefly.
/// </summary>
internal static class ResumableCsvFile
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static FileStream? TryAcquireWriter(string file)
    {
        try
        {
            return new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return null;
        }
    }

    public static IEnumerable<string> ReadLines(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8);
        while (reader.ReadLine() is { } line) yield return line;
    }

    public static async Task AppendAsync(string file, string text, CancellationToken ct, int attempts = 5, int delayMs = 200)
    {
        var bytes = Utf8.GetBytes(text);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
                await stream.WriteAsync(bytes, ct);
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < attempts)
            {
                await Task.Delay(delayMs * attempt, ct);
            }
        }
    }

    private static bool IsSharingViolation(IOException ex) => ex.HResult is SharingViolation or LockViolation;
}

using IdiotProof.Engine.Storage;

namespace IdiotProof.Engine;

public sealed class AuditLogger
{
    private readonly string logPath;
    private readonly object lockObj = new();

    public AuditLogger(IStorageProvider storage)
    {
        logPath = Path.Combine(storage.LogsPath, "audit.log");
    }

    public void Log(string action, string details, string? symbol = null)
    {
        var entry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC} | {action,-20} | {symbol ?? "-",-6} | {details}";
        lock (lockObj)
        {
            // The Blazor host and the Monitor console are separate PROCESSES
            // sharing this file; the in-process lock can't stop the other one
            // holding it open. Retry briefly on contention, then drop the line
            // — a lost audit line must never take down the caller.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.AppendAllText(logPath, entry + Environment.NewLine);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
                catch (IOException)
                {
                    return;
                }
            }
        }
    }

    public IReadOnlyList<string> GetRecent(int count = 100)
    {
        if (!File.Exists(logPath)) return [];
        var lines = File.ReadAllLines(logPath);
        return lines.TakeLast(count).Reverse().ToList();
    }
}

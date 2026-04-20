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
            File.AppendAllText(logPath, entry + Environment.NewLine);
        }
    }

    public IReadOnlyList<string> GetRecent(int count = 100)
    {
        if (!File.Exists(logPath)) return [];
        var lines = File.ReadAllLines(logPath);
        return lines.TakeLast(count).Reverse().ToList();
    }
}

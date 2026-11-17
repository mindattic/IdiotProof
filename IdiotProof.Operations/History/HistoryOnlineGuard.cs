using IdiotProof.Core.History;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.History;

/// <summary>
/// The scheduled commands' side of the history database's online window (IP-T542). A command that reads or writes
/// the history database and is run on a timer (research scan, gapper radar) asks <see cref="SkipAsync"/> first:
/// when that database is on Azure SQL Database and the time is outside the window, the command does nothing and
/// ends OK (exit code 0, so a scheduler does not count a deliberate skip as a failure) with a message that says
/// so, unless the caller passed <c>--wake-database</c>. On a local SQL Server it never skips.
/// </summary>
public static class HistoryOnlineGuard
{
    public const string OptionName = "wake-database";

    public const string OptionDescription =
        "Run even outside the database online window (trading days, 03:30-20:30 ET by default). Only matters when the history database is on Azure SQL Database, where this wakes the paused serverless database and it is billed until it pauses again.";

    /// <summary>The result to return instead of running, or null when the command may run.</summary>
    public static async Task<OperationResult?> SkipAsync(OperationContext ctx, bool wakeDatabase, string command)
    {
        if (wakeDatabase) return null;
        if (ctx.Services.GetService<HistoryOnlineWindow>() is not { IsAzure: true } window) return null;
        if (await window.IsOpenAsync(ctx.CancellationToken)) return null;

        var config = await window.ReadAsync(ctx.CancellationToken);
        var nowEt = EasternClock.ToEastern(ctx.UtcNow);
        var today = config.Describe(DateOnly.FromDateTime(nowEt));
        return OperationResult.Ok(
            new { Skipped = true, Reason = "outside-database-online-window", Command = command, NowEt = nowEt.ToString("yyyy-MM-dd HH:mm"), WindowToday = today },
            $"Skipped `{command}`: outside the database online window (now {nowEt:yyyy-MM-dd HH:mm} ET; today's window: {today}). " +
            $"The history database is on Azure SQL Database and is left asleep outside it. Nothing ran. Pass --{OptionName} to run anyway.");
    }
}

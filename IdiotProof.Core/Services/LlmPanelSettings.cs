using System.Globalization;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Services;

/// <summary>
/// The LLM voter panel's runtime-editable consensus threshold (<c>idiotproof llm threshold</c>),
/// kept in <c>SettingsKv</c> so a change reaches the Monitor's next vote without a restart and
/// survives one. <c>AppSettings.LlmConsensusThreshold</c> stays the default when nothing is saved.
/// </summary>
public static class LlmPanelSettings
{
    /// <summary>The SettingsKv key (the convention <c>SettingsKv</c> itself names).</summary>
    public const string ThresholdKey = "llm.consensusThreshold";

    /// <summary>The saved threshold when it is a valid fraction in (0, 1], else <paramref name="fallback"/>.</summary>
    public static async Task<decimal> ThresholdAsync(IDbContextFactory<AppDbContext> dbFactory, decimal fallback, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var raw = await db.SettingsKv.Where(s => s.Key == ThresholdKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v is > 0m and <= 1m ? v : fallback;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback; // the settings table being unreadable must not stop a vote; the default is the stricter-known value
        }
    }

    /// <summary>Saves the threshold (a fraction in (0, 1]).</summary>
    public static async Task SetThresholdAsync(IDbContextFactory<AppDbContext> dbFactory, decimal value, CancellationToken ct = default)
    {
        if (value is <= 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(value), "The consensus threshold is a fraction above 0 and at most 1.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var text = value.ToString(CultureInfo.InvariantCulture);
        var row = await db.SettingsKv.FirstOrDefaultAsync(s => s.Key == ThresholdKey, ct);
        if (row is null) db.SettingsKv.Add(new SettingsKv { Key = ThresholdKey, Value = text, UpdatedUtc = DateTime.UtcNow });
        else { row.Value = text; row.UpdatedUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
}

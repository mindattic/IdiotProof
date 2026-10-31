using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;

namespace IdiotProof.Operations.Account;

/// <summary>One account's answer to a read across <see cref="AccountScope"/>: the value, or why it could not be read.</summary>
internal sealed record AccountRead<T>(BrokerMode Mode, T? Value, string? Error)
{
    public string Name => Mode == BrokerMode.Paper ? "Paper" : "Live";

    public string Key => Name.ToLowerInvariant();
}

/// <summary>
/// Raw Alpaca Trading API reads for the account family, through <see cref="AlpacaApiFactory"/> so they
/// sit behind the owner's consent gate. Alpaca sends money as JSON strings; the helpers here read
/// either shape.
/// </summary>
internal static class AccountApis
{
    public static BrokerMode[] Modes(AccountScope scope) => scope switch
    {
        AccountScope.Paper => [BrokerMode.Paper],
        AccountScope.Live => [BrokerMode.Live],
        _ => [BrokerMode.Paper, BrokerMode.Live],
    };

    /// <summary>
    /// Runs <paramref name="read"/> against each account in <paramref name="scope"/>. With one account
    /// a failure propagates (the dispatcher turns it into Blocked/Failed); with both, each account's
    /// failure becomes its row's error so the other still shows.
    /// </summary>
    public static async Task<List<AccountRead<T>>> ForEachAsync<T>(OperationContext ctx, AccountScope scope, Func<AlpacaApi, Task<T>> read)
    {
        var owner = ctx.RequireOwner();
        var factory = ctx.Get<AlpacaApiFactory>();
        var modes = Modes(scope);
        var reads = await Task.WhenAll(modes.Select(async mode =>
        {
            try
            {
                await using var api = await factory.ForOwnerAsync(owner, mode, ctx.CancellationToken);
                return new AccountRead<T>(mode, await read(api), null);
            }
            catch (Exception ex) when (modes.Length > 1 && ex is AlpacaAccessException or AlpacaApiException)
            {
                return new AccountRead<T>(mode, default, ex.Message);
            }
        }));
        return [.. reads];
    }

    /// <summary>A number Alpaca sent as a string or a number; null when absent, null or unparseable.</summary>
    public static decimal? Dec(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? Dec(v) : null;

    public static decimal? Dec(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : (decimal)v.GetDouble(),
        JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
        _ => null,
    };

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => v.GetRawText(),
            }
            : null;

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    /// <summary>An ISO-8601 instant (with Z or an offset) as UTC; null when absent or unparseable.</summary>
    public static DateTime? Instant(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime
            : null;

    public static string Money(decimal? v) => v is { } m ? Rendering.Fmt.Money(m) : "-";

    public static string Signed(decimal? v) => v is { } m ? Rendering.Fmt.SignedMoney(m) : "-";

    /// <summary>The ET calendar date of <paramref name="utc"/>.</summary>
    public static DateOnly EtDate(DateTime utc) => DateOnly.FromDateTime(EasternClock.ToEastern(utc));

    /// <summary>Activity types that move cash in or out without trading: deposits, withdrawals, cash journals and cash ACATS.</summary>
    public const string CashFlowTypes = "CSD,CSW,JNLC,ACATC";

    /// <summary>
    /// Net cash moved into the account (deposits minus withdrawals) on ET dates
    /// [<paramref name="from"/>, <paramref name="to"/>], from <c>/v2/account/activities</c>.
    /// Alpaca's portfolio-history <c>profit_loss</c> counts an instant ACH deposit as profit, so
    /// true P&amp;L is the equity change minus this.
    /// </summary>
    public static async Task<decimal> NetCashFlowAsync(AlpacaApi api, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await AccountActivitiesOperation.FetchAsync(api, "/v2/account/activities", CashFlowTypes.Split(','),
            EtMidnightUtc(from), EtMidnightUtc(to.AddDays(1)), limit: 1000, ct);
        return rows.Sum(a => Dec(a, "net_amount") ?? 0);
    }

    /// <summary>Midnight ET at the start of <paramref name="date"/>, as UTC.</summary>
    public static DateTime EtMidnightUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), IdiotProof.Scripting.MarketTime.Eastern);
}

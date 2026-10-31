using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Account;

public sealed record AccountClockArgs
{
    [Option("mode", Description = "Which Alpaca account's keys ask: paper or live (default paper; the clock is the same).")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

/// <summary>Alpaca's market clock (<c>GET /v2/clock</c>): open or closed now, and the next open and close, in ET.</summary>
[Operation("account clock", Summary = "Is the market open now, and when it next opens and closes (ET), from Alpaca's clock.", Risk = OperationRisk.Read)]
[Example("idiotproof account clock")]
public sealed class AccountClockOperation : IOperation<AccountClockArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountClockArgs args, OperationContext ctx)
    {
        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(ctx.RequireOwner(), args.Mode, ctx.CancellationToken);
        var clock = await api.GetAsync("/v2/clock", ct: ctx.CancellationToken);
        var isOpen = AccountApis.Bool(clock, "is_open");
        var now = AccountApis.Instant(clock, "timestamp") ?? ctx.UtcNow;
        var nextOpen = AccountApis.Instant(clock, "next_open");
        var nextClose = AccountApis.Instant(clock, "next_close");

        var next = isOpen ? nextClose : nextOpen;
        var headline = (isOpen ? "Market OPEN" : "Market CLOSED")
            + (next is { } n ? $" · {(isOpen ? "closes" : "opens")} {Fmt.Et(n)} (in {Until(n - now)})" : "");
        var block = new KeyValueBlock()
            .Add("Now", Fmt.Et(now))
            .Add("Next open", Fmt.Et(nextOpen))
            .Add("Next close", Fmt.Et(nextClose));
        var data = new
        {
            isOpen,
            nowEt = Fmt.Et(now), nextOpenEt = Fmt.Et(nextOpen), nextCloseEt = Fmt.Et(nextClose),
            nowUtc = now, nextOpenUtc = nextOpen, nextCloseUtc = nextClose,
        };
        return OperationResult.Ok(data, headline + "\n" + block.Render());
    }

    /// <summary><c>2d 3h</c>, <c>2h 13m</c>, <c>7m</c>.</summary>
    internal static string Until(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{Math.Max(0, (int)Math.Ceiling(span.TotalMinutes))}m";
    }
}

public sealed record AccountCalendarArgs
{
    [Option("from", Description = "First date, yyyy-MM-dd (default today ET).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last date, yyyy-MM-dd (default 14 days after --from).")]
    public DateOnly? To { get; init; }

    [Option("mode", Description = "Which Alpaca account's keys ask: paper or live (default paper; the calendar is the same).")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

/// <summary>One calendar day; times are ET as Alpaca publishes them. A weekday holiday has <see cref="Open"/> null.</summary>
public sealed record MarketDay(string Date, string Day, string? PremarketOpen, string? Open, string? Close, string? AfterHoursClose, string Note);

/// <summary>
/// Alpaca's trading calendar (<c>GET /v2/calendar</c>): each trading day's extended and regular
/// session times in ET, with early closes and weekday holidays called out.
/// </summary>
[Operation("account calendar", Summary = "Trading days with premarket, regular and after-hours times (ET); early closes and holidays called out.", Risk = OperationRisk.Read)]
[Example("idiotproof account calendar")]
[Example("idiotproof account calendar --from 2026-11-20 --to 2026-12-31")]
public sealed class AccountCalendarOperation : IOperation<AccountCalendarArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountCalendarArgs args, OperationContext ctx)
    {
        var from = args.From ?? AccountApis.EtDate(ctx.UtcNow);
        var to = args.To ?? from.AddDays(14);
        if (to < from) return OperationResult.Usage("--to is before --from.");
        if (to.DayNumber - from.DayNumber > 366) return OperationResult.Usage("Ask for at most a year at a time.");

        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(ctx.RequireOwner(), args.Mode, ctx.CancellationToken);
        var json = await api.GetAsync("/v2/calendar", new Dictionary<string, string?>
        {
            ["start"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, ctx.CancellationToken);

        var trading = new Dictionary<DateOnly, JsonElement>();
        if (json.ValueKind == JsonValueKind.Array)
            foreach (var d in json.EnumerateArray())
                if (DateOnly.TryParseExact(AccountApis.Str(d, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    trading[date] = d;

        var days = new List<MarketDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var label = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dow = date.ToString("ddd", CultureInfo.InvariantCulture);
            if (trading.TryGetValue(date, out var d))
            {
                var close = AccountApis.Str(d, "close");
                days.Add(new MarketDay(label, dow, Clock(AccountApis.Str(d, "session_open")), Clock(AccountApis.Str(d, "open")),
                    Clock(close), Clock(AccountApis.Str(d, "session_close")), close is "16:00" or null ? "" : "early close"));
            }
            else if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(new MarketDay(label, dow, null, null, null, null, "closed (holiday)"));
            }
        }

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Date").Column("Day").Column("Premarket", Align.Right).Column("Open", Align.Right)
            .Column("Close", Align.Right).Column("After-hours end", Align.Right).Column("Note");
        foreach (var d in days)
            table.Row(d.Date, d.Day, d.PremarketOpen ?? "-", d.Open ?? "-", d.Close ?? "-", d.AfterHoursClose ?? "-", d.Note);
        var head = $"Market calendar {from:yyyy-MM-dd} -> {to:yyyy-MM-dd}, times ET ({days.Count(d => d.Open is not null)} trading days)";
        return OperationResult.Ok(new { from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), days },
            head + "\n" + (days.Count == 0 ? "No weekdays in that range." : table.Render()));
    }

    /// <summary><c>09:30</c> or <c>0930</c> as <c>9:30 AM</c>; null passes through.</summary>
    internal static string? Clock(string? hhmm)
    {
        if (hhmm is null) return null;
        var digits = hhmm.Replace(":", "");
        return TimeOnly.TryParseExact(digits, "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : hhmm;
    }
}

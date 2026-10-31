using System.Globalization;
using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Account;

namespace IdiotProof.Operations.Positions;

/// <summary>
/// One open position as Alpaca reports it (<c>GET /v2/positions</c>). Quantities keep their sign
/// (negative = short) and their fractions; options carry the decoded contract.
/// </summary>
public sealed record BrokerPosition
{
    public required string Symbol { get; init; }

    /// <summary>Signed: positive long, negative short. Contracts for options, coins for crypto.</summary>
    public decimal Quantity { get; init; }

    /// <summary>What is not already promised to an open order (Alpaca <c>qty_available</c>); null when not reported.</summary>
    public decimal? QuantityAvailable { get; init; }

    public decimal AverageEntryPrice { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? LastDayPrice { get; init; }
    public decimal MarketValue { get; init; }
    public decimal CostBasis { get; init; }
    public decimal UnrealizedPnl { get; init; }

    /// <summary>Unrealized P&amp;L as a fraction of cost (0.05 = 5%); null when not reported.</summary>
    public decimal? UnrealizedPnlPercent { get; init; }

    public decimal? UnrealizedIntradayPnl { get; init; }

    /// <summary>Alpaca's <c>asset_class</c>: us_equity, us_option or crypto.</summary>
    public string AssetClass { get; init; } = "us_equity";

    public string Exchange { get; init; } = "";

    /// <summary>The decoded contract for an option position, else null.</summary>
    public OptionContract? Option { get; init; }

    public bool IsShort => Quantity < 0;
    public bool IsOption => Option is not null;
    public bool IsCrypto => string.Equals(AssetClass, "crypto", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses one element of the positions array (all numbers arrive as strings).</summary>
    public static BrokerPosition Parse(JsonElement e)
    {
        var symbol = Str(e, "symbol") ?? "";
        var assetClass = Str(e, "asset_class") ?? "us_equity";
        return new BrokerPosition
        {
            Symbol = symbol,
            Quantity = Dec(e, "qty") ?? 0m,
            QuantityAvailable = Dec(e, "qty_available"),
            AverageEntryPrice = Dec(e, "avg_entry_price") ?? 0m,
            CurrentPrice = Dec(e, "current_price"),
            LastDayPrice = Dec(e, "lastday_price"),
            MarketValue = Dec(e, "market_value") ?? 0m,
            CostBasis = Dec(e, "cost_basis") ?? 0m,
            UnrealizedPnl = Dec(e, "unrealized_pl") ?? 0m,
            UnrealizedPnlPercent = Dec(e, "unrealized_plpc"),
            UnrealizedIntradayPnl = Dec(e, "unrealized_intraday_pl"),
            AssetClass = assetClass,
            Exchange = Str(e, "exchange") ?? "",
            Option = string.Equals(assetClass, "us_option", StringComparison.OrdinalIgnoreCase) ? OptionContract.ParseOcc(symbol) : null,
        };
    }

    /// <summary>The machine shape used in every positions operation's JSON.</summary>
    public object ToData() => new
    {
        Symbol, Quantity, QuantityAvailable, Side = IsShort ? "short" : "long", AverageEntryPrice, CurrentPrice, LastDayPrice,
        MarketValue, CostBasis, UnrealizedPnl, UnrealizedPnlPercent, UnrealizedIntradayPnl, AssetClass, Exchange,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static decimal? Dec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var n)) return n;
        return p.ValueKind == JsonValueKind.String
            && decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}

/// <summary>One Alpaca account's open positions, or why they could not be read.</summary>
public sealed record AccountPositions(BrokerMode Mode, IReadOnlyList<BrokerPosition> Positions, string? Error)
{
    public string Name => Mode.ToString();
}

/// <summary>Reads the owner's Alpaca positions through <see cref="AlpacaApiFactory"/> (paper, live or both).</summary>
public static class BrokerPositions
{
    /// <summary>Paper and/or Live for an <see cref="AccountScope"/>.</summary>
    public static BrokerMode[] Modes(AccountScope scope) => scope switch
    {
        AccountScope.Paper => [BrokerMode.Paper],
        AccountScope.Live => [BrokerMode.Live],
        _ => [BrokerMode.Paper, BrokerMode.Live],
    };

    /// <summary>
    /// Every open position in each account of <paramref name="scope"/>. With one account named, an
    /// access problem throws (the dispatcher reports it as Blocked); with both, it becomes that
    /// account's <see cref="AccountPositions.Error"/> so the other still shows.
    /// </summary>
    public static async Task<List<AccountPositions>> LoadAsync(OperationContext ctx, AccountScope scope)
    {
        var owner = ctx.RequireOwner();
        var result = new List<AccountPositions>();
        foreach (var mode in Modes(scope))
        {
            try
            {
                result.Add(new AccountPositions(mode, await ListAsync(ctx, owner, mode), null));
            }
            catch (Exception ex) when (scope == AccountScope.All && ex is AlpacaAccessException or AlpacaApiException)
            {
                result.Add(new AccountPositions(mode, [], ex is AlpacaApiException api ? api.AlpacaMessage : ex.Message));
            }
        }
        return result;
    }

    /// <summary>Every open position in one account; throws on access or API errors (an error is never "flat").</summary>
    public static async Task<List<BrokerPosition>> ListAsync(OperationContext ctx, Guid owner, BrokerMode mode)
    {
        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, mode, ctx.CancellationToken);
        var root = await api.GetAsync("/v2/positions", ct: ctx.CancellationToken);
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().Select(BrokerPosition.Parse).ToList() : [];
    }

    /// <summary>One symbol's position in one account, or null when the account holds none (Alpaca 404).</summary>
    public static async Task<BrokerPosition?> GetAsync(OperationContext ctx, Guid owner, BrokerMode mode, string symbol)
    {
        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, mode, ctx.CancellationToken);
        try
        {
            var root = await api.GetAsync("/v2/positions/" + Uri.EscapeDataString(PathSymbol(symbol)), ct: ctx.CancellationToken);
            return root.ValueKind == JsonValueKind.Object ? BrokerPosition.Parse(root) : null;
        }
        catch (AlpacaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Upper-cased, and a crypto pair's slash dropped (Alpaca takes BTCUSD in the path).</summary>
    public static string PathSymbol(string symbol) => symbol.Trim().ToUpperInvariant().Replace("/", "");

    /// <summary>True when <paramref name="p"/> is the position the user named (BTC/USD matches BTCUSD).</summary>
    public static bool Matches(BrokerPosition p, string symbol) =>
        string.Equals(PathSymbol(p.Symbol), PathSymbol(symbol), StringComparison.OrdinalIgnoreCase);
}

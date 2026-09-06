using System.Collections.Concurrent;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// In-memory sandbox broker for testing. No external API calls. Orders fill
/// instantly at the limit price (or $0-cost basis for market orders with no
/// reference price) and update the in-memory position book, so a sandbox
/// session behaves like a real fill loop: buy → position appears, sell →
/// position shrinks/disappears.
/// <para>
/// Options: serves a SYNTHETIC chain (strikes ±20% around a reference price, four weekly
/// expirations) with model-shaped premiums so the whole Options UI can be built, demoed and
/// Cypress-tested with zero Alpaca options entitlement. Greeks/IV are deliberately omitted
/// from sandbox quotes so the UI's local Black-Scholes fallback path gets exercised.
/// </para>
/// </summary>
public sealed class SandboxBrokerClient : IBrokerClient
{
    private readonly ConcurrentDictionary<string, Position> positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> referencePrices = new(StringComparer.OrdinalIgnoreCase);

    public BrokerType BrokerType => BrokerType.Sandbox;

    /// <summary>Sandbox is always simulated — never real money.</summary>
    public bool IsPaper => true;

    public bool IsConnected => true;

    public bool SupportsOptions => true;

    /// <summary>Sandbox pretends to be fully approved so the ticket is never disabled here.</summary>
    public Task<int> GetOptionTradingLevelAsync(CancellationToken ct = default) => Task.FromResult(3);

    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task DisconnectAsync() => Task.CompletedTask;

    /// <summary>
    /// Underlying price the synthetic chain is built around. Hosts set this from the real
    /// data feed; when unset, a deterministic per-symbol price in $15–$215 is used so the
    /// same ticker always yields the same chain.
    /// </summary>
    public void SetReferencePrice(string symbol, decimal price) => referencePrices[symbol.Trim().ToUpperInvariant()] = price;

    public decimal GetReferencePrice(string symbol)
    {
        var key = symbol.Trim().ToUpperInvariant();
        if (referencePrices.TryGetValue(key, out var p)) return p;
        var hash = key.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        return 15m + Math.Abs(hash % 20000) / 100m;
    }

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (request.AssetClass == AssetClass.Option)
            return Task.FromResult(FillOption(request));

        // Simulate an immediate fill. Fill price = limit price when present;
        // market orders without a reference price fill at 0 (harmless for the
        // position book — quantity tracking is what matters in sandbox).
        var fillPrice = request.LimitPrice ?? request.StopPrice ?? 0m;
        var qty = request.Quantity > 0
            ? request.Quantity
            : fillPrice > 0m && request.Notional is { } notional
                ? Math.Floor(notional / fillPrice)
                : 0m;
        var signedQty = request.Side == OrderSide.Buy ? qty : -qty;

        Book(request.Symbol.ToUpperInvariant(), signedQty, fillPrice, multiplier: 1, contract: null);

        var result = new OrderResult
        {
            BrokerOrderId = $"SANDBOX-{Guid.NewGuid():N}",
            IsSuccess = true,
            Message = $"Sandbox order filled ({request.Side} {(qty > 0 ? qty : request.Notional)} {request.Symbol}@{fillPrice:F2})."
        };
        return Task.FromResult(result);
    }

    private OrderResult FillOption(OrderRequest request)
    {
        if (request.Option is null)
            return new OrderResult { IsSuccess = false, Message = "Option order requires the contract (OrderRequest.Option)." };
        if (request.Quantity <= 0)
            return new OrderResult { IsSuccess = false, Message = "Option order requires a contract count of at least 1." };

        // Limit price wins; market orders fill at the synthetic mid.
        var fillPrice = request.LimitPrice ?? SyntheticQuote(request.Option, GetReferencePrice(request.Option.UnderlyingSymbol), DateTime.UtcNow).Mid;
        var signedQty = request.Side == OrderSide.Buy ? request.Quantity : -request.Quantity;

        Book(request.Option.OccSymbol, signedQty, fillPrice, request.Option.Multiplier, request.Option);

        return new OrderResult
        {
            BrokerOrderId = $"SANDBOX-{Guid.NewGuid():N}",
            IsSuccess = true,
            Message = $"Sandbox option filled ({request.Side} {request.Quantity}× {request.Option.DisplayName} @ {fillPrice:F2}/sh)."
        };
    }

    private void Book(string key, decimal signedQty, decimal fillPrice, int multiplier, OptionContract? contract)
    {
        positions.AddOrUpdate(
            key,
            _ => new Position
            {
                Symbol = key,
                Quantity = signedQty,
                AveragePrice = fillPrice,
                MarketValue = signedQty * fillPrice * multiplier,
                AssetClass = contract is null ? AssetClass.Equity : AssetClass.Option,
                Option = contract,
            },
            (_, existing) =>
            {
                var newQty = existing.Quantity + signedQty;
                return new Position
                {
                    Symbol = existing.Symbol,
                    Quantity = newQty,
                    // Weighted average only when adding in the same direction;
                    // reductions keep the original basis.
                    AveragePrice = signedQty > 0 && existing.Quantity > 0 && newQty != 0
                        ? (existing.AveragePrice * existing.Quantity + fillPrice * signedQty) / newQty
                        : existing.AveragePrice,
                    MarketValue = newQty * fillPrice * multiplier,
                    AssetClass = existing.AssetClass,
                    Option = existing.Option,
                };
            });

        // Zero-qty rows are filtered in GetPositionsAsync rather than removed here.
        // The non-atomic read-then-remove window would let a concurrent AddOrUpdate
        // delete a just-created non-zero position (TOCTOU race).
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        return Task.FromResult(new OrderResult
        {
            BrokerOrderId = orderId,
            IsSuccess = true,
            Message = "Sandbox order cancelled."
        });
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Position> result = this.positions.Values.Where(p => p.Quantity != 0m).ToList();
        return Task.FromResult(result);
    }

    // ---------------------------------------------------------------- Synthetic options

    /// <summary>Next four Fridays from today (weeklies). Deterministic given the clock.</summary>
    public static IReadOnlyList<DateOnly> SyntheticExpirations(DateOnly today)
    {
        var daysToFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
        if (daysToFriday == 0) daysToFriday = 7; // today is Friday → start next week
        var first = today.AddDays(daysToFriday);
        return [first, first.AddDays(7), first.AddDays(14), first.AddDays(28)];
    }

    public Task<IReadOnlyList<OptionContract>> GetOptionChainAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol)) return Task.FromResult<IReadOnlyList<OptionContract>>([]);
        var underlying = underlyingSymbol.Trim().ToUpperInvariant();
        var spot = GetReferencePrice(underlying);
        var step = StrikeStep(spot);
        var atm = Math.Round(spot / step) * step;

        var expirations = expiration is { } e ? [e] : SyntheticExpirations(DateOnly.FromDateTime(DateTime.UtcNow));
        var contracts = new List<OptionContract>();
        foreach (var exp in expirations)
        {
            for (var i = -8; i <= 8; i++)
            {
                var strike = atm + i * step;
                if (strike <= 0m) continue;
                foreach (var right in new[] { OptionRight.Call, OptionRight.Put })
                {
                    contracts.Add(new OptionContract
                    {
                        OccSymbol = OptionContract.BuildOcc(underlying, exp, right, strike),
                        UnderlyingSymbol = underlying,
                        Expiration = exp,
                        Strike = strike,
                        Right = right,
                        Tradable = true,
                        OpenInterest = 100 + Math.Abs(8 - Math.Abs(i)) * 250,
                    });
                }
            }
        }
        return Task.FromResult<IReadOnlyList<OptionContract>>(contracts);
    }

    public async Task<IReadOnlyList<OptionQuote>> GetOptionQuotesAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        var chain = await GetOptionChainAsync(underlyingSymbol, expiration, ct).ConfigureAwait(false);
        if (chain.Count == 0) return [];
        var spot = GetReferencePrice(chain[0].UnderlyingSymbol);
        var now = DateTime.UtcNow;
        return chain.Select(c => SyntheticQuote(c, spot, now)).ToList();
    }

    /// <summary>
    /// Intrinsic + a time-value hump that decays with |moneyness| and √T. Not Black-Scholes
    /// (Brokers doesn't reference Shared) — just plausible-looking premiums with a spread.
    /// </summary>
    private static OptionQuote SyntheticQuote(OptionContract c, decimal spot, DateTime nowUtc)
    {
        var intrinsic = c.Right == OptionRight.Call ? Math.Max(0m, spot - c.Strike) : Math.Max(0m, c.Strike - spot);
        var years = Math.Max(1.0 / 365, (c.Expiration.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc) - nowUtc).TotalDays / 365.0);
        var moneyness = (double)((c.Strike - spot) / spot);
        var timeValue = (decimal)((double)spot * 0.45 * 0.4 * Math.Sqrt(years) * Math.Exp(-Math.Abs(moneyness) * 6));
        var mid = Math.Round(intrinsic + Math.Max(0.01m, timeValue), 2);
        var half = Math.Max(0.01m, Math.Round(mid * 0.03m, 2));
        return new OptionQuote(c.OccSymbol, Math.Max(0.01m, mid - half), mid + half, mid, ImpliedVolatility: null, Greeks: null, nowUtc);
    }

    private static decimal StrikeStep(decimal spot) => spot switch
    {
        < 25m => 0.5m,
        < 100m => 1m,
        < 250m => 2.5m,
        _ => 5m,
    };
}

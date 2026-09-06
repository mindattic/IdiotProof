using IdiotProof.Shared.Options;

namespace IdiotProof.UI.Components.Options;

/// <summary>One piece of options jargon: the word on screen, a hover-length hint, and the full plain-English sentence.</summary>
public sealed record JargonEntry(string Key, string Title, string Short, string Long);

/// <summary>
/// The ONE place the Options section defines its jargon. <see cref="Jargon"/> renders an entry as a
/// dotted-underline word with a hover <c>title</c> (Short) and a click-to-open explanation (Long);
/// every <c>title="…"</c> in the chain, ticket and tracker pulls from here too, so the same term is
/// never explained two different ways. Wording tracks the bible glossary (docs/BIBLE.md §9).
/// Lives in the RCL on purpose — it must not depend on the host's DslGlossary.
/// </summary>
public static class OptionsGlossary
{
    private static readonly Dictionary<string, JargonEntry> entries = new(StringComparer.OrdinalIgnoreCase);

    static OptionsGlossary()
    {
        Add("call", "Call",
            "A bet the stock goes UP. Gives you the right to buy 100 shares at the strike.",
            "A call is the right (not the obligation) to BUY 100 shares at the strike price until expiration. You buy calls when you think the stock will rise; the contract gets more valuable as the stock climbs and as excitement about it grows.");
        Add("put", "Put",
            "A bet the stock goes DOWN. Gives you the right to sell 100 shares at the strike.",
            "A put is the right (not the obligation) to SELL 100 shares at the strike price until expiration. You buy puts when you think the stock will fall; the contract gets more valuable as the stock drops.");
        Add("strike", "Strike",
            "The price the contract lets you buy (call) or sell (put) the stock at.",
            "The strike is the fixed price written into the contract. A call is 'in the money' when the stock is above its strike; a put when the stock is below. The further the stock moves past the strike in your favour, the more real value the contract has.");
        Add("premium", "Premium",
            "The price of the option, quoted per share. × 100 for one contract.",
            "The premium is what one contract costs, quoted per share. Multiply by 100 (the contract size) to get the dollars you actually pay or receive. It is made of two parts: REAL value (in the money right now) and HYPE (everything else).");
        Add("contract", "Contract",
            "One option contract covers 100 shares. Every $ figure per share × 100.",
            "Options trade in contracts, and one contract covers 100 shares of the stock. So a premium quoted at $2.50 per share costs $250 per contract. Quantities on this page are always whole contracts.");
        Add("expiration", "Expiration",
            "The last day the contract exists. After this it is worth exactly its real value, or nothing.",
            "Expiration is the date the contract stops existing. By then all the hype (time value) has drained to zero and only the real (intrinsic) value is left — which is nothing if the stock never crossed the strike. You do not have to hold to expiration; most people sell earlier.");
        Add("dte", "DTE",
            "Days to expiration. Fewer days = hype drains faster.",
            "DTE is the number of calendar days until the contract expires. Time value decays faster and faster as DTE shrinks, so a contract with 5 days left loses its hype much quicker than one with 60.");
        Add("real", "Real (intrinsic) value",
            "What the contract is worth if exercised right now: max(0, stock − strike) for a call.",
            "Real value — intrinsic value in textbook terms — is what the contract would be worth if you exercised it this second: for a call, stock price minus strike (never below zero); for a put, strike minus stock price. It is the part of the premium that is not a guess.");
        Add("hype", "Hype (extrinsic / time value)",
            "Premium minus real value: what the market pays for the IDEA of a move. Decays to zero by expiration.",
            "Hype — extrinsic or time value in textbook terms — is the premium minus the real value: the part you pay for the possibility that the stock moves. It inflates when the stock runs and the news is hot, and it decays to zero by expiration. Selling the contract while the hype is high is the whole trade; hype is what evaporates if you wait for reality.");
        Add("hype-meter", "Hype meter",
            "Colour = how much of the premium is hype. Green: mostly real. Red: all hype.",
            "The hype meter colours each contract by the share of its premium that is hype (time value): green means mostly real value, yellow around half, red means the price is almost entirely hype. Red is not bad — it is where the biggest percentage moves happen — but it is the value that vanishes fastest.");
        Add("breakeven", "Breakeven",
            "Stock price where the trade nets zero IF held to expiration. Strike + premium for a call.",
            "Breakeven is the stock price at expiration where you neither make nor lose money: strike plus premium for a call, strike minus premium for a put. It only matters if you hold to the very end. Selling early means you can profit long before the stock ever gets there — the premium just has to rise.");
        Add("iv", "Implied volatility (IV)",
            "How big a move the market is pricing in, annualised. Higher IV = pricier options.",
            "Implied volatility is the size of the move the market is betting on, expressed as an annualised percentage. It is backed out of the live premium: higher IV means options are more expensive because traders expect bigger swings. A spike in IV is a spike in hype.");
        Add("iv-source", "IV source",
            "Alpaca = the broker's own figure. Model = solved locally with Black-Scholes.",
            "The little badge next to IV says where the number came from. 'Alpaca' means the broker supplied it with the quote. 'Model' means Alpaca sent none (common near expiration or in Sandbox), so IdiotProof solved it locally from the mid price with the Black-Scholes formula.");
        Add("model", "Model fair value",
            "What Black-Scholes says the contract should be worth at this IV. A cross-check, not a prediction.",
            "Model fair value is the theoretical price from the Black-Scholes formula given the stock price, strike, days left, risk-free rate and IV. It assumes European exercise and no dividends, so treat it as a cross-check against the live price rather than a prediction.");
        Add("bid-ask", "Bid / Ask",
            "Bid = what buyers offer. Ask = what sellers want. You buy near the ask, sell near the bid.",
            "The bid is the highest price a buyer is currently offering; the ask is the lowest price a seller will take. If you buy at market you pay roughly the ask; if you sell at market you receive roughly the bid. The gap between them is the spread — your hidden cost for trading.");
        Add("mid", "Mid",
            "Halfway between bid and ask. The default limit price on the ticket.",
            "The mid-point sits halfway between the bid and the ask and is the fairest single estimate of a contract's price. The ticket pre-fills your limit price with it; a limit at the mid may take a moment to fill, a limit at the ask fills fast but costs more.");
        Add("limit", "Limit order",
            "Fill only at your price or better. Safer; may not fill.",
            "A limit order says 'buy for no more than X per share' (or 'sell for no less than X'). It protects you from paying a bad price on a wide spread, but it can sit unfilled if the market never comes to your price.");
        Add("market", "Market order",
            "Fill right now at whatever price is available. Fast; can be expensive on wide spreads.",
            "A market order fills immediately at the best available price. On thinly traded options the spread can be wide, so a market buy can fill well above the mid. Prefer limit orders for options unless speed matters more than price.");
        Add("buy-to-open", "Buy to open",
            "Opens a NEW long position: you are buying a contract you don't already hold.",
            "Buy to open means you are starting a new position by buying a contract. This is the ordinary 'buy the idea' trade. Your maximum loss is the premium you pay — nothing more.");
        Add("sell-to-close", "Sell to close",
            "Sells a contract you already own. This is how you cash in the hype.",
            "Sell to close means you are selling a contract you already hold, ending the position. The money you receive is the current premium × 100 × contracts. This is the trade that locks in profit while the hype is high.");
        Add("sell-to-open", "Sell to open (writing)",
            "Creates a SHORT: you sell a contract you don't own and take on its obligation.",
            "Sell to open — writing an option — means selling a contract you do not hold. You collect the premium up front but take on the obligation behind it: a written call may force you to deliver shares, a written put to buy them. Losses can far exceed the premium collected. Needs a higher approval level.");
        Add("buy-to-close", "Buy to close",
            "Buys back a contract you wrote earlier, ending the short.",
            "Buy to close means buying back a contract you previously sold to open, which cancels the obligation and ends the short position.");
        Add("assignment", "Assignment risk",
            "If you WRITE an option, the buyer can exercise it against you — you must deliver or buy the shares.",
            "When you write (sell to open) an option, the person who bought it can exercise it at any time. If they do, you are 'assigned': a written call means you must sell them 100 shares per contract at the strike; a written put means you must buy 100 shares at the strike. This can happen at an unwelcome moment and is why writing needs more approval than buying.");
        Add("level", "Options approval level",
            "Alpaca's permission tier: 0 none · 1 covered only · 2 buy calls/puts · 3 spreads.",
            "Alpaca grants each account an options level. Level 0: options are off. Level 1: you may only write covered calls and cash-secured puts — you can't buy a call or put outright. Level 2: you can buy calls and puts (the buy-the-idea trade needs this). Level 3: spreads and straddles too. The ticket locks itself when the level doesn't allow what you're about to do.");
        Add("risk-free", "Risk-free rate",
            "Interest rate used by the Black-Scholes model — roughly the current T-bill yield.",
            "The risk-free rate is the interest you could earn with no risk (about the 3-month Treasury bill yield). Black-Scholes needs it to discount the strike price. It nudges model fair value and Model IV slightly; it has no effect on the real/hype split, which comes straight from the live premium.");
        Add("moneyness", "Moneyness (ITM / ATM / OTM)",
            "In / at / out of the money: whether the contract has real value right now.",
            "In the money (ITM): the contract has real value now — a call with the stock above its strike, a put with the stock below. At the money (ATM): strike ≈ stock price. Out of the money (OTM): no real value yet; the premium is all hype. ITM cells are shaded on the chain; the ATM strike carries a dot.");
        Add("sell-signal", "Sell-the-hype nudge",
            "Informational only: hype is near its recent high AND the news tape is bullish. Nothing is placed for you.",
            "The nudge appears on an open long position when two things hold: its hype (time value) is within 5% of the highest we've seen this session, and there is at least one bullish research item on the stock in the last 7 days. That is the moment the market is paying most for the idea. It is a suggestion to consider taking profit — nothing is ever placed automatically.");
        Add("sandbox", "Sandbox",
            "Pretend account inside IdiotProof: synthetic chain, instant fills, no broker, no money.",
            "Sandbox is IdiotProof's built-in practice account. The option chain is synthetic (strikes around a reference price, model-shaped premiums), every order fills instantly, and nothing leaves this machine. It is the safe default and the place to learn the flow.");
        Add("paper", "Paper",
            "Alpaca's simulated account: real market data, fake money.",
            "Paper is Alpaca's simulation account. Chains and quotes are real market data and orders go to Alpaca, but the money is fake. It needs Alpaca routing enabled on the API Keys page and the account approved for options.");
        Add("live", "Live",
            "Your real Alpaca account. Real money. Every order needs the 5-minute password elevation.",
            "Live is your real-money Alpaca account. Every order on it requires re-entering your password to open a 5-minute elevation window, and the confirmation is read back in red. Nothing here trades on its own.");
        Add("pnl", "P&L",
            "Unrealised profit or loss right now, using the mid price. Not final until you sell.",
            "P&L (profit and loss) is what you would make or lose if you closed the position at the current mid price: (now − average paid) × 100 × contracts for a long. It is unrealised — it moves with every quote until you actually sell.");
        Add("avg", "Avg / share",
            "What you paid per share, on average. × 100 per contract.",
            "Average price per share across your fills for this contract. Multiply by 100 to see what one contract cost you.");
        Add("now", "Now / share",
            "Current mid price per share for this contract.",
            "The current mid-point price per share for the contract, refreshed every 20 seconds. × 100 for the value of one contract.");
        Add("qty", "Qty",
            "Contracts held. Positive = long (you own them). Negative = short (you wrote them).",
            "The number of contracts you hold. A positive number means you bought them (long). A negative number means you wrote them (short) and carry the obligation.");
    }

    private static void Add(string key, string title, string shortText, string longText) =>
        entries[key] = new JargonEntry(key, title, shortText, longText);

    public static IReadOnlyCollection<JargonEntry> All => entries.Values;

    public static bool TryGet(string key, out JargonEntry entry) => entries.TryGetValue(key, out entry!);

    /// <summary>Entry for <paramref name="key"/>; falls back to a stub naming the key so a typo shows on screen instead of throwing mid-render.</summary>
    public static JargonEntry Get(string key) =>
        entries.TryGetValue(key, out var e) ? e : new JargonEntry(key, key, $"(no glossary entry for '{key}')", $"(no glossary entry for '{key}')");

    /// <summary>Hover text for a term — what every <c>title="…"</c> in the section should use.</summary>
    public static string Hint(string key) => Get(key).Short;

    /// <summary>Glossary key for an Alpaca <c>position_intent</c> code.</summary>
    public static string IntentKey(string positionIntent) => positionIntent switch
    {
        "buy_to_open" => "buy-to-open",
        "sell_to_close" => "sell-to-close",
        "sell_to_open" => "sell-to-open",
        "buy_to_close" => "buy-to-close",
        _ => positionIntent,
    };

    /// <summary>
    /// Plain words for what an order does to your position, instead of the raw
    /// <c>position_intent</c> code. <paramref name="existingContracts"/> is the signed count held.
    /// </summary>
    public static string IntentLabel(string positionIntent, decimal existingContracts) => positionIntent switch
    {
        "buy_to_open" => "Opens a new position",
        "sell_to_close" => $"Closes {(existingContracts > 0m ? existingContracts.ToString("0") + " you already hold" : "your position")}",
        "sell_to_open" => "Opens a SHORT — you'd be writing the option",
        "buy_to_close" => $"Buys back {(existingContracts < 0m ? Math.Abs(existingContracts).ToString("0") + " you wrote" : "the contracts you wrote")}",
        _ => positionIntent,
    };

    /// <summary>Plain-English level chip + description, delegating to <see cref="OptionsTradingLevel"/>.</summary>
    public static string LevelChip(int level) => OptionsTradingLevel.Short(level);
    public static string LevelDescription(int level) => OptionsTradingLevel.Describe(level);
}

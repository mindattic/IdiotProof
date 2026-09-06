namespace IdiotProof.Shared.Options;

/// <summary>
/// What an Alpaca options approval level actually lets you do, in one place so the ticket, the
/// banner and the tests all agree. Alpaca (<c>options_trading_level</c> on <c>/v2/account</c>):
/// <list type="bullet">
///   <item>0 — options disabled for the account.</item>
///   <item>1 — covered calls and cash-secured puts only (you may WRITE against stock/cash you hold;
///   you may NOT buy a call or a put outright).</item>
///   <item>2 — level 1 plus buying calls and puts (long options).</item>
///   <item>3 — level 2 plus spreads and straddles (multi-leg).</item>
/// </list>
/// Closing a position you already hold is allowed at any level ≥ 1.
/// </summary>
public static class OptionsTradingLevel
{
    public const int Disabled = 0;
    public const int CoveredOnly = 1;
    public const int LongOptions = 2;
    public const int Spreads = 3;

    /// <summary>Plain-English label for the level banner / tooltip.</summary>
    public static string Describe(int level) => level switch
    {
        <= Disabled => "Level 0 — not approved for options. You can browse, but the ticket stays locked.",
        CoveredOnly => "Level 1 — covered calls and cash-secured puts only. You can WRITE (sell to open) against stock or cash you hold, but you can't BUY a call or put outright.",
        LongOptions => "Level 2 — buy calls and puts (plus everything in level 1). This is what the buy-the-idea trade needs.",
        _ => "Level 3 — spreads and straddles (plus everything below). IdiotProof only places single-leg orders today.",
    };

    /// <summary>Short chip text: "Level 2 · long calls/puts".</summary>
    public static string Short(int level) => level switch
    {
        <= Disabled => "Level 0 · not approved",
        CoveredOnly => "Level 1 · covered only",
        LongOptions => "Level 2 · long calls/puts",
        _ => $"Level {level} · spreads",
    };

    public static bool AllowsBuyToOpen(int level) => level >= LongOptions;
    public static bool AllowsSellToOpen(int level) => level >= CoveredOnly;
    public static bool AllowsClosing(int level) => level >= CoveredOnly;

    /// <summary>
    /// Why this account can't send the given Alpaca <c>position_intent</c>, or null when it can.
    /// Written for the ticket's lock banner, so it says what to do instead.
    /// </summary>
    public static string? Blocker(int level, string positionIntent)
    {
        if (level <= Disabled)
            return "This Alpaca account isn't approved for options trading yet (level 0). Switch to Sandbox to practise, or request approval in the Alpaca dashboard.";

        return positionIntent switch
        {
            "buy_to_open" when !AllowsBuyToOpen(level) =>
                "This account is approved at level 1 (covered calls and cash-secured puts only), so it can't buy a call or put outright. Ask Alpaca to raise it to level 2, or practise in Sandbox.",
            "sell_to_open" when !AllowsSellToOpen(level) =>
                "This account can't write options.",
            _ => null,
        };
    }
}

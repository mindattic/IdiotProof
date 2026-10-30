// ============================================================================
// StrategyStamp - a strategy as a scannable, pasteable 72-column card
// ============================================================================
//
// A stamp is to a strategy what a QR card is to a URL: one deterministic
// block of text, generated purely from the canonical JSON (IP-LAW-8), that a
// human can read and a machine can paste back:
//
//   header        symbol · side · size · session · name · short id
//   phase flow    Setup → Filters → Entry → (fork) → Order → Risk → Exit
//   exit ladder   every exit on a price axis, then the time/momentum exits
//   fingerprint   OpenSSH "drunken bishop" randomart of the SHA-256
//   payload       BEGIN/END armor: base64url(deflate(canon)) + CRC-32
//
// Identity is the canon: same StrategyJson → byte-identical stamp; any field
// change → a new IP-XXXX-XXXX id and a visibly different fingerprint. Decode
// reads the armor back out of arbitrary pasted text and fails closed.
// ============================================================================

using System.Globalization;
using IdiotProof.Models;
using IdiotProof.Scripting.Stamp;

namespace IdiotProof.Scripting;

/// <summary>Thrown when pasted text does not carry a valid, intact strategy stamp.</summary>
public sealed class StrategyStampException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Rendering switches for <see cref="StrategyStamp.ToStamp"/>.</summary>
public sealed record StampOptions
{
    /// <summary>Box-drawing characters instead of the default plain ASCII (<c>--unicode</c>).</summary>
    public bool Unicode { get; init; }

    /// <summary>Append the armored, pasteable payload (default on).</summary>
    public bool IncludePayload { get; init; } = true;

    /// <summary>
    /// Builds options from CLI-style switches: <c>--unicode</c>, <c>--ascii</c>,
    /// <c>--no-payload</c>. Unknown arguments are ignored (the caller owns them).
    /// </summary>
    public static StampOptions FromArgs(IEnumerable<string> args)
    {
        var o = new StampOptions();
        foreach (var a in args)
        {
            switch (a.Trim().ToLowerInvariant())
            {
                case "--unicode": o = o with { Unicode = true }; break;
                case "--ascii": o = o with { Unicode = false }; break;
                case "--no-payload": o = o with { IncludePayload = false }; break;
            }
        }
        return o;
    }
}

public static class StrategyStamp
{
    /// <summary>Every stamp line is at most this many columns.</summary>
    public const int Width = StampWriter.Width;

    // ── Identity (canon → hash → id) ────────────────────────────────────

    /// <summary>
    /// The exact JSON a stamp is built from: <see cref="StrategyJson.Serialize"/>
    /// re-emitted compact (platform-independent line endings). The strategy's
    /// random <see cref="StrategyDefinition.Id"/> is not part of it.
    /// </summary>
    public static string CanonicalJson(this StrategyDefinition def) => StampCodec.CanonicalJson(def);

    /// <summary>SHA-256 of the UTF-8 canonical JSON.</summary>
    public static byte[] CanonicalHash(this StrategyDefinition def) => StampCodec.Hash(def);

    /// <summary>
    /// "IP-XXXX-XXXX": the first 8 RFC 4648 base32 characters of the canonical
    /// SHA-256 (40 bits). Same canon → same id, on every machine.
    /// </summary>
    public static string ShortId(this StrategyDefinition def) => ShortId(StampCodec.Hash(def));

    private static string ShortId(byte[] hash)
    {
        var b32 = StampCodec.Base32(hash.AsSpan(0, 5));
        return $"IP-{b32[..4]}-{b32[4..8]}";
    }

    // ── Pieces ──────────────────────────────────────────────────────────

    /// <summary>The 17x9 drunken-bishop fingerprint, framed and titled with the short id.</summary>
    public static string Fingerprint(this StrategyDefinition def, bool unicode = false)
    {
        var hash = StampCodec.Hash(def);
        var art = StampFingerprint.Render(hash, ShortId(hash), unicode ? StampGlyphs.Unicode : StampGlyphs.Ascii);
        return string.Join('\n', art) + "\n";
    }

    /// <summary>
    /// The QR-sized payload: <c>IPS1:</c> + base45(CRC-32 ‖ deflate(canon)).
    /// <see cref="Decode"/> accepts it as-is (e.g. straight from a phone scan).
    /// </summary>
    public static string ToCompactPayload(this StrategyDefinition def) =>
        StampArmor.EncodeCompact(StampCodec.CanonicalBytes(def));

    /// <summary>Just the BEGIN/END armored payload block.</summary>
    public static string ToArmor(this StrategyDefinition def) =>
        string.Join('\n', StampArmor.Encode(StampCodec.CanonicalBytes(def))) + "\n";

    // ── Decode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Recreates a strategy from pasted text. The armor may sit anywhere in
    /// the text — surrounding prose, CRLF, indentation, "&gt; " quoting and
    /// re-wrapped body lines are all tolerated — and a bare <c>IPS1:</c> line
    /// (a scanned stamp QR) is accepted too. Fails closed: missing armor, a
    /// CRC mismatch, bad base64, or JSON the strict reader does not fully
    /// understand → <see cref="StrategyStampException"/>.
    /// </summary>
    public static StrategyDefinition Decode(string text)
    {
        var json = StampArmor.DecodeToJson(text);
        try { return StrategyJson.Deserialize(json); }
        catch (StrategyJsonException ex)
        {
            throw new StrategyStampException($"Strategy stamp payload is not a strategy this build understands: {ex.Message}", ex);
        }
    }

    // ── The card ────────────────────────────────────────────────────────

    /// <summary>
    /// The full stamp: header, six-phase flow (with If/ElseIf/Else forks),
    /// exit ladder, fingerprint and armored payload. Lines are '\n'-terminated,
    /// at most <see cref="Width"/> columns, with no trailing spaces.
    /// </summary>
    public static string ToStamp(this StrategyDefinition def, StampOptions? options = null)
    {
        options ??= new StampOptions();
        var g = options.Unicode ? StampGlyphs.Unicode : StampGlyphs.Ascii;
        var canonical = StampCodec.CanonicalBytes(def);
        var hash = System.Security.Cryptography.SHA256.HashData(canonical);
        var id = ShortId(hash);

        var w = new StampWriter(g);
        Header(w, def, id);
        w.Blank();
        Flow(w, def);
        w.Blank();
        Ladder(w, def);
        w.Blank();
        FingerprintPanel(w, hash, id, canonical.Length);

        if (options.IncludePayload)
        {
            w.Blank();
            foreach (var line in StampArmor.Encode(canonical)) w.Raw(line);
        }
        return w.ToString();
    }

    // ── Header (IP-T128) ────────────────────────────────────────────────

    private static void Header(StampWriter w, StrategyDefinition def, string id)
    {
        var g = w.G;
        var title = g.IsUnicode ? "IDIOTPROOF · STRATEGY STAMP" : "IDIOTPROOF :: STRATEGY STAMP";
        w.Top(title, id, heavy: true);

        var side = def.Direction == TradeDirection.Short
            ? $"{g.Dn} SHORT  (sell to open)"
            : $"{g.Up} LONG  (buy to open)";
        w.Row(Pair(Kv("SYMBOL", g.Clean(def.Symbol)), Kv("SIDE", side)), heavy: true);
        w.Row(Pair(Kv("SIZE", StampText.Size(def)), Kv("SESSION", ShortSession(def.Session))), heavy: true);
        if (!string.IsNullOrWhiteSpace(def.Name))
            w.Row(Kv("NAME", g.Clean(def.Name)), hang: 9, heavy: true);

        // BrokerMode (Paper/Live) is a property of the saved row, not of the
        // canonical strategy, so it is deliberately absent: the same stamp
        // recreates the same strategy in either mode.
        var flags = new List<string>();
        if (def.IsAutonomous) flags.Add("autonomous");
        if (def.IsAdaptive) flags.Add("adaptive order");
        if (def.ShouldRepeat) flags.Add("repeats after exit");
        if (flags.Count > 0) w.Row(Kv("FLAGS", string.Join(", ", flags)), hang: 9, heavy: true);
        w.Bottom(heavy: true);
    }

    private static string Kv(string key, string value) => $"{key,-9}{value}";

    // Two key/value pairs on one row: the right pair starts at a fixed column.
    private static string Pair(string left, string right) =>
        left.Length < 30 ? left.PadRight(30) + right : left + "  " + right;

    private static string ShortSession(TradingSession s) => s switch
    {
        TradingSession.Premarket => "Premarket 04:00-09:30 ET",
        TradingSession.RTH => "RTH 09:30-16:00 ET",
        TradingSession.AfterHours => "After hours 16:00-20:00 ET",
        TradingSession.Extended => "Extended 04:00-20:00 ET",
        _ => s.ToString(),
    };

    // ── Phase flow (IP-T129) + forks (IP-T130) ──────────────────────────

    private static void Flow(StampWriter w, StrategyDefinition def)
    {
        var g = w.G;
        string B(string s) => $"{g.Bullet} {s}";
        var isShort = def.Direction == TradeDirection.Short;

        // 1 Setup
        w.Top("1 SETUP", "where & when");
        w.Row(B($"ticker   {g.Clean(def.Symbol)}"), hang: 11);
        w.Row(B($"session  {StampText.Session(def.Session)}"), hang: 11);
        w.Bottom();
        w.Connector();

        // 2 Filters — conditions declared Filters, plus the daily-candle entry gates.
        var filters = def.EntryConditions.Where(c => c.Phase == StrategyPhase.Filters).Select(StampText.Describe).ToList();
        if (def.EntryRollingLowDays is { } eld)
            filters.Add($"near the {eld}-day low (within {StampText.Pct(def.EntryRollingLowBuffer ?? 2.5)})");
        if (def.EntryRollingHighDays is { } ehd)
            filters.Add($"near the {ehd}-day high (within {StampText.Pct(def.EntryRollingHighBuffer ?? 2.5)})");
        w.Top("2 FILTERS", "always-on gates, all must hold");
        if (filters.Count == 0) w.Row("(none - no regime gate)");
        foreach (var f in filters) w.Row(B(f));
        w.Bottom();
        w.Connector("gates hold");

        // 3 Entry — the trigger.
        var entries = def.EntryConditions
            .Where(c => c.Phase is not StrategyPhase.Filters and not StrategyPhase.Exit)
            .Select(StampText.Describe).ToList();
        w.Top("3 ENTRY", "the trigger, all true to fire");
        if (entries.Count == 0) w.Row("(none - fires as soon as the gates hold)");
        foreach (var e in entries) w.Row(B(e));
        w.Bottom();

        // Forks: each If/ElseIf/Else block, first matching branch wins.
        for (int b = 0; b < def.ConditionalBlocks.Count; b++)
        {
            w.Connector(b == 0 ? "fires" : null);
            Fork(w, def.ConditionalBlocks[b], def.ConditionalBlocks.Count > 1 ? $"{b + 1}" : "");
        }
        w.Connector(def.ConditionalBlocks.Count == 0 ? "fires" : null);

        // 4 Order
        w.Top("4 ORDER", "what gets placed");
        w.Row(B($"{(isShort ? "SELL SHORT" : "BUY")} to open{g.Sep}{StampText.Size(def)}"));
        if (def.IsAdaptive) w.Row(B("adaptive order (price walks toward the market)"));
        w.Row(B("after Gate 2 (LLM voter quorum) and Gate 3 (Risk Guardian)"));
        if (def.ShouldRepeat) w.Row(B("re-arms after each exit (repeat)"));
        w.Bottom();
        w.Connector("filled");

        // 5 Risk
        var risk = new List<string>();
        if (def.StopLossPrice is { } sp) risk.Add($"hard stop at {StampText.Money(sp)}");
        if (def.StopLossPercent is { } sc) risk.Add($"stop {StampText.Pct(sc)} {(isShort ? "above" : "below")} entry");
        if (def.TrailingStopPercent is { } ts) risk.Add($"trailing stop {StampText.Pct(ts)} off the {(isShort ? "low" : "peak")}");
        if (def.RollingLowDays is { } rld)
            risk.Add($"support failure: within {StampText.Pct(def.RollingLowBuffer ?? 2.5)} of the {rld}-day low");
        w.Top("5 RISK", "the Guardian's rails");
        if (risk.Count == 0) w.Row("! no stop - the Risk Guardian would veto this order");
        foreach (var r in risk) w.Row(B(r));
        w.Bottom();
        w.Connector("held");

        // 6 Exit
        var exits = new List<string>();
        foreach (var t in def.TakeProfitTargets)
            exits.Add($"target {t.Label} at {StampText.Money(t.Price)}, {(isShort ? "cover" : "sell")} {t.PercentToSell}%");
        if (def.TakeProfitPrice is { } tp && !def.TakeProfitTargets.Any(t => t.Price == tp))
            exits.Add($"take profit at {StampText.Money(tp)}");
        if (def.TakeProfitPercent is { } tpp) exits.Add($"take profit {StampText.Pct(tpp)} from entry");
        if (def.ExitAtPriorHigh) exits.Add("sell into the prior high of day");
        if (def.RollingHighDays is { } rhd)
            exits.Add($"within {StampText.Pct(def.RollingHighBuffer ?? 2.5)} of the {rhd}-day high");
        if (def.PeakGivebackPercent is { } pg)
            exits.Add($"peak giveback {StampText.Pct(pg)} of the run" +
                      (def.PeakGivebackArmTime is { } arm ? $", armed {StampText.Time(arm)} ET" : ", armed on entry"));
        if (def.ExitTime is { } et) exits.Add($"sell-by {StampText.Time(et)} ET (flatten)");
        exits.AddRange(def.EntryConditions.Where(c => c.Phase == StrategyPhase.Exit).Select(StampText.Describe));
        w.Top("6 EXIT", "first to hit wins");
        if (exits.Count == 0) w.Row("(no target or time exit - stop only)");
        foreach (var x in exits) w.Row(B(x));
        w.Bottom();
    }

    private static void Fork(StampWriter w, ConditionalBlock block, string n)
    {
        var g = w.G;
        w.Top($"FORK{(n.Length > 0 ? " " + n : "")}", "first matching branch wins");
        for (int i = 0; i < block.Branches.Count; i++)
        {
            var br = block.Branches[i];
            var last = i == block.Branches.Count - 1;
            var kind = i == 0 ? "IF" : br.Condition is null ? "ELSE" : "ELSE IF";
            var when = br.Condition is null ? "" : " " + StampText.Describe(br.Condition);
            var arm = $"{(last ? g.BranchLast : g.BranchMid)}{g.BranchArm}{g.BranchArm} ";
            var stem = last ? "    " : $"{g.BranchPipe}   ";

            foreach (var (piece, k) in StampWriter.Wrap(kind + when, StampWriter.Inner - 4, kind.Length + 1).Select((p, k) => (p, k)))
                w.Row((k == 0 ? arm : stem) + piece, hang: 0);
            foreach (var piece in StampWriter.Wrap($"=> {StampText.Overrides(br.Overrides, ", ")}", StampWriter.Inner - 6, 3))
                w.Row(stem + "  " + piece, hang: 0);
        }
        if (block.Branches.Count > 0 && block.Branches[^1].Condition is not null)
        {
            // No Else: nothing matched → the base plan runs unchanged.
            w.Row($"{g.BranchLast}{g.BranchArm}{g.BranchArm} otherwise", hang: 0);
            w.Row("      => base plan unchanged", hang: 0);
        }
        w.Bottom();
    }

    // ── Exit ladder (IP-T131) ───────────────────────────────────────────

    private sealed record Rung(double? Price, string Label, string Level, string Action);

    private static void Ladder(StampWriter w, StrategyDefinition def)
    {
        var g = w.G;
        var isShort = def.Direction == TradeDirection.Short;
        var close = isShort ? "cover" : "sell";

        // Profit side and loss side, each in descending price order so the
        // ladder reads like a price axis: highest rung on top.
        var profit = new List<Rung>();
        foreach (var t in def.TakeProfitTargets)
            profit.Add(new(t.Price, t.Label, StampText.Money(t.Price), $"{close} {t.PercentToSell}%"));
        if (def.TakeProfitPrice is { } tp && !def.TakeProfitTargets.Any(t => t.Price == tp))
            profit.Add(new(tp, "TARGET", StampText.Money(tp), $"{close} all"));
        if (def.TakeProfitPercent is { } tpp)
            profit.Add(new(null, "TARGET", $"{(isShort ? "-" : "+")}{StampText.Pct(tpp)} from entry", $"{close} all"));
        if (def.ExitAtPriorHigh)
            profit.Add(new(null, "PRIOR HOD", "pre-entry high", $"{close} all"));
        if (def.RollingHighDays is { } rhd)
            profit.Add(new(null, $"{rhd}D HIGH", $"within {StampText.Pct(def.RollingHighBuffer ?? 2.5)}", $"{close} all"));

        var loss = new List<Rung>();
        if (def.StopLossPrice is { } sp) loss.Add(new(sp, "STOP", StampText.Money(sp), $"{close} all"));
        if (def.StopLossPercent is { } sc)
            loss.Add(new(null, "STOP", $"{(isShort ? "+" : "-")}{StampText.Pct(sc)} from entry", $"{close} all"));
        if (def.RollingLowDays is { } rld)
            loss.Add(new(null, $"{rld}D LOW", $"within {StampText.Pct(def.RollingLowBuffer ?? 2.5)}", "cut loss"));

        static List<Rung> Desc(List<Rung> rungs) =>
            rungs.Where(r => r.Price is not null).OrderByDescending(r => r.Price)
                 .Concat(rungs.Where(r => r.Price is null)).ToList();

        var above = Desc(isShort ? loss : profit);
        var below = Desc(isShort ? profit : loss);
        if (above.Count == 0) above.Add(new(null, isShort ? "(no stop)" : "(no target)", "", ""));
        if (below.Count == 0) below.Add(new(null, isShort ? "(no target)" : "(no stop)", "", ""));

        w.Top("EXIT LADDER", isShort ? "short: profit below, risk above" : "long: profit above, risk below");
        w.Row(def.HasBranching ? "price   (base plan; a FORK branch may override it)" : "price");
        for (int i = 0; i < above.Count; i++) RungRow(w, i == 0 ? g.Up : g.V, above[i]);

        var cross = g.IsUnicode ? '╪' : '+';
        var entry = $"{new string(g.Entry, 2)}{cross}{new string(g.Entry, 2)} ENTRY  " +
                    $"{(isShort ? "sell short" : "buy")}{g.Sep}{StampText.Size(def)} ";
        w.Row(entry + new string(g.Entry, Math.Max(0, StampWriter.Inner - entry.Length)), hang: 0);

        for (int i = 0; i < below.Count; i++) RungRow(w, i == below.Count - 1 ? g.Dn : g.V, below[i]);

        var timed = new List<Rung>();
        if (def.TrailingStopPercent is { } ts)
            timed.Add(new(null, "TRAIL", $"{StampText.Pct(ts)} off {(isShort ? "low" : "peak")}", $"{close} all"));
        if (def.PeakGivebackPercent is { } pg)
            timed.Add(new(null, "GIVEBACK", $"{StampText.Pct(pg)} of run",
                def.PeakGivebackArmTime is { } arm ? $"armed {StampText.Time(arm)} ET" : "armed on entry"));
        if (def.ExitTime is { } et)
            timed.Add(new(null, "SELL-BY", $"{StampText.Time(et)} ET", "flatten"));

        w.Divider();
        w.Row("moving & timed exits");
        if (timed.Count == 0) w.Row("   (none)");
        foreach (var r in timed) RungRow(w, r.Label == "SELL-BY" ? '@' : '~', r);
        w.Bottom();
    }

    private static void RungRow(StampWriter w, char axis, Rung r)
    {
        var text = r.Level.Length == 0
            ? $"  {axis}  {r.Label}"
            : $"  {axis}  {Leader(r.Label, 13, w.G.Dot)}{Leader(r.Level, 22, w.G.Dot)}{r.Action}";
        w.Row(text, hang: 0);
    }

    private static string Leader(string text, int width, char dot) =>
        text.Length + 2 >= width ? text + " " : text + " " + new string(dot, width - text.Length - 2) + " ";

    // ── Fingerprint panel (IP-T132) ─────────────────────────────────────

    private static void FingerprintPanel(StampWriter w, byte[] hash, string id, int canonBytes)
    {
        var art = StampFingerprint.Render(hash, id, w.G);
        var hex = Convert.ToHexStringLower(hash);
        string Hex(int row) => string.Join(' ', Enumerable.Range(0, 4).Select(k => hex.Substring(row * 16 + k * 4, 4)));

        var info = new[]
        {
            $"FINGERPRINT  {id}",
            "",
            $"sha256  {Hex(0)}",
            $"        {Hex(1)}",
            $"        {Hex(2)}",
            $"        {Hex(3)}",
            "",
            $"canon   StrategyJson v{StrategyJson.CurrentSchemaVersion}{w.G.Sep}{canonBytes.ToString("#,0", CultureInfo.InvariantCulture)} bytes",
            "Same strategy, same picture: change any field",
            "and the art changes. Paste this whole stamp",
            "back into IdiotProof to recreate the strategy.",
        };
        for (int i = 0; i < art.Count; i++)
            w.Raw(i < info.Length && info[i].Length > 0 ? $"{art[i]}    {info[i]}" : art[i]);
    }
}

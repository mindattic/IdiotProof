using static IdiotProof.Scripting.ScriptDiagnostics;

namespace IdiotProof.Scripting;

/// <summary>One <c>Verb(args)</c> in a call chain: its name, the raw text between its parentheses,
/// and the token range of its arguments (ArgsFrom..ArgsTo exclusive; empty when it has none).</summary>
internal readonly record struct ScriptCall(string Name, string RawArgs, int NameToken, int ArgsFrom, int ArgsTo);

/// <summary>
/// The token-level view of an IdiotScript that the parser reads call chains from. Top-level calls are
/// the dotted <c>.Verb(args)</c> at parenthesis depth 0 (what the old regex matched); argument
/// expressions such as <c>IsAboveVwap().And(IsEmaAbove(9))</c> or <c>b =&gt; b.Long().TakeProfit(5)</c>
/// are read as chains of their own.
/// </summary>
internal sealed class ScriptChain
{
    private readonly string text;
    internal List<Token> Tokens { get; }

    public ScriptChain(string text)
    {
        this.text = text;
        Tokens = Tokenize(text, new List<ScriptDiagnostic>(), LineStarts(text));
    }

    public IEnumerable<ScriptCall> TopLevelCalls()
    {
        for (int i = 0; i < Tokens.Count; i++)
        {
            var t = Tokens[i];
            if (t.Kind == TokenKind.LParen)
            {
                int close = MatchParen(i);
                if (close >= 0) i = close;
                continue;
            }
            if (t.Kind != TokenKind.Dot || i + 1 >= Tokens.Count || Tokens[i + 1].Kind != TokenKind.Identifier) continue;
            var call = ReadCall(i + 1, out int last);
            i = last;
            yield return call;
        }
    }

    /// <summary>The calls of the chain inside [from, to): <c>A().B(x).C</c>, after any lambda prefix.</summary>
    public List<ScriptCall> CallsIn(int from, int to)
    {
        from = SkipLambdaPrefix(from, to);
        var calls = new List<ScriptCall>();
        for (int i = from; i < to; i++)
        {
            var t = Tokens[i];
            if (t.Kind == TokenKind.LParen)
            {
                int close = MatchParen(i);
                if (close < 0 || close >= to) break;
                i = close;
                continue;
            }
            if (t.Kind != TokenKind.Identifier) continue;
            // Receivers such as Conditions.IsAboveVwap or b.Long(): the verb is the name after the dot.
            if (i + 1 < to && Tokens[i + 1].Kind == TokenKind.Dot && IsReceiver(t.Text)) continue;
            var call = ReadCall(i, out int last);
            if (last >= to) last = to - 1;
            i = last;
            calls.Add(call);
        }
        return calls;
    }

    /// <summary>Splits [from, to) at its depth-0 commas.</summary>
    public List<(int From, int To)> SplitArguments(int from, int to)
    {
        var parts = new List<(int, int)>();
        int depth = 0, start = from;
        for (int i = from; i < to; i++)
        {
            var k = Tokens[i].Kind;
            if (k == TokenKind.LParen) depth++;
            else if (k == TokenKind.RParen) depth--;
            else if (k == TokenKind.Comma && depth == 0) { parts.Add((start, i)); start = i + 1; }
        }
        parts.Add((start, to));
        return parts;
    }

    public bool IsEmpty(int from, int to) => from >= to;

    private ScriptCall ReadCall(int nameToken, out int lastToken)
    {
        var name = Tokens[nameToken].Text;
        lastToken = nameToken;
        if (nameToken + 1 < Tokens.Count && Tokens[nameToken + 1].Kind == TokenKind.LParen)
        {
            int open = nameToken + 1;
            int close = MatchParen(open);
            if (close >= 0)
            {
                lastToken = close;
                var raw = text[(Tokens[open].Start + 1)..Tokens[close].Start];
                return new ScriptCall(name, raw, nameToken, open + 1, close);
            }
        }
        return new ScriptCall(name, "", nameToken, nameToken + 1, nameToken + 1);
    }

    private int MatchParen(int open)
    {
        int depth = 0;
        for (int i = open; i < Tokens.Count; i++)
        {
            if (Tokens[i].Kind == TokenKind.LParen) depth++;
            else if (Tokens[i].Kind == TokenKind.RParen && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Skips <c>c =&gt;</c> and the lambda parameter's own receiver (<c>c.</c>).</summary>
    private int SkipLambdaPrefix(int from, int to)
    {
        if (from + 2 < to && Tokens[from].Kind == TokenKind.Identifier
            && Tokens[from + 1].Text == "=" && Tokens[from + 2].Text == ">")
        {
            var param = Tokens[from].Text;
            from += 3;
            if (from + 1 < to && Tokens[from].Kind == TokenKind.Identifier && Tokens[from].Text == param
                && Tokens[from + 1].Kind == TokenKind.Dot)
                from += 2;
        }
        return from;
    }

    private static bool IsReceiver(string name) =>
        name is "Conditions" or "Stock" or "IS" or "IdiotProof" or "Scripting" || name.Length == 1;

    // ---- expression readers ----

    /// <summary>
    /// Reads a condition expression such as <c>IsAboveVwap().And(IsEmaAbove(9)).Not()</c>. Returns null
    /// when any part of it isn't a condition verb: a branch whose condition can't be read must be
    /// skipped, never treated as "always true".
    /// </summary>
    public ICondition? ReadCondition(int from, int to)
    {
        ICondition? current = null;
        foreach (var call in CallsIn(from, to))
        {
            switch (call.Name.ToLowerInvariant())
            {
                case "and":
                case "or":
                {
                    var other = ReadCondition(call.ArgsFrom, call.ArgsTo);
                    if (current is null || other is null) return null;
                    current = call.Name.Equals("and", StringComparison.OrdinalIgnoreCase)
                        ? new AndCondition(current, other)
                        : new OrCondition(current, other);
                    break;
                }
                case "not":
                    if (current is null) return null;
                    current = new NotCondition(current);
                    break;
                default:
                    var leaf = Leaf(call);
                    if (leaf is null) return null;
                    current = current is null ? leaf : new AndCondition(current, leaf);
                    break;
            }
        }
        return current;
    }

    /// <summary>
    /// Reads a branch body such as <c>Short().TakeProfit(3).StopLoss(9.5).IsBelowVwap()</c> into overrides.
    /// Condition verbs become the branch's extra entry conditions; .And/.Or/.Not compose with the one before.
    /// </summary>
    public StrategyOverrides ReadOverrides(int from, int to)
    {
        var bb = new BranchBuilder();
        var o = bb.Overrides;
        foreach (var call in CallsIn(from, to))
        {
            var nums = ScriptParser.ParseNumericArgs(call.RawArgs);
            var strs = ScriptParser.ParseStringArgs(call.RawArgs);
            switch (call.Name.ToLowerInvariant())
            {
                case "long": bb.Long(); break;
                case "short": bb.Short(); break;
                case "stoploss": if (nums.Count >= 1) bb.StopLoss((double)nums[0]); break;
                case "stoplosspercent": if (nums.Count >= 1) bb.StopLossPercent((double)nums[0]); break;
                case "trailingstoploss": if (nums.Count >= 1) bb.TrailingStopLoss((double)nums[0]); break;
                case "takeprofit":
                    if (nums.Count >= 3) bb.TakeProfit((double)nums[0], (double)nums[1], (double)nums[2]);
                    else if (nums.Count == 2) bb.TakeProfit((double)nums[0], (double)nums[1]);
                    else if (nums.Count == 1) bb.TakeProfit((double)nums[0]);
                    break;
                case "addtarget":
                    if (nums.Count >= 2)
                        o.TakeProfitTargets.Add(new TakeProfitTarget
                        {
                            Price = (double)nums[0],
                            PercentToSell = (int)nums[1],
                            Label = strs.Count >= 3 && strs[2].Length > 0 ? strs[2] : $"T{o.TakeProfitTargets.Count + 1}",
                        });
                    break;
                case "and":
                case "or":
                {
                    var other = ReadCondition(call.ArgsFrom, call.ArgsTo);
                    if (o.EntryConditions.Count == 0 || other is null) break;
                    var last = o.EntryConditions[^1];
                    o.EntryConditions[^1] = call.Name.Equals("and", StringComparison.OrdinalIgnoreCase)
                        ? new AndCondition(last, other)
                        : new OrCondition(last, other);
                    break;
                }
                case "not":
                    if (o.EntryConditions.Count > 0) o.EntryConditions[^1] = new NotCondition(o.EntryConditions[^1]);
                    break;
                default:
                    if (Leaf(call) is { } cond) o.EntryConditions.Add(cond);
                    break;
            }
        }
        return o;
    }

    /// <summary>A single condition verb, built by the same builder case the parser uses for the base strategy.</summary>
    private static ICondition? Leaf(ScriptCall call)
    {
        try
        {
            var scratch = Stock.Ticker("X");
            if (!ScriptParser.ApplyVerb(scratch, call.Name, call.RawArgs)) return null;
            var def = scratch.Build();
            return def.EntryConditions.Count == 1 ? def.EntryConditions[0] : null;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// Reads If/Then/ElseIf/Else/EndIf and top-level .And/.Or/.Not while the parser walks a script.
/// Two forms are accepted:
/// <list type="bullet">
/// <item>Explicit, what the serializer emits: <c>.If(cond).Then(body).ElseIf(cond, body).Else(body).EndIf()</c>.</item>
/// <item>Builder form: <c>.IsAboveVwap().Then(body).ElseIf(...).Else(...)</c>, where the condition just before
/// Then() is the "if" (with or without <c>b =&gt;</c> lambdas). It ends at Else() or the next ordinary verb.</item>
/// </list>
/// </summary>
internal sealed class BlockReader(StrategyBuilder builder, ScriptChain chain)
{
    private ConditionalBlock? open;
    private bool explicitBlock;
    private bool awaitingThen;
    private ICondition? pendingIf;

    public bool TryApply(ScriptCall call)
    {
        switch (call.Name.ToLowerInvariant())
        {
            case "if":
                EndLegacyBlock();
                open = new ConditionalBlock();
                builder.AddConditionalBlock(open);
                explicitBlock = true;
                awaitingThen = !chain.IsEmpty(call.ArgsFrom, call.ArgsTo);
                pendingIf = awaitingThen ? chain.ReadCondition(call.ArgsFrom, call.ArgsTo) : null;
                return true;

            case "then":
            {
                var body = chain.ReadOverrides(call.ArgsFrom, call.ArgsTo);
                if (open is not null && explicitBlock)
                {
                    // An unreadable If condition skips its branch: it must never become an unconditional one.
                    if (awaitingThen && pendingIf is not null)
                        open.Branches.Add(new ConditionalBranch { Condition = pendingIf, Overrides = body });
                    awaitingThen = false;
                    pendingIf = null;
                    return true;
                }
                EndLegacyBlock();
                if (builder.PopLastEntryCondition() is { } cond)
                {
                    open = new ConditionalBlock();
                    builder.AddConditionalBlock(open);
                    explicitBlock = false;
                    open.Branches.Add(new ConditionalBranch { Condition = cond, Overrides = body });
                }
                return true;
            }

            case "elseif":
            {
                if (open is null) return true;
                var args = chain.SplitArguments(call.ArgsFrom, call.ArgsTo);
                if (args.Count < 2) return true;
                var cond = chain.ReadCondition(args[0].From, args[0].To);
                if (cond is not null)
                    open.Branches.Add(new ConditionalBranch { Condition = cond, Overrides = chain.ReadOverrides(args[1].From, args[1].To) });
                awaitingThen = false;
                return true;
            }

            case "else":
                if (open is null) return true;
                open.Branches.Add(new ConditionalBranch { Condition = null, Overrides = chain.ReadOverrides(call.ArgsFrom, call.ArgsTo) });
                awaitingThen = false;
                if (!explicitBlock) Close();
                return true;

            case "endif":
                Close();
                return true;

            case "and":
            case "or":
            {
                var other = chain.ReadCondition(call.ArgsFrom, call.ArgsTo);
                if (other is not null)
                {
                    bool and = call.Name.Equals("and", StringComparison.OrdinalIgnoreCase);
                    builder.ComposeLastEntryCondition(last => and ? new AndCondition(last, other) : new OrCondition(last, other));
                }
                return true;
            }

            case "not":
                builder.ComposeLastEntryCondition(last => new NotCondition(last));
                return true;

            default:
                return false;
        }
    }

    /// <summary>A builder-form block ends at the next ordinary verb; an explicit one only at EndIf().</summary>
    public void EndLegacyBlock()
    {
        if (open is not null && !explicitBlock) Close();
    }

    /// <summary>Closes any open block, explicit or not: a Step() boundary ends everything before it.</summary>
    public void CloseAll() => Close();

    private void Close()
    {
        open = null;
        explicitBlock = false;
        awaitingThen = false;
        pendingIf = null;
    }
}

/// <summary>
/// Reads the step verbs of a sequenced strategy while the parser walks a script: <c>Step("name")</c>
/// switches the builder to the next step (closing any open If/While first), <c>ThenWhen(cond)</c>
/// reads its condition with the same reader <c>If(cond)</c> uses, and <c>Within(n)</c> reads a day count.
/// </summary>
internal sealed class StepReader(StrategyBuilder builder, ScriptChain chain, BlockReader blocks)
{
    public bool TryApply(ScriptCall call)
    {
        switch (call.Name.ToLowerInvariant())
        {
            case "step":
                blocks.CloseAll();
                builder.Step(call.RawArgs.Trim().Trim('"').Trim());
                return true;

            case "thenwhen":
                blocks.EndLegacyBlock();
                if (chain.ReadCondition(call.ArgsFrom, call.ArgsTo) is { } transition)
                    builder.ThenWhen(transition);
                return true;

            case "within":
            {
                blocks.EndLegacyBlock();
                var nums = ScriptParser.ParseNumericArgs(call.RawArgs);
                if (nums.Count >= 1 && nums[0] >= 1) builder.Within((int)nums[0]);
                return true;
            }

            default:
                return false;
        }
    }
}

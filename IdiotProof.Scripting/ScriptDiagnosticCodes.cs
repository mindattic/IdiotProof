using System.Reflection;

namespace IdiotProof.Scripting;

/// <summary>
/// What one IdiotScript diagnostic code means (IP-T526): its severity, a short title, the problem
/// it reports, and whether a diagnostic with this code can carry a <see cref="ScriptFix"/>. When it
/// can, <see cref="Fix"/> says what the fix does and when it is offered; when it cannot,
/// <see cref="NoFix"/> says why the edit would be a guess. Only <see cref="ScriptDiagnosticCodes"/>
/// creates these, and <see cref="ScriptDiagnostics"/> can report a problem only through one, so the
/// published catalog and the analyzer cannot disagree about a code or its severity.
/// </summary>
public sealed record ScriptDiagnosticDescriptor
{
    internal ScriptDiagnosticDescriptor(string code, DiagnosticSeverity severity, string title, string meaning, string? fix = null, string? noFix = null)
    {
        Code = code;
        Severity = severity;
        Title = title;
        Meaning = meaning;
        Fix = fix;
        NoFix = noFix;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Title { get; }

    public string Meaning { get; }

    /// <summary>True when a diagnostic with this code can carry a fix (it still carries one only when a single correct edit exists).</summary>
    public bool CanFix => Fix is not null;

    /// <summary>What the fix does and when it is offered; null for a code that never carries one.</summary>
    public string? Fix { get; }

    /// <summary>Why this code never carries a fix; null for a code that can.</summary>
    public string? NoFix { get; }
}

/// <summary>
/// Every diagnostic code <see cref="ScriptDiagnostics"/> can report, one field per code (IP-T526).
/// <see cref="All"/> is reflected from these fields, so a code added here is in the table, and
/// <c>dsl diagnostics</c>, <c>docs dsl</c> and the agent catalog publish it without another list
/// to keep in step (IP-LAW-4).
/// </summary>
public static class ScriptDiagnosticCodes
{
    private const DiagnosticSeverity Error = DiagnosticSeverity.Error;
    private const DiagnosticSeverity Warning = DiagnosticSeverity.Warning;

    public static readonly ScriptDiagnosticDescriptor IPS001 = new("IPS001", Error, "Unknown verb",
        "The name is not a verb the parser knows. The parser skips it, so that part of the strategy would silently vanish.",
        fix: "Replaces the name with the closest known verb. Offered only when exactly one verb is within the edit limit (1 edit for names up to 6 letters, 2 up to 12, 3 beyond); two equally close verbs get a suggestion but no fix.");

    public static readonly ScriptDiagnosticDescriptor IPS002 = new("IPS002", Error, "Unclosed '('",
        "A '(' is never closed.",
        fix: "Inserts the missing ')' after the last argument. Offered only when the arguments are plain numbers, closed strings and commas and what follows is the next .Verb or the end of the script; a name or a nested call among them leaves more than one place it could go.");

    public static readonly ScriptDiagnosticDescriptor IPS003 = new("IPS003", Error, "Unexpected ')'",
        "A ')' has no matching '('.",
        fix: "Removes the ')'. Offered only when it comes right after another ')'; anywhere else a '(' may be what is missing.");

    public static readonly ScriptDiagnosticDescriptor IPS004 = new("IPS004", Error, "Unterminated string",
        "A string is never closed with '\"'; it swallows the rest of its line.",
        fix: "Inserts the closing '\"'. Offered only when the string is an argument (it follows '(' or ',') and what it swallowed is a plain value (letters, digits, space, _ : . / -) ending at the first ')' followed by the next .Verb or nothing, or at the end of the line.");

    public static readonly ScriptDiagnosticDescriptor IPS005 = new("IPS005", Error, "Missing Ticker",
        "The script never names its symbol with Ticker(\"SYM\"); without it nothing parses.",
        noFix: "Only the author knows which symbol was meant.");

    public static readonly ScriptDiagnosticDescriptor IPS006 = new("IPS006", Error, "Argument is not a number",
        "A verb argument that must be a number is something else; the parser ignores it and the verb misreads its arguments.",
        fix: "Writes the number bare. Offered only for a number in quotes or with a '$' (\"2\", $5); a word such as 'nine' has no single reading.");

    public static readonly ScriptDiagnosticDescriptor IPS007 = new("IPS007", Error, "Missing required argument",
        "A verb got fewer arguments than it requires; without them the verb is skipped.",
        noFix: "The missing value is the author's to choose; every verb with a default already takes it as optional.");

    public static readonly ScriptDiagnosticDescriptor IPS008 = new("IPS008", Warning, "Extra arguments ignored",
        "A verb got more arguments than it reads; the rest are ignored.",
        fix: "Removes the arguments the verb ignores, with the comma before them.");

    public static readonly ScriptDiagnosticDescriptor IPS009 = new("IPS009", Error, "Whole number required",
        "An argument that must be a whole number has a fraction; the parser would cut it off.",
        noFix: "Rounding up or down is the author's choice.");

    public static readonly ScriptDiagnosticDescriptor IPS010 = new("IPS010", Warning, "Verb takes no arguments",
        "A verb that takes no arguments was given some; they are ignored.",
        fix: "Removes the ignored arguments. Offered only when they are plain values; a nested call (While(IsAboveVwap())) was meant as something else.");

    public static readonly ScriptDiagnosticDescriptor IPS011 = new("IPS011", Error, "Branch verb without an open If",
        "ElseIf(...), Else(...) or EndIf() has no If(...)/Then(...) before it, so the parser skips it.",
        noFix: "How the block was meant to be built is not determinable.");

    public static readonly ScriptDiagnosticDescriptor IPS012 = new("IPS012", Error, "Branch after Else can never run",
        "An ElseIf(...) or a second Else(...) follows Else(...), which already catches every remaining case.",
        noFix: "Which branch should come first is the author's decision.");

    public static readonly ScriptDiagnosticDescriptor IPS013 = new("IPS013", Error, "If is never closed",
        "An If(...) block is never closed with .EndIf().",
        fix: "Inserts the missing .EndIf(): before the next .If(...) when one starts while the block is still open, otherwise at the end of the chain (before a trailing .Build()). The parser reads the block the same either way, so the edit is exact.");

    public static readonly ScriptDiagnosticDescriptor IPS014 = new("IPS014", Error, "If without a usable condition or Then",
        "If() has no condition, or If(...) is not followed directly by .Then(...), so its branch can never be chosen.",
        noFix: "The condition or the branch body has to be written by the author.");

    public static readonly ScriptDiagnosticDescriptor IPS015 = new("IPS015", Error, "Second Then in one If block",
        "A second Then(...) in the same If block is ignored.",
        noFix: "Whether it was meant as an ElseIf (and with which condition) or as part of the first branch is not determinable.");

    public static readonly ScriptDiagnosticDescriptor IPS016 = new("IPS016", Error, "ElseIf needs a condition and a body",
        "ElseIf(...) needs a condition and a branch body: .ElseIf(IsBelowVwap(), Short().StopLoss(9)).",
        noFix: "The missing condition or body has to be written by the author.");

    public static readonly ScriptDiagnosticDescriptor IPS020 = new("IPS020", Error, "ThenWhen has no readable condition",
        "ThenWhen(...) needs a condition the parser can read; without one the transition to the next step is dropped.",
        noFix: "The transition condition has to be written by the author.");

    public static readonly ScriptDiagnosticDescriptor IPS021 = new("IPS021", Error, "Step inside a block",
        "Step(...) sits inside an If(...) or While() block that is still open.",
        noFix: "Where the block was meant to end is not determinable.");

    public static readonly ScriptDiagnosticDescriptor IPS022 = new("IPS022", Warning, "Repeat in a later step",
        "Repeat() applies to the whole sequence, not one step; the parser treats it as if it were in the first step.",
        noFix: "Moving the verb would change how the script reads without changing what it does; the author decides.");

    public static readonly ScriptDiagnosticDescriptor IPS023 = new("IPS023", Error, "Duplicate step name",
        "Two steps of a sequence have the same name; every step needs its own.",
        noFix: "The new name is the author's to choose.");

    public static readonly ScriptDiagnosticDescriptor IPS024 = new("IPS024", Error, "Step has no direction",
        "A later step has no Long() or Short(), so it would never place an order.",
        noFix: "Long or short is the author's decision.");

    public static readonly ScriptDiagnosticDescriptor IPS025 = new("IPS025", Error, "Transition without a following step",
        "ThenWhen(...)/Within(...) has no following Step(...): the last step has nothing to move on to, so the transition would be ignored.",
        noFix: "Either the transition is removed or a step is added; the author decides.");

    public static readonly ScriptDiagnosticDescriptor IPS026 = new("IPS026", Error, "Route names no step",
        "OnStop/OnTarget/OnExit needs the name of a step of this strategy, or \"end\"; a step itself cannot be named \"end\".",
        noFix: "Which step was meant is the author's to say.");

    public static readonly ScriptDiagnosticDescriptor IPS027 = new("IPS027", Error, "Route points backwards",
        "OnStop/OnTarget/OnExit may only go to a later step or \"end\", so a sequence always finishes; Repeat runs it again.",
        noFix: "Whether the steps are reordered or the route is dropped is the author's decision.");

    public static readonly ScriptDiagnosticDescriptor IPS028 = new("IPS028", Warning, "Step can never be reached",
        "No exit of an earlier step leads to this step: every earlier step routes all of its exits elsewhere.",
        noFix: "Either a route is added to it or the step is removed; the author decides.");

    /// <summary>Every code, in code order, reflected from the fields above.</summary>
    public static IReadOnlyList<ScriptDiagnosticDescriptor> All { get; } =
    [
        .. typeof(ScriptDiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ScriptDiagnosticDescriptor))
            .Select(f => (ScriptDiagnosticDescriptor)f.GetValue(null)!)
            .OrderBy(d => d.Code, StringComparer.Ordinal),
    ];

    /// <summary>The descriptor for a code (case-insensitive), or null when there is no such code.</summary>
    public static ScriptDiagnosticDescriptor? Find(string? code) =>
        All.FirstOrDefault(d => d.Code.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));
}

using System.ComponentModel;
using System.Text.Json.Nodes;

namespace IdiotProof.Operations;

/// <summary>
/// The values of <see cref="CallChange.Kind"/>, with what each one means. The binder and the
/// dispatcher write them from here and the agent catalog publishes this table (IP-T526).
/// </summary>
public static class CallChangeKinds
{
    public const string Operation = "operation";
    public const string Option = "option";
    public const string Argument = "argument";
    public const string Value = "value";

    /// <summary>Every kind with its meaning, in the order a caller is likely to meet them.</summary>
    public static IReadOnlyList<(string Kind, string Meaning)> All { get; } =
    [
        (Operation, "The operation name was misspelled: 'from' is what was sent, 'to' the operation that was meant. 'parameter' is null."),
        (Option, "An option or JSON key name was misspelled: 'from' is what was sent, 'to' the real name (--name on the CLI, the snake_case key in JSON). 'parameter' is the JSON key."),
        (Argument, "A positional argument was passed as an option (--ticker AAPL): 'from' is the option that was sent, 'to' the argument as <name>. Pass the value without the option name."),
        (Value, "A value was written in a form its type does not accept but that has exactly one reading: 'from' is what was sent, 'to' the accepted spelling."),
    ];
}

/// <summary>
/// One correction to a call (IP-T523). <see cref="Kind"/> says what was corrected:
/// <c>operation</c> (the operation name), <c>option</c> (an option or JSON key name),
/// <c>argument</c> (a positional argument that was passed as an option) or <c>value</c> (a value
/// rewritten to the form its type accepts). <see cref="From"/> is what the caller sent and
/// <see cref="To"/> what it should have been; <see cref="Parameter"/> names the parameter, when
/// there is one.
/// </summary>
[Description("One correction to a call.")]
public sealed record CallChange(
    [property: Description("What was corrected: operation, option, argument or value (see change_kinds).")] string Kind,
    [property: Description("The JSON key of the parameter the change is about; null for the operation name.")] string? Parameter,
    [property: Description("What the caller sent.")] string? From,
    [property: Description("What it should have been.")] string? To);

/// <summary>
/// The corrected call for a usage error (IP-T523), in every form a caller can resend:
/// <see cref="CommandLine"/> for a shell, <see cref="Argv"/> for the Hub's <c>argv</c> request and
/// <see cref="Args"/> for an MCP tool call or the Hub's <c>args</c> object (snake_case keys), plus
/// the <see cref="Changes"/> that turn the failed call into it. It exists only when every problem
/// in the call had one determinable correction and the corrected call was bound again without
/// error; it is never run for the caller. <see cref="CommandLine"/> and <see cref="Argv"/> are null
/// when the values cannot be written as CLI tokens (a list item containing a comma).
/// </summary>
[Description("The whole corrected call, present only when every problem had exactly one correction and the corrected call bound again without error. It was not run: resend it to run it.")]
public sealed record CallFix(
    [property: Description("The operation to call (\"noun verb\").")] string Operation,
    [property: Description("The corrected call as one shell command line; null when the values cannot be written as CLI tokens.")] string? CommandLine,
    [property: Description("The corrected call as tokens after the program name (the Hub's argv request); null when command_line is.")] IReadOnlyList<string>? Argv,
    [property: Description("The corrected arguments as the JSON object an MCP tool call or the Hub's args request takes (snake_case keys).")] JsonObject Args,
    [property: Description("The corrections that turn the failed call into this one, in the order they were found.")] IReadOnlyList<CallChange> Changes);

/// <summary>
/// One usage problem: its message, the parameter it is about (JSON name; null for the operation
/// name or the call as a whole) and the correction for just this problem, when one is determinable.
/// </summary>
[Description("One usage problem.")]
public sealed record BindError(
    [property: Description("What is wrong, naming the parameter and the accepted values.")] string Message,
    [property: Description("The JSON key of the parameter the problem is about; null for the operation name or the call as a whole.")] string? Parameter = null,
    [property: Description("The correction for just this problem; null when there is none or more than one (a missing value, an ambiguous number or date, two equally close names).")] CallChange? Fix = null);

/// <summary>
/// The <see cref="OperationResult.Data"/> of a usage error raised before an operation runs (unknown
/// operation, unknown option, missing or unreadable argument): every problem, the corrected call
/// when there is one, and the usage synopsis. Both the CLI (<c>--json</c>) and MCP return it.
/// </summary>
[Description("The data of a usage error raised before an operation runs: unknown operation, unknown option, missing or unreadable argument.")]
public sealed record UsageErrorData(
    [property: Description("The operation the call resolved to; null when the operation name itself was not recognized.")] string? Operation,
    [property: Description("Every problem found in the call.")] IReadOnlyList<BindError> Errors,
    [property: Description("The whole corrected call, or null when at least one problem has no single correction.")] CallFix? Fix,
    [property: Description("The usage synopsis of the operation, when it is known.")] string? Usage);

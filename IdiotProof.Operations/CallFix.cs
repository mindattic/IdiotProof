using System.Text.Json.Nodes;

namespace IdiotProof.Operations;

/// <summary>
/// One correction to a call (IP-T523). <see cref="Kind"/> says what was corrected:
/// <c>operation</c> (the operation name), <c>option</c> (an option or JSON key name),
/// <c>argument</c> (a positional argument that was passed as an option) or <c>value</c> (a value
/// rewritten to the form its type accepts). <see cref="From"/> is what the caller sent and
/// <see cref="To"/> what it should have been; <see cref="Parameter"/> names the parameter, when
/// there is one.
/// </summary>
public sealed record CallChange(string Kind, string? Parameter, string? From, string? To);

/// <summary>
/// The corrected call for a usage error (IP-T523), in every form a caller can resend:
/// <see cref="CommandLine"/> for a shell, <see cref="Argv"/> for the Hub's <c>argv</c> request and
/// <see cref="Args"/> for an MCP tool call or the Hub's <c>args</c> object (snake_case keys), plus
/// the <see cref="Changes"/> that turn the failed call into it. It exists only when every problem
/// in the call had one determinable correction and the corrected call was bound again without
/// error; it is never run for the caller. <see cref="CommandLine"/> and <see cref="Argv"/> are null
/// when the values cannot be written as CLI tokens (a list item containing a comma).
/// </summary>
public sealed record CallFix(
    string Operation, string? CommandLine, IReadOnlyList<string>? Argv, JsonObject Args, IReadOnlyList<CallChange> Changes);

/// <summary>
/// One usage problem: its message, the parameter it is about (JSON name; null for the operation
/// name or the call as a whole) and the correction for just this problem, when one is determinable.
/// </summary>
public sealed record BindError(string Message, string? Parameter = null, CallChange? Fix = null);

/// <summary>
/// The <see cref="OperationResult.Data"/> of a usage error raised before an operation runs (unknown
/// operation, unknown option, missing or unreadable argument): every problem, the corrected call
/// when there is one, and the usage synopsis. Both the CLI (<c>--json</c>) and MCP return it.
/// </summary>
public sealed record UsageErrorData(string? Operation, IReadOnlyList<BindError> Errors, CallFix? Fix, string? Usage);

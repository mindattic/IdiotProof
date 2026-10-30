using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>
/// The <c>idiotproof</c> exit-code contract (IP-T61). Scripts and agents branch on these, so they
/// never change meaning. They are the numeric values of <see cref="OperationStatus"/>, so a Hub
/// result passes straight through; the CLI's own failures (Hub down, bad key, timeout) use
/// <see cref="Failed"/> and its own argv problems use <see cref="Usage"/>.
/// </summary>
public static class CliExitCodes
{
    /// <summary>The command did what it was asked.</summary>
    public const int Ok = 0;

    /// <summary>The command failed — including "the Hub is not running", a rejected key, a timeout or Ctrl+C.</summary>
    public const int Failed = 1;

    /// <summary>The command line was wrong: unknown command, bad flag or value. Nothing ran.</summary>
    public const int Usage = 2;

    /// <summary>A safety gate (risk guardian, market hours, Live elevation) refused the action. Nothing ran.</summary>
    public const int Blocked = 3;

    /// <summary>
    /// Nothing ran yet: the output is a preview plus a code; re-run the same command with
    /// <c>--confirm &lt;CODE&gt;</c> (only on a person's explicit go-ahead for Live money).
    /// </summary>
    public const int ConfirmationRequired = 4;

    /// <summary>The exit code for an operation status (the enum's numeric value).</summary>
    public static int From(OperationStatus status) => (int)status;

    /// <summary>One line per code, as printed in help and <c>agent bootstrap</c>.</summary>
    public static IReadOnlyList<(int Code, string Meaning)> All { get; } =
    [
        (Ok, "ok"),
        (Failed, "failed (includes Hub not running, key rejected, timeout, Ctrl+C)"),
        (Usage, "usage error: unknown command, bad flag or value; nothing ran"),
        (Blocked, "blocked by a safety gate; nothing ran"),
        (ConfirmationRequired, "confirmation required: re-run with --confirm <CODE>"),
    ];

    /// <summary>Short meaning of an exit code (unknown codes say so).</summary>
    public static string Describe(int code) =>
        All.FirstOrDefault(x => x.Code == code).Meaning ?? $"unknown exit code {code}";
}

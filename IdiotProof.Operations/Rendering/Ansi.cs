namespace IdiotProof.Operations.Rendering;

/// <summary>
/// ANSI styling that is a no-op unless color is enabled — and color is enabled only when the CLI
/// says stdout is a terminal (<see cref="OperationContext.Color"/>). JSON, MCP, logs and pipes
/// never see an escape code.
/// </summary>
public readonly record struct Ansi(bool Enabled)
{
    public static Ansi For(OperationContext ctx) => new(ctx.Color);

    public string Bold(string text) => Wrap(text, "1");
    public string Dim(string text) => Wrap(text, "2");
    public string Red(string text) => Wrap(text, "31");
    public string Green(string text) => Wrap(text, "32");
    public string Yellow(string text) => Wrap(text, "33");
    public string Cyan(string text) => Wrap(text, "36");

    /// <summary>Green for gains, red for losses, plain for zero.</summary>
    public string Signed(decimal value, string text) => value > 0 ? Green(text) : value < 0 ? Red(text) : text;

    private string Wrap(string text, string code) => Enabled && text.Length > 0 ? $"\u001b[{code}m{text}\u001b[0m" : text;
}

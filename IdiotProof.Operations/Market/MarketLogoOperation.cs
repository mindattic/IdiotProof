using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketLogoArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker whose logo to fetch, e.g. AAPL.")]
    public string Symbol { get; init; } = "";

    [Option("placeholder", Description = "When Alpaca has no logo, save its generated placeholder instead of failing.")]
    public bool Placeholder { get; init; }

    [Option("dir", Description = @"Folder to save into (default %LOCALAPPDATA%\MindAttic\IdiotProof\Logos).")]
    public string? Dir { get; init; }
}

/// <summary>
/// Downloads a company logo from Alpaca (<c>/v1beta1/logos/{symbol}</c>) and saves it on the Hub
/// box as <c>{SYMBOL}.{png|jpg|svg|...}</c>, reporting the path and size.
/// </summary>
[Operation("market logo", Summary = @"Download a company's logo and save it under %LOCALAPPDATA%\MindAttic\IdiotProof\Logos; reports the path and size.", Risk = OperationRisk.Write)]
[Example("idiotproof market logo AAPL")]
[Example("idiotproof market logo TSLA --placeholder")]
public sealed class MarketLogoOperation : IOperation<MarketLogoArgs>
{
    /// <summary>The default logo folder: <c>%LOCALAPPDATA%\MindAttic\IdiotProof\Logos</c>.</summary>
    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "IdiotProof", "Logos");

    public async Task<OperationResult> ExecuteAsync(MarketLogoArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof market logo AAPL`.");
        if (symbol.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || symbol.Trim('.').Length == 0)
            return OperationResult.Usage($"'{symbol}' is not a ticker.");

        await using var api = await MarketData.OpenAsync(ctx);
        var (body, contentType) = await api.GetDataBytesAsync($"/v1beta1/logos/{Uri.EscapeDataString(symbol)}",
            MarketData.Q(("placeholder", args.Placeholder ? "true" : "false")), ctx.CancellationToken);
        if (body.Length == 0) return OperationResult.Fail($"Alpaca returned an empty logo for {symbol}.");

        var dir = string.IsNullOrWhiteSpace(args.Dir) ? DefaultDir : Path.GetFullPath(args.Dir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, symbol + Extension(contentType, body));
        await File.WriteAllBytesAsync(path, body, ctx.CancellationToken);

        var human = new KeyValueBlock()
            .Add("Symbol", symbol).Add("Saved", path).Add("Size", $"{Fmt.Number(body.Length)} bytes")
            .Add("Type", contentType ?? "unknown").Render();
        return OperationResult.Ok(new { symbol, path, bytes = body.Length, contentType }, human);
    }

    /// <summary>File extension from the media type, else sniffed from the magic bytes; <c>.png</c> as a last resort.</summary>
    internal static string Extension(string? contentType, byte[] body) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/svg+xml" => ".svg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ when body.Length >= 4 && body[0] == 0x89 && body[1] == 0x50 => ".png",
        _ when body.Length >= 3 && body[0] == 0xFF && body[1] == 0xD8 => ".jpg",
        _ when body.Length >= 4 && body[0] == (byte)'<' => ".svg",
        _ => ".png",
    };
}

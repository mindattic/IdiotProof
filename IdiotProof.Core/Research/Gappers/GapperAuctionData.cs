using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// One opening (<c>O</c>) or closing (<c>C</c>) auction print for one symbol-day, as Alpaca's
/// <c>/v2/stocks/auctions</c> reports it (SIP). A symbol asked for that had no print on the day is kept as one
/// row of kind <c>-</c>, so a file records every symbol it covered.
/// </summary>
public sealed record GapperAuctionPrint(string Symbol, string Role, char Kind, DateTime? TimeUtc, double Price, double Size, string Exchange, string Condition);

/// <summary>The auction-print file format and the official-print rule (IP-T472).</summary>
public static class GapperAuctionPrints
{
    public const string Header = "symbol,role,kind,time_utc,price,size,exchange,condition";

    /// <summary>Official cross conditions first (Q = market center official open, M = official close), then the print conditions; else the largest print.</summary>
    private static readonly string[] OpenConditions = ["Q", "O"];
    private static readonly string[] CloseConditions = ["M", "6"];

    public static GapperAuctionPrint? Official(IEnumerable<GapperAuctionPrint> prints, char kind)
    {
        var all = prints.Where(p => p.Kind == kind && p.Price > 0).ToList();
        if (all.Count == 0) return null;
        foreach (var c in kind == 'O' ? OpenConditions : CloseConditions)
            if (all.FirstOrDefault(p => p.Condition == c) is { } hit) return hit;
        return all.MaxBy(p => p.Size);
    }

    public static string ToCsv(IEnumerable<GapperAuctionPrint> prints)
    {
        var sb = new StringBuilder(Header).Append('\n');
        foreach (var p in prints)
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"{p.Symbol},{p.Role},{p.Kind},{(p.TimeUtc is { } t ? t.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) : "")},{p.Price:R},{p.Size:R},{Clean(p.Exchange)},{Clean(p.Condition)}\n"));
        return sb.ToString();
    }

    public static List<GapperAuctionPrint> FromCsv(IEnumerable<string> lines)
    {
        var list = new List<GapperAuctionPrint>();
        foreach (var line in lines.Skip(1))
        {
            var f = line.Split(',');
            if (f.Length < 8 || f[2].Length != 1) continue;
            DateTime? t = DateTime.TryParse(f[3], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var u) ? u : null;
            list.Add(new GapperAuctionPrint(f[0], f[1], f[2][0], t,
                double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0,
                double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0, f[6], f[7]));
        }
        return list;
    }

    private static string Clean(string v) => v.Replace(",", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

/// <summary>
/// <c>GET /v2/stocks/auctions</c> on the data host for many symbols over one ET day, paged and rate-gated, on the
/// frozen feed (the endpoint is SIP only).
/// </summary>
public sealed class AlpacaAuctionSource(StudyKeys keys, string feed, AlpacaDataRateGate gate) : IDisposable
{
    private readonly HttpClient http = Build(keys);

    public int Requests { get; private set; }

    private static HttpClient Build(StudyKeys keys)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://data.alpaca.markets/"), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", keys.KeyId);
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", keys.Secret);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Every auction print of <paramref name="symbols"/> between the two UTC instants; the role is left empty for the caller.</summary>
    public async Task<List<GapperAuctionPrint>> GetAsync(IReadOnlyCollection<string> symbols, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var result = new List<GapperAuctionPrint>();
        string? page = null;
        do
        {
            await gate.WaitAsync(ct);
            Requests++;
            var url = $"v2/stocks/auctions?symbols={Uri.EscapeDataString(string.Join(',', symbols))}&start={fromUtc:yyyy-MM-ddTHH:mm:ssZ}&end={toUtc:yyyy-MM-ddTHH:mm:ssZ}&limit=10000&feed={feed}"
                + (page is null ? "" : "&page_token=" + Uri.EscapeDataString(page));
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("auctions", out var bySymbol) && bySymbol.ValueKind == JsonValueKind.Object)
                foreach (var sym in bySymbol.EnumerateObject())
                {
                    if (sym.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var day in sym.Value.EnumerateArray())
                        foreach (var (prop, kind) in new[] { ("o", 'O'), ("c", 'C') })
                        {
                            if (!day.TryGetProperty(prop, out var prints) || prints.ValueKind != JsonValueKind.Array) continue;
                            foreach (var p in prints.EnumerateArray())
                            {
                                DateTime? t = p.TryGetProperty("t", out var te) && te.ValueKind == JsonValueKind.String
                                    && DateTime.TryParse(te.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var w) ? w : null;
                                result.Add(new GapperAuctionPrint(sym.Name.ToUpperInvariant(), "", kind, t,
                                    Num(p, "p"), Num(p, "s"), Str(p, "x"), Str(p, "c")));
                            }
                        }
                }
            page = doc.RootElement.TryGetProperty("next_page_token", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        } while (page is not null);
        return result;
    }

    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public void Dispose() => http.Dispose();
}

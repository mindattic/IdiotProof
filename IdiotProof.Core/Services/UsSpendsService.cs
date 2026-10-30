using System.Text;
using System.Text.Json;

namespace IdiotProof.Blazor.Services;

public record UsSpendingAward(
    string AwardId,
    string RecipientName,
    decimal Amount,
    string? Description,
    string? Date
);

/// <summary>
/// Fetches government contract awards from USASpending.gov — the official DoD
/// and federal procurement database. Zero noise: every record here is a signed
/// contract obligation, not a press release. Tier 1 primary source.
/// </summary>
public sealed class UsSpendsService(IHttpClientFactory httpFactory, ILogger<UsSpendsService> logger)
{
    private const string SearchUrl = "https://api.usaspending.gov/api/v2/search/spending_by_award/";

    public async Task<List<UsSpendingAward>> GetRecentAwardsAsync(
        string companyName,
        int daysBack = 30,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return [];

        var startDate = DateTime.UtcNow.AddDays(-daysBack).ToString("yyyy-MM-dd");
        var endDate   = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var body = JsonSerializer.Serialize(new
        {
            filters = new
            {
                recipient_search_text = new[] { companyName },
                time_period = new[] { new { start_date = startDate, end_date = endDate } },
                award_type_codes = new[] { "A", "B", "C", "D" }
            },
            fields = new[] { "Award ID", "Recipient Name", "Award Amount", "Description", "Start Date" },
            sort = "Award Amount",
            order = "desc",
            limit = 10,
            page = 1
        });

        try
        {
            var http = httpFactory.CreateClient("usspends");
            using var content  = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(SearchUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("USASpending returned {Status} for {Company}", response.StatusCode, companyName);
                return [];
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("results", out var results)) return [];

            var awards = new List<UsSpendingAward>();
            foreach (var r in results.EnumerateArray())
            {
                var amount = r.TryGetProperty("Award Amount", out var amt) && amt.ValueKind == JsonValueKind.Number
                    ? amt.GetDecimal() : 0m;
                awards.Add(new UsSpendingAward(
                    AwardId:       r.TryGetProperty("Award ID",       out var aid) ? aid.GetString() ?? "" : "",
                    RecipientName: r.TryGetProperty("Recipient Name", out var rn)  ? rn.GetString()  ?? "" : "",
                    Amount:        amount,
                    Description:   r.TryGetProperty("Description",    out var desc) && desc.ValueKind != JsonValueKind.Null ? desc.GetString() : null,
                    Date:          r.TryGetProperty("Start Date",      out var sd)  && sd.ValueKind  != JsonValueKind.Null ? sd.GetString()   : null
                ));
            }
            return awards;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "USASpending fetch failed for {Company}", companyName);
            return [];
        }
    }
}

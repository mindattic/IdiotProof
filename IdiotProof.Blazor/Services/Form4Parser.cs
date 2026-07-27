using System.Globalization;
using System.Text;
using System.Xml.Linq;
using IdiotProof.Blazor.Data;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Parses SEC Form 4 (insider transaction) XML into structured, numeric
/// <see cref="InsiderTransaction"/> rows — actual share counts, prices, and
/// pct-of-holdings deltas instead of the generic "ownership changed"
/// boilerplate the research feed used to fall back to. Pure and synchronous:
/// no HTTP or DB access, so it parses whatever document string the caller
/// already fetched (see <c>EdgarService.GetFilingDocumentAsync</c>).
/// </summary>
public sealed class Form4Parser(ILogger<Form4Parser> logger)
{
    /// <summary>
    /// Parses every &lt;nonDerivativeTransaction&gt; in the filing. Derivative
    /// (options/RSU) transactions are ignored for this pass. Fails closed:
    /// returns an empty list (never throws) on any parse error.
    /// </summary>
    public List<InsiderTransaction> Parse(string xmlContent, string? filingUrl)
    {
        try
        {
            var root = XDocument.Parse(xmlContent).Root;
            if (root is null) return [];

            var ownerEl   = root.Element("reportingOwner");
            var filerName = ownerEl?.Element("reportingOwnerId")?.Element("rptOwnerName")?.Value.Trim() ?? "";
            var filerRole = ResolveFilerRole(ownerEl?.Element("reportingOwnerRelationship"));

            var table = root.Element("nonDerivativeTable");
            if (table is null) return [];

            var results = new List<InsiderTransaction>();
            foreach (var txn in table.Elements("nonDerivativeTransaction"))
            {
                var parsed = ParseTransaction(txn, filerName, filerRole, filingUrl);
                if (parsed is not null) results.Add(parsed);
            }
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Form4Parser failed to parse filing document");
            return [];
        }
    }

    /// <summary>
    /// Builds a sober, plain-English sentence for one transaction — used on
    /// the Research tab in place of raw numbers. Omits any clause whose
    /// underlying value is null rather than printing "null" or "$0.00".
    /// </summary>
    public string Summarize(InsiderTransaction t)
    {
        var verb = t.PctOfHoldingsChanged switch
        {
            > 0 => "acquired",
            < 0 => "disposed of",
            _   => "transacted",
        };

        var sb = new StringBuilder();
        sb.Append(t.FilerName);
        sb.Append(" (").Append(t.FilerRole).Append(')');
        sb.Append(' ').Append(verb).Append(' ');
        sb.Append(t.SharesTransacted.ToString("N0", CultureInfo.InvariantCulture)).Append(" shares");

        if (t.PctOfHoldingsChanged.HasValue)
        {
            sb.Append(" (").Append(Math.Abs(t.PctOfHoldingsChanged.Value).ToString("0.#", CultureInfo.InvariantCulture))
              .Append("% of holdings)");
        }

        if (t.PricePerShare.HasValue)
            sb.Append(" at $").Append(t.PricePerShare.Value.ToString("N2", CultureInfo.InvariantCulture)).Append("/share");

        if (t.DollarValue.HasValue)
            sb.Append(" ($").Append(t.DollarValue.Value.ToString("N2", CultureInfo.InvariantCulture)).Append(')');

        sb.Append(" on ").Append(t.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.Append(" — now holds ").Append(t.SharesOwnedAfter.ToString("N0", CultureInfo.InvariantCulture)).Append(" shares.");

        return sb.ToString();
    }

    /// <summary>Officer &gt; Director &gt; TenPercentOwner &gt; Other, by priority when multiple flags are set.</summary>
    private static string ResolveFilerRole(XElement? relationship)
    {
        if (relationship is null) return "Other";

        if (IsTrue(relationship.Element("isOfficer")))        return "Officer";
        if (IsTrue(relationship.Element("isDirector")))        return "Director";
        if (IsTrue(relationship.Element("isTenPercentOwner"))) return "TenPercentOwner";
        return "Other";
    }

    // EDGAR filer software emits boolean flags as either "true"/"false" or "1"/"0" — accept both.
    private static bool IsTrue(XElement? el)
    {
        if (el is null) return false;
        var v = el.Value.Trim();
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
    }

    private static InsiderTransaction? ParseTransaction(
        XElement txn, string filerName, string filerRole, string? filingUrl)
    {
        var code = txn.Element("transactionCoding")?.Element("transactionCode")?.Value.Trim() ?? "";

        var dateStr = txn.Element("transactionDate")?.Element("value")?.Value;
        if (!DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var txnDate))
            return null;

        var amounts   = txn.Element("transactionAmounts");
        var sharesStr = amounts?.Element("transactionShares")?.Element("value")?.Value;
        if (!decimal.TryParse(sharesStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var shares))
            return null;

        // transactionPricePerShare sometimes carries only a <footnoteId/> (no
        // cash price applies, e.g. some grants/exercises) — null, not a throw.
        decimal? price = null;
        var priceStr = amounts?.Element("transactionPricePerShare")?.Element("value")?.Value;
        if (decimal.TryParse(priceStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedPrice))
            price = parsedPrice;

        var acquiredDisposed = amounts?.Element("transactionAcquiredDisposedCode")?.Element("value")?.Value.Trim() ?? "";

        var postStr = txn.Element("postTransactionAmounts")?.Element("sharesOwnedFollowingTransaction")?.Element("value")?.Value;
        if (!decimal.TryParse(postStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var sharesAfter))
            return null;

        return new InsiderTransaction
        {
            FilerName            = filerName,
            FilerRole            = filerRole,
            TransactionCode      = code,
            TransactionDate      = txnDate,
            SharesTransacted     = shares,
            PricePerShare        = price,
            DollarValue          = price.HasValue ? shares * price.Value : null,
            SharesOwnedAfter     = sharesAfter,
            PctOfHoldingsChanged = ComputePctOfHoldingsChanged(shares, sharesAfter, acquiredDisposed),
            FilingUrl            = filingUrl,
        };
    }

    private static decimal? ComputePctOfHoldingsChanged(decimal sharesTransacted, decimal sharesOwnedAfter, string acquiredDisposedCode)
    {
        var isDisposed = acquiredDisposedCode.Equals("D", StringComparison.OrdinalIgnoreCase);
        var preTransactionShares = isDisposed
            ? sharesOwnedAfter + sharesTransacted
            : sharesOwnedAfter - sharesTransacted;

        if (preTransactionShares <= 0) return null; // avoid divide-by-zero / nonsensical negative base

        var magnitude = sharesTransacted / preTransactionShares * 100m;
        return isDisposed ? -magnitude : magnitude;
    }
}

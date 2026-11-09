using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T488: the keyword classifier fixes found by the NMF catalyst clusters (headlines from the train news).</summary>
public class CatalystTypesTests
{
    private static bool Has(string headline, string key, string? preClass = null) => (CatalystTypes.FromHeadline(headline, preClass) & CatalystTypes.Bit(key)) != 0;

    [Test]
    public void Pipe_IsCaseSensitive_AndAFundStakeIsNotAnInvestment()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Has("Crown PE Pipelines Division Secures $33M In Contracts For Lead Pipe Remediation", "pipe"), Is.False);
            Assert.That(Has("Pagaya Technologies Announces Upsized $350M PIPE", "pipe"), Is.True);
            Assert.That(Has("Cathie Wood Loads Up On Biotech Stocks, Continues To Raise Stake In Coinbase Global", "pipe"), Is.False);
            Assert.That(Has("Takeda Reports In 13D Filing A 13.2% Stake In Calithera Biosciences", "pipe"), Is.True);
            Assert.That(Has("DarioHealth Announces $14.3M Private Placement", "pipe"), Is.True);
        });
    }

    [Test]
    public void CoverageTemplates_GetOnlyTheCoverageBit()
    {
        foreach (var h in new[]
                 {
                     "Why Citi Trends Shares Are Trading Lower By Over 14%? Here Are Other Stocks Moving In Tuesday's Mid-Day Session",
                     "SIGA Technologies Option Alert: Dec 16 $15 Calls Sweep (18) near the Ask: 250 @ $3.3 vs 2533 OI; Earnings 8/4 After Close [est] Ref=$12.7199",
                     "Dow Rises 75 Points; Fed Increases Rates By 25bps",
                     "Vaccinex Shares Halted On Circuit Breaker To The Upside, Stock Now Up 6.20%",
                     "Earnings Scheduled For November 26, 2021",
                 })
            Assert.That(CatalystTypes.FromHeadline(h, "MA"), Is.EqualTo(CatalystTypes.Bit("coverage")), h);
    }

    [Test]
    public void Earnings_TakeTheFirstVerdict()
    {
        const string mixed = "GoodRx Holdings Q2 2024 Adj. EPS $0.08 Misses $0.09 Estimate, Sales $200.610M Beat $200.441M Estimate";
        const string beat = "Mativ Holdings Q4 2023 Adj EPS $0.23 Beats $0.14 Estimate, Sales $452.30M Miss $461.05M Estimate";
        Assert.Multiple(() =>
        {
            Assert.That(Has(mixed, "earnMiss") && !Has(mixed, "earnBeat"), Is.True);
            Assert.That(Has(beat, "earnBeat") && !Has(beat, "earnMiss"), Is.True);
        });
    }

    [Test]
    public void SelfTender_IsABuyback_NotATarget()
    {
        const string h = "OneSpan Commenced A Modified \"Dutch Auction\" Tender Offer To Purchase For Cash ~$20M Of Its Common Stock";
        Assert.Multiple(() =>
        {
            Assert.That(Has(h, "acqTarget"), Is.False);
            Assert.That(Has(h, "buyback"), Is.True);
            Assert.That(Has("Oyster Point Pharma To Be Acquired By Viatris For $11/Share", "acqTarget"), Is.True);
        });
    }

    [Test]
    public void Fda_NeedsAClinicalContext_AndAcronymsAreCased()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Has("Quidel To Buy Ortho Clinical Diagnostics For $24.68/Share: Highlights", "fda"), Is.False);
            Assert.That(Has("Vera Therapeutics Says Met Primary Endpoint For Phase 2b ORIGIN Clinical Trial", "fda"), Is.True);
            Assert.That(Has("FDA Accepts Mesoblast's Biologics License Application (BLA) For Ryoncil", "fda"), Is.True);
            Assert.That(Has("Acme Ind. Reports Higher Sales", "fda"), Is.False);
        });
    }

    [Test]
    public void Offering_NeedsAFinancingContext_AndAtmAiAreCased()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Has("Amazon Could Buy Stitch Fix, Which Would Complement Its Prime Wardrobe Offering", "offering"), Is.False);
            Assert.That(Has("Gain Therapeutics Prices $11M Public Offering Of 7,116,547 Common Shares At $1.35/Share", "offering"), Is.True);
            Assert.That(Has("Bank adds atm machines downtown", "offering"), Is.False);
            Assert.That(Has("Cantor Fitzgerald Reiterates Overweight on BigBear.ai Hldgs, Maintains $3 Price Target", "theme"), Is.False);
            Assert.That(Has("Alibaba Partners With Canaan Over AI", "theme"), Is.True);
        });
    }

    [Test]
    public void ListingCompliance_IsNotAnUplisting_AndFilingItemsMap()
    {
        const string h = "Nuwellis Granted Continued Listing On The Nasdaq Stock Market, Subject To Conditions";
        Assert.Multiple(() =>
        {
            Assert.That(Has(h, "compliance") && !Has(h, "uplist"), Is.True);
            Assert.That(Has("Eve Holding Reports Its Common Stock Will Begin Trading On NYSE Today", "uplist"), Is.True);
            Assert.That(CatalystTypes.FromFiling("8-K", ["3.01", "9.01"]), Is.EqualTo(CatalystTypes.Bit("compliance")));
            Assert.That(CatalystTypes.FromFiling("8-K", ["5.03"]), Is.EqualTo(CatalystTypes.Bit("rsplit")));
        });
    }

    [Test]
    public void ClusterMap_CoversEveryClusterName_WithKnownKeys()
    {
        foreach (var (name, keys) in CatalystClusterMap.Map)
            foreach (var k in keys)
                Assert.That(CatalystTypes.Keys, Does.Contain(k), name);
        Assert.That(CatalystClusterMap.TypesOf("registered direct offering / private placement (PIPE)"), Does.Contain("pipe"));
        Assert.That(CatalystClusterMap.TypesOf("no such cluster"), Is.Empty);
    }
}

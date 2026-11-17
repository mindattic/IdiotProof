using IdiotProof.Core.Catalysts;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T443: the frozen version 1 rules over the shipped <c>catalyst-preclass.json</c> — the same rules the
/// gapper study recomputes on history (amendment 08), so these fixtures pin that they have not moved.
/// </summary>
[TestFixture]
public sealed class CatalystLegacyClassifierTests
{
    private static CatalystPreClassRules Rules => CatalystCatalogs.PreClassRules;

    [Test]
    public void ShippedCatalog_Loads()
    {
        Assert.That(Rules.Version, Is.EqualTo(1));
        Assert.That(Rules.KeywordRules, Is.Not.Empty);
        Assert.That(Rules.EightK.Items, Does.ContainKey("2.02"));
    }

    [Test]
    public void FdaApprovalHeadline_IsFdaBullish()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme Therapeutics Receives FDA Approval for ZYX-1 in Adult Patients", "", source: "benzinga");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.Fda, 1)));
        Assert.That(r.Confidence, Is.GreaterThan(0.5));
    }

    [Test]
    public void CompleteResponseLetter_IsFdaBearish()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme Receives Complete Response Letter from FDA for ZYX-1", null);
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.Fda, -1)));
    }

    [Test]
    public void PricingOfPublicOffering_IsDilutionBearish()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme Announces Pricing of $8 Million Public Offering", "");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.OfferingDilution, -1)));
    }

    [TestCase("424B4")]
    [TestCase("424B5")]
    [TestCase("S-1")]
    [TestCase("S-3")]
    public void OfferingForms_AreDilutionBearish_WhateverTheText(string form)
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "S-1: ACME CORP", null, formType: form, source: "edgar");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.OfferingDilution, -1)));
        Assert.That(r.Rule, Does.Contain(form));
    }

    [Test]
    public void EightK_503_WithReverseSplitWording_IsReverseSplitBearish()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "8-K: ACME CORP (items 5.03, 9.01)",
            "Amendment to the certificate of incorporation to effect a 1-for-20 reverse stock split", "8-K", ["5.03", "9.01"], "edgar");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.ReverseSplit, -1)));
    }

    [Test]
    public void EightK_503_WithoutReverseSplitWording_IsOther()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "8-K: ACME CORP (items 5.03)", "Amended bylaws to change the fiscal year end.", "8-K", ["5.03"], "edgar");
        Assert.That(r.Class, Is.EqualTo(CatalystLegacyClass.Other));
    }

    [Test]
    public void ContractAward_IsBullish()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme Defense Announces Contract Award from U.S. Navy Valued at $12 Million", "");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.ContractAward, 1)));
    }

    [Test]
    public void EightK_202_IsEarningsNeutral()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "8-K: ACME CORP (items 2.02, 9.01)", "Acme reports third quarter 2026 financial results", "8-K", ["2.02", "9.01"], "edgar");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.Earnings, 0)));
    }

    [Test]
    public void EightK_101_Plus_201_IsMa_And_101_Alone_IsContract()
    {
        var ma = CatalystLegacyClassifier.Classify(Rules, "8-K: ACME CORP", "", "8-K", ["1.01", "2.01", "9.01"], "edgar");
        var contract = CatalystLegacyClassifier.Classify(Rules, "8-K: ACME CORP", "", "8-K", ["1.01", "9.01"], "edgar");
        Assert.Multiple(() =>
        {
            Assert.That((ma.Class, ma.Direction), Is.EqualTo((CatalystLegacyClass.MA, 1)));
            Assert.That((contract.Class, contract.Direction), Is.EqualTo((CatalystLegacyClass.ContractAward, 1)));
        });
    }

    [Test]
    public void EightK_101_WithMergerWording_PrefersTheStrongerMaKeyword()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "8-K: ACME CORP", "Entry into agreement and plan of merger with Beta Holdings", "8-K", ["1.01"], "edgar");
        Assert.That(r.Class, Is.EqualTo(CatalystLegacyClass.MA));
    }

    [Test]
    public void PromoWireRelease_IsPromoPr()
    {
        var r = CatalystLegacyClassifier.Classify(Rules,
            "Acme Unveils REVOLUTIONARY Game-Changing AI Platform Poised To Disrupt The Entire Industry!",
            "The groundbreaking platform is set to dominate.", source: "GlobeNewswire");
        Assert.That(r.Class, Is.EqualTo(CatalystLegacyClass.PromoPr));
        Assert.That(r.Rule, Does.StartWith("promo"));
    }

    [Test]
    public void PromoWordsWithConcreteFdaNews_StayFda()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme Receives Groundbreaking FDA Approval!", "", source: "GlobeNewswire");
        Assert.That(r.Class, Is.EqualTo(CatalystLegacyClass.Fda));
    }

    [Test]
    public void SoberEditorialHeadline_IsOther()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme shares move after analyst day", "", source: "benzinga");
        Assert.That((r.Class, r.Direction, r.Confidence), Is.EqualTo((CatalystLegacyClass.Other, 0, 0d)));
    }

    [Test]
    public void Uplisting_IsBullish()
    {
        var r = CatalystLegacyClassifier.Classify(Rules, "Acme Approved for Listing on Nasdaq Capital Market; Trading Begins Monday", "");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystLegacyClass.Uplisting, 1)));
    }

    [Test]
    public void Classification_IsDeterministic()
    {
        var a = CatalystLegacyClassifier.Classify(Rules, "Acme Receives FDA Approval", "x", "8-K", ["8.01"], "edgar");
        var b = CatalystLegacyClassifier.Classify(Rules, "Acme Receives FDA Approval", "x", "8-K", ["8.01"], "edgar");
        Assert.That(a, Is.EqualTo(b));
    }

    [TestCase("edgar", true, 1)]
    [TestCase("GlobeNewswire", false, 3)]
    [TestCase("benzinga", false, 2)]
    [TestCase("", false, 2)]
    public void SourceTier_FollowsTheCatalog(string source, bool isEdgar, int expected) =>
        Assert.That(CatalystPreClassifier.SourceTier(Rules, source, isEdgar), Is.EqualTo(expected));
}

/// <summary>
/// IP-T487: the catalyst type on top of the version 1 rules — one <see cref="CatalystPreClass"/> per item by the
/// fixed priority, the non-binding-LOI flag beside it, and the version 1 reading returned unchanged.
/// </summary>
[TestFixture]
public sealed class CatalystPreClassifierTests
{
    private static CatalystPreClassRules Rules => CatalystCatalogs.PreClassRules;

    private static CatalystPreClassification News(string headline, string? summary = null, string? source = "benzinga") =>
        CatalystPreClassifier.Classify(Rules, headline, summary, source: source);

    [TestCase("Acme Announces $12 Million Strategic Investment from Beta Capital", CatalystPreClass.OutsideInvestment)]
    [TestCase("Acme Enters Definitive Agreement to Be Acquired by Beta Holdings for $4.10 Per Share", CatalystPreClass.AcquisitionTarget)]
    [TestCase("Acme Completes Acquisition of Beta Robotics", CatalystPreClass.AcquisitionAcquirer)]
    [TestCase("Acme and Beta Announce Business Combination", CatalystPreClass.Merger)]
    [TestCase("Acme Q3 EPS $0.12 Beats $0.08 Estimate, Sales $41M Miss $43M Estimate", CatalystPreClass.EarningsBeat)]
    [TestCase("Acme Q3 EPS $(0.20) Misses $(0.11) Estimate", CatalystPreClass.EarningsMiss)]
    [TestCase("Acme Reports Third Quarter 2026 Financial Results", CatalystPreClass.EarningsReport)]
    [TestCase("Acme Raises Full-Year Guidance", CatalystPreClass.Guidance)]
    [TestCase("Acme Awarded $9 Million Contract by the U.S. Navy", CatalystPreClass.ContractPartnership)]
    [TestCase("Acme Therapeutics Receives FDA Approval for ZYX-1", CatalystPreClass.FdaClinical)]
    [TestCase("Acme Announces 1-for-20 Reverse Stock Split", CatalystPreClass.ReverseSplit)]
    [TestCase("Acme Approved for Listing on Nasdaq Capital Market; Shares to Begin Trading on Nasdaq Monday", CatalystPreClass.Uplisting)]
    [TestCase("Acme Regains Compliance With Nasdaq Minimum Bid Price Requirement", CatalystPreClass.ListingCompliance)]
    [TestCase("Acme Board Authorizes $20 Million Share Repurchase Program", CatalystPreClass.Buyback)]
    [TestCase("HC Wainwright Upgrades Acme to Buy", CatalystPreClass.AnalystUpgrade)]
    [TestCase("Acme Announces Pricing of $8 Million Public Offering", CatalystPreClass.OfferingDilution)]
    [TestCase("Acme Adopts Bitcoin Treasury Strategy", CatalystPreClass.ThemePivot)]
    [TestCase("Acme Appoints Jane Doe as Chief Financial Officer", CatalystPreClass.Other)]
    [TestCase("12 Health Care Stocks Moving In Tuesday's Pre-Market Session", CatalystPreClass.NoCatalyst)]
    public void Headline_GetsItsType(string headline, CatalystPreClass expected) =>
        Assert.That(News(headline).Class, Is.EqualTo(expected), headline);

    [Test]
    public void NonBindingLoi_IsAFlag_NotAType()
    {
        var alone = News("Acme Signs Non-Binding Letter of Intent With Beta Energy");
        var acquisition = News("Acme Signs Letter of Intent to Acquire Beta Robotics");
        var binding = News("Acme Signs Supply Agreement With Beta Energy");
        Assert.Multiple(() =>
        {
            Assert.That((alone.Class, alone.NonBindingLoi), Is.EqualTo((CatalystPreClass.ContractPartnership, true)));
            Assert.That((acquisition.Class, acquisition.NonBindingLoi), Is.EqualTo((CatalystPreClass.AcquisitionAcquirer, true)));
            Assert.That((binding.Class, binding.NonBindingLoi), Is.EqualTo((CatalystPreClass.ContractPartnership, false)));
        });
    }

    [Test]
    public void SeveralTypes_ResolveByTheFixedPriority()
    {
        Assert.Multiple(() =>
        {
            // an offering outranks the private placement beside it; a target outranks a merger word; a split outranks everything
            Assert.That(News("Acme Announces $5 Million Registered Direct Offering and Concurrent Private Placement").Class, Is.EqualTo(CatalystPreClass.OfferingDilution));
            Assert.That(News("Acme to Be Acquired by Beta in $300 Million Merger").Class, Is.EqualTo(CatalystPreClass.AcquisitionTarget));
            Assert.That(News("Acme Announces Reverse Stock Split and Public Offering").Class, Is.EqualTo(CatalystPreClass.ReverseSplit));
        });
    }

    [Test]
    public void TheVersion1Reading_ComesBackUnchanged()
    {
        var r = News("Acme Therapeutics Receives FDA Approval for ZYX-1 in Adult Patients", "");
        var legacy = CatalystLegacyClassifier.Classify(Rules, "Acme Therapeutics Receives FDA Approval for ZYX-1 in Adult Patients", "", source: "benzinga");
        Assert.That((r.Legacy, r.Direction, r.Confidence, r.LegacyRule), Is.EqualTo((legacy.Class, legacy.Direction, legacy.Confidence, legacy.Rule)));
    }

    [TestCase("424B5", null, CatalystPreClass.OfferingDilution)]
    [TestCase("S-1", null, CatalystPreClass.OfferingDilution)]
    [TestCase("SC 13D", null, CatalystPreClass.OutsideInvestment)]
    [TestCase("8-K", "3.02", CatalystPreClass.OutsideInvestment)]
    [TestCase("8-K", "1.01", CatalystPreClass.ContractPartnership)]
    [TestCase("8-K", "1.01;2.01", CatalystPreClass.AcquisitionAcquirer)]
    [TestCase("8-K", "2.02;9.01", CatalystPreClass.EarningsReport)]
    [TestCase("8-K", "3.01", CatalystPreClass.ListingCompliance)]
    [TestCase("8-K", "3.03;5.03", CatalystPreClass.ReverseSplit)]
    [TestCase("8-K", "5.02", CatalystPreClass.Other)]
    [TestCase("425", null, CatalystPreClass.Merger)]
    [TestCase("SC TO-T", null, CatalystPreClass.AcquisitionTarget)]
    [TestCase("SC TO-I", null, CatalystPreClass.Buyback)]
    [TestCase("25-NSE", null, CatalystPreClass.ListingCompliance)]
    [TestCase("10-Q", null, CatalystPreClass.Other)]
    [TestCase("4", null, CatalystPreClass.NoCatalyst)]
    [TestCase("SCHEDULE 13G/A", null, CatalystPreClass.NoCatalyst)]
    public void Filing_GetsItsTypeFromFormAndItems(string form, string? items, CatalystPreClass expected) =>
        Assert.That(CatalystPreClassifier.TypeOf("8-K: ACME MERGER CORP", form, items?.Split(';'), null).Class, Is.EqualTo(expected), $"{form} {items}");

    [Test]
    public void Filing_TakesFdaAndMergerFromTheVersion1Class_WhenItsItemsNameNoEvent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CatalystPreClassifier.TypeOf("8-K: ACME CORP", "8-K", ["8.01"], "Fda").Class, Is.EqualTo(CatalystPreClass.FdaClinical));
            Assert.That(CatalystPreClassifier.TypeOf("8-K: ACME CORP", "8-K", ["1.01"], "MA").Class, Is.EqualTo(CatalystPreClass.Merger));
            Assert.That(CatalystPreClassifier.TypeOf("8-K: ACME CORP", "8-K", ["1.01", "2.01"], "MA").Class, Is.EqualTo(CatalystPreClass.AcquisitionAcquirer));
        });
    }

    [TestCase("Fda", CatalystPreClass.FdaClinical)]
    [TestCase("ContractAward", CatalystPreClass.ContractPartnership)]
    [TestCase("MA", CatalystPreClass.Merger)]
    [TestCase("Earnings", CatalystPreClass.EarningsReport)]
    [TestCase("PromoPr", CatalystPreClass.Other)]
    [TestCase("Other", CatalystPreClass.Other)]
    [TestCase("OfferingDilution", CatalystPreClass.OfferingDilution)]
    [TestCase("EarningsBeat", CatalystPreClass.EarningsBeat)]
    public void StoredNames_OldAndNew_Parse(string stored, CatalystPreClass expected) =>
        Assert.That(CatalystPreClassNames.Parse(stored), Is.EqualTo(expected));

    [Test]
    public void StoredNames_NullOrUnknown_ParseToNull()
    {
        Assert.That(CatalystPreClassNames.Parse(null), Is.Null);
        Assert.That(CatalystPreClassNames.Parse("NotAType"), Is.Null);
    }

    [Test]
    public void Classification_IsDeterministic()
    {
        var a = CatalystPreClassifier.Classify(Rules, "Acme Receives FDA Approval", "x", "8-K", ["8.01"], "edgar");
        var b = CatalystPreClassifier.Classify(Rules, "Acme Receives FDA Approval", "x", "8-K", ["8.01"], "edgar");
        Assert.That(a, Is.EqualTo(b));
    }
}

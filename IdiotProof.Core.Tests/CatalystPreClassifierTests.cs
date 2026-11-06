using IdiotProof.Core.Catalysts;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// IP-T443: the deterministic pre-classifier over the shipped <c>catalyst-preclass.json</c> — the
/// same rules the gapper study recomputes on history, so these fixtures pin the live behavior.
/// </summary>
[TestFixture]
public sealed class CatalystPreClassifierTests
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
        var r = CatalystPreClassifier.Classify(Rules, "Acme Therapeutics Receives FDA Approval for ZYX-1 in Adult Patients", "", source: "benzinga");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.Fda, 1)));
        Assert.That(r.Confidence, Is.GreaterThan(0.5));
    }

    [Test]
    public void CompleteResponseLetter_IsFdaBearish()
    {
        var r = CatalystPreClassifier.Classify(Rules, "Acme Receives Complete Response Letter from FDA for ZYX-1", null);
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.Fda, -1)));
    }

    [Test]
    public void PricingOfPublicOffering_IsDilutionBearish()
    {
        var r = CatalystPreClassifier.Classify(Rules, "Acme Announces Pricing of $8 Million Public Offering", "");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.OfferingDilution, -1)));
    }

    [TestCase("424B4")]
    [TestCase("424B5")]
    [TestCase("S-1")]
    [TestCase("S-3")]
    public void OfferingForms_AreDilutionBearish_WhateverTheText(string form)
    {
        var r = CatalystPreClassifier.Classify(Rules, "S-1: ACME CORP", null, formType: form, source: "edgar");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.OfferingDilution, -1)));
        Assert.That(r.Rule, Does.Contain(form));
    }

    [Test]
    public void EightK_503_WithReverseSplitWording_IsReverseSplitBearish()
    {
        var r = CatalystPreClassifier.Classify(Rules, "8-K: ACME CORP (items 5.03, 9.01)",
            "Amendment to the certificate of incorporation to effect a 1-for-20 reverse stock split", "8-K", ["5.03", "9.01"], "edgar");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.ReverseSplit, -1)));
    }

    [Test]
    public void EightK_503_WithoutReverseSplitWording_IsOther()
    {
        var r = CatalystPreClassifier.Classify(Rules, "8-K: ACME CORP (items 5.03)", "Amended bylaws to change the fiscal year end.", "8-K", ["5.03"], "edgar");
        Assert.That(r.Class, Is.EqualTo(CatalystPreClass.Other));
    }

    [Test]
    public void ContractAward_IsBullish()
    {
        var r = CatalystPreClassifier.Classify(Rules, "Acme Defense Announces Contract Award from U.S. Navy Valued at $12 Million", "");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.ContractAward, 1)));
    }

    [Test]
    public void EightK_202_IsEarningsNeutral()
    {
        var r = CatalystPreClassifier.Classify(Rules, "8-K: ACME CORP (items 2.02, 9.01)", "Acme reports third quarter 2026 financial results", "8-K", ["2.02", "9.01"], "edgar");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.Earnings, 0)));
    }

    [Test]
    public void EightK_101_Plus_201_IsMa_And_101_Alone_IsContract()
    {
        var ma = CatalystPreClassifier.Classify(Rules, "8-K: ACME CORP", "", "8-K", ["1.01", "2.01", "9.01"], "edgar");
        var contract = CatalystPreClassifier.Classify(Rules, "8-K: ACME CORP", "", "8-K", ["1.01", "9.01"], "edgar");
        Assert.Multiple(() =>
        {
            Assert.That((ma.Class, ma.Direction), Is.EqualTo((CatalystPreClass.MA, 1)));
            Assert.That((contract.Class, contract.Direction), Is.EqualTo((CatalystPreClass.ContractAward, 1)));
        });
    }

    [Test]
    public void EightK_101_WithMergerWording_PrefersTheStrongerMaKeyword()
    {
        var r = CatalystPreClassifier.Classify(Rules, "8-K: ACME CORP", "Entry into agreement and plan of merger with Beta Holdings", "8-K", ["1.01"], "edgar");
        Assert.That(r.Class, Is.EqualTo(CatalystPreClass.MA));
    }

    [Test]
    public void PromoWireRelease_IsPromoPr()
    {
        var r = CatalystPreClassifier.Classify(Rules,
            "Acme Unveils REVOLUTIONARY Game-Changing AI Platform Poised To Disrupt The Entire Industry!",
            "The groundbreaking platform is set to dominate.", source: "GlobeNewswire");
        Assert.That(r.Class, Is.EqualTo(CatalystPreClass.PromoPr));
        Assert.That(r.Rule, Does.StartWith("promo"));
    }

    [Test]
    public void PromoWordsWithConcreteFdaNews_StayFda()
    {
        var r = CatalystPreClassifier.Classify(Rules, "Acme Receives Groundbreaking FDA Approval!", "", source: "GlobeNewswire");
        Assert.That(r.Class, Is.EqualTo(CatalystPreClass.Fda));
    }

    [Test]
    public void SoberEditorialHeadline_IsOther()
    {
        var r = CatalystPreClassifier.Classify(Rules, "Acme shares move after analyst day", "", source: "benzinga");
        Assert.That((r.Class, r.Direction, r.Confidence), Is.EqualTo((CatalystPreClass.Other, 0, 0d)));
    }

    [Test]
    public void Uplisting_IsBullish()
    {
        var r = CatalystPreClassifier.Classify(Rules, "Acme Approved for Listing on Nasdaq Capital Market; Trading Begins Monday", "");
        Assert.That((r.Class, r.Direction), Is.EqualTo((CatalystPreClass.Uplisting, 1)));
    }

    [Test]
    public void Classification_IsDeterministic()
    {
        var a = CatalystPreClassifier.Classify(Rules, "Acme Receives FDA Approval", "x", "8-K", ["8.01"], "edgar");
        var b = CatalystPreClassifier.Classify(Rules, "Acme Receives FDA Approval", "x", "8-K", ["8.01"], "edgar");
        Assert.That(a, Is.EqualTo(b));
    }

    [TestCase("edgar", true, 1)]
    [TestCase("GlobeNewswire", false, 3)]
    [TestCase("benzinga", false, 2)]
    [TestCase("", false, 2)]
    public void SourceTier_FollowsTheCatalog(string source, bool isEdgar, int expected) =>
        Assert.That(CatalystPreClassifier.SourceTier(Rules, source, isEdgar), Is.EqualTo(expected));
}

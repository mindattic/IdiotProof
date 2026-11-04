using IdiotProof.Evidence.Dependence;

namespace IdiotProof.Evidence.Tests;

public class CopulaTests
{
    /// <summary>Clayton(θ) sample by the conditional method: v = (u^{-θ}(w^{-θ/(1+θ)} - 1) + 1)^{-1/θ}.</summary>
    private static (double[] U, double[] V) Clayton(int n, double theta, int seed)
    {
        var rng = new Random(seed);
        var u = new double[n];
        var v = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = rng.NextDouble() * 0.998 + 0.001, w = rng.NextDouble() * 0.998 + 0.001;
            u[i] = a;
            v[i] = Math.Pow(Math.Pow(a, -theta) * (Math.Pow(w, -theta / (1 + theta)) - 1) + 1, -1 / theta);
        }
        return (u, v);
    }

    /// <summary>Gumbel = survival-flipped is not Clayton, so build an upper-tail pair as 1 - Clayton.</summary>
    private static (double[] U, double[] V) Flip((double[] U, double[] V) p) =>
        (p.U.Select(x => 1 - x).ToArray(), p.V.Select(x => 1 - x).ToArray());

    [Test]
    public void PseudoObservations_AreRanksOverNPlusOne_WithTiesAveraged()
    {
        var u = Copulas.PseudoObservations(new[] { 10.0, 30, 20, 20 });
        Assert.That(u, Is.EqualTo(new[] { 1 / 5.0, 4 / 5.0, 2.5 / 5, 2.5 / 5 }).Within(1e-12));
    }

    [Test]
    public void KendallTau_PerfectOrderAndReverse()
    {
        var x = new double[] { 1, 2, 3, 4, 5 };
        Assert.That(Copulas.KendallTau(x, x.Select(v => v * v).ToArray()), Is.EqualTo(1).Within(1e-12));
        Assert.That(Copulas.KendallTau(x, x.Select(v => -v).ToArray()), Is.EqualTo(-1).Within(1e-12));
    }

    [Test]
    public void ClaytonData_HasSignificantLowerTailDependence_AndNoUpper_AndClaytonWinsTheFit()
    {
        var (u, v) = Clayton(1500, theta: 2.0, seed: 3);

        var lower = Copulas.LowerTail(u, v, q: 0.05, permutations: 499);
        var upper = Copulas.UpperTail(u, v, q: 0.05, permutations: 499);
        var fits = Copulas.Fit(u, v);
        var clayton = fits.Single(f => f.Family == CopulaFamily.Clayton);

        Assert.That(lower.Lambda, Is.GreaterThan(0.5), "Clayton(2) has λ_L = 2^-0.5 ≈ 0.71");
        Assert.That(lower.PValue, Is.LessThan(0.01));
        Assert.That(upper.Lambda, Is.LessThan(lower.Lambda - 0.2));
        Assert.That(fits[0].Family, Is.EqualTo(CopulaFamily.Clayton), string.Join(", ", fits.Select(f => $"{f.Family} {f.Aic:F1}")));
        Assert.That(clayton.Parameter, Is.EqualTo(2.0).Within(0.4), "τ inversion recovers θ");
        Assert.That(clayton.LowerTail, Is.EqualTo(Math.Pow(2, -0.5)).Within(0.08));
    }

    [Test]
    public void UpperTailedData_PrefersGumbelOverClayton()
    {
        var (u, v) = Flip(Clayton(1500, theta: 2.0, seed: 4));
        var fits = Copulas.Fit(u, v);
        var gumbel = fits.Single(f => f.Family == CopulaFamily.Gumbel);
        var clayton = fits.Single(f => f.Family == CopulaFamily.Clayton);
        Assert.That(gumbel.Aic, Is.LessThan(clayton.Aic));
        Assert.That(gumbel.UpperTail, Is.GreaterThan(0.4));
    }

    [Test]
    public void IndependentSeries_HaveNoSignificantTailDependence_AndIndependenceOrGaussianFitsBest()
    {
        var rng = new Random(9);
        var x = Enumerable.Range(0, 1200).Select(_ => rng.NextDouble()).ToArray();
        var y = Enumerable.Range(0, 1200).Select(_ => rng.NextDouble()).ToArray();

        var lower = Copulas.LowerTail(x, y, q: 0.05, permutations: 499);
        var fits = Copulas.Fit(x, y);

        Assert.That(lower.PValue, Is.GreaterThan(0.05));
        Assert.That(fits[0].Family, Is.AnyOf(CopulaFamily.Independence, CopulaFamily.Gaussian));
    }

    [Test]
    public void ClosedForms_MatchKnownValues()
    {
        // Gumbel with θ=1 is independence: log density 0 everywhere.
        Assert.That(Copulas.GumbelLogDensity(0.3, 0.7, 1.0), Is.EqualTo(0).Within(1e-9));
        // Gaussian with ρ=0 is independence.
        Assert.That(Copulas.GaussianLogDensity(0.2, 0.9, 0.0), Is.EqualTo(0).Within(1e-12));
        // Student-t quantile is the inverse of its CDF and symmetric.
        Assert.That(Copulas.StudentTQuantile(0.5, 5), Is.EqualTo(0).Within(1e-6), "limited by the incomplete-beta CDF's ~1e-8 accuracy");
        Assert.That(Copulas.StudentTQuantile(0.975, 30), Is.EqualTo(2.042).Within(0.002));
        // t copula tail dependence grows as ν falls.
        Assert.That(Copulas.StudentTTailDependence(0.5, 3), Is.GreaterThan(Copulas.StudentTTailDependence(0.5, 30)));
    }
}

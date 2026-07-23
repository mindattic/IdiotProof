using System.Numerics;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Settings;
using MindAttic.Legion;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Scores each ResearchClaim on 20 financial dimensions via Claude Haiku,
/// then computes a 64-bit LSH signature from the resulting feature vector.
/// The same random hyperplanes are regenerated from seed 1337 on every startup,
/// so signatures remain stable and comparable across restarts without persistence.
///
/// Dimensions captured (0.0..1.0 each):
///   revenue_impact, market_share, regulatory_dependency, timeline_certainty,
///   insider_conviction, government_nexus, sector_spillover, ma_probability,
///   urgency, capex_event, product_pipeline, competitive_position,
///   execution_risk, macro_lever, momentum_catalyst, source_credibility,
///   dollar_materiality, sentiment_surprise, volatility_setup, portent_quality
/// </summary>
public sealed class ClaimVectorService
{
    private const int FeatureCount = 20;
    private const int LshBits     = 64;
    private const int Seed        = 1337;

    private readonly float[][] hyperplanes;
    private readonly LegionClient legion;
    private readonly AppSettings appSettings;
    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly ILogger<ClaimVectorService> logger;

    private const string ScorePrompt = """
        You are a quantitative financial analyst. Given the following financial claim,
        score it on exactly 20 dimensions. Each score is a float from 0.0 to 1.0.

        Return ONLY a JSON array of exactly 20 floats in this exact order:
        [
          revenue_impact,        // 0=no revenue impact, 1=transformative revenue impact
          market_share,          // 0=loses share, 0.5=neutral, 1=major share gains
          regulatory_dependency, // 0=no approval needed, 1=critical approval pending
          timeline_certainty,    // 0=no timeline known, 1=specific confirmed date
          insider_conviction,    // 0=no insider signal, 1=strong insider buying/commitment
          government_nexus,      // 0=no government angle, 1=pure government contract/award
          sector_spillover,      // 0=company-isolated, 1=entire sector affected
          ma_probability,        // 0=no M&A, 1=acquisition/merger in progress
          urgency,               // 0=multi-year horizon, 1=imminent days/weeks
          capex_event,           // 0=no capital change, 1=major capital deployment announced
          product_pipeline,      // 0=no product news, 1=breakthrough product milestone
          competitive_position,  // 0=weakens moat, 0.5=neutral, 1=strongly improves moat
          execution_risk,        // 0=zero risk/proven, 1=highly speculative/untested
          macro_lever,           // 0=macro-immune, 1=directly rides macro tailwind
          momentum_catalyst,     // 0=fades price momentum, 1=ignites/amplifies momentum
          source_credibility,    // 0=promotional/rumor, 1=primary SEC filing
          dollar_materiality,    // 0=immaterial, 1=transforms the company's balance sheet
          sentiment_surprise,    // 0=fully priced in/expected, 1=complete market surprise
          volatility_setup,      // 0=routine announcement, 1=binary outcome high-stakes event
          portent_quality        // 0=event already happened, 1=strong unpriced future catalyst
        ]

        No explanation, no markdown. Only the JSON array.
        """;

    public ClaimVectorService(
        LegionClient legion,
        AppSettings appSettings,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<ClaimVectorService> logger)
    {
        this.legion      = legion;
        this.appSettings = appSettings;
        this.dbFactory   = dbFactory;
        this.logger      = logger;
        hyperplanes      = GenerateHyperplanes();
    }

    // ── Signature computation ─────────────────────────────────────────────

    public byte[] GetSignature(float[] features)
    {
        if (features.Length < FeatureCount) return new byte[LshBits / 8];

        var sig = new byte[LshBits / 8];
        for (int i = 0; i < LshBits; i++)
        {
            float dot = 0f;
            var plane = hyperplanes[i];
            for (int j = 0; j < FeatureCount; j++)
                dot += features[j] * plane[j];

            if (dot >= 0)
                sig[i / 8] |= (byte)(1 << (i % 8));
        }
        return sig;
    }

    public static int HammingDistance(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return int.MaxValue;
        int dist = 0;
        for (int i = 0; i < a.Length; i++)
            dist += BitOperations.PopCount((uint)(a[i] ^ b[i]));
        return dist;
    }

    // ── LLM scoring ───────────────────────────────────────────────────────

    public async Task<float[]?> ScoreAsync(
        string ticker,
        string claimSummary,
        string claimType,
        string sentiment,
        string magnitude,
        bool   isPortent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appSettings.ClaudeApiKey)) return null;

        var userMsg = $"Ticker: {ticker}\nType: {claimType}\nSentiment: {sentiment}\nMagnitude: {magnitude}\nPortent: {isPortent}\n\nClaim: {claimSummary}";

        try
        {
            var raw = await legion.CallAsync(
                providerId:   "claude-api",
                apiKey:       appSettings.ClaudeApiKey,
                model:        "claude-haiku-4-5-20251001",
                systemPrompt: ScorePrompt,
                userMessage:  userMsg,
                maxTokens:    150,
                temperature:  0.0,
                ct:           ct);

            var json = raw.Trim();
            var start = json.IndexOf('[');
            var end   = json.LastIndexOf(']');
            if (start < 0 || end <= start) return null;

            json = json[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var features = new float[FeatureCount];
            int idx = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (idx >= FeatureCount) break;
                features[idx++] = Math.Clamp((float)el.GetDouble(), 0f, 1f);
            }

            return idx == FeatureCount ? features : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ClaimVectorService: score failed for {Ticker} claim", ticker);
            return null;
        }
    }

    // ── Persist vector for a saved claim (fire-and-forget friendly) ───────

    public async Task ComputeAndSaveAsync(
        Guid   claimId,
        string ticker,
        string claimSummary,
        string claimType,
        string sentiment,
        string magnitude,
        bool   isPortent,
        CancellationToken ct = default)
    {
        var features = await ScoreAsync(ticker, claimSummary, claimType, sentiment, magnitude, isPortent, ct);
        if (features is null) return;

        var sig = GetSignature(features);
        var vecJson = JsonSerializer.Serialize(features);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            if (await db.ResearchClaimVectors.AnyAsync(v => v.ClaimId == claimId, ct))
                return;

            db.ResearchClaimVectors.Add(new ResearchClaimVector
            {
                ClaimId      = claimId,
                VectorJson   = vecJson,
                LshSignature = sig,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ClaimVectorService: persist failed for claim {ClaimId}", claimId);
        }
    }

    // ── Hyperplane generation (Gaussian via Box-Muller, seed 1337) ────────

    private static float[][] GenerateHyperplanes()
    {
        var rng    = new Random(Seed);
        var planes = new float[LshBits][];

        for (int i = 0; i < LshBits; i++)
        {
            var plane = new float[FeatureCount];
            for (int j = 0; j < FeatureCount; j += 2)
            {
                var (g1, g2) = BoxMuller(rng);
                plane[j] = g1;
                if (j + 1 < FeatureCount) plane[j + 1] = g2;
            }
            Normalize(plane);
            planes[i] = plane;
        }
        return planes;
    }

    private static (float, float) BoxMuller(Random rng)
    {
        double u1, u2;
        do { u1 = rng.NextDouble(); u2 = rng.NextDouble(); } while (u1 <= double.Epsilon);
        var mag   = Math.Sqrt(-2.0 * Math.Log(u1));
        var angle = 2.0 * Math.PI * u2;
        return ((float)(mag * Math.Cos(angle)), (float)(mag * Math.Sin(angle)));
    }

    private static void Normalize(float[] v)
    {
        float sumSq = 0;
        foreach (var x in v) sumSq += x * x;
        if (sumSq <= 0) return;
        var norm = (float)Math.Sqrt(sumSq);
        for (int i = 0; i < v.Length; i++) v[i] /= norm;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// Where the pre-registered study lives: <c>docs/research/gapper-study-2026-11/</c> (the frozen
/// protocol, hypotheses and params), its <c>data/</c> (fitted profile) and <c>runs/</c> (per-stage
/// logs). Resolved from <c>GAPPER_STUDY_DIR</c>, else by walking up from the running binary to the
/// repository root.
/// </summary>
public sealed class GapperStudyPaths
{
    public const string EnvVar = "GAPPER_STUDY_DIR";
    public const string RelativeDir = "docs/research/gapper-study-2026-11";

    public GapperStudyPaths(string studyDir) => StudyDir = Path.GetFullPath(studyDir);

    public string StudyDir { get; }
    public string ParamsFile => Path.Combine(StudyDir, "params.json");
    public string HypothesesFile => Path.Combine(StudyDir, "hypotheses.json");
    public string DataDir => Path.Combine(StudyDir, "data");
    public string RunsDir => Path.Combine(StudyDir, "runs");
    public string SampleFile(bool smoke) => Path.Combine(StudyDir, smoke ? "sample.smoke.json" : "sample.json");
    public string ProfileFile(bool smoke) => Path.Combine(DataDir, smoke ? "tod-profile.smoke.json" : "tod-profile.json");
    public string UnlockFile(GapperStudyParams p) => Path.Combine(StudyDir, p.HoldoutLock.UnlockFile);
    public string HoldoutRunFile(GapperStudyParams p) => Path.Combine(StudyDir, p.HoldoutLock.RunFile);

    public static GapperStudyPaths Resolve()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return new GapperStudyPaths(env);
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, RelativeDir.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(Path.Combine(candidate, "params.json"))) return new GapperStudyPaths(candidate);
                dir = dir.Parent;
            }
        }
        throw new FileNotFoundException($"The gapper study directory was not found; set {EnvVar} to the folder holding params.json.");
    }
}

/// <summary>
/// The frozen numbers of <c>params.json</c> (IP-T442). Every stage reads them from here; nothing
/// in the pipeline hardcodes a threshold, window, tier or split date.
/// </summary>
public sealed class GapperStudyParams
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public int Version { get; init; }
    public DateTime? FrozenUtc { get; init; }
    public string TimeZone { get; init; } = "America/New_York";
    public UniverseParams Universe { get; init; } = new();
    public CandidateParams Candidate { get; init; } = new();
    public GapperParams Gapper { get; init; } = new();
    public ControlParams Controls { get; init; } = new();
    public DataTierParams DataTiers { get; init; } = new();
    public List<string> DecisionTimesEt { get; init; } = [];
    public FeatureParams Features { get; init; } = new();
    public LabelParams Labels { get; init; } = new();
    public StatisticsParams Statistics { get; init; } = new();
    public CostModelParams CostModel { get; init; } = new();
    public Dictionary<string, SplitRange> Splits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HoldoutLockParams HoldoutLock { get; init; } = new();
    public KillCriteriaParams KillCriteria { get; init; } = new();
    public ExpectedCountsParams ExpectedCounts { get; init; } = new();

    /// <summary>Hypotheses file as loaded beside the params (only the fields the harness checks).</summary>
    [JsonIgnore] public HypothesesHeader Hypotheses { get; private set; } = new();

    [JsonIgnore] public string ParamsSha256 { get; private set; } = "";
    [JsonIgnore] public string HypothesesSha256 { get; private set; } = "";

    public static GapperStudyParams Load(GapperStudyPaths paths)
    {
        var json = File.ReadAllText(paths.ParamsFile);
        var p = Parse(json);
        p.ParamsSha256 = Sha256(json);
        if (File.Exists(paths.HypothesesFile))
        {
            var h = File.ReadAllText(paths.HypothesesFile);
            p.Hypotheses = JsonSerializer.Deserialize<HypothesesHeader>(h, Options) ?? new HypothesesHeader();
            p.HypothesesSha256 = Sha256(h);
        }
        return p;
    }

    public static GapperStudyParams Parse(string json) =>
        JsonSerializer.Deserialize<GapperStudyParams>(json, Options)
        ?? throw new InvalidDataException("params.json is empty.");

    public static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>train | validation | holdout, or null when the day is outside every split.</summary>
    public string? SplitOf(DateOnly dateEt)
    {
        foreach (var (name, range) in Splits)
            if (dateEt >= range.From && dateEt <= range.To) return name.ToLowerInvariant();
        return null;
    }

    public SplitRange Split(string name) =>
        Splits.TryGetValue(name, out var r) ? r : throw new ArgumentException($"params.json has no split named '{name}'.");

    /// <summary>Price tier index (0-based) of a split-adjusted prior close, or -1 outside every tier.</summary>
    public int PriceTierOf(decimal price)
    {
        for (int i = 0; i < Controls.PriceTiers.Count; i++)
        {
            var t = Controls.PriceTiers[i];
            if (price >= t[0] && price < t[1]) return i;
        }
        return -1;
    }

    /// <summary>Each decision time of the schedule as (label, day offset, ET time-of-day).</summary>
    public IReadOnlyList<DecisionTime> DecisionTimes() => DecisionTimesEt.Select(DecisionTime.Parse).ToList();

    public static TimeSpan ParseEt(string hhmm) => TimeSpan.ParseExact(hhmm.Trim(), @"hh\:mm", CultureInfo.InvariantCulture);

    public sealed class UniverseParams
    {
        public string AssetClass { get; init; } = "us_equity";
        public List<string> AssetStatus { get; init; } = ["active", "inactive"];
        public decimal PriceMinInclusive { get; init; }
        public decimal PriceMaxExclusive { get; init; }
        public decimal Adv20DollarMin { get; init; }
        public int MinSessionsOfLast20 { get; init; }
        public BeforeAfter SplitExclusionWindowDays { get; init; } = new();
        public UnadjustedSplitGuardParams UnadjustedSplitGuard { get; init; } = new();
        public List<string> AdrNamePatterns { get; init; } = [];
        public int ReusedTickerGapSessions { get; init; }
    }

    public sealed class UnadjustedSplitGuardParams
    {
        public double MaxDailyUp { get; init; }
        public double MaxDailyDown { get; init; }
        public int LookbackDays { get; init; }
    }

    public sealed class BeforeAfter
    {
        public int Before { get; init; }
        public int After { get; init; }
    }

    public sealed class CandidateParams
    {
        public double HighOverPrevCloseMin { get; init; }
        public double VolumeOverAdv20Min { get; init; }
    }

    public sealed class GapperParams
    {
        public StartEnd Window { get; init; } = new();
        public Dictionary<string, double> Tiers { get; init; } = new();
        public string PremarketCutoffEt { get; init; } = "09:30";
        public decimal PremarketDollarVolumeFloor { get; init; }

        /// <summary>Tiers ascending by threshold.</summary>
        public IReadOnlyList<KeyValuePair<string, double>> OrderedTiers() => Tiers.OrderBy(t => t.Value).ToList();
    }

    public sealed class StartEnd
    {
        public string StartEt { get; init; } = "04:00";
        public string EndEt { get; init; } = "20:00";
    }

    public sealed class ControlParams
    {
        public int PerGapperDay { get; init; } = 1;
        public List<string> MatchOn { get; init; } = [];
        public List<decimal[]> PriceTiers { get; init; } = [];
        public string HashAlgorithm { get; init; } = "SHA-256";
        public string HashInput { get; init; } = "{symbol}|{D:yyyy-MM-dd}|{salt}";
        public string Salt { get; init; } = "";
        public List<string> FallbackOrder { get; init; } = [];
    }

    public sealed class DataTierParams
    {
        public string AlpacaFeed { get; init; } = "sip";
        public string AlpacaAdjustment { get; init; } = "raw";
        public int AlpacaRequestsPerMinute { get; init; } = 200;
        public int EdgarRequestsPerSecond { get; init; } = 10;
        public string EdgarHttpClientName { get; init; } = "edgar";
        public FromTo DailyBars { get; init; } = new();
        public MinuteBarParams MinuteBars { get; init; } = new();
        public string MinuteBarStore { get; init; } = "";
        public string ResearchDb { get; init; } = "";
        public string ResearchDbGuardPattern { get; init; } = "_Research_";
        public int LocalDbCapGb { get; init; }
        public BeforeAfter NewsWindowDays { get; init; } = new();
        public int NewsPageSize { get; init; } = 50;
        public List<string> EdgarForms { get; init; } = [];
        public BeforeAfter EdgarAcceptanceWindowDays { get; init; } = new();
        public int EdgarFullIndexQuarters { get; init; }
        public List<string> CikSources { get; init; } = [];
        public List<string> OptionalTiers { get; init; } = [];
        public double SipProvenanceMinShare { get; init; }

        /// <summary>The directory part of <see cref="MinuteBarStore"/> (everything before <c>{SYMBOL}</c>).</summary>
        public string MinuteBarRoot()
        {
            var i = MinuteBarStore.IndexOf("{SYMBOL}", StringComparison.OrdinalIgnoreCase);
            var root = i < 0 ? MinuteBarStore : MinuteBarStore[..i];
            return root.TrimEnd('\\', '/');
        }
    }

    public sealed class FromTo
    {
        public DateOnly From { get; init; }
        public DateOnly To { get; init; }
    }

    public sealed class MinuteBarParams
    {
        public string StartEt { get; init; } = "04:00";
        public string EndEt { get; init; } = "20:00";
        public int PriorDays { get; init; } = 5;
        public bool IncludeControlDay { get; init; } = true;
    }

    public sealed class FeatureParams
    {
        public int Adv20Sessions { get; init; } = 20;
        public TodProfileParams TodProfile { get; init; } = new();
        public OwnProfileParams OwnProfile { get; init; } = new();
        public List<int> VelocityWindowsMin { get; init; } = [];
        public int AccelerationWindowMin { get; init; }
        public int VolRocWindowMin { get; init; }
        public List<int> TradesWindowsMin { get; init; } = [];
        public CorwinSchultzParams CorwinSchultz { get; init; } = new();
        public double AbnormalVolumeMultiple { get; init; } = 5;
        public int RunUpDays { get; init; } = 5;
        public int DaysSinceMaxDays { get; init; } = 365;
        public Form4Params Form4 { get; init; } = new();
        public CatalystParams Catalyst { get; init; } = new();
        public int PriorDayTopMoverMaxRank { get; init; } = 100;
        public LeeMyklandParams LeeMykland { get; init; } = new();
        public bool ForwardFillEmptyMinutes { get; init; } = true;
    }

    public sealed class TodProfileParams
    {
        public string FitOn { get; init; } = "train";
        public string Sample { get; init; } = "controlDays";
        public string Statistic { get; init; } = "median";
        public bool ByAdvDecile { get; init; } = true;
        public string File { get; init; } = "data/tod-profile.json";
    }

    public sealed class OwnProfileParams
    {
        public int PriorDays { get; init; } = 5;
        public int ShrinkageK { get; init; } = 5;
    }

    public sealed class CorwinSchultzParams
    {
        public int PriorDays { get; init; } = 5;
        public string SessionStartEt { get; init; } = "04:00";
        public string SessionEndEt { get; init; } = "20:00";
        public int MinBars { get; init; } = 60;
    }

    public sealed class Form4Params
    {
        public int LookbackDays { get; init; } = 30;
        public string TransactionCode { get; init; } = "P";
    }

    public sealed class CatalystParams
    {
        public int LookbackHours { get; init; } = 24;
        public List<string> Types { get; init; } = [];
        public List<string> SourceTiers { get; init; } = [];
        public List<string> WireSources { get; init; } = [];
        public List<string> PromotionalSources { get; init; } = [];
        public string PromotionalRule { get; init; } = "";
        public int NoveltyLookbackDays { get; init; } = 90;
        public int NoveltyExcludeHours { get; init; } = 24;
        public bool ClassifyOncePerId { get; init; } = true;
    }

    public sealed class LeeMyklandParams
    {
        public int LocalWindowBars { get; init; } = 60;
        public double Alpha { get; init; } = 0.01;
    }

    public sealed class LabelParams
    {
        public string Entry { get; init; } = "nextBarOpenAfterT";
        public List<string> HorizonsEt { get; init; } = [];
        public Reach20Params Reach20 { get; init; } = new();
        public List<string> HodBuckets { get; init; } = [];
        public double FadeFlagThreshold { get; init; } = -0.30;
        public string ExtensionFromOpenAtEt { get; init; } = "09:45";
        public VelocityDeathParams VelocityDeath { get; init; } = new();
        public HaltProxyParams HaltProxy { get; init; } = new();
        public VwapParams Vwap { get; init; } = new();
    }

    public sealed class Reach20Params
    {
        public string ByEt { get; init; } = "09:45";
        public double Threshold { get; init; } = 0.20;
        public string RelativeTo { get; init; } = "prevClose";
    }

    public sealed class VelocityDeathParams
    {
        public int Window { get; init; } = 3;
        public double ArmThresholdPctPerMin { get; init; } = 0.5;
        public int ConsecutiveNegativeBars { get; init; } = 2;
        public string SearchFromEt { get; init; } = "08:30";
        public string SearchToEt { get; init; } = "16:00";
    }

    public sealed class HaltProxyParams
    {
        public int MinConsecutiveEmptyRthMinutes { get; init; } = 5;
        public int RequirePrintsInPriorMinutes { get; init; } = 5;
    }

    public sealed class VwapParams
    {
        public string AnchorEt { get; init; } = "04:00";
        public int MinConsecutiveAboveBeforeLoss { get; init; } = 3;
    }

    public sealed class StatisticsParams
    {
        public int Seed { get; init; }
        public double BhQ { get; init; }
        public double HolmAlpha { get; init; }
        public List<string> HolmOverPrimariesWithPValue { get; init; } = [];
        public int BucketBins { get; init; }
        public int MinBucketN { get; init; }
        public int BootstrapDraws { get; init; }
        public int PlaceboDraws { get; init; }
        public string ClusterBy { get; init; } = "";
        public double CiLevel { get; init; }
        public double SpaAlpha { get; init; }
        public double DsrMin { get; init; }
        public bool F3NeverSignificant { get; init; }
    }

    public sealed class CostModelParams
    {
        public JsonElement Spread { get; init; }
        public JsonElement Impact { get; init; }
        public double RoundTripFloor { get; init; }
        public string HaltFillRule { get; init; } = "";
        public JsonElement WorthTrading { get; init; }
    }

    public sealed class SplitRange
    {
        public DateOnly From { get; init; }
        public DateOnly To { get; init; }
        public int? RunsAllowed { get; init; }
    }

    public sealed class HoldoutLockParams
    {
        public string UnlockFile { get; init; } = "holdout-unlock.txt";
        public string UnlockContent { get; init; } = "";
        public string RunFile { get; init; } = "holdout-run.json";
        public bool RefuseSecondRun { get; init; } = true;
        public bool RefuseUncommittedChanges { get; init; } = true;
        public bool RefuseWhenFrozenUtcNull { get; init; } = true;
        public List<string> PostHocFlagFiles { get; init; } = [];
    }

    public sealed class KillCriteriaParams
    {
        public int MinTier20Train { get; init; }
        public double MinSipProvenanceShare { get; init; }
        public bool H11MustPassOnValidation { get; init; }
        public bool PostUnlockEditVoids { get; init; }
        public double H8DropIfCikCoverageBelow { get; init; }
    }

    public sealed class ExpectedCountsParams
    {
        public int CandidateSymbolDays { get; init; }
        public int Tier20Days { get; init; }
    }

    public sealed class HypothesesHeader
    {
        public int Version { get; init; }
        public DateTime? FrozenUtc { get; init; }
    }
}

/// <summary>One entry of the decision-time schedule: "D-1 16:00" is day offset -1 at 16:00 ET; "08:30" is day offset 0.</summary>
public sealed record DecisionTime(string Label, int DayOffset, TimeSpan TimeEt)
{
    public static DecisionTime Parse(string label)
    {
        var text = label.Trim();
        int offset = 0;
        if (text.StartsWith("D", StringComparison.OrdinalIgnoreCase))
        {
            var space = text.IndexOf(' ');
            var dayPart = space < 0 ? text : text[..space];
            var rest = space < 0 ? "" : text[(space + 1)..];
            if (dayPart.Length > 1) offset = int.Parse(dayPart[1..], CultureInfo.InvariantCulture);
            text = rest;
        }
        return new DecisionTime(label.Trim(), offset, GapperStudyParams.ParseEt(text));
    }
}

using System.Text.Json;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>
/// The exploration panel at one decision time, dense in memory: one row per population symbol-day
/// of train, its features (NaN = missing) and its labels (NaN = undefined), row-major.
/// </summary>
public sealed class ExploreTable
{
    public required string Decision { get; init; }
    public required IReadOnlyList<string> FeatureNames { get; init; }
    public required IReadOnlyList<string> LabelNames { get; init; }
    public required string[] Symbols { get; init; }
    public required DateOnly[] Days { get; init; }
    public required bool[] IsCandidate { get; init; }
    public required double[] Weights { get; init; }
    public required float[] X { get; init; }
    public required float[] L { get; init; }

    public int Rows => Symbols.Length;
    public int FeatureCount => FeatureNames.Count;

    public int Feature(string name) => IndexOf(FeatureNames, name);
    public int Label(string name) => IndexOf(LabelNames, name);

    public float Value(int row, int feature) => X[row * FeatureNames.Count + feature];
    public float LabelValue(int row, int label) => L[row * LabelNames.Count + label];

    /// <summary>The label as a bool per row; null when undefined (NaN).</summary>
    public bool?[] Binary(string label)
    {
        int j = Label(label);
        var result = new bool?[Rows];
        for (int r = 0; r < Rows; r++)
        {
            float v = LabelValue(r, j);
            result[r] = float.IsNaN(v) ? null : v > 0.5f;
        }
        return result;
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++) if (names[i] == name) return i;
        return -1;
    }
}

/// <summary>
/// Reads and writes the exploration panel cache (IP-T453): one binary file per decision time with a
/// JSON schema beside it, written by the explore <c>build</c> step from the shared feature code and
/// the frozen labeler, so every later trial reads features in seconds instead of recomputing them.
/// Rows are stored sparse (key index, value) so a feature added later costs nothing on old rows.
/// </summary>
public static class ExplorePanelFile
{
    private sealed record Schema(string Decision, string Split, List<string> Features, List<string> Labels, int Rows, DateTime BuiltUtc);

    public static string BinFile(string dir, string decision) => Path.Combine(dir, $"panel-train-{Safe(decision)}.bin");
    public static string SchemaFile(string dir, string decision) => Path.Combine(dir, $"panel-train-{Safe(decision)}.schema.json");
    private static string Safe(string decision) => decision.Replace(":", "").Replace(' ', '_').Replace('-', 'm');

    public static bool Exists(string dir, string decision) => File.Exists(BinFile(dir, decision)) && File.Exists(SchemaFile(dir, decision));

    /// <summary>A streaming writer for one decision time.</summary>
    public sealed class Writer : IDisposable
    {
        private readonly BinaryWriter bin;
        private readonly Dictionary<string, ushort> features = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ushort> labels = new(StringComparer.Ordinal);
        private readonly string dir;
        private readonly string decision;
        private readonly object gate = new();
        private int rows;
        private bool finished;

        public Writer(string dir, string decision)
        {
            this.dir = dir;
            this.decision = decision;
            Directory.CreateDirectory(dir);
            bin = new BinaryWriter(File.Create(BinFile(dir, decision) + ".tmp"));
        }

        public int RowsWritten => rows;

        public void Add(string symbol, DateOnly day, bool isCandidate, double weight, IEnumerable<KeyValuePair<string, double>> x, IEnumerable<KeyValuePair<string, double>> l)
        {
            var fx = x.Where(kv => double.IsFinite(kv.Value)).ToList();
            var fl = l.Where(kv => double.IsFinite(kv.Value)).ToList();
            lock (gate)
            {
                bin.Write(symbol);
                bin.Write(day.DayNumber);
                bin.Write(isCandidate);
                bin.Write(weight);
                WriteSparse(fx, features);
                WriteSparse(fl, labels);
                rows++;
            }
        }

        private void WriteSparse(List<KeyValuePair<string, double>> values, Dictionary<string, ushort> keys)
        {
            bin.Write((ushort)values.Count);
            foreach (var (k, v) in values)
            {
                if (!keys.TryGetValue(k, out var id))
                {
                    if (keys.Count >= ushort.MaxValue) throw new InvalidOperationException("Too many distinct keys in the explore panel.");
                    keys[k] = id = (ushort)keys.Count;
                }
                bin.Write(id);
                bin.Write((float)v);
            }
        }

        /// <summary>Close the file and write its schema; only then does the cache count as built.</summary>
        public void Finish(DateTime utc)
        {
            lock (gate)
            {
                bin.Dispose();
                var target = BinFile(dir, decision);
                File.Move(target + ".tmp", target, overwrite: true);
                var schema = new Schema(decision, "train", features.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList(), labels.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList(), rows, utc);
                File.WriteAllText(SchemaFile(dir, decision), JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }));
                finished = true;
            }
        }

        public void Dispose()
        {
            if (!finished)
            {
                bin.Dispose();
                try { File.Delete(BinFile(dir, decision) + ".tmp"); } catch (IOException) { }
            }
        }
    }

    /// <summary>Read one decision time's panel; features are sorted by name so trials see a stable column order.</summary>
    public static ExploreTable Read(string dir, string decision)
    {
        var schema = JsonSerializer.Deserialize<Schema>(File.ReadAllText(SchemaFile(dir, decision)))!;
        if (!string.Equals(schema.Split, "train", StringComparison.Ordinal)) throw new InvalidOperationException("The explore panel holds train rows only.");
        var fOrder = schema.Features.Select((name, i) => (name, i)).OrderBy(x => x.name, StringComparer.Ordinal).ToList();
        var lOrder = schema.Labels.Select((name, i) => (name, i)).OrderBy(x => x.name, StringComparer.Ordinal).ToList();
        var fPos = new int[schema.Features.Count];
        for (int k = 0; k < fOrder.Count; k++) fPos[fOrder[k].i] = k;
        var lPos = new int[schema.Labels.Count];
        for (int k = 0; k < lOrder.Count; k++) lPos[lOrder[k].i] = k;

        int n = schema.Rows, d = fOrder.Count, m = lOrder.Count;
        var symbols = new string[n]; var days = new DateOnly[n]; var cand = new bool[n]; var weights = new double[n];
        var x = new float[(long)n * d]; var l = new float[(long)n * m];
        Array.Fill(x, float.NaN); Array.Fill(l, float.NaN);
        using var reader = new BinaryReader(File.OpenRead(BinFile(dir, decision)));
        for (int r = 0; r < n; r++)
        {
            symbols[r] = reader.ReadString();
            days[r] = DateOnly.FromDayNumber(reader.ReadInt32());
            cand[r] = reader.ReadBoolean();
            weights[r] = reader.ReadDouble();
            int nf = reader.ReadUInt16();
            for (int i = 0; i < nf; i++) { int k = reader.ReadUInt16(); float v = reader.ReadSingle(); x[(long)r * d + fPos[k]] = v; }
            int nl = reader.ReadUInt16();
            for (int i = 0; i < nl; i++) { int k = reader.ReadUInt16(); float v = reader.ReadSingle(); l[(long)r * m + lPos[k]] = v; }
        }
        return new ExploreTable
        {
            Decision = decision, FeatureNames = fOrder.Select(f => f.name).ToList(), LabelNames = lOrder.Select(f => f.name).ToList(),
            Symbols = symbols, Days = days, IsCandidate = cand, Weights = weights, X = x, L = l,
        };
    }
}

/// <summary>Flattens a snapshot and the frozen labels into named numbers for the explore panel.</summary>
public static class ExploreColumns
{
    public static IEnumerable<KeyValuePair<string, double>> Features(GapperFormationSnapshot s)
    {
        static KeyValuePair<string, double> F(string k, double? v) => new(k, v ?? double.NaN);
        yield return F("GapPct", s.GapPct);
        yield return F("LogPmCumVol", Math.Log(1 + Math.Max(0, s.PmCumVol)));
        yield return F("VolRatioTod", s.VolRatioTod);
        yield return F("OwnProfileRatio", s.OwnProfileRatio);
        yield return F("Velocity1m", s.Velocity1m);
        yield return F("Velocity3m", s.Velocity3m);
        yield return F("Velocity5m", s.Velocity5m);
        yield return F("Velocity15m", s.Velocity15m);
        yield return F("Acceleration", s.Acceleration);
        yield return F("VolumeRoc", s.VolumeRoc);
        yield return F("TradesProxy", s.TradesProxy);
        yield return F("SpreadProxy", s.SpreadProxy);
        yield return F("DistToPmHighPct", s.DistToPmHighPct);
        yield return F("MinsSinceFirstAbnormal", s.MinsSinceFirstAbnormal);
        yield return F("AbnormalReturnD1", s.AbnormalReturnD1);
        yield return F("AbnormalReturnD2", s.AbnormalReturnD2);
        yield return F("AbnormalReturnD3", s.AbnormalReturnD3);
        yield return F("AbnormalReturnD4", s.AbnormalReturnD4);
        yield return F("AbnormalReturnD5", s.AbnormalReturnD5);
        yield return F("VolumeRatioD1", s.VolumeRatioD1);
        yield return F("VolumeRatioD2", s.VolumeRatioD2);
        yield return F("VolumeRatioD3", s.VolumeRatioD3);
        yield return F("VolumeRatioD4", s.VolumeRatioD4);
        yield return F("VolumeRatioD5", s.VolumeRatioD5);
        yield return F("DaysSinceReverseSplit", s.DaysSinceReverseSplit);
        yield return F("DaysSince424B", s.DaysSince424B);
        yield return F("DaysSinceS1", s.DaysSinceS1);
        yield return F("CatalystScore", s.CatalystScore);
        yield return F("CatalystAgeMinutes", s.CatalystAgeMinutes);
        yield return F("CatalystDirection", s.CatalystDirection);
        yield return F("CatalystStampIsUpperBound", s.CatalystStampIsUpperBound is { } b ? (b ? 1 : 0) : null);
        yield return F("Halted", s.Halted is { } h ? (h ? 1 : 0) : null);
        if (!string.IsNullOrEmpty(s.CatalystPreClass)) yield return F("Pre." + s.CatalystPreClass, 1);
        foreach (var (k, v) in s.Extra) yield return F("x." + k, v);
    }

    private static double? Minutes(TimeOnly? t) => t is { } x ? x.Hour * 60 + x.Minute : null;

    public static IEnumerable<KeyValuePair<string, double>> Labels(GapperDayLabels day, GapperForwardLabels fwd, bool isGapper, string? gapTier)
    {
        static KeyValuePair<string, double> F(string k, double? v) => new(k, v ?? double.NaN);
        static double? B(bool? b) => b is { } x ? (x ? 1 : 0) : null;
        yield return F("Reach20By0945", B(day.Reach20By0945));
        yield return F("IsGapper", isGapper ? 1 : 0);
        yield return F("Tier50", gapTier is "Tier50" or "Tier100" ? 1 : 0);
        yield return F("Tier100", gapTier is "Tier100" ? 1 : 0);
        yield return F("HasEntry", fwd.EntryRef is null ? 0 : 1);
        foreach (var (h, v) in fwd.FwdMax) yield return F("FwdMax" + h.Replace(":", ""), v);
        foreach (var (h, v) in fwd.FwdClose) yield return F("FwdClose" + h.Replace(":", ""), v);
        yield return F("HodMinEt", Minutes(day.HodTimeEt));
        yield return F("HodPmMinEt", Minutes(day.HodTimePmEt));
        yield return F("HodRthMinEt", Minutes(day.HodTimeRthEt));
        yield return F("VelocityDeathMinEt", Minutes(day.VelocityDeathTimeEt));
        yield return F("VwapLossMinEt", Minutes(day.VwapLossTimeEt));
        yield return F("DeathToHodMin", day.DeathToHodMin);
        yield return F("Fade", day.Fade);
        yield return F("FadeFlag30", B(day.FadeFlag30));
        yield return F("CloseBelowOpen", B(day.CloseBelowOpen));
        yield return F("ExtensionFromOpen0945", day.ExtensionFromOpen0945);
        yield return F("HaltFlag", B(day.HaltFlag));
    }
}

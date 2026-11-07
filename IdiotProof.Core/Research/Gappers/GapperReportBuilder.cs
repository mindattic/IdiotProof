using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Core.Services;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>A descriptive summary of the post-registration velocity-death variant on one split (never a verdict).</summary>
public sealed record PostRegistrationSummary(string Split, string Feature, int GapperDays, int WithDeathInWindow, double? MedianMinutesAfterPmHigh, int WindowMinutes);

/// <summary>Everything the results document is rendered from; built by the stage, rendered by <see cref="GapperReportBuilder"/>.</summary>
public sealed class GapperReportModel
{
    public required GapperStudyParams Params { get; init; }
    public required GapperHypothesisSet Hypotheses { get; init; }
    public DateTime GeneratedUtc { get; init; }
    public string? Head { get; init; }
    public string? FrozenCommit { get; init; }
    public string StudyDirName { get; init; } = "gapper-study-2026-11";
    public JsonObject? Sample { get; init; }
    public StudyStatus? Status { get; init; }
    public List<HypothesisRun> Runs { get; init; } = [];
    public bool HoldoutRunExists { get; init; }
    public int HoldoutRunsWithheld { get; init; }
    public DateTime? HoldoutRunStartUtc { get; init; }
    public List<(string Hash, string AuthorDate, string Subject)> PostUnlockCommits { get; init; } = [];
    public HashSet<string> PostHocHypotheses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double? SpreadMultiplier { get; init; }
    public string SpreadMultiplierSource { get; init; } = "";
    public IReadOnlyCollection<string> PostRegistrationFeatures { get; init; } = [];
    public List<PostRegistrationSummary> PostRegistrationSummaries { get; init; } = [];
    public List<ExportedFile> Exports { get; init; } = [];
    public string? ExportNote { get; init; }

    /// <summary>SIP provenance across train+validation study minute bars, computed live from the file store; takes precedence over <see cref="Sample"/>'s cached pull stats.</summary>
    public double? LiveSipSharePct { get; init; }

    /// <summary>Amendment 01 (IP-T448), or null when its file is absent.</summary>
    public GapperAmendment? Amendment { get; init; }
}

/// <summary>
/// Renders <c>docs/research/gapper-study-2026-11.md</c> in the evidence sweep's shape: the fixed
/// protocol (linked, never restated), the sample, the data tiers, one verdict table per family, the
/// catalyst-latency gate, exit timing with the post-registration variant marked, what it says, the
/// kill criteria and the export manifest. Holdout rows appear only when <c>holdout-run.json</c>
/// exists; hypotheses edited after the unlock are marked post-hoc and carry no verdict. Pure.
/// </summary>
public static class GapperReportBuilder
{
    private static readonly string[] FamilyTitles = ["F1 prediction buckets", "F2 catalyst groups", "F3 timing (descriptive)", "F4 strategy grids", "F5 structural"];
    private static readonly string[] Splits = ["train", "validation", "holdout"];

    public static string Build(GapperReportModel m)
    {
        var p = m.Params;
        var sb = new StringBuilder();
        var runs = m.HoldoutRunExists ? m.Runs : m.Runs.Where(r => r.Split != "holdout").ToList();
        sb.AppendLine("# Gapper study — results (IP-T442)");
        sb.AppendLine();
        sb.AppendLine($"Generated {EasternTime.Format(m.GeneratedUtc, "yyyy-MM-dd h:mm tt")} ET by `research gappers report` at `{Short(m.Head)}`. " +
            $"The protocol, hypotheses and parameters were frozen at `{Short(m.FrozenCommit)}` before any data was pulled and are not restated here; " +
            $"this file is appended to, the protocol is never edited. Paper-only research: no orders, read-only market data.");
        sb.AppendLine();

        Protocol(sb, m);
        Sample(sb, m);
        DataTiers(sb, m);
        Families(sb, m, runs);
        Amendment(sb, m, runs);
        Gate(sb, m, runs);
        ExitTiming(sb, m, runs);
        WhatThisSays(sb, m, runs);
        KillCriteria(sb, m, runs);
        Promotion(sb, m, runs);
        Exports(sb, m);
        RunFiles(sb, runs);
        return sb.ToString();
    }

    private static void Protocol(StringBuilder sb, GapperReportModel m)
    {
        var p = m.Params;
        sb.AppendLine("## Protocol (fixed before running)");
        sb.AppendLine();
        sb.AppendLine($"[`{m.StudyDirName}/protocol.md`]({m.StudyDirName}/protocol.md), [`hypotheses.json`]({m.StudyDirName}/hypotheses.json) " +
            $"(sha256 `{Short(m.Hypotheses.Sha256, 12)}`, frozen {Utc(m.Hypotheses.FrozenUtc)}) and [`params.json`]({m.StudyDirName}/params.json) " +
            $"(sha256 `{Short(p.ParamsSha256, 12)}`, frozen {Utc(p.FrozenUtc)}). Families: F1 prediction buckets (HoldoutBucketEvaluator, BH q < {p.Statistics.BhQ}), " +
            $"F2 catalyst groups (same-day BMP with a {p.Statistics.PlaceboDraws}-draw within-day placebo, BH), F3 timing (descriptive intervals at {p.Statistics.CiLevel:P0}; never \"significant\"), " +
            $"F4 strategy grids (SPA p < {p.Statistics.SpaAlpha} and DSR >= {p.Statistics.DsrMin}), F5 structural (day-clustered bootstrap, BH). " +
            $"Primaries with a p-value ({string.Join(", ", p.Statistics.HolmOverPrimariesWithPValue)}) are additionally Holm-corrected at α = {p.Statistics.HolmAlpha}. " +
            $"Seed {p.Statistics.Seed}, {p.Statistics.BootstrapDraws} bootstrap draws, minimum bucket {p.Statistics.MinBucketN}.");
        sb.AppendLine();
        sb.AppendLine("| split | days | use |");
        sb.AppendLine("|---|---|---|");
        foreach (var (name, range) in p.Splits)
            sb.AppendLine($"| {name} | {range.From:yyyy-MM-dd} .. {range.To:yyyy-MM-dd} | {(name.Equals("train", StringComparison.OrdinalIgnoreCase) ? "profile fit, exploratory tests, grid selection" : name.Equals("validation", StringComparison.OrdinalIgnoreCase) ? "replication, H11, H12 SPA" : "opened once, surviving set only")} |");
        sb.AppendLine();
        sb.AppendLine("**Verdict rule.** `supported` only when the effect meets the frozen minimum **and** the family correction (and Holm, for primaries) passes; " +
            "`refuted` when the interval sits entirely where the claim cannot hold; `undecided` otherwise; `void` when the sample is below the minimum bucket, the population is missing, or the hypothesis was dropped or edited post-unlock. " +
            "F3 verdicts come from intervals alone.");
        sb.AppendLine();
    }

    private static void Sample(StringBuilder sb, GapperReportModel m)
    {
        sb.AppendLine("## Sample");
        sb.AppendLine();
        var liveSplitNames = m.Status?.Splits.Select(x => x.Split) ?? [];
        var sampleSplitNames = (m.Sample?["splits"] as JsonObject)?.Select(kv => kv.Key) ?? [];
        var splitNames = sampleSplitNames.Union(liveSplitNames).ToList();
        if (splitNames.Count == 0)
        {
            sb.AppendLine("`sample.json` has not been written yet and the study has no universe rows (run `build-universe` and `pull --tier bars`).");
            sb.AppendLine();
            return;
        }
        sb.AppendLine($"Universe days / candidates / gapper tiers / controls below are live from `GapperStudyUniverseDays` and `GapperStudyEvents` " +
            $"(not the cached `sample.json`, which can lag a running pull); excluded counts, the premarket/open split and symbol counts fall back to " +
            (m.Sample is null
                ? $"`{m.StudyDirName}/sample.json`, which has not been written yet."
                : $"`{m.StudyDirName}/sample.json` (written {m.Sample["writtenUtc"]?.GetValue<string>() ?? "?"}, frozen commit `{Short(m.Sample["frozenCommit"]?.GetValue<string>())}`) where the live status does not carry them."));
        sb.AppendLine();
        sb.AppendLine("| split | universe days | candidates | tier20 | tier50 | tier100 | premarket / open | controls | excluded | symbols | dates |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var name in splitNames)
        {
            if (name == "holdout" && !m.HoldoutRunExists)
            {
                sb.AppendLine($"| holdout | withheld | withheld | withheld | withheld | withheld | withheld | withheld | withheld | withheld | locked |");
                continue;
            }
            var live = m.Status?.Splits.FirstOrDefault(x => x.Split == name);
            var sp = m.Sample?["splits"] is JsonObject splits && splits[name] is JsonObject node ? node : null;
            var g = sp?["gappers"] as JsonObject;
            object? universeDays = live?.UniverseDays ?? (object?)sp?["universeDays"];
            object? candidates = live?.Candidates ?? (object?)sp?["candidates"];
            object? tier20 = live?.Tier20 ?? (object?)g?["tier20"];
            object? tier50 = live?.Tier50 ?? (object?)g?["tier50"];
            object? tier100 = live?.Tier100 ?? (object?)g?["tier100"];
            object? controls = live?.Controls ?? (object?)sp?["controls"];
            string dates = live is { First: { } f, Last: { } l } ? $"{f:yyyy-MM-dd} .. {l:yyyy-MM-dd}" : $"{sp?["firstDate"]} .. {sp?["lastDate"]}";
            sb.AppendLine($"| {name} | {universeDays} | {candidates} | {tier20} | {tier50} | {tier100} | {g?["premarket"]} / {g?["open"]} | {controls} | {sp?["excluded"]} | {sp?["symbols"]} | {dates} |");
        }
        sb.AppendLine();
        if (m.Sample?["cikCoverage"] is JsonObject cik)
            sb.AppendLine($"CIK coverage of candidate symbols: {cik["mapped"]}/{cik["candidateSymbols"]} ({cik["coveragePct"]}%); H8 dropped: {cik["h8Dropped"]}.");
        sb.AppendLine($"SIP share of train+validation study minute bars (live): {(m.LiveSipSharePct is { } sip ? Pct(sip) : "not yet measurable")}.");
        sb.AppendLine();
    }

    private static void DataTiers(StringBuilder sb, GapperReportModel m)
    {
        sb.AppendLine("## Data tiers");
        sb.AppendLine();
        var st = m.Status;
        var multiplier = $"Premarket spread multiplier: {(m.SpreadMultiplier is { } x ? x.ToString("0.000", CultureInfo.InvariantCulture) : "not calibrated")} ({m.SpreadMultiplierSource}).";
        if (st is null) { sb.AppendLine("Status unavailable. " + multiplier); sb.AppendLine(); return; }
        sb.AppendLine("| tier | content | rows |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| T1 | daily bars, every Alpaca us_equity asset | {st.DailyBarRows:N0} rows, {st.DailySymbols:N0} symbols |");
        sb.AppendLine($"| T2 | 1-minute 04:00–20:00 ET bars (file store `{st.BarRoot}`) | {string.Join("; ", st.Splits.Where(s => s.Split != "holdout" || m.HoldoutRunExists).Select(s => $"{s.Split} {s.BarCoveragePct:0.0}% of symbol-days"))} |");
        sb.AppendLine($"| T3 | Alpaca news (backfilled catalyst rows) | {st.NewsRows:N0} |");
        sb.AppendLine($"| T4 | EDGAR form index | {st.EdgarRows:N0} rows, {st.EdgarWithAcceptance:N0} with acceptance time |");
        sb.AppendLine($"| T5 | ticker ↔ CIK | {st.CikMapped:N0}/{st.CikTotal:N0} mapped |");
        sb.AppendLine($"| T6 | splits | {st.SplitRows:N0} |");
        sb.AppendLine($"| panel | snapshots / labels | {string.Join("; ", st.Splits.Where(s => s.Split != "holdout" || m.HoldoutRunExists).Select(s => $"{s.Split} {s.Snapshots:N0} / {s.Labels:N0}"))} |");
        sb.AppendLine();
        sb.AppendLine($"Time-of-day profile: {(st.ProfileExists ? "`data/tod-profile.json` present (fit on train control days)" : "not fit")}. {multiplier} Holdout: {st.HoldoutState}.");
        sb.AppendLine();
    }

    private static void Families(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        sb.AppendLine("## Results by family");
        sb.AppendLine();
        sb.AppendLine("Every hypothesis on every split it was run on, including failures. Corrections are recomputed over the latest run of each hypothesis per split. " +
            "Secondaries (H13–H17) are leads, never confirmations. A hypothesis marked **post-hoc** was edited after the holdout unlock and carries no verdict.");
        sb.AppendLine();
        foreach (var family in FamilyTitles)
        {
            var code = family[..2];
            var members = m.Hypotheses.Hypotheses.Where(h => h.Family == code).ToList();
            sb.AppendLine($"### {family}");
            sb.AppendLine();
            sb.AppendLine("| hypothesis | split | n (days) | effect vs minimum | corrected statistic | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var h in members)
            {
                var own = runs.Where(r => r.Id.Equals(h.Id, StringComparison.OrdinalIgnoreCase)).OrderBy(r => Array.IndexOf(Splits, r.Split)).ToList();
                string name = $"{h.Id}{(h.Primary ? "" : " (secondary)")}{(m.PostHocHypotheses.Contains(h.Id) ? " **post-hoc**" : "")}";
                if (own.Count == 0)
                {
                    sb.AppendLine($"| {name} | — | — | — | — | not run |");
                    continue;
                }
                foreach (var r in own)
                {
                    bool postHoc = m.PostHocHypotheses.Contains(h.Id);
                    foreach (var part in r.Parts)
                    {
                        string label = r.Parts.Count > 1 ? $"{name} · {part.Name}" : name;
                        string verdict = postHoc ? "void (post-hoc)" : part.Verdict;
                        string flag = r.Inputs.PostRegistrationFeaturesUsed.Count > 0 ? " ⚠ post-registration feature" : "";
                        sb.AppendLine($"| {label} | {r.Split} | {part.N:N0} ({part.Days:N0}) | {Effect(part)} | {Corrected(r, part, m.Params)} | **{verdict}**{flag} |");
                    }
                }
            }
            sb.AppendLine();
        }
    }

    private static void Amendment(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        if (m.Amendment is not { } a) return;
        sb.AppendLine($"## Amendment {a.Id}: population-weighted detection and strategy ({a.Ticket})");
        sb.AppendLine();
        sb.AppendLine($"Registered {Utc(a.RegisteredUtc)} before any test on study data ([`amendment-01.md`]({m.StudyDirName}/amendment-01.md), sha256 `{Short(a.Sha256, 12)}`). " +
            "The frozen H1, H9, H11 and H12 are asked among candidate days, which are selected on the day's own outcome, so they bound from above what the same rule achieves on the live universe. " +
            $"Each amended hypothesis asks the same question with the same thresholds over every candidate day (weight 1) plus a seeded {a.PopulationSample.Rate:P0} sample of non-candidate universe days (weight {a.SampleWeight}). " +
            "Amended runs are corrected as their own set, so no frozen q or Holm p moves. Promotion requires the frozen conditions **and** H11p and H12p.");
        sb.AppendLine();
        sb.AppendLine("| hypothesis | split | frozen (candidate days) | population estimate | corrected statistic | verdict |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var h in a.Hypotheses)
        {
            var own = runs.Where(r => r.Id.Equals(h.Id, StringComparison.OrdinalIgnoreCase)).OrderBy(r => Array.IndexOf(Splits, r.Split)).ToList();
            if (own.Count == 0) { sb.AppendLine($"| {h.Id} (amends {h.BasedOn}) | — | — | — | — | not run |"); continue; }
            foreach (var r in own)
            {
                var part = r.Parts.FirstOrDefault();
                if (part is null) continue;
                var frozen = runs.FirstOrDefault(x => x.Id.Equals(h.BasedOn, StringComparison.OrdinalIgnoreCase) && x.Split == r.Split)?.Parts.FirstOrDefault();
                string frozenPrecision = frozen is not null && Stat(frozen, "bucketRate") is double fr ? $", precision {fr:P1}" : "";
                string populationPrecision = Stat(part, "groupRate") is double pr ? $", precision {pr:P1}" : "";
                string frozenText = frozen is null ? "not run" : $"{F(frozen.Effect)}{frozenPrecision} ({frozen.Verdict})";
                string population = $"{Effect(part)}{populationPrecision}; n {part.N:N0} ({part.Days:N0} days), {Stat(part, "sampleRows")} sample rows";
                sb.AppendLine($"| {h.Id} (amends {h.BasedOn}) | {r.Split} | {frozenText} | {population} | {Corrected(r, part, m.Params)} | **{part.Verdict}** |");
            }
        }
        sb.AppendLine();
    }

    private static string Effect(HypothesisPart part)
    {
        if (part.Verdict == HypothesisVerdict.Void && part.Effect is null) return "void: " + string.Join("; ", part.Reasons.Take(1));
        var dir = part.Direction == EffectDirection.AtLeast ? ">=" : "<=";
        var ci = part.HasInterval ? $" [{F(part.CiLow)}, {F(part.CiHigh)}]" : "";
        return $"{part.EffectName} {F(part.Effect)}{ci} vs {dir} {F(part.MinEffect)}";
    }

    private static string Corrected(HypothesisRun r, HypothesisPart part, GapperStudyParams p)
    {
        if (r.Family == "F3") return $"{(int)Math.Round(p.Statistics.CiLevel * 100)}% interval only (descriptive)";
        if (r.Id.Equals("H12", StringComparison.OrdinalIgnoreCase) || r.Inputs.AmendsHypothesis?.Equals("H12", StringComparison.OrdinalIgnoreCase) == true)
            return $"SPA p {F(Stat(part, HypothesisCorrections.SpaKey))}, DSR {F(Stat(part, HypothesisCorrections.DsrKey))}, trades {Stat(part, "selectedTrades")}";
        var text = $"p {F(part.RawP)}, BH q {F(part.FamilyQ)}";
        if (r.Primary && part.HolmP is not null) text += $", Holm p {F(part.HolmP)}";
        return text;
    }

    private static void Gate(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        sb.AppendLine("## Catalyst-latency benchmark (H11, the gatekeeper)");
        sb.AppendLine();
        sb.AppendLine("The honest prior: a premarket volume/velocity detector at 08:00 mostly re-encodes \"a catalyst was published 10–90 minutes ago\". " +
            "H11 scores an L2 logistic detector (fit on train, standardized 08:00 price/volume/run-up/filing-age features, no catalyst inputs) against the benchmark indicator " +
            "\"a news item or 8-K with pubToT ≤ 60 min exists at 08:00\" on `reach20By0945`, with a day-block bootstrap of the AUC difference.");
        sb.AppendLine();
        var h11 = runs.Where(r => r.Id.Equals("H11", StringComparison.OrdinalIgnoreCase)).ToList();
        if (h11.Count == 0) sb.AppendLine("Not run yet.");
        foreach (var r in h11)
        {
            var part = r.Parts.FirstOrDefault();
            if (part is null) continue;
            sb.AppendLine($"- **{r.Split}**: AUC detector {F(Stat(part, "aucDetector"))} vs benchmark {F(Stat(part, "aucBenchmark"))}; difference {F(part.Effect)} " +
                $"[{F(part.CiLow)}, {F(part.CiHigh)}] vs ≥ {F(part.MinEffect)}; p {F(part.RawP)}, Holm p {F(part.HolmP)}; n {part.N:N0} on {part.Days:N0} days; verdict **{part.Verdict}**.");
        }
        var h11p = runs.Where(r => r.Inputs.AmendsHypothesis?.Equals("H11", StringComparison.OrdinalIgnoreCase) == true).ToList();
        foreach (var r in h11p)
        {
            var part = r.Parts.FirstOrDefault();
            if (part is null) continue;
            sb.AppendLine($"- **{r.Split}, population ({r.Id})**: AUC detector {F(Stat(part, "aucDetector"))} vs benchmark {F(Stat(part, "aucBenchmark"))}; difference {F(part.Effect)} " +
                $"[{F(part.CiLow)}, {F(part.CiHigh)}] vs ≥ {F(part.MinEffect)}; p {F(part.RawP)}, Holm p {F(part.HolmP)}; n {part.N:N0} on {part.Days:N0} days; verdict **{part.Verdict}**.");
        }
        var validation = h11.FirstOrDefault(r => r.Split == "validation");
        var validationP = h11p.FirstOrDefault(r => r.Split == "validation");
        bool amended = m.Amendment is not null;
        sb.AppendLine();
        sb.AppendLine(validation is null || amended && validationP is null
            ? "Gate status: **not yet testable** (no validation run" + (amended ? " of H11 and H11p" : "") + "). Strategy work waits."
            : validation.Verdict == HypothesisVerdict.Supported && (!amended || validationP!.Verdict == HypothesisVerdict.Supported)
                ? "Gate status: **passed on validation** — the detector carries information beyond catalyst latency" + (amended ? " among candidates and over the population" : "") + "; strategy work may proceed to H12."
                : "Gate status: **failed on validation** — **catalyst latency is the edge.** Per the kill criteria" + (amended ? " (amendment 01 applies them to H11 and H11p)" : "") + ", strategy work stops and the product of this study is the catalyst-latency pipeline.");
        sb.AppendLine();
    }

    private static void ExitTiming(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        sb.AppendLine("## Exit timing (H4, H5, H13, H14)");
        sb.AppendLine();
        foreach (var id in new[] { "H4", "H5", "H13", "H14" })
        {
            var h = m.Hypotheses.Hypotheses.FirstOrDefault(x => x.Id == id);
            if (h is null) continue;
            sb.AppendLine($"**{id}.** {h.Claim}");
            var own = runs.Where(r => r.Id == id).ToList();
            if (own.Count == 0) sb.AppendLine("- not run");
            foreach (var r in own)
                foreach (var part in r.Parts)
                    sb.AppendLine($"- {r.Split} · {part.Name}: {Effect(part)}; n {part.N:N0} ({part.Days:N0} days); **{(m.PostHocHypotheses.Contains(id) ? "void (post-hoc)" : part.Verdict)}**");
            sb.AppendLine();
        }
        sb.AppendLine("### Post-registration variant (not pre-registered; descriptive only, no verdict)");
        sb.AppendLine();
        sb.AppendLine($"Ticket note 10972 found the frozen velocity death trivially early on a trend day and asked for a tighter secondary. " +
            $"The feature registry marks `{string.Join("`, `", m.PostRegistrationFeatures)}` as **post-registration**: death = two consecutive negative v3 readings within " +
            $"{GapperSnapshotStage.PostRegistrationDeathWindowMinutes} minutes after the premarket high, after a run of at least {m.Params.Gapper.OrderedTiers()[0].Value:P0} from the prior close. " +
            "It is reported here and excluded from every verdict table.");
        sb.AppendLine();
        if (m.PostRegistrationSummaries.Count == 0) sb.AppendLine("No 09:45 snapshots to summarize yet.");
        else
        {
            sb.AppendLine("| split | feature | Tier20 gapper days | death inside the window | median minutes after PM high |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var s in m.PostRegistrationSummaries.Where(s => s.Split != "holdout" || m.HoldoutRunExists))
                sb.AppendLine($"| {s.Split} | `{s.Feature}` ⚠ post-registration | {s.GapperDays:N0} | {s.WithDeathInWindow:N0} ({(s.GapperDays == 0 ? 0 : (double)s.WithDeathInWindow / s.GapperDays):P1}) | {F(s.MedianMinutesAfterPmHigh)} |");
        }
        sb.AppendLine();
    }

    private static void WhatThisSays(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        sb.AppendLine("## What this says");
        sb.AppendLine();
        if (runs.Count == 0) { sb.AppendLine("- No hypothesis has been run yet."); sb.AppendLine(); return; }
        foreach (var split in Splits)
        {
            var own = runs.Where(r => r.Split == split && r.Inputs.AmendmentId is null).ToList();
            if (own.Count == 0) continue;
            var counts = own.GroupBy(r => m.PostHocHypotheses.Contains(r.Id) ? "void" : r.Verdict).ToDictionary(g => g.Key, g => g.Count());
            sb.AppendLine($"- **{split}**: {own.Count} hypotheses run — " + string.Join(", ", new[] { HypothesisVerdict.Supported, HypothesisVerdict.Refuted, HypothesisVerdict.Undecided, HypothesisVerdict.Void }
                .Select(v => $"{counts.GetValueOrDefault(v)} {v}")) + ". " +
                (own.Any(r => r.Verdict == HypothesisVerdict.Supported && r.Primary && !m.PostHocHypotheses.Contains(r.Id))
                    ? "Supported primaries: " + string.Join(", ", own.Where(r => r.Verdict == HypothesisVerdict.Supported && r.Primary && !m.PostHocHypotheses.Contains(r.Id)).Select(r => r.Id)) + "."
                    : "No primary survived its family correction and Holm on this split."));
        }
        var h12 = runs.Where(r => r.Id == "H12" || r.Inputs.AmendsHypothesis == "H12").ToList();
        foreach (var r in h12)
        {
            var part = r.Parts.FirstOrDefault();
            if (part is null) continue;
            sb.AppendLine($"- {r.Id} on {r.Split}{(r.Inputs.AmendmentId is null ? " (candidate days)" : " (population)")}: selected `{Stat(part, "selectedConfig")}` ({Stat(part, "selection")}); mean net {F(part.Effect)}%/trade " +
                $"[{F(part.CiLow)}, {F(part.CiHigh)}], {part.N:N0} trades on {part.Days:N0} days, SPA p {F(Stat(part, HypothesisCorrections.SpaKey))}, DSR {F(Stat(part, HypothesisCorrections.DsrKey))}, " +
                $"mean round-trip cost {Pct(Stat(part, "meanRoundTripCost"))}: **{part.Verdict}**.");
        }
        sb.AppendLine("- Data limits stated in the protocol stand: no float, short interest, dark-pool or options flow; halts are a bar-gap proxy; fiscal.ai wire stamps bound publication time from above.");
        sb.AppendLine();
    }

    private static void KillCriteria(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        var p = m.Params;
        sb.AppendLine("## Kill-criteria status");
        sb.AppendLine();
        sb.AppendLine("| criterion | threshold | observed | status |");
        sb.AppendLine("|---|---|---|---|");
        var trainTier20 = m.Status?.Splits.FirstOrDefault(x => x.Split == "train")?.Tier20 ?? m.Sample?["splits"]?["train"]?["gappers"]?["tier20"]?.GetValue<int>();
        sb.AppendLine($"| Tier20 gapper days in train | >= {p.KillCriteria.MinTier20Train:N0} | {(trainTier20 is { } t ? t.ToString("N0") : "n/a")} | {(trainTier20 is { } t2 ? t2 >= p.KillCriteria.MinTier20Train ? "met" : "**KILL: below minimum**" : "not yet measurable")} |");
        var sip = m.LiveSipSharePct;
        sb.AppendLine($"| SIP provenance of study minute bars | >= {p.KillCriteria.MinSipProvenanceShare:P0} | {(sip is { } s ? Pct(s) : "n/a")} | {(sip is { } s2 ? s2 >= p.KillCriteria.MinSipProvenanceShare ? "met" : "**KILL: below minimum**" : "not yet measurable")} |");
        var h11v = runs.FirstOrDefault(r => r.Id == "H11" && r.Split == "validation");
        sb.AppendLine($"| H11 on validation | supported | {(h11v is null ? "not run" : h11v.Verdict)} | {(h11v is null ? "not yet testable" : h11v.Verdict == HypothesisVerdict.Supported ? "met" : "**KILL: catalyst latency is the edge**")} |");
        if (m.Amendment is not null)
        {
            var h11pv = runs.FirstOrDefault(r => r.Inputs.AmendsHypothesis == "H11" && r.Split == "validation");
            sb.AppendLine($"| H11p on validation (amendment {m.Amendment.Id}) | supported | {(h11pv is null ? "not run" : h11pv.Verdict)} | {(h11pv is null ? "not yet testable" : h11pv.Verdict == HypothesisVerdict.Supported ? "met" : "**KILL: catalyst latency is the edge**")} |");
        }
        sb.AppendLine($"| Post-unlock edits to protocol/hypotheses/params | none | {(m.HoldoutRunExists ? m.PostUnlockCommits.Count + " commit(s)" : "holdout not opened")} | {(m.HoldoutRunExists ? m.PostUnlockCommits.Count == 0 ? "met" : "**affected hypotheses void: " + string.Join(", ", m.PostHocHypotheses.OrderBy(x => x)) + "**" : "not yet applicable")} |");
        var cik = m.Sample?["cikCoverage"]?["coveragePct"]?.GetValue<double>();
        sb.AppendLine($"| H8 CIK coverage (drop, not redefine) | >= {p.KillCriteria.H8DropIfCikCoverageBelow:P0} | {(cik is { } c ? (c / 100).ToString("P1") : "n/a")} | {(cik is { } c2 ? c2 / 100 >= p.KillCriteria.H8DropIfCikCoverageBelow ? "kept" : "H8 dropped" : "not yet measurable")} |");
        sb.AppendLine();
        if (m.PostUnlockCommits.Count > 0)
        {
            sb.AppendLine("Post-unlock commits touching the frozen files:");
            foreach (var (hash, date, subject) in m.PostUnlockCommits) sb.AppendLine($"- `{Short(hash)}` {date} — {subject}");
            sb.AppendLine();
        }
    }

    private static void Promotion(StringBuilder sb, GapperReportModel m, List<HypothesisRun> runs)
    {
        var p = m.Params;
        sb.AppendLine("## Promotion and the live gate");
        sb.AppendLine();
        var h12t = runs.FirstOrDefault(r => r.Id == "H12" && r.Split == "train");
        var h12v = runs.FirstOrDefault(r => r.Id == "H12" && r.Split == "validation");
        var h12h = runs.FirstOrDefault(r => r.Id == "H12" && r.Split == "holdout");
        sb.AppendLine($"1. Feature claim survives train (q < {p.Promotion.TrainQ}): {(runs.Any(r => r.Split == "train" && r.Verdict == HypothesisVerdict.Supported && r.Id is "H1" or "H9") ? "yes" : "no / not yet")}");
        sb.AppendLine($"2. Replicates on validation with the same sign: {(runs.Any(r => r.Split == "validation" && r.Verdict == HypothesisVerdict.Supported && r.Id is "H1" or "H9") ? "yes" : "no / not yet")}");
        sb.AppendLine($"3. H12 survives SPA on validation: {(h12v is null ? "not run" : h12v.Verdict)} (train: {(h12t is null ? "not run" : h12t.Verdict)})");
        sb.AppendLine($"4. Single holdout run nets >= {p.Promotion.HoldoutMeanNetPctPerTrade}%/trade with the day-clustered lower bound > {p.Promotion.HoldoutLowerBoundAbove}: " +
            $"{(h12h is null ? (m.HoldoutRunExists ? "not run" : "holdout locked") : $"mean {F(h12h.Parts.FirstOrDefault()?.Effect)}% [{F(h12h.Parts.FirstOrDefault()?.CiLow)}, {F(h12h.Parts.FirstOrDefault()?.CiHigh)}]")}");
        sb.AppendLine($"5. Paper shadow: {p.Promotion.PaperShadowTradingDays} trading days of live snapshots agreeing with the backtest before any Live BrokerMode is offered.");
        if (m.Amendment is { } a)
        {
            var h11pv = runs.FirstOrDefault(r => r.Inputs.AmendsHypothesis == "H11" && r.Split == "validation");
            var h12pv = runs.FirstOrDefault(r => r.Inputs.AmendsHypothesis == "H12" && r.Split == "validation");
            var h12ph = runs.FirstOrDefault(r => r.Inputs.AmendsHypothesis == "H12" && r.Split == "holdout");
            sb.AppendLine($"6. Amendment {a.Id}: H11p on validation {(h11pv is null ? "not run" : h11pv.Verdict)}; H12p on validation {(h12pv is null ? "not run" : h12pv.Verdict)}; " +
                $"H12p at the holdout run >= {p.Promotion.HoldoutMeanNetPctPerTrade}%/trade with lower bound > {p.Promotion.HoldoutLowerBoundAbove}: " +
                $"{(h12ph is null ? (m.HoldoutRunExists ? "not run" : "holdout locked") : $"mean {F(h12ph.Parts.FirstOrDefault()?.Effect)}% [{F(h12ph.Parts.FirstOrDefault()?.CiLow)}, {F(h12ph.Parts.FirstOrDefault()?.CiHigh)}]")}. " +
                "Every condition above must hold; the amendment only adds conditions.");
        }
        sb.AppendLine();
    }

    private static void Exports(StringBuilder sb, GapperReportModel m)
    {
        sb.AppendLine("## Exports");
        sb.AppendLine();
        if (m.Exports.Count == 0) sb.AppendLine(m.ExportNote ?? "No CSV export in this run.");
        else
        {
            sb.AppendLine($"Under `{m.StudyDirName}/data/` with `manifest.json` (row count and SHA-256 of the rows per file):");
            sb.AppendLine();
            sb.AppendLine("| file | rows | sha256 |");
            sb.AppendLine("|---|---|---|");
            foreach (var f in m.Exports) sb.AppendLine($"| `{f.Name}` | {f.Rows:N0} | `{f.Sha256}` |");
            if (m.ExportNote is not null) sb.AppendLine().Append(m.ExportNote).AppendLine();
        }
        sb.AppendLine();
    }

    private static void RunFiles(StringBuilder sb, List<HypothesisRun> runs)
    {
        sb.AppendLine("## Run files");
        sb.AppendLine();
        if (runs.Count == 0) { sb.AppendLine("None."); return; }
        sb.AppendLine("| hypothesis | split | computed (ET) | harness commit | file |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in runs.OrderBy(r => Array.IndexOf(Splits, r.Split)).ThenBy(r => Number(r.Id)))
            sb.AppendLine($"| {r.Id} | {r.Split} | {EasternTime.Format(r.ComputedUtc, "yyyy-MM-dd HH:mm")} | `{Short(r.Inputs.Head)}` | `{Path.GetFileName(r.File ?? "")}` |");
        sb.AppendLine();
    }

    private static int Number(string id) => int.TryParse(new string(id.TrimStart('H', 'h').TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0;

    private static object? Stat(HypothesisPart part, string key)
    {
        if (!part.Statistic.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement e)
            return e.ValueKind switch
            {
                JsonValueKind.Number => e.GetDouble(),
                JsonValueKind.String => e.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => e.GetRawText(),
            };
        return v;
    }

    private static string F(object? v) => v switch
    {
        null => "n/a",
        double d => double.IsNaN(d) ? "n/a" : Math.Abs(d) >= 100 ? d.ToString("0.0", CultureInfo.InvariantCulture) : Math.Abs(d) < 0.001 && d != 0 ? d.ToString("0.0e0", CultureInfo.InvariantCulture) : d.ToString("0.000", CultureInfo.InvariantCulture),
        int i => i.ToString("N0"),
        _ => v.ToString() ?? "n/a",
    };

    private static string Pct(object? v) => v switch
    {
        double d => double.IsNaN(d) ? "n/a" : d.ToString("P2", CultureInfo.InvariantCulture),
        JsonNode n when n.GetValueKind() == JsonValueKind.Number => n.GetValue<double>().ToString("P2", CultureInfo.InvariantCulture),
        _ => "n/a",
    };

    private static string Short(string? hash, int length = 8) => string.IsNullOrWhiteSpace(hash) ? "unknown" : hash.Length > length ? hash[..length] : hash;

    private static string Utc(DateTime? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "NOT FROZEN";
}

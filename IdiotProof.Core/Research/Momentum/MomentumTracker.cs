using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IdiotProof.Core.Data;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Core.Services;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Momentum;

/// <summary>A level as stored on a claim: the spoken words, the resolved dollars (null until a reference price is known), the chart keyword.</summary>
public sealed record StoredLevel(string? Spoken, decimal? Price, string? Keyword, string? Scale);

/// <summary>One stated plan on a claim, as stored in <see cref="MomentumClaim.Scenarios"/>.</summary>
public sealed record StoredScenario(
    string? Name, string TriggerKind, StoredLevel? Trigger, List<StoredLevel> Confirm, bool RequiresConfirm,
    List<StoredLevel> Targets, StoredLevel? Invalidation, string? Quote);

/// <summary>What a command did: the console text and the structured data.</summary>
public sealed record MomentumRunResult(bool Ok, string Human, object Data);

/// <summary>
/// The "Momentum." claim tracker (IP-T493): import the collector's transcript files, extract claims with an LLM,
/// verify each claim against Alpaca 1-minute SIP bars, and report. Every step is incremental and idempotent so it
/// can be rerun as the collector lands more transcripts. Research only; it never places an order.
/// </summary>
public sealed partial class MomentumTracker(
    IDbContextFactory<AppDbContext> dbFactory,
    GapperStudyServices study,
    UserClaudeKeyResolver claudeKeys,
    MomentumClaimExtractor extractor,
    TimeProvider clock,
    ILogger<MomentumTracker> logger)
{
    public const string DefaultDir = @"D:\IdiotProofData\momentum";
    public const string PickWatchlist = "watchlist-scenario";
    public const string PickMorning = "morning-update-scenario";
    public const string Brag = "past-performance";
    public const string Statement = "market-statement";
    public const int TopGapperRank = 20;
    /// <summary>The gapper study's holdout days amendment 05 did not open: outcomes on these days must not steer the study.</summary>
    public static readonly (DateOnly From, DateOnly To) StudyLockedHoldout = (new(2025, 11, 3), new(2026, 9, 9));

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly Regex TitleDateRx = new(@"\b(\d{1,2})/(\d{1,2})/(\d{2,4})\b", RegexOptions.CultureInvariant);

    private DateTime NowUtc => clock.GetUtcNow().UtcDateTime;
    private DateOnly TodayEt => DateOnly.FromDateTime(MinuteBarMath.ToEastern(NowUtc));

    /// <summary>A session is complete once 20:05 ET has passed on it.</summary>
    public bool IsComplete(DateOnly d)
    {
        var nowEt = MinuteBarMath.ToEastern(NowUtc);
        var today = DateOnly.FromDateTime(nowEt);
        return d < today || (d == today && nowEt.TimeOfDay >= new TimeSpan(20, 5, 0));
    }

    /// <summary>
    /// watchlist (the evening "TOP 3 STOCKS"), morning-update ("TOP 3 STOCKS UPDATE", or any TOP video published on the
    /// trade date itself before 16:00 ET) or other.
    /// </summary>
    public static string KindOf(string title, DateTime? publishedUtc = null, DateOnly? tradeDate = null)
    {
        var t = title.Trim();
        if (publishedUtc is { } pu && tradeDate is { } td && Regex.IsMatch(t, @"^\s*top\b", RegexOptions.IgnoreCase))
        {
            var et = MinuteBarMath.ToEastern(pu);
            if (DateOnly.FromDateTime(et) == td && et.TimeOfDay < new TimeSpan(16, 0, 0)) return "morning-update";
        }
        if (Regex.IsMatch(t, @"\bupdate\b", RegexOptions.IgnoreCase)) return "morning-update";
        if (Regex.IsMatch(t, @"^\s*top\b", RegexOptions.IgnoreCase) && Regex.IsMatch(t, @"\bstocks?\b", RegexOptions.IgnoreCase)) return "watchlist";
        return "other";
    }

    // ── import ─────────────────────────────────────────────────────────

    public async Task<MomentumRunResult> ImportAsync(string? dir, CancellationToken ct)
    {
        dir = string.IsNullOrWhiteSpace(dir) ? DefaultDir : dir;
        var tdir = Path.Combine(dir, "transcripts");
        if (!Directory.Exists(tdir)) return new(false, $"No transcripts folder at {tdir}.", new { dir });
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = NowUtc;
        int videosNew = 0, videosUpd = 0, trNew = 0, trUpd = 0, trChanged = 0, listed = 0, bad = 0;

        var videos = await db.MomentumVideos.ToDictionaryAsync(v => v.VideoId, ct);
        var transcripts = await db.MomentumTranscripts.ToDictionaryAsync(t => t.VideoId, ct);

        // the kept videos in the channel list (so the report knows what is still waiting for captions)
        var listPath = Path.Combine(dir, "channel-list.json");
        if (File.Exists(listPath))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(listPath, ct));
            if (doc.RootElement.TryGetProperty("videos", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var v in arr.EnumerateArray())
                {
                    index++;
                    if (!(v.TryGetProperty("kept", out var k) && k.ValueKind == JsonValueKind.True)) continue;
                    var id = v.GetProperty("id").GetString() ?? "";
                    var title = v.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                    if (id.Length == 0) continue;
                    listed++;
                    if (videos.ContainsKey(id)) continue;
                    var td = TitleDate(title);
                    var row = new MomentumVideo
                    {
                        VideoId = id, Url = $"https://www.youtube.com/shorts/{id}", Title = Trunc(title, 300), TitleDate = td, TradeDate = td,
                        Kind = KindOf(title), ChannelIndex = index, ImportedUtc = now, UpdatedUtc = now,
                    };
                    db.MomentumVideos.Add(row);
                    videos[id] = row;
                    videosNew++;
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(tdir, "*.json", SearchOption.AllDirectories))
        {
            JsonElement r;
            try { using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct)); r = doc.RootElement.Clone(); }
            catch (Exception ex) when (ex is JsonException or IOException) { bad++; logger.LogWarning("momentum import: unreadable {File}: {Msg}", file, ex.Message); continue; }
            var id = Str(r, "videoId");
            if (string.IsNullOrEmpty(id)) { bad++; continue; }
            var title = Str(r, "title") ?? "";
            var dirDate = DateOnly.TryParseExact(Path.GetFileName(Path.GetDirectoryName(file)), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd) ? dd : (DateOnly?)null;
            var tradeDate = DateOf(Str(r, "tradeDate")) ?? dirDate;

            if (!videos.TryGetValue(id, out var v))
            {
                v = new MomentumVideo { VideoId = id, ImportedUtc = now };
                db.MomentumVideos.Add(v);
                videos[id] = v;
                videosNew++;
            }
            else videosUpd++;
            v.Url = Trunc(Str(r, "url") ?? $"https://www.youtube.com/shorts/{id}", 200);
            v.Title = Trunc(title, 300);
            v.PublishedUtc = DateTimeOffset.TryParse(Str(r, "publishedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pu) ? pu.UtcDateTime : v.PublishedUtc;
            v.TitleDate = DateOf(Str(r, "titleDate")) ?? TitleDate(title);
            v.TradeDate = tradeDate;
            v.Kind = KindOf(title, v.PublishedUtc, tradeDate);
            v.DurationSec = Int(r, "durationSec") ?? v.DurationSec;
            v.ChannelIndex = Int(r, "channelIndex") ?? v.ChannelIndex;
            v.FetchedUtc = DateTimeOffset.TryParse(Str(r, "fetchedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fu) ? fu.UtcDateTime : v.FetchedUtc;
            v.UpdatedUtc = now;

            var text = Str(r, "text");
            var segments = r.TryGetProperty("segments", out var seg) && seg.ValueKind == JsonValueKind.Array ? seg.GetRawText() : null;
            var error = Str(r, "error");
            var hash = string.IsNullOrEmpty(text) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (!transcripts.TryGetValue(id, out var t))
            {
                t = new MomentumTranscript { VideoId = id, ImportedUtc = now };
                db.MomentumTranscripts.Add(t);
                transcripts[id] = t;
                trNew++;
            }
            else
            {
                trUpd++;
                if (t.TextHash != hash) { t.ExtractedUtc = null; t.ExtractError = null; trChanged++; }
            }
            t.TradeDate = tradeDate;
            t.Text = text;
            t.SegmentsJson = segments;
            t.Source = "youtube-captions";
            t.Error = error is null ? null : Trunc(error, 400);
            t.TextHash = hash;
            t.UpdatedUtc = now;
        }
        await db.SaveChangesAsync(ct);

        var withText = transcripts.Values.Count(x => !string.IsNullOrEmpty(x.Text));
        var errored = transcripts.Values.Count(x => string.IsNullOrEmpty(x.Text));
        var human = $"momentum import from {dir}: {videos.Count} videos ({listed} kept in the channel list), {transcripts.Count} transcript files " +
                    $"({withText} with text, {errored} without: caption errors/rate limits); new videos {videosNew}, new transcripts {trNew}, changed text {trChanged}" +
                    (bad > 0 ? $", unreadable files {bad}" : "") + ".";
        return new(true, human, new { dir, videos = videos.Count, listed, transcripts = transcripts.Count, withText, errored, videosNew, transcriptsNew = trNew, changed = trChanged, unreadable = bad });
    }

    // ── extract ────────────────────────────────────────────────────────

    public async Task<MomentumRunResult> ExtractAsync(Guid owner, DateOnly? since, int limit, string? dataDir, CancellationToken ct, bool force = false)
    {
        var creds = await claudeKeys.ResolveAsync(owner, ct);
        if (string.IsNullOrWhiteSpace(creds.ClaudeApiKey)) return new(false, UserClaudeKeyResolver.NoKeyMessage, new { });

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = from t in db.MomentumTranscripts
                join v in db.MomentumVideos on t.VideoId equals v.VideoId
                where t.Text != null && t.Text != "" && (force || t.ExtractedUtc == null)
                select new { t, v };
        if (since is { } s) q = q.Where(x => x.t.TradeDate >= s);
        var todo = await q.OrderByDescending(x => x.t.TradeDate).ThenByDescending(x => x.v.PublishedUtc).ToListAsync(ct);
        if (limit > 0) todo = todo.Take(limit).ToList();
        if (todo.Count == 0) return new(true, "momentum extract: nothing to do (every transcript with text already has claims).", new { extracted = 0 });

        await using var md = await MarketAsync(owner, dataDir, ct);
        int ok = 0, failed = 0, claimsAdded = 0;
        var lines = new List<string>();
        foreach (var x in todo)
        {
            ct.ThrowIfCancellationRequested();
            var d = x.t.TradeDate ?? x.v.TradeDate;
            if (d is null) { x.t.ExtractError = "no trade date"; x.t.ExtractedUtc = NowUtc; failed++; continue; }
            var (res, err) = await extractor.ExtractAsync(creds.ClaudeApiKey!, x.v.Title, d, x.v.Kind, x.v.PublishedUtc, x.t.Text!, ct);
            if (res is null)
            {
                x.t.ExtractError = Trunc(err ?? "failed", 400);
                failed++;
                lines.Add($"  {d:yyyy-MM-dd} {x.v.VideoId} FAILED: {err}");
                await db.SaveChangesAsync(ct);
                continue;
            }
            var dd = d.Value;
            var vid = x.v.VideoId;
            var sameDay = (await db.MomentumClaims.Where(c => c.TradeDate == dd && c.VideoId != vid && c.Symbol != null && c.SymbolConfidence >= 0.6 && c.Rank != null)
                .Select(c => c.Symbol!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var claims = await BuildClaimsAsync(md, x.v, dd, x.t.Text!, res, sameDay, ct);
            var old = await db.MomentumClaims.Where(c => c.VideoId == x.v.VideoId).ToListAsync(ct);
            db.MomentumClaims.RemoveRange(old);
            db.MomentumClaims.AddRange(claims);
            x.t.ExtractedUtc = NowUtc;
            x.t.ExtractedBy = MomentumClaimExtractor.ExtractedBy;
            x.t.ExtractError = null;
            await db.SaveChangesAsync(ct);
            ok++;
            claimsAdded += claims.Count;
            lines.Add($"  {d:yyyy-MM-dd} {x.v.Kind,-14} {x.v.VideoId}: " + string.Join(", ", claims.Where(c => c.Rank is not null)
                .Select(c => $"#{c.Rank} {c.SpokenSymbol ?? "?"}->{c.Symbol ?? "unresolved"}{(c.SymbolConfidence is { } sc ? $" ({sc:0.00})" : "")}")) +
                $"; brags {claims.Count(c => c.ClaimType == Brag)}, statements {claims.Count(c => c.ClaimType == Statement)}");
            logger.LogInformation("momentum extract {Date} {Video}: {N} claims", d, x.v.VideoId, claims.Count);
        }
        var human = $"momentum extract ({MomentumClaimExtractor.ExtractedBy}, newest first): {ok} transcripts -> {claimsAdded} claims, {failed} failed; Alpaca requests {md.Requests}\n" + string.Join("\n", lines);
        return new(true, human, new { extracted = ok, failed, claims = claimsAdded, model = MomentumClaimExtractor.Model });
    }

    private async Task<MomentumMarketData> MarketAsync(Guid owner, string? dataDir, CancellationToken ct)
    {
        var keys = await study.ResolveKeysAsync(owner, ct);
        return new MomentumMarketData(string.IsNullOrWhiteSpace(dataDir) ? DefaultDir : dataDir, keys, new AlpacaDataRateGate(300, clock),
            c => study.LoadAssetsAsync(keys, c), logger);
    }

    /// <summary>The completed sessions shortly before <paramref name="d"/> (weekdays; holidays simply have no movers file).</summary>
    private List<DateOnly> PriorDays(DateOnly d) =>
        Enumerable.Range(1, 5).Select(i => d.AddDays(-i)).Where(x => x.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && IsComplete(x)).ToList();

    private async Task<(MoversDay? Day, MoversDay? Prev)> MoversForAsync(MomentumMarketData md, DateOnly d, CancellationToken ct)
    {
        var want = PriorDays(d);
        if (IsComplete(d)) want.Add(d);
        await md.EnsureMoversAsync(want, ct);
        var day = IsComplete(d) ? await md.MoversAsync(d, ct) : null;
        MoversDay? prev = null;
        if (day is not null) prev = await md.MoversAsync(day.Prev, ct);
        else foreach (var p in PriorDays(d)) if ((prev = await md.MoversAsync(p, ct)) is not null) break;
        return (day, prev);
    }

    private async Task<List<MomentumClaim>> BuildClaimsAsync(MomentumMarketData md, MomentumVideo v, DateOnly d, string text, MomentumExtraction x, HashSet<string> sameDay, CancellationToken ct)
    {
        var now = NowUtc;
        var claims = new List<MomentumClaim>();
        var assets = await md.AssetsAsync(ct);
        var (day, prev) = await MoversForAsync(md, d, ct);
        var spelled = SpelledTicker.FindSpelled(text);

        // candidate symbols' daily bars, so the resolver can see each one's prior close
        var spokenCands = x.Picks.Select(p => SpelledTicker.Normalize(p.SpokenSymbol)).Concat(x.Brags.Select(b => SpelledTicker.Normalize(b.SpokenSymbol)))
            .Where(s => s is not null).Select(s => s!).ToList();
        var moverSyms = (prev?.Rows ?? []).Select(r => r.S).Concat((day?.Rows ?? []).Select(r => r.S)).Distinct().ToList();
        var lastDone = IsComplete(d) ? d : PriorDays(d).FirstOrDefault(d.AddDays(-1));
        var dailyWanted = spokenCands.Where(s => assets.ContainsKey(s)).ToList();
        foreach (var c in spokenCands) dailyWanted.AddRange(SpelledTicker.Variants(c, moverSyms));
        await md.EnsureDailyAsync(dailyWanted.Distinct(), d.AddDays(-14), lastDone, ct);

        // spoken tickers first, so a pick named only by description never takes a ticker said elsewhere in the same video
        var used = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new Dictionary<ExtractedPick, (string? Sym, double Conf, string Note)>(ReferenceEqualityComparer.Instance);
        foreach (var p in x.Picks.Where(p => SpelledTicker.Normalize(p.SpokenSymbol) is not null))
        {
            var r = ResolveSymbol(md, assets, p.SpokenSymbol, spelled, p.StatedMovePct, p.Scenarios, d, day, prev, used, sameDay);
            if (r.Symbol is not null) used.Add(r.Symbol);
            resolved[p] = r;
        }
        foreach (var p in x.Picks.Where(p => !resolved.ContainsKey(p)))
        {
            var r = ResolveSymbol(md, assets, p.SpokenSymbol, spelled, p.StatedMovePct, p.Scenarios, d, day, prev, used, sameDay);
            if (r.Symbol is not null) used.Add(r.Symbol);
            resolved[p] = r;
        }
        int rankFallback = 0;
        foreach (var p in x.Picks)
        {
            rankFallback++;
            var (sym, conf, note) = resolved[p];
            decimal? reference = null, prevHigh = null, openD = null;
            if (sym is not null)
            {
                await md.EnsureDailyAsync([sym], d.AddDays(-14), lastDone, ct);
                var (pc, ph, _, today) = md.PriorClose(sym, d);
                reference = pc; prevHigh = ph; openD = today?.Open;
            }
            var scenarios = (p.Scenarios ?? []).Select(s => Store(s, reference, openD, prevHigh)).ToList();
            var quote = string.Join(" | ", new[] { p.Quote }.Concat((p.Scenarios ?? []).Select(s => s.Quote)).Where(q => !string.IsNullOrWhiteSpace(q)).Distinct());
            claims.Add(new MomentumClaim
            {
                VideoId = v.VideoId, TradeDate = d, Rank = p.Rank is >= 1 and <= 5 ? p.Rank : rankFallback,
                ClaimType = v.Kind == "morning-update" ? PickMorning : PickWatchlist,
                SpokenSymbol = Trunc(p.SpokenSymbol, 40), Symbol = sym, SymbolConfidence = sym is null ? null : Math.Round(conf, 2), SymbolNote = Trunc(note, 400),
                IdentityHint = Trunc(p.IdentityHint, 400), StatedMovePct = p.StatedMovePct,
                SetupTags = JsonSerializer.Serialize(NormalizeTags(p.SetupTags)), Direction = p.Direction?.Trim().ToLowerInvariant() == "short" ? "short" : "long",
                Scenarios = JsonSerializer.Serialize(scenarios, Json), ReferencePrice = reference is { } r ? Math.Round(r, 4) : null,
                RawQuote = quote, ExtractedBy = MomentumClaimExtractor.ExtractedBy, ExtractedUtc = now,
            });
        }
        foreach (var b in x.Brags)
        {
            var (sym, conf, note) = ResolveSymbol(md, assets, b.SpokenSymbol, spelled, null, null, d, day, prev, new HashSet<string>(), sameDay);
            claims.Add(new MomentumClaim
            {
                VideoId = v.VideoId, TradeDate = d, ClaimType = Brag, SpokenSymbol = Trunc(b.SpokenSymbol, 40), Symbol = sym,
                SymbolConfidence = sym is null ? null : Math.Round(conf, 2), SymbolNote = Trunc(note, 400),
                ClaimedPct = b.ClaimedPct, ClaimWindow = Trunc(b.Window?.Trim().ToLowerInvariant(), 40), RawQuote = b.Quote,
                ExtractedBy = MomentumClaimExtractor.ExtractedBy, ExtractedUtc = now,
            });
        }
        foreach (var m in x.MarketStatements)
            claims.Add(new MomentumClaim
            {
                VideoId = v.VideoId, TradeDate = d, ClaimType = Statement, IdentityHint = Trunc(m.Text, 400), RawQuote = m.Quote ?? m.Text,
                ExtractedBy = MomentumClaimExtractor.ExtractedBy, ExtractedUtc = now,
            });
        return claims;
    }

    private static List<string> NormalizeTags(List<string>? tags) =>
        (tags ?? []).Select(t => t.Trim().ToLowerInvariant().Replace(' ', '-'))
            .Select(t => MomentumClaimExtractor.SetupTagVocabulary.Contains(t) ? t : "other").Distinct().ToList();

    private static StoredScenario Store(ExtractedScenario s, decimal? reference, decimal? openD, decimal? prevHigh)
    {
        StoredLevel? L(string? spoken)
        {
            if (string.IsNullOrWhiteSpace(spoken)) return null;
            var kw = SpokenPrice.Keyword(spoken);
            if (kw is not null)
                return new StoredLevel(spoken, kw == "prior-day-high" ? prevHigh : kw == "prior-close" ? reference : null, kw, kw is "prior-day-high" or "prior-close" ? "chart" : null);
            var r = SpokenPrice.Resolve(spoken, reference, openD);
            return new StoredLevel(spoken, r?.Price, null, r?.Scale);
        }
        var kind = (s.TriggerKind ?? "break").Trim().ToLowerInvariant();
        if (kind is not ("break" or "reclaim" or "hold")) kind = "break";
        return new StoredScenario(s.Name, kind, L(s.TriggerLevel),
            (s.ConfirmLevels ?? []).Select(L).Where(l => l is not null).Select(l => l!).ToList(), s.RequiresConfirm,
            (s.Targets ?? []).Select(L).Where(l => l is not null).Select(l => l!).ToList(), L(s.Invalidation), s.Quote);
    }

    /// <summary>
    /// Validate a spoken ticker against Alpaca assets and the movers around the day; with no ticker, identify the stock from
    /// the stated move and the level scale among the prior session's top gainers.
    /// </summary>
    private static (string? Symbol, double Confidence, string Note) ResolveSymbol(
        MomentumMarketData md, Dictionary<string, StudyAsset> assets, string? spoken, IReadOnlyList<string> spelledInText, double? statedPct,
        List<ExtractedScenario>? scenarios, DateOnly d, MoversDay? day, MoversDay? prev, HashSet<string> exclude, IReadOnlySet<string>? sameDay = null)
    {
        var moverSyms = (prev?.Rows ?? []).Select(r => r.S).Concat((day?.Rows ?? []).Select(r => r.S)).Distinct().ToList();
        double Movement(string s)
        {
            double score = 0;
            var pr = prev?.Find(s);
            var dr = day?.Find(s);
            if (pr is not null && (pr.RankClose <= 100 || pr.RankHigh <= 100)) score += 0.3;
            else if (dr is not null && dr.RankHigh <= 100) score += 0.25;
            if (sameDay?.Contains(s) == true) score += 0.1;
            if (statedPct is { } sp && sp > 0 && pr is not null)
            {
                var best = Math.Min(Math.Abs(pr.ClosePct - sp), Math.Abs(pr.HighPct - sp));
                if (best <= Math.Max(15, sp * 0.35)) score += 0.15;
            }
            return score;
        }

        var norm = SpelledTicker.Normalize(spoken);
        if (norm is not null)
        {
            var cands = new List<(string S, double Score, string Why)>();
            bool Active(string s) => assets.TryGetValue(s, out var a) && a.Active;
            // an exact active ticker outranks a one-edit variant unless the variant is a mover that also matches the stated move or another video that day
            if (Active(norm)) cands.Add((norm, 0.62 + Movement(norm), "exact"));
            foreach (var alt in SpelledTicker.Variants(norm, moverSyms))
                if (Active(alt)) cands.Add((alt, 0.3 + Movement(alt), $"near spoken {norm}"));
            // a spelled-out ticker elsewhere in the text that extends this one ("CHS" here, "C H S N" there)
            foreach (var sp in spelledInText)
                if (sp != norm && sp.StartsWith(norm, StringComparison.Ordinal) && Active(sp)) cands.Add((sp, 0.35 + Movement(sp), $"spelled {sp} in the text"));
            var best = cands.Where(c => !exclude.Contains(c.S)).OrderByDescending(c => c.Score).FirstOrDefault();
            if (best.S is not null)
                return (best.S, Math.Min(0.98, best.Score), best.Why == "exact" ? (best.Score >= 0.8 ? "exact ticker, a top mover" : "exact ticker") : best.Why);
            return (null, 0, $"'{spoken}' is not an active Alpaca asset and no near mover matched");
        }

        // no ticker spoken: the prior session's top gainers, by stated move and level fit
        if (prev is null) return (null, 0, "no ticker spoken and no prior-session movers to identify it from");
        var numeric = (scenarios ?? []).SelectMany(s => new[] { s.TriggerLevel }.Concat(s.Targets ?? []).Concat(s.ConfirmLevels ?? []))
            .Where(t => !string.IsNullOrWhiteSpace(t) && SpokenPrice.Keyword(t) is null).ToList();
        var scored = new List<(string S, double Score)>();
        foreach (var r in prev.Rows.Where(r => r.RankClose <= 40 || r.RankHigh <= 40 || sameDay?.Contains(r.S) == true))
        {
            if (exclude.Contains(r.S) || !(assets.TryGetValue(r.S, out var a) && a.Active)) continue;
            double score = 0.1;
            // named outright in another video about the same session (the morning update often says the ticker)
            if (sameDay?.Contains(r.S) == true) score += 0.25;
            if (statedPct is { } sp && sp > 0)
            {
                var diff = Math.Abs(r.ClosePct - sp);
                score += diff <= Math.Max(8, sp * 0.15) ? 0.35 : diff <= Math.Max(15, sp * 0.35) ? 0.15 : 0;
            }
            if (numeric.Count > 0)
            {
                var fit = numeric.Select(t => SpokenPrice.Resolve(t, r.C)).Where(x => x is not null).Count(x => x!.Price / r.C is >= 0.6m and <= 3.5m);
                score += 0.2 * fit / numeric.Count;
            }
            scored.Add((r.S, score));
        }
        var ordered = scored.OrderByDescending(c => c.Score).ToList();
        if (ordered.Count == 0 || ordered[0].Score < 0.4) return (null, 0, "no ticker spoken; no prior-session mover fits the stated move and levels");
        var margin = ordered.Count > 1 ? ordered[0].Score - ordered[1].Score : 1;
        var cap = sameDay?.Contains(ordered[0].S) == true ? 0.85 : 0.75;
        var confidence = Math.Min(cap, ordered[0].Score) * (margin >= 0.1 ? 1 : 0.6);
        return (ordered[0].S, confidence, $"identified from description (prior-session mover{(sameDay?.Contains(ordered[0].S) == true ? ", named in another video that day" : "")}; runner-up {(ordered.Count > 1 ? ordered[1].S : "none")}, margin {margin:0.00})");
    }

    // ── verify ─────────────────────────────────────────────────────────

    public async Task<MomentumRunResult> VerifyAsync(Guid owner, DateOnly? since, bool force, string? dataDir, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var verified = await db.MomentumClaimOutcomes.Select(o => o.ClaimId).Distinct().ToListAsync(ct);
        var verifiedSet = verified.ToHashSet();
        var q = db.MomentumClaims.Where(c => c.ClaimType != Statement);
        if (since is { } s) q = q.Where(c => c.TradeDate >= s);
        var all = await q.OrderByDescending(c => c.TradeDate).ToListAsync(ct);
        var todo = all.Where(c => IsComplete(c.TradeDate) && (force || !verifiedSet.Contains(c.Id))).ToList();
        var pending = all.Count(c => !IsComplete(c.TradeDate));
        if (todo.Count == 0)
            return new(true, $"momentum verify: nothing to verify ({pending} claim(s) wait for their session to finish).", new { verified = 0, pending });

        var videoIds = todo.Select(c => c.VideoId).Distinct().ToList();
        var videos = await db.MomentumVideos.Where(v => videoIds.Contains(v.VideoId)).ToDictionaryAsync(v => v.VideoId, ct);
        await using var md = await MarketAsync(owner, dataDir, ct);
        var assets = await md.AssetsAsync(ct);

        // claims whose ticker did not resolve at extraction get one more try now (movers may exist now)
        foreach (var c in todo.Where(c => c.Symbol is null && c.ClaimType is PickWatchlist or PickMorning or Brag))
        {
            var (day, prev) = await MoversForAsync(md, c.TradeDate, ct);
            List<ExtractedScenario>? sc = null;
            var stored = Scenarios(c);
            if (stored.Count > 0) sc = stored.Select(x => new ExtractedScenario { TriggerLevel = x.Trigger?.Spoken, Targets = x.Targets.Select(t => t.Spoken ?? "").ToList() }).ToList();
            var sameDay = todo.Concat(all).Where(o => o.TradeDate == c.TradeDate && o.VideoId != c.VideoId && o.Symbol != null && o.SymbolConfidence >= 0.6 && o.Rank != null).Select(o => o.Symbol!).ToHashSet(StringComparer.Ordinal);
            var (sym, conf, note) = ResolveSymbol(md, assets, c.SpokenSymbol, [], c.StatedMovePct, sc, c.TradeDate, day, prev, new HashSet<string>(), sameDay);
            if (sym is not null) { c.Symbol = sym; c.SymbolConfidence = Math.Round(conf, 2); c.SymbolNote = Trunc(note + " (at verify)", 400); }
        }

        // data: daily bars around each day, minute bars on the day (and the next session for day-2 language)
        foreach (var g in todo.Where(c => c.Symbol is not null).GroupBy(c => c.TradeDate))
        {
            var last = Enumerable.Range(0, 8).Select(i => g.Key.AddDays(i)).Where(IsComplete).DefaultIfEmpty(g.Key).Max();
            await md.EnsureDailyAsync(g.Select(c => c.Symbol!), g.Key.AddDays(-21), last, ct);
        }
        var minuteWanted = new List<(string, DateOnly)>();
        foreach (var c in todo.Where(c => c.Symbol is not null && c.ClaimType is PickWatchlist or PickMorning))
        {
            minuteWanted.Add((c.Symbol!, c.TradeDate));
            if (DayTwo(c) && NextSession(md, c.Symbol!, c.TradeDate) is { } n && IsComplete(n)) minuteWanted.Add((c.Symbol!, n));
        }
        await md.EnsureMinuteAsync(minuteWanted, ct);
        await md.EnsureMoversAsync(todo.Select(c => c.TradeDate).Concat(todo.SelectMany(c => PriorDays(c.TradeDate))), ct);

        var ids = todo.Select(c => c.Id).ToList();
        var stale = await db.MomentumClaimOutcomes.Where(o => ids.Contains(o.ClaimId)).ToListAsync(ct);
        db.MomentumClaimOutcomes.RemoveRange(stale);

        var counts = new Dictionary<string, int>();
        int rows = 0;
        foreach (var c in todo)
        {
            List<MomentumClaimOutcome> outcomes = c.ClaimType == Brag
                ? [await VerifyBragAsync(md, c, videos.GetValueOrDefault(c.VideoId), ct)]
                : await VerifyPickAsync(md, c, videos.GetValueOrDefault(c.VideoId), ct);
            foreach (var o in outcomes)
            {
                db.MomentumClaimOutcomes.Add(o);
                counts[o.Verdict] = counts.GetValueOrDefault(o.Verdict) + 1;
                rows++;
            }
        }
        await db.SaveChangesAsync(ct);
        var human = $"momentum verify: {todo.Count} claim(s) -> {rows} outcome row(s) ({string.Join(", ", counts.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}); " +
                    $"{pending} claim(s) wait for their session; Alpaca requests {md.Requests}.";
        return new(true, human, new { verified = todo.Count, outcomes = rows, verdicts = counts, pending });
    }

    public static List<StoredScenario> Scenarios(MomentumClaim c)
    {
        if (string.IsNullOrWhiteSpace(c.Scenarios)) return [];
        try { return JsonSerializer.Deserialize<List<StoredScenario>>(c.Scenarios) ?? []; }
        catch (JsonException) { return []; }
    }

    public static List<string> Tags(MomentumClaim c)
    {
        if (string.IsNullOrWhiteSpace(c.SetupTags)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(c.SetupTags) ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool DayTwo(MomentumClaim c) =>
        Tags(c).Contains("day-2-continuation") || (c.RawQuote ?? "").Contains("day two", StringComparison.OrdinalIgnoreCase) || (c.RawQuote ?? "").Contains("day 2", StringComparison.OrdinalIgnoreCase);

    private static DateOnly? NextSession(MomentumMarketData md, string symbol, DateOnly d) =>
        md.Daily(symbol).FirstOrDefault(b => b.Date > d)?.Date;

    private async Task<List<MomentumClaimOutcome>> VerifyPickAsync(MomentumMarketData md, MomentumClaim c, MomentumVideo? video, CancellationToken ct)
    {
        var now = NowUtc;
        var stored = Scenarios(c);
        var list = new List<MomentumClaimOutcome>();
        MomentumClaimOutcome Base(int i) => new() { ClaimId = c.Id, ScenarioIndex = i, VerifiedUtc = now, DataSource = md.DataSource };
        if (c.Symbol is null)
        {
            for (int i = 0; i < Math.Max(1, stored.Count); i++)
            {
                var o = Base(i); o.Verdict = MomentumVerdicts.Unverifiable; o.Notes = "ticker not resolved: " + c.SymbolNote; list.Add(o);
            }
            return list;
        }
        var sym = c.Symbol;
        var d = c.TradeDate;
        var bars = md.ScenarioBars(sym, d);
        var (pc, ph, prevDate, today) = md.PriorClose(sym, d);
        var (day, prev) = await MoversForAsync(md, d, ct);
        decimal? openD = bars.FirstOrDefault(b => b.Et >= MomentumScenarioEvaluator.RthOpen)?.Open;
        var pre = bars.Where(b => b.Et < MomentumScenarioEvaluator.RthOpen).ToList();
        var rth = bars.Where(b => b.Et < MomentumScenarioEvaluator.RthClose).ToList();
        decimal? pmHigh = pre.Count > 0 ? pre.Max(b => b.High) : null;

        DateOnly? next = DayTwo(c) ? NextSession(md, sym, d) : null;
        var nextBars = next is { } n && IsComplete(n) ? md.ScenarioBars(sym, n) : [];

        if (stored.Count == 0) stored.Add(new StoredScenario(null, "break", null, [], false, [], null, null));
        for (int i = 0; i < stored.Count; i++)
        {
            var s = stored[i];
            var o = Base(i);
            o.PriorClose = pc is { } p ? Math.Round(p, 4) : null;
            o.Open0930 = openD;
            o.GapPct0930 = pc is > 0 && openD is { } od ? MomentumScenarioEvaluator.Pct(od, pc.Value) : null;
            o.PremarketHigh = pmHigh;
            o.DayHigh = rth.Count > 0 ? rth.Max(b => b.High) : null;
            o.DayClose = rth.Count > 0 ? rth[^1].Close : null;
            var dr = day?.Find(sym);
            var prr = prev?.Find(sym);
            if (day is not null)
            {
                o.GainerCount = day.Count;
                o.GainerRank = dr?.RankHigh;
                o.GapRank = dr?.RankGap;
                o.TopGapper = dr is not null && dr.RankHigh <= TopGapperRank;
            }
            o.DayHighPct = dr?.HighPct ?? (pc is > 0 && o.DayHigh is { } dh ? MomentumScenarioEvaluator.Pct(dh, pc.Value) : null);
            o.DayClosePct = dr?.ClosePct ?? (pc is > 0 && o.DayClose is { } dc ? MomentumScenarioEvaluator.Pct(dc, pc.Value) : null);
            o.PrevDayGainerRank = prr?.RankClose;
            o.PrevDayClosePct = prr?.ClosePct;
            o.TriggerKind = s.TriggerKind;

            var notes = new List<string>();
            if (bars.Count == 0) { o.Verdict = MomentumVerdicts.Unverifiable; o.Notes = $"no minute bars for {sym} on {d:yyyy-MM-dd}"; list.Add(o); continue; }

            // levels: stored dollars, else resolve now against the prior close / open; chart keywords from the bars
            ScenarioLevel? Level(StoredLevel? l)
            {
                if (l is null) return null;
                if (l.Keyword == "vwap") return new ScenarioLevel(null, true);
                if (l.Keyword == "premarket-high") return pmHigh is { } x ? new ScenarioLevel(x) : null;
                if (l.Keyword == "prior-day-high") return ph is { } y ? new ScenarioLevel(y) : null;
                if (l.Keyword == "prior-close") return pc is { } z ? new ScenarioLevel(z) : null;
                if (l.Keyword is not null) return null;
                var price = l.Price ?? SpokenPrice.Resolve(l.Spoken, pc, openD)?.Price;
                return price is { } pr2 ? new ScenarioLevel(pr2) : null;
            }
            var trig = Level(s.Trigger);
            if (trig is null)
            {
                o.Verdict = MomentumVerdicts.Unverifiable;
                o.Notes = s.Trigger is null ? "no trigger level stated" : $"trigger '{s.Trigger.Spoken}' could not be priced";
                list.Add(o); continue;
            }
            var spec = new ScenarioSpec(s.TriggerKind, trig,
                s.Confirm.Select(Level).Where(l => l is not null).Select(l => l!).ToList(),
                s.Targets.Select(Level).Where(l => l?.Price is not null).Select(l => l!.Price!.Value).ToList(),
                Level(s.Invalidation)?.Price,
                s.RequiresConfirm);
            // a call made on the session itself (the morning update) counts from the minute it was published
            DateTime? notBefore = c.ClaimType == PickMorning && video?.PublishedUtc is { } vp && DateOnly.FromDateTime(MinuteBarMath.ToEastern(vp)) == d ? vp : null;
            var r = MomentumScenarioEvaluator.Evaluate(spec, bars, notBefore);
            if (notBefore is not null) notes.Add($"counted from publish {MinuteBarMath.ToEastern(notBefore.Value):HH:mm} ET");
            o.TriggerLevel = trig.Price;
            o.TriggerHit = r.TriggerHit;
            o.TriggerUtc = r.TriggerUtc;
            o.TriggerPrice = r.TriggerPrice;
            o.ConfirmHit = r.ConfirmHit;
            o.ConfirmLevel = r.ConfirmLevel is { } cl ? Math.Round(cl, 4) : null;
            o.ConfirmUtc = r.ConfirmUtc;
            o.EntryPrice = r.EntryPrice is { } ep ? Math.Round(ep, 4) : null;
            o.EntryUtc = r.EntryUtc;
            o.TargetsJson = JsonSerializer.Serialize(r.Targets.Select(t => new { level = t.Level, hitUtc = t.HitUtc }));
            o.TargetsHit = r.TargetsHit;
            o.FirstTargetHit = r.FirstTargetHit;
            o.FirstTargetUtc = r.FirstTargetUtc;
            o.AnyTargetHit = r.AnyTargetHit;
            o.InvalidationLevel = r.InvalidationLevel is { } il ? Math.Round(il, 4) : null;
            o.InvalidationSource = r.InvalidationSource;
            o.InvalidationHitBeforeTarget = r.InvalidationHitBeforeTarget;
            o.MaxRunAfterTriggerPct = r.MaxRunAfterTriggerPct;
            o.MaxDrawdownAfterTriggerPct = r.MaxDrawdownAfterTriggerPct;
            o.CloseVsTriggerPct = r.CloseVsTriggerPct;
            o.ExitPrice = r.ExitPrice is { } xp ? Math.Round(xp, 4) : null;
            o.ExitUtc = r.ExitUtc;
            o.ExitReason = r.ExitReason;
            o.PlanPnlPct = r.PlanPnlPct;
            o.Verdict = r.Verdict;
            if (r.Note is not null) notes.Add(r.Note);
            if (spec.Targets.Count == 0) notes.Add("no priced targets");
            if (r.TriggerHit && r.TriggerUtc is { } tu && MinuteBarMath.ToEastern(tu).TimeOfDay < MomentumScenarioEvaluator.RthOpen) notes.Add("triggered premarket");
            if (nextBars.Count > 0 && r.TriggerPrice is { } tp && tp > 0)
            {
                var nb = nextBars.Where(b => b.Et < MomentumScenarioEvaluator.RthClose).ToList();
                if (nb.Count > 0)
                {
                    var hi = nb.Max(b => b.High);
                    o.D1MaxRunPct = MomentumScenarioEvaluator.Pct(hi, tp);
                    o.D1AnyTargetHit = r.Targets.Any(t => t.HitUtc is null && hi >= t.Level) || r.AnyTargetHit;
                    notes.Add($"next session {next:yyyy-MM-dd} checked for day-2 language");
                }
            }
            if (c.SymbolConfidence is < 0.5) notes.Add($"low-confidence ticker ({c.SymbolNote})");
            o.Notes = notes.Count == 0 ? null : Trunc(string.Join("; ", notes), 1000);
            list.Add(o);
        }
        return list;
    }

    private async Task<MomentumClaimOutcome> VerifyBragAsync(MomentumMarketData md, MomentumClaim c, MomentumVideo? v, CancellationToken ct)
    {
        var o = new MomentumClaimOutcome { ClaimId = c.Id, ScenarioIndex = 0, VerifiedUtc = NowUtc, DataSource = "alpaca sip daily (split-adjusted)" };
        if (c.Symbol is null || c.ClaimedPct is not { } claimed || claimed <= 0)
        {
            o.Verdict = MomentumVerdicts.Unverifiable;
            o.Notes = c.Symbol is null ? "ticker not resolved: " + c.SymbolNote : "no claimed percent";
            return o;
        }
        var pubEt = v?.PublishedUtc is { } pu ? MinuteBarMath.ToEastern(pu) : MinuteBarMath.ToEastern(MinuteBarMath.ToUtc(c.TradeDate, new TimeSpan(4, 0, 0))).AddHours(-12);
        var pd = DateOnly.FromDateTime(pubEt);
        var lastSession = pubEt.TimeOfDay >= new TimeSpan(16, 0, 0) ? pd : pd.AddDays(-1);
        DateOnly from, to;
        var monday = pd.AddDays(-(((int)pd.DayOfWeek + 6) % 7));
        switch (c.ClaimWindow)
        {
            case "previous-session": from = lastSession.AddDays(-4); to = lastSession; break;
            case "this-week": from = monday; to = lastSession; break;
            case "last-week":
                if (pd.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { from = monday; to = monday.AddDays(4); }
                else { from = monday.AddDays(-7); to = monday.AddDays(-3); }
                break;
            default: from = lastSession.AddDays(-14); to = lastSession; break;
        }
        await md.EnsureDailyAsync([c.Symbol], from.AddDays(-21), to, ct);
        var bars = md.Daily(c.Symbol).Where(b => b.Date <= to).ToList();
        var window = bars.Where(b => b.Date >= from).ToList();
        if (c.ClaimWindow == "previous-session" && window.Count > 0) window = [window[^1]];
        var before = bars.LastOrDefault(b => b.Date < (window.FirstOrDefault()?.Date ?? from));
        if (window.Count == 0 || before is null) { o.Verdict = MomentumVerdicts.Unverifiable; o.Notes = $"no daily bars for {c.Symbol} {from:yyyy-MM-dd}..{to:yyyy-MM-dd}"; return o; }
        // everything on the last window day's share basis
        var basis = window[^1].Ratio;
        decimal Adj(decimal price, SymbolDay b) => basis > 0 ? price * b.Ratio / basis : price;
        var baseClose = Adj(before.Close, before);
        var maxHigh = window.Max(b => Adj(b.High, b));
        var lastClose = Adj(window[^1].Close, window[^1]);
        double lowToHigh = 0; decimal runLow = decimal.MaxValue;
        foreach (var b in window)
        {
            runLow = Math.Min(runLow, Adj(b.Low, b));
            if (runLow > 0) lowToHigh = Math.Max(lowToHigh, MomentumScenarioEvaluator.Pct(Adj(b.High, b), runLow));
        }
        o.PriorClose = Math.Round(baseClose, 4);
        o.DayHigh = Math.Round(maxHigh, 4);
        o.DayClose = Math.Round(lastClose, 4);
        o.BragActualHighPct = MomentumScenarioEvaluator.Pct(maxHigh, baseClose);
        o.BragActualClosePct = MomentumScenarioEvaluator.Pct(lastClose, baseClose);
        o.BragActualLowToHighPct = lowToHigh;
        var best = Math.Max(o.BragActualHighPct ?? 0, lowToHigh);
        o.Verdict = best >= claimed * 0.9 ? MomentumVerdicts.BragAccurate : MomentumVerdicts.BragExaggerated;
        o.Notes = $"window {window[0].Date:yyyy-MM-dd}..{window[^1].Date:yyyy-MM-dd} ({c.ClaimWindow ?? "recent"}); claimed +{claimed:0}%, best actual +{best:0}% (prior close to high {o.BragActualHighPct:+0;-0}%, low to high +{lowToHigh:0}%, to close {o.BragActualClosePct:+0;-0}%)";
        if (o.Verdict == MomentumVerdicts.BragExaggerated)
        {
            // he often re-tells a run from a few days back under a loose window ("this week", "yesterday"): any single
            // session in the last ten, prior close to high, that reaches the claim makes it accurate with a window note
            var recent = bars.Where(b => b.Date <= to).TakeLast(11).ToList();
            for (int i = recent.Count - 1; i >= 1; i--)
            {
                var day = MomentumScenarioEvaluator.Pct(Adj(recent[i].High, recent[i]), Adj(recent[i - 1].Close, recent[i - 1]));
                if (day >= claimed * 0.9)
                {
                    o.Verdict = MomentumVerdicts.BragAccurate;
                    o.Notes += $"; outside the stated window: {recent[i].Date:yyyy-MM-dd} ran +{day:0}% prior close to high";
                    break;
                }
            }
        }
        o.Notes = Trunc(o.Notes, 1000);
        return o;
    }

    // ── helpers ──

    private static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    private static DateOnly? DateOf(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static DateOnly? TitleDate(string title)
    {
        var m = TitleDateRx.Match(title);
        if (!m.Success) return null;
        int mo = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), dd = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), y = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (y < 100) y += 2000;
        try { return new DateOnly(y, mo, dd); } catch (ArgumentOutOfRangeException) { return null; }
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(s))]
    internal static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

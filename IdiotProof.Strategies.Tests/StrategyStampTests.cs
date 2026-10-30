using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// The ASCII strategy stamp (Epic W5): a deterministic 72-column card built
/// from the canonical JSON (IP-LAW-8) that also pastes back into a strategy.
///
/// Golden files live in <c>Stamps/*.txt</c> next to this file and are read
/// from the source tree (located via [CallerFilePath]), so regenerating them
/// writes straight into the repo. To regenerate after an intentional layout
/// or canon change:
/// <code>
///   $env:IDIOTPROOF_UPDATE_GOLDEN = "1"     # bash: IDIOTPROOF_UPDATE_GOLDEN=1
///   dotnet test IdiotProof.Strategies.Tests --filter "FullyQualifiedName~StrategyStampTests"
/// </code>
/// then review the diff. Goldens are rendered WITHOUT the armored payload:
/// its bytes come from the runtime's deflate implementation, which may change
/// between .NET servicing releases without the strategy changing. The payload
/// is covered by the round-trip/tamper tests instead.
/// </summary>
public class StrategyStampTests
{
    // ── Representative strategies ───────────────────────────────────────

    private static StrategyDefinition SimpleLong() => Stock.Ticker("AAPL")
        .Name("VWAP + EMA trend long")
        .IsAboveVwap()
        .IsAboveEma(9)
        .Quantity(1)
        .Long()
        .TakeProfit(195.5)
        .StopLoss(188.25)
        .Build();

    private static StrategyDefinition Gapper() => GapperScriptFactory.Compose("GAPT", new GapperProfile
    {
        Id = "classic-gapper", Name = "Classic",
        MinGapPercent = 5, MaxGapPercent = 20, MinVolumeRatio = 2, MinPrice = 1, MaxPrice = 50,
        EntryWindowStartEt = "04:00", EntryWindowEndEt = "09:00",
        StopLossPercent = 5, TrailingStopPercent = 8, PeakGivebackPercent = 25,
        ArmExitAtEt = "09:15", SellByEt = "09:28", DefaultNotional = 1000m,
    }).Build();

    private static StrategyDefinition MultiTarget() => Stock.Ticker("SOUN")
        .Name("Breakout scale-out")
        .RequireAdxAbove(20)
        .BreaksAbove(4.2)
        .IsVolumeAbove(2)
        .Quantity(300)
        .Long()
        .TakeProfit(5.0, 6.5, 8.0)
        .StopLoss(3.9)
        .TrailingStopLoss(6)
        .AdaptiveOrder()
        .Build();

    private static StrategyDefinition Branching()
    {
        var def = Stock.Ticker("NVDA")
            .Name("VWAP fork")
            .RequireEmaStack(9, 21)
            .IsAboveVwap()
            .Then(b => b.Long().TakeProfit(152, 158).StopLossPercent(2))
            .ElseIf(c => c.IsBelowVwap(), b => b.Short().TakeProfit(141).StopLoss(149.5))
            .Else(b => b.Long().TakeProfit(150))
            .Quantity(25)
            .StopLossPercent(3)
            .SellBy("15:45")
            .Build();
        // Composition the text DSL cannot round-trip — the stamp must still describe it.
        def.EntryConditions.Add(Conditions.IsMacdBullish.Or(Conditions.IsRsiOversold(30).Not()));
        return def;
    }

    private static StrategyDefinition ShortFade() => Stock.Ticker("TSLA")
        .Name("Overbought VWAP fade")
        .Session(TradingSession.RTH)
        .RequireEntryWindow("09:45", "11:30")
        .IsBelowVwap()
        .IsRsiOverbought(75)
        .IsShootingStar()
        .Short()
        .QuantityNotional(2500m)
        .TakeProfit(236.4)
        .StopLossPercent(2)
        .SellBy("15:55")
        .Repeat()
        .Build();

    private static readonly (string Name, Func<StrategyDefinition> Make)[] Samples =
    [
        ("simple-long", SimpleLong),
        ("gapper", Gapper),
        ("multi-target", MultiTarget),
        ("branching", Branching),
        ("short", ShortFade),
    ];

    private static IEnumerable<TestCaseData> GoldenCases()
    {
        foreach (var (name, _) in Samples)
            yield return new TestCaseData(name, false).SetName($"Golden_{name}_ascii");
        yield return new TestCaseData("gapper", true).SetName("Golden_gapper_unicode");
        yield return new TestCaseData("branching", true).SetName("Golden_branching_unicode");
    }

    private static IEnumerable<TestCaseData> AllRenderings()
    {
        foreach (var (name, _) in Samples)
        {
            yield return new TestCaseData(name, false).SetName($"Shape_{name}_ascii");
            yield return new TestCaseData(name, true).SetName($"Shape_{name}_unicode");
        }
    }

    private static StrategyDefinition Make(string name) => Samples.Single(s => s.Name == name).Make();

    // ── Golden files (IP-T136) ──────────────────────────────────────────

    [TestCaseSource(nameof(GoldenCases))]
    public void Stamp_MatchesGoldenFile(string name, bool unicode)
    {
        var actual = Make(name).ToStamp(new StampOptions { Unicode = unicode, IncludePayload = false });
        var path = GoldenPath($"{name}{(unicode ? ".unicode" : "")}.txt");

        if (Environment.GetEnvironmentVariable("IDIOTPROOF_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            Assert.Pass($"Golden regenerated: {path}");
        }

        Assert.That(File.Exists(path), $"Missing golden {path} - run with IDIOTPROOF_UPDATE_GOLDEN=1 to create it.");
        var expected = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        Assert.That(actual, Is.EqualTo(expected),
            $"Stamp drifted from {Path.GetFileName(path)}. If intentional, regenerate with IDIOTPROOF_UPDATE_GOLDEN=1 and review the diff.");
    }

    private static string GoldenPath(string file, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Stamps", file);

    // ── Shape: 72 columns, no trailing spaces, ASCII is 7-bit ───────────

    [TestCaseSource(nameof(AllRenderings))]
    public void Stamp_EveryLineFitsSeventyTwoColumns_NoTrailingSpaces(string name, bool unicode)
    {
        var stamp = Make(name).ToStamp(new StampOptions { Unicode = unicode });
        var lines = stamp.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            foreach (var line in lines)
            {
                Assert.That(line.Length, Is.LessThanOrEqualTo(StrategyStamp.Width), $"too wide: '{line}'");
                Assert.That(line, Does.Not.EndWith(" "), $"trailing space: '{line}'");
                Assert.That(line, Does.Not.Contain("\r").And.Not.Contain("\t"));
                if (!unicode) Assert.That(line.All(ch => ch < 128), Is.True, $"non-ASCII in ASCII stamp: '{line}'");
            }
            // Box edges are exactly full width, so the card is a clean block.
            Assert.That(lines.Count(l => l.Length == StrategyStamp.Width), Is.GreaterThan(20), "boxes span the full width");
        });
    }

    // ── Short id (IP-T127) ──────────────────────────────────────────────

    [Test]
    public void ShortId_IsBase32OfSha256OfCanonicalJson()
    {
        var def = Gapper();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(def.CanonicalJson()));
        var b32 = Base32(hash)[..8];

        Assert.Multiple(() =>
        {
            Assert.That(def.ShortId(), Is.EqualTo($"IP-{b32[..4]}-{b32[4..]}"));
            Assert.That(def.ShortId(), Does.Match("^IP-[A-Z2-7]{4}-[A-Z2-7]{4}$"));
            Assert.That(def.CanonicalHash(), Is.EqualTo(hash));
        });
    }

    [Test]
    public void Canon_IsCompactStrategyJson_WithoutTheRandomId()
    {
        var a = Gapper();
        var b = Gapper();
        Assume.That(a.Id, Is.Not.EqualTo(b.Id), "each build gets a fresh random Guid");

        Assert.Multiple(() =>
        {
            Assert.That(a.CanonicalJson(), Does.Not.Contain(a.Id.ToString()), "attribution is not canon");
            Assert.That(a.CanonicalJson(), Does.Not.Contain("\n"), "compact: no platform line endings in the hash");
            Assert.That(a.ShortId(), Is.EqualTo(b.ShortId()), "same strategy, different Id → same stamp id");
            Assert.That(StrategyJson.Deserialize(a.CanonicalJson()).CanonicalJson(), Is.EqualTo(a.CanonicalJson()),
                "the compact canon is itself valid StrategyJson");
        });
    }

    // ── Determinism ─────────────────────────────────────────────────────

    [Test]
    public void Stamp_IsDeterministic_ForTheSameCanon()
    {
        foreach (var (name, make) in Samples)
        {
            var first = make().ToStamp();
            Assert.That(make().ToStamp(), Is.EqualTo(first), $"{name}: rebuilt strategy stamps identically");
            Assert.That(StrategyJson.Deserialize(StrategyJson.Serialize(make())).ToStamp(), Is.EqualTo(first),
                $"{name}: a JSON round-tripped copy stamps identically");
        }
    }

    [Test]
    public void DifferentStrategies_GetDifferentIdsAndFingerprints()
    {
        var ids = Samples.Select(s => s.Make().ShortId()).ToList();
        var arts = Samples.Select(s => s.Make().Fingerprint()).ToList();

        var tweaked = SimpleLong();
        tweaked.StopLossPrice = 188.26; // one cent

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Unique);
            Assert.That(arts, Is.Unique);
            Assert.That(tweaked.ShortId(), Is.Not.EqualTo(SimpleLong().ShortId()), "a one-cent stop change is a new strategy");
            Assert.That(tweaked.Fingerprint(), Is.Not.EqualTo(SimpleLong().Fingerprint()));
        });
    }

    // ── Fingerprint (IP-T132) ───────────────────────────────────────────

    [Test]
    public void Fingerprint_IsFramed17x9_TitledWithShortId_WithStartAndEnd()
    {
        var def = MultiTarget();
        var lines = def.Fingerprint().TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(11));
            Assert.That(lines.All(l => l.Length == 19), Is.True);
            Assert.That(lines[0], Does.Contain($"[{def.ShortId()}]"));
            Assert.That(lines[^1], Does.Contain("[SHA256]"));
            var field = string.Concat(lines[1..^1].Select(l => l[1..^1]));
            Assert.That(field.Count(ch => ch == 'S'), Is.EqualTo(1), "one start square");
            Assert.That(field[4 * 17 + 8], Is.EqualTo('S'), "the bishop starts in the centre");
        });
    }

    // ── Header / flow / forks / ladder content ──────────────────────────

    [Test]
    public void Header_CarriesSymbolSideSessionSizeNameAndId()
    {
        var def = Gapper();
        var head = string.Join('\n', def.ToStamp().Split('\n').Take(8));

        Assert.Multiple(() =>
        {
            Assert.That(head, Does.Contain(def.ShortId()));
            Assert.That(head, Does.Contain("GAPT"));
            Assert.That(head, Does.Contain("LONG"));
            Assert.That(head, Does.Contain("Premarket"));
            Assert.That(head, Does.Contain("$1,000 notional"));
            Assert.That(head, Does.Contain("GAPT Gapper - Classic"), "em dash folds to '-' in ASCII mode");
        });
    }

    [Test]
    public void Flow_HasAllSixPhasesInOrder()
    {
        var stamp = SimpleLong().ToStamp();
        var at = new[] { "1 SETUP", "2 FILTERS", "3 ENTRY", "4 ORDER", "5 RISK", "6 EXIT" }
            .Select(p => stamp.IndexOf(p, StringComparison.Ordinal)).ToList();

        Assert.That(at, Has.All.GreaterThanOrEqualTo(0));
        Assert.That(at, Is.Ordered);
    }

    [Test]
    public void Branching_RendersIfElseIfElseFork_AfterEntry()
    {
        var stamp = Branching().ToStamp();

        Assert.Multiple(() =>
        {
            Assert.That(stamp, Does.Contain("[ FORK ]"));
            Assert.That(stamp, Does.Contain("+-- IF price above VWAP"));
            Assert.That(stamp, Does.Contain("+-- ELSE IF price below VWAP"));
            Assert.That(stamp, Does.Contain("'-- ELSE"));
            Assert.That(stamp, Does.Contain("=> SHORT, target $141.00, stop $149.50"));
            Assert.That(stamp, Does.Contain("MACD above its signal line OR NOT RSI at or below 30 (oversold)"));
            Assert.That(stamp.IndexOf("3 ENTRY", StringComparison.Ordinal),
                Is.LessThan(stamp.IndexOf("FORK", StringComparison.Ordinal)));
            Assert.That(stamp.IndexOf("FORK", StringComparison.Ordinal),
                Is.LessThan(stamp.IndexOf("4 ORDER", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ExitLadder_Long_TargetsAboveEntry_StopBelow_HighestFirst()
    {
        var ladder = LadderOf(MultiTarget());
        int t3 = Line(ladder, "T3"), t2 = Line(ladder, "T2"), t1 = Line(ladder, "T1"),
            entry = Line(ladder, "ENTRY"), stop = Line(ladder, "STOP");

        Assert.Multiple(() =>
        {
            Assert.That(new[] { t3, t2, t1, entry, stop }, Is.Ordered.And.Unique);
            Assert.That(ladder[t1], Does.Contain("$5.00").And.Contain("sell 33%"));
            Assert.That(ladder[t3], Does.Contain("$8.00").And.Contain("sell 34%"));
            Assert.That(ladder[stop], Does.Contain("$3.90"));
            Assert.That(ladder[Line(ladder, "TRAIL")], Does.Contain("6% off peak"));
        });
    }

    [Test]
    public void ExitLadder_Short_StopAboveEntry_TargetBelow_CoverWording()
    {
        var ladder = LadderOf(ShortFade());
        int stop = Line(ladder, "STOP"), entry = Line(ladder, "ENTRY"), target = Line(ladder, "TARGET");

        Assert.Multiple(() =>
        {
            Assert.That(new[] { stop, entry, target }, Is.Ordered.And.Unique);
            Assert.That(ladder[stop], Does.Contain("+2% from entry"));
            Assert.That(ladder[target], Does.Contain("$236.40").And.Contain("cover all"));
            Assert.That(ladder[Line(ladder, "SELL-BY")], Does.Contain("15:55 ET"));
        });
    }

    [Test]
    public void ExitLadder_Gapper_ShowsGivebackArmTimeAndSellBy()
    {
        var ladder = LadderOf(Gapper());
        Assert.Multiple(() =>
        {
            Assert.That(ladder[Line(ladder, "GIVEBACK")], Does.Contain("25% of run").And.Contain("armed 09:15 ET"));
            Assert.That(ladder[Line(ladder, "SELL-BY")], Does.Contain("09:28 ET").And.Contain("flatten"));
            Assert.That(ladder[Line(ladder, "STOP")], Does.Contain("-5% from entry"));
        });
    }

    [Test]
    public void Numbers_AreInvariant_UnderACommaDecimalCulture()
    {
        var saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            var invariant = MultiTarget().ToStamp();
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.That(MultiTarget().ToStamp(), Is.EqualTo(invariant));
        }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }

    private static List<string> LadderOf(StrategyDefinition def)
    {
        var lines = def.ToStamp().Split('\n').ToList();
        var start = lines.FindIndex(l => l.Contains("EXIT LADDER"));
        var end = lines.FindIndex(start + 1, l => l.Length == 0);
        return lines.GetRange(start, end - start);
    }

    private static int Line(List<string> lines, string token)
    {
        var i = lines.FindIndex(l => Regex.IsMatch(l, $@"(^|[^A-Z]){Regex.Escape(token)}([^A-Z]|$)"));
        Assert.That(i, Is.GreaterThanOrEqualTo(0), $"'{token}' not on the ladder:\n{string.Join('\n', lines)}");
        return i;
    }

    // ── Armor + Decode (IP-T133) ────────────────────────────────────────

    [Test]
    public void Decode_RoundTripsEverySample_ToIdenticalCanon()
    {
        foreach (var (name, make) in Samples)
        {
            var def = make();
            var back = StrategyStamp.Decode(def.ToStamp());
            Assert.Multiple(() =>
            {
                Assert.That(back.CanonicalJson(), Is.EqualTo(def.CanonicalJson()), name);
                Assert.That(back.ShortId(), Is.EqualTo(def.ShortId()), name);
            });
        }
    }

    [Test]
    public void Armor_IsWrappedAt64_WithCrcLine()
    {
        var armor = Branching().ToArmor().TrimEnd('\n').Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(armor[0], Is.EqualTo("-----BEGIN IDIOTPROOF STRATEGY-----"));
            Assert.That(armor[^1], Is.EqualTo("-----END IDIOTPROOF STRATEGY-----"));
            Assert.That(armor[^2], Does.Match("^CRC: [0-9a-f]{8}$"));
            var body = armor[1..^2];
            Assert.That(body.Take(body.Length - 1).All(l => l.Length == 64), Is.True);
            Assert.That(body.All(l => Regex.IsMatch(l, "^[A-Za-z0-9_-]+$")), Is.True, "base64url, no padding");
        });
    }

    [Test]
    public void Decode_ToleratesJunkCrlfIndentationQuotingAndRewrapping()
    {
        var def = Gapper();
        var armor = def.ToArmor().TrimEnd('\n').Split('\n');

        var indented = "Hey, here's my gapper:\r\n\r\n" +
                       string.Join("\r\n", armor.Select(l => "      " + l + "   ")) +
                       "\r\n\r\nthanks!  -- sent from my phone\r\n";
        var quoted = "On Tue someone wrote:\n" + string.Join('\n', armor.Select(l => "> " + l)) + "\n> \n> cheers";
        var body = string.Concat(armor[1..^2]);
        var rewrapped = $"{armor[0]}\n{string.Join("\n", body.Chunk(23).Select(c => new string(c)))}\n  {armor[^2]}\n{armor[^1]}";

        Assert.Multiple(() =>
        {
            Assert.That(StrategyStamp.Decode(indented).ShortId(), Is.EqualTo(def.ShortId()), "indented + CRLF + prose");
            Assert.That(StrategyStamp.Decode(quoted).ShortId(), Is.EqualTo(def.ShortId()), "e-mail quoted");
            Assert.That(StrategyStamp.Decode(rewrapped).ShortId(), Is.EqualTo(def.ShortId()), "re-wrapped body");
            Assert.That(StrategyStamp.Decode("whole card:\n" + def.ToStamp(new StampOptions { Unicode = true })).ShortId(),
                Is.EqualTo(def.ShortId()), "the full Unicode card pasted back");
        });
    }

    [Test]
    public void Decode_TamperedBody_FailsCrc()
    {
        var lines = SimpleLong().ToArmor().Split('\n');
        var b = lines[1].ToCharArray();
        b[10] = b[10] == 'A' ? 'B' : 'A';
        lines[1] = new string(b);

        Assert.That(() => StrategyStamp.Decode(string.Join('\n', lines)),
            Throws.TypeOf<StrategyStampException>().With.Message.Contains("CRC mismatch"));
    }

    [Test]
    public void Decode_FailsClosed_OnMissingOrBrokenArmor()
    {
        var armor = SimpleLong().ToArmor();
        var lines = armor.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(() => StrategyStamp.Decode("just a strategy name, no stamp"),
                Throws.TypeOf<StrategyStampException>().With.Message.Contains("BEGIN"));
            Assert.That(() => StrategyStamp.Decode(""), Throws.TypeOf<StrategyStampException>());
            Assert.That(() => StrategyStamp.Decode(string.Join('\n', lines[..^1])),
                Throws.TypeOf<StrategyStampException>().With.Message.Contains("END"), "truncated");
            Assert.That(() => StrategyStamp.Decode(string.Join('\n', lines.Where(l => !l.StartsWith("CRC")))),
                Throws.TypeOf<StrategyStampException>().With.Message.Contains("CRC"), "no CRC line");
            Assert.That(() => StrategyStamp.Decode(armor.Replace("CRC: ", "CRC: zz")),
                Throws.TypeOf<StrategyStampException>().With.Message.Contains("hex"));
            Assert.That(() => StrategyStamp.Decode(armor.Replace(lines[1], lines[1][..20] + "!*" + lines[1][22..])),
                Throws.TypeOf<StrategyStampException>().With.Message.Contains("base64url"));
        });
    }

    [Test]
    public void Decode_ValidArmorAroundJsonTheReaderRejects_FailsClosed()
    {
        // A correctly-armored, CRC-valid payload whose JSON has a field this
        // build does not understand: the strict reader must refuse it.
        var json = """{ "schemaVersion": 1, "symbol": "TST", "futureRiskGate": true }""";
        var stamp = HandArmor(Encoding.UTF8.GetBytes(json));
        Assert.That(() => StrategyStamp.Decode(stamp),
            Throws.TypeOf<StrategyStampException>().With.Message.Contains("futureRiskGate"));

        var notJson = HandArmor(Encoding.UTF8.GetBytes("{ definitely not json"));
        Assert.That(() => StrategyStamp.Decode(notJson), Throws.TypeOf<StrategyStampException>());

        var notDeflate = HandArmor([1, 2, 3, 4, 5], deflate: false);
        Assert.That(() => StrategyStamp.Decode(notDeflate), Throws.TypeOf<StrategyStampException>());
    }

    // ── QR (IP-T134) ────────────────────────────────────────────────────

    [Test]
    public void CompactPayload_RoundTripsEverySample_ViaDecode()
    {
        foreach (var (name, make) in Samples)
        {
            var def = make();
            var compact = def.ToCompactPayload();
            Assert.Multiple(() =>
            {
                Assert.That(compact, Does.Match(@"^IPS1:[0-9A-Z $%*+\-./:]+$"), $"{name}: QR alphanumeric alphabet only");
                Assert.That(StrategyStamp.Decode("  " + compact + "\r\n").CanonicalJson(), Is.EqualTo(def.CanonicalJson()), name);
            });
        }
    }

    [Test]
    public void CompactPayload_Tampered_FailsCrc()
    {
        var compact = SimpleLong().ToCompactPayload().ToCharArray();
        compact[20] = compact[20] == '0' ? '1' : '0';
        Assert.That(() => StrategyStamp.Decode(new string(compact)), Throws.TypeOf<StrategyStampException>());
    }

    [Test]
    public void Qr_RendersHalfBlockCode_WithinSeventyTwoColumns()
    {
        var stamp = Gapper().ToStamp(new StampOptions { Qr = true });
        var tail = stamp[(stamp.IndexOf("-----END IDIOTPROOF STRATEGY-----", StringComparison.Ordinal))..].Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(tail.Count(l => l.Contains('█')), Is.GreaterThan(20), "a real QR, not the too-large note");
            Assert.That(tail.All(l => l.Length <= StrategyStamp.Width), Is.True);
            Assert.That(tail.All(l => !l.EndsWith(' ')), Is.True);
            Assert.That(stamp, Does.Contain("QR v"));
            Assert.That(StrategyStamp.Decode(stamp).ShortId(), Is.EqualTo(Gapper().ShortId()), "QR does not disturb the armor");
        });
    }

    [Test]
    public void Qr_TooLargeStrategy_PrintsNoteInsteadOfThrowing()
    {
        var builder = Stock.Ticker("HUGE").Name(new string('x', 200));
        for (int i = 0; i < 60; i++) builder.HoldsAbove(1.0 + i * 0.0137).IsNear(50 + i * 1.31, 0.5 + i * 0.01);
        var def = builder.Long().Quantity(1).StopLoss(0.5).Build();

        string stamp = "";
        Assert.That(() => stamp = def.ToStamp(new StampOptions { Qr = true }), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(stamp, Does.Contain("too large for a single QR"));
            Assert.That(stamp.Split('\n').All(l => l.Length <= StrategyStamp.Width), Is.True);
            Assert.That(StrategyStamp.Decode(stamp).ShortId(), Is.EqualTo(def.ShortId()), "the armor still carries it");
        });
    }

    [Test]
    public void FromArgs_ParsesCliSwitches()
    {
        var o = StampOptions.FromArgs(["--unicode", "--no-payload", "--qr", "AAPL"]);
        Assert.Multiple(() =>
        {
            Assert.That(o.Qr, Is.True);
            Assert.That(o.Unicode, Is.True);
            Assert.That(o.IncludePayload, Is.False);
            Assert.That(StampOptions.FromArgs([]), Is.EqualTo(new StampOptions()));
        });
    }

    // ── Independent reference implementations (so the tests pin the spec,
    //    not the production code's own helpers) ──────────────────────────

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(data.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        var sb = new StringBuilder();
        for (int i = 0; i < bits.Length; i += 5)
            sb.Append(alphabet[Convert.ToInt32(bits.Substring(i, Math.Min(5, bits.Length - i)).PadRight(5, '0'), 2)]);
        return sb.ToString();
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static string HandArmor(byte[] payload, bool deflate = true)
    {
        byte[] packed = payload;
        if (deflate)
        {
            using var ms = new MemoryStream();
            using (var z = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(payload);
            packed = ms.ToArray();
        }
        var body = Convert.ToBase64String(packed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"-----BEGIN IDIOTPROOF STRATEGY-----\n{body}\nCRC: {Crc32(packed):x8}\n-----END IDIOTPROOF STRATEGY-----\n";
    }
}

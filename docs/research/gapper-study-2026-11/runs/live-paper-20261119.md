# Live paper run — 2026-11-19 premarket (IP-T548), interim at 05:40 ET

Account: Paper 25K (PA39UP8N9). 441 active Multi-Stage Gapper v2 strategies, 1 share each: 158 armed
for today's radar (IP-T546) plus 283 still active from 11-18 (163 of those are not in today's top 300).
Cumulative start of day: −$17.40 (−0.07%). At 05:40 ET: $24,984.22, **−$15.78 (−0.06%)**.

## Fills so far (ET)

| Time | Symbol | Tier | Entry | Exit | Result |
|---|---|---|---|---|---|
| 04:04 | PENG | large | 45.42 | open | 5 min resting in a 1.8% spread, filled 2c under limit |
| 04:26 | MPU | penny | 3.25 | open | −3% within a minute; panel vetoed 04:23, approved 04:26 |
| 04:26 | SPRC | micro | 0.4283 | 0.69 | **+61%** TrailingStop (12% off a ~+82% peak) |
| 04:39 | INLF | micro | 0.27 | ~0.25 | −7% TrailingStop (12% trail fired below entry) |
| 04:41 | MRVL | mega | 151.00 | open | |
| 04:41 | AMD | mega | 545.99 | open | |
| 04:49 | AXTX | classic | 5.76 | open | |
| 04:50 | DELL | mega | 325.35 | open | filled 30c under limit after 8 min |
| 04:58 | OTLK | micro | 0.48 | ~0.41 | **−14.6%** StopLoss set at −10% (see 5) |
| 05:01 | GMM | penny | 1.91 | open | |
| 05:02 | BEX | classic | 12.60 | open | |
| 05:13 | NCPL | penny | 3.24 | open | −2.5% within a minute |
| 05:19 | CRDO | large | 128.88 | open | |
| 05:20 | AMDL | large | 53.80 | open | 2x AMD while AMD is held |
| 05:21 | NVDA | mega | 154.00 | open | |
| 05:22 | SOXL | large | 54.20 | open | 3x semis |
| 05:22 | CRWD | mega | 251.98 | open | |
| 05:23 | LITX | micro | 9.24 | open | |
| 05:25 | AEHR | large | 52.30 | open | |
| 05:35 | STX | mega | 541.18 | open | |

Unfilled: MOD (limit 142.28 in a $137.88/$145.00 book, ~5% spread, since 04:37), ZS (since 05:02).

## Reality vs model (divergences, running tally)

1. **Monitor tick stall.** At 04:00 dozens of signals fire at once; each runs a catalyst lookup and a
   ~5 s four-voter LLM vote sequentially inside the tick. One tick ran 04:02→04:06 and tripped the
   MonitorOutageWatchdog (auto-filed IP-T583). Ticks of 30-80 s recur all morning. Fixed on main
   (IP-T583 exits-first + 20 s vote budget, IP-T585 pass membership), **not yet deployed**.
2. **Quorum lost to an unfunded voter.** The Claude voter fails (Anthropic credit balance too low,
   then circuit-breaker). A 2-1 split of the other three is 2/4 = 50% → "no approval quorum".
   11 panel approvals by 04:21 produced 1 order. Fix: top up credits, or count only voters that answered.
3. **Wide premarket spreads.** PENG 1.8%, MOD ~5%: entry limits rest unfilled or fill only on a
   pullback, while the Monitor already counts the strategy as holding. Candidate: skip entries with
   spread > 1.0%, cancel an unfilled entry after 3 min.
4. **Re-asking the panel until it says yes.** MPU vetoed 04:23, approved 04:26 on the same signal,
   entered at the local top, −3% in a minute.
5. **Broker stops do nothing before 09:30.** The GTC STP orders at Alpaca do not trigger in extended
   hours; only the Monitor's own stop (a limit order) protects a premarket position. OTLK traded
   through its −10% stop, the first exit limit rested unfilled, the second filled: −14.6%
   (11-18: BRFH −13%, VWAV −21% vs a 10% stop). Assume ~1.4x the set stop on micro names premarket.
6. **Buying the spike.** 9 of the first 12 fills were red within minutes; the small caps stayed red,
   the large caps (AMD, DELL, MRVL) dipped and recovered. Same as 11-18 ("late entries on faded gaps").
   Candidate for penny/micro: enter on the first 1-minute higher low above VWAP within 10 min of the signal.
7. **Micro trailing stop is 12% wide.** SPRC kept +61% of a ~+82% run; INLF's trail fired below
   entry (−7%). Candidate: arm the trail only after +5%, width 8%.
8. **Sector concentration.** Semis held at once: AMD, AMDL (2x AMD), NVDA, MRVL, CRDO, SOXL (3x),
   AEHR, plus STX and DELL hardware. The radar treats each symbol independently. Candidate: at most 2
   open positions per sector, leveraged ETFs counted as their underlying.
9. **Dollar P/L is price-weighted.** With 1 share, a −0.1% move on AMD costs more than SPRC's +61%
   made; judge templates by percent per trade, not account dollars.

## Update at 07:40 ET

~45 fills by 07:40, 39 open positions. Cumulative P/L moved between −$25.16 (07:29) and +$3.43 (06:49);
at 07:40 −$14.82. Closed so far: SPRC +61%, GMM +3.7%, MRVL +0.4%, DELL −0.1%, BRNX −6%, INLF −7%,
ICCM −10.4%, SPRC re-entry −10.4%, OTLK −14.6%.

3. (stronger) **Resting entry limits fill hours later on dead signals.** ZS: placed 05:02, filled 07:13.
   MOD: placed 04:37 in a ~5% spread, filled 07:37 (3 h). RIOX 38 min, BETA 15 min, PALU 16 min. The
   Monitor counts an unfilled entry as holding, so the strategy stops evaluating. Candidate: cancel an
   entry limit unfilled after 3 min and return the strategy to watching.
10. **Stops fired by single outlier prints.** DELL 07:37: stop on a 313.11 print while trading ~326.9,
    sold at 325.02 (a +$1.58 position closed −$0.33). MRVL 07:38: stop on 145.54 while trading ~151.5,
    sold at 151.60. Two mega-caps, ~4% low prints a minute apart, so likely condition-coded / out-of-
    sequence trades counted as price. Candidate: ignore condition-coded trades for stops; premarket,
    require bid < stop or two consecutive prints below it.
11. **Re-entries after a closed trade.** SPRC re-entered at 0.48 after its +61% run and lost 10.4% in
    4 min; INLF re-entered after a −7% trailing exit (open). One more loss makes it a pattern; candidate:
    no re-entry on a symbol after a closed trade the same morning without a fresh setup.
12. **Open gains round-trip.** Open P&L peaked +$20.78 at 06:49 and was −$7.24 by 07:29; no profit-taking
    rule on the large/mega bands and stops of 3-4% let it go. Candidate (with 7): take half off at +2R or
    trail the large/mega bands after +1%.

## Close-out (09:28-09:34 ET)

Paper 25K at 09:34: $24,951.01, **cumulative −$48.99 (−0.20%)**, session −$31.59. ~60 fills over the morning;
cumulative P/L ranged from +$3.43 (06:49) to −$50.78 (09:30).

- More stops on outlier prints (10): ZS 08:32 (stop on 197.83, sold 205.30) and DT 09:06 (stop on 43.50,
  sold 46.49); five in all (DELL, MRVL, ALAB, ZS, DT). Real stops for comparison: PALU, PANW, MOD filled
  at their stop level.
- Stale entries (3) priced: ZS −$2.20 and MOD −$4.00 after 2-3 h resting. The stale-entry cleanup exists
  (IP-T506) but fires after ~90 min (LGN 07:47 → cleared 09:15), while the Monitor showed LGN as held
  with unrealized P&L. Candidate: timeout ~3 min, cancel resting entries at the 09:00 window close.
- Sector concentration (8) cost money 08:18-08:30: AMD/AMDL/NVDA/SOXL fell together (AMD −$7.27 alone).
- Premarket stops cost ~1.3x the set stop on penny/micro (OTLK 1.46x, ANPA 1.33x, ICCM ~1.2x).
- **The 09:28 flatten missed again.** Exits go out tick by tick; one tick ran 09:27 → ~09:30, so 7 were
  still held at the open (30 of 37 flattened, every fill at or better than its limit).
13. **After 09:30 an exit can never fill.** The Monitor places the exit limit and, in the same tick and
    second, finds it still `new`, logs "rested unfilled" and cancels it — even a marketable limit (RKLX
    sell 9.50 with the bid at 9.53, 09:32). Repeats every tick; OKTG, RKLX, NBIL, STXU stayed held past
    the strategy's own exit. Candidate fix (code bug): judge an exit unfilled only after one full tick /
    30 s; for anything still held at 09:29 use a market-on-open order.

## Decisions waiting on the owner

- Deploy IP-T583/IP-T585 (Hub publish) before relying on premarket exits again.
- Top up the Anthropic credit (restores the 4th voter).
- File tickets for: spread filter + entry timeout (3), re-ask rule (4), premarket stop handling (5),
  pullback entry for penny/micro (6), micro trail (7), sector cap (8).
- Deactivate the 163 stale 11-18 strategies not in today's radar.

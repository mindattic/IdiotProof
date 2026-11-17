# Gapper study, amendment 07: the catalyst taxonomy, and a train-only report by catalyst type

Recorded 2026-11-17 (IP-T487) on the user's request that gapper drivers be called catalysts and classified by a
useful type list, before the new classifier was run over any stored row and before any number below was computed.
No earlier file is edited, no hypothesis or threshold of the study changes, and the holdout stays locked
(2025-11-03 .. 2026-09-09, amendment 05). This file has no `.json` twin: it adds no hypothesis the test stage runs.

## What changes, and what does not

- **The frozen classifier stays as it is.** protocol.md and params.json freeze the catalyst type list
  {Fda, ContractAward, ReverseSplit, OfferingDilution, Uplisting, Earnings, MA, PromoPr, Other} and the rule
  tables (`catalyst-preclass.json`, version 1) "with the code". H2 and H16 read that class from the snapshot rows,
  the news ride (A02) and the alarms (A04) read its direction, and the catalyst score is built on its type priors.
  All of that is kept bit for bit: the version 1 rules move, unchanged, into `CatalystLegacyClassifier`, and the
  stored columns they fill (`PreClass`, `PreDirection`, `PreConfidence`, `CatalystScore`, `Direction`) are never
  rewritten by this work. Snapshot rows already stored keep their values, and the holdout's snapshots, when the
  single run builds them, will get the same version 1 class as train and validation did.
- **A second, descriptive label is added beside it.** `CatalystPreClass` becomes the new list: outside investment
  / PIPE, acquisition target, acquisition acquirer, merger, earnings beat, earnings miss, earnings report (no
  verdict word), guidance, contract / partnership, FDA / clinical, reverse split / ADS ratio change, uplisting,
  listing compliance (deficiency notice, regained compliance, Form 25 / 15), buyback, analyst upgrade, offering /
  dilution, theme pivot, other (company news of no listed type) and no catalyst (coverage of the move or the
  market; an insider ownership form). A non-binding LOI / MOU / term sheet is a flag on the row, not a type. It
  is stored in new columns (`CatalystType`, `NonBindingLoi`) on every news and filing row; classification may
  be stored for every row of every split because it reads no price.
- **How a row gets its type.** Deterministic, no model, no LLM: the keyword classifier already fixed on train in
  IP-T488 (`CatalystTypes`: headline regexes with the version 1 class as a hint; form type and 8-K items for
  filings) reduced to one label by a fixed priority: coverage (no catalyst) > reverse split > acquisition target >
  merger > acquisition acquirer > FDA / clinical > offering / dilution > outside investment / PIPE > earnings beat
  > earnings miss > guidance > earnings report > uplisting > listing compliance > buyback > contract / partnership
  > analyst upgrade > theme pivot > other. The LOI flag is set whenever the LOI wording is present, whatever the
  type; LOI wording with no other type is a contract / partnership. The order was written here before any outcome
  by type was computed with it.
- **Not a study feature.** No frozen or amended hypothesis reads the new label. If a later amendment wants it as a
  feature, it registers that first.

## The report (train only, descriptive)

`research gappers explore catalyst-types`, through the explore stage's split guard (it refuses every split but
train, and its inputs are the train rows only). Train = 2021-11-01 .. 2024-10-31. Nothing from validation or the
holdout is read.

**Table A, per item.** Unit: one row of the `explore catalyst-events` table built on 2026-11-09 (symbol x stored
news item or EDGAR filing other than forms 3/4/5 whose event session is a train universe day with minute bars;
rows on excluded universe days are dropped). Type: as above, from the stored headline, version 1 class, form and
items. Per type:

- **run size** = highest high from the first full minute after publication to 16:00 over the reference price (the
  last trade that ended by publication), minus 1. Median, 75th and 90th percentile, share of rows >= +20% and
  >= +50%.
- **run duration** = minutes from the first full minute after publication to the minute of that high. Median.
- **fade** = the 16:00 close over that high, minus 1. Median.
- **kept** = the 16:00 close over the reference price, minus 1 (what a long from publication still holds at the
  close, before costs). Median, and the mean with a day-clustered 95% interval.

**Table B, per gapper day.** Unit: one train gapper day of the frozen study (`GapperStudyEvents`, split train: a
census). Catalyst: the highest-priority type among the stored news and filings (8-K, 8-K/A, S-1, S-3, 424B3/4/5,
SC 13D) for the symbol published after D-1 16:00 ET and at or before the first +20% touch, so an item that only
reports the move is not counted as its driver. Days with items of no listed type are "other"; days whose only
items are coverage are "coverage only"; days with no stored item in that window are "no catalyst". Per type:

- **run size** = the day's highest gain over the split-adjusted prior close (`GapMax`). Median, 75th and 90th
  percentile, share of days >= +50% and >= +100%.
- **run duration** = minutes from the first +20% touch to the high of day; and the median high-of-day clock.
- **fade** = the day's daily-bar close over the day's high (prior close x (1 + GapMax)), minus 1. Median.
- **kept** = the daily-bar close over the prior close, minus 1. Median.

**Correction to Table B, 2026-11-17, before any number was computed.** While writing the report code it turned out
that `GapMax` is the high inside the frozen gapper window only, not the day's high. Table B therefore uses the
session high (the larger of the stored premarket high and regular-hours high, 04:00-16:00) wherever the text above
says `GapMax`: run size = session high over the split-adjusted prior close, minus 1; fade = the daily-bar close
over the session high, minus 1. `GapMax` is not used. Nothing else changes.

**Reading rules, stated before the numbers.**

1. Descriptive only. Nothing here is "significant", and no type is promoted to a trade by this report. A type
   that looks tradable goes through exploration with costs, the ledger, a registered amendment and validation
   like every other idea.
2. Long side only. The project does not short; a negative "kept" is a reason to stay out, not a short.
3. Table A is conditional on the sessions the study holds minute bars for (gapper candidate days, their
   neighbours, matched controls and the 10% population sample), so its levels run higher than the market's; the
   comparison between types is the content. Table B is conditional on the day having become a gapper, so it says
   what each kind of gapper did, not how often a catalyst makes one.
4. One ledger trial is logged per type and table (metric: the median run size), so the trial count that deflates
   later finalists includes this look.
5. The news store is Alpaca / Benzinga and the filing store is EDGAR; "no catalyst" means none stored there.

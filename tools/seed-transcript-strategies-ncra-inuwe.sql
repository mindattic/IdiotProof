-- Strategies distilled from a stock-watchlist video transcript (per Quick Action QA-3).
-- DFNS skipped: transcript calls it "the party's over... time to go home before this comes
-- crashing back down" - an exit/avoid warning, not a new long entry setup.
-- Created IsActive=0 (paused) - review on the Strategies page before arming.
DECLARE @OwnerUserId UNIQUEIDENTIFIER = (SELECT TOP 1 OwnerUserId FROM Strategies);
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

-- ── NCRA - reverse-split squeeze breakout ───────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'NCRA - Reverse-Split Squeeze Breakout',
    N'Squeezed 150%+, same reverse-split profile as DFNS (which is now rolling over). Break $3.70 prior closing high, pullback holds $3.20 + VWAP reclaim, target $5. Extension noted in transcript: break $5.40 -> pullback confirms over $5 -> next leg $7-8 (not encoded as a script target).',
    N'watchlist-transcript',
    N'Next up at number two is the one that squeezed over 150% and it could be following the same fate as DFNS. It also had a massive reverse split here on NCRA. Now, both of these things have one main thing in common. They both had these massive reverse splits, so this thing could be next up to make a big move. Now, when traders start to fly away from DFNS to look for the next setup, is it going to be NCRA? Well, there''s only one way to find out. What that looks like and what I have to see here on NCRA is a strong break over this previous high into market close over $3.70 over that closing price high. If price breaks in a strong way, watch for pullbacks to confirm some bottoms over $3.20, of course, back over VWAP. From there, we have range back to the upside to $5 a share, where we can also watch this too. If price breaks over 540, then look for pullbacks to over five to confirm for the next leg higher towards seven and eight dollars a share.',
    N'NCRA',
    N'Stock.Ticker("NCRA")
    .Name("NCRA - Reverse-Split Squeeze Breakout")
    .BreaksAbove(3.70)
    .HoldsAbove(3.20)
    .OnVwapReclaim()
    .Long()
    .QuantityShares(1)
    .TakeProfit(5.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "NCRA",
  "name": "NCRA - Reverse-Split Squeeze Breakout",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 3.70 },
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 3.20 },
    { "type": "indicator", "indicator": "VwapReclaim" }
  ],
  "takeProfitPrice": 5.00,
  "takeProfitTargets": [
    { "label": "T1", "price": 5.00, "percentToSell": 100 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── INUWE - after-hours squeeze breakout ────────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'INUWE - AH Squeeze Breakout',
    N'Beat-down all-time-lows name up 100%+ after hours, squeezing back into a gap on the daily chart. $2.50 must hold on any VWAP pullback; break $4.40 targets $7.50/$10 - most range of any setup in this list.',
    N'watchlist-transcript',
    N'Now, that stock here is INUWE, which is up over 100% here in after hours, and you might be asking yourself, well, how much room does this really have left? Well, a lot. Now, this thing was crazy beat down trading at all-time lows and all of a sudden this thing''s getting squeezing back up over its previous highs into this massive gap on the daily chart. So, now what I want to see here tomorrow is either pull back somewhere over VWAP and it''s really just over 250 is level that has to hold. It looks fantastic that way. But, if price gets over 440, look, I like it over 440 for the range of 750 and 10. Either way, this thing looks fantastic. The most amount of range out of any setup we have for tomorrow.',
    N'INUWE',
    N'Stock.Ticker("INUWE")
    .Name("INUWE - AH Squeeze Breakout")
    .HoldsAbove(2.50)
    .IsAboveVwap()
    .BreaksAbove(4.40)
    .Long()
    .QuantityShares(1)
    .TakeProfit(7.50, 10.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "INUWE",
  "name": "INUWE - AH Squeeze Breakout",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 2.50 },
    { "type": "indicator", "indicator": "VwapAbove" },
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 4.40 }
  ],
  "takeProfitPrice": 7.50,
  "takeProfitTargets": [
    { "label": "T1", "price": 7.50, "percentToSell": 50 },
    { "label": "T2", "price": 10.00, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

SELECT Id, Symbol, Title, IsActive FROM Strategies WHERE Symbol IN ('NCRA','INUWE') ORDER BY CreatedUtc DESC;

-- Strategies distilled from a stock-watchlist video transcript (per Quick Action QA-3).
-- FCUV skipped: transcript explicitly puts it on the "do not trade list" for Monday - a
-- "so trappy it was unbelievable" avoid warning, not a new long entry setup.
-- Created IsActive=0 (paused) - review on the Strategies page before arming.
DECLARE @OwnerUserId UNIQUEIDENTIFIER = (SELECT TOP 1 OwnerUserId FROM Strategies);
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

-- ── UPC - day-2 confirmation breakout (8.30) ────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'UPC - Day-2 Confirmation Breakout (8.30)',
    N'Squeezed 100%+ after hours Friday on news. Day-2 move: strong break of previous high $8.30, pullback confirms retest off VWAP, buyers take it back up through new highs -> range to past daily highs $11/$17.',
    N'watchlist-transcript',
    N'Now, all of these after hours gappers we have to watch pretty much the same way. Now, what I want to see here and you probably already guessed it if you watch my videos, you know how we have to watch these for a day two move, we have to see it confirm the upside. So, now what I want to see simply put is a strong break of this previous high over $8.30 to make sure it does confirm that day two move. If price does break in a strong way over that past high, watch for price to then pull back, confirm retest off of VWAP, find a buyer to take this back up through new highs, then we have a range back to the past daily highs of 11 and 17 bucks a share.',
    N'UPC',
    N'Stock.Ticker("UPC")
    .Name("UPC - Day-2 Confirmation Breakout (8.30)")
    .BreaksAbove(8.30)
    .OnVwapReclaim()
    .Long()
    .QuantityShares(1)
    .TakeProfit(11.00, 17.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "UPC",
  "name": "UPC - Day-2 Confirmation Breakout (8.30)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 8.30 },
    { "type": "indicator", "indicator": "VwapReclaim" }
  ],
  "takeProfitPrice": 11.00,
  "takeProfitTargets": [
    { "label": "T1", "price": 11.00, "percentToSell": 50 },
    { "label": "T2", "price": 17.00, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── CYCU - morning breakdown curl reversal (VWAP reclaim) ───────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'CYCU - Morning Breakdown Curl Reversal',
    N'Squeezed 1,000%+ last week on government-contract news, gave back most of Friday''s move. Thesis: morning breakdown attempt gets bought, confirmed bottom back over VWAP, curls back up to retest Thursday''s highs $1.50/$1.80.',
    N'watchlist-transcript',
    N'Now, it gave back most of its move Friday, but I think it looks good for a possible curl if buyers do step back in going into the new week. So, now what I want to see is I want to see it try to break down lower in the morning. When it tries to break down, buyers are going to step back in and say, "No, no, no." Look for confirmed bottoms back over VWAP to take this back up higher on the curl, back to retest Thursday''s highs into the close toward the $1.50 and $1.80.',
    N'CYCU',
    N'Stock.Ticker("CYCU")
    .Name("CYCU - Morning Breakdown Curl Reversal")
    .OnVwapReclaim()
    .IsHigherLow()
    .Long()
    .QuantityShares(1)
    .TakeProfit(1.50, 1.80)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "CYCU",
  "name": "CYCU - Morning Breakdown Curl Reversal",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "indicator", "indicator": "VwapReclaim" },
    { "type": "indicator", "indicator": "HigherLow" }
  ],
  "takeProfitPrice": 1.50,
  "takeProfitTargets": [
    { "label": "T1", "price": 1.50, "percentToSell": 50 },
    { "label": "T2", "price": 1.80, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

SELECT Id, Symbol, Title, IsActive FROM Strategies WHERE Symbol IN ('UPC','CYCU') ORDER BY CreatedUtc DESC;

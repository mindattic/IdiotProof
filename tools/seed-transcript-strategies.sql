-- Strategies distilled from a stock-watchlist video transcript (per Quick Action QA-3).
-- Created IsActive=0 (paused) - review on the Strategies page before arming.
DECLARE @OwnerUserId UNIQUEIDENTIFIER = (SELECT TOP 1 OwnerUserId FROM Strategies);
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

-- ── IPW - VWAP reclaim + breakout retest ────────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'IPW - VWAP Reclaim & Breakout Retest',
    N'AH squeeze off GPU-leasing news, sold off twice off highs (likely shorts piled in). Reclaim VWAP + break $1.15 prior high, pullback holds $1.10, targets $2.00/$2.40.',
    N'watchlist-transcript',
    N'Now first up on the list is this massive explosion on news today that somehow closed red then it had this massive squeeze in after hours on IPW with its AI strategy GPU leasing deal. And now this looks interesting to me because it sold off twice off the highs. I would assume shorts probably piled in off the highs. Can we then squeeze shorts over those highs back to new highs back to 140 and two? Well, it''s definitely possible. So what I want to see here going into tomorrow is strength back over VWAP. If buyers can reclaim get back over VWAP and then confirm bottoms above then this looks fantastic for a pullback opportunity to retest today''s high of day around a dollar and 15 cents. If price is then able to break over 115, we can also watch for dips to confirm there over a dollar and five a dollar and 10 cents for pullback opportunities for the next leg higher to $2.40 and even two bucks a share. But of course as always no break, no confirmation, no trade.',
    N'IPW',
    N'Stock.Ticker("IPW")
    .Name("IPW - VWAP Reclaim & Breakout Retest")
    .OnVwapReclaim()
    .BreaksAbove(1.15)
    .HoldsAbove(1.10)
    .Long()
    .TakeProfit(2.00, 2.40)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "IPW",
  "name": "IPW - VWAP Reclaim & Breakout Retest",
  "session": "RTH",
  "quantity": 0,
  "notionalAmount": 1000,
  "direction": "Long",
  "entryConditions": [
    { "type": "indicator", "indicator": "VwapReclaim" },
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 1.15 },
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 1.10 }
  ],
  "takeProfitPrice": 2.00,
  "takeProfitTargets": [
    { "label": "T1", "price": 2.00, "percentToSell": 50 },
    { "label": "T2", "price": 2.40, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── WBY - day-2 breakout continuation ────────────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'WBY - Day-2 Breakout Continuation',
    N'125% gapper on news, day-2 setup. Break $1.20 prior high, pullback holds $1.10 + VWAP, retest targets $1.50/$1.68.',
    N'watchlist-transcript',
    N'Next up and number two is the 125% gapper today over on WBY which also had news today. Now this also looks great for a possible day two move and there''s also two ways you can watch this too. Now I want to watch this if buyers step back in on day two. Now what that looks like is I want to see buyers step in and break over the past high over a dollar and 20 cent. If buyers do step in over 120 with strength I then want to see pullbacks to confirm back over a dollar and 10 cents of course back over VWAP. From there you have a picture perfect pullback opportunity to retest the past highs around a dollar 50 and a dollar 68. If we do get breaking over 168 there''s also pullback opportunities there for 150 and again as always back over VWAP for that next leg higher back over two and 260.',
    N'WBY',
    N'Stock.Ticker("WBY")
    .Name("WBY - Day-2 Breakout Continuation")
    .BreaksAbove(1.20)
    .IsAboveVwap()
    .HoldsAbove(1.10)
    .Long()
    .TakeProfit(1.50, 1.68)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "WBY",
  "name": "WBY - Day-2 Breakout Continuation",
  "session": "RTH",
  "quantity": 0,
  "notionalAmount": 1000,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 1.20 },
    { "type": "indicator", "indicator": "VwapAbove" },
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 1.10 }
  ],
  "takeProfitPrice": 1.50,
  "takeProfitTargets": [
    { "label": "T1", "price": 1.50, "percentToSell": 50 },
    { "label": "T2", "price": 1.68, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── LGCL - consolidation breakout ────────────────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'LGCL - Consolidation Breakout',
    N'Rocketed 100%+ this morning, coiling near highs all day. Break $2.50 (daily level) for the range breakout, targets $3.50/$5.00.',
    N'watchlist-transcript',
    N'Next up and last but not least the most explosive stock for tomorrow''s trading day is the one that''s out here consolidating almost at its highs all day, coiled and ready to break out higher. That stock here is the one that rocketed well over 100% this morning over on LGCL. Now, if price can break over 247, really over 250, this looks fantastic, a perfect daily level breakout as well for the range back higher to 350 and five.',
    N'LGCL',
    N'Stock.Ticker("LGCL")
    .Name("LGCL - Consolidation Breakout")
    .BreaksAbove(2.50)
    .Long()
    .TakeProfit(3.50, 5.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "LGCL",
  "name": "LGCL - Consolidation Breakout",
  "session": "RTH",
  "quantity": 0,
  "notionalAmount": 1000,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 2.50 }
  ],
  "takeProfitPrice": 3.50,
  "takeProfitTargets": [
    { "label": "T1", "price": 3.50, "percentToSell": 50 },
    { "label": "T2", "price": 5.00, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

SELECT Id, Symbol, Title, IsActive FROM Strategies WHERE Symbol IN ('IPW','WBY','LGCL') ORDER BY CreatedUtc DESC;

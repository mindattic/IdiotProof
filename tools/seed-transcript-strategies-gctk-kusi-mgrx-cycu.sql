-- Strategies distilled from a stock-watchlist video transcript (per Quick Action QA-3).
-- GCTK and CYCU each have two transcript-described entry paths -> two rows apiece
-- (same convention as the existing GMM/HIHO Breakout-Pullback + Higher-Low Reclaim pairs).
-- Created IsActive=1 per explicit user instruction (activate immediately). BrokerMode=Paper,
-- QuantityShares(1) per feedback_strategy_default_qty (paper buying-power protection).
DECLARE @OwnerUserId UNIQUEIDENTIFIER = (SELECT TOP 1 OwnerUserId FROM Strategies);
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

-- ── GCTK - higher-low reclaim path (0.58) ───────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'GCTK Higher-Low Reclaim (0.58)',
    N'Squeezed 200%+ today on news. Path A: higher lows confirm over VWAP at $0.58 support -> pullback opportunity to retest previous high near $1.00.',
    N'watchlist-transcript',
    N'So, now what I want to see here, there''s actually two ways to watch this. So, I either want to see it put in place higher lows back over VWAP over 58 cents. Put in place some higher lows. That looks great for a pullback opportunity to retest previous highs towards a dollar.',
    N'GCTK',
    N'Stock.Ticker("GCTK")
    .Name("GCTK Higher-Low Reclaim (0.58)")
    .HoldsAbove(0.58)
    .IsAboveVwap()
    .IsHigherLow()
    .Long()
    .QuantityShares(1)
    .TakeProfit(1.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "GCTK",
  "name": "GCTK Higher-Low Reclaim (0.58)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 0.58 },
    { "type": "indicator", "indicator": "VwapAbove" },
    { "type": "indicator", "indicator": "HigherLow" }
  ],
  "takeProfitPrice": 1.00,
  "takeProfitTargets": [
    { "label": "T1", "price": 1.00, "percentToSell": 100 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    1, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── GCTK - breakout/pullback path (1.02) ────────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'GCTK Breakout-Pullback (1.02)',
    N'Squeezed 200%+ today on news. Path B: break previous high $1.02, pullback confirms over VWAP at $0.90, next leg to past daily highs $1.30/$1.70.',
    N'watchlist-transcript',
    N'Or I even like it to if we break over the previous high of a dollar and two cents. Look for pullbacks to confirm over VWAP at 90 cents. Again, pullback opportunity for that next leg higher back to 130 and 170 to those past daily highs.',
    N'GCTK',
    N'Stock.Ticker("GCTK")
    .Name("GCTK Breakout-Pullback (1.02)")
    .BreaksAbove(1.02)
    .HoldsAbove(0.90)
    .OnVwapReclaim()
    .Long()
    .QuantityShares(1)
    .TakeProfit(1.30, 1.70)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "GCTK",
  "name": "GCTK Breakout-Pullback (1.02)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 1.02 },
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 0.90 },
    { "type": "indicator", "indicator": "VwapReclaim" }
  ],
  "takeProfitPrice": 1.30,
  "takeProfitTargets": [
    { "label": "T1", "price": 1.30, "percentToSell": 50 },
    { "label": "T2", "price": 1.70, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    1, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── KUSI - day-2 confirmation breakout (1.45) ───────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'KUSI - Day-2 Confirmation Breakout (1.45)',
    N'Up 50%+ after hours; $739K market cap, far below the new $5M compliance floor - desperate reverse-split-squeeze candidate. Transcript is explicit: "no confirmation, no trade" - requires day-2 higher lows before the breakout is valid. Break/hold $1.45 over VWAP -> daily breakout targeting past highs $2.50/$3.00, grand-slam range to $4.50.',
    N'watchlist-transcript',
    N'Now, that stock here is KUSI which right now it''s up over 50% in after hours. I want to watch it again tomorrow if buyers do start to step back in for a day two move. I got to see strength put in place some higher lows. Again, as always, back over VWAP. Make sure we''re playing the front side of that move. If buyers do step in, confirm a nice over 145 over VWAP, you have a perfect daily breakout for the next leg higher to squeeze back towards past daily highs to 250, three, and then a grand slam range to $4.50. As always, especially for this one, no confirmation, no trade.',
    N'KUSI',
    N'Stock.Ticker("KUSI")
    .Name("KUSI - Day-2 Confirmation Breakout (1.45)")
    .BreaksAbove(1.45)
    .IsAboveVwap()
    .IsHigherLow()
    .Long()
    .QuantityShares(1)
    .TakeProfit(2.50, 3.00, 4.50)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "KUSI",
  "name": "KUSI - Day-2 Confirmation Breakout (1.45)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 1.45 },
    { "type": "indicator", "indicator": "VwapAbove" },
    { "type": "indicator", "indicator": "HigherLow" }
  ],
  "takeProfitPrice": 2.50,
  "takeProfitTargets": [
    { "label": "T1", "price": 2.50, "percentToSell": 40 },
    { "label": "T2", "price": 3.00, "percentToSell": 30 },
    { "label": "T3", "price": 4.50, "percentToSell": 30 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    1, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── MGRX - VWAP confirmation breakout (0.60) ────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'MGRX - VWAP Confirmation Breakout (0.60)',
    N'Up 150%+ after hours. Watch for confirmation buyers stepping in and holding over VWAP at $0.60; range back to $1.50/$2.00.',
    N'watchlist-transcript',
    N'MGRX up over 150% here in after hours. Watch for confirmation buyers to step in over VWAP over 60 cents. Then this has range back to $1.50 and two.',
    N'MGRX',
    N'Stock.Ticker("MGRX")
    .Name("MGRX - VWAP Confirmation Breakout (0.60)")
    .HoldsAbove(0.60)
    .IsAboveVwap()
    .Long()
    .QuantityShares(1)
    .TakeProfit(1.50, 2.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "MGRX",
  "name": "MGRX - VWAP Confirmation Breakout (0.60)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 0.60 },
    { "type": "indicator", "indicator": "VwapAbove" }
  ],
  "takeProfitPrice": 1.50,
  "takeProfitTargets": [
    { "label": "T1", "price": 1.50, "percentToSell": 50 },
    { "label": "T2", "price": 2.00, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    1, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── CYCU - bottoming-wick VWAP reclaim path (1.67) ──────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'CYCU Bottoming-Wick Reclaim (1.67)',
    N'Ran 500%+ today, still cheap with room. Path A: liquidity grab / bottoming wick under VWAP that reclaims and holds $1.67 confirms the bottom -> pullback opportunity to $2.60/$3.00.',
    N'watchlist-transcript',
    N'What I want to see here is either a grab of liquidity underneath VWAP over $1.67. I want to see it put in place a bottoming wick underneath VWAP over $1.67 to confirm that bottom. If it does, that looks perfect for a pullback opportunity for the range back to 260 and three.',
    N'CYCU',
    N'Stock.Ticker("CYCU")
    .Name("CYCU Bottoming-Wick Reclaim (1.67)")
    .HoldsAbove(1.67)
    .OnVwapReclaim()
    .Long()
    .QuantityShares(1)
    .TakeProfit(2.60, 3.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "CYCU",
  "name": "CYCU Bottoming-Wick Reclaim (1.67)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 1.67 },
    { "type": "indicator", "indicator": "VwapReclaim" }
  ],
  "takeProfitPrice": 2.60,
  "takeProfitTargets": [
    { "label": "T1", "price": 2.60, "percentToSell": 50 },
    { "label": "T2", "price": 3.00, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    1, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

-- ── CYCU - breakout/pullback path (3.00) ────────────────────────────────────
INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'CYCU Breakout-Pullback (3.00)',
    N'Ran 500%+ today, still cheap with room. Path B: break $3, pullback confirms over $2.60, range back to next highs $5/$7.',
    N'watchlist-transcript',
    N'If you want to keep it easy, I like it, too, if we do break over $3 a share, watch for pullbacks over 260 to confirm, then we have range back to the upside to next highs to five and seven.',
    N'CYCU',
    N'Stock.Ticker("CYCU")
    .Name("CYCU Breakout-Pullback (3.00)")
    .BreaksAbove(3.00)
    .HoldsAbove(2.60)
    .IsAboveVwap()
    .Long()
    .QuantityShares(1)
    .TakeProfit(5.00, 7.00)
    .StopLossPercent(8)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "CYCU",
  "name": "CYCU Breakout-Pullback (3.00)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "priceLevel", "kind": "BreaksAbove", "level": 3.00 },
    { "type": "priceLevel", "kind": "HoldsAbove", "level": 2.60 },
    { "type": "indicator", "indicator": "VwapAbove" }
  ],
  "takeProfitPrice": 5.00,
  "takeProfitTargets": [
    { "label": "T1", "price": 5.00, "percentToSell": 50 },
    { "label": "T2", "price": 7.00, "percentToSell": 50 }
  ],
  "stopLossPercent": 8,
  "exitTime": "15:55"
}',
    1, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

SELECT Id, Symbol, Title, IsActive, BrokerMode FROM Strategies WHERE Symbol IN ('GCTK','KUSI','MGRX','CYCU') ORDER BY CreatedUtc DESC;

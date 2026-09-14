-- BE - Pullback & EMA Reclaim, gated by BeBexDecayScanner (per Quick Action QA-3 pattern).
-- Origin: user-pasted "Long BE / Short BEX pairs trade" idea. The short BEX leg and any
-- live cross-ticker DSL condition are out of scope (shorts don't execute yet; no strategy
-- can read a second ticker's price live) -- this is the BE-only long side, which
-- BeBexDecayScanner arms/disarms daily based on a BE-vs-BEX decay/divergence reading.
-- Created IsActive=0 (paused) - review sizing (quantity vs. notionalAmount against the
-- current BE quote) on the Strategies page before arming.
DECLARE @OwnerUserId UNIQUEIDENTIFIER = (SELECT TOP 1 OwnerUserId FROM Strategies);
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

INSERT INTO Strategies
    (Id, OwnerUserId, Title, Description, Author, OriginTranscript, Symbol, ScriptText, ScriptJson,
     IsActive, WorkspaceId, CreatedUtc, UpdatedUtc, LastFiredUtc, FireCount,
     PositionQty, LastEntryPrice, EntryFilledUtc, LastExitedUtc, LastExitPrice, LastExitReason, BrokerMode)
VALUES
(
    NEWID(), @OwnerUserId,
    N'BE - Pullback & EMA Reclaim (BEX decay-gated)',
    N'Pullback-and-reclaim long on BE (Bloom Energy): price pulls back below the fast EMA within an established 9/31 uptrend, then reclaims it on confirmed volume while holding above VWAP. Mean-reversion-aware exit (PeakGiveback) plus a hard stop and end-of-day flatten. Entries are armed/disarmed daily by BeBexDecayScanner based on a BE-vs-BEX (2x leveraged long ETF) decay/divergence reading -- see IdiotProof.Monitor/BeBexDecayScanner.cs.',
    N'user-pairs-trade-idea',
    N'TRADING STRATEGY: Long BE / Short BEX Pairs Trade (CORRECTED)

ASSETS:
- BE: Bloom Energy Corp stock (underlying asset)
- BEX: Tradr 2X Long BE Daily ETF (2x leveraged LONG on BE)

[... full pasted thesis retained for provenance; see chat history for the complete text.
Short BEX leg not automated -- short orders do not execute on this platform yet, and no
strategy DSL condition can read a second ticker''s price live. This row implements only
the BE long side, decay-gated by a separate periodic scanner rather than a live short.]',
    N'BE',
    N'Stock.Ticker("BE")
    .Name("BE - Pullback & EMA Reclaim (BEX decay-gated)")
    .RequireEmaStack(9, 31)
    .OnReclaim(9)
    .WithVolumeConfirm(1.2)
    .IsAboveVwap()
    .Long()
    .PeakGiveback(25, "10:00")
    .StopLossPercent(6)
    .SellBy("15:55")
    .Build()',
    N'{
  "schemaVersion": 1,
  "symbol": "BE",
  "name": "BE - Pullback & EMA Reclaim (BEX decay-gated)",
  "session": "RTH",
  "quantity": 1,
  "notionalAmount": null,
  "direction": "Long",
  "entryConditions": [
    { "type": "indicator", "indicator": "EmaStack", "p1": 9, "p2": 31, "phase": "Filters" },
    { "type": "indicator", "indicator": "ReclaimEma", "p1": 9 },
    { "type": "indicator", "indicator": "VolumeAbove", "p1": 1.2 },
    { "type": "indicator", "indicator": "VwapAbove" }
  ],
  "peakGivebackPercent": 25,
  "peakGivebackArmTime": "10:00",
  "stopLossPercent": 6,
  "exitTime": "15:55"
}',
    0, NULL, @Now, @Now, NULL, 0,
    0, NULL, NULL, NULL, NULL, NULL, N'Paper'
);

SELECT Id, Symbol, Title, IsActive, BrokerMode FROM Strategies WHERE Symbol = 'BE' ORDER BY CreatedUtc DESC;

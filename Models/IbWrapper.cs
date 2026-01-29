// ============================================================================
// IB Wrapper - EWrapper implementation for IBKR API
// ============================================================================
//
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  ⚠️  WARNING: DO NOT MODIFY THIS FILE  ⚠️                                  ║
// ║                                                                           ║
// ║  This class implements the EWrapper interface from the Interactive        ║
// ║  Brokers TWS API DLL (IBApi). The interface is defined by IB and cannot   ║
// ║  be changed.                                                              ║
// ║                                                                           ║
// ║  LOCKED:                                                                  ║
// ║    • Method signatures (names, parameters, return types)                  ║
// ║    • Class inheritance (must implement EWrapper)                          ║
// ║    • Interface member implementations                                     ║
// ║                                                                           ║
// ║  SAFE TO MODIFY:                                                          ║
// ║    • Method body implementations (logic inside methods)                   ║
// ║    • Private fields and helper methods                                    ║
// ║    • Custom events and properties (non-interface members)                 ║
// ║    • XML documentation comments                                           ║
// ║                                                                           ║
// ║  Reference: https://interactivebrokers.github.io/tws-api/               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// FUNCTIONALITY:
// - Captures last trade prints for VWAP calculation
// - Captures nextValidId for order IDs
// - Captures execution details for fills
// - Routes market data to registered handlers per ticker
//
// ============================================================================

using IBApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using IbContract = IBApi.Contract;

namespace IdiotProof.Models 
{
    /// <summary>
    /// EWrapper implementation for the IBKR TWS API.
    /// Handles market data, order IDs, and execution reports.
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ EXTERNAL DEPENDENCY - DO NOT MODIFY INTERFACE MEMBERS ⚠️</b></para>
    /// <para>
    /// This class implements <see cref="EWrapper"/> from the Interactive Brokers TWS API DLL.
    /// All interface method signatures are defined by IB and cannot be changed.
    /// </para>
    /// 
    /// <para><b>What is LOCKED (do not change):</b></para>
    /// <list type="bullet">
    ///   <item>Method signatures (names, parameters, return types)</item>
    ///   <item>Class inheritance (<c>: EWrapper</c>)</item>
    ///   <item>Interface member implementations</item>
    /// </list>
    /// 
    /// <para><b>What is SAFE to modify:</b></para>
    /// <list type="bullet">
    ///   <item>Method body implementations (logic inside methods)</item>
    ///   <item>Private fields and helper methods</item>
    ///   <item>Custom events like <see cref="OnLastTrade"/> and <see cref="OnOrderFill"/></item>
    ///   <item>XML documentation comments</item>
    /// </list>
    /// 
    /// <para><b>Key Events:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="OnLastTrade"/>: Fired when last trade data is received.</item>
    ///   <item><see cref="OnOrderFill"/>: Fired when an order execution is reported.</item>
    /// </list>
    /// 
    /// <para><b>Reference:</b> https://interactivebrokers.github.io/tws-api/</para>
    /// </remarks>
    public sealed class IbWrapper : EWrapper
    {
        public EReaderSignal Signal { get; } = new EReaderMonitorSignal();

        private EClientSocket? _client;

        private readonly ManualResetEventSlim _nextValidIdEvent = new ManualResetEventSlim(false);
        private readonly object _orderIdLock = new object();
        private int _nextOrderId = -1;

        // Market data caches for LAST and LAST_SIZE pairing
        private readonly ConcurrentDictionary<int, double> _lastByTicker = new();
        private readonly ConcurrentDictionary<int, int> _lastSizeByTicker = new();

        // Per-ticker handlers for routing market data to specific runners
        private readonly ConcurrentDictionary<int, Action<double, int>> _tickerHandlers = new();

        /// <summary>
        /// Event fired when any last trade is received (legacy, fires for all tickers).
        /// </summary>
        public event Action<double, int>? OnLastTrade;

        /// <summary>
        /// Event fired when any order fill is received.
        /// Parameters: orderId, fillPrice, fillSize
        /// </summary>
        public event Action<int, double, int>? OnOrderFill;

        public void AttachClient(EClientSocket client)
        {
            _client = client;
        }

        /// <summary>
        /// Registers a handler for market data from a specific ticker ID.
        /// </summary>
        public void RegisterTickerHandler(int tickerId, Action<double, int> handler)
        {
            _tickerHandlers[tickerId] = handler;
        }

        /// <summary>
        /// Unregisters the handler for a specific ticker ID.
        /// </summary>
        public void UnregisterTickerHandler(int tickerId)
        {
            _tickerHandlers.TryRemove(tickerId, out _);
        }

        public bool WaitForNextValidId(TimeSpan timeout)
        {
            return _nextValidIdEvent.Wait(timeout);
        }

        public int ConsumeNextOrderId()
        {
            lock (_orderIdLock)
            {
                if (_nextOrderId < 0)
                    throw new InvalidOperationException("nextOrderId not initialized.");

                int id = _nextOrderId;
                _nextOrderId++;
                return id;
            }
        }

        // -------------------------
        // Connection / Errors
        // -------------------------
        public void error(Exception e)
        {
            Console.WriteLine("IB ERROR (Exception): " + e);
        }

        public void error(string str)
        {
            Console.WriteLine("IB ERROR: " + str);
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            // Codes 2100-2199 are informational status messages - suppress them
            if (errorCode >= 2100 && errorCode <= 2199)
            {
                return;
            }

            Console.WriteLine($"IB ERROR: id={id} code={errorCode} msg={errorMsg}");
        }

        public void error(int id, long time, int errorCode, string errorMsg, string advancedOrderRejectJson)
        {
            // Codes 2100-2199 are informational status messages - suppress them
            if (errorCode >= 2100 && errorCode <= 2199)
            {
                return;
            }

            Console.WriteLine($"IB ERROR: id={id} code={errorCode} msg={errorMsg}");
        }

        public void connectionClosed()
        {
            Console.WriteLine("IB connection closed.");
        }

        public void connectAck()
        {
            if (_client != null && _client.AsyncEConnect)
            {
                _client.startApi();
            }
        }

        // -------------------------
        // Order IDs
        // -------------------------
        public void nextValidId(int orderId)
        {
            lock (_orderIdLock)
            {
                _nextOrderId = orderId;
            }

            Console.WriteLine("Received nextValidId: " + orderId);
            _nextValidIdEvent.Set();
        }

        // -------------------------
        // Market data (Last / Last Size)
        // -------------------------
        public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            if (field == TickType.LAST)
            {
                _lastByTicker[tickerId] = price;

                if (_lastSizeByTicker.TryGetValue(tickerId, out int size))
                {
                    // Fire ticker-specific handler if registered
                    if (_tickerHandlers.TryGetValue(tickerId, out var handler))
                    {
                        handler(price, size);
                    }

                    // Fire global event (legacy)
                    OnLastTrade?.Invoke(price, size);
                }
            }
        }

        public void tickSize(int tickerId, int field, decimal size)
        {
            if (field == TickType.LAST_SIZE)
            {
                _lastSizeByTicker[tickerId] = (int)size;

                if (_lastByTicker.TryGetValue(tickerId, out double price))
                {
                    // Fire ticker-specific handler if registered
                    if (_tickerHandlers.TryGetValue(tickerId, out var handler))
                    {
                        handler(price, (int)size);
                    }

                    // Fire global event (legacy)
                    OnLastTrade?.Invoke(price, (int)size);
                }
            }
        }

        // -------------------------
        // Fills
        // -------------------------
        public void execDetails(int reqId, IbContract contract, IBApi.Execution execution)
        {
            if (execution != null)
            {
                OnOrderFill?.Invoke(execution.OrderId, execution.Price, (int)execution.Shares);
            }
        }

        public void execDetailsEnd(int reqId) { }

        // -------------------------
        // Unused EWrapper members (required by interface)
        // -------------------------

        public void tickGeneric(int tickerId, int field, double value) { }
        public void tickString(int tickerId, int field, string value) { }
        public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double totalDividends, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate) { }
        public void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice) { }
        public void openOrder(int orderId, IbContract contract, IBApi.Order order, IBApi.OrderState orderState) { }
        public void openOrderEnd() { }
        public void updateAccountValue(string key, string value, string currency, string accountName) { }
        public void updatePortfolio(IbContract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName) { }
        public void updateAccountTime(string timestamp) { }
        public void accountDownloadEnd(string accountName) { }
        public void contractDetails(int reqId, IBApi.ContractDetails contractDetails) { }
        public void bondContractDetails(int reqId, IBApi.ContractDetails contractDetails) { }
        public void contractDetailsEnd(int reqId) { }
        public void managedAccounts(string accountsList) { }
        public void receiveFA(int faDataType, string faXmlData) { }
        public void historicalData(int reqId, Bar bar) { }
        public void historicalDataEnd(int reqId, string start, string end) { }
        public void scannerParameters(string xml) { }
        public void scannerData(int reqId, int rank, IBApi.ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr) { }
        public void scannerDataEnd(int reqId) { }
        public void realtimeBar(int reqId, long time, double open, double high, double low, double close, decimal volume, decimal wap, int count) { }
        public void currentTime(long time) { }
        public void fundamentalData(int reqId, string data) { }
        public void deltaNeutralValidation(int reqId, IBApi.DeltaNeutralContract deltaNeutralContract) { }
        public void tickSnapshotEnd(int reqId) { }
        public void marketDataType(int reqId, int marketDataType) { }
        public void commissionAndFeesReport(IBApi.CommissionAndFeesReport report) { }
        public void position(string account, IbContract contract, decimal pos, double avgCost) { }
        public void positionEnd() { }
        public void accountSummary(int reqId, string account, string tag, string value, string currency) { }
        public void accountSummaryEnd(int reqId) { }
        public void verifyMessageAPI(string apiData) { }
        public void verifyCompleted(bool isSuccessful, string errorText) { }
        public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge) { }
        public void verifyAndAuthCompleted(bool isSuccessful, string errorText) { }
        public void displayGroupList(int reqId, string groups) { }
        public void displayGroupUpdated(int reqId, string contractInfo) { }
        public void positionMulti(int reqId, string account, string modelCode, IbContract contract, decimal pos, double avgCost) { }
        public void positionMultiEnd(int reqId) { }
        public void accountUpdateMulti(int reqId, string account, string modelCode, string key, string value, string currency) { }
        public void accountUpdateMultiEnd(int reqId) { }
        public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes) { }
        public void securityDefinitionOptionParameterEnd(int reqId) { }
        public void softDollarTiers(int reqId, IBApi.SoftDollarTier[] tiers) { }
        public void familyCodes(IBApi.FamilyCode[] familyCodes) { }
        public void symbolSamples(int reqId, IBApi.ContractDescription[] contractDescriptions) { }
        public void mktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions) { }
        public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData) { }
        public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap) { }
        public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions) { }
        public void newsProviders(IBApi.NewsProvider[] newsProviders) { }
        public void newsArticle(int requestId, int articleType, string articleText) { }
        public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline) { }
        public void historicalNewsEnd(int requestId, bool hasMore) { }
        public void headTimestamp(int reqId, string headTimestamp) { }
        public void histogramData(int reqId, HistogramEntry[] data) { }
        public void historicalDataUpdate(int reqId, Bar bar) { }
        public void rerouteMktDataReq(int reqId, int conid, string exchange) { }
        public void rerouteMktDepthReq(int reqId, int conid, string exchange) { }
        public void marketRule(int marketRuleId, IBApi.PriceIncrement[] priceIncrements) { }
        public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL) { }
        public void pnlSingle(int reqId, decimal pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value) { }
        public void historicalTicks(int reqId, IBApi.HistoricalTick[] ticks, bool done) { }
        public void historicalTicksBidAsk(int reqId, IBApi.HistoricalTickBidAsk[] ticks, bool done) { }
        public void historicalTicksLast(int reqId, IBApi.HistoricalTickLast[] ticks, bool done) { }
        public void tickByTickAllLast(int reqId, int tickType, long time, double price, decimal size, IBApi.TickAttribLast tickAttribLast, string exchange, string specialConditions) { }
        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, decimal bidSize, decimal askSize, IBApi.TickAttribBidAsk tickAttribBidAsk) { }
        public void tickByTickMidPoint(int reqId, long time, double midPoint) { }
        public void orderBound(long orderId, int apiClientId, int apiOrderId) { }
        public void completedOrder(IbContract contract, IBApi.Order order, IBApi.OrderState orderState) { }
        public void completedOrdersEnd() { }
        public void replaceFAEnd(int reqId, string text) { }
        public void wshMetaData(int reqId, string dataJson) { }
        public void wshEventData(int reqId, string dataJson) { }
        public void historicalSchedule(int reqId, string startDateTime, string endDateTime, string timeZone, IBApi.HistoricalSession[] sessions) { }
        public void userInfo(int reqId, string whiteBrandingId) { }
        public void currentTimeInMillis(long time) { }
        public void updateMktDepth(int tickerId, int position, int operation, int side, double price, decimal size) { }
        public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, decimal size, bool isSmartDepth) { }
        public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange) { }
        public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVol, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice) { }

        // Protobuf callbacks (required by EWrapper interface in API 10.37)
        public void orderStatusProtoBuf(IBApi.protobuf.OrderStatus orderStatus) { }
        public void openOrderProtoBuf(IBApi.protobuf.OpenOrder openOrder) { }
        public void openOrdersEndProtoBuf(IBApi.protobuf.OpenOrdersEnd openOrdersEnd) { }
        public void errorProtoBuf(IBApi.protobuf.ErrorMessage errorMessage) { }
        public void execDetailsProtoBuf(IBApi.protobuf.ExecutionDetails executionDetails) { }
        public void execDetailsEndProtoBuf(IBApi.protobuf.ExecutionDetailsEnd executionDetailsEnd) { }
    }
}

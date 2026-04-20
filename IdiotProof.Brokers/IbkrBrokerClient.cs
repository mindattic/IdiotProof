using System.Collections.Concurrent;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Interactive Brokers broker client. Wraps the IBApi EClientSocket/EWrapper.
/// </summary>
public sealed class IbkrBrokerClient : IBrokerClient, IDisposable
{
    private readonly string host;
    private readonly int port;
    private readonly int clientId;
    private IBApi.EClientSocket? socket;
    private IBApi.EReaderMonitorSignal? signal;
    private IbkrWrapper? wrapper;
    private bool connected;

    public BrokerType BrokerType => BrokerType.Ibkr;
    public bool IsConnected => connected && (socket?.IsConnected() ?? false);

    public IbkrBrokerClient(string host = "127.0.0.1", int port = 4002, int clientId = 99)
    {
        this.host = host;
        this.port = port;
        this.clientId = clientId;
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            signal = new IBApi.EReaderMonitorSignal();
            wrapper = new IbkrWrapper();
            socket = new IBApi.EClientSocket(wrapper, signal);
            socket.eConnect(host, port, clientId);

            var reader = new IBApi.EReader(socket, signal);
            reader.Start();
            Task.Factory.StartNew(() =>
            {
                while (socket.IsConnected())
                {
                    signal.waitForSignal();
                    reader.processMsgs();
                }
            }, TaskCreationOptions.LongRunning);

            connected = socket.IsConnected();
            return Task.FromResult(connected);
        }
        catch (Exception ex)
        {
            connected = false;
            System.Diagnostics.Debug.WriteLine($"IBKR connect failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public Task DisconnectAsync()
    {
        socket?.eDisconnect();
        connected = false;
        return Task.CompletedTask;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (!IsConnected || socket == null || wrapper == null)
            return new OrderResult { IsSuccess = false, Message = "IBKR not connected." };

        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0)
            return new OrderResult { IsSuccess = false, Message = "Invalid symbol or quantity." };

        if (!wrapper.WaitForNextValidId(TimeSpan.FromSeconds(5)))
            return new OrderResult { IsSuccess = false, Message = "IBKR order IDs not initialized — TWS may not be ready." };

        var orderId = wrapper.ConsumeNextOrderId();

        var contract = new IBApi.Contract
        {
            Symbol = request.Symbol.ToUpperInvariant(),
            SecType = "STK",
            Exchange = "SMART",
            Currency = "USD"
        };

        var order = new IBApi.Order
        {
            Action = request.Side == OrderSide.Buy ? "BUY" : "SELL",
            OrderType = request.Type == OrderType.Market ? "MKT" : "LMT",
            TotalQuantity = request.Quantity,
            Tif = string.IsNullOrWhiteSpace(request.TimeInForce) ? "DAY" : request.TimeInForce.ToUpperInvariant(),
            LmtPrice = request.Type == OrderType.Limit ? (double)(request.LimitPrice ?? 0m) : 0.0
        };

        var tcs = new TaskCompletionSource<OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        wrapper.RegisterPendingOrder(orderId, tcs);

        socket.placeOrder(orderId, contract, order);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            return await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            wrapper.UnregisterPendingOrder(orderId);
            return new OrderResult { IsSuccess = false, Message = ct.IsCancellationRequested ? "Cancelled." : "Order timed out waiting for acknowledgement." };
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!IsConnected || socket == null)
            return new OrderResult { BrokerOrderId = orderId, IsSuccess = false, Message = "IBKR not connected." };

        if (!int.TryParse(orderId, out var id))
            return new OrderResult { BrokerOrderId = orderId, IsSuccess = false, Message = "Invalid IBKR order ID format." };

        socket.cancelOrder(id, new IBApi.OrderCancel());
        await Task.Delay(500, ct).ConfigureAwait(false);

        return new OrderResult { BrokerOrderId = orderId, IsSuccess = true, Message = "Cancel request sent to IBKR." };
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        if (!IsConnected || socket == null || wrapper == null)
            return [];

        var tcs = new TaskCompletionSource<IReadOnlyList<Position>>(TaskCreationOptions.RunContinuationsAsynchronously);
        wrapper.RegisterPositionCallback(positions => tcs.TrySetResult(positions));
        socket.reqPositions();

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public void Dispose()
    {
        socket?.eDisconnect();
    }
}

/// <summary>
/// EWrapper implementation with order tracking and position retrieval.
/// </summary>
internal sealed class IbkrWrapper : IBApi.EWrapper
{
    private int nextOrderId = -1;
    private readonly object orderIdLock = new();
    private readonly ManualResetEventSlim nextValidIdReady = new(false);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<OrderResult>> pendingOrders = new();

    private readonly List<Position> positionBuffer = [];
    private Action<IReadOnlyList<Position>>? positionCallback;
    private readonly object positionLock = new();

    public bool WaitForNextValidId(TimeSpan timeout) => nextValidIdReady.Wait(timeout);

    public int ConsumeNextOrderId()
    {
        lock (orderIdLock)
        {
            if (nextOrderId < 0)
                throw new InvalidOperationException("nextOrderId not initialized.");
            return nextOrderId++;
        }
    }

    public void RegisterPendingOrder(int orderId, TaskCompletionSource<OrderResult> tcs)
        => pendingOrders[orderId] = tcs;

    public void UnregisterPendingOrder(int orderId)
        => pendingOrders.TryRemove(orderId, out _);

    public void RegisterPositionCallback(Action<IReadOnlyList<Position>> callback)
    {
        lock (positionLock)
        {
            positionBuffer.Clear();
            positionCallback = callback;
        }
    }

    // ── EWrapper callbacks ──────────────────────────────────────────────────────

    public void nextValidId(int orderId)
    {
        lock (orderIdLock) { nextOrderId = orderId; }
        nextValidIdReady.Set();
    }

    public void orderStatus(int orderId, string status, decimal filled, decimal remaining,
        double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId,
        string whyHeld, double mktCapPrice) =>
        HandleOrderStatus(orderId, status, (decimal)avgFillPrice);

    public void orderStatus(int orderId, string status, decimal filled, decimal remaining,
        double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId,
        string whyHeld, double mktCapPrice) =>
        HandleOrderStatus(orderId, status, (decimal)avgFillPrice);

    private void HandleOrderStatus(int orderId, string status, decimal fillPrice)
    {
        if (!pendingOrders.TryGetValue(orderId, out var tcs)) return;

        bool terminal = status is "Filled" or "Cancelled" or "ApiCancelled" or "Inactive";
        if (!terminal) return;

        if (pendingOrders.TryRemove(orderId, out _))
        {
            tcs.TrySetResult(new OrderResult
            {
                BrokerOrderId = orderId.ToString(),
                IsSuccess = status == "Filled",
                Message = status == "Filled" ? $"Filled @ {fillPrice:F2}" : $"Order {status}."
            });
        }
    }

    public void position(string account, IBApi.Contract contract, decimal pos, double avgCost)
    {
        lock (positionLock)
        {
            positionBuffer.Add(new Position
            {
                Symbol = contract.Symbol,
                Quantity = (int)pos,
                AveragePrice = (decimal)avgCost,
                MarketValue = 0m,
                UnrealizedPnl = 0m
            });
        }
    }

    public void positionEnd()
    {
        Action<IReadOnlyList<Position>>? cb;
        List<Position> snapshot;
        lock (positionLock)
        {
            cb = positionCallback;
            positionCallback = null;
            snapshot = [.. positionBuffer];
            positionBuffer.Clear();
        }
        cb?.Invoke(snapshot);
    }

    public void error(Exception e) => System.Diagnostics.Debug.WriteLine($"IBKR Error: {e.Message}");
    public void error(string str) => System.Diagnostics.Debug.WriteLine($"IBKR Error: {str}");
    public void error(int id, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        System.Diagnostics.Debug.WriteLine($"IBKR Error [{id}] {errorCode}: {errorMsg}");
        // Reject pending order on known error codes
        if (errorCode is 103 or 110 or 201 or 399 or 4108 or 4110)
        {
            if (pendingOrders.TryRemove(id, out var tcs))
                tcs.TrySetResult(new OrderResult { BrokerOrderId = id.ToString(), IsSuccess = false, Message = $"IBKR rejected: {errorMsg}" });
        }
    }
    public void error(int id, long time, int errorCode, string errorMsg, string advancedOrderRejectJson) =>
        error(id, errorCode, errorMsg, advancedOrderRejectJson);

    public void connectionClosed() => System.Diagnostics.Debug.WriteLine("IBKR connection closed.");
    public void connectAck() { }
    public void currentTime(long time) { }
    public void currentTimeInMillis(long time) { }

    // ── No-op stubs for remaining EWrapper members ──────────────────────────────

    public void tickPrice(int tickerId, int field, double price, IBApi.TickAttrib attribs) { }
    public void tickSize(int tickerId, int field, decimal size) { }
    public void tickString(int tickerId, int field, string value) { }
    public void tickGeneric(int tickerId, int field, double value) { }
    public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double impliedFuture, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate) { }
    public void tickSnapshotEnd(int tickerId) { }
    public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions) { }
    public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice) { }
    public void marketDataType(int reqId, int marketDataType) { }
    public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState) { }
    public void openOrderEnd() { }
    public void contractDetails(int reqId, IBApi.ContractDetails contractDetails) { }
    public void contractDetailsEnd(int reqId) { }
    public void execDetails(int reqId, IBApi.Contract contract, IBApi.Execution execution) { }
    public void execDetailsEnd(int reqId) { }
    public void updateAccountValue(string key, string value, string currency, string accountName) { }
    public void updatePortfolio(IBApi.Contract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName) { }
    public void updatePortfolioValue(IBApi.Contract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName) { }
    public void updateAccountTime(string timestamp) { }
    public void accountDownloadEnd(string account) { }
    public void managedAccounts(string accountsList) { }
    public void historicalData(int reqId, IBApi.Bar bar) { }
    public void historicalDataEnd(int reqId, string start, string end) { }
    public void historicalDataUpdate(int reqId, IBApi.Bar bar) { }
    public void bondContractDetails(int reqId, IBApi.ContractDetails contract) { }
    public void updateMktDepth(int tickerId, int position, int operation, int side, double price, decimal size) { }
    public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, decimal size, bool isSmartDepth) { }
    public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange) { }
    public void receiveFA(int faDataType, string faXmlData) { }
    public void scannerParameters(string xml) { }
    public void scannerData(int reqId, int rank, IBApi.ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr) { }
    public void scannerDataEnd(int reqId) { }
    public void realtimeBar(int reqId, long date, double open, double high, double low, double close, decimal volume, decimal WAP, int count) { }
    public void fundamentalData(int reqId, string data) { }
    public void deltaNeutralValidation(int reqId, IBApi.DeltaNeutralContract deltaNeutralContract) { }
    public void commissionReport(IBApi.CommissionAndFeesReport commissionReport) { }
    public void commissionAndFeesReport(IBApi.CommissionAndFeesReport report) { }
    public void accountSummary(int reqId, string account, string tag, string value, string currency) { }
    public void accountSummaryEnd(int reqId) { }
    public void verifyMessageAPI(string apiData) { }
    public void verifyCompleted(bool isSuccessful, string errorText) { }
    public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge) { }
    public void verifyAndAuthCompleted(bool isSuccessful, string errorText) { }
    public void displayGroupList(int reqId, string groups) { }
    public void displayGroupUpdated(int reqId, string contractInfo) { }
    public void positionMulti(int requestId, string account, string modelCode, IBApi.Contract contract, decimal pos, double avgCost) { }
    public void positionMultiEnd(int requestId) { }
    public void accountUpdateMulti(int requestId, string account, string modelCode, string key, string value, string currency) { }
    public void accountUpdateMultiEnd(int requestId) { }
    public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes) { }
    public void securityDefinitionOptionParameterEnd(int reqId) { }
    public void softDollarTiers(int reqId, IBApi.SoftDollarTier[] tiers) { }
    public void familyCodes(IBApi.FamilyCode[] familyCodes) { }
    public void symbolSamples(int reqId, IBApi.ContractDescription[] contractDescriptions) { }
    public void mktDepthExchanges(IBApi.DepthMktDataDescription[] depthMktDataDescriptions) { }
    public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData) { }
    public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap) { }
    public void newsProviders(IBApi.NewsProvider[] newsProviders) { }
    public void newsArticle(int requestId, int articleType, string articleText) { }
    public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline) { }
    public void historicalNewsEnd(int requestId, bool hasMore) { }
    public void headTimestamp(int reqId, string headTimestamp) { }
    public void histogramData(int reqId, IBApi.HistogramEntry[] data) { }
    public void historicalTicks(int reqId, IBApi.HistoricalTick[] ticks, bool done) { }
    public void historicalTicksBidAsk(int reqId, IBApi.HistoricalTickBidAsk[] ticks, bool done) { }
    public void historicalTicksLast(int reqId, IBApi.HistoricalTickLast[] ticks, bool done) { }
    public void tickByTickAllLast(int reqId, int tickType, long time, double price, decimal size, IBApi.TickAttribLast tickAttribLast, string exchange, string specialConditions) { }
    public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, decimal bidSize, decimal askSize, IBApi.TickAttribBidAsk tickAttribBidAsk) { }
    public void tickByTickMidPoint(int reqId, long time, double midPoint) { }
    public void orderBound(long orderId, int apiClientId, int apiOrderId) { }
    public void completedOrder(IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState) { }
    public void completedOrdersEnd() { }
    public void replaceFAEnd(int reqId, string text) { }
    public void wshMetaData(int reqId, string dataJson) { }
    public void wshEventData(int reqId, string dataJson) { }
    public void historicalSchedule(int reqId, string startDateTime, string endDateTime, string timeZone, IBApi.HistoricalSession[] sessions) { }
    public void userInfo(int reqId, string whiteBrandingId) { }
    public void rerouteMktDataReq(int reqId, int conId, string exchange) { }
    public void rerouteMktDepthReq(int reqId, int conId, string exchange) { }
    public void marketRule(int marketRuleId, IBApi.PriceIncrement[] priceIncrements) { }
    public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL) { }
    public void pnlSingle(int reqId, decimal pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value) { }
    public void orderStatusProtoBuf(IBApi.protobuf.OrderStatus orderStatus) { }
    public void openOrderProtoBuf(IBApi.protobuf.OpenOrder openOrder) { }
    public void openOrdersEndProtoBuf(IBApi.protobuf.OpenOrdersEnd openOrdersEnd) { }
    public void errorProtoBuf(IBApi.protobuf.ErrorMessage errorMessage) { }
    public void execDetailsProtoBuf(IBApi.protobuf.ExecutionDetails executionDetails) { }
    public void execDetailsEndProtoBuf(IBApi.protobuf.ExecutionDetailsEnd executionDetailsEnd) { }
}

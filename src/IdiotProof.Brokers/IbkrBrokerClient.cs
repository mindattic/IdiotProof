using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Interactive Brokers broker client. Wraps the IBApi EClientSocket/EWrapper.
/// This is a stub that establishes the connection pattern — full EWrapper callbacks
/// will be ported incrementally from IdiotProof.Core's IbWrapper as needed.
/// </summary>
public sealed class IbkrBrokerClient : IBrokerClient, IDisposable
{
    private readonly string host;
    private readonly int port;
    private readonly int clientId;
    private IBApi.EClientSocket? socket;
    private IBApi.EReaderMonitorSignal? signal;
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
            var wrapper = new IbkrWrapper();
            socket = new IBApi.EClientSocket(wrapper, signal);
            socket.eConnect(host, port, clientId);

            // Start EReader pump
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

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        // TODO: Full order placement via socket.placeOrder()
        return Task.FromResult(new OrderResult
        {
            IsSuccess = false,
            Message = "IBKR order placement not yet fully implemented. Port from IbWrapper as needed."
        });
    }

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        return Task.FromResult(new OrderResult
        {
            BrokerOrderId = orderId,
            IsSuccess = false,
            Message = "IBKR cancel not yet implemented."
        });
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Position> empty = [];
        return Task.FromResult(empty);
    }

    public void Dispose()
    {
        socket?.eDisconnect();
    }
}

/// <summary>
/// Minimal EWrapper implementation. Extend with callbacks from IbWrapper.cs as needed.
/// </summary>
internal sealed class IbkrWrapper : IBApi.EWrapper
{
    public void error(Exception e) => System.Diagnostics.Debug.WriteLine($"IBKR Error: {e.Message}");
    public void error(string str) => System.Diagnostics.Debug.WriteLine($"IBKR Error: {str}");
    public void error(int id, int errorCode, string errorMsg, string advancedOrderRejectJson) =>
        System.Diagnostics.Debug.WriteLine($"IBKR Error [{id}] {errorCode}: {errorMsg}");
    public void connectionClosed() => System.Diagnostics.Debug.WriteLine("IBKR connection closed.");
    public void connectAck() { }
    public void currentTime(long time) { }

    // All other EWrapper methods are no-ops for now. They will be implemented as features are ported.
    public void tickPrice(int tickerId, int field, double price, IBApi.TickAttrib attribs) { }
    public void tickSize(int tickerId, int field, decimal size) { }
    public void tickString(int tickerId, int field, string value) { }
    public void tickGeneric(int tickerId, int field, double value) { }
    public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double impliedFuture, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate) { }
    public void tickSnapshotEnd(int tickerId) { }
    public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions) { }
    public void marketDataType(int reqId, int marketDataType) { }
    public void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice) { }
    public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState) { }
    public void openOrderEnd() { }
    public void contractDetails(int reqId, IBApi.ContractDetails contractDetails) { }
    public void contractDetailsEnd(int reqId) { }
    public void execDetails(int reqId, IBApi.Contract contract, IBApi.Execution execution) { }
    public void execDetailsEnd(int reqId) { }
    public void updateAccountValue(string key, string value, string currency, string accountName) { }
    public void updatePortfolioValue(IBApi.Contract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName) { }
    public void updateAccountTime(string timestamp) { }
    public void accountDownloadEnd(string account) { }
    public void nextValidId(int orderId) { }
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
    public void position(string account, IBApi.Contract contract, decimal pos, double avgCost) { }
    public void positionEnd() { }
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

    // Newer EWrapper methods required by current CSharpAPI.dll
    public void error(int id, long time, int errorCode, string errorMsg, string advancedOrderRejectJson) { }
    public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice) { }
    public void updatePortfolio(IBApi.Contract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName) { }
    public void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice) { }
    public void commissionAndFeesReport(IBApi.CommissionAndFeesReport report) { }
    public void rerouteMktDataReq(int reqId, int conId, string exchange) { }
    public void rerouteMktDepthReq(int reqId, int conId, string exchange) { }
    public void marketRule(int marketRuleId, IBApi.PriceIncrement[] priceIncrements) { }
    public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL) { }
    public void pnlSingle(int reqId, decimal pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value) { }
    public void currentTimeInMillis(long time) { }
    public void orderStatusProtoBuf(IBApi.protobuf.OrderStatus orderStatus) { }
    public void openOrderProtoBuf(IBApi.protobuf.OpenOrder openOrder) { }
    public void openOrdersEndProtoBuf(IBApi.protobuf.OpenOrdersEnd openOrdersEnd) { }
    public void errorProtoBuf(IBApi.protobuf.ErrorMessage errorMessage) { }
    public void execDetailsProtoBuf(IBApi.protobuf.ExecutionDetails executionDetails) { }
    public void execDetailsEndProtoBuf(IBApi.protobuf.ExecutionDetailsEnd executionDetailsEnd) { }
}

namespace IdiotProof.Models;

public enum TradeDirection { Long, Short }

public enum TradingSession
{
    Premarket,    // 4:00 AM - 9:30 AM ET
    RTH,          // 9:30 AM - 4:00 PM ET
    AfterHours,   // 4:00 PM - 8:00 PM ET
    Extended      // All sessions
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit,
    TrailingStop
}

public enum OrderSide { Buy, Sell }

public enum PriceType { Current, Bid, Ask, VWAP, Open, High, Low, Close }

public enum ConfidenceGrade { APlus, A, B, C, D, F }

public enum BrokerType { Ibkr, Alpaca, Sandbox }

public enum FeedType { Ibkr, Polygon }

public enum StrategyType { Iti, BreakoutPullback, LowHigh, FluentDsl, Custom }

public enum WorkspaceState { Stopped, Running, Paused }

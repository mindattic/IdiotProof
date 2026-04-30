// Project-wide aliases. The IdiotProof.Models project also defines OrderSide,
// OrderType, and TradingSession (with a smaller set of values, used by the
// canonical TradeSetup / OrderRequest models). IdiotProof.Core almost always
// wants the local IdiotProof.Enums versions which have richer member sets
// (e.g. TradingSession.PreMarketEndEarly, TradingSession.Active, etc.). These
// global usings disambiguate the bare names so existing Core code keeps
// resolving to its expected enum without per-file aliases everywhere.
global using OrderSide = IdiotProof.Enums.OrderSide;
global using OrderType = IdiotProof.Enums.OrderType;
global using TradingSession = IdiotProof.Enums.TradingSession;

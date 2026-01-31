// ============================================================================
// ValidationCodes - Standardized validation error codes
// ============================================================================

namespace IdiotProof.Shared.Validation
{
    /// <summary>
    /// Standardized validation error codes used across frontend and backend.
    /// </summary>
    public static class ValidationCodes
    {
        // ====================================================================
        // General Validation
        // ====================================================================
        public const string Required = "REQUIRED";
        public const string InvalidFormat = "INVALID_FORMAT";
        public const string InvalidLength = "INVALID_LENGTH";
        public const string InvalidRange = "INVALID_RANGE";
        public const string InvalidValue = "INVALID_VALUE";
        public const string Duplicate = "DUPLICATE";
        public const string NotFound = "NOT_FOUND";

        // ====================================================================
        // Security Validation
        // ====================================================================
        public const string InjectionDetected = "INJECTION_DETECTED";
        public const string InvalidCharacters = "INVALID_CHARACTERS";
        public const string PathTraversal = "PATH_TRAVERSAL";
        public const string MaliciousContent = "MALICIOUS_CONTENT";

        // ====================================================================
        // Strategy Validation
        // ====================================================================
        public const string InvalidSymbol = "INVALID_SYMBOL";
        public const string MissingTicker = "MISSING_TICKER";
        public const string MissingCondition = "MISSING_CONDITION";
        public const string MissingOrder = "MISSING_ORDER";
        public const string InvalidOrderSequence = "INVALID_ORDER_SEQUENCE";
        public const string InvalidTimeRange = "INVALID_TIME_RANGE";
        public const string InvalidSession = "INVALID_SESSION";
        public const string DuplicateCondition = "DUPLICATE_CONDITION";

        // ====================================================================
        // Order Validation
        // ====================================================================
        public const string InvalidQuantity = "INVALID_QUANTITY";
        public const string InvalidPrice = "INVALID_PRICE";
        public const string InvalidStopLoss = "INVALID_STOP_LOSS";
        public const string InvalidTakeProfit = "INVALID_TAKE_PROFIT";
        public const string StopLossAboveEntry = "STOP_LOSS_ABOVE_ENTRY";
        public const string TakeProfitBelowEntry = "TAKE_PROFIT_BELOW_ENTRY";
        public const string InvalidRiskReward = "INVALID_RISK_REWARD";
        public const string ExceedsMaxPosition = "EXCEEDS_MAX_POSITION";
        public const string ExceedsMaxRisk = "EXCEEDS_MAX_RISK";

        // ====================================================================
        // JSON Validation
        // ====================================================================
        public const string InvalidJson = "INVALID_JSON";
        public const string SchemaMismatch = "SCHEMA_MISMATCH";
        public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";
        public const string InvalidFieldType = "INVALID_FIELD_TYPE";

        // ====================================================================
        // Connection/System
        // ====================================================================
        public const string ConnectionFailed = "CONNECTION_FAILED";
        public const string Timeout = "TIMEOUT";
        public const string NotConnected = "NOT_CONNECTED";
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    }
}

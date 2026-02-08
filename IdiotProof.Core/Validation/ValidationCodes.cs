// ============================================================================
// ValidationCodes - Standardized validation error codes
// ============================================================================
//
// NOMENCLATURE:
// - Validation Code: A unique identifier for a validation error type
// - Error: A condition that prevents operation (must be fixed)
// - Warning: A condition that may cause issues (informational)
//
// CODE CATEGORIES:
// 1. General Validation - Required fields, format, length, range
// 2. Security Validation - Injection detection, path traversal
// 3. Strategy Validation - Symbol, conditions, orders
// 4. Order Validation - Quantity, price, risk management
// 5. JSON Validation - Schema, field types
// 6. IdiotScript Validation - Syntax, commands, round-trip
// 7. Connection/System - Network and service errors
//
// USAGE:
//   if (error.Code == ValidationCodes.Required) { /* handle */ }
//   if (error.Code == ValidationCodes.InjectionDetected) { /* security alert */ }
//
// ============================================================================

namespace IdiotProof.Validation {
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

        // Short Position Validation
        /// <summary>Short position entry must be higher than take profit.</summary>
        public const string ShortEntryBelowTakeProfit = "SHORT_ENTRY_BELOW_TAKE_PROFIT";

        /// <summary>Short position stop loss must be above entry.</summary>
        public const string ShortStopLossBelowEntry = "SHORT_STOP_LOSS_BELOW_ENTRY";

        /// <summary>Short position take profit must be below stop loss.</summary>
        public const string ShortTakeProfitAboveStopLoss = "SHORT_TAKE_PROFIT_ABOVE_STOP_LOSS";

        // ====================================================================
        // JSON Validation
        // ====================================================================
        public const string InvalidJson = "INVALID_JSON";
        public const string SchemaMismatch = "SCHEMA_MISMATCH";
        public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";
        public const string InvalidFieldType = "INVALID_FIELD_TYPE";

        // ====================================================================
        // IdiotScript Validation
        // ====================================================================
        /// <summary>Invalid script syntax (unbalanced parens, etc.).</summary>
        public const string InvalidSyntax = "INVALID_SYNTAX";

        /// <summary>Unknown or invalid command in script.</summary>
        public const string InvalidCommand = "INVALID_COMMAND";

        /// <summary>Round-trip conversion produced different results.</summary>
        public const string RoundTripMismatch = "ROUNDTRIP_MISMATCH";

        /// <summary>Script command missing required parameter.</summary>
        public const string MissingParameter = "MISSING_PARAMETER";

        /// <summary>Script parameter value is out of valid range.</summary>
        public const string ParameterOutOfRange = "PARAMETER_OUT_OF_RANGE";

        /// <summary>Fluent API method has no IdiotScript equivalent.</summary>
        public const string NoScriptEquivalent = "NO_SCRIPT_EQUIVALENT";

        /// <summary>IdiotScript command has no fluent API equivalent.</summary>
        public const string NoFluentEquivalent = "NO_FLUENT_EQUIVALENT";

        /// <summary>Parameter mismatch between fluent API and IdiotScript.</summary>
        public const string ParameterMismatch = "PARAMETER_MISMATCH";

        /// <summary>Invalid boolean value provided.</summary>
        public const string InvalidBoolean = "INVALID_BOOLEAN";

        /// <summary>Command execution order violation.</summary>
        public const string OrderOfOperationsViolation = "ORDER_OF_OPERATIONS_VIOLATION";

        /// <summary>Separation of responsibility violation (e.g., mixing order and condition).</summary>
        public const string ResponsibilityViolation = "RESPONSIBILITY_VIOLATION";

        // ====================================================================
        // Connection/System
        // ====================================================================
        public const string ConnectionFailed = "CONNECTION_FAILED";
        public const string Timeout = "TIMEOUT";
        public const string NotConnected = "NOT_CONNECTED";
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    }
}



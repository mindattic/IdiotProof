// ============================================================================
// OrderValidator - Validates order parameters for safe execution
// ============================================================================

namespace IdiotProof.Validation {
    /// <summary>
    /// Validates order parameters before submission to ensure safety.
    /// </summary>
    public static class OrderValidator
    {
        /// <summary>
        /// Default maximum position value in dollars.
        /// </summary>
        public const double DefaultMaxPositionValue = 1_000_000;

        /// <summary>
        /// Default maximum risk per trade percentage.
        /// </summary>
        public const double DefaultMaxRiskPercent = 0.05; // 5%

        /// <summary>
        /// Default minimum risk/reward ratio.
        /// </summary>
        public const double DefaultMinRiskReward = 1.0; // 1:1

        /// <summary>
        /// Validates a buy order.
        /// </summary>
        public static ValidationResult ValidateBuyOrder(
            int quantity,
            double entryPrice,
            double? stopLoss = null,
            double? takeProfit = null,
            OrderValidationOptions? options = null)
        {
            options ??= new OrderValidationOptions();
            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            // Basic validations
            errors.AddRange(InputValidator.ValidateQuantity(quantity, "Quantity", options.MaxQuantity).Errors);
            errors.AddRange(InputValidator.ValidatePrice(entryPrice, "Entry Price").Errors);

            // Position value check
            var positionValue = quantity * entryPrice;
            if (positionValue > options.MaxPositionValue)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.ExceedsMaxPosition,
                    $"Position value ${positionValue:N2} exceeds maximum allowed ${options.MaxPositionValue:N2}",
                    "PositionValue",
                    positionValue));
            }

            // Stop loss validations for BUY orders
            if (stopLoss.HasValue)
            {
                errors.AddRange(InputValidator.ValidatePrice(stopLoss.Value, "Stop Loss").Errors);

                if (stopLoss.Value >= entryPrice)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.StopLossAboveEntry,
                        "Stop loss must be below entry price for buy orders",
                        "StopLoss",
                        stopLoss.Value));
                }

                // Risk calculation
                var riskPerShare = entryPrice - stopLoss.Value;
                var totalRisk = riskPerShare * quantity;
                var riskPercent = totalRisk / positionValue;

                if (riskPercent > options.MaxRiskPercent)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.ExceedsMaxRisk,
                        $"Risk per trade ({riskPercent:P1}) exceeds maximum allowed ({options.MaxRiskPercent:P1})",
                        "Risk",
                        riskPercent));
                }
            }
            else if (options.RequireStopLoss)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidStopLoss,
                    "Stop loss is required",
                    "StopLoss"));
            }

            // Take profit validations for BUY orders
            if (takeProfit.HasValue)
            {
                errors.AddRange(InputValidator.ValidatePrice(takeProfit.Value, "Take Profit").Errors);

                if (takeProfit.Value <= entryPrice)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.TakeProfitBelowEntry,
                        "Take profit must be above entry price for buy orders",
                        "TakeProfit",
                        takeProfit.Value));
                }

                // Risk/Reward validation
                if (stopLoss.HasValue)
                {
                    var risk = entryPrice - stopLoss.Value;
                    var reward = takeProfit.Value - entryPrice;
                    var riskReward = reward / risk;

                    if (riskReward < options.MinRiskReward)
                    {
                        warnings.Add(new ValidationWarning(
                            ValidationCodes.InvalidRiskReward,
                            $"Risk/Reward ratio ({riskReward:F2}) is below recommended minimum ({options.MinRiskReward:F2})",
                            "RiskReward"));
                    }
                }
            }

            return new ValidationResult { Errors = errors, Warnings = warnings };
        }

        /// <summary>
        /// Validates a sell order.
        /// </summary>
        public static ValidationResult ValidateSellOrder(
            int quantity,
            double entryPrice,
            double? stopLoss = null,
            double? takeProfit = null,
            OrderValidationOptions? options = null)
        {
            options ??= new OrderValidationOptions();
            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            // Basic validations
            errors.AddRange(InputValidator.ValidateQuantity(quantity, "Quantity", options.MaxQuantity).Errors);
            errors.AddRange(InputValidator.ValidatePrice(entryPrice, "Entry Price").Errors);

            // Position value check
            var positionValue = quantity * entryPrice;
            if (positionValue > options.MaxPositionValue)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.ExceedsMaxPosition,
                    $"Position value ${positionValue:N2} exceeds maximum allowed ${options.MaxPositionValue:N2}",
                    "PositionValue",
                    positionValue));
            }

            // Stop loss validations for SELL orders (shorts)
            if (stopLoss.HasValue)
            {
                errors.AddRange(InputValidator.ValidatePrice(stopLoss.Value, "Stop Loss").Errors);

                if (stopLoss.Value <= entryPrice)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.InvalidStopLoss,
                        "Stop loss must be above entry price for sell (short) orders",
                        "StopLoss",
                        stopLoss.Value));
                }
            }

            // Take profit validations for SELL orders (shorts)
            if (takeProfit.HasValue)
            {
                errors.AddRange(InputValidator.ValidatePrice(takeProfit.Value, "Take Profit").Errors);

                if (takeProfit.Value >= entryPrice)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.InvalidTakeProfit,
                        "Take profit must be below entry price for sell (short) orders",
                        "TakeProfit",
                        takeProfit.Value));
                }
            }

            return new ValidationResult { Errors = errors, Warnings = warnings };
        }

        /// <summary>
        /// Validates a trailing stop loss configuration.
        /// </summary>
        public static ValidationResult ValidateTrailingStopLoss(double percent, string fieldName = "Trailing Stop Loss")
        {
            var errors = new List<ValidationError>();

            if (percent <= 0)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} must be greater than 0%",
                    fieldName,
                    percent));
            }

            if (percent > 0.50) // More than 50% is usually a mistake
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} of {percent:P0} seems too high. Maximum recommended is 50%",
                    fieldName,
                    percent));
            }

            return new ValidationResult { Errors = errors };
        }

        /// <summary>
        /// Validates ATR-based stop loss configuration.
        /// </summary>
        public static ValidationResult ValidateAtrStopLoss(
            double multiplier,
            double? maxStopPercent = null,
            string fieldName = "ATR Stop Loss")
        {
            var errors = new List<ValidationError>();

            if (multiplier <= 0 || multiplier > 10)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} multiplier must be between 0.1 and 10",
                    $"{fieldName}.Multiplier",
                    multiplier));
            }

            if (maxStopPercent.HasValue && (maxStopPercent <= 0 || maxStopPercent > 1))
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} max stop percent must be between 0% and 100%",
                    $"{fieldName}.MaxStopPercent",
                    maxStopPercent));
            }

            return new ValidationResult { Errors = errors };
        }
    }

    /// <summary>
    /// Options for order validation.
    /// </summary>
    public class OrderValidationOptions
    {
        /// <summary>
        /// Maximum position value in dollars.
        /// </summary>
        public double MaxPositionValue { get; set; } = OrderValidator.DefaultMaxPositionValue;

        /// <summary>
        /// Maximum quantity per order.
        /// </summary>
        public int MaxQuantity { get; set; } = 100_000;

        /// <summary>
        /// Maximum risk per trade as percentage of position.
        /// </summary>
        public double MaxRiskPercent { get; set; } = OrderValidator.DefaultMaxRiskPercent;

        /// <summary>
        /// Minimum acceptable risk/reward ratio.
        /// </summary>
        public double MinRiskReward { get; set; } = OrderValidator.DefaultMinRiskReward;

        /// <summary>
        /// Whether stop loss is required.
        /// </summary>
        public bool RequireStopLoss { get; set; } = false;

        /// <summary>
        /// Whether take profit is required.
        /// </summary>
        public bool RequireTakeProfit { get; set; } = false;
    }
}



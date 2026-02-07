// ============================================================================
// StrategyValidator - Validates strategy definitions comprehensively
// ============================================================================

using IdiotProof.Core.Models;

namespace IdiotProof.Core.Validation
{
    /// <summary>
    /// Validates trading strategy definitions for both frontend and backend.
    /// Ensures strategies are complete, well-formed, and safe to execute.
    /// </summary>
    public static class StrategyValidator
    {
        /// <summary>
        /// Maximum number of segments allowed in a strategy.
        /// </summary>
        public const int MaxSegments = 50;

        /// <summary>
        /// Maximum strategy name length.
        /// </summary>
        public const int MaxNameLength = 100;

        /// <summary>
        /// Maximum notes length.
        /// </summary>
        public const int MaxNotesLength = 2000;

        /// <summary>
        /// Validates a complete strategy definition.
        /// </summary>
        public static ValidationResult ValidateStrategy(StrategyDefinition strategy)
        {
            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            // Basic required fields
            errors.AddRange(ValidateName(strategy.Name).Errors);
            errors.AddRange(InputValidator.ValidateTickerSymbol(strategy.Symbol).Errors);

            // Segment validations
            if (strategy.Segments == null || strategy.Segments.Count == 0)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.Required,
                    "Strategy must have at least one segment",
                    "Segments"));
            }
            else
            {
                // Validate segment count
                if (strategy.Segments.Count > MaxSegments)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.InvalidRange,
                        $"Strategy cannot have more than {MaxSegments} segments",
                        "Segments"));
                }

                // Validate segment order and completeness
                var sequenceResult = ValidateSegmentSequence(strategy.Segments);
                errors.AddRange(sequenceResult.Errors);
                warnings.AddRange(sequenceResult.Warnings);
            }

            // Validate notes if provided
            if (!string.IsNullOrEmpty(strategy.Notes))
            {
                errors.AddRange(InputValidator.ValidateSafeText(strategy.Notes, "Notes").Errors);
                errors.AddRange(InputValidator.ValidateLength(strategy.Notes, "Notes", 0, MaxNotesLength).Errors);
            }

            return new ValidationResult { Errors = errors, Warnings = warnings };
        }

        /// <summary>
        /// Validates a strategy name.
        /// </summary>
        public static ValidationResult ValidateName(string? name)
        {
            var result = InputValidator.ValidateRequired(name, "Name");
            if (!result.IsValid)
                return result;

            result = InputValidator.ValidateLength(name, "Name", 1, MaxNameLength);
            if (!result.IsValid)
                return result;

            return InputValidator.ValidateSafeText(name, "Name", allowNull: false);
        }

        /// <summary>
        /// Validates that segments are in correct order and complete.
        /// </summary>
        public static ValidationResult ValidateSegmentSequence(IReadOnlyList<StrategySegment> segments)
        {
            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            bool hasCondition = false;
            bool hasOrder = false;
            bool hasTicker = false;
            bool hasRepeat = false;
            bool hasTakeProfit = false;
            bool hasAutonomousTrading = false;
            int orderIndex = -1;

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var category = segment.Category.ToString().ToUpperInvariant();
                var segmentType = segment.Type.ToString().ToUpperInvariant();

                // Track segment types
                switch (segmentType)
                {
                    case "TICKER":
                        hasTicker = true;
                        if (i != 0)
                        {
                            warnings.Add(new ValidationWarning(
                                "TICKER_POSITION",
                                "Ticker should be the first segment",
                                $"Segments[{i}]"));
                        }
                        break;

                    case "ORDER":
                    case "LONG":
                    case "SHORT":
                    case "CLOSE":
                    case "CLOSELONG":
                    case "CLOSESHORT":
                        hasOrder = true;
                        orderIndex = i;
                        break;

                    case "REPEAT":
                        hasRepeat = true;
                        break;

                    case "TAKEPROFIT":
                    case "TAKEPROFITRANGE":
                        hasTakeProfit = true;
                        break;

                    case "AUTONOMOUSTRADING":
                    case "ISAUTONOMOUSTRADING":
                        hasAutonomousTrading = true;
                        break;
                }

                // Check for conditions
                if (category == "PRICECONDITION" || category == "VWAPCONDITION" || category == "INDICATORCONDITION")
                {
                    hasCondition = true;

                    // Conditions after order are unusual
                    if (hasOrder && orderIndex >= 0 && i > orderIndex)
                    {
                        warnings.Add(new ValidationWarning(
                            "CONDITION_AFTER_ORDER",
                            $"Condition '{segment.Type}' appears after order - this may be unintended",
                            $"Segments[{i}]"));
                    }
                }

                // Validate individual segment parameters
                errors.AddRange(ValidateSegmentParameters(segment, i).Errors);
            }

            // Check required elements
            if (!hasTicker)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.MissingTicker,
                    "Strategy must have a Ticker segment",
                    "Segments"));
            }

            // AutonomousTrading handles its own conditions internally - no manual conditions needed
            // AdaptiveOrder also handles exit conditions internally
            if (!hasCondition && !hasAutonomousTrading)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.MissingCondition,
                    "Strategy must have at least one condition before the order (or use AutonomousTrading)",
                    "Segments"));
            }

            // AutonomousTrading handles its own orders - no explicit order needed
            if (!hasOrder && !hasAutonomousTrading)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.MissingOrder,
                    "Strategy must have an order (Buy, Sell, or Close) or use AutonomousTrading",
                    "Segments"));
            }

            // Warn if Repeat is used without TakeProfit (strategy won't know when to reset)
            if (hasRepeat && !hasTakeProfit && !hasAutonomousTrading)
            {
                warnings.Add(new ValidationWarning(
                    "REPEAT_WITHOUT_TAKEPROFIT",
                    "Repeat is enabled but no TakeProfit is set - strategy may not know when to reset",
                    "Repeat"));
            }

            return new ValidationResult { Errors = errors, Warnings = warnings };
        }

        /// <summary>
        /// Validates parameters within a segment.
        /// </summary>
        public static ValidationResult ValidateSegmentParameters(StrategySegment segment, int index)
        {
            var errors = new List<ValidationError>();

            // Validate each parameter based on segment type
            foreach (var param in segment.Parameters ?? [])
            {
                if (param.IsRequired && IsMissingRequiredValue(param.Value))
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.MissingRequiredField,
                        $"Parameter '{param.Name}' is required for {segment.Type}",
                        $"Segments[{index}].Parameters[{param.Name}]"));
                    continue;
                }

                // Type-specific validation
                errors.AddRange(ValidateParameterValue(param, segment.Type.ToString(), index).Errors);
            }

            return new ValidationResult { Errors = errors };
        }

        private static bool IsMissingRequiredValue(object? value)
        {
            if (value == null)
                return true;

            if (value is string s)
                return string.IsNullOrWhiteSpace(s);

            return false;
        }

        /// <summary>
        /// Validates a parameter value based on its type.
        /// </summary>
        private static ValidationResult ValidateParameterValue(SegmentParameter param, string segmentType, int segmentIndex)
        {
            var fieldName = $"Segments[{segmentIndex}].Parameters[{param.Name}]";

            if (param.Value == null)
                return ValidationResult.Success();

            return param.Type.ToString().ToUpperInvariant() switch
            {
                "PRICE" or "DOUBLE" => ValidateNumericParameter(param, fieldName),
                "INTEGER" => ValidateIntegerParameter(param, fieldName),
                "PERCENTAGE" => ValidatePercentageParameter(param, fieldName),
                "STRING" => InputValidator.ValidateSafeText(param.Value?.ToString(), fieldName),
                _ => ValidationResult.Success()
            };
        }

        private static ValidationResult ValidateNumericParameter(SegmentParameter param, string fieldName)
        {
            if (param.Value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidValue,
                        $"{fieldName} has an invalid numeric value",
                        fieldName);
                }

                if (param.MinValue.HasValue && d < param.MinValue.Value)
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidRange,
                        $"{fieldName} must be at least {param.MinValue}",
                        fieldName);
                }

                if (param.MaxValue.HasValue && d > param.MaxValue.Value)
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidRange,
                        $"{fieldName} must be at most {param.MaxValue}",
                        fieldName);
                }
            }

            return ValidationResult.Success();
        }

        private static ValidationResult ValidateIntegerParameter(SegmentParameter param, string fieldName)
        {
            if (param.Value is int i)
            {
                if (param.MinValue.HasValue && i < (int)param.MinValue.Value)
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidRange,
                        $"{fieldName} must be at least {param.MinValue}",
                        fieldName);
                }

                if (param.MaxValue.HasValue && i > (int)param.MaxValue.Value)
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidRange,
                        $"{fieldName} must be at most {param.MaxValue}",
                        fieldName);
                }
            }

            return ValidationResult.Success();
        }

        private static ValidationResult ValidatePercentageParameter(SegmentParameter param, string fieldName)
        {
            if (param.Value is double d)
            {
                if (d < 0 || d > 1)
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidRange,
                        $"{fieldName} must be between 0% and 100%",
                        fieldName);
                }
            }

            return ValidationResult.Success();
        }

        // ====================================================================
        // ORDER PRICE VALIDATION
        // ====================================================================

        /// <summary>
        /// Validates that order prices are logically consistent for the position direction.
        /// For LONG positions: Entry &lt; TakeProfit, Entry &gt; StopLoss
        /// For SHORT positions: Entry &gt; TakeProfit, Entry &lt; StopLoss
        /// </summary>
        public static ValidationResult ValidateOrderPrices(
            bool isShortPosition,
            double? entryPrice,
            double? takeProfitPrice,
            double? stopLossPrice)
        {
            var errors = new List<ValidationError>();

            if (isShortPosition)
            {
                errors.AddRange(ValidateShortPositionPrices(entryPrice, takeProfitPrice, stopLossPrice).Errors);
            }
            else
            {
                errors.AddRange(ValidateLongPositionPrices(entryPrice, takeProfitPrice, stopLossPrice).Errors);
            }

            return new ValidationResult { Errors = errors };
        }

        /// <summary>
        /// Validates price relationships for a SHORT position.
        /// Short selling: You sell high (entry), buy back low (take profit).
        /// Entry must be HIGHER than take profit.
        /// Stop loss must be ABOVE entry (to limit losses when price rises).
        /// </summary>
        public static ValidationResult ValidateShortPositionPrices(
            double? entryPrice,
            double? takeProfitPrice,
            double? stopLossPrice)
        {
            var errors = new List<ValidationError>();

            // Entry must be higher than take profit (sell high, buy low)
            if (entryPrice.HasValue && takeProfitPrice.HasValue)
            {
                if (entryPrice.Value <= takeProfitPrice.Value)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.ShortEntryBelowTakeProfit,
                        $"Short position entry ({entryPrice:F2}) must be higher than take profit ({takeProfitPrice:F2}). You sell high, buy back low.",
                        "EntryPrice"));
                }
            }

            // Stop loss must be above entry (limit losses when price rises)
            if (entryPrice.HasValue && stopLossPrice.HasValue)
            {
                if (stopLossPrice.Value <= entryPrice.Value)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.ShortStopLossBelowEntry,
                        $"Short position stop loss ({stopLossPrice:F2}) must be above entry ({entryPrice:F2}). Stop loss protects against rising prices.",
                        "StopLossPrice"));
                }
            }

            // Take profit must be below stop loss
            if (takeProfitPrice.HasValue && stopLossPrice.HasValue)
            {
                if (takeProfitPrice.Value >= stopLossPrice.Value)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.ShortTakeProfitAboveStopLoss,
                        $"Short position take profit ({takeProfitPrice:F2}) must be below stop loss ({stopLossPrice:F2}).",
                        "TakeProfitPrice"));
                }
            }

            return new ValidationResult { Errors = errors };
        }

        /// <summary>
        /// Validates price relationships for a LONG position.
        /// Long buying: You buy low (entry), sell high (take profit).
        /// Entry must be LOWER than take profit.
        /// Stop loss must be BELOW entry (to limit losses when price falls).
        /// </summary>
        public static ValidationResult ValidateLongPositionPrices(
            double? entryPrice,
            double? takeProfitPrice,
            double? stopLossPrice)
        {
            var errors = new List<ValidationError>();

            // Entry must be lower than take profit (buy low, sell high)
            if (entryPrice.HasValue && takeProfitPrice.HasValue)
            {
                if (entryPrice.Value >= takeProfitPrice.Value)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.TakeProfitBelowEntry,
                        $"Long position take profit ({takeProfitPrice:F2}) must be higher than entry ({entryPrice:F2}). You buy low, sell high.",
                        "TakeProfitPrice"));
                }
            }

            // Stop loss must be below entry (limit losses when price falls)
            if (entryPrice.HasValue && stopLossPrice.HasValue)
            {
                if (stopLossPrice.Value >= entryPrice.Value)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.StopLossAboveEntry,
                        $"Long position stop loss ({stopLossPrice:F2}) must be below entry ({entryPrice:F2}). Stop loss protects against falling prices.",
                        "StopLossPrice"));
                }
            }

            return new ValidationResult { Errors = errors };
        }

        /// <summary>
        /// Extracts order prices from strategy segments and validates them.
        /// </summary>
        public static ValidationResult ValidateStrategyOrderPrices(StrategyDefinition strategy)
        {
            var errors = new List<ValidationError>();

            // Determine if this is a short position
            var isShortPosition = strategy.Segments.Any(s =>
                s.Type.ToString().Equals("SELL", StringComparison.OrdinalIgnoreCase) ||
                s.Type.ToString().Equals("SHORTPOSITION", StringComparison.OrdinalIgnoreCase));

            // Extract entry price from conditions or order
            double? entryPrice = ExtractPriceFromSegments(strategy.Segments, "ENTRY", "PRICE", "LIMITPRICE", "ISPRICEABOVE", "ISPRICEBELOW");

            // Extract take profit
            double? takeProfitPrice = ExtractPriceFromSegments(strategy.Segments, "TAKEPROFIT");

            // Extract stop loss
            double? stopLossPrice = ExtractPriceFromSegments(strategy.Segments, "STOPLOSS");

            // Validate based on position type
            errors.AddRange(ValidateOrderPrices(isShortPosition, entryPrice, takeProfitPrice, stopLossPrice).Errors);

            return new ValidationResult { Errors = errors };
        }

        /// <summary>
        /// Extracts price value from segments matching given type names.
        /// </summary>
        private static double? ExtractPriceFromSegments(IEnumerable<StrategySegment> segments, params string[] typeNames)
        {
            foreach (var segment in segments)
            {
                var segmentType = segment.Type.ToString().ToUpperInvariant();
                if (typeNames.Any(t => segmentType.Contains(t.ToUpperInvariant())))
                {
                    var priceParam = segment.Parameters?.FirstOrDefault(p =>
                        p.Name.Equals("Price", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("Level", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("LimitPrice", StringComparison.OrdinalIgnoreCase));

                    if (priceParam?.Value is double d)
                        return d;

                    if (priceParam?.Value != null && double.TryParse(priceParam.Value.ToString(), out var parsed))
                        return parsed;
                }
            }
            return null;
        }
    }
}



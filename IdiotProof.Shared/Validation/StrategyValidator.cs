// ============================================================================
// StrategyValidator - Validates strategy definitions comprehensively
// ============================================================================

using IdiotProof.Shared.Models;

namespace IdiotProof.Shared.Validation
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

                    case "BUY":
                    case "SELL":
                    case "CLOSE":
                    case "CLOSELONG":
                    case "CLOSESHORT":
                        hasOrder = true;
                        orderIndex = i;
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

            if (!hasCondition)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.MissingCondition,
                    "Strategy must have at least one condition before the order",
                    "Segments"));
            }

            if (!hasOrder)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.MissingOrder,
                    "Strategy must have an order (Buy, Sell, or Close)",
                    "Segments"));
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
                if (param.IsRequired && param.Value == null)
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
    }
}

// ============================================================================
// ValidationExtensions - Extension methods for strategy validation
// ============================================================================
//
// NOMENCLATURE:
// - Validation: Checking data correctness before processing
// - Sanitization: Removing potentially dangerous content
// - Round-trip: Converting data format and back to verify integrity
// - IdiotScript: The text-based DSL for trading strategies
// - StrategyDefinition: The parsed in-memory representation
//
// EXTENSION CATEGORIES:
// 1. Strategy Validation - ValidateForSave, ValidateForExecution
// 2. Segment Validation - ValidateParameters
// 3. IdiotScript Validation - ValidateIdiotScript, ValidateIdiotScriptSecurity
// 4. Sanitization - SanitizeIdiotScript
// 5. Round-trip Validation - ValidateIdiotScriptRoundTrip, ValidateSerializationRoundTrip
//
// USAGE:
//   var result = strategy.ValidateForSave();
//   var security = script.ValidateIdiotScriptSecurity();
//   var sanitized = script.SanitizeIdiotScript();
//
// ============================================================================

using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.Validation
{
    /// <summary>
    /// Provides extension methods to validate Shared models.
    /// </summary>
    public static class ValidationExtensions
    {
        /// <summary>
        /// Validates a complete strategy definition for saving.
        /// </summary>
        public static ValidationResult ValidateForSave(this StrategyDefinition strategy)
        {
            var results = new List<ValidationResult>
            {
                StrategyValidator.ValidateName(strategy.Name),
                InputValidator.ValidateTickerSymbol(strategy.Symbol),
                InputValidator.ValidateSafeText(strategy.Notes, "Notes"),
                InputValidator.ValidateSafeText(strategy.Description, "Description")
            };

            if (strategy.Segments.Count > 0)
            {
                results.Add(StrategyValidator.ValidateStrategy(strategy));
            }
            else
            {
                results.Add(ValidationResult.Failure(
                    ValidationCodes.Required,
                    "Strategy must have at least one segment",
                    "Segments"));
            }

            return ValidationResult.Combine(results.ToArray());
        }

        /// <summary>
        /// Validates a complete strategy definition for execution.
        /// More strict than save validation.
        /// </summary>
        public static ValidationResult ValidateForExecution(this StrategyDefinition strategy)
        {
            var saveValidation = strategy.ValidateForSave();
            if (!saveValidation.IsValid)
                return saveValidation;

            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            // Verify all parameters have values
            foreach (var segment in strategy.Segments)
            {
                foreach (var param in segment.Parameters.Where(p => p.IsRequired))
                {
                    if (param.Value == null)
                    {
                        errors.Add(new ValidationError(
                            ValidationCodes.MissingRequiredField,
                            $"Required parameter '{param.Label}' is missing in segment '{segment.DisplayName}'",
                            $"Segments[{segment.Order}].Parameters[{param.Name}]"));
                    }
                }
            }

            if (errors.Count > 0)
                return new ValidationResult { Errors = errors, Warnings = warnings };

            return saveValidation;
        }

        /// <summary>
        /// Validates a segment's parameters.
        /// </summary>
        public static ValidationResult ValidateParameters(this StrategySegment segment, int index)
        {
            return StrategyValidator.ValidateSegmentParameters(segment, index);
        }

        /// <summary>
        /// Validates an IdiotScript string for syntax and security.
        /// </summary>
        /// <param name="script">The IdiotScript to validate.</param>
        /// <returns>Validation result with errors and warnings.</returns>
        public static ValidationResult ValidateIdiotScript(this string? script)
        {
            return IdiotScriptValidator.Validate(script);
        }

        /// <summary>
        /// Validates an IdiotScript string for security threats only.
        /// Use this for quick security checks before processing.
        /// </summary>
        /// <param name="script">The IdiotScript to validate.</param>
        /// <returns>Validation result with security errors.</returns>
        public static ValidationResult ValidateIdiotScriptSecurity(this string? script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return ValidationResult.Success();

            return IdiotScriptValidator.ValidateSecurity(script);
        }

        /// <summary>
        /// Validates round-trip conversion for an IdiotScript.
        /// </summary>
        /// <param name="script">The IdiotScript to validate.</param>
        /// <returns>Validation result with any conversion discrepancies.</returns>
        public static ValidationResult ValidateIdiotScriptRoundTrip(this string script)
        {
            return IdiotScriptValidator.ValidateRoundTrip(script);
        }

        /// <summary>
        /// Sanitizes an IdiotScript by removing potentially dangerous content.
        /// </summary>
        /// <param name="script">The IdiotScript to sanitize.</param>
        /// <returns>Sanitized script.</returns>
        public static string SanitizeIdiotScript(this string script)
        {
            return IdiotScriptValidator.Sanitize(script);
        }

        /// <summary>
        /// Validates that a StrategyDefinition can be serialized to IdiotScript and back.
        /// </summary>
        /// <param name="strategy">The strategy to validate.</param>
        /// <returns>Validation result with any conversion issues.</returns>
        public static ValidationResult ValidateSerializationRoundTrip(this StrategyDefinition strategy)
        {
            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            try
            {
                // Serialize to script
                var script = IdiotScriptSerializer.Serialize(strategy);

                // Parse back to strategy
                var roundTrip = IdiotScriptParser.Parse(script);

                // Compare key fields
                if (strategy.Symbol != roundTrip.Symbol)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.RoundTripMismatch,
                        $"Symbol mismatch after round-trip: '{strategy.Symbol}' vs '{roundTrip.Symbol}'",
                        "Symbol"));
                }

                // Compare segment counts
                var originalConditions = strategy.Segments.Count(s =>
                    s.Category == Enums.SegmentCategory.PriceCondition ||
                    s.Category == Enums.SegmentCategory.VwapCondition ||
                    s.Category == Enums.SegmentCategory.IndicatorCondition);

                var roundTripConditions = roundTrip.Segments.Count(s =>
                    s.Category == Enums.SegmentCategory.PriceCondition ||
                    s.Category == Enums.SegmentCategory.VwapCondition ||
                    s.Category == Enums.SegmentCategory.IndicatorCondition);

                if (originalConditions != roundTripConditions)
                {
                    warnings.Add(new ValidationWarning(
                        "CONDITION_COUNT_MISMATCH",
                        $"Condition count differs: {originalConditions} vs {roundTripConditions}",
                        "Segments"));
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.RoundTripMismatch,
                    $"Round-trip serialization failed: {ex.Message}",
                    "Strategy"));
            }

            return new ValidationResult { Errors = errors, Warnings = warnings };
        }
    }
}

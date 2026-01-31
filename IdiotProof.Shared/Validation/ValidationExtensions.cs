// ============================================================================
// ValidationExtensions - Extension methods for strategy validation
// ============================================================================

using IdiotProof.Shared.Models;

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
    }
}

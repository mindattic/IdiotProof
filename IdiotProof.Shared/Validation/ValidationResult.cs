// ============================================================================
// ValidationResult - Result of a validation operation
// ============================================================================

namespace IdiotProof.Shared.Validation
{
    /// <summary>
    /// Represents the result of a validation operation.
    /// </summary>
    public sealed class ValidationResult
    {
        /// <summary>
        /// Whether all validations passed.
        /// </summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>
        /// List of validation errors.
        /// </summary>
        public List<ValidationError> Errors { get; init; } = [];

        /// <summary>
        /// List of validation warnings (non-blocking).
        /// </summary>
        public List<ValidationWarning> Warnings { get; init; } = [];

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        public static ValidationResult Success() => new();

        /// <summary>
        /// Creates a failed validation result with a single error.
        /// </summary>
        public static ValidationResult Failure(string errorCode, string message, string? fieldName = null)
        {
            return new ValidationResult
            {
                Errors = [new ValidationError(errorCode, message, fieldName)]
            };
        }

        /// <summary>
        /// Creates a failed validation result with a single error and attempted value.
        /// </summary>
        public static ValidationResult Failure(string errorCode, string message, string? fieldName, object? attemptedValue)
        {
            return new ValidationResult
            {
                Errors = [new ValidationError(errorCode, message, fieldName, attemptedValue)]
            };
        }

        /// <summary>
        /// Creates a failed validation result with multiple errors.
        /// </summary>
        public static ValidationResult Failure(IEnumerable<ValidationError> errors)
        {
            return new ValidationResult { Errors = errors.ToList() };
        }

        /// <summary>
        /// Combines multiple validation results into one.
        /// </summary>
        public static ValidationResult Combine(params ValidationResult[] results)
        {
            var combined = new ValidationResult();
            foreach (var result in results)
            {
                combined.Errors.AddRange(result.Errors);
                combined.Warnings.AddRange(result.Warnings);
            }
            return combined;
        }

        /// <summary>
        /// Throws a ValidationException if the result is invalid.
        /// </summary>
        public void ThrowIfInvalid()
        {
            if (!IsValid)
                throw new ValidationException(this);
        }

        /// <summary>
        /// Gets a summary of all error messages.
        /// </summary>
        public string GetErrorSummary() =>
            string.Join("; ", Errors.Select(e => e.Message));
    }

    /// <summary>
    /// Represents a single validation error.
    /// </summary>
    public sealed record ValidationError(
        string Code,
        string Message,
        string? FieldName = null,
        object? AttemptedValue = null);

    /// <summary>
    /// Represents a validation warning (non-blocking).
    /// </summary>
    public sealed record ValidationWarning(
        string Code,
        string Message,
        string? FieldName = null);

    /// <summary>
    /// Exception thrown when validation fails.
    /// </summary>
    public sealed class ValidationException : Exception
    {
        public ValidationResult Result { get; }

        public ValidationException(ValidationResult result)
            : base(result.GetErrorSummary())
        {
            Result = result;
        }
    }
}

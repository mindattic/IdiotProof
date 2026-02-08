// ============================================================================
// InputValidator - Validates primitive inputs and detects malicious content
// ============================================================================

using System.Text.RegularExpressions;

namespace IdiotProof.Validation {
    /// <summary>
    /// Validates primitive inputs for security and format compliance.
    /// Used by both frontend and backend for consistent validation.
    /// </summary>
    public static partial class InputValidator
    {
        // ====================================================================
        // Regex Patterns
        // ====================================================================

        [GeneratedRegex(@"^[A-Z]{1,5}$", RegexOptions.Compiled)]
        private static partial Regex TickerSymbolRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
        private static partial Regex SafeFilenameRegex();

        [GeneratedRegex(@"^[a-zA-Z0-9\s\-_\.\,\!\?\'\""\(\)\@\#\$\%\&\*]+$", RegexOptions.Compiled)]
        private static partial Regex SafeTextRegex();

        [GeneratedRegex(@"<script|javascript:|on\w+=|<iframe|<object|<embed|vbscript:|data:", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex XssPatternRegex();

        [GeneratedRegex(@"(\-\-|;|\||&&|\$\(|`|\bOR\b|\bAND\b|\bUNION\b|\bSELECT\b|\bDROP\b|\bINSERT\b|\bDELETE\b|\bUPDATE\b|\bEXEC\b|\bEXECUTE\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex SqlInjectionPatternRegex();

        [GeneratedRegex(@"\.\.[/\\]|~[/\\]|[/\\]\.\.|\%2e\%2e|\%252e", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex PathTraversalPatternRegex();

        [GeneratedRegex(@"\{\{.*\}\}|\$\{.*\}|\#\{.*\}", RegexOptions.Compiled)]
        private static partial Regex TemplateInjectionPatternRegex();

        // ====================================================================
        // String Validation
        // ====================================================================

        /// <summary>
        /// Validates that a string is not null or empty.
        /// </summary>
        public static ValidationResult ValidateRequired(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.Required,
                    $"{fieldName} is required",
                    fieldName);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates string length is within bounds.
        /// </summary>
        public static ValidationResult ValidateLength(string? value, string fieldName, int minLength, int maxLength)
        {
            if (value == null)
                return ValidationResult.Success();

            if (value.Length < minLength || value.Length > maxLength)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidLength,
                    $"{fieldName} must be between {minLength} and {maxLength} characters",
                    fieldName);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a stock ticker symbol (1-5 uppercase letters).
        /// </summary>
        public static ValidationResult ValidateTickerSymbol(string? symbol, string fieldName = "Symbol")
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return ValidationResult.Failure(
                    ValidationCodes.Required,
                    $"{fieldName} is required",
                    fieldName);
            }

            var normalized = symbol.Trim().ToUpperInvariant();
            if (!TickerSymbolRegex().IsMatch(normalized))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidSymbol,
                    $"{fieldName} must be 1-5 uppercase letters",
                    fieldName,
                    symbol);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates text is safe (no dangerous characters).
        /// </summary>
        public static ValidationResult ValidateSafeText(string? value, string fieldName, bool allowNull = true)
        {
            if (string.IsNullOrEmpty(value))
                return allowNull ? ValidationResult.Success() : ValidationResult.Failure(ValidationCodes.Required, $"{fieldName} is required", fieldName);

            // Check for XSS patterns
            if (XssPatternRegex().IsMatch(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InjectionDetected,
                    $"{fieldName} contains potentially malicious content",
                    fieldName);
            }

            // Check for SQL injection patterns
            if (SqlInjectionPatternRegex().IsMatch(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InjectionDetected,
                    $"{fieldName} contains potentially malicious content",
                    fieldName);
            }

            // Check for template injection
            if (TemplateInjectionPatternRegex().IsMatch(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InjectionDetected,
                    $"{fieldName} contains potentially malicious content",
                    fieldName);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a filename is safe (no path traversal).
        /// </summary>
        public static ValidationResult ValidateFilename(string? filename, string fieldName = "Filename")
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return ValidationResult.Failure(
                    ValidationCodes.Required,
                    $"{fieldName} is required",
                    fieldName);
            }

            // Check for path traversal
            if (PathTraversalPatternRegex().IsMatch(filename))
            {
                return ValidationResult.Failure(
                    ValidationCodes.PathTraversal,
                    $"{fieldName} contains invalid path characters",
                    fieldName);
            }

            // Check for valid filename characters
            if (!SafeFilenameRegex().IsMatch(filename))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidCharacters,
                    $"{fieldName} contains invalid characters",
                    fieldName);
            }

            return ValidationResult.Success();
        }

        // ====================================================================
        // Numeric Validation
        // ====================================================================

        /// <summary>
        /// Validates an integer is within range.
        /// </summary>
        public static ValidationResult ValidateRange(int value, string fieldName, int min, int max)
        {
            if (value < min || value > max)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} must be between {min} and {max}",
                    fieldName,
                    value);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a double is within range.
        /// </summary>
        public static ValidationResult ValidateRange(double value, string fieldName, double min, double max)
        {
            if (value < min || value > max || double.IsNaN(value) || double.IsInfinity(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} must be between {min} and {max}",
                    fieldName,
                    value);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a price value is positive.
        /// </summary>
        public static ValidationResult ValidatePrice(double price, string fieldName)
        {
            if (price <= 0 || double.IsNaN(price) || double.IsInfinity(price))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidPrice,
                    $"{fieldName} must be a positive number",
                    fieldName,
                    price);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a quantity is positive.
        /// </summary>
        public static ValidationResult ValidateQuantity(int quantity, string fieldName, int maxQuantity = 1_000_000)
        {
            if (quantity <= 0)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidQuantity,
                    $"{fieldName} must be a positive number",
                    fieldName,
                    quantity);
            }

            if (quantity > maxQuantity)
            {
                return ValidationResult.Failure(
                    ValidationCodes.ExceedsMaxPosition,
                    $"{fieldName} exceeds maximum allowed ({maxQuantity})",
                    fieldName,
                    quantity);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a percentage is between 0 and 1.
        /// </summary>
        public static ValidationResult ValidatePercentage(double percent, string fieldName)
        {
            if (percent < 0 || percent > 1 || double.IsNaN(percent))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} must be between 0% and 100%",
                    fieldName,
                    percent);
            }
            return ValidationResult.Success();
        }

        // ====================================================================
        // Time Validation
        // ====================================================================

        /// <summary>
        /// Validates a time range is valid (start before end).
        /// </summary>
        public static ValidationResult ValidateTimeRange(TimeOnly? startTime, TimeOnly? endTime, string fieldName = "Time range")
        {
            if (startTime.HasValue && endTime.HasValue && startTime.Value >= endTime.Value)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidTimeRange,
                    $"{fieldName} start time must be before end time",
                    fieldName);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates a DateTime is in the future.
        /// </summary>
        public static ValidationResult ValidateFutureDate(DateTime dateTime, string fieldName)
        {
            if (dateTime <= DateTime.UtcNow)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidRange,
                    $"{fieldName} must be in the future",
                    fieldName,
                    dateTime);
            }
            return ValidationResult.Success();
        }

        // ====================================================================
        // Enum Validation
        // ====================================================================

        /// <summary>
        /// Validates an enum value is defined.
        /// </summary>
        public static ValidationResult ValidateEnum<TEnum>(TEnum value, string fieldName) where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidValue,
                    $"{fieldName} has an invalid value",
                    fieldName,
                    value);
            }
            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates and parses an enum from string.
        /// </summary>
        public static ValidationResult ValidateEnumString<TEnum>(string? value, string fieldName, out TEnum result) where TEnum : struct, Enum
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return ValidationResult.Failure(
                    ValidationCodes.Required,
                    $"{fieldName} is required",
                    fieldName);
            }

            if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out result) || !Enum.IsDefined(result))
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidValue,
                    $"{fieldName} has an invalid value: {value}",
                    fieldName,
                    value);
            }

            return ValidationResult.Success();
        }

        // ====================================================================
        // Collection Validation
        // ====================================================================

        /// <summary>
        /// Validates a collection is not empty.
        /// </summary>
        public static ValidationResult ValidateNotEmpty<T>(IEnumerable<T>? collection, string fieldName)
        {
            if (collection == null || !collection.Any())
            {
                return ValidationResult.Failure(
                    ValidationCodes.Required,
                    $"{fieldName} must contain at least one item",
                    fieldName);
            }
            return ValidationResult.Success();
        }
    }
}



// ============================================================================
// JsonValidator - Validates JSON content for safety and correctness
// ============================================================================

using System.Text.Json;

namespace IdiotProof.Validation {
    /// <summary>
    /// Validates JSON content before parsing and processing.
    /// </summary>
    public static class JsonValidator
    {
        /// <summary>
        /// Maximum allowed JSON size in bytes.
        /// </summary>
        public const int MaxJsonSize = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// Maximum nesting depth for JSON.
        /// </summary>
        public const int MaxDepth = 32;

        /// <summary>
        /// Validates that a string is valid JSON.
        /// </summary>
        public static ValidationResult ValidateJson(string? json, string fieldName = "JSON")
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return ValidationResult.Failure(
                    ValidationCodes.Required,
                    $"{fieldName} is required",
                    fieldName);
            }

            // Check size
            if (json.Length > MaxJsonSize)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidLength,
                    $"{fieldName} exceeds maximum size of {MaxJsonSize / 1024 / 1024}MB",
                    fieldName);
            }

            // Try parsing
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    MaxDepth = MaxDepth,
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                return ValidationResult.Success();
            }
            catch (JsonException ex)
            {
                return ValidationResult.Failure(
                    ValidationCodes.InvalidJson,
                    $"{fieldName} is not valid JSON: {ex.Message}",
                    fieldName);
            }
        }

        /// <summary>
        /// Validates and deserializes JSON to a type.
        /// </summary>
        public static ValidationResult ValidateAndDeserialize<T>(
            string? json,
            out T? result,
            string fieldName = "JSON") where T : class
        {
            result = default;

            var jsonValidation = ValidateJson(json, fieldName);
            if (!jsonValidation.IsValid)
                return jsonValidation;

            try
            {
                result = JsonSerializer.Deserialize<T>(json!, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                });

                if (result == null)
                {
                    return ValidationResult.Failure(
                        ValidationCodes.InvalidJson,
                        $"{fieldName} could not be deserialized",
                        fieldName);
                }

                return ValidationResult.Success();
            }
            catch (JsonException ex)
            {
                return ValidationResult.Failure(
                    ValidationCodes.SchemaMismatch,
                    $"{fieldName} does not match expected schema: {ex.Message}",
                    fieldName);
            }
        }

        /// <summary>
        /// Validates JSON contains required fields.
        /// </summary>
        public static ValidationResult ValidateRequiredFields(
            string json,
            IEnumerable<string> requiredFields,
            string fieldName = "JSON")
        {
            var errors = new List<ValidationError>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                foreach (var field in requiredFields)
                {
                    if (!root.TryGetProperty(field, out var prop) ||
                        prop.ValueKind == JsonValueKind.Null ||
                        (prop.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(prop.GetString())))
                    {
                        errors.Add(new ValidationError(
                            ValidationCodes.MissingRequiredField,
                            $"Required field '{field}' is missing or empty",
                            field));
                    }
                }
            }
            catch (JsonException ex)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidJson,
                    $"Failed to validate required fields: {ex.Message}",
                    fieldName));
            }

            return new ValidationResult { Errors = errors };
        }

        /// <summary>
        /// Validates JSON for strategy import (checks for malicious content).
        /// </summary>
        public static ValidationResult ValidateStrategyImportJson(string? json)
        {
            var jsonValidation = ValidateJson(json, "Strategy JSON");
            if (!jsonValidation.IsValid)
                return jsonValidation;

            var errors = new List<ValidationError>();

            // Required fields for strategy
            var requiredFields = new[] { "name", "symbol", "segments" };
            var fieldsValidation = ValidateRequiredFields(json!, requiredFields);
            if (!fieldsValidation.IsValid)
                errors.AddRange(fieldsValidation.Errors);

            // Check for suspicious content in string values
            try
            {
                using var doc = JsonDocument.Parse(json!);
                ValidateElementSecurity(doc.RootElement, "", errors);
            }
            catch
            {
                // Already validated above
            }

            return new ValidationResult { Errors = errors };
        }

        private static void ValidateElementSecurity(JsonElement element, string path, List<ValidationError> errors)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                    {
                        var propPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                        ValidateElementSecurity(prop.Value, propPath, errors);
                    }
                    break;

                case JsonValueKind.Array:
                    int i = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        ValidateElementSecurity(item, $"{path}[{i}]", errors);
                        i++;
                    }
                    break;

                case JsonValueKind.String:
                    var value = element.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var textValidation = InputValidator.ValidateSafeText(value, path);
                        if (!textValidation.IsValid)
                        {
                            errors.AddRange(textValidation.Errors);
                        }
                    }
                    break;
            }
        }
    }
}



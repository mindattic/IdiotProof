// ============================================================================
// IdiotScriptValidator - Validates IdiotScript syntax and content security
// ============================================================================
//
// NOMENCLATURE:
// - IdiotScript: The text-based DSL (Ticker(AAPL).Breakout(150).Buy)
// - Fluent API: The C# builder pattern API (Stock.Ticker().Breakout().Long())
// - Validation: Checking script correctness and security
// - Sanitization: Removing potentially dangerous content
// - XSS: Cross-Site Scripting (JavaScript injection attacks)
// - Injection: SQL, command, or template injection attacks
// - Whitelist: Allowed commands and constants
// - Round-trip: Converting IdiotScript → Strategy → IdiotScript
//
// VALIDATION LAYERS:
// 1. Security Validation - XSS, SQL injection, template injection
// 2. Syntax Validation - Balanced parentheses, valid characters
// 3. Command Validation - Whitelist enforcement
// 4. Parameter Validation - Type checking, range validation
// 5. Round-trip Validation - Bidirectional conversion integrity
//
// USAGE:
//   var result = IdiotScriptValidator.Validate(script);
//   if (!result.IsValid) { /* handle errors */ }
//
//   var sanitized = IdiotScriptValidator.Sanitize(script);
//   var securityCheck = script.ValidateIdiotScriptSecurity();
//
// ============================================================================

using System.Text.RegularExpressions;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.Validation;

/// <summary>
/// Validates IdiotScript for syntax correctness and security.
/// Used by both frontend and backend to ensure safe script handling.
/// </summary>
public static partial class IdiotScriptValidator
{
    // ========================================================================
    // REGEX PATTERNS
    // ========================================================================

    /// <summary>Pattern to detect script injection attempts.</summary>
    [GeneratedRegex(@"<script|javascript:|on\w+=|<iframe|<object|<embed|vbscript:|data:", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex XssPattern();

    /// <summary>Pattern to detect SQL injection attempts.</summary>
    [GeneratedRegex(@"(\-\-|;|\||&&|\$\(|`|\bOR\b|\bAND\b|\bUNION\b|\bSELECT\b|\bDROP\b|\bINSERT\b|\bDELETE\b|\bUPDATE\b|\bEXEC\b|\bEXECUTE\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SqlInjectionPattern();

    /// <summary>Pattern to detect template injection attempts.</summary>
    [GeneratedRegex(@"\{\{.*\}\}|\$\{.*\}|\#\{.*\}", RegexOptions.Compiled)]
    private static partial Regex TemplateInjectionPattern();

    /// <summary>Pattern for valid command names (alphanumeric and underscore).</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex ValidCommandNamePattern();

    /// <summary>Pattern for valid ticker symbols.</summary>
    [GeneratedRegex(@"^[A-Z]{1,5}$", RegexOptions.Compiled)]
    private static partial Regex ValidTickerPattern();

    /// <summary>Pattern to detect dangerous characters in string content.</summary>
    [GeneratedRegex(@"[<>\""`\\]", RegexOptions.Compiled)]
    private static partial Regex DangerousStringCharsPattern();

    // ========================================================================
    // WHITELISTED COMMANDS
    // ========================================================================

    /// <summary>
    /// Whitelist of valid IdiotScript command names (case-insensitive).
    /// Any command not in this list will be rejected.
    /// </summary>
    public static readonly HashSet<string> ValidCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // Symbol/Identity commands
        "TICKER", "SYM", "SYMBOL", "STOCK", "STRATEGY",
        "NAME", "DESC", "DESCRIPTION", "ENABLED", "ISENABLED",

        // Session/Time commands
        "SESSION", "TIMEFRAME", "START", "END",
        "EXITSTRATEGY", "CLOSEPOSITION", "CLOSE", "CLOSELONG", "CLOSESHORT",

        // Quantity/Entry commands
        "QTY", "QUANTITY", "ENTRY", "PRICE",

        // Order Direction commands
        "ORDER", "LONG", "SHORT",

        // Risk Management commands
        "TP", "TAKEPROFIT", "SL", "STOPLOSS",
        "TSL", "TRAILINGSTOPLOSS",
        "ADAPTIVEORDER", "ISADAPTIVEORDER",

        // Price Condition commands
        "BREAKOUT", "PULLBACK",
        "ISPRICEABOVE", "ISPRICEBELOW", "PRICEBELOW",

        // Gap Condition commands
        "GAPUP", "GAPDOWN", "ISGAPUP", "ISGAPDOWN",

        // VWAP Condition commands
        "ABOVEVWAP", "BELOWVWAP", "VWAP",
        "ISABOVEVWAP", "ISBELOWVWAP",
        "CLOSEABOVEVWAP", "ISCLOSEABOVEVWAP",
        "VWAPREJECTION", "VWAPREJECTED", "ISVWAPREJECTION", "ISVWAPREJECTED",

        // EMA Condition commands
        "EMAABOVE", "EMABELOW", "EMABETWEEN", "EMATURNINGUP",
        "ISEMAABOVE", "ISEMABELOW", "ISEMABETWEEN", "ISEMATURNINGUP",

        // RSI Condition commands
        "RSIOVERSOLD", "RSIOVERBOUGHT",
        "ISRSIOVERSOLD", "ISRSIOVERBOUGHT",

        // ADX Condition commands
        "ADXABOVE", "ISADXABOVE",

        // MACD Condition commands
        "MACDBULLISH", "MACDBEARISH",
        "ISMACDBULLISH", "ISMACDBEARISH",

        // DI (Directional Index) Condition commands
        "DIPOSITIVE", "DINEGATIVE",
        "ISDIPOSITIVE", "ISDINEGATIVE",

        // Momentum Condition commands
        "MOMENTUMABOVE", "MOMENTUMBELOW",
        "ISMOMENTUMABOVE", "ISMOMENTUMBELOW",

        // ROC Condition commands
        "ROCABOVE", "ROCBELOW",
        "ISROCABOVE", "ISROCBELOW",

        // Pattern Condition commands
        "HIGHERLOWS", "ISHIGHERLOWS",
        "VOLUMEABOVE", "ISVOLUMEABOVE",

        // Order Configuration commands
        "TIMEINFORCE", "TIF",
        "OUTSIDERTH", "EXTENDEDHOURS",
        "ALLORNONE", "AON",
        "ORDERTYPE",

        // Execution Behavior commands
        "REPEAT", "ISREPEAT",
        "PROFITABLE", "ISPROFITABLE",
        "AUTONOMOUSTRADING", "ISAUTONOMOUSTRADING",

        // Boolean keywords
        "TRUE", "FALSE", "YES", "NO", "Y", "N"
    };

    /// <summary>
    /// Whitelist of valid IS. constant prefixes.
    /// </summary>
    public static readonly HashSet<string> ValidConstantPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sessions
        "IS.PREMARKET", "IS.RTH", "IS.AFTERHOURS", "IS.EXTENDED", "IS.ACTIVE",
        "IS.PREMARKET_END_EARLY", "IS.PREMARKET_START_LATE",

        // Times
        "IS.BELL", "IS.PREMARKET.BELL", "IS.RTH.BELL", "IS.AFTERHOURS.BELL",
        "IS.OPEN", "IS.CLOSE", "IS.EOD",
        "IS.PM_START", "IS.AH_END",

        // Trailing Stop percentages
        "IS.TIGHT", "IS.MODERATE", "IS.STANDARD", "IS.LOOSE", "IS.WIDE",

        // Order direction
        "IS.LONG", "IS.SHORT", "IS.CLOSE_LONG", "IS.CLOSE_SHORT",

        // Adaptive Order modes
        "IS.CONSERVATIVE", "IS.BALANCED", "IS.AGGRESSIVE",

        // Indicator thresholds
        "IS.RSI_OVERSOLD", "IS.RSI_OVERBOUGHT", "IS.ADX_STRONG", "IS.ADX_WEAK",

        // Boolean constants
        "IS.TRUE", "IS.FALSE"
    };

    // ========================================================================
    // VALIDATION METHODS
    // ========================================================================

    /// <summary>
    /// Performs comprehensive validation of an IdiotScript string.
    /// </summary>
    /// <param name="script">The script to validate.</param>
    /// <returns>Validation result with errors and warnings.</returns>
    public static ValidationResult Validate(string? script)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        if (string.IsNullOrWhiteSpace(script))
        {
            errors.Add(new ValidationError(
                ValidationCodes.Required,
                "Script cannot be empty",
                "Script"));
            return new ValidationResult { Errors = errors };
        }

        // Security validations (highest priority)
        var securityResult = ValidateSecurity(script);
        if (!securityResult.IsValid)
            return securityResult;

        // Syntax validation
        var syntaxResult = ValidateSyntax(script);
        errors.AddRange(syntaxResult.Errors);
        warnings.AddRange(syntaxResult.Warnings);

        // Command whitelist validation
        var commandResult = ValidateCommands(script);
        errors.AddRange(commandResult.Errors);
        warnings.AddRange(commandResult.Warnings);

        // Parse validation (try to parse the script)
        var parseResult = ValidateParsing(script);
        errors.AddRange(parseResult.Errors);
        warnings.AddRange(parseResult.Warnings);

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Validates script for security threats (XSS, injection, etc.).
    /// </summary>
    public static ValidationResult ValidateSecurity(string script)
    {
        var errors = new List<ValidationError>();

        // Check for XSS patterns
        if (XssPattern().IsMatch(script))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InjectionDetected,
                "Script contains potentially malicious XSS content",
                "Script"));
        }

        // Check for SQL injection patterns
        if (SqlInjectionPattern().IsMatch(script))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InjectionDetected,
                "Script contains potentially malicious SQL injection content",
                "Script"));
        }

        // Check for template injection
        if (TemplateInjectionPattern().IsMatch(script))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InjectionDetected,
                "Script contains potentially malicious template injection content",
                "Script"));
        }

        return new ValidationResult { Errors = errors };
    }

    /// <summary>
    /// Validates IdiotScript syntax structure.
    /// </summary>
    public static ValidationResult ValidateSyntax(string script)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Check balanced parentheses
        int parenDepth = 0;
        int position = 0;
        foreach (char c in script)
        {
            position++;
            if (c == '(')
                parenDepth++;
            else if (c == ')')
            {
                parenDepth--;
                if (parenDepth < 0)
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.InvalidSyntax,
                        $"Unexpected closing parenthesis at position {position}",
                        "Script",
                        script));
                    break;
                }
            }
        }

        if (parenDepth > 0)
        {
            errors.Add(new ValidationError(
                ValidationCodes.InvalidSyntax,
                $"Missing {parenDepth} closing parenthesis(es)",
                "Script",
                script));
        }

        // Check for empty parentheses where content is expected
        if (script.Contains("()"))
        {
            // Some commands allow empty parentheses (e.g., BREAKOUT())
            // This is just a warning, not an error
        }

        // Check for invalid character sequences
        if (script.Contains(".."))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InvalidSyntax,
                "Invalid syntax: consecutive periods",
                "Script"));
        }

        if (script.Contains(",,"))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InvalidSyntax,
                "Invalid syntax: consecutive commas",
                "Script"));
        }

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Validates that all commands in the script are whitelisted.
    /// </summary>
    public static ValidationResult ValidateCommands(string script)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Extract command names from the script
        var commands = ExtractCommandNames(script);

        foreach (var command in commands)
        {
            // Skip IS. constants - validate them separately
            if (command.StartsWith("IS.", StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidConstantPrefixes.Contains(command))
                {
                    errors.Add(new ValidationError(
                        ValidationCodes.InvalidCommand,
                        $"Unknown constant: {command}",
                        "Script",
                        command));
                }
                continue;
            }

            // Check command is in whitelist
            if (!ValidCommands.Contains(command))
            {
                errors.Add(new ValidationError(
                    ValidationCodes.InvalidCommand,
                    $"Unknown command: {command}",
                    "Script",
                    command));
            }
        }

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Validates the script can be parsed without errors.
    /// </summary>
    public static ValidationResult ValidateParsing(string script)
    {
        var (isValid, parseErrors) = IdiotScriptParser.Validate(script);

        if (!isValid)
        {
            return new ValidationResult
            {
                Errors = parseErrors.Select(e => new ValidationError(
                    ValidationCodes.InvalidSyntax,
                    e,
                    "Script")).ToList()
            };
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates round-trip conversion: Script → Strategy → Script.
    /// Ensures no data loss during conversion.
    /// </summary>
    /// <param name="script">The original script to validate.</param>
    /// <returns>Validation result with any discrepancies noted.</returns>
    public static ValidationResult ValidateRoundTrip(string script)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        try
        {
            // Parse the script
            var strategy = IdiotScriptParser.Parse(script);

            // Serialize back to script
            var regeneratedScript = IdiotScriptSerializer.Serialize(strategy);

            // Parse the regenerated script
            var roundTripStrategy = IdiotScriptParser.Parse(regeneratedScript);

            // Compare key properties
            if (strategy.Symbol != roundTripStrategy.Symbol)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.RoundTripMismatch,
                    $"Symbol mismatch: {strategy.Symbol} vs {roundTripStrategy.Symbol}",
                    "Symbol"));
            }

            if (strategy.Enabled != roundTripStrategy.Enabled)
            {
                warnings.Add(new ValidationWarning(
                    "ROUNDTRIP_WARNING",
                    $"Enabled flag may have changed: {strategy.Enabled} vs {roundTripStrategy.Enabled}",
                    "Enabled"));
            }

            if (strategy.Segments.Count != roundTripStrategy.Segments.Count)
            {
                warnings.Add(new ValidationWarning(
                    "ROUNDTRIP_WARNING",
                    $"Segment count differs: {strategy.Segments.Count} vs {roundTripStrategy.Segments.Count}",
                    "Segments"));
            }
        }
        catch (IdiotScriptException ex)
        {
            errors.Add(new ValidationError(
                ValidationCodes.RoundTripMismatch,
                $"Round-trip conversion failed: {ex.Message}",
                "Script"));
        }

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Validates a string parameter value for security.
    /// </summary>
    public static ValidationResult ValidateStringParameter(string? value, string parameterName, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(value))
            return ValidationResult.Success();

        var errors = new List<ValidationError>();

        // Check length
        if (value.Length > maxLength)
        {
            errors.Add(new ValidationError(
                ValidationCodes.InvalidLength,
                $"{parameterName} exceeds maximum length of {maxLength}",
                parameterName,
                value));
        }

        // Check for dangerous characters
        if (DangerousStringCharsPattern().IsMatch(value))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InvalidCharacters,
                $"{parameterName} contains invalid characters",
                parameterName,
                value));
        }

        // Check for XSS
        if (XssPattern().IsMatch(value))
        {
            errors.Add(new ValidationError(
                ValidationCodes.InjectionDetected,
                $"{parameterName} contains potentially malicious content",
                parameterName));
        }

        return new ValidationResult { Errors = errors };
    }

    /// <summary>
    /// Validates a ticker symbol.
    /// </summary>
    public static ValidationResult ValidateTickerSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return ValidationResult.Failure(
                ValidationCodes.Required,
                "Ticker symbol is required",
                "Symbol");
        }

        var normalized = symbol.Trim().ToUpperInvariant();
        if (!ValidTickerPattern().IsMatch(normalized))
        {
            return ValidationResult.Failure(
                ValidationCodes.InvalidSymbol,
                "Ticker symbol must be 1-5 uppercase letters",
                "Symbol",
                symbol);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a boolean value (including IS.TRUE/IS.FALSE constants).
    /// </summary>
    public static ValidationResult ValidateBoolean(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Success();

        if (!IdiotScriptConstants.IsValidBoolean(value))
        {
            return ValidationResult.Failure(
                ValidationCodes.InvalidValue,
                $"{fieldName} must be a valid boolean (Y, YES, TRUE, N, NO, FALSE, IS.TRUE, IS.FALSE)",
                fieldName,
                value);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Sanitizes script content by removing potentially dangerous elements.
    /// </summary>
    /// <param name="script">The script to sanitize.</param>
    /// <returns>Sanitized script.</returns>
    public static string Sanitize(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return string.Empty;

        // Remove potential XSS content
        script = XssPattern().Replace(script, string.Empty);

        // Remove potential SQL injection content
        script = SqlInjectionPattern().Replace(script, string.Empty);

        // Remove potential template injection
        script = TemplateInjectionPattern().Replace(script, string.Empty);

        // Normalize whitespace
        script = Regex.Replace(script, @"\s+", " ").Trim();

        return script;
    }

    // ========================================================================
    // FLUENT API / IDIOTSCRIPT MAPPING VALIDATION
    // ========================================================================

    /// <summary>
    /// Validates that all fluent API methods have IdiotScript equivalents.
    /// Used to ensure bidirectional conversion is possible.
    /// </summary>
    /// <returns>Validation result with any missing mappings.</returns>
    public static ValidationResult ValidateFluentApiCoverage()
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            // Check that IdiotScript commands exist
            if (mapping.IdiotScriptCommands.Length == 0)
            {
                errors.Add(new ValidationError(
                    ValidationCodes.NoScriptEquivalent,
                    $"Fluent API method '{mapping.FluentMethod}' has no IdiotScript equivalent",
                    "FluentApiMapping"));
            }

            // Check that at least one IdiotScript command is in the whitelist
            var anyInWhitelist = mapping.IdiotScriptCommands.Any(cmd =>
                ValidCommands.Contains(cmd) || cmd.StartsWith("IS.", StringComparison.OrdinalIgnoreCase));

            if (!anyInWhitelist && mapping.IdiotScriptCommands.Length > 0)
            {
                warnings.Add(new ValidationWarning(
                    "COMMAND_NOT_WHITELISTED",
                    $"IdiotScript commands for '{mapping.FluentMethod}' not in validator whitelist: {string.Join(", ", mapping.IdiotScriptCommands)}",
                    "FluentApiMapping"));
            }
        }

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Validates that all whitelisted IdiotScript commands have fluent API equivalents.
    /// </summary>
    /// <returns>Validation result with any unmapped commands.</returns>
    public static ValidationResult ValidateIdiotScriptCoverage()
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        var mappedCommands = FluentApiScriptMapping.AllMappings
            .SelectMany(m => m.IdiotScriptCommands)
            .Select(c => c.ToUpperInvariant())
            .ToHashSet();

        foreach (var command in ValidCommands)
        {
            // Skip boolean keywords - they're parameters, not commands
            if (command is "TRUE" or "FALSE" or "YES" or "NO" or "Y" or "N")
                continue;

            if (!mappedCommands.Contains(command.ToUpperInvariant()))
            {
                warnings.Add(new ValidationWarning(
                    ValidationCodes.NoFluentEquivalent,
                    $"IdiotScript command '{command}' has no documented fluent API equivalent",
                    "IdiotScriptMapping"));
            }
        }

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Validates that fluent API and IdiotScript methods share compatible parameters.
    /// </summary>
    /// <returns>Validation result with any parameter mismatches.</returns>
    public static ValidationResult ValidateParameterCompatibility()
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        foreach (var mapping in FluentApiScriptMapping.AllMappings)
        {
            // Warn if no parameters are documented
            if (mapping.Parameters.Length == 0 && mapping.RequiresParameters)
            {
                warnings.Add(new ValidationWarning(
                    ValidationCodes.ParameterMismatch,
                    $"Method '{mapping.FluentMethod}' requires parameters but none are documented",
                    "ParameterMapping"));
            }
        }

        return new ValidationResult { Errors = errors, Warnings = warnings };
    }

    /// <summary>
    /// Runs all mapping validations to ensure complete bidirectional conversion support.
    /// </summary>
    /// <returns>Combined validation result.</returns>
    public static ValidationResult ValidateMappingCompleteness()
    {
        var results = new[]
        {
            ValidateFluentApiCoverage(),
            ValidateIdiotScriptCoverage(),
            ValidateParameterCompatibility()
        };

        return ValidationResult.Combine(results);
    }

    /// <summary>
    /// Validates a boolean parameter value.
    /// Valid values: Y, YES, yes, true, TRUE, 1, N, NO, no, false, FALSE, 0, IS.TRUE, IS.FALSE
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="fieldName">The field name for error messages.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateBooleanParameter(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Success();

        if (!IdiotScriptConstants.AllBooleanValues.Contains(value))
        {
            return ValidationResult.Failure(
                ValidationCodes.InvalidValue,
                $"{fieldName} must be a valid boolean: Y, YES, TRUE, N, NO, FALSE, IS.TRUE, IS.FALSE",
                fieldName,
                value);
        }

        return ValidationResult.Success();
    }

    // ========================================================================
    // HELPER METHODS
    // ========================================================================

    /// <summary>
    /// Extracts command names from a script (without parameters).
    /// </summary>
    private static List<string> ExtractCommandNames(string script)
    {
        var commands = new List<string>();
        var current = new System.Text.StringBuilder();
        int parenDepth = 0;
        bool inIsConstant = false;

        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];

            if (c == '(')
            {
                // Save command name before opening paren
                if (parenDepth == 0 && current.Length > 0)
                {
                    var cmd = current.ToString().Trim();
                    if (!string.IsNullOrEmpty(cmd))
                        commands.Add(cmd);
                    current.Clear();
                }
                parenDepth++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
            }
            else if (c == '.' && parenDepth == 0)
            {
                // Check if this is part of IS. prefix
                var currentStr = current.ToString().Trim();
                if (currentStr.Equals("IS", StringComparison.OrdinalIgnoreCase))
                {
                    inIsConstant = true;
                    current.Append(c);
                }
                else if (currentStr.StartsWith("IS.", StringComparison.OrdinalIgnoreCase))
                {
                    // Still part of IS. constant (e.g., IS.PREMARKET.BELL)
                    current.Append(c);
                }
                else
                {
                    // Command delimiter
                    if (current.Length > 0)
                    {
                        var cmd = current.ToString().Trim();
                        if (!string.IsNullOrEmpty(cmd))
                            commands.Add(cmd);
                    }
                    current.Clear();
                    inIsConstant = false;
                }
            }
            else if (parenDepth == 0)
            {
                // Building command name outside parentheses
                if (char.IsLetterOrDigit(c) || c == '_' || (inIsConstant && c == '.'))
                {
                    current.Append(c);
                }
            }
            else if (parenDepth > 0)
            {
                // Inside parentheses - check for IS. constants in parameters
                if (c == 'I' && i + 2 < script.Length &&
                    script[i + 1] == 'S' && script[i + 2] == '.')
                {
                    // Found IS. inside parameters - extract the constant
                    var constBuilder = new System.Text.StringBuilder("IS.");
                    i += 3;
                    while (i < script.Length && (char.IsLetterOrDigit(script[i]) || script[i] == '_' || script[i] == '.'))
                    {
                        constBuilder.Append(script[i]);
                        i++;
                    }
                    i--; // Back up one since the loop will increment
                    commands.Add(constBuilder.ToString());
                }
            }
        }

        // Add final command
        if (current.Length > 0)
        {
            var cmd = current.ToString().Trim();
            if (!string.IsNullOrEmpty(cmd))
                commands.Add(cmd);
        }

        return commands;
    }
}



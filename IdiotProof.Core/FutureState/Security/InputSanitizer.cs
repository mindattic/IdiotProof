// IdiotProof.Core.FutureState.Security
// Input Sanitization for preventing injection attacks
// Covers SQL injection, XSS, command injection, and IdiotScript injection

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IdiotProof.Core.FutureState.Security;

/// <summary>
/// Comprehensive input sanitization to prevent injection attacks.
/// </summary>
public static class InputSanitizer
{
    // Maximum lengths for various input types
    public const int MaxTickerLength = 10;
    public const int MaxStrategyNameLength = 100;
    public const int MaxScriptLength = 10000;
    public const int MaxClientIdLength = 50;
    public const int MaxGenericStringLength = 1000;
    
    // Regex patterns for validation
    private static readonly Regex TickerPattern = new(@"^[A-Z]{1,5}$", RegexOptions.Compiled);
    private static readonly Regex AlphanumericPattern = new(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeNamePattern = new(@"^[a-zA-Z0-9_\-\s\.]+$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    
    // SQL injection patterns to detect
    private static readonly string[] SqlInjectionPatterns = new[]
    {
        @"('|""|;|--|#|/\*|\*/)",          // Common SQL chars
        @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE)\b)",
        @"(\b(UNION|JOIN|WHERE|FROM|INTO)\b)",
        @"(\b(OR|AND)\s+\d+\s*=\s*\d+)",   // OR 1=1 style
        @"(xp_|sp_)",                       // SQL Server stored procs
        @"(\bCAST\b|\bCONVERT\b|\bCHAR\b)", // SQL functions
    };
    
    // XSS patterns to detect
    private static readonly string[] XssPatterns = new[]
    {
        @"<script",
        @"javascript:",
        @"vbscript:",
        @"on\w+\s*=",                      // Event handlers (onclick, onerror, etc.)
        @"<iframe",
        @"<object",
        @"<embed",
        @"<link",
        @"<meta",
        @"expression\s*\(",                 // CSS expression
        @"url\s*\(",                        // CSS url injection
    };
    
    // Command injection patterns
    private static readonly string[] CommandInjectionPatterns = new[]
    {
        @"[;&|`$]",                         // Shell metacharacters
        @"\$\(",                            // Command substitution
        @"`",                               // Backtick execution
        @"\.\./",                           // Directory traversal
        @"\\",                              // Backslash (Windows paths)
        @"(^|\s)(rm|del|format|shutdown)\s", // Dangerous commands
    };
    
    /// <summary>
    /// Result of input validation.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; init; }
        public string SanitizedValue { get; init; } = string.Empty;
        public List<string> Errors { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
        
        public static ValidationResult Success(string sanitizedValue) => 
            new() { IsValid = true, SanitizedValue = sanitizedValue };
            
        public static ValidationResult Failure(params string[] errors) =>
            new() { IsValid = false, Errors = errors.ToList() };
    }
    
    /// <summary>
    /// Validates and sanitizes a ticker symbol.
    /// </summary>
    public static ValidationResult ValidateTicker(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failure("Ticker cannot be empty");
            
        var ticker = input.Trim().ToUpperInvariant();
        
        if (ticker.Length > MaxTickerLength)
            return ValidationResult.Failure($"Ticker too long (max {MaxTickerLength})");
            
        if (!TickerPattern.IsMatch(ticker))
            return ValidationResult.Failure("Ticker must be 1-5 uppercase letters");
            
        return ValidationResult.Success(ticker);
    }
    
    /// <summary>
    /// Validates and sanitizes a client ID.
    /// </summary>
    public static ValidationResult ValidateClientId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failure("Client ID cannot be empty");
            
        var clientId = input.Trim();
        
        if (clientId.Length > MaxClientIdLength)
            return ValidationResult.Failure($"Client ID too long (max {MaxClientIdLength})");
            
        if (!AlphanumericPattern.IsMatch(clientId))
            return ValidationResult.Failure("Client ID must be alphanumeric with dashes/underscores only");
            
        // Check for injection patterns
        if (ContainsSqlInjection(clientId))
            return ValidationResult.Failure("Client ID contains potentially dangerous characters");
            
        return ValidationResult.Success(clientId);
    }
    
    /// <summary>
    /// Validates and sanitizes a strategy name.
    /// </summary>
    public static ValidationResult ValidateStrategyName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Success("Unnamed Strategy"); // Default name
            
        var name = input.Trim();
        
        if (name.Length > MaxStrategyNameLength)
            return ValidationResult.Failure($"Strategy name too long (max {MaxStrategyNameLength})");
            
        if (!SafeNamePattern.IsMatch(name))
            return ValidationResult.Failure("Strategy name contains invalid characters");
            
        // Encode HTML entities for safe display
        name = HtmlEncode(name);
        
        return ValidationResult.Success(name);
    }
    
    /// <summary>
    /// Validates and sanitizes an IdiotScript.
    /// </summary>
    public static ValidationResult ValidateIdiotScript(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failure("Script cannot be empty");
            
        var script = input.Trim();
        
        if (script.Length > MaxScriptLength)
            return ValidationResult.Failure($"Script too long (max {MaxScriptLength})");
            
        var errors = new List<string>();
        var warnings = new List<string>();
        
        // Check for SQL injection attempts
        if (ContainsSqlInjection(script))
            errors.Add("Script contains SQL injection patterns");
            
        // Check for command injection
        if (ContainsCommandInjection(script))
            warnings.Add("Script contains potentially dangerous command patterns");
            
        // Check for XSS (scripts shouldn't contain HTML)
        if (ContainsXss(script))
            errors.Add("Script contains HTML/JavaScript injection patterns");
            
        // IdiotScript-specific validation
        // Only allow known IdiotScript commands
        var allowedPatterns = new[]
        {
            @"Ticker\s*\([^)]*\)",
            @"Name\s*\([^)]*\)",
            @"Session\s*\([^)]*\)",
            @"Quantity\s*\([^)]*\)",
            @"Entry\s*\([^)]*\)",
            @"Breakout\s*\([^)]*\)",
            @"Pullback\s*\([^)]*\)",
            @"Order\s*\([^)]*\)",
            @"Long\s*\(\s*\)",
            @"Short\s*\(\s*\)",
            @"TakeProfit\s*\([^)]*\)",
            @"StopLoss\s*\([^)]*\)",
            @"TrailingStopLoss\s*\([^)]*\)",
            @"ExitStrategy\s*\([^)]*\)",
            @"Is\w+\s*\([^)]*\)",        // All Is... conditions
            @"AdaptiveOrder\s*\([^)]*\)",
            @"AutonomousTrading\s*\([^)]*\)",
            @"Repeat\s*\(\s*\)",
            @"IsProfitable\s*\(\s*\)",
            @"OutsideRTH\s*\(\s*\)",
            @"TakeProfitOutsideRTH\s*\(\s*\)",
            @"PriceType\s*\([^)]*\)",
            @"OrderType\s*\([^)]*\)",
            @"Build\s*\(\s*\)",
            @"IS\.\w+",                   // IS.LONG, IS.RTH, etc.
            @"Price\.\w+",                // Price.Current, etc.
            @"\d+(\.\d+)?",               // Numbers
            @"\.",                        // Method chaining
        };
        
        // Remove all allowed patterns and check what remains
        var remaining = script;
        foreach (var pattern in allowedPatterns)
        {
            remaining = Regex.Replace(remaining, pattern, " ", RegexOptions.IgnoreCase);
        }
        
        remaining = Regex.Replace(remaining, @"\s+", " ").Trim();
        
        if (!string.IsNullOrEmpty(remaining))
        {
            warnings.Add($"Script contains unrecognized content: {remaining.Substring(0, Math.Min(50, remaining.Length))}");
        }
        
        if (errors.Count > 0)
            return new ValidationResult { IsValid = false, Errors = errors, Warnings = warnings };
            
        return new ValidationResult { IsValid = true, SanitizedValue = script, Warnings = warnings };
    }
    
    /// <summary>
    /// Validates numeric input within bounds.
    /// </summary>
    public static ValidationResult ValidateNumber(string? input, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failure("Number cannot be empty");
            
        if (!double.TryParse(input.Trim(), out var value))
            return ValidationResult.Failure("Invalid number format");
            
        if (value < min || value > max)
            return ValidationResult.Failure($"Number must be between {min} and {max}");
            
        return ValidationResult.Success(value.ToString());
    }
    
    /// <summary>
    /// Validates quantity (positive integer).
    /// </summary>
    public static ValidationResult ValidateQuantity(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failure("Quantity cannot be empty");
            
        if (!int.TryParse(input.Trim(), out var quantity))
            return ValidationResult.Failure("Quantity must be a whole number");
            
        if (quantity <= 0)
            return ValidationResult.Failure("Quantity must be positive");
            
        if (quantity > 1_000_000)
            return ValidationResult.Failure("Quantity exceeds maximum (1,000,000)");
            
        return ValidationResult.Success(quantity.ToString());
    }
    
    /// <summary>
    /// Validates price (positive decimal with reasonable range).
    /// </summary>
    public static ValidationResult ValidatePrice(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Failure("Price cannot be empty");
            
        if (!decimal.TryParse(input.Trim(), out var price))
            return ValidationResult.Failure("Invalid price format");
            
        if (price <= 0)
            return ValidationResult.Failure("Price must be positive");
            
        if (price > 1_000_000)
            return ValidationResult.Failure("Price exceeds maximum ($1,000,000)");
            
        return ValidationResult.Success(price.ToString("F4"));
    }
    
    /// <summary>
    /// Sanitizes a generic string by removing dangerous characters.
    /// </summary>
    public static string SanitizeString(string? input, int maxLength = MaxGenericStringLength)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
            
        var result = input.Trim();
        
        // Truncate
        if (result.Length > maxLength)
            result = result.Substring(0, maxLength);
            
        // Remove control characters
        result = Regex.Replace(result, @"[\x00-\x1F\x7F]", "");
        
        // HTML encode for safe display
        result = HtmlEncode(result);
        
        return result;
    }
    
    /// <summary>
    /// HTML encodes a string for safe display.
    /// </summary>
    public static string HtmlEncode(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
            
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }
    
    /// <summary>
    /// Checks if input contains SQL injection patterns.
    /// </summary>
    public static bool ContainsSqlInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;
            
        foreach (var pattern in SqlInjectionPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }
    
    /// <summary>
    /// Checks if input contains XSS patterns.
    /// </summary>
    public static bool ContainsXss(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;
            
        foreach (var pattern in XssPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }
    
    /// <summary>
    /// Checks if input contains command injection patterns.
    /// </summary>
    public static bool ContainsCommandInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;
            
        foreach (var pattern in CommandInjectionPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Extension methods for fluent sanitization.
/// </summary>
public static class SanitizationExtensions
{
    public static string AsTicker(this string? input) =>
        InputSanitizer.ValidateTicker(input) is { IsValid: true } result 
            ? result.SanitizedValue 
            : throw new ArgumentException("Invalid ticker");
            
    public static string AsClientId(this string? input) =>
        InputSanitizer.ValidateClientId(input) is { IsValid: true } result 
            ? result.SanitizedValue 
            : throw new ArgumentException("Invalid client ID");
            
    public static string AsSafeString(this string? input, int maxLength = 1000) =>
        InputSanitizer.SanitizeString(input, maxLength);
}

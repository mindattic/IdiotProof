// ============================================================================
// Percent - Percentage Value Definitions
// ============================================================================

namespace IdiotProof.Core.Helpers;

/// <summary>
/// Common percentage values for trailing stop losses and other calculations.
/// All values are expressed as decimals (e.g., 0.10 for 10%).
/// </summary>
public static class Percent
{
    /// <summary>1% (0.01)</summary>
    public const double One = 0.01;

    /// <summary>2% (0.02)</summary>
    public const double Two = 0.02;

    /// <summary>3% (0.03)</summary>
    public const double Three = 0.03;

    /// <summary>4% (0.04)</summary>
    public const double Four = 0.04;

    /// <summary>5% (0.05)</summary>
    public const double Five = 0.05;

    /// <summary>6% (0.06)</summary>
    public const double Six = 0.06;

    /// <summary>7% (0.07)</summary>
    public const double Seven = 0.07;

    /// <summary>8% (0.08)</summary>
    public const double Eight = 0.08;

    /// <summary>9% (0.09)</summary>
    public const double Nine = 0.09;

    /// <summary>10% (0.10)</summary>
    public const double Ten = 0.10;

    /// <summary>15% (0.15)</summary>
    public const double Fifteen = 0.15;

    /// <summary>20% (0.20)</summary>
    public const double Twenty = 0.20;

    /// <summary>25% (0.25)</summary>
    public const double TwentyFive = 0.25;

    /// <summary>30% (0.30)</summary>
    public const double Thirty = 0.30;

    /// <summary>50% (0.50)</summary>
    public const double Fifty = 0.50;

    /// <summary>
    /// Creates a custom percentage value.
    /// </summary>
    /// <param name="value">The percentage as a whole number (e.g., 12 for 12%).</param>
    /// <returns>The percentage as a decimal (e.g., 0.12 for 12%).</returns>
    public static double Custom(double value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Percentage must be between 0 and 100.");
        
        return value / 100.0;
    }
}



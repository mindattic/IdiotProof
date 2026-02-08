// ============================================================================
// OrderDirection - Specifies the direction of an order (Long or Short)
// ============================================================================

namespace IdiotProof.Enums;

/// <summary>
/// Specifies the direction of a trading order.
/// </summary>
public enum OrderDirection
{
    /// <summary>
    /// Long position - buy to open.
    /// Profits when price goes up.
    /// </summary>
    Long,

    /// <summary>
    /// Short position - sell to open.
    /// Profits when price goes down.
    /// </summary>
    Short
}



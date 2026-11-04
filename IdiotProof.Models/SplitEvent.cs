namespace IdiotProof.Models;

/// <summary>
/// A stock split effective at the open of <see cref="ExDate"/>: every <see cref="OldRate"/> shares
/// became <see cref="NewRate"/> shares (a 1-for-10 reverse split is OldRate 10, NewRate 1).
/// </summary>
public sealed record SplitEvent(string Symbol, DateOnly ExDate, decimal OldRate, decimal NewRate)
{
    /// <summary>Multiplier that puts a pre-split price on the post-split basis.</summary>
    public decimal PriceFactor => NewRate == 0 ? 1m : OldRate / NewRate;
}

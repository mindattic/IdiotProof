using System.Security.Cryptography;
using System.Text;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// The deterministic control subset: a symbol-day is a control when the SHA-256 of
/// <c>{symbol}|{date}|{salt}</c> (the study's hash input shape) lands on 0 modulo <c>oneIn</c>, so
/// the same names are controls on every host and every restart.
/// </summary>
public static class FormationControlSample
{
    public const string Salt = "formation-control";

    public static bool IsControl(string symbol, DateOnly dateEt, int oneIn)
    {
        if (oneIn <= 1) return true;
        var input = $"{symbol.Trim().ToUpperInvariant()}|{dateEt:yyyy-MM-dd}|{Salt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bucket = BitConverter.ToUInt64(hash, 0) % (ulong)oneIn;
        return bucket == 0;
    }
}

using System.Text;
using IdiotProof.Models;

namespace IdiotProof.Alerts;

/// <summary>
/// Discord/email rendering for the canonical <see cref="TradeSetup"/>. Kept here
/// (not on the model) so the canonical type stays presentation-free.
/// </summary>
internal static class TradeSetupFormatter
{
    public static string DirectionLabel(this TradeSetup s) => s.IsLong ? "LONG" : "SHORT";

    public static string DirectionEmoji(this TradeSetup s) => s.IsLong ? "📈" : "📉";

    public static string ToDiscordEmbed(this TradeSetup s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{s.DirectionEmoji()} **{s.DirectionLabel()} Setup** (ID: `{s.SetupId}`)");
        sb.AppendLine("```");
        sb.AppendLine($"Entry:   ${s.EntryPrice:F2}");
        var stopPct = s.EntryPrice > 0m
            ? Math.Abs((s.StopLoss - s.EntryPrice) / s.EntryPrice * 100m) : 0m;
        var tpPct = s.EntryPrice > 0m
            ? Math.Abs((s.TakeProfit - s.EntryPrice) / s.EntryPrice * 100m) : 0m;
        sb.AppendLine($"Stop:    ${s.StopLoss:F2} ({(s.IsLong ? "-" : "+")}{stopPct:F1}%)");
        sb.AppendLine($"Target:  ${s.TakeProfit:F2} ({(s.IsLong ? "+" : "-")}{tpPct:F1}%)");
        sb.AppendLine($"Trail:   {s.TrailingStopPercent:F1}%");
        sb.AppendLine($"Qty:     {s.Quantity} shares");
        sb.AppendLine($"Risk:    ${s.RiskDollars:F2}");
        sb.AppendLine($"Reward:  ${s.RewardDollars:F2}");
        sb.AppendLine($"R:R:     {s.RiskRewardRatio:F1}:1");
        sb.AppendLine("```");
        return sb.ToString();
    }
}

namespace IdiotProof.Blazor.Data;

public sealed class LiveBar
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StrategyId { get; set; }
    public string DateEt { get; set; } = "";    // "2026-07-21"
    public string Et { get; set; } = "";         // "09:35"
    public int Min { get; set; }                 // 575 = 9*60+35
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public double Vwap { get; set; }
    public double WindowHigh { get; set; }
    public double Volx { get; set; }
    public bool InSession { get; set; }
    public string CondBitsJson { get; set; } = "[]";  // bool[] — all entry conditions
    public bool Fire { get; set; }
    public bool Exit { get; set; }
    public DateTime WrittenUtc { get; set; }
}

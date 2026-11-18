namespace IdiotProof.Strategies.Tests;

/// <summary>The scripts <see cref="ScriptDiagnosticCodesTests"/> analyzes (IP-T526); kept free of the test framework so they can be run from anywhere.</summary>
public static class ScriptDiagnosticCorpus
{
    /// <summary>Scripts that between them make the analyzer report every code, and a fix for every code that can carry one.</summary>
    public static readonly string[] Scripts =
    [
        "Ticker(\"X\").IsAbveVwap().Long()",                                              // IPS001, fix
        "Ticker(\"X\").Zzzzqqqq().Long()",                                                // IPS001, no close verb
        "Ticker(\"X\")\n.IsAboveEma(9\n.Long()",                                          // IPS002, fix
        "Ticker(\"X\").Long())",                                                          // IPS003, fix
        "Ticker(\"X\").Name(\"abc).Long()",                                               // IPS004, fix
        ".IsAboveVwap().Long()",                                                          // IPS005
        "Ticker(\"X\").StopLoss(\"2\").Long()",                                           // IPS006, fix
        "Ticker(\"X\").IsAboveEma(nine).Long()",                                          // IPS006, no fix
        "Ticker(\"X\").IsBetweenEma(9).Long()",                                           // IPS007
        "Ticker(\"X\").IsAboveVwap(3).StopLoss(1,2).Long()",                              // IPS010 fix, IPS008 fix
        "Ticker(\"X\").IsAboveEma(9.5).Long()",                                           // IPS009
        "Ticker(\"X\")\n.IsAboveVwap()\n  .Else(StopLoss(2))\n.If(IsBelowVwap()).Then(StopLoss(1)).EndIf()", // IPS011
        "Ticker(\"X\")\n.If(IsAboveVwap())\n.Then(StopLoss(1))\n.Else(StopLoss(2))\n.ElseIf(IsBelowVwap(), StopLoss(3))\n.EndIf()", // IPS012
        "Ticker(\"X\")\n.If(IsAboveVwap())\n.Then(StopLoss(1))\n.Long()",                 // IPS013, fix
        "Ticker(\"X\")\n.If(IsAboveVwap())\n.Long()\n.EndIf()",                           // IPS014
        "Ticker(\"X\").If(IsAboveVwap()).Then(StopLoss(1)).Then(StopLoss(2)).EndIf()",    // IPS015
        "Ticker(\"X\")\n.If(IsAboveVwap()).Then(StopLoss(1))\n.ElseIf(IsBelowVwap())\n.EndIf()", // IPS016
        "Ticker(\"X\").Step(\"a\").IsAboveVwap().Long().StopLoss(1).ThenWhen().Step(\"b\").Long().StopLoss(1)", // IPS020
        "Ticker(\"X\").Step(\"a\").IsAboveVwap().Long().StopLoss(1).ThenWhen(IsBelowVwap()).If(IsAboveVwap()).Step(\"b\").Long().StopLoss(1)", // IPS021
        "Ticker(\"X\").Step(\"a\").IsAboveVwap().Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"b\").Long().StopLoss(1).Repeat()", // IPS022
        "Ticker(\"X\").Step(\"a\").IsAboveVwap().Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"a\").Long().StopLoss(1)", // IPS023
        "Ticker(\"X\").Step(\"a\").IsAboveVwap().Long().StopLoss(1).ThenWhen(IsBelowVwap()).Step(\"b\").StopLoss(1)", // IPS024
        "Ticker(\"X\").Step(\"a\").IsAboveVwap().Long().StopLoss(1).ThenWhen(IsBelowVwap())", // IPS025
        Ride + ".OnStop(\"nope\").Step(\"b\")" + Leg + ".OnTarget(\"ride\")",          // IPS026, IPS027
        Ride + ".Step(\"end\")" + Leg,                                                  // IPS026 (a step named "end")
        Ride + ".OnExit(\"c\").Step(\"b\")" + Leg + ".Step(\"c\")" + Leg,                 // IPS028
    ];

    private const string Ride =
        "Ticker(\"GAPT\").Step(\"ride\").Session(IS.RTH).Quantity(1).IsAboveVwap().Long().StopLossPercent(5).TakeProfitPercent(10).SellBy(\"11:00\")";

    private const string Leg = ".IsAboveVwap().Long().StopLossPercent(4)";
}

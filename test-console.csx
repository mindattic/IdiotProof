using static IdiotProof.UI.ConsoleChars;

Console.WriteLine();
Console.WriteLine(Title.Banner("Console Characters Demo", 50)[0]);
Console.WriteLine(Title.Banner("Console Characters Demo", 50)[1]);
Console.WriteLine(Title.Banner("Console Characters Demo", 50)[2]);
Console.WriteLine();

Console.WriteLine(Title.Section("Box Drawing", 40));
var box = BoxBuilder.CreateBox(new[] { "Line 1", "Line 2 is longer", "Line 3" });
foreach (var line in box) Console.WriteLine(line);
Console.WriteLine();

Console.WriteLine(Title.Section("Status Badges", 40));
Console.WriteLine($"  Success: {Badge.OK}  Error: {Badge.ERR}  Warning: {Badge.WARN}");
Console.WriteLine($"  Trading: {Badge.LONG}  {Badge.SHORT}  {Badge.BUY}  {Badge.SELL}");
Console.WriteLine($"  Status:  {Badge.CONNECTED}  {Badge.PAPER}");
Console.WriteLine();

Console.WriteLine(Title.Section("Symbols", 40));
Console.WriteLine($"  Success={Symbol.Success} Error={Symbol.Error} Warning={Symbol.Warning}");
Console.WriteLine($"  Enabled={Symbol.Enabled} Disabled={Symbol.Disabled}");
Console.WriteLine($"  Long={Symbol.Long} Short={Symbol.Short} Buy={Symbol.Buy} Sell={Symbol.Sell}");
Console.WriteLine();

Console.WriteLine(Title.Section("Progress Bars", 40));
Console.WriteLine($"  0%:   {Progress.Bar(0, 25)}");
Console.WriteLine($"  50%:  {Progress.Bar(50, 25)}");
Console.WriteLine($"  100%: {Progress.Bar(100, 25)}");
Console.WriteLine();

Console.WriteLine(Title.Section("Arrows", 40));
Console.WriteLine($"  Direction: {Arrow.Up} {Arrow.Down} {Arrow.Left} {Arrow.Right}");
Console.WriteLine($"  Flow:      {Arrow.LongRight} {Arrow.FlowRight} {Arrow.BidiArrow}");
Console.WriteLine();

Console.WriteLine(Title.Section("Titled Box", 40));
var titledBox = BoxBuilder.CreateTitledBox("Settings", new[] { "Option 1: Enabled", "Option 2: Disabled", "Option 3: Auto" });
foreach (var line in titledBox) Console.WriteLine(line);
Console.WriteLine();

Console.WriteLine(Title.Section("Table", 40));
var table = Table.Create(new[] { "Symbol", "Action", "Price" }, new[] { 
    new[] { "NVDA", "BUY", "$450.00" },
    new[] { "AAPL", "SELL", "$175.50" },
    new[] { "TSLA", "HOLD", "$190.25" }
});
foreach (var line in table) Console.WriteLine(line);

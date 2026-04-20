using IdiotProof.Brokers;
using IdiotProof.DataFeeds;
using IdiotProof.Engine;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using IdiotProof.Models;
using IdiotProof.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

// ── DI Setup ─────────────────────────────────────────────────────────────────

var storage = new WebStorageProvider(Path.Combine(AppContext.BaseDirectory, "AppData"));
var services = new ServiceCollection();
services.AddIdiotProofEngine(storage);

var serviceProvider = services.BuildServiceProvider();

// ── Spectre CLI ───────────────────────────────────────────────────────────────

var app = new CommandApp(new TypeRegistrar(serviceProvider));
app.Configure(config =>
{
    config.SetApplicationName("idiotproof");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<SignalCommand>("signal")
        .WithDescription("Evaluate strategies for a ticker and display signals.");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show workspace summary and current positions.");

    config.AddCommand<PositionsCommand>("positions")
        .WithDescription("Fetch and display open positions from broker.");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Start strategy evaluation loop with live display.");

    config.AddCommand<WorkspacesCommand>("workspaces")
        .WithDescription("List all workspaces with their configuration.");
});

return await app.RunAsync(args);

// ── DI Infrastructure ─────────────────────────────────────────────────────────

internal sealed class TypeRegistrar(IServiceProvider provider) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(provider);
    public void Register(Type service, Type implementation) { }
    public void RegisterInstance(Type service, object implementation) { }
    public void RegisterLazy(Type service, Func<object> factory) { }
}

internal sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        return provider.GetService(type) ?? ActivatorUtilities.CreateInstance(provider, type);
    }
}

// ── signal command ────────────────────────────────────────────────────────────

internal sealed class SignalSettings : CommandSettings
{
    [CommandArgument(0, "<TICKER>")]
    [Description("The stock ticker symbol to evaluate.")]
    public string Ticker { get; set; } = string.Empty;

    [CommandOption("--strategy|-s")]
    [Description("Name of a specific strategy to use (default: all).")]
    public string? Strategy { get; set; }

    [CommandOption("--candles|-n")]
    [Description("Number of candles to fetch (default: 60).")]
    [DefaultValue(60)]
    public int Candles { get; set; } = 60;
}

internal sealed class SignalCommand(StrategyRegistry registry, SwitchableMarketDataFeed dataFeed, AppSettings settings) : Command<SignalSettings>
{
    public override int Execute(CommandContext context, SignalSettings args)
    {
        var symbol = args.Ticker.ToUpperInvariant();
        var candleCount = Math.Max(10, args.Candles);

        AnsiConsole.MarkupLine($"[bold cyan]Fetching {candleCount} candles for [yellow]{symbol}[/]...[/]");

        List<Candle> candles = [];

        try
        {
            var endUtc = DateTime.UtcNow;
            var startUtc = endUtc.AddMinutes(-candleCount * 5); // 5-min candles as default
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var asyncEnum = dataFeed.GetHistoricalCandlesAsync(symbol, startUtc, endUtc, TimeSpan.FromMinutes(5), cts.Token);
            var task = Task.Run(async () =>
            {
                await foreach (var c in asyncEnum) candles.Add(c);
            });
            task.Wait(cts.Token);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Data feed error: {ex.Message}[/]");
            // Fall through with empty candles; show "no signals"
        }

        if (candles.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No candle data returned. Cannot evaluate signals.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[dim]Retrieved {candles.Count} candles.[/]");

        var stratCtx = new StrategyContext
        {
            Timezone = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone),
            EvaluationTimeUtc = DateTime.UtcNow
        };

        IReadOnlyList<IStrategy> strategies = args.Strategy is not null
            ? registry.Get(args.Strategy) is { } s ? [s] : []
            : registry.GetAll();

        if (strategies.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No strategy found named '{args.Strategy}'.[/]");
            return 1;
        }

        var allSignals = new List<(TradeSignal signal, string stratName)>();
        foreach (var strat in strategies)
        {
            try
            {
                var signals = strat.Evaluate(symbol, candles, stratCtx);
                foreach (var sig in signals)
                    allSignals.Add((sig, strat.Name));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error in {strat.Name}: {ex.Message}[/]");
            }
        }

        if (allSignals.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No signals found.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Signals for [cyan]{symbol}[/][/]")
            .AddColumn("[bold]Direction[/]")
            .AddColumn("[bold]Confidence[/]")
            .AddColumn("[bold]Entry[/]")
            .AddColumn("[bold]Stop[/]")
            .AddColumn("[bold]Target[/]")
            .AddColumn("[bold]Strategy[/]")
            .AddColumn("[bold]Reason[/]");

        foreach (var (sig, _) in allSignals.OrderByDescending(x => x.signal.ConfidencePercent))
        {
            var dirColor = sig.Direction == TradeDirection.Long ? "green" : "red";
            var dirText = sig.Direction == TradeDirection.Long ? "LONG" : "SHORT";
            var target = sig.Targets.Count > 0 ? sig.Targets[0].ToString("F2") : "-";

            table.AddRow(
                $"[{dirColor}]{dirText}[/]",
                $"[bold]{sig.ConfidencePercent:F0}%[/]",
                sig.SuggestedEntry.ToString("F2"),
                sig.SuggestedStop.ToString("F2"),
                target,
                sig.StrategyName,
                sig.Reason.Length > 60 ? sig.Reason[..60] + "…" : sig.Reason
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ── status command ────────────────────────────────────────────────────────────

internal sealed class StatusCommand(WorkspaceManager workspaceManager, BrokerRouter brokerRouter, AppSettings settings, AuditLogger auditLogger) : Command<EmptyCommandSettings>
{
    public override int Execute(CommandContext context, EmptyCommandSettings args)
    {
        AnsiConsole.Write(new Rule("[bold cyan]IdiotProof Status[/]").LeftJustified());

        // Workspace summary
        var wsTable = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Workspaces[/]")
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Tickers[/]")
            .AddColumn("[bold]Strategies[/]")
            .AddColumn("[bold]AutoTrade[/]")
            .AddColumn("[bold]Broker[/]");

        foreach (var ws in workspaceManager.Tabs)
        {
            var tickers = ws.Watchlist.Count > 0 ? string.Join(", ", ws.Watchlist) : "[dim]none[/]";
            var strats = ws.Strategies.Count > 0
                ? string.Join(", ", ws.Strategies.Where(s => s.Enabled).Select(s => s.StrategyName))
                : "[dim]none[/]";
            var autoTrade = ws.Settings.AutoTrade ? "[green]Yes[/]" : "[red]No[/]";
            var broker = ws.BrokerOverride ?? settings.DefaultBroker;

            wsTable.AddRow(ws.Name, tickers, strats, autoTrade, broker);
        }
        AnsiConsole.Write(wsTable);

        // Positions
        AnsiConsole.WriteLine();
        var defaultBrokerType = Enum.TryParse<BrokerType>(settings.DefaultBroker, ignoreCase: true, out var bt) ? bt : BrokerType.Sandbox;
        try
        {
            var broker = brokerRouter.GetBroker(defaultBrokerType);
            if (broker.IsConnected)
            {
                var positions = broker.GetPositionsAsync().GetAwaiter().GetResult();
                if (positions.Count > 0)
                {
                    var posTable = BuildPositionsTable(positions);
                    AnsiConsole.Write(posTable);
                }
                else
                {
                    AnsiConsole.MarkupLine("[dim]No open positions.[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[dim]Broker ({defaultBrokerType}) not connected.[/]");
            }
        }
        catch
        {
            AnsiConsole.MarkupLine("[dim]Could not fetch positions.[/]");
        }

        // Audit log tail
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Recent Audit Log[/]").LeftJustified());
        var recent = auditLogger.GetRecent(10);
        if (recent.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No audit log entries.[/]");
        }
        else
        {
            foreach (var line in recent)
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(line)}[/]");
        }

        return 0;
    }

    private static Table BuildPositionsTable(IReadOnlyList<Position> positions)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Current Positions[/]")
            .AddColumn("[bold]Symbol[/]")
            .AddColumn("[bold]Qty[/]")
            .AddColumn("[bold]Avg Price[/]")
            .AddColumn("[bold]Market Value[/]")
            .AddColumn("[bold]P&L[/]")
            .AddColumn("[bold]% P&L[/]");

        foreach (var pos in positions)
        {
            var pnlColor = pos.UnrealizedPnl >= 0 ? "green" : "red";
            var pnlPct = pos.AveragePrice > 0 && pos.Quantity > 0
                ? pos.UnrealizedPnl / (pos.AveragePrice * pos.Quantity) * 100m
                : 0m;

            table.AddRow(
                $"[bold]{pos.Symbol}[/]",
                pos.Quantity.ToString(),
                $"${pos.AveragePrice:F2}",
                $"${pos.MarketValue:F2}",
                $"[{pnlColor}]{(pos.UnrealizedPnl >= 0 ? "+" : "")}{pos.UnrealizedPnl:F2}[/]",
                $"[{pnlColor}]{(pnlPct >= 0 ? "+" : "")}{pnlPct:F2}%[/]"
            );
        }

        return table;
    }
}

// ── positions command ─────────────────────────────────────────────────────────

internal sealed class PositionsSettings : CommandSettings
{
    [CommandOption("--broker|-b")]
    [Description("Broker name (Ibkr, Alpaca, Sandbox). Default: uses app setting.")]
    public string? Broker { get; set; }
}

internal sealed class PositionsCommand(BrokerRouter brokerRouter, AppSettings settings) : Command<PositionsSettings>
{
    public override int Execute(CommandContext context, PositionsSettings args)
    {
        var brokerName = args.Broker ?? settings.DefaultBroker;
        IBrokerClient broker;

        try
        {
            broker = brokerRouter.GetBroker(brokerName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold]Fetching positions from [cyan]{brokerName}[/]...[/]");

        IReadOnlyList<Position> positions;
        try
        {
            positions = broker.GetPositionsAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to fetch positions: {ex.Message}[/]");
            return 1;
        }

        if (positions.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No open positions.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Open Positions — [cyan]{brokerName}[/][/]")
            .AddColumn("[bold]Symbol[/]")
            .AddColumn("[bold]Qty[/]")
            .AddColumn("[bold]Avg Price[/]")
            .AddColumn("[bold]Market Value[/]")
            .AddColumn("[bold]P&L[/]")
            .AddColumn("[bold]% P&L[/]");

        foreach (var pos in positions)
        {
            var pnlColor = pos.UnrealizedPnl >= 0 ? "green" : "red";
            var pnlPct = pos.AveragePrice > 0 && pos.Quantity > 0
                ? pos.UnrealizedPnl / (pos.AveragePrice * pos.Quantity) * 100m
                : 0m;

            table.AddRow(
                $"[bold]{pos.Symbol}[/]",
                pos.Quantity.ToString(),
                $"${pos.AveragePrice:F2}",
                $"${pos.MarketValue:F2}",
                $"[{pnlColor}]{(pos.UnrealizedPnl >= 0 ? "+" : "")}{pos.UnrealizedPnl:F2}[/]",
                $"[{pnlColor}]{(pnlPct >= 0 ? "+" : "")}{pnlPct:F2}%[/]"
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ── run command ───────────────────────────────────────────────────────────────

internal sealed class RunSettings : CommandSettings
{
    [CommandOption("--interval|-i")]
    [Description("Evaluation interval in seconds (default: 30).")]
    [DefaultValue(30)]
    public int Interval { get; set; } = 30;
}

internal sealed class RunCommand(
    StrategyRegistry registry,
    WorkspaceManager workspaceManager,
    SwitchableMarketDataFeed dataFeed,
    AppSettings settings) : Command<RunSettings>
{
    private record TickerState(string Symbol, decimal LastPrice, int SignalCount, DateTime LastEvalUtc, string Status);

    public override int Execute(CommandContext context, RunSettings args)
    {
        var interval = Math.Max(5, args.Interval);

        AnsiConsole.MarkupLine("[bold cyan]Starting strategy evaluation loop. Press [yellow]Ctrl+C[/] to stop.[/]");
        AnsiConsole.MarkupLine($"[dim]Interval: {interval}s[/]");

        var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);

        // Gather all tickers from all workspaces
        var tickers = workspaceManager.Tabs
            .SelectMany(t => t.Watchlist)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tickers.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tickers in any workspace. Add tickers to a workspace first.[/]");
            return 0;
        }

        var states = tickers.ToDictionary(
            t => t,
            t => new TickerState(t, 0m, 0, DateTime.UtcNow, "Pending"),
            StringComparer.OrdinalIgnoreCase);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        AnsiConsole.Live(BuildTable(states, tz))
            .AutoClear(false)
            .Start(liveCtx =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    foreach (var ticker in tickers)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        try
                        {
                            var endUtc = DateTime.UtcNow;
                            var startUtc = endUtc.AddMinutes(-60 * 5); // ~60 5-min candles
                            var candles = new List<Candle>();

                            var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                            fetchCts.CancelAfter(TimeSpan.FromSeconds(15));

                            var asyncEnum = dataFeed.GetHistoricalCandlesAsync(ticker, startUtc, endUtc, TimeSpan.FromMinutes(5), fetchCts.Token);
                            Task.Run(async () =>
                            {
                                await foreach (var c in asyncEnum) candles.Add(c);
                            }).Wait(fetchCts.Token);

                            var lastPrice = candles.Count > 0 ? candles[^1].Close : 0m;

                            var stratCtx = new StrategyContext
                            {
                                Timezone = tz,
                                EvaluationTimeUtc = DateTime.UtcNow
                            };

                            var allSignals = new List<TradeSignal>();
                            foreach (var strat in registry.GetAll())
                            {
                                try
                                {
                                    var sigs = strat.Evaluate(ticker, candles, stratCtx);
                                    allSignals.AddRange(sigs);
                                }
                                catch { /* skip failed strategy */ }
                            }

                            states[ticker] = new TickerState(ticker, lastPrice, allSignals.Count, DateTime.UtcNow,
                                allSignals.Count > 0 ? $"[green]{allSignals.Count} signal(s)[/]" : "[dim]No signals[/]");
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            states[ticker] = new TickerState(ticker, 0m, 0, DateTime.UtcNow, $"[red]Error: {ex.Message[..Math.Min(ex.Message.Length, 30)]}[/]");
                        }

                        liveCtx.UpdateTarget(BuildTable(states, tz));
                    }

                    // Wait for next interval
                    try { Task.Delay(TimeSpan.FromSeconds(interval), cts.Token).Wait(); }
                    catch (OperationCanceledException) { break; }
                    catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { break; }
                }
            });

        AnsiConsole.MarkupLine("[bold cyan]Evaluation loop stopped.[/]");
        return 0;
    }

    private static Table BuildTable(Dictionary<string, TickerState> states, TimeZoneInfo tz)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold cyan]Strategy Evaluation Loop[/]")
            .AddColumn("[bold]Symbol[/]")
            .AddColumn("[bold]Last Price[/]")
            .AddColumn("[bold]Signals[/]")
            .AddColumn("[bold]Last Eval[/]")
            .AddColumn("[bold]Status[/]");

        foreach (var kvp in states)
        {
            var s = kvp.Value;
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(s.LastEvalUtc, tz).ToString("HH:mm:ss");
            var priceStr = s.LastPrice > 0 ? $"${s.LastPrice:F2}" : "[dim]—[/]";

            table.AddRow(
                $"[bold]{s.Symbol}[/]",
                priceStr,
                s.SignalCount > 0 ? $"[green]{s.SignalCount}[/]" : "[dim]0[/]",
                $"[dim]{localTime}[/]",
                s.Status
            );
        }

        return table;
    }
}

// ── workspaces command ────────────────────────────────────────────────────────

internal sealed class WorkspacesCommand(WorkspaceManager workspaceManager, AppSettings settings) : Command<EmptyCommandSettings>
{
    public override int Execute(CommandContext context, EmptyCommandSettings args)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Workspaces[/]")
            .AddColumn("[bold]ID[/]")
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Tickers[/]")
            .AddColumn("[bold]Strategies[/]")
            .AddColumn("[bold]AutoTrade[/]")
            .AddColumn("[bold]Broker[/]")
            .AddColumn("[bold]Data Feed[/]")
            .AddColumn("[bold]Created[/]");

        foreach (var ws in workspaceManager.Tabs)
        {
            var tickers = ws.Watchlist.Count > 0 ? string.Join(", ", ws.Watchlist) : "[dim]none[/]";
            var strats = ws.Strategies.Count > 0
                ? string.Join(", ", ws.Strategies.Select(s => s.Enabled ? s.StrategyName : $"[dim]{s.StrategyName}[/]"))
                : "[dim]none[/]";
            var autoTrade = ws.Settings.AutoTrade ? "[green]Yes[/]" : "[red]No[/]";
            var broker = ws.BrokerOverride ?? $"[dim]{settings.DefaultBroker}[/]";
            var feed = ws.DataFeedOverride ?? $"[dim]{settings.DefaultDataFeed}[/]";
            var created = ws.CreatedUtc.ToString("yyyy-MM-dd");

            table.AddRow(ws.TabId, ws.Name, tickers, strats, autoTrade, broker, feed, created);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]Total: {workspaceManager.Tabs.Count} workspace(s)[/]");
        return 0;
    }
}

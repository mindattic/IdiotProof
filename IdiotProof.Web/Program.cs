using IdiotProof.Web.Components;
using IdiotProof.Web.Hubs;
using IdiotProof.Web.Services;
using IdiotProof.Web.Services.AI;
using IdiotProof.Web.Services.MarketScanner;
using IdiotProof.Web.Services.MarketScanner.Sources;
using IdiotProof.Web.Services.TradingView;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Controllers for API endpoints
builder.Services.AddControllers();

// Add SignalR
builder.Services.AddSignalR();

// Add HTTP client factory for scrapers
builder.Services.AddHttpClient();

// Register Market Scanner services
builder.Services.AddSingleton<GapperAggregator>();
builder.Services.AddSingleton<MarketScannerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketScannerService>());
builder.Services.AddSingleton<TradingHubNotifier>();

// Register gapper sources (scrapers)
builder.Services.AddTransient<IGapperSource, StockAnalysisSource>();
builder.Services.AddTransient<IGapperSource, BarchartSource>();
builder.Services.AddTransient<IGapperSource, FinvizSource>();
builder.Services.AddTransient<IGapperSource, TradingViewSource>();

// Register TradingView chart services
builder.Services.AddSingleton<ChartDataService>();

// Register AI Advisor
var aiConfig = new AiAdvisorConfig
{
    ApiKey = builder.Configuration["OpenAI:ApiKey"] ?? "",
    Model = builder.Configuration["OpenAI:Model"] ?? "gpt-4o"
};
builder.Services.AddSingleton(aiConfig);
builder.Services.AddScoped<AiAdvisorService>();

// Register Trade Execution service
builder.Services.AddScoped<TradeExecutionService>();

// Register Historical Data Provider for backtesting
builder.Services.AddSingleton<HistoricalDataProvider>();

// Register Market Data Broadcaster for live updates
builder.Services.AddSingleton<MarketDataBroadcaster>();

// Register IBKR Web Bridge (receives data from Core, broadcasts to clients)
builder.Services.AddSingleton<IbkrWebBridge>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IbkrWebBridge>());

var app = builder.Build();

// Initialize TradingHubNotifier (to subscribe to events)
_ = app.Services.GetRequiredService<TradingHubNotifier>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();

// Map API controllers
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<TradingHub>("/hubs/trading");
app.MapHub<MarketDataHub>("/hubs/marketdata");

app.Run();

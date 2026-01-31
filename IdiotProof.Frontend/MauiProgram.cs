using IdiotProof.Frontend.Services;

namespace IdiotProof.Frontend
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Add Blazor WebView
            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
#endif

            // Register services
            builder.Services.AddSingleton<IStrategyService, StrategyService>();
            builder.Services.AddSingleton<IBackendService, BackendService>();

            return builder.Build();
        }
    }
}

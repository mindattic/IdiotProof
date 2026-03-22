using IdiotProof.Engine;
using IdiotProof.Engine.Storage;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Desktop;

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
            });

        builder.Services.AddMauiBlazorWebView();

        // Register IdiotProof engine with desktop storage (%LOCALAPPDATA%\MindAttic)
        var storage = new DesktopStorageProvider();
        builder.Services.AddIdiotProofEngine(storage);

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

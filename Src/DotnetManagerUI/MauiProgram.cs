using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Embedding;

namespace DotnetManagerUI
{
    public static partial class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder.UseMauiEmbeddedApp<MyApp>()
                   .ConfigureFonts(fonts =>
                   {
                       fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                       fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemiBold");
                   });

            builder.Services.AddMauiBlazorWebView();

            // The BlazorWebView resolves every view model and service from this container, so the
            // application services are registered here rather than in a separate host.
            Startup.ConfigureServices(builder.Services);

#if DEBUG
            // Caution: Recommended to enable Developer Tools only for development!!!
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            Startup.Initialize(app.Services);
            return app;
        }
    }
}

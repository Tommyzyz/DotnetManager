using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Features.Dashboard;
using DotnetManager.RazorLib.Features.NuGet;
using DotnetManager.RazorLib.Features.Sdk;
using DotnetManager.RazorLib.Features.Tools;
using DotnetManager.RazorLib.Features.Workloads;
using DotnetManager.RazorLib.Resources;
using DotnetManager.Services;

namespace DotnetManager;

public static class Startup
{
    public static IServiceProvider? Services { get; private set; }

    public static void Init()
    {
        var host = Host.CreateDefaultBuilder()
                       .ConfigureServices(WireupServices)
                       .Build();
        Services = host.Services;

        var settings = Services.GetRequiredService<IAppSettingsStore>();
        Services.GetRequiredService<LocalizationState>().Initialize(settings.Current.Language);
    }

    private static void WireupServices(IServiceCollection services)
    {
        services.AddWpfBlazorWebView();

        services.AddSingleton<LocalizationState>();
        services.AddSingleton<UiLocalizer>();
        services.AddSingleton<IActivityLog, ActivityLog>();
        services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.AddSingleton<IDotnetLocator, DotnetLocator>();
        services.AddSingleton<IDotnetCli, DotnetCli>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INuGetSettingsService, NuGetSettingsService>();
        services.AddSingleton<INuGetPackageQuery, NuGetPackageQuery>();
        services.AddSingleton<ICacheInspector, CacheInspector>();
        services.AddSingleton<ILocalToolManifestService, LocalToolManifestService>();
        services.AddSingleton<IGlobalToolService, GlobalToolService>();
        services.AddSingleton<ISdkService, SdkService>();
        services.AddSingleton<IDotnetEnvironmentService, DotnetEnvironmentService>();
        services.AddSingleton<IDotnetSdkInstaller, SdkInstallerService>();

        // Shared by the layout banner and the SDK page: one detection result for the whole app.
        services.AddSingleton<DotnetEnvironmentViewModel>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<LocalToolsViewModel>();
        services.AddTransient<GlobalToolsViewModel>();
        services.AddTransient<ToolSearchViewModel>();
        services.AddTransient<PackageSourcesViewModel>();
        services.AddTransient<NuGetCachesViewModel>();
        services.AddTransient<NuGetConfigFilesViewModel>();
        services.AddTransient<WorkloadsViewModel>();
        services.AddTransient<SdkInfoViewModel>();
        services.AddTransient<ActivityLogViewModel>();
        services.AddTransient<AppSettingsViewModel>();

#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
    }
}

using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Features.Dashboard;
using DotnetManager.RazorLib.Features.NuGet;
using DotnetManager.RazorLib.Features.Sdk;
using DotnetManager.RazorLib.Features.Tools;
using DotnetManager.RazorLib.Features.Workloads;
using DotnetManager.RazorLib.Resources;
using DotnetManagerUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotnetManagerUI;

/// <summary>
/// Registers the application services for the two ways this app starts.
/// <para>
/// The shell builds its container inside <see cref="MauiProgram"/> (the MAUI host owns the BlazorWebView,
/// so every view model is resolved from that container). The elevated child process the shell starts with
/// the runas verb (<c>--request=</c>) builds a plain host here and never touches a window.
/// </para>
/// </summary>
public static class Startup
{
    public static IServiceProvider? Services { get; private set; }

    /// <summary>Builds the container for the headless (elevated) child process.</summary>
    public static void Init()
    {
        var host = Host.CreateDefaultBuilder()
                       .ConfigureServices(ConfigureServices)
                       .Build();

        Services = host.Services;
        Localize(host.Services);
    }

    /// <summary>Adds every application service. Called by the headless host and by MauiProgram.</summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<LocalizationState>();
        services.AddSingleton<UiLocalizer>();

        // The single "current project" every project-scoped page reads and writes.
        services.AddSingleton<ProjectTarget>();
        services.AddSingleton<IActivityLog, ActivityLog>();
        services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.AddSingleton<IDotnetLocator, DotnetLocator>();
        services.AddSingleton<IDotnetCli, DotnetCli>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IShellNavigation, ShellNavigation>();
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
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<PackageSourcesViewModel>();
        services.AddTransient<ProjectPackagesViewModel>();
        services.AddTransient<NuGetCachesViewModel>();
        services.AddTransient<NuGetConfigFilesViewModel>();
        services.AddTransient<WorkloadsViewModel>();
        services.AddTransient<SdkInfoViewModel>();
        services.AddTransient<ActivityLogViewModel>();
        services.AddTransient<AppSettingsViewModel>();
    }

    /// <summary>Adopts the container the shell built and applies the persisted UI language.</summary>
    public static void Initialize(IServiceProvider services)
    {
        Services = services;
        Localize(services);
    }

    private static void Localize(IServiceProvider services)
    {
        var settings = services.GetRequiredService<IAppSettingsStore>();
        services.GetRequiredService<LocalizationState>().Initialize(settings.Current.Language);
    }
}

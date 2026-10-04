using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Dashboard;

/// <summary>Loads the data shown on the overview page.</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly ISdkService _sdk;
    private readonly IGlobalToolService _tools;
    private readonly INuGetSettingsService _settings;
    private readonly IEnvironmentService _environment;
    private readonly ICacheInspector _caches;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private string? sdkVersion;

    [ObservableProperty]
    private IReadOnlyList<SdkInfo> sdks = [];

    [ObservableProperty]
    private IReadOnlyList<RuntimeInfo> runtimes = [];

    [ObservableProperty]
    private IReadOnlyList<GlobalToolInfo> globalTools = [];

    [ObservableProperty]
    private IReadOnlyList<WorkloadInfo> workloads = [];

    [ObservableProperty]
    private NuGetSettingsSnapshot? nuget;

    [ObservableProperty]
    private string? globalPackagesSize;

    [ObservableProperty]
    private string? httpCacheSize;

    public DashboardViewModel(
        ISdkService sdk,
        IGlobalToolService tools,
        INuGetSettingsService settings,
        IEnvironmentService environment,
        ICacheInspector caches,
        IDotnetCli cli)
    {
        _sdk = sdk;
        _tools = tools;
        _settings = settings;
        _environment = environment;
        _caches = caches;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public string DotnetPath => _cli.ExecutablePath;

    public bool IsElevated => _environment.IsElevated;

    public string GlobalPackagesFolder => Nuget?.GlobalPackagesFolder.Value ?? string.Empty;
    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            SdkVersion = (await _sdk.GetSdksAsync()).LastOrDefault()?.Version ?? "unknown";
            Sdks = await _sdk.GetSdksAsync();
            Runtimes = await _sdk.GetRuntimesAsync();
            GlobalTools = await _tools.ListAsync();
            Workloads = await _sdk.GetWorkloadsAsync();
            Nuget = _settings.Load(rootDirectory: null);
            OnPropertyChanged(nameof(GlobalPackagesFolder));

            await MeasureCachesAsync();
        });
    }

    private async Task MeasureCachesAsync()
    {
        var snapshot = Nuget;
        if (snapshot is null)
        {
            return;
        }

        var packages = snapshot.GlobalPackagesFolder.Value;
        if (!string.IsNullOrWhiteSpace(packages))
        {
            var (bytes, files) = await _caches.GetDirectorySizeAsync(packages);
            GlobalPackagesSize = FormatSize(bytes, files);
        }

        var httpCache = snapshot.HttpCacheFolder.Value;
        if (!string.IsNullOrWhiteSpace(httpCache))
        {
            var (bytes, files) = await _caches.GetDirectorySizeAsync(httpCache);
            HttpCacheSize = FormatSize(bytes, files);
        }
    }

    public static string FormatSize(long bytes, int files)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]} / {files} files";
    }
}

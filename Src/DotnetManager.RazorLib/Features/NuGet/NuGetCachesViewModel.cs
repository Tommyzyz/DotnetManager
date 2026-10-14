using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.NuGet;

/// <summary>One NuGet cache location, optionally enriched with its measured size.</summary>
public sealed partial class CacheRow : ObservableObject
{
    [ObservableProperty]
    private string? sizeText;

    [ObservableProperty]
    private bool isMeasuring;

    public required string Kind { get; init; }

    public required string LabelKey { get; init; }

    public required string EnvironmentVariable { get; init; }

    public required string Path { get; init; }

    public required string Source { get; init; }

    public bool SupportsConfigValue => string.Equals(Kind, "global-packages", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Shows the NuGet cache locations, measures them, clears them and changes where they live.</summary>
public sealed partial class NuGetCachesViewModel : ViewModelBase
{
    private readonly INuGetSettingsService _settings;
    private readonly ICacheInspector _inspector;
    private readonly IEnvironmentService _environment;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _appSettings;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<CacheRow> caches = [];

    [ObservableProperty]
    private NuGetSettingsSnapshot? snapshot;

    [ObservableProperty]
    private IReadOnlyList<NuGetPathEntry> fallbackFolders = [];

    [ObservableProperty]
    private string newFallbackFolder = string.Empty;

    [ObservableProperty]
    private string editKind = "global-packages";

    partial void OnEditKindChanged(string value) => OnPropertyChanged(nameof(CanApplyConfigValue));

    [ObservableProperty]
    private string editPath = string.Empty;

    [ObservableProperty]
    private EnvScope editScope = EnvScope.User;

    [ObservableProperty]
    private string? totalSizeText;

    public NuGetCachesViewModel(
        INuGetSettingsService settings,
        ICacheInspector inspector,
        IEnvironmentService environment,
        IDialogService dialogs,
        IAppSettingsStore appSettings,
        IDotnetCli cli)
    {
        _settings = settings;
        _inspector = inspector;
        _environment = environment;
        _dialogs = dialogs;
        _appSettings = appSettings;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public bool IsElevated => _environment.IsElevated;

    public string UserConfigPath => _settings.UserConfigPath;

    public CacheRow? SelectedCache => Caches.FirstOrDefault(cache =>
        string.Equals(cache.Kind, EditKind, StringComparison.OrdinalIgnoreCase));

    /// <summary>Only the global packages folder has a matching NuGet.Config key (globalPackagesFolder).</summary>
    public bool CanApplyConfigValue => SelectedCache?.SupportsConfigValue == true;
    public async Task InitializeAsync() => await RefreshAsync();
    public Task RefreshAsync() => RunAsync(RefreshCoreAsync);

    /// <summary>Reloads cache locations without the busy guard, so mutations can refresh from their own scope.</summary>
    private async Task RefreshCoreAsync()
    {
        var snapshot = _settings.Load(_appSettings.Current.LastWorkingDirectory);
        Snapshot = snapshot;

        var fromCli = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cliResult = await _cli.RunToResultAsync(
            ["nuget", "locals", "all", "--list"],
            new CliOptions { Description = "dotnet nuget locals all --list", InvariantOutput = true });

        if (cliResult.Success)
        {
            foreach (var (key, value) in NuGetLocalsParser.Parse(cliResult.StandardOutput))
            {
                fromCli[key] = value;
            }
        }

        Caches =
        [
            Build("global-packages", "NuGetCache.GlobalPackages", "NUGET_PACKAGES", snapshot.GlobalPackagesFolder, fromCli),
            Build("http-cache", "NuGetCache.HttpCache", "NUGET_HTTP_CACHE_PATH", snapshot.HttpCacheFolder, fromCli),
            Build("plugins-cache", "NuGetCache.PluginsCache", "NUGET_PLUGINS_CACHE_PATH", snapshot.PluginsCacheFolder, fromCli),
            Build("temp", "NuGetCache.Temp", "NUGET_SCRATCH", snapshot.TempFolder, fromCli),
        ];

        FallbackFolders = snapshot.FallbackFolders;
        SetStatus(cliResult.Success
            ? "Cache locations loaded from the CLI."
            : "Cache locations were computed locally because the CLI call failed.");
    }

    public async Task MeasureAsync(CacheRow? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            row.IsMeasuring = true;
            try
            {
                var (bytes, files) = await _inspector.GetDirectorySizeAsync(row.Path);
                row.SizeText = Dashboard.DashboardViewModel.FormatSize(bytes, files);
            }
            finally
            {
                row.IsMeasuring = false;
            }
        });
    }

    public async Task MeasureAllAsync()
    {
        await RunAsync(async () =>
        {
            long totalBytes = 0;
            var totalFiles = 0;

            foreach (var row in Caches)
            {
                row.IsMeasuring = true;
                var (bytes, files) = await _inspector.GetDirectorySizeAsync(row.Path);
                totalBytes += bytes;
                totalFiles += files;
                row.SizeText = Dashboard.DashboardViewModel.FormatSize(bytes, files);
                row.IsMeasuring = false;
                SetStatus($"Measured {row.Kind}: {Dashboard.DashboardViewModel.FormatSize(bytes, files)}");
            }

            TotalSizeText = Dashboard.DashboardViewModel.FormatSize(totalBytes, totalFiles);
            SetStatus("All cache sizes were measured.");
        });
    }

    public async Task ClearAsync(CacheRow? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        if (!_dialogs.Confirm($"Clear the {row.Kind} cache?\n{row.Path}", "Clear NuGet cache"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["nuget", "locals", row.Kind, "--clear"],
                $"dotnet nuget locals {row.Kind} --clear");

            SetStatus(result.Success ? $"Cleared {row.Kind}." : $"Clearing {row.Kind} failed.", !result.Success);
            row.SizeText = null;
        });
    }

    public async Task ClearAllAsync()
    {
        if (!_dialogs.Confirm("Clear all NuGet caches (http, global packages, temp and plugins)?", "Clear all NuGet caches"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(["nuget", "locals", "all", "--clear"], "dotnet nuget locals all --clear");
            SetStatus(result.Success ? "All caches were cleared." : "Clearing the caches failed.", !result.Success);

            foreach (var row in Caches)
            {
                row.SizeText = null;
            }
        });
    }

    public void Open(CacheRow? row)
    {
        if (row is not null && Directory.Exists(row.Path))
        {
            _dialogs.RevealInShell(row.Path);
        }
    }

    public void UseLocation(CacheRow? row)
    {
        if (row is null)
        {
            return;
        }

        EditKind = row.Kind;
        EditPath = row.Path;
    }

    public async Task ApplyEnvironmentVariableAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(EditPath))
            {
                SetStatus("Enter a directory first.", isError: true);
                return;
            }

            var variable = EnvironmentVariableFor(EditKind);
            if (variable is null)
            {
                SetStatus($"'{EditKind}' cannot be configured through a NuGet variable.", isError: true);
                return;
            }

            var result = await _environment.SetAsync(variable, EditPath.Trim(), EditScope);
            await RefreshCoreAsync();
            SetStatus(result.Message, !result.Success);
        });
    }

    public async Task RemoveEnvironmentVariableAsync()
    {
        await RunAsync(async () =>
        {
            var variable = EnvironmentVariableFor(EditKind);
            if (variable is null)
            {
                return;
            }

            var result = await _environment.SetAsync(variable, null, EditScope);
            await RefreshCoreAsync();
            SetStatus(result.Message, !result.Success);
        });
    }

    public async Task ApplyConfigValueAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(EditPath))
            {
                SetStatus("Enter a directory first.", isError: true);
                return;
            }

            var result = await _settings.SetConfigValueAsync(_settings.UserConfigPath, "globalPackagesFolder", EditPath.Trim());
            await RefreshCoreAsync();
            SetStatus(result.Message, !result.Success);
        });
    }

    public async Task AddFallbackFolderAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewFallbackFolder))
            {
                SetStatus("Enter or pick a directory first.", isError: true);
                return;
            }

            var folders = FallbackFolders.Select(folder => folder.Path).ToList();
            if (!folders.Contains(NewFallbackFolder.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(NewFallbackFolder.Trim());
            }

            var result = await _settings.SetFallbackFoldersAsync(_settings.UserConfigPath, folders);
            NewFallbackFolder = string.Empty;
            await RefreshCoreAsync();
            SetStatus(result.Message, !result.Success);
        });
    }

    public async Task RemoveFallbackFolderAsync(NuGetPathEntry? folder)
    {
        if (folder is null || IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var folders = FallbackFolders
                .Where(entry => !string.Equals(entry.Path, folder.Path, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Path)
                .ToList();

            var result = await _settings.SetFallbackFoldersAsync(_settings.UserConfigPath, folders);
            await RefreshCoreAsync();
            SetStatus(result.Message, !result.Success);
        });
    }

    public void PickEditDirectory()
    {
        var picked = _dialogs.PickFolder("Select the new cache directory", EditPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            EditPath = picked;
        }
    }

    public void PickFallbackFolder()
    {
        var picked = _dialogs.PickFolder("Select a fallback package folder", NewFallbackFolder);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            NewFallbackFolder = picked;
        }
    }

    private static CacheRow Build(
        string kind,
        string labelKey,
        string environmentVariable,
        NuGetValue value,
        IReadOnlyDictionary<string, string> fromCli)
    {
        var path = fromCli.TryGetValue(kind, out var cliPath) ? cliPath : value.Value ?? string.Empty;

        return new CacheRow
        {
            Kind = kind,
            LabelKey = labelKey,
            EnvironmentVariable = environmentVariable,
            Path = path,
            Source = value.Source == NuGetValueSource.Environment
                ? value.SourceDetail ?? environmentVariable
                : string.Empty,
        };
    }

    private static string? EnvironmentVariableFor(string kind) => kind switch
    {
        "global-packages" => "NUGET_PACKAGES",
        "http-cache" => "NUGET_HTTP_CACHE_PATH",
        "plugins-cache" => "NUGET_PLUGINS_CACHE_PATH",
        "temp" => "NUGET_SCRATCH",
        _ => null,
    };
}

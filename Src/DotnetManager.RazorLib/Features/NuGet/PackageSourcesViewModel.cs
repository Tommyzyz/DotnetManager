using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.NuGet;

/// <summary>Lists, adds, removes and toggles NuGet package sources.</summary>
public sealed partial class PackageSourcesViewModel : ViewModelBase
{
    private readonly INuGetSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _appSettings;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private string? rootDirectory;

    [ObservableProperty]
    private NuGetSettingsSnapshot? snapshot;

    [ObservableProperty]
    private IReadOnlyList<PackageSourceInfo> sources = [];

    [ObservableProperty]
    private IReadOnlyList<NuGetConfigFileInfo> configFiles = [];

    [ObservableProperty]
    private NuGetConfigFileInfo? targetConfigFile;

    [ObservableProperty]
    private string newSourceName = string.Empty;

    [ObservableProperty]
    private string newSourceUrl = string.Empty;

    public PackageSourcesViewModel(
        INuGetSettingsService settings,
        IDialogService dialogs,
        IAppSettingsStore appSettings,
        IDotnetCli cli)
    {
        _settings = settings;
        _dialogs = dialogs;
        _appSettings = appSettings;
        _cli = cli;
        rootDirectory = appSettings.Current.LastWorkingDirectory ?? Environment.CurrentDirectory;
    }

    protected override IDotnetCli Cli => _cli;

    public string EffectiveTargetFile => TargetConfigFile?.Path ?? _settings.UserConfigPath;

    public bool TargetNeedsElevation => _settings.RequiresElevation(EffectiveTargetFile);
    public async Task InitializeAsync() => await RefreshAsync();
    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            await Task.Yield();
            var snapshot = _settings.Load(RootDirectory);
            Snapshot = snapshot;
            Sources = snapshot.Sources;
            ConfigFiles = snapshot.ConfigFiles;

            TargetConfigFile = ConfigFiles.FirstOrDefault(file => file.Exists && !file.IsMachineWide)
                ?? ConfigFiles.FirstOrDefault(file => !file.IsMachineWide);

            OnPropertyChanged(nameof(EffectiveTargetFile));
            OnPropertyChanged(nameof(TargetNeedsElevation));

            if (snapshot.LoadError is not null)
            {
                SetStatus(snapshot.LoadError, isError: true);
            }
            else
            {
                SetStatus($"{Sources.Count} source(s), {Sources.Count(source => source.Enabled)} enabled.");
            }
        });
    }

    public async Task PickRootDirectoryAsync()
    {
        var picked = _dialogs.PickFolder("Select the directory whose NuGet.Config chain should be used", RootDirectory);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        RootDirectory = picked;
        _appSettings.Current.LastWorkingDirectory = picked;
        await _appSettings.SaveAsync();
        await RefreshAsync();
    }

    public void SetTargetFile(string? path)
    {
        TargetConfigFile = ConfigFiles.FirstOrDefault(file =>
            string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    public void UseUserProfile() => RootDirectory = null;
    public async Task AddOrUpdateSourceAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewSourceName) || string.IsNullOrWhiteSpace(NewSourceUrl))
            {
                SetStatus("Both the source name and URL are required.", isError: true);
                return;
            }

            var target = EffectiveTargetFile;
            var result = await _settings.AddOrUpdateSourceAsync(target, NewSourceName.Trim(), NewSourceUrl.Trim());
            SetStatus(result.Message, !result.Success);

            if (result.Success)
            {
                NewSourceName = string.Empty;
                NewSourceUrl = string.Empty;
                await RefreshAsync();
            }
        });
    }

    public async Task RemoveSourceAsync(PackageSourceInfo? source)
    {
        if (source is null || IsBusy)
        {
            return;
        }

        var target = source.ConfigFile ?? EffectiveTargetFile;
        if (!_dialogs.Confirm($"Remove the source '{source.Name}' from\n{target}?", "Remove package source"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _settings.RemoveSourceAsync(target, source.Name);
            SetStatus(result.Message, !result.Success);
            await RefreshAsync();
        });
    }

    public async Task ToggleSourceAsync(PackageSourceInfo? source)
    {
        if (source is null || IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (source.Enabled)
            {
                // Disabling always has to be recorded in a higher precedence file than the definition.
                var result = await _settings.SetSourceEnabledAsync(EffectiveTargetFile, source.Name, enabled: false);
                SetStatus(result.Message, !result.Success);
            }
            else
            {
                var disabledEntry = Snapshot?.DisabledSources.FirstOrDefault(entry =>
                    string.Equals(entry.Key, source.Name, StringComparison.OrdinalIgnoreCase));

                var result = await _settings.SetSourceEnabledAsync(
                    disabledEntry?.ConfigFile ?? EffectiveTargetFile,
                    source.Name,
                    enabled: true);

                SetStatus(result.Message, !result.Success);
            }

            await RefreshAsync();
        });
    }

    public void RevealFile(NuGetConfigFileInfo? file)
    {
        var path = file?.Path ?? EffectiveTargetFile;
        if (File.Exists(path))
        {
            _dialogs.RevealInShell(path);
        }
        else
        {
            _dialogs.RevealInShell(Path.GetDirectoryName(path) ?? path);
        }
    }

    public async Task CreateConfigFileAsync(NuGetConfigFileInfo? file)
    {
        if (file is null || IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _settings.WriteRawFileAsync(
                file.Path,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                </configuration>
                """);

            SetStatus(result.Message, !result.Success);
            await RefreshAsync();
        });
    }

    partial void OnTargetConfigFileChanged(NuGetConfigFileInfo? value)
    {
        OnPropertyChanged(nameof(EffectiveTargetFile));
        OnPropertyChanged(nameof(TargetNeedsElevation));
    }
}

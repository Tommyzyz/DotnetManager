using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.NuGet;

/// <summary>Shows the effective NuGet configuration chain and allows raw editing of a single file.</summary>
public sealed partial class NuGetConfigFilesViewModel : ViewModelBase
{
    private readonly INuGetSettingsService _settings;
    private readonly IAppSettingsStore _appSettings;
    private readonly IDialogService _dialogs;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<NuGetConfigFileInfo> files = [];

    [ObservableProperty]
    private NuGetConfigFileInfo? selectedFile;

    [ObservableProperty]
    private string rawContent = string.Empty;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private IReadOnlyList<NuGetConfigEntry> configValues = [];

    [ObservableProperty]
    private IReadOnlyList<NuGetConfigEntry> disabledSources = [];

    private string _originalContent = string.Empty;

    public NuGetConfigFilesViewModel(
        INuGetSettingsService settings,
        IAppSettingsStore appSettings,
        IDialogService dialogs,
        IDotnetCli cli)
    {
        _settings = settings;
        _appSettings = appSettings;
        _dialogs = dialogs;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public bool RequiresElevation => SelectedFile is not null && _settings.RequiresElevation(SelectedFile.Path);

    public bool CanSave => SelectedFile is not null && IsDirty;
    public async Task InitializeAsync() => await RefreshAsync();
    public Task RefreshAsync() => RunAsync(RefreshCoreAsync);

    /// <summary>Reloads the file list and reselects without the busy guard, so mutations can refresh in-place.</summary>
    private async Task RefreshCoreAsync()
    {
        await Task.Yield();
        var snapshot = _settings.Load(_appSettings.Current.LastWorkingDirectory);
        Files = snapshot.ConfigFiles;
        ConfigValues = snapshot.ConfigValues;
        DisabledSources = snapshot.DisabledSources;

        var target = SelectedFile is not null
            ? Files.FirstOrDefault(file => string.Equals(file.Path, SelectedFile.Path, StringComparison.OrdinalIgnoreCase))
            : Files.FirstOrDefault(file => file.Exists);

        await LoadFileAsync(target);
    }

    public Task SelectAsync(NuGetConfigFileInfo? file) => RunAsync(() => LoadFileAsync(file));

    /// <summary>Loads a single file without the busy guard; the file's content is re-read on every call.</summary>
    private async Task LoadFileAsync(NuGetConfigFileInfo? file)
    {
        await Task.Yield();
        SelectedFile = file;
        OnPropertyChanged(nameof(RequiresElevation));
        OnPropertyChanged(nameof(CanSave));

        if (file is null)
        {
            RawContent = string.Empty;
            _originalContent = string.Empty;
            IsDirty = false;
            return;
        }

        _originalContent = _settings.ReadRawFile(file.Path);
        RawContent = _originalContent;
        IsDirty = false;

        if (!file.Exists)
        {
            SetStatus("This file does not exist yet; saving will create it.");
        }
    }

    public async Task SelectByPathAsync(string? path)
    {
        var file = Files.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
        if (file is not null)
        {
            await SelectAsync(file);
        }
    }

    public async Task SaveAsync()
    {
        if (SelectedFile is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _settings.WriteRawFileAsync(SelectedFile.Path, RawContent);

            if (result.Success)
            {
                await RefreshCoreAsync();
            }

            SetStatus(result.Message, !result.Success);
        });
    }

    public async Task CreateFileAsync(NuGetConfigFileInfo? file)
    {
        if (file is null)
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

            await RefreshCoreAsync();
            SetStatus(result.Message, !result.Success);
        });
    }

    public void RevealFile(NuGetConfigFileInfo? file)
    {
        if (file is null)
        {
            return;
        }

        if (File.Exists(file.Path))
        {
            _dialogs.RevealInShell(file.Path);
        }
        else
        {
            _dialogs.RevealInShell(Path.GetDirectoryName(file.Path) ?? file.Path);
        }
    }

    partial void OnRawContentChanged(string value)
    {
        IsDirty = !string.Equals(value, _originalContent, StringComparison.Ordinal);
        OnPropertyChanged(nameof(CanSave));
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>Application level settings: language, dotnet location and default directories.</summary>
public sealed partial class AppSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IAppSettingsStore _settings;
    private readonly IDotnetLocator _locator;
    private readonly IDialogService _dialogs;
    private readonly LocalizationState _localization;
    private readonly ProjectTarget _target;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private string language;

    [ObservableProperty]
    private string dotnetPath = string.Empty;

    [ObservableProperty]
    private string defaultWorkingDirectory = string.Empty;

    [ObservableProperty]
    private bool includePrerelease;

    [ObservableProperty]
    private bool autoCheckToolUpdates;

    public AppSettingsViewModel(
        IAppSettingsStore settings,
        IDotnetLocator locator,
        IDialogService dialogs,
        LocalizationState localization,
        ProjectTarget target,
        IDotnetCli cli)
    {
        _settings = settings;
        _locator = locator;
        _dialogs = dialogs;
        _localization = localization;
        _target = target;
        _cli = cli;

        language = settings.Current.Language;
        dotnetPath = settings.Current.DotnetPath ?? string.Empty;
        defaultWorkingDirectory = target.Directory;
        includePrerelease = settings.Current.IncludePrerelease;
        autoCheckToolUpdates = settings.Current.AutoCheckToolUpdates;

        _target.Changed += HandleTargetChanged;
    }

    public void Dispose() => _target.Changed -= HandleTargetChanged;

    /// <summary>Another page moved the project target, so the text box follows it.</summary>
    private void HandleTargetChanged() => DefaultWorkingDirectory = _target.Directory;

    protected override IDotnetCli Cli => _cli;

    public IReadOnlyList<string> Languages => LocalizationState.SupportedLanguages;

    public string SettingsFilePath => _settings.SettingsFilePath;

    public string ResolvedDotnetPath => _locator.Resolve(DotnetPath);

    public bool DotnetPathIsValid => _locator.IsValid(ResolvedDotnetPath);
    public async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            _settings.Current.Language = Language;
            _settings.Current.DotnetPath = string.IsNullOrWhiteSpace(DotnetPath) ? null : DotnetPath.Trim();
            _settings.Current.IncludePrerelease = IncludePrerelease;
            _settings.Current.AutoCheckToolUpdates = AutoCheckToolUpdates;

            // Switch language before any await so the change and its Changed event land in the calling flow.
            _localization.SetLanguage(Language);
            await _settings.SaveAsync();

            // The working directory is the shared project target, so it is written through its one owner.
            await _target.UseAsync(DefaultWorkingDirectory);

            SetStatus($"Saved to {SettingsFilePath}");
        });
    }

    public void PickDotnetPath()
    {
        var picked = _dialogs.PickFile("Select dotnet.exe", "dotnet.exe|dotnet.exe|Executables (*.exe)|*.exe", DotnetPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            DotnetPath = picked;
        }
    }

    public async Task PickWorkingDirectoryAsync()
    {
        if (await _target.PickAsync("Select the default working directory"))
        {
            DefaultWorkingDirectory = _target.Directory;
        }
    }

    public void RevealSettingsFile() => _dialogs.RevealInShell(_settings.SettingsFilePath);
    public void ResetDotnetPath()
    {
        DotnetPath = string.Empty;
        OnPropertyChanged(nameof(ResolvedDotnetPath));
        OnPropertyChanged(nameof(DotnetPathIsValid));
    }

    public static string DisplayLanguage(string code) => LocalizationState.DisplayName(code);

    partial void OnDotnetPathChanged(string value)
    {
        OnPropertyChanged(nameof(ResolvedDotnetPath));
        OnPropertyChanged(nameof(DotnetPathIsValid));
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>Application level settings: language, dotnet location and default directories.</summary>
public sealed partial class AppSettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _settings;
    private readonly IDotnetLocator _locator;
    private readonly IDialogService _dialogs;
    private readonly LocalizationState _localization;
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
        IDotnetCli cli)
    {
        _settings = settings;
        _locator = locator;
        _dialogs = dialogs;
        _localization = localization;
        _cli = cli;

        language = settings.Current.Language;
        dotnetPath = settings.Current.DotnetPath ?? string.Empty;
        defaultWorkingDirectory = settings.Current.LastWorkingDirectory ?? string.Empty;
        includePrerelease = settings.Current.IncludePrerelease;
        autoCheckToolUpdates = settings.Current.AutoCheckToolUpdates;
    }

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
            _settings.Current.LastWorkingDirectory = string.IsNullOrWhiteSpace(DefaultWorkingDirectory)
                ? null
                : DefaultWorkingDirectory.Trim();
            _settings.Current.IncludePrerelease = IncludePrerelease;
            _settings.Current.AutoCheckToolUpdates = AutoCheckToolUpdates;

            // Switch language before any await so the change and its Changed event land in the calling flow.
            _localization.SetLanguage(Language);
            await _settings.SaveAsync();
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

    public void PickWorkingDirectory()
    {
        var picked = _dialogs.PickFolder("Select the default working directory", DefaultWorkingDirectory);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            DefaultWorkingDirectory = picked;
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

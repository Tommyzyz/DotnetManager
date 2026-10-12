using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>Searches nuget.org for dotnet tool packages and installs them globally or locally.</summary>
public sealed partial class ToolSearchViewModel : ViewModelBase, IDisposable
{
    private readonly INuGetPackageQuery _query;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _settings;
    private readonly ProjectTarget _target;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private bool includePrerelease;

    [ObservableProperty]
    private bool toolOnly = true;

    [ObservableProperty]
    private IReadOnlyList<PackageSearchResult> results = [];

    [ObservableProperty]
    private bool installGlobally = true;

    [ObservableProperty]
    private bool hasSearched;

    public ToolSearchViewModel(
        INuGetPackageQuery query,
        IDialogService dialogs,
        IAppSettingsStore settings,
        ProjectTarget target,
        IDotnetCli cli)
    {
        _query = query;
        _dialogs = dialogs;
        _settings = settings;
        _target = target;
        _cli = cli;
        includePrerelease = settings.Current.IncludePrerelease;

        _target.Changed += HandleTargetChanged;
    }

    protected override IDotnetCli Cli => _cli;

    /// <summary>The shared project target, used as the working directory of <c>--local</c> installs.</summary>
    public string InstallDirectory => _target.Directory;

    public void Dispose() => _target.Changed -= HandleTargetChanged;

    private void HandleTargetChanged() => OnPropertyChanged(nameof(InstallDirectory));
    public async Task SearchAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(QueryText))
            {
                SetStatus("Enter a search term.", isError: true);
                return;
            }

            Results = await _query.SearchAsync(QueryText.Trim(), IncludePrerelease, ToolOnly);
            HasSearched = true;
            SetStatus(Results.Count == 0 ? "No matching package was found." : $"{Results.Count} package(s) found.");
        });
    }

    public async Task InstallAsync(PackageSearchResult? package)
    {
        if (package is null || IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var arguments = new List<string> { "tool", "install", package.Id };
            string? workingDirectory = null;

            if (InstallGlobally)
            {
                arguments.Add("--global");
            }
            else
            {
                arguments.Add("--local");
                workingDirectory = InstallDirectory;
            }

            if (!string.IsNullOrWhiteSpace(package.LatestVersion))
            {
                arguments.Add("--version");
                arguments.Add(package.LatestVersion);
            }

            if (IncludePrerelease)
            {
                arguments.Add("--prerelease");
            }

            var result = await RunCliAsync(
                arguments,
                $"dotnet tool install{(InstallGlobally ? " --global" : " --local")} {package.Id}",
                workingDirectory);

            SetStatus(result.Success ? $"Installed {package.Id}." : $"Installing {package.Id} failed.", !result.Success);
        });
    }

    public void OpenPackagePage(PackageSearchResult? package)
    {
        if (package is not null)
        {
            _dialogs.RevealInShell($"https://www.nuget.org/packages/{package.Id}");
        }
    }

    partial void OnIncludePrereleaseChanged(bool value)
    {
        _settings.Current.IncludePrerelease = value;
        _ = _settings.SaveAsync();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>Searches nuget.org for dotnet tool packages and installs them globally or locally.</summary>
public sealed partial class ToolSearchViewModel : ViewModelBase
{
    private readonly INuGetPackageQuery _query;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _settings;
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
    private string installDirectory = string.Empty;

    [ObservableProperty]
    private bool hasSearched;

    public ToolSearchViewModel(
        INuGetPackageQuery query,
        IDialogService dialogs,
        IAppSettingsStore settings,
        IDotnetCli cli)
    {
        _query = query;
        _dialogs = dialogs;
        _settings = settings;
        _cli = cli;
        installDirectory = settings.Current.LastWorkingDirectory ?? Environment.CurrentDirectory;
        includePrerelease = settings.Current.IncludePrerelease;
    }

    protected override IDotnetCli Cli => _cli;
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

    public async Task PickInstallDirectoryAsync()
    {
        var picked = _dialogs.PickFolder("Select the directory for the local tool manifest", InstallDirectory);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        InstallDirectory = picked;
        _settings.Current.LastWorkingDirectory = picked;
        await _settings.SaveAsync();
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

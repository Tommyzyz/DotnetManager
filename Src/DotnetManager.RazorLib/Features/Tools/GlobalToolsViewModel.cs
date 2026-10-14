using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>Manages the machine-wide tools reported by <c>dotnet tool list --global</c>.</summary>
public sealed partial class GlobalToolsViewModel : ViewModelBase
{
    private readonly IGlobalToolService _tools;
    private readonly INuGetPackageQuery _query;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _settings;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<ToolRow> tools = [];

    [ObservableProperty]
    private string newPackageId = string.Empty;

    [ObservableProperty]
    private string newPackageVersion = string.Empty;

    [ObservableProperty]
    private bool installPrerelease;

    [ObservableProperty]
    private string filter = string.Empty;

    public GlobalToolsViewModel(
        IGlobalToolService tools,
        INuGetPackageQuery query,
        IDialogService dialogs,
        IAppSettingsStore settings,
        IDotnetCli cli)
    {
        _tools = tools;
        _query = query;
        _dialogs = dialogs;
        _settings = settings;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public string ToolsDirectory => _tools.ToolsDirectory;

    public string StoreDirectory => _tools.StoreDirectory;

    public IReadOnlyList<ToolRow> FilteredTools => string.IsNullOrWhiteSpace(Filter)
        ? Tools
        : Tools.Where(tool => tool.PackageId.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    public async Task InitializeAsync()
    {
        await RefreshAsync();

        if (_settings.Current.AutoCheckToolUpdates && Tools.Count > 0)
        {
            await CheckUpdatesAsync();
        }
    }
    public Task RefreshAsync() => RunAsync(LoadToolsAsync);

    /// <summary>
    /// Reloads the tool list. Safe to call from inside a <see cref="ViewModelBase.RunAsync"/> scope: a nested
    /// <see cref="RefreshAsync"/> call would return immediately because the busy guard rejects re-entrant calls.
    /// </summary>
    private async Task LoadToolsAsync()
    {
        var tools = await _tools.ListAsync();
        Tools = tools
            .Select(tool => new ToolRow
            {
                PackageId = tool.PackageId,
                Version = tool.Version,
                Commands = tool.Commands,
            })
            .ToList();

        SetStatus($"{Tools.Count} global tool(s) installed.");
    }

    public async Task InstallAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewPackageId))
            {
                SetStatus("Enter a package id.", isError: true);
                return;
            }

            var arguments = new List<string> { "tool", "install", NewPackageId.Trim(), "--global" };
            if (!string.IsNullOrWhiteSpace(NewPackageVersion))
            {
                arguments.Add("--version");
                arguments.Add(NewPackageVersion.Trim());
            }

            if (InstallPrerelease)
            {
                arguments.Add("--prerelease");
            }

            var result = await RunCliAsync(arguments, $"dotnet tool install --global {NewPackageId}");
            var message = result.Success ? $"Installed {NewPackageId}." : "The installation failed.";

            if (result.Success)
            {
                NewPackageId = string.Empty;
                NewPackageVersion = string.Empty;
                await LoadToolsAsync();
            }

            SetStatus(message, !result.Success);
        });
    }

    public async Task UpdateAsync(ToolRow? row)
    {
        if (row is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["tool", "update", row.PackageId, "--global"],
                $"dotnet tool update --global {row.PackageId}");

            var message = result.Success ? $"Updated {row.PackageId}." : "The update failed.";
            if (result.Success)
            {
                await LoadToolsAsync();
            }

            SetStatus(message, !result.Success);
        });
    }

    public async Task UpdateAllAsync()
    {
        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["tool", "update", "--global", "--all"],
                "dotnet tool update --global --all");

            var message = result.Success ? "All global tools were updated." : "The update failed.";
            await LoadToolsAsync();
            SetStatus(message, !result.Success);
        });
    }

    public async Task UninstallAsync(ToolRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Uninstall '{row.PackageId}'?", "Uninstall global tool"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["tool", "uninstall", row.PackageId, "--global"],
                $"dotnet tool uninstall --global {row.PackageId}");

            var message = result.Success ? $"Uninstalled {row.PackageId}." : "The uninstall failed.";
            await LoadToolsAsync();
            SetStatus(message, !result.Success);
        });
    }

    public async Task CheckUpdatesAsync()
    {
        await RunAsync(async () =>
        {
            var updates = 0;
            foreach (var row in Tools)
            {
                row.IsChecking = true;
                row.LatestVersion = await _query.GetLatestVersionAsync(row.PackageId, InstallPrerelease);
                row.IsChecking = false;

                if (row.HasUpdate)
                {
                    updates++;
                }
            }

            SetStatus(updates == 0 ? "Everything is up to date." : $"{updates} tool(s) can be updated.");
        }, clearOutput: false);
    }

    public void OpenToolsFolder() => _dialogs.RevealInShell(_tools.ToolsDirectory);
    public void OpenStoreFolder() => _dialogs.RevealInShell(_tools.StoreDirectory);

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredTools));

    partial void OnToolsChanged(IReadOnlyList<ToolRow> value) => OnPropertyChanged(nameof(FilteredTools));
}

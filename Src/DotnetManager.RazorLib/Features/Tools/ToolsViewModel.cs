using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>
/// Every installed tool, in both scopes, plus the nuget.org search that installs new ones.
/// <para>
/// This replaces the separate local/global tool pages. Those two view models had drifted into
/// near-duplicates - identical update loops, installs that only differed by <c>--global</c> versus
/// <c>--local</c>, and an uninstall that confirmed in one scope but not the other - while a third page
/// did nothing but search nuget.org. One view model now owns all three, parameterized by
/// <see cref="ToolScope"/>, so the scope is a switch inside one page instead of a separate menu entry.
/// </para>
/// <para>
/// Local tools keep one dimension the scope switch alone cannot express: a directory tree can hold
/// several <c>dotnet-tools.json</c> manifests, so <see cref="ManifestPaths"/> is offered as a second
/// selector and local operations pass <c>--tool-manifest</c> explicitly rather than trusting the
/// working directory to imply the right manifest.
/// </para>
/// </summary>
public sealed partial class ToolsViewModel : ViewModelBase, IDisposable
{
    private readonly IGlobalToolService _globalTools;
    private readonly ILocalToolManifestService _manifests;
    private readonly INuGetPackageQuery _query;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _settings;
    private readonly ProjectTarget _target;
    private readonly IDotnetCli _cli;

    // ------------------------------------------------------------------ scope

    [ObservableProperty]
    private ToolScope scope = ToolScope.Global;

    // --------------------------------------------------- local manifest state

    [ObservableProperty]
    private IReadOnlyList<string> manifestPaths = [];

    [ObservableProperty]
    private string? selectedManifestPath;

    [ObservableProperty]
    private ToolManifest? manifest;

    // ------------------------------------------------------ installed tools

    [ObservableProperty]
    private IReadOnlyList<ToolRow> tools = [];

    [ObservableProperty]
    private string filter = string.Empty;

    // ------------------------------------------------------------- install

    [ObservableProperty]
    private string newPackageId = string.Empty;

    [ObservableProperty]
    private string newPackageVersion = string.Empty;

    [ObservableProperty]
    private bool installPrerelease;

    // -------------------------------------------------------- nuget search

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private bool includePrerelease;

    [ObservableProperty]
    private bool toolOnly = true;

    [ObservableProperty]
    private IReadOnlyList<PackageSearchResult> results = [];

    [ObservableProperty]
    private bool hasSearched;

    public ToolsViewModel(
        IGlobalToolService globalTools,
        ILocalToolManifestService manifests,
        INuGetPackageQuery query,
        IDialogService dialogs,
        IAppSettingsStore settings,
        ProjectTarget target,
        IDotnetCli cli)
    {
        _globalTools = globalTools;
        _manifests = manifests;
        _query = query;
        _dialogs = dialogs;
        _settings = settings;
        _target = target;
        _cli = cli;

        includePrerelease = settings.Current.IncludePrerelease;
        _target.Changed += HandleTargetChanged;
    }

    protected override IDotnetCli Cli => _cli;

    public void Dispose() => _target.Changed -= HandleTargetChanged;

    // ------------------------------------------------------------ projections

    public bool IsGlobal => Scope == ToolScope.Global;

    public bool IsLocal => !IsGlobal;

    /// <summary>The shared project target; local manifests are discovered below it.</summary>
    public string WorkingDirectory => _target.Directory;

    public string ToolsDirectory => _globalTools.ToolsDirectory;

    public string StoreDirectory => _globalTools.StoreDirectory;

    public IReadOnlyList<ToolRow> FilteredTools => string.IsNullOrWhiteSpace(Filter)
        ? Tools
        : Tools.Where(tool => tool.PackageId.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    public bool HasManifest => Manifest is not null;

    /// <summary>The directory holding the selected manifest: the project directory local commands run in.</summary>
    public string? ManifestDirectory
    {
        get
        {
            var path = Manifest?.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(path);
            return string.Equals(Path.GetFileName(directory), ".config", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(directory)
                : directory;
        }
    }

    /// <summary>Where <c>--local</c> installs land when the selected scope is local.</summary>
    public string LocalInstallDirectory => ManifestDirectory ?? WorkingDirectory;

    // -------------------------------------------------------------- lifecycle

    public async Task InitializeAsync()
    {
        await RefreshAsync();

        if (_settings.Current.AutoCheckToolUpdates && Tools.Count > 0)
        {
            await CheckUpdatesAsync();
        }
    }

    public Task RefreshAsync() => RunAsync(RefreshCoreAsync);

    /// <summary>Reloads without the busy guard, so mutations can refresh from inside their own scope.</summary>
    private async Task RefreshCoreAsync()
    {
        if (IsGlobal)
        {
            ManifestPaths = [];
            SelectedManifestPath = null;
            Manifest = null;
            NotifyManifestChanged();

            var installed = await _globalTools.ListAsync();
            Tools = installed
                .Select(tool => new ToolRow
                {
                    PackageId = tool.PackageId,
                    Version = tool.Version,
                    Commands = tool.Commands,
                })
                .ToList();

            SetStatus($"{Tools.Count} global tool(s) installed.");
            return;
        }

        ManifestPaths = _manifests.FindManifests(WorkingDirectory);

        if (ManifestPaths.Count == 0)
        {
            SelectedManifestPath = null;
            Manifest = null;
            Tools = [];
            NotifyManifestChanged();
            SetStatus("No tool manifest was found below this directory.");
            return;
        }

        var path = SelectedManifestPath is not null && ManifestPaths.Contains(SelectedManifestPath)
            ? SelectedManifestPath
            : ManifestPaths[0];

        await LoadManifestAsync(path);
    }

    public async Task SetScopeAsync(ToolScope value)
    {
        if (Scope == value)
        {
            return;
        }

        Scope = value;
        await RefreshAsync();
    }

    public async Task SelectManifestAsync(string path)
    {
        await RunAsync(async () =>
        {
            SelectedManifestPath = path;
            await LoadManifestAsync(path);
        });
    }

    private Task LoadManifestAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        SelectedManifestPath = path;
        var manifest = _manifests.Load(path);
        Manifest = manifest;
        NotifyManifestChanged();

        Tools = manifest.Tools
            .Select(tool => new ToolRow
            {
                PackageId = tool.PackageId,
                Version = tool.Version,
                Commands = tool.Commands,
                RollForward = tool.RollForward,
            })
            .ToList();

        if (!manifest.IsValid)
        {
            SetStatus(manifest.Error, isError: true);
        }
        else
        {
            SetStatus($"{Tools.Count} tool(s) in {manifest.Path}");
        }

        return Task.CompletedTask;
    }

    // ----------------------------------------------------------------- install

    /// <summary>Creates <c>.config/dotnet-tools.json</c> in the project target.</summary>
    public async Task CreateManifestAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _manifests.CreateManifestAsync(WorkingDirectory);
            SetStatus(result.Message, !result.Success);
            await RefreshCoreAsync();
        });
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

            var id = NewPackageId.Trim();
            var arguments = new List<string> { "tool", "install", id };
            string? workingDirectory = null;

            if (IsGlobal)
            {
                arguments.Add("--global");
            }
            else
            {
                arguments.Add("--local");

                // The CLI would otherwise create a manifest of its own in the project root, which is not
                // where "New manifest" puts it (.config/dotnet-tools.json). Point both at the same file.
                var manifestPath = await EnsureManifestAsync();
                if (manifestPath is null)
                {
                    return;
                }

                arguments.Add("--tool-manifest");
                arguments.Add(manifestPath);
                workingDirectory = ManifestDirectory;
            }

            if (!string.IsNullOrWhiteSpace(NewPackageVersion))
            {
                arguments.Add("--version");
                arguments.Add(NewPackageVersion.Trim());
            }

            if (InstallPrerelease)
            {
                arguments.Add("--prerelease");
            }

            var result = await RunCliAsync(
                arguments,
                $"dotnet tool install{(IsGlobal ? " --global" : " --local")} {id}",
                workingDirectory);

            SetStatus(result.Success ? $"Installed {id}." : $"Installing {id} failed.", !result.Success);

            if (result.Success)
            {
                NewPackageId = string.Empty;
                NewPackageVersion = string.Empty;
                await RefreshCoreAsync();
            }
        });
    }

    /// <summary>Installs a package picked from the search results, in the current scope.</summary>
    public async Task InstallFromSearchAsync(PackageSearchResult? package)
    {
        if (package is null || IsBusy)
        {
            return;
        }

        NewPackageId = package.Id;
        NewPackageVersion = package.LatestVersion ?? string.Empty;
        await InstallAsync();
    }

    public void OpenPackagePage(PackageSearchResult? package)
    {
        if (package is not null)
        {
            _dialogs.RevealInShell($"https://www.nuget.org/packages/{package.Id}");
        }
    }

    /// <summary>Returns the manifest to install into, creating the default one when none exists yet.</summary>
    private async Task<string?> EnsureManifestAsync()
    {
        if (Manifest is { IsValid: true } current)
        {
            return current.Path;
        }

        var created = await _manifests.CreateManifestAsync(WorkingDirectory);
        if (!created.Success)
        {
            SetStatus(created.Message, isError: true);
            return null;
        }

        ManifestPaths = _manifests.FindManifests(WorkingDirectory);
        var path = ManifestPaths.FirstOrDefault();
        if (path is null)
        {
            SetStatus(created.Message, isError: true);
            return null;
        }

        await LoadManifestAsync(path);
        return path;
    }

    // -------------------------------------------------------- update / remove

    public async Task UpdateAsync(ToolRow? row)
    {
        if (row is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                UpdateArguments(row.PackageId),
                $"dotnet tool update {row.PackageId}{(IsGlobal ? " --global" : " --local")}",
                IsGlobal ? null : ManifestDirectory);

            SetStatus(result.Success ? $"Updated {row.PackageId}." : $"Updating {row.PackageId} failed.", !result.Success);

            if (result.Success)
            {
                await RefreshCoreAsync();
            }
        });
    }

    public async Task UpdateAllAsync()
    {
        await RunAsync(async () =>
        {
            if (IsGlobal)
            {
                var globalResult = await RunCliAsync(
                    ["tool", "update", "--global", "--all"],
                    "dotnet tool update --global --all");

                await RefreshCoreAsync();
                SetStatus(
                    globalResult.Success ? "All global tools were updated." : "The update failed.",
                    !globalResult.Success);
                return;
            }

            // Local: the CLI supports --all here too, but only per manifest, and a tree can hold several.
            if (Manifest is not { IsValid: true } current)
            {
                SetStatus("No tool manifest was found below this directory.", isError: true);
                return;
            }

            var result = await RunCliAsync(
                ["tool", "update", "--local", "--all", "--tool-manifest", current.Path],
                "dotnet tool update --local --all",
                ManifestDirectory);

            await LoadManifestAsync(current.Path);
            SetStatus(result.Success ? "All tools in this manifest were updated." : "The update failed.", !result.Success);
        });
    }

    public async Task UninstallAsync(ToolRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Uninstall '{row.PackageId}'?", "Uninstall tool"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var arguments = new List<string> { "tool", "uninstall", row.PackageId };
            if (IsGlobal)
            {
                arguments.Add("--global");
            }
            else
            {
                arguments.Add("--local");

                if (Manifest is not { IsValid: true } current)
                {
                    SetStatus("No tool manifest was found below this directory.", isError: true);
                    return;
                }

                arguments.Add("--tool-manifest");
                arguments.Add(current.Path);
            }

            var result = await RunCliAsync(
                arguments,
                $"dotnet tool uninstall {row.PackageId}{(IsGlobal ? " --global" : " --local")}",
                IsGlobal ? null : ManifestDirectory);

            SetStatus(result.Success ? $"Uninstalled {row.PackageId}." : $"Uninstalling {row.PackageId} failed.", !result.Success);
            await RefreshCoreAsync();
        });
    }

    /// <summary>Drops the entry from the manifest without touching the restored tool (local only).</summary>
    public async Task RemoveFromManifestAsync(ToolRow? row)
    {
        if (row is null || Manifest is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _manifests.RemoveToolAsync(Manifest, row.PackageId);
            SetStatus(result.Message, !result.Success);
            await LoadManifestAsync(SelectedManifestPath);
        });
    }

    // ------------------------------------------------------------- local only

    public async Task RestoreAsync()
    {
        if (Manifest is not { IsValid: true } current)
        {
            SetStatus("No tool manifest was found below this directory.", isError: true);
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["tool", "restore", "--tool-manifest", current.Path],
                "dotnet tool restore",
                ManifestDirectory);

            SetStatus(result.Success ? "Tool restore finished." : "Tool restore failed.", !result.Success);
        });
    }

    public async Task RunToolAsync(ToolRow? row)
    {
        if (row is null || row.Commands.Count == 0)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["tool", "run", row.Commands[0]],
                $"dotnet tool run {row.Commands[0]}",
                ManifestDirectory);

            SetStatus(
                result.Success ? $"{row.Commands[0]} finished." : $"{row.Commands[0]} failed.",
                !result.Success);
        });
    }

    public async Task SetRollForwardAsync(ToolRow? row)
    {
        if (row is null || Manifest is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            // Three states, matching the manifest: absent -> "true" -> "false" -> absent again.
            var next = row.RollForward switch
            {
                null => "true",
                "true" => "false",
                "false" => null,
                _ => "true",
            };

            var result = await _manifests.SetRollForwardAsync(Manifest, row.PackageId, next);
            SetStatus(result.Message, !result.Success);
            await LoadManifestAsync(SelectedManifestPath);
        });
    }

    public void OpenManifestFolder()
    {
        var directory = ManifestDirectory;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _dialogs.RevealInShell(directory);
        }
    }

    public void OpenToolsFolder() => _dialogs.RevealInShell(_globalTools.ToolsDirectory);

    public void OpenStoreFolder() => _dialogs.RevealInShell(_globalTools.StoreDirectory);

    // ---------------------------------------------------------- update checks

    public async Task CheckUpdatesAsync()
    {
        await RunAsync(async () =>
        {
            var updates = 0;

            foreach (var row in Tools)
            {
                row.IsChecking = true;
                row.LatestVersion = await _query.GetLatestVersionAsync(row.PackageId, InstallPrerelease || IncludePrerelease);
                row.IsChecking = false;

                if (row.HasUpdate)
                {
                    updates++;
                }
            }

            SetStatus(updates == 0 ? "Everything is up to date." : $"{updates} tool(s) can be updated.");
        }, clearOutput: false);
    }

    /// <summary>Fills the install box with the newest version of the typed package id.</summary>
    public async Task SearchPackageAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewPackageId))
            {
                SetStatus("Enter a package id to look up.", isError: true);
                return;
            }

            var id = NewPackageId.Trim();
            var latest = await _query.GetLatestVersionAsync(id, InstallPrerelease);
            SetStatus(latest is null ? $"'{id}' was not found." : $"Latest version of {id}: {latest}");
        });
    }

    // ---------------------------------------------------------- nuget search

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

    // ------------------------------------------------------------ notifications

    partial void OnScopeChanged(ToolScope value)
    {
        OnPropertyChanged(nameof(IsGlobal));
        OnPropertyChanged(nameof(IsLocal));
    }

    partial void OnToolsChanged(IReadOnlyList<ToolRow> value) => OnPropertyChanged(nameof(FilteredTools));

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredTools));

    partial void OnIncludePrereleaseChanged(bool value)
    {
        _settings.Current.IncludePrerelease = value;
        _ = _settings.SaveAsync();
    }

    private void NotifyManifestChanged()
    {
        OnPropertyChanged(nameof(HasManifest));
        OnPropertyChanged(nameof(ManifestDirectory));
        OnPropertyChanged(nameof(LocalInstallDirectory));
    }

    private void HandleTargetChanged()
    {
        OnPropertyChanged(nameof(WorkingDirectory));
        OnPropertyChanged(nameof(LocalInstallDirectory));
        _ = RefreshAsync();
    }

    private IReadOnlyList<string> UpdateArguments(string packageId)
    {
        if (IsGlobal)
        {
            return ["tool", "update", packageId, "--global"];
        }

        var arguments = new List<string> { "tool", "update", packageId, "--local" };
        if (Manifest is { IsValid: true } current)
        {
            arguments.Add("--tool-manifest");
            arguments.Add(current.Path);
        }

        return arguments;
    }
}

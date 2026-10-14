using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>Manages the tools declared in <c>.config/dotnet-tools.json</c> manifests.</summary>
public sealed partial class LocalToolsViewModel : ViewModelBase, IDisposable
{
    private readonly ILocalToolManifestService _manifests;
    private readonly INuGetPackageQuery _query;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _settings;
    private readonly ProjectTarget _target;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<string> manifestPaths = [];

    [ObservableProperty]
    private string? selectedManifestPath;

    [ObservableProperty]
    private ToolManifest? manifest;

    [ObservableProperty]
    private IReadOnlyList<ToolRow> tools = [];

    [ObservableProperty]
    private string newPackageId = string.Empty;

    [ObservableProperty]
    private string newPackageVersion = string.Empty;

    [ObservableProperty]
    private bool installPrerelease;

    public LocalToolsViewModel(
        ILocalToolManifestService manifests,
        INuGetPackageQuery query,
        IDialogService dialogs,
        IAppSettingsStore settings,
        ProjectTarget target,
        IDotnetCli cli)
    {
        _manifests = manifests;
        _query = query;
        _dialogs = dialogs;
        _settings = settings;
        _target = target;
        _cli = cli;

        _target.Changed += HandleTargetChanged;
    }

    protected override IDotnetCli Cli => _cli;

    /// <summary>The shared project target; this page never keeps a directory of its own.</summary>
    public string WorkingDirectory => _target.Directory;

    public void Dispose() => _target.Changed -= HandleTargetChanged;

    private void HandleTargetChanged()
    {
        OnPropertyChanged(nameof(WorkingDirectory));
        _ = RefreshAsync();
    }

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

    public bool HasManifest => Manifest is not null;
    public async Task InitializeAsync()
    {
        await RefreshAsync();

        if (_settings.Current.AutoCheckToolUpdates && HasManifest)
        {
            await CheckUpdatesAsync();
        }
    }
    public Task RefreshAsync() => RunAsync(RefreshCoreAsync);

    /// <summary>Reloads manifests without the busy guard, so CreateManifestAsync can call it from its own scope.</summary>
    private async Task RefreshCoreAsync()
    {
        ManifestPaths = _manifests.FindManifests(WorkingDirectory);

        if (ManifestPaths.Count == 0)
        {
            SelectedManifestPath = null;
            Manifest = null;
            Tools = [];
            SetStatus("No tool manifest was found below this directory.");
            return;
        }

        var target = SelectedManifestPath is not null && ManifestPaths.Contains(SelectedManifestPath)
            ? SelectedManifestPath
            : ManifestPaths[0];

        await LoadManifestAsync(target);
    }

    public async Task SelectManifestAsync(string path)
    {
        await RunAsync(async () =>
        {
            SelectedManifestPath = path;
            await LoadManifestAsync(path);
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

            var arguments = new List<string> { "tool", "install", NewPackageId.Trim(), "--local" };
            if (!string.IsNullOrWhiteSpace(NewPackageVersion))
            {
                arguments.Add("--version");
                arguments.Add(NewPackageVersion.Trim());
            }

            if (InstallPrerelease)
            {
                arguments.Add("--prerelease");
            }

            var result = await RunCliAsync(arguments, $"dotnet tool install {NewPackageId}", ManifestDirectory);
            SetStatus(
                result.Success ? $"Installed {NewPackageId}." : "The installation failed, see the output above.",
                !result.Success);

            if (result.Success)
            {
                NewPackageId = string.Empty;
                NewPackageVersion = string.Empty;
                await LoadManifestAsync(SelectedManifestPath);
            }
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
                ["tool", "update", row.PackageId, "--local"],
                $"dotnet tool update {row.PackageId}",
                ManifestDirectory);

            SetStatus(result.Success ? $"Updated {row.PackageId}." : "The update failed.", !result.Success);
            if (result.Success)
            {
                await LoadManifestAsync(SelectedManifestPath);
            }
        });
    }

    public async Task UninstallAsync(ToolRow? row)
    {
        if (row is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await RunCliAsync(
                ["tool", "uninstall", row.PackageId, "--local"],
                $"dotnet tool uninstall {row.PackageId}",
                ManifestDirectory);

            SetStatus(result.Success ? $"Uninstalled {row.PackageId}." : "The uninstall failed.", !result.Success);
            await LoadManifestAsync(SelectedManifestPath);
        });
    }

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

    public async Task RestoreAsync()
    {
        await RunAsync(async () =>
        {
            var result = await RunCliAsync(["tool", "restore"], "dotnet tool restore", ManifestDirectory);
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
            var next = row.RollForward switch
            {
                null or "false" => "true",
                "true" => null,
                _ => null,
            };

            var result = await _manifests.SetRollForwardAsync(Manifest, row.PackageId, next);
            SetStatus(result.Message, !result.Success);
            await LoadManifestAsync(SelectedManifestPath);
        });
    }

    public async Task CheckUpdatesAsync()
    {
        await RunAsync(async () =>
        {
            var includePrerelease = InstallPrerelease;
            var updates = 0;

            foreach (var row in Tools)
            {
                row.IsChecking = true;
                row.LatestVersion = await _query.GetLatestVersionAsync(row.PackageId, includePrerelease);
                row.IsChecking = false;

                if (row.HasUpdate)
                {
                    updates++;
                }
            }

            SetStatus(updates == 0 ? "Everything is up to date." : $"{updates} tool(s) can be updated.");
        }, clearOutput: false);
    }

    public async Task UpdateAllAsync()
    {
        await RunAsync(async () =>
        {
            var pending = Tools.Where(row => row.HasUpdate).ToList();
            if (pending.Count == 0)
            {
                SetStatus("Everything is up to date.");
                return;
            }

            var failures = 0;
            foreach (var row in pending)
            {
                var result = await RunCliAsync(
                    ["tool", "update", row.PackageId, "--local"],
                    $"dotnet tool update {row.PackageId}",
                    ManifestDirectory);

                if (!result.Success)
                {
                    failures++;
                }
            }

            await LoadManifestAsync(SelectedManifestPath);
            SetStatus(failures == 0 ? $"Updated {pending.Count} tool(s)." : $"{failures} update(s) failed.", failures > 0);
        });
    }

    public async Task SearchPackageAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewPackageId))
            {
                SetStatus("Enter a package id to look up.", isError: true);
                return;
            }

            var latest = await _query.GetLatestVersionAsync(NewPackageId.Trim(), InstallPrerelease);
            SetStatus(latest is null
                ? $"'{NewPackageId}' was not found."
                : $"Latest version of {NewPackageId}: {latest}");
        });
    }

    private async Task LoadManifestAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedManifestPath = path;
        var manifest = _manifests.Load(path);
        Manifest = manifest;
        OnPropertyChanged(nameof(ManifestDirectory));
        OnPropertyChanged(nameof(HasManifest));

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
    }
}

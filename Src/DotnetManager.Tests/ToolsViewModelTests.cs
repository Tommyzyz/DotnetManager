using System.IO;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Features.Tools;
using DotnetManager.Tests.Fakes;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>
/// Covers the merged tools page: one view model now serves the global scope, the local (manifest) scope
/// and the nuget.org search. The scope switch is the behaviour these tests pin down, because it is what
/// replaced the separate local/global pages.
/// </summary>
public sealed class ToolsViewModelTests
{
    private const string LocalListOutput = """
        包 ID           版本         命令             清单
        --------------------------------------------------------------------------------------------------
        dotnetsay      3.0.3      dotnetsay      C:\proj\.config\dotnet-tools.json
        dotnet-ef      9.0.0      dotnet-ef      C:\proj\.config\dotnet-tools.json
        """;

    [Fact]
    public async Task The_global_scope_lists_the_machine_wide_tools_and_ignores_the_project_target()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);

        await h.RefreshAsync(ToolScope.Global);

        Assert.True(h.Vm.IsGlobal);
        Assert.False(h.Vm.IsLocal);
        Assert.Empty(h.Vm.ManifestPaths);
        Assert.Null(h.Vm.Manifest);
        Assert.Equal(["dotnetsay", "dotnet-ef"], h.Vm.Tools.Select(t => t.PackageId).ToList());
        // The list comes from IGlobalToolService, so the machine scope reads no manifest and runs no CLI.
        Assert.Empty(h.Cli.Invocations);
    }

    [Fact]
    public async Task The_local_scope_reads_the_manifest_below_the_project_target()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);

        await h.RefreshAsync(ToolScope.Local);

        Assert.True(h.Vm.IsLocal);
        Assert.Equal([h.ManifestPath], h.Vm.ManifestPaths);
        Assert.Equal(h.ManifestPath, h.Vm.SelectedManifestPath);
        Assert.True(h.Vm.HasManifest);
        // The manifest - not dotnet tool list - is the source of truth for local tools.
        Assert.Empty(h.Cli.Invocations);
        Assert.Equal(2, h.Vm.Tools.Count);
    }

    [Fact]
    public async Task Switching_the_scope_reloads_from_the_new_source()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Global);
        var globalTools = h.Vm.Tools.Select(t => t.PackageId).ToList();
        var globalManifestPaths = h.Vm.ManifestPaths;

        await h.Vm.SetScopeAsync(ToolScope.Local);

        Assert.Equal(["dotnetsay", "dotnet-ef"], globalTools);
        Assert.Empty(globalManifestPaths);
        Assert.Equal(h.ManifestPath, h.Vm.Manifest?.Path);
        Assert.Equal([h.ManifestPath], h.Vm.ManifestPaths);
        // The global list is gone: the local scope shows the manifest contents instead of both at once.
        Assert.Equal(2, h.Vm.Tools.Count);
    }

    [Fact]
    public async Task A_local_install_targets_the_selected_manifest_explicitly()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Local);
        h.Vm.NewPackageId = "dotnetsay";
        h.Vm.NewPackageVersion = "3.0.3";

        await h.Vm.InstallAsync();

        var install = Assert.Single(h.Cli.InvocationsStartingWith("tool"));
        Assert.Equal("install", install[1]);
        Assert.Contains("--local", install);
        // The manifest is passed explicitly, so the CLI cannot create a second one in the project root.
        Assert.Contains("--tool-manifest", install);
        Assert.Contains(h.ManifestPath, install);
        Assert.DoesNotContain("--global", install);
    }

    [Fact]
    public async Task A_global_install_never_touches_a_manifest()
    {
        using var h = new Harness();
        await h.RefreshAsync(ToolScope.Global);
        h.Vm.NewPackageId = "dotnetsay";

        await h.Vm.InstallAsync();

        var install = Assert.Single(h.Cli.InvocationsStartingWith("tool"));
        Assert.Contains("--global", install);
        Assert.DoesNotContain("--tool-manifest", install);
        Assert.DoesNotContain("--local", install);
    }

    [Fact]
    public async Task A_local_install_creates_the_manifest_when_the_project_has_none()
    {
        using var h = new Harness();
        await h.RefreshAsync(ToolScope.Local);
        Assert.False(h.Vm.HasManifest);
        h.Vm.NewPackageId = "dotnetsay";

        await h.Vm.InstallAsync();

        // Creation went through the manifest service, which writes .config/dotnet-tools.json.
        Assert.True(File.Exists(h.ManifestPath));
        Assert.Contains(h.Cli.Invocations, args => args.Contains("--tool-manifest") && args.Contains(h.ManifestPath));
    }

    [Fact]
    public async Task Update_all_covers_every_global_tool_in_one_command()
    {
        using var h = new Harness();
        await h.RefreshAsync(ToolScope.Global);

        await h.Vm.UpdateAllAsync();

        var update = Assert.Single(h.Cli.InvocationsStartingWith("tool"));
        Assert.Equal(["tool", "update", "--global", "--all"], update);
    }

    [Fact]
    public async Task Update_all_in_the_local_scope_is_scoped_to_the_selected_manifest()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Local);

        await h.Vm.UpdateAllAsync();

        var update = Assert.Single(h.Cli.InvocationsStartingWith("tool"));
        Assert.Contains("--local", update);
        Assert.Contains("--all", update);
        // A tree can hold several manifests, so --all has to be pinned to one file.
        Assert.Contains(h.ManifestPath, update);
    }

    [Fact]
    public async Task Uninstalling_asks_for_confirmation_in_both_scopes()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Global);
        var globalRow = h.Vm.Tools[0];

        await h.Vm.UninstallAsync(globalRow);

        h.Dialogs.ConfirmResult = false;
        await h.Vm.SetScopeAsync(ToolScope.Local);
        await h.Vm.UninstallAsync(h.Vm.Tools[0]);

        // Two prompts: one per scope. The old local page uninstalled without asking.
        Assert.Equal(2, h.Dialogs.ConfirmMessages.Count);
        var uninstalls = h.Cli.InvocationsStartingWith("tool").Where(a => a[1] == "uninstall").ToList();
        Assert.Single(uninstalls);
    }

    [Fact]
    public async Task An_entry_removed_from_the_manifest_leaves_the_tool_alone()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Local);

        await h.Vm.RemoveFromManifestAsync(h.Vm.Tools.First(t => t.PackageId == "dotnetsay"));

        // Editing the manifest is a file operation, so no CLI call happens.
        Assert.Empty(h.Cli.InvocationsStartingWith("tool"));
        Assert.Contains("dotnetsay", h.Manifests.Removed);
    }

    [Fact]
    public async Task The_search_half_of_the_page_still_queries_nuget_and_installs_into_the_current_scope()
    {
        using var h = new Harness();
        h.Query.Results = [new PackageSearchResult("dotnetsay", "3.0.3", "Say hello", "Author", 12, true)];
        await h.RefreshAsync(ToolScope.Global);
        h.Vm.QueryText = "dotnetsay";

        await h.Vm.SearchAsync();
        await h.Vm.InstallFromSearchAsync(h.Vm.Results[0]);

        Assert.True(h.Vm.HasSearched);
        var install = Assert.Single(h.Cli.InvocationsStartingWith("tool"));
        Assert.Contains("--global", install);
        Assert.Contains("3.0.3", install);
    }

    [Fact]
    public async Task Moving_the_project_target_reloads_the_local_scope()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Local);
        using var other = new TempDirectory();

        await h.Target.UseAsync(other.Path);

        Assert.Equal(other.Path, h.Vm.WorkingDirectory);
        // The old manifest is gone; the new directory has none.
        Assert.False(h.Vm.HasManifest);
    }

    [Fact]
    public async Task The_filter_narrows_the_installed_list_without_hiding_it_entirely()
    {
        using var h = new Harness();
        h.Manifests.Manifests.Add(h.ManifestPath);
        await h.RefreshAsync(ToolScope.Local);

        h.Vm.Filter = "EF";

        Assert.Equal(["dotnet-ef"], h.Vm.FilteredTools.Select(t => t.PackageId).ToList());

        h.Vm.Filter = "   ";

        Assert.Equal(2, h.Vm.FilteredTools.Count);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public Harness()
        {
            _dir.CreateDirectory(".config");
            ManifestPath = _dir.Combine(Path.Combine(".config", "dotnet-tools.json"));
            Settings.Current.LastWorkingDirectory = _dir.Path;
            Settings.Current.AutoCheckToolUpdates = false;
            Target = new ProjectTarget(Settings, Dialogs);
            Vm = new ToolsViewModel(Tools, Manifests, Query, Dialogs, Settings, Target, Cli);
        }

        public string ManifestPath { get; }

        public FakeDotnetCli Cli { get; } = new();

        public FakeDialogService Dialogs { get; } = new();

        public FakeAppSettingsStore Settings { get; } = new();

        public FakeGlobalToolService Tools { get; } = new();

        public FakeLocalToolManifestService Manifests { get; } = new();

        public FakeNuGetPackageQuery Query { get; } = new();

        public ProjectTarget Target { get; }

        public ToolsViewModel Vm { get; }

        public async Task RefreshAsync(ToolScope scope)
        {
            await Vm.SetScopeAsync(scope);
            await Vm.RefreshAsync();
        }

        public void Dispose() => _dir.Dispose();
    }

    /// <summary>Answers <c>dotnet tool list --global</c> with a fixed table.</summary>
    private sealed class FakeGlobalToolService : IGlobalToolService
    {
        public string ToolsDirectory => @"C:\Users\tester\.dotnet\tools";

        public string StoreDirectory => @"C:\Users\tester\.dotnet\tools\.store";

        public Task<IReadOnlyList<GlobalToolInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GlobalToolInfo>>(
            [
                new GlobalToolInfo("dotnetsay", "3.0.3", ["dotnetsay"]),
                new GlobalToolInfo("dotnet-ef", "9.0.0", ["dotnet-ef"]),
            ]);
    }

    /// <summary>An in-memory manifest store so the tests never touch a real .config directory.</summary>
    private sealed class FakeLocalToolManifestService : ILocalToolManifestService
    {
        public List<string> Manifests { get; } = [];

        public List<string> Removed { get; } = [];

        /// <summary>When set, creation is refused and the message is surfaced as the status.</summary>
        public string? CreateFailure { get; set; }

        public IReadOnlyList<string> FindManifests(string directory, int maxDepth = 6) =>
            [.. Manifests.Where(path => path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))];

        public ToolManifest Load(string manifestPath) => new()
        {
            Path = manifestPath,
            Version = 1,
            IsRoot = true,
            Tools =
            [
                new ManifestTool("dotnetsay", "3.0.3", ["dotnetsay"], null),
                new ManifestTool("dotnet-ef", "9.0.0", ["dotnet-ef"], "true"),
            ],
        };

        public Task<OperationResult> CreateManifestAsync(string directory, bool isRoot = true, CancellationToken cancellationToken = default)
        {
            if (CreateFailure is not null)
            {
                return Task.FromResult(OperationResult.Fail(CreateFailure));
            }

            var path = Path.Combine(directory, ".config", "dotnet-tools.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"version":1,"isRoot":true,"tools":{}}""");

            if (!Manifests.Contains(path))
            {
                Manifests.Add(path);
            }

            return Task.FromResult(OperationResult.Ok($"Created {path}"));
        }

        public Task<OperationResult> RemoveToolAsync(ToolManifest manifest, string packageId, CancellationToken cancellationToken = default)
        {
            Removed.Add(packageId);
            return Task.FromResult(OperationResult.Ok($"Updated {manifest.Path}"));
        }

        public Task<OperationResult> SetRollForwardAsync(ToolManifest manifest, string packageId, string? rollForward, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok($"Updated {manifest.Path}"));
    }

    private sealed class FakeNuGetPackageQuery : INuGetPackageQuery
    {
        public IReadOnlyList<PackageSearchResult> Results { get; set; } = [];

        public string? LatestVersion { get; set; } = "9.9.9";

        public Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestVersion);

        public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(
            string query,
            bool includePrerelease = false,
            bool toolOnly = true,
            int take = 25,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results);
    }
}

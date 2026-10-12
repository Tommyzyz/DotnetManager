using System.IO;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Features.NuGet;
using DotnetManager.Tests.Fakes;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>Drives <see cref="ProjectPackagesViewModel"/> through a fake CLI and fake dialogs.</summary>
public sealed class ProjectPackagesViewModelTests
{
    private const string NoProjects = """{"version":1,"projects":[]}""";

    private const string NuGetSourceFailure =
        "error: 无法加载源 https://api.nuget.org/v3/index.json 的服务索引。\nerror: The SSL connection could not be established.";

    [Fact]
    public async Task Refresh_collects_counts_and_flags_from_the_three_list_commands()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Outdated.Package", "1.0.0"), ("Vulnerable.Package", "2.0.0")),
            Outdated(project, ("Outdated.Package", "1.0.0", "1.0.3")),
            Vulnerable(project, ("Vulnerable.Package", "High", "GHSA-1234")));

        await h.RefreshAsync();

        Assert.Equal(2, h.Vm.TotalCount);
        Assert.Equal(1, h.Vm.OutdatedCount);
        Assert.Equal(1, h.Vm.VulnerableCount);
        Assert.Equal(0, h.Vm.UpToDateCount);
        // A found vulnerability is surfaced as an error-styled status.
        Assert.True(h.Vm.StatusIsError);
        Assert.Contains("2 个包引用", h.Vm.StatusMessage);
        Assert.Contains("1 个包存在已知安全漏洞", h.Vm.StatusMessage);
        Assert.Contains("可更新 1 个", h.Vm.StatusMessage);
    }

    [Fact]
    public async Task Refresh_orders_the_grid_with_the_most_severe_packages_first()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Z.Unsafe", "1.0.0"), ("A.Outdated", "1.0.0"), ("M.Clean", "3.0.0")),
            Outdated(project, ("A.Outdated", "1.0.0", "2.0.0")),
            Vulnerable(project, ("Z.Unsafe", "Critical", "GHSA-critical")));

        await h.RefreshAsync();

        var ids = h.Vm.FilteredPackages.Select(p => p.PackageId).ToList();
        Assert.Equal(new[] { "Z.Unsafe", "A.Outdated", "M.Clean" }, ids);
    }

    [Fact]
    public async Task Refresh_marks_the_status_as_error_when_a_remote_check_fails()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Some.Package", "1.0.0")),
            vulnerableResult: new CliResult(1, string.Empty, NuGetSourceFailure, TimeSpan.Zero));

        await h.RefreshAsync();

        Assert.True(h.Vm.StatusIsError);
        Assert.Contains("安全漏洞检查失败", h.Vm.StatusMessage);
        Assert.Contains("结果不完整", h.Vm.StatusMessage);
        // The packages are still listed - only the extra information is missing.
        Assert.Equal(1, h.Vm.TotalCount);
    }

    [Fact]
    public async Task Refresh_reports_when_no_package_is_referenced()
    {
        using var h = new Harness();
        h.Cli.Responder = Pipeline("""{"version":1,"problems":[{"text":"还原失败。","level":"error"}]}""");

        await h.RefreshAsync();

        Assert.Empty(h.Vm.Packages);
        Assert.Equal("未发现任何 NuGet 包引用。", h.Vm.StatusMessage);
    }

    [Fact]
    public async Task Refresh_streams_the_command_line_and_output_into_the_log_panel()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(Base(project, ("Some.Package", "1.0.0")));

        await h.RefreshAsync();

        Assert.True(h.Vm.HasOutput);
        Assert.Contains("> dotnet list", h.Vm.OutputText);
        Assert.Contains("Some.Package", h.Vm.OutputText);
    }

    [Fact]
    public async Task Refresh_runs_the_base_outdated_and_vulnerable_stages_once_per_project()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(Base(project, ("Some.Package", "1.0.0")));

        await h.RefreshAsync();

        Assert.Equal(3, h.Cli.InvocationsStartingWith("list").Count);
        Assert.Single(h.Cli.InvocationsContaining("--outdated"));
        Assert.Single(h.Cli.InvocationsContaining("--vulnerable"));
    }

    [Fact]
    public async Task Refresh_passes_the_transitive_and_prerelease_switches_through()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(Base(project, ("Some.Package", "1.0.0")));
        h.Vm.IncludeTransitive = true;
        h.Vm.IncludePrerelease = true;

        await h.RefreshAsync();

        var listCalls = h.Cli.InvocationsStartingWith("list");
        Assert.All(listCalls, args => Assert.Contains("--include-transitive", args));
        Assert.Contains("--include-prerelease", Assert.Single(h.Cli.InvocationsContaining("--outdated")));
        Assert.DoesNotContain("--include-prerelease", Assert.Single(h.Cli.InvocationsContaining("--vulnerable")));
    }

    [Theory]
    [InlineData(PackageFilterMode.VulnerableOnly, "Vulnerable.Package")]
    [InlineData(PackageFilterMode.OutdatedOnly, "Outdated.Package")]
    [InlineData(PackageFilterMode.UpToDateOnly, "Clean.Package")]
    public async Task FilterMode_restricts_the_grid(PackageFilterMode mode, string expected)
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Vulnerable.Package", "1.0.0"), ("Outdated.Package", "1.0.0"), ("Clean.Package", "1.0.0")),
            Outdated(project, ("Outdated.Package", "1.0.0", "1.0.3")),
            Vulnerable(project, ("Vulnerable.Package", "Moderate", "GHSA-1")));
        await h.RefreshAsync();

        h.Vm.SetFilter(mode);

        Assert.Equal(expected, Assert.Single(h.Vm.FilteredPackages).PackageId);
    }

    [Fact]
    public async Task Transitive_filter_separates_direct_references_from_transitive_ones()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            $$"""
            {"version":1,"projects":[{"path":"{{Json(project)}}","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Direct.Package","resolvedVersion":"1.0.0"}],"transitivePackages":[{"id":"Transitive.Package","resolvedVersion":"2.0.0"}]}]}]}
            """);

        await h.RefreshAsync();

        h.Vm.SetFilter(PackageFilterMode.TransitiveOnly);
        Assert.Equal("Transitive.Package", Assert.Single(h.Vm.FilteredPackages).PackageId);

        h.Vm.SetFilter(PackageFilterMode.TopLevelOnly);
        Assert.Equal("Direct.Package", Assert.Single(h.Vm.FilteredPackages).PackageId);
    }

    [Theory]
    [InlineData("vulnerable", "Vulnerable.Package")]
    [InlineData("GHSA-9", "Vulnerable.Package")]
    [InlineData("outdated", "Outdated.Package")]
    [InlineData("App", "Vulnerable.Package")]
    public async Task Search_matches_package_ids_advisories_and_project_names(string query, string expected)
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Vulnerable.Package", "1.0.0"), ("Outdated.Package", "1.0.0")),
            Outdated(project, ("Outdated.Package", "1.0.0", "1.0.3")),
            Vulnerable(project, ("Vulnerable.Package", "High", "GHSA-9")));
        await h.RefreshAsync();

        h.Vm.SearchText = query;

        Assert.Contains(expected, h.Vm.FilteredPackages.Select(p => p.PackageId));
    }

    [Fact]
    public async Task Search_that_matches_nothing_yields_an_empty_grid()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(Base(project, ("Some.Package", "1.0.0")));
        await h.RefreshAsync();

        h.Vm.SearchText = "does-not-exist";

        Assert.Empty(h.Vm.FilteredPackages);
        Assert.Equal(1, h.Vm.TotalCount);
    }

    [Fact]
    public async Task UpgradePackage_adds_the_latest_version_to_the_project_file()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Outdated.Package", "1.0.0")),
            Outdated(project, ("Outdated.Package", "1.0.0", "1.0.3")));
        await h.RefreshAsync();

        await h.Vm.UpgradePackageAsync(Assert.Single(h.Vm.Packages));

        var add = Assert.Single(h.Cli.InvocationsStartingWith("add"));
        Assert.Equal(new[] { "add", Json(project), "package", "Outdated.Package", "--version", "1.0.3" }, add);
        Assert.False(h.Vm.StatusIsError);
        Assert.Contains("成功升级", h.Vm.StatusMessage);
        // The list is reloaded after the upgrade (three list stages run once more).
        Assert.Equal(6, h.Cli.InvocationsStartingWith("list").Count);
    }

    [Fact]
    public async Task UpgradePackage_is_ignored_when_the_package_needs_no_upgrade()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(Base(project, ("Clean.Package", "1.0.0")));
        await h.RefreshAsync();

        await h.Vm.UpgradePackageAsync(Assert.Single(h.Vm.Packages));

        Assert.Empty(h.Cli.InvocationsStartingWith("add"));
    }

    [Fact]
    public async Task UpgradeAll_confirms_first_and_upgrades_every_outdated_package_once()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        // A.Package is listed once per targeted framework; the bulk upgrade must touch it once.
        h.Cli.Responder = Pipeline(
            """
            {"version":1,"projects":[{"path":"__PATH__","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"A.Package","resolvedVersion":"1.0.0"}]},{"framework":"net9.0","topLevelPackages":[{"id":"A.Package","resolvedVersion":"1.0.0"}]},{"framework":"net10.0","topLevelPackages":[{"id":"B.Package","resolvedVersion":"1.0.0"}]}]}]}
            """.Replace("__PATH__", Json(project)),
            """
            {"version":1,"projects":[{"path":"__PATH__","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"A.Package","resolvedVersion":"1.0.0","latestVersion":"1.5.0"}]},{"framework":"net9.0","topLevelPackages":[{"id":"A.Package","resolvedVersion":"1.0.0","latestVersion":"1.5.0"}]},{"framework":"net10.0","topLevelPackages":[{"id":"B.Package","resolvedVersion":"1.0.0","latestVersion":"2.2.0"}]}]}]}
            """.Replace("__PATH__", Json(project)));
        await h.RefreshAsync();

        Assert.Equal(3, h.Vm.OutdatedCount);

        await h.Vm.UpgradeAllAsync();

        Assert.Single(h.Dialogs.ConfirmMessages);
        var added = h.Cli.InvocationsStartingWith("add");
        Assert.Equal(2, added.Count);
        Assert.Contains(added, a => a.Contains("A.Package") && a.Contains("1.5.0"));
        Assert.Contains(added, a => a.Contains("B.Package") && a.Contains("2.2.0"));
        Assert.Contains("成功 2 个", h.Vm.StatusMessage);
    }

    [Fact]
    public async Task UpgradeAll_does_nothing_when_the_user_declines()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            Base(project, ("Outdated.Package", "1.0.0")),
            Outdated(project, ("Outdated.Package", "1.0.0", "1.0.3")));
        await h.RefreshAsync();
        h.Dialogs.ConfirmResult = false;

        await h.Vm.UpgradeAllAsync();

        Assert.Empty(h.Cli.InvocationsStartingWith("add"));
        Assert.Single(h.Dialogs.ConfirmMessages);
    }

    [Fact]
    public async Task UpgradeAll_leaves_transitive_packages_alone_unless_they_are_included()
    {
        using var h = new Harness();
        var project = h.CreateProject("App.csproj");
        h.Cli.Responder = Pipeline(
            """
            {"version":1,"projects":[{"path":"__PATH__","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Direct.Package","resolvedVersion":"1.0.0"}],"transitivePackages":[{"id":"Transitive.Package","resolvedVersion":"1.0.0"}]}]}]}
            """.Replace("__PATH__", Json(project)),
            """
            {"version":1,"projects":[{"path":"__PATH__","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Direct.Package","resolvedVersion":"1.0.0","latestVersion":"1.1.0"}],"transitivePackages":[{"id":"Transitive.Package","resolvedVersion":"1.0.0","latestVersion":"1.1.0"}]}]}]}
            """.Replace("__PATH__", Json(project)));
        await h.RefreshAsync();

        await h.Vm.UpgradeAllAsync();
        Assert.Single(h.Cli.InvocationsStartingWith("add"));
        Assert.Contains("Direct.Package", Assert.Single(h.Cli.InvocationsStartingWith("add")));

        h.Cli.Invocations.Clear();
        h.Vm.IncludeTransitive = true;
        await h.RefreshAsync();

        await h.Vm.UpgradeAllAsync();

        var added = h.Cli.InvocationsStartingWith("add");
        Assert.Equal(2, added.Count);
        Assert.Contains(added, a => a.Contains("Transitive.Package"));
    }

    [Fact]
    public async Task Picking_a_folder_moves_the_shared_target_for_every_page_that_follows_it()
    {
        using var h = new Harness();
        using var picked = new TempDirectory();

        // A second page bound to the same target, the way each shell page has its own view model.
        using var otherPage = new ProjectPackagesViewModel(h.Cli, h.Dialogs, h.Target);
        var notified = 0;
        h.Target.Changed += () => notified++;

        h.Dialogs.FolderToPick = picked.Path;
        await h.Target.PickAsync("Select the project directory");

        Assert.Equal(picked.Path, h.Vm.WorkingDirectory);
        Assert.Equal(picked.Path, otherPage.WorkingDirectory);
        Assert.Equal(picked.Path, h.Settings.Current.LastWorkingDirectory);
        Assert.Equal(1, h.Settings.SaveCount);
        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task Picking_a_project_file_keeps_the_containing_folder_as_the_working_directory()
    {
        using var h = new Harness();
        using var dir = new TempDirectory();
        var project = Path.Combine(dir.Path, "Picked.csproj");
        File.WriteAllText(project, "<Project />");
        h.Dialogs.FileToPick = project;

        await h.Vm.PickFileAsync();

        Assert.Equal(dir.Path, h.Vm.WorkingDirectory);
        Assert.Equal(project, h.Vm.SelectedTargetPath);
    }

    [Fact]
    public async Task Refreshing_a_directory_that_does_not_exist_reports_an_error()
    {
        using var h = new Harness();

        // The shared target keeps the path after the directory disappears behind its back.
        h.Dir.Dispose();

        await h.Vm.RefreshAsync();

        Assert.True(h.Vm.StatusIsError);
        Assert.Equal("指定的工作目录不存在。", h.Vm.StatusMessage);
        Assert.Empty(h.Cli.Invocations);
    }

    [Fact]
    public async Task OpenCurrentFolder_reveals_the_working_directory()
    {
        using var h = new Harness();

        h.Vm.OpenCurrentFolder();

        Assert.Equal(h.Dir.Path, Assert.Single(h.Dialogs.RevealedPaths));
    }

    // ---------------------------------------------------------------- helpers

    private static string Json(string path) => path.Replace('\\', '/');

    private static string Base(string project, params (string Id, string Version)[] packages)
    {
        var items = packages.Select(p => $$"""{"id":"{{p.Id}}","requestedVersion":"{{p.Version}}","resolvedVersion":"{{p.Version}}"}""");
        return $$"""
            {"version":1,"parameters":"","projects":[{"path":"{{Json(project)}}","frameworks":[{"framework":"net8.0","topLevelPackages":[{{string.Join(',', items)}}]}]}]}
            """;
    }

    private static string Outdated(string project, params (string Id, string Resolved, string Latest)[] packages)
    {
        var items = packages.Select(p => $$"""{"id":"{{p.Id}}","resolvedVersion":"{{p.Resolved}}","latestVersion":"{{p.Latest}}"}""");
        return $$"""
            {"version":1,"parameters":"--outdated","projects":[{"path":"{{Json(project)}}","frameworks":[{"framework":"net8.0","topLevelPackages":[{{string.Join(',', items)}}]}]}]}
            """;
    }

    private static string Vulnerable(string project, params (string Id, string Severity, string Advisory)[] packages)
    {
        var items = packages.Select(p => $$"""{"id":"{{p.Id}}","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"{{p.Severity}}","advisoryurl":"https://github.com/advisories/{{p.Advisory}}","id":"{{p.Advisory}}"}]}""");
        return $$"""
            {"version":1,"parameters":"--vulnerable","projects":[{"path":"{{Json(project)}}","frameworks":[{"framework":"net8.0","topLevelPackages":[{{string.Join(',', items)}}]}]}]}
            """;
    }

    /// <summary>Answers the base / outdated / vulnerable stages, and package additions.</summary>
    private static Func<IReadOnlyList<string>, CliOptions?, CliResult> Pipeline(
        string? @base = null,
        string? outdated = null,
        string? vulnerable = null,
        CliResult? outdatedResult = null,
        CliResult? vulnerableResult = null)
    {
        return (args, _) =>
        {
            if (args.Contains("--outdated"))
            {
                return outdatedResult ?? new CliResult(0, outdated ?? NoProjects, string.Empty, TimeSpan.Zero);
            }

            if (args.Contains("--vulnerable"))
            {
                return vulnerableResult ?? new CliResult(0, vulnerable ?? NoProjects, string.Empty, TimeSpan.Zero);
            }

            if (args.Count > 0 && args[0] == "add")
            {
                return new CliResult(0, "已将包引用添加到项目。", string.Empty, TimeSpan.Zero);
            }

            return new CliResult(0, @base ?? NoProjects, string.Empty, TimeSpan.Zero);
        };
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Settings.Current.LastWorkingDirectory = Dir.Path;
            Target = new ProjectTarget(Settings, Dialogs);
            Vm = new ProjectPackagesViewModel(Cli, Dialogs, Target);
        }

        public TempDirectory Dir { get; } = new();

        public FakeDotnetCli Cli { get; } = new();

        public FakeDialogService Dialogs { get; } = new();

        public FakeAppSettingsStore Settings { get; } = new();

        /// <summary>The shared project target the view model reads its directory from.</summary>
        public ProjectTarget Target { get; }

        public ProjectPackagesViewModel Vm { get; }

        /// <summary>Creates a project file and returns its full path.</summary>
        public string CreateProject(string fileName)
        {
            var path = Dir.WriteFile(fileName, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Vm.ScanTargets();
            return path;
        }

        /// <summary>Discovers the project targets and then runs the three checks.</summary>
        public Task RefreshAsync()
        {
            Vm.ScanTargets();
            return Vm.RefreshAsync();
        }

        public void Dispose() => Dir.Dispose();
    }
}

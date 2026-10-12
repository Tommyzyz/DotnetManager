using System.IO;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Features.NuGet;
using DotnetManager.Tests.Fakes;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>Covers how project targets are discovered below the selected directory.</summary>
public sealed class ProjectTargetsTests
{
    [Fact]
    public void ScanTargets_finds_projects_and_solutions_below_the_working_directory()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("App.csproj");
        dir.WriteFile(@"src\Lib\Lib.csproj");
        dir.WriteFile("Solution.sln");
        var vm = CreateViewModel(dir);

        vm.ScanTargets();

        Assert.Contains(vm.AvailableTargets, t => t.IsSolution && t.TargetPath!.EndsWith("Solution.sln"));
        // The "all projects" entry carries no path, so null has to be allowed for.
        Assert.Contains(vm.AvailableTargets, t => !t.IsSolution && t.TargetPath is not null && t.TargetPath.EndsWith("App.csproj"));
        Assert.Contains(vm.AvailableTargets, t => !t.IsSolution && t.TargetPath is not null && t.TargetPath.EndsWith("Lib.csproj"));
    }

    [Fact]
    public void ScanTargets_ignores_projects_inside_build_and_dependency_folders()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("App.csproj");
        dir.WriteFile(@"obj\Generated.csproj");
        dir.WriteFile(@"bin\Copied.csproj");
        dir.WriteFile(@"node_modules\Package\Bundled.csproj");
        var vm = CreateViewModel(dir);

        vm.ScanTargets();

        var projectPaths = vm.AvailableTargets
            .Where(t => !t.IsSolution && t.TargetPath is not null)
            .Select(t => t.TargetPath!)
            .ToList();

        Assert.Equal(["App.csproj"], projectPaths.Select(Path.GetFileName).ToList());
    }

    [Fact]
    public void ScanTargets_only_offers_the_all_projects_entry_when_several_projects_exist()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("A.csproj");
        var single = CreateViewModel(dir);

        single.ScanTargets();

        Assert.DoesNotContain(single.AvailableTargets, t => t.TargetPath is null);

        dir.WriteFile("B.csproj");
        var both = CreateViewModel(dir);

        both.ScanTargets();

        var all = Assert.Single(both.AvailableTargets, t => t.TargetPath is null);
        Assert.False(all.IsSolution);
        Assert.Contains("2", all.DisplayName);
    }

    [Fact]
    public void ScanTargets_selects_the_first_target_and_keeps_a_valid_selection()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("A.csproj");
        var vm = CreateViewModel(dir);

        vm.ScanTargets();

        var selected = vm.SelectedTargetPath;
        Assert.NotNull(selected);
        Assert.EndsWith("A.csproj", selected);

        // Scanning again must not lose the selection.
        vm.ScanTargets();
        Assert.Equal(selected, vm.SelectedTargetPath);
    }

    [Fact]
    public void ScanTargets_replaces_a_selection_that_no_longer_exists()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("A.csproj");
        var vm = CreateViewModel(dir);
        vm.ScanTargets();
        vm.SelectedTargetPath = Path.Combine(dir.Path, "Deleted.csproj");

        vm.ScanTargets();

        Assert.EndsWith("A.csproj", vm.SelectedTargetPath);
    }

    [Fact]
    public void ScanTargets_on_an_empty_directory_yields_no_target()
    {
        using var dir = new TempDirectory();
        var vm = CreateViewModel(dir);

        vm.ScanTargets();

        Assert.Empty(vm.AvailableTargets);
        Assert.Null(vm.SelectedTargetPath);
    }

    [Fact]
    public void All_projects_entry_runs_every_project_when_it_is_selected()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("A.csproj");
        dir.WriteFile("B.csproj");
        var vm = CreateViewModel(dir);
        vm.ScanTargets();

        // Selecting the entry without a path means "everything that was found".
        Assert.Null(vm.SelectedTargetPath);
        Assert.Equal(2, vm.AvailableTargets.Count(t => !t.IsSolution && t.TargetPath is not null));
    }

    [Fact]
    public void ScanTargets_tolerates_a_working_directory_that_does_not_exist()
    {
        var dir = new TempDirectory();
        var target = CreateTarget(dir);

        // The shared target still points at a directory that is gone by the time the scan runs.
        dir.Dispose();

        var vm = CreateViewModel(target);

        vm.ScanTargets();

        Assert.Empty(vm.AvailableTargets);
    }

    private static ProjectPackagesViewModel CreateViewModel(TempDirectory dir) =>
        CreateViewModel(CreateTarget(dir));

    private static ProjectPackagesViewModel CreateViewModel(ProjectTarget target) =>
        new(new FakeDotnetCli(), new FakeDialogService(), target);

    /// <summary>The shared project target, seeded from a throw-away directory.</summary>
    private static ProjectTarget CreateTarget(TempDirectory dir)
    {
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = dir.Path;
        return new ProjectTarget(settings, new FakeDialogService());
    }
}

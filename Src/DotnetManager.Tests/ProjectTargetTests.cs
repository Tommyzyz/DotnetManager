using System.IO;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.Tests.Fakes;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>
/// Covers the shared "current project" that the project-scoped pages work against: it owns the
/// directory, persists it, and is the one place that tells the pages to reload.
/// </summary>
public sealed class ProjectTargetTests
{
    [Fact]
    public void It_starts_on_the_persisted_directory()
    {
        using var dir = new TempDirectory();

        var target = CreateTarget(dir.Path);

        Assert.Equal(dir.Path, target.Directory);
    }

    [Fact]
    public void It_falls_back_when_the_persisted_directory_is_gone()
    {
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DotnetManager.Tests",
            "gone-" + Guid.NewGuid().ToString("N"));

        var target = new ProjectTarget(settings, new FakeDialogService());

        Assert.Equal(Environment.CurrentDirectory, target.Directory);
    }

    [Fact]
    public async Task Adopting_a_directory_persists_it_and_notifies_once()
    {
        using var dir = new TempDirectory();
        using var other = new TempDirectory();
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = dir.Path;
        var target = new ProjectTarget(settings, new FakeDialogService());
        var notified = 0;
        target.Changed += () => notified++;

        Assert.True(await target.UseAsync(other.Path));

        Assert.Equal(other.Path, target.Directory);
        Assert.Equal(other.Path, settings.Current.LastWorkingDirectory);
        Assert.Equal(1, settings.SaveCount);
        Assert.Equal(1, notified);

        // Re-adopting the same directory is not a change, so nothing is saved or reloaded.
        Assert.False(await target.UseAsync(other.Path));

        Assert.Equal(1, settings.SaveCount);
        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task A_directory_that_does_not_exist_is_ignored()
    {
        using var dir = new TempDirectory();
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = dir.Path;
        var target = new ProjectTarget(settings, new FakeDialogService());
        var notified = 0;
        target.Changed += () => notified++;

        Assert.False(await target.UseAsync(System.IO.Path.Combine(dir.Path, "missing")));

        Assert.Equal(dir.Path, target.Directory);
        Assert.Equal(dir.Path, settings.Current.LastWorkingDirectory);
        Assert.Equal(0, settings.SaveCount);
        Assert.Equal(0, notified);
    }

    [Fact]
    public async Task Picking_adopts_what_the_dialog_returned()
    {
        using var dir = new TempDirectory();
        using var picked = new TempDirectory();
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = dir.Path;
        var dialogs = new FakeDialogService { FolderToPick = picked.Path };
        var target = new ProjectTarget(settings, dialogs);

        Assert.True(await target.PickAsync("Select the project directory"));

        Assert.Equal(picked.Path, target.Directory);
    }

    [Fact]
    public async Task Picking_does_nothing_when_the_dialog_is_cancelled()
    {
        using var dir = new TempDirectory();
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = dir.Path;
        var target = new ProjectTarget(settings, new FakeDialogService { FolderToPick = null });

        Assert.False(await target.PickAsync("Select the project directory"));

        Assert.Equal(dir.Path, target.Directory);
        Assert.Equal(0, settings.SaveCount);
    }

    private static ProjectTarget CreateTarget(string directory)
    {
        var settings = new FakeAppSettingsStore();
        settings.Current.LastWorkingDirectory = directory;
        return new ProjectTarget(settings, new FakeDialogService());
    }
}

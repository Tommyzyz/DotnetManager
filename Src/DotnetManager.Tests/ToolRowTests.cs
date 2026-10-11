using DotnetManager.RazorLib.Features.Tools;
using Xunit;

namespace DotnetManager.Tests;

public sealed class ToolRowTests
{
    [Fact]
    public void HasUpdate_is_true_only_when_a_different_latest_version_is_known()
    {
        var row = Row("1.0.0");

        Assert.False(row.HasUpdate);

        row.LatestVersion = "1.0.0";
        Assert.False(row.HasUpdate);

        row.LatestVersion = "1.0.1";
        Assert.True(row.HasUpdate);

        row.LatestVersion = "  ";
        Assert.False(row.HasUpdate);
    }

    [Fact]
    public void HasUpdate_ignores_version_casing()
    {
        var row = Row("1.0.0-BETA");

        row.LatestVersion = "1.0.0-beta";

        Assert.False(row.HasUpdate);
    }

    [Fact]
    public void Setting_the_latest_version_raises_change_notifications_for_the_grid()
    {
        var row = Row("1.0.0");
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.LatestVersion = "2.0.0";

        Assert.Contains(nameof(ToolRow.LatestVersion), changed);
        Assert.Contains(nameof(ToolRow.HasUpdate), changed);
    }

    [Fact]
    public void CommandText_joins_the_commands_and_is_empty_without_any()
    {
        Assert.Equal("dotnet-ef", Row("1.0.0", "dotnet-ef").CommandText);
        Assert.Equal("dotnet ef", Row("1.0.0", "dotnet", "ef").CommandText);
        Assert.Equal(string.Empty, Row("1.0.0").CommandText);
    }

    [Fact]
    public void IsChecking_defaults_to_false()
    {
        Assert.False(Row("1.0.0").IsChecking);
    }

    private static ToolRow Row(string version, params string[] commands) => new()
    {
        PackageId = "Sample.Tool",
        Version = version,
        Commands = commands,
    };
}

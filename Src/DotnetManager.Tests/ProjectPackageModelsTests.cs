using DotnetManager.RazorLib.Features.NuGet;
using Xunit;

namespace DotnetManager.Tests;

public sealed class ProjectPackageModelsTests
{
    [Theory]
    [InlineData("Critical", 4)]
    [InlineData("critical", 4)]
    [InlineData("High", 3)]
    [InlineData("Moderate", 2)]
    [InlineData("Medium", 2)]
    [InlineData("Low", 1)]
    [InlineData("unknown", 0)]
    [InlineData("", 0)]
    public void SeverityWeight_ranks_the_severities_nuget_reports(string severity, int expected)
    {
        var vulnerability = new PackageVulnerability(severity, null, null);

        Assert.Equal(expected, vulnerability.SeverityWeight);
    }

    [Fact]
    public void SeverityWeight_tolerates_a_missing_severity()
    {
        var vulnerability = new PackageVulnerability(null!, null, null);

        Assert.Equal(0, vulnerability.SeverityWeight);
    }

    [Fact]
    public void IsOutdated_compares_the_resolved_version_with_the_latest()
    {
        var outdated = Item(resolved: "13.0.1", latest: "13.0.3");
        var current = Item(resolved: "13.0.3", latest: "13.0.3");
        var unknown = Item(resolved: "13.0.1", latest: null);

        Assert.True(outdated.IsOutdated);
        Assert.False(current.IsOutdated);
        Assert.False(unknown.IsOutdated);
    }

    [Fact]
    public void IsOutdated_ignores_version_casing()
    {
        var item = Item(resolved: "13.0.3-BETA1", latest: "13.0.3-beta1");

        Assert.False(item.IsOutdated);
    }

    [Fact]
    public void CanUpgrade_requires_a_reason_and_a_manually_referenced_package()
    {
        var outdated = Item(resolved: "1.0.0", latest: "2.0.0");
        var vulnerable = Item(resolved: "1.0.0", latest: null);
        vulnerable.Vulnerabilities.Add(new PackageVulnerability("High", null, null));
        var clean = Item(resolved: "1.0.0", latest: "1.0.0");
        var autoReferenced = Item(resolved: "1.0.0", latest: "2.0.0");
        autoReferenced.IsAutoReferenced = true;

        Assert.True(outdated.CanUpgrade);
        Assert.True(vulnerable.CanUpgrade);
        Assert.False(clean.CanUpgrade);
        Assert.False(autoReferenced.CanUpgrade);
    }

    [Fact]
    public void MaxSeverity_picks_the_most_severe_advisory()
    {
        var item = Item();
        item.Vulnerabilities.Add(new PackageVulnerability("Low", null, null));
        item.Vulnerabilities.Add(new PackageVulnerability("Critical", null, null));
        item.Vulnerabilities.Add(new PackageVulnerability("Moderate", null, null));

        Assert.Equal("Critical", item.MaxSeverity);
        Assert.Equal(4, item.MaxSeverityWeight);
        Assert.True(item.HasVulnerabilities);
    }

    [Fact]
    public void MaxSeverity_is_empty_for_a_clean_package()
    {
        var item = Item();

        Assert.Null(item.MaxSeverity);
        Assert.Equal(0, item.MaxSeverityWeight);
        Assert.False(item.HasVulnerabilities);
    }

    [Fact]
    public void ProjectName_is_derived_from_the_project_path()
    {
        var item = Item();
        item.ProjectPath = @"C:\work\Sample\Sample.App.csproj";

        Assert.Equal("Sample.App", item.ProjectName);
    }

    [Fact]
    public void ProjectName_is_empty_without_a_path()
    {
        Assert.Equal(string.Empty, Item().ProjectName);
    }

    private static ProjectPackageItem Item(string? resolved = null, string? latest = null) => new()
    {
        PackageId = "Sample.Package",
        ResolvedVersion = resolved,
        LatestVersion = latest,
    };
}

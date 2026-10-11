using DotnetManager.RazorLib.Features.NuGet;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>
/// Covers the JSON dialects that <c>dotnet list package</c> produces for the base list and for the
/// <c>--outdated</c> / <c>--vulnerable</c> variants, plus the degraded inputs that appear when the
/// command fails (restore messages, error payloads, truncated output).
/// </summary>
public sealed class ProjectPackagesJsonParsingTests
{
    private const string BaseJson = """
        {"version":1,"parameters":"","projects":[{"path":"C:/proj/A.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Newtonsoft.Json","requestedVersion":"13.*","resolvedVersion":"13.0.1"},{"id":"ILCompiler","requestedVersion":"[1.0, )","resolvedVersion":"1.0.0","autoReferenced":"true"}],"transitivePackages":[{"id":"Transitive.One","resolvedVersion":"2.0.0"}]}]}]}
        """;

    [Fact]
    public void ParseBase_reads_top_level_transitive_and_auto_referenced_packages()
    {
        var packages = ParseBase(BaseJson);

        Assert.Equal(3, packages.Count);

        var json = Assert.Single(packages, p => p.PackageId == "Newtonsoft.Json");
        Assert.Equal("13.*", json.RequestedVersion);
        Assert.Equal("13.0.1", json.ResolvedVersion);
        Assert.Equal("net8.0", json.Framework);
        Assert.Equal("C:/proj/A.csproj", json.ProjectPath);
        Assert.False(json.IsTransitive);
        Assert.False(json.IsAutoReferenced);

        var compiler = Assert.Single(packages, p => p.PackageId == "ILCompiler");
        Assert.True(compiler.IsAutoReferenced);

        var transitive = Assert.Single(packages, p => p.PackageId == "Transitive.One");
        Assert.True(transitive.IsTransitive);
        Assert.Equal("2.0.0", transitive.ResolvedVersion);
        Assert.Null(transitive.RequestedVersion);
    }

    [Fact]
    public void ParseBase_falls_back_to_the_requested_target_when_the_json_has_no_path()
    {
        var packages = ParseBase("""{"version":1,"projects":[{"frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"X","resolvedVersion":"1.0.0"}]}]}]}""");

        Assert.Equal("C:/fallback.csproj", Assert.Single(packages).ProjectPath);
    }

    [Fact]
    public void ParseBase_does_not_add_the_same_package_twice()
    {
        var packages = new List<ProjectPackageItem>();

        ProjectPackagesViewModel.ParseBasePackagesJson(BaseJson, packages, "C:/fallback.csproj");
        ProjectPackagesViewModel.ParseBasePackagesJson(BaseJson, packages, "C:/fallback.csproj");

        Assert.Equal(3, packages.Count);
    }

    [Fact]
    public void ParseBase_skips_entries_without_an_id()
    {
        var packages = ParseBase("""{"version":1,"projects":[{"path":"a.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"resolvedVersion":"1.0.0"}]}]}]}""");

        Assert.Empty(packages);
    }

    [Fact]
    public void MergeOutdated_reads_through_the_restore_noise_printed_before_the_json()
    {
        var packages = ParseBase(BaseJson);
        var stdout = """
            正在确定要还原的项目…
            所有项目均是最新的，无法还原。
            {"version":1,"parameters":"--outdated","projects":[{"path":"C:/proj/A.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Newtonsoft.Json","resolvedVersion":"13.0.1","latestVersion":"13.0.3"}],"transitivePackages":[{"id":"Transitive.One","resolvedVersion":"2.0.0","latestVersion":"2.1.0"}]}]}]}
            """;

        var merged = ProjectPackagesViewModel.TryParseAndMerge(
            stdout,
            packages,
            ProjectPackagesViewModel.MergeOutdatedPackagesJson);

        Assert.True(merged);
        Assert.Equal("13.0.3", Assert.Single(packages, p => p.PackageId == "Newtonsoft.Json").LatestVersion);
        Assert.Equal("2.1.0", Assert.Single(packages, p => p.PackageId == "Transitive.One").LatestVersion);
        Assert.True(Assert.Single(packages, p => p.PackageId == "Newtonsoft.Json").IsOutdated);
        Assert.False(Assert.Single(packages, p => p.PackageId == "ILCompiler").IsOutdated);
    }

    [Fact]
    public void MergeVulnerable_reads_the_advisory_url_and_id_fields()
    {
        var packages = ParseBase(BaseJson);
        var stdout = """
            {"version":1,"parameters":"--vulnerable","projects":[{"path":"C:/proj/A.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Newtonsoft.Json","resolvedVersion":"13.0.1","vulnerabilities":[{"severity":"High","advisoryurl":"https://github.com/advisories/GHSA-35jh-3q34-3f3h","id":"GHSA-35jh-3q34-3f3h"},{"severity":"Critical","cve":"CVE-2024-12345"}]}]}]}]}
            """;

        var merged = ProjectPackagesViewModel.TryParseAndMerge(
            stdout,
            packages,
            ProjectPackagesViewModel.MergeVulnerablePackagesJson);

        Assert.True(merged);
        var item = Assert.Single(packages, p => p.PackageId == "Newtonsoft.Json");
        Assert.True(item.HasVulnerabilities);
        Assert.Equal(2, item.Vulnerabilities.Count);
        Assert.Equal("https://github.com/advisories/GHSA-35jh-3q34-3f3h", item.Vulnerabilities[0].AdvisoryUrl);
        Assert.Equal("GHSA-35jh-3q34-3f3h", item.Vulnerabilities[0].Cve);
        Assert.Equal("CVE-2024-12345", item.Vulnerabilities[1].Cve);
        Assert.Equal("Critical", item.MaxSeverity);
    }

    [Fact]
    public void MergeVulnerable_falls_back_to_the_raw_severity_field()
    {
        var packages = ParseBase(BaseJson);
        var stdout = """{"version":1,"projects":[{"path":"C:/proj/A.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Newtonsoft.Json","resolvedVersion":"13.0.1","severity":"Moderate"}]}]}]}""";

        ProjectPackagesViewModel.MergeVulnerablePackagesJson(stdout, packages);

        Assert.Equal("Moderate", Assert.Single(packages, p => p.PackageId == "Newtonsoft.Json").MaxSeverity);
    }

    [Fact]
    public void MergeVulnerable_ignores_packages_that_are_not_in_the_project()
    {
        var packages = ParseBase(BaseJson);
        var stdout = """{"version":1,"projects":[{"path":"C:/proj/Other.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Some.Other","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"High"}]}]}]}]}""";

        ProjectPackagesViewModel.MergeVulnerablePackagesJson(stdout, packages);

        Assert.DoesNotContain(packages, p => p.HasVulnerabilities);
    }

    [Theory]
    // restore failed: NuGet prints a problems array instead of projects
    [InlineData("""{"version":1,"problems":[{"text":"还原失败。","level":"error"}]}""")]
    // the command failed before any JSON was produced
    [InlineData("error: 无法加载源 https://api.nuget.org/v3/index.json 的服务索引。\nerror: The SSL connection could not be established.")]
    // empty stdout
    [InlineData("")]
    // truncated json
    [InlineData("""{"version":1,"projects":[{"path":"x", """)]
    public void MergeGuards_reject_payloads_that_carry_no_project_data(string stdout)
    {
        var packages = ParseBase(BaseJson);

        var merged = ProjectPackagesViewModel.TryParseAndMerge(
            stdout,
            packages,
            ProjectPackagesViewModel.MergeOutdatedPackagesJson);

        Assert.False(merged);
        Assert.All(packages, p => Assert.Null(p.LatestVersion));
    }

    [Theory]
    [InlineData("""{"version":1,"parameters":"","projects":[]}""", """{"version":1,"parameters":"","projects":[]}""")]
    [InlineData("leading noise {\"a\":1} trailing noise", """{"a":1}""")]
    [InlineData("{\"outer\":{\"inner\":1}}", "{\"outer\":{\"inner\":1}}")]
    public void ExtractJson_returns_the_object_embedded_in_the_output(string stdout, string expected)
    {
        Assert.Equal(expected, ProjectPackagesViewModel.ExtractJson(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{")]
    public void ExtractJson_returns_empty_when_there_is_no_object(string stdout)
    {
        Assert.Equal(string.Empty, ProjectPackagesViewModel.ExtractJson(stdout));
    }

    private static List<ProjectPackageItem> ParseBase(string stdout)
    {
        var packages = new List<ProjectPackageItem>();
        ProjectPackagesViewModel.ParseBasePackagesJson(stdout, packages, "C:/fallback.csproj");
        return packages;
    }
}

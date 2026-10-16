using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Resources;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>
/// The UI resolves every label through the resource files. A missing key silently renders as the
/// raw key name, so these tests guard the keys the pages rely on.
/// </summary>
public sealed class LocalizationTests
{
    /// <summary>Keys used by the navigation menu, the tools page and the project dependency page.</summary>
    private static readonly string[] FeatureKeys =
    [
        "App.Title",
        "Nav.Overview",
        "Nav.GroupProject",
        "Nav.GroupNuGet",
        "Nav.GroupEnvironment",
        "Nav.Tools",
        "Nav.GlobalJson",
        "Nav.Sdk",
        "Nav.Workloads",
        "Nav.Output",
        "Nav.Settings",
        "Nav.ProjectPackages",
        "Tools.Title",
        "Tools.Scope.Global",
        "Tools.Scope.Local",
        "Tools.Install",
        "Tools.InstallTarget.Global",
        "Tools.InstallTarget.Local",
        "Tools.SearchTitle",
        "Tools.NoneInstalled",
        "ProjectPackages.Title",
        "ProjectPackages.TargetProject",
        "ProjectPackages.AllProjects",
        "ProjectPackages.CheckAll",
        "ProjectPackages.SelectFile",
        "ProjectPackages.UpgradeAll",
        "ProjectPackages.Upgrade",
        "ProjectPackages.IncludeTransitive",
        "ProjectPackages.IncludePrerelease",
        "ProjectPackages.TotalPackages",
        "ProjectPackages.VulnerableCount",
        "ProjectPackages.OutdatedCount",
        "ProjectPackages.UpToDateCount",
        "ProjectPackages.FilterAll",
        "ProjectPackages.FilterVulnerable",
        "ProjectPackages.FilterOutdated",
        "ProjectPackages.FilterUpToDate",
        "ProjectPackages.FilterTopLevel",
        "ProjectPackages.FilterTransitive",
        "ProjectPackages.SearchPlaceholder",
        "ProjectPackages.NoPackagesFound",
        "ProjectPackages.StatusVulnerable",
        "ProjectPackages.StatusOutdated",
        "ProjectPackages.StatusLatest",
        "ProjectPackages.StatusSafe",
        "ProjectPackages.Advisory",
        "ProjectPackages.BadgeTransitive",
        "ProjectPackages.AutoReferenced",
        "ProjectPackages.Transitive",
        "ProjectPackages.Vulnerabilities",
    ];

    public static TheoryData<string> Languages => new() { LocalizationState.DefaultLanguage, "en" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_key_used_by_the_ui_resolves_in_every_language(string language)
    {
        var localizer = CreateLocalizer(language);

        var missing = FeatureKeys.Where(key => localizer[key] == key).ToList();

        Assert.True(
            missing.Count == 0,
            $"'{language}' is missing {missing.Count} resource key(s): {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Common_keys_used_across_the_shell_resolve(string language)
    {
        var localizer = CreateLocalizer(language);
        string[] keys =
        [
            "Common.Refresh", "Common.SelectFolder", "Common.WorkingDirectory", "Common.OpenFolder",
            "Common.Status", "Common.Name", "Common.Version", "Common.Actions", "Common.Package",
            "Common.PreRelease", "Common.Search",
        ];

        var missing = keys.Where(key => localizer[key] == key).ToList();

        Assert.True(missing.Count == 0, $"'{language}' is missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_two_languages_return_different_text_for_the_same_key()
    {
        var chinese = CreateLocalizer(LocalizationState.DefaultLanguage);
        var english = CreateLocalizer("en");

        Assert.NotEqual(chinese["ProjectPackages.Title"], english["ProjectPackages.Title"]);
    }

    [Fact]
    public void An_unknown_key_falls_back_to_the_key_itself()
    {
        var localizer = CreateLocalizer("en");

        Assert.Equal("No.Such.Key", localizer["No.Such.Key"]);
    }

    [Fact]
    public void Formatted_strings_are_expanded()
    {
        var localizer = CreateLocalizer(LocalizationState.DefaultLanguage);

        var text = localizer["ProjectPackages.UpgradeAllFinished", 2, 1];

        Assert.Contains("2", text);
        Assert.Contains("1", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("ZH-HANS", "zh-Hans")]
    [InlineData("fr", LocalizationState.DefaultLanguage)]
    [InlineData("", LocalizationState.DefaultLanguage)]
    [InlineData(null, LocalizationState.DefaultLanguage)]
    public void The_language_code_is_normalised(string? requested, string expected)
    {
        var state = new LocalizationState();

        state.Initialize(requested);

        Assert.Equal(expected, state.Language);
        Assert.Equal(expected, state.Culture.Name);
    }

    [Fact]
    public void Switching_the_language_notifies_listeners_but_initialising_does_not()
    {
        var state = new LocalizationState();
        var notifications = 0;
        state.Changed += () => notifications++;

        state.Initialize("en");
        Assert.Equal(0, notifications);

        state.SetLanguage("zh-Hans");
        Assert.Equal(1, notifications);

        state.SetLanguage("zh-Hans");
        Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData("zh-Hans", "简体中文")]
    [InlineData("en", "English")]
    [InlineData("de", "de")]
    public void DisplayName_is_readable(string code, string expected)
    {
        Assert.Equal(expected, LocalizationState.DisplayName(code));
    }

    [Fact]
    public void Default_language_is_chinese_and_the_supported_list_matches_the_resource_files()
    {
        Assert.Equal("zh-Hans", LocalizationState.DefaultLanguage);
        Assert.Equal(new[] { "zh-Hans", "en" }, LocalizationState.SupportedLanguages);
    }

    private static UiLocalizer CreateLocalizer(string language)
    {
        var state = new LocalizationState();
        state.Initialize(language);
        return new UiLocalizer(state);
    }
}

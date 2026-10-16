using System.Reflection;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Shared;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>
/// The native shell navigates by handing a <see cref="ShellRoutes"/> constant to the Blazor router.
/// Nothing in the compiler links the two, so a renamed or split page would silently leave a menu entry
/// pointing at a route that no longer exists. These tests read the real <c>@page</c> directives back out
/// of the Razor components and compare them with the constants.
/// </summary>
public sealed class ShellRoutesTests
{
    /// <summary>Every route the Razor components actually declare.</summary>
    private static readonly HashSet<string> Declared = [.. typeof(MainLayout).Assembly
        .GetTypes()
        .SelectMany(type => type.GetCustomAttributes<RouteAttribute>())
        .Select(attribute => attribute.Template)];

    public static TheoryData<string> Routes =>
    [
        ShellRoutes.Overview,
        ShellRoutes.Tools,
        ShellRoutes.ProjectPackages,
        ShellRoutes.PackageSources,
        ShellRoutes.NuGetCaches,
        ShellRoutes.NuGetConfigFiles,
        ShellRoutes.Workloads,
        ShellRoutes.Sdk,
        ShellRoutes.Output,
        ShellRoutes.Settings,
    ];

    [Theory]
    [MemberData(nameof(Routes))]
    public void Every_shell_route_resolves_to_a_page(string route) =>
        Assert.Contains(route, Declared);

    /// <summary>
    /// The merged tools page: one route covers both scopes, so the three separate ones must be gone.
    /// </summary>
    [Theory]
    [InlineData("/tools/local")]
    [InlineData("/tools/global")]
    [InlineData("/tools/search")]
    public void The_retired_per_scope_tool_routes_are_no_longer_pages(string route) =>
        Assert.DoesNotContain(route, Declared);

    /// <summary>
    /// The per-project global.json editor was removed as unused. The environment banner still reads
    /// global.json to warn about a missing SDK, but that check belongs to the host service and has no
    /// page, so a route here would mean the deleted editor came back.
    /// </summary>
    [Fact]
    public void There_is_no_global_json_page()
    {
        Assert.DoesNotContain("/global-json", Declared);
        Assert.DoesNotContain("GlobalJson", typeof(ShellRoutes).GetFields().Select(field => field.Name));
    }
}

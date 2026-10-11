using DotnetManager.RazorLib.Features.NuGet;
using Xunit;

namespace DotnetManager.Tests;

/// <summary>
/// The NuGet page reads folder locations from <c>dotnet nuget locals all --list</c>, whose output is
/// plain <c>kind: path</c> text that must survive drive letters and localised surroundings.
/// </summary>
public sealed class NuGetLocalsParserTests
{
    [Fact]
    public void Parse_reads_every_known_location()
    {
        const string output = """
            http-cache: C:\Users\Tommy\AppData\Local\NuGet\v3-cache
            global-packages: D:\code\nuget\packages
            temp: C:\Users\Tommy\AppData\Local\Temp\NuGetScratch
            plugins-cache: C:\Users\Tommy\AppData\Local\NuGet\plugins-cache
            """;

        var result = NuGetLocalsParser.Parse(output);

        Assert.Equal(4, result.Count);
        Assert.Equal(@"C:\Users\Tommy\AppData\Local\NuGet\v3-cache", result["http-cache"]);
        Assert.Equal(@"D:\code\nuget\packages", result["global-packages"]);
        Assert.Equal(@"C:\Users\Tommy\AppData\Local\Temp\NuGetScratch", result["temp"]);
        Assert.Equal(@"C:\Users\Tommy\AppData\Local\NuGet\plugins-cache", result["plugins-cache"]);
    }

    [Fact]
    public void Parse_keeps_windows_drive_letters_intact()
    {
        var result = NuGetLocalsParser.Parse("global-packages: D:\\code\\nuget\\packages");

        // The split must happen on the first colon only.
        Assert.StartsWith("D:", Assert.Single(result.Values));
    }

    [Fact]
    public void Parse_ignores_unknown_kinds_and_lines_without_a_value()
    {
        const string output = """
            all: D:\code\nuget\packages
            global-packages:
            global-packages:   
            项目: 已还原
            not a pair
            http-cache: C:\cache
            """;

        var result = NuGetLocalsParser.Parse(output);

        Assert.Single(result);
        Assert.Equal(@"C:\cache", result["http-cache"]);
    }

    [Fact]
    public void Parse_is_case_insensitive_for_the_kind()
    {
        var result = NuGetLocalsParser.Parse("Global-Packages: D:\\packages");

        Assert.Equal(@"D:\packages", result["global-packages"]);
    }

    [Fact]
    public void Parse_takes_the_last_value_when_a_kind_is_repeated()
    {
        var result = NuGetLocalsParser.Parse("temp: C:\\first" + Environment.NewLine + "temp: C:\\second");

        Assert.Equal(@"C:\second", Assert.Single(result.Values));
    }

    [Fact]
    public void Parse_trims_surrounding_whitespace()
    {
        var result = NuGetLocalsParser.Parse("  global-packages :   D:\\packages   ");

        Assert.Equal(@"D:\packages", Assert.Single(result.Values));
    }

    [Fact]
    public void Parse_returns_an_empty_map_for_empty_output()
    {
        Assert.Empty(NuGetLocalsParser.Parse(string.Empty));
        Assert.Empty(NuGetLocalsParser.Parse(Environment.NewLine));
    }
}

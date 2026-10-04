using System.IO;
using System.Text.RegularExpressions;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

/// <summary>Reads the global tool list produced by <c>dotnet tool list --global</c>.</summary>
public sealed partial class GlobalToolService : IGlobalToolService
{
    private readonly IDotnetCli _cli;

    public GlobalToolService(IDotnetCli cli) => _cli = cli;

    public string ToolsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dotnet",
        "tools");

    public string StoreDirectory => Path.Combine(ToolsDirectory, ".store");

    public async Task<IReadOnlyList<GlobalToolInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await _cli.RunToResultAsync(
            ["tool", "list", "--global"],
            new CliOptions { Description = "dotnet tool list --global", InvariantOutput = true },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return [];
        }

        return Parse(result.StandardOutput);
    }

    public static IReadOnlyList<GlobalToolInfo> Parse(string output)
    {
        var tools = new List<GlobalToolInfo>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('-') || line.StartsWith('='))
            {
                continue;
            }

            var columns = ColumnSeparator().Split(line);

            // The header row ("Package Id / Version / Commands") and blank notices have no version column.
            if (columns.Length < 2 || !LooksLikeVersion(columns[1]))
            {
                continue;
            }

            var commands = columns.Length > 2
                ? columns[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                : [];

            tools.Add(new GlobalToolInfo(columns[0], columns[1], commands));
        }

        return tools;
    }

    private static bool LooksLikeVersion(string value) => value.Length > 0 && char.IsDigit(value[0]);

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnSeparator();
}

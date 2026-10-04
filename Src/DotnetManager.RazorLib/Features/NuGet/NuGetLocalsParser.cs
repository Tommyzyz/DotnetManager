namespace DotnetManager.RazorLib.Features.NuGet;

/// <summary>Parses the <c>type: path</c> lines printed by <c>dotnet nuget locals all --list</c>.</summary>
public static class NuGetLocalsParser
{
    private static readonly string[] KnownKinds = ["http-cache", "global-packages", "temp", "plugins-cache"];

    public static IReadOnlyDictionary<string, string> Parse(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var kind = line[..separator].Trim();
            if (!KnownKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = line[(separator + 1)..].Trim();
            if (path.Length > 0)
            {
                result[kind] = path;
            }
        }

        return result;
    }
}

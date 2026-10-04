using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

public sealed partial class SdkService : ISdkService
{
    private readonly IDotnetCli _cli;

    public SdkService(IDotnetCli cli) => _cli = cli;

    public async Task<IReadOnlyList<SdkInfo>> GetSdksAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--list-sdks"], "dotnet --list-sdks", cancellationToken).ConfigureAwait(false);
        return result.Success ? ParseSdks(result.StandardOutput) : [];
    }

    public async Task<IReadOnlyList<RuntimeInfo>> GetRuntimesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--list-runtimes"], "dotnet --list-runtimes", cancellationToken).ConfigureAwait(false);
        return result.Success ? ParseRuntimes(result.StandardOutput) : [];
    }

    public async Task<string> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await _cli.RunToResultAsync(
            ["--info"],
            new CliOptions { Description = "dotnet --info" },
            cancellationToken).ConfigureAwait(false);

        return result.CombinedOutput;
    }

    public async Task<IReadOnlyList<WorkloadInfo>> GetWorkloadsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["workload", "list"], "dotnet workload list", cancellationToken).ConfigureAwait(false);
        return result.Success ? ParseWorkloads(result.StandardOutput) : [];
    }

    public Task<CliResult> SdkCheckAsync(CancellationToken cancellationToken = default) =>
        RunAsync(["sdk", "check"], "dotnet sdk check", cancellationToken);

    public string? FindGlobalJson(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public Task<OperationResult> WriteGlobalJsonAsync(string directory, string sdkVersion, string rollForward, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Task.FromResult(OperationResult.Fail("The selected directory does not exist."));
        }

        if (string.IsNullOrWhiteSpace(sdkVersion))
        {
            return Task.FromResult(OperationResult.Fail("An SDK version is required."));
        }

        var path = Path.Combine(directory, "global.json");
        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidOperationException("'global.json' is not a JSON object.");
            }
            else
            {
                root = new JsonObject();
            }

            var sdk = root["sdk"] as JsonObject ?? new JsonObject();
            sdk["version"] = sdkVersion;
            if (!string.IsNullOrWhiteSpace(rollForward))
            {
                sdk["rollForward"] = rollForward;
            }

            root["sdk"] = sdk;
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return Task.FromResult(OperationResult.Ok($"Wrote {path}"));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    public static IReadOnlyList<SdkInfo> ParseSdks(string output)
    {
        var sdks = new List<SdkInfo>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var (head, path) = SplitBrackets(line);
            if (head.Length == 0 || path.Length == 0)
            {
                continue;
            }

            sdks.Add(new SdkInfo(head, path));
        }

        return sdks;
    }

    public static IReadOnlyList<RuntimeInfo> ParseRuntimes(string output)
    {
        var runtimes = new List<RuntimeInfo>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var (head, path) = SplitBrackets(line);
            if (head.Length == 0 || path.Length == 0)
            {
                continue;
            }

            var parts = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            runtimes.Add(new RuntimeInfo(parts[0], parts[1], path));
        }

        return runtimes;
    }

    public static IReadOnlyList<WorkloadInfo> ParseWorkloads(string output)
    {
        var workloads = new List<WorkloadInfo>();
        var insideTable = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!insideTable)
            {
                if (line.TrimStart().StartsWith('-'))
                {
                    insideTable = true;
                }

                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var columns = ColumnSeparator().Split(trimmed);
            if (columns.Length < 2)
            {
                // Trailing hints such as "Use `dotnet workload search` ..." are not table rows.
                continue;
            }

            workloads.Add(new WorkloadInfo(
                columns[0],
                columns.Length > 2 ? columns[2] : string.Empty,
                columns[1]));
        }

        return workloads;
    }

    private Task<CliResult> RunAsync(IReadOnlyList<string> arguments, string description, CancellationToken cancellationToken) =>
        _cli.RunToResultAsync(
            arguments,
            new CliOptions { Description = description, InvariantOutput = true },
            cancellationToken);

    private static (string Head, string Path) SplitBrackets(string line)
    {
        var open = line.IndexOf('[');
        var close = line.LastIndexOf(']');
        return open <= 0 || close <= open
            ? (string.Empty, string.Empty)
            : (line[..open].Trim(), line[(open + 1)..close].Trim());
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnSeparator();
}

using System.IO;
using System.Text.Json.Nodes;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManagerUI.Services;

/// <summary>Reads and edits <c>.config/dotnet-tools.json</c> tool manifests.</summary>
public sealed class LocalToolManifestService : ILocalToolManifestService
{
    private const string ManifestFileName = "dotnet-tools.json";
    private const string ManifestDirectory = ".config";

    private static readonly string[] SkippedDirectories =
    [
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages", "artifacts",
    ];

    public IReadOnlyList<string> FindManifests(string directory, int maxDepth = 6)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<string>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((Path.GetFullPath(directory), 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            var root = Path.Combine(current, ".config", ManifestFileName);
            if (File.Exists(root))
            {
                results.Add(root);
            }

            var legacy = Path.Combine(current, ManifestFileName);
            if (File.Exists(legacy))
            {
                results.Add(legacy);
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                queue.Enqueue((child, depth + 1));
            }
        }

        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ToolManifest Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return new ToolManifest { Path = manifestPath, IsValid = false, Error = "The manifest file does not exist." };
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
            if (node is null)
            {
                return new ToolManifest { Path = manifestPath, IsValid = false, Error = "The manifest root is not a JSON object." };
            }

            var tools = new List<ManifestTool>();
            if (node["tools"] is JsonObject toolObject)
            {
                foreach (var (packageId, value) in toolObject)
                {
                    if (value is not JsonObject tool)
                    {
                        continue;
                    }

                    var commands = (tool["commands"] as JsonArray)?
                        .Select(command => command?.GetValue<string>())
                        .Where(command => !string.IsNullOrWhiteSpace(command))
                        .Select(command => command!)
                        .ToList() ?? [];

                    tools.Add(new ManifestTool(
                        packageId,
                        tool["version"]?.GetValue<string>() ?? string.Empty,
                        commands,
                        ReadRollForward(tool["rollForward"])));
                }
            }

            return new ToolManifest
            {
                Path = manifestPath,
                Version = node["version"]?.GetValue<int>() ?? 1,
                IsRoot = node["isRoot"]?.GetValue<bool>() ?? false,
                Tools = tools.OrderBy(tool => tool.PackageId, StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            return new ToolManifest { Path = manifestPath, IsValid = false, Error = ex.Message };
        }
    }

    public Task<OperationResult> CreateManifestAsync(string directory, bool isRoot = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Task.FromResult(OperationResult.Fail("The selected directory does not exist."));
        }

        var configDirectory = Path.Combine(directory, ManifestDirectory);
        var manifestPath = Path.Combine(configDirectory, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            return Task.FromResult(OperationResult.Fail($"'{manifestPath}' already exists."));
        }

        var document = new JsonObject
        {
            ["version"] = 1,
            ["isRoot"] = isRoot,
            ["tools"] = new JsonObject(),
        };

        try
        {
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(manifestPath, document.ToJsonString(JsonOptions));
            return Task.FromResult(OperationResult.Ok($"Created {manifestPath}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    public Task<OperationResult> RemoveToolAsync(ToolManifest manifest, string packageId, CancellationToken cancellationToken = default) =>
        EditAsync(manifest, root =>
        {
            if (root["tools"] is JsonObject tools)
            {
                tools.Remove(packageId);
            }
        }, cancellationToken);

    public Task<OperationResult> SetRollForwardAsync(ToolManifest manifest, string packageId, string? rollForward, CancellationToken cancellationToken = default) =>
        EditAsync(manifest, root =>
        {
            if (root["tools"] is not JsonObject tools || tools[packageId] is not JsonObject tool)
            {
                throw new InvalidOperationException($"The package '{packageId}' is not part of this manifest.");
            }

            if (string.IsNullOrWhiteSpace(rollForward))
            {
                tool.Remove("rollForward");
                return;
            }

            tool["rollForward"] = rollForward;
        }, cancellationToken);

    private static Task<OperationResult> EditAsync(ToolManifest manifest, Action<JsonObject> mutate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(manifest.Path) || !File.Exists(manifest.Path))
        {
            return Task.FromResult(OperationResult.Fail("The manifest file does not exist."));
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(manifest.Path)) as JsonObject
                ?? throw new InvalidOperationException("The manifest root is not a JSON object.");

            mutate(root);
            File.WriteAllText(manifest.Path, root.ToJsonString(JsonOptions));
            return Task.FromResult(OperationResult.Ok($"Updated {manifest.Path}"));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message));
        }
    }

    private static string? ReadRollForward(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

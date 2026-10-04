namespace DotnetManager.RazorLib.Abstractions;

public sealed record ManifestTool(string PackageId, string Version, IReadOnlyList<string> Commands, string? RollForward);

public sealed class ToolManifest
{
    public required string Path { get; init; }

    public int Version { get; init; }

    public bool IsRoot { get; init; }

    public IReadOnlyList<ManifestTool> Tools { get; init; } = [];

    public bool IsValid { get; init; } = true;

    public string? Error { get; init; }
}

public sealed record GlobalToolInfo(string PackageId, string Version, IReadOnlyList<string> Commands);

public sealed record PackageSearchResult(
    string Id,
    string? LatestVersion,
    string? Description,
    string? Authors,
    long? TotalDownloads,
    bool IsToolPackage);

public interface ILocalToolManifestService
{
    IReadOnlyList<string> FindManifests(string directory, int maxDepth = 6);

    ToolManifest Load(string manifestPath);

    Task<OperationResult> CreateManifestAsync(string directory, bool isRoot = true, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveToolAsync(ToolManifest manifest, string packageId, CancellationToken cancellationToken = default);

    Task<OperationResult> SetRollForwardAsync(ToolManifest manifest, string packageId, string? rollForward, CancellationToken cancellationToken = default);
}

public interface IGlobalToolService
{
    string ToolsDirectory { get; }

    string StoreDirectory { get; }

    Task<IReadOnlyList<GlobalToolInfo>> ListAsync(CancellationToken cancellationToken = default);
}

public interface INuGetPackageQuery
{
    Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageSearchResult>> SearchAsync(
        string query,
        bool includePrerelease = false,
        bool toolOnly = true,
        int take = 25,
        CancellationToken cancellationToken = default);
}

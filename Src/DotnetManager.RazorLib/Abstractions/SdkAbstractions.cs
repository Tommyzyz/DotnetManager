namespace DotnetManager.RazorLib.Abstractions;

public sealed record SdkInfo(string Version, string Path);

public sealed record RuntimeInfo(string Name, string Version, string Path);

public sealed record WorkloadInfo(string Id, string InstallSource, string ManifestVersion);

public interface ISdkService
{
    Task<IReadOnlyList<SdkInfo>> GetSdksAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeInfo>> GetRuntimesAsync(CancellationToken cancellationToken = default);

    Task<string> GetInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkloadInfo>> GetWorkloadsAsync(CancellationToken cancellationToken = default);

    Task<CliResult> SdkCheckAsync(CancellationToken cancellationToken = default);

    string? FindGlobalJson(string? directory);

    Task<OperationResult> WriteGlobalJsonAsync(string directory, string sdkVersion, string rollForward, CancellationToken cancellationToken = default);
}

public interface ICacheInspector
{
    Task<(long Bytes, int Files)> GetDirectorySizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}

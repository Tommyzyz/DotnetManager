namespace DotnetManager.RazorLib.Abstractions;

public sealed record SdkInfo(string Version, string Path);

public sealed record RuntimeInfo(string Name, string Version, string Path);

public sealed record WorkloadInfo(string Id, string InstallSource, string ManifestVersion);

/// <summary>An installable workload, as reported by <c>dotnet workload search</c>.</summary>
public sealed record WorkloadCandidate(string Id, string Description);

public interface ISdkService
{
    Task<IReadOnlyList<SdkInfo>> GetSdksAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeInfo>> GetRuntimesAsync(CancellationToken cancellationToken = default);

    Task<string> GetInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkloadInfo>> GetWorkloadsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists every workload the installed SDK could install (<c>dotnet workload search</c>).</summary>
    Task<IReadOnlyList<WorkloadCandidate>> GetInstallableWorkloadsAsync(CancellationToken cancellationToken = default);

    Task<CliResult> SdkCheckAsync(CancellationToken cancellationToken = default);

    string? FindGlobalJson(string? directory);

    Task<OperationResult> WriteGlobalJsonAsync(string directory, string sdkVersion, string rollForward, CancellationToken cancellationToken = default);
}

public interface ICacheInspector
{
    Task<(long Bytes, int Files)> GetDirectorySizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of looking for a usable .NET installation.</summary>
public enum DotnetState
{
    /// <summary>Detection itself failed (for example the CLI could not be started).</summary>
    Unknown,

    /// <summary>No dotnet.exe anywhere and nothing in the registry.</summary>
    NotInstalled,

    /// <summary>dotnet.exe exists but <c>--list-sdks</c> is empty: runtime only.</summary>
    RuntimeOnly,

    Installed,

    /// <summary>An SDK is registered in the registry but the running process cannot see it (stale PATH).</summary>
    InstalledButStalePath,

    /// <summary>An SDK exists but the one required by global.json does not.</summary>
    SdkVersionTooOld,
}

/// <summary>A snapshot of the machine's .NET installation.</summary>
public sealed record DotnetEnvironmentStatus
{
    public DotnetState State { get; init; }

    public string DotnetPath { get; init; } = string.Empty;

    public DotnetPathSource PathSource { get; init; }

    public IReadOnlyList<SdkInfo> Sdks { get; init; } = [];

    public IReadOnlyList<RuntimeInfo> Runtimes { get; init; } = [];

    /// <summary>SDK versions the registry knows about, even when the process cannot see them.</summary>
    public IReadOnlyList<string> RegistrySdkVersions { get; init; } = [];

    public string? RegistryDotnetPath { get; init; }

    public string? GlobalJsonPath { get; init; }

    public string? RequiredSdkVersion { get; init; }

    public string? RequiredRollForward { get; init; }

    public bool GlobalJsonSatisfied { get; init; } = true;

    /// <summary>Extra context for the UI, such as why a check failed.</summary>
    public string? Detail { get; init; }
}

public interface IDotnetEnvironmentService
{
    /// <summary>
    /// Detects dotnet, distinguishes "runtime only" from "no SDK at all", falls back to the registry
    /// when PATH is stale and reports whether the SDK required by global.json is present.
    /// </summary>
    Task<DotnetEnvironmentStatus> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>Guides the user to an official SDK download; it never installs silently.</summary>
public interface IDotnetSdkInstaller
{
    /// <summary>Opens the official download page in the browser. <paramref name="channel"/> is e.g. "10.0".</summary>
    Task<OperationResult> OpenDownloadPageAsync(string? channel = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the official installer to %TEMP%, verifies its SHA-512 when the release metadata
    /// provides one, and starts it so the user completes the setup in Microsoft's own UI.
    /// </summary>
    Task<OperationResult> DownloadAndRunInstallerAsync(string? channel = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

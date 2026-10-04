namespace DotnetManager.RazorLib.Abstractions;

public enum NuGetValueSource
{
    Unknown,
    Default,
    ConfigFile,
    Environment,
}

public sealed record NuGetValue(string? Value, NuGetValueSource Source, string? SourceDetail)
{
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}

public sealed record PackageSourceInfo(
    string Name,
    string Url,
    bool Enabled,
    string? ConfigFile,
    bool IsMachineWide,
    bool IsBuiltIn,
    bool HasCredentials,
    string? ProtocolVersion);

public sealed record NuGetConfigFileInfo(string Path, string Scope, bool Exists, bool IsMachineWide, bool IsReadOnly);

public sealed record NuGetConfigEntry(string Key, string Value, string? ConfigFile);

public sealed record NuGetPathEntry(string Path, string? Source);

public sealed class NuGetSettingsSnapshot
{
    public string? RootDirectory { get; init; }

    public NuGetValue GlobalPackagesFolder { get; init; } = new(null, NuGetValueSource.Unknown, null);

    public NuGetValue HttpCacheFolder { get; init; } = new(null, NuGetValueSource.Unknown, null);

    public NuGetValue PluginsCacheFolder { get; init; } = new(null, NuGetValueSource.Unknown, null);

    public NuGetValue TempFolder { get; init; } = new(null, NuGetValueSource.Unknown, null);

    public IReadOnlyList<NuGetPathEntry> FallbackFolders { get; init; } = [];

    public IReadOnlyList<PackageSourceInfo> Sources { get; init; } = [];

    public IReadOnlyList<NuGetConfigFileInfo> ConfigFiles { get; init; } = [];

    public IReadOnlyList<NuGetConfigEntry> DisabledSources { get; init; } = [];

    public IReadOnlyList<NuGetConfigEntry> ConfigValues { get; init; } = [];

    public string? LoadError { get; init; }
}

public interface INuGetSettingsService
{
    string UserConfigPath { get; }

    string MachineConfigPath { get; }

    /// <summary>Returns the config file that would apply to a project in the given directory, or null.</summary>
    string? FindConfigFile(string? directory);

    NuGetSettingsSnapshot Load(string? rootDirectory);

    IReadOnlyList<NuGetConfigFileInfo> GetConfigFiles(string? rootDirectory);

    string ReadRawFile(string path);

    bool RequiresElevation(string path);

    Task<OperationResult> SetConfigValueAsync(string configFile, string key, string? value, CancellationToken cancellationToken = default);

    Task<OperationResult> SetFallbackFoldersAsync(string configFile, IReadOnlyList<string> folders, CancellationToken cancellationToken = default);

    Task<OperationResult> AddOrUpdateSourceAsync(string configFile, string name, string url, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveSourceAsync(string configFile, string name, CancellationToken cancellationToken = default);

    Task<OperationResult> SetSourceEnabledAsync(string configFile, string name, bool enabled, CancellationToken cancellationToken = default);

    Task<OperationResult> WriteRawFileAsync(string path, string content, CancellationToken cancellationToken = default);
}

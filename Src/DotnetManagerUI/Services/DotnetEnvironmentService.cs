using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManagerUI.Services;

/// <summary>
/// Works out whether this machine can actually build .NET projects.
/// <para>
/// "dotnet.exe exists" is not enough: a runtime-only install also ships dotnet.exe, and an SDK
/// installed after this process started is invisible to PATH. Both cases are detected separately so
/// the UI can offer the right fix instead of a generic "not installed".
/// </para>
/// </summary>
public sealed class DotnetEnvironmentService : IDotnetEnvironmentService
{
    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(30);

    private readonly IDotnetLocator _locator;
    private readonly IDotnetCli _cli;
    private readonly IAppSettingsStore _settings;

    public DotnetEnvironmentService(IDotnetLocator locator, IDotnetCli cli, IAppSettingsStore settings)
    {
        _locator = locator;
        _cli = cli;
        _settings = settings;
    }

    public async Task<DotnetEnvironmentStatus> DetectAsync(CancellationToken cancellationToken = default)
    {
        var resolved = _locator.ResolveDetailed(_settings.Current.DotnetPath);
        var registrySdks = DotnetLocator.RegistrySdkVersions();
        var registryPath = DotnetLocator.RegistryInstallDir();

        var baseStatus = new DotnetEnvironmentStatus
        {
            DotnetPath = resolved.Path,
            PathSource = resolved.Source,
            RegistrySdkVersions = registrySdks,
            RegistryDotnetPath = registryPath,
        };

        if (!_locator.IsValid(resolved.Path))
        {
            // Nothing on disk. The registry may still know about an SDK this process cannot reach.
            return registrySdks.Count > 0
                ? await AdoptRegistryInstallAsync(baseStatus, registryPath, cancellationToken).ConfigureAwait(false)
                : await ApplyGlobalJsonAsync(baseStatus with { State = DotnetState.NotInstalled }, cancellationToken).ConfigureAwait(false);
        }

        var sdks = await ListSdksAsync(cancellationToken).ConfigureAwait(false);
        if (sdks.Count == 0)
        {
            // dotnet.exe without any SDK: either a runtime-only install or a stale PATH.
            return registrySdks.Count > 0
                ? await AdoptRegistryInstallAsync(baseStatus, registryPath, cancellationToken).ConfigureAwait(false)
                : await ApplyGlobalJsonAsync(baseStatus with { State = DotnetState.RuntimeOnly }, cancellationToken).ConfigureAwait(false);
        }

        var status = baseStatus with
        {
            State = DotnetState.Installed,
            Sdks = sdks,
            Runtimes = await ListRuntimesAsync(cancellationToken).ConfigureAwait(false),
        };

        return await ApplyGlobalJsonAsync(status, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Points the app at the registry-reported install (persisted, so no restart is needed) and
    /// re-checks against that dotnet.exe.
    /// </summary>
    private async Task<DotnetEnvironmentStatus> AdoptRegistryInstallAsync(
        DotnetEnvironmentStatus baseStatus,
        string? registryPath,
        CancellationToken cancellationToken)
    {
        var dotnet = registryPath is null ? null : Path.Combine(registryPath, "dotnet.exe");
        if (!_locator.IsValid(dotnet))
        {
            return await ApplyGlobalJsonAsync(baseStatus with { State = DotnetState.NotInstalled }, cancellationToken).ConfigureAwait(false);
        }

        _settings.Current.DotnetPath = dotnet;
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);

        var resolved = _locator.ResolveDetailed(dotnet);
        var sdks = await ListSdksAsync(cancellationToken).ConfigureAwait(false);

        var status = baseStatus with
        {
            State = DotnetState.InstalledButStalePath,
            DotnetPath = resolved.Path,
            PathSource = DotnetPathSource.Registry,
            Sdks = sdks,
            Runtimes = await ListRuntimesAsync(cancellationToken).ConfigureAwait(false),
        };

        if (sdks.Count == 0)
        {
            status = status with { State = DotnetState.RuntimeOnly };
        }

        return await ApplyGlobalJsonAsync(status, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies the global.json requirement on top of whatever was found on disk.</summary>
    private Task<DotnetEnvironmentStatus> ApplyGlobalJsonAsync(DotnetEnvironmentStatus status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = _settings.Current.LastWorkingDirectory;
        var globalJson = string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
            ? null
            : SdkService.FindGlobalJsonIn(directory);

        if (globalJson is null)
        {
            return Task.FromResult(status);
        }

        var (version, rollForward) = ReadGlobalJson(globalJson);
        var withGlobalJson = status with
        {
            GlobalJsonPath = globalJson,
            RequiredSdkVersion = version,
            RequiredRollForward = rollForward,
        };

        if (string.IsNullOrWhiteSpace(version))
        {
            return Task.FromResult(withGlobalJson);
        }

        var satisfied = status.Sdks.Any(sdk => Satisfies(sdk.Version, version, rollForward));
        if (satisfied || status.State is DotnetState.NotInstalled or DotnetState.RuntimeOnly)
        {
            return Task.FromResult(withGlobalJson with { GlobalJsonSatisfied = satisfied });
        }

        return Task.FromResult(withGlobalJson with
        {
            State = DotnetState.SdkVersionTooOld,
            GlobalJsonSatisfied = false,
        });
    }

    private async Task<IReadOnlyList<SdkInfo>> ListSdksAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["--list-sdks"], "dotnet --list-sdks", cancellationToken).ConfigureAwait(false);
        return result is null ? [] : SdkService.ParseSdks(result.StandardOutput);
    }

    private async Task<IReadOnlyList<RuntimeInfo>> ListRuntimesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["--list-runtimes"], "dotnet --list-runtimes", cancellationToken).ConfigureAwait(false);
        return result is null ? [] : SdkService.ParseRuntimes(result.StandardOutput);
    }

    private async Task<CliResult?> RunAsync(IReadOnlyList<string> arguments, string description, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cli.RunToResultAsync(
                arguments,
                new CliOptions { Description = description, InvariantOutput = true, Timeout = CliTimeout },
                cancellationToken).ConfigureAwait(false);

            return result.Success ? result : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>sdk.version</c> and <c>sdk.rollForward</c> from global.json.</summary>
    public static (string? Version, string? RollForward) ReadGlobalJson(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return (null, null);
            }

            var sdk = root["sdk"] as JsonObject;
            return (sdk?["version"]?.GetValue<string>(), sdk?["rollForward"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Applies the documented rollForward rules loosely: relaxed enough to avoid crying wolf when a
    /// newer SDK is allowed, strict enough to warn when the build would actually fail.
    /// </summary>
    public static bool Satisfies(string installed, string required, string? rollForward)
    {
        if (string.Equals(installed, required, StringComparison.Ordinal))
        {
            return true;
        }

        var actual = Parse(installed);
        var wanted = Parse(required);
        if (actual is null || wanted is null)
        {
            return false;
        }

        return rollForward?.ToLowerInvariant() switch
        {
            "disable" => false,
            "patch" or "latestpatch" => actual.Major == wanted.Major && actual.Minor == wanted.Minor && actual.Build >= wanted.Build,
            "feature" or "latestfeature" => actual.Major == wanted.Major && actual.Minor == wanted.Minor,
            "minor" or "latestminor" => actual.Major == wanted.Major && actual.Minor >= wanted.Minor,
            _ => actual >= wanted,
        };
    }

    private static Version? Parse(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        // SDK versions may carry a preview suffix such as 10.0.100-preview.1.
        var patch = parts[2].Split('-', StringSplitOptions.RemoveEmptyEntries)[0];

        return int.TryParse(parts[0], out var major)
            && int.TryParse(parts[1], out var minor)
            && int.TryParse(patch, out var patchNumber)
            ? new Version(major, minor, patchNumber)
            : null;
    }
}

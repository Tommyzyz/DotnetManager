using System.IO;
using System.Text.Json;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.RazorLib.Features.Workloads;

/// <summary>
/// Reads the installable workloads straight out of the SDK's own manifests instead of scraping the
/// <c>dotnet workload search</c> table.
/// <para>
/// Layout: <c>&lt;dotnet root&gt;/sdk-manifests/&lt;band&gt;/&lt;manifest id&gt;/&lt;version&gt;/WorkloadManifest.json</c>,
/// where each manifest carries a <c>workloads</c> object with an id and a description. Two details
/// matter: workloads flagged <c>"abstract": true</c> are building blocks the CLI never offers for
/// install, and several versions of the same manifest can sit side by side, in which case the SDK
/// uses the highest one.
/// </para>
/// </summary>
public static class WorkloadManifestReader
{
    private const string ManifestsDirectoryName = "sdk-manifests";
    private const string ManifestFileName = "WorkloadManifest.json";

    /// <summary>Reads every workload the newest installed SDK band can install.</summary>
    public static IReadOnlyList<WorkloadCandidate> Read(string? dotnetRoot)
    {
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return [];
        }

        var manifestsRoot = Path.Combine(dotnetRoot, ManifestsDirectoryName);
        if (!Directory.Exists(manifestsRoot))
        {
            return [];
        }

        if (NewestBand(manifestsRoot) is not { } band)
        {
            return [];
        }

        var candidates = new Dictionary<string, WorkloadCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifestIdDirectory in EnumerateDirectories(band))
        {
            if (NewestManifest(manifestIdDirectory) is not { } manifest)
            {
                continue;
            }

            foreach (var candidate in ReadManifest(manifest))
            {
                candidates.TryAdd(candidate.Id, candidate);
            }
        }

        return [.. candidates.Values.OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Bands are named like 10.0.400 and one root can hold several. The highest band is not always the
    /// right one - an SDK can leave an empty band directory behind - so the newest band that actually
    /// carries manifests wins, which is the band <c>dotnet workload search</c> reports from as well.
    /// </summary>
    private static string? NewestBand(string manifestsRoot)
    {
        foreach (var directory in EnumerateDirectories(manifestsRoot)
                     .Where(directory => Version.TryParse(Path.GetFileName(directory), out _))
                     .OrderByDescending(directory => Version.Parse(Path.GetFileName(directory))))
        {
            if (HasManifest(directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static bool HasManifest(string bandDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(bandDirectory, ManifestFileName, SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Several versions of one manifest can be present; the SDK resolves the highest.</summary>
    private static string? NewestManifest(string manifestIdDirectory)
    {
        string? newestManifest = null;
        var newestVersion = new Version(0, 0);

        foreach (var versionDirectory in EnumerateDirectories(manifestIdDirectory))
        {
            var manifest = Path.Combine(versionDirectory, ManifestFileName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            // A directory that is not a version simply loses against one that is.
            var version = Version.TryParse(Path.GetFileName(versionDirectory), out var parsed) ? parsed : newestVersion;
            if (newestManifest is null || version > newestVersion)
            {
                newestVersion = version;
                newestManifest = manifest;
            }
        }

        return newestManifest;
    }

    private static IReadOnlyList<WorkloadCandidate> ReadManifest(string manifestPath)
    {
        JsonDocument document;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            document = JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // One unreadable manifest must not hide the workloads of the others.
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("workloads", out var workloads)
                || workloads.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var candidates = new List<WorkloadCandidate>();
            foreach (var workload in workloads.EnumerateObject())
            {
                if (IsAbstract(workload.Value))
                {
                    continue;
                }

                candidates.Add(new WorkloadCandidate(workload.Name, ReadDescription(workload.Value)));
            }

            return candidates;
        }
    }

    private static bool IsAbstract(JsonElement workload) =>
        workload.ValueKind == JsonValueKind.Object
        && workload.TryGetProperty("abstract", out var isAbstract)
        && isAbstract.ValueKind == JsonValueKind.True;

    private static string ReadDescription(JsonElement workload) =>
        workload.ValueKind == JsonValueKind.Object
        && workload.TryGetProperty("description", out var description)
        && description.ValueKind == JsonValueKind.String
            ? description.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> EnumerateDirectories(string path)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(path)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

using DotnetManager.RazorLib.Abstractions;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotnetManager.Services;

/// <summary>Queries package versions and tool search results through the official NuGet client stack.</summary>
public sealed class NuGetPackageQuery : INuGetPackageQuery
{
    private const string NuGetOrgSource = "https://api.nuget.org/v3/index.json";

    private readonly INuGetSettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SourceRepository? _repository;

    public NuGetPackageQuery(INuGetSettingsService settings) => _settings = settings;

    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        try
        {
            var repository = await GetRepositoryAsync(cancellationToken).ConfigureAwait(false);
            var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
            if (resource is null)
            {
                return null;
            }

            using var cache = new SourceCacheContext();
            var versions = await resource
                .GetAllVersionsAsync(packageId, cache, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);

            return versions
                .Where(version => includePrerelease || !version.IsPrerelease)
                .OrderByDescending(version => version, VersionComparer.VersionRelease)
                .FirstOrDefault()
                ?.ToNormalizedString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(
        string query,
        bool includePrerelease = false,
        bool toolOnly = true,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var repository = await GetRepositoryAsync(cancellationToken).ConfigureAwait(false);
            var resource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken).ConfigureAwait(false);
            if (resource is null)
            {
                return [];
            }

            var filter = new SearchFilter(includePrerelease);
            if (toolOnly)
            {
                filter.PackageType = "DotnetTool";
            }

            var metadata = await resource
                .SearchAsync(query, filter, skip: 0, take: take, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);

            return metadata
                .Select(item => new PackageSearchResult(
                    item.Identity.Id,
                    item.Identity.Version?.ToNormalizedString(),
                    item.Description,
                    item.Authors,
                    item.DownloadCount,
                    toolOnly))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private async Task<SourceRepository> GetRepositoryAsync(CancellationToken cancellationToken)
    {
        if (_repository is not null)
        {
            return _repository;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _repository ??= Repository.Factory.GetCoreV3(ResolveSourceUrl());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Prefers nuget.org and falls back to the first enabled HTTP source the user configured.</summary>
    private string ResolveSourceUrl()
    {
        try
        {
            var snapshot = _settings.Load(rootDirectory: null);
            var enabled = snapshot.Sources.Where(source => source.Enabled).ToList();

            var official = enabled.FirstOrDefault(source => source.Url.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase));
            if (official is not null)
            {
                return official.Url;
            }

            var httpSource = enabled.FirstOrDefault(source =>
                source.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            return httpSource?.Url ?? NuGetOrgSource;
        }
        catch (Exception)
        {
            return NuGetOrgSource;
        }
    }
}

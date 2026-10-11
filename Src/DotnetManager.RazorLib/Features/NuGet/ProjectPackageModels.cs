namespace DotnetManager.RazorLib.Features.NuGet;

public sealed record PackageVulnerability(
    string Severity,
    string? AdvisoryUrl,
    string? Cve)
{
    public int SeverityWeight => Severity?.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "moderate" or "medium" => 2,
        "low" => 1,
        _ => 0,
    };
}

public sealed class ProjectPackageItem
{
    public string ProjectPath { get; set; } = string.Empty;

    public string ProjectName => string.IsNullOrWhiteSpace(ProjectPath)
        ? string.Empty
        : Path.GetFileNameWithoutExtension(ProjectPath);

    public string Framework { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string? RequestedVersion { get; set; }

    public string? ResolvedVersion { get; set; }

    public string? LatestVersion { get; set; }

    public bool IsTransitive { get; set; }

    public bool IsAutoReferenced { get; set; }

    public List<PackageVulnerability> Vulnerabilities { get; set; } = [];

    public bool HasVulnerabilities => Vulnerabilities.Count > 0;

    public bool IsOutdated =>
        !string.IsNullOrWhiteSpace(LatestVersion) &&
        !string.Equals(ResolvedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);

    public bool CanUpgrade => !IsAutoReferenced && (IsOutdated || HasVulnerabilities);

    public int MaxSeverityWeight => Vulnerabilities.Count == 0
        ? 0
        : Vulnerabilities.Max(v => v.SeverityWeight);

    public string? MaxSeverity => Vulnerabilities
        .OrderByDescending(v => v.SeverityWeight)
        .FirstOrDefault()?.Severity;
}

public sealed record ProjectTargetOption(string DisplayName, string? TargetPath, bool IsSolution);

public enum PackageFilterMode
{
    All,
    VulnerableOnly,
    OutdatedOnly,
    UpToDateOnly,
    TopLevelOnly,
    TransitiveOnly,
}

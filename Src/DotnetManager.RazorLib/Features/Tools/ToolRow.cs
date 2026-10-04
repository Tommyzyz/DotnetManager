using CommunityToolkit.Mvvm.ComponentModel;

namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>A single tool, either from a local manifest or from the global tool list.</summary>
public sealed partial class ToolRow : ObservableObject
{
    [ObservableProperty]
    private string? latestVersion;

    [ObservableProperty]
    private bool isChecking;

    public required string PackageId { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyList<string> Commands { get; init; }

    public string? RollForward { get; init; }

    public string CommandText => Commands.Count == 0 ? string.Empty : string.Join(' ', Commands);

    public bool HasUpdate =>
        !string.IsNullOrWhiteSpace(LatestVersion)
        && !string.Equals(Version, LatestVersion, StringComparison.OrdinalIgnoreCase);

    partial void OnLatestVersionChanged(string? value) => OnPropertyChanged(nameof(HasUpdate));
}

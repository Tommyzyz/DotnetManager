namespace DotnetManager.RazorLib.Abstractions;

/// <summary>One executed operation, kept for the run log page.</summary>
public sealed record ActivityEntry(
    DateTimeOffset Timestamp,
    string Description,
    string CommandLine,
    int? ExitCode,
    TimeSpan Duration,
    string Output)
{
    public bool Success => ExitCode == 0;

    public bool IsRunning => ExitCode is null;
}

public interface IActivityLog
{
    IReadOnlyList<ActivityEntry> Entries { get; }

    event Action<ActivityEntry>? EntryAdded;

    event Action? Cleared;

    void Add(ActivityEntry entry);

    void Clear();
}

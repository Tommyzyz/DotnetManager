using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>Shows every dotnet command the application has executed.</summary>
public sealed partial class ActivityLogViewModel : ViewModelBase, IDisposable
{
    private readonly IActivityLog _activity;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<ActivityEntry> entries = [];

    [ObservableProperty]
    private ActivityEntry? selectedEntry;

    [ObservableProperty]
    private string filter = string.Empty;

    public ActivityLogViewModel(IActivityLog activity, IDotnetCli cli)
    {
        _activity = activity;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public IReadOnlyList<ActivityEntry> FilteredEntries => string.IsNullOrWhiteSpace(Filter)
        ? Entries
        : Entries.Where(entry =>
            entry.Description.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)
            || entry.CommandLine.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    public int EntryCount => Entries.Count;

    public void Attach()
    {
        Entries = _activity.Entries;
        _activity.EntryAdded += OnEntryAdded;
        _activity.Cleared += OnCleared;
    }

    public void Clear()
    {
        _activity.Clear();
        Entries = [];
        SelectedEntry = null;
    }

    public void Select(ActivityEntry? entry) => SelectedEntry = entry;

    public void Dispose()
    {
        _activity.EntryAdded -= OnEntryAdded;
        _activity.Cleared -= OnCleared;
    }

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredEntries));

    partial void OnEntriesChanged(IReadOnlyList<ActivityEntry> value)
    {
        OnPropertyChanged(nameof(FilteredEntries));
        OnPropertyChanged(nameof(EntryCount));
    }

    private void OnEntryAdded(ActivityEntry entry) => Entries = _activity.Entries;

    private void OnCleared() => Entries = [];
}

using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

public sealed class ActivityLog : IActivityLog
{
    private const int MaxEntries = 500;

    private readonly Lock _gate = new();
    private readonly LinkedList<ActivityEntry> _entries = new();

    public IReadOnlyList<ActivityEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public event Action<ActivityEntry>? EntryAdded;

    public event Action? Cleared;

    public void Add(ActivityEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveLast();
            }
        }

        EntryAdded?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Cleared?.Invoke();
    }
}

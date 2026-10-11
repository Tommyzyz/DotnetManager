using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Tests.Fakes;

/// <summary>Keeps settings in memory and counts saves.</summary>
public sealed class FakeAppSettingsStore : IAppSettingsStore
{
    public AppSettings Current { get; } = new();

    public string SettingsFilePath { get; init; } = "settings.json";

    public int SaveCount { get; private set; }

    public event Action? Changed;

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}

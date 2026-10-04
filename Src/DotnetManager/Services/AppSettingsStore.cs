using System.IO;
using System.Text.Json;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public AppSettingsStore()
    {
        SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DotnetManager",
            "settings.json");

        Current = Load();
    }

    public AppSettings Current { get; }

    public string SettingsFilePath { get; }

    public event Action? Changed;

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions, cancellationToken);
        Changed?.Invoke();
    }

    private AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }
}

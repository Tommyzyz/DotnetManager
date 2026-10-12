using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>
/// The one project every project-scoped page works against.
/// <para>
/// Each of those pages used to keep its own working directory, seeded from the same setting but updated
/// independently, so picking a folder on one page left the others pointing somewhere else. This is the
/// single writer now: pages read <see cref="Directory"/> and change it through <see cref="PickAsync"/>,
/// and every page reloads when <see cref="Changed"/> fires.
/// </para>
/// </summary>
public sealed partial class ProjectTarget : ObservableObject
{
    private readonly IAppSettingsStore _settings;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private string directory;

    public ProjectTarget(IAppSettingsStore settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        directory = Normalize(settings.Current.LastWorkingDirectory) ?? Environment.CurrentDirectory;
    }

    /// <summary>Raised after the target changed, so the project-scoped pages can reload from it.</summary>
    public event Action? Changed;

    /// <summary>Points every project-scoped page at <paramref name="path"/>; false when nothing changed.</summary>
    public async Task<bool> UseAsync(string? path)
    {
        if (Normalize(path) is not { } full || string.Equals(full, Directory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory = full;
        _settings.Current.LastWorkingDirectory = full;
        await _settings.SaveAsync();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Asks for a directory and adopts the pick.</summary>
    public Task<bool> PickAsync(string title) => UseAsync(_dialogs.PickFolder(title, Directory));

    private static string? Normalize(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IODirectory.Exists(path) ? IOPath.GetFullPath(path) : null;
}

namespace DotnetManager.RazorLib.Abstractions;

public sealed record OperationResult(bool Success, string? Message = null, string? Detail = null)
{
    public static OperationResult Ok(string? message = null) => new(true, message);

    public static OperationResult Fail(string message, string? detail = null) => new(false, message, detail);
}

public enum EnvScope
{
    /// <summary>HKCU\Environment - no elevation required.</summary>
    User,

    /// <summary>HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment - requires elevation.</summary>
    Machine,

    /// <summary>The environment of the running process, never persisted.</summary>
    Process,
}

public interface IEnvironmentService
{
    bool IsElevated { get; }

    string? Get(string name, EnvScope scope);

    /// <summary>Reads the value the current process would use.</summary>
    string? GetEffective(string name);

    Task<OperationResult> SetAsync(string name, string? value, EnvScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Names of the headless operations that an elevated child process can perform.
/// Shared between the view models (which request them) and the host (which executes them).
/// </summary>
public static class HeadlessOperations
{
    public const string WriteFile = "write-file";
    public const string SetEnvironmentVariable = "set-env";
    public const string RunDotnet = "dotnet-run";
}

public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>Relaunches this executable elevated and runs the given headless operation.</summary>
    Task<OperationResult> RunElevatedAsync(string operation, IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default);
}

public interface IDialogService
{
    string? PickFolder(string title, string? initialDirectory = null);

    string? PickFile(string title, string filter, string? initialDirectory = null);

    bool Confirm(string message, string title);

    void ShowError(string message, string title);

    void ShowInfo(string message, string title);

    /// <summary>Opens a file or folder with the shell (Explorer).</summary>
    void RevealInShell(string path);
}

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-Hans";

    public string? DotnetPath { get; set; }

    public string? LastWorkingDirectory { get; set; }

    public bool IncludePrerelease { get; set; }

    public bool AutoCheckToolUpdates { get; set; } = true;
}

public interface IAppSettingsStore
{
    AppSettings Current { get; }

    string SettingsFilePath { get; }

    event Action? Changed;

    Task SaveAsync(CancellationToken cancellationToken = default);
}

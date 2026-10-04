using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

/// <summary>Runs privileged operations inside a child process that was started with the runas verb.</summary>
public sealed class ElevationService : IElevationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public bool IsElevated => EnvironmentService.CurrentUserIsAdministrator();

    public async Task<OperationResult> RunElevatedAsync(
        string operation,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken = default)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return OperationResult.Fail("Cannot determine the application path for elevation.");
        }

        var requestFile = Path.Combine(Path.GetTempPath(), $"dotnetmanager-request-{Guid.NewGuid():N}.json");
        var resultFile = Path.Combine(Path.GetTempPath(), $"dotnetmanager-result-{Guid.NewGuid():N}.json");
        var request = new HeadlessRequest(operation, new Dictionary<string, string>(payload));
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request, SerializerOptions), cancellationToken)
            .ConfigureAwait(false);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = BuildArguments(executable, requestFile, resultFile),
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return OperationResult.Fail("The elevated process could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return ReadResult(resultFile, process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return OperationResult.Fail("The elevation prompt was declined (UAC).", ex.Message);
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("The elevated operation was cancelled.");
        }
        finally
        {
            TryDelete(requestFile);
            TryDelete(resultFile);
        }
    }

    private static string BuildArguments(string executable, string requestFile, string resultFile)
    {
        var parts = new List<string>();

        // When the app runs as "dotnet app.dll" the entry assembly has to be passed through again.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssembly))
            {
                parts.Add(Quote(entryAssembly!));
            }
        }

        parts.Add(Quote($"--request={requestFile}"));
        parts.Add(Quote($"--out={resultFile}"));
        return string.Join(' ', parts);
    }

    private static OperationResult ReadResult(string resultFile, int exitCode)
    {
        if (!File.Exists(resultFile))
        {
            return OperationResult.Fail(
                $"The elevated operation finished with exit code {exitCode} but produced no result.",
                "Try running the application as administrator to see the underlying error.");
        }

        try
        {
            var json = File.ReadAllText(resultFile);
            var result = JsonSerializer.Deserialize<HeadlessResult>(json, SerializerOptions);
            if (result is null)
            {
                return OperationResult.Fail("The elevated operation returned an empty result.");
            }

            return result.Success
                ? OperationResult.Ok(result.Message)
                : OperationResult.Fail(result.Message ?? "The elevated operation failed.", result.Detail);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return OperationResult.Fail("The elevated operation result could not be read.", ex.Message);
        }
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup only.
        }
    }
}

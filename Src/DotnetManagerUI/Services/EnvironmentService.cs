using System.Security;
using System.Security.Principal;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManagerUI.Services;

public sealed class EnvironmentService : IEnvironmentService
{
    private readonly IElevationService _elevation;

    public EnvironmentService(IElevationService elevation) => _elevation = elevation;

    public bool IsElevated => CurrentUserIsAdministrator();

    public string? Get(string name, EnvScope scope) => scope switch
    {
        EnvScope.User => ReadVariable(name, EnvironmentVariableTarget.User),
        EnvScope.Machine => ReadVariable(name, EnvironmentVariableTarget.Machine),
        _ => ReadVariable(name, EnvironmentVariableTarget.Process),
    };

    public string? GetEffective(string name) => Environment.GetEnvironmentVariable(name);

    public async Task<OperationResult> SetAsync(string name, string? value, EnvScope scope, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("The variable name must not be empty.");
        }

        var normalized = string.IsNullOrWhiteSpace(value) ? null : value;

        if (scope == EnvScope.Process)
        {
            Environment.SetEnvironmentVariable(name, normalized, EnvironmentVariableTarget.Process);
            return OperationResult.Ok($"{name} = {normalized ?? "(unset)"}");
        }

        if (scope == EnvScope.Machine && !IsElevated)
        {
            return await _elevation.RunElevatedAsync(
                HeadlessOperations.SetEnvironmentVariable,
                new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["value"] = normalized ?? string.Empty,
                    ["scope"] = nameof(EnvScope.Machine),
                },
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            Environment.SetEnvironmentVariable(
                name,
                normalized,
                scope == EnvScope.Machine ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return OperationResult.Fail(ex.Message);
        }

        // Keep the current process in sync so the UI reflects the new value immediately.
        Environment.SetEnvironmentVariable(name, normalized, EnvironmentVariableTarget.Process);
        return OperationResult.Ok($"{name} = {normalized ?? "(unset)"}");
    }

    public static bool CurrentUserIsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ReadVariable(string name, EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, target);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

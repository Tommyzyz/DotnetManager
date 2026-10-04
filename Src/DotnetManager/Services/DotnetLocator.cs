using System.IO;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

public sealed class DotnetLocator : IDotnetLocator
{
    private const string ExecutableName = "dotnet.exe";

    public string Resolve(string? preferredPath = null)
    {
        if (IsValid(preferredPath))
        {
            return preferredPath!;
        }

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, ExecutableName);
            if (IsValid(candidate))
            {
                return candidate;
            }
        }

        var onPath = Environment.GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().Trim('"'))
            .Where(entry => entry.Length > 0)
            .Select(entry => Path.Combine(entry, ExecutableName))
            .FirstOrDefault(IsValid);

        if (onPath is not null)
        {
            return onPath;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            ExecutableName);

        return IsValid(fallback) ? fallback : "dotnet";
    }

    public bool IsValid(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}

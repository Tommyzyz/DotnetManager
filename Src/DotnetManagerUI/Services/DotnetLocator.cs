using System.IO;
using System.Security;
using DotnetManager.RazorLib.Abstractions;
using Microsoft.Win32;

namespace DotnetManagerUI.Services;

public sealed class DotnetLocator : IDotnetLocator
{
    private const string ExecutableName = "dotnet.exe";

    /// <summary>Registry locations that record where dotnet was installed.</summary>
    private static readonly string[] InstallDirKeys =
    {
        @"SOFTWARE\dotnet\Setup\InstalledVersions\x64",
        @"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64",
        @"SOFTWARE\dotnet\Setup\InstalledVersions\x86",
    };

    public string Resolve(string? preferredPath = null) => ResolveDetailed(preferredPath).Path;

    public ResolvedDotnet ResolveDetailed(string? preferredPath = null)
    {
        if (IsValid(preferredPath))
        {
            return new ResolvedDotnet(preferredPath!, DotnetPathSource.Configured);
        }

        foreach (var variable in new[] { "DOTNET_ROOT", "DOTNET_ROOT(x86)" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, ExecutableName);
            if (IsValid(candidate))
            {
                return new ResolvedDotnet(candidate, DotnetPathSource.DotnetRoot);
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
            return new ResolvedDotnet(onPath, DotnetPathSource.Path);
        }

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var candidate = Path.Combine(folder, "dotnet", ExecutableName);
            if (IsValid(candidate))
            {
                return new ResolvedDotnet(candidate, DotnetPathSource.ProgramFiles);
            }
        }

        // The registry keeps the install directory even when PATH has not been refreshed yet,
        // which is exactly the situation right after an SDK was installed.
        var registered = RegistryInstallDir();
        if (registered is not null && IsValid(Path.Combine(registered, ExecutableName)))
        {
            return new ResolvedDotnet(Path.Combine(registered, ExecutableName), DotnetPathSource.Registry);
        }

        return new ResolvedDotnet("dotnet", DotnetPathSource.None);
    }

    public bool IsValid(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <summary>Reads the install directory dotnet recorded under HKLM, if any.</summary>
    public static string? RegistryInstallDir()
    {
        foreach (var keyName in InstallDirKeys)
        {
            var directory = ReadValue(keyName, "InstallDir");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory.Trim().TrimEnd('\\');
            }
        }

        return null;
    }

    /// <summary>Reads the SDK versions dotnet recorded under HKLM, if any.</summary>
    public static IReadOnlyList<string> RegistrySdkVersions()
    {
        foreach (var keyName in InstallDirKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{keyName}\sdk");
                if (key is null)
                {
                    continue;
                }

                var versions = key.GetSubKeyNames()
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (versions.Count > 0)
                {
                    return versions;
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // A missing or unreadable key simply means "nothing registered here".
            }
        }

        return [];
    }

    private static string? ReadValue(string keyName, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyName);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}

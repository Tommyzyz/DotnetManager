using System.IO;
using System.Text.Json;
using DotnetManager.RazorLib.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetManager.Services;

/// <summary>Request contract that the parent process hands to the elevated child through a temp file.</summary>
public sealed record HeadlessRequest(string Operation, Dictionary<string, string> Payload);

/// <summary>Result contract that the elevated child process writes back as JSON.</summary>
public sealed record HeadlessResult(bool Success, string? Message, string? Detail);

/// <summary>
/// Entry point for the <c>--request</c> child process that the UI starts with the runas verb.
/// The request and the result travel as files so that paths, quotes and JSON payloads survive intact.
/// </summary>
public static class Headless
{
    private const string RequestPrefix = "--request=";
    private const string OutputPrefix = "--out=";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static bool IsHeadless(IReadOnlyList<string> arguments) =>
        arguments.Any(argument => argument.StartsWith(RequestPrefix, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, IServiceProvider services)
    {
        var requestFile = ReadArgument(arguments, RequestPrefix);
        var resultFile = ReadArgument(arguments, OutputPrefix);
        HeadlessResult result;

        try
        {
            if (string.IsNullOrWhiteSpace(requestFile) || !File.Exists(requestFile))
            {
                result = new HeadlessResult(false, "The request file is missing.", null);
            }
            else
            {
                var json = await File.ReadAllTextAsync(requestFile).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<HeadlessRequest>(json, SerializerOptions);
                result = request is null
                    ? new HeadlessResult(false, "The request file could not be parsed.", null)
                    : await ExecuteAsync(request, services).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            result = new HeadlessResult(false, ex.Message, ex.ToString());
        }

        if (!string.IsNullOrWhiteSpace(resultFile))
        {
            var directory = Path.GetDirectoryName(resultFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(resultFile, JsonSerializer.Serialize(result, SerializerOptions)).ConfigureAwait(false);
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<HeadlessResult> ExecuteAsync(HeadlessRequest request, IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(request.Operation))
        {
            return new HeadlessResult(false, "No operation was specified.", null);
        }

        try
        {
            return request.Operation switch
            {
                HeadlessOperations.WriteFile => WriteFile(request.Payload),
                HeadlessOperations.SetEnvironmentVariable => SetEnvironmentVariable(request.Payload),
                HeadlessOperations.RunDotnet => await RunDotnetAsync(request.Payload, services).ConfigureAwait(false),
                _ => new HeadlessResult(false, $"Unknown operation '{request.Operation}'.", null),
            };
        }
        catch (Exception ex)
        {
            return new HeadlessResult(false, ex.Message, ex.ToString());
        }
    }

    private static HeadlessResult WriteFile(IReadOnlyDictionary<string, string> payload)
    {
        if (!payload.TryGetValue("source", out var source) || !payload.TryGetValue("target", out var target))
        {
            return new HeadlessResult(false, "Both 'source' and 'target' are required.", null);
        }

        if (!File.Exists(source))
        {
            return new HeadlessResult(false, $"The source file '{source}' does not exist.", null);
        }

        var content = File.ReadAllText(source);
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(target))
        {
            File.Copy(target, target + ".bak", overwrite: true);
        }

        File.WriteAllText(target, content);
        return new HeadlessResult(true, $"Wrote {target}", null);
    }

    private static HeadlessResult SetEnvironmentVariable(IReadOnlyDictionary<string, string> payload)
    {
        if (!payload.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return new HeadlessResult(false, "The variable name is required.", null);
        }

        payload.TryGetValue("value", out var value);
        payload.TryGetValue("scope", out var scope);

        var target = string.Equals(scope, nameof(EnvScope.Machine), StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableTarget.Machine
            : EnvironmentVariableTarget.User;

        Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(value) ? null : value, target);
        return new HeadlessResult(true, $"{name} updated ({target}).", null);
    }

    private static async Task<HeadlessResult> RunDotnetAsync(IReadOnlyDictionary<string, string> payload, IServiceProvider services)
    {
        if (!payload.TryGetValue("args", out var json) || string.IsNullOrWhiteSpace(json))
        {
            return new HeadlessResult(false, "The 'args' payload is required.", null);
        }

        var commandArguments = JsonSerializer.Deserialize<string[]>(json);
        if (commandArguments is null || commandArguments.Length == 0)
        {
            return new HeadlessResult(false, "The 'args' payload must contain at least one argument.", null);
        }

        payload.TryGetValue("workingDirectory", out var workingDirectory);
        var cli = services.GetRequiredService<IDotnetCli>();
        var result = await cli.RunToResultAsync(
            commandArguments,
            new CliOptions
            {
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                Description = "dotnet " + string.Join(' ', commandArguments),
            }).ConfigureAwait(false);

        return new HeadlessResult(result.Success, $"dotnet exited with code {result.ExitCode}.", result.CombinedOutput);
    }

    private static string? ReadArgument(IReadOnlyList<string> arguments, string prefix) =>
        arguments
            .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?[prefix.Length..];
}

namespace DotnetManager.RazorLib.Abstractions;

/// <summary>A single line of output produced by a dotnet CLI invocation.</summary>
public sealed record CliLine(string Text, bool IsError = false);

/// <summary>The captured result of a finished dotnet CLI invocation.</summary>
public sealed record CliResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)
{
    public bool Success => ExitCode == 0;

    public string CombinedOutput
    {
        get
        {
            var parts = new[] { StandardOutput, StandardError }.Where(static s => !string.IsNullOrWhiteSpace(s));
            return string.Join(Environment.NewLine, parts);
        }
    }
}

public sealed class CliOptions
{
    public string? WorkingDirectory { get; init; }

    /// <summary>Human readable label used for the activity log.</summary>
    public string? Description { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>Runs the CLI with an English UI culture so table output stays parseable.</summary>
    public bool InvariantOutput { get; init; }
}

public interface IDotnetCli
{
    string ExecutablePath { get; }

    IAsyncEnumerable<CliLine> RunAsync(IReadOnlyList<string> arguments, CliOptions? options = null, CancellationToken cancellationToken = default);

    Task<CliResult> RunToResultAsync(IReadOnlyList<string> arguments, CliOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a command, reporting every output line to <paramref name="onOutput"/> as it arrives.</summary>
    Task<CliResult> RunToResultAsync(IReadOnlyList<string> arguments, CliOptions? options, Action<CliLine>? onOutput, CancellationToken cancellationToken = default);
}

public interface IDotnetLocator
{
    /// <summary>Resolves the dotnet executable, preferring an explicit user configured path.</summary>
    string Resolve(string? preferredPath = null);

    bool IsValid(string? path);
}

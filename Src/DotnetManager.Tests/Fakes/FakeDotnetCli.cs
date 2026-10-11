using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Tests.Fakes;

/// <summary>
/// Stand-in for the dotnet CLI: records every invocation and answers through
/// <see cref="Responder"/>, so view models can be driven without a real process.
/// </summary>
public sealed class FakeDotnetCli : IDotnetCli
{
    public List<IReadOnlyList<string>> Invocations { get; } = [];

    /// <summary>Answers one invocation. The default succeeds with no output.</summary>
    public Func<IReadOnlyList<string>, CliOptions?, CliResult> Responder { get; set; } =
        static (_, _) => new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero);

    public string ExecutablePath => "dotnet";

    public IReadOnlyList<IReadOnlyList<string>> InvocationsContaining(string token) =>
        [.. Invocations.Where(args => args.Any(a => a.Contains(token, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>Matches on the first argument, so paths that happen to contain a word do not confuse a query.</summary>
    public IReadOnlyList<IReadOnlyList<string>> InvocationsStartingWith(string command) =>
        [.. Invocations.Where(args => args.Count > 0 && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase))];

    public Task<CliResult> RunToResultAsync(
        IReadOnlyList<string> arguments,
        CliOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Respond(arguments, options));

    public Task<CliResult> RunToResultAsync(
        IReadOnlyList<string> arguments,
        CliOptions? options,
        Action<CliLine>? onOutput,
        CancellationToken cancellationToken = default)
    {
        var result = Respond(arguments, options);

        if (onOutput is not null)
        {
            foreach (var line in result.CombinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                onOutput(new CliLine(line.TrimEnd('\r')));
            }
        }

        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<CliLine> RunAsync(
        IReadOnlyList<string> arguments,
        CliOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = Respond(arguments, options);
        foreach (var line in result.CombinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return new CliLine(line.TrimEnd('\r'));
        }

        await Task.CompletedTask;
    }

    private CliResult Respond(IReadOnlyList<string> arguments, CliOptions? options)
    {
        Invocations.Add([.. arguments]);
        return Responder(arguments, options);
    }
}

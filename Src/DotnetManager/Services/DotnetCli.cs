using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.Services;

/// <summary>Runs the dotnet CLI, streaming output line by line and recording every run in the activity log.</summary>
public sealed class DotnetCli : IDotnetCli
{
    private readonly IDotnetLocator _locator;
    private readonly IAppSettingsStore _settings;
    private readonly IActivityLog _activity;
    private readonly LocalizationState _localization;

    public DotnetCli(IDotnetLocator locator, IAppSettingsStore settings, IActivityLog activity, LocalizationState localization)
    {
        _locator = locator;
        _settings = settings;
        _activity = activity;
        _localization = localization;
    }

    public string ExecutablePath => _locator.Resolve(_settings.Current.DotnetPath);

    public async IAsyncEnumerable<CliLine> RunAsync(
        IReadOnlyList<string> arguments,
        CliOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var run = Start(arguments, options, cancellationToken);
        await foreach (var line in run.Lines.ReadAllAsync(CancellationToken.None))
        {
            yield return line;
        }
    }

    public Task<CliResult> RunToResultAsync(
        IReadOnlyList<string> arguments,
        CliOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunToResultAsync(arguments, options, onOutput: null, cancellationToken);

    public async Task<CliResult> RunToResultAsync(
        IReadOnlyList<string> arguments,
        CliOptions? options,
        Action<CliLine>? onOutput,
        CancellationToken cancellationToken = default)
    {
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        using var run = Start(arguments, options, cancellationToken);

        await foreach (var line in run.Lines.ReadAllAsync(CancellationToken.None))
        {
            (line.IsError ? standardError : standardOutput).AppendLine(line.Text);
            onOutput?.Invoke(line);
        }

        var exitCode = await run.Completion.ConfigureAwait(false);
        return new CliResult(exitCode, standardOutput.ToString(), standardError.ToString(), run.Elapsed);
    }

    private CliRun Start(IReadOnlyList<string> arguments, CliOptions? options, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(arguments, options);
        var description = options?.Description ?? $"dotnet {string.Join(' ', arguments)}";
        var commandLine = CommandLine(startInfo);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var channel = Channel.CreateUnbounded<CliLine>(new UnboundedChannelOptions { SingleReader = true });

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            standardOutput.AppendLine(e.Data);
            channel.Writer.TryWrite(new CliLine(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            standardError.AppendLine(e.Data);
            channel.Writer.TryWrite(new CliLine(e.Data, true));
        };

        var timeoutSource = options?.Timeout is { } timeout ? new CancellationTokenSource(timeout) : null;
        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource?.Token ?? CancellationToken.None);
        var registration = linkedSource.Token.Register(() => TryKill(process));

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            registration.Dispose();
            linkedSource.Dispose();
            timeoutSource?.Dispose();
            channel.Writer.TryComplete();

            var message = $"Cannot start '{startInfo.FileName}': {ex.Message}";
            _activity.Add(new ActivityEntry(DateTimeOffset.Now, description, commandLine, -1, TimeSpan.Zero, message));
            channel.Writer.TryWrite(new CliLine(message, true));
            return new CliRun(process, channel.Reader, Task.FromResult(-1), TimeSpan.Zero);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var stopwatch = Stopwatch.StartNew();
        var completion = Task.Run(() =>
        {
            var note = default(string);
            var exitCode = -1;
            try
            {
                process.WaitForExit();
                exitCode = process.HasExited ? process.ExitCode : -1;
                note = linkedSource.IsCancellationRequested
                    ? timeoutSource?.IsCancellationRequested == true ? "Timed out." : "Cancelled."
                    : null;
            }
            finally
            {
                stopwatch.Stop();
                channel.Writer.TryComplete();
                _activity.Add(new ActivityEntry(
                    DateTimeOffset.Now,
                    description,
                    commandLine,
                    exitCode,
                    stopwatch.Elapsed,
                    Combine(standardOutput, standardError, note)));

                registration.Dispose();
                linkedSource.Dispose();
                timeoutSource?.Dispose();
            }

            return exitCode;
        });

        return new CliRun(process, channel.Reader, completion, stopwatch.Elapsed);
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments, CliOptions? options)
    {
        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrWhiteSpace(options?.WorkingDirectory) && Directory.Exists(options.WorkingDirectory))
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = options?.InvariantOutput == true ? "en" : _localization.Language;

        if (options?.Environment is { } extra)
        {
            foreach (var (key, value) in extra)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(key);
                }
                else
                {
                    startInfo.Environment[key] = value;
                }
            }
        }

        return startInfo;
    }

    private static string Combine(StringBuilder standardOutput, StringBuilder standardError, string? note)
    {
        var builder = new StringBuilder();
        builder.Append(standardOutput);
        if (standardError.Length > 0)
        {
            builder.Append(standardError);
        }

        if (note is not null)
        {
            builder.AppendLine(note);
        }

        return builder.ToString().TrimEnd();
    }

    private static string CommandLine(ProcessStartInfo startInfo) =>
        string.Join(' ', new[] { startInfo.FileName }.Concat(startInfo.ArgumentList.Select(QuoteIfNeeded)));

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process already exited or cannot be terminated; nothing useful to do here.
        }
    }

    private sealed class CliRun(Process process, ChannelReader<CliLine> lines, Task<int> completion, TimeSpan elapsed) : IDisposable
    {
        public Process Process { get; } = process;

        public ChannelReader<CliLine> Lines { get; } = lines;

        public Task<int> Completion { get; } = completion;

        public TimeSpan Elapsed { get; } = elapsed;

        public void Dispose() => Process.Dispose();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>Shared busy/status/output plumbing for the page view models.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private const int MaxOutputLines = 400;

    private readonly List<string> _output = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool statusIsError;

    protected abstract IDotnetCli Cli { get; }

    public IReadOnlyList<string> Output => _output;

    public bool HasOutput => _output.Count > 0;

    public string OutputText => string.Join(Environment.NewLine, _output);

    protected void SetStatus(string? message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    protected void ClearOutput()
    {
        _output.Clear();
        NotifyOutputChanged();
    }

    protected void AppendOutput(string line)
    {
        _output.Add(line);
        while (_output.Count > MaxOutputLines)
        {
            _output.RemoveAt(0);
        }

        NotifyOutputChanged();
    }

    /// <summary>Runs a dotnet command, appending its streamed output and returning the final result.</summary>
    protected Task<CliResult> RunCliAsync(
        IReadOnlyList<string> arguments,
        string description,
        string? workingDirectory = null,
        bool invariantOutput = true)
    {
        AppendOutput($"> {description}");

        return Cli.RunToResultAsync(
            arguments,
            new CliOptions
            {
                Description = description,
                WorkingDirectory = workingDirectory,
                InvariantOutput = invariantOutput,
            },
            line => AppendOutput(line.Text));
    }

    /// <summary>Runs one operation with busy/status handling.</summary>
    protected async Task RunAsync(Func<Task> operation, bool clearOutput = true)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        StatusMessage = null;
        if (clearOutput)
        {
            ClearOutput();
        }

        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operation cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyOutputChanged()
    {
        OnPropertyChanged(nameof(Output));
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(OutputText));
    }
}

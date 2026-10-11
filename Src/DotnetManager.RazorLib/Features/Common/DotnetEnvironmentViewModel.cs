using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Resources;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>
/// Shared state for the .NET SDK detection shown in the layout banner and on the SDK page.
/// Registered as a singleton so both views see the same result and the same re-check.
/// </summary>
public sealed partial class DotnetEnvironmentViewModel : ViewModelBase
{
    private readonly IDotnetEnvironmentService _environment;
    private readonly IDotnetSdkInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly UiLocalizer _localizer;

    [ObservableProperty]
    private DotnetEnvironmentStatus? status;

    [ObservableProperty]
    private bool dismissed;

    [ObservableProperty]
    private string downloadStatus = string.Empty;

    public DotnetEnvironmentViewModel(
        IDotnetCli cli,
        IDotnetEnvironmentService environment,
        IDotnetSdkInstaller installer,
        IDialogService dialogs,
        UiLocalizer localizer)
    {
        Cli = cli;
        _environment = environment;
        _installer = installer;
        _dialogs = dialogs;
        _localizer = localizer;
    }

    protected override IDotnetCli Cli { get; }

    /// <summary>The banner is only shown when something needs the user's attention.</summary>
    public bool NeedsAttention => !Dismissed && Status is
    {
        State: DotnetState.NotInstalled or DotnetState.RuntimeOnly or DotnetState.InstalledButStalePath or DotnetState.SdkVersionTooOld,
    };

    /// <summary>The release channel the user should install, e.g. "8.0" when global.json asks for it.</summary>
    public string? Channel
    {
        get
        {
            var required = Status?.RequiredSdkVersion;
            if (string.IsNullOrWhiteSpace(required))
            {
                return null;
            }

            var parts = required.Split('.');
            return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : null;
        }
    }

    public bool DownloadVisible => Status?.State is DotnetState.NotInstalled or DotnetState.RuntimeOnly or DotnetState.SdkVersionTooOld;

    public Task InitializeAsync() => Status is null ? RefreshAsync() : Task.CompletedTask;

    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            Status = await _environment.DetectAsync();
            DownloadStatus = string.Empty;

            if (Status.State is DotnetState.Installed or DotnetState.InstalledButStalePath)
            {
                SetStatus(_localizer["Env.Redetect.Done", Status.Sdks.Count, Status.Runtimes.Count]);
            }
            else if (!string.IsNullOrWhiteSpace(Status.Detail))
            {
                SetStatus(Status.Detail, isError: true);
            }
            else
            {
                SetStatus(_localizer["Env.State." + StateKey(Status.State)]);
            }
        });
    }

    public async Task DownloadInstallerAsync()
    {
        var result = await RunDownloadAsync();
        if (result is { Success: true })
        {
            // The installer was started; re-check so the banner disappears once it finishes.
            await RefreshAsync();
        }
    }

    public async Task OpenDownloadPageAsync()
    {
        var result = await _installer.OpenDownloadPageAsync(Channel);
        SetStatus(result.Success
            ? _localizer["Env.Download.OpenPage", result.Message ?? string.Empty]
            : _localizer["Env.Download.Failed", result.Message ?? string.Empty],
            isError: !result.Success);
    }

    public void Dismiss() => Dismissed = true;

    /// <summary>Shows the banner again from the SDK page.</summary>
    public void Restore() => Dismissed = false;

    private async Task<OperationResult?> RunDownloadAsync()
    {
        OperationResult? outcome = null;

        await RunAsync(async () =>
        {
            var progress = new Progress<string>(line =>
            {
                DownloadStatus = line;
                AppendOutput(line);
            });

            SetStatus(_localizer["Env.Download.Start"]);
            outcome = await _installer.DownloadAndRunInstallerAsync(Channel, progress);
            DownloadStatus = string.Empty;

            SetStatus(outcome.Success
                ? _localizer["Env.Download.Done", outcome.Message ?? string.Empty]
                : _localizer["Env.Download.Failed", outcome.Message ?? string.Empty],
                isError: !outcome.Success);
        });

        return outcome;
    }

    partial void OnStatusChanged(DotnetEnvironmentStatus? value)
    {
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(DownloadVisible));
    }

    partial void OnDismissedChanged(bool value) => OnPropertyChanged(nameof(NeedsAttention));

    public static string StateKey(DotnetState state) => state switch
    {
        DotnetState.NotInstalled => "NotInstalled",
        DotnetState.RuntimeOnly => "RuntimeOnly",
        DotnetState.Installed => "Installed",
        DotnetState.InstalledButStalePath => "StalePath",
        DotnetState.SdkVersionTooOld => "VersionTooOld",
        _ => "Unknown",
    };
}

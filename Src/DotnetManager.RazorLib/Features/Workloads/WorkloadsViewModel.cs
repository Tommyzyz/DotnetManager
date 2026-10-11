using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Workloads;

/// <summary>Lists, searches, installs, updates and removes .NET workloads.</summary>
public sealed partial class WorkloadsViewModel : ViewModelBase
{
    private readonly ISdkService _sdk;
    private readonly IElevationService _elevation;
    private readonly IEnvironmentService _environment;
    private readonly IDialogService _dialogs;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<WorkloadInfo> workloads = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string newWorkloadId = string.Empty;

    [ObservableProperty]
    private bool runElevated;

    public WorkloadsViewModel(
        ISdkService sdk,
        IElevationService elevation,
        IEnvironmentService environment,
        IDialogService dialogs,
        IDotnetCli cli)
    {
        _sdk = sdk;
        _elevation = elevation;
        _environment = environment;
        _dialogs = dialogs;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public bool IsElevated => _environment.IsElevated;
    public async Task InitializeAsync() => await RefreshAsync();
    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            Workloads = await _sdk.GetWorkloadsAsync();
            SetStatus($"{Workloads.Count} workload(s) installed.");
        });
    }

    public async Task ShowRawListAsync()
    {
        await RunAsync(async () =>
        {
            await RunCliAsync(["workload", "list"], "dotnet workload list");
            SetStatus("Raw workload list loaded.");
        });
    }

    public async Task SearchAsync()
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                SetStatus("Enter a search term.", isError: true);
                return;
            }

            await RunCliAsync(["workload", "search", SearchQuery.Trim()], $"dotnet workload search {SearchQuery}");
            SetStatus("Search finished.");
        });
    }

    public async Task InstallAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWorkloadId))
        {
            SetStatus("Enter a workload id.", isError: true);
            return;
        }

        await RunWorkloadAsync(["workload", "install", NewWorkloadId.Trim()], $"dotnet workload install {NewWorkloadId}");
        NewWorkloadId = string.Empty;
        await RefreshAsync();
    }

    public async Task UpdateAllAsync()
    {
        await RunWorkloadAsync(["workload", "update"], "dotnet workload update");
        await RefreshAsync();
    }

    public async Task RepairAsync()
    {
        await RunWorkloadAsync(["workload", "repair"], "dotnet workload repair");
        await RefreshAsync();
    }

    public async Task UninstallAsync(WorkloadInfo? workload)
    {
        if (workload is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Uninstall the workload '{workload.Id}'?", "Uninstall workload"))
        {
            return;
        }

        await RunWorkloadAsync(["workload", "uninstall", workload.Id], $"dotnet workload uninstall {workload.Id}");
        await RefreshAsync();
    }

    private async Task RunWorkloadAsync(IReadOnlyList<string> arguments, string description)
    {
        await RunAsync(async () =>
        {
            if (RunElevated && !IsElevated)
            {
                var result = await _elevation.RunElevatedAsync(
                    HeadlessOperations.RunDotnet,
                    new Dictionary<string, string>
                    {
                        ["args"] = JsonSerializer.Serialize(arguments, AppJsonSerializerContext.Default.IReadOnlyListString),
                    });

                if (!string.IsNullOrWhiteSpace(result.Detail))
                {
                    AppendOutput(result.Detail);
                }

                SetStatus(result.Message, !result.Success);
                return;
            }

            var cliResult = await RunCliAsync(arguments, description);
            SetStatus(cliResult.Success ? $"{description} finished." : $"{description} failed.", !cliResult.Success);
        });
    }
}

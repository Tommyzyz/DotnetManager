using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Sdk;

/// <summary>
/// Machine scoped: the installed SDKs and runtimes, <c>dotnet sdk check</c> and <c>dotnet --info</c>.
/// <para>
/// This page never looks at the selected project, and nothing here writes to one: the global.json editor
/// that used to sit beside the SDK list was dropped, because selecting an SDK version per project is not
/// something this app needs to do. The environment banner still reads global.json to warn when the SDK it
/// asks for is missing - that check lives in the host's <c>DotnetEnvironmentService</c>, not here.
/// </para>
/// </summary>
public sealed partial class SdkInfoViewModel : ViewModelBase
{
    private readonly ISdkService _sdk;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<SdkInfo> sdks = [];

    [ObservableProperty]
    private IReadOnlyList<RuntimeInfo> runtimes = [];

    [ObservableProperty]
    private string infoText = string.Empty;

    [ObservableProperty]
    private string checkText = string.Empty;

    public SdkInfoViewModel(ISdkService sdk, IDotnetCli cli)
    {
        _sdk = sdk;
        _cli = cli;
    }

    protected override IDotnetCli Cli => _cli;

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            Sdks = await _sdk.GetSdksAsync();
            Runtimes = await _sdk.GetRuntimesAsync();
            InfoText = await _sdk.GetInfoAsync();
            SetStatus($"{Sdks.Count} SDK(s) and {Runtimes.Count} runtime(s) detected.");
        });
    }

    public async Task SdkCheckAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _sdk.SdkCheckAsync();
            CheckText = result.CombinedOutput;
            SetStatus(result.Success ? "SDK check finished." : "SDK check failed.", !result.Success);
        }, clearOutput: false);
    }
}

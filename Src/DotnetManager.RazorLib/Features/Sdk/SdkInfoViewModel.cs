using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Sdk;

/// <summary>
/// Machine scoped: the installed SDKs and runtimes, <c>dotnet sdk check</c> and <c>dotnet --info</c>.
/// <para>
/// This page never looks at the selected project. It used to, because the <c>global.json</c> editor
/// lived here, but global.json is written into a project directory: that editor moved to
/// <see cref="GlobalJsonViewModel"/>, which is the only part of this feature that is project scoped.
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

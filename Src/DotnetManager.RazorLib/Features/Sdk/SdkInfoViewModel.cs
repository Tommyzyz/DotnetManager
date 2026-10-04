using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Sdk;

/// <summary>Shows installed SDKs and runtimes, runs <c>dotnet sdk check</c> and edits <c>global.json</c>.</summary>
public sealed partial class SdkInfoViewModel : ViewModelBase
{
    private readonly ISdkService _sdk;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _appSettings;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<SdkInfo> sdks = [];

    [ObservableProperty]
    private IReadOnlyList<RuntimeInfo> runtimes = [];

    [ObservableProperty]
    private string infoText = string.Empty;

    [ObservableProperty]
    private string checkText = string.Empty;

    [ObservableProperty]
    private string globalJsonDirectory = string.Empty;

    [ObservableProperty]
    private string globalJsonPath = string.Empty;

    [ObservableProperty]
    private string globalJsonVersion = string.Empty;

    [ObservableProperty]
    private string globalJsonRollForward = "latestMinor";

    public SdkInfoViewModel(
        ISdkService sdk,
        IDialogService dialogs,
        IAppSettingsStore appSettings,
        IDotnetCli cli)
    {
        _sdk = sdk;
        _dialogs = dialogs;
        _appSettings = appSettings;
        _cli = cli;
        globalJsonDirectory = appSettings.Current.LastWorkingDirectory ?? Environment.CurrentDirectory;
    }

    protected override IDotnetCli Cli => _cli;

    public IReadOnlyList<string> RollForwardOptions { get; } =
    [
        "disable", "patch", "feature", "minor", "major",
        "latestPatch", "latestFeature", "latestMinor", "latestMajor",
    ];
    public async Task InitializeAsync() => await RefreshAsync();
    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            Sdks = await _sdk.GetSdksAsync();
            Runtimes = await _sdk.GetRuntimesAsync();
            InfoText = await _sdk.GetInfoAsync();
            GlobalJsonVersion = Sdks.LastOrDefault()?.Version ?? string.Empty;
            await RefreshGlobalJsonAsync();
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

    public async Task PickGlobalJsonDirectoryAsync()
    {
        var picked = _dialogs.PickFolder("Select the directory for global.json", GlobalJsonDirectory);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        GlobalJsonDirectory = picked;
        _appSettings.Current.LastWorkingDirectory = picked;
        await _appSettings.SaveAsync();
        await RefreshGlobalJsonAsync();
    }

    public async Task WriteGlobalJsonAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _sdk.WriteGlobalJsonAsync(GlobalJsonDirectory, GlobalJsonVersion.Trim(), GlobalJsonRollForward);
            SetStatus(result.Message, !result.Success);
            await RefreshGlobalJsonAsync();
        });
    }

    public void OpenGlobalJson()
    {
        if (File.Exists(GlobalJsonPath))
        {
            _dialogs.RevealInShell(GlobalJsonPath);
        }
        else
        {
            _dialogs.RevealInShell(GlobalJsonDirectory);
        }
    }

    private Task RefreshGlobalJsonAsync()
    {
        GlobalJsonPath = _sdk.FindGlobalJson(GlobalJsonDirectory) ?? Path.Combine(GlobalJsonDirectory, "global.json");
        SetStatus(File.Exists(GlobalJsonPath) ? $"global.json found at {GlobalJsonPath}" : "No global.json in this directory chain.");
        return Task.CompletedTask;
    }
}

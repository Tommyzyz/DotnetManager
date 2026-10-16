using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.Sdk;

/// <summary>
/// Project scoped: reads and writes <c>global.json</c> for the shared <see cref="ProjectTarget"/>.
/// <para>
/// This is the project-coupled half that used to sit on the SDK page. The SDK/runtime list next to it
/// describes the machine, so it stayed there; <c>global.json</c> describes one directory, so it works
/// through the same target as <c>LocalTools</c>, <c>ToolSearch</c> and <c>ProjectPackages</c>.
/// </para>
/// </summary>
public sealed partial class GlobalJsonViewModel : ViewModelBase, IDisposable
{
    private readonly ISdkService _sdk;
    private readonly IDialogService _dialogs;
    private readonly ProjectTarget _target;
    private readonly IDotnetCli _cli;

    [ObservableProperty]
    private IReadOnlyList<SdkInfo> sdks = [];

    [ObservableProperty]
    private string globalJsonPath = string.Empty;

    [ObservableProperty]
    private string globalJsonVersion = string.Empty;

    [ObservableProperty]
    private string globalJsonRollForward = "latestMinor";

    public GlobalJsonViewModel(
        ISdkService sdk,
        IDialogService dialogs,
        ProjectTarget target,
        IDotnetCli cli)
    {
        _sdk = sdk;
        _dialogs = dialogs;
        _target = target;
        _cli = cli;

        _target.Changed += HandleTargetChanged;
    }

    protected override IDotnetCli Cli => _cli;

    /// <summary>The shared project target; global.json is looked up below it.</summary>
    public string GlobalJsonDirectory => _target.Directory;

    public void Dispose() => _target.Changed -= HandleTargetChanged;

    private void HandleTargetChanged()
    {
        OnPropertyChanged(nameof(GlobalJsonDirectory));

        // Only the path is re-read: the SDK list and the version pick belong to the machine, and
        // resetting them here would silently discard a version the user just chose.
        RefreshGlobalJson();
    }

    public IReadOnlyList<string> RollForwardOptions { get; } =
    [
        "disable", "patch", "feature", "minor", "major",
        "latestPatch", "latestFeature", "latestMinor", "latestMajor",
    ];

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            Sdks = await _sdk.GetSdksAsync();
            GlobalJsonVersion = Sdks.LastOrDefault()?.Version ?? string.Empty;
            RefreshGlobalJson();
            SetStatus($"{Sdks.Count} SDK(s) available for global.json.");
        });
    }

    public async Task WriteGlobalJsonAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _sdk.WriteGlobalJsonAsync(GlobalJsonDirectory, GlobalJsonVersion.Trim(), GlobalJsonRollForward);
            SetStatus(result.Message, !result.Success);
            RefreshGlobalJson();
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

    private void RefreshGlobalJson()
    {
        GlobalJsonPath = _sdk.FindGlobalJson(GlobalJsonDirectory) ?? Path.Combine(GlobalJsonDirectory, "global.json");
        SetStatus(File.Exists(GlobalJsonPath) ? $"global.json found at {GlobalJsonPath}" : "No global.json in this directory chain.");
    }
}

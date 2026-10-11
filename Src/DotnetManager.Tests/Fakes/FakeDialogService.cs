using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Tests.Fakes;

/// <summary>Records dialogs instead of showing them.</summary>
public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;

    public string? FolderToPick { get; set; }

    public string? FileToPick { get; set; }

    public List<string> ConfirmMessages { get; } = [];

    public List<string> RevealedPaths { get; } = [];

    public List<string> Errors { get; } = [];

    public string? PickFolder(string title, string? initialDirectory = null) => FolderToPick;

    public string? PickFile(string title, string filter, string? initialDirectory = null) => FileToPick;

    public bool Confirm(string message, string title)
    {
        ConfirmMessages.Add(message);
        return ConfirmResult;
    }

    public void ShowError(string message, string title) => Errors.Add(message);

    public void ShowInfo(string message, string title)
    {
    }

    public void RevealInShell(string path) => RevealedPaths.Add(path);
}

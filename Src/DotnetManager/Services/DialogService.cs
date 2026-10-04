using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DotnetManager.RazorLib.Abstractions;
using Microsoft.Win32;

namespace DotnetManager.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory = null) => Invoke(() =>
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    });

    public string? PickFile(string title, string filter, string? initialDirectory = null) => Invoke(() =>
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    });

    public bool Confirm(string message, string title) => Invoke(() =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    public void ShowError(string message, string title) =>
        Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));

    public void ShowInfo(string message, string title) =>
        Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));

    public void RevealInShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            ShowError(ex.Message, path);
        }
    }

    private static T Invoke<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess()
            ? action()
            : dispatcher.Invoke(action);
    }

    private static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}

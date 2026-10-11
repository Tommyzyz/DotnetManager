using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManagerUI.Services;

/// <summary>
/// WinUI implementation of the shell dialogs.
/// <para>
/// The pickers are the shell's own <c>IFileOpenDialog</c>. It is synchronous and modal, which is what the
/// view models expect (they call <see cref="IDialogService"/> from Blazor's UI thread and use the result
/// immediately), and it takes the same "description|pattern" filter string the WPF dialog used.
/// The message boxes come straight from user32 instead of the WPF ones.
/// </para>
/// </summary>
public sealed class DialogService : IDialogService
{
    /// <summary>SIGDN_FILESYSPATH: ask a shell item for its path.</summary>
    private const uint SigdnFileSystemPath = 0x80058000;

    /// <summary>ERROR_CANCELLED: the user closed the dialog without choosing anything.</summary>
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    private static readonly Guid FileOpenDialogClsid = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");

    public string? PickFolder(string title, string? initialDirectory = null) => Invoke(() =>
    {
        var dialog = CreateDialog(
            title,
            initialDirectory,
            FileOpenOptions.PickFolders | FileOpenOptions.ForceFileSystem | FileOpenOptions.PathMustExist);

        return Show(dialog);
    });

    public string? PickFile(string title, string filter, string? initialDirectory = null) => Invoke(() =>
    {
        var dialog = CreateDialog(
            title,
            initialDirectory,
            FileOpenOptions.FileMustExist | FileOpenOptions.PathMustExist | FileOpenOptions.NoChangeDir);

        ApplyFilter(dialog, filter);
        return Show(dialog);
    });

    public bool Confirm(string message, string title) => Invoke(() =>
        ShowMessageBox(message, title, MessageBoxOptions.YesNo | MessageBoxOptions.IconQuestion) == MessageBoxResult.Yes);

    public void ShowError(string message, string title) =>
        Invoke(() => ShowMessageBox(message, title, MessageBoxOptions.Ok | MessageBoxOptions.IconError));

    public void ShowInfo(string message, string title) =>
        Invoke(() => ShowMessageBox(message, title, MessageBoxOptions.Ok | MessageBoxOptions.IconInformation));

    public void RevealInShell(string path)
    {
        try
        {
            // Opens a folder in Explorer and a URL in the default browser; both go through the shell.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            ShowError(ex.Message, path);
        }
    }

    private static IFileOpenDialog CreateDialog(string title, string? initialDirectory, FileOpenOptions options)
    {
        var clsid = Type.GetTypeFromCLSID(FileOpenDialogClsid)
            ?? throw new InvalidOperationException("The shell file dialog is not available.");

        var dialog = (IFileOpenDialog)Activator.CreateInstance(clsid)!;
        dialog.SetOptions((uint)options);

        if (!string.IsNullOrWhiteSpace(title))
        {
            dialog.SetTitle(title);
        }

        if (TryCreateShellItem(initialDirectory) is { } folder)
        {
            dialog.SetFolder(folder);
        }

        return dialog;
    }

    private static void ApplyFilter(IFileOpenDialog dialog, string filter)
    {
        var specs = ParseFilter(filter);
        if (specs.Length > 0)
        {
            dialog.SetFileTypes((uint)specs.Length, specs);
        }
    }

    /// <summary>Splits the Win32 "description|pattern|description|pattern" filter into the shell's pairs.</summary>
    private static ComDlgFilterSpec[] ParseFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [];
        }

        var parts = filter.Split('|');
        var specs = new List<ComDlgFilterSpec>();
        for (var index = 0; index + 1 < parts.Length; index += 2)
        {
            var spec = parts[index + 1].Trim();
            if (spec.Length == 0)
            {
                continue;
            }

            var name = parts[index].Trim();
            specs.Add(new ComDlgFilterSpec { Name = name.Length == 0 ? spec : name, Spec = spec });
        }

        return [.. specs];
    }

    private static string? Show(IFileOpenDialog dialog)
    {
        var hresult = dialog.Show(OwnerHandle);
        if (hresult != 0)
        {
            if (hresult != ErrorCancelled)
            {
                Marshal.ThrowExceptionForHR(hresult);
            }

            return null;
        }

        dialog.GetResult(out var item);
        item.GetDisplayName(SigdnFileSystemPath, out var path);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static IShellItem? TryCreateShellItem(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(directory, 0, ref iid, out var item);
            return item;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or NotSupportedException)
        {
            // A folder the shell cannot parse simply means "no initial location".
            return null;
        }
    }

    private static MessageBoxResult ShowMessageBox(string message, string title, MessageBoxOptions options)
    {
        var result = MessageBoxNative(OwnerHandle, message, title, (uint)options);
        return result switch
        {
            6 => MessageBoxResult.Yes,
            7 => MessageBoxResult.No,
            _ => MessageBoxResult.Ok,
        };
    }

    /// <summary>The main window handle, used as the owner so the dialogs are modal to the shell.</summary>
    private static nint OwnerHandle
    {
        get
        {
            try
            {
                return App.MainWindow is { } window
                    ? Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id)
                    : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Blazor renders on the UI thread, so this normally runs inline; it only marshals when a background
    /// task (the environment probe, a cache scan) asks for a dialog.
    /// </summary>
    private static T Invoke<T>(Func<T> action)
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is null || queue.HasThreadAccess)
        {
            return action();
        }

        var result = default(T);
        using var completed = new ManualResetEventSlim();
        if (!queue.TryEnqueue(() =>
            {
                try
                {
                    result = action();
                }
                finally
                {
                    completed.Set();
                }
            }))
        {
            return action();
        }

        completed.Wait();
        return result!;
    }

    private static void Invoke(Action action) => Invoke<object?>(() =>
    {
        action();
        return null;
    });

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        nint pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxNative(nint hWnd, string text, string caption, uint type);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ComDlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Spec;
    }

    /// <summary>FILEOPENDIALOGOPTIONS.</summary>
    [Flags]
    private enum FileOpenOptions : uint
    {
        NoChangeDir = 0x00000008,
        PickFolders = 0x00000020,
        ForceFileSystem = 0x00000040,
        PathMustExist = 0x00000800,
        FileMustExist = 0x00001000,
    }

    /// <summary>MessageBox button/icon flags.</summary>
    [Flags]
    private enum MessageBoxOptions : uint
    {
        Ok = 0x00000000,
        YesNo = 0x00000004,
        IconError = 0x00000010,
        IconQuestion = 0x00000020,
        IconInformation = 0x00000040,
    }

    private enum MessageBoxResult
    {
        Ok,
        Yes,
        No,
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);

        void GetParent(out IShellItem ppsi);

        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    /// <summary>IModalWindow + IFileDialog, in vtable order.</summary>
    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(nint hwndOwner);

        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] rgFilterSpec);

        void SetFileTypeIndex(uint iFileType);

        void GetFileTypeIndex(out uint piFileType);

        void Advise(nint pfde, out uint pdwCookie);

        void Unadvise(uint dwCookie);

        void SetOptions(uint fos);

        void GetOptions(out uint pfos);

        void SetDefaultFolder(IShellItem psi);

        void SetFolder(IShellItem psi);

        void GetFolder(out IShellItem ppsi);

        void GetCurrentSelection(out IShellItem ppsi);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        void GetResult(out IShellItem ppsi);

        void AddPlace(IShellItem psi, uint fdap);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(nint pFilter);
    }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        void GetResults(out nint ppenum);

        void GetSelectedItems(out nint ppsai);
    }
}

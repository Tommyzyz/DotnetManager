using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DotnetManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;

    public MainWindow()
    {
        Resources.Add("services", Startup.Services);
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        TitleBarTheme.Apply(hwnd);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The web UI tracks prefers-color-scheme live; keep the caption in sync when the system theme flips.
        if (msg == WmSettingChange && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            TitleBarTheme.Apply(hwnd);
        }

        return IntPtr.Zero;
    }
}

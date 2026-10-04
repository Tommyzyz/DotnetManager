using System.Windows;
using DotnetManager.Services;

namespace DotnetManager;

/// <summary>
/// Custom entry point: <c>--exec</c> runs a privileged headless operation without touching WPF at all,
/// everything else starts the normal BlazorWebView application.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Headless.IsHeadless(args))
        {
            Startup.Init();
            return Headless.RunAsync(args, Startup.Services!).GetAwaiter().GetResult();
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}

using DotnetManagerUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Application = Microsoft.UI.Xaml.Application;
using Window = Microsoft.UI.Xaml.Window;

namespace DotnetManagerUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>The shell window. The shell dialogs need its handle.</summary>
        public static Window? Shell { get; private set; }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            // The UI starts a privileged child process with the runas verb and hands it a request on the
            // command line; that child only performs the headless operation and never shows a window.
            var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (Headless.IsHeadless(arguments))
            {
                Environment.Exit(RunHeadless(arguments));
                return;
            }

            window ??= new MainWindow();
            Shell = window;

            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            window.Activate();
        }

        private static int RunHeadless(string[] arguments)
        {
            Startup.Init();
            return Headless.RunAsync(arguments, Startup.Services!).GetAwaiter().GetResult();
        }
    }
}

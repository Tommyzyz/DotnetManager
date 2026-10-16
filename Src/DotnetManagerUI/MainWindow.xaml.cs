using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;
using DotnetManager.RazorLib.Resources;
using DotnetManagerUI.Services;
using DotnetManagerUI.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// MAUI is globally imported by the MAUI SDK, so the WinUI Window type has to be pinned explicitly.
using Window = Microsoft.UI.Xaml.Window;

namespace DotnetManagerUI
{
    /// <summary>
    /// The WinUI shell: the built-in <see cref="TitleBar"/> control for the caption plus a
    /// <see cref="NavigationView"/> whose items drive the Blazor router through
    /// <see cref="IShellNavigation"/>. The BlazorWebView itself lives in <see cref="MainPage"/>
    /// inside the content frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private bool shellReady;

        public MainWindow()
        {
            InitializeComponent();

            // The TitleBar control replaces the system caption and carries the window title.
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
            }

            ContentFrame.NavigationFailed += OnNavigationFailed;

            // Navigating builds MainPage, which is what creates the MAUI container the shell reads
            // its services from, so the shell is populated once the window is live.
            _ = ContentFrame.Navigate(typeof(MainPage));

            NavView.Loaded += OnShellLoaded;
        }

        private void OnShellLoaded(object sender, RoutedEventArgs e)
        {
            if (shellReady)
            {
                return;
            }

            shellReady = true;

            ApplyLocalizedText();
            NavView.SelectedItem = ItemOverview;

            if (Resolve<LocalizationState>() is { } localization)
            {
                localization.Changed += ApplyLocalizedText;
            }
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // The built-in Settings entry has no route tag of its own, so it is handled separately.
            if (args.IsSettingsSelected)
            {
                Resolve<IShellNavigation>()?.Request(ShellRoutes.Settings);
                return;
            }

            if (args.SelectedItem is NavigationViewItem { Tag: string route } && !string.IsNullOrWhiteSpace(route))
            {
                Resolve<IShellNavigation>()?.Request(route);
            }
        }

        /// <summary>Replaces the fallback labels with the localized strings; runs again on a language change.</summary>
        private void ApplyLocalizedText()
        {
            var localizer = Resolve<UiLocalizer>();
            if (localizer is null)
            {
                return;
            }

            SetText(GroupProject, localizer, "Nav.GroupProject");
            SetText(ItemProjectPackages, localizer, "Nav.ProjectPackages");
            SetText(ItemTools, localizer, "Nav.Tools");

            SetText(GroupNuGet, localizer, "Nav.GroupNuGet");
            SetText(ItemPackageSources, localizer, "Nav.PackageSources");
            SetText(ItemNuGetCaches, localizer, "Nav.Caches");
            SetText(ItemNuGetConfig, localizer, "Nav.ConfigFiles");

            SetText(GroupEnvironment, localizer, "Nav.GroupEnvironment");
            SetText(ItemSdk, localizer, "Nav.Sdk");
            SetText(ItemWorkloads, localizer, "Nav.Workloads");
            SetText(ItemOutput, localizer, "Nav.Output");

            // The shell's own Settings entry lives at the bottom of the pane; WinUI labels it from the
            // system language, so it is re-labelled here to follow the app language instead.
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                SetText(settingsItem, localizer, "Nav.Settings");
            }

            var title = localizer["App.Title"];
            NavView.PaneTitle = title;
            AppTitleBar.Title = title;
            Title = title;

            RefreshStatus(localizer);
        }

        /// <summary>
        /// The privilege and dotnet path used to sit in the web layout's top row; they belong to the
        /// window chrome now, so they live in the title bar's subtitle.
        /// </summary>
        private void RefreshStatus(UiLocalizer localizer)
        {
            var privilege = Resolve<IEnvironmentService>()?.IsElevated == true
                ? localizer["App.Elevated"]
                : localizer["App.NotElevated"];

            var dotnet = Resolve<IDotnetCli>()?.ExecutablePath;

            AppTitleBar.Subtitle = string.IsNullOrWhiteSpace(dotnet)
                ? privilege
                : $"{privilege} · {dotnet}";
        }

        private static void SetText(ContentControl element, UiLocalizer localizer, string key) =>
            element.Content = localizer[key];

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e) =>
            throw new Exception($"Failed to load page - {e.SourcePageType.FullName}");

        /// <summary>The services come from the MAUI container the BlazorWebView also uses.</summary>
        private static T? Resolve<T>() where T : class => Startup.Services?.GetService(typeof(T)) as T;
    }
}

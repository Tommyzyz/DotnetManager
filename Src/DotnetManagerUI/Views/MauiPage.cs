using Microsoft.AspNetCore.Components.WebView.Maui;

namespace DotnetManagerUI.Views
{
    /// <summary>
    /// Hosts the Blazor UI. MAUI is only here for the BlazorWebView (WinUI 3 has no first-party one);
    /// the window, the caption and every dialog are WinUI, so this page is deliberately nothing but
    /// the web view.
    /// </summary>
    public partial class MauiPage : ContentPage
    {
        public MauiPage()
        {
            var bwv = new BlazorWebView()
            {
                StartPath = "/",
                HostPage = "wwwroot/index.html"
            };

            bwv.RootComponents.Add(new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Main),
                Parameters = null
            });

            Content = bwv;
        }
    }
}
